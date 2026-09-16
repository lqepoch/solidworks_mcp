using System.Collections.Immutable;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Provider.Fake;

/// <summary>Configuration for a deterministic FakeCad provider instance.</summary>
public sealed record FakeCadOptions
{
    /// <summary>Default deterministic session identity.</summary>
    public SessionId SessionId { get; init; } = new("fake-session-001");

    /// <summary>Failure injector shared by provider, session and document objects.</summary>
    public FakeCadFailureInjector Failures { get; init; } = new();

    /// <summary>Optional safe workspace label used in fake export receipts.</summary>
    public string WorkspaceName { get; init; } = "fake-workspace";

    /// <summary>Capabilities exposed by the fake; callers may remove capabilities to test unsupported behavior.</summary>
    public CadCapabilitySet Capabilities { get; init; } = new(
    [
        new CadCapability(CadCapabilityNames.PartMutation, supported: true),
        new CadCapability(CadCapabilityNames.AssemblyMutation, supported: true),
        new CadCapability(CadCapabilityNames.DrawingMutation, supported: true),
        new CadCapability(CadCapabilityNames.Inspection, supported: true),
        new CadCapability(CadCapabilityNames.Export, supported: true),
        new CadCapability(CadCapabilityNames.Selection, supported: true),
        new CadCapability(CadCapabilityNames.PatternSemantics, supported: false, "Pattern semantics are not implemented in this A03 fake.")
    ]);
}
