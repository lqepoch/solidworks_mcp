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
/// Proves the public MCP drawing.repair path against a real SOLIDWORKS drawing.
/// 使用真实 SOLIDWORKS 工程图验证公开 MCP drawing.repair 路径。
/// </summary>
/// <remarks>
/// The fixture creates the source model through the provider boundary only to prepare a registered target, then sends
/// the actual repair through the official MCP SDK. The external harness owns one fresh SOLIDWORKS process and closes
/// stale processes before the test starts. fixture 只通过 Provider 准备已注册 target，真正修复必须经过官方 MCP SDK；
/// 外部 harness 负责启动唯一 fresh SOLIDWORKS，并在测试前清理旧进程。
/// </remarks>
[Collection(LiveSolidWorksTestGroup.Name)]
public sealed class McpDrawingRepairLiveTests
{
    /// <summary>Moves exactly one native note through MCP and proves its persisted position after reopen.</summary>
    [OptInLiveFact]
    public async Task PublicMcpDrawingRepairMovesExactAnnotationAndPersists()
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
        string partPath = Path.Combine(workspace, $"MCP-D08-Repair-Part-{suffix}.sldprt");
        string drawingPath = Path.Combine(workspace, $"MCP-D08-Repair-Drawing-{suffix}.slddrw");
        ICadPartDocument? part = null;
        ICadDrawingDocument? drawing = null;
        bool completed = false;

        await using var provider = new SolidWorksCadProvider(new CadPathAllowlist([workspace]));
        try
        {
            OperationResult<ICadSession> sessionResult = await provider.StartSessionAsync(
                new CadSessionOptions { RequestedProcessId = processId });
            Assert.True(sessionResult.IsSuccess, FormatError(sessionResult.Error));
            ICadSession session = sessionResult.Value!;

            // Prepare one persisted model and drawing under the same provider session that MCP will later reuse.
            // 先在同一个 Provider session 中准备持久化零件和工程图，随后 MCP 重用该 session。
            OperationResult<ICadPartDocument> partResult = await session.CreatePartAsync(
                new CreatePartRequest
                {
                    RequestedDocumentId = new DocumentId($"mcp-d08-part-{suffix}"),
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
                    Name = "MCP-D08-Repair-Extrusion",
                    Depth = Length.FromMillimeters(10d),
                });
            Assert.True(extrusion.IsSuccess, FormatError(extrusion.Error));
            OperationResult<SaveReceipt> partSave = await part.SaveAsync();
            Assert.True(partSave.IsSuccess, FormatError(partSave.Error));

            OperationResult<ICadDrawingDocument> drawingResult = await session.CreateDrawingAsync(
                new CreateDrawingRequest
                {
                    RequestedDocumentId = new DocumentId($"mcp-d08-drawing-{suffix}"),
                    Path = drawingPath,
                    SourceDocumentId = part.DocumentId,
                    Configuration = part.Configuration,
                });
            Assert.True(drawingResult.IsSuccess, FormatError(drawingResult.Error));
            drawing = drawingResult.Value!;

            OperationResult<DrawingViewSnapshot> view = await drawing.AddViewAsync(
                new DrawingViewRequest
                {
                    RequestedViewId = new ViewId($"mcp-d08-view-{suffix}"),
                    Name = "Front",
                    Orientation = "Front",
                    Position = new Coordinate2D(Length.FromMillimeters(100d), Length.FromMillimeters(100d)),
                    ScaleDenominator = 1,
                });
            Assert.True(view.IsSuccess, FormatError(view.Error));

            OperationResult<DrawingAnnotationSnapshot> note = await drawing.AddAnnotationAsync(
                new DrawingAnnotationRequest
                {
                    RequestedAnnotationId = new AnnotationId($"mcp-d08-annotation-{suffix}"),
                    ViewId = view.Value!.ViewId,
                    Kind = "note",
                    Text = "MCP D08 repair fixture",
                    Position = new Coordinate2D(Length.FromMillimeters(100d), Length.FromMillimeters(70d)),
                });
            Assert.True(note.IsSuccess, FormatError(note.Error));

            string expectedStateHash = drawing.StateHash;
            Coordinate2D newPosition = new(Length.FromMillimeters(125d), Length.FromMillimeters(80d));

            // The host uses the same provider instance/session; no second SOLIDWORKS process is created here.
            // MCP host 复用同一个 Provider/session；此处绝不创建第二个 SOLIDWORKS 进程。
            await using InMemoryMcpHost host = await InMemoryMcpHost.CreateAsync(provider, processId);
            {
                CallToolResult result = await host.Client.CallToolAsync(
                    "drawing.repair",
                    new Dictionary<string, object?>
                    {
                        ["schemaVersion"] = ProtocolSchema.CurrentVersion,
                        ["documentId"] = drawing.DocumentId.Value,
                        ["expectedStateHash"] = expectedStateHash,
                        ["repairPlanJson"] = "{\"schemaVersion\":\"1.0\",\"fingerprint\":\"mcp-d08-plan-001\",\"actions\":["
                            + "{\"actionCode\":\"layout.apply-planned-position\",\"targetId\":\"" + note.Value!.AnnotationId.Value + "\","
                            + "\"findingCode\":\"annotation-repositioned\",\"preconditionFingerprint\":\"mcp-d08-precondition-001\","
                            + "\"newPositionXMillimeters\":125,\"newPositionYMillimeters\":80}]}",
                        ["operationId"] = $"mcp-d08-repair-{suffix}",
                    });

                Assert.False(
                    result.IsError,
                    string.Join(Environment.NewLine, result.Content)
                    + Environment.NewLine
                    + result.StructuredContent.ToString());
                Assert.Contains("save-reopen-verified", result.StructuredContent.ToString(), StringComparison.Ordinal);
                Assert.Contains(note.Value.AnnotationId.Value, result.StructuredContent.ToString(), StringComparison.Ordinal);

                // Inspect while the MCP host still owns the shared session; its deterministic dispose then performs the
                // normal provider shutdown. MCP host 仍持有 shared session 时完成最终 inspection，随后按正常 provider
                // lifecycle dispose，避免在 session 已关闭后重复使用旧 facade。
                OperationResult<CadInspectionSnapshot> reopened = await session.Inspection.InspectAsync(drawing.DocumentId);
                Assert.True(reopened.IsSuccess, FormatError(reopened.Error));
                DrawingAnnotationSnapshot persisted = Assert.Single(reopened.Value!.Annotations);
                Assert.Equal(note.Value.AnnotationId, persisted.AnnotationId);
                Assert.Equal(newPosition, persisted.Position);
                Assert.True(File.Exists(partPath));
                Assert.True(File.Exists(drawingPath));
                completed = true;
            }
        }
        finally
        {
            // Close only identities created by this fixture. The harness still owns final process shutdown and verifies
            // SLDWORKS_COUNT=0 after the test process exits. 这里只关闭 fixture identity；harness 负责最终进程退出，
            // 并在测试后验证 SLDWORKS_COUNT=0。
            if (drawing is not null)
            {
                await drawing.CloseAsync(CancellationToken.None);
            }

            if (part is not null)
            {
                await part.CloseAsync(CancellationToken.None);
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

    /// <summary>Owns one official MCP SDK in-memory transport connected to the requested native process.</summary>
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
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSolidWorksMcp(
                provider,
                new SolidWorksMcpConfiguration(providerMode: ProviderModes.Native),
                new CadSessionOptions { RequestedProcessId = processId });
            services
                .AddMcpServer(options =>
                {
                    options.ServerInfo = new Implementation
                    {
                        Name = "solidworks-mcp-live-repair-contract",
                        Version = ProtocolSchema.CurrentVersion,
                    };
                    options.Capabilities = new ServerCapabilities { Tools = new ToolsCapability() };
                })
                .WithStreamServerTransport(
                    clientToServer.Reader.AsStream(),
                    serverToClient.Writer.AsStream())
                .WithTools<CadMcpTools>();

            ServiceProvider serviceProvider = services.BuildServiceProvider(validateScopes: true);
            McpServer server = serviceProvider.GetRequiredService<McpServer>();
            Task serverTask = server.RunAsync(cancellation.Token);
            McpClient client = await McpClient.CreateAsync(
                new StreamClientTransport(
                    clientToServer.Writer.AsStream(),
                    serverToClient.Reader.AsStream()),
                cancellationToken: cancellation.Token).ConfigureAwait(false);
            return new InMemoryMcpHost(clientToServer, serverToClient, cancellation, serviceProvider, serverTask, client);
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
                // Expected bounded shutdown path for the in-memory MCP server.
                // 内存 MCP server 的有界关闭路径，OperationCanceledException 属于预期结果。
            }

            await services.DisposeAsync().ConfigureAwait(false);
            cancellation.Dispose();
        }
    }
}
