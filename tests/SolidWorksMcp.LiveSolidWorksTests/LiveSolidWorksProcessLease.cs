using System.Diagnostics;
using System.Globalization;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Core;
using SolidWorksMcp.Protocol;
using SolidWorksMcp.Provider.SolidWorks;
using Xunit;

namespace SolidWorksMcp.LiveSolidWorksTests;

/// <summary>
/// Owns the explicitly selected SOLIDWORKS process for one Live-test assembly run.
/// 为一整轮 Live 测试持有调用方明确指定的 SOLIDWORKS 进程所有权。
/// </summary>
/// <remarks>
/// The production provider deliberately detaches without calling <c>ISldWorks.ExitApp</c>, because an arbitrary
/// interactive session may contain user work.  The Live runner is different: the runner explicitly opts into process
/// ownership with <c>SOLIDWORKS_MCP_LIVE_CLOSE_PROCESS=1</c>, so this fixture requests a graceful close for that exact
/// PID after all Live tests in the collection finish.  It never terminates a process and never answers a modal dialog.
///
/// 生产 Provider 故意只 detach，不调用 <c>ISldWorks.ExitApp</c>，因为任意交互式 session 可能包含用户工作。
/// Live runner 不同：只有显式设置 <c>SOLIDWORKS_MCP_LIVE_CLOSE_PROCESS=1</c> 才拥有这个精确 PID；fixture 会在本组
/// Live 测试全部完成后请求正常退出。它绝不强杀进程，也不对模态对话框盲点 Enter/Escape/OK。
/// </remarks>
public sealed class LiveSolidWorksProcessLease : IAsyncLifetime
{
    private int? processId;
    private DateTime? startTimeUtc;
    private bool closeWhenFinished;
    private string? workspace;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        string? pidText = Environment.GetEnvironmentVariable("SOLIDWORKS_MCP_LIVE_PROCESS_ID");
        closeWhenFinished = string.Equals(
            Environment.GetEnvironmentVariable("SOLIDWORKS_MCP_LIVE_CLOSE_PROCESS"),
            "1",
            StringComparison.Ordinal);

        if (!closeWhenFinished
            || !int.TryParse(pidText, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedPid)
            || parsedPid <= 0)
        {
            // Hosted-safe discovery and explicitly non-owned interactive runs are no-op by design.
            // Hosted-safe discovery 或未显式声明 ownership 的交互式运行按设计不做进程副作用。
            return;
        }

        using Process process = GetSolidWorksProcess(parsedPid);
        process.Refresh();
        processId = parsedPid;
        startTimeUtc = process.StartTime.ToUniversalTime();
        workspace = Environment.GetEnvironmentVariable("SOLIDWORKS_MCP_LIVE_WORKSPACE");
        bool cleanupOnly = string.Equals(
            Environment.GetEnvironmentVariable("SOLIDWORKS_MCP_LIVE_CLEANUP_ONLY"),
            "1",
            StringComparison.Ordinal);
        if (cleanupOnly && !string.IsNullOrWhiteSpace(workspace))
        {
            await using var provider = new SolidWorksCadProvider(
                new CadPathAllowlist([Path.GetFullPath(workspace.Trim())]));
            OperationResult<MutationReceipt> cleanup = await provider.ExitOwnedLiveProcessAsync(
                parsedPid,
                Path.GetFullPath(workspace.Trim())).ConfigureAwait(false);
            if (!cleanup.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Live workspace preflight cleanup failed: {FormatError(cleanup.Error)}");
            }
        }
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (!closeWhenFinished || processId is not int ownedProcessId || startTimeUtc is not DateTime expectedStart)
        {
            return;
        }

        Process process;
        try
        {
            process = GetSolidWorksProcess(ownedProcessId);
        }
        catch (InvalidOperationException exception) when (exception.InnerException is ArgumentException)
        {
            // ExitApp may finish between the collection fixture's initialization and disposal. That is already the
            // desired terminal state, so do not turn a successful process shutdown into a cleanup failure.
            // ExitApp 可能在 fixture 初始化和 disposal 之间完成；这已经是期望终态，不能把成功关闭误判成失败。
            return;
        }

        using (process)
        {
            await DisposeOwnedProcessAsync(process, ownedProcessId, expectedStart).ConfigureAwait(false);
        }
    }

    private async Task DisposeOwnedProcessAsync(Process process, int ownedProcessId, DateTime expectedStart)
    {
        process.Refresh();
        DateTime actualStart = process.StartTime.ToUniversalTime();
        if (actualStart != expectedStart)
        {
            throw new InvalidOperationException(
                $"BLOCKED_HUMAN_ACTION_REQUIRED: SOLIDWORKS PID {ownedProcessId.ToString(CultureInfo.InvariantCulture)} was reused before cleanup.");
        }

        if (process.HasExited)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(workspace))
        {
            await using var provider = new SolidWorksCadProvider(
                new CadPathAllowlist([Path.GetFullPath(workspace.Trim())]));
            OperationResult<MutationReceipt> cleanup = await provider.ExitOwnedLiveProcessAsync(
                ownedProcessId,
                Path.GetFullPath(workspace.Trim())).ConfigureAwait(false);
            if (!cleanup.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Live workspace cleanup failed: {FormatError(cleanup.Error)}");
            }
        }

        process.Refresh();
        if (process.HasExited)
        {
            return;
        }

        if (!process.Responding)
        {
            throw new InvalidOperationException(
                $"BLOCKED_HUMAN_ACTION_REQUIRED: SOLIDWORKS PID {ownedProcessId.ToString(CultureInfo.InvariantCulture)} is not responding; no blind termination was attempted.");
        }

        if (!process.CloseMainWindow())
        {
            throw new InvalidOperationException(
                $"BLOCKED_HUMAN_ACTION_REQUIRED: SOLIDWORKS PID {ownedProcessId.ToString(CultureInfo.InvariantCulture)} did not expose a closable main window; no blind termination was attempted.");
        }

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(45));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"BLOCKED_HUMAN_ACTION_REQUIRED: SOLIDWORKS PID {ownedProcessId.ToString(CultureInfo.InvariantCulture)} did not exit after a graceful close request; inspect the visible dialog and close it manually.");
        }
    }

    private static Process GetSolidWorksProcess(int requestedProcessId)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(requestedProcessId);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                $"The explicitly owned SOLIDWORKS PID {requestedProcessId.ToString(CultureInfo.InvariantCulture)} is no longer running.",
                exception);
        }

        if (!process.ProcessName.Equals("SLDWORKS", StringComparison.OrdinalIgnoreCase))
        {
            process.Dispose();
            throw new InvalidOperationException(
                $"The explicit Live process ID {requestedProcessId.ToString(CultureInfo.InvariantCulture)} is not SLDWORKS.");
        }

        return process;
    }

    private static string FormatError(OperationError? error) =>
        error is null
            ? "<no-operation-error>"
            : $"code={error.Code}; category={error.Category}; message={error.Message}";
}

/// <summary>
/// Serializes Live tests and scopes the process lease to the whole Live assembly.
/// 串行化 Live 测试，并把进程 lease 的生命周期绑定到整个 Live 程序集。
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LiveSolidWorksTestGroup : ICollectionFixture<LiveSolidWorksProcessLease>
{
    public const string Name = "Live SOLIDWORKS";
}
