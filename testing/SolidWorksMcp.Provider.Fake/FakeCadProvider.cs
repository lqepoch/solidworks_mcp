using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Provider.Fake;

/// <summary>Deterministic vendor-free provider used by contract and FakeCad tests.</summary>
/// <remarks>
/// This class deliberately models a provider boundary, not a shortcut around it. The native SOLIDWORKS provider
/// will implement the same <see cref="ICadProvider"/> contract and can therefore be tested by the same suite.
/// 该类刻意模拟真正的 Provider 边界，而不是绕过边界的测试捷径；原生 Provider 必须实现同一契约。
/// </remarks>
/// <remarks>Creates a provider with deterministic defaults or caller-supplied capabilities/failures.</remarks>
public sealed class FakeCadProvider(FakeCadOptions? providerOptions = null) : ICadProvider, IAsyncDisposable
{
    private readonly FakeCadOptions options = providerOptions ?? new FakeCadOptions();
    private FakeCadSession? session;

    /// <inheritdoc />
    public CadCapabilitySet Capabilities => options.Capabilities;

    /// <inheritdoc />
    public ValueTask<OperationResult<ICadSession>> StartSessionAsync(
        CadSessionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromResult(FakeCadResults.Cancelled<ICadSession>("start-session"));
        }

        if (this.options.Failures.TryTake(FakeCadFailurePoints.StartSession, out OperationError? injectedError))
        {
            return ValueTask.FromResult(FakeCadResults.Failure<ICadSession>("start-session", injectedError!));
        }

        lock (this)
        {
            if (session is null)
            {
                SessionId sessionId = options.RequestedSessionId ?? this.options.SessionId;
                session = new FakeCadSession(this.options, sessionId);
            }

            return ValueTask.FromResult(
                FakeCadResults.Success<ICadSession>(
                    session,
                    "start-session",
                    new EvidenceObservation("session.id", session.SessionId.Value)));
        }
    }

    /// <summary>Releases the provider-owned fake session using the same lifecycle expected of a native provider.</summary>
    public async ValueTask DisposeAsync()
    {
        if (session is not null)
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }
}
