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

    /// <summary>
    /// Provider-neutral template profile selected by the engineering RulePack.
    /// 由工程 RulePack 选择的厂商无关模板 profile。
    ///
    /// The value is a semantic request, not a machine path. The native provider discovers the installed template
    /// from the running SOLIDWORKS installation and fails closed when the requested profile is unavailable. 这里是
    /// 语义请求而不是机器绝对路径；native provider 从当前 SOLIDWORKS 安装中发现模板，不可用时 fail closed。
    /// </summary>
    public DrawingTemplateProfile TemplateProfile { get; init; } = DrawingTemplateProfile.SolidWorksDefault;

    /// <summary>
    /// Explicit sheet contract selected by the RulePack/compiler. The provider must apply it and read it back;
    /// a template filename alone is never accepted as proof of paper size or projection.
    /// 由 RulePack/compiler 选择的显式图纸契约；Provider 必须应用后读回验证，不能把模板文件名当作图幅或投影证明。
    /// </summary>
    public DrawingSheetRequest? Sheet { get; init; }
}

/// <summary>
/// Vendor-neutral drawing-sheet contract. Dimensions are canonical millimetres and projection is semantic text so
/// CadAbstractions does not reference RuleEngine or SOLIDWORKS enums.
/// 厂商无关的工程图图纸契约；尺寸统一为毫米，投影使用语义文本，避免 CadAbstractions 引用 RuleEngine 或 COM enum。
/// </summary>
public sealed record DrawingSheetRequest
{
    /// <summary>Stable sheet name used by the provider.</summary>
    public string Name { get; init; } = "Sheet1";

    /// <summary>Standard paper name, for example A4 or A3.</summary>
    public required string PaperSize { get; init; }

    /// <summary>Paper width in millimetres in landscape/portrait orientation as selected by the compiler.</summary>
    public required Length Width { get; init; }

    /// <summary>Paper height in millimetres in landscape/portrait orientation as selected by the compiler.</summary>
    public required Length Height { get; init; }

    /// <summary>Projection method name, currently FirstAngle or ThirdAngle.</summary>
    public required string ProjectionMethod { get; init; }
}

/// <summary>
/// Drawing template families understood by the provider without leaking vendor COM types into the core.
/// Provider 支持的工程图模板族，不把厂商 COM 类型泄漏到 Core/Compiler。
/// </summary>
public enum DrawingTemplateProfile
{
    /// <summary>Use the user's configured SOLIDWORKS default template.</summary>
    SolidWorksDefault,

    /// <summary>Use the installed GB mechanical drawing template and its native title-block frame.</summary>
    GbMechanical,
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

/// <summary>
/// Declares one obround slot cut on the verified planar support face of a part.
/// 声明在已验证平面支撑面上的一个长圆槽切除。
/// </summary>
/// <remarks>
/// The two points are the ends of the slot centerline and width is the finished slot width. The provider converts
/// these canonical millimetres to native units and creates one native sketch-slot profile before cutting through all.
/// 两个点表示槽中心线端点，width 表示成品槽宽。Provider 只在边界处把毫米转换为 native units，先创建一个原生
/// sketch-slot profile，再执行贯穿切除。SupportFaceProbe is an explicit geometric probe, not a global selection.
/// SupportFaceProbe 是显式几何探针，不是全局 selection。
/// </remarks>
public sealed record SlotCutRequest
{
    /// <summary>Optional stable feature identity.</summary>
    public FeatureId? RequestedFeatureId { get; init; }

    /// <summary>Semantic feature name retained in the engineering graph.</summary>
    public string Name { get; init; } = "Slot-1";

    /// <summary>Finished slot width in canonical millimetres.</summary>
    public Length Width { get; init; }

    /// <summary>First centerline endpoint on the sketch plane.</summary>
    public Coordinate2D Start { get; init; }

    /// <summary>Second centerline endpoint on the sketch plane.</summary>
    public Coordinate2D End { get; init; }

    /// <summary>
    /// A point known to lie on the planar support face, used by the native provider's declarative ray selection.
    /// 已知位于平面支撑面上的点，供 native provider 进行声明式射线选面。
    /// </summary>
    public Coordinate2D SupportFaceProbe { get; init; }

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
/// Declares one native drawing surface-finish symbol with explicit approved engineering provenance.
/// 声明一个带有明确、已批准工程来源的 native 工程图表面粗糙度符号。
/// </summary>
/// <remarks>
/// SOLIDWORKS supports both attached and unattached surface-finish symbols. This first provider contract deliberately
/// scopes the symbol to one exact drawing view and records the attachment mode as view-scoped/unattached until a
/// persistent model-edge selector is available. It never turns a guessed roughness value into release evidence.
/// SOLIDWORKS 同时支持关联与非关联表面粗糙度符号。本首版 provider contract 只绑定精确 drawing view，并在尚未具备
/// persistent model-edge selector 时明确记录为 view-scoped/unattached；绝不把猜测的粗糙度值伪装成 release evidence。
/// </remarks>
public sealed record SurfaceFinishSymbolRequest
{
    /// <summary>Stable annotation identity required for idempotent native read-back.</summary>
    public required AnnotationId RequestedAnnotationId { get; init; }

    /// <summary>Exact drawing view that owns the symbol placement.</summary>
    public required ViewId ViewId { get; init; }

    /// <summary>Paper-space symbol position in canonical millimetres.</summary>
    public Coordinate2D Position { get; init; }

    /// <summary>Native surface symbol style represented without vendor enum types.</summary>
    public SurfaceFinishSymbolType SymbolType { get; init; } = SurfaceFinishSymbolType.MachiningRequired;

    /// <summary>Direction of lay represented without vendor enum types.</summary>
    public SurfaceFinishLayDirection LayDirection { get; init; } = SurfaceFinishLayDirection.None;

    /// <summary>Native leader style; a leader makes paper-space placement deterministic in the public API.</summary>
    /// <summary>Native leader style；leader 使 public API 的纸空间 placement 具备确定性。</summary>
    public SurfaceFinishLeaderStyle LeaderStyle { get; init; } = SurfaceFinishLeaderStyle.Straight;

    /// <summary>Native arrow style used when a leader is requested.</summary>
    public SurfaceFinishArrowStyle ArrowStyle { get; init; } = SurfaceFinishArrowStyle.Open;

    /// <summary>Manufacturing method text supplied by an approved requirement, if any.</summary>
    public string? ProductionMethod { get; init; }

    /// <summary>Maximum roughness text supplied by an approved requirement, if any.</summary>
    public string? MaximumRoughness { get; init; }

    /// <summary>Minimum roughness text supplied by an approved requirement, if any.</summary>
    public string? MinimumRoughness { get; init; }

    /// <summary>Optional sampling-length text supplied by an approved requirement.</summary>
    public string? SamplingLength { get; init; }

    /// <summary>Optional machining allowance text supplied by an approved requirement.</summary>
    public string? MachiningAllowance { get; init; }

    /// <summary>Optional other roughness values supplied by an approved requirement.</summary>
    public string? OtherValues { get; init; }

    /// <summary>Optional roughness-spacing text supplied by an approved requirement.</summary>
    public string? RoughnessSpacing { get; init; }

    /// <summary>Requirement provenance class; AI proposals are not eligible for this native mutation.</summary>
    public string ProvenanceKind { get; init; } = string.Empty;

    /// <summary>Concrete provenance method such as human approval or native PMI import.</summary>
    public string ProvenanceMethod { get; init; } = string.Empty;

    /// <summary>Approval state required before a native symbol can be created.</summary>
    public DrawingAnnotationApprovalState ApprovalState { get; init; }

    /// <summary>Stable requirement-graph keys covered by this symbol.</summary>
    public ImmutableArray<string> CoverageKeys { get; init; } = [];
}

/// <summary>
/// Selects the native geometry classes that SOLIDWORKS may center-mark in one exact drawing view.
/// 选择 SOLIDWORKS 可以在一个精确工程图视图中自动生成中心标记的原生几何类别。
/// </summary>
[Flags]
public enum DrawingCenterMarkTarget
{
    /// <summary>No target; rejected by the request validator.</summary>
    None = 0,

    /// <summary>Hole edges and cylindrical hole features.</summary>
    Holes = 1,

    /// <summary>Fillet/round arcs.</summary>
    Fillets = 2,

    /// <summary>Obround slot geometry.</summary>
    Slots = 4,
}

/// <summary>Connection-line options aligned to SOLIDWORKS swCenterMarkConnectionLine_e.</summary>
/// <summary>与 SOLIDWORKS swCenterMarkConnectionLine_e 对齐的中心标记连接线选项。</summary>
[Flags]
public enum DrawingCenterMarkConnectionLines
{
    /// <summary>Do not add connection lines.</summary>
    None = 0,

    /// <summary>Show linear pattern connection lines.</summary>
    Linear = 1,

    /// <summary>Show circular pattern connection lines.</summary>
    Circular = 2,

    /// <summary>Show radial pattern connection lines.</summary>
    Radial = 4,

    /// <summary>Show base center-mark lines.</summary>
    Base = 8,
}

/// <summary>
/// Requests deterministic native center marks for one exact drawing view.
/// 请求在一个精确 drawing view 上确定性生成 native center marks。
/// </summary>
/// <remarks>
/// This request deliberately uses SOLIDWORKS' bounded view operation instead of exposing global selection marks. The
/// provider verifies the exact view, compares native center-mark counts before/after, names each new annotation, and
/// returns read-back snapshots. 该请求刻意使用 SOLIDWORKS 有界 view operation，而不是向 MCP 暴露全局 selection mark；
/// Provider 会校验精确视图、比较 native center-mark 数量、命名新 annotation，并返回读回快照。
/// </remarks>
public sealed record DrawingCenterMarkRequest
{
    /// <summary>Stable identity prefix for the generated center-mark set.</summary>
    public required AnnotationId RequestedAnnotationId { get; init; }

    /// <summary>Exact drawing view that owns the center marks.</summary>
    public required ViewId ViewId { get; init; }

    /// <summary>Allowlisted native geometry target classes.</summary>
    public DrawingCenterMarkTarget Target { get; init; } = DrawingCenterMarkTarget.Holes;

    /// <summary>Pattern connection lines to request.</summary>
    public DrawingCenterMarkConnectionLines ConnectionLines { get; init; } = DrawingCenterMarkConnectionLines.None;

    /// <summary>Whether a slot center or each slot end receives the mark.</summary>
    public bool LinearSlotCenter { get; init; } = true;

    /// <summary>Whether an arc center or each arc end receives the mark.</summary>
    public bool ArcSlotCenter { get; init; } = true;

    /// <summary>Whether SOLIDWORKS document center-mark display defaults should be used.</summary>
    public bool UseDocumentDefaults { get; init; } = true;

    /// <summary>Center-mark size when document defaults are disabled.</summary>
    public Length Size { get; init; } = Length.FromMillimeters(3d);

    /// <summary>Gap when document defaults are disabled.</summary>
    public Length Gap { get; init; } = Length.FromMillimeters(0.5d);

    /// <summary>Whether extension lines are requested when document defaults are disabled.</summary>
    public bool ExtendedLines { get; init; }

    /// <summary>Whether the center-line font is requested when document defaults are disabled.</summary>
    public bool CenterLineFont { get; init; } = true;

    /// <summary>Minimum number of newly created native marks required as evidence.</summary>
    public int MinimumNewMarks { get; init; } = 1;

    /// <summary>Requirement provenance class; unapproved proposals cannot mutate a drawing.</summary>
    public string ProvenanceKind { get; init; } = string.Empty;

    /// <summary>Concrete provenance method, for example model-native center recognition.</summary>
    public string ProvenanceMethod { get; init; } = string.Empty;

    /// <summary>Approval state required before native materialization.</summary>
    public DrawingAnnotationApprovalState ApprovalState { get; init; }

    /// <summary>Stable requirement-graph keys covered by the generated set.</summary>
    public ImmutableArray<string> CoverageKeys { get; init; } = [];
}

/// <summary>Provider-neutral surface symbol style values aligned to the public SOLIDWORKS enum.</summary>
public enum SurfaceFinishSymbolType
{
    Basic = 0,
    MachiningRequired = 1,
    DoNotMachine = 2,
}

/// <summary>Provider-neutral direction-of-lay values aligned to the public SOLIDWORKS enum.</summary>
public enum SurfaceFinishLayDirection
{
    None = 0,
    Circular = 1,
    Cross = 2,
    MultiDirectional = 3,
    Parallel = 4,
    Perpendicular = 5,
    Radial = 6,
    Particulate = 7,
}

/// <summary>Provider-neutral leader styles aligned to the public SOLIDWORKS enum.</summary>
public enum SurfaceFinishLeaderStyle
{
    NoLeader = 0,
    Straight = 1,
    Bent = 2,
}

/// <summary>Provider-neutral arrow styles aligned to the public SOLIDWORKS enum.</summary>
public enum SurfaceFinishArrowStyle
{
    Open = 0,
    Closed = 1,
    Dot = 3,
    NoArrow = 10,
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
/// Request to apply one verified paper-space position repair to a drawing view.
/// 应用一个经过验证的 drawing view 纸空间位置修复请求。
/// </summary>
/// <remarks>
/// A view move is never addressed by enumeration index alone. The provider must resolve the stable ViewId, compare
/// the expected document hash and current position, preserve the native view scale, rebuild, and read back a positive
/// outline. 视图移动绝不能只使用 enumeration index；Provider 必须解析稳定 ViewId、校验 document hash 与当前坐标、
/// 保留 native view scale、rebuild，并读回正面积 outline。
/// </remarks>
public sealed record DrawingViewPositionRepairRequest
{
    /// <summary>Exact stable drawing-view identity returned by inspection.</summary>
    public required ViewId ViewId { get; init; }

    /// <summary>State hash captured when the deterministic reflow plan was created.</summary>
    public required string ExpectedDocumentStateHash { get; init; }

    /// <summary>Planner fingerprint retained for audit correlation.</summary>
    public required string PreconditionFingerprint { get; init; }

    /// <summary>Paper-space position observed before planning the repair.</summary>
    public required Coordinate2D ExpectedCurrentPosition { get; init; }

    /// <summary>Deterministic paper-space target selected by the drawing compiler.</summary>
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
