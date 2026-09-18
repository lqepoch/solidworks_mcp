using System.Collections.Immutable;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;
using SolidWorksMcp.RuleEngine;

namespace SolidWorksMcp.AutoDrawing;

/// <summary>
/// One model-driven native view placement request.
/// 一个由模型尺寸和图幅规则共同决定的 native view 放置请求。
/// </summary>
public sealed record PlannedDrawingView
{
    /// <summary>Stable semantic name and requested standard model orientation.</summary>
    public required string Name { get; init; }

    /// <summary>Provider-facing orientation, such as Front/Top/Isometric.</summary>
    public required string Orientation { get; init; }

    /// <summary>Paper-space center in millimetres.</summary>
    public required Coordinate2D Position { get; init; }

    /// <summary>Estimated projected width used for planning evidence only.</summary>
    public required Length EstimatedWidth { get; init; }

    /// <summary>Estimated projected height used for planning evidence only.</summary>
    public required Length EstimatedHeight { get; init; }
}

/// <summary>
/// Deterministic placement plan derived from the source profile, sheet rule and projection method.
/// 根据源 profile、图幅规则和投影法生成的确定性布局 plan。
/// </summary>
public sealed record DrawingViewPlacementPlan
{
    /// <summary>Explicit sheet contract passed to the native provider.</summary>
    public required DrawingSheetRequest Sheet { get; init; }

    public required string SheetName { get; init; }

    public required Length SheetWidth { get; init; }

    public required Length SheetHeight { get; init; }

    public required ImmutableArray<PlannedDrawingView> Views { get; init; }

    /// <summary>Model-driven position for a compact pattern/feature note.</summary>
    public required Coordinate2D FeatureNotePosition { get; init; }

    /// <summary>Model-driven position for a slot note.</summary>
    public required Coordinate2D SlotNotePosition { get; init; }

    /// <summary>Model-driven section-view center in the free lower-right corridor.</summary>
    public required Coordinate2D SectionViewPosition { get; init; }

    /// <summary>Deterministic trace explaining why the positions were chosen.</summary>
    public required ImmutableArray<string> Rationale { get; init; }
}

/// <summary>
/// Plans a GB-style first-angle sheet without using screenshot coordinates or a fixed Letter canvas.
/// 在不使用截图坐标和固定 Letter 画布的前提下规划 GB 风格第一角图幅。
/// </summary>
/// <remarks>
/// The planner consumes the actual engineering profile request and the resolved RulePack. It does not claim that an
/// estimated outline is the final native outline: the provider still reads <c>IView.GetOutline()</c> after creation.
/// Its purpose is to give SOLIDWORKS a defensible initial layout, keep projected views aligned, reserve the native
/// title block, and make a bad sheet/scale decision visible before annotations are written. 规划器消费实际工程
/// profile 和 resolved RulePack；它不把估算轮廓冒充最终 native outline，Provider 创建后仍必须读回
/// <c>IView.GetOutline()</c>。它的职责是给 SOLIDWORKS 一个有依据的初始布局、保持投影视图对齐、避让 native
/// 标题栏，并在写入标注前暴露图幅/比例决策问题。
/// </remarks>
public static class DrawingViewPlacementPlanner
{
    /// <summary>Plans the first sheet from profile geometry and GB RulePack values.</summary>
    public static DrawingViewPlacementPlan Plan(
        SketchProfileRequest profile,
        Length extrusionDepth,
        int scaleDenominator,
        ResolvedDrawingRulePack? rulePack,
        bool needsSectionView)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scaleDenominator);

        double[] xs = [.. profile.Segments
            .SelectMany(segment => segment.Kind is SketchCurveKind.ThreePointArc
                ? new[] { segment.Start.X.Millimeters, segment.End.X.Millimeters, segment.Through.X.Millimeters }
                : [segment.Start.X.Millimeters, segment.End.X.Millimeters])
            .Where(double.IsFinite)];
        double[] ys = [.. profile.Segments
            .SelectMany(segment => segment.Kind is SketchCurveKind.ThreePointArc
                ? new[] { segment.Start.Y.Millimeters, segment.End.Y.Millimeters, segment.Through.Y.Millimeters }
                : [segment.Start.Y.Millimeters, segment.End.Y.Millimeters])
            .Where(double.IsFinite)];
        if (xs.Length == 0 || ys.Length == 0)
        {
            throw new ArgumentException("A non-empty profile is required for view planning.", nameof(profile));
        }

        double modelWidth = Math.Max(1d, xs.Max() - xs.Min());
        double modelHeight = Math.Max(1d, ys.Max() - ys.Min());
        double depth = Math.Max(1d, extrusionDepth.Millimeters);
        double denominator = scaleDenominator;

        SheetSizeRule sheet = SelectSheet(rulePack);
        double sheetWidth = sheet.Width.Millimeters;
        double sheetHeight = sheet.Height.Millimeters;
        double margin = rulePack?.Values.SheetMargin.Millimeters ?? 10d;
        double viewGap = Math.Max(
            rulePack?.Values.ViewSpacing.Millimeters ?? 20d,
            rulePack?.Values.DimensionSpacing.Millimeters ?? 8d);
        ReservedZoneRule? titleBlock = rulePack?.Values.ReservedZones.FirstOrDefault(zone =>
            zone.ZoneId.Equals("title-block", StringComparison.OrdinalIgnoreCase));
        double titleBlockLeft = titleBlock?.Left.Millimeters ?? sheetWidth;
        double titleBlockBottom = titleBlock?.Bottom.Millimeters ?? margin;
        double titleBlockHeight = titleBlock?.Height.Millimeters ?? 0d;
        double left = margin;
        double right = Math.Max(left, titleBlockLeft - margin);
        double bottom = Math.Max(margin, titleBlockBottom + titleBlockHeight + viewGap);
        double top = Math.Max(bottom, sheetHeight - margin);

        // First-angle layout: the top view is below the front view, while the projected centre remains vertically
        // aligned. 第一角投影：俯视图位于主视图下方，同时保持投影中心线竖向对齐。
        double frontWidth = modelWidth / denominator;
        double frontHeight = modelHeight / denominator;
        double topWidth = modelWidth / denominator;
        double topHeight = depth / denominator;
        double isoWidth = Math.Max(modelWidth, depth) / denominator;
        double isoHeight = Math.Max(modelHeight, depth) / denominator;

        double projectedColumnWidth = Math.Max(frontWidth, topWidth);
        double primaryX = left + (projectedColumnWidth / 2d);
        bool firstAngle = rulePack?.Values.ProjectionMethod is not DrawingProjectionMethod.ThirdAngle;
        double frontY = bottom + (frontHeight / 2d);
        double topY = firstAngle
            ? frontY - (frontHeight / 2d) - viewGap - (topHeight / 2d)
            : frontY + (frontHeight / 2d) + viewGap + (topHeight / 2d);
        double maxFrontY = top - (frontHeight / 2d);
        if (frontY > maxFrontY)
        {
            // Keep the primary view on-sheet. The compiler must still compare native outlines and may upgrade the
            // selected sheet later; this initial phase never pretends that an estimate is final evidence.
            // 保证主视图初始位置位于图幅内；compiler 仍必须比较 native outline，必要时升级图幅，不能把估算冒充最终证据。
            frontY = maxFrontY;
            topY = firstAngle
                ? frontY - (frontHeight / 2d) - viewGap - (topHeight / 2d)
                : frontY + (frontHeight / 2d) + viewGap + (topHeight / 2d);
        }

        double rightColumnLeft = Math.Max(primaryX + (projectedColumnWidth / 2d) + viewGap, right - isoWidth);
        double isoX = rightColumnLeft + (isoWidth / 2d);
        double isoY = frontY;
        double reservedTitleLeft = titleBlockLeft;
        if (isoX + (isoWidth / 2d) > reservedTitleLeft && isoY - (isoHeight / 2d) < bottom)
        {
            isoY = Math.Max(bottom + (isoHeight / 2d), frontY);
        }

        double corridorX = Math.Min(
            sheetWidth - margin,
            Math.Max(reservedTitleLeft - viewGap, isoX + isoWidth / 2d + viewGap));
        double sectionY = bottom + Math.Max(isoHeight, viewGap) / 2d;
        if (!needsSectionView)
        {
            sectionY = bottom + 8d;
        }

        return new DrawingViewPlacementPlan
        {
            SheetName = sheet.Name,
            SheetWidth = sheet.Width,
            SheetHeight = sheet.Height,
            Sheet = new DrawingSheetRequest
            {
                Name = "Sheet1",
                PaperSize = sheet.Name,
                Width = sheet.Width,
                Height = sheet.Height,
                ProjectionMethod = rulePack?.Values.ProjectionMethod.ToString() ?? "FirstAngle",
            },
            Views =
            [
                View("Front", "Front", primaryX, frontY, frontWidth, frontHeight),
                View("Top", "Top", primaryX, topY, topWidth, topHeight),
                View("Isometric", "Isometric", isoX, isoY, isoWidth, isoHeight),
            ],
            FeatureNotePosition = new Coordinate2D(
                Length.FromMillimeters(Math.Max(left, primaryX - projectedColumnWidth / 2d)),
                Length.FromMillimeters(Math.Min(sheetHeight - margin, frontY + frontHeight / 2d + viewGap / 2d))),
            SlotNotePosition = new Coordinate2D(
                Length.FromMillimeters(Math.Max(left, primaryX - projectedColumnWidth / 2d)),
                Length.FromMillimeters(Math.Max(margin, Math.Min(topY, topY - topHeight / 2d - viewGap / 2d)))),
            SectionViewPosition = new Coordinate2D(
                Length.FromMillimeters(corridorX),
                Length.FromMillimeters(sectionY)),
            Rationale =
            [
                $"sheet={sheet.Name};size={sheet.Width.Millimeters:0.###}x{sheet.Height.Millimeters:0.###}mm",
                $"projection={rulePack?.Values.ProjectionMethod.ToString() ?? "FirstAngle"}",
                $"profile-estimate={modelWidth:0.###}x{modelHeight:0.###}x{depth:0.###}mm",
                $"primary/top centres share X; {(firstAngle ? "first-angle projected view is below primary" : "third-angle projected view is above primary")}",
                titleBlock is null
                    ? "no title-block reserved zone was supplied by the RulePack"
                    : $"title-block reserved zone comes from RulePack: {titleBlock.ZoneId} {titleBlock.Width.Millimeters:0.###}x{titleBlock.Height.Millimeters:0.###}mm",
            ],
        };

        static PlannedDrawingView View(string name, string orientation, double x, double y, double width, double height) => new()
        {
            Name = name,
            Orientation = orientation,
            Position = new Coordinate2D(Length.FromMillimeters(x), Length.FromMillimeters(y)),
            EstimatedWidth = Length.FromMillimeters(width),
            EstimatedHeight = Length.FromMillimeters(height),
        };
    }

    private static SheetSizeRule SelectSheet(ResolvedDrawingRulePack? rulePack)
    {
        SheetSizeRule? a4 = rulePack?.Values.AllowedSheetSizes.FirstOrDefault(sheet =>
            sheet.Name.Equals("A4", StringComparison.OrdinalIgnoreCase));
        if (a4 is not null)
        {
            return a4;
        }

        return new SheetSizeRule
        {
            Name = "A4",
            Width = Length.FromMillimeters(297d),
            Height = Length.FromMillimeters(210d),
        };
    }
}
