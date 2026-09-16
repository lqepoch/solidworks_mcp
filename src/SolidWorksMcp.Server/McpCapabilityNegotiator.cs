using System.Collections.Immutable;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Core;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Server;

/// <summary>Effective availability of one registered MCP tool for the current provider and configuration.</summary>
/// <remarks>
/// Availability is calculated from declared provider capability and feature flags, never from a trial COM call.
/// 可用性只根据 Provider 声明能力和 feature flags 计算，绝不通过试探性 COM 调用来决定。
/// </remarks>
public sealed record McpToolAvailability
{
    /// <summary>Creates an immutable availability record.</summary>
    public McpToolAvailability(McpToolDescriptor descriptor, bool isAvailable, string? reason = null)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        IsAvailable = isAvailable;
        Reason = reason;
    }

    /// <summary>Gets the registered tool metadata.</summary>
    public McpToolDescriptor Descriptor { get; }

    /// <summary>Gets whether the current provider/configuration combination may invoke the tool.</summary>
    public bool IsAvailable { get; }

    /// <summary>Gets the safe deterministic reason when invocation is unavailable.</summary>
    public string? Reason { get; }
}

/// <summary>Negotiates the compact tool surface against explicit provider capabilities and feature flags.</summary>
/// <remarks>
/// This is a preflight policy service. It prevents unsupported combinations before session startup and therefore
/// keeps capability discovery side-effect free. 这是一个前置策略服务，在启动 Session 之前阻断不支持的组合，
/// 因而能力发现保持无副作用。
/// </remarks>
public sealed class McpCapabilityNegotiator(
    ICadProvider provider,
    McpToolCatalog catalog,
    SolidWorksMcpConfiguration configuration)
{
    private readonly ICadProvider provider = provider ?? throw new ArgumentNullException(nameof(provider));
    private readonly McpToolCatalog catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    private readonly SolidWorksMcpConfiguration configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

    /// <summary>Returns all registered tools with their current effective availability.</summary>
    public ImmutableArray<McpToolAvailability> GetAvailability() =>
        [.. catalog.Tools.Select(Evaluate)];

    /// <summary>Returns an error when a tool is unavailable, or null when it can be invoked.</summary>
    public OperationError? ValidateInvocation(string toolName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        McpToolAvailability? availability = GetAvailability()
            .FirstOrDefault(item => string.Equals(item.Descriptor.Name, toolName, StringComparison.Ordinal));
        if (availability is null)
        {
            return new OperationError(
                ErrorCodes.InvalidRequest,
                $"Tool '{toolName}' is not registered.",
                ErrorCategories.Validation,
                remediation: "Use cad.capabilities or tools/list to select a registered tool.");
        }

        if (availability.IsAvailable)
        {
            return null;
        }

        return new OperationError(
            ErrorCodes.UnsupportedCapability,
            availability.Reason ?? $"Tool '{toolName}' is unavailable for the current provider/configuration.",
            ErrorCategories.Capability,
            remediation: "Select a supported capability or enable an explicitly approved feature flag.",
            details: new Dictionary<string, string>
            {
                ["tool"] = toolName,
                ["providerMode"] = configuration.ProviderMode,
            });
    }

    private McpToolAvailability Evaluate(McpToolDescriptor descriptor)
    {
        if (descriptor.RequiredFeature is not null && !configuration.Features.IsEnabled(descriptor.RequiredFeature))
        {
            return new McpToolAvailability(
                descriptor,
                isAvailable: false,
                $"Tool '{descriptor.Name}' requires disabled feature '{descriptor.RequiredFeature}'.");
        }

        if (descriptor.RequiredCapability is not null && !provider.Capabilities.Supports(descriptor.RequiredCapability))
        {
            CadCapability? capability = provider.Capabilities.Find(descriptor.RequiredCapability);
            string reason = capability?.Reason
                ?? $"Provider does not declare capability '{descriptor.RequiredCapability}'.";
            return new McpToolAvailability(descriptor, isAvailable: false, reason);
        }

        return new McpToolAvailability(descriptor, isAvailable: true);
    }
}
