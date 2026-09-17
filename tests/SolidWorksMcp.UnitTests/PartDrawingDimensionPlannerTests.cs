using System.Globalization;
using SolidWorksMcp.AutoDrawing;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.EngineeringModel;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.UnitTests;

/// <summary>
/// Verifies D05 feature-by-feature dimension planning without SOLIDWORKS, PDFs or visible-text heuristics.
/// 在没有 SOLIDWORKS、PDF 和可见文字猜测的条件下验证 D05 逐特征尺寸规划。
/// </summary>
public sealed class PartDrawingDimensionPlannerTests
{
    [Fact]
    public void MissingSlotXPositionIsReportedExactlyAndBlocksApprovedRelease()
    {
        PartDrawingDimensionRequirementGraph graph = Graph(
            new PartDrawingFeatureRequirement
            {
                FeatureId = new FeatureId("slot-s03"),
                DisplayName = "Slot S03",
                Kind = DrawingFeatureKind.Slot,
                Status = EngineeringRequirementStatus.Approved,
                Provenance = Provenance("model_native"),
                Definitions =
                [
                    Definition("width", "Width", DrawingDimensionDefinitionKind.Size),
                    Definition("x-position", "X position", DrawingDimensionDefinitionKind.PositionX),
                    Definition("y-position", "Y position", DrawingDimensionDefinitionKind.PositionY),
                ],
            });

        PartDrawingDimensionPlan plan = PartDrawingDimensionPlanner.Plan(graph, [
            Evidence("slot-s03", "width", "dim-width"),
            Evidence("slot-s03", "y-position", "dim-y"),
        ]);

        PartDrawingDimensionCoverageFinding missing = Assert.Single(plan.Findings.Where(value => value.DefinitionKey == "x-position"));
        Assert.Equal(PartDrawingDimensionFindingStatus.Blocking, missing.Status);
        Assert.Equal("missing-definition", missing.Code);
        Assert.Equal("Slot S03: X position is missing.", missing.Explanation);
        Assert.False(plan.CanRelease);
        Assert.Equal(PartDrawingDimensionPlanAction.Create, plan.Items.Single(value => value.DefinitionKey == "x-position").Action);
    }

    [Fact]
    public void HolePatternRequiresYpitchAndApprovedFunctionalDatum()
    {
        PartDrawingDimensionRequirementGraph graph = Graph(
            new PartDrawingFeatureRequirement
            {
                FeatureId = new FeatureId("hole-pattern-17"),
                DisplayName = "HolePattern17",
                Kind = DrawingFeatureKind.HolePattern,
                Status = EngineeringRequirementStatus.Approved,
                Provenance = Provenance("hole_wizard"),
                Definitions =
                [
                    Definition("diameter", "Diameter", DrawingDimensionDefinitionKind.Size),
                    Definition("quantity", "Quantity", DrawingDimensionDefinitionKind.Quantity),
                    Definition("x-pitch", "X pitch", DrawingDimensionDefinitionKind.PatternPitch),
                    Definition("y-pitch", "Y pitch", DrawingDimensionDefinitionKind.PatternPitch),
                ],
                DatumRequirements = [
                    new PartDrawingDatumRequirement
                    {
                        DatumId = "DATUM-A",
                        Role = DrawingDatumRole.Primary,
                        Status = EngineeringRequirementStatus.Approved,
                        Provenance = Provenance("dimxpert"),
                    },
                ],
            });

        PartDrawingDimensionPlan plan = PartDrawingDimensionPlanner.Plan(
            graph,
            [
                Evidence("hole-pattern-17", "diameter", "dim-diameter"),
                Evidence("hole-pattern-17", "quantity", "dim-quantity"),
                Evidence("hole-pattern-17", "x-pitch", "dim-x-pitch"),
            ]);

        PartDrawingDimensionCoverageFinding missing = Assert.Single(plan.Findings.Where(value => value.DefinitionKey == "y-pitch"));
        Assert.Equal("HolePattern17: Y pitch is missing.", missing.Explanation);
        Assert.Contains(plan.SelectedDatums, datum => datum.DatumId == "DATUM-A" && datum.Role == DrawingDatumRole.Primary);
        Assert.False(plan.CanRelease);
    }

    [Fact]
    public void FunctionalManufacturingAndReferenceClassificationAreNotInterchangeable()
    {
        PartDrawingDimensionRequirementGraph graph = Graph(
            new PartDrawingFeatureRequirement
            {
                FeatureId = new FeatureId("plate-01"),
                DisplayName = "Plate P01",
                Kind = DrawingFeatureKind.Plate,
                Status = EngineeringRequirementStatus.Approved,
                Provenance = Provenance("model_native"),
                Definitions = [Definition("thickness", "Thickness", DrawingDimensionDefinitionKind.Size, DrawingDimensionClassification.Manufacturing)],
            });

        PartDrawingDimensionPlan plan = PartDrawingDimensionPlanner.Plan(
            graph,
            [Evidence("plate-01", "thickness", "dim-reference", DrawingDimensionClassification.Reference)]);

        PartDrawingDimensionCoverageFinding finding = Assert.Single(plan.Findings);
        Assert.Equal(PartDrawingDimensionFindingStatus.ReviewRequired, finding.Status);
        Assert.Equal("classification-mismatch", finding.Code);
        Assert.False(plan.CanRelease);
    }

    [Fact]
    public void DuplicateAssociativeEvidenceHasStableWinnerAndSuppressesTheRest()
    {
        PartDrawingDimensionRequirementGraph graph = Graph(
            new PartDrawingFeatureRequirement
            {
                FeatureId = new FeatureId("hole-01"),
                DisplayName = "Hole H01",
                Kind = DrawingFeatureKind.Hole,
                Status = EngineeringRequirementStatus.Approved,
                Provenance = Provenance("model_native"),
                Definitions = [Definition("diameter", "Diameter", DrawingDimensionDefinitionKind.Size)],
            });

        PartDrawingDimensionPlan plan = PartDrawingDimensionPlanner.Plan(
            graph,
            [
                Evidence("hole-01", "diameter", "dim-z"),
                Evidence("hole-01", "diameter", "dim-a"),
            ]);

        Assert.Equal(PartDrawingDimensionFindingStatus.Warning, Assert.Single(plan.Findings).Status);
        Assert.Equal(PartDrawingDimensionPlanAction.RetainExisting, plan.Items.Single(value => value.ExistingDimensionId?.Value == "dim-a").Action);
        Assert.Equal(PartDrawingDimensionPlanAction.SuppressRedundant, plan.Items.Single(value => value.ExistingDimensionId?.Value == "dim-z").Action);
        Assert.True(plan.CanRelease);
    }

    [Fact]
    public void MultipleApprovedDatumsRemainReviewRequiredEvenWhenDimensionsAreCovered()
    {
        PartDrawingDimensionRequirementGraph graph = Graph(
            new PartDrawingFeatureRequirement
            {
                FeatureId = new FeatureId("hole-02"),
                DisplayName = "Hole H02",
                Kind = DrawingFeatureKind.Hole,
                Status = EngineeringRequirementStatus.Approved,
                Provenance = Provenance("model_native"),
                Definitions = [Definition("diameter", "Diameter", DrawingDimensionDefinitionKind.Size)],
                DatumRequirements =
                [
                    new PartDrawingDatumRequirement
                    {
                        DatumId = "DATUM-A",
                        Role = DrawingDatumRole.Primary,
                        Status = EngineeringRequirementStatus.Approved,
                        Provenance = Provenance("dimxpert"),
                    },
                    new PartDrawingDatumRequirement
                    {
                        DatumId = "DATUM-B",
                        Role = DrawingDatumRole.Primary,
                        Status = EngineeringRequirementStatus.Approved,
                        Provenance = Provenance("pmi"),
                    },
                ],
            });

        PartDrawingDimensionPlan plan = PartDrawingDimensionPlanner.Plan(
            graph,
            [Evidence("hole-02", "diameter", "dim-diameter")]);

        Assert.Equal(PartDrawingDimensionPlanStatus.ReviewRequired, plan.Status);
        Assert.False(plan.CanRelease);
        Assert.Contains("multiple-approved-datum-candidates:hole-02:Primary", plan.Diagnostics);
    }

    [Fact]
    public void CoverageReportAggregatesDimensionFindingsWithoutTextMatching()
    {
        PartDrawingRequirementSet requirements = PartDrawingRequirementSet.FromReviewedClasses(
            "dimension-coverage",
            [PartDrawingSemanticClass.OrthographicViews],
            DateTimeOffset.Parse("2026-09-16T00:00:00Z", CultureInfo.InvariantCulture))
            .Approve([.. PartDrawingRequirementSet.FromReviewedClasses(
                "dimension-coverage",
                [PartDrawingSemanticClass.OrthographicViews],
                DateTimeOffset.Parse("2026-09-16T00:00:00Z", CultureInfo.InvariantCulture)).Requirements.Select(value => value.RequirementId)]);
        PartDrawingPlan drawingPlan = PartDrawingPlanner.Plan(new PartDrawingPlanRequest
        {
            Requirements = requirements,
            PreferredPrimaryOrientationApproved = true,
        });
        PartDrawingDimensionPlan dimensionPlan = PartDrawingDimensionPlanner.Plan(
            Graph(new PartDrawingFeatureRequirement
            {
                FeatureId = new FeatureId("slot-s03"),
                DisplayName = "Slot S03",
                Kind = DrawingFeatureKind.Slot,
                Status = EngineeringRequirementStatus.Approved,
                Provenance = Provenance("model_native"),
                Definitions = [Definition("x-position", "X position", DrawingDimensionDefinitionKind.PositionX)],
            }), []);

        PartDrawingCoverageReport report = PartDrawingCoverageAnalyzer.Analyze(requirements, drawingPlan, Snapshot(), dimensionPlan);

        Assert.False(report.CanRelease);
        Assert.Equal("Slot S03: X position is missing.", Assert.Single(report.DimensionFindings).Explanation);
    }

    private static PartDrawingDimensionRequirementGraph Graph(params PartDrawingFeatureRequirement[] features) =>
        new("d05-test-profile", features);

    private static PartDrawingDimensionDefinition Definition(
        string key,
        string name,
        DrawingDimensionDefinitionKind kind,
        DrawingDimensionClassification classification = DrawingDimensionClassification.Manufacturing) =>
        new()
        {
            DefinitionKey = key,
            DisplayName = name,
            Kind = kind,
            Classification = classification,
        };

    private static PartDrawingDimensionEvidence Evidence(
        string featureId,
        string definitionKey,
        string dimensionId,
        DrawingDimensionClassification classification = DrawingDimensionClassification.Manufacturing) =>
        new()
        {
            FeatureId = new FeatureId(featureId),
            DefinitionKey = definitionKey,
            DimensionId = new DimensionId(dimensionId),
            Classification = classification,
            IsAssociative = true,
            IsVisible = true,
            Provenance = Provenance("model_native"),
        };

    private static EngineeringProvenance Provenance(string source) =>
        new(source, "synthetic_unit_fixture", DateTimeOffset.Parse("2026-09-16T00:00:00Z", CultureInfo.InvariantCulture));

    private static CadInspectionSnapshot Snapshot() => new()
    {
        Document = new CadDocumentSummary
        {
            DocumentId = new DocumentId("drawing-01"),
            DocumentType = CadDocumentType.Drawing,
            Path = string.Empty,
            Configuration = "Default",
            StateHash = "sha256:test",
            IsDirty = false,
        },
    };
}
