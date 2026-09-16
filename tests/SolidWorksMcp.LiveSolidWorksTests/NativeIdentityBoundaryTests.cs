using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;
using SolidWorksMcp.Provider.SolidWorks;

namespace SolidWorksMcp.LiveSolidWorksTests;

/// <summary>
/// Verifies native identity guards without starting SOLIDWORKS.
/// 在不启动 SOLIDWORKS 的情况下验证 native identity guard。
/// </summary>
public sealed class NativeIdentityBoundaryTests
{
    [Fact]
    public void RegistryRejectsStaleCandidateAndPreservesNewerState()
    {
        var registry = new SolidWorksDocumentRegistry();
        SolidWorksDocumentDescriptor initial = CreateDescriptor("sha256:initial");
        registry.Add(initial);

        SolidWorksDocumentDescriptor newer = initial with { StateHash = "sha256:newer", IsDirty = true };
        Assert.True(registry.TryUpdateDescriptor(initial, newer, out SolidWorksDocumentDescriptor? committed));
        Assert.Equal(newer, committed);

        SolidWorksDocumentDescriptor stale = initial with { StateHash = "sha256:stale" };
        Assert.False(registry.TryUpdateDescriptor(initial, stale, out SolidWorksDocumentDescriptor? rejected));
        Assert.Null(rejected);
        Assert.True(registry.TryGet(initial.DocumentId, out SolidWorksDocumentDescriptor? current));
        Assert.Equal(newer, current);
    }

    [Fact]
    public void RegistryRejectsCandidateWithDifferentDocumentIdentity()
    {
        var registry = new SolidWorksDocumentRegistry();
        SolidWorksDocumentDescriptor initial = CreateDescriptor("sha256:initial");
        registry.Add(initial);

        SolidWorksDocumentDescriptor otherDocument = initial with
        {
            DocumentId = new DocumentId("document:other"),
            StateHash = "sha256:other",
        };

        Assert.False(registry.TryUpdateDescriptor(initial, otherDocument, out SolidWorksDocumentDescriptor? rejected));
        Assert.Null(rejected);
        Assert.True(registry.TryGet(initial.DocumentId, out SolidWorksDocumentDescriptor? current));
        Assert.Equal(initial, current);
    }

    /// <summary>
    /// Creates a deterministic descriptor for registry-only tests; no path is opened or written.
    /// 创建 registry-only test 使用的确定性 descriptor；不会打开或写入任何 path。
    /// </summary>
    private static SolidWorksDocumentDescriptor CreateDescriptor(string stateHash) => new(
        new DocumentId("document:part"),
        CadDocumentType.Part,
        "C:\\isolated\\part.sldprt",
        "part",
        "Default",
        stateHash,
        IsDirty: false,
        ProfileFeatureName: "Sketch1");
}
