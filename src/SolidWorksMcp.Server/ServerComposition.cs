using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SolidWorksMcp.CadAbstractions;

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
        ICadProvider? provider = null)
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

        services.TryAddSingleton(McpToolCatalog.CreateDefault());
        services.TryAddSingleton<McpOperationCorrelation>();
        services.TryAddSingleton<CadSessionAccessor>();
        return services;
    }
}
