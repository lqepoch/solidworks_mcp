using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Core;
using SolidWorksMcp.Protocol;
using SolidWorksMcp.Provider.Fake;
using SolidWorksMcp.Server;

namespace SolidWorksMcp.ContractTests;

/// <summary>Protects the stable MCP tool names/metadata and default-off feature policy.</summary>
/// <remarks>
/// These are compatibility tests for the agent-facing contract, not implementation-detail tests.
/// 这些是 Agent-facing 契约的兼容性测试，不是对内部实现细节的测试。
/// </remarks>
public sealed class McpCompatibilityContractTests
{
    /// <summary>Default discovery order and safety metadata are intentionally stable.</summary>
    [Fact]
    public void DefaultToolRegistryHasStableNamesAndSafetyMetadata()
    {
        McpToolDescriptor[] tools = [.. McpToolCatalog.CreateDefault().Tools];

        Assert.Equal(
            ["cad.health", "cad.capabilities", "cad.create-part", "cad.build-part-drawing", "cad.inspect", "tolerance.explain", "drawing.validate", "drawing.repair", "drawing.release"],
            tools.Select(tool => tool.Name));
        Assert.All(tools, tool =>
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Tier));
            Assert.False(string.IsNullOrWhiteSpace(tool.Preconditions));
            Assert.False(string.IsNullOrWhiteSpace(tool.SideEffects));
        });
        Assert.Equal(CadCapabilityNames.PartMutation, tools[2].RequiredCapability);
        Assert.Equal(CadCapabilityNames.DrawingMutation, tools[3].RequiredCapability);
        Assert.Equal(CadCapabilityNames.Inspection, tools[4].RequiredCapability);
        Assert.Null(tools[5].RequiredCapability);
        Assert.Equal(CadCapabilityNames.DrawingMutation, tools[7].RequiredCapability);
    }

    /// <summary>Experimental behavior stays unavailable until its explicit feature flag is enabled.</summary>
    [Fact]
    public void DisabledExperimentalFeatureFailsCapabilityNegotiation()
    {
        var catalog = new McpToolCatalog(
        [
            new McpToolDescriptor(
                "drawing.experimental",
                "experimental",
                "feature flag",
                "may propose drawing changes",
                requiredFeature: FeatureFlagNames.ExperimentalDrawing),
        ]);
        var negotiator = new McpCapabilityNegotiator(
            new FakeCadProvider(),
            catalog,
            new SolidWorksMcpConfiguration(providerMode: ProviderModes.Fake));

        OperationError? error = negotiator.ValidateInvocation("drawing.experimental");

        Assert.NotNull(error);
        Assert.Equal(ErrorCodes.UnsupportedCapability, error!.Code);
        Assert.Contains(FeatureFlagNames.ExperimentalDrawing, error.Message, StringComparison.Ordinal);
    }
}
