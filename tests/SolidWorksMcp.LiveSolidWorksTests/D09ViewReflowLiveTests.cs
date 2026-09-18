using System.Globalization;
using SolidWorksMcp.AutoDrawing;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Core;
using SolidWorksMcp.Protocol;
using SolidWorksMcp.Provider.SolidWorks;

namespace SolidWorksMcp.LiveSolidWorksTests;

/// <summary>
/// Proves the native view-reflow contract with two intentionally overlapping views in one real drawing.
/// 使用真实工程图中两个刻意重叠的视图验证 native view-reflow contract。
/// </summary>
[Collection(LiveSolidWorksTestGroup.Name)]
public sealed class D09ViewReflowLiveTests
{
    /// <summary>Plans from native outlines, moves one view through SetXform, saves and reopens the drawing.</summary>
    [OptInLiveFact]
    public async Task NativeViewReflowMovesOneViewAndPersistsOutlineProof()
    {
        string? pidText = Environment.GetEnvironmentVariable("SOLIDWORKS_MCP_LIVE_PROCESS_ID");
        string? workspaceText = Environment.GetEnvironmentVariable("SOLIDWORKS_MCP_LIVE_WORKSPACE");
        if (!int.TryParse(pidText, NumberStyles.None, CultureInfo.InvariantCulture, out int processId)
            || processId <= 0
            || string.IsNullOrWhiteSpace(workspaceText))
        {
            throw new InvalidOperationException("The opt-in Live test lost its required process/workspace inputs.");
        }

        string workspace = Path.GetFullPath(workspaceText.Trim());
        Directory.CreateDirectory(workspace);
        string suffix = Guid.NewGuid().ToString("N");
        string partPath = Path.Combine(workspace, $"D09-Reflow-Part-{suffix}.sldprt");
        string drawingPath = Path.Combine(workspace, $"D09-Reflow-Drawing-{suffix}.slddrw");
        ICadPartDocument? part = null;
        ICadDrawingDocument? drawing = null;
        bool completed = false;

        await using var provider = new SolidWorksCadProvider(new CadPathAllowlist([workspace]));
        try
        {
            ICadSession session = (await provider.StartSessionAsync(new CadSessionOptions { RequestedProcessId = processId })).RequireSuccess();
            part = (await session.CreatePartAsync(new CreatePartRequest
            {
                RequestedDocumentId = new DocumentId($"d09-reflow-part-{suffix}"),
                Path = partPath,
                InitialRectangle = new RectangleProfileRequest
                {
                    Width = Length.FromMillimeters(80d),
                    Height = Length.FromMillimeters(50d),
                },
            })).RequireSuccess();
            _ = (await part.AddExtrusionAsync(new ExtrusionRequest
            {
                Name = "D09-Reflow-Extrusion",
                Depth = Length.FromMillimeters(10d),
            })).RequireSuccess();
            _ = (await part.SaveAsync()).RequireSuccess();

            drawing = (await session.CreateDrawingAsync(new CreateDrawingRequest
            {
                RequestedDocumentId = new DocumentId($"d09-reflow-drawing-{suffix}"),
                Path = drawingPath,
                SourceDocumentId = part.DocumentId,
                Configuration = part.Configuration,
            })).RequireSuccess();

            Coordinate2D overlap = new(Length.FromMillimeters(100d), Length.FromMillimeters(100d));
            _ = (await drawing.AddViewAsync(new DrawingViewRequest
            {
                RequestedViewId = new ViewId($"d09-reflow-front-{suffix}"),
                Name = "Front",
                Orientation = "Front",
                Position = overlap,
                ScaleDenominator = 1,
            })).RequireSuccess();
            _ = (await drawing.AddViewAsync(new DrawingViewRequest
            {
                RequestedViewId = new ViewId($"d09-reflow-top-{suffix}"),
                Name = "Top",
                Orientation = "Top",
                Position = overlap,
                ScaleDenominator = 1,
            })).RequireSuccess();

            CadInspectionSnapshot inspection = (await session.Inspection.InspectAsync(drawing.DocumentId)).RequireSuccess();
            Assert.NotNull(inspection.Sheet);
            DrawingSheetSnapshot sheet = inspection.Sheet!;
            Assert.All(inspection.Views, view => Assert.NotNull(view.Outline));
            DrawingViewReflowPlan plan = DrawingViewReflowPlanner.Plan(new DrawingViewReflowRequest
            {
                SheetId = sheet.Name,
                SheetBounds = new DrawingLayoutRect(Length.FromMillimeters(0d), Length.FromMillimeters(0d), sheet.Width, sheet.Height),
                Views = inspection.Views,
                MinimumViewSpacing = Length.FromMillimeters(2d),
            });
            Assert.True(plan.CanApply, string.Join("; ", plan.Findings.Select(finding => finding.Code)));
            Assert.Contains(plan.Placements, placement => placement.WasRepositioned);

            string expectedStateHash = inspection.Document.StateHash;
            foreach (DrawingViewReflowPlacement placement in plan.Placements.Where(value => value.WasRepositioned))
            {
                DrawingViewRepairReceipt receipt = (await drawing.RepositionViewAsync(new DrawingViewPositionRepairRequest
                {
                    ViewId = placement.ViewId,
                    ExpectedDocumentStateHash = expectedStateHash,
                    PreconditionFingerprint = plan.Fingerprint,
                    ExpectedCurrentPosition = placement.ExpectedCurrentPosition,
                    NewPosition = placement.NewPosition,
                })).RequireSuccess();
                Assert.Equal(placement.NewPosition, receipt.Position);
                Assert.True(receipt.Outline.Width.Millimeters > 0d);
                expectedStateHash = receipt.StateHash;
            }

            _ = (await drawing.SaveAsync()).RequireSuccess();
            CadInspectionSnapshot reopened = (await drawing.ReopenAndInspectAsync()).RequireSuccess();
            Assert.All(reopened.Views, view => Assert.NotNull(view.Outline));
            DrawingLayoutPlan finalLayout = PartDrawingLayoutPlanner.Plan(new DrawingLayoutRequest
            {
                SheetId = sheet.Name,
                SheetBounds = new DrawingLayoutRect(Length.FromMillimeters(0d), Length.FromMillimeters(0d), sheet.Width, sheet.Height),
                Items =
                [
                    .. reopened.Views.Select(view => new DrawingLayoutItem
                    {
                        ItemId = $"view:{view.ViewId.Value}",
                        Kind = DrawingLayoutItemKind.View,
                        RequestedBounds = ToRect(view.Outline!),
                        IsFixed = true,
                    }),
                ],
            });
            Assert.DoesNotContain(finalLayout.Findings, finding => finding.Code == "view-view-collision");
            Assert.True(File.Exists(partPath));
            Assert.True(File.Exists(drawingPath));
            completed = true;
        }
        finally
        {
            if (drawing is not null)
            {
                await drawing.CloseAsync(CancellationToken.None);
            }

            if (part is not null)
            {
                await part.CloseAsync(CancellationToken.None);
            }

            bool keepArtifact = string.Equals(Environment.GetEnvironmentVariable("SOLIDWORKS_MCP_LIVE_KEEP_ARTIFACT"), "1", StringComparison.Ordinal);
            if (completed && !keepArtifact)
            {
                TryDelete(partPath);
                TryDelete(drawingPath);
            }
        }
    }

    private static DrawingLayoutRect ToRect(DrawingViewOutlineSnapshot outline) => new(
        outline.Left,
        outline.Bottom,
        Length.FromMillimeters(outline.Right.Millimeters - outline.Left.Millimeters),
        Length.FromMillimeters(outline.Top.Millimeters - outline.Bottom.Millimeters));

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Live evidence is retained when cleanup is not safe; do not hide the native test result.
            // 清理不安全时保留 Live evidence，不能覆盖 native test 结果。
        }
    }
}

/// <summary>Local result assertion used only by this native Live fixture.</summary>
internal static class D09LiveResultExtensions
{
    /// <summary>Unwraps a result while preserving the native provider error in the assertion message.</summary>
    public static T RequireSuccess<T>(this OperationResult<T> result)
    {
        Assert.True(result.IsSuccess, $"Expected success but received {result.Error?.Code}: {result.Error?.Message}");
        Assert.NotNull(result.Value);
        return result.Value!;
    }
}
