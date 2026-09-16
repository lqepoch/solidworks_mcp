using System.Text.Json;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.UnitTests;

/// <summary>
/// Locks the canonical unit contract before any provider or drawing compiler code is added.
/// </summary>
/// <remarks>
/// 这些测试不是“数学小数测试”，而是防止 CAD 中最昂贵的错误：米/毫米和弧度/角度被静默混用。
/// </remarks>
public sealed class EngineeringUnitsTests
{
    /// <summary>Verifies the millimetre/metre conversion at values that expose a 1000x mistake.</summary>
    [Theory]
    [InlineData(0d)]
    [InlineData(1d)]
    [InlineData(12.7d)]
    [InlineData(1000d)]
    [InlineData(-25.4d)]
    public void LengthConvertsMillimetresAndMetresWithoutScaleDrift(double millimeters)
    {
        var length = Length.FromMillimeters(millimeters);

        Assert.Equal(millimeters, length.Millimeters, 12);
        Assert.Equal(millimeters / 1000d, length.ToMeters(), 12);
        Assert.Equal(millimeters, Length.FromMeters(length.ToMeters()).Millimeters, 10);
    }

    /// <summary>Verifies radians/degrees with quarter-turn, half-turn and signed angles.</summary>
    [Theory]
    [InlineData(0d, 0d)]
    [InlineData(90d, 1.5707963267948966d)]
    [InlineData(180d, 3.141592653589793d)]
    [InlineData(-45d, -0.7853981633974483d)]
    public void AngleConvertsDegreesAndRadiansWithoutUnitConfusion(double degrees, double radians)
    {
        var angle = Angle.FromDegrees(degrees);

        Assert.Equal(degrees, angle.Degrees, 12);
        Assert.Equal(radians, angle.ToRadians(), 12);
        Assert.Equal(degrees, Angle.FromRadians(radians).Degrees, 12);
    }

    /// <summary>Verifies squared and cubed conversion factors instead of accidentally using the linear factor.</summary>
    [Fact]
    public void AreaAndVolumeUseSquaredAndCubedFactors()
    {
        Assert.Equal(1_000_000d, Area.FromSquareMeters(1d).SquareMillimeters, 12);
        Assert.Equal(1d, Area.FromSquareMillimeters(1_000_000d).ToSquareMeters(), 12);
        Assert.Equal(1_000_000_000d, Volume.FromCubicMeters(1d).CubicMillimeters, 12);
        Assert.Equal(1d, Volume.FromCubicMillimeters(1_000_000_000d).ToCubicMeters(), 12);
    }

    /// <summary>Rejects values that cannot be represented as physical quantities.</summary>
    [Fact]
    public void PhysicalQuantitiesRejectNaNInfinityAndNegativeMassAreaVolume()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Length.FromMillimeters(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Angle.FromRadians(double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => Mass.FromKilograms(-0.001d));
        Assert.Throws<ArgumentOutOfRangeException>(() => Area.FromSquareMillimeters(-1d));
        Assert.Throws<ArgumentOutOfRangeException>(() => Volume.FromCubicMillimeters(double.NegativeInfinity));
    }

    /// <summary>Verifies tolerance and limit invariants so impossible manufacturing bands fail early.</summary>
    [Fact]
    public void ToleranceAndLimitsRejectReversedBands()
    {
        Assert.Throws<ArgumentException>(() => new Tolerance(Length.FromMillimeters(0.2d), Length.FromMillimeters(-0.2d)));
        Assert.Throws<ArgumentException>(() => new DimensionLimits(Length.FromMillimeters(10d), Length.FromMillimeters(9.9d)));

        var symmetric = Tolerance.Symmetric(Length.FromMillimeters(-0.25d));
        Assert.Equal(-0.25d, symmetric.LowerDeviation.Millimeters, 12);
        Assert.Equal(0.25d, symmetric.UpperDeviation.Millimeters, 12);
    }

    /// <summary>Ensures dimensional coordinates cannot be assembled from unlabelled scalar fields.</summary>
    [Fact]
    public void CoordinatesExposeExplicitLengthComponents()
    {
        var coordinate = new Coordinate3D(
            Length.FromMillimeters(1d),
            Length.FromMillimeters(2d),
            Length.FromMillimeters(3d));

        Assert.Equal(1d, coordinate.X.Millimeters);
        Assert.Equal(2d, coordinate.Y.Millimeters);
        Assert.Equal(3d, coordinate.Z.Millimeters);
    }

    /// <summary>Confirms JSON keeps the explicit unit property rather than silently flattening it to an unlabelled number.</summary>
    [Fact]
    public void LengthJsonRoundTripPreservesCanonicalUnit()
    {
        string json = JsonSerializer.Serialize(Length.FromMillimeters(25.4d));
        var roundTrip = JsonSerializer.Deserialize<Length>(json);

        Assert.Contains("Millimeters", json, StringComparison.Ordinal);
        Assert.Equal(25.4d, roundTrip.Millimeters, 12);
    }
}
