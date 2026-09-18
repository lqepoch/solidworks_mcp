using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.AutoDrawing;

/// <summary>
/// Deterministic input for one native-outline-backed drawing-view reflow pass.
/// 基于 native outline 的单次 drawing view 确定性重排输入。
/// </summary>
public sealed record DrawingViewReflowRequest
{
    /// <summary>Stable sheet identity from native inspection.</summary>
    public required string SheetId { get; init; }

    /// <summary>Full sheet bounds in canonical paper-space millimetres.</summary>
    public required DrawingLayoutRect SheetBounds { get; init; }

    /// <summary>Per-side paper-space margins from the active RulePack.</summary>
    public DrawingLayoutMargins Margins { get; init; } = new(0d, 0d, 0d, 0d);

    /// <summary>Views and their native persisted outlines.</summary>
    public ImmutableArray<DrawingViewSnapshot> Views { get; init; } = [];

    /// <summary>Explicit title-block/BOM/customer zones that views cannot overlap.</summary>
    public ImmutableArray<DrawingLayoutReservedZone> ReservedZones { get; init; } = [];

    /// <summary>Minimum free gap between view outlines, in canonical millimetres.</summary>
    public Length MinimumViewSpacing { get; init; } = Length.FromMillimeters(2d);

    /// <summary>Finite number of deterministic search rings.</summary>
    public int MaxReflowRings { get; init; } = 8;
}

/// <summary>One deterministic target position for an exact native drawing view.</summary>
public sealed record DrawingViewReflowPlacement
{
    /// <summary>Stable view identity from inspection.</summary>
    public required ViewId ViewId { get; init; }

    /// <summary>Position used as the provider precondition.</summary>
    public required Coordinate2D ExpectedCurrentPosition { get; init; }

    /// <summary>Target paper-space position selected by this pass.</summary>
    public required Coordinate2D NewPosition { get; init; }

    /// <summary>Native outline before the repair.</summary>
    public required DrawingLayoutRect CurrentBounds { get; init; }

    /// <summary>Planned outline after translating the native outline.</summary>
    public required DrawingLayoutRect NewBounds { get; init; }

    /// <summary>Whether a provider mutation is required.</summary>
    public bool WasRepositioned { get; init; }

    /// <summary>Stable explanation of the candidate chosen by the search.</summary>
    public required string Rationale { get; init; }
}

/// <summary>Output of one native-outline-backed view reflow pass.</summary>
public sealed record DrawingViewReflowPlan
{
    /// <summary>Planner schema version.</summary>
    public const string SchemaVersion = "1.0";

    /// <summary>Stable sheet identity.</summary>
    public required string SheetId { get; init; }

    /// <summary>Placements in deterministic ViewId order.</summary>
    public ImmutableArray<DrawingViewReflowPlacement> Placements { get; init; } = [];

    /// <summary>Blocking/review findings emitted during planning.</summary>
    public ImmutableArray<DrawingLayoutFinding> Findings { get; init; } = [];

    /// <summary>Stable hash for the mutation precondition and audit log.</summary>
    public required string Fingerprint { get; init; }

    /// <summary>True only when all native views have a safe, collision-free target.</summary>
    public bool CanApply => Findings.All(finding => finding.Status is DrawingLayoutFindingStatus.Pass or DrawingLayoutFindingStatus.Warning);
}

/// <summary>
/// Vendor-neutral, bounded view-only reflow planner.
/// 厂商无关且有界的 view-only 重排规划器。
/// </summary>
/// <remarks>
/// The planner translates persisted native rectangles only; it never estimates geometry from model dimensions or
/// annotation text. Native view identity, scale, and alignment remain provider responsibilities. 规划器只平移已经
/// persisted 的 native 矩形，不从模型尺寸或标注文字估算几何；native identity、scale 与 alignment 仍由 Provider 负责。
/// </remarks>
public static class DrawingViewReflowPlanner
{
    private const double ToleranceMillimeters = 0.000001d;

    /// <summary>Plans a deterministic set of view translations without calling SOLIDWORKS.</summary>
    public static DrawingViewReflowPlan Plan(DrawingViewReflowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        DrawingLayoutRect usable = new(
            request.SheetBounds.Left.Millimeters + request.Margins.Left.Millimeters,
            request.SheetBounds.Bottom.Millimeters + request.Margins.Bottom.Millimeters,
            request.SheetBounds.Width.Millimeters - request.Margins.Left.Millimeters - request.Margins.Right.Millimeters,
            request.SheetBounds.Height.Millimeters - request.Margins.Bottom.Millimeters - request.Margins.Top.Millimeters);

        var placements = new List<DrawingViewReflowPlacement>(request.Views.Length);
        var findings = new List<DrawingLayoutFinding>();
        DrawingLayoutReservedZone[] zones = [.. request.ReservedZones.OrderBy(zone => zone.ZoneId, StringComparer.Ordinal)];
        var accepted = new List<DrawingLayoutRect>(request.Views.Length);

        foreach (DrawingViewSnapshot view in request.Views.OrderBy(value => value.ViewId.Value, StringComparer.Ordinal))
        {
            if (view.Outline is null)
            {
                findings.Add(
                    new DrawingLayoutFinding
                    {
                        Status = DrawingLayoutFindingStatus.ReviewRequired,
                        Code = "missing-native-outline",
                        ItemIds = [$"view:{view.ViewId.Value}"],
                        Explanation = "The view has no persisted native outline, so a safe reflow target cannot be proved.",
                    });
                continue;
            }

            DrawingLayoutRect current = ToRect(view.Outline);
            Candidate? candidate = FindCandidate(current, usable, zones, accepted, request);
            if (candidate is null)
            {
                placements.Add(
                    Placement(view, current, current, false, "no-safe-view-target"));
                findings.Add(
                    new DrawingLayoutFinding
                    {
                        Status = DrawingLayoutFindingStatus.Blocking,
                        Code = "view-reflow-infeasible",
                        ItemIds = [$"view:{view.ViewId.Value}"],
                        Explanation = "No deterministic paper-space target fits the native outline on the current sheet.",
                    });
                accepted.Add(current);
                continue;
            }

            placements.Add(Placement(view, current, candidate.Value.Bounds, true, candidate.Value.Rationale));
            accepted.Add(candidate.Value.Bounds);
        }

        foreach (DrawingViewReflowPlacement placement in placements)
        {
            if (!usable.Contains(placement.NewBounds, ToleranceMillimeters))
            {
                findings.Add(Finding("off-sheet-view", placement, "The reflow target extends outside the usable sheet."));
            }

            foreach (DrawingLayoutReservedZone zone in zones.Where(zone => placement.NewBounds.Intersects(zone.Bounds, ToleranceMillimeters)))
            {
                findings.Add(
                    new DrawingLayoutFinding
                    {
                        Status = DrawingLayoutFindingStatus.Blocking,
                        Code = "reserved-zone-collision",
                        ItemIds = [$"view:{placement.ViewId.Value}"],
                        ZoneId = zone.ZoneId,
                        Explanation = "The reflow target overlaps a reserved title-block/BOM/customer zone.",
                    });
            }
        }

        DrawingViewReflowPlacement[] orderedPlacements = [.. placements.OrderBy(value => value.ViewId.Value, StringComparer.Ordinal)];
        DrawingLayoutFinding[] orderedFindings = [.. findings
            .Distinct()
            .OrderBy(value => value.Status)
            .ThenBy(value => value.Code, StringComparer.Ordinal)
            .ThenBy(value => string.Join("|", value.ItemIds), StringComparer.Ordinal)];
        var draft = new DrawingViewReflowPlan
        {
            SheetId = request.SheetId.Trim(),
            Placements = [.. orderedPlacements],
            Findings = [.. orderedFindings],
            Fingerprint = string.Empty,
        };
        return draft with { Fingerprint = Fingerprint(draft) };
    }

    private static DrawingViewReflowPlacement Placement(
        DrawingViewSnapshot view,
        DrawingLayoutRect current,
        DrawingLayoutRect target,
        bool moved,
        string rationale)
    {
        DrawingLayoutPoint center = target.Center;
        return new DrawingViewReflowPlacement
        {
            ViewId = view.ViewId,
            ExpectedCurrentPosition = view.Position,
            NewPosition = new Coordinate2D(center.X, center.Y),
            CurrentBounds = current,
            NewBounds = target,
            WasRepositioned = moved && !NearlyEqual(current, target),
            Rationale = rationale,
        };
    }

    private static Candidate? FindCandidate(
        DrawingLayoutRect current,
        DrawingLayoutRect usable,
        IReadOnlyList<DrawingLayoutReservedZone> zones,
        IReadOnlyList<DrawingLayoutRect> accepted,
        DrawingViewReflowRequest request)
    {
        foreach ((double x, double y, string rationale) in CandidateOffsets(current, request))
        {
            DrawingLayoutRect candidate = new(
                Length.FromMillimeters(current.Left.Millimeters + x),
                Length.FromMillimeters(current.Bottom.Millimeters + y),
                current.Width,
                current.Height);
            if (!usable.Contains(candidate, ToleranceMillimeters)
                || zones.Any(zone => candidate.Intersects(zone.Bounds, ToleranceMillimeters))
                || accepted.Any(existing => candidate.Intersects(existing, ToleranceMillimeters)))
            {
                continue;
            }

            return new Candidate(candidate, rationale);
        }

        return null;
    }

    private static IEnumerable<(double X, double Y, string Rationale)> CandidateOffsets(
        DrawingLayoutRect current,
        DrawingViewReflowRequest request)
    {
        yield return (0d, 0d, "native-position-is-clear");
        double step = request.MinimumViewSpacing.Millimeters + Math.Max(current.Width.Millimeters, current.Height.Millimeters);
        for (int ring = 1; ring <= request.MaxReflowRings; ring++)
        {
            double distance = step * ring;
            yield return (0d, distance, "reflow-above");
            yield return (distance, 0d, "reflow-right");
            yield return (-distance, 0d, "reflow-left");
            yield return (0d, -distance, "reflow-below");
            yield return (distance, distance, "reflow-upper-right");
            yield return (-distance, distance, "reflow-upper-left");
            yield return (distance, -distance, "reflow-lower-right");
            yield return (-distance, -distance, "reflow-lower-left");
        }
    }

    private static DrawingLayoutFinding Finding(string code, DrawingViewReflowPlacement placement, string explanation) =>
        new()
        {
            Status = DrawingLayoutFindingStatus.Blocking,
            Code = code,
            ItemIds = [$"view:{placement.ViewId.Value}"],
            Explanation = explanation,
        };

    private static DrawingLayoutRect ToRect(DrawingViewOutlineSnapshot outline)
    {
        double width = outline.Right.Millimeters - outline.Left.Millimeters;
        double height = outline.Top.Millimeters - outline.Bottom.Millimeters;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0d || height <= 0d)
        {
            throw new ArgumentException("A native drawing view outline must have positive finite dimensions.", nameof(outline));
        }

        return new DrawingLayoutRect(outline.Left, outline.Bottom, Length.FromMillimeters(width), Length.FromMillimeters(height));
    }

    private static void Validate(DrawingViewReflowRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SheetId) || request.Views.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A sheet identity and at least one native view are required.", nameof(request));
        }

        if (request.MinimumViewSpacing.Millimeters < 0d || !double.IsFinite(request.MinimumViewSpacing.Millimeters))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "MinimumViewSpacing must be finite and non-negative.");
        }

        if (request.MaxReflowRings < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "MaxReflowRings cannot be negative.");
        }

        if (request.Views.Select(view => view.ViewId.Value).Distinct(StringComparer.Ordinal).Count() != request.Views.Length)
        {
            throw new ArgumentException("View identities must be unique.", nameof(request));
        }
    }

    private static bool NearlyEqual(DrawingLayoutRect first, DrawingLayoutRect second) =>
        Math.Abs(first.Left.Millimeters - second.Left.Millimeters) <= ToleranceMillimeters
        && Math.Abs(first.Bottom.Millimeters - second.Bottom.Millimeters) <= ToleranceMillimeters
        && Math.Abs(first.Width.Millimeters - second.Width.Millimeters) <= ToleranceMillimeters
        && Math.Abs(first.Height.Millimeters - second.Height.Millimeters) <= ToleranceMillimeters;

    private static string Fingerprint(DrawingViewReflowPlan plan)
    {
        var builder = new StringBuilder();
        builder.Append(DrawingViewReflowPlan.SchemaVersion).Append('|').Append(plan.SheetId).Append('|');
        foreach (DrawingViewReflowPlacement placement in plan.Placements)
        {
            builder.Append(placement.ViewId.Value).Append('|')
                .Append(placement.ExpectedCurrentPosition.X.Millimeters.ToString("G17", CultureInfo.InvariantCulture)).Append(',')
                .Append(placement.ExpectedCurrentPosition.Y.Millimeters.ToString("G17", CultureInfo.InvariantCulture)).Append("->")
                .Append(placement.NewPosition.X.Millimeters.ToString("G17", CultureInfo.InvariantCulture)).Append(',')
                .Append(placement.NewPosition.Y.Millimeters.ToString("G17", CultureInfo.InvariantCulture)).Append('|')
                .Append(placement.Rationale).Append(';');
        }

        foreach (DrawingLayoutFinding finding in plan.Findings)
        {
            builder.Append(finding.Status).Append('|').Append(finding.Code).Append('|')
                .Append(string.Join(',', finding.ItemIds)).Append('|').Append(finding.ZoneId).Append(';');
        }

        return $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant()}";
    }

    private readonly record struct Candidate(DrawingLayoutRect Bounds, string Rationale);
}
