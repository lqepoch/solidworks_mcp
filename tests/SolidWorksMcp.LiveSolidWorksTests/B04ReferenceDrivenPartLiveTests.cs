using System.Globalization;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Core;
using SolidWorksMcp.Protocol;
using SolidWorksMcp.Provider.SolidWorks;

namespace SolidWorksMcp.LiveSolidWorksTests;

/// <summary>
/// Opt-in native proof for a rounded reference-driven slice: an arc-based plate profile, semantic hole group and drawing.
/// 显式 opt-in 的圆角参考驱动切片：由圆弧组成的板件轮廓、具备工程语义的孔组和真实工程图。
/// </summary>
/// <remarks>
/// This fixture intentionally uses a redacted engineering class rather than any confidential drawing text or dimensions.
/// The private drawing workflow records only feature classes; this test is the provider contract for that class.
/// 本 fixture 只使用脱敏后的工程类别，不包含任何机密图纸文字或尺寸；私密图纸流程只记录特征类别，本测试验证该类别的
/// Provider contract。
/// </remarks>
[Collection(LiveSolidWorksTestGroup.Name)]
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

            // The profile is deliberately a rounded rectangle made from native lines and three-point arcs rather
            // than a primitive cylinder or a sharp polygon. It proves curved boundary intent and repeated-hole
            // semantics. 这里故意使用由原生直线和三点圆弧组成的圆角矩形，而不是圆柱或尖角多边形，证明曲线
            // 轮廓意图和重复孔组工程语义都被保留。
            OperationResult<ICadPartDocument> createResult = await session.CreatePartAsync(
                new CreatePartRequest
                {
                    Path = partPath,
                    InitialSketchProfile = RoundedPlateProfile(),
                });
            Assert.True(createResult.IsSuccess, FormatError(createResult.Error));
            ICadPartDocument part = createResult.Value!;

            OperationResult<FeatureSnapshot> extrusion = await part.AddExtrusionAsync(
                new ExtrusionRequest
                {
                    Name = "ReferenceRoundedPlate-Thickness",
                    Depth = Length.FromMillimeters(8d),
                });
            Assert.True(extrusion.IsSuccess, FormatError(extrusion.Error));

            // Keep one native driving dimension in the part before model-item insertion. The drawing compiler must
            // consume a dimension owned by SOLIDWORKS, not infer text from the rounded profile or manufacture a
            // display dimension from the test request. 先在零件中保留一个由 SOLIDWORKS 所有的 driving dimension，
            // 再执行 Model Items 插入；工程图编译器必须消费 native dimension，不能从圆角轮廓猜文字，也不能
            // 用测试请求伪造显示尺寸。
            OperationResult<DimensionSnapshot> thicknessDimension = await part.SetDimensionValueAsync(
                new DimensionUpdateRequest
                {
                    ParameterName = $"D1@{extrusion.Value!.Name}",
                    Value = Length.FromMillimeters(8d),
                    Configuration = part.Configuration,
                });
            Assert.True(thicknessDimension.IsSuccess, FormatError(thicknessDimension.Error));
            Assert.Equal(8d, thicknessDimension.Value!.Value.Millimeters, precision: 8);

            OperationResult<FeatureSnapshot> holes = await part.AddThroughHolePatternAsync(
                new ThroughHolePatternRequest
                {
                    Name = "ReferenceRoundedPlate-HolePattern-2X",
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
                observation => observation.Key == "semantic.name" && observation.Value == "ReferenceRoundedPlate-HolePattern-2X");

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

            // B05 proof: the native provider resolves an exact feature by declarative semantic identity while the
            // document state hash is still the one observed immediately before the operation.  No global selection
            // mark, active-document guess or feature-list index is sent to the provider.
            // B05 证明：native provider 在 mutation 前按声明式语义 identity 解析精确 feature，并校验刚刚观察到的
            // document state hash。调用方不发送全局 selection mark、不猜 active document，也不依赖 feature 列表索引。
            OperationResult<CadSelectionSnapshot> semanticSelection = await session.Selection.ResolveAsync(
                new CadEntitySelector
                {
                    DocumentId = part.DocumentId,
                    EntityKind = CadEntityKind.Feature,
                    SemanticName = holeFeature.Name,
                    ExpectedStateHash = inspection.Value.Document.StateHash,
                });
            Assert.True(semanticSelection.IsSuccess, FormatError(semanticSelection.Error));
            Assert.Equal(CadSelectionResolution.SemanticSelector, semanticSelection.Value!.Resolution);
            Assert.Equal($"{part.DocumentId.Value}:feature:{holeFeature.Name}", semanticSelection.Value.Entity.Identity);
            Assert.NotNull(semanticSelection.Value.PersistentReference);
            Assert.Equal("solidworks.persist3", semanticSelection.Value.PersistentReference!.Format);

            // The token is opaque to the test and to MCP callers: it is round-tripped unchanged into a second
            // selector, proving the native persistent-reference path rather than a repeated name lookup.
            // token 对测试和 MCP caller 都是不透明的：这里原样回传到第二个 selector，证明的是 native persistent
            // reference path，而不是再次按名字查找。
            OperationResult<CadSelectionSnapshot> persistentSelection = await session.Selection.ResolveAsync(
                new CadEntitySelector
                {
                    DocumentId = part.DocumentId,
                    EntityKind = CadEntityKind.Feature,
                    PersistentReference = semanticSelection.Value.PersistentReference,
                    ExpectedStateHash = inspection.Value.Document.StateHash,
                });
            Assert.True(persistentSelection.IsSuccess, FormatError(persistentSelection.Error));
            Assert.Equal(CadSelectionResolution.PersistentReference, persistentSelection.Value!.Resolution);
            Assert.Equal(semanticSelection.Value.Entity, persistentSelection.Value.Entity);

            // The geometry-signature fallback is provider-defined and deterministic; it is still checked against the
            // current native feature tree rather than treated as a free-form name alias.
            // geometry-signature fallback 由 Provider 定义且确定性校验；它仍会对当前 native feature tree 做验证，
            // 不是把任意字符串当作 name alias。
            OperationResult<CadSelectionSnapshot> geometrySelection = await session.Selection.ResolveAsync(
                new CadEntitySelector
                {
                    DocumentId = part.DocumentId,
                    EntityKind = CadEntityKind.Feature,
                    GeometrySignature = new CadGeometrySignature
                    {
                        Value = $"solidworks.geometry.v1|kind=Feature|name={holeFeature.Name}",
                    },
                    ExpectedStateHash = inspection.Value.Document.StateHash,
                });
            Assert.True(geometrySelection.IsSuccess, FormatError(geometrySelection.Error));
            Assert.Equal(CadSelectionResolution.GeometrySignature, geometrySelection.Value!.Resolution);
            Assert.Equal(semanticSelection.Value.Entity, geometrySelection.Value.Entity);

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
                    RequestedAnnotationId = new AnnotationId("ReferenceRoundedPlate-note"),
                    ViewId = frontView!.ViewId,
                    Kind = "note",
                    Text = "REFERENCE ROUNDED PLATE",
                    CoverageKeys = ["reference-rounded-plate.semantic-note"],
                    Position = new Coordinate2D(Length.FromMillimeters(45d), Length.FromMillimeters(235d)),
                });
            Assert.True(note.IsSuccess, FormatError(note.Error));
            Assert.Equal("note", note.Value!.Kind);
            Assert.Equal("REFERENCE ROUNDED PLATE", note.Value.Text);

            OperationResult<DrawingAnnotationSnapshot> modelDimensions = await drawing.AddAnnotationAsync(
                new DrawingAnnotationRequest
                {
                    RequestedAnnotationId = new AnnotationId("ReferenceRoundedPlate-model-dimension"),
                    ViewId = frontView.ViewId,
                    Kind = "model-dimensions",
                    CoverageKeys = ["reference-rounded-plate.native-model-dimension"],
                    Position = new Coordinate2D(Length.FromMillimeters(45d), Length.FromMillimeters(225d)),
                });
            Assert.True(modelDimensions.IsSuccess, FormatError(modelDimensions.Error));
            Assert.Equal("model-dimension", modelDimensions.Value!.Kind);
            Assert.False(string.IsNullOrWhiteSpace(modelDimensions.Value.Text));

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
                    && annotation.Text == "REFERENCE ROUNDED PLATE");
            Assert.Contains(
                drawingInspection.Value.Annotations,
                annotation => annotation.Kind == "model-dimension"
                    && !string.IsNullOrWhiteSpace(annotation.Text));

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
                    && annotation.Text == "REFERENCE ROUNDED PLATE");
            Assert.Contains(
                reopenedDrawing.Value.Annotations,
                annotation => annotation.Kind == "model-dimension"
                    && !string.IsNullOrWhiteSpace(annotation.Text));
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

    /// <summary>Builds a generic rounded plate profile without copying confidential drawing dimensions.</summary>
    private static SketchProfileRequest RoundedPlateProfile() => new()
    {
        Segments =
        [
            Curve(SketchCurveKind.Line, (-32d, -25d), (32d, -25d)),
            Curve(SketchCurveKind.ThreePointArc, (32d, -25d), (40d, -17d), (37.657d, -22.657d)),
            Curve(SketchCurveKind.Line, (40d, -17d), (40d, 17d)),
            Curve(SketchCurveKind.ThreePointArc, (40d, 17d), (32d, 25d), (37.657d, 22.657d)),
            Curve(SketchCurveKind.Line, (32d, 25d), (-32d, 25d)),
            Curve(SketchCurveKind.ThreePointArc, (-32d, 25d), (-40d, 17d), (-37.657d, 22.657d)),
            Curve(SketchCurveKind.Line, (-40d, 17d), (-40d, -17d)),
            Curve(SketchCurveKind.ThreePointArc, (-40d, -17d), (-32d, -25d), (-37.657d, -22.657d)),
        ],
    };

    private static SketchCurveRequest Curve(
        SketchCurveKind kind,
        (double X, double Y) start,
        (double X, double Y) end,
        (double X, double Y)? through = null) => new()
        {
            Kind = kind,
            Start = Point(start),
            End = Point(end),
            Through = Point(through ?? (0d, 0d)),
        };

    private static Coordinate2D Point((double X, double Y) point) => new(
        Length.FromMillimeters(point.X),
        Length.FromMillimeters(point.Y));

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
