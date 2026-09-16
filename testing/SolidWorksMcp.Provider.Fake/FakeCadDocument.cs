using System.Security.Cryptography;
using System.Text;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Provider.Fake;

/// <summary>Shared document lifecycle and deterministic state-hash implementation for FakeCad documents.</summary>
internal abstract class FakeCadDocument(FakeCadSession session, DocumentId documentId, string path, string configuration, CadDocumentType documentType) : ICadDocument
{
    private int stateVersion;
    private bool documentClosed;

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

    /// <summary>Gets whether the fake document handle is currently closed.</summary>
    /// <remarks>用于让 FakeCad inspection 对 document close 生命周期保持可观察，而不是把 close 当成无操作。</remarks>
    internal bool IsDocumentClosed => documentClosed;

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
    public Task<OperationResult<MutationReceipt>> CloseAsync(CancellationToken cancellationToken = default)
    {
        const string operation = "close-document";
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(FakeCadResults.Cancelled<MutationReceipt>(operation));
        }

        if (Session.IsClosed)
        {
            return Task.FromResult(FakeCadResults.Closed<MutationReceipt>(operation));
        }

        // FakeCad has no external file lock, so this models the lifecycle state without touching the host filesystem.
        // FakeCad 没有外部文件锁，因此只模拟生命周期状态，不接触宿主文件系统。
        documentClosed = true;
        return Task.FromResult(
            FakeCadResults.Success(
                new MutationReceipt { Operation = "document.close", StateHash = "closed" },
                operation,
                new EvidenceObservation("document.id", DocumentId.Value),
                new EvidenceObservation("document.closed", bool.TrueString)));
    }

    /// <inheritdoc />
    public Task<OperationResult<CadInspectionSnapshot>> ReopenAndInspectAsync(
        CancellationToken cancellationToken = default)
    {
        const string operation = "reopen-inspect";
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(FakeCadResults.Cancelled<CadInspectionSnapshot>(operation));
        }

        if (Session.IsClosed)
        {
            return Task.FromResult(FakeCadResults.Closed<CadInspectionSnapshot>(operation));
        }

        if (IsDirty)
        {
            return Task.FromResult(
                FakeCadResults.Failure<CadInspectionSnapshot>(
                    operation,
                    new OperationError(
                        ErrorCodes.StateConflict,
                        "The fake document has unsaved changes and cannot be reopened as persisted state.",
                        ErrorCategories.State,
                        remediation: "Save the document before requesting a persisted reopen.")));
        }

        // The fake keeps the semantic model in memory; toggling the lifecycle flag still exercises the public contract.
        // Fake 保留内存中的 semantic model；切换生命周期标志仍可验证公共契约，但不冒充磁盘 I/O 证明。
        documentClosed = true;
        documentClosed = false;
        CadInspectionSnapshot snapshot = BuildInspection();
        return Task.FromResult(
            FakeCadResults.Success(
                snapshot,
                operation,
                new EvidenceObservation("document.id", DocumentId.Value),
                new EvidenceObservation("document.reopen", "logical-in-memory"),
                new EvidenceObservation("state.hash", snapshot.Document.StateHash)));
    }

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
