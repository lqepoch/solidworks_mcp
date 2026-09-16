using System.Collections.Concurrent;
using System.Collections.Immutable;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Core;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.UnitTests;

/// <summary>Tests the F01 transaction gate with deterministic registered handlers, not arbitrary executable code.</summary>
/// <remarks>
/// The handlers below stand in for FakeCad/native provider adapters.  They prove engine policy independently from
/// COM: invalid plans never reach a handler, retry is finite, duplicate idempotency keys reconcile, and invariant
/// failure is not committed.  这些 handler 只是 FakeCad/native adapter 的 deterministic double；测试事务策略而不是
/// COM 细节，确保非法计划不执行、retry 有限、重复 key 可 reconcile、invariant 失败不 commit。
/// </remarks>
public sealed class CadTransactionEngineTests
{
    /// <summary>Unknown operation code must be rejected before any provider handler runs.</summary>
    [Fact]
    public async Task UnknownOperationIsRejectedBeforeExecution()
    {
        var executor = new RecordingExecutor();
        var verifier = new RecordingVerifier(passed: true);
        var engine = new CadTransactionEngine(executor, verifier);

        OperationResult<CadTransactionReceipt> result = await engine.ExecuteAsync(CreatePlan("not-allowlisted"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.InvalidRequest, result.Error!.Code);
        Assert.Empty(executor.Calls);
        Assert.Equal(0, verifier.Calls);
    }

    /// <summary>One retryable timeout is retried once and then commits after verified completion.</summary>
    [Fact]
    public async Task RetryableFailureUsesFiniteRetryBudget()
    {
        var executor = new RecordingExecutor(
        [
            Failure(ErrorCodes.Timeout, retryable: true),
            Success("sha256:post-state"),
        ]);
        var engine = new CadTransactionEngine(executor, new RecordingVerifier(passed: true));

        OperationResult<CadTransactionReceipt> result = await engine.ExecuteAsync(CreatePlan("part.create", maxRetries: 1));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(2, executor.Calls.Count);
        Assert.Equal(2, result.Value!.Attempts);
        Assert.Equal("committed", result.Value.Status);
    }

    /// <summary>Concurrent duplicate requests with one idempotency key execute once and reconcile the receipt.</summary>
    [Fact]
    public async Task DuplicateIdempotencyKeyReconcilesInsteadOfDuplicating()
    {
        var executor = new RecordingExecutor();
        var engine = new CadTransactionEngine(executor, new RecordingVerifier(passed: true));
        CadTransactionPlan plan = CreatePlan("part.create");

        OperationResult<CadTransactionReceipt>[] results = await Task.WhenAll(
            engine.ExecuteAsync(plan),
            engine.ExecuteAsync(plan));

        Assert.All(results, result => Assert.True(result.IsSuccess, result.Error?.Message));
        Assert.Single(executor.Calls);
        Assert.Contains(
            results,
            result => result.Evidence!.Observations.Any(observation => observation.Key == "idempotency.status" && observation.Value == "reconciled"));
    }

    /// <summary>Failed invariant verification returns a blocking error and does not store a committed receipt.</summary>
    [Fact]
    public async Task InvariantFailureIsNotCommitted()
    {
        var executor = new RecordingExecutor();
        var verifier = new RecordingVerifier(passed: false);
        var engine = new CadTransactionEngine(executor, verifier);

        OperationResult<CadTransactionReceipt> result = await engine.ExecuteAsync(CreatePlan("part.mutate"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.InvariantViolation, result.Error!.Code);
        Assert.Equal(1, verifier.Calls);
        Assert.Single(executor.Calls);
    }

    /// <summary>Provider-call budget exhaustion stops retries even when the handler keeps reporting retryable failure.</summary>
    [Fact]
    public async Task ProviderCallBudgetIsFinite()
    {
        var executor = new RecordingExecutor([Failure(ErrorCodes.Timeout, retryable: true)]);
        var engine = new CadTransactionEngine(executor, new RecordingVerifier(passed: true));
        CadTransactionPlan plan = CreatePlan("part.create", maxRetries: 8) with
        {
            Budget = new CadOperationBudget { MaxOperations = 1, MaxRetries = 8, MaxProviderCalls = 1, Timeout = TimeSpan.FromSeconds(5) },
        };

        OperationResult<CadTransactionReceipt> result = await engine.ExecuteAsync(plan);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.Timeout, result.Error!.Code);
        Assert.Single(executor.Calls);
        Assert.Equal(0, result.Value?.OperationsExecuted ?? 0);
    }

    private static CadTransactionPlan CreatePlan(string operationCode, int maxRetries = 2) => new()
    {
        TransactionId = new TransactionId($"transaction-{Guid.NewGuid():N}"),
        IdempotencyKey = new IdempotencyKey($"idempotency-{Guid.NewGuid():N}"),
        Target = new CadTransactionTarget
        {
            SessionId = new SessionId("fake-session-001"),
            DocumentId = new DocumentId("fake-document-001"),
            DocumentType = CadDocumentType.Part,
            Configuration = "Default",
            ExpectedStateHash = "sha256:pre-state",
        },
        Operations = [new CadPlannedOperation { OperationCode = operationCode, RiskLevel = CadRiskLevel.ModelMutation }],
        Preconditions = [new CadPlanPrecondition { Kind = CadPreconditionKind.ExpectedStateHash, ExpectedValue = "sha256:pre-state" }],
        Invariants = [new CadPlanInvariant { Code = "body.count", ExpectedValue = "1" }],
        Budget = new CadOperationBudget { MaxOperations = 1, MaxRetries = maxRetries, MaxProviderCalls = 16, Timeout = TimeSpan.FromSeconds(5) },
    };

    private static OperationResult<CadOperationExecution> Success(string stateHash, string operationCode = "part.create") => OperationResults.Success(
        new CadOperationExecution
        {
            OperationCode = operationCode,
            Changed = true,
            StateHash = stateHash,
            Observations = [new EvidenceObservation("state.hash", stateHash)],
        },
        "fake:operation",
        new OperationEvidence("fake-transaction"));

    private static OperationResult<CadOperationExecution> Failure(string code, bool retryable) => OperationResults.Failure<CadOperationExecution>(
        "fake:operation",
        new OperationError(code, "Synthetic operation result.", ErrorCategories.Execution, retryable: retryable));

    private sealed class RecordingExecutor(IEnumerable<OperationResult<CadOperationExecution>>? scripted = null) : ICadPlanOperationExecutor
    {
        private readonly ConcurrentQueue<OperationResult<CadOperationExecution>> script = new(scripted ?? []);

        public ConcurrentBag<CadPlannedOperation> Calls { get; } = [];

        public Task<OperationResult<CadOperationExecution>> ExecuteAsync(
            CadPlannedOperation operation,
            CadTransactionContext context,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(operation);
            OperationResult<CadOperationExecution> result = script.TryDequeue(out OperationResult<CadOperationExecution>? scriptedResult)
                ? scriptedResult
                : Success("sha256:post-state", operation.OperationCode);
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingVerifier(bool passed) : ICadPlanVerifier
    {
        public int Calls { get; private set; }

        public Task<OperationResult<CadVerificationResult>> VerifyAsync(
            CadTransactionPlan plan,
            CadTransactionContext context,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(
                OperationResults.Success(
                    new CadVerificationResult
                    {
                        Passed = passed,
                        Observations = [new EvidenceObservation("invariants.proved", passed.ToString())],
                    },
                    "fake:verify",
                    new OperationEvidence("fake-verifier")));
        }
    }
}
