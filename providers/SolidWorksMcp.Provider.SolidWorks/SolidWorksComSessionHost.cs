using SolidWorks.Interop.sldworks;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Provider.SolidWorks;

/// <summary>
/// Coordinates one process-bound SOLIDWORKS COM attachment and its dedicated single-writer STA.
/// </summary>
/// <remarks>
/// The host is intentionally smaller than a future CAD service.  Its only responsibilities are apartment affinity,
/// ROT attachment, session identity validation and lifecycle.  Modeling, drawing and transaction policy belong in
/// higher layers so the COM adapter does not become a God Service.
///
/// Host 故意保持小而专一：只负责 Apartment 亲和性、ROT 附着、Session identity 校验和生命周期。建模、出图和
/// transaction policy 必须位于更高层，避免 COM adapter 演化成 God Service。
/// </remarks>
internal sealed class SolidWorksComSessionHost(StaComDispatcher? suppliedDispatcher = null) : IAsyncDisposable
{
    private readonly StaComDispatcher dispatcher = suppliedDispatcher ?? new StaComDispatcher();
    private SolidWorksSessionAttachment? attachment;
    private int disposed;

    /// <summary>Gets whether a native attachment is currently owned by this host.</summary>
    public bool IsAttached => Volatile.Read(ref attachment) is not null;

    /// <summary>Attaches to an existing, explicitly identified interactive SOLIDWORKS session.</summary>
    /// <remarks>
    /// No process is launched here.  With no requested PID, ROT discovery must find exactly one eligible session;
    /// with a PID, every accepted COM object must report that same PID through ISldWorks.GetProcessID().
    /// 这里绝不启动进程。未指定 PID 时必须恰好找到一个候选；指定 PID 时，每个接受的 COM 对象都必须通过
    /// ISldWorks.GetProcessID() 报告相同 PID。
    /// </remarks>
    public async Task<OperationResult<SolidWorksSessionInfo>> AttachAsync(
        CadSessionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (cancellationToken.IsCancellationRequested)
        {
            return SolidWorksProviderResults.Cancelled<SolidWorksSessionInfo>("session.attach");
        }

        try
        {
            return await dispatcher.InvokeAsync(() => AttachOnSta(options), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return SolidWorksProviderResults.Cancelled<SolidWorksSessionInfo>("session.attach");
        }
        catch (ObjectDisposedException exception)
        {
            return SolidWorksProviderResults.ProviderFailure<SolidWorksSessionInfo>(
                "session.attach",
                exception,
                "The SOLIDWORKS COM session host has been disposed.");
        }
        catch (Exception exception)
        {
            return SolidWorksProviderResults.ProviderFailure<SolidWorksSessionInfo>(
                "session.attach",
                exception,
                "SOLIDWORKS session attachment failed before a verified session was returned.");
        }
    }

    /// <summary>Validates that the current attachment still represents the expected process and revision.</summary>
    /// <remarks>
    /// The method is the seed of the future pre-mutation state guard.  A future document operation must call the
    /// equivalent verification immediately before mutation rather than trusting a previously active document.
    /// 这是未来 mutation 前状态 guard 的基础；正式写入必须在写入前重新验证，而不能信任旧的 ActiveDoc。
    /// </remarks>
    public async Task<OperationResult<SolidWorksSessionInfo>> VerifyAsync(
        SessionId expectedSessionId,
        string expectedAttachmentGeneration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedAttachmentGeneration);
        if (cancellationToken.IsCancellationRequested)
        {
            return SolidWorksProviderResults.Cancelled<SolidWorksSessionInfo>("session.verify");
        }

        try
        {
            return await dispatcher.InvokeAsync(
                () => VerifyOnSta(expectedSessionId, expectedAttachmentGeneration),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return SolidWorksProviderResults.Cancelled<SolidWorksSessionInfo>("session.verify");
        }
        catch (Exception exception)
        {
            return SolidWorksProviderResults.ProviderFailure<SolidWorksSessionInfo>(
                "session.verify",
                exception,
                "SOLIDWORKS session verification failed before the operation could proceed.");
        }
    }

    /// <summary>
    /// Executes a vendor adapter callback on the owned STA after re-checking the process identity.
    /// 在重新校验进程 identity 后，于当前 owned STA 执行 vendor adapter callback。
    /// </summary>
    /// <remarks>
    /// The callback must consume and release every COM child it obtains before returning a vendor-neutral result.
    /// This method deliberately returns only <typeparamref name="T"/> and never a COM interface, so RCWs cannot leak
    /// into the session or MCP layers.  callback 必须在返回 vendor-neutral result 前释放所有 COM 子对象；本方法只返回
    /// T，禁止返回 COM interface，避免 RCW 泄漏到 session/MCP 层。
    /// </remarks>
    internal async Task<OperationResult<T>> InvokeOnStaAsync<T>(
        SessionId expectedSessionId,
        string expectedAttachmentGeneration,
        Func<ISldWorks, OperationResult<T>> callback,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedAttachmentGeneration);
        if (cancellationToken.IsCancellationRequested)
        {
            return SolidWorksProviderResults.Cancelled<T>("session.invoke");
        }

        try
        {
            return await dispatcher.InvokeAsync(
                () =>
                {
                    dispatcher.AssertDispatcherThread();
                    OperationResult<SolidWorksSessionInfo> verification = VerifyOnSta(
                        expectedSessionId,
                        expectedAttachmentGeneration);
                    if (!verification.IsSuccess || attachment is null)
                    {
                        return verification.Error is null
                            ? SolidWorksProviderResults.Failure<T>(
                                "session.invoke",
                                new OperationError(
                                    ErrorCodes.StateConflict,
                                    "The SOLIDWORKS session disappeared before the operation started.",
                                    ErrorCategories.State))
                            : OperationResults.Failure<T>(verification.OperationId, verification.Error, verification.Evidence);
                    }

                    return callback(attachment.Application);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return SolidWorksProviderResults.Cancelled<T>("session.invoke");
        }
        catch (Exception exception)
        {
            return SolidWorksProviderResults.ProviderFailure<T>(
                "session.invoke",
                exception,
                "The SOLIDWORKS STA operation failed before a verified vendor-neutral result was returned.");
        }
    }

    /// <summary>Detaches the COM RCW without terminating the user's SOLIDWORKS process.</summary>
    /// <remarks>
    /// Detach releases only the provider-owned RCW.  It deliberately does not call ISldWorks.ExitApp(), because an
    /// attached interactive process belongs to the user and may contain unsaved work.  这里只释放 Provider 自己的
    /// RCW，故意不调用 ISldWorks.ExitApp()；用户的交互进程可能有未保存设计，不能由 MCP 关闭。
    /// </remarks>
    public async Task<OperationResult<MutationReceipt>> DetachAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return SolidWorksProviderResults.Cancelled<MutationReceipt>("session.detach");
        }

        try
        {
            return await dispatcher.InvokeAsync(DetachOnSta, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return SolidWorksProviderResults.Cancelled<MutationReceipt>("session.detach");
        }
        catch (Exception exception)
        {
            return SolidWorksProviderResults.ProviderFailure<MutationReceipt>(
                "session.detach",
                exception,
                "SOLIDWORKS session detachment failed.");
        }
    }

    /// <summary>Releases the host and its dispatcher using a bounded best-effort shutdown.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        // Try the public lifecycle path first so the release remains on the STA.  Dispose cannot throw a COM
        // exception into application shutdown; the operation result remains available to explicit callers.
        // 先走公开生命周期路径，确保 RCW 在 STA 上释放；Dispose 不把 COM 异常抛进宿主关闭流程。
        await DetachAsync().ConfigureAwait(false);
        dispatcher.Dispose();
    }

    private OperationResult<SolidWorksSessionInfo> AttachOnSta(CadSessionOptions options)
    {
        dispatcher.AssertDispatcherThread();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        if (attachment is not null)
        {
            OperationResult<SolidWorksSessionInfo> verification = VerifyOnSta(
                attachment.Info.SessionId,
                attachment.Info.AttachmentGeneration);
            if (!verification.IsSuccess)
            {
                return verification;
            }

            if (options.RequestedProcessId is int requested && requested != attachment.Info.ProcessId)
            {
                return SolidWorksProviderResults.Failure<SolidWorksSessionInfo>(
                    "session.attach",
                    new OperationError(
                        ErrorCodes.StateConflict,
                        $"The provider is already attached to SOLIDWORKS process {attachment.Info.ProcessId}; requested process {requested}.",
                        ErrorCategories.State,
                        remediation: "Close the current provider session before selecting another SOLIDWORKS process."));
            }

            return verification;
        }

        RotLookupResult lookup = RotSolidWorksConnector.Find(options.RequestedProcessId);
        if (!lookup.IsSuccess)
        {
            return lookup.Error is OperationResult<object> error
                ? ConvertError<SolidWorksSessionInfo>(error)
                : SolidWorksProviderResults.ProviderFailure<SolidWorksSessionInfo>(
                    "session.attach",
                    new InvalidOperationException("ROT lookup returned no result."),
                    "SOLIDWORKS session discovery returned no result.");
        }

        attachment = lookup.Attachment;
        return SolidWorksProviderResults.Success(
            "session.attach",
            attachment!.Info,
            new EvidenceObservation("session.id", attachment.Info.SessionId.Value),
            new EvidenceObservation("session.process-id", attachment.Info.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new EvidenceObservation("session.revision", attachment.Info.Revision),
            new EvidenceObservation("session.user-control", attachment.Info.UserControl.ToString()),
            new EvidenceObservation("session.visible", attachment.Info.Visible.ToString()),
            new EvidenceObservation("com.thread-id", System.Environment.CurrentManagedThreadId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new EvidenceObservation("com.apartment", Thread.CurrentThread.GetApartmentState().ToString()));
    }

    private OperationResult<SolidWorksSessionInfo> VerifyOnSta(
        SessionId expectedSessionId,
        string expectedAttachmentGeneration)
    {
        dispatcher.AssertDispatcherThread();
        if (attachment is null)
        {
            return SolidWorksProviderResults.Failure<SolidWorksSessionInfo>(
                "session.verify",
                new OperationError(
                    ErrorCodes.StateConflict,
                    "No SOLIDWORKS session is attached to this provider host.",
                    ErrorCategories.State,
                    remediation: "Attach to the intended interactive SOLIDWORKS process first."));
        }

        int actualProcessId = attachment.Application.GetProcessID();
        string actualRevision = attachment.Application.RevisionNumber()?.Trim() ?? string.Empty;
        string actualSessionId = $"sw:{actualProcessId}:{actualRevision}";
        if (actualProcessId != attachment.Info.ProcessId
            || !actualRevision.Equals(attachment.Info.Revision, StringComparison.Ordinal)
            || !actualSessionId.Equals(expectedSessionId.Value, StringComparison.Ordinal)
            || !attachment.Info.AttachmentGeneration.Equals(expectedAttachmentGeneration, StringComparison.Ordinal))
        {
            return SolidWorksProviderResults.Failure<SolidWorksSessionInfo>(
                "session.verify",
                new OperationError(
                    ErrorCodes.StateConflict,
                    "The attached SOLIDWORKS process or API revision no longer matches the expected session identity.",
                    ErrorCategories.State,
                    remediation: "Stop the current operation and re-attach to the intended SOLIDWORKS process.",
                    details: new Dictionary<string, string>
                    {
                        ["expected-session-id"] = expectedSessionId.Value,
                        ["actual-session-id"] = actualSessionId,
                        ["expected-attachment-generation"] = expectedAttachmentGeneration,
                        ["actual-attachment-generation"] = attachment.Info.AttachmentGeneration,
                    }));
        }

        return SolidWorksProviderResults.Success(
            "session.verify",
            attachment.Info,
            new EvidenceObservation("session.id", attachment.Info.SessionId.Value),
            new EvidenceObservation("session.process-id", attachment.Info.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new EvidenceObservation("session.revision", attachment.Info.Revision),
            new EvidenceObservation("com.thread-id", System.Environment.CurrentManagedThreadId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new EvidenceObservation("com.apartment", Thread.CurrentThread.GetApartmentState().ToString()));
    }

    private OperationResult<MutationReceipt> DetachOnSta()
    {
        dispatcher.AssertDispatcherThread();
        if (attachment is null)
        {
            return SolidWorksProviderResults.Success(
                "session.detach",
                new MutationReceipt { Operation = "session.detach", StateHash = "detached" },
                new EvidenceObservation("session.state", "detached"),
                new EvidenceObservation("application.exit-requested", bool.FalseString));
        }

        SolidWorksSessionAttachment current = attachment;
        current.ReleaseOnSta();
        // Clear ownership only after the RCW release succeeds; a release exception must leave a retryable handle.
        // 只有 RCW 释放成功后才清除 ownership；若释放异常，必须保留可重试的句柄。
        attachment = null;
        return SolidWorksProviderResults.Success(
            "session.detach",
            new MutationReceipt { Operation = "session.detach", StateHash = "detached" },
            new EvidenceObservation("session.state", "detached"),
            new EvidenceObservation("application.exit-requested", bool.FalseString));
    }

    private static OperationResult<T> ConvertError<T>(OperationResult<object> source)
    {
        return source.Error is null
            ? SolidWorksProviderResults.ProviderFailure<T>(
                "session.attach",
                new InvalidOperationException("ROT lookup returned an invalid error envelope."),
                "SOLIDWORKS session discovery returned an invalid error envelope.")
            : OperationResults.Failure<T>(source.OperationId, source.Error, source.Evidence);
    }
}
