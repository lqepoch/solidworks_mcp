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

        Assert.Equal(4, tools.Count);
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
