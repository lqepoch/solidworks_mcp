using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Provider.SolidWorks;

/// <summary>
/// Native SOLIDWORKS provider foundation: attach to one existing process and expose explicit future capabilities.
/// </summary>
/// <remarks>
/// B02 intentionally does not claim part, assembly, drawing, inspection or export support yet.  Returning an
/// attached session with those capabilities disabled is safer than exposing a half-implemented mutation path.
/// #18/B02 目前只完成安全附着，不宣称零件、装配、工程图、检查或导出能力；显式 disabled 比半成品 mutation 更安全。
/// </remarks>
public sealed class SolidWorksCadProvider : ICadProvider, IAsyncDisposable
{
    private static readonly CadCapabilitySet capabilities = new(
    [
        new CadCapability(CadCapabilityNames.PartMutation, supported: false, "Native part mutation is planned after the B02 session foundation."),
        new CadCapability(CadCapabilityNames.AssemblyMutation, supported: false, "Native assembly mutation is planned after the B02 session foundation."),
        new CadCapability(CadCapabilityNames.DrawingMutation, supported: false, "Native drawing mutation is planned after the B02 session foundation."),
        new CadCapability(CadCapabilityNames.Inspection, supported: false, "Native inspection is enabled by a later provider issue after COM identity guards are complete."),
        new CadCapability(CadCapabilityNames.Export, supported: false, "Native export is enabled by a later provider issue after COM identity guards are complete."),
        new CadCapability(CadCapabilityNames.Selection, supported: false, "Native selection is enabled after the B03 document registry can bind selectors to documents."),
        new CadCapability(CadCapabilityNames.PatternSemantics, supported: false, "Pattern semantics are owned by the engineering layer and are not implemented in B02."),
    ]);

    private readonly SolidWorksComSessionHost host;
    private readonly Lock gate = new();
    private SolidWorksCadSession? session;
    private int disposed;

    /// <summary>Creates a native provider that discovers existing SOLIDWORKS sessions through ROT.</summary>
    public SolidWorksCadProvider()
        : this(new SolidWorksComSessionHost())
    {
    }

    /// <summary>Creates a provider with an internal host; the overload keeps lifecycle tests deterministic.</summary>
    internal SolidWorksCadProvider(SolidWorksComSessionHost host)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
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
                session = new SolidWorksCadSession(host, attached.Value!, capabilities);
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
