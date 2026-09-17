using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using SolidWorksMcp.AutoDrawing;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Core;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Server;

/// <summary>Bounded input for one single-part drawing release execution.</summary>
/// <remarks>
/// The semantic QA input is produced before this service is called. The service still re-inspects the exact document
/// immediately before checkpoint and mutation, so a stale MCP plan cannot silently publish another drawing. 语义 QA
/// input 在调用本服务前生成；服务仍会在 checkpoint 和 mutation 前重新 inspection 精确 document，避免 stale plan
/// 静默发布另一张图。
/// </remarks>
internal sealed record DrawingReleaseExecutionRequest
{
    public required DrawingQaRequest PreflightQa { get; init; }

    public required DrawingArtifactPolicy ArtifactPolicy { get; init; }

    public required TransactionId TransactionId { get; init; }

    public required IdempotencyKey IdempotencyKey { get; init; }
}

/// <summary>Verified result returned by the governed drawing.release operation.</summary>
public sealed record DrawingReleaseExecutionResult
{
    /// <summary>Exact drawing identity released by the transaction.</summary>
    public required DocumentId DocumentId { get; init; }

    /// <summary>State hash after save, export and final QA inspection.</summary>
    public required string FinalStateHash { get; init; }

    /// <summary>Final complete QA plan, including artifact checksum findings.</summary>
    public required DrawingQaPlan QaPlan { get; init; }

    /// <summary>Manifest persisted beside the drawing artifact.</summary>
    public required DrawingReleaseEvidenceManifest Manifest { get; init; }

    /// <summary>Verified native drawing save receipt.</summary>
    public required SaveReceipt Save { get; init; }

    /// <summary>Verified neutral export receipts.</summary>
    public ImmutableArray<ExportReceipt> Exports { get; init; } = [];
}

/// <summary>
/// Orchestrates one drawing-release transaction without knowing any SOLIDWORKS COM type.
/// 在不知道任何 SOLIDWORKS COM 类型的前提下编排一次工程图发布事务。
/// </summary>
/// <remarks>
/// This class is deliberately a coordinator, not a God Service: semantic planners stay in AutoDrawing, the provider
/// owns COM/export policy, and Core owns transaction/checkpoint sequencing. 本类只做 coordinator，不成为 God Service：
/// semantic planner 仍在 AutoDrawing，Provider 仍拥有 COM/export policy，Core 仍拥有 transaction/checkpoint 顺序。
/// </remarks>
public sealed class DrawingReleaseService(
    ICadIdempotencyStore idempotencyStore,
    string? checkpointRoot = null)
{
    private static readonly JsonSerializerOptions ManifestJsonOptions = new() { WriteIndented = true };

    private readonly ICadIdempotencyStore idempotencyStore = idempotencyStore ?? throw new ArgumentNullException(nameof(idempotencyStore));
    private readonly string checkpointRoot = ResolveCheckpointRoot(checkpointRoot);

    /// <summary>Runs preflight, durable checkpoint, export, final QA and manifest persistence.</summary>
    internal async Task<OperationResult<DrawingReleaseExecutionResult>> ExecuteAsync(
        ICadSession session,
        DrawingReleaseExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        DrawingQaRequest preflightQa = request.PreflightQa;
        CadInspectionSnapshot plannedInspection = preflightQa.Drawing;
        DocumentId documentId = plannedInspection.Document.DocumentId;

        if (plannedInspection.Document.DocumentType is not CadDocumentType.Drawing)
        {
            return Failure(
                request.TransactionId.Value,
                new OperationError(
                    ErrorCodes.InvalidRequest,
                    "The drawing.release target must be a drawing document.",
                    ErrorCategories.Validation));
        }

        OperationResult<CadInspectionSnapshot> freshInspectionResult = await session.Inspection.InspectAsync(
            documentId,
            cancellationToken).ConfigureAwait(false);
        if (!freshInspectionResult.IsSuccess || freshInspectionResult.Value is null)
        {
            return Failure(request.TransactionId.Value, freshInspectionResult.Error!, freshInspectionResult.Evidence);
        }

        CadInspectionSnapshot freshInspection = freshInspectionResult.Value;
        if (!freshInspection.Document.StateHash.Equals(plannedInspection.Document.StateHash, StringComparison.Ordinal))
        {
            return Failure(
                request.TransactionId.Value,
                StateConflict(
                    "The drawing changed after semantic release planning and before the release preflight."),
                freshInspectionResult.Evidence);
        }

        OperationResult<ICadDrawingDocument> drawingResult = await session.GetDrawingAsync(
            documentId,
            cancellationToken).ConfigureAwait(false);
        if (!drawingResult.IsSuccess || drawingResult.Value is null)
        {
            return Failure(request.TransactionId.Value, drawingResult.Error!, drawingResult.Evidence);
        }

        ICadDrawingDocument drawing = drawingResult.Value;
        DrawingQaPlan preflightPlan = DrawingQaReleasePlanner.Analyze(preflightQa with { Drawing = freshInspection });
        if (!preflightPlan.CanRelease)
        {
            DrawingReleaseEvidenceManifest blockedManifest = DrawingQaReleasePlanner.CreateManifest(
                preflightQa with
                {
                    Drawing = freshInspection,
                    Artifacts = [],
                    IncludeArtifactFindings = true,
                },
                preflightPlan,
                DateTimeOffset.UtcNow);
            OperationResult<string> manifestWrite = await WriteManifestAsync(
                request.ArtifactPolicy.ManifestPath,
                blockedManifest,
                cancellationToken).ConfigureAwait(false);
            ImmutableArray<EvidenceObservation> observations =
            [
                new("release.status", "blocked-preflight"),
                new("release.qa-fingerprint", preflightPlan.Fingerprint),
                new("release.blocking-findings", string.Join(',', preflightPlan.Findings.Where(item => item.Status is not DrawingQaFindingStatus.Pass and not DrawingQaFindingStatus.Warning).Select(item => item.Code))),
                new("release.manifest.fingerprint", blockedManifest.Fingerprint),
            ];
            if (manifestWrite.IsSuccess)
            {
                observations = [.. observations, new EvidenceObservation("release.manifest.path", request.ArtifactPolicy.ManifestPath)];
            }
            else
            {
                observations = [.. observations, new EvidenceObservation("release.manifest.write", "failed")];
            }

            return OperationResults.Failure<DrawingReleaseExecutionResult>(
                request.TransactionId.Value,
                new OperationError(
                    ErrorCodes.ReviewRequired,
                    "The drawing release gate is blocked by unresolved QA findings.",
                    ErrorCategories.Policy,
                    remediation: "Resolve every blocking/review finding and create a new release plan."),
                new OperationEvidence("drawing.release", observations, manifestWrite.IsSuccess ? [request.ArtifactPolicy.ManifestPath] : []));
        }

        var state = new DrawingReleaseTransactionState();
        var executor = new DrawingReleaseOperationExecutor(session, drawing, request.ArtifactPolicy, state);
        var verifier = new DrawingReleasePlanVerifier(
            session,
            request.ArtifactPolicy,
            preflightQa,
            state);
        ICadLogicalRollbackHandler rollback = new DrawingReleaseLogicalRollbackHandler(session, documentId);
        var checkpoints = new FileCadCheckpointCoordinator(
            new FileCadCheckpointOptions
            {
                RootDirectory = checkpointRoot,
                ProviderVersion = session.GetType().Name,
            },
            rollback);
        var engine = new CadTransactionEngine(
            executor,
            verifier,
            idempotencyStore,
            checkpoints,
            session.GetType().Name);

        CadTransactionPlan transaction = new()
        {
            TransactionId = request.TransactionId,
            IdempotencyKey = request.IdempotencyKey,
            Target = new CadTransactionTarget
            {
                SessionId = session.SessionId,
                DocumentId = documentId,
                DocumentPath = freshInspection.Document.Path,
                DocumentType = CadDocumentType.Drawing,
                Configuration = freshInspection.Document.Configuration,
                ExpectedStateHash = freshInspection.Document.StateHash,
            },
            Operations =
            [
                new CadPlannedOperation
                {
                    OperationCode = "drawing.release",
                    RiskLevel = CadRiskLevel.DrawingRelease,
                    Arguments = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["documentId"] = documentId.Value,
                        ["artifact-count"] = request.ArtifactPolicy.Artifacts.Length.ToString(CultureInfo.InvariantCulture),
                    }.ToImmutableDictionary(StringComparer.Ordinal),
                },
            ],
            Preconditions =
            [
                new CadPlanPrecondition { Kind = CadPreconditionKind.SessionIdentity, ExpectedValue = session.SessionId.Value },
                new CadPlanPrecondition { Kind = CadPreconditionKind.DocumentIdentity, ExpectedValue = documentId.Value },
                new CadPlanPrecondition { Kind = CadPreconditionKind.ExpectedStateHash, ExpectedValue = freshInspection.Document.StateHash },
                new CadPlanPrecondition { Kind = CadPreconditionKind.Capability, ExpectedValue = CadCapabilityNames.DrawingMutation },
                new CadPlanPrecondition { Kind = CadPreconditionKind.PathAllowlist, ExpectedValue = "provider-owned" },
            ],
            Invariants =
            [
                new CadPlanInvariant { Code = "drawing.qa.release", ExpectedValue = "true" },
                new CadPlanInvariant { Code = "drawing.artifacts.verified", ExpectedValue = "true" },
            ],
            Budget = new CadOperationBudget
            {
                MaxOperations = 1,
                MaxRetries = 0,
                Timeout = TimeSpan.FromMinutes(3),
                MaxProviderCalls = 32,
            },
            VerificationStrategy = "provider.inspect",
        };

        OperationResult<CadTransactionReceipt> committed = await engine.ExecuteAsync(transaction, cancellationToken).ConfigureAwait(false);
        if (!committed.IsSuccess || committed.Value is null)
        {
            return Failure(request.TransactionId.Value, committed.Error!, committed.Evidence);
        }

        if (state.Save is null || state.FinalPlan is null || state.Manifest is null)
        {
            return Failure(
                request.TransactionId.Value,
                new OperationError(
                    ErrorCodes.InvariantViolation,
                    "The drawing release transaction committed without complete save, QA and manifest evidence.",
                    ErrorCategories.Invariant),
                committed.Evidence);
        }

        var output = new DrawingReleaseExecutionResult
        {
            DocumentId = documentId,
            FinalStateHash = state.FinalPlan.DocumentStateHash,
            QaPlan = state.FinalPlan,
            Manifest = state.Manifest,
            Save = state.Save,
            Exports = state.Exports,
        };
        ImmutableArray<string> artifactPaths = [request.ArtifactPolicy.ManifestPath, .. state.Manifest.Artifacts.Select(item => item.TargetPath)];
        return OperationResults.Success(
            output,
            request.TransactionId.Value,
            new OperationEvidence(
                "drawing.release",
                [
                    new EvidenceObservation("release.status", "committed"),
                    new EvidenceObservation("release.transaction.id", committed.Value.TransactionId.Value),
                    new EvidenceObservation("release.idempotency.key", committed.Value.IdempotencyKey.Value),
                    new EvidenceObservation("release.qa-fingerprint", state.FinalPlan.Fingerprint),
                    new EvidenceObservation("release.manifest.fingerprint", state.Manifest.Fingerprint),
                    new EvidenceObservation("release.persistence", "save-export-inspect-manifest-verified"),
                ],
                artifactPaths,
                state.FinalPlan.DocumentStateHash));
    }

    private static string ResolveCheckpointRoot(string? configuredRoot)
    {
        string root = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SolidWorksMcp",
                "checkpoints")
            : configuredRoot.Trim();
        return Path.GetFullPath(root);
    }

    private static OperationResult<DrawingReleaseExecutionResult> Failure(
        string operationId,
        OperationError error,
        OperationEvidence? evidence = null) =>
        OperationResults.Failure<DrawingReleaseExecutionResult>(operationId, error, evidence);

    private static OperationError StateConflict(string message) => new(
        ErrorCodes.StateConflict,
        message,
        ErrorCategories.State,
        remediation: "Inspect the exact drawing again and create a new release plan.");

    private static async Task<OperationResult<string>> WriteManifestAsync(
        string manifestPath,
        DrawingReleaseEvidenceManifest manifest,
        CancellationToken cancellationToken)
    {
        try
        {
            string fullPath = Path.GetFullPath(manifestPath);
            string? directory = Path.GetDirectoryName(fullPath);
            if (directory is null)
            {
                return OperationResults.Failure<string>(
                    "drawing.release.manifest",
                    new OperationError(ErrorCodes.PathNotAllowed, "The release manifest has no parent directory.", ErrorCategories.Policy));
            }

            Directory.CreateDirectory(directory);
            string temporaryPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
            string json = JsonSerializer.Serialize(manifest, ManifestJsonOptions);
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, fullPath, overwrite: true);
            return OperationResults.Success(
                fullPath,
                "drawing.release.manifest",
                new OperationEvidence("drawing.release.manifest", [new EvidenceObservation("manifest.path", fullPath)]));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException exception)
        {
            return OperationResults.Failure<string>(
                "drawing.release.manifest",
                new OperationError(
                    ErrorCodes.ProviderFailure,
                    "The release evidence manifest could not be written.",
                    ErrorCategories.Provider,
                    details: new Dictionary<string, string>(StringComparer.Ordinal) { ["exception.type"] = exception.GetType().Name }));
        }
        catch (UnauthorizedAccessException)
        {
            return OperationResults.Failure<string>(
                "drawing.release.manifest",
                new OperationError(ErrorCodes.PathNotAllowed, "The release evidence manifest path is not writable.", ErrorCategories.Policy));
        }
    }

    private sealed class DrawingReleaseTransactionState
    {
        public SaveReceipt? Save { get; set; }

        public ImmutableArray<ExportReceipt> Exports { get; set; } = [];

        public DrawingQaPlan? FinalPlan { get; set; }

        public DrawingReleaseEvidenceManifest? Manifest { get; set; }
    }

    private sealed class DrawingReleaseOperationExecutor(
        ICadSession session,
        ICadDrawingDocument drawing,
        DrawingArtifactPolicy artifactPolicy,
        DrawingReleaseTransactionState state) : ICadPlanOperationExecutor
    {
        public async Task<OperationResult<CadOperationExecution>> ExecuteAsync(
            CadPlannedOperation operation,
            CadTransactionContext context,
            CancellationToken cancellationToken = default)
        {
            if (!operation.OperationCode.Equals("drawing.release", StringComparison.Ordinal))
            {
                return OperationResults.Failure<CadOperationExecution>(
                    "drawing.release.transaction",
                    new OperationError(ErrorCodes.InvalidRequest, "The release executor received an unexpected operation code.", ErrorCategories.Validation));
            }

            OperationResult<CadInspectionSnapshot> before = await session.Inspection.InspectAsync(
                context.Plan.Target.DocumentId!.Value,
                cancellationToken).ConfigureAwait(false);
            if (!before.IsSuccess || before.Value is null)
            {
                return OperationResults.Failure<CadOperationExecution>("drawing.release.execute", before.Error!, before.Evidence);
            }

            if (!before.Value.Document.StateHash.Equals(context.Plan.Target.ExpectedStateHash, StringComparison.Ordinal))
            {
                return OperationResults.Failure<CadOperationExecution>("drawing.release.execute", StateConflict("The drawing state changed before release mutation."), before.Evidence);
            }

            OperationResult<SaveReceipt> saved = await drawing.SaveAsync(cancellationToken).ConfigureAwait(false);
            if (!saved.IsSuccess || saved.Value is null)
            {
                return OperationResults.Failure<CadOperationExecution>("drawing.release.execute", saved.Error!, saved.Evidence);
            }

            state.Save = saved.Value;
            var exports = ImmutableArray.CreateBuilder<ExportReceipt>();
            foreach (DrawingArtifactRequest artifact in artifactPolicy.Artifacts)
            {
                if (artifact.Format.Equals("SLDDRW", StringComparison.Ordinal))
                {
                    if (!Path.GetFullPath(saved.Value.Path).Equals(Path.GetFullPath(artifact.TargetPath), StringComparison.OrdinalIgnoreCase))
                    {
                        return OperationResults.Failure<CadOperationExecution>(
                            "drawing.release.execute",
                            new OperationError(
                                ErrorCodes.PathNotAllowed,
                                "The native drawing artifact target must equal the exact persisted drawing path.",
                                ErrorCategories.Policy));
                    }

                    if (!File.Exists(saved.Value.Path))
                    {
                        return OperationResults.Failure<CadOperationExecution>(
                            "drawing.release.execute",
                            new OperationError(ErrorCodes.InvariantViolation, "The saved drawing artifact was not observed on disk.", ErrorCategories.Invariant));
                    }

                    continue;
                }

                OperationResult<ExportReceipt> exported = await session.Export.ExportAsync(
                    context.Plan.Target.DocumentId.Value,
                    new CadExportRequest
                    {
                        Format = artifact.Format,
                        TargetPath = artifact.TargetPath,
                        AllowOverwrite = artifact.AllowOverwrite,
                    },
                    cancellationToken).ConfigureAwait(false);
                if (!exported.IsSuccess || exported.Value is null)
                {
                    return OperationResults.Failure<CadOperationExecution>("drawing.release.execute", exported.Error!, exported.Evidence);
                }

                exports.Add(exported.Value);
            }

            state.Exports = exports.ToImmutable();
            return OperationResults.Success(
                new CadOperationExecution
                {
                    OperationCode = operation.OperationCode,
                    Changed = true,
                    StateHash = saved.Value.StateHash,
                },
                "drawing.release.execute",
                new OperationEvidence(
                    "drawing.release.transaction",
                    [
                        new EvidenceObservation("release.save", "verified"),
                        new EvidenceObservation("release.export.count", exports.Count.ToString(CultureInfo.InvariantCulture)),
                        new EvidenceObservation("release.state-hash.after-save", saved.Value.StateHash),
                    ],
                    [saved.Value.Path, .. exports.Select(item => item.TargetPath)],
                    saved.Value.StateHash));
        }
    }

    private sealed class DrawingReleasePlanVerifier(
        ICadSession session,
        DrawingArtifactPolicy artifactPolicy,
        DrawingQaRequest preflightQa,
        DrawingReleaseTransactionState state) : ICadPlanVerifier
    {
        public async Task<OperationResult<CadVerificationResult>> VerifyAsync(
            CadTransactionPlan plan,
            CadTransactionContext context,
            CancellationToken cancellationToken = default)
        {
            OperationResult<CadInspectionSnapshot> inspected = await session.Inspection.InspectAsync(
                plan.Target.DocumentId!.Value,
                cancellationToken).ConfigureAwait(false);
            if (!inspected.IsSuccess || inspected.Value is null)
            {
                return OperationResults.Failure<CadVerificationResult>("drawing.release.verify", inspected.Error!, inspected.Evidence);
            }

            CadInspectionSnapshot finalDrawing = inspected.Value;
            if (finalDrawing.HasErrors)
            {
                return OperationResults.Failure<CadVerificationResult>(
                    "drawing.release.verify",
                    new OperationError(ErrorCodes.InvariantViolation, "The final drawing inspection contains rebuild errors.", ErrorCategories.Invariant),
                    inspected.Evidence);
            }

            ImmutableArray<DrawingArtifactProof>.Builder artifacts = ImmutableArray.CreateBuilder<DrawingArtifactProof>();
            foreach (DrawingArtifactRequest artifact in artifactPolicy.Artifacts)
            {
                string target = artifact.Format.Equals("SLDDRW", StringComparison.Ordinal)
                    ? finalDrawing.Document.Path
                    : artifact.TargetPath;
                artifacts.Add(DrawingQaReleasePlanner.VerifyArtifact(artifact.Format, target, finalDrawing.Document.StateHash));
            }

            DrawingQaRequest finalRequest = preflightQa with
            {
                Drawing = finalDrawing,
                ExpectedDocumentStateHash = null,
                Artifacts = artifacts.ToImmutable(),
                IncludeArtifactFindings = true,
            };
            DrawingQaPlan finalPlan = DrawingQaReleasePlanner.Analyze(finalRequest);
            DrawingReleaseEvidenceManifest manifest = DrawingQaReleasePlanner.CreateManifest(
                finalRequest,
                finalPlan,
                DateTimeOffset.UtcNow);
            OperationResult<string> manifestWrite = await WriteManifestAsync(
                artifactPolicy.ManifestPath,
                manifest,
                cancellationToken).ConfigureAwait(false);

            state.FinalPlan = finalPlan;
            state.Manifest = manifest;
            if (!finalPlan.CanRelease)
            {
                return OperationResults.Failure<CadVerificationResult>(
                    "drawing.release.verify",
                    new OperationError(
                        ErrorCodes.ReviewRequired,
                        "The final drawing QA release gate did not pass.",
                        ErrorCategories.Policy,
                        remediation: "Inspect the persisted drawing and resolve every final QA finding."),
                    new OperationEvidence("drawing.release.verify", [
                        new EvidenceObservation("release.qa-fingerprint", finalPlan.Fingerprint),
                        new EvidenceObservation("release.blocking-findings", string.Join(',', finalPlan.Findings.Where(item => item.Status is not (DrawingQaFindingStatus.Pass or DrawingQaFindingStatus.Warning)).Select(item => item.Code))),
                    ]));
            }

            if (!manifestWrite.IsSuccess)
            {
                return OperationResults.Failure<CadVerificationResult>("drawing.release.verify", manifestWrite.Error!, manifestWrite.Evidence);
            }

            return OperationResults.Success(
                new CadVerificationResult
                {
                    Passed = true,
                    Observations =
                    [
                        new EvidenceObservation("drawing.qa.release", bool.TrueString),
                        new EvidenceObservation("drawing.artifacts.verified", bool.TrueString),
                        new EvidenceObservation("drawing.final-state-hash", finalDrawing.Document.StateHash),
                        new EvidenceObservation("drawing.manifest.fingerprint", manifest.Fingerprint),
                    ],
                },
                "drawing.release.verify",
                new OperationEvidence("drawing.release.verify", [
                    new EvidenceObservation("drawing.qa.release", bool.TrueString),
                    new EvidenceObservation("drawing.manifest.path", artifactPolicy.ManifestPath),
                ], [artifactPolicy.ManifestPath], finalDrawing.Document.StateHash));
        }
    }

    private sealed class DrawingReleaseLogicalRollbackHandler(ICadSession session, DocumentId documentId) : ICadLogicalRollbackHandler
    {
        public async Task<OperationResult<CadLogicalRollbackAttempt>> TryRollbackAsync(
            CadCheckpoint checkpoint,
            CadTransactionContext context,
            CancellationToken cancellationToken = default)
        {
            OperationResult<CadInspectionSnapshot> inspection = await session.Inspection.InspectAsync(
                documentId,
                cancellationToken).ConfigureAwait(false);
            if (!inspection.IsSuccess || inspection.Value is null)
            {
                return OperationResults.Failure<CadLogicalRollbackAttempt>(
                    "drawing.release.rollback",
                    inspection.Error!,
                    inspection.Evidence);
            }

            // Release operations are required to be source-state preserving. If a failure occurs, the provider
            // inspection must prove that identity before the checkpoint is considered sufficient. 发布操作必须保持
            // source CAD state；失败后先由 provider inspection 证明 identity 未变，才允许 checkpoint recovery 结束。
            bool sameState = !string.IsNullOrWhiteSpace(checkpoint.Manifest.SourceStateHash)
                && inspection.Value.Document.StateHash.Equals(checkpoint.Manifest.SourceStateHash, StringComparison.Ordinal);
            var attempt = new CadLogicalRollbackAttempt
            {
                Applied = sameState,
                PreStateVerified = sameState,
                RestoredStateHash = inspection.Value.Document.StateHash,
            };
            return OperationResults.Success(
                attempt,
                "drawing.release.rollback",
                new OperationEvidence("drawing.release.rollback", [
                    new EvidenceObservation("rollback.logical-reversal", "source-state-inspection"),
                    new EvidenceObservation("rollback.pre-state-verified", sameState.ToString()),
                    new EvidenceObservation("rollback.state-hash", inspection.Value.Document.StateHash),
                ], stateHash: inspection.Value.Document.StateHash));
        }
    }
}
