namespace SolidWorksMcp.Provider.SolidWorks;

/// <summary>Marks the only production module permitted to depend on SOLIDWORKS Interop.</summary>
/// <remarks>标记唯一允许依赖 SOLIDWORKS Interop 的生产模块；其他层必须通过抽象契约访问 CAD。</remarks>
internal static class ModuleMarker
{
}
