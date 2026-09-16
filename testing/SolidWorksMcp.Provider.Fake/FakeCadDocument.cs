using System.Security.Cryptography;
using System.Text;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Provider.Fake;

/// <summary>Shared document lifecycle and deterministic state-hash implementation for FakeCad documents.</summary>
internal abstract class FakeCadDocument(FakeCadSession session, DocumentId documentId, string path, string configuration, CadDocumentType documentType) : ICadDocument
{
    private int stateVersion;

    /// <inheritdoc />
    public DocumentId DocumentId { get; } = documentId;

    /// <inheritdoc />
    public CadDocumentType DocumentType { get; } = documentType;

    /// <inheritdoc />
    public string Path { get; } = path;

    /// <inheritdoc />
    public string Configuration { get; } = configuration;

    /// <inheritdoc />
    public string StateHash => ComputeStateHash();

    /// <inheritdoc />
    public bool IsDirty { get; private set; }

    protected FakeCadSession Session { get; } = session;

    /// <summary>Marks a verified mutation and advances the deterministic document revision.</summary>
    protected void MarkMutated()
    {
        stateVersion++;
        IsDirty = true;
    }

    /// <summary>Returns the stable common document summary used in inspection evidence.</summary>
    internal CadDocumentSummary CreateSummary() => new()
    {
        DocumentId = DocumentId,
        DocumentType = DocumentType,
        Path = Path,
        Configuration = Configuration,
        StateHash = StateHash,
        IsDirty = IsDirty,
    };

    /// <summary>Builds the complete provider-neutral inspection snapshot.</summary>
    internal abstract CadInspectionSnapshot BuildInspection();

    /// <summary>Provides deterministic state material; it never uses object hash codes or process-local addresses.</summary>
    protected abstract string DescribeState();

    /// <inheritdoc />
    public Task<OperationResult<RebuildReceipt>> RebuildAsync(CancellationToken cancellationToken = default)
    {
        const string operation = "rebuild";
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(FakeCadResults.Cancelled<RebuildReceipt>(operation));
        }

        if (Session.IsClosed)
        {
            return Task.FromResult(FakeCadResults.Failure<RebuildReceipt>(operation, ClosedError()));
        }

        if (Session.Failures.TryTake(FakeCadFailurePoints.Rebuild, out OperationError? injectedError))
        {
            return Task.FromResult(FakeCadResults.Failure<RebuildReceipt>(operation, injectedError!));
        }

        MarkMutated();
        var receipt = new RebuildReceipt { StateHash = StateHash, HasErrors = false };
        return Task.FromResult(
            FakeCadResults.Success(
                receipt,
                operation,
                new EvidenceObservation("rebuild.errors", "0"),
                new EvidenceObservation("state.hash", StateHash)));
    }

    /// <inheritdoc />
    public Task<OperationResult<SaveReceipt>> SaveAsync(CancellationToken cancellationToken = default)
    {
        const string operation = "save";
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(FakeCadResults.Cancelled<SaveReceipt>(operation));
        }

        if (Session.IsClosed)
        {
            return Task.FromResult(FakeCadResults.Failure<SaveReceipt>(operation, ClosedError()));
        }

        if (Session.Failures.TryTake(FakeCadFailurePoints.Save, out OperationError? injectedError))
        {
            return Task.FromResult(FakeCadResults.Failure<SaveReceipt>(operation, injectedError!));
        }

        IsDirty = false;
        var receipt = new SaveReceipt { Path = Path, StateHash = StateHash };
        return Task.FromResult(
            FakeCadResults.Success(
                receipt,
                operation,
                new EvidenceObservation("document.path", Path),
                new EvidenceObservation("state.hash", StateHash)));
    }

    private string ComputeStateHash()
    {
        string material = $"{DocumentId.Value}|{DocumentType}|{Configuration}|{stateVersion}|{IsDirty}|{DescribeState()}";
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return $"sha256:{Convert.ToHexString(digest).ToLowerInvariant()}";
    }

    private static OperationError ClosedError() => new(
        ErrorCodes.StateConflict,
        "The fake CAD session is closed.",
        ErrorCategories.State,
        remediation: "Start a new provider session.");
}
