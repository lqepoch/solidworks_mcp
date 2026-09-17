using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.UnitTests;

/// <summary>Protects the vendor-neutral closed-profile contract used by reference-driven native fixtures.</summary>
/// <remarks>
/// These tests intentionally use generic geometry classes only. They do not load or encode any confidential drawing
/// values. 这些测试只使用通用几何类别，不读取也不编码任何秘密图纸数值。
/// </remarks>
public sealed class SketchProfileValidationTests
{
    [Fact]
    public void DShapedProfileWithArcIsValid()
    {
        SketchProfileRequest profile = new()
        {
            Segments =
            [
                Line(-20, -20, 20, -20),
                Line(20, -20, 20, 0),
                Arc(20, 0, 0, 25, -20, 0),
                Line(-20, 0, -20, -20),
            ],
        };

        Assert.Null(SketchProfileValidation.Validate(profile));
    }

    [Fact]
    public void DisconnectedProfileIsRejectedInsteadOfBeingSilentlyClosed()
    {
        SketchProfileRequest profile = new()
        {
            Segments =
            [
                Line(-10, -10, 10, -10),
                Line(10, -10, 10, 10),
                Line(10, 10, -10, 11),
            ],
        };

        Assert.Equal("segment-2-is-disconnected", SketchProfileValidation.Validate(profile));
    }

    [Fact]
    public void CollinearThreePointArcIsRejected()
    {
        SketchProfileRequest profile = new()
        {
            Segments =
            [
                Line(0, 0, 10, 0),
                Arc(10, 0, 15, 0, 20, 0),
                Line(20, 0, 0, 0),
            ],
        };

        Assert.Equal("segment-1-arc-points-collinear", SketchProfileValidation.Validate(profile));
    }

    private static SketchCurveRequest Line(double startX, double startY, double endX, double endY) => new()
    {
        Kind = SketchCurveKind.Line,
        Start = Point(startX, startY),
        End = Point(endX, endY),
    };

    private static SketchCurveRequest Arc(
        double startX,
        double startY,
        double throughX,
        double throughY,
        double endX,
        double endY) => new()
        {
            Kind = SketchCurveKind.ThreePointArc,
            Start = Point(startX, startY),
            Through = Point(throughX, throughY),
            End = Point(endX, endY),
        };

    private static Coordinate2D Point(double x, double y) => new(
        Length.FromMillimeters(x),
        Length.FromMillimeters(y));
}
