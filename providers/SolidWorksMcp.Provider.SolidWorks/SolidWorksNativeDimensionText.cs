using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Provider.SolidWorks;

/// <summary>
/// Reads a display dimension from native SOLIDWORKS state without trusting request text.
/// 从 SOLIDWORKS 原生状态读取 DisplayDimension；不信任请求文本。
/// </summary>
internal static class SolidWorksNativeDimensionText
{
    /// <summary>
    /// Builds deterministic diagnostic text from legal SOLIDWORKS text parts and the associated native value.
    /// 使用合法的 SOLIDWORKS 文本片段和关联原生数值生成确定性的诊断文本。
    /// </summary>
    /// <remarks>
    /// `swDimensionTextAll` is explicitly invalid for IDisplayDimension.GetText.  The helper therefore reads only
    /// the four legal text-part values, then reads IDimension.SystemValue from the DisplayDimension association. The
    /// numeric fallback is still native evidence; it is not a value synthesized from an MCP request.
    /// `swDimensionTextAll` 对 IDisplayDimension.GetText 明确无效，因此这里只读取四种合法文本片段，再从
    /// DisplayDimension 关联的 IDimension 读取 SystemValue。数值 fallback 仍是原生证据，不是 MCP 请求合成值。
    /// </remarks>
    public static string Read(DisplayDimension displayDimension)
    {
        ArgumentNullException.ThrowIfNull(displayDimension);

        string[] textParts =
        [
            displayDimension.GetText((int)swDimensionTextParts_e.swDimensionTextPrefix)?.Trim() ?? string.Empty,
            displayDimension.GetText((int)swDimensionTextParts_e.swDimensionTextSuffix)?.Trim() ?? string.Empty,
            displayDimension.GetText((int)swDimensionTextParts_e.swDimensionTextCalloutAbove)?.Trim() ?? string.Empty,
            displayDimension.GetText((int)swDimensionTextParts_e.swDimensionTextCalloutBelow)?.Trim() ?? string.Empty,
        ];

        object? rawDimension = null;
        try
        {
            rawDimension = displayDimension.GetDimension();
            if (rawDimension is not Dimension nativeDimension)
            {
                throw new InvalidOperationException(
                    "SOLIDWORKS returned a DisplayDimension without an associated native Dimension object.");
            }

            double systemValueMeters = nativeDimension.SystemValue;
            if (!double.IsFinite(systemValueMeters))
            {
                throw new InvalidOperationException("SOLIDWORKS returned a non-finite native dimension value.");
            }

            string nativeValue = Length.FromMeters(systemValueMeters).Millimeters.ToString(
                "G17",
                CultureInfo.InvariantCulture);
            string nativeParts = string.Join(" ", textParts.Where(static part => part.Length > 0));
            return nativeParts.Length == 0 ? nativeValue : $"{nativeValue} {nativeParts}";
        }
        finally
        {
            // Both the associated Dimension and the DisplayDimension are owned by the provider STA.  The caller
            // releases DisplayDimension; this helper releases only the temporary association RCW.
            // 关联 Dimension 与 DisplayDimension 都属于 Provider STA；调用方释放 DisplayDimension，本 helper
            // 只释放临时关联 RCW。
            SolidWorksDocumentRouting.Release(rawDimension);
        }
    }
}
