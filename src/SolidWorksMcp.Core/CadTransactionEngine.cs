using System.Collections.Concurrent;
using System.Collections.Immutable;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Core;

/// <summary>Risk levels used by policy before a CAD operation is allowed to execute.</summary>
/// <remarks>
/// The ordering is intentional: larger values require stronger preconditions and later issues will add checkpoint,
/// approval and release-gate policy.  风险等级有明确顺序；数值越高，后续需要的 checkpoint、审批和 Release Gate 越强。
/// </remarks>
public enum CadRiskLevel
{
    /// <summary>Read-only inspection.</summary>
    Read,

    /// <summary>Low-risk write that does not change model topology.</summary>
    LowRiskWrite,

    /// <summary>Part/model topology mutation.</summary>
    ModelMutation,

    /// <summary>Assembly structure or mate mutation.</summary>
    AssemblyMutation,

    /// <summary>Engineering drawing mutation.</summary>
    DrawingMutation,

    /// <summary>Drawing release or equivalent governed publication.</summary>
    DrawingRelease,

    /// <summary>Destructive save or overwrite.</summary>
    DestructiveSaveOverwrite,
}

/// <summary>Stable precondition categories serialized in a transaction plan.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum CadPreconditionKind
{
    /// <summary>Expected provider process/session identity.</summary>
    SessionIdentity,

    /// <summary>Expected document identity and type.</summary>
    DocumentIdentity,

    /// <summary>Expected document state hash.</summary>
    ExpectedStateHash,

    /// <summary>Required declared capability.</summary>
    Capability,

    /// <summary>Target path must satisfy a policy allowlist.</summary>
    PathAllowlist,
}

/// <summary>Process/session/document binding carried by every transaction plan.</summary>
/// <remarks>
/// This target is deliberately independent of SOLIDWORKS COM types.  The native adapter fills the same values from
/// its STA-owned session and document registry; it must never redirect to whatever document happens to be active.
/// 该 target 不依赖 SOLIDWORKS COM 类型；原生 adapter 从 STA Session/document registry 填入同一契约，不能重定向
/// 到“恰好 active”的文档。
/// </remarks>
public sealed record CadTransactionTarget
{
    /// <summary>Bound provider session identity.</summary>
    public required SessionId SessionId { get; init; }

    /// <summary>Optional document identity for document-scoped operations.</summary>
    public DocumentId? DocumentId { get; init; }

    /// <summary>Optional canonical document path for policy and audit.</summary>
    public string? DocumentPath { get; init; }

    /// <summary>Optional document type assertion.</summary>
    public CadDocumentType? DocumentType { get; init; }

    /// <summary>Optional configuration assertion.</summary>
    public string? Configuration { get; init; }

    /// <summary>Expected state hash captured immediately before planning.</summary>
    public string? ExpectedStateHash { get; init; }
}

/// <summary>One whitelisted, serializable operation in a transaction plan.</summary>
/// <remarks>
/// Arguments are opaque data for the registered operation handler, not executable code.  The engine rejects unknown
/// operation codes, so this type cannot become an arbitrary eval/PowerShell/macro escape hatch.
/// Arguments 是注册 operation handler 的数据，不是可执行代码；未知 operation code 会被拒绝，不能演化成 eval、
/// PowerShell 或宏的万能入口。
/// </remarks>
public sealed record CadPlannedOperation
{
    /// <summary>Stable allowlisted operation code, such as <c>part.create</c>.</summary>
    public required string OperationCode { get; init; }

    /// <summary>Risk level used for preflight policy.</summary>
    public required CadRiskLevel RiskLevel { get; init; }

    /// <summary>Deterministic scalar arguments; handlers define their own schema.</summary>
    public ImmutableDictionary<string, string> Arguments { get; init; } = [];
}

/// <summary>One declarative plan precondition.</summary>
public sealed record CadPlanPrecondition
{
    /// <summary>Precondition category.</summary>
    public required CadPreconditionKind Kind { get; init; }

    /// <summary>Expected canonical value or capability name.</summary>
    public required string ExpectedValue { get; init; }
}

/// <summary>One postcondition/invariant to be proved by a registered verifier.</summary>
public sealed record CadPlanInvariant
{
    /// <summary>Stable invariant code, for example <c>body.count</c>.</summary>
    public required string Code { get; init; }

    /// <summary>Expected canonical value.</summary>
    public required string ExpectedValue { get; init; }
}

/// <summary>Finite execution budget for one plan.</summary>
public sealed record CadOperationBudget
{
    /// <summary>Maximum number of planned operations.</summary>
    public int MaxOperations { get; init; } = 32;

    /// <summary>Maximum retry count after a retryable operation failure.</summary>
    public int MaxRetries { get; init; } = 2;

    /// <summary>Maximum wall-clock time for the plan.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Maximum provider/COM calls reserved by the plan.</summary>
    public int MaxProviderCalls { get; init; } = 256;
}

/// <summary>Serializable, whitelisted CAD transaction plan.</summary>
public sealed record CadTransactionPlan
{
    /// <summary>Schema version for migration and audit.</summary>
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>Stable transaction identity.</summary>
    public required TransactionId TransactionId { get; init; }

    /// <summary>Caller key used to reconcile duplicate requests.</summary>
    public required IdempotencyKey IdempotencyKey { get; init; }

    /// <summary>Process/document/configuration target.</summary>
    public required CadTransactionTarget Target { get; init; }

    /// <summary>Whitelisted operations to execute in order.</summary>
    public required ImmutableArray<CadPlannedOperation> Operations { get; init; }

    /// <summary>Preconditions that must be evaluated before the first operation.</summary>
    public ImmutableArray<CadPlanPrecondition> Preconditions { get; init; } = [];

    /// <summary>Invariants that must be verified before commit.</summary>
    public ImmutableArray<CadPlanInvariant> Invariants { get; init; } = [];

    /// <summary>Finite execution budget.</summary>
    public CadOperationBudget Budget { get; init; } = new();

    /// <summary>Named verification strategy; no arbitrary verifier expression is accepted.</summary>
    public string VerificationStrategy { get; init; } = "provider.inspect";
}

/// <summary>Structured result from one registered plan operation.</summary>
public sealed record CadOperationExecution
{
    /// <summary>Operation code that actually ran.</summary>
    public required string OperationCode { get; init; }

    /// <summary>Whether the operation changed CAD state.</summary>
    public required bool Changed { get; init; }

    /// <summary>Optional resulting state hash.</summary>
    public string? StateHash { get; init; }

    /// <summary>Provider observations supporting the operation result.</summary>
    public ImmutableArray<EvidenceObservation> Observations { get; init; } = [];
}

/// <summary>Proof returned by the registered invariant verifier.</summary>
public sealed record CadVerificationResult
{
    /// <summary>Whether all requested invariants were proven.</summary>
    public required bool Passed { get; init; }

    /// <summary>Verified observations.</summary>
    public ImmutableArray<EvidenceObservation> Observations { get; init; } = [];
}

/// <summary>Context passed to registered operation handlers without exposing mutable engine state.</summary>
public sealed record CadTransactionContext
{
    /// <summary>Current immutable plan.</summary>
    public required CadTransactionPlan Plan { get; init; }

    /// <summary>One-based attempt number for the current operation.</summary>
    public required int Attempt { get; init; }

    /// <summary>Number of provider calls already consumed by the plan.</summary>
    public required int ProviderCallsUsed { get; init; }
}

/// <summary>Registered operation implementation; handlers are selected by allowlisted operation code.</summary>
public interface ICadPlanOperationExecutor
{
    /// <summary>Executes one planned operation under the engine's timeout and cancellation token.</summary>
    Task<OperationResult<CadOperationExecution>> ExecuteAsync(
        CadPlannedOperation operation,
        CadTransactionContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>Registered postcondition verifier for one named verification strategy.</summary>
public interface ICadPlanVerifier
{
    /// <summary>Verifies all plan invariants and returns evidence, never a bare boolean.</summary>
    Task<OperationResult<CadVerificationResult>> VerifyAsync(
        CadTransactionPlan plan,
        CadTransactionContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>Committed/rejected transaction receipt stored for idempotency reconciliation.</summary>
public sealed record CadTransactionReceipt
{
    /// <summary>Transaction identity.</summary>
    public required TransactionId TransactionId { get; init; }

    /// <summary>Idempotency key.</summary>
    public required IdempotencyKey IdempotencyKey { get; init; }

    /// <summary>Stable terminal status.</summary>
    public required string Status { get; init; }

    /// <summary>Number of operation attempts.</summary>
    public required int Attempts { get; init; }

    /// <summary>Number of successfully completed planned operations.</summary>
    public required int OperationsExecuted { get; init; }

    /// <summary>Final state hash if the verifier produced one.</summary>
    public string? FinalStateHash { get; init; }
}

/// <summary>Thread-safe storage abstraction for successful idempotency results.</summary>
public interface ICadIdempotencyStore
{
    /// <summary>Looks up a previously committed receipt.</summary>
    bool TryGet(IdempotencyKey key, out CadTransactionReceipt? receipt);

    /// <summary>Records the first committed receipt for a key.</summary>
    bool TryAdd(IdempotencyKey key, CadTransactionReceipt receipt);
}

/// <summary>In-memory idempotency store suitable for one host lifetime and deterministic tests.</summary>
public sealed class InMemoryCadIdempotencyStore : ICadIdempotencyStore
{
    private readonly ConcurrentDictionary<string, CadTransactionReceipt> receipts = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public bool TryGet(IdempotencyKey key, out CadTransactionReceipt? receipt) => receipts.TryGetValue(key.Value, out receipt);

    /// <inheritdoc />
    public bool TryAdd(IdempotencyKey key, CadTransactionReceipt receipt) => receipts.TryAdd(key.Value, receipt);
}

/// <summary>Validates a transaction plan before any registered handler is invoked.</summary>
public static class CadTransactionPlanValidator
{
    private static readonly ImmutableHashSet<string> allowedOperations =
        ["part.create", "part.mutate", "assembly.mutate", "drawing.mutate", "drawing.release", "document.save"];

    /// <summary>Returns null when the plan is valid, otherwise a stable validation error.</summary>
    public static OperationError? Validate(CadTransactionPlan? plan)
    {
        if (plan is null)
        {
            return new OperationError(ErrorCodes.InvalidRequest, "A CAD transaction plan is required.", ErrorCategories.Validation);
        }

        if (!string.Equals(plan.SchemaVersion, "1.0", StringComparison.Ordinal))
        {
            return new OperationError(ErrorCodes.InvalidRequest, "The CAD transaction plan schema version is unsupported.", ErrorCategories.Validation);
        }

        if (plan.Operations.IsDefaultOrEmpty)
        {
            return new OperationError(ErrorCodes.InvalidRequest, "A CAD transaction plan must contain at least one operation.", ErrorCategories.Validation);
        }

        if (plan.Operations.Length > plan.Budget.MaxOperations || plan.Budget.MaxOperations <= 0)
        {
            return new OperationError(ErrorCodes.InvalidRequest, "The transaction operation count exceeds its finite budget.", ErrorCategories.Validation);
        }

        if (plan.Budget.MaxRetries is < 0 or > 8 || plan.Budget.MaxProviderCalls <= 0 || plan.Budget.Timeout <= TimeSpan.Zero)
        {
            return new OperationError(ErrorCodes.InvalidRequest, "The transaction budget is invalid or unbounded.", ErrorCategories.Validation);
        }

        if (string.IsNullOrWhiteSpace(plan.TransactionId.Value) || string.IsNullOrWhiteSpace(plan.IdempotencyKey.Value))
        {
            return new OperationError(ErrorCodes.InvalidRequest, "Transaction and idempotency identities are required.", ErrorCategories.Validation);
        }

        if (string.IsNullOrWhiteSpace(plan.VerificationStrategy) || !string.Equals(plan.VerificationStrategy, "provider.inspect", StringComparison.Ordinal))
        {
            return new OperationError(ErrorCodes.InvalidRequest, "The verification strategy is not allowlisted.", ErrorCategories.Validation);
        }

        if (string.IsNullOrWhiteSpace(plan.Target.SessionId.Value))
        {
            return new OperationError(ErrorCodes.InvalidRequest, "A transaction target must bind a provider session.", ErrorCategories.Validation);
        }

        foreach (CadPlannedOperation operation in plan.Operations)
        {
            if (operation is null || string.IsNullOrWhiteSpace(operation.OperationCode) || !allowedOperations.Contains(operation.OperationCode))
            {
                return new OperationError(
                    ErrorCodes.InvalidRequest,
                    $"Operation code '{operation?.OperationCode ?? "<null>"}' is not allowlisted.",
                    ErrorCategories.Validation,
                    remediation: "Use a registered high-level CAD operation; arbitrary code is not supported.");
            }
        }

        bool requiresState = plan.Operations.Any(operation => operation.RiskLevel >= CadRiskLevel.ModelMutation);
        if (requiresState
            && string.IsNullOrWhiteSpace(plan.Target.ExpectedStateHash)
            && !plan.Preconditions.Any(precondition => precondition.Kind == CadPreconditionKind.ExpectedStateHash))
        {
            return new OperationError(
                ErrorCodes.StateConflict,
                "Model, assembly, drawing and release mutations require an expected state hash.",
                ErrorCategories.State,
                remediation: "Inspect the target and include its current state hash before planning a mutation.");
        }

        return null;
    }
}

/// <summary>Executes validated plans with finite retry, timeout, budget and idempotency semantics.</summary>
/// <remarks>
/// F01 intentionally stops before checkpoint/rollback.  F02 adds the fail-closed checkpoint boundary; this engine
/// already leaves a clean seam so a high-risk operation can be blocked before its handler runs.
/// F01 暂不实现 checkpoint/rollback；F02 将增加 fail-closed checkpoint 边界，本引擎已预留在 handler 之前阻断高风险操作的 seam。
/// </remarks>
public sealed class CadTransactionEngine(
    ICadPlanOperationExecutor operationExecutor,
    ICadPlanVerifier planVerifier,
    ICadIdempotencyStore? suppliedIdempotencyStore = null)
{
    private readonly ICadPlanOperationExecutor executor = operationExecutor ?? throw new ArgumentNullException(nameof(operationExecutor));
    private readonly ICadPlanVerifier verifier = planVerifier ?? throw new ArgumentNullException(nameof(planVerifier));
    private readonly ICadIdempotencyStore idempotencyStore = suppliedIdempotencyStore ?? new InMemoryCadIdempotencyStore();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> keyGates = new(StringComparer.Ordinal);

    /// <summary>Runs one plan or reconciles a previously committed request with the same idempotency key.</summary>
    public async Task<OperationResult<CadTransactionReceipt>> ExecuteAsync(
        CadTransactionPlan plan,
        CancellationToken cancellationToken = default)
    {
        OperationError? validationError = CadTransactionPlanValidator.Validate(plan);
        if (validationError is not null)
        {
            return Failure(plan?.TransactionId.Value ?? "invalid", validationError);
        }

        string operationId = $"transaction:{plan!.TransactionId.Value}";
        SemaphoreSlim gate = keyGates.GetOrAdd(plan.IdempotencyKey.Value, static _ => new SemaphoreSlim(1, 1));
        try
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Failure(
                operationId,
                new OperationError(ErrorCodes.Cancelled, "The CAD transaction was cancelled while waiting for its idempotency gate.", ErrorCategories.Execution));
        }

        try
        {
            if (idempotencyStore.TryGet(plan.IdempotencyKey, out CadTransactionReceipt? existing) && existing is not null)
            {
                return OperationResults.Success(
                    existing,
                    operationId,
                    new OperationEvidence(
                        "cad-transaction",
                        [
                            new EvidenceObservation("idempotency.status", "reconciled"),
                            new EvidenceObservation("transaction.id", existing.TransactionId.Value),
                            new EvidenceObservation("idempotency.key", existing.IdempotencyKey.Value),
                        ]));
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(plan.Budget.Timeout);
            int attempts = 0;
            int providerCalls = 0;
            int operationsExecuted = 0;
            string? finalStateHash = null;
            var observations = ImmutableArray.CreateBuilder<EvidenceObservation>();

            foreach (CadPlannedOperation operation in plan.Operations)
            {
                OperationResult<CadOperationExecution>? execution = null;
                for (int attempt = 1; attempt <= plan.Budget.MaxRetries + 1; attempt++)
                {
                    attempts++;
                    if (++providerCalls > plan.Budget.MaxProviderCalls)
                    {
                        return Failure(
                            operationId,
                            new OperationError(
                                ErrorCodes.Timeout,
                                "The transaction provider-call budget was exhausted before completion.",
                                ErrorCategories.Execution,
                                retryable: false,
                                remediation: "Reduce the plan scope or increase its explicit finite provider-call budget."),
                            observations.ToImmutable());
                    }

                    try
                    {
                        var context = new CadTransactionContext
                        {
                            Plan = plan,
                            Attempt = attempt,
                            ProviderCallsUsed = providerCalls,
                        };
                        execution = await executor.ExecuteAsync(operation, context, timeout.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return Failure(
                            operationId,
                            new OperationError(ErrorCodes.Cancelled, "The CAD transaction was cancelled by its caller.", ErrorCategories.Execution),
                            observations.ToImmutable());
                    }
                    catch (OperationCanceledException)
                    {
                        return Failure(
                            operationId,
                            new OperationError(
                                ErrorCodes.Timeout,
                                "The CAD transaction exceeded its finite timeout budget.",
                                ErrorCategories.Execution,
                                retryable: false,
                                remediation: "Inspect the CAD session and start a new bounded transaction."),
                            observations.ToImmutable());
                    }

                    if (execution is null || execution.IsSuccess || execution.Error?.Retryable != true || attempt > plan.Budget.MaxRetries)
                    {
                        break;
                    }
                }

                if (execution is null || !execution.IsSuccess || execution.Value is null)
                {
                    return Failure(
                        operationId,
                        execution?.Error ?? new OperationError(ErrorCodes.ProviderFailure, "The operation returned no result.", ErrorCategories.Provider),
                        observations.ToImmutable());
                }

                if (!execution.Value.OperationCode.Equals(operation.OperationCode, StringComparison.Ordinal))
                {
                    return Failure(
                        operationId,
                        new OperationError(
                            ErrorCodes.InvariantViolation,
                            "The registered handler returned evidence for a different operation code.",
                            ErrorCategories.Invariant,
                            remediation: "Fix the operation registry before retrying the transaction."),
                        observations.ToImmutable());
                }

                operationsExecuted++;
                observations.AddRange(execution.Evidence?.Observations ?? []);
                observations.Add(new EvidenceObservation("operation.code", execution.Value.OperationCode));
                observations.Add(new EvidenceObservation("operation.attempts", attempts.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                finalStateHash = execution.Value.StateHash ?? finalStateHash;
            }

            var verifyContext = new CadTransactionContext
            {
                Plan = plan,
                Attempt = attempts,
                ProviderCallsUsed = providerCalls,
            };
            OperationResult<CadVerificationResult>? verification;
            try
            {
                verification = await verifier.VerifyAsync(plan, verifyContext, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Failure(
                    operationId,
                    new OperationError(ErrorCodes.Cancelled, "The CAD transaction was cancelled by its caller.", ErrorCategories.Execution),
                    observations.ToImmutable());
            }
            catch (OperationCanceledException)
            {
                return Failure(
                    operationId,
                    new OperationError(
                        ErrorCodes.Timeout,
                        "The CAD transaction exceeded its finite timeout budget during verification.",
                        ErrorCategories.Execution,
                        remediation: "Inspect the CAD session and start a new bounded transaction."),
                    observations.ToImmutable());
            }

            if (verification is null || !verification.IsSuccess || verification.Value is null)
            {
                return Failure(
                    operationId,
                    verification?.Error ?? new OperationError(ErrorCodes.InvariantViolation, "The verifier returned no result.", ErrorCategories.Invariant),
                    observations.ToImmutable());
            }

            observations.AddRange(verification.Evidence?.Observations ?? []);
            if (!verification.Value.Passed)
            {
                return Failure(
                    operationId,
                    new OperationError(
                        ErrorCodes.InvariantViolation,
                        "One or more transaction invariants were not proven.",
                        ErrorCategories.Invariant,
                        remediation: "Inspect the target state and reconcile or roll back before retrying."),
                    observations.ToImmutable());
            }

            var receipt = new CadTransactionReceipt
            {
                TransactionId = plan.TransactionId,
                IdempotencyKey = plan.IdempotencyKey,
                Status = "committed",
                Attempts = attempts,
                OperationsExecuted = operationsExecuted,
                FinalStateHash = finalStateHash,
            };
            idempotencyStore.TryAdd(plan.IdempotencyKey, receipt);
            observations.Add(new EvidenceObservation("transaction.status", receipt.Status));
            observations.Add(new EvidenceObservation("transaction.id", receipt.TransactionId.Value));
            return OperationResults.Success(receipt, operationId, new OperationEvidence("cad-transaction", observations.ToImmutable(), stateHash: finalStateHash));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure(
                operationId,
                new OperationError(ErrorCodes.Cancelled, "The CAD transaction was cancelled by its caller.", ErrorCategories.Execution));
        }
        finally
        {
            gate.Release();
        }
    }

    private static OperationResult<CadTransactionReceipt> Failure(
        string operationId,
        OperationError error,
        ImmutableArray<EvidenceObservation> observations = default)
    {
        return OperationResults.Failure<CadTransactionReceipt>(
            operationId,
            error,
            new OperationEvidence("cad-transaction", observations.IsDefault ? [] : observations));
    }
}
