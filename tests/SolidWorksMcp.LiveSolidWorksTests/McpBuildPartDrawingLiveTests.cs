using System.Globalization;
using System.IO.Pipelines;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Core;
using SolidWorksMcp.Protocol;
using SolidWorksMcp.Provider.SolidWorks;
using SolidWorksMcp.Server;

namespace SolidWorksMcp.LiveSolidWorksTests;

/// <summary>
/// Proves the actual Codex-facing MCP path can create both a native 3D part and a native 2D drawing artifact.
/// 证明真实面向 Codex 的 MCP path 可以同时创建 native 三维零件和 native 二维工程图 artifact。
/// </summary>
/// <remarks>
/// This test attaches to the one process prepared by <c>Invoke-SolidWorksLiveTests.ps1</c>. It never launches a second
/// SOLIDWORKS process and never reuses an arbitrary ActiveDoc. The process harness owns stale-process cleanup and
/// graceful shutdown. 本测试只连接 harness 准备的唯一进程，绝不另起 SOLIDWORKS，也不复用任意 ActiveDoc；旧进程
/// 清理与最终 graceful shutdown 均由 harness 负责。
/// </remarks>
[Collection(LiveSolidWorksTestGroup.Name)]
public sealed class McpBuildPartDrawingLiveTests
{
    [OptInLiveFact]
    public async Task McpBuildPartDrawingCreatesVerifiedNativeArtifacts()
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
        string partPath = Path.Combine(workspace, $"MCP-Build-Part-{suffix}.sldprt");
        string drawingPath = Path.Combine(workspace, $"MCP-Build-Part-{suffix}.slddrw");
        string pdfPath = Path.Combine(workspace, $"MCP-Build-Part-{suffix}.pdf");
        bool completed = false;

        try
        {
            // The provider is intentionally injected into the official MCP SDK server, not called directly by this test.
            // Provider 被注入官方 MCP SDK server；本测试不是绕过 MCP 直接调用 Provider。
            var provider = new SolidWorksCadProvider(new CadPathAllowlist([workspace]));
            await using var host = await InMemoryMcpHost.CreateAsync(provider, processId);
            CallToolResult result = await host.Client.CallToolAsync(
                "cad.build-part-drawing",
                new Dictionary<string, object?>
                {
                    ["schemaVersion"] = ProtocolSchema.CurrentVersion,
                    ["operationId"] = $"mcp-live-build-{suffix}",
                    ["documentId"] = $"mcp-live-part-{suffix}",
                    ["drawingDocumentId"] = $"mcp-live-drawing-{suffix}",
                    ["configuration"] = "Default",
                    ["partPath"] = partPath,
                    ["drawingPath"] = drawingPath,
                    ["pdfPath"] = pdfPath,
                    ["extrusionDepthMillimeters"] = 10d,
                    ["initialSketchProfileJson"] = DProfileJson,
                    ["throughHolePatternJson"] = ThroughHolePatternJson,
                    ["scaleDenominator"] = 1,
                });

            Assert.False(
                result.IsError,
                string.Join(Environment.NewLine, result.Content)
                + Environment.NewLine
                + result.StructuredContent.ToString());
            Assert.Contains("part.hole-pattern", result.StructuredContent.ToString(), StringComparison.Ordinal);
            Assert.Contains("drawing.section-view", result.StructuredContent.ToString(), StringComparison.Ordinal);
            Assert.Contains("Section A-A", result.StructuredContent.ToString(), StringComparison.Ordinal);
            Assert.Contains("pattern-callout", result.StructuredContent.ToString(), StringComparison.Ordinal);
            Assert.Contains("drawing.pattern-callout.reopened", result.StructuredContent.ToString(), StringComparison.Ordinal);
            Assert.Contains("2X", result.StructuredContent.ToString(), StringComparison.Ordinal);
            Assert.Contains("THRU", result.StructuredContent.ToString(), StringComparison.Ordinal);
            Assert.Contains("PITCH 20", result.StructuredContent.ToString(), StringComparison.Ordinal);
            Assert.Contains("SYMMETRIC", result.StructuredContent.ToString(), StringComparison.Ordinal);
            Assert.True(File.Exists(partPath), "The MCP workflow did not persist the native .sldprt artifact.");
            Assert.True(File.Exists(drawingPath), "The MCP workflow did not persist the native .slddrw artifact.");
            Assert.True(File.Exists(pdfPath), "The MCP workflow did not export the native PDF artifact.");
            Assert.True(new FileInfo(partPath).Length > 0);
            Assert.True(new FileInfo(drawingPath).Length > 0);
            Assert.True(new FileInfo(pdfPath).Length > 0);
            completed = true;
        }
        finally
        {
            bool keepArtifact = string.Equals(
                Environment.GetEnvironmentVariable("SOLIDWORKS_MCP_LIVE_KEEP_ARTIFACT"),
                "1",
                StringComparison.Ordinal);
            if (completed && !keepArtifact)
            {
                foreach (string artifactPath in new[] { partPath, drawingPath, pdfPath })
                {
                    TryDeleteArtifact(artifactPath);
                }
            }
        }
    }

    /// <summary>
    /// Builds two redacted single-part reference classes through the public MCP operation and verifies both the
    /// persisted 3D model and the persisted 2D drawing artifact. 通过公开 MCP operation 构建两个脱敏单零件参考类别，
    /// 并验证持久化三维模型与持久化二维工程图 artifact。
    /// </summary>
    /// <remarks>
    /// The cases intentionally describe semantic shape classes observed during private local review, not source-PDF
    /// dimensions, names or title-block values. The first case is a rounded plate; the second is a formed U-bracket
    /// whose wall is represented by an outer and inner arc loop. This is a real non-cylindrical geometry proof, while
    /// the source-specific numbers remain outside Git and outside the product runtime. 这里故意只描述私密本地复核
    /// 得到的语义形状类别，不写入源 PDF 尺寸、名称或标题栏内容。第一个 case 是圆角板，第二个 case 是由内外
    /// 圆弧闭环表达壁厚的成形 U 形支架。这是真实非圆柱几何证明，而源图纸专属数值留在 Git 和产品 runtime 之外。
    /// </remarks>
    [OptInLiveFact]
    public async Task McpReferenceDrivenSinglePartClassesCreateVerifiedThreeDAndTwoDArtifacts()
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
        string runId = Guid.NewGuid().ToString("N");
        ReferencePartCase[] cases =
        [
            new ReferencePartCase(
                "rounded-plate",
                RoundedPlateReferenceProfile(),
                6d,
                ThroughHolePatternJson,
                SlotCutJson: null,
                SurfaceFinishJson: SurfaceFinishJson(),
                IncludeDetailView: true),
            new ReferencePartCase(
                "formed-u-bracket",
                FormedUBracketReferenceProfile(),
                12d,
                ThroughHolePatternJson: null,
                SlotCutJson: SlotCutJson,
                SurfaceFinishJson: null,
                IncludeDetailView: false),
        ];

        foreach (ReferencePartCase referenceCase in cases)
        {
            string caseId = $"{referenceCase.Id}-{runId}";
            string partPath = Path.Combine(workspace, $"MCP-Reference-{caseId}.sldprt");
            string drawingPath = Path.Combine(workspace, $"MCP-Reference-{caseId}.slddrw");
            string pdfPath = Path.Combine(workspace, $"MCP-Reference-{caseId}.pdf");
            string evidencePath = Path.Combine(workspace, $"MCP-Reference-{caseId}.result.json");
            bool completed = false;

            try
            {
                // Each case is a separate provider/MCP host lifetime, but both cases share the one process leased by
                // Invoke-SolidWorksLiveTests.ps1. No case starts SLDWORKS.exe or attaches through ActiveDoc.
                // 每个 case 都使用独立 Provider/MCP host 生命周期，但两者共享 harness 租用的唯一进程；任何 case
                // 都不会启动 SLDWORKS.exe，也不会通过 ActiveDoc 猜测文档。
                var provider = new SolidWorksMcp.Provider.SolidWorks.SolidWorksCadProvider(
                    new CadPathAllowlist([workspace]));
                await using var host = await InMemoryMcpHost.CreateAsync(provider, processId);
                var arguments = new Dictionary<string, object?>
                {
                    ["schemaVersion"] = ProtocolSchema.CurrentVersion,
                    ["operationId"] = $"mcp-live-reference-{caseId}",
                    ["documentId"] = $"mcp-reference-part-{caseId}",
                    ["drawingDocumentId"] = $"mcp-reference-drawing-{caseId}",
                    ["configuration"] = "Default",
                    ["partPath"] = partPath,
                    ["drawingPath"] = drawingPath,
                    ["pdfPath"] = pdfPath,
                    ["extrusionDepthMillimeters"] = referenceCase.ExtrusionDepthMillimeters,
                    ["initialSketchProfileJson"] = referenceCase.ProfileJson,
                    ["scaleDenominator"] = 1,
                };
                if (referenceCase.IncludeDetailView)
                {
                    // The parent identity is generated by the compiler from the caller's stable drawing identity;
                    // building it here proves the MCP contract carries a declarative binding, not an active-view guess.
                    // parent identity 由 compiler 基于 caller 的 stable drawing identity 生成；这里显式传入可证明 MCP
                    // contract 携带的是 declarative binding，而不是猜测当前 active view。
                    arguments["detailViewJson"] = DetailViewJson($"mcp-reference-drawing-{caseId}");
                }
                if (referenceCase.ThroughHolePatternJson is not null)
                {
                    arguments["throughHolePatternJson"] = referenceCase.ThroughHolePatternJson;
                }
                if (referenceCase.SlotCutJson is not null)
                {
                    arguments["slotCutJson"] = referenceCase.SlotCutJson;
                }
                if (referenceCase.SurfaceFinishJson is not null)
                {
                    arguments["surfaceFinishJson"] = referenceCase.SurfaceFinishJson.Replace(
                        "{0}",
                        $"mcp-reference-drawing-{caseId}",
                        StringComparison.Ordinal);
                }

                CallToolResult result = await host.Client.CallToolAsync("cad.build-part-drawing", arguments);

                Assert.False(
                    result.IsError,
                    string.Join(Environment.NewLine, result.Content)
                    + Environment.NewLine
                    + result.StructuredContent.ToString());
                Assert.Contains("workflow", result.StructuredContent.ToString(), StringComparison.Ordinal);
                Assert.Contains("drawing.view.count", result.StructuredContent.ToString(), StringComparison.Ordinal);
                Assert.Contains("export.format", result.StructuredContent.ToString(), StringComparison.Ordinal);
                Assert.Contains("drawing.manufacturing.native-import.count", result.StructuredContent.ToString(), StringComparison.Ordinal);
                Assert.Contains("annotation-native-materialized", result.StructuredContent.ToString(), StringComparison.Ordinal);
                AssertNativeGeometryAndDrawingEvidence(result, referenceCase);
                if (string.Equals(
                    Environment.GetEnvironmentVariable("SOLIDWORKS_MCP_LIVE_KEEP_ARTIFACT"),
                    "1",
                    StringComparison.Ordinal))
                {
                    // Keep a local-only structured result beside retained CAD/PDF artifacts so native COM evidence can
                    // be inspected without ever copying private source drawings into Git. 保留 artifact 时把结构化
                    // result 写在本机 workspace，便于检查 native COM evidence；绝不把私密源图纸复制进 Git。
                    File.WriteAllText(evidencePath, result.StructuredContent?.ToString() ?? "{}", System.Text.Encoding.UTF8);
                }
                Assert.True(File.Exists(partPath), $"The {referenceCase.Id} part was not persisted.");
                Assert.True(File.Exists(drawingPath), $"The {referenceCase.Id} drawing was not persisted.");
                Assert.True(File.Exists(pdfPath), $"The {referenceCase.Id} PDF was not exported.");
                Assert.True(new FileInfo(partPath).Length > 0);
                Assert.True(new FileInfo(drawingPath).Length > 0);
                Assert.True(new FileInfo(pdfPath).Length > 0);
                completed = true;
            }
            finally
            {
                bool keepArtifact = string.Equals(
                    Environment.GetEnvironmentVariable("SOLIDWORKS_MCP_LIVE_KEEP_ARTIFACT"),
                    "1",
                    StringComparison.Ordinal);
                if (completed && !keepArtifact)
                {
                    TryDeleteArtifact(partPath);
                    TryDeleteArtifact(drawingPath);
                    TryDeleteArtifact(pdfPath);
                }
            }
        }
    }

    /// <summary>Names one redacted semantic reference case and carries only its provider-neutral profile JSON.</summary>
    /// <summary>表示一个脱敏语义参考 case；这里只携带厂商无关的 profile JSON。</summary>
    private sealed record ReferencePartCase(
        string Id,
        string ProfileJson,
        double ExtrusionDepthMillimeters,
        string? ThroughHolePatternJson,
        string? SlotCutJson,
        string? SurfaceFinishJson,
        bool IncludeDetailView);

    /// <summary>
    /// Verifies engineering data returned by the MCP result, not only that files exist.
    /// 验证 MCP result 返回的工程数据，而不是只验证文件存在。
    /// </summary>
    /// <remarks>
    /// The assertions intentionally use broad invariants shared by both redacted classes: one solid body, at least one
    /// native feature/topology entity, positive measured volume, a non-degenerate bounding box, and multiple persisted
    /// drawing views/annotations. This keeps the test independent of private source dimensions while proving that the
    /// profile became real geometry and that the drawing was inspected after reopen.
    /// 这些断言只使用两个脱敏类别共有的宽不变量：一个实体 body、至少一个 native feature/topology entity、正体积、
    /// 非退化包围盒，以及多个重开后读回的工程图视图/标注；不依赖私密源尺寸，但能证明 profile 已成为真实几何。
    /// </remarks>
    private static void AssertNativeGeometryAndDrawingEvidence(
        CallToolResult result,
        ReferencePartCase referenceCase)
    {
        Assert.NotNull(result.StructuredContent);
        JsonElement root = result.StructuredContent!.Value;
        JsonElement value = root.GetProperty("value");
        string structuredText = result.StructuredContent.Value.ToString();
        Assert.Contains("GB.rulepack", structuredText, StringComparison.Ordinal);
        Assert.Contains("FirstAngle", structuredText, StringComparison.Ordinal);
        JsonElement part = value.GetProperty("part");
        JsonElement bodies = part.GetProperty("bodies");
        JsonElement features = part.GetProperty("features");
        JsonElement topology = part.GetProperty("topologyEntities");

        Assert.Equal(1, bodies.GetArrayLength());
        Assert.True(features.GetArrayLength() >= 1, $"{referenceCase.Id} has no verified native feature.");
        Assert.True(topology.GetArrayLength() >= 1, $"{referenceCase.Id} has no verified topology entity.");

        JsonElement body = bodies[0];
        Assert.True(body.GetProperty("featureCount").GetInt32() >= 1);
        Assert.True(body.GetProperty("volume").GetProperty("cubicMillimeters").GetDouble() > 0d);
        double minX = ReadLength(body.GetProperty("boundingBoxMinimum").GetProperty("x"));
        double maxX = ReadLength(body.GetProperty("boundingBoxMaximum").GetProperty("x"));
        double minY = ReadLength(body.GetProperty("boundingBoxMinimum").GetProperty("y"));
        double maxY = ReadLength(body.GetProperty("boundingBoxMaximum").GetProperty("y"));
        double minZ = ReadLength(body.GetProperty("boundingBoxMinimum").GetProperty("z"));
        double maxZ = ReadLength(body.GetProperty("boundingBoxMaximum").GetProperty("z"));
        Assert.True(maxX > minX, $"{referenceCase.Id} has a degenerate X bounding box.");
        Assert.True(maxY > minY, $"{referenceCase.Id} has a degenerate Y bounding box.");
        Assert.True(maxZ > minZ, $"{referenceCase.Id} has a degenerate Z bounding box.");

        double extrusionDepth = ReadLength(value.GetProperty("extrusion").GetProperty("depth"));
        Assert.Equal(referenceCase.ExtrusionDepthMillimeters, extrusionDepth, precision: 6);

        JsonElement drawing = value.GetProperty("drawing");
        int minimumViewCount = referenceCase.IncludeDetailView ? 4 : 3;
        Assert.True(
            drawing.GetProperty("views").GetArrayLength() >= minimumViewCount,
            $"{referenceCase.Id} has insufficient drawing views.");
        Assert.True(drawing.GetProperty("annotations").GetArrayLength() >= 1, $"{referenceCase.Id} has no drawing annotation.");
        JsonElement detailView = value.GetProperty("detailView");
        if (referenceCase.IncludeDetailView)
        {
            Assert.NotEqual(JsonValueKind.Null, detailView.ValueKind);
            Assert.Equal("Detail A-A", detailView.GetProperty("orientation").GetString());
            Assert.Equal(1, detailView.GetProperty("scaleDenominator").GetInt32());
            double detailX = ReadLength(detailView.GetProperty("position").GetProperty("x"));
            double detailY = ReadLength(detailView.GetProperty("position").GetProperty("y"));
            Assert.InRange(detailX, 0.1d, 279.3d);
            Assert.InRange(detailY, 0.1d, 215.8d);
            Assert.Contains("explicit-approved-region", structuredText, StringComparison.Ordinal);
            Assert.Contains("drawing.detail.view.detail-local-circle-center.meters", structuredText, StringComparison.Ordinal);
            Assert.Contains("drawing.detail.view.detail-source-circle-params.meters", structuredText, StringComparison.Ordinal);
            Assert.Contains("drawing.detail.view.detail-polyline-count", structuredText, StringComparison.Ordinal);
            Assert.Contains("drawing.detail.view.detail-parent-native-name", structuredText, StringComparison.Ordinal);
            JsonElement observations = root.GetProperty("evidence").GetProperty("observations");
            string localCircleCenter = ObservationValue(observations, "drawing.detail.view.detail-local-circle-center.meters");
            string sourceCircle = ObservationValue(observations, "drawing.detail.view.detail-source-circle-params.meters");
            int projectedPolylineCount = int.Parse(
                ObservationValue(observations, "drawing.detail.view.detail-polyline-count"),
                CultureInfo.InvariantCulture);
            Assert.StartsWith("0,0.01", localCircleCenter, StringComparison.Ordinal);
            Assert.StartsWith("0,0.01,0,0,0,1,0.012", sourceCircle, StringComparison.Ordinal);
            Assert.True(projectedPolylineCount >= 2, "The native detail view must contain the source region and model projection, not only its boundary.");
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, detailView.ValueKind);
        }

        JsonElement slotCut = value.GetProperty("slotCut");
        if (referenceCase.SlotCutJson is not null)
        {
            Assert.NotEqual(JsonValueKind.Null, slotCut.ValueKind);
            Assert.Contains("slot-cut", structuredText, StringComparison.Ordinal);
            JsonElement slotCallout = value.GetProperty("slotCallout");
            Assert.NotEqual(JsonValueKind.Null, slotCallout.ValueKind);
            Assert.Equal("slot-callout", slotCallout.GetProperty("kind").GetString());
            Assert.Equal("SLOT W4; C-C 12", slotCallout.GetProperty("text").GetString());
            Assert.Contains("drawing.slot-callout.reopened", structuredText, StringComparison.Ordinal);
            Assert.Contains("part.slot-cut.native.slot.native-length-millimeters", structuredText, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, slotCut.ValueKind);
            Assert.Equal(JsonValueKind.Null, value.GetProperty("slotCallout").ValueKind);
        }

        JsonElement surfaceFinish = value.GetProperty("surfaceFinish");
        if (referenceCase.SurfaceFinishJson is not null)
        {
            Assert.NotEqual(JsonValueKind.Null, surfaceFinish.ValueKind);
            Assert.Equal("surface-finish", surfaceFinish.GetProperty("kind").GetString());
            Assert.Equal("0.8", surfaceFinish.GetProperty("text").GetString());
            Assert.Contains("drawing.surface-finish.reopened", structuredText, StringComparison.Ordinal);
            Assert.Contains("drawing.surface-finish.native.surface-finish.association", structuredText, StringComparison.Ordinal);
            Assert.Contains("human_approved", structuredText, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, surfaceFinish.ValueKind);
        }

        JsonElement[] drawingViews = [.. drawing.GetProperty("views").EnumerateArray()];
        bool hasFirstAngleProjectedPlacement = drawingViews.Any(view =>
        {
            double x = ReadLength(view.GetProperty("position").GetProperty("x"));
            double y = ReadLength(view.GetProperty("position").GetProperty("y"));
            return Math.Abs(x - 90d) < 1d && Math.Abs(y - 50d) < 1d;
        });
        string positions = string.Join(
            "; ",
            drawingViews.Select(view =>
                $"{ReadLength(view.GetProperty("position").GetProperty("x")):G17},{ReadLength(view.GetProperty("position").GetProperty("y")):G17}"));
        Assert.True(
            hasFirstAngleProjectedPlacement,
            $"{referenceCase.Id} did not apply the GB first-angle projected-view placement; actual view positions={positions} mm.");
    }

    /// <summary>Reads a serialized canonical millimetre value from the MCP contract. / 从 MCP contract 读取序列化后的毫米值。</summary>
    private static double ReadLength(JsonElement length) => length.GetProperty("millimeters").GetDouble();

    /// <summary>Reads one evidence observation by its stable key. / 按稳定 key 读取一条 evidence observation。</summary>
    private static string ObservationValue(JsonElement observations, string key) =>
        observations.EnumerateArray()
            .Single(observation => string.Equals(observation.GetProperty("key").GetString(), key, StringComparison.Ordinal))
            .GetProperty("value")
            .GetString()
        ?? string.Empty;

    /// <summary>Returns a generic rounded-plate profile; it contains no private source dimensions.</summary>
    /// <summary>返回通用圆角板 profile；不包含私密源图纸尺寸。</summary>
    private static string RoundedPlateReferenceProfile() => "{\"segments\":["
        + "{\"kind\":\"line\",\"startXMillimeters\":-32,\"startYMillimeters\":-24,\"endXMillimeters\":32,\"endYMillimeters\":-24},"
        + "{\"kind\":\"arc\",\"startXMillimeters\":32,\"startYMillimeters\":-24,\"throughXMillimeters\":37,\"throughYMillimeters\":-22,\"endXMillimeters\":39,\"endYMillimeters\":-17},"
        + "{\"kind\":\"line\",\"startXMillimeters\":39,\"startYMillimeters\":-17,\"endXMillimeters\":39,\"endYMillimeters\":17},"
        + "{\"kind\":\"arc\",\"startXMillimeters\":39,\"startYMillimeters\":17,\"throughXMillimeters\":37,\"throughYMillimeters\":22,\"endXMillimeters\":32,\"endYMillimeters\":24},"
        + "{\"kind\":\"line\",\"startXMillimeters\":32,\"startYMillimeters\":24,\"endXMillimeters\":-32,\"endYMillimeters\":24},"
        + "{\"kind\":\"arc\",\"startXMillimeters\":-32,\"startYMillimeters\":24,\"throughXMillimeters\":-37,\"throughYMillimeters\":22,\"endXMillimeters\":-39,\"endYMillimeters\":17},"
        + "{\"kind\":\"line\",\"startXMillimeters\":-39,\"startYMillimeters\":17,\"endXMillimeters\":-39,\"endYMillimeters\":-17},"
        + "{\"kind\":\"arc\",\"startXMillimeters\":-39,\"startYMillimeters\":-17,\"throughXMillimeters\":-37,\"throughYMillimeters\":-22,\"endXMillimeters\":-32,\"endYMillimeters\":-24}"
        + "]}";

    /// <summary>Returns a generic closed U-bracket wall profile with outer and inner crown arcs.</summary>
    /// <summary>返回由外圆弧和内圆弧构成的通用闭合 U 形支架壁厚 profile。</summary>
    private static string FormedUBracketReferenceProfile() => "{\"segments\":["
        + "{\"kind\":\"line\",\"startXMillimeters\":-30,\"startYMillimeters\":-26,\"endXMillimeters\":-30,\"endYMillimeters\":0},"
        + "{\"kind\":\"arc\",\"startXMillimeters\":-30,\"startYMillimeters\":0,\"throughXMillimeters\":0,\"throughYMillimeters\":30,\"endXMillimeters\":30,\"endYMillimeters\":0},"
        + "{\"kind\":\"line\",\"startXMillimeters\":30,\"startYMillimeters\":0,\"endXMillimeters\":30,\"endYMillimeters\":-26},"
        + "{\"kind\":\"line\",\"startXMillimeters\":30,\"startYMillimeters\":-26,\"endXMillimeters\":20,\"endYMillimeters\":-26},"
        + "{\"kind\":\"line\",\"startXMillimeters\":20,\"startYMillimeters\":-26,\"endXMillimeters\":20,\"endYMillimeters\":0},"
        + "{\"kind\":\"arc\",\"startXMillimeters\":20,\"startYMillimeters\":0,\"throughXMillimeters\":0,\"throughYMillimeters\":20,\"endXMillimeters\":-20,\"endYMillimeters\":0},"
        + "{\"kind\":\"line\",\"startXMillimeters\":-20,\"startYMillimeters\":0,\"endXMillimeters\":-20,\"endYMillimeters\":-26},"
        + "{\"kind\":\"line\",\"startXMillimeters\":-20,\"startYMillimeters\":-26,\"endXMillimeters\":-30,\"endYMillimeters\":-26}"
        + "]}";

    /// <summary>Returns a bounded explicit detail request for the rounded-plate parent Front view.</summary>
    /// <summary>返回圆角板 Front 父视图的 bounded 显式 detail 请求。</summary>
    private static string DetailViewJson(string drawingDocumentId) => "{"
        + $"\"parentViewId\":\"{drawingDocumentId}:front\","
        + "\"name\":\"Detail A\",\"label\":\"A\","
        + "\"detailCenterXMillimeters\":90,\"detailCenterYMillimeters\":135,"
        + "\"detailRadiusMillimeters\":12,\"positionXMillimeters\":220,\"positionYMillimeters\":170,"
        + "\"scaleNumerator\":2,\"scaleDenominator\":1,\"fullOutline\":true,\"jaggedOutline\":false"
        + "}";

    /// <summary>Returns an approval-gated generic roughness symbol request for the rounded-plate case.</summary>
    /// <summary>返回圆角板 case 使用的、经过审批门禁的通用粗糙度符号请求。</summary>
    private static string SurfaceFinishJson() => "{"
        + "\"annotationId\":\"{0}:surface-finish:plate\","
        + "\"viewId\":\"{0}:front\","
        + "\"positionXMillimeters\":145,\"positionYMillimeters\":35,"
        + "\"symbolType\":\"MachiningRequired\","
        + "\"layDirection\":\"None\",\"leaderStyle\":\"Straight\",\"arrowStyle\":\"Open\","
        + "\"maximumRoughness\":\"0.8\",\"provenanceKind\":\"human_approved\","
        + "\"provenanceMethod\":\"live-reference-fixture\",\"approvalState\":\"Approved\","
        + "\"coverageKeys\":[\"surface-finish.primary-faces\"]"
        + "}";

    private const string DProfileJson = "{\"segments\":["
        + "{\"kind\":\"line\",\"startXMillimeters\":-20,\"startYMillimeters\":-20,\"endXMillimeters\":20,\"endYMillimeters\":-20},"
        + "{\"kind\":\"line\",\"startXMillimeters\":20,\"startYMillimeters\":-20,\"endXMillimeters\":20,\"endYMillimeters\":0},"
        + "{\"kind\":\"arc\",\"startXMillimeters\":20,\"startYMillimeters\":0,\"throughXMillimeters\":0,\"throughYMillimeters\":22,\"endXMillimeters\":-20,\"endYMillimeters\":0},"
        + "{\"kind\":\"line\",\"startXMillimeters\":-20,\"startYMillimeters\":0,\"endXMillimeters\":-20,\"endYMillimeters\":-20}"
        + "]}";

    private const string ThroughHolePatternJson = "{\"name\":\"MCP-Mounting-Hole-Group\",\"diameterMillimeters\":6,\"centers\":["
        + "{\"xMillimeters\":0,\"yMillimeters\":-10},"
        + "{\"xMillimeters\":0,\"yMillimeters\":10}"
        + "]}";

    /// <summary>Returns a generic slot on the left leg of the bracket; no private drawing value is encoded. 返回支架左腿上的通用长圆槽；不编码私有图纸值。</summary>
    private const string SlotCutJson = "{\"name\":\"MCP-Access-Slot\",\"widthMillimeters\":4,"
        + "\"start\":{\"xMillimeters\":-25,\"yMillimeters\":-20},"
        + "\"end\":{\"xMillimeters\":-25,\"yMillimeters\":-8},"
        + "\"supportFaceProbe\":{\"xMillimeters\":-26,\"yMillimeters\":-14}}";

    private static void TryDeleteArtifact(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            Console.Error.WriteLine($"live-artifact-cleanup=deferred; path={path}; reason=document-still-open");
        }
        catch (UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"live-artifact-cleanup=deferred; path={path}; reason=filesystem-lock");
        }
    }

    /// <summary>Owns the official MCP SDK in-memory transport while the production executable remains stdio.</summary>
    private sealed class InMemoryMcpHost : IAsyncDisposable
    {
        private readonly Pipe clientToServer;
        private readonly Pipe serverToClient;
        private readonly CancellationTokenSource cancellation;
        private readonly ServiceProvider services;
        private readonly Task serverTask;

        private InMemoryMcpHost(
            Pipe clientToServer,
            Pipe serverToClient,
            CancellationTokenSource cancellation,
            ServiceProvider services,
            Task serverTask,
            McpClient client)
        {
            this.clientToServer = clientToServer;
            this.serverToClient = serverToClient;
            this.cancellation = cancellation;
            this.services = services;
            this.serverTask = serverTask;
            Client = client;
        }

        public McpClient Client { get; }

        public static async Task<InMemoryMcpHost> CreateAsync(SolidWorksCadProvider provider, int processId)
        {
            var clientToServer = new Pipe();
            var serverToClient = new Pipe();
            var cancellation = new CancellationTokenSource();
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddLogging();
            serviceCollection.AddSolidWorksMcp(
                provider,
                new SolidWorksMcpConfiguration(providerMode: ProviderModes.Native),
                new CadSessionOptions { RequestedProcessId = processId });
            serviceCollection
                .AddMcpServer(options =>
                {
                    options.ServerInfo = new Implementation
                    {
                        Name = "solidworks-mcp-live-contract",
                        Version = ProtocolSchema.CurrentVersion,
                    };
                    options.Capabilities = new ServerCapabilities { Tools = new ToolsCapability() };
                })
                .WithStreamServerTransport(
                    clientToServer.Reader.AsStream(),
                    serverToClient.Writer.AsStream())
                .WithTools<CadMcpTools>();

            ServiceProvider services = serviceCollection.BuildServiceProvider(validateScopes: true);
            McpServer server = services.GetRequiredService<McpServer>();
            Task serverTask = server.RunAsync(cancellation.Token);
            McpClient client = await McpClient.CreateAsync(
                new StreamClientTransport(
                    clientToServer.Writer.AsStream(),
                    serverToClient.Reader.AsStream()),
                cancellationToken: cancellation.Token).ConfigureAwait(false);

            return new InMemoryMcpHost(clientToServer, serverToClient, cancellation, services, serverTask, client);
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync().ConfigureAwait(false);
            await cancellation.CancelAsync().ConfigureAwait(false);
            clientToServer.Writer.Complete();
            serverToClient.Writer.Complete();
            try
            {
                await serverTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is the bounded, expected in-memory server shutdown path.
                // 取消是内存 server 有界关闭的预期路径。
            }

            await services.DisposeAsync().ConfigureAwait(false);
            cancellation.Dispose();
        }
    }
}
