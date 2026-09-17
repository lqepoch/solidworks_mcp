using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Core;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Provider.SolidWorks;

/// <summary>
/// Native SOLIDWORKS provider: attach to one existing process and expose verified part plus single-part drawing slices.
/// </summary>
/// <remarks>
/// The native provider enables persisted part creation and the first real drawing loop. Assembly/export remain
/// explicitly disabled until their own API and Live evidence gates are complete. 原生 Provider 已启用已持久化零件
/// 和首个真实工程图闭环；装配/export 仍要等各自 API 与 Live evidence 门禁完成后开启。
/// </remarks>
public sealed class SolidWorksCadProvider : ICadProvider, IAsyncDisposable
{
    private static readonly CadCapabilitySet capabilities = new(
    [
        new CadCapability(CadCapabilityNames.PartMutation, supported: true),
        new CadCapability(CadCapabilityNames.AssemblyMutation, supported: false, "Native assembly mutation is planned after the B02 session foundation."),
        new CadCapability(CadCapabilityNames.DrawingMutation, supported: true),
        new CadCapability(CadCapabilityNames.Inspection, supported: true),
        new CadCapability(CadCapabilityNames.Export, supported: false, "Native export is enabled by a later provider issue after COM identity guards are complete."),
        new CadCapability(CadCapabilityNames.Selection, supported: false, "Native selection is enabled after the B03 document registry can bind selectors to documents."),
        new CadCapability(CadCapabilityNames.PatternSemantics, supported: false, "Pattern semantics are owned by the engineering layer and are not implemented in B02."),
    ]);

    private readonly SolidWorksComSessionHost host;
    private readonly CadPathAllowlist pathAllowlist;
    private readonly Lock gate = new();
    private SolidWorksCadSession? session;
    private int disposed;

    /// <summary>Creates a native provider that discovers existing SOLIDWORKS sessions through ROT.</summary>
    public SolidWorksCadProvider()
        : this(new SolidWorksComSessionHost())
    {
    }

    /// <summary>Creates a native provider with an explicit persisted-artifact path policy.</summary>
    /// <remarks>
    /// An explicit policy is required for native file creation; the parameterless constructor denies all creates.
    /// 原生文件创建必须显式提供路径策略；无参构造函数默认拒绝所有 create，避免 fail-open。
    /// </remarks>
    public SolidWorksCadProvider(CadPathAllowlist pathAllowlist)
        : this(new SolidWorksComSessionHost(), pathAllowlist)
    {
    }

    /// <summary>Creates a provider with an internal host; the overload keeps lifecycle tests deterministic.</summary>
    internal SolidWorksCadProvider(SolidWorksComSessionHost host)
        : this(host, CadPathAllowlist.DenyAll)
    {
    }

    /// <summary>Internal constructor that injects both STA host and path policy for deterministic tests.</summary>
    internal SolidWorksCadProvider(SolidWorksComSessionHost host, CadPathAllowlist pathAllowlist)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        this.pathAllowlist = pathAllowlist ?? throw new ArgumentNullException(nameof(pathAllowlist));
    }

    /// <inheritdoc />
    public CadCapabilitySet Capabilities => capabilities;

    /// <inheritdoc />
    public async ValueTask<OperationResult<ICadSession>> StartSessionAsync(
        CadSessionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (Volatile.Read(ref disposed) != 0)
        {
            return SolidWorksProviderResults.Failure<ICadSession>(
                "session.attach",
                new OperationError(
                    ErrorCodes.StateConflict,
                    "The native SOLIDWORKS provider has been disposed.",
                    ErrorCategories.State,
                    remediation: "Create a new provider instance for a new MCP host lifetime."));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return SolidWorksProviderResults.Cancelled<ICadSession>("session.attach");
        }

        OperationResult<SolidWorksSessionInfo> attached = await host.AttachAsync(options, cancellationToken).ConfigureAwait(false);
        if (!attached.IsSuccess)
        {
            return OperationResults.Failure<ICadSession>(attached.OperationId, attached.Error!, attached.Evidence);
        }

        lock (gate)
        {
            if (session is null || session.IsClosed)
            {
                session = new SolidWorksCadSession(host, attached.Value!, capabilities, pathAllowlist);
            }

            return SolidWorksProviderResults.Success<ICadSession>(
                "session.attach",
                session,
                new EvidenceObservation("session.id", session.SessionId.Value),
                new EvidenceObservation("session.process-id", attached.Value!.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new EvidenceObservation("session.revision", attached.Value.Revision));
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        SolidWorksCadSession? current;
        lock (gate)
        {
            current = session;
        }

        if (current is not null)
        {
            await current.DisposeAsync().ConfigureAwait(false);
        }

        await host.DisposeAsync().ConfigureAwait(false);
    }
}
