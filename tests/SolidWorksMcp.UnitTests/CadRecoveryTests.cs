using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Core;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.UnitTests;

/// <summary>
/// Proves the F02 recovery boundary with injected failures at checkpoint, operation and verification steps.
/// 通过在 checkpoint、operation、verification 三个步骤注入失败，证明 F02 的恢复边界。
/// </summary>
public sealed class CadRecoveryTests
{
    /// <summary>A missing checkpoint blocks a model mutation before the operation handler is reached.</summary>
    [Fact]
    public async Task CheckpointFailureBlocksHighRiskMutation()
    {
        var executor = new RecordingExecutor();
        var engine = new CadTransactionEngine(executor, new PassingVerifier());

        OperationResult<CadTransactionReceipt> result = await engine.ExecuteAsync(CreatePlan(CadRiskLevel.ModelMutation));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.CheckpointFailed, result.Error!.Code);
        Assert.Empty(executor.Calls);
    }

    /// <summary>An operation failure attempts recovery and preserves the original failure only after verified restore.</summary>
    [Fact]
    public async Task OperationFailureRecoversAndPreservesOriginalErrorWhenPreStateIsVerified()
    {
        var executor = new RecordingExecutor(
            OperationResults.Failure<CadOperationExecution>(
                "fake:operation",
                new OperationError(ErrorCodes.ProviderFailure, "Synthetic provider failure.", ErrorCategories.Provider)));
        var coordinator = new RecordingCheckpointCoordinator(recoveryPreStateVerified: true);
        var engine = new CadTransactionEngine(executor, new PassingVerifier(), suppliedCheckpointCoordinator: coordinator);

        OperationResult<CadTransactionReceipt> result = await engine.ExecuteAsync(CreatePlan(CadRiskLevel.ModelMutation));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.ProviderFailure, result.Error!.Code);
        Assert.Equal(1, coordinator.CreateCalls);
        Assert.Equal(1, coordinator.RecoverCalls);
        Assert.Contains(result.Evidence!.Observations, observation => observation.Key == "recovery.pre_state_verified" && observation.Value == "True");
    }

    /// <summary>An unverified recovery is itself blocking and cannot be reported as a successful rollback.</summary>
    [Fact]
    public async Task UnverifiedRecoveryReturnsRollbackFailed()
    {
        var executor = new RecordingExecutor(
            OperationResults.Failure<CadOperationExecution>(
                "fake:operation",
                new OperationError(ErrorCodes.ProviderFailure, "Synthetic provider failure.", ErrorCategories.Provider)));
        var coordinator = new RecordingCheckpointCoordinator(recoveryPreStateVerified: false);
        var engine = new CadTransactionEngine(executor, new PassingVerifier(), suppliedCheckpointCoordinator: coordinator);

        OperationResult<CadTransactionReceipt> result = await engine.ExecuteAsync(CreatePlan(CadRiskLevel.AssemblyMutation));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.RollbackFailed, result.Error!.Code);
        Assert.Equal("False", result.Error.Details["recovery.pre_state_verified"]);
        Assert.Contains(result.Evidence!.Observations, observation => observation.Key == "recovery.status" && observation.Value == "Unrecovered");
    }

    /// <summary>Invariant failure also enters the same recovery path, proving recovery is not limited to provider errors.</summary>
    [Fact]
    public async Task VerificationFailureRecoversBeforeReturningInvariantError()
    {
        var coordinator = new RecordingCheckpointCoordinator(recoveryPreStateVerified: true);
        var engine = new CadTransactionEngine(
            new RecordingExecutor(),
            new FailingVerifier(),
            suppliedCheckpointCoordinator: coordinator);

        OperationResult<CadTransactionReceipt> result = await engine.ExecuteAsync(CreatePlan(CadRiskLevel.DrawingMutation));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.InvariantViolation, result.Error!.Code);
        Assert.Equal(1, coordinator.RecoverCalls);
    }

    /// <summary>Read-only plans do not require a checkpoint and remain usable with the fail-closed default.</summary>
    [Fact]
    public async Task ReadOnlyPlanDoesNotRequireCheckpoint()
    {
        var engine = new CadTransactionEngine(new RecordingExecutor(), new PassingVerifier());

        OperationResult<CadTransactionReceipt> result = await engine.ExecuteAsync(CreatePlan(CadRiskLevel.Read));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.DoesNotContain(result.Evidence!.Observations, observation => observation.Key == "checkpoint.id");
    }

    /// <summary>
    /// Persists a manifest and byte snapshot, then demonstrates that file equality alone is not CAD-state proof.
    /// 持久化 manifest 与字节 snapshot，并证明文件一致性本身不能冒充 CAD state proof。
    /// </summary>
    [Fact]
    public async Task FileCoordinatorPersistsManifestAndKeepsCadStateProofFailClosed()
    {
        string workspace = Directory.CreateTempSubdirectory("solidworks-mcp-f02-").FullName;
        string sourcePath = Path.Combine(workspace, "source.sldprt");
        string checkpointRoot = Path.Combine(workspace, "checkpoints");
        File.WriteAllBytes(sourcePath, [1, 2, 3, 5, 8, 13]);

        try
        {
            var coordinator = new FileCadCheckpointCoordinator(
                new FileCadCheckpointOptions
                {
                    RootDirectory = checkpointRoot,
                    ProviderVersion = "solidworks-test-provider",
                });
            var plan = CreatePlan(CadRiskLevel.ModelMutation) with
            {
                Target = CreatePlan(CadRiskLevel.ModelMutation).Target with { DocumentPath = sourcePath },
            };
            var request = CadCheckpointRequest.FromPlan(
                plan,
                "solidworks-test-provider",
                DateTimeOffset.UtcNow.AddHours(1));

            OperationResult<CadCheckpoint> checkpointResult = await coordinator.CreateAsync(request);

            Assert.True(checkpointResult.IsSuccess, checkpointResult.Error?.Message);
            CadCheckpoint checkpoint = checkpointResult.Value!;
            Assert.True(File.Exists(checkpoint.Manifest.ManifestPath));
            Assert.True(File.Exists(checkpoint.Manifest.SnapshotPath));
            Assert.Contains("SourceFileHash", File.ReadAllText(checkpoint.Manifest.ManifestPath), StringComparison.Ordinal);

            File.WriteAllBytes(sourcePath, [42]);
            OperationResult<CadRecoveryResult> recovery = await coordinator.RecoverAsync(
                checkpoint,
                new CadTransactionContext { Plan = plan, Attempt = 1, ProviderCallsUsed = 0 });

            Assert.True(recovery.IsSuccess);
            Assert.Equal(CadRecoveryStatus.CheckpointRestored, recovery.Value!.Status);
            Assert.False(recovery.Value.PreStateVerified);
            Assert.Equal("file-bytes-only", recovery.Value.VerificationScope);
            Assert.Equal([1, 2, 3, 5, 8, 13], File.ReadAllBytes(sourcePath));
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    /// <summary>Expired cleanup removes only the coordinator's own checkpoint directory.</summary>
    [Fact]
    public async Task RetentionPrunesExpiredCheckpoint()
    {
        string workspace = Directory.CreateTempSubdirectory("solidworks-mcp-f02-retention-").FullName;
        string sourcePath = Path.Combine(workspace, "source.sldprt");
        string checkpointRoot = Path.Combine(workspace, "checkpoints");
        File.WriteAllBytes(sourcePath, [7, 11]);

        try
        {
            var coordinator = new FileCadCheckpointCoordinator(new FileCadCheckpointOptions { RootDirectory = checkpointRoot });
            CadTransactionPlan plan = CreatePlan(CadRiskLevel.ModelMutation) with
            {
                Target = CreatePlan(CadRiskLevel.ModelMutation).Target with { DocumentPath = sourcePath },
            };
            OperationResult<CadCheckpoint> checkpoint = await coordinator.CreateAsync(
                CadCheckpointRequest.FromPlan(plan, "test", DateTimeOffset.UtcNow.AddMinutes(-1)));

            Assert.True(checkpoint.IsSuccess, checkpoint.Error?.Message);
            OperationResult<CadCheckpointRetentionReceipt> retention = await coordinator.PruneExpiredAsync(DateTimeOffset.UtcNow);

            Assert.True(retention.IsSuccess, retention.Error?.Message);
            Assert.Equal(1, retention.Value!.Scanned);
            Assert.True(
                retention.Value.Failed == 0,
                string.Join(", ", retention.Evidence!.Observations.Select(observation => $"{observation.Key}={observation.Value}")));
            Assert.Equal(1, retention.Value.Removed);
            Assert.False(Directory.Exists(Path.GetDirectoryName(checkpoint.Value!.Manifest.ManifestPath)!));
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    private static CadTransactionPlan CreatePlan(CadRiskLevel risk) => new()
    {
        TransactionId = new TransactionId($"f02-transaction-{Guid.NewGuid():N}"),
        IdempotencyKey = new IdempotencyKey($"f02-idempotency-{Guid.NewGuid():N}"),
        Target = new CadTransactionTarget
        {
            SessionId = new SessionId("fake-session-f02"),
            DocumentId = new DocumentId("fake-document-f02"),
            DocumentType = CadDocumentType.Part,
            Configuration = "Default",
            ExpectedStateHash = risk >= CadRiskLevel.ModelMutation ? "sha256:f02-pre-state" : null,
        },
        Operations =
        [
            new CadPlannedOperation
            {
                OperationCode = risk >= CadRiskLevel.DrawingMutation ? "drawing.mutate" : "part.mutate",
                RiskLevel = risk,
            },
        ],
        Preconditions = risk >= CadRiskLevel.ModelMutation
            ? [new CadPlanPrecondition { Kind = CadPreconditionKind.ExpectedStateHash, ExpectedValue = "sha256:f02-pre-state" }]
            : [],
        Invariants = [new CadPlanInvariant { Code = "state.hash", ExpectedValue = "sha256:f02-post-state" }],
        Budget = new CadOperationBudget { MaxOperations = 1, MaxRetries = 0, MaxProviderCalls = 4, Timeout = TimeSpan.FromSeconds(5) },
    };

    private sealed class RecordingExecutor(params OperationResult<CadOperationExecution>[] results) : ICadPlanOperationExecutor
    {
        private readonly Queue<OperationResult<CadOperationExecution>> scripted = new(results);

        public List<CadPlannedOperation> Calls { get; } = [];

        public Task<OperationResult<CadOperationExecution>> ExecuteAsync(
            CadPlannedOperation operation,
            CadTransactionContext context,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(operation);
            if (scripted.TryDequeue(out OperationResult<CadOperationExecution>? result))
            {
                return Task.FromResult(result);
            }

            return Task.FromResult(
                OperationResults.Success(
                    new CadOperationExecution { OperationCode = operation.OperationCode, Changed = false, StateHash = "sha256:f02-post-state" },
                    "fake:operation",
                    new OperationEvidence("fake-executor")));
        }
    }

    private sealed class PassingVerifier : ICadPlanVerifier
    {
        public Task<OperationResult<CadVerificationResult>> VerifyAsync(
            CadTransactionPlan plan,
            CadTransactionContext context,
            CancellationToken cancellationToken = default) => Task.FromResult(
                OperationResults.Success(
                    new CadVerificationResult { Passed = true },
                    "fake:verify",
                    new OperationEvidence("fake-verifier")));
    }

    private sealed class FailingVerifier : ICadPlanVerifier
    {
        public Task<OperationResult<CadVerificationResult>> VerifyAsync(
            CadTransactionPlan plan,
            CadTransactionContext context,
            CancellationToken cancellationToken = default) => Task.FromResult(
                OperationResults.Success(
                    new CadVerificationResult { Passed = false },
                    "fake:verify",
                    new OperationEvidence("fake-verifier")));
    }

    private sealed class RecordingCheckpointCoordinator(bool recoveryPreStateVerified) : ICadCheckpointCoordinator
    {
        private readonly bool recoveryPreStateVerified = recoveryPreStateVerified;
        private CadCheckpoint? checkpoint;

        public int CreateCalls { get; private set; }

        public int RecoverCalls { get; private set; }

        public Task<OperationResult<CadCheckpoint>> CreateAsync(
            CadCheckpointRequest request,
            CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            string checkpointId = $"f02-checkpoint-{Guid.NewGuid():N}";
            var manifest = new CadCheckpointManifest
            {
                CheckpointId = checkpointId,
                TransactionId = request.TransactionId,
                IdempotencyKey = request.IdempotencyKey,
                SessionId = request.SessionId,
                DocumentId = request.DocumentId,
                SourceStateHash = request.SourceStateHash,
                ProviderVersion = request.ProviderVersion,
                IntendedOperationCodes = request.IntendedOperationCodes,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                RetainUntilUtc = request.RetainUntilUtc,
                ManifestPath = $"memory://{checkpointId}/manifest.json",
            };
            checkpoint = new CadCheckpoint { Manifest = manifest };
            return Task.FromResult(
                OperationResults.Success(
                    checkpoint,
                    $"checkpoint:{request.TransactionId.Value}",
                    new OperationEvidence(
                        "f02-test-coordinator",
                        [new EvidenceObservation("checkpoint.status", "created")])
                )
            );
        }

        public Task<OperationResult<CadRecoveryResult>> RecoverAsync(
            CadCheckpoint suppliedCheckpoint,
            CadTransactionContext context,
            CancellationToken cancellationToken = default)
        {
            RecoverCalls++;
            bool sameCheckpoint = checkpoint?.Manifest.CheckpointId == suppliedCheckpoint.Manifest.CheckpointId;
            var recovery = new CadRecoveryResult
            {
                Status = sameCheckpoint && recoveryPreStateVerified ? CadRecoveryStatus.CheckpointRestored : CadRecoveryStatus.Unrecovered,
                PreStateVerified = sameCheckpoint && recoveryPreStateVerified,
                RestoredStateHash = sameCheckpoint ? suppliedCheckpoint.Manifest.SourceStateHash : null,
                VerificationScope = "f02-test-double",
                Observations =
                [
                    new EvidenceObservation("recovery.status", sameCheckpoint && recoveryPreStateVerified ? "CheckpointRestored" : "Unrecovered"),
                ],
            };
            return Task.FromResult(
                OperationResults.Success(
                    recovery,
                    $"rollback:{context.Plan.TransactionId.Value}",
                    new OperationEvidence("f02-test-coordinator", recovery.Observations)));
        }
    }
}
