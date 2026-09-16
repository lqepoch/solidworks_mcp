using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.ContractTests;

/// <summary>
/// Shared behavioral contract for every CAD provider implementation.
/// </summary>
/// <remarks>
/// This suite intentionally speaks only to CadAbstractions. The FakeCad provider runs it today; the native
/// SOLIDWORKS provider must run the same workflow once its Live harness is enabled, preventing contract drift.
/// 该套件只依赖 CadAbstractions；当前由 FakeCad 执行，真实 SOLIDWORKS Provider 接入后复用同一套行为验收。
/// </remarks>
public static class CadProviderContractSuite
{
    /// <summary>Runs the minimum part-to-drawing workflow required by A03.</summary>
    public static async Task RunPartToDrawingWorkflowAsync(ICadProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        await using ICadSession session = (await provider.StartSessionAsync(
            new CadSessionOptions { RequestedSessionId = new SessionId("contract-session-001") })).RequireSuccess();

        ICadPartDocument part = (await session.CreatePartAsync(new CreatePartRequest
        {
            RequestedDocumentId = new DocumentId("contract-part-001"),
            Configuration = "Default",
        })).RequireSuccess();

        BodySnapshot body = (await part.CreateBodyAsync(new CreateBodyRequest
        {
            RequestedBodyId = new BodyId("contract-body-001"),
            Name = "Plate",
        })).RequireSuccess();
        Assert.Equal("contract-body-001", body.BodyId.Value);

        FeatureSnapshot feature = (await part.AddExtrusionAsync(new ExtrusionRequest
        {
            RequestedFeatureId = new FeatureId("contract-feature-001"),
            Name = "Plate thickness",
            Depth = Length.FromMillimeters(12d),
            TargetBodyId = body.BodyId,
        })).RequireSuccess();
        Assert.Equal("contract-feature-001", feature.FeatureId.Value);

        RebuildReceipt rebuild = (await part.RebuildAsync()).RequireSuccess();
        Assert.False(rebuild.HasErrors);

        CadInspectionSnapshot partInspection = (await session.Inspection.InspectAsync(part.DocumentId)).RequireSuccess();
        Assert.Single(partInspection.Bodies);
        Assert.Single(partInspection.Features);
        Assert.Equal(rebuild.StateHash, partInspection.Document.StateHash);

        ICadDrawingDocument drawing = (await session.CreateDrawingAsync(new CreateDrawingRequest
        {
            RequestedDocumentId = new DocumentId("contract-drawing-001"),
            SourceDocumentId = part.DocumentId,
            Configuration = "Default",
        })).RequireSuccess();
        DrawingViewSnapshot view = (await drawing.AddViewAsync(new DrawingViewRequest
        {
            RequestedViewId = new ViewId("contract-view-001"),
            Name = "Front",
            Orientation = "Front",
            Position = new Coordinate2D(Length.FromMillimeters(100d), Length.FromMillimeters(80d)),
            ScaleDenominator = 1,
        })).RequireSuccess();
        _ = (await drawing.AddAnnotationAsync(new DrawingAnnotationRequest
        {
            RequestedAnnotationId = new AnnotationId("contract-annotation-001"),
            ViewId = view.ViewId,
            Kind = "note",
            Text = "12 mm plate",
            Position = new Coordinate2D(Length.FromMillimeters(100d), Length.FromMillimeters(60d)),
        })).RequireSuccess();

        CadInspectionSnapshot drawingInspection = (await session.Inspection.InspectAsync(drawing.DocumentId)).RequireSuccess();
        Assert.Single(drawingInspection.Views);
        Assert.Single(drawingInspection.Annotations);

        ExportReceipt export = (await session.Export.ExportAsync(
            drawing.DocumentId,
            new CadExportRequest { Format = "PDF", TargetPath = "contract-output.pdf" })).RequireSuccess();
        Assert.Equal("PDF", export.Format);
        Assert.Equal(drawing.StateHash, export.SourceStateHash);

        SaveReceipt save = (await drawing.SaveAsync()).RequireSuccess();
        Assert.Equal(drawing.Path, save.Path);
        Assert.False(drawing.IsDirty);
    }

    /// <summary>Unwraps a result for assertions while preserving the original stable error in failure output.</summary>
    private static T RequireSuccess<T>(this OperationResult<T> result)
    {
        Assert.True(result.IsSuccess, $"Expected success but received {result.Error?.Code}: {result.Error?.Message}");
        Assert.NotNull(result.Value);
        return result.Value!;
    }
}
