using System.Collections.Immutable;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Core;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Provider.SolidWorks;

/// <summary>
/// Vendor-neutral drawing facade backed by one persisted SOLIDWORKS drawing and one exact source-model identity.
/// 以一个已持久化 SOLIDWORKS drawing 和一个精确 source-model identity 为后端的 vendor-neutral drawing facade。
/// </summary>
/// <remarks>
/// No COM object is retained between calls. Every operation reacquires both the drawing and, when needed, its source
/// model on the provider STA, then verifies path/type/configuration/state before mutation. 不跨调用保存 COM object；每次
/// operation 都在 Provider STA 上重新获取 drawing/source，并在 mutation 前验证 path/type/configuration/state。
/// </remarks>
internal sealed class SolidWorksNativeDrawingDocument(
    SolidWorksComSessionHost host,
    SolidWorksDocumentRegistry registry,
    SessionId sessionId,
    string attachmentGeneration,
    SolidWorksDocumentDescriptor initialDescriptor,
    DocumentId sourceDocumentId,
    string sourceDocumentPath) : ICadDrawingDocument
{
    private readonly SolidWorksComSessionHost host = host ?? throw new ArgumentNullException(nameof(host));
    private readonly SolidWorksDocumentRegistry registry = registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly SessionId sessionId = sessionId;
    private readonly string attachmentGeneration = string.IsNullOrWhiteSpace(attachmentGeneration)
        ? throw new ArgumentException("The native session attachment generation is required.", nameof(attachmentGeneration))
        : attachmentGeneration;
    private readonly DocumentId sourceDocumentId = sourceDocumentId;
    private readonly string sourceDocumentPath = string.IsNullOrWhiteSpace(sourceDocumentPath)
        ? throw new ArgumentException("The source document path is required.", nameof(sourceDocumentPath))
        : sourceDocumentPath;
    private SolidWorksDocumentDescriptor descriptor = initialDescriptor ?? throw new ArgumentNullException(nameof(initialDescriptor));

    public DocumentId DocumentId => descriptor.DocumentId;

    public CadDocumentType DocumentType => descriptor.DocumentType;

    public string Path => descriptor.Path;

    public string Configuration => descriptor.Configuration;

    public string StateHash => descriptor.StateHash;

    public bool IsDirty => descriptor.IsDirty;

    /// <summary>Creates one real drawing view from an exact open source-model path.</summary>
    public async Task<OperationResult<DrawingViewSnapshot>> AddViewAsync(
        DrawingViewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Orientation))
        {
            return SolidWorksProviderResults.Failure<DrawingViewSnapshot>(
                "drawing.view.create",
                new OperationError(
                    ErrorCodes.InvalidRequest,
                    "A drawing view name and model orientation are required.",
                    ErrorCategories.Validation));
        }

        if (request.ScaleDenominator is <= 0)
        {
            return SolidWorksProviderResults.Failure<DrawingViewSnapshot>(
                "drawing.view.create",
                new OperationError(
                    ErrorCodes.InvalidRequest,
                    "The drawing view scale denominator must be greater than zero.",
                    ErrorCategories.Validation));
        }

        OperationResult<NativeDrawingViewResult> result = await host.InvokeOnStaAsync(
            sessionId,
            attachmentGeneration,
            application => AddViewOnSta(application, request),
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Value is null)
        {
            return OperationResults.Failure<DrawingViewSnapshot>(result.OperationId, result.Error!, result.Evidence);
        }

        if (!TryCommitDescriptor(result.Value.ExpectedDescriptor, result.Value.Descriptor, out _))
        {
            return DescriptorCommitFailure<DrawingViewSnapshot>(
                result.OperationId,
                result.Value.ExpectedDescriptor,
                result.Value.Descriptor);
        }

        return OperationResults.Success(
            result.Value.View,
            result.OperationId,
            result.Evidence ?? new OperationEvidence("solidworks-drawing"));
    }

    /// <summary>
    /// Annotation mutation remains explicit until model-item/PMI association is proven; no fake note is emitted.
    /// 在证明 Model Item/PMI 关联前，annotation mutation 显式保持未实现；绝不伪造一个看似成功的 note。
    /// </summary>
    public Task<OperationResult<DrawingAnnotationSnapshot>> AddAnnotationAsync(
        DrawingAnnotationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(
            SolidWorksProviderResults.Unsupported<DrawingAnnotationSnapshot>(
                "drawing.annotation.create",
                new CadCapability(
                    CadCapabilityNames.DrawingMutation,
                    supported: false,
                    "Native view creation is verified; associative model-item/PMI annotation is a later drawing slice.")));
    }

    /// <summary>Rebuilds the actual drawing and returns a native view evidence snapshot.</summary>
    public async Task<OperationResult<RebuildReceipt>> RebuildAsync(CancellationToken cancellationToken = default)
    {
        OperationResult<NativeDrawingRebuildResult> result = await host.InvokeOnStaAsync(
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
            result.Evidence ?? new OperationEvidence("solidworks-drawing"));
    }

    /// <summary>Saves the exact drawing path after a state-hash preflight and read-back.</summary>
    public async Task<OperationResult<SaveReceipt>> SaveAsync(CancellationToken cancellationToken = default)
    {
        OperationResult<NativeDrawingSaveResult> result = await host.InvokeOnStaAsync(
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
            result.Evidence ?? new OperationEvidence("solidworks-drawing"));
    }

    /// <summary>Closes only the exact clean persisted drawing registered by this facade.</summary>
    public async Task<OperationResult<MutationReceipt>> CloseAsync(CancellationToken cancellationToken = default)
    {
        OperationResult<MutationReceipt> result = await host.InvokeOnStaAsync(
            sessionId,
            attachmentGeneration,
            CloseOnSta,
            cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            registry.Remove(DocumentId);
        }

        return result;
    }

    /// <summary>Reopens the saved .slddrw and verifies that the native views survive persistence.</summary>
    public async Task<OperationResult<CadInspectionSnapshot>> ReopenAndInspectAsync(
        CancellationToken cancellationToken = default)
    {
        OperationResult<NativeDrawingReopenResult> result = await host.InvokeOnStaAsync(
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
            result.Evidence ?? new OperationEvidence("solidworks-drawing"));
    }

    private OperationResult<NativeDrawingViewResult> AddViewOnSta(
        ISldWorks application,
        DrawingViewRequest request)
    {
        const string operation = "drawing.view.create";
        if (!registry.TryGet(descriptor.DocumentId, out SolidWorksDocumentDescriptor? current) || current is null)
        {
            return SolidWorksProviderResults.Failure<NativeDrawingViewResult>(
                operation,
                new OperationError(ErrorCodes.NotFound, "The native drawing identity is no longer registered.", ErrorCategories.State));
        }

        if (!registry.TryGet(sourceDocumentId, out SolidWorksDocumentDescriptor? sourceDescriptor) || sourceDescriptor is null)
        {
            return SolidWorksProviderResults.Failure<NativeDrawingViewResult>(
                operation,
                new OperationError(ErrorCodes.NotFound, "The drawing source model identity is no longer registered.", ErrorCategories.State));
        }

        OperationResult<ModelDoc2> resolvedDrawing = SolidWorksDocumentRouting.ResolveOpenDocument(
            application,
            current,
            activate: true,
            verifyStateHash: true);
        if (!resolvedDrawing.IsSuccess || resolvedDrawing.Value is null)
        {
            return OperationResults.Failure<NativeDrawingViewResult>(
                resolvedDrawing.OperationId,
                resolvedDrawing.Error!,
                resolvedDrawing.Evidence);
        }

        OperationResult<ModelDoc2> resolvedSource = SolidWorksDocumentRouting.ResolveOpenDocument(
            application,
            sourceDescriptor,
            activate: false,
            verifyStateHash: true);
        if (!resolvedSource.IsSuccess || resolvedSource.Value is null)
        {
            SolidWorksDocumentRouting.Release(resolvedDrawing.Value);
            return OperationResults.Failure<NativeDrawingViewResult>(
                resolvedSource.OperationId,
                resolvedSource.Error!,
                resolvedSource.Evidence);
        }

        View? view = null;
        try
        {
            var drawing = (IDrawingDoc)resolvedDrawing.Value;
            string modelViewName = ToNativeModelViewName(request.Orientation);
            view = drawing.CreateDrawViewFromModelView3(
                sourceDocumentPath,
                modelViewName,
                request.Position.X.ToMeters(),
                request.Position.Y.ToMeters(),
                0d);
            if (view is null)
            {
                return SolidWorksProviderResults.Failure<NativeDrawingViewResult>(
                    operation,
                    new OperationError(
                        ErrorCodes.ProviderFailure,
                        "SOLIDWORKS returned no drawing view from CreateDrawViewFromModelView3.",
                        ErrorCategories.Provider,
                        remediation: "Verify that the source model is open and the requested standard view exists."));
            }

            if (request.ScaleDenominator is int denominator)
            {
                // IView.ScaleDecimal is a native paper/model ratio. The contract accepts the readable 1:N form.
                // IView.ScaleDecimal 是原生 paper/model 比例；抽象层使用可读的 1:N 表示。
                view.ScaleDecimal = 1d / denominator;
            }

            drawing.ForceRebuild();
            string nativeName = view.GetName2()?.Trim() ?? request.Name.Trim();
            double[] position = ReadNumbers(view.Position);
            double[] outline = ReadNumbers(view.GetOutline());
            if (outline.Length < 4 || outline[2] <= outline[0] || outline[3] <= outline[1])
            {
                return SolidWorksProviderResults.Failure<NativeDrawingViewResult>(
                    operation,
                    new OperationError(
                        ErrorCodes.InvariantViolation,
                        "SOLIDWORKS created a drawing view without a positive paper-space outline.",
                        ErrorCategories.Invariant,
                        remediation: "Preserve the drawing and inspect the source model view and template."));
            }

            var snapshot = new DrawingViewSnapshot
            {
                ViewId = request.RequestedViewId ?? new ViewId($"{current.DocumentId.Value}:view:{Guid.NewGuid():N}"),
                Name = request.Name.Trim(),
                Orientation = request.Orientation.Trim(),
                Position = new Coordinate2D(
                    Length.FromMeters(position.Length > 0 ? position[0] : request.Position.X.ToMeters()),
                    Length.FromMeters(position.Length > 1 ? position[1] : request.Position.Y.ToMeters())),
                ScaleDenominator = request.ScaleDenominator,
            };
            string stateHash = SolidWorksDocumentRouting.ComputeStateHash(resolvedDrawing.Value);
            var updated = current with
            {
                StateHash = stateHash,
                IsDirty = resolvedDrawing.Value.GetSaveFlag(),
            };
            return SolidWorksProviderResults.Success(
                operation,
                new NativeDrawingViewResult(snapshot, updated, current),
                new EvidenceObservation("view.id", snapshot.ViewId.Value),
                new EvidenceObservation("view.name", nativeName),
                new EvidenceObservation("view.orientation", request.Orientation.Trim()),
                new EvidenceObservation("view.outline.meters", string.Join(',', outline.Select(value => value.ToString("G17", System.Globalization.CultureInfo.InvariantCulture)))),
                new EvidenceObservation("state.hash", stateHash));
        }
        catch (Exception exception)
        {
            return SolidWorksProviderResults.ProviderFailure<NativeDrawingViewResult>(
                operation,
                exception,
                "SOLIDWORKS drawing-view creation failed before a verified view snapshot was returned.");
        }
        finally
        {
            SolidWorksDocumentRouting.Release(view);
            SolidWorksDocumentRouting.Release(resolvedSource.Value);
            SolidWorksDocumentRouting.Release(resolvedDrawing.Value);
        }
    }

    private OperationResult<NativeDrawingRebuildResult> RebuildOnSta(ISldWorks application)
    {
        const string operation = "drawing.rebuild";
        if (!registry.TryGet(descriptor.DocumentId, out SolidWorksDocumentDescriptor? current) || current is null)
        {
            return SolidWorksProviderResults.Failure<NativeDrawingRebuildResult>(
                operation,
                new OperationError(ErrorCodes.NotFound, "The native drawing identity is no longer registered.", ErrorCategories.State));
        }

        OperationResult<ModelDoc2> resolved = SolidWorksDocumentRouting.ResolveOpenDocument(
            application,
            current,
            activate: true,
            verifyStateHash: true);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return OperationResults.Failure<NativeDrawingRebuildResult>(resolved.OperationId, resolved.Error!, resolved.Evidence);
        }

        try
        {
            ((IDrawingDoc)resolved.Value).ForceRebuild();
            OperationResult<CadInspectionSnapshot> inspection = SolidWorksNativeInspectionReader.ReadDrawing(resolved.Value, current);
            if (!inspection.IsSuccess || inspection.Value is null)
            {
                return OperationResults.Failure<NativeDrawingRebuildResult>(inspection.OperationId, inspection.Error!, inspection.Evidence);
            }

            string stateHash = inspection.Value.Document.StateHash;
            var updated = current with { StateHash = stateHash, IsDirty = inspection.Value.Document.IsDirty };
            var receipt = new RebuildReceipt
            {
                StateHash = stateHash,
                HasErrors = inspection.Value.HasErrors,
                Diagnostics = inspection.Value.Diagnostics,
            };
            return SolidWorksProviderResults.Success(
                operation,
                new NativeDrawingRebuildResult(receipt, updated, current),
                new EvidenceObservation("view.count", inspection.Value.Views.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new EvidenceObservation("state.hash", stateHash));
        }
        finally
        {
            SolidWorksDocumentRouting.Release(resolved.Value);
        }
    }

    private OperationResult<NativeDrawingSaveResult> SaveOnSta(ISldWorks application)
    {
        const string operation = "drawing.save";
        if (!registry.TryGet(descriptor.DocumentId, out SolidWorksDocumentDescriptor? current) || current is null)
        {
            return SolidWorksProviderResults.Failure<NativeDrawingSaveResult>(
                operation,
                new OperationError(ErrorCodes.NotFound, "The native drawing identity is no longer registered.", ErrorCategories.State));
        }

        OperationResult<ModelDoc2> resolved = SolidWorksDocumentRouting.ResolveOpenDocument(
            application,
            current,
            activate: true,
            verifyStateHash: true);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return OperationResults.Failure<NativeDrawingSaveResult>(resolved.OperationId, resolved.Error!, resolved.Evidence);
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
                return SolidWorksProviderResults.Failure<NativeDrawingSaveResult>(
                    operation,
                    new OperationError(
                        ErrorCodes.ProviderFailure,
                        "SOLIDWORKS drawing Save3 did not prove a successful save.",
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
            return SolidWorksProviderResults.Success(
                operation,
                new NativeDrawingSaveResult(
                    new SaveReceipt { Path = current.Path, StateHash = stateHash },
                    updated,
                    current),
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

    private OperationResult<MutationReceipt> CloseOnSta(ISldWorks application)
    {
        const string operation = "drawing.close";
        if (!registry.TryGet(descriptor.DocumentId, out SolidWorksDocumentDescriptor? current) || current is null)
        {
            return SolidWorksProviderResults.Failure<MutationReceipt>(
                operation,
                new OperationError(ErrorCodes.NotFound, "The native drawing identity is no longer registered.", ErrorCategories.State));
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

        try
        {
            if (resolved.Value.GetSaveFlag())
            {
                return SolidWorksProviderResults.Failure<MutationReceipt>(
                    operation,
                    new OperationError(
                        ErrorCodes.StateConflict,
                        "The native drawing has unsaved changes and cannot be closed by this provider.",
                        ErrorCategories.State,
                        remediation: "Save the drawing before requesting the explicit document close."));
            }

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
                            "SOLIDWORKS reported the exact drawing as still open after CloseDoc.",
                            ErrorCategories.State,
                            retryable: true,
                            remediation: "Resolve any drawing or modal-dialog state before retrying."),
                        new EvidenceObservation("document.closed", bool.FalseString));
                }
            }
            finally
            {
                SolidWorksDocumentRouting.Release(stillOpen);
            }

            return SolidWorksProviderResults.Success(
                operation,
                new MutationReceipt { Operation = operation, StateHash = current.StateHash },
                new EvidenceObservation("document.closed", bool.TrueString),
                new EvidenceObservation("document.path", current.Path));
        }
        finally
        {
            SolidWorksDocumentRouting.Release(resolved.Value);
        }
    }

    private OperationResult<NativeDrawingReopenResult> ReopenAndInspectOnSta(ISldWorks application)
    {
        const string operation = "drawing.reopen-inspect";
        if (!registry.TryGet(descriptor.DocumentId, out SolidWorksDocumentDescriptor? current) || current is null)
        {
            return SolidWorksProviderResults.Failure<NativeDrawingReopenResult>(
                operation,
                new OperationError(ErrorCodes.NotFound, "The native drawing identity is no longer registered.", ErrorCategories.State));
        }

        OperationResult<ModelDoc2> resolved = SolidWorksDocumentRouting.ResolveOpenDocument(
            application,
            current,
            activate: true,
            verifyStateHash: true);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return OperationResults.Failure<NativeDrawingReopenResult>(resolved.OperationId, resolved.Error!, resolved.Evidence);
        }

        ModelDoc2? currentModel = resolved.Value;
        ModelDoc2? reopened = null;
        try
        {
            if (currentModel.GetSaveFlag())
            {
                return SolidWorksProviderResults.Failure<NativeDrawingReopenResult>(
                    operation,
                    new OperationError(
                        ErrorCodes.StateConflict,
                        "The native drawing has unsaved changes and cannot be reopened as persisted state.",
                        ErrorCategories.State,
                        remediation: "Save the drawing before requesting a persisted reopen."));
            }

            application.CloseDoc(current.Path);
            SolidWorksDocumentRouting.Release(currentModel);
            currentModel = null;
            ModelDoc2? stillOpen = application.GetOpenDocument(current.Path);
            try
            {
                if (stillOpen is not null)
                {
                    return SolidWorksProviderResults.Failure<NativeDrawingReopenResult>(
                        operation,
                        new OperationError(
                            ErrorCodes.StateConflict,
                            "SOLIDWORKS did not close the registered drawing before the reopen step.",
                            ErrorCategories.State,
                            retryable: true,
                            remediation: "Resolve the drawing or modal-dialog state before retrying."));
                }
            }
            finally
            {
                SolidWorksDocumentRouting.Release(stillOpen);
            }

            int fileErrors = 0;
            int fileWarnings = 0;
            reopened = application.OpenDoc6(
                current.Path,
                (int)swDocumentTypes_e.swDocDRAWING,
                (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                current.Configuration,
                ref fileErrors,
                ref fileWarnings);
            if (reopened is null || fileErrors != 0)
            {
                return SolidWorksProviderResults.Failure<NativeDrawingReopenResult>(
                    operation,
                    new OperationError(
                        ErrorCodes.ProviderFailure,
                        "SOLIDWORKS OpenDoc6 did not prove a successful drawing reopen.",
                        ErrorCategories.Provider,
                        details: new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["file-errors"] = fileErrors.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            ["file-warnings"] = fileWarnings.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        }));
            }

            string actualPath = System.IO.Path.GetFullPath(reopened.GetPathName()?.Trim() ?? string.Empty);
            if (!actualPath.Equals(current.Path, StringComparison.OrdinalIgnoreCase)
                || !SolidWorksDocumentRouting.MatchesType(reopened, CadDocumentType.Drawing))
            {
                return SolidWorksProviderResults.Failure<NativeDrawingReopenResult>(
                    operation,
                    new OperationError(
                        ErrorCodes.StateConflict,
                        "The document returned by OpenDoc6 does not match the registered drawing identity.",
                        ErrorCategories.State));
            }

            OperationResult<CadInspectionSnapshot> inspection = SolidWorksNativeInspectionReader.ReadDrawing(reopened, current);
            if (!inspection.IsSuccess || inspection.Value is null)
            {
                return OperationResults.Failure<NativeDrawingReopenResult>(inspection.OperationId, inspection.Error!, inspection.Evidence);
            }

            if (inspection.Value.Views.Length == 0)
            {
                return SolidWorksProviderResults.Failure<NativeDrawingReopenResult>(
                    operation,
                    new OperationError(
                        ErrorCodes.InvariantViolation,
                        "The persisted drawing reopened without any verified drawing view.",
                        ErrorCategories.Invariant));
            }

            string stateHash = inspection.Value.Document.StateHash;
            var updated = current with { StateHash = stateHash, IsDirty = inspection.Value.Document.IsDirty };
            return SolidWorksProviderResults.Success(
                operation,
                new NativeDrawingReopenResult(inspection.Value, updated, current),
                new EvidenceObservation("document.closed", bool.TrueString),
                new EvidenceObservation("document.reopened", bool.TrueString),
                new EvidenceObservation("view.count", inspection.Value.Views.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new EvidenceObservation("open-doc6.file-errors", fileErrors.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new EvidenceObservation("open-doc6.file-warnings", fileWarnings.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new EvidenceObservation("state.hash", stateHash));
        }
        catch (Exception exception)
        {
            return SolidWorksProviderResults.ProviderFailure<NativeDrawingReopenResult>(
                operation,
                exception,
                "SOLIDWORKS drawing reopen/inspection failed before a complete persistence proof was returned.");
        }
        finally
        {
            SolidWorksDocumentRouting.Release(reopened);
            SolidWorksDocumentRouting.Release(currentModel);
        }
    }

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
                "The native drawing mutation completed but its document descriptor could not be committed without overwriting newer state.",
                ErrorCategories.State,
                remediation: "Do not blindly retry the mutation; re-inspect the drawing and create a new operation plan.",
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

    private static string ToNativeModelViewName(string orientation)
    {
        return orientation.Trim().ToLowerInvariant() switch
        {
            "front" => "*Front",
            "back" => "*Back",
            "left" => "*Left",
            "right" => "*Right",
            "top" => "*Top",
            "bottom" => "*Bottom",
            "isometric" or "iso" => "*Isometric",
            "trimetric" => "*Trimetric",
            "dimetric" => "*Dimetric",
            _ => orientation.Trim().StartsWith('*') ? orientation.Trim() : $"*{orientation.Trim()}",
        };
    }

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
}

/// <summary>Internal result carrying a verified native drawing view and descriptor update.</summary>
internal sealed record NativeDrawingViewResult(
    DrawingViewSnapshot View,
    SolidWorksDocumentDescriptor Descriptor,
    SolidWorksDocumentDescriptor ExpectedDescriptor);

/// <summary>Internal result carrying a drawing rebuild receipt and descriptor update.</summary>
internal sealed record NativeDrawingRebuildResult(
    RebuildReceipt Receipt,
    SolidWorksDocumentDescriptor Descriptor,
    SolidWorksDocumentDescriptor ExpectedDescriptor);

/// <summary>Internal result carrying a drawing save receipt and descriptor update.</summary>
internal sealed record NativeDrawingSaveResult(
    SaveReceipt Receipt,
    SolidWorksDocumentDescriptor Descriptor,
    SolidWorksDocumentDescriptor ExpectedDescriptor);

/// <summary>Internal result carrying verified drawing evidence after close/open persistence proof.</summary>
internal sealed record NativeDrawingReopenResult(
    CadInspectionSnapshot Snapshot,
    SolidWorksDocumentDescriptor Descriptor,
    SolidWorksDocumentDescriptor ExpectedDescriptor);
