using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Server;

/// <summary>Owns one lazily started CAD session for the stdio server lifetime.</summary>
/// <remarks>
/// The accessor serializes session creation and disposes the provider session during host shutdown. It does not
/// replace the later transaction/single-STA dispatcher; it is only the A02 composition boundary.
/// 这里仅负责 A02 的 session 生命周期，不替代后续的 Transaction 与 STA Dispatcher。
/// </remarks>
public sealed class CadSessionAccessor(ICadProvider provider, CadSessionOptions sessionOptions) : IAsyncDisposable
{
    private readonly ICadProvider provider = provider ?? throw new ArgumentNullException(nameof(provider));
    private readonly CadSessionOptions sessionOptions = sessionOptions ?? throw new ArgumentNullException(nameof(sessionOptions));
    private readonly SemaphoreSlim gate = new(1, 1);
    private ICadSession? session;
    private bool disposed;

    /// <summary>Gets the existing session or starts one exactly once.</summary>
    /// <remarks>
    /// The semaphore protects both the first-start race and the disposed-state check. Provider errors are returned as
    /// typed operation results so MCP callers receive stable error codes rather than an unstructured exception.
    /// 信号量同时保护首次启动竞争和 disposed 状态检查；Provider 错误以强类型结果返回，让 MCP 调用方获得稳定错误码，
    /// 而不是不可诊断的非结构化异常。
    /// </remarks>
    public async Task<OperationResult<ICadSession>> GetOrStartAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return OperationResults.Failure<ICadSession>(
                "server:session-start",
                new OperationError(
                    ErrorCodes.Cancelled,
                    "The CAD session start was cancelled.",
                    ErrorCategories.Execution));
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (disposed)
            {
                return OperationResults.Failure<ICadSession>(
                    "server:session-start",
                    new OperationError(
                        ErrorCodes.StateConflict,
                        "The CAD session accessor is disposed.",
                        ErrorCategories.State));
            }

            if (session is not null)
            {
                return OperationResults.Success(
                    session,
                    "server:session-existing",
                    new OperationEvidence("server", [new EvidenceObservation("session.id", session.SessionId.Value)]));
            }

            OperationResult<ICadSession> started = await provider.StartSessionAsync(
                sessionOptions,
                cancellationToken).ConfigureAwait(false);
            if (!started.IsSuccess)
            {
                return started;
            }

            session = started.Value!;
            return started;
        }
        catch (OperationCanceledException)
        {
            return OperationResults.Failure<ICadSession>(
                "server:session-start",
                new OperationError(
                    ErrorCodes.Cancelled,
                    "The CAD session start was cancelled.",
                    ErrorCategories.Execution));
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Closes the provider session and synchronization gate during host shutdown.</summary>
    /// <remarks>
    /// Disposal belongs to the host lifetime; individual MCP tools never dispose the shared session. 生命周期由 Host
    /// 统一管理；单个 MCP tool 绝不能释放共享 Session。
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (session is not null)
            {
                await session.DisposeAsync().ConfigureAwait(false);
                session = null;
            }
        }
        finally
        {
            gate.Release();
            gate.Dispose();
        }
    }
}
