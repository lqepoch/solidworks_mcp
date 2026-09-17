using System.Collections.Immutable;
using System.Globalization;
using SolidWorksMcp.CadAbstractions;

namespace SolidWorksMcp.AutoDrawing;

/// <summary>
/// The deterministic semantic compression result for one repeated feature group.
/// 一个重复特征组的确定性工程语义压缩结果。
/// </summary>
/// <remarks>
/// This type is deliberately vendor-neutral. It describes what the drawing must say; the provider later decides how
/// to persist the annotation in the native drawing file. 该类型刻意保持 vendor-neutral：这里只决定图纸应表达什么，
/// 后续由 Provider 决定如何把标注写入 native drawing 文件。
/// </remarks>
public sealed record RepeatedFeatureCalloutPlan
{
    /// <summary>Stable source feature/group name.</summary>
    public required string FeatureName { get; init; }

    /// <summary>Compact deterministic callout text, for example <c>2X Ø6 THRU; PITCH 20; SYMMETRIC</c>.</summary>
    public required string Text { get; init; }

    /// <summary>Detected distribution classification used in evidence and explain output.</summary>
    public required string Distribution { get; init; }

    /// <summary>Stable coverage keys proving quantity, size and pattern semantics.</summary>
    public ImmutableArray<string> CoverageKeys { get; init; } = [];

    /// <summary>Short deterministic explanation of why the compact form was selected.</summary>
    public required string Rationale { get; init; }
}

/// <summary>
/// Plans compact engineering notation for a repeated through-hole group without duplicating one diameter per hole.
/// 为重复通孔组规划紧凑工程标注，避免为每个孔重复生成一个直径标注。
/// </summary>
public static class RepeatedFeatureCalloutPlanner
{
    private const double GeometryToleranceMillimeters = 0.000001d;

    /// <summary>
    /// Detects linear, circular and mirror evidence only when the supplied centers prove it numerically.
    /// 只有输入中心点在数值上证明了线性、圆周或对称关系时，才生成对应语义。
    /// </summary>
    public static RepeatedFeatureCalloutPlan Plan(ThroughHolePatternRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("A repeated-feature name is required.", nameof(request));
        }

        if (request.Diameter.Millimeters <= 0d || !double.IsFinite(request.Diameter.Millimeters))
        {
            throw new ArgumentException("A repeated-feature diameter must be finite and positive.", nameof(request));
        }

        if (request.Centers.IsDefaultOrEmpty)
        {
            throw new ArgumentException("At least one repeated-feature center is required.", nameof(request));
        }

        ImmutableArray<Coordinate> coordinates =
        [
            .. request.Centers.Select(point => new Coordinate(point.X.Millimeters, point.Y.Millimeters)),
        ];
        LinearEvidence? linear = DetectLinear(coordinates);
        CircularEvidence? circular = DetectCircular(coordinates);
        bool originSymmetry = DetectOriginSymmetry(coordinates);

        string distribution;
        string distributionText;
        var coverage = ImmutableArray.CreateBuilder<string>();
        string featureKey = NormalizeKey(request.Name);
        coverage.Add($"feature.{featureKey}.quantity");
        coverage.Add($"feature.{featureKey}.size");
        coverage.Add($"feature.{featureKey}.depth");

        if (linear is not null)
        {
            distribution = $"linear-{linear.Value.Axis}";
            distributionText = $"PITCH {FormatMillimeters(linear.Value.PitchMillimeters)}";
            coverage.Add($"feature.{featureKey}.linear.pitch");
            coverage.Add($"feature.{featureKey}.linear.equal-spacing");
        }
        else if (circular is not null)
        {
            distribution = "circular";
            distributionText = $"PCD {FormatMillimeters(circular.Value.PcdMillimeters)}; EQ SP";
            coverage.Add($"feature.{featureKey}.circular.pcd");
            coverage.Add($"feature.{featureKey}.circular.equal-distribution");
        }
        else
        {
            distribution = "explicit-centers";
            distributionText = "EXPLICIT CENTERS";
            coverage.Add($"feature.{featureKey}.explicit-centers");
        }

        if (originSymmetry)
        {
            distributionText += "; SYMMETRIC";
            coverage.Add($"feature.{featureKey}.symmetry");
        }

        string text = string.Concat(
            request.Centers.Length.ToString(CultureInfo.InvariantCulture),
            "X Ø",
            FormatMillimeters(request.Diameter.Millimeters),
            " THRU; ",
            distributionText);
        return new RepeatedFeatureCalloutPlan
        {
            FeatureName = request.Name.Trim(),
            Text = text,
            Distribution = distribution,
            CoverageKeys = coverage.ToImmutable(),
            Rationale = linear is not null
                ? "Equal linear spacing was proven from ordered center coordinates; quantity and pitch are emitted once."
                : circular is not null
                    ? "Equal circular spacing was proven from center radii/angles; quantity, PCD and distribution are emitted once."
                    : originSymmetry
                        ? "Explicit centers were retained because no single pitch/angle was proven; origin symmetry is still emitted."
                        : "Explicit centers were retained because no single pitch, angle or symmetry relation was proven.",
        };
    }

    private static LinearEvidence? DetectLinear(ImmutableArray<Coordinate> coordinates)
    {
        if (coordinates.Length < 2)
        {
            return null;
        }

        bool sameX = coordinates.All(point => NearlyEqual(point.X, coordinates[0].X));
        bool sameY = coordinates.All(point => NearlyEqual(point.Y, coordinates[0].Y));
        if (sameX == sameY)
        {
            // A mirrored linear pattern has multiple parallel rows. Prove the common pitch from every row before
            // compressing it; a visually similar but irregular set must remain explicit. 镜像线性阵列可能有多条
            // 平行 row；只有所有 row 共享同一等距序列时才压缩，形状相似但不规则时仍保留 explicit centers。
            if (TryCommonUniformValues(
                    coordinates.GroupBy(point => Math.Round(point.X, 6)).Select(group => group.Select(point => point.Y)),
                    out double mirroredPitchY))
            {
                return new LinearEvidence("Y", mirroredPitchY);
            }

            if (TryCommonUniformValues(
                    coordinates.GroupBy(point => Math.Round(point.Y, 6)).Select(group => group.Select(point => point.X)),
                    out double mirroredPitchX))
            {
                return new LinearEvidence("X", mirroredPitchX);
            }

            return null;
        }

        double[] values = [.. (sameX
                ? coordinates.Select(point => point.Y)
                : coordinates.Select(point => point.X))
            .OrderBy(value => value)];
        double pitch = values[1] - values[0];
        if (pitch <= GeometryToleranceMillimeters)
        {
            return null;
        }

        for (int index = 2; index < values.Length; index++)
        {
            if (!NearlyEqual(values[index] - values[index - 1], pitch))
            {
                return null;
            }
        }

        return new LinearEvidence(sameX ? "Y" : "X", pitch);
    }

    private static bool TryCommonUniformValues(
        IEnumerable<IEnumerable<double>> groupedValues,
        out double pitch)
    {
        pitch = 0d;
        double[][] groups = [.. groupedValues.Select(values => values.OrderBy(value => value).ToArray())];
        if (groups.Length < 2 || groups.Any(group => group.Length < 2) || groups.Select(group => group.Length).Distinct().Count() != 1)
        {
            return false;
        }

        double[] baseline = groups[0];
        pitch = baseline[1] - baseline[0];
        if (pitch <= GeometryToleranceMillimeters)
        {
            return false;
        }

        for (int index = 2; index < baseline.Length; index++)
        {
            if (!NearlyEqual(baseline[index] - baseline[index - 1], pitch))
            {
                return false;
            }
        }

        return groups.Skip(1).All(group => group.Zip(baseline, NearlyEqual).All(equal => equal));
    }

    private static CircularEvidence? DetectCircular(ImmutableArray<Coordinate> coordinates)
    {
        if (coordinates.Length < 3)
        {
            return null;
        }

        double[] radii = [.. coordinates.Select(point => Math.Sqrt((point.X * point.X) + (point.Y * point.Y)))];
        if (radii[0] <= GeometryToleranceMillimeters || radii.Any(radius => !NearlyEqual(radius, radii[0])))
        {
            return null;
        }

        double[] angles = [.. coordinates
            .Select(point => Math.Atan2(point.Y, point.X))
            .OrderBy(angle => angle)];
        double step = (2d * Math.PI) / coordinates.Length;
        for (int index = 1; index < angles.Length; index++)
        {
            if (!NearlyEqual(angles[index] - angles[index - 1], step))
            {
                return null;
            }
        }

        double wraparound = (angles[0] + (2d * Math.PI)) - angles[^1];
        return NearlyEqual(wraparound, step)
            ? new CircularEvidence(2d * radii[0])
            : null;
    }

    private static bool DetectOriginSymmetry(ImmutableArray<Coordinate> coordinates)
    {
        if (coordinates.Length < 2)
        {
            return false;
        }

        return coordinates.All(point => coordinates.Any(candidate =>
            NearlyEqual(candidate.X, -point.X) && NearlyEqual(candidate.Y, -point.Y)));
    }

    private static string NormalizeKey(string value) => string.Concat(
            value.Trim().ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-'))
        .Trim('-');

    private static string FormatMillimeters(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static bool NearlyEqual(double left, double right) =>
        Math.Abs(left - right) <= GeometryToleranceMillimeters;

    private readonly record struct Coordinate(double X, double Y);

    private readonly record struct LinearEvidence(string Axis, double PitchMillimeters);

    private readonly record struct CircularEvidence(double PcdMillimeters);
}
