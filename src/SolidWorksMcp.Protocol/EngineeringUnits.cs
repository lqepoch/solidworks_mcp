using System.Globalization;
using System.Text.Json.Serialization;

namespace SolidWorksMcp.Protocol;

/// <summary>
/// Represents a linear distance in the MCP and engineering-domain canonical unit: millimetres.
/// </summary>
/// <remarks>
/// MCP 与工程语义层统一使用毫米；只有 Provider 的 COM 边界才把它转换为 SOLIDWORKS 要求的米。
/// Keeping the unit in the type name and API prevents accidental 1000x scale errors at module boundaries.
/// </remarks>
public readonly record struct Length
{
    /// <summary>Creates a length from millimetres.</summary>
    /// <param name="millimeters">The finite signed distance in millimetres.</param>
    /// <remarks>这是显式单位构造函数；调用方不得把无单位的尺寸 double 直接传入业务接口。</remarks>
    [JsonConstructor]
    public Length(double millimeters)
    {
        QuantityValidation.RequireFinite(millimeters, nameof(millimeters));
        Millimeters = millimeters;
    }

    /// <summary>Gets the canonical value in millimetres.</summary>
    public double Millimeters { get; }

    /// <summary>Creates a length from a millimetre value.</summary>
    public static Length FromMillimeters(double millimeters) => new(millimeters);

    /// <summary>Creates a length from a metre value, applying exactly 1000 mm per metre.</summary>
    /// <remarks>用于 Provider 边界；该倍率通过单元测试锁定，避免米/毫米陷阱。</remarks>
    public static Length FromMeters(double meters)
    {
        QuantityValidation.RequireFinite(meters, nameof(meters));
        return new(meters * 1000d);
    }

    /// <summary>Converts the canonical millimetre value to metres for the SOLIDWORKS boundary.</summary>
    public double ToMeters() => Millimeters / 1000d;

    /// <summary>Returns a deterministic diagnostic representation including its unit.</summary>
    public override string ToString() => $"{Millimeters.ToString("G17", CultureInfo.InvariantCulture)} mm";
}

/// <summary>Represents a plane or rotational angle in degrees.</summary>
/// <remarks>工程/MCP 层使用度；SOLIDWORKS API 的弧度转换只能发生在 Provider 边界。</remarks>
public readonly record struct Angle
{
    /// <summary>Creates an angle from degrees.</summary>
    /// <param name="degrees">The finite signed angle in degrees.</param>
    [JsonConstructor]
    public Angle(double degrees)
    {
        QuantityValidation.RequireFinite(degrees, nameof(degrees));
        Degrees = degrees;
    }

    /// <summary>Gets the canonical angle in degrees.</summary>
    public double Degrees { get; }

    /// <summary>Creates an angle from degrees.</summary>
    public static Angle FromDegrees(double degrees) => new(degrees);

    /// <summary>Creates an angle from radians using the exact .NET trigonometric conversion.</summary>
    /// <remarks>该转换专门覆盖 SOLIDWORKS 弧度 API，禁止在调用方自行近似 PI。</remarks>
    public static Angle FromRadians(double radians)
    {
        QuantityValidation.RequireFinite(radians, nameof(radians));
        return new(radians * (180d / Math.PI));
    }

    /// <summary>Converts degrees to radians for a Provider/COM call.</summary>
    public double ToRadians() => Degrees * (Math.PI / 180d);

    /// <summary>Returns a deterministic diagnostic representation including its unit.</summary>
    public override string ToString() => $"{Degrees.ToString("G17", CultureInfo.InvariantCulture)} deg";
}

/// <summary>Represents mass in kilograms, the SI mass unit used by SOLIDWORKS measurements.</summary>
/// <remarks>界面可显示克，但内部契约固定使用千克，避免单位混淆。</remarks>
public readonly record struct Mass
{
    /// <summary>Creates a non-negative mass from kilograms.</summary>
    [JsonConstructor]
    public Mass(double kilograms)
    {
        QuantityValidation.RequireNonNegativeFinite(kilograms, nameof(kilograms));
        Kilograms = kilograms;
    }

    /// <summary>Gets the canonical mass in kilograms.</summary>
    public double Kilograms { get; }

    /// <summary>Creates a mass from kilograms.</summary>
    public static Mass FromKilograms(double kilograms) => new(kilograms);

    /// <summary>Creates a mass from grams using exactly 1000 g per kg.</summary>
    public static Mass FromGrams(double grams)
    {
        QuantityValidation.RequireNonNegativeFinite(grams, nameof(grams));
        return new(grams / 1000d);
    }

    /// <summary>Converts the canonical value to grams for presentation or report export.</summary>
    public double ToGrams() => Kilograms * 1000d;
}

/// <summary>Represents area in square millimetres.</summary>
/// <remarks>Provider conversion to square metres uses a 1,000,000 factor and is tested explicitly.</remarks>
public readonly record struct Area
{
    /// <summary>Creates a non-negative area from square millimetres.</summary>
    [JsonConstructor]
    public Area(double squareMillimeters)
    {
        QuantityValidation.RequireNonNegativeFinite(squareMillimeters, nameof(squareMillimeters));
        SquareMillimeters = squareMillimeters;
    }

    /// <summary>Gets the canonical area in square millimetres.</summary>
    public double SquareMillimeters { get; }

    /// <summary>Creates an area from square millimetres.</summary>
    public static Area FromSquareMillimeters(double squareMillimeters) => new(squareMillimeters);

    /// <summary>Creates an area from square metres using the squared metre-to-millimetre factor.</summary>
    public static Area FromSquareMeters(double squareMeters)
    {
        QuantityValidation.RequireNonNegativeFinite(squareMeters, nameof(squareMeters));
        return new(squareMeters * 1_000_000d);
    }

    /// <summary>Converts the canonical value to square metres.</summary>
    public double ToSquareMeters() => SquareMillimeters / 1_000_000d;
}

/// <summary>Represents volume in cubic millimetres.</summary>
/// <remarks>Provider conversion to cubic metres uses a 1,000,000,000 factor.</remarks>
public readonly record struct Volume
{
    /// <summary>Creates a non-negative volume from cubic millimetres.</summary>
    [JsonConstructor]
    public Volume(double cubicMillimeters)
    {
        QuantityValidation.RequireNonNegativeFinite(cubicMillimeters, nameof(cubicMillimeters));
        CubicMillimeters = cubicMillimeters;
    }

    /// <summary>Gets the canonical volume in cubic millimetres.</summary>
    public double CubicMillimeters { get; }

    /// <summary>Creates a volume from cubic millimetres.</summary>
    public static Volume FromCubicMillimeters(double cubicMillimeters) => new(cubicMillimeters);

    /// <summary>Creates a volume from cubic metres using the cubed metre-to-millimetre factor.</summary>
    public static Volume FromCubicMeters(double cubicMeters)
    {
        QuantityValidation.RequireNonNegativeFinite(cubicMeters, nameof(cubicMeters));
        return new(cubicMeters * 1_000_000_000d);
    }

    /// <summary>Converts the canonical value to cubic metres.</summary>
    public double ToCubicMeters() => CubicMillimeters / 1_000_000_000d;
}

/// <summary>Describes signed lower and upper deviations from a nominal dimension.</summary>
/// <remarks>
/// 下偏差和上偏差都相对于名义值；允许负下偏差和正上偏差，但要求 lower 不大于 upper。
/// </remarks>
public readonly record struct Tolerance
{
    /// <summary>Creates a tolerance band from signed lower and upper deviations.</summary>
    [JsonConstructor]
    public Tolerance(Length lowerDeviation, Length upperDeviation)
    {
        if (lowerDeviation.Millimeters > upperDeviation.Millimeters)
        {
            throw new ArgumentException("Lower deviation must not exceed upper deviation.", nameof(lowerDeviation));
        }

        LowerDeviation = lowerDeviation;
        UpperDeviation = upperDeviation;
    }

    /// <summary>Gets the signed lower deviation from nominal.</summary>
    public Length LowerDeviation { get; }

    /// <summary>Gets the signed upper deviation from nominal.</summary>
    public Length UpperDeviation { get; }

    /// <summary>Creates a symmetric plus/minus tolerance.</summary>
    public static Tolerance Symmetric(Length halfWidth)
    {
        double absoluteWidth = Math.Abs(halfWidth.Millimeters);
        return new(Length.FromMillimeters(-absoluteWidth), Length.FromMillimeters(absoluteWidth));
    }
}

/// <summary>Represents a closed dimensional interval in canonical length units.</summary>
public readonly record struct DimensionLimits
{
    /// <summary>Creates inclusive minimum and maximum limits.</summary>
    [JsonConstructor]
    public DimensionLimits(Length minimum, Length maximum)
    {
        if (minimum.Millimeters > maximum.Millimeters)
        {
            throw new ArgumentException("Minimum must not exceed maximum.", nameof(minimum));
        }

        Minimum = minimum;
        Maximum = maximum;
    }

    /// <summary>Gets the inclusive functional/manufacturing minimum.</summary>
    public Length Minimum { get; }

    /// <summary>Gets the inclusive functional/manufacturing maximum.</summary>
    public Length Maximum { get; }
}

/// <summary>Represents a two-dimensional coordinate whose components are lengths.</summary>
public readonly record struct Coordinate2D
{
    /// <summary>Creates a coordinate from explicit length components.</summary>
    [JsonConstructor]
    public Coordinate2D(Length x, Length y)
    {
        X = x;
        Y = y;
    }

    /// <summary>Gets the horizontal component.</summary>
    public Length X { get; }

    /// <summary>Gets the vertical component.</summary>
    public Length Y { get; }
}

/// <summary>Represents a three-dimensional coordinate whose components are lengths.</summary>
public readonly record struct Coordinate3D
{
    /// <summary>Creates a coordinate from explicit length components.</summary>
    [JsonConstructor]
    public Coordinate3D(Length x, Length y, Length z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>Gets the X component.</summary>
    public Length X { get; }

    /// <summary>Gets the Y component.</summary>
    public Length Y { get; }

    /// <summary>Gets the Z component.</summary>
    public Length Z { get; }
}

/// <summary>
/// Represents a finite normalized score in the inclusive range [0, 1].
/// 表示 [0, 1] 闭区间内的有限归一化评分，避免工程规划 API 暴露无语义的裸 double。
/// </summary>
public readonly record struct NormalizedScore : IComparable<NormalizedScore>
{
    /// <summary>Creates a score and rejects NaN, infinity and out-of-range values.</summary>
    [JsonConstructor]
    public NormalizedScore(double value)
    {
        QuantityValidation.RequireFinite(value, nameof(value));
        if (value is < 0d or > 1d)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "A normalized score must be in [0, 1].");
        }

        Value = value;
    }

    /// <summary>Gets the normalized scalar for internal ordering or numeric reporting.</summary>
    public double Value { get; }

    /// <summary>Creates a normalized score from a finite ratio.</summary>
    public static NormalizedScore FromRatio(double value) => new(value);

    /// <summary>
    /// Combines the fixed planner evidence weights: coverage 50%, readability 30%, exposure 20%.
    /// 使用固定规划权重合并证据：覆盖度 50%、可读性 30%、制造特征暴露 20%。
    /// </summary>
    public static NormalizedScore FromPlanningEvidence(
        NormalizedScore coverage,
        NormalizedScore readability,
        NormalizedScore manufacturingExposure) =>
        new((coverage.Value * 0.50) + (readability.Value * 0.30) + (manufacturingExposure.Value * 0.20));

    /// <summary>Compares scores for deterministic candidate ordering.</summary>
    public int CompareTo(NormalizedScore other) => Value.CompareTo(other.Value);

    /// <summary>Orders two normalized scores without exposing an untyped scalar at the engineering boundary.</summary>
    public static bool operator <(NormalizedScore left, NormalizedScore right) => left.Value < right.Value;

    /// <summary>Orders two normalized scores without exposing an untyped scalar at the engineering boundary.</summary>
    public static bool operator <=(NormalizedScore left, NormalizedScore right) => left.Value <= right.Value;

    /// <summary>Orders two normalized scores without exposing an untyped scalar at the engineering boundary.</summary>
    public static bool operator >(NormalizedScore left, NormalizedScore right) => left.Value > right.Value;

    /// <summary>Orders two normalized scores without exposing an untyped scalar at the engineering boundary.</summary>
    public static bool operator >=(NormalizedScore left, NormalizedScore right) => left.Value >= right.Value;
}

/// <summary>Centralizes validation so every dimensional value rejects NaN and infinity consistently.</summary>
internal static class QuantityValidation
{
    /// <summary>Rejects non-finite floating point values at the explicit-unit boundary.</summary>
    public static void RequireFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The value must be finite.");
        }
    }

    /// <summary>Rejects non-finite and negative physical quantities.</summary>
    public static void RequireNonNegativeFinite(double value, string parameterName)
    {
        RequireFinite(value, parameterName);
        if (value < 0d)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The value must be non-negative.");
        }
    }
}
