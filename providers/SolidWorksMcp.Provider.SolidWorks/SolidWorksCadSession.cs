using SolidWorksMcp.CadAbstractions;
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
    private int lifecycleState;

    public SolidWorksCadSession(
        SolidWorksComSessionHost host,
        SolidWorksSessionInfo info,
        CadCapabilitySet capabilities)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        ArgumentNullException.ThrowIfNull(info);
        Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        SessionId = info.SessionId;
        Inspection = new UnsupportedSolidWorksInspectionService(
            Capabilities,
            () => Volatile.Read(ref lifecycleState) == 2,
            SessionId);
        Export = new UnsupportedSolidWorksExportService(
            Capabilities,
            () => Volatile.Read(ref lifecycleState) == 2,
            SessionId);
        Selection = new UnsupportedSolidWorksSelectionService(
            Capabilities,
            () => Volatile.Read(ref lifecycleState) == 2,
            SessionId);
    }

    public SessionId SessionId { get; }

    public CadCapabilitySet Capabilities { get; }

    public ICadInspectionService Inspection { get; }

    public ICadExportService Export { get; }

    public ICadSelectionService Selection { get; }

    /// <summary>Gets whether this logical session has completed its detach lifecycle.</summary>
    internal bool IsClosed => Volatile.Read(ref lifecycleState) == 2;

    public Task<OperationResult<ICadPartDocument>> CreatePartAsync(
        CreatePartRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            request is null
                ? SolidWorksProviderResults.Failure<ICadPartDocument>(
                    "create-part",
                    new OperationError(ErrorCodes.InvalidRequest, "The part request is required.", ErrorCategories.Validation))
                : UnsupportedOrClosed<ICadPartDocument>(CadCapabilityNames.PartMutation, "create-part"));
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

    public Task<OperationResult<ICadDrawingDocument>> CreateDrawingAsync(
        CreateDrawingRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            request is null
                ? SolidWorksProviderResults.Failure<ICadDrawingDocument>(
                    "create-drawing",
                    new OperationError(ErrorCodes.InvalidRequest, "The drawing request is required.", ErrorCategories.Validation))
                : UnsupportedOrClosed<ICadDrawingDocument>(CadCapabilityNames.DrawingMutation, "create-drawing"));
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

/// <summary>Native selection placeholder kept explicit until B03 registers verified document handles.</summary>
internal sealed class UnsupportedSolidWorksSelectionService(
    CadCapabilitySet capabilities,
    Func<bool> isClosed,
    SessionId sessionId) : ICadSelectionService
{
    public Task<OperationResult<CadSelectionSnapshot>> ResolveAsync(
        CadEntitySelector selector,
        CancellationToken cancellationToken = default)
    {
        if (selector is null)
        {
            return Task.FromResult(SolidWorksProviderResults.Failure<CadSelectionSnapshot>(
                "selection.resolve",
                new OperationError(ErrorCodes.InvalidRequest, "The declarative entity selector is required.", ErrorCategories.Validation)));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(SolidWorksProviderResults.Cancelled<CadSelectionSnapshot>("selection.resolve"));
        }

        if (isClosed())
        {
            return Task.FromResult(SolidWorksProviderResults.Closed<CadSelectionSnapshot>("selection.resolve", sessionId));
        }

        CadCapability capability = capabilities.Find(CadCapabilityNames.Selection)
            ?? new CadCapability(CadCapabilityNames.Selection, supported: false, "The native capability was not declared.");
        return Task.FromResult(SolidWorksProviderResults.Unsupported<CadSelectionSnapshot>("selection.resolve", capability));
    }
}
