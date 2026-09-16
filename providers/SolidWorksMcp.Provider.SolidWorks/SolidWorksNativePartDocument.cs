using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Provider.SolidWorks;

/// <summary>Vendor-neutral part document facade with STA-routed, identity-checked native operations.</summary>
/// <remarks>
/// This facade owns no ModelDoc2 RCW.  Every call resolves the registered persisted path, checks the document type and
/// active configuration, performs the smallest COM action, rebuilds, then inspects.  facade 不保存 ModelDoc2 RCW；每次
/// 调用都重新解析 persisted path、校验 type/configuration，执行最小 COM action，再 rebuild 和 inspect。
/// </remarks>
internal sealed class SolidWorksNativePartDocument(
    SolidWorksComSessionHost host,
    SolidWorksDocumentRegistry registry,
    SessionId sessionId,
    SolidWorksDocumentDescriptor initialDescriptor) : ICadPartDocument
{
    private readonly SolidWorksComSessionHost host = host ?? throw new ArgumentNullException(nameof(host));
    private readonly SolidWorksDocumentRegistry registry = registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly SessionId sessionId = sessionId;
    private SolidWorksDocumentDescriptor descriptor = initialDescriptor ?? throw new ArgumentNullException(nameof(initialDescriptor));

    /// <inheritdoc />
    public DocumentId DocumentId => descriptor.DocumentId;

    /// <inheritdoc />
    public CadDocumentType DocumentType => descriptor.DocumentType;

    /// <inheritdoc />
    public string Path => descriptor.Path;

    /// <inheritdoc />
    public string Configuration => descriptor.Configuration;

    /// <inheritdoc />
    public string StateHash => descriptor.StateHash;

    /// <inheritdoc />
    public bool IsDirty => descriptor.IsDirty;

    /// <inheritdoc />
    public Task<OperationResult<BodySnapshot>> CreateBodyAsync(
        CreateBodyRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(SolidWorksProviderResults.Unsupported<BodySnapshot>(
            "body.create",
            new CadCapability(
                CadCapabilityNames.PartMutation,
                supported: false,
                "B03 first closes the create-sketch-extrusion inspection loop; explicit empty-body creation follows its API proof.")));

    /// <summary>Creates a blind extrusion from the verified initial sketch profile.</summary>
    public async Task<OperationResult<FeatureSnapshot>> AddExtrusionAsync(
        ExtrusionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Depth.Millimeters <= 0d)
        {
            return SolidWorksProviderResults.Failure<FeatureSnapshot>(
                "feature.extrusion",
                new OperationError(
                    ErrorCodes.InvalidRequest,
                    "The extrusion depth must be greater than zero.",
                    ErrorCategories.Validation));
        }

        if (string.IsNullOrWhiteSpace(descriptor.ProfileFeatureName))
        {
            return SolidWorksProviderResults.Failure<FeatureSnapshot>(
                "feature.extrusion",
                new OperationError(
                    ErrorCodes.InvalidRequest,
                    "This part has no provider-identified sketch profile for the B03 extrusion workflow.",
                    ErrorCategories.Validation,
                    remediation: "Create the part with InitialCircleRadius, then retry the extrusion."));
        }

        OperationResult<NativeExtrusionResult> result = await host.InvokeOnStaAsync(
            sessionId,
            application => AddExtrusionOnSta(application, request),
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Value is null)
        {
            return OperationResults.Failure<FeatureSnapshot>(result.OperationId, result.Error!, result.Evidence);
        }

        descriptor = result.Value.Descriptor;
        registry.TryUpdateDescriptor(descriptor, out SolidWorksDocumentDescriptor? updated);
        if (updated is not null)
        {
            descriptor = updated;
        }

        return OperationResults.Success(
            result.Value.Feature,
            result.OperationId,
            result.Evidence ?? new OperationEvidence("solidworks-part"));
    }

    /// <inheritdoc />
    public async Task<OperationResult<RebuildReceipt>> RebuildAsync(CancellationToken cancellationToken = default)
    {
        OperationResult<NativeRebuildResult> result = await host.InvokeOnStaAsync(
            sessionId,
            RebuildOnSta,
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Value is null)
        {
            return OperationResults.Failure<RebuildReceipt>(result.OperationId, result.Error!, result.Evidence);
        }

        descriptor = result.Value.Descriptor;
        registry.TryUpdateDescriptor(descriptor, out SolidWorksDocumentDescriptor? updated);
        if (updated is not null)
        {
            descriptor = updated;
        }

        return OperationResults.Success(
            result.Value.Receipt,
            result.OperationId,
            result.Evidence ?? new OperationEvidence("solidworks-part"));
    }

    /// <inheritdoc />
    public async Task<OperationResult<SaveReceipt>> SaveAsync(CancellationToken cancellationToken = default)
    {
        OperationResult<NativeSaveResult> result = await host.InvokeOnStaAsync(
            sessionId,
            SaveOnSta,
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Value is null)
        {
            return OperationResults.Failure<SaveReceipt>(result.OperationId, result.Error!, result.Evidence);
        }

        descriptor = result.Value.Descriptor;
        registry.TryUpdateDescriptor(descriptor, out SolidWorksDocumentDescriptor? updated);
        if (updated is not null)
        {
            descriptor = updated;
        }

        return OperationResults.Success(
            result.Value.Receipt,
            result.OperationId,
            result.Evidence ?? new OperationEvidence("solidworks-part"));
    }

    private OperationResult<NativeExtrusionResult> AddExtrusionOnSta(ISldWorks application, ExtrusionRequest request)
    {
        if (!registry.TryGet(descriptor.DocumentId, out SolidWorksDocumentDescriptor? current) || current is null)
        {
            return SolidWorksProviderResults.Failure<NativeExtrusionResult>(
                "feature.extrusion",
                new OperationError(ErrorCodes.NotFound, "The native document identity is no longer registered.", ErrorCategories.State));
        }

        string stage = "resolve-document";
        OperationResult<ModelDoc2> resolved = SolidWorksDocumentRouting.ResolveOpenDocument(
            application,
            current,
            activate: true,
            verifyStateHash: true);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return OperationResults.Failure<NativeExtrusionResult>(resolved.OperationId, resolved.Error!, resolved.Evidence);
        }

        ModelDoc2 model = resolved.Value;
        IFeature? feature = null;
        try
        {
            string profileFeatureName = current.ProfileFeatureName!;
            stage = "clear-selection";
            model.ClearSelection2(true);
            stage = "select-profile";
            bool sketchSelected = model.Extension.SelectByID2(
                profileFeatureName,
                "SKETCH",
                0d,
                0d,
                0d,
                false,
                0,
                null,
                0);
            if (!sketchSelected)
            {
                return SolidWorksProviderResults.Failure<NativeExtrusionResult>(
                    "feature.extrusion",
                    new OperationError(
                        ErrorCodes.SelectionStale,
                        "The registered sketch profile could not be selected in the current model state.",
                        ErrorCategories.State,
                        remediation: "Re-inspect the document and do not reuse a stale profile identity."));
            }

            stage = "feature-extrusion3";
            feature = model.FeatureManager.FeatureExtrusion3(
                // SOLIDWORKS calls Sd the single-direction switch.  A blind extrusion with D2=0 must set it to true;
                // UseAutoSelect lets SOLIDWORKS resolve the selected closed sketch into the new solid body.
                // SOLIDWORKS 将 Sd 定义为单向开关；D2=0 的盲拉伸必须为 true，UseAutoSelect 让 SOLIDWORKS 将已选闭合
                // 草图解析为新 solid body。这两个参数错误时，真实 SW2022 会静默返回 null feature。
                Sd: true,
                Flip: false,
                Dir: false,
                T1: (int)swEndConditions_e.swEndCondBlind,
                T2: (int)swEndConditions_e.swEndCondBlind,
                D1: request.Depth.ToMeters(),
                D2: 0d,
                Dchk1: false,
                Dchk2: false,
                Ddir1: false,
                Ddir2: false,
                Dang1: 0d,
                Dang2: 0d,
                OffsetReverse1: false,
                OffsetReverse2: false,
                TranslateSurface1: false,
                TranslateSurface2: false,
                Merge: true,
                UseFeatScope: false,
                UseAutoSelect: true,
                T0: 0,
                StartOffset: 0d,
                FlipStartOffset: false);
            if (feature is null)
            {
                return SolidWorksProviderResults.Failure<NativeExtrusionResult>(
                    "feature.extrusion",
                    new OperationError(
                        ErrorCodes.ProviderFailure,
                        "SOLIDWORKS returned no feature from FeatureExtrusion3.",
                        ErrorCategories.Provider));
            }

            // Feature-tree inspection may return the same COM identity as FeatureExtrusion3.  The inspection reader
            // is allowed to release its traversal RCWs, so copy the feature metadata immediately and never
            // dereference the feature RCW after inspection.  COM feature metadata must be snapshotted before a
            // sibling reader can FinalReleaseComObject the shared RCW.  feature tree inspection 可能返回与
            // FeatureExtrusion3 相同的 COM identity；reader 可以释放 traversal RCW，因此必须立即复制 metadata，
            // inspection 后禁止再次解引用 feature RCW，避免 InvalidComObjectException。
            stage = "read-feature-metadata";
            string featureName = feature.Name?.Trim() ?? "Extrusion";
            string featureKind = feature.GetTypeName2()?.Trim() ?? "Extrusion";

            stage = "rebuild";
            bool rebuilt = model.ForceRebuild3(true);
            if (!rebuilt)
            {
                return SolidWorksProviderResults.Failure<NativeExtrusionResult>(
                    "feature.extrusion",
                    new OperationError(
                        ErrorCodes.InvariantViolation,
                        "SOLIDWORKS did not report a successful rebuild after extrusion.",
                        ErrorCategories.Invariant,
                        remediation: "Preserve the document and inspect the SOLIDWORKS feature error state."));
            }

            stage = "inspect-result";
            OperationResult<CadInspectionSnapshot> inspection = SolidWorksNativeInspectionReader.ReadPart(model, current);
            if (!inspection.IsSuccess || inspection.Value is null)
            {
                return OperationResults.Failure<NativeExtrusionResult>(inspection.OperationId, inspection.Error!, inspection.Evidence);
            }

            if (inspection.Value.Bodies.Length == 0 || inspection.Value.Bodies[0].Volume.CubicMillimeters <= 0d)
            {
                return SolidWorksProviderResults.Failure<NativeExtrusionResult>(
                    "feature.extrusion",
                    new OperationError(
                        ErrorCodes.InvariantViolation,
                        "Extrusion returned but inspection did not prove a positive solid volume.",
                        ErrorCategories.Invariant,
                        remediation: "Preserve the artifact and inspect profile selection, feature errors and units."));
            }

            var featureSnapshot = new FeatureSnapshot
            {
                FeatureId = new FeatureId($"{current.DocumentId.Value}:feature:{featureName}"),
                Name = featureName,
                Kind = featureKind,
                BodyId = inspection.Value.Bodies[0].BodyId,
                Depth = request.Depth,
            };
            var nextDescriptor = current with
            {
                StateHash = inspection.Value.Document.StateHash,
                IsDirty = model.GetSaveFlag(),
            };
            return SolidWorksProviderResults.Success(
                "feature.extrusion",
                new NativeExtrusionResult(featureSnapshot, nextDescriptor),
                new EvidenceObservation("feature.name", featureSnapshot.Name),
                new EvidenceObservation("feature.kind", featureSnapshot.Kind),
                new EvidenceObservation("feature.depth", request.Depth.ToString()),
                new EvidenceObservation("body.count", inspection.Value.Bodies.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new EvidenceObservation("volume.cubic-millimeters", inspection.Value.Bodies[0].Volume.CubicMillimeters.ToString("G17", System.Globalization.CultureInfo.InvariantCulture)),
                new EvidenceObservation("state.hash", nextDescriptor.StateHash));
        }
        catch (System.Runtime.InteropServices.InvalidComObjectException exception)
        {
            return SolidWorksProviderResults.Failure<NativeExtrusionResult>(
                "feature.extrusion",
                new OperationError(
                    ErrorCodes.ProviderFailure,
                    "SOLIDWORKS separated a COM object during the native extrusion workflow.",
                    ErrorCategories.Provider,
                    retryable: true,
                    details: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["stage"] = stage,
                        ["hresult"] = $"0x{exception.HResult:X8}",
                    },
                    remediation: "Preserve the isolated artifact and inspect RCW lifetime at the reported stage."));
        }
        finally
        {
            SolidWorksDocumentRouting.Release(feature);
            SolidWorksDocumentRouting.Release(model);
        }
    }

    private OperationResult<NativeRebuildResult> RebuildOnSta(ISldWorks application)
    {
        if (!registry.TryGet(descriptor.DocumentId, out SolidWorksDocumentDescriptor? current) || current is null)
        {
            return SolidWorksProviderResults.Failure<NativeRebuildResult>(
                "part.rebuild",
                new OperationError(ErrorCodes.NotFound, "The native document identity is no longer registered.", ErrorCategories.State));
        }

        OperationResult<ModelDoc2> resolved = SolidWorksDocumentRouting.ResolveOpenDocument(
            application,
            current,
            activate: true,
            verifyStateHash: true);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return OperationResults.Failure<NativeRebuildResult>(resolved.OperationId, resolved.Error!, resolved.Evidence);
        }

        try
        {
            bool rebuilt = resolved.Value.ForceRebuild3(true);
            if (!rebuilt)
            {
                return SolidWorksProviderResults.Failure<NativeRebuildResult>(
                    "part.rebuild",
                    new OperationError(
                        ErrorCodes.InvariantViolation,
                        "SOLIDWORKS ForceRebuild3 returned false.",
                        ErrorCategories.Invariant));
            }

            string stateHash = SolidWorksDocumentRouting.ComputeStateHash(resolved.Value);
            var updated = current with { StateHash = stateHash, IsDirty = resolved.Value.GetSaveFlag() };
            var receipt = new RebuildReceipt { StateHash = stateHash, HasErrors = false };
            return SolidWorksProviderResults.Success(
                "part.rebuild",
                new NativeRebuildResult(receipt, updated),
                new EvidenceObservation("rebuild.returned", bool.TrueString),
                new EvidenceObservation("state.hash", stateHash));
        }
        finally
        {
            SolidWorksDocumentRouting.Release(resolved.Value);
        }
    }

    private OperationResult<NativeSaveResult> SaveOnSta(ISldWorks application)
    {
        if (!registry.TryGet(descriptor.DocumentId, out SolidWorksDocumentDescriptor? current) || current is null)
        {
            return SolidWorksProviderResults.Failure<NativeSaveResult>(
                "part.save",
                new OperationError(ErrorCodes.NotFound, "The native document identity is no longer registered.", ErrorCategories.State));
        }

        OperationResult<ModelDoc2> resolved = SolidWorksDocumentRouting.ResolveOpenDocument(
            application,
            current,
            activate: true,
            verifyStateHash: true);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return OperationResults.Failure<NativeSaveResult>(resolved.OperationId, resolved.Error!, resolved.Evidence);
        }

        try
        {
            int saveErrors = 0;
            int saveWarnings = 0;
            bool saved = resolved.Value.Save3(
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                ref saveErrors,
                ref saveWarnings);
            if (!saved || saveErrors != 0)
            {
                return SolidWorksProviderResults.Failure<NativeSaveResult>(
                    "part.save",
                    new OperationError(
                        ErrorCodes.ProviderFailure,
                        "SOLIDWORKS Save3 did not prove a successful save.",
                        ErrorCategories.Provider,
                        details: new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["save-returned"] = saved.ToString(),
                            ["save-errors"] = saveErrors.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            ["save-warnings"] = saveWarnings.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        }));
            }

            string stateHash = SolidWorksDocumentRouting.ComputeStateHash(resolved.Value);
            var updated = current with { StateHash = stateHash, IsDirty = resolved.Value.GetSaveFlag() };
            var receipt = new SaveReceipt { Path = current.Path, StateHash = stateHash };
            return SolidWorksProviderResults.Success(
                "part.save",
                new NativeSaveResult(receipt, updated),
                new EvidenceObservation("save.returned", saved.ToString()),
                new EvidenceObservation("save.errors", saveErrors.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new EvidenceObservation("save.warnings", saveWarnings.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new EvidenceObservation("state.hash", stateHash));
        }
        finally
        {
            SolidWorksDocumentRouting.Release(resolved.Value);
        }
    }
}

/// <summary>Internal result carrying the feature and the newly inspected document metadata.</summary>
internal sealed record NativeExtrusionResult(FeatureSnapshot Feature, SolidWorksDocumentDescriptor Descriptor);

/// <summary>Internal result carrying a rebuild receipt and updated document metadata.</summary>
internal sealed record NativeRebuildResult(RebuildReceipt Receipt, SolidWorksDocumentDescriptor Descriptor);

/// <summary>Internal result carrying a save receipt and updated document metadata.</summary>
internal sealed record NativeSaveResult(SaveReceipt Receipt, SolidWorksDocumentDescriptor Descriptor);
