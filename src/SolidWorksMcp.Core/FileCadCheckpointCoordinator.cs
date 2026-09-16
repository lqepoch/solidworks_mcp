using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Core;

/// <summary>Options for the durable file checkpoint coordinator.</summary>
/// <remarks>
/// The root is an operator-selected evidence directory, not a repository path.  The coordinator creates only
/// GUID-named child directories below that root.  根目录由部署者显式选择，不是仓库目录；协调器只在该根目录下创建
/// GUID 子目录，从而把 checkpoint artifact 与源工程、源代码隔离。
/// </remarks>
public sealed record FileCadCheckpointOptions
{
    /// <summary>Absolute or resolvable local directory used exclusively for checkpoint artifacts.</summary>
    public required string RootDirectory { get; init; }

    /// <summary>Default retention applied when a request does not specify a deadline.</summary>
    public TimeSpan DefaultRetention { get; init; } = CadCheckpointPolicy.DefaultRetention;

    /// <summary>Maximum source size accepted by the generic file-copy implementation.</summary>
    public long MaxSourceBytes { get; init; } = 2L * 1024 * 1024 * 1024;

    /// <summary>Provider revision placed in each manifest.</summary>
    public string ProviderVersion { get; init; } = "unknown";
}

/// <summary>Summary returned by bounded checkpoint retention cleanup.</summary>
public sealed record CadCheckpointRetentionReceipt
{
    /// <summary>Number of checkpoint directories inspected.</summary>
    public required int Scanned { get; init; }

    /// <summary>Number of expired checkpoint directories removed.</summary>
    public required int Removed { get; init; }

    /// <summary>Number of directories that could not be inspected or removed.</summary>
    public required int Failed { get; init; }
}

/// <summary>
/// Durable generic file checkpoint implementation for local diagnostics and future provider composition.
/// 面向本地诊断和后续 Provider composition 的持久化通用文件 checkpoint 实现。
/// </summary>
/// <remarks>
/// This component proves copied file bytes only.  It intentionally does not claim that an open SOLIDWORKS document's
/// opaque model state was restored; that claim requires a provider inspection callback and remains a separate seam.
/// 此组件只证明复制后的文件字节一致；它刻意不声称已恢复打开中的 SOLIDWORKS opaque model state，这一结论必须由
/// Provider inspection callback 证明，并保持为独立 seam。
/// </remarks>
public sealed class FileCadCheckpointCoordinator : ICadCheckpointCoordinator
{
    private static readonly JsonSerializerOptions ManifestJsonOptions = new() { WriteIndented = true };

    private readonly string rootDirectory;
    private readonly TimeSpan defaultRetention;
    private readonly long maxSourceBytes;
    private readonly string providerVersion;
    private readonly TimeProvider timeProvider;
    private readonly ICadLogicalRollbackHandler? logicalRollback;

    /// <summary>Creates a coordinator that owns only its configured checkpoint root.</summary>
    public FileCadCheckpointCoordinator(
        FileCadCheckpointOptions options,
        ICadLogicalRollbackHandler? logicalRollback = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RootDirectory);
        if (options.DefaultRetention <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Checkpoint retention must be positive.");
        }

        if (options.MaxSourceBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "The checkpoint source-size limit must be positive.");
        }

        rootDirectory = Path.GetFullPath(options.RootDirectory.Trim());
        defaultRetention = options.DefaultRetention;
        maxSourceBytes = options.MaxSourceBytes;
        providerVersion = string.IsNullOrWhiteSpace(options.ProviderVersion) ? "unknown" : options.ProviderVersion.Trim();
        this.logicalRollback = logicalRollback;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<OperationResult<CadCheckpoint>> CreateAsync(
        CadCheckpointRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        string checkpointId = $"file-{Guid.NewGuid():N}";
        string checkpointDirectory = Path.Combine(rootDirectory, checkpointId);
        string manifestPath = Path.Combine(checkpointDirectory, "manifest.json");
        DateTimeOffset createdAtUtc = timeProvider.GetUtcNow();
        var observations = ImmutableArray.CreateBuilder<EvidenceObservation>();

        try
        {
            Directory.CreateDirectory(rootDirectory);
            Directory.CreateDirectory(checkpointDirectory);

            if (string.IsNullOrWhiteSpace(request.SourceDocumentPath))
            {
                // A manifest without a source snapshot cannot protect an existing document.  Fail closed instead of
                // presenting metadata as a rollback capability; unsaved/new documents need a provider logical undo.
                // 没有 source snapshot 的 manifest 不能保护现有文档。这里直接 fail closed；未保存/新建文档必须由
                // Provider 提供 logical undo，不能把 metadata 冒充 rollback 能力。
                return await CheckpointFailureAsync(
                    request,
                    checkpointDirectory,
                    "A persisted source document path is required for a durable file checkpoint.",
                    "Provide a provider-backed checkpoint for unsaved or newly created documents.").ConfigureAwait(false);
            }

            string sourcePath = Path.GetFullPath(request.SourceDocumentPath.Trim());
            if (IsWithinRoot(sourcePath) || !File.Exists(sourcePath))
            {
                return await CheckpointFailureAsync(
                    request,
                    checkpointDirectory,
                    "The checkpoint source path is missing or resolves inside the checkpoint root.",
                    "Verify the persisted document path and the checkpoint root before retrying.").ConfigureAwait(false);
            }

            FileInfo sourceInfo = new(sourcePath);
            if (sourceInfo.Length > maxSourceBytes)
            {
                return await CheckpointFailureAsync(
                    request,
                    checkpointDirectory,
                    "The source document exceeds the checkpoint size limit.",
                    "Increase the explicit finite limit or use a provider-native checkpoint strategy.").ConfigureAwait(false);
            }

            string snapshotPath = Path.Combine(checkpointDirectory, "source.snapshot");
            await CopyFileAsync(sourcePath, snapshotPath, cancellationToken).ConfigureAwait(false);
            string sourceFileHash = await ComputeSha256Async(snapshotPath, cancellationToken).ConfigureAwait(false);
            DateTimeOffset retainUntilUtc = request.RetainUntilUtc == default
                ? createdAtUtc.Add(defaultRetention)
                : request.RetainUntilUtc;
            var manifest = new CadCheckpointManifest
            {
                CheckpointId = checkpointId,
                TransactionId = request.TransactionId,
                IdempotencyKey = request.IdempotencyKey,
                SessionId = request.SessionId,
                DocumentId = request.DocumentId,
                SourceDocumentPath = sourcePath,
                SourceStateHash = request.SourceStateHash,
                SourceFileHash = sourceFileHash,
                ProviderVersion = string.IsNullOrWhiteSpace(request.ProviderVersion) ? providerVersion : request.ProviderVersion.Trim(),
                IntendedOperationCodes = request.IntendedOperationCodes,
                CreatedAtUtc = createdAtUtc,
                RetainUntilUtc = retainUntilUtc,
                ManifestPath = manifestPath,
                SnapshotPath = snapshotPath,
            };
            await WriteManifestAtomicallyAsync(manifestPath, manifest, cancellationToken).ConfigureAwait(false);

            observations.Add(new EvidenceObservation("checkpoint.status", "created"));
            observations.Add(new EvidenceObservation("checkpoint.id", checkpointId));
            observations.Add(new EvidenceObservation("checkpoint.source_file_hash", sourceFileHash));
            observations.Add(new EvidenceObservation("checkpoint.retention_until_utc", retainUntilUtc.ToString("O")));
            return OperationResults.Success(
                new CadCheckpoint { Manifest = manifest },
                $"checkpoint:{request.TransactionId.Value}",
                new OperationEvidence("file-checkpoint", observations, [manifestPath, snapshotPath], request.SourceStateHash));
        }
        catch (OperationCanceledException)
        {
            DeleteOwnedDirectory(checkpointDirectory);
            throw;
        }
        catch (Exception exception)
        {
            DeleteOwnedDirectory(checkpointDirectory);
            return OperationResults.Failure<CadCheckpoint>(
                $"checkpoint:{request.TransactionId.Value}",
                new OperationError(
                    ErrorCodes.CheckpointFailed,
                    "The durable checkpoint could not be created.",
                    ErrorCategories.Policy,
                    details: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["exception.type"] = exception.GetType().Name,
                    },
                    remediation: "Inspect the checkpoint root, file locks and available disk space; do not mutate until it is healthy."));
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<CadRecoveryResult>> RecoverAsync(
        CadCheckpoint checkpoint,
        CadTransactionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var observations = ImmutableArray.CreateBuilder<EvidenceObservation>();

        if (logicalRollback is not null)
        {
            try
            {
                OperationResult<CadLogicalRollbackAttempt>? logical = await logicalRollback.TryRollbackAsync(
                    checkpoint,
                    context,
                    cancellationToken).ConfigureAwait(false);
                observations.AddRange(logical?.Evidence?.Observations ?? []);
                if (logical?.IsSuccess == true && logical.Value is not null)
                {
                    observations.Add(new EvidenceObservation("recovery.logical_reversal", logical.Value.Applied.ToString()));
                    if (logical.Value.Applied && logical.Value.PreStateVerified)
                    {
                        var verifiedLogical = new CadRecoveryResult
                        {
                            Status = CadRecoveryStatus.LogicalReversalVerified,
                            PreStateVerified = true,
                            RestoredStateHash = logical.Value.RestoredStateHash,
                            VerificationScope = "provider-state",
                            Observations = observations.ToImmutable(),
                        };
                        return OperationResults.Success(
                            verifiedLogical,
                            $"rollback:{context.Plan.TransactionId.Value}",
                            new OperationEvidence("file-checkpoint", verifiedLogical.Observations, stateHash: verifiedLogical.RestoredStateHash));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Recovery itself remains bounded by the engine; a cancelled logical attempt falls through to the
                // snapshot path, if one exists.  logical rollback 取消后仍尝试 snapshot path，但整体由 engine 限时。
                observations.Add(new EvidenceObservation("recovery.logical_reversal", "cancelled"));
            }
            catch (Exception exception)
            {
                observations.Add(new EvidenceObservation("recovery.logical_exception_type", exception.GetType().Name));
            }
        }
        else
        {
            observations.Add(new EvidenceObservation("recovery.logical_reversal", "unavailable"));
        }

        string? sourcePath = checkpoint.Manifest.SourceDocumentPath;
        string? snapshotPath = checkpoint.Manifest.SnapshotPath;
        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(snapshotPath))
        {
            return RecoveryResult(
                context,
                CadRecoveryStatus.Unrecovered,
                preStateVerified: false,
                restoredStateHash: null,
                verificationScope: "none",
                observations,
                "No durable snapshot is available after logical reversal was unavailable.");
        }

        string sourceFullPath = Path.GetFullPath(sourcePath);
        string snapshotFullPath = Path.GetFullPath(snapshotPath);
        if (!IsWithinRoot(snapshotFullPath) || !File.Exists(snapshotFullPath) || IsWithinRoot(sourceFullPath))
        {
            observations.Add(new EvidenceObservation("recovery.snapshot", "invalid_or_missing"));
            return RecoveryResult(
                context,
                CadRecoveryStatus.Unrecovered,
                preStateVerified: false,
                restoredStateHash: null,
                verificationScope: "none",
                observations,
                "The checkpoint snapshot path is invalid or unavailable.");
        }

        try
        {
            // This overwrite is recovery-only and targets the exact source path captured in the manifest.  The native
            // provider must additionally close/reopen or inspect the COM document before claiming state restoration.
            // 该覆盖动作只允许发生在 recovery，并且目标必须是 manifest 中的 exact source path；原生 Provider 仍须
            // 关闭/重开或 inspection COM 文档后，才能声称 CAD state 已恢复。
            await CopyFileAsync(snapshotFullPath, sourceFullPath, cancellationToken, overwrite: true).ConfigureAwait(false);
            string restoredFileHash = await ComputeSha256Async(sourceFullPath, cancellationToken).ConfigureAwait(false);
            bool fileBytesMatch = string.Equals(restoredFileHash, checkpoint.Manifest.SourceFileHash, StringComparison.OrdinalIgnoreCase);
            observations.Add(new EvidenceObservation("recovery.snapshot", fileBytesMatch ? "restored" : "hash_mismatch"));
            observations.Add(new EvidenceObservation("recovery.file_bytes_verified", fileBytesMatch.ToString()));

            // File-byte equality intentionally does not imply the opaque CAD state hash is equal.  Keep this false until
            // a provider inspection callback supplies the stronger proof required by the transaction engine.
            // 文件字节相同并不等于 opaque CAD state hash 相同；在 Provider inspection callback 提供强证明前必须保持 false。
            return RecoveryResult(
                context,
                fileBytesMatch ? CadRecoveryStatus.CheckpointRestored : CadRecoveryStatus.Unrecovered,
                preStateVerified: false,
                restoredStateHash: null,
                verificationScope: "file-bytes-only",
                observations,
                fileBytesMatch
                    ? "The source bytes match the checkpoint, but provider CAD-state verification is still required."
                    : "The restored source bytes do not match the checkpoint hash.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            observations.Add(new EvidenceObservation("recovery.exception_type", exception.GetType().Name));
            return RecoveryResult(
                context,
                CadRecoveryStatus.Unrecovered,
                preStateVerified: false,
                restoredStateHash: null,
                verificationScope: "none",
                observations,
                "The checkpoint snapshot could not be restored to the source path.");
        }
    }

    /// <summary>Prunes only expired, well-formed checkpoint children and never scans outside the configured root.</summary>
    public async Task<OperationResult<CadCheckpointRetentionReceipt>> PruneExpiredAsync(
        DateTimeOffset nowUtc,
        int maxDirectories = 256,
        CancellationToken cancellationToken = default)
    {
        if (maxDirectories <= 0)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDirectories);
        }

        int scanned = 0;
        int removed = 0;
        int failed = 0;
        var retentionObservations = ImmutableArray.CreateBuilder<EvidenceObservation>();
        if (!Directory.Exists(rootDirectory))
        {
            return OperationResults.Success(
                new CadCheckpointRetentionReceipt { Scanned = 0, Removed = 0, Failed = 0 },
                "checkpoint:retention",
                new OperationEvidence("file-checkpoint", [new EvidenceObservation("retention.status", "empty")]));
        }

        foreach (string directory in Directory.EnumerateDirectories(rootDirectory).Take(maxDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            scanned++;
            string manifestPath = Path.Combine(directory, "manifest.json");
            try
            {
                CadCheckpointManifest? manifest;
                await using (FileStream manifestStream = new(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    manifest = await JsonSerializer.DeserializeAsync<CadCheckpointManifest>(manifestStream, ManifestJsonOptions, cancellationToken).ConfigureAwait(false);
                }

                if (manifest is null || manifest.RetainUntilUtc > nowUtc)
                {
                    continue;
                }

                DeleteOwnedDirectory(directory);
                removed++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                failed++;
                retentionObservations.Add(new EvidenceObservation("retention.failure_type", exception.GetType().Name));
            }
        }

        var receipt = new CadCheckpointRetentionReceipt { Scanned = scanned, Removed = removed, Failed = failed };
        ImmutableArray<EvidenceObservation> evidence =
        [
            new EvidenceObservation("retention.scanned", scanned.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new EvidenceObservation("retention.removed", removed.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new EvidenceObservation("retention.failed", failed.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            .. retentionObservations.ToImmutable(),
        ];
        return OperationResults.Success(
            receipt,
            "checkpoint:retention",
            new OperationEvidence("file-checkpoint", evidence));
    }

    private async Task<OperationResult<CadCheckpoint>> CheckpointFailureAsync(
        CadCheckpointRequest request,
        string checkpointDirectory,
        string message,
        string remediation)
    {
        DeleteOwnedDirectory(checkpointDirectory);
        return await Task.FromResult(
            OperationResults.Failure<CadCheckpoint>(
                $"checkpoint:{request.TransactionId.Value}",
                new OperationError(ErrorCodes.CheckpointFailed, message, ErrorCategories.Policy, remediation: remediation))).ConfigureAwait(false);
    }

    private static OperationResult<CadRecoveryResult> RecoveryResult(
        CadTransactionContext context,
        CadRecoveryStatus status,
        bool preStateVerified,
        string? restoredStateHash,
        string verificationScope,
        ImmutableArray<EvidenceObservation>.Builder observations,
        string message)
    {
        observations.Add(new EvidenceObservation("recovery.status", status.ToString()));
        observations.Add(new EvidenceObservation("recovery.pre_state_verified", preStateVerified.ToString()));
        observations.Add(new EvidenceObservation("recovery.message", message));
        var recovery = new CadRecoveryResult
        {
            Status = status,
            PreStateVerified = preStateVerified,
            RestoredStateHash = restoredStateHash,
            VerificationScope = verificationScope,
            Observations = observations.ToImmutable(),
        };
        return OperationResults.Success(
            recovery,
            $"rollback:{context.Plan.TransactionId.Value}",
            new OperationEvidence("file-checkpoint", recovery.Observations, stateHash: restoredStateHash));
    }

    private static async Task WriteManifestAtomicallyAsync(
        string manifestPath,
        CadCheckpointManifest manifest,
        CancellationToken cancellationToken)
    {
        string temporaryPath = $"{manifestPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, manifest, ManifestJsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, manifestPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken,
        bool overwrite = false)
    {
        await using FileStream source = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using FileStream destination = new(
            destinationPath,
            overwrite ? FileMode.Create : FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        await source.CopyToAsync(destination, 1024 * 1024, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return $"sha256:{Convert.ToHexString(hash)}";
    }

    private bool IsWithinRoot(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string relative = Path.GetRelativePath(rootDirectory, fullPath);
        return string.Equals(relative, ".", StringComparison.Ordinal)
            || !relative.StartsWith("..", StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }

    private void DeleteOwnedDirectory(string directory)
    {
        string fullPath = Path.GetFullPath(directory);
        string relative = Path.GetRelativePath(rootDirectory, fullPath);
        if (string.Equals(relative, ".", StringComparison.Ordinal)
            || relative.StartsWith("..", StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
        {
            return;
        }

        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }
    }
}

/// <summary>Optional provider hook for a safe logical reversal before file restoration.</summary>
public interface ICadLogicalRollbackHandler
{
    /// <summary>Attempts an inverse operation and must report whether the original state was proven.</summary>
    Task<OperationResult<CadLogicalRollbackAttempt>> TryRollbackAsync(
        CadCheckpoint checkpoint,
        CadTransactionContext context,
        CancellationToken cancellationToken = default);
}
