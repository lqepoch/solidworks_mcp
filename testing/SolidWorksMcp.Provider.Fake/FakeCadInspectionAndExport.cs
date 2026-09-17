using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Provider.Fake;

/// <summary>Fake inspection facade that always returns a complete deterministic snapshot or a structured error.</summary>
internal sealed class FakeCadInspectionService(FakeCadSession session) : ICadInspectionService
{
    private readonly FakeCadSession session = session;

    /// <inheritdoc />
    public Task<OperationResult<CadInspectionSnapshot>> InspectAsync(
        DocumentId documentId,
        CancellationToken cancellationToken = default)
    {
        const string operation = "inspect";
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(FakeCadResults.Cancelled<CadInspectionSnapshot>(operation));
        }

        if (session.IsClosed)
        {
            return Task.FromResult(FakeCadResults.Failure<CadInspectionSnapshot>(operation, ClosedError()));
        }

        if (!session.Supports(CadCapabilityNames.Inspection, out CadCapability capability))
        {
            return Task.FromResult(FakeCadResults.Unsupported<CadInspectionSnapshot>(operation, capability));
        }

        if (session.Failures.TryTake(FakeCadFailurePoints.Inspect, out OperationError? injectedError))
        {
            return Task.FromResult(FakeCadResults.Failure<CadInspectionSnapshot>(operation, injectedError!));
        }

        if (!session.TryGetDocument(documentId, out FakeCadDocument? document) || document is null)
        {
            return Task.FromResult(FakeCadResults.NotFound<CadInspectionSnapshot>(operation, documentId.Value));
        }

        if (document.IsDocumentClosed)
        {
            return Task.FromResult(
                FakeCadResults.Failure<CadInspectionSnapshot>(
                    operation,
                    new OperationError(
                        ErrorCodes.StateConflict,
                        "The fake CAD document is closed.",
                        ErrorCategories.State,
                        remediation: "Reopen the document handle before inspecting it.")));
        }

        CadInspectionSnapshot snapshot = document.BuildInspection();
        var observations = new List<EvidenceObservation>
        {
            new("document.id", documentId.Value),
            new("state.hash", snapshot.Document.StateHash),
            new("body.count", snapshot.Bodies.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("feature.count", snapshot.Features.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("view.count", snapshot.Views.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("annotation.count", snapshot.Annotations.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        };
        if (document is FakeCadPartDocument partDocument)
        {
            // A valid profile is observable on inspect as well as create; this prevents a fake implementation from
            // accepting the request and then silently dropping it.  valid profile 在 inspect/create 都可观察，防止
            // Fake 实现“接收但丢弃”请求。
            observations.AddRange(partDocument.GetInitialSketchProfileEvidence());
        }

        return Task.FromResult(FakeCadResults.Success(snapshot, operation, [.. observations]));
    }

    private static OperationError ClosedError() => new(
        ErrorCodes.StateConflict,
        "The fake CAD session is closed.",
        ErrorCategories.State,
        remediation: "Start a new provider session.");
}

/// <summary>Fake export facade that validates requests and emits deterministic export evidence without writing user files.</summary>
internal sealed class FakeCadExportService(FakeCadSession session) : ICadExportService
{
    private readonly FakeCadSession session = session;

    /// <inheritdoc />
    public Task<OperationResult<ExportReceipt>> ExportAsync(
        DocumentId documentId,
        CadExportRequest request,
        CancellationToken cancellationToken = default)
    {
        const string operation = "export";
        if (request is null)
        {
            return Task.FromResult(FakeCadResults.Invalid<ExportReceipt>(operation, "The export request is required."));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(FakeCadResults.Cancelled<ExportReceipt>(operation));
        }

        if (session.IsClosed)
        {
            return Task.FromResult(FakeCadResults.Failure<ExportReceipt>(operation, ClosedError()));
        }

        if (!session.Supports(CadCapabilityNames.Export, out CadCapability capability))
        {
            return Task.FromResult(FakeCadResults.Unsupported<ExportReceipt>(operation, capability));
        }

        if (session.Failures.TryTake(FakeCadFailurePoints.Export, out OperationError? injectedError))
        {
            return Task.FromResult(FakeCadResults.Failure<ExportReceipt>(operation, injectedError!));
        }

        if (!session.TryGetDocument(documentId, out FakeCadDocument? document) || document is null)
        {
            return Task.FromResult(FakeCadResults.NotFound<ExportReceipt>(operation, documentId.Value));
        }

        if (string.IsNullOrWhiteSpace(request.Format) || string.IsNullOrWhiteSpace(request.TargetPath))
        {
            return Task.FromResult(FakeCadResults.Invalid<ExportReceipt>(operation, "Export format and target path are required."));
        }

        var receipt = new ExportReceipt
        {
            TargetPath = request.TargetPath.Trim(),
            Format = request.Format.Trim().ToUpperInvariant(),
            SourceStateHash = document.StateHash,
        };
        return Task.FromResult(
            FakeCadResults.Success(
                receipt,
                operation,
                new EvidenceObservation("document.id", documentId.Value),
                new EvidenceObservation("export.format", receipt.Format),
                new EvidenceObservation("fake.workspace", session.WorkspaceName),
                new EvidenceObservation("state.hash", document.StateHash)));
    }

    private static OperationError ClosedError() => new(
        ErrorCodes.StateConflict,
        "The fake CAD session is closed.",
        ErrorCategories.State,
        remediation: "Start a new provider session.");
}
