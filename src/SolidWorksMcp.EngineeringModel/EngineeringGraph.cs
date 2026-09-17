using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SolidWorksMcp.EngineeringModel;

/// <summary>
/// Identifies the engineering graph bounded by one immutable graph document.
/// 标识一个 immutable graph document 所表达的工程语义图类型。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EngineeringGraphKind
{
    /// <summary>Design intent such as a feature's purpose or a repeated-feature seed.</summary>
    /// <summary>设计意图，例如 feature purpose 或 repeated-feature seed。</summary>
    DesignIntent,

    /// <summary>Manufacturing requirements that a drawing must cover.</summary>
    /// <summary>工程图必须覆盖的制造要求。</summary>
    ManufacturingRequirement,

    /// <summary>Assembly interfaces and component relationships.</summary>
    /// <summary>装配接口和 component relationship。</summary>
    AssemblyInterface,

    /// <summary>Functional dimensions, tolerances and stack-up relationships.</summary>
    /// <summary>功能尺寸、公差和公差链关系。</summary>
    Tolerance,

    /// <summary>Drawing views, annotations and coverage semantics.</summary>
    /// <summary>工程图 views、annotations 和 coverage 语义。</summary>
    DrawingSemantic,
}

/// <summary>
/// Stable node identity classes. Geometry, CAD features, requirements and annotations intentionally remain distinct.
/// 稳定 node identity 类别；geometry、CAD feature、requirement、annotation 必须保持不同 identity。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EngineeringGraphNodeKind
{
    GeometricEntity,
    CadFeature,
    EngineeringRequirement,
    DrawingView,
    DrawingAnnotation,
    ComponentDefinition,
    ComponentInstance,
    FunctionalDimension,
    Datum,
    PatternGroup,
}

/// <summary>
/// Typed semantic relationship between two graph nodes.
/// 两个 graph node 之间的 typed semantic relationship。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EngineeringGraphEdgeKind
{
    DerivedFrom,
    Satisfies,
    DependsOn,
    Represents,
    ConstrainedBy,
    MemberOf,
    RepeatedAs,
    LocatedBy,
    Covers,
    InterfacesWith,
}

/// <summary>
/// One immutable engineering graph node with stable identity and explicit scalar attributes.
/// 一个 immutable engineering graph node，具有稳定 identity 和显式 scalar attributes。
/// </summary>
public sealed class EngineeringGraphNode(
    string nodeId,
    EngineeringGraphNodeKind kind,
    string name,
    IReadOnlyDictionary<string, string>? attributes = null)
{
    /// <summary>Stable identity independent of regenerated CAD or drawing enumeration order.</summary>
    /// <summary>独立于 CAD/drawing enumeration 顺序的稳定 identity。</summary>
    public string NodeId { get; } = RequireNonBlank(nodeId, nameof(nodeId));

    /// <summary>Semantic node kind, not a vendor COM type.</summary>
    /// <summary>语义 node kind，不是 vendor COM type。</summary>
    public EngineeringGraphNodeKind Kind { get; } = kind;

    /// <summary>Stable display name used for diagnostics and explainability.</summary>
    /// <summary>用于 diagnostics 和 explainability 的稳定 display name。</summary>
    public string Name { get; } = RequireNonBlank(name, nameof(name));

    /// <summary>Small deterministic scalar attributes; large or secret source payloads are not allowed here.</summary>
    /// <summary>少量确定性的 scalar attributes；这里不允许 large/secret source payload。</summary>
    public ImmutableDictionary<string, string> Attributes { get; } = (attributes ?? new Dictionary<string, string>(StringComparer.Ordinal))
        .ToImmutableDictionary(
            pair => RequireNonBlank(pair.Key, nameof(attributes)),
            pair => pair.Value ?? string.Empty,
            StringComparer.Ordinal);

    private static string RequireNonBlank(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}

/// <summary>
/// One immutable directed semantic edge. Both endpoints must be present in the containing graph.
/// 一个 immutable directed semantic edge；两个 endpoint 必须存在于所属 graph。
/// </summary>
public sealed class EngineeringGraphEdge
{
    /// <summary>Creates an edge after validating its endpoint identities.</summary>
    /// <summary>创建 edge，并校验 endpoint identities。</summary>
    [JsonConstructor]
    public EngineeringGraphEdge(
        string sourceNodeId,
        string targetNodeId,
        EngineeringGraphEdgeKind kind,
        string? rationale = null)
    {
        SourceNodeId = RequireNonBlank(sourceNodeId, nameof(sourceNodeId));
        TargetNodeId = RequireNonBlank(targetNodeId, nameof(targetNodeId));
        if (SourceNodeId.Equals(TargetNodeId, StringComparison.Ordinal))
        {
            throw new ArgumentException("A semantic edge cannot connect a node to itself.", nameof(targetNodeId));
        }

        Kind = kind;
        Rationale = string.IsNullOrWhiteSpace(rationale) ? null : rationale.Trim();
    }

    /// <summary>Source node identity.</summary>
    /// <summary>source node identity。</summary>
    public string SourceNodeId { get; }

    /// <summary>Target node identity.</summary>
    /// <summary>target node identity。</summary>
    public string TargetNodeId { get; }

    /// <summary>Typed relationship kind.</summary>
    /// <summary>typed relationship kind。</summary>
    public EngineeringGraphEdgeKind Kind { get; }

    /// <summary>Optional short, non-secret reason for the relationship.</summary>
    /// <summary>可选的简短、非秘密 relationship reason。</summary>
    public string? Rationale { get; }

    private static string RequireNonBlank(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}

/// <summary>
/// Describes a node whose stable identity is unchanged but whose semantic payload changed between two graph snapshots.
/// 描述两个 graph snapshot 之间 stable identity 未变、但 semantic payload 已变化的 node。
/// </summary>
public sealed record EngineeringGraphNodeChange(
    EngineeringGraphNode Before,
    EngineeringGraphNode After);

/// <summary>
/// Describes an edge whose endpoints and relationship kind are unchanged but whose provenance rationale changed.
/// 描述两个 endpoint 和 relationship kind 未变、但 provenance rationale 发生变化的 edge。
/// </summary>
public sealed record EngineeringGraphEdgeChange(
    EngineeringGraphEdge Before,
    EngineeringGraphEdge After);

/// <summary>
/// Deterministic immutable difference between two versioned engineering graph snapshots.
/// 两个 versioned engineering graph snapshot 之间的 deterministic immutable 差异。
/// </summary>
/// <remarks>
/// A diff is keyed by stable node identities and edge endpoints, never by insertion order. This makes it suitable for
/// audit, planner invalidation and human review when a regenerated CAD model changes. Diff 以 stable node identity 和
/// edge endpoint 为 key，绝不依赖插入顺序，因此可以用于 audit、planner cache invalidation 和人工复核。
/// </remarks>
public sealed record EngineeringGraphDiff
{
    /// <summary>Creates one immutable graph diff.</summary>
    public EngineeringGraphDiff(
        string sourceFingerprint,
        string targetFingerprint,
        IEnumerable<EngineeringGraphNode> addedNodes,
        IEnumerable<EngineeringGraphNode> removedNodes,
        IEnumerable<EngineeringGraphNodeChange> changedNodes,
        IEnumerable<EngineeringGraphEdge> addedEdges,
        IEnumerable<EngineeringGraphEdge> removedEdges,
        IEnumerable<EngineeringGraphEdgeChange> changedEdges)
    {
        SourceFingerprint = RequireFingerprint(sourceFingerprint, nameof(sourceFingerprint));
        TargetFingerprint = RequireFingerprint(targetFingerprint, nameof(targetFingerprint));
        AddedNodes = [.. (addedNodes ?? throw new ArgumentNullException(nameof(addedNodes)))
            .OrderBy(node => node.NodeId, StringComparer.Ordinal)];
        RemovedNodes = [.. (removedNodes ?? throw new ArgumentNullException(nameof(removedNodes)))
            .OrderBy(node => node.NodeId, StringComparer.Ordinal)];
        ChangedNodes = [.. (changedNodes ?? throw new ArgumentNullException(nameof(changedNodes)))
            .OrderBy(change => change.After.NodeId, StringComparer.Ordinal)];
        AddedEdges = [.. (addedEdges ?? throw new ArgumentNullException(nameof(addedEdges)))
            .OrderBy(EdgeIdentity, StringComparer.Ordinal)];
        RemovedEdges = [.. (removedEdges ?? throw new ArgumentNullException(nameof(removedEdges)))
            .OrderBy(EdgeIdentity, StringComparer.Ordinal)];
        ChangedEdges = [.. (changedEdges ?? throw new ArgumentNullException(nameof(changedEdges)))
            .OrderBy(change => EdgeIdentity(change.After), StringComparer.Ordinal)];
    }

    /// <summary>Fingerprint of the older/source graph snapshot.</summary>
    public string SourceFingerprint { get; }

    /// <summary>Fingerprint of the newer/target graph snapshot.</summary>
    public string TargetFingerprint { get; }

    /// <summary>Nodes present only in the target snapshot.</summary>
    public ImmutableArray<EngineeringGraphNode> AddedNodes { get; }

    /// <summary>Nodes present only in the source snapshot.</summary>
    public ImmutableArray<EngineeringGraphNode> RemovedNodes { get; }

    /// <summary>Nodes present in both snapshots with changed semantic payload.</summary>
    public ImmutableArray<EngineeringGraphNodeChange> ChangedNodes { get; }

    /// <summary>Edges present only in the target snapshot.</summary>
    public ImmutableArray<EngineeringGraphEdge> AddedEdges { get; }

    /// <summary>Edges present only in the source snapshot.</summary>
    public ImmutableArray<EngineeringGraphEdge> RemovedEdges { get; }

    /// <summary>Edges with stable endpoints/kind but changed rationale/provenance.</summary>
    public ImmutableArray<EngineeringGraphEdgeChange> ChangedEdges { get; }

    /// <summary>True when both graph snapshots are semantically identical.</summary>
    public bool IsEmpty => AddedNodes.IsEmpty
        && RemovedNodes.IsEmpty
        && ChangedNodes.IsEmpty
        && AddedEdges.IsEmpty
        && RemovedEdges.IsEmpty
        && ChangedEdges.IsEmpty;

    private static string RequireFingerprint(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }

    private static string EdgeIdentity(EngineeringGraphEdge edge) =>
        $"{edge.SourceNodeId}\u001f{edge.TargetNodeId}\u001f{edge.Kind}";
}

/// <summary>
/// Versioned immutable graph kernel shared by design, manufacturing, tolerance and drawing semantics.
/// 由 design、manufacturing、tolerance、drawing semantics 共享的 versioned immutable graph kernel。
/// </summary>
/// <remarks>
/// A graph stores relationships between engineering identities; it is not a flattened list of geometry and it is not a
/// repository for source PDF/CAD text.  Graph construction validates all endpoint references before the graph can be
/// consumed by planners or release gates. Graph 存储 engineering identity 之间的关系，不是 geometry 平铺列表，也不是
/// source PDF/CAD text 仓库；构造时验证所有 endpoint，之后 planner/release gate 才能使用。
/// </remarks>
public sealed class EngineeringGraph
{
    /// <summary>Current graph schema version.</summary>
    public const string CurrentSchemaVersion = "1.0";

    /// <summary>Creates and validates one complete immutable graph.</summary>
    /// <summary>创建并验证一个完整的 immutable graph。</summary>
    public EngineeringGraph(
        string schemaVersion,
        string graphId,
        EngineeringGraphKind kind,
        IEnumerable<EngineeringGraphNode> nodes,
        IEnumerable<EngineeringGraphEdge> edges)
    {
        SchemaVersion = RequireNonBlank(schemaVersion, nameof(schemaVersion));
        if (!SchemaVersion.Equals(CurrentSchemaVersion, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Unsupported engineering graph schema '{SchemaVersion}'.", nameof(schemaVersion));
        }

        GraphId = RequireNonBlank(graphId, nameof(graphId));
        Kind = kind;
        Nodes = [.. (nodes ?? throw new ArgumentNullException(nameof(nodes)))];
        Edges = [.. (edges ?? throw new ArgumentNullException(nameof(edges)))];
        if (Nodes.Any(node => node is null))
        {
            throw new ArgumentException("Engineering graph nodes cannot contain null values.", nameof(nodes));
        }

        if (Edges.Any(edge => edge is null))
        {
            throw new ArgumentException("Engineering graph edges cannot contain null values.", nameof(edges));
        }

        if (Nodes.Select(node => node.NodeId).Distinct(StringComparer.Ordinal).Count() != Nodes.Length)
        {
            throw new ArgumentException("Engineering graph node identities must be unique.", nameof(nodes));
        }

        var nodeIds = Nodes
            .Select(node => node.NodeId)
            .ToImmutableHashSet(StringComparer.Ordinal);
        EngineeringGraphEdge? dangling = Edges.FirstOrDefault(edge =>
            !nodeIds.Contains(edge.SourceNodeId) || !nodeIds.Contains(edge.TargetNodeId));
        if (dangling is not null)
        {
            throw new ArgumentException(
                $"Engineering graph edge '{dangling.SourceNodeId}->{dangling.TargetNodeId}' references a missing node.",
                nameof(edges));
        }

        ValidateUniqueEdges();
        ValidateNoCycles();
        ValidateRequirementsAreConnected();
    }

    /// <summary>Schema version used for serialization and migrations.</summary>
    public string SchemaVersion { get; }

    /// <summary>Stable graph identity within one engineering model.</summary>
    public string GraphId { get; }

    /// <summary>Bounded engineering semantic domain.</summary>
    public EngineeringGraphKind Kind { get; }

    /// <summary>Immutable graph nodes.</summary>
    public ImmutableArray<EngineeringGraphNode> Nodes { get; }

    /// <summary>Immutable directed semantic edges.</summary>
    public ImmutableArray<EngineeringGraphEdge> Edges { get; }

    /// <summary>
    /// Compares this immutable snapshot with a target snapshot using stable identities and deterministic ordering.
    /// 使用 stable identity 和 deterministic ordering 比较当前 immutable snapshot 与 target snapshot。
    /// </summary>
    /// <param name="target">The newer graph snapshot to compare.</param>
    /// <returns>An immutable diff; empty collections mean no semantic changes.</returns>
    public EngineeringGraphDiff CreateDiff(EngineeringGraph target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var sourceNodes = Nodes.ToDictionary(node => node.NodeId, StringComparer.Ordinal);
        var targetNodes = target.Nodes.ToDictionary(node => node.NodeId, StringComparer.Ordinal);
        ImmutableArray<EngineeringGraphNode> addedNodes = [..
            target.Nodes
                .Where(node => !sourceNodes.ContainsKey(node.NodeId))
                .OrderBy(node => node.NodeId, StringComparer.Ordinal)];
        ImmutableArray<EngineeringGraphNode> removedNodes = [..
            Nodes
                .Where(node => !targetNodes.ContainsKey(node.NodeId))
                .OrderBy(node => node.NodeId, StringComparer.Ordinal)];
        ImmutableArray<EngineeringGraphNodeChange> changedNodes = [..
            target.Nodes
                .Where(node => sourceNodes.TryGetValue(node.NodeId, out EngineeringGraphNode? before)
                    && !NodeEquivalent(before!, node))
                .Select(node => new EngineeringGraphNodeChange(sourceNodes[node.NodeId], node))
                .OrderBy(change => change.After.NodeId, StringComparer.Ordinal)];

        var sourceEdges = Edges.ToDictionary(EdgeIdentity, StringComparer.Ordinal);
        var targetEdges = target.Edges.ToDictionary(EdgeIdentity, StringComparer.Ordinal);
        ImmutableArray<EngineeringGraphEdge> addedEdges = [..
            target.Edges
                .Where(edge => !sourceEdges.ContainsKey(EdgeIdentity(edge)))
                .OrderBy(EdgeIdentity, StringComparer.Ordinal)];
        ImmutableArray<EngineeringGraphEdge> removedEdges = [..
            Edges
                .Where(edge => !targetEdges.ContainsKey(EdgeIdentity(edge)))
                .OrderBy(EdgeIdentity, StringComparer.Ordinal)];
        ImmutableArray<EngineeringGraphEdgeChange> changedEdges = [..
            target.Edges
                .Where(edge => sourceEdges.TryGetValue(EdgeIdentity(edge), out EngineeringGraphEdge? before)
                    && !string.Equals(before!.Rationale, edge.Rationale, StringComparison.Ordinal))
                .Select(edge => new EngineeringGraphEdgeChange(sourceEdges[EdgeIdentity(edge)], edge))
                .OrderBy(change => EdgeIdentity(change.After), StringComparer.Ordinal)];

        return new EngineeringGraphDiff(
            CanonicalFingerprint,
            target.CanonicalFingerprint,
            addedNodes,
            removedNodes,
            changedNodes,
            addedEdges,
            removedEdges,
            changedEdges);
    }

    /// <summary>
    /// Deterministic fingerprint independent of insertion order, used to bind planner/cache evidence.
    /// 独立于 insertion order 的 deterministic fingerprint，用于绑定 planner/cache evidence。
    /// </summary>
    [JsonIgnore]
    public string CanonicalFingerprint
    {
        get
        {
            var canonical = new StringBuilder();
            canonical.Append(SchemaVersion).Append('\u001f').Append(GraphId).Append('\u001f').Append(Kind);
            foreach (EngineeringGraphNode node in Nodes.OrderBy(value => value.NodeId, StringComparer.Ordinal))
            {
                canonical.Append('\u001e').Append(node.NodeId).Append('\u001f').Append(node.Kind).Append('\u001f').Append(node.Name);
                foreach (KeyValuePair<string, string> attribute in node.Attributes.OrderBy(value => value.Key, StringComparer.Ordinal))
                {
                    canonical.Append('\u001f').Append(attribute.Key).Append('=').Append(attribute.Value);
                }
            }

            foreach (EngineeringGraphEdge edge in Edges
                .OrderBy(value => value.SourceNodeId, StringComparer.Ordinal)
                .ThenBy(value => value.TargetNodeId, StringComparer.Ordinal)
                .ThenBy(value => value.Kind))
            {
                canonical.Append('\u001d')
                    .Append(edge.SourceNodeId).Append('\u001f')
                    .Append(edge.TargetNodeId).Append('\u001f')
                    .Append(edge.Kind).Append('\u001f')
                    .Append(edge.Rationale ?? string.Empty);
            }

            return $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))}";
        }
    }

    /// <summary>Serializes only the versioned graph contract, never source drawing material.</summary>
    /// <summary>只序列化 versioned graph contract，不序列化 source drawing material。</summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    /// <summary>Deserializes and re-validates a graph before returning it to the domain layer.</summary>
    /// <summary>反序列化并重新验证 graph，之后才返回给 domain layer。</summary>
    public static EngineeringGraph FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        EngineeringGraphPayload? payload = JsonSerializer.Deserialize<EngineeringGraphPayload>(json, JsonOptions) ?? throw new JsonException("Engineering graph JSON produced no graph object.");
        return new EngineeringGraph(
            payload.SchemaVersion,
            payload.GraphId,
            payload.Kind,
            payload.Nodes.Select(node => new EngineeringGraphNode(node.NodeId, node.Kind, node.Name, node.Attributes)),
            payload.Edges.Select(edge => new EngineeringGraphEdge(edge.SourceNodeId, edge.TargetNodeId, edge.Kind, edge.Rationale)));
    }

    /// <summary>
    /// Serialization-only DTOs keep the public immutable graph constructors strict while allowing JSON round-trips.
    /// serialization 专用 DTO 让 public immutable graph constructor 保持严格，同时支持 JSON round-trip。
    /// </summary>
    private sealed record EngineeringGraphPayload(
        string SchemaVersion,
        string GraphId,
        EngineeringGraphKind Kind,
        IReadOnlyList<EngineeringGraphNodePayload> Nodes,
        IReadOnlyList<EngineeringGraphEdgePayload> Edges);

    private sealed record EngineeringGraphNodePayload(
        string NodeId,
        EngineeringGraphNodeKind Kind,
        string Name,
        IReadOnlyDictionary<string, string> Attributes);

    private sealed record EngineeringGraphEdgePayload(
        string SourceNodeId,
        string TargetNodeId,
        EngineeringGraphEdgeKind Kind,
        string? Rationale);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Rejects duplicate relationship identities before a graph can be consumed by a planner.</summary>
    /// <summary>在 graph 被 planner 消费前拒绝重复 relationship identity。</summary>
    private void ValidateUniqueEdges()
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (EngineeringGraphEdge edge in Edges)
        {
            if (!identities.Add(EdgeIdentity(edge)))
            {
                throw new ArgumentException(
                    $"Engineering graph relationship '{EdgeIdentity(edge)}' is duplicated.",
                    nameof(Edges));
            }
        }
    }

    /// <summary>
    /// Rejects directed cycles because the current engineering graph is a dependency/provenance DAG.
    /// 拒绝有向环；当前 engineering graph 是 dependency/provenance DAG。
    /// </summary>
    private void ValidateNoCycles()
    {
        var outgoing = Edges
            .GroupBy(edge => edge.SourceNodeId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(edge => edge.TargetNodeId).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);
        var path = new List<string>();

        foreach (EngineeringGraphNode node in Nodes.OrderBy(value => value.NodeId, StringComparer.Ordinal))
        {
            Visit(node.NodeId);
        }

        void Visit(string nodeId)
        {
            if (visited.Contains(nodeId))
            {
                return;
            }

            if (!active.Add(nodeId))
            {
                int start = path.IndexOf(nodeId);
                string cycle = string.Join(" -> ", path.Skip(Math.Max(0, start)).Append(nodeId));
                throw new ArgumentException($"Engineering graph contains a directed cycle: {cycle}.", nameof(Edges));
            }

            path.Add(nodeId);
            if (outgoing.TryGetValue(nodeId, out string[]? targets))
            {
                foreach (string target in targets)
                {
                    Visit(target);
                }
            }

            path.RemoveAt(path.Count - 1);
            active.Remove(nodeId);
            visited.Add(nodeId);
        }
    }

    /// <summary>Rejects isolated engineering requirements that cannot be traced to any semantic relation.</summary>
    /// <summary>拒绝孤立的 engineering requirement；孤立 requirement 无法追溯到任何 semantic relation。</summary>
    private void ValidateRequirementsAreConnected()
    {
        var connected = Edges
            .SelectMany(edge => new[] { edge.SourceNodeId, edge.TargetNodeId })
            .ToImmutableHashSet(StringComparer.Ordinal);
        EngineeringGraphNode? orphan = Nodes.FirstOrDefault(node =>
            node.Kind is EngineeringGraphNodeKind.EngineeringRequirement
            && !connected.Contains(node.NodeId));
        if (orphan is not null)
        {
            throw new ArgumentException(
                $"Engineering requirement '{orphan.NodeId}' is orphaned and has no semantic relationship.",
                nameof(Nodes));
        }
    }

    private static bool NodeEquivalent(EngineeringGraphNode left, EngineeringGraphNode right)
    {
        return left.Kind == right.Kind
            && left.Name.Equals(right.Name, StringComparison.Ordinal)
            && left.Attributes.Count == right.Attributes.Count
            && left.Attributes.All(pair => right.Attributes.TryGetValue(pair.Key, out string? value)
                && value.Equals(pair.Value, StringComparison.Ordinal));
    }

    private static string EdgeIdentity(EngineeringGraphEdge edge) =>
        $"{edge.SourceNodeId}\u001f{edge.TargetNodeId}\u001f{edge.Kind}";

    private static string RequireNonBlank(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}
