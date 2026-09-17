using SolidWorksMcp.AutoDrawing;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.UnitTests;

/// <summary>
/// Proves repeated-feature semantics are compressed deterministically instead of becoming one annotation per instance.
/// 验证重复特征会被确定性地压缩，而不是退化成每个实例一条标注。
/// </summary>
public sealed class RepeatedFeatureCalloutPlannerTests
{
    [Fact]
    public void TenHoleLinearPatternWithOriginMirrorProducesOneCompleteCallout()
    {
        ThroughHolePatternRequest request = new()
        {
            Name = "Bracket-Hole-Pattern",
            Diameter = Length.FromMillimeters(12d),
            Centers =
            [
                new Coordinate2D(Length.FromMillimeters(-40d), Length.FromMillimeters(-20d)),
                new Coordinate2D(Length.FromMillimeters(-40d), Length.FromMillimeters(-10d)),
                new Coordinate2D(Length.FromMillimeters(-40d), Length.FromMillimeters(0d)),
                new Coordinate2D(Length.FromMillimeters(-40d), Length.FromMillimeters(10d)),
                new Coordinate2D(Length.FromMillimeters(-40d), Length.FromMillimeters(20d)),
                new Coordinate2D(Length.FromMillimeters(40d), Length.FromMillimeters(20d)),
                new Coordinate2D(Length.FromMillimeters(40d), Length.FromMillimeters(10d)),
                new Coordinate2D(Length.FromMillimeters(40d), Length.FromMillimeters(0d)),
                new Coordinate2D(Length.FromMillimeters(40d), Length.FromMillimeters(-10d)),
                new Coordinate2D(Length.FromMillimeters(40d), Length.FromMillimeters(-20d)),
            ],
        };

        RepeatedFeatureCalloutPlan plan = RepeatedFeatureCalloutPlanner.Plan(request);

        Assert.Equal("linear-Y", plan.Distribution);
        Assert.Equal("10X Ø12 THRU; PITCH 10; SYMMETRIC", plan.Text);
        Assert.Contains("feature.bracket-hole-pattern.quantity", plan.CoverageKeys);
        Assert.Contains("feature.bracket-hole-pattern.linear.pitch", plan.CoverageKeys);
        Assert.Contains("feature.bracket-hole-pattern.symmetry", plan.CoverageKeys);
        Assert.Equal(plan.CoverageKeys.Length, plan.CoverageKeys.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain("instance-01", plan.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void UnprovenDistributionFallsBackToExplicitCentersWithoutInventingPitch()
    {
        ThroughHolePatternRequest request = new()
        {
            Name = "Irregular-Hole-Group",
            Diameter = Length.FromMillimeters(6d),
            Centers =
            [
                new Coordinate2D(Length.FromMillimeters(1d), Length.FromMillimeters(2d)),
                new Coordinate2D(Length.FromMillimeters(9d), Length.FromMillimeters(5d)),
                new Coordinate2D(Length.FromMillimeters(18d), Length.FromMillimeters(7d)),
            ],
        };

        RepeatedFeatureCalloutPlan plan = RepeatedFeatureCalloutPlanner.Plan(request);

        Assert.Equal("explicit-centers", plan.Distribution);
        Assert.Contains("EXPLICIT CENTERS", plan.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("PITCH", plan.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("PCD", plan.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("SYMMETRIC", plan.Text, StringComparison.Ordinal);
    }
}
