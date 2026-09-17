using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
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
            string template = application.GetDocumentTemplate(
                (int)swDocumentTypes_e.swDocDRAWING,
                string.Empty,
                0,
                0d,
                0d);
            if (string.IsNullOrWhiteSpace(template) || !File.Exists(template))
            {
                return SolidWorksProviderResults.Failure<SolidWorksCreatedDrawing>(
                    "drawing.create",
                    new OperationError(
                        ErrorCodes.NotFound,
                        "SOLIDWORKS did not return an existing drawing document template.",
                        ErrorCategories.Provider,
                        remediation: "Run the Windows doctor and configure a valid native drawing template."));
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
                ProfileFeatureName: null);
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
}
