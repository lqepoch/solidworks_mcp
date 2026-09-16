using System.Collections.Immutable;
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
    string attachmentGeneration,
    SolidWorksDocumentDescriptor initialDescriptor) : ICadPartDocument
{
    private readonly SolidWorksComSessionHost host = host ?? throw new ArgumentNullException(nameof(host));
    private readonly SolidWorksDocumentRegistry registry = registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly SessionId sessionId = sessionId;
    private readonly string attachmentGeneration = string.IsNullOrWhiteSpace(attachmentGeneration)
        ? throw new ArgumentException("The native session attachment generation is required.", nameof(attachmentGeneration))
        : attachmentGeneration;
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
            attachmentGeneration,
            application => AddExtrusionOnSta(application, request),
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Value is null)
        {
            return OperationResults.Failure<FeatureSnapshot>(result.OperationId, result.Error!, result.Evidence);
        }

        if (!TryCommitDescriptor(result.Value.ExpectedDescriptor, result.Value.Descriptor, out _))
        {
            return DescriptorCommitFailure<FeatureSnapshot>(
                result.OperationId,
                result.Value.ExpectedDescriptor,
                result.Value.Descriptor);
        }

        return OperationResults.Success(
            result.Value.Feature,
            result.OperationId,
            result.Evidence ?? new OperationEvidence("solidworks-part"));
    }

    /// <inheritdoc />
    public async Task<OperationResult<DimensionSnapshot>> SetDimensionValueAsync(
        DimensionUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ParameterName)
            || request.ParameterName.Contains('\r')
            || request.ParameterName.Contains('\n')
            || request.ParameterName.IndexOf('@') <= 0
            || request.Value.Millimeters <= 0d)
        {
            return SolidWorksProviderResults.Failure<DimensionSnapshot>(
                "dimension.set",
                new OperationError(
                    ErrorCodes.InvalidRequest,
                    "A full SOLIDWORKS dimension name and a positive canonical value are required.",
                    ErrorCategories.Validation,
                    remediation: "Use a full parameter name such as D1@FeatureName and a value in millimetres."));
        }

        OperationResult<NativeDimensionResult> result = await host.InvokeOnStaAsync(
            sessionId,
            attachmentGeneration,
            application => SetDimensionOnSta(application, request),
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Value is null)
        {
            return OperationResults.Failure<DimensionSnapshot>(result.OperationId, result.Error!, result.Evidence);
        }

        if (!TryCommitDescriptor(result.Value.ExpectedDescriptor, result.Value.Descriptor, out _))
        {
            return DescriptorCommitFailure<DimensionSnapshot>(
                result.OperationId,
                result.Value.ExpectedDescriptor,
                result.Value.Descriptor);
        }

        return OperationResults.Success(
            result.Value.Dimension,
            result.OperationId,
            result.Evidence ?? new OperationEvidence("solidworks-part"));
    }

    /// <inheritdoc />
    public async Task<OperationResult<RebuildReceipt>> RebuildAsync(CancellationToken cancellationToken = default)
    {
        OperationResult<NativeRebuildResult> result = await host.InvokeOnStaAsync(
            sessionId,
            attachmentGeneration,
            RebuildOnSta,
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Value is null)
        {
            return OperationResults.Failure<RebuildReceipt>(result.OperationId, result.Error!, result.Evidence);
        }

        if (!TryCommitDescriptor(result.Value.ExpectedDescriptor, result.Value.Descriptor, out _))
        {
            return DescriptorCommitFailure<RebuildReceipt>(
                result.OperationId,
                result.Value.ExpectedDescriptor,
                result.Value.Descriptor);
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
            attachmentGeneration,
            SaveOnSta,
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Value is null)
        {
            return OperationResults.Failure<SaveReceipt>(result.OperationId, result.Error!, result.Evidence);
        }

        if (!TryCommitDescriptor(result.Value.ExpectedDescriptor, result.Value.Descriptor, out _))
        {
            return DescriptorCommitFailure<SaveReceipt>(
                result.OperationId,
                result.Value.ExpectedDescriptor,
                result.Value.Descriptor);
        }

        return OperationResults.Success(
            result.Value.Receipt,
            result.OperationId,
            result.Evidence ?? new OperationEvidence("solidworks-part"));
    }

    /// <inheritdoc />
    public async Task<OperationResult<MutationReceipt>> CloseAsync(CancellationToken cancellationToken = default)
    {
        OperationResult<MutationReceipt> result = await host.InvokeOnStaAsync(
            sessionId,
            attachmentGeneration,
            CloseOnSta,
            cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            // The facade must not route another request to a document after CloseDoc was verified.  Removing the
            // registry entry also makes accidental reuse fail as a structured state error instead of touching ActiveDoc.
            // CloseDoc 验证成功后，facade 不得再把请求路由到该 document；移除 registry entry 可让误用返回结构化
            // state error，而不是误操作当时的 ActiveDoc。
            registry.Remove(DocumentId);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<OperationResult<CadInspectionSnapshot>> ReopenAndInspectAsync(
        CancellationToken cancellationToken = default)
    {
        OperationResult<NativeReopenResult> result = await host.InvokeOnStaAsync(
            sessionId,
            attachmentGeneration,
            ReopenAndInspectOnSta,
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Value is null)
        {
            return OperationResults.Failure<CadInspectionSnapshot>(result.OperationId, result.Error!, result.Evidence);
        }

        if (!TryCommitDescriptor(result.Value.ExpectedDescriptor, result.Value.Descriptor, out _))
        {
            return DescriptorCommitFailure<CadInspectionSnapshot>(
                result.OperationId,
                result.Value.ExpectedDescriptor,
                result.Value.Descriptor);
        }

        return OperationResults.Success(
            result.Value.Snapshot,
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

            if (inspection.Value.HasErrors)
            {
                return OperationResults.Failure<NativeExtrusionResult>(
                    "solidworks:feature.extrusion",
                    new OperationError(
                        ErrorCodes.InvariantViolation,
                        "SOLIDWORKS reported feature errors after the extrusion rebuild.",
                        ErrorCategories.Invariant,
                        remediation: "Preserve the artifact, inspect the structured What's Wrong diagnostics and repair the feature."),
                    new OperationEvidence(
                        "solidworks-provider",
                        BuildDiagnosticEvidence(inspection.Value.Diagnostics),
                        stateHash: inspection.Value.Document.StateHash));
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
                new NativeExtrusionResult(featureSnapshot, nextDescriptor, current),
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

            // ForceRebuild3 returning true is not sufficient proof: SOLIDWORKS can still mark a feature with a
            // What's Wrong error. Reuse the complete inspection reader so geometry and diagnostics share one state hash.
            // ForceRebuild3 返回 true 仍不是充分证据：SOLIDWORKS 可能同时在 Feature 上标记 What's Wrong error。
            // 复用完整 inspection reader，让 geometry、diagnostics 共用同一个 state hash。
            OperationResult<CadInspectionSnapshot> inspection = SolidWorksNativeInspectionReader.ReadPart(resolved.Value, current);
            if (!inspection.IsSuccess || inspection.Value is null)
            {
                return OperationResults.Failure<NativeRebuildResult>(
                    "solidworks:part.rebuild",
                    inspection.Error
                        ?? new OperationError(
                            ErrorCodes.InvariantViolation,
                            "SOLIDWORKS rebuild produced no complete inspection evidence.",
                            ErrorCategories.Invariant),
                    inspection.Evidence);
            }

            ImmutableArray<CadDiagnostic> diagnostics = inspection.Value.Diagnostics;
            string stateHash = inspection.Value.Document.StateHash;
            var updated = current with { StateHash = stateHash, IsDirty = inspection.Value.Document.IsDirty };
            var receipt = new RebuildReceipt
            {
                StateHash = stateHash,
                HasErrors = inspection.Value.HasErrors,
                Diagnostics = diagnostics,
            };
            ImmutableArray<EvidenceObservation> evidence =
            [
                new EvidenceObservation("rebuild.returned", bool.TrueString),
                new EvidenceObservation("diagnostic.count", diagnostics.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new EvidenceObservation("diagnostic.error.count", diagnostics.Count(diagnostic => diagnostic.Severity is CadDiagnosticSeverity.Error).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new EvidenceObservation("diagnostic.warning.count", diagnostics.Count(diagnostic => diagnostic.Severity is CadDiagnosticSeverity.Warning).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                .. BuildDiagnosticEvidence(diagnostics),
                new EvidenceObservation("state.hash", stateHash),
            ];

            if (inspection.Value.HasErrors)
            {
                return OperationResults.Failure<NativeRebuildResult>(
                    "solidworks:part.rebuild",
                    new OperationError(
                        ErrorCodes.InvariantViolation,
                        "SOLIDWORKS rebuild returned feature errors in the post-rebuild What's Wrong inspection.",
                        ErrorCategories.Invariant,
                        remediation: "Preserve the artifact, inspect the structured diagnostics and repair the feature before retrying."),
                    new OperationEvidence("solidworks-provider", evidence, stateHash: stateHash));
            }

            return SolidWorksProviderResults.Success(
                "part.rebuild",
                new NativeRebuildResult(receipt, updated, current),
                [.. evidence]);
        }
        finally
        {
            SolidWorksDocumentRouting.Release(resolved.Value);
        }
    }

    /// <summary>
    /// Sets one full native parameter name in the registered configuration and verifies the resulting solid.
    /// 在登记的 configuration 中设置一个完整 native parameter name，并验证变更后的 solid。
    /// </summary>
    private OperationResult<NativeDimensionResult> SetDimensionOnSta(
        ISldWorks application,
        DimensionUpdateRequest request)
    {
        const string operation = "dimension.set";
        if (!registry.TryGet(descriptor.DocumentId, out SolidWorksDocumentDescriptor? current) || current is null)
        {
            return SolidWorksProviderResults.Failure<NativeDimensionResult>(
                operation,
                new OperationError(ErrorCodes.NotFound, "The native document identity is no longer registered.", ErrorCategories.State));
        }

        if (request.Configuration is not null
            && !request.Configuration.Trim().Equals(current.Configuration, StringComparison.Ordinal))
        {
            return SolidWorksProviderResults.Failure<NativeDimensionResult>(
                operation,
                new OperationError(
                    ErrorCodes.StateConflict,
                    "The requested dimension configuration does not match the registered document configuration.",
                    ErrorCategories.State,
                    details: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["expected-configuration"] = current.Configuration,
                        ["requested-configuration"] = request.Configuration.Trim(),
                    },
                    remediation: "Re-plan the operation against the document's registered active configuration."));
        }

        OperationResult<ModelDoc2> resolved = SolidWorksDocumentRouting.ResolveOpenDocument(
            application,
            current,
            activate: true,
            verifyStateHash: true);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return OperationResults.Failure<NativeDimensionResult>(resolved.OperationId, resolved.Error!, resolved.Evidence);
        }

        ModelDoc2 model = resolved.Value;
        object? parameter = null;
        string stage = "resolve-dimension";
        try
        {
            stage = "get-parameter";
            parameter = model.Parameter(request.ParameterName.Trim());
            if (parameter is not IDimension dimension)
            {
                return SolidWorksProviderResults.Failure<NativeDimensionResult>(
                    operation,
                    new OperationError(
                        ErrorCodes.SelectionStale,
                        "The requested full SOLIDWORKS dimension parameter was not found.",
                        ErrorCategories.State,
                        remediation: "Re-inspect the current feature tree and use a fresh full dimension identity."),
                    new EvidenceObservation("dimension.requested-name", request.ParameterName.Trim()));
            }

            string fullName = dimension.FullName?.Trim() ?? request.ParameterName.Trim();
            string shortName = dimension.Name?.Trim() ?? fullName.Split('@', 2)[0];
            stage = "read-before-value";
            double previousMeters = ReadCurrentDimensionMeters(dimension);

            // SetSystemValue3 is the documented configuration-aware setter.  It uses metres at the COM boundary;
            // all public requests remain canonical millimetres until this exact adapter call.
            // SetSystemValue3 是官方的 configuration-aware setter；COM 边界使用米，公共 request 一直保持毫米，
            // 仅在这个 adapter 调用点转换。
            stage = "set-system-value3";
            int setStatus = dimension.SetSystemValue3(
                request.Value.ToMeters(),
                (int)swSetValueInConfiguration_e.swSetValue_InThisConfiguration,
                null);
            if (setStatus != (int)swSetValueReturnStatus_e.swSetValue_Successful)
            {
                return SolidWorksProviderResults.Failure<NativeDimensionResult>(
                    operation,
                    new OperationError(
                        ErrorCodes.ProviderFailure,
                        "SOLIDWORKS rejected the requested dimension value.",
                        ErrorCategories.Provider,
                        details: new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["dimension"] = fullName,
                            ["set-status"] = setStatus.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        },
                        remediation: "Inspect the dimension type, driven state and feature errors before retrying."),
                    new EvidenceObservation("dimension.name", fullName),
                    new EvidenceObservation("dimension.set-status", setStatus.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            stage = "read-after-value";
            double actualMeters = ReadCurrentDimensionMeters(dimension);
            double requestedMeters = request.Value.ToMeters();
            if (Math.Abs(actualMeters - requestedMeters) > 1e-9d)
            {
                return SolidWorksProviderResults.Failure<NativeDimensionResult>(
                    operation,
                    new OperationError(
                        ErrorCodes.InvariantViolation,
                        "SOLIDWORKS accepted the dimension setter but the read-back value did not match the request.",
                        ErrorCategories.Invariant,
                        details: new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["dimension"] = fullName,
                            ["requested-meters"] = requestedMeters.ToString("G17", System.Globalization.CultureInfo.InvariantCulture),
                            ["actual-meters"] = actualMeters.ToString("G17", System.Globalization.CultureInfo.InvariantCulture),
                        },
                        remediation: "Preserve the document and inspect configuration-specific or equation-driven behavior."));
            }

            stage = "rebuild";
            bool rebuilt = model.ForceRebuild3(true);
            if (!rebuilt)
            {
                return SolidWorksProviderResults.Failure<NativeDimensionResult>(
                    operation,
                    new OperationError(
                        ErrorCodes.InvariantViolation,
                        "SOLIDWORKS did not report a successful rebuild after dimension mutation.",
                        ErrorCategories.Invariant,
                        remediation: "Preserve the artifact and inspect the feature error state."));
            }

            stage = "inspect-after-dimension";
            OperationResult<CadInspectionSnapshot> inspection = SolidWorksNativeInspectionReader.ReadPart(model, current);
            if (!inspection.IsSuccess || inspection.Value is null)
            {
                return OperationResults.Failure<NativeDimensionResult>(inspection.OperationId, inspection.Error!, inspection.Evidence);
            }

            if (inspection.Value.HasErrors)
            {
                return OperationResults.Failure<NativeDimensionResult>(
                    "solidworks:dimension.set",
                    new OperationError(
                        ErrorCodes.InvariantViolation,
                        "SOLIDWORKS reported feature errors after the dimension rebuild.",
                        ErrorCategories.Invariant,
                        remediation: "Preserve the artifact, inspect the structured What's Wrong diagnostics and repair the feature."),
                    new OperationEvidence(
                        "solidworks-provider",
                        BuildDiagnosticEvidence(inspection.Value.Diagnostics),
                        stateHash: inspection.Value.Document.StateHash));
            }

            if (inspection.Value.Bodies.Length == 0 || inspection.Value.Bodies[0].Volume.CubicMillimeters <= 0d)
            {
                return SolidWorksProviderResults.Failure<NativeDimensionResult>(
                    operation,
                    new OperationError(
                        ErrorCodes.InvariantViolation,
                        "Dimension mutation completed but inspection did not prove a positive solid volume.",
                        ErrorCategories.Invariant,
                        remediation: "Preserve the artifact and inspect the dimension, feature and rebuild errors."));
            }

            var snapshot = new DimensionSnapshot
            {
                DimensionId = new DimensionId($"{current.DocumentId.Value}:dimension:{fullName}"),
                Name = shortName,
                FullName = fullName,
                Configuration = inspection.Value.Document.Configuration,
                Value = Length.FromMeters(actualMeters),
                IsReadOnly = dimension.ReadOnly,
            };
            var nextDescriptor = current with
            {
                StateHash = inspection.Value.Document.StateHash,
                IsDirty = model.GetSaveFlag(),
            };
            return SolidWorksProviderResults.Success(
                operation,
                new NativeDimensionResult(snapshot, nextDescriptor, current),
                new EvidenceObservation("dimension.name", snapshot.FullName),
                new EvidenceObservation("dimension.previous-millimeters", Length.FromMeters(previousMeters).ToString()),
                new EvidenceObservation("dimension.value-millimeters", snapshot.Value.ToString()),
                new EvidenceObservation("dimension.set-status", ((int)swSetValueReturnStatus_e.swSetValue_Successful).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new EvidenceObservation("rebuild.returned", bool.TrueString),
                new EvidenceObservation("body.count", inspection.Value.Bodies.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new EvidenceObservation("volume.cubic-millimeters", inspection.Value.Bodies[0].Volume.CubicMillimeters.ToString("G17", System.Globalization.CultureInfo.InvariantCulture)),
                new EvidenceObservation("state.hash", nextDescriptor.StateHash));
        }
        catch (Exception exception)
        {
            return SolidWorksProviderResults.Failure<NativeDimensionResult>(
                operation,
                new OperationError(
                    ErrorCodes.ProviderFailure,
                    "SOLIDWORKS failed during the named dimension mutation workflow.",
                    ErrorCategories.Provider,
                    retryable: true,
                    details: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["stage"] = stage,
                        ["exception-type"] = exception.GetType().FullName ?? exception.GetType().Name,
                        ["hresult"] = $"0x{exception.HResult:X8}",
                    },
                    remediation: "Preserve the isolated artifact and inspect the reported dimension stage."));
        }
        finally
        {
            SolidWorksDocumentRouting.Release(parameter);
            SolidWorksDocumentRouting.Release(model);
        }
    }

    /// <summary>Reads the current dimension value in system units and normalizes COM's object return.</summary>
    /// <remarks>使用官方的 GetSystemValue3 读回系统单位；对 COM object return 做单一转换，避免散落转换逻辑。</remarks>
    private static double ReadCurrentDimensionMeters(IDimension dimension)
    {
        object? rawValue = dimension.GetSystemValue3(
            (int)swInConfigurationOpts_e.swThisConfiguration,
            null);
        return Convert.ToDouble(rawValue, System.Globalization.CultureInfo.InvariantCulture);
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
                new NativeSaveResult(receipt, updated, current),
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

    /// <summary>
    /// Closes exactly the registered clean document and verifies that SOLIDWORKS no longer exposes it as open.
    /// 关闭 registry 中精确登记且已保存的 document，并验证 SOLIDWORKS 不再将它报告为 open。
    /// </summary>
    private OperationResult<MutationReceipt> CloseOnSta(ISldWorks application)
    {
        const string operation = "part.close";
        if (!registry.TryGet(descriptor.DocumentId, out SolidWorksDocumentDescriptor? current) || current is null)
        {
            return SolidWorksProviderResults.Failure<MutationReceipt>(
                operation,
                new OperationError(
                    ErrorCodes.NotFound,
                    "The native document identity is no longer registered.",
                    ErrorCategories.State));
        }

        OperationResult<ModelDoc2> resolved = SolidWorksDocumentRouting.ResolveOpenDocument(
            application,
            current,
            activate: true,
            verifyStateHash: true);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return OperationResults.Failure<MutationReceipt>(resolved.OperationId, resolved.Error!, resolved.Evidence);
        }

        ModelDoc2 model = resolved.Value;
        try
        {
            if (model.GetSaveFlag())
            {
                return SolidWorksProviderResults.Failure<MutationReceipt>(
                    operation,
                    new OperationError(
                        ErrorCodes.StateConflict,
                        "The native document has unsaved changes and cannot be closed by this provider.",
                        ErrorCategories.State,
                        remediation: "Save the document, then retry the explicit document close."));
            }

            // CloseDoc is intentionally invoked with the canonical registered path, never with a title or ActiveDoc.
            // CloseDoc 明确使用 registry 的 canonical path，绝不使用 title 或 ActiveDoc。
            application.CloseDoc(current.Path);

            ModelDoc2? stillOpen = application.GetOpenDocument(current.Path);
            try
            {
                if (stillOpen is not null)
                {
                    return SolidWorksProviderResults.Failure<MutationReceipt>(
                        operation,
                        new OperationError(
                            ErrorCodes.StateConflict,
                            "SOLIDWORKS reported the exact document as still open after CloseDoc.",
                            ErrorCategories.State,
                            retryable: true,
                            remediation: "Inspect the SOLIDWORKS document state and resolve any blocking dialog before retrying."),
                        new EvidenceObservation("document.closed", bool.FalseString));
                }
            }
            finally
            {
                SolidWorksDocumentRouting.Release(stillOpen);
            }

            return SolidWorksProviderResults.Success(
                operation,
                new MutationReceipt { Operation = operation, StateHash = "closed" },
                new EvidenceObservation("document.id", current.DocumentId.Value),
                new EvidenceObservation("document.path", current.Path),
                new EvidenceObservation("document.closed", bool.TrueString));
        }
        catch (Exception exception)
        {
            return SolidWorksProviderResults.ProviderFailure<MutationReceipt>(
                operation,
                exception,
                "SOLIDWORKS failed while closing the registered document.");
        }
        finally
        {
            SolidWorksDocumentRouting.Release(model);
        }
    }

    /// <summary>
    /// Proves persisted lifecycle integrity using OpenDoc6, then performs a fresh native part inspection.
    /// 使用 OpenDoc6 证明持久化生命周期完整性，再执行一次新的原生零件 inspection。
    /// </summary>
    private OperationResult<NativeReopenResult> ReopenAndInspectOnSta(ISldWorks application)
    {
        const string operation = "part.reopen-inspect";
        if (!registry.TryGet(descriptor.DocumentId, out SolidWorksDocumentDescriptor? current) || current is null)
        {
            return SolidWorksProviderResults.Failure<NativeReopenResult>(
                operation,
                new OperationError(
                    ErrorCodes.NotFound,
                    "The native document identity is no longer registered.",
                    ErrorCategories.State));
        }

        OperationResult<ModelDoc2> resolved = SolidWorksDocumentRouting.ResolveOpenDocument(
            application,
            current,
            activate: true,
            verifyStateHash: true);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return OperationResults.Failure<NativeReopenResult>(resolved.OperationId, resolved.Error!, resolved.Evidence);
        }

        ModelDoc2? currentModel = resolved.Value;
        ModelDoc2? reopened = null;
        string stage = "preflight";
        try
        {
            if (currentModel.GetSaveFlag())
            {
                return SolidWorksProviderResults.Failure<NativeReopenResult>(
                    operation,
                    new OperationError(
                        ErrorCodes.StateConflict,
                        "The native document has unsaved changes and cannot be reopened as persisted state.",
                        ErrorCategories.State,
                        remediation: "Save the document before requesting a persisted reopen."));
            }

            stage = "close";
            application.CloseDoc(current.Path);
            SolidWorksDocumentRouting.Release(currentModel);
            currentModel = null;

            stage = "verify-close";
            ModelDoc2? stillOpen = application.GetOpenDocument(current.Path);
            try
            {
                if (stillOpen is not null)
                {
                    return SolidWorksProviderResults.Failure<NativeReopenResult>(
                        operation,
                        new OperationError(
                            ErrorCodes.StateConflict,
                            "SOLIDWORKS did not close the registered document before the reopen step.",
                            ErrorCategories.State,
                            retryable: true,
                            remediation: "Resolve the document or modal-dialog state, then retry the lifecycle proof."),
                        new EvidenceObservation("document.closed", bool.FalseString));
                }
            }
            finally
            {
                SolidWorksDocumentRouting.Release(stillOpen);
            }

            // OpenDoc6 is the documented replacement for obsolete silent-open methods.  The configuration is explicit
            // so reopening cannot silently switch to an unrelated active configuration.
            // OpenDoc6 是官方文档中 obsolete silent-open 方法的替代接口；显式传入 configuration，避免 reopen 静默
            // 切到无关的 active configuration。
            stage = "open-doc6";
            int fileErrors = 0;
            int fileWarnings = 0;
            reopened = application.OpenDoc6(
                current.Path,
                (int)swDocumentTypes_e.swDocPART,
                (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                current.Configuration,
                ref fileErrors,
                ref fileWarnings);
            if (reopened is null || fileErrors != 0)
            {
                return SolidWorksProviderResults.Failure<NativeReopenResult>(
                    operation,
                    new OperationError(
                        ErrorCodes.ProviderFailure,
                        "SOLIDWORKS OpenDoc6 did not prove a successful part reopen.",
                        ErrorCategories.Provider,
                        retryable: true,
                        details: new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["file-errors"] = fileErrors.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            ["file-warnings"] = fileWarnings.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        },
                        remediation: "Inspect the persisted part and SOLIDWORKS file-load diagnostics before retrying."),
                    new EvidenceObservation("document.closed", bool.TrueString),
                    new EvidenceObservation("open-doc6.returned", (reopened is not null).ToString()),
                    new EvidenceObservation("open-doc6.file-errors", fileErrors.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    new EvidenceObservation("open-doc6.file-warnings", fileWarnings.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            stage = "verify-open-identity";
            string actualPath = reopened.GetPathName()?.Trim() ?? string.Empty;
            string canonicalActualPath = actualPath.Length == 0 ? string.Empty : System.IO.Path.GetFullPath(actualPath);
            if (!canonicalActualPath.Equals(current.Path, StringComparison.OrdinalIgnoreCase)
                || !SolidWorksDocumentRouting.MatchesType(reopened, CadDocumentType.Part))
            {
                return SolidWorksProviderResults.Failure<NativeReopenResult>(
                    operation,
                    new OperationError(
                        ErrorCodes.StateConflict,
                        "The document returned by OpenDoc6 does not match the registered part identity.",
                        ErrorCategories.State,
                        details: new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["expected-path"] = current.Path,
                            ["actual-path"] = canonicalActualPath,
                            ["expected-type"] = CadDocumentType.Part.ToString(),
                            ["actual-type"] = reopened.GetType().ToString(System.Globalization.CultureInfo.InvariantCulture),
                        },
                        remediation: "Do not continue with the reopened handle; re-register the intended document."));
            }

            string configuration = SolidWorksDocumentRouting.ReadConfiguration(reopened);
            if (!configuration.Equals(current.Configuration, StringComparison.Ordinal))
            {
                return SolidWorksProviderResults.Failure<NativeReopenResult>(
                    operation,
                    new OperationError(
                        ErrorCodes.StateConflict,
                        "The reopened part configuration does not match the registered configuration.",
                        ErrorCategories.State,
                        details: new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["expected-configuration"] = current.Configuration,
                            ["actual-configuration"] = configuration,
                        },
                        remediation: "Activate the intended configuration explicitly and re-plan the operation."));
            }

            stage = "inspect-reopened-part";
            OperationResult<CadInspectionSnapshot> inspection = SolidWorksNativeInspectionReader.ReadPart(reopened, current);
            if (!inspection.IsSuccess || inspection.Value is null)
            {
                return OperationResults.Failure<NativeReopenResult>(inspection.OperationId, inspection.Error!, inspection.Evidence);
            }

            if (current.ProfileFeatureName is not null
                && !inspection.Value.Features.Any(feature => feature.Name.Equals(current.ProfileFeatureName, StringComparison.Ordinal)))
            {
                return SolidWorksProviderResults.Failure<NativeReopenResult>(
                    operation,
                    new OperationError(
                        ErrorCodes.SelectionStale,
                        "The provider-identified sketch profile was not present after reopening the persisted part.",
                        ErrorCategories.State,
                        remediation: "Preserve the artifact for diagnosis and rebuild the selection identity from inspection."),
                    new EvidenceObservation("profile.name", current.ProfileFeatureName));
            }

            string reopenedStateHash = inspection.Value.Document.StateHash;
            if (!reopenedStateHash.Equals(current.StateHash, StringComparison.Ordinal))
            {
                return SolidWorksProviderResults.Failure<NativeReopenResult>(
                    operation,
                    new OperationError(
                        ErrorCodes.StateConflict,
                        "The persisted part state hash changed across close and reopen.",
                        ErrorCategories.State,
                        details: new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["expected-state-hash"] = current.StateHash,
                            ["actual-state-hash"] = reopenedStateHash,
                        },
                        remediation: "Treat the reopen as an external state change and create a new operation plan."),
                    new EvidenceObservation("document.closed", bool.TrueString),
                    new EvidenceObservation("document.reopened", bool.TrueString),
                    new EvidenceObservation("state.hash", reopenedStateHash));
            }

            var updated = current with
            {
                StateHash = reopenedStateHash,
                IsDirty = inspection.Value.Document.IsDirty,
            };
            return SolidWorksProviderResults.Success(
                operation,
                new NativeReopenResult(inspection.Value, updated, current),
                new EvidenceObservation("document.id", current.DocumentId.Value),
                new EvidenceObservation("document.closed", bool.TrueString),
                new EvidenceObservation("document.reopened", bool.TrueString),
                new EvidenceObservation("open-doc6.file-errors", fileErrors.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new EvidenceObservation("open-doc6.file-warnings", fileWarnings.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new EvidenceObservation("body.count", inspection.Value.Bodies.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new EvidenceObservation("feature.count", inspection.Value.Features.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new EvidenceObservation("state.hash", reopenedStateHash));
        }
        catch (Exception exception)
        {
            return SolidWorksProviderResults.Failure<NativeReopenResult>(
                operation,
                new OperationError(
                    ErrorCodes.ProviderFailure,
                    "SOLIDWORKS failed during the persisted part close/reopen workflow.",
                    ErrorCategories.Provider,
                    retryable: true,
                    details: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["stage"] = stage,
                        ["exception-type"] = exception.GetType().FullName ?? exception.GetType().Name,
                        ["hresult"] = $"0x{exception.HResult:X8}",
                    },
                    remediation: "Preserve the isolated artifact and inspect the reported lifecycle stage."));
        }
        finally
        {
            SolidWorksDocumentRouting.Release(currentModel);
            SolidWorksDocumentRouting.Release(reopened);
        }
    }

    /// <summary>
    /// Converts safe diagnostic records into evidence observations for failed mutations.
    /// 将安全诊断记录转换为失败 mutation 使用的 evidence observations。
    /// </summary>
    private static ImmutableArray<EvidenceObservation> BuildDiagnosticEvidence(
        IEnumerable<CadDiagnostic> diagnostics)
    {
        var observations = ImmutableArray.CreateBuilder<EvidenceObservation>();
        int index = 0;
        foreach (CadDiagnostic diagnostic in diagnostics)
        {
            string prefix = $"diagnostic.{index++}";
            observations.Add(new EvidenceObservation($"{prefix}.code", diagnostic.Code));
            observations.Add(new EvidenceObservation($"{prefix}.severity", diagnostic.Severity.ToString()));
            observations.Add(new EvidenceObservation($"{prefix}.scope", diagnostic.Scope));
            observations.Add(new EvidenceObservation($"{prefix}.message", diagnostic.Message));
            if (diagnostic.EntityIdentity is not null)
            {
                observations.Add(new EvidenceObservation($"{prefix}.entity", diagnostic.EntityIdentity));
            }

            if (diagnostic.NativeCode is int nativeCode)
            {
                observations.Add(new EvidenceObservation(
                    $"{prefix}.native-code",
                    nativeCode.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
        }

        return observations.ToImmutable();
    }

    /// <summary>
    /// Commits a native descriptor only when the registry still contains the exact descriptor observed before the
    /// operation. 将 native descriptor 提交到 registry，但仅当 registry 仍持有 operation 前精确观察到的 descriptor。
    /// </summary>
    private bool TryCommitDescriptor(
        SolidWorksDocumentDescriptor expected,
        SolidWorksDocumentDescriptor candidate,
        out SolidWorksDocumentDescriptor? committed)
    {
        if (registry.TryUpdateDescriptor(expected, candidate, out committed) && committed is not null)
        {
            descriptor = committed;
            return true;
        }

        committed = null;
        return false;
    }

    /// <summary>
    /// Returns a non-retryable state conflict when native work completed but its local routing commit lost a race.
    /// 当 native work 已完成、但本地 routing commit 发生竞争失败时，返回不可盲重试的 state conflict。
    /// </summary>
    private OperationResult<T> DescriptorCommitFailure<T>(
        string operationId,
        SolidWorksDocumentDescriptor expected,
        SolidWorksDocumentDescriptor candidate)
    {
        registry.TryGet(expected.DocumentId, out SolidWorksDocumentDescriptor? latest);
        return OperationResults.Failure<T>(
            operationId,
            new OperationError(
                ErrorCodes.StateConflict,
                "The native mutation completed but its document descriptor could not be committed without overwriting newer state.",
                ErrorCategories.State,
                remediation: "Do not blindly retry the mutation; re-inspect the document and create a new operation plan.",
                details: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["native-mutation-completed"] = bool.TrueString,
                    ["expected-state-hash"] = expected.StateHash,
                    ["candidate-state-hash"] = candidate.StateHash,
                    ["registered-state-hash"] = latest?.StateHash ?? "missing",
                }),
            new OperationEvidence(
                "solidworks-document-registry",
                [
                    new EvidenceObservation("descriptor.commit", "CAS_FAILED"),
                    new EvidenceObservation("expected-state-hash", expected.StateHash),
                    new EvidenceObservation("candidate-state-hash", candidate.StateHash),
                    new EvidenceObservation("registered-state-hash", latest?.StateHash ?? "missing"),
                ],
                stateHash: latest?.StateHash ?? candidate.StateHash));
    }
}

/// <summary>Internal result carrying the feature and the newly inspected document metadata.</summary>
internal sealed record NativeExtrusionResult(
    FeatureSnapshot Feature,
    SolidWorksDocumentDescriptor Descriptor,
    SolidWorksDocumentDescriptor ExpectedDescriptor);

/// <summary>Internal result carrying a rebuild receipt and updated document metadata.</summary>
internal sealed record NativeRebuildResult(
    RebuildReceipt Receipt,
    SolidWorksDocumentDescriptor Descriptor,
    SolidWorksDocumentDescriptor ExpectedDescriptor);

/// <summary>Internal result carrying a save receipt and updated document metadata.</summary>
internal sealed record NativeSaveResult(
    SaveReceipt Receipt,
    SolidWorksDocumentDescriptor Descriptor,
    SolidWorksDocumentDescriptor ExpectedDescriptor);

/// <summary>Internal result carrying fresh inspection evidence after a native persisted reopen.</summary>
internal sealed record NativeReopenResult(
    CadInspectionSnapshot Snapshot,
    SolidWorksDocumentDescriptor Descriptor,
    SolidWorksDocumentDescriptor ExpectedDescriptor);

/// <summary>Internal result carrying a verified native dimension update and new document metadata.</summary>
internal sealed record NativeDimensionResult(
    DimensionSnapshot Dimension,
    SolidWorksDocumentDescriptor Descriptor,
    SolidWorksDocumentDescriptor ExpectedDescriptor);
