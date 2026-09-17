using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.AutoDrawing;

/// <summary>
/// The semantic class of one paper-space rectangle supplied to the layout planner.
/// 布局规划器接收的单个纸空间矩形的语义类别。
/// </summary>
public enum DrawingLayoutItemKind
{
    /// <summary>A model drawing view, including a section/detail view.</summary>
    View,

    /// <summary>A functional, manufacturing or reference dimension.</summary>
    Dimension,

    /// <summary>A hole/pattern/GD&amp;T or other manufacturing callout.</summary>
    Callout,

    /// <summary>A free-standing label or note.</summary>
    Label,

    /// <summary>A center mark or centerline annotation.</summary>
    CenterMark,

    /// <summary>A leader-owned annotation route or arrow graphic.</summary>
    Leader,

    /// <summary>Projected model geometry used for annotation-geometry collision checks.</summary>
    Geometry,
}

/// <summary>Deterministic layout finding severity.</summary>
public enum DrawingLayoutFindingStatus
{
    /// <summary>The item is inside the sheet and has no detected blocking collision.</summary>
    Pass,

    /// <summary>A non-blocking layout hygiene condition was recorded.</summary>
    Warning,

    /// <summary>The layout needs a human or compiler decision before release.</summary>
    ReviewRequired,

    /// <summary>The current sheet cannot safely contain the requested layout.</summary>
    Blocking,
}

/// <summary>
/// Deterministic follow-up requested when the current sheet cannot satisfy layout constraints.
/// 当前图幅无法满足布局约束时记录的确定性后续动作。
/// </summary>
public enum DrawingLayoutEscalationAction
{
    /// <summary>No escalation is required.</summary>
    None,

    /// <summary>Increase the spacing between dimension tiers and route leaders again.</summary>
    IncreaseDimensionTierSpacing,

    /// <summary>Reduce the drawing scale while preserving model association.</summary>
    ReduceScale,

    /// <summary>Choose a larger sheet from the active RulePack.</summary>
    UpgradeSheet,

    /// <summary>Move a local requirement to a generated detail view.</summary>
    AddDetailView,

    /// <summary>Split the drawing across another sheet.</summary>
    AddSheet,
}

/// <summary>One point in a routed paper-space leader path, in canonical millimetres.</summary>
/// <remarks>
/// This is intentionally not a SOLIDWORKS point. It is a vendor-neutral compiler value and is converted by the
/// provider only after the plan has passed policy checks. 这里不是 SOLIDWORKS point，而是无厂商布局值。
/// </remarks>
public readonly record struct DrawingLayoutPoint
{
    /// <summary>Creates a paper-space point from explicit unit-bearing lengths.</summary>
    public DrawingLayoutPoint(Length x, Length y)
        : this(x.Millimeters, y.Millimeters)
    {
    }

    /// <summary>Internal compiler constructor used only after values are canonical millimetres.</summary>
    internal DrawingLayoutPoint(double xMillimeters, double yMillimeters)
    {
        X = Length.FromMillimeters(xMillimeters);
        Y = Length.FromMillimeters(yMillimeters);
    }

    /// <summary>Horizontal component in canonical millimetres.</summary>
    public Length X { get; }

    /// <summary>Vertical component in canonical millimetres.</summary>
    public Length Y { get; }

    internal double XMillimeters => X.Millimeters;

    internal double YMillimeters => Y.Millimeters;

    /// <summary>Returns a deterministic invariant representation.</summary>
    public override string ToString() =>
        $"{XMillimeters.ToString("G17", CultureInfo.InvariantCulture)},{YMillimeters.ToString("G17", CultureInfo.InvariantCulture)}";
}

/// <summary>
/// An axis-aligned paper-space rectangle in millimetres.
/// 纸空间中的轴对齐矩形，单位为毫米。
/// </summary>
public readonly record struct DrawingLayoutRect
{
    /// <summary>Creates a finite rectangle from explicit unit-bearing lengths.</summary>
    public DrawingLayoutRect(Length left, Length bottom, Length width, Length height)
        : this(left.Millimeters, bottom.Millimeters, width.Millimeters, height.Millimeters)
    {
    }

    /// <summary>Internal compiler constructor used only with canonical millimetres.</summary>
    internal DrawingLayoutRect(double leftMillimeters, double bottomMillimeters, double widthMillimeters, double heightMillimeters)
    {
        RequireFinite(leftMillimeters, nameof(leftMillimeters));
        RequireFinite(bottomMillimeters, nameof(bottomMillimeters));
        RequireFinite(widthMillimeters, nameof(widthMillimeters));
        RequireFinite(heightMillimeters, nameof(heightMillimeters));
        if (widthMillimeters < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(widthMillimeters), "Rectangle width cannot be negative.");
        }

        if (heightMillimeters < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(heightMillimeters), "Rectangle height cannot be negative.");
        }

        Left = Length.FromMillimeters(leftMillimeters);
        Bottom = Length.FromMillimeters(bottomMillimeters);
        Width = Length.FromMillimeters(widthMillimeters);
        Height = Length.FromMillimeters(heightMillimeters);
    }

    /// <summary>Left edge in canonical millimetres.</summary>
    public Length Left { get; }

    /// <summary>Bottom edge in canonical millimetres.</summary>
    public Length Bottom { get; }

    /// <summary>Width in canonical millimetres.</summary>
    public Length Width { get; }

    /// <summary>Height in canonical millimetres.</summary>
    public Length Height { get; }

    internal double LeftMillimeters => Left.Millimeters;

    internal double BottomMillimeters => Bottom.Millimeters;

    internal double WidthMillimeters => Width.Millimeters;

    internal double HeightMillimeters => Height.Millimeters;

    /// <summary>Right edge in canonical millimetres.</summary>
    internal double RightMillimeters => LeftMillimeters + WidthMillimeters;

    /// <summary>Top edge in canonical millimetres.</summary>
    internal double TopMillimeters => BottomMillimeters + HeightMillimeters;

    /// <summary>Center point in millimetres.</summary>
    public DrawingLayoutPoint Center => new(LeftMillimeters + WidthMillimeters / 2d, BottomMillimeters + HeightMillimeters / 2d);

    /// <summary>Returns whether this rectangle fully contains the other rectangle.</summary>
    public bool Contains(DrawingLayoutRect other) => Contains(other, 0.000001d);

    /// <summary>Internal tolerance-aware containment check in canonical millimetres.</summary>
    internal bool Contains(DrawingLayoutRect other, double toleranceMillimeters) =>
        other.LeftMillimeters >= LeftMillimeters - toleranceMillimeters
        && other.BottomMillimeters >= BottomMillimeters - toleranceMillimeters
        && other.RightMillimeters <= RightMillimeters + toleranceMillimeters
        && other.TopMillimeters <= TopMillimeters + toleranceMillimeters;

    /// <summary>
    /// Returns true only for positive-area overlap; touching edges are legal and do not count as a collision.
    /// 只有正面积相交才算碰撞；仅边缘接触是合法的，不算 collision。
    /// </summary>
    public bool Intersects(DrawingLayoutRect other) => Intersects(other, 0.000001d);

    /// <summary>Internal tolerance-aware intersection check in canonical millimetres.</summary>
    internal bool Intersects(DrawingLayoutRect other, double toleranceMillimeters) =>
        LeftMillimeters < other.RightMillimeters - toleranceMillimeters
        && RightMillimeters > other.LeftMillimeters + toleranceMillimeters
        && BottomMillimeters < other.TopMillimeters - toleranceMillimeters
        && TopMillimeters > other.BottomMillimeters + toleranceMillimeters;

    /// <summary>Expands the rectangle by an explicit unit-bearing clearance.</summary>
    public DrawingLayoutRect Inflate(Length clearance) => Inflate(clearance.Millimeters);

    /// <summary>Internal clearance operation in canonical millimetres.</summary>
    internal DrawingLayoutRect Inflate(double clearanceMillimeters)
    {
        RequireFinite(clearanceMillimeters, nameof(clearanceMillimeters));
        if (clearanceMillimeters < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(clearanceMillimeters), "Clearance cannot be negative.");
        }

        return new(
            LeftMillimeters - clearanceMillimeters,
            BottomMillimeters - clearanceMillimeters,
            WidthMillimeters + clearanceMillimeters * 2d,
            HeightMillimeters + clearanceMillimeters * 2d);
    }

    private static void RequireFinite(double value, string name)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(name, "Layout coordinates must be finite.");
        }
    }
}

/// <summary>Per-side sheet margin in millimetres.</summary>
public readonly record struct DrawingLayoutMargins
{
    /// <summary>Creates margins from explicit unit-bearing lengths.</summary>
    public DrawingLayoutMargins(Length left, Length bottom, Length right, Length top)
        : this(left.Millimeters, bottom.Millimeters, right.Millimeters, top.Millimeters)
    {
    }

    /// <summary>Internal compiler constructor used only with canonical millimetres.</summary>
    internal DrawingLayoutMargins(double leftMillimeters, double bottomMillimeters, double rightMillimeters, double topMillimeters)
    {
        if (double.IsNaN(leftMillimeters) || double.IsInfinity(leftMillimeters) || leftMillimeters < 0d
            || double.IsNaN(bottomMillimeters) || double.IsInfinity(bottomMillimeters) || bottomMillimeters < 0d
            || double.IsNaN(rightMillimeters) || double.IsInfinity(rightMillimeters) || rightMillimeters < 0d
            || double.IsNaN(topMillimeters) || double.IsInfinity(topMillimeters) || topMillimeters < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(leftMillimeters), "Sheet margins must be finite and non-negative.");
        }

        Left = Length.FromMillimeters(leftMillimeters);
        Bottom = Length.FromMillimeters(bottomMillimeters);
        Right = Length.FromMillimeters(rightMillimeters);
        Top = Length.FromMillimeters(topMillimeters);
    }

    /// <summary>Left margin in canonical millimetres.</summary>
    public Length Left { get; }

    /// <summary>Bottom margin in canonical millimetres.</summary>
    public Length Bottom { get; }

    /// <summary>Right margin in canonical millimetres.</summary>
    public Length Right { get; }

    /// <summary>Top margin in canonical millimetres.</summary>
    public Length Top { get; }

    internal double LeftMillimeters => Left.Millimeters;

    internal double BottomMillimeters => Bottom.Millimeters;

    internal double RightMillimeters => Right.Millimeters;

    internal double TopMillimeters => Top.Millimeters;
}

/// <summary>A title block, BOM area or customer-reserved rectangle unavailable to annotations/views.</summary>
public sealed record DrawingLayoutReservedZone
{
    /// <summary>Stable zone identity.</summary>
    public required string ZoneId { get; init; }

    /// <summary>Non-secret semantic zone kind, for example title-block or bom.</summary>
    public required string ZoneKind { get; init; }

    /// <summary>Reserved paper-space bounds.</summary>
    public required DrawingLayoutRect Bounds { get; init; }
}

/// <summary>
/// Explicit provider/rule-pack geometry consumed by the deterministic layout planner.
/// Provider/RulePack 提供给确定性布局规划器的显式几何输入。
/// </summary>
public sealed record DrawingLayoutItem
{
    /// <summary>Stable item identity; it must remain independent of enumeration order.</summary>
    public required string ItemId { get; init; }

    /// <summary>Semantic item kind.</summary>
    public required DrawingLayoutItemKind Kind { get; init; }

    /// <summary>Requested paper-space bounds in millimetres.</summary>
    public required DrawingLayoutRect RequestedBounds { get; init; }

    /// <summary>Owning view identity when the item is associated with a view.</summary>
    public string? ViewId { get; init; }

    /// <summary>Stable engineering entity/requirement anchor, never source PDF text.</summary>
    public string? AnchorId { get; init; }

    /// <summary>Requested starting dimension tier; zero is the first tier.</summary>
    public int RequestedDimensionTier { get; init; }

    /// <summary>When true, the compiler must preserve the requested bounds and only report conflicts.</summary>
    public bool IsFixed { get; init; }
}

/// <summary>One final placement and optional orthogonal leader route.</summary>
public sealed record DrawingLayoutPlacement
{
    /// <summary>Stable item identity.</summary>
    public required string ItemId { get; init; }

    /// <summary>Semantic kind copied from the input item.</summary>
    public required DrawingLayoutItemKind Kind { get; init; }

    /// <summary>Final planned paper-space bounds.</summary>
    public required DrawingLayoutRect Bounds { get; init; }

    /// <summary>Dimension tier selected by the planner, or null for non-dimensions.</summary>
    public int? DimensionTier { get; init; }

    /// <summary>Whether the planner moved the item while preserving its association.</summary>
    public bool WasRepositioned { get; init; }

    /// <summary>Deterministic route points; an empty array means no leader route is required.</summary>
    public ImmutableArray<DrawingLayoutPoint> LeaderRoute { get; init; } = [];

    /// <summary>Human-readable deterministic reason for the placement decision.</summary>
    public required string Rationale { get; init; }
}

/// <summary>One machine-readable layout QA finding.</summary>
public sealed record DrawingLayoutFinding
{
    /// <summary>Severity used by the release gate.</summary>
    public required DrawingLayoutFindingStatus Status { get; init; }

    /// <summary>Stable diagnostic code.</summary>
    public required string Code { get; init; }

    /// <summary>Stable item identities participating in the finding.</summary>
    public ImmutableArray<string> ItemIds { get; init; } = [];

    /// <summary>Optional reserved-zone identity.</summary>
    public string? ZoneId { get; init; }

    /// <summary>Non-secret explanation suitable for audit output.</summary>
    public required string Explanation { get; init; }
}

/// <summary>One re-planning recommendation emitted for an infeasible sheet.</summary>
public sealed record DrawingLayoutEscalation
{
    /// <summary>Recommended compiler action.</summary>
    public required DrawingLayoutEscalationAction Action { get; init; }

    /// <summary>Whether the request policy permits this action on the current pass.</summary>
    public required bool IsAvailable { get; init; }

    /// <summary>Stable reason, including the triggering diagnostic codes.</summary>
    public required string Rationale { get; init; }

    /// <summary>Stable diagnostic codes that led to this recommendation.</summary>
    public ImmutableArray<string> TriggerCodes { get; init; } = [];
}

/// <summary>Inputs to one deterministic layout pass.</summary>
public sealed record DrawingLayoutRequest
{
    /// <summary>Stable sheet identity.</summary>
    public required string SheetId { get; init; }

    /// <summary>Full sheet bounds in paper-space millimetres.</summary>
    public required DrawingLayoutRect SheetBounds { get; init; }

    /// <summary>Title-block/BOM/edge margins that annotations cannot cross.</summary>
    public DrawingLayoutMargins Margins { get; init; } = new(0d, 0d, 0d, 0d);

    /// <summary>Explicit view, geometry and annotation rectangles.</summary>
    public ImmutableArray<DrawingLayoutItem> Items { get; init; } = [];

    /// <summary>Explicit reserved zones such as title block or BOM area.</summary>
    public ImmutableArray<DrawingLayoutReservedZone> ReservedZones { get; init; } = [];

    /// <summary>Minimum desired gap used while searching annotation positions.</summary>
    public Length MinimumAnnotationSpacing { get; init; } = Length.FromMillimeters(2d);

    /// <summary>Vertical distance between successive dimension tiers.</summary>
    public Length DimensionTierSpacing { get; init; } = Length.FromMillimeters(6d);

    /// <summary>Maximum dimension tier/offset rings attempted before escalation.</summary>
    public int MaxReflowRings { get; init; } = 8;

    /// <summary>Allow the next compiler phase to choose a smaller drawing scale.</summary>
    public bool AllowScaleReduction { get; init; } = true;

    /// <summary>Allow the next compiler phase to choose a larger RulePack sheet.</summary>
    public bool AllowSheetUpgrade { get; init; } = true;

    /// <summary>Allow the next compiler phase to split requirements over another sheet.</summary>
    public bool AllowAdditionalSheet { get; init; } = true;

    /// <summary>Allow a local requirement to be re-planned as a detail view.</summary>
    public bool AllowDetailView { get; init; } = true;
}

/// <summary>Immutable output of one D06 layout pass.</summary>
public sealed record DrawingLayoutPlan
{
    /// <summary>Schema version for persisted QA evidence.</summary>
    public const string SchemaVersion = "1.0";

    /// <summary>Stable sheet identity.</summary>
    public required string SheetId { get; init; }

    /// <summary>Final positions in deterministic identity order.</summary>
    public ImmutableArray<DrawingLayoutPlacement> Placements { get; init; } = [];

    /// <summary>All findings in deterministic severity/code/item order.</summary>
    public ImmutableArray<DrawingLayoutFinding> Findings { get; init; } = [];

    /// <summary>Possible next compiler actions, in deterministic policy order.</summary>
    public ImmutableArray<DrawingLayoutEscalation> Escalations { get; init; } = [];

    /// <summary>Stable hash of the plan, useful for regeneration and audit comparison.</summary>
    public required string Fingerprint { get; init; }

    /// <summary>True only when no blocking/review layout finding remains.</summary>
    public bool CanRelease => Findings.All(finding => finding.Status is DrawingLayoutFindingStatus.Pass or DrawingLayoutFindingStatus.Warning);
}

/// <summary>
/// Vendor-neutral deterministic paper-space layout planner.
/// 无厂商依赖的确定性纸空间布局规划器。
/// </summary>
/// <remarks>
/// The planner never calls SOLIDWORKS and never infers a rectangle from annotation text. A provider must supply native
/// outline evidence, and a RulePack must supply sheet/reserved-zone policy. This keeps visual layout separate from COM
/// mutation and makes the same collision proof executable in FakeCad, hosted CI and native Live tests.
/// 规划器不调用 SOLIDWORKS，也不从文字猜测矩形；Provider 必须提供 native outline evidence，RulePack 必须提供图幅/保留区策略。
/// </remarks>
public static class PartDrawingLayoutPlanner
{
    private const double ToleranceMillimeters = 0.000001d;

    /// <summary>Plans positions, detects collisions and records deterministic escalation reasons.</summary>
    public static DrawingLayoutPlan Plan(DrawingLayoutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        DrawingLayoutRect usableBounds = new(
            request.SheetBounds.LeftMillimeters + request.Margins.LeftMillimeters,
            request.SheetBounds.BottomMillimeters + request.Margins.BottomMillimeters,
            request.SheetBounds.WidthMillimeters - request.Margins.LeftMillimeters - request.Margins.RightMillimeters,
            request.SheetBounds.HeightMillimeters - request.Margins.BottomMillimeters - request.Margins.TopMillimeters);

        var placements = new List<DrawingLayoutPlacement>(request.Items.Length);
        var fixedPlacements = request.Items
            .Where(IsFixedItem)
            .OrderBy(item => KindOrder(item.Kind))
            .ThenBy(item => item.ItemId, StringComparer.Ordinal)
            .Select(item => Placement(item, item.RequestedBounds, null, false, [], "fixed-provider-geometry"))
            .ToList();
        placements.AddRange(fixedPlacements);

        var findings = new List<DrawingLayoutFinding>();
        var reservedZones = request.ReservedZones
            .OrderBy(zone => zone.ZoneId, StringComparer.Ordinal)
            .ToArray();

        ValidateFixedGeometry(usableBounds, fixedPlacements, reservedZones, findings);

        foreach (DrawingLayoutItem item in request.Items
                     .Where(item => !IsFixedItem(item))
                     .OrderBy(item => KindOrder(item.Kind))
                     .ThenBy(item => item.ViewId, StringComparer.Ordinal)
                     .ThenBy(item => item.AnchorId, StringComparer.Ordinal)
                     .ThenBy(item => item.ItemId, StringComparer.Ordinal))
        {
            CandidatePlacement? candidate = FindCandidate(item, request, usableBounds, reservedZones, placements);
            if (candidate is null)
            {
                DrawingLayoutPlacement failed = Placement(
                    item,
                    item.RequestedBounds,
                    item.Kind is DrawingLayoutItemKind.Dimension ? Math.Max(0, item.RequestedDimensionTier) : null,
                    false,
                    [],
                    "infeasible-current-sheet");
                placements.Add(failed);
                findings.Add(
                    new DrawingLayoutFinding
                    {
                        Status = DrawingLayoutFindingStatus.Blocking,
                        Code = "layout-infeasible",
                        ItemIds = [item.ItemId],
                        Explanation = "The item could not be placed inside the usable sheet without a collision or reserved-zone overlap.",
                    });
                continue;
            }

            DrawingLayoutPoint originalCenter = item.RequestedBounds.Center;
            bool moved = !NearlyEqual(candidate.Value.Bounds, item.RequestedBounds);
            ImmutableArray<DrawingLayoutPoint> route = moved && item.Kind is DrawingLayoutItemKind.Callout or DrawingLayoutItemKind.Label or DrawingLayoutItemKind.Leader
                ? OrthogonalRoute(originalCenter, candidate.Value.Bounds.Center)
                : [];
            placements.Add(
                Placement(
                    item,
                    candidate.Value.Bounds,
                    candidate.Value.DimensionTier,
                    moved,
                    route,
                    moved ? candidate.Value.Rationale : "requested-position-is-clear"));
        }

        DetectFinalCollisions(placements, reservedZones, usableBounds, findings);
        AddPassFindings(request.Items, placements, findings);

        DrawingLayoutEscalation[] escalations = BuildEscalations(request, findings);
        ImmutableArray<DrawingLayoutPlacement> orderedPlacements = [.. placements.OrderBy(value => value.ItemId, StringComparer.Ordinal)];
        ImmutableArray<DrawingLayoutFinding> orderedFindings = [.. findings
            .Distinct()
            .OrderBy(value => value.Status)
            .ThenBy(value => value.Code, StringComparer.Ordinal)
            .ThenBy(value => string.Join("|", value.ItemIds), StringComparer.Ordinal)
            .ThenBy(value => value.ZoneId, StringComparer.Ordinal)];

        DrawingLayoutPlan draft = new()
        {
            SheetId = request.SheetId,
            Placements = orderedPlacements,
            Findings = orderedFindings,
            Escalations = [.. escalations],
            Fingerprint = string.Empty,
        };

        return draft with { Fingerprint = Fingerprint(draft) };
    }

    private static void ValidateRequest(DrawingLayoutRequest request)
    {
        RequireNonBlank(request.SheetId, nameof(request.SheetId));
        if (request.SheetBounds.WidthMillimeters <= 0d || request.SheetBounds.HeightMillimeters <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Sheet bounds must have positive dimensions.");
        }

        if (request.MinimumAnnotationSpacing.Millimeters < 0d || double.IsNaN(request.MinimumAnnotationSpacing.Millimeters)
            || double.IsInfinity(request.MinimumAnnotationSpacing.Millimeters))
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        if (request.DimensionTierSpacing.Millimeters <= 0d || double.IsNaN(request.DimensionTierSpacing.Millimeters)
            || double.IsInfinity(request.DimensionTierSpacing.Millimeters))
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        if (request.MaxReflowRings < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        if (request.Items.Any(item => item is null) || request.ReservedZones.Any(zone => zone is null))
        {
            throw new ArgumentException("Layout items and reserved zones cannot contain null values.", nameof(request));
        }

        if (request.Items.Select(item => item.ItemId).Distinct(StringComparer.Ordinal).Count() != request.Items.Length)
        {
            throw new ArgumentException("Layout item identities must be unique.", nameof(request));
        }

        if (request.ReservedZones.Select(zone => zone.ZoneId).Distinct(StringComparer.Ordinal).Count() != request.ReservedZones.Length)
        {
            throw new ArgumentException("Reserved-zone identities must be unique.", nameof(request));
        }

        foreach (DrawingLayoutItem item in request.Items)
        {
            RequireNonBlank(item.ItemId, nameof(item.ItemId));
            if (item.RequestedDimensionTier < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(request));
            }
        }

        foreach (DrawingLayoutReservedZone zone in request.ReservedZones)
        {
            RequireNonBlank(zone.ZoneId, nameof(zone.ZoneId));
            RequireNonBlank(zone.ZoneKind, nameof(zone.ZoneKind));
        }

        DrawingLayoutRect usable = new(
            request.SheetBounds.LeftMillimeters + request.Margins.LeftMillimeters,
            request.SheetBounds.BottomMillimeters + request.Margins.BottomMillimeters,
            request.SheetBounds.WidthMillimeters - request.Margins.LeftMillimeters - request.Margins.RightMillimeters,
            request.SheetBounds.HeightMillimeters - request.Margins.BottomMillimeters - request.Margins.TopMillimeters);
        if (usable.WidthMillimeters <= 0d || usable.HeightMillimeters <= 0d)
        {
            throw new ArgumentException("Sheet margins must leave a positive usable sheet area.", nameof(request));
        }
    }

    private static bool IsFixedItem(DrawingLayoutItem item) =>
        item.IsFixed || item.Kind is DrawingLayoutItemKind.View or DrawingLayoutItemKind.Geometry;

    private static int KindOrder(DrawingLayoutItemKind kind) => kind switch
    {
        DrawingLayoutItemKind.View => 0,
        DrawingLayoutItemKind.Geometry => 1,
        DrawingLayoutItemKind.Dimension => 2,
        DrawingLayoutItemKind.Callout => 3,
        DrawingLayoutItemKind.CenterMark => 4,
        DrawingLayoutItemKind.Label => 5,
        DrawingLayoutItemKind.Leader => 6,
        _ => 99,
    };

    private static void ValidateFixedGeometry(
        DrawingLayoutRect usableBounds,
        List<DrawingLayoutPlacement> fixedPlacements,
        IReadOnlyList<DrawingLayoutReservedZone> reservedZones,
        List<DrawingLayoutFinding> findings)
    {
        DrawingLayoutPlacement[] fixedItems = [.. fixedPlacements];
        foreach (DrawingLayoutPlacement placement in fixedItems)
        {
            if (!usableBounds.Contains(placement.Bounds, ToleranceMillimeters))
            {
                findings.Add(
                    new DrawingLayoutFinding
                    {
                        Status = DrawingLayoutFindingStatus.Blocking,
                        Code = placement.Kind is DrawingLayoutItemKind.View ? "off-sheet-view" : "off-sheet-geometry",
                        ItemIds = [placement.ItemId],
                        Explanation = "A fixed provider geometry item extends outside the usable sheet bounds.",
                    });
            }

            foreach (DrawingLayoutReservedZone zone in reservedZones.Where(zone => placement.Bounds.Intersects(zone.Bounds)))
            {
                findings.Add(
                    new DrawingLayoutFinding
                    {
                        Status = DrawingLayoutFindingStatus.Blocking,
                        Code = "reserved-zone-collision",
                        ItemIds = [placement.ItemId],
                        ZoneId = zone.ZoneId,
                        Explanation = "A fixed provider geometry item overlaps a reserved title-block/BOM/customer zone.",
                    });
            }
        }

        for (int i = 0; i < fixedItems.Length; i++)
        {
            for (int j = i + 1; j < fixedItems.Length; j++)
            {
                if (!fixedItems[i].Bounds.Intersects(fixedItems[j].Bounds, ToleranceMillimeters))
                {
                    continue;
                }

                string code = fixedItems[i].Kind is DrawingLayoutItemKind.View && fixedItems[j].Kind is DrawingLayoutItemKind.View
                    ? "view-view-collision"
                    : "geometry-geometry-collision";
                findings.Add(
                    new DrawingLayoutFinding
                    {
                        Status = DrawingLayoutFindingStatus.Blocking,
                        Code = code,
                        ItemIds = [fixedItems[i].ItemId, fixedItems[j].ItemId],
                        Explanation = code == "view-view-collision"
                            ? "Two fixed drawing views overlap in paper space."
                            : "Two fixed projected geometry regions overlap in paper space.",
                    });
            }
        }
    }

    private static CandidatePlacement? FindCandidate(
        DrawingLayoutItem item,
        DrawingLayoutRequest request,
        DrawingLayoutRect usableBounds,
        IReadOnlyList<DrawingLayoutReservedZone> reservedZones,
        IReadOnlyList<DrawingLayoutPlacement> placed)
    {
        foreach ((double x, double y, int tier, string rationale) in CandidateOffsets(item, request))
        {
            DrawingLayoutRect candidateBounds = new(
                item.RequestedBounds.LeftMillimeters + x,
                item.RequestedBounds.BottomMillimeters + y,
                item.RequestedBounds.WidthMillimeters,
                item.RequestedBounds.HeightMillimeters);
            if (!usableBounds.Contains(candidateBounds, ToleranceMillimeters)
                || reservedZones.Any(zone => candidateBounds.Intersects(zone.Bounds, ToleranceMillimeters))
                || placed.Any(existing => Blocks(candidateBounds, item.Kind, existing.Bounds, existing.Kind, request.MinimumAnnotationSpacing.Millimeters)))
            {
                continue;
            }

            return new CandidatePlacement(candidateBounds, item.Kind is DrawingLayoutItemKind.Dimension ? tier : null, rationale);
        }

        return null;
    }

    private static IEnumerable<(double X, double Y, int Tier, string Rationale)> CandidateOffsets(
        DrawingLayoutItem item,
        DrawingLayoutRequest request)
    {
        yield return (0d, 0d, item.RequestedDimensionTier, "requested-position-is-clear");

        for (int ring = 1; ring <= request.MaxReflowRings; ring++)
        {
            double step = item.Kind is DrawingLayoutItemKind.Dimension
                ? request.DimensionTierSpacing.Millimeters
                : request.MinimumAnnotationSpacing.Millimeters + Math.Max(item.RequestedBounds.WidthMillimeters, item.RequestedBounds.HeightMillimeters);
            if (step <= 0d)
            {
                step = request.DimensionTierSpacing.Millimeters;
            }

            double distance = step * ring;
            int tier = item.RequestedDimensionTier + ring;
            // Stable clockwise order: above, right, left, below, then the four diagonals.
            // 固定顺时针顺序：上、右、左、下，再处理四个对角方向，保证再生结果稳定。
            (double X, double Y, string Name)[] offsets =
            [
                (0d, distance, "tier-above"),
                (distance, 0d, "shift-right"),
                (-distance, 0d, "shift-left"),
                (0d, -distance, "shift-below"),
                (distance, distance, "shift-upper-right"),
                (-distance, distance, "shift-upper-left"),
                (distance, -distance, "shift-lower-right"),
                (-distance, -distance, "shift-lower-left"),
            ];

            foreach ((double x, double y, string name) in offsets)
            {
                yield return (x, y, tier, name);
            }
        }
    }

    private static bool Blocks(
        DrawingLayoutRect candidate,
        DrawingLayoutItemKind candidateKind,
        DrawingLayoutRect existing,
        DrawingLayoutItemKind existingKind,
        double annotationSpacing)
    {
        bool candidateAnnotation = IsAnnotation(candidateKind);
        bool existingAnnotation = IsAnnotation(existingKind);
        DrawingLayoutRect comparison = candidateAnnotation && existingAnnotation
            ? candidate.Inflate(annotationSpacing)
            : candidate;
        return comparison.Intersects(existing, ToleranceMillimeters);
    }

    private static void DetectFinalCollisions(
        List<DrawingLayoutPlacement> placements,
        IReadOnlyList<DrawingLayoutReservedZone> reservedZones,
        DrawingLayoutRect usableBounds,
        List<DrawingLayoutFinding> findings)
    {
        foreach (DrawingLayoutPlacement placement in placements)
        {
            if (!usableBounds.Contains(placement.Bounds, ToleranceMillimeters))
            {
                findings.Add(
                    new DrawingLayoutFinding
                    {
                        Status = DrawingLayoutFindingStatus.Blocking,
                        Code = IsAnnotation(placement.Kind) ? "off-sheet-annotation" : "off-sheet-item",
                        ItemIds = [placement.ItemId],
                        Explanation = "The planned item extends outside the usable sheet bounds.",
                    });
            }

            foreach (DrawingLayoutReservedZone zone in reservedZones.Where(zone => placement.Bounds.Intersects(zone.Bounds, ToleranceMillimeters)))
            {
                findings.Add(
                    new DrawingLayoutFinding
                    {
                        Status = DrawingLayoutFindingStatus.Blocking,
                        Code = "reserved-zone-collision",
                        ItemIds = [placement.ItemId],
                        ZoneId = zone.ZoneId,
                        Explanation = "The planned item overlaps a reserved title-block/BOM/customer zone.",
                    });
            }
        }

        for (int i = 0; i < placements.Count; i++)
        {
            for (int j = i + 1; j < placements.Count; j++)
            {
                DrawingLayoutPlacement first = placements[i];
                DrawingLayoutPlacement second = placements[j];
                if (!first.Bounds.Intersects(second.Bounds, ToleranceMillimeters))
                {
                    continue;
                }

                if (first.Kind is DrawingLayoutItemKind.View && second.Kind is DrawingLayoutItemKind.View)
                {
                    findings.Add(Collision("view-view-collision", [first.ItemId, second.ItemId], "Two drawing views overlap in paper space."));
                }
                else if (IsAnnotation(first.Kind) && IsAnnotation(second.Kind))
                {
                    findings.Add(Collision("annotation-annotation-collision", [first.ItemId, second.ItemId], "Two annotations overlap in paper space."));
                }
                else if (IsAnnotation(first.Kind) && second.Kind is DrawingLayoutItemKind.View or DrawingLayoutItemKind.Geometry
                    || IsAnnotation(second.Kind) && first.Kind is DrawingLayoutItemKind.View or DrawingLayoutItemKind.Geometry)
                {
                    findings.Add(Collision("annotation-geometry-collision", [first.ItemId, second.ItemId], "An annotation overlaps projected geometry or a drawing view."));
                }
            }
        }
    }

    private static void AddPassFindings(
        IReadOnlyList<DrawingLayoutItem> input,
        List<DrawingLayoutPlacement> placements,
        List<DrawingLayoutFinding> findings)
    {
        foreach (DrawingLayoutPlacement placement in placements.Where(value => value.WasRepositioned))
        {
            findings.Add(
                new DrawingLayoutFinding
                {
                    Status = DrawingLayoutFindingStatus.Warning,
                    Code = "annotation-repositioned",
                    ItemIds = [placement.ItemId],
                    Explanation = "The planner moved the item deterministically while preserving its stable association.",
                });
        }

        foreach (DrawingLayoutItem item in input.Where(value => placements.Any(placement => placement.ItemId == value.ItemId)))
        {
            if (!findings.Any(finding => finding.ItemIds.Contains(item.ItemId, StringComparer.Ordinal)))
            {
                findings.Add(
                    new DrawingLayoutFinding
                    {
                        Status = DrawingLayoutFindingStatus.Pass,
                        Code = "layout-clear",
                        ItemIds = [item.ItemId],
                        Explanation = "The item is inside the usable sheet and has no detected blocking collision.",
                    });
            }
        }
    }

    private static DrawingLayoutEscalation[] BuildEscalations(DrawingLayoutRequest request, IReadOnlyList<DrawingLayoutFinding> findings)
    {
        string[] triggerCodes = [.. findings
            .Where(finding => finding.Status is DrawingLayoutFindingStatus.Blocking or DrawingLayoutFindingStatus.ReviewRequired)
            .Select(finding => finding.Code)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => code, StringComparer.Ordinal)];
        if (triggerCodes.Length == 0)
        {
            return
            [
                new DrawingLayoutEscalation
                {
                    Action = DrawingLayoutEscalationAction.None,
                    IsAvailable = true,
                    Rationale = "The current sheet satisfies the deterministic layout checks.",
                },
            ];
        }

        bool sheetGeometryFailure = triggerCodes.Any(code => code is "view-view-collision" or "off-sheet-view" or "reserved-zone-collision");
        DrawingLayoutEscalationAction action = sheetGeometryFailure
            ? request.AllowSheetUpgrade ? DrawingLayoutEscalationAction.UpgradeSheet
            : request.AllowScaleReduction ? DrawingLayoutEscalationAction.ReduceScale
            : request.AllowAdditionalSheet ? DrawingLayoutEscalationAction.AddSheet
            : DrawingLayoutEscalationAction.AddSheet
            : request.AllowScaleReduction ? DrawingLayoutEscalationAction.ReduceScale
            : request.AllowDetailView ? DrawingLayoutEscalationAction.AddDetailView
            : request.AllowAdditionalSheet ? DrawingLayoutEscalationAction.AddSheet
            : DrawingLayoutEscalationAction.IncreaseDimensionTierSpacing;
        bool available = action switch
        {
            DrawingLayoutEscalationAction.UpgradeSheet => request.AllowSheetUpgrade,
            DrawingLayoutEscalationAction.ReduceScale => request.AllowScaleReduction,
            DrawingLayoutEscalationAction.AddDetailView => request.AllowDetailView,
            DrawingLayoutEscalationAction.AddSheet => request.AllowAdditionalSheet,
            DrawingLayoutEscalationAction.IncreaseDimensionTierSpacing => true,
            _ => true,
        };

        return
        [
            new DrawingLayoutEscalation
            {
                Action = action,
                IsAvailable = available,
                TriggerCodes = [.. triggerCodes],
                Rationale = $"The current sheet remains infeasible because: {string.Join(", ", triggerCodes)}.",
            },
        ];
    }

    private static DrawingLayoutPlacement Placement(
        DrawingLayoutItem item,
        DrawingLayoutRect bounds,
        int? dimensionTier,
        bool moved,
        ImmutableArray<DrawingLayoutPoint> route,
        string rationale) =>
        new()
        {
            ItemId = item.ItemId,
            Kind = item.Kind,
            Bounds = bounds,
            DimensionTier = dimensionTier,
            WasRepositioned = moved,
            LeaderRoute = route,
            Rationale = rationale,
        };

    private static DrawingLayoutFinding Collision(string code, ImmutableArray<string> itemIds, string explanation) =>
        new()
        {
            Status = DrawingLayoutFindingStatus.Blocking,
            Code = code,
            ItemIds = itemIds,
            Explanation = explanation,
        };

    private static bool IsAnnotation(DrawingLayoutItemKind kind) => kind is not (DrawingLayoutItemKind.View or DrawingLayoutItemKind.Geometry);

    private static bool NearlyEqual(DrawingLayoutRect first, DrawingLayoutRect second) =>
        Math.Abs(first.LeftMillimeters - second.LeftMillimeters) < ToleranceMillimeters
        && Math.Abs(first.BottomMillimeters - second.BottomMillimeters) < ToleranceMillimeters
        && Math.Abs(first.WidthMillimeters - second.WidthMillimeters) < ToleranceMillimeters
        && Math.Abs(first.HeightMillimeters - second.HeightMillimeters) < ToleranceMillimeters;

    private static ImmutableArray<DrawingLayoutPoint> OrthogonalRoute(DrawingLayoutPoint start, DrawingLayoutPoint end)
    {
        DrawingLayoutPoint elbow = new(end.XMillimeters, start.YMillimeters);
        return [start, elbow, end];
    }

    private static string Fingerprint(DrawingLayoutPlan plan)
    {
        var builder = new StringBuilder();
        builder.Append(DrawingLayoutPlan.SchemaVersion).Append('|').Append(plan.SheetId).Append('|');
        foreach (DrawingLayoutPlacement placement in plan.Placements)
        {
            builder.Append(placement.ItemId).Append('|')
                .Append(placement.Kind).Append('|')
                .Append(placement.Bounds.LeftMillimeters.ToString("G17", CultureInfo.InvariantCulture)).Append('|')
                .Append(placement.Bounds.BottomMillimeters.ToString("G17", CultureInfo.InvariantCulture)).Append('|')
                .Append(placement.Bounds.WidthMillimeters.ToString("G17", CultureInfo.InvariantCulture)).Append('|')
                .Append(placement.Bounds.HeightMillimeters.ToString("G17", CultureInfo.InvariantCulture)).Append('|')
                .Append(placement.DimensionTier?.ToString(CultureInfo.InvariantCulture) ?? "-").Append('|')
                .Append(placement.Rationale).Append(';');
        }

        foreach (DrawingLayoutFinding finding in plan.Findings)
        {
            builder.Append(finding.Status).Append('|').Append(finding.Code).Append('|')
                .Append(string.Join(",", finding.ItemIds)).Append('|').Append(finding.ZoneId).Append(';');
        }

        foreach (DrawingLayoutEscalation escalation in plan.Escalations)
        {
            builder.Append(escalation.Action).Append('|').Append(escalation.IsAvailable).Append('|')
                .Append(string.Join(",", escalation.TriggerCodes)).Append(';');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static void RequireNonBlank(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
    }

    private readonly record struct CandidatePlacement(DrawingLayoutRect Bounds, int? DimensionTier, string Rationale);
}
