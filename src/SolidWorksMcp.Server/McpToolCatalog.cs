using System.Collections.Immutable;

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
        string? requiredCapability = null)
    {
        Name = RequireNonBlank(name, nameof(name));
        Tier = RequireNonBlank(tier, nameof(tier));
        Preconditions = RequireNonBlank(preconditions, nameof(preconditions));
        SideEffects = RequireNonBlank(sideEffects, nameof(sideEffects));
        RequiredCapability = requiredCapability;
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

    private static string RequireNonBlank(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}

/// <summary>Immutable registry for the compact high-level MCP tool surface.</summary>
/// <remarks>
/// The registry is intentionally small. Primitive COM-like operations belong to an advanced/debug tier introduced by
/// later Issues, not to the default agent-facing surface. 默认工具保持紧凑，避免用数百个低层调用淹没 Codex。
/// </remarks>
public sealed class McpToolCatalog
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

    /// <summary>Creates the A02 default registry.</summary>
    public static McpToolCatalog CreateDefault() => new(
    [
        new McpToolDescriptor("cad.health", "read", "none", "none"),
        new McpToolDescriptor("cad.capabilities", "read", "none", "none"),
        new McpToolDescriptor("cad.create-part", "mutation", "schemaVersion=1.0; valid configuration; provider session", "creates one CAD part document", CadAbstractions.CadCapabilityNames.PartMutation),
        new McpToolDescriptor("cad.inspect", "read", "schemaVersion=1.0; stable document ID", "none", CadAbstractions.CadCapabilityNames.Inspection),
    ]);
}
