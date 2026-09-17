using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Provider.Fake;

/// <summary>
/// Builds privacy-safe, deterministic evidence for an initial sketch profile.
/// 为初始草图 profile 生成不泄露源图纸内容且可重复的证据。
/// </summary>
/// <remarks>
/// The FakeCad provider must not merely accept a profile and then ignore it.  The canonical geometry summary is
/// therefore emitted on create/inspect results and included in the document state material.  Only primitive kinds,
/// counts and a hash are exposed; raw drawing values are never copied into test output.
/// FakeCad 不能“接收但忽略” profile，因此创建/检查结果和文档状态材料都会包含规范化几何摘要。对外只暴露
/// primitive 类型、数量和 hash，不把源图纸的原始数值写入测试输出。
/// </remarks>
internal static class FakeCadSketchProfileEvidence
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>Returns the shared validation reason, or null for a valid profile.</summary>
    public static string? Validate(SketchProfileRequest? profile) => SketchProfileValidation.Validate(profile);

    /// <summary>Creates observations for a valid non-null profile.</summary>
    public static EvidenceObservation[] AcceptedObservations(SketchProfileRequest profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        (int lineCount, int arcCount) = CountPrimitives(profile);
        return
        [
            new("initial-sketch-profile.status", "accepted"),
            new("initial-sketch-profile.validation", "connected-closed-loop"),
            new("initial-sketch-profile.segment-count", profile.Segments.Length.ToString(Invariant)),
            new("initial-sketch-profile.line-count", lineCount.ToString(Invariant)),
            new("initial-sketch-profile.three-point-arc-count", arcCount.ToString(Invariant)),
            new("initial-sketch-profile.geometry-hash", GeometryHash(profile)),
        ];
    }

    /// <summary>Creates safe observations for a rejected profile.</summary>
    public static EvidenceObservation[] RejectedObservations(string reason) =>
    [
        new("initial-sketch-profile.status", "rejected"),
        new("initial-sketch-profile.validation", reason),
    ];

    /// <summary>Returns state material that proves a valid profile participates in the document identity.</summary>
    public static string StateToken(SketchProfileRequest? profile) => profile is null
        ? "initial-sketch-profile:none"
        : $"initial-sketch-profile:{GeometryHash(profile)}";

    private static (int LineCount, int ArcCount) CountPrimitives(SketchProfileRequest profile)
    {
        int lineCount = 0;
        int arcCount = 0;
        foreach (SketchCurveRequest segment in profile.Segments)
        {
            switch (segment.Kind)
            {
                case SketchCurveKind.Line:
                    lineCount++;
                    break;
                case SketchCurveKind.ThreePointArc:
                    arcCount++;
                    break;
            }
        }

        return (lineCount, arcCount);
    }

    private static string GeometryHash(SketchProfileRequest profile)
    {
        var canonical = new StringBuilder();
        canonical.Append("segments=").Append(profile.Segments.Length.ToString(Invariant));
        foreach (SketchCurveRequest segment in profile.Segments)
        {
            canonical.Append(";kind=").Append(((int)segment.Kind).ToString(Invariant));
            AppendPoint(canonical, "start", segment.Start);
            AppendPoint(canonical, "through", segment.Through);
            AppendPoint(canonical, "end", segment.End);
        }

        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return $"sha256:{Convert.ToHexString(digest).ToLowerInvariant()}";
    }

    private static void AppendPoint(StringBuilder canonical, string name, Coordinate2D point)
    {
        canonical.Append(';').Append(name).Append(".x=")
            .Append(point.X.Millimeters.ToString("G17", Invariant));
        canonical.Append(';').Append(name).Append(".y=")
            .Append(point.Y.Millimeters.ToString("G17", Invariant));
    }
}
