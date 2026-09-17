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

    [Fact]
    public void GraphRejectsOrphanEngineeringRequirement()
    {
        EngineeringGraphNode requirement = new(
            "requirement:orphan",
            EngineeringGraphNodeKind.EngineeringRequirement,
            "Untraced manufacturing requirement");

        ArgumentException exception = Assert.Throws<ArgumentException>(() => new EngineeringGraph(
            EngineeringGraph.CurrentSchemaVersion,
            "part-orphan",
            EngineeringGraphKind.ManufacturingRequirement,
            [requirement],
            []));

        Assert.Contains("orphaned", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GraphRejectsDirectedDependencyCycle()
    {
        EngineeringGraphNode feature = new("feature:seed", EngineeringGraphNodeKind.CadFeature, "Seed Hole");
        EngineeringGraphNode requirement = new(
            "requirement:hole",
            EngineeringGraphNodeKind.EngineeringRequirement,
            "Hole coverage");

        ArgumentException exception = Assert.Throws<ArgumentException>(() => new EngineeringGraph(
            EngineeringGraph.CurrentSchemaVersion,
            "part-cycle",
            EngineeringGraphKind.ManufacturingRequirement,
            [feature, requirement],
            [
                new EngineeringGraphEdge("feature:seed", "requirement:hole", EngineeringGraphEdgeKind.Satisfies),
                new EngineeringGraphEdge("requirement:hole", "feature:seed", EngineeringGraphEdgeKind.DependsOn),
            ]));

        Assert.Contains("directed cycle", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GraphRejectsDuplicateRelationshipIdentity()
    {
        EngineeringGraphNode feature = new("feature:seed", EngineeringGraphNodeKind.CadFeature, "Seed Hole");
        EngineeringGraphNode requirement = new(
            "requirement:hole",
            EngineeringGraphNodeKind.EngineeringRequirement,
            "Hole coverage");

        ArgumentException exception = Assert.Throws<ArgumentException>(() => new EngineeringGraph(
            EngineeringGraph.CurrentSchemaVersion,
            "part-duplicate-edge",
            EngineeringGraphKind.ManufacturingRequirement,
            [feature, requirement],
            [
                new EngineeringGraphEdge("feature:seed", "requirement:hole", EngineeringGraphEdgeKind.Satisfies, "first"),
                new EngineeringGraphEdge("feature:seed", "requirement:hole", EngineeringGraphEdgeKind.Satisfies, "second"),
            ]));

        Assert.Contains("duplicated", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GraphDiffReportsStableNodeAndEdgeChanges()
    {
        EngineeringGraph source = CreateGraph();
        EngineeringGraph target = new(
            EngineeringGraph.CurrentSchemaVersion,
            source.GraphId,
            source.Kind,
            [
                new EngineeringGraphNode(
                    "feature:seed",
                    EngineeringGraphNodeKind.CadFeature,
                    "Seed Hole (updated)",
                    new Dictionary<string, string> { ["diameter-mm"] = "12", ["quantity"] = "2" }),
                new EngineeringGraphNode(
                    "requirement:hole-pattern",
                    EngineeringGraphNodeKind.EngineeringRequirement,
                    "Hole pattern coverage",
                    new Dictionary<string, string> { ["coverage-key"] = "features.hole.size-location-callout" }),
                new EngineeringGraphNode(
                    "annotation:hole-callout",
                    EngineeringGraphNodeKind.DrawingAnnotation,
                    "Hole callout"),
            ],
            [
                new EngineeringGraphEdge(
                    "feature:seed",
                    "requirement:hole-pattern",
                    EngineeringGraphEdgeKind.Satisfies,
                    "updated-native-hole-feature"),
                new EngineeringGraphEdge(
                    "requirement:hole-pattern",
                    "annotation:hole-callout",
                    EngineeringGraphEdgeKind.Covers,
                    "approved-model-item"),
            ]);

        EngineeringGraphDiff diff = source.CreateDiff(target);

        Assert.False(diff.IsEmpty);
        Assert.Single(diff.AddedNodes);
        Assert.Equal("annotation:hole-callout", diff.AddedNodes[0].NodeId);
        Assert.Single(diff.ChangedNodes);
        Assert.Equal("feature:seed", diff.ChangedNodes[0].After.NodeId);
        Assert.Single(diff.AddedEdges);
        Assert.Single(diff.ChangedEdges);
        Assert.Equal("updated-native-hole-feature", diff.ChangedEdges[0].After.Rationale);

        EngineeringGraphDiff reverse = target.CreateDiff(source);
        Assert.Single(reverse.RemovedNodes);
        Assert.Single(reverse.RemovedEdges);
        Assert.Single(reverse.ChangedNodes);
        Assert.Single(reverse.ChangedEdges);
    }

    [Fact]
    public void IdenticalGraphDiffIsEmpty()
    {
        EngineeringGraph graph = CreateGraph();

        EngineeringGraphDiff diff = graph.CreateDiff(EngineeringGraph.FromJson(graph.ToJson()));

        Assert.True(diff.IsEmpty);
        Assert.Equal(graph.CanonicalFingerprint, diff.SourceFingerprint);
        Assert.Equal(graph.CanonicalFingerprint, diff.TargetFingerprint);
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
