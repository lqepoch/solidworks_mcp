using System.Globalization;
using System.IO.Pipelines;
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
            new ReferencePartCase("rounded-plate", RoundedPlateReferenceProfile(), 6d),
            new ReferencePartCase("formed-u-bracket", FormedUBracketReferenceProfile(), 12d),
        ];

        foreach (ReferencePartCase referenceCase in cases)
        {
            string caseId = $"{referenceCase.Id}-{runId}";
            string partPath = Path.Combine(workspace, $"MCP-Reference-{caseId}.sldprt");
            string drawingPath = Path.Combine(workspace, $"MCP-Reference-{caseId}.slddrw");
            string pdfPath = Path.Combine(workspace, $"MCP-Reference-{caseId}.pdf");
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
                CallToolResult result = await host.Client.CallToolAsync(
                    "cad.build-part-drawing",
                    new Dictionary<string, object?>
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
                    });

                Assert.False(
                    result.IsError,
                    string.Join(Environment.NewLine, result.Content)
                    + Environment.NewLine
                    + result.StructuredContent.ToString());
                Assert.Contains("workflow", result.StructuredContent.ToString(), StringComparison.Ordinal);
                Assert.Contains("drawing.view.count", result.StructuredContent.ToString(), StringComparison.Ordinal);
                Assert.Contains("export.format", result.StructuredContent.ToString(), StringComparison.Ordinal);
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
    private sealed record ReferencePartCase(string Id, string ProfileJson, double ExtrusionDepthMillimeters);

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
