using System.Collections.Immutable;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.CadAbstractions;

/// <summary>Options for starting one logical provider session.</summary>
public sealed record CadSessionOptions
{
    /// <summary>Optional deterministic/requested session identity.</summary>
    public SessionId? RequestedSessionId { get; init; }

    /// <summary>Optional process binding supplied by a native provider.</summary>
    public int? RequestedProcessId { get; init; }
}

/// <summary>Request to create a part document from one optional deterministic seed profile.</summary>
public sealed record CreatePartRequest
{
    /// <summary>Optional stable identity used by FakeCad tests or idempotent callers.</summary>
    public DocumentId? RequestedDocumentId { get; init; }

    /// <summary>Optional target path; providers must apply their own allowlist policy.</summary>
    public string? Path { get; init; }

    /// <summary>Configuration name used for the new document.</summary>
    public string Configuration { get; init; } = "Default";

    /// <summary>
    /// Optional canonical radius for an initial circle sketch in the new part.
    /// 新建零件时可选的初始圆草图半径；使用显式 Length，避免 MCP/UI 把毫米误当成 SOLIDWORKS 米。
    /// </summary>
    public Length? InitialCircleRadius { get; init; }

    /// <summary>
    /// Optional centered rectangular profile for a reference-driven plate or bracket.
    /// 可选的居中矩形草图轮廓，用于参考驱动的板件或支架；与 InitialCircleRadius 互斥。
    /// </summary>
    public RectangleProfileRequest? InitialRectangle { get; init; }

    /// <summary>
    /// Optional closed planar polygon profile for reference-driven non-cylindrical parts.
    /// 可选的闭合平面多边形轮廓，用于参考驱动的非圆柱零件。
    /// </summary>
    /// <remarks>
    /// Vertices are ordered around the boundary in canonical millimetres. The profile is kept in the abstraction
    /// layer so a private drawing adapter can provide geometry without leaking vendor COM types or the source PDF.
    /// 顶点按边界顺序使用统一毫米表达；该轮廓保留在抽象层，使私密图纸适配器可以提供几何而不泄漏厂商
    /// COM 类型或源 PDF。
    /// </remarks>
    public PolygonProfileRequest? InitialPolygon { get; init; }

    /// <summary>
    /// Optional closed planar sketch made from lines and three-point arcs.
    /// 可选的闭合平面草图，由直线和三点圆弧组成。
    /// </summary>
    /// <remarks>
    /// This is the first structured profile input that can represent the rounded and D-shaped single-part drawings
    /// used by the private review workflow without embedding a vendor COM type or a private PDF value in the product.
    /// Segments are validated as one connected closed loop before the native provider creates them. 这是首个能够
    /// 表达私密图纸复核中圆角轮廓和 D 形轮廓的结构化 profile 输入；它不携带厂商 COM 类型，也不把私密 PDF
    /// 数值写入产品。Provider 建模前会验证所有 segment 是否连接成一个闭环。
    /// </remarks>
    public SketchProfileRequest? InitialSketchProfile { get; init; }
}

/// <summary>Planar rectangle used as the seed profile of a non-cylindrical part.</summary>
/// <remarks>
/// The profile is expressed in canonical millimetres and is converted only inside the native provider. The type is
/// intentionally vendor-neutral so EngineeringModel can produce it without referencing SOLIDWORKS COM. 轮廓使用统一
/// 毫米，只有 native provider 才转换为 SOLIDWORKS 米；该类型不携带任何 SOLIDWORKS COM 类型。
/// </remarks>
public sealed record RectangleProfileRequest
{
    /// <summary>Overall profile width in canonical millimetres.</summary>
    public Length Width { get; init; }

    /// <summary>Overall profile height in canonical millimetres.</summary>
    public Length Height { get; init; }
}

/// <summary>Closed planar polygon used as a deterministic seed sketch.</summary>
public sealed record PolygonProfileRequest
{
    /// <summary>Boundary vertices in counter-clockwise or clockwise order.</summary>
    public ImmutableArray<Coordinate2D> Vertices { get; init; } = [];
}

/// <summary>Connected closed planar sketch profile independent of SOLIDWORKS COM.</summary>
/// <remarks>
/// Coordinates use canonical millimetres. The first segment's start must equal the last segment's end and every
/// adjacent pair must connect within the validator tolerance. This prevents a provider from silently closing an open
/// profile with an invented edge. 坐标统一使用毫米；第一条 segment 起点必须与最后一条终点一致，所有相邻端点
/// 必须在 validator 容差内连接，避免 Provider 擅自添加“猜出来”的闭合边。
/// </remarks>
public sealed record SketchProfileRequest
{
    /// <summary>Ordered boundary curves, either line or three-point arc.</summary>
    public ImmutableArray<SketchCurveRequest> Segments { get; init; } = [];
}

/// <summary>One vendor-neutral planar sketch curve.</summary>
public sealed record SketchCurveRequest
{
    /// <summary>Curve primitive kind.</summary>
    public SketchCurveKind Kind { get; init; } = SketchCurveKind.Line;

    /// <summary>Curve start point in canonical millimetres.</summary>
    public Coordinate2D Start { get; init; }

    /// <summary>
    /// A point on the arc for <see cref="SketchCurveKind.ThreePointArc"/>; ignored for lines.
    /// 三点圆弧的圆弧通过点；直线忽略此字段。
    /// </summary>
    public Coordinate2D Through { get; init; }

    /// <summary>Curve end point in canonical millimetres.</summary>
    public Coordinate2D End { get; init; }
}

/// <summary>Supported deterministic planar sketch primitives.</summary>
public enum SketchCurveKind
{
    /// <summary>A straight segment.</summary>
    Line = 0,

    /// <summary>An arc defined by start, end and an on-arc point.</summary>
    ThreePointArc = 1,
}

/// <summary>Request to create an assembly document.</summary>
public sealed record CreateAssemblyRequest
{
    /// <summary>Optional stable identity used by deterministic tests.</summary>
    public DocumentId? RequestedDocumentId { get; init; }

    /// <summary>Optional target path; providers must apply their own allowlist policy.</summary>
    public string? Path { get; init; }

    /// <summary>Configuration name used for the new assembly.</summary>
    public string Configuration { get; init; } = "Default";
}

/// <summary>Request to create a drawing document associated with a source document.</summary>
public sealed record CreateDrawingRequest
{
    /// <summary>Optional stable identity used by deterministic tests.</summary>
    public DocumentId? RequestedDocumentId { get; init; }

    /// <summary>Optional target path; providers must apply their own allowlist policy.</summary>
    public string? Path { get; init; }

    /// <summary>Source model identity represented by this drawing.</summary>
    public DocumentId? SourceDocumentId { get; init; }

    /// <summary>Drawing configuration name.</summary>
    public string Configuration { get; init; } = "Default";
}

/// <summary>Request to create a solid body in a part.</summary>
public sealed record CreateBodyRequest
{
    /// <summary>Optional stable body identity.</summary>
    public BodyId? RequestedBodyId { get; init; }

    /// <summary>Human-readable semantic name.</summary>
    public string Name { get; init; } = "Body-1";
}

/// <summary>Request to create a simple extruded feature in a part.</summary>
public sealed record ExtrusionRequest
{
    /// <summary>Optional stable feature identity.</summary>
    public FeatureId? RequestedFeatureId { get; init; }

    /// <summary>Feature semantic name.</summary>
    public string Name { get; init; } = "Boss-Extrude-1";

    /// <summary>Signed profile extrusion depth in canonical millimetres.</summary>
    public Length Depth { get; init; }

    /// <summary>Target body identity; null means the active body.</summary>
    public BodyId? TargetBodyId { get; init; }
}

/// <summary>
/// Describes a repeated through-hole group. Centers are retained as engineering intent instead of being flattened into
/// anonymous faces. 描述重复通孔组；中心点作为工程语义保留，不退化成无名面集合。
/// </summary>
public sealed record ThroughHolePatternRequest
{
    /// <summary>Optional stable feature identity.</summary>
    public FeatureId? RequestedFeatureId { get; init; }

    /// <summary>Semantic feature name.</summary>
    public string Name { get; init; } = "HolePattern-1";

    /// <summary>Hole diameter in canonical millimetres.</summary>
    public Length Diameter { get; init; }

    /// <summary>Hole centers on the seed sketch plane in canonical millimetres.</summary>
    public ImmutableArray<Coordinate2D> Centers { get; init; } = [];

    /// <summary>Optional target body identity; null means the active solid body.</summary>
    public BodyId? TargetBodyId { get; init; }
}

/// <summary>Request to change one named model dimension in the registered active configuration.</summary>
/// <remarks>
/// The first native slice accepts the full SOLIDWORKS parameter name, for example <c>D1@Boss-Extrude-1</c>.
/// 先实现的 native slice 接收完整 SOLIDWORKS parameter name，例如 <c>D1@Boss-Extrude-1</c>。
/// </remarks>
public sealed record DimensionUpdateRequest
{
    /// <summary>Full model parameter name; short names are intentionally rejected by the native provider.</summary>
    public string ParameterName { get; init; } = string.Empty;

    /// <summary>New value in canonical millimetres, converted to SOLIDWORKS metres only at the provider boundary.</summary>
    public Length Value { get; init; }

    /// <summary>
    /// Optional configuration assertion.  When present, it must match the document's registered active configuration.
    /// 可选 configuration assertion；填写时必须匹配 document 登记的 active configuration。
    /// </summary>
    public string? Configuration { get; init; }
}

/// <summary>Request to insert one assembly component instance.</summary>
public sealed record InsertComponentRequest
{
    /// <summary>Optional stable component instance identity.</summary>
    public ComponentId? RequestedComponentId { get; init; }

    /// <summary>Referenced model document identity.</summary>
    public DocumentId ReferencedDocumentId { get; init; }

    /// <summary>Referenced configuration name.</summary>
    public string Configuration { get; init; } = "Default";

    /// <summary>Placement origin in canonical millimetres.</summary>
    public Coordinate3D Origin { get; init; }

    /// <summary>Requested load state; native providers must preserve or explicitly report degradation.</summary>
    public CadLoadState LoadState { get; init; } = CadLoadState.Resolved;
}

/// <summary>Request to add a semantic mate between two component instances.</summary>
public sealed record MateRequest
{
    /// <summary>Optional stable mate identity.</summary>
    public FeatureId? RequestedMateId { get; init; }

    /// <summary>Mate semantic name.</summary>
    public string Name { get; init; } = "Mate-1";

    /// <summary>First component identity.</summary>
    public ComponentId FirstComponentId { get; init; }

    /// <summary>Second component identity.</summary>
    public ComponentId SecondComponentId { get; init; }

    /// <summary>Semantic mate type such as coincident, concentric or distance.</summary>
    public string MateType { get; init; } = "coincident";

    /// <summary>Optional distance value for a distance mate.</summary>
    public Length? Distance { get; init; }
}

/// <summary>Request to create one drawing view.</summary>
public sealed record DrawingViewRequest
{
    /// <summary>Optional stable drawing-view identity.</summary>
    public ViewId? RequestedViewId { get; init; }

    /// <summary>Semantic view name, for example Front or Isometric.</summary>
    public string Name { get; init; } = "Front";

    /// <summary>Named model orientation used by the provider.</summary>
    public string Orientation { get; init; } = "Front";

    /// <summary>Paper-space position in canonical millimetres.</summary>
    public Coordinate2D Position { get; init; }

    /// <summary>Optional drawing scale denominator; null means provider default.</summary>
    public int? ScaleDenominator { get; init; }
}

/// <summary>
/// Request to create one native section view from an explicit parent-view cutting line.
/// 使用明确的父视图剖切线创建一个 native section view 的请求。
/// </summary>
/// <remarks>
/// Coordinates are paper-space millimetres. The provider owns the CAD sketch and selection mechanics; the request only
/// carries deterministic engineering intent and never exposes a vendor COM type. 坐标使用纸空间毫米；Provider 负责
/// CAD sketch 与 selection 细节，请求只承载确定性的工程意图，不暴露厂商 COM 类型。
/// </remarks>
public sealed record DrawingSectionViewRequest
{
    /// <summary>Optional stable identity for the generated section view.</summary>
    public ViewId? RequestedViewId { get; init; }

    /// <summary>Parent view whose exact native binding owns the cutting line.</summary>
    public required ViewId ParentViewId { get; init; }

    /// <summary>Semantic name such as Section A-A.</summary>
    public string Name { get; init; } = "Section A-A";

    /// <summary>Native section-label seed, for example A.</summary>
    public string Label { get; init; } = "A";

    /// <summary>Paper-space center of the generated section view in millimetres.</summary>
    public Coordinate2D Position { get; init; }

    /// <summary>Start point of the straight cutting line in parent-view paper coordinates.</summary>
    public Coordinate2D CutLineStart { get; init; }

    /// <summary>End point of the straight cutting line in parent-view paper coordinates.</summary>
    public Coordinate2D CutLineEnd { get; init; }

    /// <summary>Optional section scale denominator; null preserves provider/template scale.</summary>
    public int? ScaleDenominator { get; init; }

    /// <summary>Whether the native section view uses the reverse cut direction.</summary>
    public bool ChangeDirection { get; init; }

    /// <summary>Whether the section view should scale with the source model.</summary>
    public bool ScaleWithModel { get; init; } = true;
}

/// <summary>
/// Request to create one native circular detail view from a parent drawing-view region.
/// 根据父 drawing view 局部区域创建一个 native 圆形 detail view 的请求。
/// </summary>
/// <remarks>
/// All coordinates are paper-space millimetres. The parent region is intentionally explicit: the compiler must prove
/// which small feature needs enlargement instead of asking SOLIDWORKS to detail an arbitrary active selection. 所有
/// 坐标均为纸空间毫米；父视图区域必须明确，以便 compiler 证明具体哪个小特征需要放大，而不是依赖任意 active
/// selection。
/// </remarks>
public sealed record DrawingDetailViewRequest
{
    /// <summary>Optional stable identity for the generated detail view.</summary>
    public ViewId? RequestedViewId { get; init; }

    /// <summary>Parent view whose exact native binding owns the detail circle.</summary>
    public required ViewId ParentViewId { get; init; }

    /// <summary>Semantic name such as Detail A.</summary>
    public string Name { get; init; } = "Detail A";

    /// <summary>Native detail label, for example A.</summary>
    public string Label { get; init; } = "A";

    /// <summary>Paper-space center of the source detail circle in millimetres.</summary>
    public Coordinate2D DetailCenter { get; init; }

    /// <summary>Positive paper-space radius of the source detail circle in millimetres.</summary>
    public Length DetailRadius { get; init; }

    /// <summary>Paper-space center of the enlarged detail view in millimetres.</summary>
    public Coordinate2D Position { get; init; }

    /// <summary>Detail scale numerator relative to model, for example 2 in 2:1.</summary>
    public int ScaleNumerator { get; init; } = 2;

    /// <summary>Detail scale denominator relative to model, for example 1 in 2:1.</summary>
    public int ScaleDenominator { get; init; } = 1;

    /// <summary>Whether SOLIDWORKS draws the complete circle outline.</summary>
    public bool FullOutline { get; init; }

    /// <summary>Whether SOLIDWORKS uses a jagged detail-circle outline.</summary>
    public bool JaggedOutline { get; init; }
}

/// <summary>
/// Request to apply one verified annotation-position repair.
/// 应用一个经过验证的标注位置修复请求。
/// </summary>
/// <remarks>
/// This contract is intentionally narrower than a generic drawing editor. The provider resolves the exact stable
/// annotation identity, checks the expected document state and current paper-space position, then reads the native
/// position back after mutation. 该 contract 刻意窄于通用 drawing editor：Provider 必须解析稳定 annotation identity，
/// 校验期望 document state 与当前纸空间位置，并在 mutation 后读回 native position。
/// </remarks>
public sealed record DrawingAnnotationPositionRepairRequest
{
    /// <summary>Exact stable annotation identity returned by inspection.</summary>
    public required AnnotationId AnnotationId { get; init; }

    /// <summary>State hash captured when the repair plan was created.</summary>
    public required string ExpectedDocumentStateHash { get; init; }

    /// <summary>Planner precondition fingerprint retained for audit correlation.</summary>
    public required string PreconditionFingerprint { get; init; }

    /// <summary>Paper-space position that inspection observed before planning the repair.</summary>
    public required Coordinate2D ExpectedCurrentPosition { get; init; }

    /// <summary>Deterministic paper-space position selected by the layout compiler.</summary>
    public required Coordinate2D NewPosition { get; init; }
}

/// <summary>
/// Allowlisted model-item annotation categories that SOLIDWORKS can insert through IDrawingDoc.InsertModelAnnotations3.
/// 允许通过 IDrawingDoc.InsertModelAnnotations3 导入的模型标注类别；枚举值属于 vendor-neutral contract。
/// </summary>
[Flags]
public enum DrawingModelAnnotationImportKinds
{
    /// <summary>No model-item import is requested.</summary>
    None = 0,

    /// <summary>Dimensions marked or not marked for drawing.</summary>
    Dimensions = 1 << 0,

    /// <summary>Hole Wizard hole callouts.</summary>
    HoleCallouts = 1 << 1,

    /// <summary>Hole Wizard location dimensions.</summary>
    HoleWizardLocationDimensions = 1 << 2,

    /// <summary>Hole Wizard profile dimensions.</summary>
    HoleWizardProfileDimensions = 1 << 3,

    /// <summary>Pattern instance/revolution counts.</summary>
    InstanceCounts = 1 << 4,

    /// <summary>Datum feature annotations.</summary>
    Datums = 1 << 5,

    /// <summary>Datum target annotations.</summary>
    DatumTargets = 1 << 6,

    /// <summary>Geometric tolerances.</summary>
    GdAndTolerances = 1 << 7,

    /// <summary>Model notes.</summary>
    Notes = 1 << 8,

    /// <summary>Surface-finish symbols.</summary>
    SurfaceFinish = 1 << 9,

    /// <summary>Weld symbols.</summary>
    WeldSymbols = 1 << 10,

    /// <summary>Cosmetic thread annotations.</summary>
    CosmeticThreads = 1 << 11,

    /// <summary>Dimensions carrying native tolerances.</summary>
    TolerancedDimensions = 1 << 12,
}

/// <summary>Approval state required before a critical native annotation may be materialized.</summary>
public enum DrawingAnnotationApprovalState
{
    /// <summary>AI or rule proposal; it is not eligible for a release mutation.</summary>
    Proposal,

    /// <summary>Needs human review before release.</summary>
    ReviewRequired,

    /// <summary>Explicitly approved engineering intent.</summary>
    Approved,

    /// <summary>Approved and released into the current drawing revision.</summary>
    Released,
}

/// <summary>Request to add one drawing annotation associated with a view.</summary>
public sealed record DrawingAnnotationRequest
{
    /// <summary>Optional stable annotation identity.</summary>
    public AnnotationId? RequestedAnnotationId { get; init; }

    /// <summary>Owning view identity.</summary>
    public ViewId ViewId { get; init; }

    /// <summary>Annotation semantic kind such as model-dimension or note.</summary>
    public string Kind { get; init; } = "note";

    /// <summary>
    /// Optional native Model Items categories. Non-empty values use SOLIDWORKS' associative import path instead of
    /// synthesizing display text. 非空时必须通过 SOLIDWORKS native associative Model Items 路径导入，不能合成文本。
    /// </summary>
    public DrawingModelAnnotationImportKinds ModelItemKinds { get; init; }

    /// <summary>Stable feature/requirement identity used for audit correlation, never a PDF text payload.</summary>
    public string? FeatureIdentity { get; init; }

    /// <summary>Redacted provenance source class, for example model_native or pmi.</summary>
    public string? ProvenanceKind { get; init; }

    /// <summary>Redacted provenance method, for example hole_wizard or insert_model_annotations3.</summary>
    public string? ProvenanceMethod { get; init; }

    /// <summary>Approval state of the semantic requirement behind this mutation.</summary>
    public DrawingAnnotationApprovalState ApprovalState { get; init; } = DrawingAnnotationApprovalState.Proposal;

    /// <summary>Display text; source semantic identity must be carried by higher layers.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Stable engineering coverage keys explicitly associated with this annotation.
    /// 与标注显式关联的稳定工程覆盖 key；QA 不通过猜测可见文本来判定尺寸是否完整。
    /// </summary>
    public ImmutableArray<string> CoverageKeys { get; init; } = [];

    /// <summary>Paper-space position in canonical millimetres.</summary>
    public Coordinate2D Position { get; init; }
}

/// <summary>Request to export a document into a neutral or native file format.</summary>
public sealed record CadExportRequest
{
    /// <summary>Format identifier such as STEP, IGES, PDF or SLDPRT.</summary>
    public string Format { get; init; } = string.Empty;

    /// <summary>Target path after provider allowlist validation.</summary>
    public string TargetPath { get; init; } = string.Empty;

    /// <summary>Whether an existing target may be overwritten.</summary>
    public bool AllowOverwrite { get; init; }
}
