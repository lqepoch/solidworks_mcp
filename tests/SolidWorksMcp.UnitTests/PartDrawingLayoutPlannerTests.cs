using System.Globalization;
using SolidWorksMcp.AutoDrawing;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.EngineeringModel;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.UnitTests;

public sealed class PartDrawingLayoutPlannerTests
{
    [Fact]
    public void RegenerationIsStableAndOverlappingDimensionsUseDeterministicTiers()
    {
        DrawingLayoutRequest firstRequest = BaseRequest(
            [
                Fixed("view-front", DrawingLayoutItemKind.View, 20, 150, 80, 50),
                Fixed("view-top", DrawingLayoutItemKind.View, 120, 150, 80, 50),
                Annotation("dimension-a", DrawingLayoutItemKind.Dimension, 30, 95, 40, 6, "feature-a"),
                Annotation("dimension-b", DrawingLayoutItemKind.Dimension, 30, 95, 40, 6, "feature-b"),
            ]);
        DrawingLayoutRequest reversedRequest = firstRequest with { Items = [.. firstRequest.Items.Reverse()] };

        DrawingLayoutPlan first = PartDrawingLayoutPlanner.Plan(firstRequest);
        DrawingLayoutPlan second = PartDrawingLayoutPlanner.Plan(reversedRequest);

        Assert.True(first.CanRelease);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(
            first.Placements.Select(item => $"{item.ItemId}|{item.Bounds}|{item.DimensionTier}|{item.Rationale}"),
            second.Placements.Select(item => $"{item.ItemId}|{item.Bounds}|{item.DimensionTier}|{item.Rationale}"));
        Assert.NotEqual(
            first.Placements.Single(item => item.ItemId == "dimension-a").Bounds,
            first.Placements.Single(item => item.ItemId == "dimension-b").Bounds);
        Assert.True(first.Placements.Single(item => item.ItemId == "dimension-b").DimensionTier > 0);
        Assert.Contains(first.Findings, finding => finding.Code == "annotation-repositioned");
    }

    [Fact]
    public void FixedViewOverlapIsBlockingAndRequestsSheetEscalation()
    {
        DrawingLayoutPlan plan = PartDrawingLayoutPlanner.Plan(
            BaseRequest(
                [
                    Fixed("view-front", DrawingLayoutItemKind.View, 20, 50, 80, 50),
                    Fixed("view-top", DrawingLayoutItemKind.View, 60, 70, 80, 50),
                ]));

        Assert.False(plan.CanRelease);
        Assert.Contains(plan.Findings, finding => finding.Code == "view-view-collision" && finding.Status == DrawingLayoutFindingStatus.Blocking);
        Assert.Equal(DrawingLayoutEscalationAction.UpgradeSheet, plan.Escalations.Single().Action);
        Assert.True(plan.Escalations.Single().IsAvailable);
    }

    [Fact]
    public void ReservedZoneAndOffSheetGeometryAreExplicitBlockingFindings()
    {
        DrawingLayoutPlan plan = PartDrawingLayoutPlanner.Plan(
            BaseRequest(
                [
                    Fixed("view-title-block-overlap", DrawingLayoutItemKind.View, 150, 10, 40, 35),
                    Fixed("view-off-sheet", DrawingLayoutItemKind.View, 185, 180, 30, 30),
                ]
            ) with
            {
                ReservedZones =
                [
                    new DrawingLayoutReservedZone
                    {
                        ZoneId = "title-block",
                        ZoneKind = "title-block",
                        Bounds = Rect(145, 5, 55, 45),
                    },
                ],
            });

        Assert.False(plan.CanRelease);
        Assert.Contains(plan.Findings, finding => finding.Code == "reserved-zone-collision" && finding.ZoneId == "title-block");
        Assert.Contains(plan.Findings, finding => finding.Code == "off-sheet-view" && finding.ItemIds.Contains("view-off-sheet"));
        Assert.Contains(plan.Escalations.Single().TriggerCodes, code => code == "off-sheet-view");
    }

    [Fact]
    public void AnnotationGeometryCollisionIsReflowedWithAssociationPreserved()
    {
        DrawingLayoutPlan plan = PartDrawingLayoutPlanner.Plan(
            BaseRequest(
                [
                    Fixed("view-front", DrawingLayoutItemKind.View, 70, 70, 50, 50),
                    Annotation("callout-hole-pattern", DrawingLayoutItemKind.Callout, 85, 85, 18, 8, "hole-pattern-01"),
                ]));

        DrawingLayoutPlacement placement = plan.Placements.Single(item => item.ItemId == "callout-hole-pattern");
        Assert.True(plan.CanRelease);
        Assert.True(placement.WasRepositioned);
        Assert.Equal("tier-above", placement.Rationale);
        Assert.Equal(3, placement.LeaderRoute.Length);
        Assert.Contains(plan.Findings, finding => finding.Code == "annotation-repositioned");
        Assert.DoesNotContain(plan.Findings, finding => finding.Code == "annotation-geometry-collision");
    }

    [Fact]
    public void InfeasibleDenseSheetRecordsBlockingReasonAndScaleEscalation()
    {
        DrawingLayoutPlan plan = PartDrawingLayoutPlanner.Plan(
            BaseRequest(
                [
                    Fixed("view-full-sheet", DrawingLayoutItemKind.View, 0, 0, 100, 100),
                    Annotation("dimension-inside-view", DrawingLayoutItemKind.Dimension, 35, 45, 30, 10, "plate-thickness"),
                ]) with
            {
                SheetBounds = Rect(0, 0, 100, 100),
                MaxReflowRings = 0,
                AllowSheetUpgrade = false,
                AllowAdditionalSheet = false,
            });

        Assert.False(plan.CanRelease);
        Assert.Contains(plan.Findings, finding => finding.Code == "layout-infeasible" && finding.Status == DrawingLayoutFindingStatus.Blocking);
        Assert.Equal(DrawingLayoutEscalationAction.ReduceScale, plan.Escalations.Single().Action);
        Assert.Contains("layout-infeasible", plan.Escalations.Single().Rationale, StringComparison.Ordinal);
    }

    private static DrawingLayoutRequest BaseRequest(IReadOnlyList<DrawingLayoutItem> items) => new()
    {
        SheetId = "sheet-01",
        SheetBounds = Rect(0, 0, 210, 297),
        Margins = new DrawingLayoutMargins(Mm(5), Mm(5), Mm(5), Mm(5)),
        Items = [.. items],
        MinimumAnnotationSpacing = Mm(2),
        DimensionTierSpacing = Mm(6),
        MaxReflowRings = 8,
    };

    private static DrawingLayoutItem Fixed(string id, DrawingLayoutItemKind kind, double left, double bottom, double width, double height) => new()
    {
        ItemId = id,
        Kind = kind,
        RequestedBounds = Rect(left, bottom, width, height),
        IsFixed = true,
    };

    private static DrawingLayoutItem Annotation(
        string id,
        DrawingLayoutItemKind kind,
        double left,
        double bottom,
        double width,
        double height,
        string anchorId) => new()
        {
            ItemId = id,
            Kind = kind,
            RequestedBounds = Rect(left, bottom, width, height),
            AnchorId = anchorId,
        };

    private static DrawingLayoutRect Rect(double left, double bottom, double width, double height) =>
        new(Mm(left), Mm(bottom), Mm(width), Mm(height));

    private static Length Mm(double value) => Length.FromMillimeters(value);
}

public sealed class PartDrawingCoverageLayoutTests
{
    [Fact]
    public void BlockingLayoutProofPreventsCoverageRelease()
    {
        var draft = PartDrawingRequirementSet.FromReviewedClasses(
            "layout-gate-profile",
            [PartDrawingSemanticClass.OrthographicViews],
            DateTimeOffset.Parse("2026-09-16T00:00:00Z", CultureInfo.InvariantCulture));
        PartDrawingRequirementSet approved = draft.Approve(draft.Requirements.Select(requirement => requirement.RequirementId));
        PartDrawingPlan plan = PartDrawingPlanner.Plan(new PartDrawingPlanRequest
        {
            Requirements = approved,
            PreferredPrimaryOrientationApproved = true,
        });
        DrawingLayoutPlan layout = PartDrawingLayoutPlanner.Plan(
            new DrawingLayoutRequest
            {
                SheetId = "sheet-qa",
                SheetBounds = new DrawingLayoutRect(Mm(0), Mm(0), Mm(100), Mm(100)),
                Items =
                [
                    new DrawingLayoutItem
                    {
                        ItemId = "view-a",
                        Kind = DrawingLayoutItemKind.View,
                        RequestedBounds = new DrawingLayoutRect(Mm(0), Mm(0), Mm(80), Mm(80)),
                        IsFixed = true,
                    },
                    new DrawingLayoutItem
                    {
                        ItemId = "view-b",
                        Kind = DrawingLayoutItemKind.View,
                        RequestedBounds = new DrawingLayoutRect(Mm(40), Mm(40), Mm(50), Mm(50)),
                        IsFixed = true,
                    },
                ],
            });

        PartDrawingCoverageReport report = PartDrawingCoverageAnalyzer.Analyze(
            approved,
            plan,
            Snapshot(),
            layoutPlan: layout);

        Assert.False(report.CanRelease);
        Assert.Same(layout, report.LayoutPlan);
    }

    private static CadInspectionSnapshot Snapshot() => new()
    {
        Document = new CadDocumentSummary
        {
            DocumentId = new DocumentId("drawing-layout-qa"),
            DocumentType = CadDocumentType.Drawing,
            Path = string.Empty,
            Configuration = "Default",
            StateHash = "sha256:layout-qa",
            IsDirty = false,
        },
    };

    private static Length Mm(double value) => Length.FromMillimeters(value);
}
