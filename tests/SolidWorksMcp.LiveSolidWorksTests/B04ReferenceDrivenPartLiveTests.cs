using System.Globalization;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Core;
using SolidWorksMcp.Protocol;
using SolidWorksMcp.Provider.SolidWorks;

namespace SolidWorksMcp.LiveSolidWorksTests;

/// <summary>
/// Opt-in native proof for a non-cylindrical reference-driven slice: an L-bracket profile, semantic hole group and drawing.
/// 显式 opt-in 的非圆柱参考驱动切片：L 形支架轮廓、具备工程语义的孔组和真实工程图。
/// </summary>
/// <remarks>
/// This fixture intentionally uses a redacted engineering class rather than any confidential drawing text or dimensions.
/// The private drawing workflow records only feature classes; this test is the provider contract for that class.
/// 本 fixture 只使用脱敏后的工程类别，不包含任何机密图纸文字或尺寸；私密图纸流程只记录特征类别，本测试验证该类别的
/// Provider contract。
/// </remarks>
public sealed class B04ReferenceDrivenPartLiveTests
{
    [OptInLiveFact]
    public async Task CreateReferenceBracketWithThroughHoleGroupAndDrawing()
    {
        string? pidText = Environment.GetEnvironmentVariable("SOLIDWORKS_MCP_LIVE_PROCESS_ID");
        string? workspaceText = Environment.GetEnvironmentVariable("SOLIDWORKS_MCP_LIVE_WORKSPACE");
        if (!int.TryParse(pidText, NumberStyles.None, CultureInfo.InvariantCulture, out int processId)
            || processId <= 0
            || string.IsNullOrWhiteSpace(workspaceText))
        {
            throw new InvalidOperationException(
                "The opt-in Live test was discovered as runnable, but its required process/workspace inputs disappeared.");
        }

        string workspace = Path.GetFullPath(workspaceText.Trim());
        Directory.CreateDirectory(workspace);
        string suffix = Guid.NewGuid().ToString("N");
        string partPath = Path.Combine(workspace, $"B04-Reference-Part-{suffix}.sldprt");
        string drawingPath = Path.Combine(workspace, $"B04-Reference-Part-{suffix}.slddrw");
        bool completed = false;
        try
        {
            await using var provider = new SolidWorksCadProvider(new CadPathAllowlist([workspace]));
            OperationResult<ICadSession> sessionResult = await provider.StartSessionAsync(
                new CadSessionOptions { RequestedProcessId = processId });
            Assert.True(sessionResult.IsSuccess, FormatError(sessionResult.Error));
            ICadSession session = sessionResult.Value!;

            // The profile is deliberately an asymmetric L-bracket rather than a primitive cylinder or a single
            // rectangle. It proves that the provider executes a reference-driven closed-profile plan and preserves
            // a repeated hole group as engineering intent. 这里故意使用非对称 L 形闭合轮廓，而不是圆柱或单个矩形，
            // 证明 Provider 执行参考驱动的 profile plan，并保留重复孔组的工程语义。
            OperationResult<ICadPartDocument> createResult = await session.CreatePartAsync(
                new CreatePartRequest
                {
                    Path = partPath,
                    InitialPolygon = new PolygonProfileRequest
                    {
                        Vertices =
                        [
                            new Coordinate2D(Length.FromMillimeters(-40d), Length.FromMillimeters(-25d)),
                            new Coordinate2D(Length.FromMillimeters(40d), Length.FromMillimeters(-25d)),
                            new Coordinate2D(Length.FromMillimeters(40d), Length.FromMillimeters(25d)),
                            new Coordinate2D(Length.FromMillimeters(-8d), Length.FromMillimeters(25d)),
                            new Coordinate2D(Length.FromMillimeters(-8d), Length.FromMillimeters(-17d)),
                            new Coordinate2D(Length.FromMillimeters(-40d), Length.FromMillimeters(-17d)),
                        ],
                    },
                });
            Assert.True(createResult.IsSuccess, FormatError(createResult.Error));
            ICadPartDocument part = createResult.Value!;

            OperationResult<FeatureSnapshot> extrusion = await part.AddExtrusionAsync(
                new ExtrusionRequest
                {
                    Name = "ReferenceBracket-Thickness",
                    Depth = Length.FromMillimeters(8d),
                });
            Assert.True(extrusion.IsSuccess, FormatError(extrusion.Error));

            OperationResult<FeatureSnapshot> holes = await part.AddThroughHolePatternAsync(
                new ThroughHolePatternRequest
                {
                    Name = "ReferenceHolePattern-2X",
                    Diameter = Length.FromMillimeters(6d),
                    Centers =
                    [
                        new Coordinate2D(Length.FromMillimeters(16d), Length.FromMillimeters(-10d)),
                        new Coordinate2D(Length.FromMillimeters(16d), Length.FromMillimeters(10d)),
                    ],
                });
            Assert.True(holes.IsSuccess, FormatError(holes.Error));
            Assert.False(string.IsNullOrWhiteSpace(holes.Value!.Name));
            Assert.Contains(
                holes.Evidence!.Observations,
                observation => observation.Key == "semantic.name" && observation.Value == "ReferenceHolePattern-2X");

            OperationResult<RebuildReceipt> rebuild = await part.RebuildAsync();
            Assert.True(rebuild.IsSuccess, FormatError(rebuild.Error));
            Assert.False(rebuild.Value!.HasErrors);

            OperationResult<CadInspectionSnapshot> inspection = await session.Inspection.InspectAsync(part.DocumentId);
            Assert.True(inspection.IsSuccess, FormatError(inspection.Error));
            Assert.Single(inspection.Value!.Bodies);
            Assert.True(inspection.Value.Bodies[0].Volume.CubicMillimeters > 0d);
            FeatureSnapshot extrusionFeature = extrusion.Value!;
            FeatureSnapshot holeFeature = holes.Value!;
            Assert.Contains(inspection.Value.Features, feature => feature.Name == extrusionFeature.Name);
            Assert.Contains(inspection.Value.Features, feature => feature.Name == holeFeature.Name);

            OperationResult<SaveReceipt> save = await part.SaveAsync();
            Assert.True(save.IsSuccess, FormatError(save.Error));
            Assert.True(File.Exists(partPath));

            OperationResult<ICadDrawingDocument> drawingCreate = await session.CreateDrawingAsync(
                new CreateDrawingRequest
                {
                    Path = drawingPath,
                    SourceDocumentId = part.DocumentId,
                    Configuration = part.Configuration,
                });
            Assert.True(drawingCreate.IsSuccess, FormatError(drawingCreate.Error));
            ICadDrawingDocument drawing = drawingCreate.Value!;

            DrawingViewSnapshot? frontView = null;
            foreach ((string orientation, double x, double y) in new[]
                     {
                         ("Front", 90d, 125d),
                         ("Top", 90d, 210d),
                         ("Isometric", 210d, 125d),
                     })
            {
                OperationResult<DrawingViewSnapshot> view = await drawing.AddViewAsync(
                    new DrawingViewRequest
                    {
                        Name = orientation,
                        Orientation = orientation,
                        Position = new Coordinate2D(Length.FromMillimeters(x), Length.FromMillimeters(y)),
                        ScaleDenominator = 1,
                    });
                Assert.True(view.IsSuccess, $"{orientation}: {FormatError(view.Error)}");
                if (orientation.Equals("Front", StringComparison.OrdinalIgnoreCase))
                {
                    frontView = view.Value;
                }
            }

            Assert.NotNull(frontView);
            OperationResult<DrawingAnnotationSnapshot> note = await drawing.AddAnnotationAsync(
                new DrawingAnnotationRequest
                {
                    RequestedAnnotationId = new AnnotationId("ReferenceBracket-note"),
                    ViewId = frontView!.ViewId,
                    Kind = "note",
                    Text = "REFERENCE BRACKET",
                    CoverageKeys = ["reference-bracket.semantic-note"],
                    Position = new Coordinate2D(Length.FromMillimeters(45d), Length.FromMillimeters(235d)),
                });
            Assert.True(note.IsSuccess, FormatError(note.Error));
            Assert.Equal("note", note.Value!.Kind);
            Assert.Equal("REFERENCE BRACKET", note.Value.Text);

            OperationResult<RebuildReceipt> drawingRebuild = await drawing.RebuildAsync();
            Assert.True(drawingRebuild.IsSuccess, FormatError(drawingRebuild.Error));
            Assert.False(drawingRebuild.Value!.HasErrors);

            OperationResult<CadInspectionSnapshot> drawingInspection = await session.Inspection.InspectAsync(drawing.DocumentId);
            Assert.True(drawingInspection.IsSuccess, FormatError(drawingInspection.Error));
            Assert.True(drawingInspection.Value!.Views.Length >= 3);
            Assert.Contains(drawingInspection.Value.Views, view => view.Orientation.Contains("Front", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(drawingInspection.Value.Views, view => view.Orientation.Contains("Top", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(drawingInspection.Value.Views, view => view.Orientation.Contains("Isometric", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(
                drawingInspection.Value.Annotations,
                annotation => annotation.AnnotationId == note.Value.AnnotationId
                    && annotation.Kind == "note"
                    && annotation.Text == "REFERENCE BRACKET");

            OperationResult<SaveReceipt> drawingSave = await drawing.SaveAsync();
            Assert.True(drawingSave.IsSuccess, FormatError(drawingSave.Error));
            Assert.True(File.Exists(drawingPath));

            OperationResult<CadInspectionSnapshot> reopenedDrawing = await drawing.ReopenAndInspectAsync();
            Assert.True(reopenedDrawing.IsSuccess, FormatError(reopenedDrawing.Error));
            Assert.True(reopenedDrawing.Value!.Views.Length >= 3);
            Assert.Contains(
                reopenedDrawing.Value.Annotations,
                annotation => annotation.AnnotationId == note.Value.AnnotationId
                    && annotation.Kind == "note"
                    && annotation.Text == "REFERENCE BRACKET");
            Assert.Equal(drawingSave.Value!.StateHash, reopenedDrawing.Value.Document.StateHash);

            Assert.True((await drawing.CloseAsync()).IsSuccess);
            OperationResult<CadInspectionSnapshot> reopenedPart = await part.ReopenAndInspectAsync();
            Assert.True(reopenedPart.IsSuccess, FormatError(reopenedPart.Error));
            Assert.Single(reopenedPart.Value!.Bodies);
            Assert.True(reopenedPart.Value.Bodies[0].Volume.CubicMillimeters > 0d);
            Assert.Contains(reopenedPart.Value.Features, feature => feature.Name == holes.Value.Name);
            Assert.Equal(save.Value!.StateHash, reopenedPart.Value.Document.StateHash);

            Assert.True((await part.CloseAsync()).IsSuccess);
            completed = true;
            await session.CloseAsync();
        }
        finally
        {
            bool keepArtifact = string.Equals(
                Environment.GetEnvironmentVariable("SOLIDWORKS_MCP_LIVE_KEEP_ARTIFACT"),
                "1",
                StringComparison.Ordinal);
            if (completed && !keepArtifact)
            {
                foreach (string artifactPath in new[] { partPath, drawingPath })
                {
                    try
                    {
                        if (File.Exists(artifactPath))
                        {
                            File.Delete(artifactPath);
                        }
                    }
                    catch (IOException)
                    {
                        Console.Error.WriteLine("live-artifact-cleanup=deferred; reason=document-still-open");
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Console.Error.WriteLine("live-artifact-cleanup=deferred; reason=filesystem-lock");
                    }
                }
            }
        }
    }

    private static string FormatError(OperationError? error)
    {
        if (error is null)
        {
            return "<no-operation-error>";
        }

        string details = error.Details.Count == 0
            ? string.Empty
            : $" details={string.Join(';', error.Details.Select(pair => $"{pair.Key}={pair.Value}"))}";
        return $"code={error.Code}; category={error.Category}; message={error.Message};{details}";
    }
}
