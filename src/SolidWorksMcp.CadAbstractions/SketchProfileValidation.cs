namespace SolidWorksMcp.CadAbstractions;

/// <summary>
/// Validates a structured planar sketch before any provider-specific geometry is created.
/// 在 Provider 创建任何厂商几何之前验证结构化平面草图。
/// </summary>
/// <remarks>
/// The validator deliberately checks connectivity and primitive validity only. Self-intersection, manufacturability
/// and feature intent belong to the EngineeringModel/RuleEngine layers; silently repairing any of those here would
/// make the resulting CAD model differ from the requested design. Validator 只检查连接性和 primitive 合法性；自交、
/// 可制造性和工程意图属于 EngineeringModel/RuleEngine，不能在此处静默修复而改变设计。
/// </remarks>
public static class SketchProfileValidation
{
    /// <summary>Maximum endpoint gap accepted when a profile is expressed in canonical millimetres.</summary>
    private const double ConnectionToleranceMillimeters = 1e-6d;

    /// <summary>Returns a stable reason when the profile is invalid, or null when it is valid.</summary>
    public static string? Validate(SketchProfileRequest? profile)
    {
        if (profile is null)
        {
            return "profile-required";
        }

        if (profile.Segments.Length < 3)
        {
            return "at-least-three-segments-required";
        }

        for (int index = 0; index < profile.Segments.Length; index++)
        {
            SketchCurveRequest segment = profile.Segments[index];
            if (!IsFinite(segment.Start) || !IsFinite(segment.End) || !IsFinite(segment.Through))
            {
                return $"segment-{index}-contains-non-finite-coordinate";
            }

            if (DistanceSquared(segment.Start, segment.End) <= ConnectionToleranceMillimeters * ConnectionToleranceMillimeters)
            {
                return $"segment-{index}-has-zero-length";
            }

            if (segment.Kind is not SketchCurveKind.Line and not SketchCurveKind.ThreePointArc)
            {
                return $"segment-{index}-kind-unsupported";
            }

            if (segment.Kind == SketchCurveKind.ThreePointArc
                && Math.Abs(Cross(segment.Start, segment.Through, segment.End))
                    <= ConnectionToleranceMillimeters * ConnectionToleranceMillimeters)
            {
                return $"segment-{index}-arc-points-collinear";
            }

            SketchCurveRequest next = profile.Segments[(index + 1) % profile.Segments.Length];
            if (DistanceSquared(segment.End, next.Start)
                > ConnectionToleranceMillimeters * ConnectionToleranceMillimeters)
            {
                return $"segment-{index}-is-disconnected";
            }
        }

        return null;
    }

    /// <summary>Returns true when a profile passes validation.</summary>
    public static bool IsValid(SketchProfileRequest? profile) => Validate(profile) is null;

    private static bool IsFinite(SolidWorksMcp.Protocol.Coordinate2D point) =>
        double.IsFinite(point.X.Millimeters) && double.IsFinite(point.Y.Millimeters);

    private static double DistanceSquared(
        SolidWorksMcp.Protocol.Coordinate2D first,
        SolidWorksMcp.Protocol.Coordinate2D second)
    {
        double dx = first.X.Millimeters - second.X.Millimeters;
        double dy = first.Y.Millimeters - second.Y.Millimeters;
        return (dx * dx) + (dy * dy);
    }

    private static double Cross(
        SolidWorksMcp.Protocol.Coordinate2D start,
        SolidWorksMcp.Protocol.Coordinate2D through,
        SolidWorksMcp.Protocol.Coordinate2D end)
    {
        double firstX = through.X.Millimeters - start.X.Millimeters;
        double firstY = through.Y.Millimeters - start.Y.Millimeters;
        double secondX = end.X.Millimeters - start.X.Millimeters;
        double secondY = end.Y.Millimeters - start.Y.Millimeters;
        return (firstX * secondY) - (firstY * secondX);
    }
}
