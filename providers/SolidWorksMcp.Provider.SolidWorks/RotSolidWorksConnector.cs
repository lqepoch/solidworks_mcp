using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using SolidWorks.Interop.sldworks;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Provider.SolidWorks;

/// <summary>Process-bound metadata for one attached SOLIDWORKS automation session.</summary>
/// <remarks>
/// The generated session ID is derived from the actual SOLIDWORKS PID and API revision.  A caller cannot replace it
/// with a friendly name, because a friendly name cannot protect against mutating another interactive process.
/// Session ID 由真实 SOLIDWORKS PID 和 API revision 派生，调用方不能用任意友好名称替换它，以避免误写另一个进程。
/// </remarks>
internal sealed record SolidWorksSessionInfo(
    SessionId SessionId,
    int ProcessId,
    string Revision,
    bool UserControl,
    bool Visible);

/// <summary>Owns the one COM interface returned by ROT until the STA host detaches it.</summary>
/// <remarks>
/// This type is internal on purpose.  No COM interface or RCW is allowed to cross the CadAbstractions boundary or
/// be stored by MCP callers.  该类型故意保持 internal；任何 COM 接口和 RCW 都不得穿过 CadAbstractions 边界，
/// 也不得交给 MCP 调用方保存。
/// </remarks>
internal sealed class SolidWorksSessionAttachment(ISldWorks application, SolidWorksSessionInfo info)
{
    public ISldWorks Application { get; } = application;

    public SolidWorksSessionInfo Info { get; } = info;

    /// <summary>Releases this RCW on the same STA that owns it.</summary>
    public void ReleaseOnSta()
    {
        if (Marshal.IsComObject(Application))
        {
            Marshal.FinalReleaseComObject(Application);
        }
    }
}

/// <summary>Result of enumerating the Running Object Table without leaking a COM object on failure.</summary>
internal sealed record RotLookupResult(SolidWorksSessionAttachment? Attachment, OperationResult<object>? Error)
{
    public bool IsSuccess => Attachment is not null;
}

/// <summary>
/// Finds already-running SOLIDWORKS sessions through COM's Running Object Table (ROT).
/// </summary>
/// <remarks>
/// The connector deliberately does not call <c>new SldWorks()</c>, <c>Activator.CreateInstance</c>, or
/// <c>Marshal.GetActiveObject</c>.  Those patterns can start a new process or attach to an arbitrary "active"
/// process, which violates the requested-process safety contract.  ROT enumeration plus ISldWorks.GetProcessID()
/// gives an explicit process identity; an unqualified request is accepted only when exactly one session qualifies.
///
/// Connector 不会调用 new SldWorks、Activator.CreateInstance 或 Marshal.GetActiveObject，因为这些模式可能启动新
/// 进程或附着到任意“active”进程。ROT 枚举加 ISldWorks.GetProcessID() 才能得到明确的进程身份；未指定 PID 时只有
/// 恰好一个候选 Session 才允许附着。
/// </remarks>
internal static class RotSolidWorksConnector
{
    private const int SOk = 0;

    /// <summary>Enumerates ROT entries and returns an attachment owned by the caller's STA thread.</summary>
    public static RotLookupResult Find(int? requestedProcessId)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Failure(new PlatformNotSupportedException("SOLIDWORKS COM requires Windows."), "SOLIDWORKS COM is supported only on Windows.");
        }

        IRunningObjectTable? runningObjectTable = null;
        IEnumMoniker? monikerEnumerator = null;
        IBindCtx? bindContext = null;
        SolidWorksSessionAttachment? selected = null;
        bool transferSelected = false;
        int candidateCount = 0;

        try
        {
            int hresult = GetRunningObjectTable(0, out runningObjectTable);
            if (hresult != SOk || runningObjectTable is null)
            {
                return Failure(
                    new ComDiscoveryException("GetRunningObjectTable failed.", hresult),
                    "The SOLIDWORKS Running Object Table could not be opened.");
            }

            hresult = CreateBindCtx(0, out bindContext);
            if (hresult != SOk || bindContext is null)
            {
                return Failure(
                    new ComDiscoveryException("CreateBindCtx failed.", hresult),
                    "The COM bind context for SOLIDWORKS session discovery could not be created.");
            }

            runningObjectTable.EnumRunning(out monikerEnumerator);
            if (monikerEnumerator is null)
            {
                return Failure(
                    new ComDiscoveryException("EnumRunning returned no enumerator.", unchecked((int)0x80004005)),
                    "The SOLIDWORKS Running Object Table could not be enumerated.");
            }

            var monikers = new IMoniker[1];
            while (monikerEnumerator.Next(1, monikers, IntPtr.Zero) == SOk)
            {
                IMoniker? moniker = monikers[0];
                monikers[0] = null!;
                if (moniker is null)
                {
                    continue;
                }

                object? candidate = null;
                try
                {
                    runningObjectTable.GetObject(moniker, out candidate!);
                    if (candidate is not ISldWorks application)
                    {
                        continue;
                    }

                    if (!TryReadSessionInfo(application, out SolidWorksSessionInfo? info))
                    {
                        // Stale or non-SOLIDWORKS ROT entries are normal.  They are ignored, not reported as a
                        // successful session, and their RCWs are released before the next moniker is inspected.
                        // ROT 中的过期或非 SOLIDWORKS 条目是正常现象；忽略它们，并在检查下一个 moniker 前释放 RCW。
                        continue;
                    }

                    // Nullable analysis cannot infer the out-value invariant from the boolean result alone.
                    // 显式检查 out 值，既保护运行时也让 nullable 分析准确表达 ROT 边界。
                    if (info is null)
                    {
                        continue;
                    }

                    if (requestedProcessId is int requested && info.ProcessId != requested)
                    {
                        continue;
                    }

                    candidateCount++;
                    if (selected is null)
                    {
                        selected = new SolidWorksSessionAttachment(application, info);
                        candidate = null;
                        if (requestedProcessId is not null)
                        {
                            // A PID is unique by definition; the first exact match is sufficient and avoids
                            // holding duplicate ROT aliases for the same process.
                            // PID 本身唯一；第一个精确匹配已经足够，避免保留同一进程的重复 ROT alias。
                            break;
                        }
                    }
                }
                catch (COMException)
                {
                    // A process can exit between ROT enumeration and GetObject/API inspection.  Treat that entry
                    // as stale and continue; the final result remains explicit if no live match survives.
                    // 枚举和 API 检查之间进程可能退出；将该条目标记为 stale 并继续，最终结果仍会明确报告。
                }
                finally
                {
                    ReleaseComObject(candidate);
                    ReleaseComObject(moniker);
                }
            }

            if (requestedProcessId is null && candidateCount > 1)
            {
                selected?.ReleaseOnSta();
                selected = null;
                return new RotLookupResult(
                    null,
                    SolidWorksProviderResults.Ambiguous<object>("session.attach", candidateCount));
            }

            if (selected is null)
            {
                return new RotLookupResult(
                    null,
                    SolidWorksProviderResults.NotFound<object>("session.attach", requestedProcessId));
            }

            transferSelected = true;
            return new RotLookupResult(selected, null);
        }
        catch (Exception exception) when (exception is COMException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Failure(exception, "SOLIDWORKS COM session discovery failed.");
        }
        finally
        {
            if (!transferSelected)
            {
                selected?.ReleaseOnSta();
            }

            ReleaseComObject(monikerEnumerator);
            ReleaseComObject(bindContext);
            ReleaseComObject(runningObjectTable);
        }
    }

    private static bool TryReadSessionInfo(ISldWorks application, out SolidWorksSessionInfo? info)
    {
        info = null;

        int processId = application.GetProcessID();
        if (processId <= 0 || !IsLiveSolidWorksProcess(processId))
        {
            return false;
        }

        string revision = application.RevisionNumber()?.Trim() ?? string.Empty;
        if (revision.Length == 0)
        {
            return false;
        }

        // Read-only identity metadata is sampled at attach time.  It is never used to mutate visibility or user
        // control, and it gives later code an explicit record of the interactive session state it observed.
        // 这些只读身份元数据在附着时采样；不会修改 Visible/UserControl，并记录当时观察到的交互 Session 状态。
        info = new SolidWorksSessionInfo(
            new SessionId($"sw:{processId}:{revision}"),
            processId,
            revision,
            application.UserControl,
            application.Visible);
        return true;
    }

    private static bool IsLiveSolidWorksProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName.Equals("SLDWORKS", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static RotLookupResult Failure(Exception exception, string message)
    {
        return new RotLookupResult(
            null,
            SolidWorksProviderResults.ProviderFailure<object>("session.attach", exception, message));
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }

    /// <summary>Represents an HRESULT returned by a native COM discovery API without constructing COMException directly.</summary>
    /// <remarks>
    /// CA2201 correctly discourages application code from constructing the runtime-reserved COMException type.
    /// This private exception preserves the HRESULT as safe diagnostic metadata while actual COM calls are still
    /// caught as COMException at the boundary.  CA2201 禁止业务代码直接构造运行时保留的 COMException；这里使用私有
    /// 异常保存 HRESULT，真实 COM 调用抛出的 COMException 仍在边界处捕获。
    /// </remarks>
    private sealed class ComDiscoveryException : Exception
    {
        public ComDiscoveryException(string message, int hresult)
            : base(message)
        {
            HResult = hresult;
        }
    }

    [SuppressMessage("Interoperability", "SYSLIB1054", Justification = "COM ROT APIs require classic ABI marshalling for IRunningObjectTable out parameters.")]
    [DllImport("ole32.dll")]
    private static extern int GetRunningObjectTable(uint reserved, out IRunningObjectTable? runningObjectTable);

    [SuppressMessage("Interoperability", "SYSLIB1054", Justification = "COM bind-context API requires classic ABI marshalling for IBindCtx out parameters.")]
    [DllImport("ole32.dll")]
    private static extern int CreateBindCtx(uint reserved, out IBindCtx? bindContext);
}
