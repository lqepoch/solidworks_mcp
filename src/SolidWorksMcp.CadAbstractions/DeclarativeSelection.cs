using System.Text.Json.Serialization;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.CadAbstractions;

/// <summary>Stable semantic kinds accepted by the declarative selection boundary.</summary>
/// <remarks>
/// Clients describe what they mean to select; they never send a SOLIDWORKS selection mark or an enumeration index.
/// 调用方只描述工程语义，不传 SOLIDWORKS 全局 Selection Mark，也不依赖易失的 enumeration index。
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CadEntityKind
{
    /// <summary>Reference plane or planar datum.</summary>
    Plane,

    /// <summary>Topological face.</summary>
    Face,

    /// <summary>Topological edge.</summary>
    Edge,

    /// <summary>Topological vertex.</summary>
    Vertex,

    /// <summary>Sketch segment or sketch entity.</summary>
    SketchEntity,

    /// <summary>Feature-tree feature.</summary>
    Feature,

    /// <summary>Solid or surface body.</summary>
    Body,

    /// <summary>Assembly component instance.</summary>
    Component,

    /// <summary>Drawing view.</summary>
    DrawingView,

    /// <summary>Drawing annotation or table/annotation object.</summary>
    Annotation,
}

/// <summary>How the provider proved a selector resolved to one entity.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CadSelectionResolution
{
    /// <summary>A vendor persistent reference resolved directly.</summary>
    PersistentReference,

    /// <summary>A deterministic geometry signature resolved the entity after reference fallback.</summary>
    GeometrySignature,

    /// <summary>A named semantic selector resolved the entity.</summary>
    SemanticSelector,
}

/// <summary>Opaque persistent-reference token owned by a provider.</summary>
/// <remarks>
/// The token is serialized as text so no vendor SAFEARRAY or RCW crosses the abstraction boundary.  The Format
/// identifies the provider-specific encoding and prevents a token from one API generation being silently reused.
/// Token 以文本传输，厂商 SAFEARRAY 和 RCW 不穿过抽象边界；Format 防止不同 API generation 的 token 被误复用。
/// </remarks>
public sealed record CadPersistentReference
{
    /// <summary>Base64 or provider-defined opaque token; callers must not parse it.</summary>
    public required string Token { get; init; }

    /// <summary>Provider encoding identifier, for example <c>solidworks.persist3</c>.</summary>
    public string Format { get; init; } = "solidworks.persist3";
}

/// <summary>Provider-neutral, canonical geometry-signature fallback.</summary>
/// <remarks>
/// A signature is evidence, not an enumeration position.  Providers must include enough deterministic geometry/topology
/// facts to detect ambiguity and must return review/stale errors when more than one entity matches.
/// Signature 是证据，不是 enumeration position；Provider 必须能检测歧义，多个匹配时返回 review/stale，而不是猜一个。
/// </remarks>
public sealed record CadGeometrySignature
{
    /// <summary>Schema identifier for the canonical signature encoding.</summary>
    public string SchemaVersion { get; init; } = "geometry-signature/1";

    /// <summary>Opaque canonical signature payload.</summary>
    public required string Value { get; init; }
}

/// <summary>Declarative selector for one document entity.</summary>
/// <remarks>
/// Resolution precedence is persistent reference → geometry signature → semantic name.  ExpectedStateHash is an
/// optimistic concurrency guard: a selector captured before a rebuild cannot silently target a changed topology.
/// 解析优先级为 persistent reference → geometry signature → semantic name。ExpectedStateHash 是并发 guard，
/// rebuild 后旧 selector 不能静默指向新 topology。
/// </remarks>
public sealed record CadEntitySelector
{
    /// <summary>Document that owns the entity.</summary>
    public required DocumentId DocumentId { get; init; }

    /// <summary>Expected semantic entity kind.</summary>
    public required CadEntityKind EntityKind { get; init; }

    /// <summary>Optional human/engineering name, used only after stronger references.</summary>
    public string? SemanticName { get; init; }

    /// <summary>Optional provider persistent reference.</summary>
    public CadPersistentReference? PersistentReference { get; init; }

    /// <summary>Optional deterministic geometry/topology fallback.</summary>
    public CadGeometrySignature? GeometrySignature { get; init; }

    /// <summary>Optional document state hash captured when this selector was produced.</summary>
    public string? ExpectedStateHash { get; init; }

    /// <summary>Gets whether at least one resolution hint is present.</summary>
    [JsonIgnore]
    public bool HasResolutionHint => PersistentReference is not null
        || GeometrySignature is not null
        || !string.IsNullOrWhiteSpace(SemanticName);
}

/// <summary>Resolved, vendor-neutral entity identity returned to orchestration code.</summary>
public sealed record CadEntityReference
{
    /// <summary>Resolved entity kind.</summary>
    public required CadEntityKind EntityKind { get; init; }

    /// <summary>Stable provider identity, never a transient enumeration index.</summary>
    public required string Identity { get; init; }
}

/// <summary>Evidence-bearing result of declarative selector resolution.</summary>
public sealed record CadSelectionSnapshot
{
    /// <summary>Document owning the resolved entity.</summary>
    public required DocumentId DocumentId { get; init; }

    /// <summary>Resolved stable entity identity.</summary>
    public required CadEntityReference Entity { get; init; }

    /// <summary>Resolution strategy that produced the identity.</summary>
    public required CadSelectionResolution Resolution { get; init; }

    /// <summary>Current state hash proved during resolution.</summary>
    public required string StateHash { get; init; }

    /// <summary>Stable hash of the declarative selector used for audit correlation.</summary>
    public required string SelectorFingerprint { get; init; }

    /// <summary>
    /// Opaque provider reference captured during successful resolution, when the provider can persist the entity.
    /// 成功解析时由 Provider 捕获的不透明 persistent reference；Provider 无法安全持久化时可以为空。
    /// </summary>
    /// <remarks>
    /// Callers must round-trip this value without parsing the token.  The format prevents a reference from one CAD
    /// provider or API generation from being silently sent to another. 调用方只能原样回传，不能解析 token；Format
    /// 防止不同 CAD Provider 或 API generation 的 reference 被静默混用。
    /// </remarks>
    public CadPersistentReference? PersistentReference { get; init; }
}
