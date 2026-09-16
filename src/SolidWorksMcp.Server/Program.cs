using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SolidWorksMcp.Protocol;
using SolidWorksMcp.Server;

// The production executable uses the official MCP C# SDK and stdio as its default transport.
// 生产入口使用官方 MCP C# SDK，并将 stdio 作为默认传输；日志只写 stderr，保持 stdout 为 MCP wire。
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSolidWorksMcp();
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation
        {
            Name = "solidworks-mcp",
            Version = ProtocolSchema.CurrentVersion,
        };
        options.Capabilities = new ServerCapabilities { Tools = new ToolsCapability() };
    })
    .WithStdioServerTransport()
    .WithTools<CadMcpTools>();

builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

await builder.Build().RunAsync();
