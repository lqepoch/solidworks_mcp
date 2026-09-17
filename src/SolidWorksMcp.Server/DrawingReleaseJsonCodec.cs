using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using SolidWorksMcp.AutoDrawing;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.EngineeringModel;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Server;

/// <summary>
/// Bounded decoder for the high-level single-part drawing release plan.
/// 单零件工程图高层发布计划使用有界 decoder，避免把任意 JSON、路径或 COM 参数直接传入业务层。
/// </summary>
/// <remarks>
/// The wire format carries only redacted engineering identities and numeric paper-space geometry. It deliberately does
/// not accept PDF text, source-file content, executable expressions or vendor objects. Wire format 只承载脱敏的工程
/// identity 和纸空间数值几何，刻意不接受 PDF 文本、源文件内容、可执行表达式或厂商对象。
/// </remarks>
internal static class DrawingReleaseJsonCodec
{
    private const int MaxJsonCharacters = 384 * 1024;
    private const int MaxRequirements = 256;
    private const int MaxLayoutItems = 512;
    private const int MaxReservedZones = 64;
    private const int MaxManufacturingRequirements = 256;
    private const int MaxManufacturingEvidence = 1024;
    private const int MaxCoverageKeys = 64;
    private const int MaxIdentityLength = 256;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        MaxDepth = 16,
    };

    /// <summary>Decodes the redacted requirement graph used by coverage analysis.</summary>
    public static bool TryParseRequirements(
        string? json,
        out PartDrawingRequirementSet? requirements,
        out string? error)
    {
        requirements = null;
        if (!TryDeserialize(json, out RequirementSetDto? dto, out error) || dto is null)
        {
            return false;
        }

        if (!Required(dto.ProfileId, "profileId", out error)
            || dto.Requirements is null
            || dto.Requirements.Count == 0
            || dto.Requirements.Count > MaxRequirements)
        {
            error ??= $"requirementGraphJson requires 1..{MaxRequirements} requirements.";
            return false;
        }

        try
        {
            var items = new List<PartDrawingRequirement>(dto.Requirements.Count);
            foreach (RequirementDto? item in dto.Requirements)
            {
                if (item is null
                    || !Required(item.RequirementId, "requirementId", out error)
                    || !Required(item.CoverageKey, "coverageKey", out error)
                    || !Required(item.SourceKind, "sourceKind", out error)
                    || !Required(item.Method, "method", out error)
                    || !Required(item.ObservedAtUtc, "observedAtUtc", out error))
                {
                    error ??= "requirementGraphJson requirements cannot contain null values.";
                    return false;
                }

                if (!Bounded(item.RequirementId!, "requirementId", out error)
                    || !Bounded(item.CoverageKey!, "coverageKey", out error))
                {
                    return false;
                }

                items.Add(
                    new PartDrawingRequirement
                    {
                        RequirementId = new EngineeringRequirementId(item.RequirementId!.Trim()),
                        SemanticClass = item.SemanticClass,
                        CoverageKey = item.CoverageKey!.Trim(),
                        Required = item.Required,
                        Status = item.Status,
                        Provenance = new EngineeringProvenance(
                            item.SourceKind!.Trim(),
                            item.Method!.Trim(),
                            item.ObservedAtUtc!.Value),
                    });
            }

            requirements = new PartDrawingRequirementSet(dto.ProfileId!.Trim(), items);
            return true;
        }
        catch (ArgumentException exception)
        {
            error = $"requirementGraphJson is invalid: {exception.Message}";
            return false;
        }
    }

    /// <summary>Decodes the small high-level orientation/projection policy.</summary>
    public static bool TryParsePlanOptions(
        string? json,
        out DrawingReleasePlanOptions? options,
        out string? error)
    {
        options = null;
        if (!TryDeserialize(json, out PlanOptionsDto? dto, out error) || dto is null)
        {
            return false;
        }

        if (!Required(dto.PreferredPrimaryOrientation, "preferredPrimaryOrientation", out error)
            || !Bounded(dto.PreferredPrimaryOrientation!, "preferredPrimaryOrientation", out error))
        {
            return false;
        }

        options = new DrawingReleasePlanOptions
        {
            PreferredPrimaryOrientation = dto.PreferredPrimaryOrientation!.Trim(),
            PreferredPrimaryOrientationApproved = dto.PreferredPrimaryOrientationApproved,
            Projection = dto.Projection,
        };
        return true;
    }

    /// <summary>Decodes and executes no code while building one deterministic layout request.</summary>
    public static bool TryParseLayout(
        string? json,
        out DrawingLayoutRequest? request,
        out string? error)
    {
        request = null;
        if (!TryDeserialize(json, out LayoutRequestDto? dto, out error) || dto is null)
        {
            return false;
        }

        if (!Required(dto.SheetId, "sheetId", out error)
            || !Bounded(dto.SheetId!, "sheetId", out error)
            || dto.SheetBounds is null
            || dto.Margins is null
            || dto.Items is null
            || dto.Items.Count > MaxLayoutItems
            || dto.ReservedZones is null
            || dto.ReservedZones.Count > MaxReservedZones)
        {
            error ??= "layoutJson requires sheetId, sheetBounds, margins, items and reservedZones.";
            return false;
        }

        try
        {
            request = new DrawingLayoutRequest
            {
                SheetId = dto.SheetId!.Trim(),
                SheetBounds = ToRect(dto.SheetBounds),
                Margins = ToMargins(dto.Margins),
                Items = [.. dto.Items.Select(ToLayoutItem)],
                ReservedZones = [.. dto.ReservedZones.Select(ToReservedZone)],
                MinimumAnnotationSpacing = Length.FromMillimeters(dto.MinimumAnnotationSpacingMillimeters),
                DimensionTierSpacing = Length.FromMillimeters(dto.DimensionTierSpacingMillimeters),
                MaxReflowRings = dto.MaxReflowRings,
                AllowScaleReduction = dto.AllowScaleReduction,
                AllowSheetUpgrade = dto.AllowSheetUpgrade,
                AllowAdditionalSheet = dto.AllowAdditionalSheet,
                AllowDetailView = dto.AllowDetailView,
            };
            return true;
        }
        catch (ArgumentException exception)
        {
            error = $"layoutJson is invalid: {exception.Message}";
            return false;
        }
    }

    /// <summary>Decodes exact manufacturing requirements and their provider evidence.</summary>
    public static bool TryParseManufacturing(
        string? json,
        out DrawingManufacturingInput? input,
        out string? error)
    {
        input = null;
        if (!TryDeserialize(json, out ManufacturingDto? dto, out error) || dto is null)
        {
            return false;
        }

        if (dto.Requirements is null
            || dto.Requirements.Count == 0
            || dto.Requirements.Count > MaxManufacturingRequirements
            || dto.Evidence is null
            || dto.Evidence.Count > MaxManufacturingEvidence)
        {
            error = $"manufacturingJson requires 1..{MaxManufacturingRequirements} requirements and at most {MaxManufacturingEvidence} evidence records.";
            return false;
        }

        try
        {
            var requirements = new List<DrawingManufacturingAnnotationRequirement>(dto.Requirements.Count);
            foreach (ManufacturingRequirementDto? item in dto.Requirements)
            {
                if (item is null
                    || !Required(item.RequirementId, "requirementId", out error)
                    || !Required(item.FeatureIdentity, "featureIdentity", out error)
                    || !Required(item.ViewId, "viewId", out error))
                {
                    error ??= "manufacturingJson requirements cannot contain null values.";
                    return false;
                }

                requirements.Add(
                    new DrawingManufacturingAnnotationRequirement
                    {
                        RequirementId = item.RequirementId!.Trim(),
                        FeatureIdentity = item.FeatureIdentity!.Trim(),
                        Kind = item.Kind,
                        ViewId = new ViewId(item.ViewId!.Trim()),
                        CoverageKeys = ParseCoverageKeys(item.CoverageKeys, "requirement coverageKeys"),
                        IsCritical = item.IsCritical,
                    });
            }

            var evidence = new List<DrawingManufacturingAnnotationEvidence>(dto.Evidence.Count);
            foreach (ManufacturingEvidenceDto? item in dto.Evidence)
            {
                if (item is null
                    || !Required(item.EvidenceId, "evidenceId", out error)
                    || !Required(item.RequirementId, "requirementId", out error)
                    || !Required(item.FeatureIdentity, "featureIdentity", out error)
                    || !Required(item.ViewId, "viewId", out error)
                    || !Required(item.ProvenanceKind, "provenanceKind", out error)
                    || !Required(item.ProvenanceMethod, "provenanceMethod", out error))
                {
                    error ??= "manufacturingJson evidence cannot contain null values.";
                    return false;
                }

                evidence.Add(
                    new DrawingManufacturingAnnotationEvidence
                    {
                        EvidenceId = item.EvidenceId!.Trim(),
                        RequirementId = item.RequirementId!.Trim(),
                        FeatureIdentity = item.FeatureIdentity!.Trim(),
                        Kind = item.Kind,
                        ViewId = new ViewId(item.ViewId!.Trim()),
                        AnnotationId = string.IsNullOrWhiteSpace(item.AnnotationId) ? null : new AnnotationId(item.AnnotationId.Trim()),
                        ProvenanceKind = item.ProvenanceKind!.Trim(),
                        ProvenanceMethod = item.ProvenanceMethod!.Trim(),
                        ApprovalState = item.ApprovalState,
                        IsAssociative = item.IsAssociative,
                        IsAmbiguous = item.IsAmbiguous,
                        CoverageKeys = ParseCoverageKeys(item.CoverageKeys, "evidence coverageKeys"),
                    });
            }

            input = new DrawingManufacturingInput(requirements, evidence);
            return true;
        }
        catch (ArgumentException exception)
        {
            error = $"manufacturingJson is invalid: {exception.Message}";
            return false;
        }
    }

    /// <summary>Decodes explicit artifact formats/paths and the same-directory evidence manifest target.</summary>
    public static bool TryParseArtifactPolicy(
        string? json,
        out DrawingArtifactPolicy? policy,
        out string? error)
    {
        policy = null;
        if (!TryDeserialize(json, out ArtifactPolicyDto? dto, out error) || dto is null)
        {
            return false;
        }

        if (dto.Artifacts is null || dto.Artifacts.Count == 0 || dto.Artifacts.Count > 8)
        {
            error = "artifactPolicyJson requires 1..8 artifact entries.";
            return false;
        }

        if (!Required(dto.ManifestPath, "manifestPath", out error)
            || !Bounded(dto.ManifestPath!, "manifestPath", out error))
        {
            return false;
        }

        var artifacts = new List<DrawingArtifactRequest>(dto.Artifacts.Count);
        foreach (ArtifactDto? item in dto.Artifacts)
        {
            if (item is null
                || !Required(item.Format, "format", out error)
                || !Required(item.TargetPath, "targetPath", out error)
                || !Bounded(item.TargetPath!, "targetPath", out error))
            {
                error ??= "artifactPolicyJson artifacts cannot contain null values.";
                return false;
            }

            ArtifactDto artifact = item!;
            string format = artifact.Format!.Trim().ToUpperInvariant();
            if (format is not ("SLDDRW" or "PDF" or "DWG" or "DXF"))
            {
                error = $"artifactPolicyJson format '{format}' is not in the release allowlist.";
                return false;
            }

            string targetPath = artifact.TargetPath!.Trim();
            if (!Path.IsPathFullyQualified(targetPath)
                || targetPath.Contains('\r')
                || targetPath.Contains('\n'))
            {
                error = "artifactPolicyJson targetPath values must be absolute and must not contain CR/LF.";
                return false;
            }

            artifacts.Add(new DrawingArtifactRequest(format, Path.GetFullPath(targetPath), artifact.AllowOverwrite));
        }

        if (artifacts.Select(item => item.Format).Distinct(StringComparer.Ordinal).Count() != artifacts.Count)
        {
            error = "artifactPolicyJson artifact formats must be unique.";
            return false;
        }

        string manifestPath = Path.GetFullPath(dto.ManifestPath!.Trim());
        if (!Path.IsPathFullyQualified(manifestPath)
            || !manifestPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            || manifestPath.Contains('\r')
            || manifestPath.Contains('\n'))
        {
            error = "artifactPolicyJson manifestPath must be an absolute .json path without CR/LF.";
            return false;
        }

        policy = new DrawingArtifactPolicy([.. artifacts], manifestPath);
        return true;
    }

    private static DrawingLayoutItem ToLayoutItem(LayoutItemDto? dto)
    {
        if (dto is null || !Required(dto.ItemId, "itemId", out _)
            || dto.RequestedBounds is null)
        {
            throw new ArgumentException("Every layout item needs itemId and requestedBounds.");
        }

        return new DrawingLayoutItem
        {
            ItemId = dto.ItemId!.Trim(),
            Kind = dto.Kind,
            RequestedBounds = ToRect(dto.RequestedBounds),
            ViewId = string.IsNullOrWhiteSpace(dto.ViewId) ? null : dto.ViewId.Trim(),
            AnchorId = string.IsNullOrWhiteSpace(dto.AnchorId) ? null : dto.AnchorId.Trim(),
            RequestedDimensionTier = dto.RequestedDimensionTier,
            IsFixed = dto.IsFixed,
        };
    }

    private static DrawingLayoutReservedZone ToReservedZone(ReservedZoneDto? dto)
    {
        if (dto is null || !Required(dto.ZoneId, "zoneId", out _)
            || !Required(dto.ZoneKind, "zoneKind", out _)
            || dto.Bounds is null)
        {
            throw new ArgumentException("Every reserved zone needs zoneId, zoneKind and bounds.");
        }

        return new DrawingLayoutReservedZone
        {
            ZoneId = dto.ZoneId!.Trim(),
            ZoneKind = dto.ZoneKind!.Trim(),
            Bounds = ToRect(dto.Bounds),
        };
    }

    private static DrawingLayoutRect ToRect(RectDto dto) => new(
        Length.FromMillimeters(dto.LeftMillimeters),
        Length.FromMillimeters(dto.BottomMillimeters),
        Length.FromMillimeters(dto.WidthMillimeters),
        Length.FromMillimeters(dto.HeightMillimeters));

    private static DrawingLayoutMargins ToMargins(MarginsDto dto) => new(
        Length.FromMillimeters(dto.LeftMillimeters),
        Length.FromMillimeters(dto.BottomMillimeters),
        Length.FromMillimeters(dto.RightMillimeters),
        Length.FromMillimeters(dto.TopMillimeters));

    private static ImmutableArray<string> ParseCoverageKeys(List<string>? values, string name)
    {
        if (values is null || values.Count > MaxCoverageKeys || values.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException($"{name} must be a bounded non-empty-string array.");
        }

        return [.. values.Select(value => value.Trim())];
    }

    private static bool TryDeserialize<T>(string? json, out T? value, out string? error)
    {
        value = default;
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "JSON input is required.";
            return false;
        }

        if (json.Length > MaxJsonCharacters)
        {
            error = $"JSON input cannot exceed {MaxJsonCharacters} characters.";
            return false;
        }

        try
        {
            value = JsonSerializer.Deserialize<T>(json, Options);
            if (value is null)
            {
                error = "JSON input deserialized to null.";
                return false;
            }

            return true;
        }
        catch (JsonException exception)
        {
            error = $"JSON input is malformed: {exception.Message}";
            return false;
        }
    }

    private static bool Required(string? value, string name, out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            error = $"{name} is required.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool Required(DateTimeOffset? value, string name, out string? error)
    {
        if (value is null)
        {
            error = $"{name} is required.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool Bounded(string value, string name, out string? error)
    {
        if (value.Length > MaxIdentityLength)
        {
            error = $"{name} exceeds the bounded identity length policy.";
            return false;
        }

        error = null;
        return true;
    }

    private sealed class RequirementSetDto
    {
        public string? ProfileId { get; set; }

        public List<RequirementDto?>? Requirements { get; set; }
    }

    private sealed class RequirementDto
    {
        public string? RequirementId { get; set; }

        public PartDrawingSemanticClass SemanticClass { get; set; }

        public string? CoverageKey { get; set; }

        public bool Required { get; set; } = true;

        public EngineeringRequirementStatus Status { get; set; }

        public string? SourceKind { get; set; }

        public string? Method { get; set; }

        public DateTimeOffset? ObservedAtUtc { get; set; }
    }

    private sealed class PlanOptionsDto
    {
        public string? PreferredPrimaryOrientation { get; set; }

        public bool PreferredPrimaryOrientationApproved { get; set; }

        public PartDrawingProjectionMethod Projection { get; set; } = PartDrawingProjectionMethod.FirstAngle;
    }

    private sealed class LayoutRequestDto
    {
        public string? SheetId { get; set; }

        public RectDto? SheetBounds { get; set; }

        public MarginsDto? Margins { get; set; }

        public List<LayoutItemDto?>? Items { get; set; }

        public List<ReservedZoneDto?>? ReservedZones { get; set; }

        public double MinimumAnnotationSpacingMillimeters { get; set; } = 2d;

        public double DimensionTierSpacingMillimeters { get; set; } = 6d;

        public int MaxReflowRings { get; set; } = 8;

        public bool AllowScaleReduction { get; set; } = true;

        public bool AllowSheetUpgrade { get; set; } = true;

        public bool AllowAdditionalSheet { get; set; } = true;

        public bool AllowDetailView { get; set; } = true;
    }

    private sealed class LayoutItemDto
    {
        public string? ItemId { get; set; }

        public DrawingLayoutItemKind Kind { get; set; }

        public RectDto? RequestedBounds { get; set; }

        public string? ViewId { get; set; }

        public string? AnchorId { get; set; }

        public int RequestedDimensionTier { get; set; }

        public bool IsFixed { get; set; }
    }

    private sealed class ReservedZoneDto
    {
        public string? ZoneId { get; set; }

        public string? ZoneKind { get; set; }

        public RectDto? Bounds { get; set; }
    }

    private sealed class RectDto
    {
        public double LeftMillimeters { get; set; }

        public double BottomMillimeters { get; set; }

        public double WidthMillimeters { get; set; }

        public double HeightMillimeters { get; set; }
    }

    private sealed class MarginsDto
    {
        public double LeftMillimeters { get; set; }

        public double BottomMillimeters { get; set; }

        public double RightMillimeters { get; set; }

        public double TopMillimeters { get; set; }
    }

    private sealed class ManufacturingDto
    {
        public List<ManufacturingRequirementDto?>? Requirements { get; set; }

        public List<ManufacturingEvidenceDto?>? Evidence { get; set; }
    }

    private sealed class ManufacturingRequirementDto
    {
        public string? RequirementId { get; set; }

        public string? FeatureIdentity { get; set; }

        public ManufacturingAnnotationKind Kind { get; set; }

        public string? ViewId { get; set; }

        public List<string>? CoverageKeys { get; set; }

        public bool IsCritical { get; set; } = true;
    }

    private sealed class ManufacturingEvidenceDto
    {
        public string? EvidenceId { get; set; }

        public string? RequirementId { get; set; }

        public string? FeatureIdentity { get; set; }

        public ManufacturingAnnotationKind Kind { get; set; }

        public string? ViewId { get; set; }

        public string? AnnotationId { get; set; }

        public string? ProvenanceKind { get; set; }

        public string? ProvenanceMethod { get; set; }

        public DrawingAnnotationApprovalState ApprovalState { get; set; }

        public bool IsAssociative { get; set; }

        public bool IsAmbiguous { get; set; }

        public List<string>? CoverageKeys { get; set; }
    }

    private sealed class ArtifactPolicyDto
    {
        public List<ArtifactDto?>? Artifacts { get; set; }

        public string? ManifestPath { get; set; }
    }

    private sealed class ArtifactDto
    {
        public string? Format { get; set; }

        public string? TargetPath { get; set; }

        public bool AllowOverwrite { get; set; }
    }
}

/// <summary>Parsed orientation and projection choices for one release preflight.</summary>
internal sealed record DrawingReleasePlanOptions
{
    public required string PreferredPrimaryOrientation { get; init; }

    public required bool PreferredPrimaryOrientationApproved { get; init; }

    public required PartDrawingProjectionMethod Projection { get; init; }
}

/// <summary>Parsed exact manufacturing requirement/evidence collections.</summary>
internal sealed record DrawingManufacturingInput(
    IReadOnlyList<DrawingManufacturingAnnotationRequirement> Requirements,
    IReadOnlyList<DrawingManufacturingAnnotationEvidence> Evidence);

/// <summary>One explicit release artifact target; path policy remains provider-owned for CAD exports.</summary>
internal sealed record DrawingArtifactRequest(string Format, string TargetPath, bool AllowOverwrite);

/// <summary>Complete bounded release artifact policy including the privacy-safe manifest path.</summary>
internal sealed record DrawingArtifactPolicy(
    ImmutableArray<DrawingArtifactRequest> Artifacts,
    string ManifestPath);
