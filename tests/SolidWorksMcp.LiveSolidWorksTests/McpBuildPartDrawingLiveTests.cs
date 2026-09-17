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
                    ["scaleDenominator"] = 1,
                });

            Assert.False(result.IsError, string.Join(Environment.NewLine, result.Content));
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

    private const string DProfileJson = "{\"segments\":["
        + "{\"kind\":\"line\",\"startXMillimeters\":-20,\"startYMillimeters\":-20,\"endXMillimeters\":20,\"endYMillimeters\":-20},"
        + "{\"kind\":\"line\",\"startXMillimeters\":20,\"startYMillimeters\":-20,\"endXMillimeters\":20,\"endYMillimeters\":0},"
        + "{\"kind\":\"arc\",\"startXMillimeters\":20,\"startYMillimeters\":0,\"throughXMillimeters\":0,\"throughYMillimeters\":22,\"endXMillimeters\":-20,\"endYMillimeters\":0},"
        + "{\"kind\":\"line\",\"startXMillimeters\":-20,\"startYMillimeters\":0,\"endXMillimeters\":-20,\"endYMillimeters\":-20}"
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
