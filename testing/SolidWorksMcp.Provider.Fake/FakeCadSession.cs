using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Provider.Fake;

/// <summary>In-memory single-session state machine used by FakeCad contract tests.</summary>
/// <remarks>
/// The lock protects document registration and session lifecycle; individual document mutations are also serialized
/// through this same session gate. 这模拟原生 Provider 的 single-writer 约束，但不依赖 COM 或 SOLIDWORKS。
/// </remarks>
internal sealed class FakeCadSession : ICadSession
{
    private readonly FakeCadOptions options;
    private readonly Lock gate = new();
    private readonly Dictionary<DocumentId, FakeCadDocument> documents = [];
    private int documentSequence;
    private bool closed;

    public FakeCadSession(FakeCadOptions options, SessionId sessionId)
    {
        this.options = options;
        SessionId = sessionId;
        Inspection = new FakeCadInspectionService(this);
        Export = new FakeCadExportService(this);
    }

    /// <inheritdoc />
    public SessionId SessionId { get; }

    /// <inheritdoc />
    public CadCapabilitySet Capabilities => options.Capabilities;

    /// <inheritdoc />
    public ICadInspectionService Inspection { get; }

    /// <inheritdoc />
    public ICadExportService Export { get; }

    internal FakeCadFailureInjector Failures => options.Failures;

    internal string WorkspaceName => options.WorkspaceName;

    internal bool IsClosed
    {
        get
        {
            lock (gate)
            {
                return closed;
            }
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<ICadPartDocument>> CreatePartAsync(
        CreatePartRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return Task.FromResult(FakeCadResults.Invalid<ICadPartDocument>("create-part", "The part request is required."));
        }

        return CreateDocumentAsync<ICadPartDocument>(
            request.RequestedDocumentId,
            request.Path,
            request.Configuration,
            CadDocumentType.Part,
            static (session, id, path, configuration) => new FakeCadPartDocument(session, id, path, configuration),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult<ICadAssemblyDocument>> CreateAssemblyAsync(
        CreateAssemblyRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return Task.FromResult(FakeCadResults.Invalid<ICadAssemblyDocument>("create-assembly", "The assembly request is required."));
        }

        return CreateDocumentAsync<ICadAssemblyDocument>(
            request.RequestedDocumentId,
            request.Path,
            request.Configuration,
            CadDocumentType.Assembly,
            static (session, id, path, configuration) => new FakeCadAssemblyDocument(session, id, path, configuration),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult<ICadDrawingDocument>> CreateDrawingAsync(
        CreateDrawingRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return Task.FromResult(FakeCadResults.Invalid<ICadDrawingDocument>("create-drawing", "The drawing request is required."));
        }

        if (request.SourceDocumentId is DocumentId sourceDocumentId && !ContainsDocument(sourceDocumentId))
        {
            return Task.FromResult(FakeCadResults.NotFound<ICadDrawingDocument>("create-drawing", sourceDocumentId.Value));
        }

        return CreateDocumentAsync<ICadDrawingDocument>(
            request.RequestedDocumentId,
            request.Path,
            request.Configuration,
            CadDocumentType.Drawing,
            (session, id, path, configuration) => new FakeCadDrawingDocument(session, id, path, configuration, request.SourceDocumentId),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult<MutationReceipt>> CloseAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(FakeCadResults.Cancelled<MutationReceipt>("close-session"));
        }

        lock (gate)
        {
            if (closed)
            {
                return Task.FromResult(
                    FakeCadResults.Success<MutationReceipt>(
                        new MutationReceipt { Operation = "session.close", StateHash = "closed" },
                        "close-session",
                        new EvidenceObservation("session.state", "closed")));
            }

            if (Failures.TryTake(FakeCadFailurePoints.CloseSession, out OperationError? injectedError))
            {
                return Task.FromResult(FakeCadResults.Failure<MutationReceipt>("close-session", injectedError!));
            }

            closed = true;
            return Task.FromResult(
                FakeCadResults.Success<MutationReceipt>(
                    new MutationReceipt { Operation = "session.close", StateHash = "closed" },
                    "close-session",
                    new EvidenceObservation("session.state", "closed")));
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // Dispose is deliberately best-effort: the typed CloseAsync result remains available to callers that need evidence.
        // Dispose 保持 best-effort；需要审计证据的调用方应显式检查 CloseAsync 返回值。
        await CloseAsync().ConfigureAwait(false);
    }

    internal bool ContainsDocument(DocumentId documentId)
    {
        lock (gate)
        {
            return documents.ContainsKey(documentId);
        }
    }

    internal bool TryGetDocument(DocumentId documentId, out FakeCadDocument? document)
    {
        lock (gate)
        {
            return documents.TryGetValue(documentId, out document);
        }
    }

    internal bool Supports(string capabilityName, out CadCapability capability)
    {
        capability = Capabilities.Find(capabilityName)
            ?? new CadCapability(capabilityName, supported: false, "The capability was not declared by this session.");
        return capability.Supported;
    }

    internal TDocument? GetDocument<TDocument>(DocumentId documentId)
        where TDocument : FakeCadDocument
    {
        return TryGetDocument(documentId, out FakeCadDocument? document) ? document as TDocument : null;
    }

    private Task<OperationResult<TContract>> CreateDocumentAsync<TContract>(
        DocumentId? requestedDocumentId,
        string? requestedPath,
        string configuration,
        CadDocumentType documentType,
        Func<FakeCadSession, DocumentId, string, string, FakeCadDocument> factory,
        CancellationToken cancellationToken)
        where TContract : ICadDocument
    {
        string operation = $"create-{documentType.ToString().ToLowerInvariant()}";
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(FakeCadResults.Cancelled<TContract>(operation));
        }

        string capabilityName = documentType switch
        {
            CadDocumentType.Part => CadCapabilityNames.PartMutation,
            CadDocumentType.Assembly => CadCapabilityNames.AssemblyMutation,
            CadDocumentType.Drawing => CadCapabilityNames.DrawingMutation,
            _ => string.Empty,
        };
        if (!Supports(capabilityName, out CadCapability capability))
        {
            return Task.FromResult(FakeCadResults.Unsupported<TContract>(operation, capability));
        }

        if (Failures.TryTake(FakeCadFailurePoints.CreateDocument, out OperationError? injectedError))
        {
            return Task.FromResult(FakeCadResults.Failure<TContract>(operation, injectedError!));
        }

        if (string.IsNullOrWhiteSpace(configuration))
        {
            return Task.FromResult(FakeCadResults.Invalid<TContract>(operation, "Configuration is required."));
        }

        lock (gate)
        {
            if (closed)
            {
                return Task.FromResult(FakeCadResults.Failure<TContract>(operation, ClosedError()));
            }

            DocumentId documentId = requestedDocumentId ?? NextDocumentId(documentType);
            if (documents.ContainsKey(documentId))
            {
                return Task.FromResult(FakeCadResults.Invalid<TContract>(operation, $"Document identity '{documentId.Value}' already exists."));
            }

            string path = string.IsNullOrWhiteSpace(requestedPath)
                ? $"fake://{options.WorkspaceName}/{documentId.Value}"
                : requestedPath.Trim();
            FakeCadDocument fakeDocument = factory(this, documentId, path, configuration.Trim());
            var contract = (TContract)(ICadDocument)fakeDocument;
            documents.Add(documentId, fakeDocument);
            return Task.FromResult(
                FakeCadResults.Success(
                    contract,
                    operation,
                    new EvidenceObservation("document.id", documentId.Value),
                    new EvidenceObservation("document.type", documentType.ToString())));
        }
    }

    private DocumentId NextDocumentId(CadDocumentType documentType)
    {
        documentSequence++;
        return new DocumentId($"doc-{documentType.ToString().ToLowerInvariant()}-{documentSequence:000}");
    }

    private static OperationError ClosedError() => new(
        ErrorCodes.StateConflict,
        "The fake CAD session is closed.",
        ErrorCategories.State,
        remediation: "Start a new provider session.");
}
