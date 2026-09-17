using System.Text.Json;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Server;

/// <summary>
/// Parses one bounded semantic obround-slot request for the high-level part compiler.
/// 解析高层零件 compiler 使用的一个有界长圆槽工程语义请求。
/// </summary>
/// <remarks>
/// The wire contract keeps the support-face probe explicit. This prevents the native provider from falling back to
/// ActiveDoc/temporary face enumeration when a slot is located on a narrow flange or bracket leg.
/// wire contract 显式携带支撑面探针，防止槽位于窄法兰或支架腿时 native provider 退化到 ActiveDoc/临时 face index。
/// </remarks>
internal static class SlotCutMcpCodec
{
    private const int MaximumJsonLength = 64 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    /// <summary>Parses and validates one optional slot-cut JSON value.</summary>
    public static bool TryParse(
        string? json,
        out SlotCutRequest? slot,
        out string? error)
    {
        slot = null;
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        if (json.Length > MaximumJsonLength)
        {
            error = "slotCutJson-too-large";
            return false;
        }

        WireSlot? wire;
        try
        {
            wire = JsonSerializer.Deserialize<WireSlot>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            error = "slotCutJson-invalid-json";
            return false;
        }

        if (wire is null || string.IsNullOrWhiteSpace(wire.Name))
        {
            error = "slotCutJson-name-required";
            return false;
        }

        if (!TryPositiveFinite(wire.WidthMillimeters, out double width))
        {
            error = "slotCutJson-width-invalid";
            return false;
        }

        if (!TryPoint(wire.Start, out Coordinate2D start)
            || !TryPoint(wire.End, out Coordinate2D end))
        {
            error = "slotCutJson-centerline-point-invalid";
            return false;
        }

        if (!TryPoint(wire.SupportFaceProbe, out Coordinate2D supportFaceProbe))
        {
            error = "slotCutJson-support-face-probe-invalid";
            return false;
        }

        double centerlineLength = Math.Sqrt(
            Math.Pow(end.X.Millimeters - start.X.Millimeters, 2d)
            + Math.Pow(end.Y.Millimeters - start.Y.Millimeters, 2d));
        if (!double.IsFinite(centerlineLength) || centerlineLength <= 0d)
        {
            error = "slotCutJson-centerline-degenerate";
            return false;
        }

        slot = new SlotCutRequest
        {
            Name = wire.Name.Trim(),
            Width = Length.FromMillimeters(width),
            Start = start,
            End = end,
            SupportFaceProbe = supportFaceProbe,
        };
        return true;
    }

    private static bool TryPoint(WirePoint? point, out Coordinate2D result)
    {
        result = default;
        if (!TryFinite(point?.XMillimeters, out double x)
            || !TryFinite(point?.YMillimeters, out double y))
        {
            return false;
        }

        result = new Coordinate2D(Length.FromMillimeters(x), Length.FromMillimeters(y));
        return true;
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

    private sealed class WireSlot
    {
        public string? Name { get; init; }

        public double? WidthMillimeters { get; init; }

        public WirePoint? Start { get; init; }

        public WirePoint? End { get; init; }

        public WirePoint? SupportFaceProbe { get; init; }
    }

    private sealed class WirePoint
    {
        public double? XMillimeters { get; init; }

        public double? YMillimeters { get; init; }
    }
}
