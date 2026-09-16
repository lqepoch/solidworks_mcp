using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Server;

/// <summary>Explicit safe default until the native SOLIDWORKS provider is configured by Issue #17.</summary>
/// <remarks>
/// The server can complete MCP handshake and capability discovery without silently pretending that CAD mutation is
/// available. MCP 握手可以在没有本机 Provider 时完成，但所有 CAD 能力都会明确返回 unsupported。
/// </remarks>
internal sealed class UnavailableCadProvider : ICadProvider
{
    private static readonly CadCapabilitySet capabilities = new(
    [
        new CadCapability(CadCapabilityNames.PartMutation, supported: false, "Native SOLIDWORKS provider is not configured yet."),
        new CadCapability(CadCapabilityNames.AssemblyMutation, supported: false, "Native SOLIDWORKS provider is not configured yet."),
        new CadCapability(CadCapabilityNames.DrawingMutation, supported: false, "Native SOLIDWORKS provider is not configured yet."),
        new CadCapability(CadCapabilityNames.Inspection, supported: false, "Native SOLIDWORKS provider is not configured yet."),
        new CadCapability(CadCapabilityNames.Export, supported: false, "Native SOLIDWORKS provider is not configured yet."),
        new CadCapability(CadCapabilityNames.Selection, supported: false, "Native SOLIDWORKS provider is not configured yet."),
    ]);

    /// <inheritdoc />
    public CadCapabilitySet Capabilities => capabilities;

    /// <inheritdoc />
    public ValueTask<OperationResult<ICadSession>> StartSessionAsync(
        CadSessionOptions options,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(
            OperationResults.Failure<ICadSession>(
                "server:session-start",
                new OperationError(
                    ErrorCodes.UnsupportedCapability,
                    "No native SOLIDWORKS CAD provider is configured.",
                    ErrorCategories.Capability,
                    remediation: "Run the Windows doctor and configure the native provider.")));
    }
}
