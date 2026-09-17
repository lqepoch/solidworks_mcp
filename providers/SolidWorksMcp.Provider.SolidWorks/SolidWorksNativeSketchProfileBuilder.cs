using SolidWorks.Interop.sldworks;
using SolidWorksMcp.CadAbstractions;

namespace SolidWorksMcp.Provider.SolidWorks;

/// <summary>
/// Creates a validated vendor-neutral sketch profile on the currently active SOLIDWORKS sketch.
/// 在当前激活的 SOLIDWORKS 草图中创建已经验证的 vendor-neutral profile。
/// </summary>
/// <remarks>
/// This class is intentionally limited to primitive creation. It does not decide dimensions, feature intent, or
/// manufacturing meaning; those remain in the engineering layers. 该类只负责 primitive 创建，不决定尺寸、Feature
/// 意图或制造语义；这些职责仍属于工程语义层。
/// </remarks>
internal static class SolidWorksNativeSketchProfileBuilder
{
    /// <summary>
    /// Creates every requested curve and returns a stable failure reason instead of inventing missing geometry.
    /// 创建所有请求曲线；失败时返回稳定原因，不擅自补齐缺失几何。
    /// </summary>
    public static string? TryCreate(ModelDoc2 model, SketchProfileRequest profile)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(profile);

        string? validationError = SketchProfileValidation.Validate(profile);
        if (validationError is not null)
        {
            return validationError;
        }

        for (int index = 0; index < profile.Segments.Length; index++)
        {
            SketchCurveRequest curve = profile.Segments[index];
            SketchSegment? created = curve.Kind switch
            {
                SketchCurveKind.Line => model.SketchManager.CreateLine(
                    curve.Start.X.ToMeters(),
                    curve.Start.Y.ToMeters(),
                    0d,
                    curve.End.X.ToMeters(),
                    curve.End.Y.ToMeters(),
                    0d),
                SketchCurveKind.ThreePointArc => model.SketchManager.Create3PointArc(
                    curve.Start.X.ToMeters(),
                    curve.Start.Y.ToMeters(),
                    0d,
                    curve.End.X.ToMeters(),
                    curve.End.Y.ToMeters(),
                    0d,
                    curve.Through.X.ToMeters(),
                    curve.Through.Y.ToMeters(),
                    0d),
                _ => null,
            };

            if (created is null)
            {
                return $"segment-{index}-native-creation-returned-null";
            }

            // The sketch now owns the native curve; release only this temporary RCW on the provider STA.
            // 草图已经接管 native curve；这里只在 Provider STA 释放临时 RCW。
            SolidWorksDocumentRouting.Release(created);
        }

        return null;
    }
}
