using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Provider.Fake;

/// <summary>Centralized result/evidence construction for FakeCad operations.</summary>
/// <remarks>统一结果构造可以让 FakeCad 与未来原生 Provider 共享错误语义，避免每个 Service 自己拼 JSON。</remarks>
internal static class FakeCadResults
{
    /// <summary>Creates a success envelope with deterministic fake evidence.</summary>
    public static OperationResult<T> Success<T>(T value, string operation, params EvidenceObservation[] observations)
    {
        string operationId = $"fake:{operation}";
        var evidence = new OperationEvidence("fake-cad", observations, stateHash: observations
            .FirstOrDefault(observation => observation.Key.Equals("state.hash", StringComparison.Ordinal))?.Value);
        return OperationResults.Success(value, operationId, evidence);
    }

    /// <summary>Creates a failure envelope with the stable error supplied by the test or fake implementation.</summary>
    public static OperationResult<T> Failure<T>(string operation, OperationError error, params EvidenceObservation[] observations)
    {
        string operationId = $"fake:{operation}";
        var evidence = new OperationEvidence("fake-cad", observations);
        return OperationResults.Failure<T>(operationId, error, evidence);
    }

    /// <summary>Creates an explicit cancellation error without throwing from the provider boundary.</summary>
    public static OperationResult<T> Cancelled<T>(string operation)
    {
        return Failure<T>(
            operation,
            new OperationError(
                ErrorCodes.Cancelled,
                "The fake CAD operation was cancelled before it started.",
                ErrorCategories.Execution,
                retryable: false,
                remediation: "Start a new operation with a live cancellation token."));
    }

    /// <summary>Creates a validation failure for malformed fake requests.</summary>
    public static OperationResult<T> Invalid<T>(string operation, string message, string? remediation = null)
    {
        return Failure<T>(
            operation,
            new OperationError(ErrorCodes.InvalidRequest, message, ErrorCategories.Validation, remediation: remediation));
    }

    /// <summary>Creates a not-found failure for an identity that is absent from the fake session.</summary>
    public static OperationResult<T> NotFound<T>(string operation, string identity)
    {
        return Failure<T>(
            operation,
            new OperationError(
                ErrorCodes.NotFound,
                $"The CAD identity '{identity}' was not found in the session.",
                ErrorCategories.State,
                remediation: "Inspect the session and use a current stable identity."));
    }

    /// <summary>Creates the common state-conflict result used after a session has been closed.</summary>
    public static OperationResult<T> Closed<T>(string operation)
    {
        return Failure<T>(
            operation,
            new OperationError(
                ErrorCodes.StateConflict,
                "The fake CAD session is closed.",
                ErrorCategories.State,
                remediation: "Start a new provider session."));
    }

    /// <summary>Creates an explicit unsupported-capability failure.</summary>
    public static OperationResult<T> Unsupported<T>(string operation, CadCapability capability)
    {
        return Failure<T>(
            operation,
            new OperationError(
                ErrorCodes.UnsupportedCapability,
                $"Capability '{capability.Name}' is not supported by this provider.",
                ErrorCategories.Capability,
                remediation: capability.Reason ?? "Select a provider that declares this capability."));
    }
}
