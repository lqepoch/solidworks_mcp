using System.Collections.Immutable;

namespace SolidWorksMcp.Protocol;

/// <summary>Schema identifiers and versions for serialized MCP/CAD contracts.</summary>
/// <remarks>
/// Schema version 是可演进契约的一部分；生产消费者必须检查它，而不能根据 JSON 字段猜测版本。
/// </remarks>
public static class ProtocolSchema
{
    /// <summary>Current version of the common operation result envelope.</summary>
    public const string OperationResult = "solidworks-mcp.operation-result";

    /// <summary>Current version for all contracts introduced by A04.</summary>
    public const string CurrentVersion = "1.0";
}

/// <summary>Stable categories used to classify an operation failure for policy and remediation.</summary>
public static class ErrorCategories
{
    /// <summary>Input or contract validation failed.</summary>
    public const string Validation = "validation";

    /// <summary>Requested capability is not supported by the selected provider.</summary>
    public const string Capability = "capability";

    /// <summary>Target identity or expected state did not match.</summary>
    public const string State = "state";

    /// <summary>CAD operation was blocked by policy or human-only interaction.</summary>
    public const string Policy = "policy";

    /// <summary>Provider or COM boundary failed.</summary>
    public const string Provider = "provider";

    /// <summary>Postcondition or invariant verification failed.</summary>
    public const string Invariant = "invariant";

    /// <summary>Operation timed out or was cancelled.</summary>
    public const string Execution = "execution";
}

/// <summary>Stable machine-readable error codes; changing a code is a breaking contract change.</summary>
public static class ErrorCodes
{
    /// <summary>Request failed validation.</summary>
    public const string InvalidRequest = "INVALID_REQUEST";

    /// <summary>A required identity or document was not found.</summary>
    public const string NotFound = "NOT_FOUND";

    /// <summary>The selected provider does not support the requested capability.</summary>
    public const string UnsupportedCapability = "UNSUPPORTED_CAPABILITY";

    /// <summary>Target identity or expected state hash no longer matches.</summary>
    public const string StateConflict = "STATE_CONFLICT";

    /// <summary>Persistent or semantic selection became stale after model mutation.</summary>
    public const string SelectionStale = "SELECTION_STALE";

    /// <summary>Checkpoint creation failed and the mutation must be fail-closed.</summary>
    public const string CheckpointFailed = "CHECKPOINT_FAILED";

    /// <summary>Unknown or unsafe modal dialog requires a human action.</summary>
    public const string BlockedHumanActionRequired = "BLOCKED_HUMAN_ACTION_REQUIRED";

    /// <summary>Finite retry budget or operation deadline was exhausted.</summary>
    public const string Timeout = "TIMEOUT";

    /// <summary>Operation was cancelled by the caller.</summary>
    public const string Cancelled = "CANCELLED";

    /// <summary>Provider boundary returned a CAD/COM failure.</summary>
    public const string ProviderFailure = "PROVIDER_FAILURE";

    /// <summary>Verified postconditions or invariants did not hold.</summary>
    public const string InvariantViolation = "INVARIANT_VIOLATION";

    /// <summary>Rollback could not prove restoration.</summary>
    public const string RollbackFailed = "ROLLBACK_FAILED";

    /// <summary>Release gate found unresolved engineering review items.</summary>
    public const string ReviewRequired = "REVIEW_REQUIRED";
}

/// <summary>One structured observation supporting a success or failure claim.</summary>
public sealed record EvidenceObservation
{
    /// <summary>Creates an observation with a stable key and textual value.</summary>
    public EvidenceObservation(string key, string value, string? expectedValue = null)
    {
        Key = RequireNonBlank(key, nameof(key));
        Value = value ?? throw new ArgumentNullException(nameof(value));
        ExpectedValue = expectedValue;
    }

    /// <summary>Gets the machine-readable observation key.</summary>
    public string Key { get; }

    /// <summary>Gets the observed value.</summary>
    public string Value { get; }

    /// <summary>Gets the optional expected value used by verification evidence.</summary>
    public string? ExpectedValue { get; }

    private static string RequireNonBlank(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}

/// <summary>Evidence attached to a result so callers can distinguish verified facts from a boolean return.</summary>
public sealed record OperationEvidence
{
    /// <summary>Creates an evidence bundle from a source and immutable observations.</summary>
    public OperationEvidence(
        string source,
        IEnumerable<EvidenceObservation>? observations = null,
        IEnumerable<string>? artifactPaths = null,
        string? stateHash = null)
    {
        Source = RequireNonBlank(source, nameof(source));
        Observations = [.. (observations ?? [])];
        ArtifactPaths = [.. (artifactPaths ?? []).Select(RequireNonBlankPath)];
        StateHash = stateHash;
    }

    /// <summary>Gets the evidence producer, for example fake-cad or solidworks-provider.</summary>
    public string Source { get; }

    /// <summary>Gets immutable observations collected during inspection and verification.</summary>
    public ImmutableArray<EvidenceObservation> Observations { get; }

    /// <summary>Gets immutable paths to generated evidence artifacts.</summary>
    public ImmutableArray<string> ArtifactPaths { get; }

    /// <summary>Gets the optional state hash bound to the observations.</summary>
    public string? StateHash { get; }

    private static string RequireNonBlank(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }

    private static string RequireNonBlankPath(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(value));
        return value.Trim();
    }
}

/// <summary>Structured error payload with a stable code, category and remediation hint.</summary>
public sealed record OperationError
{
    /// <summary>Creates an immutable structured error.</summary>
    public OperationError(
        string code,
        string message,
        string category,
        bool retryable = false,
        string? remediation = null,
        IReadOnlyDictionary<string, string>? details = null)
    {
        Code = RequireNonBlank(code, nameof(code));
        Message = RequireNonBlank(message, nameof(message));
        Category = RequireNonBlank(category, nameof(category));
        Retryable = retryable;
        Remediation = remediation;
        Details = (details ?? new Dictionary<string, string>())
            .ToImmutableDictionary(StringComparer.Ordinal);
    }

    /// <summary>Gets the stable machine-readable error code.</summary>
    public string Code { get; }

    /// <summary>Gets the safe human-readable error message.</summary>
    public string Message { get; }

    /// <summary>Gets the stable policy category.</summary>
    public string Category { get; }

    /// <summary>Gets whether a bounded retry may be attempted.</summary>
    public bool Retryable { get; }

    /// <summary>Gets an optional safe remediation instruction.</summary>
    public string? Remediation { get; }

    /// <summary>Gets immutable diagnostic details without exception or secret payloads.</summary>
    public ImmutableDictionary<string, string> Details { get; }

    private static string RequireNonBlank(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}

/// <summary>Versioned success/error/evidence envelope shared by provider and MCP layers.</summary>
/// <typeparam name="T">The typed result payload; dimensional fields must use explicit unit types.</typeparam>
public sealed record OperationResult<T>
{
    /// <summary>Creates a validated versioned result envelope.</summary>
    /// <remarks>
    /// The constructor is public for serializer compatibility; normal application code should prefer
    /// <see cref="OperationResults.Success{T}(T, string, OperationEvidence)"/> or
    /// <see cref="OperationResults.Failure{T}(string, OperationError, OperationEvidence?)"/>.
    /// 构造函数保留给序列化器和边界适配器；业务代码应使用非泛型工厂，避免制造半有效信封。
    /// </remarks>
    [System.Text.Json.Serialization.JsonConstructor]
    public OperationResult(
        string schema,
        string schemaVersion,
        string operationId,
        bool isSuccess,
        T? value,
        OperationError? error,
        OperationEvidence? evidence)
    {
        Schema = RequireNonBlank(schema, nameof(schema));
        SchemaVersion = RequireNonBlank(schemaVersion, nameof(schemaVersion));
        OperationId = RequireNonBlank(operationId, nameof(operationId));
        if (isSuccess == (error is not null))
        {
            throw new ArgumentException("A result must contain an error exactly when it is not successful.", nameof(error));
        }

        IsSuccess = isSuccess;
        Value = value;
        Error = error;
        Evidence = evidence;
    }

    /// <summary>Gets the schema identifier, which must be checked before deserialization by external consumers.</summary>
    public string Schema { get; }

    /// <summary>Gets the schema version for migration and compatibility decisions.</summary>
    public string SchemaVersion { get; }

    /// <summary>Gets the operation identity for audit correlation.</summary>
    public string OperationId { get; }

    /// <summary>Gets whether the operation reached a verified success outcome.</summary>
    public bool IsSuccess { get; }

    /// <summary>Gets the typed value when the operation succeeded.</summary>
    public T? Value { get; }

    /// <summary>Gets the structured error when the operation failed.</summary>
    public OperationError? Error { get; }

    /// <summary>Gets verification evidence for either success or failure.</summary>
    public OperationEvidence? Evidence { get; }

    private static string RequireNonBlank(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}

/// <summary>Non-generic factory methods for creating valid typed operation envelopes.</summary>
/// <remarks>
/// Keeping factories outside the generic record avoids the CA1000 generic-static pattern while retaining type safety.
/// 将工厂放在非泛型类型中，既满足分析器纪律，也让所有调用方共享同一验证路径。
/// </remarks>
public static class OperationResults
{

    /// <summary>Creates a verified success envelope.</summary>
    /// <param name="value">The verified typed result.</param>
    /// <param name="operationId">The audit correlation identifier.</param>
    /// <param name="evidence">Evidence proving the result beyond a raw boolean.</param>
    public static OperationResult<T> Success<T>(T value, string operationId, OperationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentNullException.ThrowIfNull(evidence);

        return new OperationResult<T>(
            ProtocolSchema.OperationResult,
            ProtocolSchema.CurrentVersion,
            operationId.Trim(),
            isSuccess: true,
            value,
            error: null,
            evidence);
    }

    /// <summary>Creates a failure envelope with an explicit stable error and optional evidence.</summary>
    public static OperationResult<T> Failure<T>(string operationId, OperationError error, OperationEvidence? evidence = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentNullException.ThrowIfNull(error);

        return new OperationResult<T>(
            ProtocolSchema.OperationResult,
            ProtocolSchema.CurrentVersion,
            operationId.Trim(),
            isSuccess: false,
            value: default,
            error,
            evidence);
    }
}
