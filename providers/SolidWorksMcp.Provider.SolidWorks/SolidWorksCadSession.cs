using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Core;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Provider.SolidWorks;

/// <summary>Vendor-neutral session facade backed by one process-bound native attachment.</summary>
/// <remarks>
/// This facade deliberately contains no COM property.  Future document facades will route every COM operation
/// through <see cref="SolidWorksComSessionHost"/> and will verify session/document identity immediately before a
/// mutation.  这里不暴露任何 COM 属性；未来文档 facade 必须通过 Host 排队，并在每次 mutation 前校验 identity。
/// </remarks>
internal sealed class SolidWorksCadSession : ICadSession
{
    private readonly SolidWorksComSessionHost host;
    private readonly CadPathAllowlist pathAllowlist;
    private readonly string attachmentGeneration;
    private readonly SolidWorksDocumentRegistry registry = new();
    private int lifecycleState;

    public SolidWorksCadSession(
        SolidWorksComSessionHost host,
        SolidWorksSessionInfo info,
        CadCapabilitySet capabilities,
        CadPathAllowlist pathAllowlist)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        ArgumentNullException.ThrowIfNull(info);
        Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        this.pathAllowlist = pathAllowlist ?? throw new ArgumentNullException(nameof(pathAllowlist));
        SessionId = info.SessionId;
        attachmentGeneration = string.IsNullOrWhiteSpace(info.AttachmentGeneration)
            ? throw new ArgumentException("The native session attachment generation is required.", nameof(info))
            : info.AttachmentGeneration;
        Inspection = new SolidWorksInspectionService(
            host,
            registry,
            SessionId,
            attachmentGeneration,
            () => Volatile.Read(ref lifecycleState) == 2);
        Export = new UnsupportedSolidWorksExportService(
            Capabilities,
            () => Volatile.Read(ref lifecycleState) == 2,
            SessionId);
        Selection = new SolidWorksNativeSelectionService(
            host,
            registry,
            SessionId,
            attachmentGeneration,
            Capabilities,
            () => Volatile.Read(ref lifecycleState) == 2);
    }

    public SessionId SessionId { get; }

    public CadCapabilitySet Capabilities { get; }

    public ICadInspectionService Inspection { get; }

    public ICadExportService Export { get; }

    public ICadSelectionService Selection { get; }

    /// <summary>Gets whether this logical session has completed its detach lifecycle.</summary>
    internal bool IsClosed => Volatile.Read(ref lifecycleState) == 2;

    public async Task<OperationResult<ICadPartDocument>> CreatePartAsync(
        CreatePartRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return SolidWorksProviderResults.Failure<ICadPartDocument>(
                "part.create",
                new OperationError(ErrorCodes.InvalidRequest, "The part request is required.", ErrorCategories.Validation));
        }

        if (IsClosed)
        {
            return SolidWorksProviderResults.Closed<ICadPartDocument>("part.create", SessionId);
        }

        OperationResult<SolidWorksCreatedPart> created = await host.InvokeOnStaAsync(
            SessionId,
            attachmentGeneration,
            application => SolidWorksPartFactory.CreateOnSta(application, SessionId, request, pathAllowlist),
            cancellationToken).ConfigureAwait(false);
        if (!created.IsSuccess || created.Value is null)
        {
            return OperationResults.Failure<ICadPartDocument>(created.OperationId, created.Error!, created.Evidence);
        }

        registry.Add(created.Value.Descriptor);
        var document = new SolidWorksNativePartDocument(
            host,
            registry,
            SessionId,
            attachmentGeneration,
            created.Value.Descriptor);
        return OperationResults.Success<ICadPartDocument>(
            document,
            created.OperationId,
            created.Evidence ?? new OperationEvidence("solidworks-provider"));
    }

    public Task<OperationResult<ICadAssemblyDocument>> CreateAssemblyAsync(
        CreateAssemblyRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            request is null
                ? SolidWorksProviderResults.Failure<ICadAssemblyDocument>(
                    "create-assembly",
                    new OperationError(ErrorCodes.InvalidRequest, "The assembly request is required.", ErrorCategories.Validation))
                : UnsupportedOrClosed<ICadAssemblyDocument>(CadCapabilityNames.AssemblyMutation, "create-assembly"));
    }

    public async Task<OperationResult<ICadDrawingDocument>> CreateDrawingAsync(
        CreateDrawingRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return SolidWorksProviderResults.Failure<ICadDrawingDocument>(
                "drawing.create",
                new OperationError(ErrorCodes.InvalidRequest, "The drawing request is required.", ErrorCategories.Validation));
        }

        if (IsClosed)
        {
            return SolidWorksProviderResults.Closed<ICadDrawingDocument>("drawing.create", SessionId);
        }

        if (request.SourceDocumentId is not DocumentId sourceDocumentId)
        {
            return SolidWorksProviderResults.Failure<ICadDrawingDocument>(
                "drawing.create",
                new OperationError(
                    ErrorCodes.InvalidRequest,
                    "A persisted source document identity is required to create a native drawing.",
                    ErrorCategories.Validation));
        }

        if (!registry.TryGet(sourceDocumentId, out SolidWorksDocumentDescriptor? sourceDescriptor) || sourceDescriptor is null)
        {
            return SolidWorksProviderResults.Failure<ICadDrawingDocument>(
                "drawing.create",
                new OperationError(
                    ErrorCodes.NotFound,
                    "The requested source document identity is not registered in this provider session.",
                    ErrorCategories.State,
                    remediation: "Create or register the exact source model in this provider session before creating its drawing."));
        }

        OperationResult<SolidWorksCreatedDrawing> created = await host.InvokeOnStaAsync(
            SessionId,
            attachmentGeneration,
            application => SolidWorksDrawingFactory.CreateOnSta(
                application,
                SessionId,
                request,
                sourceDescriptor,
                pathAllowlist),
            cancellationToken).ConfigureAwait(false);
        if (!created.IsSuccess || created.Value is null)
        {
            return OperationResults.Failure<ICadDrawingDocument>(created.OperationId, created.Error!, created.Evidence);
        }

        registry.Add(created.Value.Descriptor);
        var document = new SolidWorksNativeDrawingDocument(
            host,
            registry,
            SessionId,
            attachmentGeneration,
            created.Value.Descriptor,
            created.Value.SourceDocumentId,
            created.Value.SourceDocumentPath);
        return OperationResults.Success<ICadDrawingDocument>(
            document,
            created.OperationId,
            created.Evidence ?? new OperationEvidence("solidworks-provider"));
    }

    public async Task<OperationResult<MutationReceipt>> CloseAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return SolidWorksProviderResults.Cancelled<MutationReceipt>("session.close");
        }

        if (Interlocked.CompareExchange(ref lifecycleState, 1, 0) != 0)
        {
            return SolidWorksProviderResults.Success(
                "session.close",
                new MutationReceipt { Operation = "session.close", StateHash = "closed" },
                new EvidenceObservation("session.state", "closed"),
                new EvidenceObservation("application.exit-requested", bool.FalseString));
        }

        OperationResult<MutationReceipt> detached = await host.DetachAsync(cancellationToken).ConfigureAwait(false);
        if (detached.IsSuccess)
        {
            Volatile.Write(ref lifecycleState, 2);
        }
        else
        {
            // A failed detach is not reported as closed; callers may retry and the host still owns the RCW.
            // detach 失败不能伪报 closed；Host 仍持有 RCW，调用方可以安全重试。
            Volatile.Write(ref lifecycleState, 0);
        }

        return detached;
    }

    public async ValueTask DisposeAsync()
    {
        // CloseAsync carries the explicit evidence.  Dispose is best-effort for framework shutdown, matching the
        // provider contract without silently calling ISldWorks.ExitApp().  Dispose 仅作为宿主关闭时的 best-effort。
        await CloseAsync().ConfigureAwait(false);
    }

    private OperationResult<T> UnsupportedOrClosed<T>(string capabilityName, string operation)
    {
        if (IsClosed)
        {
            return SolidWorksProviderResults.Closed<T>(operation, SessionId);
        }

        CadCapability capability = Capabilities.Find(capabilityName)
            ?? new CadCapability(capabilityName, supported: false, "The native capability was not declared.");
        return SolidWorksProviderResults.Unsupported<T>(operation, capability);
    }
}
/// <summary>Inspection facade that keeps B02 capability declarations honest until native document inspection exists.</summary>
internal sealed class UnsupportedSolidWorksInspectionService(
    CadCapabilitySet capabilities,
    Func<bool> isClosed,
    SessionId sessionId) : ICadInspectionService
{
    public Task<OperationResult<CadInspectionSnapshot>> InspectAsync(
        DocumentId documentId,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(SolidWorksProviderResults.Cancelled<CadInspectionSnapshot>("inspect"));
        }

        if (isClosed())
        {
            return Task.FromResult(SolidWorksProviderResults.Closed<CadInspectionSnapshot>("inspect", sessionId));
        }

        CadCapability capability = capabilities.Find(CadCapabilityNames.Inspection)
            ?? new CadCapability(CadCapabilityNames.Inspection, supported: false, "The native capability was not declared.");
        return Task.FromResult(SolidWorksProviderResults.Unsupported<CadInspectionSnapshot>("inspect", capability));
    }
}
/// <summary>Export facade that returns an explicit capability error instead of touching the native session.</summary>
internal sealed class UnsupportedSolidWorksExportService(
    CadCapabilitySet capabilities,
    Func<bool> isClosed,
    SessionId sessionId) : ICadExportService
{
    public Task<OperationResult<ExportReceipt>> ExportAsync(
        DocumentId documentId,
        CadExportRequest request,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(SolidWorksProviderResults.Cancelled<ExportReceipt>("export"));
        }

        if (isClosed())
        {
            return Task.FromResult(SolidWorksProviderResults.Closed<ExportReceipt>("export", sessionId));
        }

        CadCapability capability = capabilities.Find(CadCapabilityNames.Export)
            ?? new CadCapability(CadCapabilityNames.Export, supported: false, "The native capability was not declared.");
        return Task.FromResult(SolidWorksProviderResults.Unsupported<ExportReceipt>("export", capability));
    }
}
