using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SolidWorksMcp.AutoDrawing;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Core;

namespace SolidWorksMcp.Server;

/// <summary>Registers the A02 server composition without taking a dependency on a vendor provider assembly.</summary>
/// <remarks>
/// This composition root is deliberately provider-neutral. Native COM registration will be added behind the same
/// interface by the provider issues; the server itself must remain buildable on a hosted runner without SOLIDWORKS.
/// 组合根刻意保持 Provider 中立；原生 COM 将由后续 Provider Issue 在同一接口后接入，Server 必须能在没有
/// SOLIDWORKS 的 Hosted Runner 上独立构建。
/// </remarks>
public static class ServerComposition
{
    /// <summary>Registers the default safe provider, or a test/native provider explicitly supplied by the host.</summary>
    /// <remarks>
    /// Passing a provider is explicit dependency injection for FakeCad and future native hosting. Omitting it is safe:
    /// CAD mutations remain unsupported and are reported as a stable capability error.
    /// 显式传入 Provider 用于 FakeCad 和未来原生 Host；省略时仍是安全行为，CAD mutation 会返回稳定的能力错误。
    /// </remarks>
    public static IServiceCollection AddSolidWorksMcp(
        this IServiceCollection services,
        ICadProvider? provider = null,
        SolidWorksMcpConfiguration? configuration = null,
        CadSessionOptions? sessionOptions = null,
        CadPathAllowlist? pathAllowlist = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (provider is not null)
        {
            services.AddSingleton(provider);
            services.AddSingleton<ICadProvider>(provider);
        }
        else
        {
            services.TryAddSingleton<ICadProvider, UnavailableCadProvider>();
        }

        services.TryAddSingleton(configuration ?? new SolidWorksMcpConfiguration());
        services.TryAddSingleton(sessionOptions ?? new CadSessionOptions());
        services.TryAddSingleton(pathAllowlist ?? CadPathAllowlist.DenyAll);
        services.TryAddSingleton(McpToolCatalog.CreateDefault());
        services.TryAddSingleton<IMcpOperationCatalog>(serviceProvider => serviceProvider.GetRequiredService<McpToolCatalog>());
        services.TryAddSingleton<InMemoryMcpAuditSink>();
        services.TryAddSingleton<IMcpAuditSink>(serviceProvider => serviceProvider.GetRequiredService<InMemoryMcpAuditSink>());
        services.TryAddSingleton<McpControlPlane>(serviceProvider =>
            new McpControlPlane(
                serviceProvider.GetRequiredService<IMcpOperationCatalog>(),
                serviceProvider.GetRequiredService<ICadProvider>().Capabilities,
                serviceProvider.GetRequiredService<SolidWorksMcpConfiguration>(),
                serviceProvider.GetRequiredService<CadPathAllowlist>(),
                serviceProvider.GetRequiredService<IMcpAuditSink>()));
        services.TryAddSingleton<McpOperationCorrelation>();
        services.TryAddSingleton<McpCapabilityNegotiator>();
        services.TryAddSingleton<CadSessionAccessor>();
        // The idempotency store is shared for the stdio host lifetime. Release execution creates a transaction engine
        // per bound session, while this store reconciles duplicate MCP requests across those short-lived engines.
        // 幂等 store 共享整个 stdio Host 生命周期；release 每次按 session 创建短生命周期 transaction engine，
        // 该 store 仍可跨 engine 对重复 MCP request 做 reconcile。
        services.TryAddSingleton<ICadIdempotencyStore, InMemoryCadIdempotencyStore>();
        services.TryAddSingleton<DrawingReleaseService>();
        return services;
    }
}
