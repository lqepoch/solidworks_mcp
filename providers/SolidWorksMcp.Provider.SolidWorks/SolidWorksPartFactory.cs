using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Core;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Provider.SolidWorks;

/// <summary>Creates one persisted native part and returns only vendor-neutral metadata across the STA boundary.</summary>
/// <remarks>
/// B03 deliberately requires a persisted <c>.sldprt</c> path.  An unsaved document cannot be safely reacquired later
/// without trusting the current ActiveDoc, which is forbidden by the session/document binding contract.  B03 故意要求
/// 已持久化的 <c>.sldprt</c> path；未保存文档无法安全重获，若依赖当前 ActiveDoc 会违反 session/document binding。
/// </remarks>
internal static class SolidWorksPartFactory
{
    /// <summary>Creates a new part from a validated profile, saves it and verifies its native identity.</summary>
    public static OperationResult<SolidWorksCreatedPart> CreateOnSta(
        ISldWorks application,
        SessionId sessionId,
        CreatePartRequest request,
        CadPathAllowlist pathAllowlist)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(pathAllowlist);

        CadPathValidationResult pathValidation = pathAllowlist.ValidateCreateTarget(request.Path, ".sldprt");
        if (!pathValidation.IsAllowed || string.IsNullOrWhiteSpace(pathValidation.FullPath))
        {
            return SolidWorksProviderResults.Failure<SolidWorksCreatedPart>(
                "part.create",
                new OperationError(
                    pathValidation.FailureReason == "outside-allowlist"
                        ? ErrorCodes.PathNotAllowed
                        : ErrorCodes.InvalidRequest,
                    "The native part target failed the configured persisted-artifact path policy.",
                    pathValidation.FailureReason == "outside-allowlist"
                        ? ErrorCategories.Policy
                        : ErrorCategories.Validation,
                    details: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["path-policy-reason"] = pathValidation.FailureReason ?? "invalid",
                    },
                    remediation: "Use an existing absolute .sldprt target below an explicitly allowlisted workspace."));
        }

        string path = pathValidation.FullPath;

        if (File.Exists(path))
        {
            return SolidWorksProviderResults.Failure<SolidWorksCreatedPart>(
                "part.create",
                new OperationError(
                    ErrorCodes.StateConflict,
                    "The native B03 create path already exists; overwrite is not enabled by this operation.",
                    ErrorCategories.State,
                    remediation: "Choose a new isolated output path or use the future governed overwrite operation."));
        }

        if (!string.Equals(request.Configuration, "Default", StringComparison.Ordinal))
        {
            return SolidWorksProviderResults.Failure<SolidWorksCreatedPart>(
                "part.create",
                new OperationError(
                    ErrorCodes.UnsupportedCapability,
                    "B03 initial part creation currently supports only the default configuration.",
                    ErrorCategories.Capability));
        }

        ModelDoc2? model = null;
        bool persistedTargetOwned = false;
        bool ownershipTransferred = false;
        try
        {
            string template = application.GetDocumentTemplate(
                (int)swDocumentTypes_e.swDocPART,
                string.Empty,
                0,
                0d,
                0d);
            if (string.IsNullOrWhiteSpace(template) || !File.Exists(template))
            {
                return SolidWorksProviderResults.Failure<SolidWorksCreatedPart>(
                    "part.create",
                    new OperationError(
                        ErrorCodes.NotFound,
                        "SOLIDWORKS did not return an existing part document template.",
                        ErrorCategories.Provider,
                        remediation: "Run the Windows doctor and configure a valid native part template."));
            }

            model = application.INewDocument2(
                template,
                // INewDocument2 receives the document type through the template; its second parameter is PaperSize.
                // Passing swDocPART here is an enum/category mix-up even if some templates tolerate the numeric value.
                // INewDocument2 由 template 决定文档类型；第二参数实际是 PaperSize。即使某些 template 容忍该数值，
                // 传 swDocPART 仍是 enum/category 混用，必须使用已核验的默认纸张值 0。
                0,
                0d,
                0d);
            if (model is null)
            {
                return SolidWorksProviderResults.Failure<SolidWorksCreatedPart>(
                    "part.create",
                    new OperationError(
                        ErrorCodes.ProviderFailure,
                        "SOLIDWORKS returned no ModelDoc2 for the new part.",
                        ErrorCategories.Provider));
            }

            int profileCount = (request.InitialSketchProfile is not null ? 1 : 0)
                + (request.InitialPolygon is not null ? 1 : 0)
                + (request.InitialRectangle is not null ? 1 : 0)
                + (request.InitialCircleRadius is not null ? 1 : 0);
            if (profileCount > 1)
            {
                return SolidWorksProviderResults.Failure<SolidWorksCreatedPart>(
                    "part.create",
                    new OperationError(
                        ErrorCodes.InvalidRequest,
                        "A native part may specify only one initial planar profile.",
                        ErrorCategories.Validation));
            }

            string? profileFeatureName = null;
            if (request.InitialSketchProfile is SketchProfileRequest sketchProfile)
            {
                bool planeSelected = model.Extension.SelectByID2(
                    "Front Plane",
                    "PLANE",
                    0d,
                    0d,
                    0d,
                    false,
                    0,
                    null,
                    0);
                if (!planeSelected)
                {
                    return SolidWorksProviderResults.Failure<SolidWorksCreatedPart>(
                        "part.create",
                        new OperationError(
                            ErrorCodes.ProviderFailure,
                            "The SOLIDWORKS Front Plane could not be selected for the structured sketch profile.",
                            ErrorCategories.Provider));
                }

                model.SketchManager.InsertSketch(true);
                bool sketchOpen = true;
                try
                {
                    string? profileError = SolidWorksNativeSketchProfileBuilder.TryCreate(model, sketchProfile);
                    if (profileError is not null)
                    {
                        return SolidWorksProviderResults.Failure<SolidWorksCreatedPart>(
                            "part.create",
                            new OperationError(
                                ErrorCodes.InvalidRequest,
                                "The structured sketch profile was rejected before native extrusion.",
                                ErrorCategories.Validation,
                                details: new Dictionary<string, string>(StringComparer.Ordinal)
                                {
                                    ["profile-reason"] = profileError,
                                },
                                remediation: "Provide one finite, connected, closed profile made from lines and non-collinear three-point arcs."));
                    }

                    // Model Items only imports dimensions that are marked for drawing. Creating one native,
                    // associative line dimension while the structured sketch is open gives the later drawing
                    // compiler eligible SOLIDWORKS evidence; setting a feature value afterward is not equivalent.
                    // Model Items 只会导入被标记为 drawing 的尺寸。在 structured sketch 打开时创建一个原生关联线性
                    // 尺寸，才能给后续工程图编译器提供可导入的 SOLIDWORKS 证据；之后设置 feature 数值并不等价。
                    SketchCurveRequest? firstLine = sketchProfile.Segments.FirstOrDefault(
                        static segment => segment.Kind == SketchCurveKind.Line);
                    if (firstLine is not null)
                    {
                        string? dimensionError = TryCreateNativeDrivingDimension(
                            application,
                            model,
                            firstLine.Start,
                            firstLine.End,
                            "structured sketch profile");
                        if (dimensionError is not null)
                        {
                            return SolidWorksProviderResults.Failure<SolidWorksCreatedPart>(
                                "part.create",
                                new OperationError(
                                    ErrorCodes.ProviderFailure,
                                    dimensionError,
                                    ErrorCategories.Provider,
                                    remediation: "Preserve the artifact and inspect native profile dimension creation."));
                        }
                    }
                }
                finally
                {
                    if (sketchOpen)
                    {
                        model.SketchManager.InsertSketch(true);
                    }
                }

                model.ClearSelection2(true);
                profileFeatureName = FindFirstSketchFeatureName(model);
                if (string.IsNullOrWhiteSpace(profileFeatureName))
                {
                    return SolidWorksProviderResults.Failure<SolidWorksCreatedPart>(
                        "part.create",
                        new OperationError(
                            ErrorCodes.InvariantViolation,
                            "The structured sketch profile was created but could not be identified in the feature tree.",
                            ErrorCategories.Invariant,
                            remediation: "Preserve the isolated artifact and inspect the native sketch feature identity."));
                }
            }
            else if (request.InitialPolygon is PolygonProfileRequest polygonProfile)
            {
                if (polygonProfile.Vertices.Length < 3
                    || polygonProfile.Vertices.Any(vertex => !double.IsFinite(vertex.X.Millimeters) || !double.IsFinite(vertex.Y.Millimeters)))
                {
                    return SolidWorksProviderResults.Failure<SolidWorksCreatedPart>(
                        "part.create",
                        new OperationError(
                            ErrorCodes.InvalidRequest,
                            "A polygon profile requires at least three finite vertices.",
                            ErrorCategories.Validation));
                }

                bool planeSelected = model.Extension.SelectByID2(
                    "Front Plane",
                    "PLANE",
                    0d,
                    0d,
                    0d,
                    false,
                    0,
                    null,
                    0);
                if (!planeSelected)
                {
                    return SolidWorksProviderResults.Failure<SolidWorksCreatedPart>(
                        "part.create",
                        new OperationError(
                            ErrorCodes.ProviderFailure,
                            "The SOLIDWORKS Front Plane could not be selected for the polygon sketch.",
                            ErrorCategories.Provider));
                }

                model.SketchManager.InsertSketch(true);
                bool sketchOpen = true;
                try
                {
                    // CreateLine is intentionally used instead of a vendor-neutral polygon helper: each segment is
                    // still created by SOLIDWORKS and can therefore be inspected as native sketch evidence.
                    // CreateLine 是有意使用的原生 primitive；每条边都由 SOLIDWORKS 创建，可在 native inspection 中核对。
                    for (int index = 0; index < polygonProfile.Vertices.Length; index++)
                    {
                        Coordinate2D start = polygonProfile.Vertices[index];
                        Coordinate2D end = polygonProfile.Vertices[(index + 1) % polygonProfile.Vertices.Length];
                        SketchSegment? segment = model.SketchManager.CreateLine(
                            start.X.ToMeters(),
                            start.Y.ToMeters(),
                            0d,
                            end.X.ToMeters(),
                            end.Y.ToMeters(),
                            0d);
                        if (segment is null)
                        {
                            return SolidWorksProviderResults.Failure<SolidWorksCreatedPart>(
                                "part.create",
                                new OperationError(
                                    ErrorCodes.ProviderFailure,
                                    "SOLIDWORKS returned no sketch segment for one polygon edge.",
                                    ErrorCategories.Provider,
                                    remediation: "Preserve the artifact and inspect polygon topology and profile units."));
                        }

                        SolidWorksDocumentRouting.Release(segment);
                    }

                    // Create one native, associative driving dimension while the sketch is still open.  The drawing
                    // compiler later imports only dimensions that SOLIDWORKS itself returns from
                    // InsertModelAnnotations3; keeping this dimension native is therefore essential evidence that a
                    // drawing dimension is linked to model geometry rather than synthesized from request text.
                    // 在草图仍处于打开状态时创建一个原生、具有关联性的驱动尺寸。后续工程图编译器只导入
                    // SOLIDWORKS 通过 InsertModelAnnotations3 返回的尺寸；因此必须保留原生尺寸，才能证明二维
                    // 尺寸确实关联到模型几何，而不是由请求文本伪造出来。
                    Coordinate2D firstStart = polygonProfile.Vertices[0];
                    Coordinate2D firstEnd = polygonProfile.Vertices[1];
                    double segmentMidpointX = (firstStart.X.ToMeters() + firstEnd.X.ToMeters()) / 2d;
                    double segmentMidpointY = (firstStart.Y.ToMeters() + firstEnd.Y.ToMeters()) / 2d;
                    bool segmentSelected = model.Extension.SelectByID2(
                        string.Empty,
                        "SKETCHSEGMENT",
                        segmentMidpointX,
                        segmentMidpointY,
                        0d,
                        false,
                        0,
                        null,
                        0);
                    if (!segmentSelected)
                    {
                        return SolidWorksProviderResults.Failure<SolidWorksCreatedPart>(
                            "part.create",
                            new OperationError(
                                ErrorCodes.ProviderFailure,
                                "SOLIDWORKS could not select the first native polygon edge for its driving dimension.",
                                ErrorCategories.Provider,
                                remediation: "Preserve the artifact and inspect the sketch segment location and unit conversion."));
                    }

                    // A single selected line unambiguously defines a linear length, so the verified API path is
                    // ModelDoc2.AddDimension2.  AddDimension is reserved for selections that need explicit extension
                    // direction (for example an angular dimension); using that overload here can leave SOLIDWORKS
                    // waiting for additional selection context in an interactive sketch.
                    // 单条直线已足以唯一确定线性长度，因此这里使用已核对的 ModelDoc2.AddDimension2。AddDimension
                    // 适合需要明确延长线方向的选择（例如角度尺寸）；在交互草图中错误使用该重载可能让
                    // SOLIDWORKS 等待更多选择上下文。
                    // SOLIDWORKS 2022 can open a modal "Enter dimension value" prompt when this preference is on.
                    // A modal prompt blocks the same STA that owns the COM call, so cancellation cannot recover it.
                    // Temporarily disabling the documented preference is a safety preflight, not a blind dialog key.
                    // SOLIDWORKS 2022 如果该偏好为 true，会弹出“输入尺寸值”模态框；模态框会阻塞拥有 COM 的
                    // 同一个 STA，普通 cancellation 无法恢复。这里临时关闭官方偏好，是安全 preflight，不是盲目
                    // 发送 Enter/Escape/OK。
                    const int inputDimensionValuePreference = (int)swUserPreferenceToggle_e.swInputDimValOnCreate;
                    bool previousInputDimensionValuePreference = application.GetUserPreferenceToggle(inputDimensionValuePreference);
                    application.SetUserPreferenceToggle(inputDimensionValuePreference, false);
                    try
                    {
                        // IAddDimension2 is the strongly typed installed-typelib path.  The object remains native and
                        // associative; only its temporary RCW is released after the document owns the dimension.
                        // IAddDimension2 是 installed typelib 中已核对的强类型路径。尺寸仍由 SOLIDWORKS 原生维护
                        // 并关联到草图；文档接管尺寸后这里只释放临时 RCW。
                        DisplayDimension? nativeProfileDimension = model.IAddDimension2(
                            segmentMidpointX,
                            segmentMidpointY - 0.01d,
                            0d);
                        if (nativeProfileDimension is null)
                        {
                            return SolidWorksProviderResults.Failure<SolidWorksCreatedPart>(
                                "part.create",
                                new OperationError(
                                    ErrorCodes.InvariantViolation,
                                    "SOLIDWORKS returned no native driving dimension for the polygon profile.",
                                    ErrorCategories.Invariant,
                                    remediation: "Preserve the artifact and inspect the sketch dimension API result before drawing generation."));
                        }

                        SolidWorksDocumentRouting.Release(nativeProfileDimension);
                    }
                    finally
                    {
                        // Always restore the user's preference before leaving the STA.  A failed restore is allowed to
                        // surface as a provider failure instead of silently changing interactive SOLIDWORKS state.
                        // 离开 STA 前必须恢复用户原值；恢复失败要作为 Provider failure 暴露，不能静默污染用户环境。
                        application.SetUserPreferenceToggle(inputDimensionValuePreference, previousInputDimensionValuePreference);
                    }

                    model.ClearSelection2(true);
                }
                finally
                {
                    if (sketchOpen)
                    {
                        model.SketchManager.InsertSketch(true);
                    }
                }

                model.ClearSelection2(true);
                profileFeatureName = FindFirstSketchFeatureName(model);
                if (string.IsNullOrWhiteSpace(profileFeatureName))
                {
                    return SolidWorksProviderResults.Failure<SolidWorksCreatedPart>(
                        "part.create",
                        new OperationError(
                            ErrorCodes.InvariantViolation,
                            "The polygon sketch was created but could not be identified in the feature tree.",
                            ErrorCategories.Invariant,
                            remediation: "Preserve the test artifact and inspect the SOLIDWORKS feature tree."));
                }
            }
            else if (request.InitialRectangle is RectangleProfileRequest rectangleProfile)
            {
                if (rectangleProfile.Width.Millimeters <= 0d || rectangleProfile.Height.Millimeters <= 0d)
                {
                    return SolidWorksProviderResults.Failure<SolidWorksCreatedPart>(
                        "part.create",
                        new OperationError(
                            ErrorCodes.InvalidRequest,
                            "The initial rectangle width and height must be greater than zero.",
                            ErrorCategories.Validation));
                }

                bool planeSelected = model.Extension.SelectByID2(
                    "Front Plane",
                    "PLANE",
                    0d,
                    0d,
                    0d,
                    false,
                    0,
                    null,
                    0);
                if (!planeSelected)
                {
                    return SolidWorksProviderResults.Failure<SolidWorksCreatedPart>(
                        "part.create",
                        new OperationError(
                            ErrorCodes.ProviderFailure,
                            "The SOLIDWORKS Front Plane could not be selected for the initial rectangle sketch.",
                            ErrorCategories.Provider));
                }

                model.SketchManager.InsertSketch(true);
                object? rectangleSegments = null;
                try
                {
                    // CreateCornerRectangle is the verified installed-typelib primitive. Supplying symmetric corners
                    // keeps the profile centered so declarative hole centers use the same engineering coordinates.
                    // CreateCornerRectangle 是已核对 installed typelib 的 primitive；使用对称角点让轮廓居中，
                    // 这样 declarative hole center 可以继续使用同一工程坐标。
                    rectangleSegments = model.SketchManager.CreateCornerRectangle(
                        -rectangleProfile.Width.ToMeters() / 2d,
                        -rectangleProfile.Height.ToMeters() / 2d,
                        0d,
                        rectangleProfile.Width.ToMeters() / 2d,
                        rectangleProfile.Height.ToMeters() / 2d,
                        0d);
                }
                finally
                {
                    model.SketchManager.InsertSketch(true);
                    SolidWorksDocumentRouting.Release(rectangleSegments);
                }

                model.ClearSelection2(true);
                profileFeatureName = FindFirstSketchFeatureName(model);
                if (string.IsNullOrWhiteSpace(profileFeatureName))
                {
                    return SolidWorksProviderResults.Failure<SolidWorksCreatedPart>(
                        "part.create",
                        new OperationError(
                            ErrorCodes.InvariantViolation,
                            "The initial rectangle sketch was created but could not be identified in the feature tree.",
                            ErrorCategories.Invariant,
                            remediation: "Preserve the test artifact and inspect the SOLIDWORKS feature tree."));
                }
            }
            else if (request.InitialCircleRadius is Length radius)
            {
                if (radius.Millimeters <= 0d)
                {
                    return SolidWorksProviderResults.Failure<SolidWorksCreatedPart>(
                        "part.create",
                        new OperationError(
                            ErrorCodes.InvalidRequest,
                            "The initial circle radius must be greater than zero.",
                            ErrorCategories.Validation));
                }

                bool planeSelected = model.Extension.SelectByID2(
                    "Front Plane",
                    "PLANE",
                    0d,
                    0d,
                    0d,
                    false,
                    0,
                    null,
                    0);
                if (!planeSelected)
                {
                    return SolidWorksProviderResults.Failure<SolidWorksCreatedPart>(
                        "part.create",
                        new OperationError(
                            ErrorCodes.ProviderFailure,
                            "The SOLIDWORKS Front Plane could not be selected for the initial sketch.",
                            ErrorCategories.Provider));
                }

                model.SketchManager.InsertSketch(true);
                SketchSegment? circle = model.SketchManager.CreateCircleByRadius(0d, 0d, 0d, radius.ToMeters());
                model.SketchManager.InsertSketch(true);
                SolidWorksDocumentRouting.Release(circle);
                model.ClearSelection2(true);
                profileFeatureName = FindFirstSketchFeatureName(model);
                if (string.IsNullOrWhiteSpace(profileFeatureName))
                {
                    return SolidWorksProviderResults.Failure<SolidWorksCreatedPart>(
                        "part.create",
                        new OperationError(
                            ErrorCodes.InvariantViolation,
                            "The initial sketch was created but could not be identified in the feature tree.",
                            ErrorCategories.Invariant,
                            remediation: "Preserve the test artifact and inspect the SOLIDWORKS feature tree."));
                }
            }

            // SaveAs3(NewName, SaveAsVersion, Options) is an old but available SOLIDWORKS API.  The second argument
            // is the file-format version, while the third is the bitmask containing Silent; mixing these values
            // produces swFileSaveFormatNotAvailable (32) even for a valid native .sldprt target.
            // SaveAs3(NewName, SaveAsVersion, Options) 是仍可用的旧 SOLIDWORKS API：第二参数是文件格式版本，
            // 第三参数才是包含 Silent 的选项位掩码；交换它们会让有效的 .sldprt 目标返回 32。
            int saveError = model.SaveAs3(
                path,
                (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent);
            if (saveError != 0)
            {
                return SolidWorksProviderResults.Failure<SolidWorksCreatedPart>(
                    "part.create",
                    new OperationError(
                        ErrorCodes.ProviderFailure,
                        "SOLIDWORKS could not save the newly created part.",
                        ErrorCategories.Provider,
                        details: new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["save-error-code"] = saveError.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            ["target-path"] = path,
                        },
                        remediation: "Preserve the test artifact and inspect the save error code before retrying."));
            }

            string actualPath = Path.GetFullPath(model.GetPathName()?.Trim() ?? string.Empty);
            persistedTargetOwned = actualPath.Equals(path, StringComparison.OrdinalIgnoreCase);
            if (!actualPath.Equals(path, StringComparison.OrdinalIgnoreCase)
                || !SolidWorksDocumentRouting.MatchesType(model, CadDocumentType.Part))
            {
                return SolidWorksProviderResults.Failure<SolidWorksCreatedPart>(
                    "part.create",
                    new OperationError(
                        ErrorCodes.StateConflict,
                        "The newly created document did not reopen with the expected path and part type identity.",
                        ErrorCategories.State));
            }

            var descriptor = new SolidWorksDocumentDescriptor(
                new DocumentId($"{sessionId.Value}:document:{Guid.NewGuid():N}"),
                CadDocumentType.Part,
                path,
                model.GetTitle()?.Trim() ?? Path.GetFileNameWithoutExtension(path),
                SolidWorksDocumentRouting.ReadConfiguration(model),
                SolidWorksDocumentRouting.ComputeStateHash(model),
                model.GetSaveFlag(),
                profileFeatureName);
            ownershipTransferred = true;
            return SolidWorksProviderResults.Success(
                "part.create",
                new SolidWorksCreatedPart(descriptor),
                new EvidenceObservation("document.id", descriptor.DocumentId.Value),
                new EvidenceObservation("document.path", descriptor.Path),
                new EvidenceObservation("document.type", descriptor.DocumentType.ToString()),
                new EvidenceObservation("document.configuration", descriptor.Configuration),
                new EvidenceObservation("state.hash", descriptor.StateHash),
                new EvidenceObservation("initial.sketch", profileFeatureName ?? "none"));
        }
        catch (Exception exception)
        {
            return SolidWorksProviderResults.ProviderFailure<SolidWorksCreatedPart>(
                "part.create",
                exception,
                "SOLIDWORKS part creation failed before a verified document identity was returned.");
        }
        finally
        {
            // If verification fails after SaveAs3, close only the exact clean target that this operation persisted.
            // Never close an unknown active document, and never close a dirty document that could trigger a modal save
            // prompt. If verification succeeds, ownership is transferred to the returned document facade.
            // 若 SaveAs3 后的验证失败，只关闭本 operation 持久化且确认路径精确匹配、同时保持 clean 的 target。
            // 绝不关闭未知 ActiveDoc，也不关闭可能触发 modal save prompt 的 dirty document。验证成功后 ownership
            // 转移给返回的 document facade。
            if (!ownershipTransferred && persistedTargetOwned && model is not null)
            {
                TryCloseCleanPersistedTarget(application, model, path);
            }

            // The document remains open in SOLIDWORKS, but this provider-owned RCW is released on the STA.  Future
            // operations reacquire it by descriptor.Path and verify identity again.
            // 文档仍保持在 SOLIDWORKS 中打开，但 Provider-owned RCW 在 STA 上释放；后续操作按 path 重获并再次校验。
            SolidWorksDocumentRouting.Release(model);
        }
    }

    /// <summary>
    /// Compensates a post-save verification failure without accepting an unknown modal state.
    /// 对保存后的验证失败执行补偿关闭，但不接受未知 modal 状态。
    /// </summary>
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
            try
            {
                // The operation has no safe recovery if SOLIDWORKS retained the exact document after CloseDoc.
                // Keep the artifact for diagnosis; do not attempt blind retry/force-close here.
                // CloseDoc 后若 exact document 仍 open，则保留 artifact 供诊断；不 blind retry 或 force-close。
            }
            finally
            {
                SolidWorksDocumentRouting.Release(stillOpen);
            }
        }
        catch
        {
            // Compensation is deliberately bounded and fail-closed. The original operation result remains the
            // authoritative failure; swallowing here prevents a cleanup COM exception from masking it.
            // 补偿操作故意 bounded 且 fail-closed；原 operation result 仍是权威 failure，避免 cleanup COM exception 覆盖原错。
        }
    }

    private static string? FindFirstSketchFeatureName(ModelDoc2 model)
    {
        var current = model.FirstFeature() as IFeature;
        try
        {
            while (current is not null)
            {
                string typeName = current.GetTypeName2()?.Trim() ?? string.Empty;
                string name = current.Name?.Trim() ?? string.Empty;
                // SW2022 reports the origin as OriginProfileFeature, so a broad "Profile" match selects Origin and
                // makes FeatureExtrusion3 return null.  The real feature-tree evidence is Sketch1:ProfileFeature;
                // require both parts of that observed identity for this deterministic first-sketch workflow.
                // SW2022 会把原点报告为 OriginProfileFeature，宽泛的 Profile 匹配会选中 Origin 并令
                // FeatureExtrusion3 返回 null。真实 feature tree 证据是 Sketch1:ProfileFeature，故本流程要求两者同时满足。
                if (typeName.Equals("ProfileFeature", StringComparison.OrdinalIgnoreCase)
                    && name.StartsWith("Sketch", StringComparison.OrdinalIgnoreCase))
                {
                    return name;
                }

                var next = current.GetNextFeature() as IFeature;
                SolidWorksDocumentRouting.Release(current);
                current = next;
            }

            return null;
        }
        finally
        {
            SolidWorksDocumentRouting.Release(current);
        }
    }

    /// <summary>
    /// Creates one native associative line dimension on an open sketch and restores the user's modal preference.
    /// 在打开的草图中创建一个原生关联线性尺寸，并恢复用户的模态偏好。
    /// </summary>
    /// <remarks>
    /// The official API distinguishes a dimension value from the DisplayDimension.MarkedForDrawing state. This
    /// helper follows the verified installed-typelib path used by the polygon baseline and keeps the COM object on
    /// the provider STA. The drawing layer still imports and verifies the resulting native annotation.
    /// 官方 API 区分尺寸值与 DisplayDimension.MarkedForDrawing 状态。本 helper 复用已核验的 installed typelib
    /// 路径，并让 COM 对象始终留在 Provider STA；工程图层仍负责导入和验证最终 native annotation。
    /// </remarks>
    private static string? TryCreateNativeDrivingDimension(
        ISldWorks application,
        ModelDoc2 model,
        Coordinate2D start,
        Coordinate2D end,
        string profileDescription)
    {
        double midpointX = (start.X.ToMeters() + end.X.ToMeters()) / 2d;
        double midpointY = (start.Y.ToMeters() + end.Y.ToMeters()) / 2d;
        bool selected = model.Extension.SelectByID2(
            string.Empty,
            "SKETCHSEGMENT",
            midpointX,
            midpointY,
            0d,
            false,
            0,
            null,
            0);
        if (!selected)
        {
            return $"SOLIDWORKS could not select a native {profileDescription} edge for its driving dimension.";
        }

        // Disabling the documented input-dimension prompt is a bounded COM preflight. It avoids a modal dialog on
        // the provider STA and never sends blind Enter/Escape/OK keystrokes.
        // 关闭官方 input-dimension prompt 是有边界的 COM preflight；它避免 Provider STA 被模态框阻塞，绝不发送
        // blind Enter/Escape/OK 按键。
        const int inputDimensionValuePreference = (int)swUserPreferenceToggle_e.swInputDimValOnCreate;
        bool previousPreference = application.GetUserPreferenceToggle(inputDimensionValuePreference);
        application.SetUserPreferenceToggle(inputDimensionValuePreference, false);
        try
        {
            DisplayDimension? nativeDimension = model.IAddDimension2(midpointX, midpointY - 0.01d, 0d);
            if (nativeDimension is null)
            {
                return $"SOLIDWORKS returned no native driving dimension for the {profileDescription}.";
            }

            SolidWorksDocumentRouting.Release(nativeDimension);
            return null;
        }
        finally
        {
            // Restore the interactive preference before returning; a restore exception intentionally surfaces
            // through the provider's structured failure path instead of silently changing SOLIDWORKS state.
            // 返回前恢复交互偏好；恢复异常有意通过 Provider 结构化 failure 路径暴露，不静默改变状态。
            application.SetUserPreferenceToggle(inputDimensionValuePreference, previousPreference);
        }
    }
}
