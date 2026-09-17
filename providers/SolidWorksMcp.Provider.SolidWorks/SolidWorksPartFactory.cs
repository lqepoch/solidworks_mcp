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
    /// <summary>Creates a new part, optionally adds a deterministic initial circle sketch, saves and verifies it.</summary>
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

            int profileCount = (request.InitialPolygon is not null ? 1 : 0)
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
            if (request.InitialPolygon is PolygonProfileRequest polygonProfile)
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
}
