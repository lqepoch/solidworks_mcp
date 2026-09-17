using System.Collections.Immutable;
using System.Text.Json;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Server;

/// <summary>
/// Parses the bounded MCP JSON representation of one semantic through-hole group.
/// 解析一个具备工程语义的重复通孔组的 bounded MCP JSON 表示。
/// </summary>
/// <remarks>
/// The codec validates units, finite values, cardinality and semantic naming before the provider session starts.
/// It never creates COM objects and never converts a group into anonymous primitive holes.
/// codec 在启动 Provider session 前校验单位、有限值、数量和语义名称；绝不创建 COM 对象，也不把孔组退化成匿名 primitive hole。
/// </remarks>
internal static class ThroughHolePatternMcpCodec
{
    private const int MaximumJsonLength = 64 * 1024;
    private const int MaximumHoleCount = 128;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    /// <summary>Parses and validates one optional hole-group JSON value.</summary>
    public static bool TryParse(
        string? json,
        out ThroughHolePatternRequest? pattern,
        out string? error)
    {
        pattern = null;
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        if (json.Length > MaximumJsonLength)
        {
            error = "throughHolePatternJson-too-large";
            return false;
        }

        WirePattern? wire;
        try
        {
            wire = JsonSerializer.Deserialize<WirePattern>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            error = "throughHolePatternJson-invalid-json";
            return false;
        }

        if (wire is null || string.IsNullOrWhiteSpace(wire.Name))
        {
            error = "throughHolePatternJson-name-required";
            return false;
        }

        if (!TryPositiveFinite(wire.DiameterMillimeters, out double diameter))
        {
            error = "throughHolePatternJson-diameter-invalid";
            return false;
        }

        if (wire.Centers is null || wire.Centers.Count is < 1 or > MaximumHoleCount)
        {
            error = "throughHolePatternJson-centers-count-invalid";
            return false;
        }

        var centers = ImmutableArray.CreateBuilder<Coordinate2D>(wire.Centers.Count);
        foreach (WirePoint point in wire.Centers)
        {
            if (!TryFinite(point.XMillimeters, out double x) || !TryFinite(point.YMillimeters, out double y))
            {
                error = "throughHolePatternJson-center-invalid";
                return false;
            }

            centers.Add(new Coordinate2D(Length.FromMillimeters(x), Length.FromMillimeters(y)));
        }

        pattern = new ThroughHolePatternRequest
        {
            Name = wire.Name.Trim(),
            Diameter = Length.FromMillimeters(diameter),
            Centers = centers.ToImmutable(),
        };
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

    private sealed class WirePattern
    {
        public string? Name { get; init; }
        public double? DiameterMillimeters { get; init; }
        public List<WirePoint>? Centers { get; init; }
    }

    private sealed class WirePoint
    {
        public double? XMillimeters { get; init; }
        public double? YMillimeters { get; init; }
    }
}
