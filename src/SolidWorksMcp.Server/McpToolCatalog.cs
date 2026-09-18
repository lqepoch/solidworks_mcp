using System.Collections.Immutable;

using SolidWorksMcp.Core;

namespace SolidWorksMcp.Server;

/// <summary>Stable metadata that explains tool preconditions and side effects to humans and agents.</summary>
/// <remarks>
/// The descriptor is data, not executable policy: it makes the first discovery layer explicit while the tool still
/// performs authoritative validation at invocation time. 描述符是数据而不是可执行策略：它显式表达发现层元数据，
/// 但真正的输入和状态校验仍必须在调用时由工具执行。
/// </remarks>
public sealed record McpToolDescriptor
{
    /// <summary>Creates a tool descriptor.</summary>
    /// <remarks>Blank metadata is rejected so an agent cannot receive an apparently safe but undocumented operation.</remarks>
    /// <remarks>拒绝空白元数据，避免 Agent 看到一个看似安全但没有文档约束的操作。</remarks>
    public McpToolDescriptor(
        string name,
        string tier,
        string preconditions,
        string sideEffects,
        string? requiredCapability = null,
        string? requiredFeature = null,
        CadRiskLevel? riskLevel = null,
        bool? requiresSession = null,
        bool? requiresIdempotency = null,
        bool? requiresExpectedStateHash = null)
    {
        Name = RequireNonBlank(name, nameof(name));
        Tier = RequireNonBlank(tier, nameof(tier));
        Preconditions = RequireNonBlank(preconditions, nameof(preconditions));
        SideEffects = RequireNonBlank(sideEffects, nameof(sideEffects));
        RequiredCapability = NormalizeOptional(requiredCapability);
        RequiredFeature = NormalizeOptional(requiredFeature);
        RiskLevel = riskLevel ?? InferRiskLevel(Tier, Name);
        MutatesCad = RiskLevel != CadRiskLevel.Read;
        RequiresSession = requiresSession ?? RequiredCapability is not null;
        RequiresIdempotency = requiresIdempotency ?? MutatesCad;
        RequiresExpectedStateHash = requiresExpectedStateHash ?? MutatesCad;
        Operation = new McpOperationDescriptor
        {
            Name = Name,
            Tier = Tier,
            RiskLevel = RiskLevel,
            MutatesCad = MutatesCad,
            RequiresSession = RequiresSession,
            RequiresIdempotency = RequiresIdempotency,
            RequiresExpectedStateHash = RequiresExpectedStateHash,
            RequiredCapability = RequiredCapability,
            RequiredFeature = RequiredFeature,
        };
    }

    /// <summary>Gets the MCP tool name.</summary>
    public string Name { get; }

    /// <summary>Gets the interaction tier, such as read or mutation.</summary>
    public string Tier { get; }

    /// <summary>Gets preconditions that must be true before invocation.</summary>
    public string Preconditions { get; }

    /// <summary>Gets side effects, including whether the tool mutates CAD state.</summary>
    public string SideEffects { get; }

    /// <summary>Gets the optional provider capability required by the tool.</summary>
    public string? RequiredCapability { get; }

    /// <summary>Gets the optional default-off feature flag required by the tool.</summary>
    public string? RequiredFeature { get; }

    /// <summary>Typed control-plane risk used by policy, audit and transaction selection.</summary>
    public CadRiskLevel RiskLevel { get; }

    /// <summary>Whether this descriptor may mutate CAD or persisted artifacts.</summary>
    public bool MutatesCad { get; }

    /// <summary>Whether execution must be bound to a provider session.</summary>
    public bool RequiresSession { get; }

    /// <summary>Whether an explicit replay key is required.</summary>
    public bool RequiresIdempotency { get; }

    /// <summary>Whether a document state hash is required before mutation.</summary>
    public bool RequiresExpectedStateHash { get; }

    /// <summary>Provider-neutral descriptor consumed by the Core control plane.</summary>
    public McpOperationDescriptor Operation { get; }

    private static CadRiskLevel InferRiskLevel(string tier, string name) => tier switch
    {
        "read" when name is "cad.inspect" or "drawing.validate" => CadRiskLevel.Read,
        "read" => CadRiskLevel.Read,
        "release" => CadRiskLevel.DrawingRelease,
        "mutation" when name.StartsWith("drawing.", StringComparison.Ordinal) => CadRiskLevel.DrawingMutation,
        "mutation" => CadRiskLevel.ModelMutation,
        _ => CadRiskLevel.Read,
    };

    private static string RequireNonBlank(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Immutable registry for the compact high-level MCP tool surface.</summary>
/// <remarks>
/// The registry is intentionally small. Primitive COM-like operations belong to an advanced/debug tier introduced by
/// later Issues, not to the default agent-facing surface. 默认工具保持紧凑，避免用数百个低层调用淹没 Codex。
/// </remarks>
public sealed class McpToolCatalog : IMcpOperationCatalog
{
    /// <summary>Creates a catalog from descriptors and rejects duplicate tool names.</summary>
    /// <remarks>
    /// Registration order is preserved for deterministic discovery; duplicate names fail during composition instead of
    /// being resolved by last-registration-wins behavior. 注册顺序用于确定性发现；重复名称在组合阶段失败，
    /// 不允许依赖“后注册覆盖先注册”的隐式行为。
    /// </remarks>
    public McpToolCatalog(IEnumerable<McpToolDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        Tools = [.. descriptors];
        if (Tools.Select(tool => tool.Name).Distinct(StringComparer.Ordinal).Count() != Tools.Length)
        {
            throw new ArgumentException("Tool names must be unique.", nameof(descriptors));
        }
    }

    /// <summary>Gets immutable tool metadata in deterministic registry order.</summary>
    public ImmutableArray<McpToolDescriptor> Tools { get; }

    /// <summary>Returns the typed provider-neutral operation descriptors in the same deterministic order.</summary>
    public ImmutableArray<McpOperationDescriptor> List() => [.. Tools.Select(tool => tool.Operation)];

    /// <summary>Looks up one operation for the Core control-plane admission gate.</summary>
    public bool TryGet(string name, out McpOperationDescriptor? descriptor)
    {
        McpToolDescriptor? tool = Tools.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.Ordinal));
        descriptor = tool?.Operation;
        return descriptor is not null;
    }

    /// <summary>Creates the A02 default registry.</summary>
    /// <remarks>
    /// Experimental tools can be registered with <see cref="FeatureFlagNames.ExperimentalDrawing"/> or another
    /// allowlisted feature, but no current default tool silently opts into experimental behavior. 实验 tool 必须绑定
    /// 白名单 feature flag；当前默认 tool 不会静默启用任何实验行为。
    /// </remarks>
    public static McpToolCatalog CreateDefault() => new(
    [
        new McpToolDescriptor("cad.health", "read", "none", "none"),
        new McpToolDescriptor("cad.capabilities", "read", "none", "none"),
        new McpToolDescriptor("cad.create-part", "mutation", "schemaVersion=1.0; valid configuration; provider session", "creates one CAD part document", CadAbstractions.CadCapabilityNames.PartMutation, requiresExpectedStateHash: false),
        new McpToolDescriptor("cad.build-part-drawing-intent", "mutation", "schemaVersion=1.0; bounded intent JSON; allowlisted part/drawing/PDF paths; resolved RulePack", "creates a draft part/drawing/PDF through the deterministic compiler; release remains a separate gated operation", CadAbstractions.CadCapabilityNames.DrawingMutation, riskLevel: CadRiskLevel.DrawingMutation, requiresExpectedStateHash: false),
        new McpToolDescriptor("cad.build-part-drawing", "mutation", "schemaVersion=1.0; allowlisted part/drawing/PDF paths; closed profile; resolved RulePack", "creates a draft part/drawing/PDF with native read-back evidence; release remains a separate gated operation", CadAbstractions.CadCapabilityNames.DrawingMutation, riskLevel: CadRiskLevel.DrawingMutation, requiresExpectedStateHash: false),
        new McpToolDescriptor("cad.inspect", "read", "schemaVersion=1.0; stable document ID", "none", CadAbstractions.CadCapabilityNames.Inspection, requiresSession: true),
        new McpToolDescriptor("tolerance.explain", "read", "schemaVersion=1.0; bounded redacted tolerance JSON", "none"),
        new McpToolDescriptor("drawing.validate", "read", "schemaVersion=1.0; stable drawing document ID; bounded requirement/evidence JSON", "none", CadAbstractions.CadCapabilityNames.Inspection, requiresSession: true),
        new McpToolDescriptor("drawing.repair", "mutation", "schemaVersion=1.0; exact drawing/state hash; one bounded layout repair action", "moves one exact annotation, saves and reopens the drawing", CadAbstractions.CadCapabilityNames.DrawingMutation, riskLevel: CadRiskLevel.DrawingMutation, requiresSession: true),
        new McpToolDescriptor("drawing.release", "release", "schemaVersion=1.0; exact drawing/state hash; approved requirement/layout/annotation evidence; explicit artifact policy", "checkpointed save, configured export, final QA and evidence manifest", CadAbstractions.CadCapabilityNames.DrawingMutation, riskLevel: CadRiskLevel.DrawingRelease, requiresSession: true),
    ]);
}
