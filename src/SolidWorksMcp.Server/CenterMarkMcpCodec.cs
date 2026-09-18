using System.Text.Json;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Server;

/// <summary>
/// Parses an approval-gated, bounded native center-mark request.
/// 解析一个经过审批门禁且有边界的 native center-mark 请求。
/// </summary>
/// <remarks>
/// The wire contract expresses engineering intent and an exact drawing view. It does not expose SOLIDWORKS selection
/// marks or COM enums. The native provider performs the view activation and count/read-back proof. wire contract 只表达
/// 工程意图和精确 drawing view，不暴露 SOLIDWORKS selection mark 或 COM enum；native Provider 负责 view activation
/// 以及数量/读回证明。
/// </remarks>
internal static class CenterMarkMcpCodec
{
    private const int MaximumJsonLength = 32 * 1024;
    private const int MaximumIdentityLength = 256;
    private const int MaximumTextLength = 256;
    private const int MaximumCoverageKeyLength = 256;
    private const int MaximumCoverageKeyCount = 64;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        MaxDepth = 8,
    };

    /// <summary>Parses the optional center-mark JSON before any CAD session is opened.</summary>
    /// <summary>在打开 CAD session 之前解析可选 center-mark JSON。</summary>
    public static bool TryParse(
        string? json,
        out DrawingCenterMarkRequest? request,
        out string? error)
    {
        request = null;
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        if (json.Length > MaximumJsonLength)
        {
            error = "centerMarkJson-too-large";
            return false;
        }

        WireCenterMark? wire;
        try
        {
            wire = JsonSerializer.Deserialize<WireCenterMark>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            error = "centerMarkJson-invalid-json";
            return false;
        }

        if (wire is null
            || !TryIdentity(wire.AnnotationId, out string annotationId)
            || !TryIdentity(wire.ViewId, out string viewId)
            || !TryText(wire.ProvenanceKind, out string provenanceKind)
            || !TryText(wire.ProvenanceMethod, out string provenanceMethod))
        {
            error = "centerMarkJson-identity-or-provenance-required";
            return false;
        }

        if (!TryFlags(wire.Target, DrawingCenterMarkTarget.Holes, out DrawingCenterMarkTarget target)
            || target is DrawingCenterMarkTarget.None)
        {
            error ??= "Target is not a supported center-mark target.";
            return false;
        }

        if (!TryFlags(wire.ConnectionLines, DrawingCenterMarkConnectionLines.None, out DrawingCenterMarkConnectionLines connectionLines))
        {
            error = "ConnectionLines contains an unsupported center-mark flag.";
            return false;
        }

        if (!TryEnum(wire.ApprovalState, nameof(wire.ApprovalState), null, out DrawingAnnotationApprovalState approvalState, out error))
        {
            return false;
        }

        if (approvalState is not (DrawingAnnotationApprovalState.Approved or DrawingAnnotationApprovalState.Released))
        {
            error = "centerMarkJson-approval-required";
            return false;
        }

        double size = wire.SizeMillimeters ?? 3d;
        double gap = wire.GapMillimeters ?? 0.5d;
        int minimum = wire.MinimumNewMarks ?? 1;
        if (!double.IsFinite(size) || size < 0d
            || !double.IsFinite(gap) || gap < 0d
            || minimum is < 1 or > 64)
        {
            error = "centerMarkJson-display-values-invalid";
            return false;
        }

        string[] coverageKeys = wire.CoverageKeys?.Select(value => value?.Trim() ?? string.Empty).ToArray() ?? [];
        if (coverageKeys.Length > MaximumCoverageKeyCount
            || coverageKeys.Any(value => value.Length is 0 or > MaximumCoverageKeyLength
                || value.Any(char.IsControl)))
        {
            error = "centerMarkJson-coverage-keys-invalid";
            return false;
        }

        request = new DrawingCenterMarkRequest
        {
            RequestedAnnotationId = new AnnotationId(annotationId),
            ViewId = new ViewId(viewId),
            Target = target,
            ConnectionLines = connectionLines,
            LinearSlotCenter = wire.LinearSlotCenter ?? true,
            ArcSlotCenter = wire.ArcSlotCenter ?? true,
            UseDocumentDefaults = wire.UseDocumentDefaults ?? true,
            Size = Length.FromMillimeters(size),
            Gap = Length.FromMillimeters(gap),
            ExtendedLines = wire.ExtendedLines ?? false,
            CenterLineFont = wire.CenterLineFont ?? true,
            MinimumNewMarks = minimum,
            ProvenanceKind = provenanceKind,
            ProvenanceMethod = provenanceMethod,
            ApprovalState = approvalState,
            CoverageKeys = [.. coverageKeys],
        };
        return true;
    }

    private static bool TryIdentity(string? value, out string normalized)
    {
        normalized = value?.Trim() ?? string.Empty;
        return normalized.Length is > 0 and <= MaximumIdentityLength
            && normalized.All(character => !char.IsControl(character));
    }

    private static bool TryText(string? value, out string normalized)
    {
        normalized = value?.Trim() ?? string.Empty;
        return normalized.Length is > 0 and <= MaximumTextLength
            && normalized.All(character => !char.IsControl(character));
    }

    private static bool TryEnum<TEnum>(
        string? value,
        string name,
        TEnum? defaultValue,
        out TEnum result,
        out string? error)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value) && defaultValue.HasValue)
        {
            result = defaultValue.Value;
            error = null;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(value)
            && Enum.TryParse(value.Trim(), ignoreCase: true, out result)
            && Enum.IsDefined(result))
        {
            error = null;
            return true;
        }

        result = default;
        error = $"{name} is not supported.";
        return false;
    }

    private static bool TryFlags<TEnum>(string? value, TEnum defaultValue, out TEnum result)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = defaultValue;
            return true;
        }

        if (!Enum.TryParse(value.Trim(), ignoreCase: true, out result))
        {
            return false;
        }

        ulong raw = Convert.ToUInt64(result, System.Globalization.CultureInfo.InvariantCulture);
        ulong defined = Enum.GetValues<TEnum>()
            .Aggregate(0UL, (mask, item) => mask | Convert.ToUInt64(item, System.Globalization.CultureInfo.InvariantCulture));
        return (raw & ~defined) == 0;
    }

    private sealed class WireCenterMark
    {
        public string? AnnotationId { get; init; }
        public string? ViewId { get; init; }
        public string? Target { get; init; }
        public string? ConnectionLines { get; init; }
        public bool? LinearSlotCenter { get; init; }
        public bool? ArcSlotCenter { get; init; }
        public bool? UseDocumentDefaults { get; init; }
        public double? SizeMillimeters { get; init; }
        public double? GapMillimeters { get; init; }
        public bool? ExtendedLines { get; init; }
        public bool? CenterLineFont { get; init; }
        public int? MinimumNewMarks { get; init; }
        public string? ProvenanceKind { get; init; }
        public string? ProvenanceMethod { get; init; }
        public string? ApprovalState { get; init; }
        public string[]? CoverageKeys { get; init; }
    }
}
