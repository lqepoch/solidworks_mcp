using System.Collections.Immutable;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.CadAbstractions;

/// <summary>Common document identity and state visible without vendor types.</summary>
public sealed record CadDocumentSummary
{
    /// <summary>Document identity.</summary>
    public required DocumentId DocumentId { get; init; }

    /// <summary>Document kind.</summary>
    public required CadDocumentType DocumentType { get; init; }

    /// <summary>Provider-visible path, which may be empty for unsaved documents.</summary>
    public required string Path { get; init; }

    /// <summary>Active configuration.</summary>
    public required string Configuration { get; init; }

    /// <summary>Deterministic state hash at inspection time.</summary>
    public required string StateHash { get; init; }

    /// <summary>Whether mutations are not yet persisted.</summary>
    public required bool IsDirty { get; init; }
}

/// <summary>Inspection snapshot of one body.</summary>
public sealed record BodySnapshot
{
    /// <summary>Stable body identity.</summary>
    public required BodyId BodyId { get; init; }

    /// <summary>Semantic body name.</summary>
    public required string Name { get; init; }

    /// <summary>Number of features contributing to the body.</summary>
    public required int FeatureCount { get; init; }

    /// <summary>Bounding-box minimum in canonical millimetres.</summary>
    public required Coordinate3D BoundingBoxMinimum { get; init; }

    /// <summary>Bounding-box maximum in canonical millimetres.</summary>
    public required Coordinate3D BoundingBoxMaximum { get; init; }

    /// <summary>Measured volume in cubic millimetres.</summary>
    public required Volume Volume { get; init; }

    /// <summary>Measured mass in kilograms.</summary>
    public required Mass Mass { get; init; }
}

/// <summary>Inspection snapshot of one model feature.</summary>
public sealed record FeatureSnapshot
{
    /// <summary>Stable feature identity.</summary>
    public required FeatureId FeatureId { get; init; }

    /// <summary>Semantic feature name.</summary>
    public required string Name { get; init; }

    /// <summary>Vendor-neutral feature kind.</summary>
    public required string Kind { get; init; }

    /// <summary>Owning body identity.</summary>
    public required BodyId BodyId { get; init; }

    /// <summary>Optional extrusion depth or analogous linear feature value.</summary>
    public Length? Depth { get; init; }
}

/// <summary>
/// Inspection evidence for one topological entity that can be fed back into the declarative selection boundary.
/// 可回传给声明式 selection boundary 的单个拓扑实体 inspection evidence。
/// </summary>
/// <remarks>
/// PersistentReference is the preferred round-trip token. GeometrySignature is optional because some SOLIDWORKS
/// topology (notably native faces without an assigned imported-body face ID) has no safe, unique geometry fallback.
/// PersistentReference 是首选回传 token。GeometrySignature 可以为空，因为部分 SOLIDWORKS topology（尤其没有
/// imported-body face ID 的 native face）没有安全且唯一的 geometry fallback。
/// </remarks>
public sealed record CadTopologyEntitySnapshot
{
    /// <summary>Vendor-neutral entity kind.</summary>
    public required CadEntityKind EntityKind { get; init; }

    /// <summary>Provider identity for audit correlation; never an enumeration ordinal.</summary>
    public required string Identity { get; init; }

    /// <summary>Optional provider-owned geometry search evidence.</summary>
    public CadGeometrySignature? GeometrySignature { get; init; }

    /// <summary>Opaque persistent reference captured on the provider STA, when supported.</summary>
    public CadPersistentReference? PersistentReference { get; init; }
}

/// <summary>Verified snapshot of one named model dimension.</summary>
/// <remarks>
/// The value is read back from the CAD parameter after mutation/rebuild; it is not copied from a request alone.
/// value 必须在 mutation/rebuild 后从 CAD parameter 读回，不能只把 request 当成成功证据。
/// </remarks>
public sealed record DimensionSnapshot
{
    /// <summary>Stable provider-neutral dimension identity.</summary>
    public required DimensionId DimensionId { get; init; }

    /// <summary>Short native dimension name, such as D1.</summary>
    public required string Name { get; init; }

    /// <summary>Full native parameter name used for resolution.</summary>
    public required string FullName { get; init; }

    /// <summary>Configuration in which the value was verified.</summary>
    public required string Configuration { get; init; }

    /// <summary>Read-back value in canonical millimetres.</summary>
    public required Length Value { get; init; }

    /// <summary>Whether SOLIDWORKS reports this dimension as read-only.</summary>
    public required bool IsReadOnly { get; init; }
}

/// <summary>Inspection snapshot of one assembly component instance.</summary>
public sealed record ComponentSnapshot
{
    /// <summary>Stable instance identity.</summary>
    public required ComponentId ComponentId { get; init; }

    /// <summary>Referenced model document identity.</summary>
    public required DocumentId ReferencedDocumentId { get; init; }

    /// <summary>Referenced configuration.</summary>
    public required string Configuration { get; init; }

    /// <summary>Placement origin in canonical millimetres.</summary>
    public required Coordinate3D Origin { get; init; }

    /// <summary>Actual loading state.</summary>
    public required CadLoadState LoadState { get; init; }
}

/// <summary>Inspection snapshot of one assembly mate.</summary>
public sealed record MateSnapshot
{
    /// <summary>Stable mate identity.</summary>
    public required FeatureId MateId { get; init; }

    /// <summary>Mate semantic name.</summary>
    public required string Name { get; init; }

    /// <summary>First component identity.</summary>
    public required ComponentId FirstComponentId { get; init; }

    /// <summary>Second component identity.</summary>
    public required ComponentId SecondComponentId { get; init; }

    /// <summary>Semantic mate type.</summary>
    public required string MateType { get; init; }

    /// <summary>Optional distance constraint.</summary>
    public Length? Distance { get; init; }
}

/// <summary>Inspection snapshot of one drawing view.</summary>
public sealed record DrawingViewSnapshot
{
    /// <summary>Stable view identity.</summary>
    public required ViewId ViewId { get; init; }

    /// <summary>Semantic view name.</summary>
    public required string Name { get; init; }

    /// <summary>Model orientation name.</summary>
    public required string Orientation { get; init; }

    /// <summary>Paper-space position.</summary>
    public required Coordinate2D Position { get; init; }

    /// <summary>Optional scale denominator.</summary>
    public int? ScaleDenominator { get; init; }

    /// <summary>
    /// Native paper-space outline read from the provider after view creation. Null means the provider could not prove
    /// the outline and the drawing must not be treated as release-ready.
    /// Provider 创建视图后读回的 native 纸空间轮廓；为空表示无法证明轮廓，工程图不能当作可发布结果。
    /// </summary>
    public DrawingViewOutlineSnapshot? Outline { get; init; }
}

/// <summary>Provider-neutral native drawing-view outline in canonical millimetres.</summary>
/// <summary>使用统一毫米表达的厂商无关 native drawing view 轮廓。</summary>
public sealed record DrawingViewOutlineSnapshot
{
    public required Length Left { get; init; }

    public required Length Bottom { get; init; }

    public required Length Right { get; init; }

    public required Length Top { get; init; }

    public Length Width => Length.FromMillimeters(Right.Millimeters - Left.Millimeters);

    public Length Height => Length.FromMillimeters(Top.Millimeters - Bottom.Millimeters);
}

/// <summary>
/// Read-back of the effective native sheet. This is deliberately separate from a RulePack request so a wrong template,
/// Letter sheet or wrong projection cannot be hidden by a planner's estimate.
/// 有效 native 图纸的读回结果；它与 RulePack request 分离，防止错误模板、Letter 图幅或错误投影被 planner 估算掩盖。
/// </summary>
public sealed record DrawingSheetSnapshot
{
    public required string Name { get; init; }

    public required string PaperSize { get; init; }

    public required Length Width { get; init; }

    public required Length Height { get; init; }

    public required bool IsLandscape { get; init; }

    public required string ProjectionMethod { get; init; }

    /// <summary>Native template path/name reported by SOLIDWORKS; callers should redact local roots in public logs.</summary>
    public required string TemplateName { get; init; }
}

/// <summary>Inspection snapshot of one drawing annotation.</summary>
public sealed record DrawingAnnotationSnapshot
{
    /// <summary>Stable annotation identity.</summary>
    public required AnnotationId AnnotationId { get; init; }

    /// <summary>Owning view identity.</summary>
    public required ViewId ViewId { get; init; }

    /// <summary>Semantic annotation kind.</summary>
    public required string Kind { get; init; }

    /// <summary>Visible annotation text.</summary>
    public required string Text { get; init; }

    /// <summary>
    /// Stable engineering coverage keys preserved from the semantic annotation request.
    /// 从语义标注请求保留下来的稳定工程覆盖 key；不把原始 PDF 文字当作 QA 证据。
    /// </summary>
    public ImmutableArray<string> CoverageKeys { get; init; } = [];

    /// <summary>Paper-space position.</summary>
    public required Coordinate2D Position { get; init; }
}

/// <summary>Complete deterministic inspection result used by FakeCad and native providers.</summary>
public sealed record CadInspectionSnapshot
{
    /// <summary>Common document summary.</summary>
    public required CadDocumentSummary Document { get; init; }

    /// <summary>Part bodies; empty for non-part documents.</summary>
    public ImmutableArray<BodySnapshot> Bodies { get; init; } = [];

    /// <summary>Model features; empty when not available.</summary>
    public ImmutableArray<FeatureSnapshot> Features { get; init; } = [];

    /// <summary>
    /// Topology evidence suitable for a subsequent declarative selector; empty when the provider cannot capture it.
    /// 可供后续声明式 selector 使用的拓扑 evidence；Provider 无法安全 capture 时为空。
    /// </summary>
    public ImmutableArray<CadTopologyEntitySnapshot> TopologyEntities { get; init; } = [];

    /// <summary>
    /// Structured rebuild/What's Wrong diagnostics captured during inspection.
    /// inspection 期间采集的结构化 rebuild/What's Wrong 诊断；不会用截图或裸 bool 替代它。
    /// </summary>
    public ImmutableArray<CadDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>True when at least one error-level diagnostic is present.</summary>
    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Severity is CadDiagnosticSeverity.Error);

    /// <summary>Assembly components; empty for non-assembly documents.</summary>
    public ImmutableArray<ComponentSnapshot> Components { get; init; } = [];

    /// <summary>Assembly mates; empty for non-assembly documents.</summary>
    public ImmutableArray<MateSnapshot> Mates { get; init; } = [];

    /// <summary>Drawing views; empty for non-drawing documents.</summary>
    public ImmutableArray<DrawingViewSnapshot> Views { get; init; } = [];

    /// <summary>Effective drawing sheet, present only for drawing documents.</summary>
    public DrawingSheetSnapshot? Sheet { get; init; }

    /// <summary>Drawing annotations; empty for non-drawing documents.</summary>
    public ImmutableArray<DrawingAnnotationSnapshot> Annotations { get; init; } = [];
}

/// <summary>Evidence-bearing completion receipt for a successful rebuild.</summary>
public sealed record RebuildReceipt
{
    /// <summary>Gets the resulting state hash.</summary>
    public required string StateHash { get; init; }

    /// <summary>Gets whether the provider reported rebuild errors.</summary>
    public required bool HasErrors { get; init; }

    /// <summary>Gets structured diagnostics captured after the rebuild.</summary>
    public ImmutableArray<CadDiagnostic> Diagnostics { get; init; } = [];
}

/// <summary>Evidence-bearing completion receipt for a successful save.</summary>
public sealed record SaveReceipt
{
    /// <summary>Gets the persisted document path.</summary>
    public required string Path { get; init; }

    /// <summary>Gets the persisted state hash.</summary>
    public required string StateHash { get; init; }
}

/// <summary>Evidence-bearing completion receipt for an export operation.</summary>
public sealed record ExportReceipt
{
    /// <summary>Gets the exported target path.</summary>
    public required string TargetPath { get; init; }

    /// <summary>Gets the exported format identifier.</summary>
    public required string Format { get; init; }

    /// <summary>Gets the source state hash used for export.</summary>
    public required string SourceStateHash { get; init; }
}

/// <summary>Evidence returned after a native annotation-position repair.</summary>
public sealed record DrawingRepairReceipt
{
    /// <summary>Stable action code materialized by the provider.</summary>
    public required string ActionCode { get; init; }

    /// <summary>Exact annotation identity changed by the provider.</summary>
    public required AnnotationId AnnotationId { get; init; }

    /// <summary>Position read back from the native drawing after rebuild.</summary>
    public required Coordinate2D Position { get; init; }

    /// <summary>Resulting document state hash after the verified mutation.</summary>
    public required string StateHash { get; init; }
}

/// <summary>Evidence returned after a native drawing-view position repair.</summary>
public sealed record DrawingViewRepairReceipt
{
    /// <summary>Stable action code materialized by the provider.</summary>
    public required string ActionCode { get; init; }

    /// <summary>Exact drawing-view identity changed by the provider.</summary>
    public required ViewId ViewId { get; init; }

    /// <summary>Position read back from the native drawing after rebuild.</summary>
    public required Coordinate2D Position { get; init; }

    /// <summary>Positive native paper-space outline read back after the repair.</summary>
    public required DrawingViewOutlineSnapshot Outline { get; init; }

    /// <summary>Resulting document state hash after the verified mutation.</summary>
    public required string StateHash { get; init; }
}

/// <summary>Evidence-bearing completion receipt for a successful mutation.</summary>
public sealed record MutationReceipt
{
    /// <summary>Gets the operation name recorded by the provider.</summary>
    public required string Operation { get; init; }

    /// <summary>Gets the resulting document state hash.</summary>
    public required string StateHash { get; init; }
}
