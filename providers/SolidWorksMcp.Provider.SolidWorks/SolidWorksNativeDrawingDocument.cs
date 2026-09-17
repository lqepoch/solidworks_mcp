using System.Collections.Concurrent;
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
    // A requested provider ViewId is not a SOLIDWORKS COM name. Keep the short-lived binding in the facade and
    // reconstruct ordinal bindings from persisted drawing inspection after reopen. Provider ViewId 不是 SOLIDWORKS
    // COM name；facade 保留短期绑定，重开后再从 persisted drawing inspection 重建 ordinal binding。
    private readonly ConcurrentDictionary<string, string> nativeViewNames = new(StringComparer.Ordinal);

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

        nativeViewNames[result.Value.View.ViewId.Value] = result.Value.NativeName;

        return OperationResults.Success(
            result.Value.View,
            result.OperationId,
            result.Evidence ?? new OperationEvidence("solidworks-drawing"));
    }

    /// <summary>
    /// Creates one native section view from a declarative cutting line bound to an exact parent view.
    /// 根据绑定到精确父视图的声明式剖切线创建一个 native section view。
    /// </summary>
    /// <remarks>
    /// SOLIDWORKS requires a drawing sketch segment to be selected before CreateSectionViewAt5. The adapter performs
    /// that selection on the owning STA and verifies the returned view outline; no global active view is guessed.
    /// SOLIDWORKS 要求在 CreateSectionViewAt5 前选中 drawing sketch segment；adapter 在所属 STA 完成选择并验证
    /// 返回 view 的 outline，不猜测全局 active view。
    /// </remarks>
    public async Task<OperationResult<DrawingViewSnapshot>> AddSectionViewAsync(
        DrawingSectionViewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Name)
            || string.IsNullOrWhiteSpace(request.Label)
            || string.IsNullOrWhiteSpace(request.ParentViewId.Value)
            || request.CutLineStart == request.CutLineEnd)
        {
            return SolidWorksProviderResults.Failure<DrawingViewSnapshot>(
                "drawing.section-view.create",
                new OperationError(
                    ErrorCodes.InvalidRequest,
                    "A section name, label, parent ViewId and non-zero cutting line are required.",
                    ErrorCategories.Validation));
        }

        if (request.ScaleDenominator is <= 0)
        {
            return SolidWorksProviderResults.Failure<DrawingViewSnapshot>(
                "drawing.section-view.create",
                new OperationError(
                    ErrorCodes.InvalidRequest,
                    "The section-view scale denominator must be greater than zero.",
                    ErrorCategories.Validation));
        }

        OperationResult<NativeDrawingViewResult> result = await host.InvokeOnStaAsync(
            sessionId,
            attachmentGeneration,
            application => AddSectionViewOnSta(application, request),
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

        nativeViewNames[result.Value.View.ViewId.Value] = result.Value.NativeName;
        return OperationResults.Success(
            result.Value.View,
            result.OperationId,
            result.Evidence ?? new OperationEvidence("solidworks-drawing"));
    }

    /// <summary>
    /// Creates a native SOLIDWORKS note in the requested drawing view.
    /// 创建真实 SOLIDWORKS 原生 note，并绑定到请求的 drawing view。
    /// </summary>
    /// <remarks>
    /// This annotation slice supports explicit notes and a separate native model-dimension insertion mode. It never
    /// treats note text as a model dimension; model dimensions must be returned by SOLIDWORKS itself. 当前 slice 支持
    /// 显式 note 和独立的 native model-dimension insertion；绝不把 note 文本冒充成模型尺寸，尺寸必须由 SOLIDWORKS
    /// 原生返回。
    /// </remarks>
    public async Task<OperationResult<DrawingAnnotationSnapshot>> AddAnnotationAsync(
        DrawingAnnotationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string kind = request.Kind.Trim();
        bool isNote = kind.Equals("note", StringComparison.OrdinalIgnoreCase)
            || kind.Equals("pattern-callout", StringComparison.OrdinalIgnoreCase);
        bool isModelDimensions = kind.Equals("model-dimensions", StringComparison.OrdinalIgnoreCase);
        bool isModelItems = request.ModelItemKinds is not DrawingModelAnnotationImportKinds.None;
        bool missingFeatureIdentity = isModelItems && string.IsNullOrWhiteSpace(request.FeatureIdentity);
        bool missingApproval = isModelItems
            && request.ApprovalState is not (DrawingAnnotationApprovalState.Approved or DrawingAnnotationApprovalState.Released);
        if ((!isNote && !isModelDimensions && !isModelItems)
            || (isNote && string.IsNullOrWhiteSpace(request.Text))
            || string.IsNullOrWhiteSpace(request.ViewId.Value)
            || missingFeatureIdentity
            || missingApproval)
        {
            string message = missingApproval
                ? "Critical native model-item annotations require Approved or Released semantic intent."
                : missingFeatureIdentity
                    ? "Native model-item annotations require a stable FeatureIdentity for audit and association scope."
                : "The native drawing annotation slice supports Kind='note', Kind='pattern-callout', Kind='model-dimensions' or an allowlisted ModelItemKinds value with a ViewId.";
            string errorCode = missingApproval ? ErrorCodes.ReviewRequired : ErrorCodes.InvalidRequest;
            string errorCategory = missingApproval ? ErrorCategories.Policy : ErrorCategories.Validation;
            return SolidWorksProviderResults.Failure<DrawingAnnotationSnapshot>(
                "drawing.annotation.create",
                new OperationError(
                    errorCode,
                    message,
                    errorCategory,
                    remediation: "Use approved semantic provenance for critical model items; do not synthesize a hole/GD&T/tolerance string."));
        }

        OperationResult<NativeDrawingAnnotationResult> result = await host.InvokeOnStaAsync(
            sessionId,
            attachmentGeneration,
            application => isNote
                ? AddNoteOnSta(application, request)
                : InsertModelItemsOnSta(application, request),
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Value is null)
        {
            return OperationResults.Failure<DrawingAnnotationSnapshot>(result.OperationId, result.Error!, result.Evidence);
        }

        if (!TryCommitDescriptor(result.Value.ExpectedDescriptor, result.Value.Descriptor, out _))
        {
            return DescriptorCommitFailure<DrawingAnnotationSnapshot>(
                result.OperationId,
                result.Value.ExpectedDescriptor,
                result.Value.Descriptor);
        }

        return OperationResults.Success(
            result.Value.Annotation,
            result.OperationId,
            result.Evidence ?? new OperationEvidence("solidworks-drawing"));
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

    private OperationResult<NativeDrawingViewResult> AddSectionViewOnSta(
        ISldWorks application,
        DrawingSectionViewRequest request)
    {
        const string operation = "drawing.section-view.create";
        if (!registry.TryGet(descriptor.DocumentId, out SolidWorksDocumentDescriptor? current) || current is null)
        {
            return SolidWorksProviderResults.Failure<NativeDrawingViewResult>(
                operation,
                new OperationError(ErrorCodes.NotFound, "The native drawing identity is no longer registered.", ErrorCategories.State));
        }

        if (!nativeViewNames.TryGetValue(request.ParentViewId.Value, out string? parentNativeName)
            || string.IsNullOrWhiteSpace(parentNativeName))
        {
            return SolidWorksProviderResults.Failure<NativeDrawingViewResult>(
                operation,
                new OperationError(
                    ErrorCodes.SelectionStale,
                    "The section parent ViewId is not bound to a current native drawing view.",
                    ErrorCategories.State,
                    remediation: "Re-inspect the drawing and use the current declarative parent ViewId."));
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

        SketchSegment? cutLine = null;
        View? sectionView = null;
        try
        {
            var drawing = (IDrawingDoc)resolvedDrawing.Value;
            if (!drawing.ActivateView(parentNativeName))
            {
                return SolidWorksProviderResults.Failure<NativeDrawingViewResult>(
                    operation,
                    new OperationError(
                        ErrorCodes.SelectionStale,
                        "SOLIDWORKS did not activate the exact section parent view.",
                        ErrorCategories.State,
                        remediation: "Preserve the drawing and inspect the parent view binding before retrying."));
            }

            resolvedDrawing.Value.ClearSelection2(true);
            cutLine = resolvedDrawing.Value.SketchManager.CreateLine(
                request.CutLineStart.X.ToMeters(),
                request.CutLineStart.Y.ToMeters(),
                0d,
                request.CutLineEnd.X.ToMeters(),
                request.CutLineEnd.Y.ToMeters(),
                0d);
            if (cutLine is null || !cutLine.Select4(false, null))
            {
                return SolidWorksProviderResults.Failure<NativeDrawingViewResult>(
                    operation,
                    new OperationError(
                        ErrorCodes.ProviderFailure,
                        "SOLIDWORKS did not create and select the exact section cutting line.",
                        ErrorCategories.Provider,
                        remediation: "Preserve the drawing and inspect the parent-view sketch selection."));
            }

            int options = request.ScaleWithModel
                ? (int)swCreateSectionViewAtOptions_e.swCreateSectionView_ScaleWithModel
                : 0;
            if (request.ChangeDirection)
            {
                options |= (int)swCreateSectionViewAtOptions_e.swCreateSectionView_ChangeDirection;
            }

            sectionView = drawing.CreateSectionViewAt5(
                request.Position.X.ToMeters(),
                request.Position.Y.ToMeters(),
                0d,
                request.Label.Trim(),
                options,
                null,
                0d);
            if (sectionView is null)
            {
                return SolidWorksProviderResults.Failure<NativeDrawingViewResult>(
                    operation,
                    new OperationError(
                        ErrorCodes.ProviderFailure,
                        "SOLIDWORKS returned no native section view from CreateSectionViewAt5.",
                        ErrorCategories.Provider,
                        remediation: "Preserve the drawing and inspect the selected cutting line and section options."));
            }

            // CreateSectionViewAt5 creates a dependent view and may retain the parent's alignment constraint. The
            // official IView::Position contract says an aligned view can move only along its alignment vector, so
            // remove that dependency before applying the compiler's explicit sheet-space transform.
            // CreateSectionViewAt5 会创建 dependent view，并可能保留 parent alignment constraint。官方 IView::Position
            // 说明 aligned view 只能沿 alignment vector 移动，因此先 RemoveAlignment，再应用 compiler 的明确纸面 transform。
            int inheritedAlignment = sectionView.GetAlignment();
            sectionView.RemoveAlignment();
            sectionView.PositionLocked = false;
            double requestedScale = sectionView.ScaleDecimal;
            if (request.ScaleDenominator is int denominator)
            {
                requestedScale = 1d / denominator;
            }

            bool positioned = sectionView.SetXform(
                new[]
                {
                    request.Position.X.ToMeters(),
                    request.Position.Y.ToMeters(),
                    requestedScale,
                });
            if (!positioned)
            {
                return SolidWorksProviderResults.Failure<NativeDrawingViewResult>(
                    operation,
                    new OperationError(
                        ErrorCodes.ProviderFailure,
                        "SOLIDWORKS rejected the explicit section-view placement transform.",
                        ErrorCategories.Provider,
                        details: new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["requested-position-meters"] = $"{request.Position.X.ToMeters():G17},{request.Position.Y.ToMeters():G17}",
                            ["inherited-alignment"] = inheritedAlignment.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        },
                        remediation: "Preserve the drawing and inspect native section-view alignment before retrying."));
            }

            drawing.ForceRebuild();
            string nativeName = sectionView.GetName2()?.Trim() ?? request.Name.Trim();
            double[] position = ReadNumbers(sectionView.Position);
            double[] outline = ReadNumbers(sectionView.GetOutline());
            if (outline.Length < 4 || outline[2] <= outline[0] || outline[3] <= outline[1])
            {
                return SolidWorksProviderResults.Failure<NativeDrawingViewResult>(
                    operation,
                    new OperationError(
                        ErrorCodes.InvariantViolation,
                        "SOLIDWORKS created a section view without a positive paper-space outline.",
                        ErrorCategories.Invariant,
                        remediation: "Preserve the drawing and inspect the section view and template layout."));
            }

            var snapshot = new DrawingViewSnapshot
            {
                ViewId = request.RequestedViewId ?? new ViewId($"{current.DocumentId.Value}:section-view:{Guid.NewGuid():N}"),
                Name = request.Name.Trim(),
                Orientation = $"Section {request.Label.Trim()}-{request.Label.Trim()}",
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
                new NativeDrawingViewResult(snapshot, updated, current, nativeName),
                new EvidenceObservation("view.id", snapshot.ViewId.Value),
                new EvidenceObservation("view.kind", "section"),
                new EvidenceObservation("view.parent-id", request.ParentViewId.Value),
                new EvidenceObservation("view.label", request.Label.Trim()),
                new EvidenceObservation("view.native-name", nativeName),
                new EvidenceObservation("view.inherited-alignment", inheritedAlignment.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new EvidenceObservation("view.alignment-removed", bool.TrueString),
                new EvidenceObservation("view.position.requested-meters", $"{request.Position.X.ToMeters():G17},{request.Position.Y.ToMeters():G17}"),
                new EvidenceObservation("view.position.actual-meters", string.Join(',', position.Select(value => value.ToString("G17", System.Globalization.CultureInfo.InvariantCulture)))),
                new EvidenceObservation("view.outline.meters", string.Join(',', outline.Select(value => value.ToString("G17", System.Globalization.CultureInfo.InvariantCulture)))),
                new EvidenceObservation("state.hash", stateHash));
        }
        catch (Exception exception)
        {
            return SolidWorksProviderResults.ProviderFailure<NativeDrawingViewResult>(
                operation,
                exception,
                "SOLIDWORKS native section-view creation failed before a complete view proof was returned.");
        }
        finally
        {
            SolidWorksDocumentRouting.Release(sectionView);
            SolidWorksDocumentRouting.Release(cutLine);
            SolidWorksDocumentRouting.Release(resolvedDrawing.Value);
        }
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
                new NativeDrawingViewResult(snapshot, updated, current, nativeName),
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

    private OperationResult<NativeDrawingAnnotationResult> AddNoteOnSta(
        ISldWorks application,
        DrawingAnnotationRequest request)
    {
        const string operation = "drawing.annotation.create";
        if (!registry.TryGet(descriptor.DocumentId, out SolidWorksDocumentDescriptor? current) || current is null)
        {
            return SolidWorksProviderResults.Failure<NativeDrawingAnnotationResult>(
                operation,
                new OperationError(ErrorCodes.NotFound, "The native drawing identity is no longer registered.", ErrorCategories.State));
        }

        OperationResult<ModelDoc2> resolvedDrawing = SolidWorksDocumentRouting.ResolveOpenDocument(
            application,
            current,
            activate: true,
            verifyStateHash: true);
        if (!resolvedDrawing.IsSuccess || resolvedDrawing.Value is null)
        {
            return OperationResults.Failure<NativeDrawingAnnotationResult>(
                resolvedDrawing.OperationId,
                resolvedDrawing.Error!,
                resolvedDrawing.Evidence);
        }

        View? view = null;
        Note? note = null;
        Annotation? annotation = null;
        try
        {
            var drawing = (IDrawingDoc)resolvedDrawing.Value;
            view = FindNativeView(
                drawing,
                current.DocumentId.Value,
                request.ViewId.Value,
                nativeViewNames.TryGetValue(request.ViewId.Value, out string? boundName) ? boundName : null,
                out string? nativeViewName);
            if (view is null || string.IsNullOrWhiteSpace(nativeViewName))
            {
                return SolidWorksProviderResults.Failure<NativeDrawingAnnotationResult>(
                    operation,
                    new OperationError(
                        ErrorCodes.SelectionStale,
                        "The requested drawing view identity could not be resolved to a current native view.",
                        ErrorCategories.State,
                        remediation: "Re-inspect the drawing and use the current declarative ViewId."));
            }

            if (!drawing.ActivateView(nativeViewName))
            {
                return SolidWorksProviderResults.Failure<NativeDrawingAnnotationResult>(
                    operation,
                    new OperationError(
                        ErrorCodes.ProviderFailure,
                        "SOLIDWORKS did not activate the exact drawing view before note creation.",
                        ErrorCategories.Provider,
                        remediation: "Preserve the drawing and inspect the native view identity before retrying."));
            }

            resolvedDrawing.Value.ClearSelection2(true);
            // CreateText2 is the verified native note primitive. The paper-space coordinates are already canonical
            // millimetres at the abstraction boundary and are converted to SOLIDWORKS metres exactly once here.
            // CreateText2 是已核对的原生 note primitive；抽象层坐标为毫米，只在这里一次性转换为 SOLIDWORKS 米。
            note = drawing.CreateText2(
                request.Text.Trim(),
                request.Position.X.ToMeters(),
                request.Position.Y.ToMeters(),
                0d,
                0.005d,
                0d) as Note;
            if (note is null)
            {
                return SolidWorksProviderResults.Failure<NativeDrawingAnnotationResult>(
                    operation,
                    new OperationError(
                        ErrorCodes.ProviderFailure,
                        "SOLIDWORKS returned no native Note from CreateText2.",
                        ErrorCategories.Provider));
            }

            annotation = note.GetAnnotation() as Annotation;
            if (annotation is null || annotation.GetType() != (int)swAnnotationType_e.swNote)
            {
                return SolidWorksProviderResults.Failure<NativeDrawingAnnotationResult>(
                    operation,
                    new OperationError(
                        ErrorCodes.InvariantViolation,
                        "The created object was not verified as a native note annotation.",
                        ErrorCategories.Invariant));
            }

            string annotationIdentity = request.RequestedAnnotationId?.Value
                ?? $"{current.DocumentId.Value}:annotation:{Guid.NewGuid():N}";
            if (!annotation.SetName(annotationIdentity))
            {
                return SolidWorksProviderResults.Failure<NativeDrawingAnnotationResult>(
                    operation,
                    new OperationError(
                        ErrorCodes.ProviderFailure,
                        "SOLIDWORKS did not persist the requested stable annotation identity.",
                        ErrorCategories.Provider,
                        remediation: "Use a new annotation identity and preserve the artifact for diagnosis."));
            }

            string actualText = note.GetText()?.Trim() ?? string.Empty;
            if (!actualText.Equals(request.Text.Trim(), StringComparison.Ordinal))
            {
                return SolidWorksProviderResults.Failure<NativeDrawingAnnotationResult>(
                    operation,
                    new OperationError(
                        ErrorCodes.InvariantViolation,
                        "SOLIDWORKS did not read back the exact native note text.",
                        ErrorCategories.Invariant));
            }

            drawing.ForceRebuild();
            double[] position = ReadNumbers(annotation.GetPosition());
            var snapshot = new DrawingAnnotationSnapshot
            {
                AnnotationId = new AnnotationId(annotationIdentity),
                ViewId = request.ViewId,
                Kind = request.Kind.Trim(),
                Text = actualText,
                CoverageKeys = request.CoverageKeys,
                Position = new Coordinate2D(
                    Length.FromMeters(position.Length > 0 ? position[0] : request.Position.X.ToMeters()),
                    Length.FromMeters(position.Length > 1 ? position[1] : request.Position.Y.ToMeters())),
            };
            string stateHash = SolidWorksDocumentRouting.ComputeStateHash(resolvedDrawing.Value);
            var updated = current with
            {
                StateHash = stateHash,
                IsDirty = resolvedDrawing.Value.GetSaveFlag(),
            };
            return SolidWorksProviderResults.Success(
                operation,
                new NativeDrawingAnnotationResult(snapshot, updated, current),
                new EvidenceObservation("annotation.id", annotationIdentity),
                new EvidenceObservation("annotation.kind", request.Kind.Trim()),
                new EvidenceObservation("annotation.native-type", ((int)swAnnotationType_e.swNote).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new EvidenceObservation("annotation.view-id", request.ViewId.Value),
                new EvidenceObservation("annotation.native-view", nativeViewName),
                new EvidenceObservation("state.hash", stateHash));
        }
        catch (Exception exception)
        {
            return SolidWorksProviderResults.ProviderFailure<NativeDrawingAnnotationResult>(
                operation,
                exception,
                "SOLIDWORKS native note creation failed before a complete annotation proof was returned.");
        }
        finally
        {
            SolidWorksDocumentRouting.Release(annotation);
            SolidWorksDocumentRouting.Release(note);
            SolidWorksDocumentRouting.Release(view);
            SolidWorksDocumentRouting.Release(resolvedDrawing.Value);
        }
    }

    /// <summary>
    /// Imports only allowlisted annotations returned by SOLIDWORKS model-item insertion for one exact drawing view.
    /// 只导入 SOLIDWORKS 对指定 drawing view 原生返回且属于 allowlist 的模型标注。
    /// </summary>
    /// <remarks>
    /// The request never supplies display text or a numeric value. If SOLIDWORKS returns no requested native
    /// annotation, the operation fails closed. This prevents a request label from masquerading as associative
    /// engineering evidence. 请求不提供显示文字或数值；若 SOLIDWORKS 没有返回请求的 native annotation，操作
    /// fail-closed，避免把 request label 冒充成关联工程证据。
    /// </remarks>
    private OperationResult<NativeDrawingAnnotationResult> InsertModelItemsOnSta(
        ISldWorks application,
        DrawingAnnotationRequest request)
    {
        const string operation = "drawing.annotation.insert-model-items";
        if (!registry.TryGet(descriptor.DocumentId, out SolidWorksDocumentDescriptor? current) || current is null)
        {
            return SolidWorksProviderResults.Failure<NativeDrawingAnnotationResult>(
                operation,
                new OperationError(ErrorCodes.NotFound, "The native drawing identity is no longer registered.", ErrorCategories.State));
        }

        OperationResult<ModelDoc2> resolvedDrawing = SolidWorksDocumentRouting.ResolveOpenDocument(
            application,
            current,
            activate: true,
            verifyStateHash: true);
        if (!resolvedDrawing.IsSuccess || resolvedDrawing.Value is null)
        {
            return OperationResults.Failure<NativeDrawingAnnotationResult>(
                resolvedDrawing.OperationId,
                resolvedDrawing.Error!,
                resolvedDrawing.Evidence);
        }

        View? view = null;
        try
        {
            var drawing = (IDrawingDoc)resolvedDrawing.Value;
            view = FindNativeView(
                drawing,
                current.DocumentId.Value,
                request.ViewId.Value,
                nativeViewNames.TryGetValue(request.ViewId.Value, out string? boundName) ? boundName : null,
                out string? nativeViewName);
            if (view is null || string.IsNullOrWhiteSpace(nativeViewName))
            {
                return SolidWorksProviderResults.Failure<NativeDrawingAnnotationResult>(
                    operation,
                    new OperationError(
                        ErrorCodes.SelectionStale,
                        "The requested drawing view identity could not be resolved to a current native view.",
                        ErrorCategories.State,
                        remediation: "Re-inspect the drawing and use the current declarative ViewId."));
            }

            resolvedDrawing.Value.ClearSelection2(true);
            if (!resolvedDrawing.Value.Extension.SelectByID2(
                    nativeViewName,
                    "DRAWINGVIEW",
                    0d,
                    0d,
                    0d,
                    false,
                    0,
                    null,
                    0)
                || !drawing.ActivateView(nativeViewName))
            {
                return SolidWorksProviderResults.Failure<NativeDrawingAnnotationResult>(
                    operation,
                    new OperationError(
                        ErrorCodes.ProviderFailure,
                        "SOLIDWORKS did not select and activate the exact drawing view for model-item insertion.",
                        ErrorCategories.Provider,
                        remediation: "Preserve the drawing and inspect the native view identity before retrying."));
            }

            int nativeImportFlags = request.ModelItemKinds is DrawingModelAnnotationImportKinds.None
                ? (int)(swInsertAnnotation_e.swInsertDimensionsMarkedForDrawing
                    | swInsertAnnotation_e.swInsertDimensionsNotMarkedForDrawing)
                : ToNativeModelItemFlags(request.ModelItemKinds);
            object? rawAnnotations = drawing.InsertModelAnnotations3(
                (int)swImportModelItemsSource_e.swImportModelItemsFromEntireModel,
                nativeImportFlags,
                false,
                false,
                false,
                true);
            if (rawAnnotations is not Array nativeAnnotations || nativeAnnotations.Length == 0)
            {
                return SolidWorksProviderResults.Failure<NativeDrawingAnnotationResult>(
                    operation,
                    new OperationError(
                        ErrorCodes.InvariantViolation,
                        "SOLIDWORKS returned no model annotations for the selected drawing view.",
                        ErrorCategories.Invariant,
                        remediation: "Add approved model/PMI annotations first; do not synthesize a hole, tolerance or symbol from request text."));
            }

            Annotation? firstAnnotation = null;
            object? firstSpecificAnnotation = null;
            int acceptedAnnotationCount = 0;
            try
            {
                for (int index = 0; index < nativeAnnotations.Length; index++)
                {
                    if (nativeAnnotations.GetValue(index) is not Annotation annotation)
                    {
                        continue;
                    }

                    object? specific = null;
                    bool retained = false;
                    try
                    {
                        int nativeType = annotation.GetType();
                        if (!IsRequestedModelAnnotation(nativeType, request.ModelItemKinds))
                        {
                            continue;
                        }

                        acceptedAnnotationCount++;
                        specific = annotation.GetSpecificAnnotation();
                        if (firstAnnotation is null)
                        {
                            firstAnnotation = annotation;
                            firstSpecificAnnotation = specific;
                            retained = true;
                        }
                    }
                    finally
                    {
                        if (!retained)
                        {
                            SolidWorksDocumentRouting.Release(specific);
                            SolidWorksDocumentRouting.Release(annotation);
                        }
                    }
                }

                if (firstAnnotation is null || acceptedAnnotationCount == 0)
                {
                    return SolidWorksProviderResults.Failure<NativeDrawingAnnotationResult>(
                        operation,
                        new OperationError(
                            ErrorCodes.InvariantViolation,
                            "SOLIDWORKS returned model annotations, but none matched the requested native annotation categories.",
                            ErrorCategories.Invariant,
                            remediation: "Inspect native annotation types and RulePack support before retrying."));
                }

                string nativeIdentity = firstAnnotation.GetName()?.Trim() ?? string.Empty;
                string annotationIdentity = string.IsNullOrWhiteSpace(nativeIdentity)
                    ? $"{current.DocumentId.Value}:annotation:model-item:{Guid.NewGuid():N}"
                    : nativeIdentity;
                string nativeKind = NativeAnnotationKind(firstAnnotation.GetType());
                string text = firstSpecificAnnotation is DisplayDimension firstDimension
                    ? SolidWorksNativeDimensionText.Read(firstDimension)
                    : nativeKind;
                if (firstSpecificAnnotation is DisplayDimension && string.IsNullOrWhiteSpace(text))
                {
                    return SolidWorksProviderResults.Failure<NativeDrawingAnnotationResult>(
                        operation,
                        new OperationError(
                            ErrorCodes.InvariantViolation,
                            "SOLIDWORKS returned a DisplayDimension without readable native value evidence.",
                            ErrorCategories.Invariant,
                            remediation: "Preserve the drawing and inspect the associated native dimension before release."));
                }
                double[] position = ReadNumbers(firstAnnotation.GetPosition());
                drawing.ForceRebuild();
                string stateHash = SolidWorksDocumentRouting.ComputeStateHash(resolvedDrawing.Value);
                var snapshot = new DrawingAnnotationSnapshot
                {
                    AnnotationId = new AnnotationId(annotationIdentity),
                    ViewId = request.ViewId,
                    Kind = nativeKind,
                    Text = text,
                    CoverageKeys = request.CoverageKeys,
                    Position = new Coordinate2D(
                        Length.FromMeters(position.Length > 0 ? position[0] : 0d),
                        Length.FromMeters(position.Length > 1 ? position[1] : 0d)),
                };
                var updated = current with
                {
                    StateHash = stateHash,
                    IsDirty = resolvedDrawing.Value.GetSaveFlag(),
                };
                return SolidWorksProviderResults.Success(
                    operation,
                    new NativeDrawingAnnotationResult(snapshot, updated, current),
                    new EvidenceObservation("annotation.id", annotationIdentity),
                    new EvidenceObservation("annotation.kind", nativeKind),
                    new EvidenceObservation("annotation.native-type", firstAnnotation.GetType().ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    new EvidenceObservation("annotation.native-count", acceptedAnnotationCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    new EvidenceObservation("annotation.model-item-kinds", request.ModelItemKinds.ToString()),
                    new EvidenceObservation("annotation.feature-identity", request.FeatureIdentity ?? "unspecified"),
                    new EvidenceObservation("annotation.provenance.kind", request.ProvenanceKind ?? "unspecified"),
                    new EvidenceObservation("annotation.provenance.method", request.ProvenanceMethod ?? "unspecified"),
                    new EvidenceObservation("annotation.approval-state", request.ApprovalState.ToString()),
                    new EvidenceObservation("annotation.view-id", request.ViewId.Value),
                    new EvidenceObservation("annotation.native-view", nativeViewName),
                    new EvidenceObservation("state.hash", stateHash));
            }
            finally
            {
                SolidWorksDocumentRouting.Release(firstSpecificAnnotation);
                SolidWorksDocumentRouting.Release(firstAnnotation);
            }
        }
        catch (Exception exception)
        {
            return SolidWorksProviderResults.ProviderFailure<NativeDrawingAnnotationResult>(
                operation,
                exception,
                "SOLIDWORKS model-item insertion failed before a complete native annotation proof was returned.");
        }
        finally
        {
            SolidWorksDocumentRouting.Release(view);
            SolidWorksDocumentRouting.Release(resolvedDrawing.Value);
        }
    }

    /// <summary>
    /// Maps the vendor-neutral allowlist to the verified SOLIDWORKS swInsertAnnotation_e bitmask.
    /// 将 vendor-neutral allowlist 映射为已核对的 SOLIDWORKS swInsertAnnotation_e bitmask。
    /// </summary>
    private static int ToNativeModelItemFlags(DrawingModelAnnotationImportKinds kinds)
    {
        swInsertAnnotation_e flags = 0;
        if (kinds.HasFlag(DrawingModelAnnotationImportKinds.Dimensions))
        {
            flags |= swInsertAnnotation_e.swInsertDimensionsMarkedForDrawing
                | swInsertAnnotation_e.swInsertDimensionsNotMarkedForDrawing;
        }

        if (kinds.HasFlag(DrawingModelAnnotationImportKinds.HoleCallouts))
        {
            flags |= swInsertAnnotation_e.swInsertholeCallout;
        }

        if (kinds.HasFlag(DrawingModelAnnotationImportKinds.HoleWizardLocationDimensions))
        {
            flags |= swInsertAnnotation_e.swInsertHoleWizardLocationDimensions;
        }

        if (kinds.HasFlag(DrawingModelAnnotationImportKinds.HoleWizardProfileDimensions))
        {
            flags |= swInsertAnnotation_e.swInsertHoleWizardProfileDimensions;
        }

        if (kinds.HasFlag(DrawingModelAnnotationImportKinds.InstanceCounts))
        {
            flags |= swInsertAnnotation_e.swInsertInstanceCounts;
        }

        if (kinds.HasFlag(DrawingModelAnnotationImportKinds.Datums))
        {
            flags |= swInsertAnnotation_e.swInsertDatums;
        }

        if (kinds.HasFlag(DrawingModelAnnotationImportKinds.DatumTargets))
        {
            flags |= swInsertAnnotation_e.swInsertDatumTargets;
        }

        if (kinds.HasFlag(DrawingModelAnnotationImportKinds.GdAndTolerances))
        {
            flags |= swInsertAnnotation_e.swInsertGTols;
        }

        if (kinds.HasFlag(DrawingModelAnnotationImportKinds.Notes))
        {
            flags |= swInsertAnnotation_e.swInsertNotes;
        }

        if (kinds.HasFlag(DrawingModelAnnotationImportKinds.SurfaceFinish))
        {
            flags |= swInsertAnnotation_e.swInsertSFSymbols;
        }

        if (kinds.HasFlag(DrawingModelAnnotationImportKinds.WeldSymbols))
        {
            flags |= swInsertAnnotation_e.swInsertWelds;
        }

        if (kinds.HasFlag(DrawingModelAnnotationImportKinds.CosmeticThreads))
        {
            flags |= swInsertAnnotation_e.swInsertCThreads;
        }

        if (kinds.HasFlag(DrawingModelAnnotationImportKinds.TolerancedDimensions))
        {
            flags |= swInsertAnnotation_e.swInsertTolerancedDims;
        }

        return (int)flags;
    }

    /// <summary>Accepts only native annotation types that correspond to the requested import categories.</summary>
    private static bool IsRequestedModelAnnotation(int nativeType, DrawingModelAnnotationImportKinds requestedKinds)
    {
        if (requestedKinds is DrawingModelAnnotationImportKinds.None)
        {
            return nativeType == (int)swAnnotationType_e.swDisplayDimension;
        }

        return nativeType switch
        {
            (int)swAnnotationType_e.swDisplayDimension => requestedKinds.HasFlag(DrawingModelAnnotationImportKinds.Dimensions)
                || requestedKinds.HasFlag(DrawingModelAnnotationImportKinds.HoleCallouts)
                || requestedKinds.HasFlag(DrawingModelAnnotationImportKinds.HoleWizardLocationDimensions)
                || requestedKinds.HasFlag(DrawingModelAnnotationImportKinds.HoleWizardProfileDimensions)
                || requestedKinds.HasFlag(DrawingModelAnnotationImportKinds.InstanceCounts)
                || requestedKinds.HasFlag(DrawingModelAnnotationImportKinds.TolerancedDimensions),
            (int)swAnnotationType_e.swDatumTag => requestedKinds.HasFlag(DrawingModelAnnotationImportKinds.Datums),
            (int)swAnnotationType_e.swDatumTargetSym => requestedKinds.HasFlag(DrawingModelAnnotationImportKinds.DatumTargets),
            (int)swAnnotationType_e.swGTol => requestedKinds.HasFlag(DrawingModelAnnotationImportKinds.GdAndTolerances),
            (int)swAnnotationType_e.swNote => requestedKinds.HasFlag(DrawingModelAnnotationImportKinds.Notes),
            (int)swAnnotationType_e.swSFSymbol => requestedKinds.HasFlag(DrawingModelAnnotationImportKinds.SurfaceFinish),
            (int)swAnnotationType_e.swWeldSymbol => requestedKinds.HasFlag(DrawingModelAnnotationImportKinds.WeldSymbols),
            (int)swAnnotationType_e.swCThread => requestedKinds.HasFlag(DrawingModelAnnotationImportKinds.CosmeticThreads),
            _ => false,
        };
    }

    /// <summary>Returns a stable vendor-neutral kind for native annotation evidence.</summary>
    private static string NativeAnnotationKind(int nativeType) => nativeType switch
    {
        (int)swAnnotationType_e.swDisplayDimension => "model-dimension",
        (int)swAnnotationType_e.swDatumTag => "datum",
        (int)swAnnotationType_e.swDatumTargetSym => "datum-target",
        (int)swAnnotationType_e.swGTol => "gdt",
        (int)swAnnotationType_e.swNote => "note",
        (int)swAnnotationType_e.swSFSymbol => "surface-finish",
        (int)swAnnotationType_e.swWeldSymbol => "weld",
        (int)swAnnotationType_e.swCThread => "cosmetic-thread",
        _ => "native-model-annotation",
    };

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

    /// <summary>
    /// Resolves a vendor-neutral ViewId to one current native drawing view and releases every non-selected RCW.
    /// 将 vendor-neutral ViewId 解析为当前 native drawing view，并释放所有未选中的 RCW。
    /// </summary>
    private static View? FindNativeView(
        IDrawingDoc drawing,
        string documentIdentity,
        string requestedViewId,
        string? boundNativeName,
        out string? nativeName)
    {
        nativeName = null;
        object? raw = drawing.GetViews();
        if (raw is not Array sheets)
        {
            return null;
        }

        int ordinal = 0;
        for (int sheetIndex = 0; sheetIndex < sheets.Length; sheetIndex++)
        {
            if (sheets.GetValue(sheetIndex) is not Array sheetViews)
            {
                continue;
            }

            for (int viewIndex = 0; viewIndex < sheetViews.Length; viewIndex++)
            {
                if (sheetViews.GetValue(viewIndex) is not View candidate)
                {
                    continue;
                }

                ordinal++;
                string candidateName = candidate.GetName2()?.Trim() ?? string.Empty;
                bool isOrdinalMatch = requestedViewId.Equals(
                    $"{documentIdentity}:view:{ordinal}",
                    StringComparison.Ordinal);
                bool isNameMatch = !string.IsNullOrWhiteSpace(boundNativeName)
                    && candidateName.Equals(boundNativeName, StringComparison.Ordinal);
                if (isOrdinalMatch || isNameMatch)
                {
                    nativeName = candidateName;
                    return candidate;
                }

                SolidWorksDocumentRouting.Release(candidate);
            }
        }

        return null;
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
    SolidWorksDocumentDescriptor ExpectedDescriptor,
    string NativeName);

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

/// <summary>Internal result carrying a verified native note annotation and descriptor update.</summary>
internal sealed record NativeDrawingAnnotationResult(
    DrawingAnnotationSnapshot Annotation,
    SolidWorksDocumentDescriptor Descriptor,
    SolidWorksDocumentDescriptor ExpectedDescriptor);
