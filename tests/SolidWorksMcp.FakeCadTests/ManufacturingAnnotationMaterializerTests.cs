using SolidWorksMcp.AutoDrawing;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;
using SolidWorksMcp.Provider.Fake;

namespace SolidWorksMcp.FakeCadTests;

/// <summary>
/// Verifies native-Model-Item materialization against the provider-neutral FakeCad contract.
/// 使用 Provider-neutral FakeCad contract 验证 native Model Item 物化。
/// </summary>
public sealed class ManufacturingAnnotationMaterializerTests
{
    [Fact]
    public async Task DetailViewUsesExplicitParentRegionAndScale()
    {
        await using var provider = new FakeCadProvider();
        await using ICadSession session = (await provider.StartSessionAsync(new CadSessionOptions())).RequireSuccess();
        ICadDrawingDocument drawing = (await session.CreateDrawingAsync(new CreateDrawingRequest
        {
            RequestedDocumentId = new DocumentId("detail-drawing-001"),
        })).RequireSuccess();
        DrawingViewSnapshot parent = (await drawing.AddViewAsync(new DrawingViewRequest
        {
            RequestedViewId = new ViewId("Front"),
            Name = "Front",
            Orientation = "Front",
            Position = new Coordinate2D(Length.FromMillimeters(90d), Length.FromMillimeters(125d)),
        })).RequireSuccess();

        OperationResult<DrawingViewSnapshot> detail = await drawing.AddDetailViewAsync(new DrawingDetailViewRequest
        {
            RequestedViewId = new ViewId("Detail-A"),
            ParentViewId = parent.ViewId,
            Name = "Detail A",
            Label = "A",
            DetailCenter = new Coordinate2D(Length.FromMillimeters(90d), Length.FromMillimeters(125d)),
            DetailRadius = Length.FromMillimeters(8d),
            Position = new Coordinate2D(Length.FromMillimeters(210d), Length.FromMillimeters(125d)),
            ScaleNumerator = 2,
            ScaleDenominator = 1,
        });

        Assert.True(detail.IsSuccess, detail.Error?.Message);
        Assert.Equal("Detail A", detail.Value!.Name);
        Assert.Equal("Detail A-A", detail.Value.Orientation);
        Assert.Equal(1, detail.Value.ScaleDenominator);
        CadInspectionSnapshot inspection = (await session.Inspection.InspectAsync(drawing.DocumentId)).RequireSuccess();
        Assert.Equal(2, inspection.Views.Length);
    }

    [Fact]
    public async Task ApprovedPlannedModelDimensionBecomesReleaseEvidence()
    {
        await using var provider = new FakeCadProvider();
        await using ICadSession session = (await provider.StartSessionAsync(new CadSessionOptions())).RequireSuccess();
        ICadDrawingDocument drawing = (await session.CreateDrawingAsync(new CreateDrawingRequest
        {
            RequestedDocumentId = new DocumentId("materializer-drawing-001"),
        })).RequireSuccess();
        DrawingViewSnapshot view = (await drawing.AddViewAsync(new DrawingViewRequest
        {
            RequestedViewId = new ViewId("Front"),
            Name = "Front",
            Orientation = "Front",
            Position = new Coordinate2D(Length.FromMillimeters(90d), Length.FromMillimeters(125d)),
        })).RequireSuccess();

        DrawingManufacturingAnnotationPlan plan = ManufacturingAnnotationPlanner.Plan(
            [new DrawingManufacturingAnnotationRequirement
            {
                RequirementId = "profile-depth",
                FeatureIdentity = "Profile-Extrusion",
                Kind = ManufacturingAnnotationKind.ModelDimension,
                ViewId = view.ViewId,
                CoverageKeys = ["part.profile.depth"],
            }],
            []);

        OperationResult<DrawingManufacturingAnnotationMaterialization> materialized =
            await ManufacturingAnnotationMaterializer.MaterializeAsync(drawing, plan);

        Assert.True(materialized.IsSuccess, materialized.Error?.Message);
        Assert.NotNull(materialized.Value);
        Assert.True(materialized.Value!.Plan.CanRelease);
        Assert.Equal("annotation-native-materialized", Assert.Single(materialized.Value.Plan.Items).Code);
        DrawingAnnotationSnapshot annotation = Assert.Single(materialized.Value.MaterializedAnnotations);
        CadInspectionSnapshot inspection = (await session.Inspection.InspectAsync(drawing.DocumentId)).RequireSuccess();
        Assert.Contains(inspection.Annotations, item => item.AnnotationId == annotation.AnnotationId);
    }

    [Fact]
    public async Task DuplicateProviderScopeIsRejectedBeforeAnyMutation()
    {
        await using var provider = new FakeCadProvider();
        await using ICadSession session = (await provider.StartSessionAsync(new CadSessionOptions())).RequireSuccess();
        ICadDrawingDocument drawing = (await session.CreateDrawingAsync(new CreateDrawingRequest
        {
            RequestedDocumentId = new DocumentId("materializer-drawing-002"),
        })).RequireSuccess();
        DrawingViewSnapshot view = (await drawing.AddViewAsync(new DrawingViewRequest
        {
            RequestedViewId = new ViewId("Front"),
            Name = "Front",
            Orientation = "Front",
            Position = new Coordinate2D(Length.FromMillimeters(90d), Length.FromMillimeters(125d)),
        })).RequireSuccess();

        DrawingManufacturingAnnotationPlan plan = ManufacturingAnnotationPlanner.Plan(
            [Requirement("depth-01", "Profile-Extrusion", view.ViewId), Requirement("depth-02", "Other-Feature", view.ViewId)],
            []);

        OperationResult<DrawingManufacturingAnnotationMaterialization> materialized =
            await ManufacturingAnnotationMaterializer.MaterializeAsync(drawing, plan);

        Assert.False(materialized.IsSuccess);
        Assert.Equal(ErrorCodes.InvariantViolation, materialized.Error!.Code);
        CadInspectionSnapshot inspection = (await session.Inspection.InspectAsync(drawing.DocumentId)).RequireSuccess();
        Assert.Empty(inspection.Annotations);
    }

    private static DrawingManufacturingAnnotationRequirement Requirement(
        string requirementId,
        string featureIdentity,
        ViewId viewId) => new()
        {
            RequirementId = requirementId,
            FeatureIdentity = featureIdentity,
            Kind = ManufacturingAnnotationKind.ModelDimension,
            ViewId = viewId,
        };
}
