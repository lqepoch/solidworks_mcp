using SolidWorksMcp.EngineeringModel;

namespace SolidWorksMcp.UnitTests;

/// <summary>
/// Protects the immutable engineering graph kernel used by the single-part compiler.
/// 保护单零件 compiler 使用的 immutable engineering graph kernel。
/// </summary>
public sealed class EngineeringGraphTests
{
    [Fact]
    public void GraphRoundTripsWithoutCollapsingFeatureAndRequirementIdentity()
    {
        EngineeringGraph graph = CreateGraph();

        var roundTrip = EngineeringGraph.FromJson(graph.ToJson());

        Assert.Equal(graph.CanonicalFingerprint, roundTrip.CanonicalFingerprint);
        Assert.Contains(roundTrip.Nodes, node => node.Kind is EngineeringGraphNodeKind.CadFeature);
        Assert.Contains(roundTrip.Nodes, node => node.Kind is EngineeringGraphNodeKind.EngineeringRequirement);
        Assert.Contains(roundTrip.Edges, edge => edge.Kind is EngineeringGraphEdgeKind.Satisfies);
    }

    [Fact]
    public void CanonicalFingerprintIgnoresInsertionOrder()
    {
        EngineeringGraph first = CreateGraph();
        EngineeringGraph second = new(
            EngineeringGraph.CurrentSchemaVersion,
            "part-001",
            EngineeringGraphKind.ManufacturingRequirement,
            first.Nodes.Reverse(),
            first.Edges.Reverse());

        Assert.Equal(first.CanonicalFingerprint, second.CanonicalFingerprint);
    }

    [Fact]
    public void GraphRejectsDuplicateNodeIdentity()
    {
        EngineeringGraphNode node = new("feature:seed", EngineeringGraphNodeKind.CadFeature, "Seed Hole");

        Assert.Throws<ArgumentException>(() => new EngineeringGraph(
            EngineeringGraph.CurrentSchemaVersion,
            "part-duplicate",
            EngineeringGraphKind.DesignIntent,
            [node, node],
            []));
    }

    [Fact]
    public void GraphRejectsDanglingEdge()
    {
        EngineeringGraphNode node = new("feature:seed", EngineeringGraphNodeKind.CadFeature, "Seed Hole");

        Assert.Throws<ArgumentException>(() => new EngineeringGraph(
            EngineeringGraph.CurrentSchemaVersion,
            "part-dangling",
            EngineeringGraphKind.DesignIntent,
            [node],
            [new EngineeringGraphEdge("feature:seed", "requirement:missing", EngineeringGraphEdgeKind.Satisfies)]));
    }

    private static EngineeringGraph CreateGraph() => new(
        EngineeringGraph.CurrentSchemaVersion,
        "part-001",
        EngineeringGraphKind.ManufacturingRequirement,
        [
            new EngineeringGraphNode(
                "feature:seed",
                EngineeringGraphNodeKind.CadFeature,
                "Seed Hole",
                new Dictionary<string, string>
                {
                    ["diameter-mm"] = "12",
                    ["quantity"] = "1",
                }),
            new EngineeringGraphNode(
                "requirement:hole-pattern",
                EngineeringGraphNodeKind.EngineeringRequirement,
                "Hole pattern coverage",
                new Dictionary<string, string> { ["coverage-key"] = "features.hole.size-location-callout" }),
        ],
        [new EngineeringGraphEdge(
            "feature:seed",
            "requirement:hole-pattern",
            EngineeringGraphEdgeKind.Satisfies,
            "native-hole-feature")]);
}
