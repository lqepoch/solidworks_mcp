using System.Collections.Concurrent;
using System.Diagnostics;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;
using SolidWorksMcp.Provider.SolidWorks;

namespace SolidWorksMcp.LiveSolidWorksTests;

/// <summary>
/// Verifies the native provider's thread-affinity and process-binding foundation without requiring a CAD mutation.
/// </summary>
/// <remarks>
/// These are safe to run on a hosted Windows runner: they test the dispatcher and a deliberately impossible PID,
/// not a customer document and not a launched SOLIDWORKS process.  真正的几何 Live test 仍然保留在显式 opt-in
/// harness 中；本组只证明 B02 的安全边界，不冒充完整 COM/几何能力。
/// </remarks>
public sealed class StaDispatcherTests
{
    /// <summary>Concurrent callers must be serialized onto one STA worker rather than running COM work in parallel.</summary>
    [Fact]
    public async Task ConcurrentWorkRunsSeriallyOnOneStaThread()
    {
        using var dispatcher = new StaComDispatcher();
        var workerThreadIds = new ConcurrentBag<int>();
        var apartmentStates = new ConcurrentBag<ApartmentState>();
        int activeCalls = 0;
        int maximumConcurrentCalls = 0;

        Task<int>[] calls = [.. Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => dispatcher.InvokeAsync(() =>
            {
                int current = Interlocked.Increment(ref activeCalls);
                UpdateMaximum(ref maximumConcurrentCalls, current);
                workerThreadIds.Add(Environment.CurrentManagedThreadId);
                apartmentStates.Add(Thread.CurrentThread.GetApartmentState());
                Thread.Sleep(2);
                Interlocked.Decrement(ref activeCalls);
                return Environment.CurrentManagedThreadId;
            })))];

        int[] returnedThreadIds = await Task.WhenAll(calls);

        Assert.Equal(1, maximumConcurrentCalls);
        Assert.Single(workerThreadIds.Distinct());
        Assert.Single(returnedThreadIds.Distinct());
        Assert.All(apartmentStates, state => Assert.Equal(ApartmentState.STA, state));
    }

    /// <summary>Direct calls from a non-worker thread must fail the executable COM-affinity guard.</summary>
    [Fact]
    public void AffinityGuardRejectsTheCallingThread()
    {
        using var dispatcher = new StaComDispatcher();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(dispatcher.AssertDispatcherThread);

        Assert.Contains("STA", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Cancellation removes queued work before execution, preserving fail-closed COM semantics.</summary>
    [Fact]
    public async Task CancellationPreventsQueuedWorkFromExecuting()
    {
        using var dispatcher = new StaComDispatcher();
        using var firstOperationStarted = new ManualResetEventSlim(false);
        using var releaseFirstOperation = new ManualResetEventSlim(false);
        using var cancellation = new CancellationTokenSource();

        Task<int> first = dispatcher.InvokeAsync(() =>
        {
            firstOperationStarted.Set();
            releaseFirstOperation.Wait();
            return 1;
        });

        Assert.True(firstOperationStarted.Wait(TimeSpan.FromSeconds(2)), "The first queued item did not reach the STA worker.");
        Task<int> cancelled = dispatcher.InvokeAsync(() => 2, cancellation.Token);
        cancellation.Cancel();
        releaseFirstOperation.Set();

        Assert.Equal(1, await first);
        await Assert.ThrowsAsync<TaskCanceledException>(async () => await cancelled);
    }

    /// <summary>An unmatched PID must return an explicit discovery failure and never create a SOLIDWORKS process.</summary>
    [Fact]
    public async Task NativeProviderDoesNotLaunchForAnUnmatchedProcessId()
    {
        await using var provider = new SolidWorksCadProvider();
        var options = new CadSessionOptions { RequestedProcessId = int.MaxValue };

        OperationResult<ICadSession> result = await provider.StartSessionAsync(options);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(ErrorCodes.NotFound, result.Error!.Code);
        Assert.Contains("process ID", result.Error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            Process.GetProcessesByName("SLDWORKS"),
            process => process.Id == int.MaxValue);
    }

    private static void UpdateMaximum(ref int maximum, int candidate)
    {
        int observed;
        do
        {
            observed = Volatile.Read(ref maximum);
            if (candidate <= observed)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref maximum, candidate, observed) != observed);
    }
}
