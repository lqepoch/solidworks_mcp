using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Provider.SolidWorks;

/// <summary>Centralizes stable result construction at the native provider boundary.</summary>
/// <remarks>
/// Keeping operation IDs, error categories and evidence creation here prevents each future COM adapter from
/// inventing a subtly different failure contract.  将结果构造集中在这里，避免未来每个 COM adapter 各自发明
/// 不兼容的 operation ID、错误类别和证据格式。
/// </remarks>
internal static class SolidWorksProviderResults
{
    /// <summary>Creates a provider success with observations and optional state hash.</summary>
    public static OperationResult<T> Success<T>(string operation, T value, params EvidenceObservation[] observations)
    {
        string operationId = $"solidworks:{operation}";
        string? stateHash = observations
            .FirstOrDefault(observation => observation.Key.Equals("state.hash", StringComparison.Ordinal))?.Value;
        return OperationResults.Success(value, operationId, new OperationEvidence("solidworks-provider", observations, stateHash: stateHash));
    }

    /// <summary>Creates a provider failure with safe, structured diagnostics.</summary>
    public static OperationResult<T> Failure<T>(string operation, OperationError error, params EvidenceObservation[] observations)
    {
        return OperationResults.Failure<T>(
            $"solidworks:{operation}",
            error,
            new OperationEvidence("solidworks-provider", observations));
    }

    /// <summary>Creates a cancellation result without throwing across the provider contract.</summary>
    public static OperationResult<T> Cancelled<T>(string operation) => Failure<T>(
        operation,
        new OperationError(
            ErrorCodes.Cancelled,
            "The SOLIDWORKS provider operation was cancelled before it started.",
            ErrorCategories.Execution,
            remediation: "Retry with a live cancellation token and a bounded operation budget."));

    /// <summary>Creates a not-found result for a requested interactive process/session.</summary>
    public static OperationResult<T> NotFound<T>(string operation, int? requestedProcessId) => Failure<T>(
        operation,
        new OperationError(
            ErrorCodes.NotFound,
            requestedProcessId is int pid
                ? $"No running SOLIDWORKS COM session matched process ID {pid}."
                : "No running SOLIDWORKS COM session was found in the Running Object Table.",
            ErrorCategories.Provider,
            retryable: true,
            remediation: "Start SOLIDWORKS, wait for startup to complete, then retry session attachment.",
            details: requestedProcessId is int requested
                ? new Dictionary<string, string> { ["requestedProcess-id"] = requested.ToString(System.Globalization.CultureInfo.InvariantCulture) }
                : null));

    /// <summary>Creates a deterministic ambiguity failure when an unqualified attach sees multiple sessions.</summary>
    public static OperationResult<T> Ambiguous<T>(string operation, int count) => Failure<T>(
        operation,
        new OperationError(
            ErrorCodes.StateConflict,
            $"The Running Object Table contains {count} eligible SOLIDWORKS sessions; an explicit process binding is required.",
            ErrorCategories.State,
            remediation: "Provide CadSessionOptions.RequestedProcessId for the intended SOLIDWORKS process.",
            details: new Dictionary<string, string> { ["eligible-session-count"] = count.ToString(System.Globalization.CultureInfo.InvariantCulture) }));

    /// <summary>Creates a provider boundary failure without exposing COM exception text or stack traces.</summary>
    public static OperationResult<T> ProviderFailure<T>(string operation, Exception exception, string message) => Failure<T>(
        operation,
        new OperationError(
            ErrorCodes.ProviderFailure,
            message,
            ErrorCategories.Provider,
            retryable: true,
            remediation: "Check the SOLIDWORKS process, COM registration and provider diagnostic log.",
            details: new Dictionary<string, string>
            {
                ["exception-type"] = exception.GetType().FullName ?? exception.GetType().Name,
                ["hresult"] = $"0x{exception.HResult:X8}",
            }));

    /// <summary>Creates an explicit unsupported result; unimplemented native operations must not silently no-op.</summary>
    public static OperationResult<T> Unsupported<T>(string operation, CadCapability capability) => Failure<T>(
        operation,
        new OperationError(
            ErrorCodes.UnsupportedCapability,
            $"Capability '{capability.Name}' is not supported by the native SOLIDWORKS provider yet.",
            ErrorCategories.Capability,
            remediation: capability.Reason ?? "Use a provider/session that declares this capability."));

    /// <summary>Creates a state-conflict result for a closed native session.</summary>
    public static OperationResult<T> Closed<T>(string operation, SessionId sessionId) => Failure<T>(
        operation,
        new OperationError(
            ErrorCodes.StateConflict,
            $"SOLIDWORKS session '{sessionId.Value}' is closed.",
            ErrorCategories.State,
            remediation: "Start a new provider session bound to the intended SOLIDWORKS process."));
}
