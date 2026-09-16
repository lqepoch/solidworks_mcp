using System.Collections.Immutable;

namespace SolidWorksMcp.CadAbstractions;

/// <summary>Vendor-neutral document kinds exposed by the CAD provider boundary.</summary>
/// <remarks>业务层只依赖这些稳定值；SOLIDWORKS swDocumentTypes 映射留在 Provider 内部。</remarks>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum CadDocumentType
{
    /// <summary>Three-dimensional part document.</summary>
    Part,

    /// <summary>Assembly document containing component instances.</summary>
    Assembly,

    /// <summary>Two-dimensional engineering drawing document.</summary>
    Drawing,
}

/// <summary>Explicit loading state for large assemblies.</summary>
/// <remarks>禁止把未覆盖的组件误报成已解析；provider 必须保留真实状态。</remarks>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum CadLoadState
{
    /// <summary>Fully resolved geometry and feature data are available.</summary>
    Resolved,

    /// <summary>Component is loaded in a lightweight representation.</summary>
    Lightweight,

    /// <summary>Component is intentionally suppressed.</summary>
    Suppressed,

    /// <summary>Component is not loaded in the current session.</summary>
    Unloaded,

    /// <summary>Component is represented by a SpeedPak.</summary>
    SpeedPak,

    /// <summary>Only the selected branch was opened for inspection.</summary>
    SelectiveOpen,

    /// <summary>Assembly is in Large Design Review mode.</summary>
    LargeDesignReview,
}

/// <summary>Capability names shared by FakeCad and future native providers.</summary>
public static class CadCapabilityNames
{
    /// <summary>Part creation and mutation capability.</summary>
    public const string PartMutation = "part.mutation";

    /// <summary>Assembly component and mate mutation capability.</summary>
    public const string AssemblyMutation = "assembly.mutation";

    /// <summary>Drawing view and annotation mutation capability.</summary>
    public const string DrawingMutation = "drawing.mutation";

    /// <summary>Inspection and invariant evidence capability.</summary>
    public const string Inspection = "inspection";

    /// <summary>File export capability.</summary>
    public const string Export = "export";

    /// <summary>Native pattern/feature semantic extraction capability.</summary>
    public const string PatternSemantics = "semantic.patterns";
}

/// <summary>Describes whether one provider capability is supported or explicitly unavailable.</summary>
public sealed record CadCapability
{
    /// <summary>Creates a capability description.</summary>
    public CadCapability(string name, bool supported, string? reason = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (supported && reason is not null)
        {
            throw new ArgumentException("A supported capability must not carry an unsupported reason.", nameof(reason));
        }

        Name = name.Trim();
        Supported = supported;
        Reason = reason;
    }

    /// <summary>Gets the stable capability name.</summary>
    public string Name { get; }

    /// <summary>Gets whether the provider supports this capability in the current session.</summary>
    public bool Supported { get; }

    /// <summary>Gets the explicit reason when the capability is unavailable.</summary>
    public string? Reason { get; }
}

/// <summary>Immutable capability snapshot; unsupported operations must return a structured error, not silently no-op.</summary>
public sealed record CadCapabilitySet
{
    /// <summary>Creates a deterministic capability set keyed by stable capability name.</summary>
    public CadCapabilitySet(IEnumerable<CadCapability> capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        Capabilities = capabilities.ToImmutableDictionary(capability => capability.Name, StringComparer.Ordinal);
    }

    /// <summary>Gets all declared capabilities, including explicit unsupported entries.</summary>
    public ImmutableDictionary<string, CadCapability> Capabilities { get; }

    /// <summary>Returns whether a capability is explicitly supported.</summary>
    public bool Supports(string name) =>
        Capabilities.TryGetValue(name, out CadCapability? capability) && capability.Supported;

    /// <summary>Returns the capability declaration or null when the provider did not declare it.</summary>
    public CadCapability? Find(string name) =>
        Capabilities.TryGetValue(name, out CadCapability? capability) ? capability : null;
}
