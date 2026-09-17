using System.Text.Json;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Server;

/// <summary>
/// Parses the bounded MCP JSON for one explicit circular detail-view request.
/// 解析一个显式圆形局部放大视图的 bounded MCP JSON。
/// </summary>
/// <remarks>
/// The codec accepts only paper-space millimetres and scalar view geometry. It deliberately has no path, macro,
/// arbitrary selection-mark or free-form COM field, so a client cannot turn detail creation into an unbounded native
/// command. codec 只接受纸空间毫米和标量视图几何；不接受路径、宏、任意 selection mark 或自由 COM 字段，避免
/// client 将局部放大视图变成无边界 native command。
/// </remarks>
internal static class DetailViewMcpCodec
{
    private const int MaximumJsonLength = 16 * 1024;
    private const int MaximumIdentityLength = 256;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        MaxDepth = 8,
    };

    /// <summary>Parses one optional detail request before a provider session starts.</summary>
    public static bool TryParse(
        string? json,
        out DrawingDetailViewRequest? request,
        out string? error)
    {
        request = null;
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        if (json.Length > MaximumJsonLength)
        {
            error = "detailViewJson-too-large";
            return false;
        }

        WireDetailView? wire;
        try
        {
            wire = JsonSerializer.Deserialize<WireDetailView>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            error = "detailViewJson-invalid-json";
            return false;
        }

        if (wire is null
            || !TryIdentity(wire.ParentViewId, out string parentViewId)
            || !TryIdentity(wire.Name, out string name)
            || !TryIdentity(wire.Label, out string label))
        {
            error = "detailViewJson-identity-required";
            return false;
        }

        if (!TryFinite(wire.DetailCenterXMillimeters, out double detailCenterX)
            || !TryFinite(wire.DetailCenterYMillimeters, out double detailCenterY)
            || !TryPositiveFinite(wire.DetailRadiusMillimeters, out double detailRadius)
            || !TryFinite(wire.PositionXMillimeters, out double positionX)
            || !TryFinite(wire.PositionYMillimeters, out double positionY))
        {
            error = "detailViewJson-coordinate-invalid";
            return false;
        }

        int scaleNumerator = wire.ScaleNumerator ?? 2;
        int scaleDenominator = wire.ScaleDenominator ?? 1;
        if (scaleNumerator <= 0 || scaleDenominator <= 0)
        {
            error = "detailViewJson-scale-invalid";
            return false;
        }

        request = new DrawingDetailViewRequest
        {
            ParentViewId = new ViewId(parentViewId),
            Name = name,
            Label = label,
            DetailCenter = new Coordinate2D(
                Length.FromMillimeters(detailCenterX),
                Length.FromMillimeters(detailCenterY)),
            DetailRadius = Length.FromMillimeters(detailRadius),
            Position = new Coordinate2D(
                Length.FromMillimeters(positionX),
                Length.FromMillimeters(positionY)),
            ScaleNumerator = scaleNumerator,
            ScaleDenominator = scaleDenominator,
            FullOutline = wire.FullOutline,
            JaggedOutline = wire.JaggedOutline,
        };
        return true;
    }

    private static bool TryIdentity(string? value, out string normalized)
    {
        normalized = value?.Trim() ?? string.Empty;
        return normalized.Length is > 0 and <= MaximumIdentityLength
            && normalized.All(character => !char.IsControl(character));
    }

    private static bool TryPositiveFinite(double? value, out double result)
    {
        result = value.GetValueOrDefault();
        return value.HasValue && double.IsFinite(result) && result > 0d;
    }

    private static bool TryFinite(double? value, out double result)
    {
        result = value.GetValueOrDefault();
        return value.HasValue && double.IsFinite(result);
    }

    private sealed class WireDetailView
    {
        public string? ParentViewId { get; init; }
        public string? Name { get; init; }
        public string? Label { get; init; }
        public double? DetailCenterXMillimeters { get; init; }
        public double? DetailCenterYMillimeters { get; init; }
        public double? DetailRadiusMillimeters { get; init; }
        public double? PositionXMillimeters { get; init; }
        public double? PositionYMillimeters { get; init; }
        public int? ScaleNumerator { get; init; }
        public int? ScaleDenominator { get; init; }
        public bool FullOutline { get; init; }
        public bool JaggedOutline { get; init; }
    }
}
