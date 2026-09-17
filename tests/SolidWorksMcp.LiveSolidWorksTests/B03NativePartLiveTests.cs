using System.Globalization;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Core;
using SolidWorksMcp.Protocol;
using SolidWorksMcp.Provider.SolidWorks;

namespace SolidWorksMcp.LiveSolidWorksTests;

/// <summary>
/// Opt-in real SOLIDWORKS B03 proof: create, inspect, save, close, reopen and re-inspect a persisted part.
/// 显式 opt-in 的真实 SOLIDWORKS B03 证明：创建、inspection、保存、关闭、重新打开并再次 inspection 持久化零件。
/// </summary>
/// <remarks>
/// Hosted CI never runs this test because it needs a user-authorized interactive SOLIDWORKS process.  The test does
/// not launch or close SOLIDWORKS; it requires a specific PID, an explicit isolated workspace, and leaves failed
/// artifacts for diagnosis. Hosted CI 不运行此测试，因为它需要用户授权的交互式 SOLIDWORKS；测试不启动/关闭
/// SOLIDWORKS，而是要求明确 PID 和隔离 workspace，并在失败时保留 artifact 供诊断。
/// </remarks>
[Collection(LiveSolidWorksTestGroup.Name)]
public sealed class B03NativePartLiveTests
{
    /// <summary>Runs this test only when the operator explicitly supplied the native Live inputs.</summary>
    [OptInLiveFact]
    public async Task CreateSketchExtrudeRebuildInspectAndSave()
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
        string partPath = Path.Combine(workspace, $"B03-Circle-{Guid.NewGuid():N}.sldprt");
        string drawingPath = Path.Combine(workspace, $"B03-Circle-{Guid.NewGuid():N}.slddrw");
        bool completed = false;
        try
        {
            // The Live harness grants this run only its isolated temporary workspace; the provider denies all other
            // native create paths. Live harness 仅向本次运行授予隔离临时 workspace；Provider 拒绝所有其它 native path。
            await using var provider = new SolidWorksCadProvider(new CadPathAllowlist([workspace]));
            OperationResult<ICadSession> sessionResult = await provider.StartSessionAsync(
                new CadSessionOptions { RequestedProcessId = processId });
            Assert.True(sessionResult.IsSuccess, FormatError(sessionResult.Error));
            ICadSession session = sessionResult.Value!;

            OperationResult<ICadPartDocument> createResult = await session.CreatePartAsync(
                new CreatePartRequest
                {
                    Path = partPath,
                    InitialCircleRadius = Length.FromMillimeters(25d),
                });
            Assert.True(createResult.IsSuccess, FormatError(createResult.Error));
            ICadPartDocument part = createResult.Value!;

            OperationResult<CadInspectionSnapshot> beforeExtrusion = await session.Inspection.InspectAsync(part.DocumentId);
            Assert.True(beforeExtrusion.IsSuccess, FormatError(beforeExtrusion.Error));

            OperationResult<FeatureSnapshot> extrusion = await part.AddExtrusionAsync(
                new ExtrusionRequest
                {
                    Name = "B03-Circle-Extrusion",
                    Depth = Length.FromMillimeters(10d),
                });
            Assert.True(
                extrusion.IsSuccess,
                $"{FormatError(extrusion.Error)} pre-extrusion-features="
                + string.Join(
                    ",",
                    beforeExtrusion.Value!.Features.Select(feature => $"{feature.Name}:{feature.Kind}"))
                + $" pre-extrusion-bodies={beforeExtrusion.Value.Bodies.Length}");
            Assert.Equal(10d, extrusion.Value!.Depth!.Value.Millimeters, precision: 8);

            OperationResult<RebuildReceipt> rebuild = await part.RebuildAsync();
            Assert.True(rebuild.IsSuccess, FormatError(rebuild.Error));
            Assert.False(rebuild.Value!.HasErrors);

            OperationResult<CadInspectionSnapshot> inspection = await session.Inspection.InspectAsync(part.DocumentId);
            Assert.True(inspection.IsSuccess, FormatError(inspection.Error));
            Assert.Single(inspection.Value!.Bodies);
            Assert.Contains(inspection.Value.Features, feature => feature.Name == extrusion.Value.Name);
            Assert.True(inspection.Value.Bodies[0].Volume.CubicMillimeters > 0d);
            Assert.NotEqual("", inspection.Value.Document.StateHash);

            double volumeBeforeDimension = inspection.Value.Bodies[0].Volume.CubicMillimeters;
            OperationResult<DimensionSnapshot> changedDimension = await part.SetDimensionValueAsync(
                new DimensionUpdateRequest
                {
                    ParameterName = $"D1@{extrusion.Value.Name}",
                    Value = Length.FromMillimeters(15d),
                    Configuration = part.Configuration,
                });
            Assert.True(changedDimension.IsSuccess, FormatError(changedDimension.Error));
            Assert.Equal("D1", changedDimension.Value!.Name);
            Assert.Equal(15d, changedDimension.Value.Value.Millimeters, precision: 8);

            OperationResult<CadInspectionSnapshot> afterDimension = await session.Inspection.InspectAsync(part.DocumentId);
            Assert.True(afterDimension.IsSuccess, FormatError(afterDimension.Error));
            Assert.Single(afterDimension.Value!.Bodies);
            Assert.True(afterDimension.Value.Bodies[0].Volume.CubicMillimeters > volumeBeforeDimension);
            Assert.Contains(afterDimension.Value.Features, feature => feature.Name == extrusion.Value.Name);

            OperationResult<SaveReceipt> save = await part.SaveAsync();
            Assert.True(save.IsSuccess, FormatError(save.Error));
            Assert.True(File.Exists(partPath));

            // This is the first real 3D-to-2D proof: the drawing is created by SOLIDWORKS from its local template,
            // native views are inserted from the persisted part, and the saved .slddrw is reopened and inspected.
            // 这是首个真实三维到二维证明：由 SOLIDWORKS 使用本机 template 建图，从已持久化零件插入原生视图，
            // 保存 .slddrw 后重新打开并 inspection；这里不是 FakeCad 或仅测试内存对象。
            OperationResult<ICadDrawingDocument> drawingCreate = await session.CreateDrawingAsync(
                new CreateDrawingRequest
                {
                    Path = drawingPath,
                    SourceDocumentId = part.DocumentId,
                    Configuration = part.Configuration,
                });
            Assert.True(drawingCreate.IsSuccess, FormatError(drawingCreate.Error));
            ICadDrawingDocument drawing = drawingCreate.Value!;

            OperationResult<DrawingViewSnapshot> frontView = await drawing.AddViewAsync(
                new DrawingViewRequest
                {
                    RequestedViewId = new ViewId("b03-front-view"),
                    Name = "Front",
                    Orientation = "Front",
                    Position = new Coordinate2D(Length.FromMillimeters(100d), Length.FromMillimeters(130d)),
                    ScaleDenominator = 1,
                });
            Assert.True(frontView.IsSuccess, FormatError(frontView.Error));

            OperationResult<DrawingViewSnapshot> isometricView = await drawing.AddViewAsync(
                new DrawingViewRequest
                {
                    RequestedViewId = new ViewId("b03-isometric-view"),
                    Name = "Isometric",
                    Orientation = "Isometric",
                    Position = new Coordinate2D(Length.FromMillimeters(220d), Length.FromMillimeters(110d)),
                    ScaleDenominator = 1,
                });
            Assert.True(isometricView.IsSuccess, FormatError(isometricView.Error));

            OperationResult<RebuildReceipt> drawingRebuild = await drawing.RebuildAsync();
            Assert.True(drawingRebuild.IsSuccess, FormatError(drawingRebuild.Error));
            Assert.False(drawingRebuild.Value!.HasErrors);

            OperationResult<CadInspectionSnapshot> drawingInspection = await session.Inspection.InspectAsync(drawing.DocumentId);
            Assert.True(drawingInspection.IsSuccess, FormatError(drawingInspection.Error));
            Assert.True(drawingInspection.Value!.Views.Length >= 2);
            Assert.Contains(drawingInspection.Value.Views, view => view.Orientation.Contains("Front", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(drawingInspection.Value.Views, view => view.Orientation.Contains("Isometric", StringComparison.OrdinalIgnoreCase));

            OperationResult<SaveReceipt> drawingSave = await drawing.SaveAsync();
            Assert.True(drawingSave.IsSuccess, FormatError(drawingSave.Error));
            Assert.True(File.Exists(drawingPath));

            OperationResult<CadInspectionSnapshot> reopenedDrawing = await drawing.ReopenAndInspectAsync();
            Assert.True(reopenedDrawing.IsSuccess, FormatError(reopenedDrawing.Error));
            Assert.True(reopenedDrawing.Value!.Views.Length >= 2);
            Assert.Equal(drawingSave.Value!.StateHash, reopenedDrawing.Value.Document.StateHash);

            OperationResult<MutationReceipt> closeDrawing = await drawing.CloseAsync();
            Assert.True(closeDrawing.IsSuccess, FormatError(closeDrawing.Error));

            // ReopenAndInspectAsync is the provider-level lifecycle proof.  It must close the exact registered path,
            // call the documented native open operation on the provider STA, and verify geometry/feature identity
            // after reopening.  ReopenAndInspectAsync 是 Provider 层的生命周期证明：必须关闭 registry 中的精确
            // path，在 Provider STA 上调用官方 open operation，并在 reopen 后复核 geometry/feature identity。
            OperationResult<CadInspectionSnapshot> reopened = await part.ReopenAndInspectAsync();
            Assert.True(reopened.IsSuccess, FormatError(reopened.Error));
            Assert.Single(reopened.Value!.Bodies);
            Assert.Contains(reopened.Value.Features, feature => feature.Name == extrusion.Value.Name);
            Assert.True(reopened.Value.Bodies[0].Volume.CubicMillimeters > 0d);
            // The pre-save inspection hash includes the dirty/save-flag marker; persisted reopen must match the
            // post-save receipt hash instead.  保存前 inspection hash 包含 dirty/save-flag；持久化 reopen 应与
            // SaveReceipt 的保存后 hash 比较，而不是与保存前 hash 比较。
            Assert.Equal(save.Value!.StateHash, reopened.Value.Document.StateHash);
            Assert.Equal(part.Configuration, reopened.Value.Document.Configuration);

            OperationResult<MutationReceipt> close = await part.CloseAsync();
            Assert.True(close.IsSuccess, FormatError(close.Error));
            Assert.Contains(
                close.Evidence!.Observations,
                observation => observation.Key == "document.closed" && observation.Value == bool.TrueString);
            completed = true;
            await session.CloseAsync();
        }
        finally
        {
            // A failed run intentionally preserves the isolated artifact.  A successful run attempts to clean only
            // its own file; SOLIDWORKS may still hold the saved document open after provider detach, in which case
            // the artifact remains quarantined in the explicit test workspace rather than turning a CAD pass into a
            // cleanup failure.  失败运行故意保留隔离 artifact；成功运行只尝试清理自己创建的文件。Provider
            // detach 后 SOLIDWORKS 可能仍保持文档打开，此时 artifact 留在显式 test workspace 中作为 quarantine，
            // 不把 CAD 通过误判成清理失败。
            bool keepArtifact = string.Equals(
                Environment.GetEnvironmentVariable("SOLIDWORKS_MCP_LIVE_KEEP_ARTIFACT"),
                "1",
                StringComparison.Ordinal);
            if (completed && !keepArtifact)
            {
                foreach (string artifactPath in new[] { partPath, drawingPath })
                {
                    if (!File.Exists(artifactPath))
                    {
                        continue;
                    }

                    try
                    {
                        File.Delete(artifactPath);
                    }
                    catch (IOException)
                    {
                        Console.Error.WriteLine($"live-artifact-cleanup=deferred; path={artifactPath}; reason=document-still-open");
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Console.Error.WriteLine($"live-artifact-cleanup=deferred; path={artifactPath}; reason=filesystem-lock");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Keeps provider error-code/details visible in Live evidence instead of reducing a failure to one sentence.
    /// 在 Live evidence 中保留 provider error-code/details，避免把失败压缩成单句消息。
    /// </summary>
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

/// <summary>Fact attribute that turns an absent explicit Live configuration into a discovery-time skip.</summary>
/// <remarks>
/// xUnit 2.9 does not treat a runtime <c>SkipException</c> as a skip in this repository's runner.  Setting the static
/// Fact metadata at discovery keeps hosted CI honest: the test is skipped, never counted as passed. xUnit 2.9 在此
/// runner 中不会把运行时 SkipException 稳定识别成 skip；发现阶段设置 Fact metadata 才能保证 Hosted CI 真实记录。
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class OptInLiveFactAttribute : FactAttribute
{
    /// <summary>Marks the test skipped unless both an exact PID and isolated workspace are present.</summary>
    public OptInLiveFactAttribute()
    {
        string? pid = Environment.GetEnvironmentVariable("SOLIDWORKS_MCP_LIVE_PROCESS_ID");
        string? workspace = Environment.GetEnvironmentVariable("SOLIDWORKS_MCP_LIVE_WORKSPACE");
        if (!int.TryParse(pid, NumberStyles.None, CultureInfo.InvariantCulture, out int processId)
            || processId <= 0
            || string.IsNullOrWhiteSpace(workspace))
        {
            Skip = "Set SOLIDWORKS_MCP_LIVE_PROCESS_ID and SOLIDWORKS_MCP_LIVE_WORKSPACE to run the real B03 loop.";
        }
    }
}
