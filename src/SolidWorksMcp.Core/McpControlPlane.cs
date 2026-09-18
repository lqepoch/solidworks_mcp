using System.Collections.Concurrent;
using System.Collections.Immutable;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Core;

/// <summary>
/// Describes one bounded MCP operation independently from the transport SDK or a CAD vendor.
/// 在不依赖 MCP transport SDK 或 CAD 厂商的前提下，描述一个有界 MCP operation。
/// </summary>
/// <remarks>
/// This is the authoritative control-plane contract. Server adapters may add human-readable descriptions, but they
/// must not weaken these risk, binding or capability requirements. 这是控制面的权威契约；Server adapter 可以补充
/// 面向 Agent 的说明，但不能降低这里定义的风险、绑定或能力要求。
/// </remarks>
public sealed record McpOperationDescriptor
{
    /// <summary>Stable tool/operation name exposed to MCP clients.</summary>
    public required string Name { get; init; }

    /// <summary>Compact discovery tier, for example read, mutation or release.</summary>
    public required string Tier { get; init; }

    /// <summary>Maximum CAD risk represented by this operation.</summary>
    public required CadRiskLevel RiskLevel { get; init; }

    /// <summary>Whether the operation can change a CAD document or persisted artifact.</summary>
    public required bool MutatesCad { get; init; }

    /// <summary>Whether execution must be bound to an explicit provider session.</summary>
    public required bool RequiresSession { get; init; }

    /// <summary>Whether a caller must provide an idempotency key for this operation.</summary>
    public required bool RequiresIdempotency { get; init; }

    /// <summary>Whether a document-scoped mutation must carry the state hash captured during planning.</summary>
    public required bool RequiresExpectedStateHash { get; init; }

    /// <summary>Optional provider capability required by the operation.</summary>
    public string? RequiredCapability { get; init; }

    /// <summary>Optional default-off feature flag required by the operation.</summary>
    public string? RequiredFeature { get; init; }
}

/// <summary>Provider-neutral registry used by the MCP control plane before a tool reaches a provider.</summary>
/// <remarks>
/// The registry is deliberately an allowlist. It is not a command evaluator and has no API for arbitrary code,
/// PowerShell, macro or COM expressions. 注册表刻意是 allowlist，不提供任意代码、PowerShell、宏或 COM expression。
/// </remarks>
public interface IMcpOperationCatalog
{
    /// <summary>Finds a registered operation by its exact stable name.</summary>
    bool TryGet(string name, out McpOperationDescriptor? descriptor);

    /// <summary>Returns descriptors in deterministic discovery order.</summary>
    ImmutableArray<McpOperationDescriptor> List();
}

/// <summary>All security-relevant context supplied to one MCP invocation.</summary>
/// <remarks>
/// A context is created at the MCP boundary and is enriched after the provider session/document is bound. The active
/// SOLIDWORKS document is never used as an implicit target. Context 在 MCP 边界创建，并在绑定 Provider
/// session/document 后补充信息；绝不把 active document 当作隐式目标。
/// </remarks>
public sealed record McpInvocationContext
{
    /// <summary>Application operation identity used for result correlation and audit.</summary>
    public required string OperationId { get; init; }

    /// <summary>Exact registered tool name.</summary>
    public required string ToolName { get; init; }

    /// <summary>Caller-provided replay key; duplicate mutation requests use the same key.</summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>Bound provider session identity.</summary>
    public SessionId? SessionId { get; init; }

    /// <summary>Bound target document identity.</summary>
    public DocumentId? DocumentId { get; init; }

    /// <summary>Configuration assertion bound to the document operation.</summary>
    public string? Configuration { get; init; }

    /// <summary>State hash captured immediately before planning a document mutation.</summary>
    public string? ExpectedStateHash { get; init; }

    /// <summary>Private target path supplied to policy; it is never copied into public audit records.</summary>
    public string? TargetPath { get; init; }

    /// <summary>Required target extension used by path policy, for example .sldprt.</summary>
    public string? RequiredPathExtension { get; init; }
}

/// <summary>Explicit bounds for the control plane instead of hidden fail-open defaults.</summary>
public sealed record McpControlPlaneOptions
{
    /// <summary>Highest risk accepted by this host profile.</summary>
    public CadRiskLevel MaximumRisk { get; init; } = CadRiskLevel.DestructiveSaveOverwrite;

    /// <summary>Require a state hash whenever the descriptor marks a document mutation.</summary>
    public bool RequireExpectedStateHashForMutations { get; init; } = true;

    /// <summary>Require a caller key whenever the descriptor marks an idempotent operation.</summary>
    public bool RequireIdempotencyKey { get; init; } = true;
}

/// <summary>Redacted audit event emitted by the MCP control plane.</summary>
/// <remarks>
/// No raw CAD path, PDF text, COM exception text or credentials are allowed here. This record is safe to persist as a
/// support artifact when the operator explicitly requests one. 这里不允许保存 CAD path、PDF text、COM exception
/// text 或 credentials，因此在用户明确请求时可安全写入 support artifact。
/// </remarks>
public sealed record McpAuditRecord
{
    /// <summary>UTC event time.</summary>
    public required DateTimeOffset TimestampUtc { get; init; }

    /// <summary>One of admission.accepted, admission.rejected, execution.completed or execution.failed.</summary>
    public required string Event { get; init; }

    /// <summary>Application operation identity.</summary>
    public required string OperationId { get; init; }

    /// <summary>Registered operation name.</summary>
    public required string ToolName { get; init; }

    /// <summary>Risk level declared by the registry.</summary>
    public required CadRiskLevel RiskLevel { get; init; }

    /// <summary>Whether the invocation carried a target path without exposing its value.</summary>
    public bool HasTargetPath { get; init; }

    /// <summary>Target extension only; the directory and file name are intentionally absent.</summary>
    public string? TargetExtension { get; init; }

    /// <summary>Stable terminal error code, when the event represents a failure.</summary>
    public string? ErrorCode { get; init; }
}

/// <summary>Sink for structured, redacted MCP audit events.</summary>
public interface IMcpAuditSink
{
    /// <summary>Persists one event without accepting unredacted request payloads.</summary>
    ValueTask WriteAsync(McpAuditRecord record, CancellationToken cancellationToken = default);
}

/// <summary>In-process audit sink used by the stdio host and deterministic tests.</summary>
/// <remarks>
/// A later durable sink can implement <see cref="IMcpAuditSink"/> without changing the MCP boundary. The bounded
/// in-memory sink avoids writing files during hosted CI or leaking private drawing paths. 后续 durable sink 只需实现
/// 同一接口，不必修改 MCP boundary；当前内存 sink 避免 Hosted CI 写文件或泄露秘密图纸路径。
/// </remarks>
public sealed class InMemoryMcpAuditSink : IMcpAuditSink
{
    private readonly ConcurrentQueue<McpAuditRecord> records = new();

    /// <summary>Gets a point-in-time immutable snapshot for diagnostics and tests.</summary>
    public ImmutableArray<McpAuditRecord> Records => [.. records];

    /// <inheritdoc />
    public ValueTask WriteAsync(McpAuditRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();
        records.Enqueue(record);
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// The provider-neutral admission and execution gate for MCP operations.
/// Provider-neutral 的 MCP operation 准入与执行 gate。
/// </summary>
/// <remarks>
/// This class is intentionally not a CAD service. It validates registration, risk, capability, path and binding
/// invariants, then delegates execution to one bounded handler. Business workflows such as drawing compilation remain
/// behind that handler. 本类刻意不是 CAD service：它只验证注册、风险、能力、路径和 binding invariant，然后把执行
/// 委托给一个有界 handler；工程图 compiler 等业务流程仍然位于 handler 后方。
/// </remarks>
public sealed class McpControlPlane(
    IMcpOperationCatalog catalog,
    CadCapabilitySet capabilities,
    SolidWorksMcpConfiguration configuration,
    CadPathAllowlist pathAllowlist,
    IMcpAuditSink auditSink,
    McpControlPlaneOptions? options = null)
{
    private readonly IMcpOperationCatalog catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    private readonly CadCapabilitySet capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
    private readonly SolidWorksMcpConfiguration configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    private readonly CadPathAllowlist pathAllowlist = pathAllowlist ?? throw new ArgumentNullException(nameof(pathAllowlist));
    private readonly IMcpAuditSink auditSink = auditSink ?? throw new ArgumentNullException(nameof(auditSink));
    private readonly McpControlPlaneOptions options = options ?? new();

    /// <summary>Returns the immutable operation registry for capability discovery.</summary>
    public ImmutableArray<McpOperationDescriptor> ListOperations() => catalog.List();

    /// <summary>Returns a stable policy error, or null when the invocation may reach its handler.</summary>
    public OperationError? Validate(McpInvocationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!catalog.TryGet(context.ToolName, out McpOperationDescriptor? descriptor) || descriptor is null)
        {
            return new OperationError(
                ErrorCodes.InvalidRequest,
                $"MCP operation '{context.ToolName}' is not registered.",
                ErrorCategories.Validation,
                remediation: "Use cad.capabilities or tools/list to select a registered operation.");
        }

        if (!string.Equals(context.ToolName, descriptor.Name, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(context.OperationId))
        {
            return new OperationError(
                ErrorCodes.InvalidRequest,
                "The MCP invocation identity is incomplete.",
                ErrorCategories.Validation);
        }

        if (descriptor.RiskLevel > options.MaximumRisk)
        {
            return new OperationError(
                ErrorCodes.BlockedHumanActionRequired,
                "The requested MCP risk level is not enabled by this host profile.",
                ErrorCategories.Policy,
                remediation: "Use an explicitly approved host profile for this operation.",
                details: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["tool"] = descriptor.Name,
                    ["risk"] = descriptor.RiskLevel.ToString(),
                });
        }

        if (descriptor.RequiredFeature is not null && !configuration.Features.IsEnabled(descriptor.RequiredFeature))
        {
            return new OperationError(
                ErrorCodes.UnsupportedCapability,
                "The MCP operation requires a disabled feature flag.",
                ErrorCategories.Capability,
                remediation: "Enable the explicitly approved feature flag before invoking this operation.",
                details: new Dictionary<string, string>(StringComparer.Ordinal) { ["feature"] = descriptor.RequiredFeature });
        }

        if (descriptor.RequiredCapability is not null && !capabilities.Supports(descriptor.RequiredCapability))
        {
            CadCapability? capability = capabilities.Find(descriptor.RequiredCapability);
            return new OperationError(
                ErrorCodes.UnsupportedCapability,
                capability?.Reason ?? "The selected provider does not declare the required capability.",
                ErrorCategories.Capability,
                remediation: "Select a supported operation or configure a provider that declares the capability.",
                details: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["capability"] = descriptor.RequiredCapability,
                });
        }

        if (descriptor.RequiresSession && context.SessionId is null)
        {
            return new OperationError(
                ErrorCodes.StateConflict,
                "The operation is not bound to a provider session.",
                ErrorCategories.State,
                remediation: "Start or select the exact provider session before execution.");
        }

        if (descriptor.MutatesCad && descriptor.RequiresExpectedStateHash
            && options.RequireExpectedStateHashForMutations
            && string.IsNullOrWhiteSpace(context.ExpectedStateHash))
        {
            return new OperationError(
                ErrorCodes.StateConflict,
                "This document mutation requires the expected state hash captured during planning.",
                ErrorCategories.State,
                remediation: "Inspect the exact document and retry with its current state hash.");
        }

        if (descriptor.MutatesCad && descriptor.RequiresIdempotency
            && options.RequireIdempotencyKey
            && string.IsNullOrWhiteSpace(context.IdempotencyKey))
        {
            return new OperationError(
                ErrorCodes.InvalidRequest,
                "This mutation requires an explicit idempotency key.",
                ErrorCategories.Validation,
                remediation: "Reuse the same key when retrying the same intended mutation.");
        }

        if (!string.IsNullOrWhiteSpace(context.TargetPath) || !string.IsNullOrWhiteSpace(context.RequiredPathExtension))
        {
            if (string.IsNullOrWhiteSpace(context.RequiredPathExtension))
            {
                return new OperationError(ErrorCodes.InvalidRequest, "A target path requires an extension policy.", ErrorCategories.Validation);
            }

            CadPathValidationResult path = pathAllowlist.ValidateCreateTarget(context.TargetPath, context.RequiredPathExtension);
            if (!path.IsAllowed)
            {
                return new OperationError(
                    ErrorCodes.PathNotAllowed,
                    "The target path is outside the configured CAD artifact policy.",
                    ErrorCategories.Policy,
                    remediation: "Use a new artifact path below an operator-approved workspace.",
                    details: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["reason"] = path.FailureReason ?? "denied",
                        ["extension"] = context.RequiredPathExtension,
                    });
            }
        }

        return null;
    }

    /// <summary>Executes one admitted operation and emits redacted begin/terminal audit records.</summary>
    public async Task<OperationResult<T>> ExecuteAsync<T>(
        McpInvocationContext context,
        Func<CancellationToken, Task<OperationResult<T>>> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(handler);
        OperationError? admissionError = Validate(context);
        McpOperationDescriptor descriptor = ResolveDescriptor(context.ToolName);
        if (admissionError is not null)
        {
            await AuditAsync(context, descriptor, "admission.rejected", admissionError.Code, cancellationToken).ConfigureAwait(false);
            return OperationResults.Failure<T>(context.OperationId, admissionError);
        }

        await AuditAsync(context, descriptor, "admission.accepted", null, cancellationToken).ConfigureAwait(false);
        try
        {
            OperationResult<T> result = await handler(cancellationToken).ConfigureAwait(false);
            await AuditAsync(
                context,
                descriptor,
                result.IsSuccess ? "execution.completed" : "execution.failed",
                result.Error?.Code,
                CancellationToken.None).ConfigureAwait(false);
            return result;
        }
        catch (OperationCanceledException)
        {
            var error = new OperationError(ErrorCodes.Cancelled, "The MCP operation was cancelled.", ErrorCategories.Execution);
            await AuditAsync(context, descriptor, "execution.failed", error.Code, CancellationToken.None).ConfigureAwait(false);
            return OperationResults.Failure<T>(context.OperationId, error);
        }
        catch (Exception exception)
        {
            // Exception text and COM HRESULTs stay in local diagnostics; the MCP envelope exposes only a safe type label.
            // 异常文本和 COM HRESULT 只留在本机诊断；MCP envelope 只暴露安全的类型标签。
            var error = new OperationError(
                ErrorCodes.ProviderFailure,
                "The MCP operation failed at its provider boundary.",
                ErrorCategories.Provider,
                retryable: true,
                details: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["exception.type"] = exception.GetType().Name,
                });
            await AuditAsync(context, descriptor, "execution.failed", error.Code, CancellationToken.None).ConfigureAwait(false);
            return OperationResults.Failure<T>(context.OperationId, error);
        }
    }

    private McpOperationDescriptor ResolveDescriptor(string name) =>
        catalog.TryGet(name, out McpOperationDescriptor? descriptor) && descriptor is not null
            ? descriptor
            : new McpOperationDescriptor
            {
                Name = name,
                Tier = "unknown",
                RiskLevel = CadRiskLevel.Read,
                MutatesCad = false,
                RequiresSession = false,
                RequiresIdempotency = false,
                RequiresExpectedStateHash = false,
            };

    private async ValueTask AuditAsync(
        McpInvocationContext context,
        McpOperationDescriptor descriptor,
        string eventName,
        string? errorCode,
        CancellationToken cancellationToken)
    {
        string? extension = string.IsNullOrWhiteSpace(context.TargetPath)
            ? null
            : Path.GetExtension(context.TargetPath.Trim());
        await auditSink.WriteAsync(
            new McpAuditRecord
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Event = eventName,
                OperationId = context.OperationId,
                ToolName = descriptor.Name,
                RiskLevel = descriptor.RiskLevel,
                HasTargetPath = !string.IsNullOrWhiteSpace(context.TargetPath),
                TargetExtension = string.IsNullOrWhiteSpace(extension) ? null : extension,
                ErrorCode = errorCode,
            },
            cancellationToken).ConfigureAwait(false);
    }
}
