using System.IO.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Core;
using SolidWorksMcp.Protocol;
using SolidWorksMcp.Provider.Fake;
using SolidWorksMcp.Server;

namespace SolidWorksMcp.ContractTests;

/// <summary>Exercises the official MCP SDK handshake, tool discovery, schema and pre-business validation.</summary>
/// <remarks>
/// The in-memory stream transport is used only in tests; production remains stdio. This proves a real MCP client can
/// discover the compact registry and call FakeCad through the same DI composition as the executable.
/// 测试使用内存流传输，生产仍固定为 stdio；这验证真实 MCP Client 能通过同一 DI 组合发现并调用 FakeCad。
/// </remarks>
public sealed class McpServerIntegrationTests
{
    /// <summary>SDK client initialization and tools/list must expose the compact registry and metadata.</summary>
    [Fact]
    public async Task McpClientHandshakeAndDiscoveryExposeToolMetadata()
    {
        var countingProvider = new CountingCadProvider(new FakeCadProvider());
        await using var host = await InMemoryMcpHost.CreateAsync(countingProvider);

        IList<McpClientTool> tools = await host.Client.ListToolsAsync();

        Assert.Equal(8, tools.Count);
        McpClientTool createTool = Assert.Single(tools, tool => tool.Name == "cad.create-part");
        Assert.Contains("Preconditions", createTool.Description, StringComparison.Ordinal);
        Assert.Contains("Side effects", createTool.Description, StringComparison.Ordinal);
        Assert.Contains("schemaVersion", createTool.JsonSchema.GetProperty("required").EnumerateArray().Select(value => value.GetString()));

        CallToolResult capabilityResult = await host.Client.CallToolAsync("cad.capabilities");
        Assert.False(capabilityResult.IsError);
        Assert.Contains("CAD operation completed", capabilityResult.Content.OfType<TextContentBlock>().Single().Text, StringComparison.Ordinal);
        Assert.Equal(0, countingProvider.StartSessionCount);
    }

    /// <summary>Schema/version rejection must happen before the provider starts a CAD session.</summary>
    [Fact]
    public async Task MalformedToolInputFailsBeforeBusinessExecution()
    {
        var countingProvider = new CountingCadProvider(new FakeCadProvider());
        await using var host = await InMemoryMcpHost.CreateAsync(countingProvider);

        CallToolResult invalid = await host.Client.CallToolAsync(
            "cad.create-part",
            new Dictionary<string, object?>
            {
                ["schemaVersion"] = "0.0",
                ["documentId"] = "should-not-be-created",
                ["configuration"] = "Default",
            });

        Assert.True(invalid.IsError);
        Assert.Contains(ErrorCodes.InvalidRequest, invalid.Content.OfType<TextContentBlock>().Single().Text, StringComparison.Ordinal);
        Assert.Equal(0, countingProvider.StartSessionCount);
    }

    /// <summary>drawing.validate rejects malformed requirement JSON before starting a provider session.</summary>
    [Fact]
    public async Task DrawingValidateMalformedRequirementFailsBeforeBusinessExecution()
    {
        var countingProvider = new CountingCadProvider(new FakeCadProvider());
        await using var host = await InMemoryMcpHost.CreateAsync(countingProvider);

        CallToolResult invalid = await host.Client.CallToolAsync(
            "drawing.validate",
            new Dictionary<string, object?>
            {
                ["schemaVersion"] = ProtocolSchema.CurrentVersion,
                ["documentId"] = "drawing-validation-001",
                ["dimensionRequirementJson"] = "{}",
                ["dimensionEvidenceJson"] = "[]",
            });

        Assert.True(invalid.IsError);
        Assert.Contains(ErrorCodes.InvalidRequest, invalid.Content.OfType<TextContentBlock>().Single().Text, StringComparison.Ordinal);
        Assert.Equal(0, countingProvider.StartSessionCount);
    }

    /// <summary>drawing.release rejects malformed high-level JSON before a CAD session is started.</summary>
    [Fact]
    public async Task DrawingReleaseMalformedPlanFailsBeforeBusinessExecution()
    {
        var countingProvider = new CountingCadProvider(new FakeCadProvider());
        await using var host = await InMemoryMcpHost.CreateAsync(countingProvider);

        CallToolResult invalid = await host.Client.CallToolAsync(
            "drawing.release",
            new Dictionary<string, object?>
            {
                ["schemaVersion"] = ProtocolSchema.CurrentVersion,
                ["documentId"] = "drawing-release-001",
                ["expectedStateHash"] = "state-001",
                ["requirementGraphJson"] = "{}",
                ["planOptionsJson"] = "{}",
                ["dimensionRequirementJson"] = "{}",
                ["dimensionEvidenceJson"] = "[]",
                ["layoutJson"] = "{}",
                ["manufacturingJson"] = "{}",
                ["artifactPolicyJson"] = "{}",
                ["transactionId"] = "transaction-release-001",
                ["idempotencyKey"] = "idempotency-release-001",
            });

        Assert.True(invalid.IsError);
        Assert.Contains(ErrorCodes.InvalidRequest, invalid.Content.OfType<TextContentBlock>().Single().Text, StringComparison.Ordinal);
        Assert.Equal(0, countingProvider.StartSessionCount);
    }

    /// <summary>
    /// Public drawing.repair must mutate only the exact annotation and prove the persisted reopen state.
    /// 公开 drawing.repair 只能修改精确 annotation，并证明保存重开后的持久化状态。
    /// </summary>
    [Fact]
    public async Task DrawingRepairMovesOneExactAnnotationAndVerifiesReopen()
    {
        var provider = new FakeCadProvider();
        OperationResult<ICadSession> started = await provider.StartSessionAsync(new CadSessionOptions());
        Assert.True(started.IsSuccess, started.Error?.Message);
        ICadSession session = started.Value!;

        OperationResult<ICadPartDocument> part = await session.CreatePartAsync(
            new CreatePartRequest { RequestedDocumentId = new DocumentId("repair-source-part"), Configuration = "Default" });
        Assert.True(part.IsSuccess, part.Error?.Message);

        OperationResult<ICadDrawingDocument> drawing = await session.CreateDrawingAsync(
            new CreateDrawingRequest
            {
                RequestedDocumentId = new DocumentId("repair-target-drawing"),
                Configuration = "Default",
                SourceDocumentId = part.Value!.DocumentId,
            });
        Assert.True(drawing.IsSuccess, drawing.Error?.Message);

        OperationResult<DrawingViewSnapshot> view = await drawing.Value!.AddViewAsync(
            new DrawingViewRequest
            {
                RequestedViewId = new ViewId("repair-view"),
                Name = "Front",
                Orientation = "Front",
                Position = new Coordinate2D(Length.FromMillimeters(50d), Length.FromMillimeters(50d)),
            });
        Assert.True(view.IsSuccess, view.Error?.Message);

        OperationResult<DrawingAnnotationSnapshot> annotation = await drawing.Value.AddAnnotationAsync(
            new DrawingAnnotationRequest
            {
                RequestedAnnotationId = new AnnotationId("repair-annotation"),
                ViewId = view.Value!.ViewId,
                Kind = "note",
                Text = "deterministic-repair",
                Position = new Coordinate2D(Length.FromMillimeters(20d), Length.FromMillimeters(20d)),
            });
        Assert.True(annotation.IsSuccess, annotation.Error?.Message);
        OperationResult<SaveReceipt> save = await drawing.Value.SaveAsync();
        Assert.True(save.IsSuccess, save.Error?.Message);
        OperationResult<CadInspectionSnapshot> inspection = await session.Inspection.InspectAsync(drawing.Value.DocumentId);
        Assert.True(inspection.IsSuccess, inspection.Error?.Message);

        await using var host = await InMemoryMcpHost.CreateAsync(provider);
        CallToolResult result = await host.Client.CallToolAsync(
            "drawing.repair",
            new Dictionary<string, object?>
            {
                ["schemaVersion"] = ProtocolSchema.CurrentVersion,
                ["documentId"] = drawing.Value.DocumentId.Value,
                ["expectedStateHash"] = inspection.Value!.Document.StateHash,
                ["repairPlanJson"] = "{\"schemaVersion\":\"1.0\",\"fingerprint\":\"plan-repair-001\",\"actions\":["
                    + "{\"actionCode\":\"layout.apply-planned-position\",\"targetId\":\"repair-annotation\","
                    + "\"findingCode\":\"annotation-repositioned\",\"preconditionFingerprint\":\"finding-001\","
                    + "\"newPositionXMillimeters\":80,\"newPositionYMillimeters\":60}]}",
            });

        Assert.False(result.IsError);
        Assert.Contains("save-reopen-verified", result.StructuredContent!.Value.ToString(), StringComparison.Ordinal);
        Assert.Contains("repair-annotation", result.StructuredContent.Value.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Valid tool input must cross the server boundary and create exactly one FakeCad document.</summary>
    [Fact]
    public async Task ValidCreatePartToolUsesFakeProviderAndReturnsStructuredResult()
    {
        var countingProvider = new CountingCadProvider(new FakeCadProvider());
        await using var host = await InMemoryMcpHost.CreateAsync(countingProvider);

        CallToolResult result = await host.Client.CallToolAsync(
            "cad.create-part",
            new Dictionary<string, object?>
            {
                ["schemaVersion"] = ProtocolSchema.CurrentVersion,
                ["operationId"] = "mcp-contract-001",
                ["documentId"] = "mcp-part-001",
                ["configuration"] = "Default",
            });

        Assert.False(result.IsError);
        Assert.Equal(1, countingProvider.StartSessionCount);
        Assert.Contains("CAD operation completed", result.Content.OfType<TextContentBlock>().Single().Text, StringComparison.Ordinal);
        Assert.NotNull(result.StructuredContent);
    }

    /// <summary>Valid closed line/arc profile JSON crosses the compact MCP boundary without exposing COM types.</summary>
    /// <remarks>
    /// This is intentionally a generic D-shaped profile. It verifies only the public schema/validation path; native
    /// geometry remains the responsibility of the SOLIDWORKS provider Live suite. 这里使用通用 D 形轮廓，只验证
    /// 公共 schema 和 validation path；native geometry 仍由 SOLIDWORKS Provider Live suite 负责。
    /// </remarks>
    [Fact]
    public async Task ValidStructuredSketchProfileReachesFakeProvider()
    {
        var countingProvider = new CountingCadProvider(new FakeCadProvider());
        await using var host = await InMemoryMcpHost.CreateAsync(countingProvider);

        CallToolResult result = await host.Client.CallToolAsync(
            "cad.create-part",
            new Dictionary<string, object?>
            {
                ["schemaVersion"] = ProtocolSchema.CurrentVersion,
                ["documentId"] = "mcp-curved-part-001",
                ["configuration"] = "Default",
                ["initialSketchProfileJson"] = "{\"segments\":["
                    + "{\"kind\":\"line\",\"startXMillimeters\":-20,\"startYMillimeters\":-20,\"endXMillimeters\":20,\"endYMillimeters\":-20},"
                    + "{\"kind\":\"line\",\"startXMillimeters\":20,\"startYMillimeters\":-20,\"endXMillimeters\":20,\"endYMillimeters\":0},"
                    + "{\"kind\":\"arc\",\"startXMillimeters\":20,\"startYMillimeters\":0,\"throughXMillimeters\":0,\"throughYMillimeters\":22,\"endXMillimeters\":-20,\"endYMillimeters\":0},"
                    + "{\"kind\":\"line\",\"startXMillimeters\":-20,\"startYMillimeters\":0,\"endXMillimeters\":-20,\"endYMillimeters\":-20}"
                    + "]}",
            });

        Assert.False(result.IsError);
        Assert.Equal(1, countingProvider.StartSessionCount);
    }

    /// <summary>High-level build tool executes the complete FakeCad 3D-to-2D orchestration without primitive tool spam.</summary>
    /// <remarks>
    /// The same request shape is used by the native Live path; FakeCad only proves orchestration and contract ordering.
    /// 相同请求形状会被 native Live path 使用；FakeCad 这里只证明编排和契约顺序，不冒充真实 SOLIDWORKS 几何。
    /// </remarks>
    [Fact]
    public async Task BuildPartDrawingToolRunsTheHighLevelWorkflow()
    {
        var countingProvider = new CountingCadProvider(new FakeCadProvider());
        await using var host = await InMemoryMcpHost.CreateAsync(countingProvider);

        CallToolResult result = await host.Client.CallToolAsync(
            "cad.build-part-drawing",
            new Dictionary<string, object?>
            {
                ["schemaVersion"] = ProtocolSchema.CurrentVersion,
                ["documentId"] = "mcp-build-part-001",
                ["drawingDocumentId"] = "mcp-build-drawing-001",
                ["configuration"] = "Default",
                ["partPath"] = "C:\\mcp-artifacts\\mcp-build-part-001.sldprt",
                ["drawingPath"] = "C:\\mcp-artifacts\\mcp-build-drawing-001.slddrw",
                ["pdfPath"] = "C:\\mcp-artifacts\\mcp-build-drawing-001.pdf",
                ["extrusionDepthMillimeters"] = 8d,
                ["initialSketchProfileJson"] = "{\"segments\":["
                    + "{\"kind\":\"line\",\"startXMillimeters\":-20,\"startYMillimeters\":-15,\"endXMillimeters\":0,\"endYMillimeters\":-15},"
                    + "{\"kind\":\"arc\",\"startXMillimeters\":0,\"startYMillimeters\":-15,\"throughXMillimeters\":0,\"throughYMillimeters\":15,\"endXMillimeters\":15,\"endYMillimeters\":0},"
                    + "{\"kind\":\"line\",\"startXMillimeters\":15,\"startYMillimeters\":0,\"endXMillimeters\":-20,\"endYMillimeters\":15},"
                    + "{\"kind\":\"line\",\"startXMillimeters\":-20,\"startYMillimeters\":15,\"endXMillimeters\":-20,\"endYMillimeters\":-15}"
                    + "]}",
                ["throughHolePatternJson"] = "{\"name\":\"Mounting-Hole-Group\",\"diameterMillimeters\":6,\"centers\":["
                    + "{\"xMillimeters\":0,\"yMillimeters\":-8},"
                    + "{\"xMillimeters\":0,\"yMillimeters\":8}"
                    + "]}",
            });

        Assert.False(result.IsError);
        Assert.Equal(1, countingProvider.StartSessionCount);
        Assert.Contains("CAD operation completed", result.Content.OfType<TextContentBlock>().Single().Text, StringComparison.Ordinal);
        Assert.NotNull(result.StructuredContent);
        Assert.Contains("part.hole-pattern", result.StructuredContent!.Value.ToString(), StringComparison.Ordinal);
        Assert.Contains("drawing.section-view", result.StructuredContent.Value.ToString(), StringComparison.Ordinal);
        Assert.Contains("Section A-A", result.StructuredContent.Value.ToString(), StringComparison.Ordinal);
        Assert.Contains("drawing.pattern-callout", result.StructuredContent.Value.ToString(), StringComparison.Ordinal);
        Assert.Contains("drawing.pattern-callout.reopened", result.StructuredContent.Value.ToString(), StringComparison.Ordinal);
        Assert.Contains("2X", result.StructuredContent.Value.ToString(), StringComparison.Ordinal);
        Assert.Contains("THRU", result.StructuredContent.Value.ToString(), StringComparison.Ordinal);
        Assert.Contains("PITCH 16", result.StructuredContent.Value.ToString(), StringComparison.Ordinal);
        Assert.Contains("SYMMETRIC", result.StructuredContent.Value.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Invalid repeated-hole JSON fails before a provider session can mutate a document.</summary>
    /// <remarks>
    /// The profile is valid on purpose; only the semantic hole-group name is invalid. This isolates the codec's
    /// fail-closed boundary instead of accidentally testing profile validation. 轮廓故意保持有效，只让重复孔组
    /// 的语义名称无效，从而单独证明 codec 在 Provider/session 之前 fail closed。
    /// </remarks>
    [Fact]
    public async Task InvalidRepeatedHolePatternFailsBeforeBusinessExecution()
    {
        var countingProvider = new CountingCadProvider(new FakeCadProvider());
        await using var host = await InMemoryMcpHost.CreateAsync(countingProvider);

        CallToolResult result = await host.Client.CallToolAsync(
            "cad.build-part-drawing",
            new Dictionary<string, object?>
            {
                ["schemaVersion"] = ProtocolSchema.CurrentVersion,
                ["documentId"] = "mcp-invalid-hole-pattern-part-001",
                ["drawingDocumentId"] = "mcp-invalid-hole-pattern-drawing-001",
                ["configuration"] = "Default",
                ["partPath"] = "C:\\mcp-artifacts\\invalid-hole-pattern-part-001.sldprt",
                ["drawingPath"] = "C:\\mcp-artifacts\\invalid-hole-pattern-drawing-001.slddrw",
                ["pdfPath"] = "C:\\mcp-artifacts\\invalid-hole-pattern-drawing-001.pdf",
                ["extrusionDepthMillimeters"] = 8d,
                ["initialSketchProfileJson"] = "{\"segments\":["
                    + "{\"kind\":\"line\",\"startXMillimeters\":0,\"startYMillimeters\":0,\"endXMillimeters\":20,\"endYMillimeters\":0},"
                    + "{\"kind\":\"line\",\"startXMillimeters\":20,\"startYMillimeters\":0,\"endXMillimeters\":20,\"endYMillimeters\":20},"
                    + "{\"kind\":\"line\",\"startXMillimeters\":20,\"startYMillimeters\":20,\"endXMillimeters\":0,\"endYMillimeters\":20},"
                    + "{\"kind\":\"line\",\"startXMillimeters\":0,\"startYMillimeters\":20,\"endXMillimeters\":0,\"endYMillimeters\":0}"
                    + "]}",
                ["throughHolePatternJson"] = "{\"diameterMillimeters\":6,\"centers\":[{\"xMillimeters\":5,\"yMillimeters\":5}]}",
            });

        Assert.True(result.IsError);
        Assert.Contains(ErrorCodes.InvalidRequest, result.Content.OfType<TextContentBlock>().Single().Text, StringComparison.Ordinal);
        Assert.Equal(0, countingProvider.StartSessionCount);
    }

    /// <summary>Disconnected profile JSON fails before provider/session startup.</summary>
    [Fact]
    public async Task DisconnectedStructuredSketchProfileFailsBeforeBusinessExecution()
    {
        var countingProvider = new CountingCadProvider(new FakeCadProvider());
        await using var host = await InMemoryMcpHost.CreateAsync(countingProvider);

        CallToolResult result = await host.Client.CallToolAsync(
            "cad.create-part",
            new Dictionary<string, object?>
            {
                ["schemaVersion"] = ProtocolSchema.CurrentVersion,
                ["documentId"] = "mcp-invalid-curved-part-001",
                ["configuration"] = "Default",
                ["initialSketchProfileJson"] = "{\"segments\":["
                    + "{\"kind\":\"line\",\"startXMillimeters\":0,\"startYMillimeters\":0,\"endXMillimeters\":10,\"endYMillimeters\":0},"
                    + "{\"kind\":\"line\",\"startXMillimeters\":20,\"startYMillimeters\":0,\"endXMillimeters\":0,\"endYMillimeters\":10},"
                    + "{\"kind\":\"line\",\"startXMillimeters\":0,\"startYMillimeters\":10,\"endXMillimeters\":0,\"endYMillimeters\":0}"
                    + "]}",
            });

        Assert.True(result.IsError);
        Assert.Contains(ErrorCodes.InvalidRequest, result.Content.OfType<TextContentBlock>().Single().Text, StringComparison.Ordinal);
        Assert.Equal(0, countingProvider.StartSessionCount);
    }

    /// <summary>Capability negotiation must reject an unsupported provider combination before session startup.</summary>
    [Fact]
    public async Task UnsupportedProviderCapabilityFailsDeterministicallyBeforeSessionStartup()
    {
        var options = new FakeCadOptions
        {
            Capabilities = new CadCapabilitySet(
            [
                new CadCapability(CadCapabilityNames.Inspection, supported: true),
            ]),
        };
        var countingProvider = new CountingCadProvider(new FakeCadProvider(options));
        await using var host = await InMemoryMcpHost.CreateAsync(
            countingProvider,
            new SolidWorksMcpConfiguration(providerMode: ProviderModes.Fake));

        CallToolResult result = await host.Client.CallToolAsync(
            "cad.create-part",
            new Dictionary<string, object?>
            {
                ["schemaVersion"] = ProtocolSchema.CurrentVersion,
                ["documentId"] = "unsupported-part",
                ["configuration"] = "Default",
            });

        Assert.True(result.IsError);
        Assert.Contains(ErrorCodes.UnsupportedCapability, result.Content.OfType<TextContentBlock>().Single().Text, StringComparison.Ordinal);
        Assert.Equal(0, countingProvider.StartSessionCount);
    }

    /// <summary>Counts provider starts without changing the provider contract, proving validation ordering.</summary>
    private sealed class CountingCadProvider(ICadProvider inner) : ICadProvider
    {
        private readonly ICadProvider inner = inner ?? throw new ArgumentNullException(nameof(inner));
        private int startSessionCount;

        public int StartSessionCount => Volatile.Read(ref startSessionCount);

        /// <inheritdoc />
        public CadCapabilitySet Capabilities => inner.Capabilities;

        /// <inheritdoc />
        public ValueTask<OperationResult<ICadSession>> StartSessionAsync(
            CadSessionOptions options,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref startSessionCount);
            return inner.StartSessionAsync(options, cancellationToken);
        }
    }

    /// <summary>Owns paired in-memory streams, SDK server/client and deterministic async cleanup.</summary>
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

        public static async Task<InMemoryMcpHost> CreateAsync(
            ICadProvider provider,
            SolidWorksMcpConfiguration? configuration = null)
        {
            ArgumentNullException.ThrowIfNull(provider);
            var clientToServer = new Pipe();
            var serverToClient = new Pipe();
            var cancellation = new CancellationTokenSource();
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddLogging();
            serviceCollection.AddSolidWorksMcp(provider, configuration);
            serviceCollection
                .AddMcpServer(options =>
                {
                    options.ServerInfo = new Implementation
                    {
                        Name = "solidworks-mcp-contract-test",
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

        /// <inheritdoc />
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
                // Cancellation is the expected bounded shutdown path for the in-memory server.
                // 内存服务器通过取消令牌进行有界关闭，OperationCanceledException 属于预期路径。
            }

            await services.DisposeAsync().ConfigureAwait(false);
            cancellation.Dispose();
        }

    }
}
