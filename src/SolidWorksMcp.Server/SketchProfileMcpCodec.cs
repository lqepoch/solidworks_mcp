using System.Collections.Immutable;
using System.Text.Json;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Server;

/// <summary>
/// Parses the bounded JSON representation used by the flat MCP create-part tool into a vendor-neutral profile.
/// 将扁平 MCP create-part tool 使用的受限 JSON 表示解析为 vendor-neutral profile。
/// </summary>
/// <remarks>
/// The official MCP C# SDK reflects method parameters into top-level schema properties. A single explicit JSON field
/// keeps that public schema compact while this codec still validates every coordinate and delegates topology rules to
/// <see cref="SketchProfileValidation"/>. The codec never creates COM objects or repairs an open loop.
/// 官方 MCP C# SDK 会把方法参数反射为顶层 schema 属性。单一显式 JSON 字段保持公共 schema 紧凑，同时本 codec
/// 仍校验每个坐标，并把拓扑规则交给 <see cref="SketchProfileValidation"/>；这里绝不创建 COM 对象或修复开口轮廓。
/// </remarks>
internal static class SketchProfileMcpCodec
{
    private const int MaximumJsonLength = 256 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    /// <summary>Parses a profile JSON value, returning a stable validation reason instead of throwing to MCP.</summary>
    public static bool TryParse(
        string? json,
        out SketchProfileRequest? profile,
        out string? error)
    {
        profile = null;
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        if (json.Length > MaximumJsonLength)
        {
            error = "initialSketchProfileJson-too-large";
            return false;
        }

        WireProfile? wire;
        try
        {
            wire = JsonSerializer.Deserialize<WireProfile>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            error = "initialSketchProfileJson-invalid-json";
            return false;
        }

        if (wire?.Segments is null)
        {
            error = "initialSketchProfileJson-segments-required";
            return false;
        }

        var segments = ImmutableArray.CreateBuilder<SketchCurveRequest>(wire.Segments.Count);
        for (int index = 0; index < wire.Segments.Count; index++)
        {
            WireCurve curve = wire.Segments[index];
            if (!TryParseKind(curve.Kind, out SketchCurveKind kind))
            {
                error = $"initialSketchProfileJson-segment-{index}-kind-unsupported";
                return false;
            }

            if (!TryPoint(curve.StartXMillimeters, curve.StartYMillimeters, out Coordinate2D start)
                || !TryPoint(curve.EndXMillimeters, curve.EndYMillimeters, out Coordinate2D end))
            {
                error = $"initialSketchProfileJson-segment-{index}-endpoint-invalid";
                return false;
            }

            Coordinate2D through = default;
            if (kind == SketchCurveKind.ThreePointArc
                && !TryPoint(curve.ThroughXMillimeters, curve.ThroughYMillimeters, out through))
            {
                error = $"initialSketchProfileJson-segment-{index}-through-point-invalid";
                return false;
            }

            segments.Add(
                new SketchCurveRequest
                {
                    Kind = kind,
                    Start = start,
                    Through = through,
                    End = end,
                });
        }

        profile = new SketchProfileRequest { Segments = segments.ToImmutable() };
        string? validationError = SketchProfileValidation.Validate(profile);
        if (validationError is not null)
        {
            profile = null;
            error = $"initialSketchProfileJson-{validationError}";
            return false;
        }

        return true;
    }

    private static bool TryParseKind(string? text, out SketchCurveKind kind)
    {
        kind = default;
        if (string.Equals(text, "line", StringComparison.OrdinalIgnoreCase))
        {
            kind = SketchCurveKind.Line;
            return true;
        }

        if (string.Equals(text, "arc", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "three-point-arc", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "threePointArc", StringComparison.OrdinalIgnoreCase))
        {
            kind = SketchCurveKind.ThreePointArc;
            return true;
        }

        return false;
    }

    private static bool TryPoint(
        double? xMillimeters,
        double? yMillimeters,
        out Coordinate2D point)
    {
        point = default;
        if (xMillimeters is not double x
            || yMillimeters is not double y
            || !double.IsFinite(x)
            || !double.IsFinite(y))
        {
            return false;
        }

        point = new Coordinate2D(Length.FromMillimeters(x), Length.FromMillimeters(y));
        return true;
    }

    private sealed class WireProfile
    {
        public List<WireCurve>? Segments { get; init; }
    }

    private sealed class WireCurve
    {
        public string? Kind { get; init; }
        public double? StartXMillimeters { get; init; }
        public double? StartYMillimeters { get; init; }
        public double? ThroughXMillimeters { get; init; }
        public double? ThroughYMillimeters { get; init; }
        public double? EndXMillimeters { get; init; }
        public double? EndYMillimeters { get; init; }
    }
}
