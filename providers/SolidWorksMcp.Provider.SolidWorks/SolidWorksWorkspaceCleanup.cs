using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Provider.SolidWorks;

/// <summary>
/// Closes only clean or explicitly saveable test artifacts below one exact workspace root.
/// 只关闭一个精确 workspace 根目录下、可安全保存/关闭的测试 artifact。
/// </summary>
/// <remarks>
/// This is a Live-test lifecycle adapter, not a production "close everything" escape hatch. It enumerates native open
/// documents, canonicalizes their paths, ignores documents outside the test root and saves only artifacts under that
/// root before closing them. It does not use ActiveDoc, CloseAllDocuments or a blind modal-dialog action.
///
/// 这是 Live-test 生命周期 adapter，不是生产用的“关闭全部”逃生口。它枚举 native open documents、规范化路径，忽略
/// 测试根目录之外的 document，只保存并关闭根目录内的 artifact；不使用 ActiveDoc、CloseAllDocuments 或 blind modal。
/// </remarks>
internal static class SolidWorksWorkspaceCleanup
{
    /// <summary>
    /// Saves only generated documents below the exact workspace and requests ExitApp for an explicitly owned Live PID.
    /// 只保存精确 workspace 下生成的 document，并为明确 owned 的 Live PID 请求 ExitApp。
    /// </summary>
    public static OperationResult<MutationReceipt> PrepareAndExitOnSta(ISldWorks application, string workspace)
    {
        const string operation = "live.exit-owned-process";
        ArgumentNullException.ThrowIfNull(application);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);

        string root = Path.GetFullPath(workspace.Trim())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        object? rawDocuments = null;
        int preparedCount = 0;
        try
        {
            rawDocuments = application.GetDocuments();
            if (rawDocuments is Array documents)
            {
                for (int index = 0; index < documents.Length; index++)
                {
                    if (documents.GetValue(index) is not ModelDoc2 model)
                    {
                        continue;
                    }

                    try
                    {
                        string path = model.GetPathName()?.Trim() ?? string.Empty;
                        if (path.Length == 0)
                        {
                            continue;
                        }

                        string fullPath = Path.GetFullPath(path);
                        if (!IsWithinRoot(root, fullPath) || !IsGeneratedCadExtension(fullPath))
                        {
                            return SolidWorksProviderResults.Failure<MutationReceipt>(
                                operation,
                                new OperationError(
                                    ErrorCodes.StateConflict,
                                    "The owned Live process contains a document outside the exact test workspace.",
                                    ErrorCategories.State,
                                    remediation: "Close unrelated documents manually before retrying the owned-process cleanup."));
                        }

                        if (model.GetSaveFlag())
                        {
                            int saveErrors = 0;
                            int saveWarnings = 0;
                            bool saved = model.Save3(
                                (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                                ref saveErrors,
                                ref saveWarnings);
                            if (!saved || saveErrors != 0 || model.GetSaveFlag())
                            {
                                return SolidWorksProviderResults.Failure<MutationReceipt>(
                                    operation,
                                    new OperationError(
                                        ErrorCodes.ProviderFailure,
                                        "A generated Live artifact could not be saved before ExitApp.",
                                        ErrorCategories.Provider,
                                        remediation: "Inspect the generated artifact or visible SOLIDWORKS dialog before retrying."));
                            }
                        }

                        preparedCount++;
                    }
                    finally
                    {
                        SolidWorksDocumentRouting.Release(model);
                    }
                }
            }

            // ExitApp is used only after exact PID ownership and workspace scope have been verified.  It is not part
            // of the production provider detach path and never substitutes for modal-dialog policy.
            // 只有在确认精确 PID ownership 和 workspace scope 后才调用 ExitApp；生产 Provider detach 不走这里，且
            // 不能用 ExitApp 代替 modal-dialog policy。
            application.ExitApp();
            return SolidWorksProviderResults.Success(
                operation,
                new MutationReceipt
                {
                    Operation = operation,
                    StateHash = $"prepared:{preparedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                },
                new EvidenceObservation("documents.prepared", preparedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new EvidenceObservation("application.exit-requested", bool.TrueString));
        }
        catch (Exception exception)
        {
            return SolidWorksProviderResults.ProviderFailure<MutationReceipt>(
                operation,
                exception,
                "SOLIDWORKS owned-process exit failed before a verified process shutdown request.");
        }
        finally
        {
            SolidWorksDocumentRouting.Release(rawDocuments);
        }
    }

    public static OperationResult<MutationReceipt> CloseOnSta(ISldWorks application, string workspace)
    {
        const string operation = "live.cleanup-workspace";
        ArgumentNullException.ThrowIfNull(application);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);

        string root = Path.GetFullPath(workspace.Trim())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        object? rawDocuments = null;
        int closedCount = 0;
        try
        {
            rawDocuments = application.GetDocuments();
            if (rawDocuments is not Array documents)
            {
                return SolidWorksProviderResults.Success(
                    operation,
                    new MutationReceipt { Operation = operation, StateHash = "workspace-empty" },
                    new EvidenceObservation("documents.closed", "0"));
            }

            for (int index = 0; index < documents.Length; index++)
            {
                if (documents.GetValue(index) is not ModelDoc2 model)
                {
                    continue;
                }

                try
                {
                    string path = model.GetPathName()?.Trim() ?? string.Empty;
                    if (path.Length == 0)
                    {
                        continue;
                    }

                    string fullPath = Path.GetFullPath(path);
                    if (!IsWithinRoot(root, fullPath)
                        || !IsGeneratedCadExtension(fullPath))
                    {
                        continue;
                    }

                    if (model.GetSaveFlag())
                    {
                        int saveErrors = 0;
                        int saveWarnings = 0;
                        bool saved = model.Save3(
                            (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                            ref saveErrors,
                            ref saveWarnings);
                        if (!saved || saveErrors != 0 || model.GetSaveFlag())
                        {
                            return SolidWorksProviderResults.Failure<MutationReceipt>(
                                operation,
                                new OperationError(
                                    ErrorCodes.ProviderFailure,
                                    "A generated Live artifact could not be saved before cleanup.",
                                    ErrorCategories.Provider,
                                    details: new Dictionary<string, string>(StringComparer.Ordinal)
                                    {
                                        ["save-returned"] = saved.ToString(),
                                        ["save-errors"] = saveErrors.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                        ["save-warnings"] = saveWarnings.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                    },
                                    remediation: "Inspect the generated artifact or visible SOLIDWORKS dialog before retrying."));
                        }
                    }

                    int documentCountBefore = application.GetDocumentCount();
                    application.CloseDoc(fullPath);
                    bool closed = false;
                    ModelDoc2? stillOpen = null;
                    try
                    {
                        // SOLIDWORKS can keep a stale GetOpenDocument RCW visible for a short interval after CloseDoc.
                        // Poll the documented session count as well, but remain bounded and stay on the owning STA.
                        // SOLIDWORKS 在 CloseDoc 后短时间内可能仍返回 stale GetOpenDocument RCW；同时轮询官方 session
                        // count，但保持有界并始终在所属 STA 上执行。
                        for (int attempt = 0; attempt < 20; attempt++)
                        {
                            SolidWorksDocumentRouting.Release(stillOpen);
                            stillOpen = application.GetOpenDocument(fullPath);
                            int documentCountAfter = application.GetDocumentCount();
                            if (stillOpen is null || documentCountAfter < documentCountBefore)
                            {
                                closed = true;
                                break;
                            }

                            Thread.Sleep(TimeSpan.FromMilliseconds(250));
                        }

                        if (!closed)
                        {
                            return SolidWorksProviderResults.Failure<MutationReceipt>(
                                operation,
                                new OperationError(
                                    ErrorCodes.StateConflict,
                                    "SOLIDWORKS retained a generated Live artifact after the bounded graceful close request.",
                                    ErrorCategories.State,
                                    retryable: true,
                                    remediation: "Inspect the exact generated document and any modal dialog before retrying."));
                        }
                    }
                    finally
                    {
                        SolidWorksDocumentRouting.Release(stillOpen);
                    }

                    closedCount++;
                }
                finally
                {
                    SolidWorksDocumentRouting.Release(model);
                }
            }

            return SolidWorksProviderResults.Success(
                operation,
                new MutationReceipt
                {
                    Operation = operation,
                    StateHash = $"closed:{closedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                },
                new EvidenceObservation("documents.closed", closedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new EvidenceObservation("documents.root-scope", "exact-workspace"));
        }
        catch (Exception exception)
        {
            return SolidWorksProviderResults.ProviderFailure<MutationReceipt>(
                operation,
                exception,
                "SOLIDWORKS Live workspace cleanup failed before the process-close request.");
        }
        finally
        {
            SolidWorksDocumentRouting.Release(rawDocuments);
        }
    }

    private static bool IsGeneratedCadExtension(string path) =>
        path.EndsWith(".sldprt", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".slddrw", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase);

    private static bool IsWithinRoot(string root, string candidate)
    {
        if (root.Equals(candidate, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string separator = root.EndsWith(Path.DirectorySeparatorChar)
            || root.EndsWith(Path.AltDirectorySeparatorChar)
            ? string.Empty
            : Path.DirectorySeparatorChar.ToString();
        return candidate.StartsWith(root + separator, StringComparison.OrdinalIgnoreCase);
    }
}
