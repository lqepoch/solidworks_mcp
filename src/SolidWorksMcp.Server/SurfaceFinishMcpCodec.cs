using System.Text.Json;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Server;

/// <summary>
/// Parses one bounded, approval-gated surface-finish request for the part drawing compiler.
/// 解析一个供零件工程图 compiler 使用的、有界且经过审批门禁的表面粗糙度请求。
/// </summary>
/// <remarks>
/// The wire contract is intentionally semantic rather than a raw SOLIDWORKS argument bag. It accepts paper-space
/// placement, public enum names, provenance and coverage keys, while refusing an unapproved proposal. The first
/// provider slice creates a view-scoped symbol; persistent model-edge attachment will be added behind a declarative
/// selector in a later slice. 这里的 wire contract 是工程语义，而不是 SOLIDWORKS 原始参数袋：只接受纸空间位置、
/// public enum 名称、provenance 与 coverage key，并拒绝未批准 proposal。首个 Provider slice 创建 view-scoped symbol；
/// persistent model-edge attachment 将在后续 declarative selector slice 中加入。
/// </remarks>
internal static class SurfaceFinishMcpCodec
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

    /// <summary>Parses one optional surface-finish JSON value before a CAD session is opened.</summary>
    /// <summary>在打开 CAD session 之前解析一个可选的 surface-finish JSON。</summary>
    public static bool TryParse(
        string? json,
        out SurfaceFinishSymbolRequest? request,
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
            error = "surfaceFinishJson-too-large";
            return false;
        }

        WireSurfaceFinish? wire;
        try
        {
            wire = JsonSerializer.Deserialize<WireSurfaceFinish>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            error = "surfaceFinishJson-invalid-json";
            return false;
        }

        if (wire is null
            || !TryIdentity(wire.AnnotationId, out string annotationId)
            || !TryIdentity(wire.ViewId, out string viewId))
        {
            error = "surfaceFinishJson-identity-required";
            return false;
        }

        if (!TryFinite(wire.PositionXMillimeters, out double positionX)
            || !TryFinite(wire.PositionYMillimeters, out double positionY))
        {
            error = "surfaceFinishJson-position-invalid";
            return false;
        }

        if (!TryText(wire.MaximumRoughness, out string maximumRoughness))
        {
            error = "surfaceFinishJson-maximum-roughness-required";
            return false;
        }

        if (!TryOptionalText(wire.ProductionMethod, out string? productionMethod)
            || !TryOptionalText(wire.MinimumRoughness, out string? minimumRoughness)
            || !TryOptionalText(wire.SamplingLength, out string? samplingLength)
            || !TryOptionalText(wire.MachiningAllowance, out string? machiningAllowance)
            || !TryOptionalText(wire.OtherValues, out string? otherValues)
            || !TryOptionalText(wire.RoughnessSpacing, out string? roughnessSpacing)
            || !TryText(wire.ProvenanceKind, out string provenanceKind)
            || !TryText(wire.ProvenanceMethod, out string provenanceMethod))
        {
            error = "surfaceFinishJson-text-invalid";
            return false;
        }

        if (!TryEnum(wire.SymbolType, nameof(wire.SymbolType), SurfaceFinishSymbolType.MachiningRequired, out SurfaceFinishSymbolType symbolType, out error)
            || !TryEnum(wire.LayDirection, nameof(wire.LayDirection), SurfaceFinishLayDirection.None, out SurfaceFinishLayDirection layDirection, out error)
            || !TryEnum(wire.LeaderStyle, nameof(wire.LeaderStyle), SurfaceFinishLeaderStyle.Straight, out SurfaceFinishLeaderStyle leaderStyle, out error)
            || !TryEnum(wire.ArrowStyle, nameof(wire.ArrowStyle), SurfaceFinishArrowStyle.Open, out SurfaceFinishArrowStyle arrowStyle, out error)
            || !TryEnum(wire.ApprovalState, nameof(wire.ApprovalState), null, out DrawingAnnotationApprovalState approvalState, out error))
        {
            return false;
        }

        if (approvalState is not (DrawingAnnotationApprovalState.Approved or DrawingAnnotationApprovalState.Released))
        {
            error = "surfaceFinishJson-approval-required";
            return false;
        }

        string[] coverageKeys = wire.CoverageKeys?.Select(value => value?.Trim() ?? string.Empty).ToArray() ?? [];
        if (coverageKeys.Length > MaximumCoverageKeyCount
            || coverageKeys.Any(value => value.Length is 0 or > MaximumCoverageKeyLength
                || value.Any(char.IsControl)))
        {
            error = "surfaceFinishJson-coverage-keys-invalid";
            return false;
        }

        request = new SurfaceFinishSymbolRequest
        {
            RequestedAnnotationId = new AnnotationId(annotationId),
            ViewId = new ViewId(viewId),
            Position = new Coordinate2D(
                Length.FromMillimeters(positionX),
                Length.FromMillimeters(positionY)),
            SymbolType = symbolType,
            LayDirection = layDirection,
            LeaderStyle = leaderStyle,
            ArrowStyle = arrowStyle,
            ProductionMethod = productionMethod,
            MaximumRoughness = maximumRoughness,
            MinimumRoughness = minimumRoughness,
            SamplingLength = samplingLength,
            MachiningAllowance = machiningAllowance,
            OtherValues = otherValues,
            RoughnessSpacing = roughnessSpacing,
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

    private static bool TryOptionalText(string? value, out string? normalized)
    {
        normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is null
            || normalized.Length <= MaximumTextLength
            && normalized.All(character => !char.IsControl(character));
    }

    private static bool TryFinite(double? value, out double result)
    {
        result = value.GetValueOrDefault();
        return value.HasValue && double.IsFinite(result);
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
        error = $"{name} is not a supported {typeof(TEnum).Name}.";
        return false;
    }

    private sealed class WireSurfaceFinish
    {
        public string? AnnotationId { get; init; }
        public string? ViewId { get; init; }
        public double? PositionXMillimeters { get; init; }
        public double? PositionYMillimeters { get; init; }
        public string? SymbolType { get; init; }
        public string? LayDirection { get; init; }
        public string? LeaderStyle { get; init; }
        public string? ArrowStyle { get; init; }
        public string? ProductionMethod { get; init; }
        public string? MaximumRoughness { get; init; }
        public string? MinimumRoughness { get; init; }
        public string? SamplingLength { get; init; }
        public string? MachiningAllowance { get; init; }
        public string? OtherValues { get; init; }
        public string? RoughnessSpacing { get; init; }
        public string? ProvenanceKind { get; init; }
        public string? ProvenanceMethod { get; init; }
        public string? ApprovalState { get; init; }
        public string[]? CoverageKeys { get; init; }
    }
}
