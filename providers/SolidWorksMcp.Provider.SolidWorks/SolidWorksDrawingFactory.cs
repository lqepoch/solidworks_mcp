using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System.Diagnostics;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Core;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Provider.SolidWorks;

/// <summary>
/// Creates one persisted native drawing from a provider-owned open model.
/// 从 Provider 自己登记且处于打开状态的模型创建一个已持久化的原生工程图。
/// </summary>
/// <remarks>
/// This factory owns only the COM sequence needed to create the drawing document. View planning and engineering
/// semantics stay above this adapter. 该 factory 只负责创建 drawing document 所需的 COM 序列；视图规划和工程语义
/// 保持在上层，避免把业务规则堆进 COM wrapper。
/// </remarks>
internal static class SolidWorksDrawingFactory
{
    /// <summary>Creates, persists and identity-checks one drawing document.</summary>
    public static OperationResult<SolidWorksCreatedDrawing> CreateOnSta(
        ISldWorks application,
        SessionId sessionId,
        CreateDrawingRequest request,
        SolidWorksDocumentDescriptor sourceDescriptor,
        CadPathAllowlist pathAllowlist)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sourceDescriptor);
        ArgumentNullException.ThrowIfNull(pathAllowlist);

        CadPathValidationResult pathValidation = pathAllowlist.ValidateCreateTarget(request.Path, ".slddrw");
        if (!pathValidation.IsAllowed || string.IsNullOrWhiteSpace(pathValidation.FullPath))
        {
            return SolidWorksProviderResults.Failure<SolidWorksCreatedDrawing>(
                "drawing.create",
                new OperationError(
                    pathValidation.FailureReason == "outside-allowlist"
                        ? ErrorCodes.PathNotAllowed
                        : ErrorCodes.InvalidRequest,
                    "The native drawing target failed the configured persisted-artifact path policy.",
                    pathValidation.FailureReason == "outside-allowlist"
                        ? ErrorCategories.Policy
                        : ErrorCategories.Validation,
                    details: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["path-policy-reason"] = pathValidation.FailureReason ?? "invalid",
                    },
                    remediation: "Use a new absolute .slddrw target below an explicitly allowlisted workspace."));
        }

        string path = pathValidation.FullPath;
        if (File.Exists(path))
        {
            return SolidWorksProviderResults.Failure<SolidWorksCreatedDrawing>(
                "drawing.create",
                new OperationError(
                    ErrorCodes.StateConflict,
                    "The native drawing target already exists; overwrite is not enabled by this operation.",
                    ErrorCategories.State,
                    remediation: "Choose a new isolated output path or use a future governed overwrite operation."));
        }

        if (sourceDescriptor.DocumentType is not (CadDocumentType.Part or CadDocumentType.Assembly))
        {
            return SolidWorksProviderResults.Failure<SolidWorksCreatedDrawing>(
                "drawing.create",
                new OperationError(
                    ErrorCodes.InvalidRequest,
                    "A drawing source must be a registered part or assembly document.",
                    ErrorCategories.Validation));
        }

        ModelDoc2? source = null;
        ModelDoc2? drawing = null;
        bool persistedTargetOwned = false;
        bool ownershipTransferred = false;
        try
        {
            OperationResult<ModelDoc2> sourceResult = SolidWorksDocumentRouting.ResolveOpenDocument(
                application,
                sourceDescriptor,
                activate: false,
                verifyStateHash: false);
            if (!sourceResult.IsSuccess || sourceResult.Value is null)
            {
                return OperationResults.Failure<SolidWorksCreatedDrawing>(
                    sourceResult.OperationId,
                    sourceResult.Error!,
                    sourceResult.Evidence);
            }

            source = sourceResult.Value;
            string? template = ResolveTemplate(application, request.TemplateProfile);
            if (string.IsNullOrWhiteSpace(template) || !File.Exists(template))
            {
                return SolidWorksProviderResults.Failure<SolidWorksCreatedDrawing>(
                    "drawing.create",
                    new OperationError(
                        ErrorCodes.NotFound,
                        request.TemplateProfile == DrawingTemplateProfile.GbMechanical
                            ? "The installed SOLIDWORKS GB drawing template could not be discovered."
                            : "SOLIDWORKS did not return an existing drawing document template.",
                        ErrorCategories.Provider,
                        remediation: "Run the Windows doctor and verify that the requested native drawing template is installed."));
            }

            // INewDocument2 is used instead of ActiveDoc so template selection is explicit and deterministic.
            // 使用 INewDocument2 而不是 ActiveDoc，确保模板选择是显式且可复现的。
            drawing = application.INewDocument2(
                template,
                // INewDocument2's second argument is PaperSize; the template already determines drawing type.
                // INewDocument2 的第二参数是 PaperSize，template 已经决定这是 drawing 文档类型。
                0,
                0d,
                0d);
            if (drawing is null || !SolidWorksDocumentRouting.MatchesType(drawing, CadDocumentType.Drawing))
            {
                return SolidWorksProviderResults.Failure<SolidWorksCreatedDrawing>(
                    "drawing.create",
                    new OperationError(
                        ErrorCodes.ProviderFailure,
                        "SOLIDWORKS did not return a verified drawing document from the discovered template.",
                        ErrorCategories.Provider));
            }

            OperationResult<NativeSheetVerification> sheetVerification = ConfigureAndVerifySheet(
                drawing,
                request,
                template);
            if (!sheetVerification.IsSuccess || sheetVerification.Value is null)
            {
                return OperationResults.Failure<SolidWorksCreatedDrawing>(
                    sheetVerification.OperationId,
                    sheetVerification.Error!,
                    sheetVerification.Evidence);
            }

            int saveError = drawing.SaveAs3(
                path,
                (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent);
            if (saveError != 0)
            {
                return SolidWorksProviderResults.Failure<SolidWorksCreatedDrawing>(
                    "drawing.create",
                    new OperationError(
                        ErrorCodes.ProviderFailure,
                        "SOLIDWORKS could not persist the newly created drawing.",
                        ErrorCategories.Provider,
                        details: new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["save-error-code"] = saveError.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            ["target-path"] = path,
                        },
                        remediation: "Preserve the artifact and inspect the SOLIDWORKS save error code."));
            }

            string actualPath = Path.GetFullPath(drawing.GetPathName()?.Trim() ?? string.Empty);
            persistedTargetOwned = actualPath.Equals(path, StringComparison.OrdinalIgnoreCase);
            if (!persistedTargetOwned)
            {
                return SolidWorksProviderResults.Failure<SolidWorksCreatedDrawing>(
                    "drawing.create",
                    new OperationError(
                        ErrorCodes.StateConflict,
                        "The newly created drawing did not report the expected persisted path.",
                        ErrorCategories.State));
            }

            var descriptor = new SolidWorksDocumentDescriptor(
                new DocumentId($"{sessionId.Value}:document:{Guid.NewGuid():N}"),
                CadDocumentType.Drawing,
                path,
                drawing.GetTitle()?.Trim() ?? Path.GetFileNameWithoutExtension(path),
                SolidWorksDocumentRouting.ReadConfiguration(drawing),
                SolidWorksDocumentRouting.ComputeStateHash(drawing),
                drawing.GetSaveFlag(),
                ProfileFeatureName: null,
                SourceDocumentId: sourceDescriptor.DocumentId,
                SourceDocumentPath: sourceDescriptor.Path);
            ownershipTransferred = true;
            return SolidWorksProviderResults.Success(
                "drawing.create",
                new SolidWorksCreatedDrawing(descriptor, sourceDescriptor.DocumentId, sourceDescriptor.Path),
                new EvidenceObservation("document.id", descriptor.DocumentId.Value),
                new EvidenceObservation("document.path", descriptor.Path),
                new EvidenceObservation("document.type", descriptor.DocumentType.ToString()),
                new EvidenceObservation("source.document.id", sourceDescriptor.DocumentId.Value),
                new EvidenceObservation("source.document.path", sourceDescriptor.Path),
                new EvidenceObservation("drawing.template", template),
                new EvidenceObservation("drawing.template-profile", request.TemplateProfile.ToString()),
                new EvidenceObservation("drawing.sheet.paper-size", sheetVerification.Value.PaperSize),
                new EvidenceObservation("drawing.sheet.size-millimeters", $"{sheetVerification.Value.WidthMillimeters:G17}x{sheetVerification.Value.HeightMillimeters:G17}"),
                new EvidenceObservation("drawing.sheet.projection", sheetVerification.Value.ProjectionMethod),
                new EvidenceObservation("drawing.sheet.template-name", sheetVerification.Value.TemplateName),
                new EvidenceObservation("state.hash", descriptor.StateHash));
        }
        catch (Exception exception)
        {
            return SolidWorksProviderResults.ProviderFailure<SolidWorksCreatedDrawing>(
                "drawing.create",
                exception,
                "SOLIDWORKS drawing creation failed before a verified document identity was returned.");
        }
        finally
        {
            // Close only the exact clean target owned by this operation. Never close an arbitrary active document.
            // 只关闭本 operation 确认拥有、路径精确且 clean 的 target；绝不关闭任意 ActiveDoc。
            if (!ownershipTransferred && persistedTargetOwned && drawing is not null)
            {
                TryCloseCleanPersistedTarget(application, drawing, path);
            }

            SolidWorksDocumentRouting.Release(source);
            SolidWorksDocumentRouting.Release(drawing);
        }
    }

    /// <summary>Best-effort bounded compensation for a failed post-save identity check.</summary>
    private static void TryCloseCleanPersistedTarget(ISldWorks application, ModelDoc2 model, string expectedPath)
    {
        try
        {
            if (model.GetSaveFlag())
            {
                return;
            }

            string actualPath = model.GetPathName()?.Trim() ?? string.Empty;
            if (!Path.GetFullPath(actualPath).Equals(expectedPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            application.CloseDoc(expectedPath);
            ModelDoc2? stillOpen = application.GetOpenDocument(expectedPath);
            SolidWorksDocumentRouting.Release(stillOpen);
        }
        catch
        {
            // Cleanup must never mask the authoritative creation failure or perform a blind retry.
            // cleanup 不能覆盖原始创建失败，也不能在未知 modal 状态下 blind retry。
        }
    }

    /// <summary>
    /// Applies the explicit compiler-selected sheet contract and immediately reads it back through ISheet. A drawing
    /// template is not trusted to imply A4, landscape or first-angle projection.
    /// 应用 compiler 选择的显式图纸契约，并立即通过 ISheet 读回；绝不信任模板文件名能隐含 A4、横向或第一角投影。
    /// </summary>
    private static OperationResult<NativeSheetVerification> ConfigureAndVerifySheet(
        ModelDoc2 drawing,
        CreateDrawingRequest request,
        string templatePath)
    {
        const string operation = "drawing.create.sheet";
        if (request.Sheet is null)
        {
            if (request.TemplateProfile is DrawingTemplateProfile.GbMechanical)
            {
                return SolidWorksProviderResults.Failure<NativeSheetVerification>(
                    operation,
                    new OperationError(
                        ErrorCodes.InvalidRequest,
                        "GB drawing creation requires an explicit sheet contract.",
                        ErrorCategories.Validation,
                        remediation: "Resolve a versioned RulePack sheet before creating the native drawing."));
            }

            return SolidWorksProviderResults.Success(
                operation,
                new NativeSheetVerification("unknown", 0d, 0d, "unknown", Path.GetFileName(templatePath)));
        }

        DrawingSheetRequest expected = request.Sheet;
        int paperSize = ToPaperSize(expected.PaperSize, expected.Width, expected.Height);
        int templateSize = ToTemplateSize(expected.PaperSize, expected.Width, expected.Height);
        bool firstAngle = expected.ProjectionMethod.Equals("FirstAngle", StringComparison.OrdinalIgnoreCase);
        string sheetName = string.IsNullOrWhiteSpace(expected.Name) ? "Sheet1" : expected.Name.Trim();
        string nativeTemplate = templatePath;
        var drawingDocument = (IDrawingDoc)drawing;
        bool setup = drawingDocument.SetupSheet3(
            sheetName,
            paperSize,
            templateSize,
            1d,
            1d,
            firstAngle,
            nativeTemplate,
            expected.Width.ToMeters(),
            expected.Height.ToMeters());
        if (!setup)
        {
            return SolidWorksProviderResults.Failure<NativeSheetVerification>(
                operation,
                new OperationError(
                    ErrorCodes.ProviderFailure,
                    "SOLIDWORKS rejected the explicit drawing sheet setup.",
                    ErrorCategories.Provider,
                    remediation: "Preserve the drawing and inspect the installed template and paper-size mapping."));
        }

        if (!drawing.ForceRebuild3(false))
        {
            return SolidWorksProviderResults.Failure<NativeSheetVerification>(
                operation,
                new OperationError(
                    ErrorCodes.InvariantViolation,
                    "SOLIDWORKS did not rebuild after the explicit sheet setup.",
                    ErrorCategories.Invariant));
        }

        ISheet? sheet = null;
        try
        {
            sheet = ((IDrawingDoc)drawing).IGetCurrentSheet() as ISheet;
            if (sheet is null)
            {
                return SolidWorksProviderResults.Failure<NativeSheetVerification>(
                    operation,
                    new OperationError(
                        ErrorCodes.InvariantViolation,
                        "SOLIDWORKS returned no current sheet after setup.",
                        ErrorCategories.Invariant));
            }

            double[] properties = ReadNumbers(sheet.GetProperties2());
            if (properties.Length < 7)
            {
                return SolidWorksProviderResults.Failure<NativeSheetVerification>(
                    operation,
                    new OperationError(
                        ErrorCodes.InvariantViolation,
                        "SOLIDWORKS returned incomplete sheet properties.",
                        ErrorCategories.Invariant));
            }

            double width = properties[5];
            double height = properties[6];
            if (Math.Abs(width - expected.Width.ToMeters()) > 0.00001d
                || Math.Abs(height - expected.Height.ToMeters()) > 0.00001d
                || (Math.Abs(properties[4]) > 0.5d) != firstAngle
                || !PaperSizeName(Convert.ToInt32(properties[0], System.Globalization.CultureInfo.InvariantCulture), width, height)
                    .Equals(expected.PaperSize, StringComparison.OrdinalIgnoreCase))
            {
                return SolidWorksProviderResults.Failure<NativeSheetVerification>(
                    operation,
                    new OperationError(
                        ErrorCodes.InvariantViolation,
                        "The native sheet read-back differs from the explicit compiler contract.",
                        ErrorCategories.Invariant,
                        details: new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["expected-paper-size"] = expected.PaperSize,
                            ["actual-paper-size"] = PaperSizeName(Convert.ToInt32(properties[0], System.Globalization.CultureInfo.InvariantCulture), width, height),
                            ["expected-size-meters"] = $"{expected.Width.ToMeters():G17}x{expected.Height.ToMeters():G17}",
                            ["actual-size-meters"] = $"{width:G17}x{height:G17}",
                            ["expected-projection"] = expected.ProjectionMethod,
                            ["actual-projection"] = Math.Abs(properties[4]) > 0.5d ? "FirstAngle" : "ThirdAngle",
                        },
                        remediation: "Do not continue to create views; fix the native template/sheet setup or choose an approved sheet."));
            }

            return SolidWorksProviderResults.Success(
                operation,
                new NativeSheetVerification(
                    expected.PaperSize,
                    width * 1000d,
                    height * 1000d,
                    firstAngle ? "FirstAngle" : "ThirdAngle",
                    Path.GetFileName(sheet.GetTemplateName()?.Trim() ?? string.Empty)),
                new EvidenceObservation("sheet.name", sheet.GetName()?.Trim() ?? sheetName),
                new EvidenceObservation("sheet.paper-size", expected.PaperSize),
                new EvidenceObservation("sheet.width-millimeters", (width * 1000d).ToString("G17", System.Globalization.CultureInfo.InvariantCulture)),
                new EvidenceObservation("sheet.height-millimeters", (height * 1000d).ToString("G17", System.Globalization.CultureInfo.InvariantCulture)),
                new EvidenceObservation("sheet.projection", firstAngle ? "FirstAngle" : "ThirdAngle"),
                new EvidenceObservation("sheet.template-name", Path.GetFileName(sheet.GetTemplateName()?.Trim() ?? string.Empty)));
        }
        finally
        {
            SolidWorksDocumentRouting.Release(sheet);
        }
    }

    private static int ToPaperSize(string paperSize, Length width, Length height) => paperSize.Trim().ToUpperInvariant() switch
    {
        "A4" when width.Millimeters >= height.Millimeters => (int)swDwgPaperSizes_e.swDwgPaperA4size,
        "A4" => (int)swDwgPaperSizes_e.swDwgPaperA4sizeVertical,
        "A3" => (int)swDwgPaperSizes_e.swDwgPaperA3size,
        "A2" => (int)swDwgPaperSizes_e.swDwgPaperA2size,
        "A1" => (int)swDwgPaperSizes_e.swDwgPaperA1size,
        "A0" => (int)swDwgPaperSizes_e.swDwgPaperA0size,
        _ => (int)swDwgPaperSizes_e.swDwgPapersUserDefined,
    };

    private static int ToTemplateSize(string paperSize, Length width, Length height) => paperSize.Trim().ToUpperInvariant() switch
    {
        "A4" when width.Millimeters >= height.Millimeters => (int)swDwgTemplates_e.swDwgTemplateA4size,
        "A4" => (int)swDwgTemplates_e.swDwgTemplateA4sizeVertical,
        "A3" => (int)swDwgTemplates_e.swDwgTemplateA3size,
        "A2" => (int)swDwgTemplates_e.swDwgTemplateA2size,
        "A1" => (int)swDwgTemplates_e.swDwgTemplateA1size,
        "A0" => (int)swDwgTemplates_e.swDwgTemplateA0size,
        _ => (int)swDwgTemplates_e.swDwgTemplateCustom,
    };

    private static string PaperSizeName(int paperSize, double widthMeters, double heightMeters) => paperSize switch
    {
        (int)swDwgPaperSizes_e.swDwgPaperA4size or (int)swDwgPaperSizes_e.swDwgPaperA4sizeVertical => "A4",
        (int)swDwgPaperSizes_e.swDwgPaperA3size => "A3",
        (int)swDwgPaperSizes_e.swDwgPaperA2size => "A2",
        (int)swDwgPaperSizes_e.swDwgPaperA1size => "A1",
        (int)swDwgPaperSizes_e.swDwgPaperA0size => "A0",
        _ => $"custom:{widthMeters * 1000d:0.###}x{heightMeters * 1000d:0.###}",
    };

    private static double[] ReadNumbers(object? value)
    {
        if (value is not Array array)
        {
            return [];
        }

        double[] values = new double[array.Length];
        for (int index = 0; index < array.Length; index++)
        {
            values[index] = array.GetValue(index) is object item
                ? Convert.ToDouble(item, System.Globalization.CultureInfo.InvariantCulture)
                : 0d;
        }

        return values;
    }

    /// <summary>
    /// Resolves a semantic template profile from the exact SOLIDWORKS process that owns this STA call.
    /// 从本次 STA 调用实际绑定的 SOLIDWORKS 进程解析语义模板 profile。
    /// </summary>
    /// <remarks>
    /// A GB drawing must not silently fall back to the user's Letter/ANSI default. The executable path is obtained
    /// from the process identified by the COM session, then the installation's own template tree is searched in a
    /// deterministic order. This keeps personal absolute paths out of requests and turns missing templates into a
    /// visible provider error instead of a visually wrong PDF. GB 图纸不能静默退回用户的 Letter/ANSI 默认模板；
    /// 这里使用 COM session 对应进程的 executable path，在该安装目录内按确定顺序查找，缺失时显式失败。
    /// </remarks>
    private static string? ResolveTemplate(ISldWorks application, DrawingTemplateProfile profile)
    {
        if (profile is DrawingTemplateProfile.SolidWorksDefault)
        {
            return application.GetDocumentTemplate(
                (int)swDocumentTypes_e.swDocDRAWING,
                string.Empty,
                0,
                0d,
                0d);
        }

        if (profile is not DrawingTemplateProfile.GbMechanical)
        {
            return null;
        }

        string? installRoot = null;
        try
        {
            int processId = application.GetProcessID();
            using Process process = Process.GetProcessById(processId);
            installRoot = process.MainModule?.FileName is string executable
                ? Path.GetDirectoryName(executable)
                : null;
        }
        catch
        {
            // The provider must fail closed if process identity cannot be inspected. It must not guess from a
            // developer-specific absolute path or from whichever template SOLIDWORKS currently has selected.
            // 如果无法读取进程身份，Provider 必须 fail closed，不能猜个人绝对路径或沿用当前默认模板。
        }

        if (string.IsNullOrWhiteSpace(installRoot))
        {
            return null;
        }

        string[] candidates =
        [
            Path.Combine(installRoot, "data", "templates", "gb.drwdot"),
            Path.Combine(installRoot, "lang", "chinese", "Tutorial", "draw.drwdot"),
        ];
        return candidates.FirstOrDefault(path => File.Exists(path));
    }
}

/// <summary>Read-back facts for the sheet configured during native drawing creation.</summary>
internal sealed record NativeSheetVerification(
    string PaperSize,
    double WidthMillimeters,
    double HeightMillimeters,
    string ProjectionMethod,
    string TemplateName);
