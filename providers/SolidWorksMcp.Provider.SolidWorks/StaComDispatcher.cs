using System.Collections.Concurrent;

namespace SolidWorksMcp.Provider.SolidWorks;

/// <summary>
/// Owns the one dedicated STA thread on which every SOLIDWORKS COM call is executed.
/// </summary>
/// <remarks>
/// SOLIDWORKS exposes an out-of-process COM automation server, but its object model is not a general-purpose
/// concurrent API.  A single queue is therefore a safety boundary, not merely a performance optimization:
/// it prevents two MCP requests from mutating one session at the same time and keeps every RCW on its creating
/// apartment.  The production provider must not let callers invoke COM directly from thread-pool threads.
///
/// SOLIDWORKS 是进程外 COM 自动化服务器，但其对象模型不是通用并发 API。单队列因此是安全边界，而不只是
/// 性能优化：它保证两个 MCP 请求不会同时修改同一个 Session，并让每个 RCW 留在创建它的 Apartment 中。
/// 生产 Provider 不允许调用方从线程池线程直接访问 COM。
/// </remarks>
internal sealed class StaComDispatcher : IDisposable
{
    private const int QueueCapacity = 256;
    private static readonly TimeSpan DisposeJoinTimeout = TimeSpan.FromSeconds(5);

    private readonly BlockingCollection<WorkItem> queue = new(new ConcurrentQueue<WorkItem>(), QueueCapacity);
    private readonly Thread worker;
    private int workerThreadId;
    private int disposed;

    /// <summary>Creates and starts a background worker whose apartment state is explicitly STA.</summary>
    /// <remarks>
    /// Apartment state is set before <see cref="Thread.Start()"/>.  Setting it after startup is too late and would
    /// make a test appear green while the real COM call is still running on an unspecified apartment.
    /// 必须在启动线程前设置 STA；启动后再设置已经太晚，否则测试可能通过而真实 COM 调用仍处于未定义 Apartment。
    /// </remarks>
    public StaComDispatcher()
    {
        worker = new Thread(Run)
        {
            IsBackground = true,
            Name = "SolidWorksMcp.SolidWorks.STA",
        };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
    }

    /// <summary>Gets whether the caller is already executing on this dispatcher's STA worker.</summary>
    public bool IsDispatcherThread => Volatile.Read(ref workerThreadId) == Environment.CurrentManagedThreadId;

    /// <summary>Queues one synchronous COM-bound operation and returns its asynchronous completion.</summary>
    /// <typeparam name="T">The non-COM result produced by the queued operation.</typeparam>
    /// <param name="operation">A synchronous delegate.  It must not leak an RCW to its caller.</param>
    /// <param name="cancellationToken">Cancellation before dequeue prevents the operation from running.</param>
    /// <returns>A task completed by the STA worker after the operation has finished.</returns>
    /// <remarks>
    /// Cancellation cannot interrupt a COM call that has already started.  It only cancels a queued item before
    /// execution; this fail-closed behavior is important because aborting halfway through a mutation would leave
    /// the CAD session in an unknown state.  已开始的 COM 调用不能被安全中断；取消只阻止尚未出队的工作项执行，
    /// 这样不会把半完成的 CAD mutation 留在未知状态。
    /// </remarks>
    public Task<T> InvokeAsync<T>(Func<T> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellationToken);
        }

        // Re-entrant calls are safe because they already have the required affinity.  Enqueuing from the STA itself
        // would deadlock when the worker waits for its own queue item.
        // 已在 STA 上的重入调用直接执行；若再次入队，worker 会等待自己的工作项而死锁。
        if (IsDispatcherThread)
        {
            return ExecuteInline(operation);
        }

        var item = new WorkItem<T>(operation, cancellationToken);
        try
        {
            queue.Add(item, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            throw new ObjectDisposedException(nameof(StaComDispatcher));
        }
        catch (OperationCanceledException)
        {
            item.Cancel(cancellationToken);
            throw;
        }

        return item.Completion.Task;
    }

    /// <summary>Throws unless the current thread is this dispatcher's STA worker.</summary>
    /// <remarks>
    /// This guard is intentionally available to every thin COM adapter.  It turns an architectural rule into an
    /// executable check instead of relying on code review alone.  该 guard 让 COM 线程亲和性成为可执行约束，而
    /// 不是只靠代码审查维持的约定。
    /// </remarks>
    public void AssertDispatcherThread()
    {
        if (!IsDispatcherThread || Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            throw new InvalidOperationException(
                "SOLIDWORKS COM access must run on the provider's dedicated STA dispatcher thread.");
        }
    }

    /// <summary>Stops the queue and waits a bounded time for the worker to leave.</summary>
    /// <remarks>
    /// Pending operations are cancelled during shutdown.  The current operation is allowed to finish because a
    /// thread abort would be unsafe for COM and could strand a mutation half-complete.
    /// 关闭时取消尚未执行的工作；当前正在执行的操作必须完成，因为强制终止 COM 线程可能留下半完成 mutation。
    /// </remarks>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        queue.CompleteAdding();

        // Drain items that have not started.  A worker that has already taken an item is allowed to finish it.
        // 清空尚未开始的工作项；已经被 worker 取出的工作项允许完成。
        while (queue.TryTake(out WorkItem? pending))
        {
            pending.Cancel(new CancellationToken(canceled: true));
        }

        bool canJoinWorker = !IsDispatcherThread;
        if (canJoinWorker)
        {
            canJoinWorker = worker.Join(DisposeJoinTimeout);
        }

        // Do not dispose the collection while the worker may still be inside GetConsumingEnumerable().  A bounded
        // join is safer than corrupting the queue; the worker owns no external resource after it exits and the
        // managed collection can then be reclaimed normally.  如果 worker 仍可能在消费队列，不能提前 Dispose 集合。
        if (canJoinWorker)
        {
            queue.Dispose();
        }
    }

    private static Task<T> ExecuteInline<T>(Func<T> operation)
    {
        try
        {
            return Task.FromResult(operation());
        }
        catch (Exception exception)
        {
            return Task.FromException<T>(exception);
        }
    }

    private void Run()
    {
        workerThreadId = Environment.CurrentManagedThreadId;
        AssertDispatcherThread();

        foreach (WorkItem item in queue.GetConsumingEnumerable())
        {
            item.Execute();
        }
    }

    private abstract class WorkItem
    {
        /// <summary>Executes the item if it was not cancelled while waiting in the bounded queue.</summary>
        public abstract void Execute();

        /// <summary>Completes the item as cancelled without invoking its COM-bound delegate.</summary>
        public abstract void Cancel(CancellationToken cancellationToken);
    }

    private sealed class WorkItem<T>(Func<T> operation, CancellationToken cancellationToken) : WorkItem
    {
        public TaskCompletionSource<T> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void Execute()
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Cancel(cancellationToken);
                return;
            }

            try
            {
                Completion.TrySetResult(operation());
            }
            catch (OperationCanceledException exception)
            {
                Completion.TrySetCanceled(exception.CancellationToken);
            }
            catch (Exception exception)
            {
                Completion.TrySetException(exception);
            }
        }

        public override void Cancel(CancellationToken token)
        {
            Completion.TrySetCanceled(token);
        }
    }
}
