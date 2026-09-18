using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Core;
using SolidWorksMcp.Protocol;
using SolidWorksMcp.Server;
#if SOLIDWORKS_MCP_NATIVE_PROVIDER
using SolidWorksMcp.Provider.SolidWorks;
#endif

// The production executable uses the official MCP C# SDK and stdio as its default transport.
// 生产入口使用官方 MCP C# SDK，并将 stdio 作为默认传输；日志只写 stderr，保持 stdout 为 MCP wire。
ConfigurationLoadResult loadedConfiguration = SolidWorksMcpConfigurationLoader.Load(args);
SolidWorksMcpConfiguration runtimeConfiguration = loadedConfiguration.Configuration;
ICadProvider? runtimeProvider = null;
#if SOLIDWORKS_MCP_NATIVE_PROVIDER
if (runtimeConfiguration.ProviderMode == ProviderModes.Native)
{
    // Native mode is explicit and binds the provider to the private, user-local path policy loaded above.
    // Native mode 必须显式开启，并把 provider 绑定到上面加载的用户本地私密路径策略。
    runtimeProvider = new SolidWorksCadProvider(loadedConfiguration.PathAllowlist);
}
#else
if (runtimeConfiguration.ProviderMode == ProviderModes.Native)
{
    throw new InvalidOperationException(
        "Native provider mode was requested, but this build does not include the opt-in SOLIDWORKS provider. Run the Windows doctor and rebuild the full solution.");
}
#endif
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSolidWorksMcp(runtimeProvider, runtimeConfiguration, pathAllowlist: loadedConfiguration.PathAllowlist);
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation
        {
            Name = "solidworks-mcp",
            Version = ProtocolSchema.CurrentVersion,
        };
        options.Capabilities = new ServerCapabilities
        {
            Tools = new ToolsCapability(),
            Resources = new ResourcesCapability(),
        };
    })
    .WithStdioServerTransport()
    .WithTools<CadMcpTools>()
    .WithResources<SolidWorksAiResources>();

builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

await builder.Build().RunAsync();
