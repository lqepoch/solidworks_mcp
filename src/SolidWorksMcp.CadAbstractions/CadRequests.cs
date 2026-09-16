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

/// <summary>Request to create a part document.</summary>
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

/// <summary>Request to add one drawing annotation associated with a view.</summary>
public sealed record DrawingAnnotationRequest
{
    /// <summary>Optional stable annotation identity.</summary>
    public AnnotationId? RequestedAnnotationId { get; init; }

    /// <summary>Owning view identity.</summary>
    public ViewId ViewId { get; init; }

    /// <summary>Annotation semantic kind such as model-dimension or note.</summary>
    public string Kind { get; init; } = "note";

    /// <summary>Display text; source semantic identity must be carried by higher layers.</summary>
    public string Text { get; init; } = string.Empty;

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
