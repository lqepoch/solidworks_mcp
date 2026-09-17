using System.Globalization;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Core;
using SolidWorksMcp.Protocol;
using SolidWorksMcp.Provider.SolidWorks;

namespace SolidWorksMcp.LiveSolidWorksTests;

/// <summary>
/// Verifies the D08 native annotation-position repair against a real SOLIDWORKS session.
/// 使用真实 SOLIDWORKS session 验证 D08 native annotation-position repair。
/// </summary>
/// <remarks>
/// The process lifecycle is deliberately outside this test. Invoke-SolidWorksLiveTests.ps1 closes every old session,
/// starts exactly one fresh process and owns its graceful shutdown. 本测试不自行管理进程；统一 harness 会关闭旧 session、
/// 启动唯一 fresh process，并负责 graceful shutdown。
/// </remarks>
[Collection(LiveSolidWorksTestGroup.Name)]
public sealed class D08AnnotationRepairLiveTests
{
    /// <summary>Creates a real note, moves only that named annotation, rebuilds, saves and reopens it.</summary>
    [OptInLiveFact]
    public async Task RepositionAnnotationUsesStableIdentityAndReadBackProof()
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
        string partPath = Path.Combine(workspace, $"D08-Repair-Part-{suffix}.sldprt");
        string drawingPath = Path.Combine(workspace, $"D08-Repair-Drawing-{suffix}.slddrw");
        bool completed = false;
        ICadSession? session = null;
        ICadPartDocument? part = null;
        ICadDrawingDocument? drawing = null;

        try
        {
            // The allowlist is the isolated Live workspace; no user design directory can be touched by this fixture.
            // allowlist 只授予隔离 Live workspace，测试不能触碰用户正式设计目录。
            await using var provider = new SolidWorksCadProvider(new CadPathAllowlist([workspace]));
            OperationResult<ICadSession> sessionResult = await provider.StartSessionAsync(
                new CadSessionOptions { RequestedProcessId = processId });
            Assert.True(sessionResult.IsSuccess, FormatError(sessionResult.Error));
            session = sessionResult.Value!;

            OperationResult<ICadPartDocument> partResult = await session.CreatePartAsync(
                new CreatePartRequest
                {
                    Path = partPath,
                    InitialRectangle = new RectangleProfileRequest
                    {
                        Width = Length.FromMillimeters(80d),
                        Height = Length.FromMillimeters(50d),
                    },
                });
            Assert.True(partResult.IsSuccess, FormatError(partResult.Error));
            part = partResult.Value!;

            OperationResult<FeatureSnapshot> extrusion = await part.AddExtrusionAsync(
                new ExtrusionRequest
                {
                    Name = "D08-Repair-Extrusion",
                    Depth = Length.FromMillimeters(10d),
                });
            Assert.True(extrusion.IsSuccess, FormatError(extrusion.Error));
            OperationResult<SaveReceipt> partSave = await part.SaveAsync();
            Assert.True(partSave.IsSuccess, FormatError(partSave.Error));

            OperationResult<ICadDrawingDocument> drawingResult = await session.CreateDrawingAsync(
                new CreateDrawingRequest
                {
                    Path = drawingPath,
                    SourceDocumentId = part.DocumentId,
                    Configuration = part.Configuration,
                });
            Assert.True(drawingResult.IsSuccess, FormatError(drawingResult.Error));
            drawing = drawingResult.Value!;

            OperationResult<DrawingViewSnapshot> view = await drawing.AddViewAsync(
                new DrawingViewRequest
                {
                    RequestedViewId = new ViewId("d08-repair-view"),
                    Name = "Front",
                    Orientation = "Front",
                    Position = new Coordinate2D(Length.FromMillimeters(100d), Length.FromMillimeters(100d)),
                    ScaleDenominator = 1,
                });
            Assert.True(view.IsSuccess, FormatError(view.Error));

            Coordinate2D oldPosition = new(Length.FromMillimeters(100d), Length.FromMillimeters(70d));
            Coordinate2D newPosition = new(Length.FromMillimeters(125d), Length.FromMillimeters(80d));
            OperationResult<DrawingAnnotationSnapshot> note = await drawing.AddAnnotationAsync(
                new DrawingAnnotationRequest
                {
                    RequestedAnnotationId = new AnnotationId("d08-repair-annotation"),
                    ViewId = view.Value!.ViewId,
                    Kind = "note",
                    Text = "D08 repair fixture",
                    Position = oldPosition,
                });
            Assert.True(note.IsSuccess, FormatError(note.Error));
            string expectedStateHash = drawing.StateHash;

            // The provider must reject a stale position even when the document state hash happens to be current.
            // 即使 document state hash 当前，Provider 也必须拒绝过期的 annotation position。
            OperationResult<DrawingRepairReceipt> stalePosition = await drawing.RepositionAnnotationAsync(
                new DrawingAnnotationPositionRepairRequest
                {
                    AnnotationId = note.Value!.AnnotationId,
                    ExpectedDocumentStateHash = expectedStateHash,
                    PreconditionFingerprint = "d08-stale-position",
                    ExpectedCurrentPosition = new Coordinate2D(Length.FromMillimeters(99d), Length.FromMillimeters(70d)),
                    NewPosition = newPosition,
                });
            Assert.False(stalePosition.IsSuccess);
            Assert.Equal(ErrorCodes.StateConflict, stalePosition.Error!.Code);

            OperationResult<DrawingRepairReceipt> repaired = await drawing.RepositionAnnotationAsync(
                new DrawingAnnotationPositionRepairRequest
                {
                    AnnotationId = note.Value.AnnotationId,
                    ExpectedDocumentStateHash = expectedStateHash,
                    PreconditionFingerprint = "d08-live-position-repair",
                    ExpectedCurrentPosition = oldPosition,
                    NewPosition = newPosition,
                });
            Assert.True(repaired.IsSuccess, FormatError(repaired.Error));
            Assert.Equal(note.Value.AnnotationId, repaired.Value!.AnnotationId);
            Assert.Equal(newPosition, repaired.Value.Position);

            OperationResult<CadInspectionSnapshot> inspected = await session.Inspection.InspectAsync(drawing.DocumentId);
            Assert.True(inspected.IsSuccess, FormatError(inspected.Error));
            DrawingAnnotationSnapshot inspectedNote = Assert.Single(inspected.Value!.Annotations);
            Assert.Equal(note.Value.AnnotationId, inspectedNote.AnnotationId);
            Assert.Equal(newPosition, inspectedNote.Position);

            OperationResult<SaveReceipt> drawingSave = await drawing.SaveAsync();
            Assert.True(drawingSave.IsSuccess, FormatError(drawingSave.Error));
            OperationResult<CadInspectionSnapshot> reopened = await drawing.ReopenAndInspectAsync();
            Assert.True(reopened.IsSuccess, FormatError(reopened.Error));
            DrawingAnnotationSnapshot reopenedNote = Assert.Single(reopened.Value!.Annotations);
            Assert.Equal(note.Value.AnnotationId, reopenedNote.AnnotationId);
            Assert.Equal(newPosition, reopenedNote.Position);
            Assert.True(File.Exists(partPath));
            Assert.True(File.Exists(drawingPath));
            completed = true;
        }
        finally
        {
            // Close only identities created by this fixture. The external harness closes the owned SOLIDWORKS PID.
            // 这里只关闭本 fixture 创建的 document identity；外部 harness 负责关闭 owned SOLIDWORKS PID。
            if (drawing is not null)
            {
                await drawing.CloseAsync(CancellationToken.None);
            }

            if (part is not null)
            {
                await part.CloseAsync(CancellationToken.None);
            }

            if (session is not null)
            {
                await session.CloseAsync(CancellationToken.None);
            }

            bool keepArtifact = string.Equals(
                Environment.GetEnvironmentVariable("SOLIDWORKS_MCP_LIVE_KEEP_ARTIFACT"),
                "1",
                StringComparison.Ordinal);
            if (completed && !keepArtifact)
            {
                TryDelete(partPath);
                TryDelete(drawingPath);
            }
        }
    }

    private static string FormatError(OperationError? error) =>
        error is null
            ? "<no-operation-error>"
            : $"code={error.Code}; category={error.Category}; message={error.Message}";

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            Console.Error.WriteLine($"live-artifact-cleanup=deferred; path={path}; reason=filesystem-lock");
        }
        catch (UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"live-artifact-cleanup=deferred; path={path}; reason=access-denied");
        }
    }
}
