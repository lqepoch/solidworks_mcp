using SolidWorksMcp.AutoDrawing;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.UnitTests;

/// <summary>
/// Proves that view reflow is based on native outlines, stable identities and deterministic candidates.
/// 证明 view reflow 只依赖 native outline、稳定 identity 和确定性候选位置。
/// </summary>
public sealed class DrawingViewReflowPlannerTests
{
    [Fact]
    public void OverlapMovesOnlyTheLaterStableViewAndIsOrderIndependent()
    {
        DrawingViewSnapshot first = View("view-front", 70d, 70d, 60d, 40d, "Front");
        DrawingViewSnapshot second = View("view-top", 80d, 75d, 60d, 40d, "Top");
        DrawingViewReflowRequest request = BaseRequest([first, second]);

        DrawingViewReflowPlan plan = DrawingViewReflowPlanner.Plan(request);
        DrawingViewReflowPlan reversed = DrawingViewReflowPlanner.Plan(request with { Views = [second, first] });

        Assert.True(plan.CanApply);
        Assert.Equal(plan.Fingerprint, reversed.Fingerprint);
        Assert.False(plan.Placements.Single(value => value.ViewId.Value == "view-front").WasRepositioned);
        DrawingViewReflowPlacement moved = plan.Placements.Single(value => value.ViewId.Value == "view-top");
        Assert.True(moved.WasRepositioned);
        Assert.Equal("reflow-right", moved.Rationale);
        Assert.DoesNotContain(plan.Findings, value => value.Status == DrawingLayoutFindingStatus.Blocking);
    }

    [Fact]
    public void MissingNativeOutlineCannotProduceARepairTarget()
    {
        DrawingViewReflowPlan plan = DrawingViewReflowPlanner.Plan(
            BaseRequest(
            [
                new DrawingViewSnapshot
                {
                    ViewId = new ViewId("view-without-outline"),
                    Name = "Front",
                    Orientation = "Front",
                    Position = Point(50d, 50d),
                },
            ]));

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Findings, value => value.Code == "missing-native-outline");
        Assert.Empty(plan.Placements);
    }

    private static DrawingViewReflowRequest BaseRequest(IReadOnlyList<DrawingViewSnapshot> views) => new()
    {
        SheetId = "sheet-reflow",
        SheetBounds = new DrawingLayoutRect(Mm(0), Mm(0), Mm(210), Mm(150)),
        Margins = new DrawingLayoutMargins(Mm(5), Mm(5), Mm(5), Mm(5)),
        Views = [.. views],
        MinimumViewSpacing = Mm(2),
        MaxReflowRings = 8,
    };

    private static DrawingViewSnapshot View(string id, double x, double y, double width, double height, string orientation)
    {
        return new DrawingViewSnapshot
        {
            ViewId = new ViewId(id),
            Name = id,
            Orientation = orientation,
            Position = Point(x, y),
            Outline = new DrawingViewOutlineSnapshot
            {
                Left = Mm(x - width / 2d),
                Bottom = Mm(y - height / 2d),
                Right = Mm(x + width / 2d),
                Top = Mm(y + height / 2d),
            },
        };
    }

    private static Coordinate2D Point(double x, double y) => new(Mm(x), Mm(y));

    private static Length Mm(double value) => Length.FromMillimeters(value);
}
