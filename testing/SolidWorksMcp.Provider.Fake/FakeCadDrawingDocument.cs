using System.Collections.Immutable;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Provider.Fake;

/// <summary>Deterministic in-memory drawing document for the first auto-drawing contract workflow.</summary>
internal sealed class FakeCadDrawingDocument(
    FakeCadSession session,
    DocumentId documentId,
    string path,
    string configuration,
    DocumentId? sourceDocumentId) : FakeCadDocument(session, documentId, path, configuration, CadDocumentType.Drawing), ICadDrawingDocument
{
    private readonly DocumentId? sourceDocumentId = sourceDocumentId;
    private readonly List<DrawingViewSnapshot> views = [];
    private readonly List<DrawingAnnotationSnapshot> annotations = [];
    private int viewSequence;
    private int annotationSequence;

    /// <inheritdoc />
    public Task<OperationResult<DrawingViewSnapshot>> AddViewAsync(
        DrawingViewRequest request,
        CancellationToken cancellationToken = default)
    {
        const string operation = "add-view";
        if (request is null)
        {
            return Task.FromResult(FakeCadResults.Invalid<DrawingViewSnapshot>(operation, "The drawing view request is required."));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(FakeCadResults.Cancelled<DrawingViewSnapshot>(operation));
        }

        if (!Session.Supports(CadCapabilityNames.DrawingMutation, out CadCapability capability))
        {
            return Task.FromResult(FakeCadResults.Unsupported<DrawingViewSnapshot>(operation, capability));
        }

        if (Session.IsClosed)
        {
            return Task.FromResult(FakeCadResults.Closed<DrawingViewSnapshot>(operation));
        }

        if (Session.Failures.TryTake(FakeCadFailurePoints.AddView, out OperationError? injectedError))
        {
            return Task.FromResult(FakeCadResults.Failure<DrawingViewSnapshot>(operation, injectedError!));
        }

        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Orientation))
        {
            return Task.FromResult(FakeCadResults.Invalid<DrawingViewSnapshot>(operation, "View name and orientation are required."));
        }

        ViewId viewId = request.RequestedViewId ?? new ViewId($"view-{++viewSequence:000}");
        if (views.Any(view => view.ViewId == viewId))
        {
            return Task.FromResult(FakeCadResults.Invalid<DrawingViewSnapshot>(operation, $"View identity '{viewId.Value}' already exists."));
        }

        var snapshot = new DrawingViewSnapshot
        {
            ViewId = viewId,
            Name = request.Name.Trim(),
            Orientation = request.Orientation.Trim(),
            Position = request.Position,
            ScaleDenominator = request.ScaleDenominator,
        };
        views.Add(snapshot);
        MarkMutated();
        return Task.FromResult(
            FakeCadResults.Success(
                snapshot,
                operation,
                new EvidenceObservation("view.id", viewId.Value),
                new EvidenceObservation("view.orientation", snapshot.Orientation),
                new EvidenceObservation("state.hash", StateHash)));
    }

    /// <inheritdoc />
    public Task<OperationResult<DrawingViewSnapshot>> AddSectionViewAsync(
        DrawingSectionViewRequest request,
        CancellationToken cancellationToken = default)
    {
        const string operation = "add-section-view";
        if (request is null)
        {
            return Task.FromResult(FakeCadResults.Invalid<DrawingViewSnapshot>(operation, "The section-view request is required."));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(FakeCadResults.Cancelled<DrawingViewSnapshot>(operation));
        }

        if (!Session.Supports(CadCapabilityNames.DrawingMutation, out CadCapability capability))
        {
            return Task.FromResult(FakeCadResults.Unsupported<DrawingViewSnapshot>(operation, capability));
        }

        if (Session.IsClosed)
        {
            return Task.FromResult(FakeCadResults.Closed<DrawingViewSnapshot>(operation));
        }

        if (!views.Any(view => view.ViewId == request.ParentViewId))
        {
            return Task.FromResult(FakeCadResults.NotFound<DrawingViewSnapshot>(operation, request.ParentViewId.Value));
        }

        if (string.IsNullOrWhiteSpace(request.Name)
            || string.IsNullOrWhiteSpace(request.Label)
            || request.CutLineStart == request.CutLineEnd)
        {
            return Task.FromResult(FakeCadResults.Invalid<DrawingViewSnapshot>(
                operation,
                "Section name, label and a non-zero cutting line are required."));
        }

        ViewId viewId = request.RequestedViewId ?? new ViewId($"section-view-{++viewSequence:000}");
        if (views.Any(view => view.ViewId == viewId))
        {
            return Task.FromResult(FakeCadResults.Invalid<DrawingViewSnapshot>(operation, $"View identity '{viewId.Value}' already exists."));
        }

        var snapshot = new DrawingViewSnapshot
        {
            ViewId = viewId,
            Name = request.Name.Trim(),
            Orientation = $"Section {request.Label.Trim()}-{request.Label.Trim()}",
            Position = request.Position,
            ScaleDenominator = request.ScaleDenominator,
        };
        views.Add(snapshot);
        MarkMutated();
        return Task.FromResult(
            FakeCadResults.Success(
                snapshot,
                operation,
                new EvidenceObservation("view.id", viewId.Value),
                new EvidenceObservation("view.kind", "section"),
                new EvidenceObservation("view.parent-id", request.ParentViewId.Value),
                new EvidenceObservation("view.label", request.Label.Trim()),
                new EvidenceObservation("state.hash", StateHash)));
    }

    /// <inheritdoc />
    public Task<OperationResult<DrawingAnnotationSnapshot>> AddAnnotationAsync(
        DrawingAnnotationRequest request,
        CancellationToken cancellationToken = default)
    {
        const string operation = "add-annotation";
        if (request is null)
        {
            return Task.FromResult(FakeCadResults.Invalid<DrawingAnnotationSnapshot>(operation, "The annotation request is required."));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(FakeCadResults.Cancelled<DrawingAnnotationSnapshot>(operation));
        }

        if (!Session.Supports(CadCapabilityNames.DrawingMutation, out CadCapability capability))
        {
            return Task.FromResult(FakeCadResults.Unsupported<DrawingAnnotationSnapshot>(operation, capability));
        }

        if (Session.IsClosed)
        {
            return Task.FromResult(FakeCadResults.Closed<DrawingAnnotationSnapshot>(operation));
        }

        if (Session.Failures.TryTake(FakeCadFailurePoints.AddAnnotation, out OperationError? injectedError))
        {
            return Task.FromResult(FakeCadResults.Failure<DrawingAnnotationSnapshot>(operation, injectedError!));
        }

        if (!views.Any(view => view.ViewId == request.ViewId))
        {
            return Task.FromResult(FakeCadResults.NotFound<DrawingAnnotationSnapshot>(operation, request.ViewId.Value));
        }

        bool isModelItems = request.ModelItemKinds is not DrawingModelAnnotationImportKinds.None;
        if (string.IsNullOrWhiteSpace(request.Kind)
            || (!isModelItems && string.IsNullOrWhiteSpace(request.Text))
            || (isModelItems && string.IsNullOrWhiteSpace(request.FeatureIdentity))
            || (isModelItems && request.ApprovalState is not (DrawingAnnotationApprovalState.Approved or DrawingAnnotationApprovalState.Released)))
        {
            return Task.FromResult(FakeCadResults.Invalid<DrawingAnnotationSnapshot>(
                operation,
                isModelItems
                    ? "Native model-item annotations require FeatureIdentity and Approved/Released intent."
                    : "Annotation kind and text are required."));
        }

        AnnotationId annotationId = request.RequestedAnnotationId ?? new AnnotationId($"annotation-{++annotationSequence:000}");
        if (annotations.Any(annotation => annotation.AnnotationId == annotationId))
        {
            return Task.FromResult(FakeCadResults.Invalid<DrawingAnnotationSnapshot>(operation, $"Annotation identity '{annotationId.Value}' already exists."));
        }

        var snapshot = new DrawingAnnotationSnapshot
        {
            AnnotationId = annotationId,
            ViewId = request.ViewId,
            Kind = isModelItems ? "native-model-item" : request.Kind.Trim(),
            Text = request.Text.Trim(),
            CoverageKeys = request.CoverageKeys,
            Position = request.Position,
        };
        annotations.Add(snapshot);
        MarkMutated();
        return Task.FromResult(
            FakeCadResults.Success(
                snapshot,
                operation,
                new EvidenceObservation("annotation.id", annotationId.Value),
                new EvidenceObservation("annotation.view-id", snapshot.ViewId.Value),
                new EvidenceObservation("annotation.feature-identity", request.FeatureIdentity ?? "unspecified"),
                new EvidenceObservation("annotation.model-item-kinds", request.ModelItemKinds.ToString()),
                new EvidenceObservation("annotation.approval-state", request.ApprovalState.ToString()),
                new EvidenceObservation("state.hash", StateHash)));
    }

    /// <inheritdoc />
    public Task<OperationResult<DrawingRepairReceipt>> RepositionAnnotationAsync(
        DrawingAnnotationPositionRepairRequest request,
        CancellationToken cancellationToken = default)
    {
        const string operation = "reposition-annotation";
        if (request is null
            || string.IsNullOrWhiteSpace(request.AnnotationId.Value)
            || string.IsNullOrWhiteSpace(request.ExpectedDocumentStateHash)
            || string.IsNullOrWhiteSpace(request.PreconditionFingerprint))
        {
            return Task.FromResult(FakeCadResults.Invalid<DrawingRepairReceipt>(
                operation,
                "Annotation identity, expected state hash and repair precondition fingerprint are required."));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(FakeCadResults.Cancelled<DrawingRepairReceipt>(operation));
        }

        if (!Session.Supports(CadCapabilityNames.DrawingMutation, out CadCapability capability))
        {
            return Task.FromResult(FakeCadResults.Unsupported<DrawingRepairReceipt>(operation, capability));
        }

        if (Session.IsClosed)
        {
            return Task.FromResult(FakeCadResults.Closed<DrawingRepairReceipt>(operation));
        }

        if (!StateHash.Equals(request.ExpectedDocumentStateHash.Trim(), StringComparison.Ordinal))
        {
            return Task.FromResult(
                FakeCadResults.Failure<DrawingRepairReceipt>(
                    operation,
                    new OperationError(
                        ErrorCodes.StateConflict,
                        "The drawing state changed after the repair plan was created.",
                        ErrorCategories.State,
                        remediation: "Inspect the drawing again and create a new targeted repair plan.")));
        }

        int index = annotations.FindIndex(annotation => annotation.AnnotationId == request.AnnotationId);
        if (index < 0)
        {
            return Task.FromResult(FakeCadResults.NotFound<DrawingRepairReceipt>(operation, request.AnnotationId.Value));
        }

        DrawingAnnotationSnapshot current = annotations[index];
        if (!NearlyEqual(current.Position, request.ExpectedCurrentPosition))
        {
            return Task.FromResult(
                FakeCadResults.Failure<DrawingRepairReceipt>(
                    operation,
                    new OperationError(
                        ErrorCodes.StateConflict,
                        "The annotation position no longer matches the repair precondition.",
                        ErrorCategories.State,
                        remediation: "Inspect the exact annotation and regenerate a targeted repair plan.")));
        }

        DrawingAnnotationSnapshot updated = current with { Position = request.NewPosition };
        annotations[index] = updated;
        MarkMutated();
        DrawingRepairReceipt receipt = new()
        {
            ActionCode = "layout.apply-planned-position",
            AnnotationId = updated.AnnotationId,
            Position = updated.Position,
            StateHash = StateHash,
        };
        return Task.FromResult(
            FakeCadResults.Success(
                receipt,
                operation,
                new EvidenceObservation("repair.action", receipt.ActionCode),
                new EvidenceObservation("repair.annotation-id", receipt.AnnotationId.Value),
                new EvidenceObservation("repair.precondition-fingerprint", request.PreconditionFingerprint.Trim()),
                new EvidenceObservation("state.hash", receipt.StateHash)));
    }

    private static bool NearlyEqual(Coordinate2D first, Coordinate2D second) =>
        Math.Abs(first.X.Millimeters - second.X.Millimeters) <= 0.000001d
        && Math.Abs(first.Y.Millimeters - second.Y.Millimeters) <= 0.000001d;

    /// <inheritdoc />
    internal override CadInspectionSnapshot BuildInspection() => new()
    {
        Document = CreateSummary(),
        Views = [.. views.OrderBy(view => view.ViewId.Value, StringComparer.Ordinal)],
        Annotations = [.. annotations.OrderBy(annotation => annotation.AnnotationId.Value, StringComparer.Ordinal)],
    };

    /// <inheritdoc />
    protected override string DescribeState() => string.Join(
        ";",
        $"source:{sourceDocumentId?.Value ?? "none"}",
        views
            .OrderBy(view => view.ViewId.Value, StringComparer.Ordinal)
            .Select(view => $"view:{view.ViewId.Value}:{view.Orientation}:{view.Position.X.Millimeters:G17}:{view.Position.Y.Millimeters:G17}"),
        annotations
            .OrderBy(annotation => annotation.AnnotationId.Value, StringComparer.Ordinal)
            .Select(annotation => $"annotation:{annotation.AnnotationId.Value}:{annotation.ViewId.Value}:{annotation.Kind}:{annotation.Text}"));
}
