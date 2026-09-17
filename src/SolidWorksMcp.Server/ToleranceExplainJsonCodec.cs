using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using SolidWorksMcp.EngineeringModel;
using SolidWorksMcp.Protocol;
using SolidWorksMcp.Tolerancing;

namespace SolidWorksMcp.Server;

/// <summary>
/// Decodes the bounded, redacted input of <c>tolerance.explain</c>.
/// </summary>
/// <remarks>
/// This codec intentionally accepts engineering values and provenance classes only. It never accepts a PDF path,
/// drawing text, arbitrary expressions or provider/COM objects. 该 codec 只接受工程数值和脱敏 provenance，
/// 不接受 PDF 路径、图纸原文、任意表达式或 Provider/COM 对象。
/// </remarks>
internal static class ToleranceExplainJsonCodec
{
    private const int MaxJsonCharacters = 64 * 1024;
    private const int MaxTerms = 64;
    private const int MaxIdentityLength = 256;
    private const int MaxTextLength = 256;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 8,
    };

    /// <summary>Parses one redacted tolerance request before any provider session is requested.</summary>
    public static bool TryParse(
        string? json,
        out ToleranceExplainRequest? request,
        out string? error)
    {
        request = null;
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "toleranceRequestJson is required.";
            return false;
        }

        if (json.Length > MaxJsonCharacters)
        {
            error = $"toleranceRequestJson cannot exceed {MaxJsonCharacters} characters.";
            return false;
        }

        ToleranceRequestDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ToleranceRequestDto>(json, Options);
        }
        catch (JsonException exception)
        {
            error = $"toleranceRequestJson is malformed: {exception.Message}";
            return false;
        }

        if (dto is null)
        {
            error = "toleranceRequestJson deserialized to null.";
            return false;
        }

        if (!RequiredIdentity(dto.DimensionId, "dimensionId", out error)
            || !RequiredIdentity(dto.StackupId, "stackupId", out error)
            || !RequiredText(dto.SourceKind, "sourceKind", out error)
            || !RequiredText(dto.ProvenanceMethod, "provenanceMethod", out error))
        {
            return false;
        }

        if (!TryFinite(dto.DesignNominalMillimeters, "designNominalMillimeters", out double designNominal, out error)
            || !TryFinite(dto.FunctionalMinimumMillimeters, "functionalMinimumMillimeters", out double functionalMinimum, out error)
            || !TryFinite(dto.FunctionalMaximumMillimeters, "functionalMaximumMillimeters", out double functionalMaximum, out error)
            || !TryFinite(dto.DrawingNominalMillimeters, "drawingNominalMillimeters", out double drawingNominal, out error)
            || !TryFinite(dto.DrawingLowerDeviationMillimeters, "drawingLowerDeviationMillimeters", out double drawingLower, out error)
            || !TryFinite(dto.DrawingUpperDeviationMillimeters, "drawingUpperDeviationMillimeters", out double drawingUpper, out error))
        {
            return false;
        }

        if (functionalMinimum > functionalMaximum)
        {
            error = "functionalMinimumMillimeters must not exceed functionalMaximumMillimeters.";
            return false;
        }

        if (drawingLower > drawingUpper)
        {
            error = "drawingLowerDeviationMillimeters must not exceed drawingUpperDeviationMillimeters.";
            return false;
        }

        if (!TryParseEnum(dto.ConstraintKind, "constraintKind", out ToleranceConstraintKind constraintKind, out error)
            || !TryParseEnum(dto.Method, "method", out ToleranceAnalysisMethod method, out error)
            || !TryParseEnum(dto.ApprovalState, "approvalState", out EngineeringRequirementStatus approvalState, out error)
            || !TryParseEnum(dto.Criticality ?? nameof(ToleranceCriticality.Normal), "criticality", out ToleranceCriticality criticality, out error))
        {
            return false;
        }

        if (!TryParseUtc(dto.ObservedAtUtc, "observedAtUtc", out DateTimeOffset observedAtUtc, out error))
        {
            return false;
        }

        if (dto.Terms is null || dto.Terms.Count == 0 || dto.Terms.Count > MaxTerms)
        {
            error = $"terms requires 1..{MaxTerms} entries.";
            return false;
        }

        var terms = ImmutableArray.CreateBuilder<ToleranceStackTerm>(dto.Terms.Count);
        foreach (ToleranceTermDto? term in dto.Terms)
        {
            if (term is null)
            {
                error = "terms cannot contain null values.";
                return false;
            }

            if (!RequiredIdentity(term.TermId, "termId", out error)
                || !RequiredText(term.SourceKind, "term.sourceKind", out error)
                || !RequiredText(term.ProvenanceMethod, "term.provenanceMethod", out error))
            {
                return false;
            }

            if (!TryFinite(term.NominalMillimeters, "term.nominalMillimeters", out double nominal, out error)
                || !TryFinite(term.LowerDeviationMillimeters, "term.lowerDeviationMillimeters", out double lowerDeviation, out error)
                || !TryFinite(term.UpperDeviationMillimeters, "term.upperDeviationMillimeters", out double upperDeviation, out error))
            {
                return false;
            }

            if (lowerDeviation > upperDeviation)
            {
                error = $"Tolerance term '{term.TermId}' has lower deviation greater than upper deviation.";
                return false;
            }

            if (!TryParseEnum(term.Direction, $"term.direction:{term.TermId}", out ToleranceStackDirection direction, out error)
                || !TryParseUtc(term.ObservedAtUtc, $"term.observedAtUtc:{term.TermId}", out DateTimeOffset termObservedAtUtc, out error))
            {
                return false;
            }

            terms.Add(
                new ToleranceStackTerm
                {
                    TermId = term.TermId!.Trim(),
                    Nominal = Length.FromMillimeters(nominal),
                    Tolerance = new Tolerance(Length.FromMillimeters(lowerDeviation), Length.FromMillimeters(upperDeviation)),
                    Direction = direction,
                    Provenance = new EngineeringProvenance(term.SourceKind!.Trim(), term.ProvenanceMethod!.Trim(), termObservedAtUtc),
                });
        }

        if (terms.Select(term => term.TermId).Distinct(StringComparer.Ordinal).Count() != terms.Count)
        {
            error = "termId values must be unique.";
            return false;
        }

        Tolerance? generalTolerance = null;
        if (dto.GeneralLowerDeviationMillimeters is not null || dto.GeneralUpperDeviationMillimeters is not null)
        {
            if (!TryFinite(dto.GeneralLowerDeviationMillimeters, "generalLowerDeviationMillimeters", out double generalLower, out error)
                || !TryFinite(dto.GeneralUpperDeviationMillimeters, "generalUpperDeviationMillimeters", out double generalUpper, out error))
            {
                return false;
            }

            if (generalLower > generalUpper)
            {
                error = "generalLowerDeviationMillimeters must not exceed generalUpperDeviationMillimeters.";
                return false;
            }

            generalTolerance = new Tolerance(Length.FromMillimeters(generalLower), Length.FromMillimeters(generalUpper));
        }

        ManufacturingCapability? manufacturingCapability = null;
        if (dto.ManufacturingCapability is not null)
        {
            ManufacturingCapabilityDto capability = dto.ManufacturingCapability;
            if (!RequiredIdentity(capability.CapabilityId, "manufacturingCapability.capabilityId", out error)
                || !RequiredText(capability.InspectionMethod, "manufacturingCapability.inspectionMethod", out error)
                || !RequiredText(capability.SourceKind, "manufacturingCapability.sourceKind", out error)
                || !RequiredText(capability.ProvenanceMethod, "manufacturingCapability.provenanceMethod", out error)
                || !TryFinite(capability.MinimumMillimeters, "manufacturingCapability.minimumMillimeters", out double capabilityMinimum, out error)
                || !TryFinite(capability.MaximumMillimeters, "manufacturingCapability.maximumMillimeters", out double capabilityMaximum, out error)
                || !TryParseUtc(capability.ObservedAtUtc, "manufacturingCapability.observedAtUtc", out DateTimeOffset capabilityObservedAtUtc, out error))
            {
                return false;
            }

            if (capabilityMinimum > capabilityMaximum)
            {
                error = "manufacturingCapability.minimumMillimeters must not exceed maximumMillimeters.";
                return false;
            }

            manufacturingCapability = new ManufacturingCapability
            {
                CapabilityId = capability.CapabilityId!.Trim(),
                AchievableLimits = new DimensionLimits(Length.FromMillimeters(capabilityMinimum), Length.FromMillimeters(capabilityMaximum)),
                InspectionMethod = capability.InspectionMethod!.Trim(),
                Provenance = new EngineeringProvenance(capability.SourceKind!.Trim(), capability.ProvenanceMethod!.Trim(), capabilityObservedAtUtc),
            };
        }

        string? fitClass = OptionalText(dto.FitClass, "fitClass", out error);
        if (error is not null)
        {
            return false;
        }

        string? inspectionMethod = OptionalText(dto.InspectionMethod, "inspectionMethod", out error);
        if (error is not null)
        {
            return false;
        }

        request = new ToleranceExplainRequest
        {
            Requirement = new FunctionalDimensionRequirement
            {
                DimensionId = dto.DimensionId!.Trim(),
                DesignNominal = Length.FromMillimeters(designNominal),
                FunctionalLimits = new DimensionLimits(Length.FromMillimeters(functionalMinimum), Length.FromMillimeters(functionalMaximum)),
                DrawingNominal = Length.FromMillimeters(drawingNominal),
                DrawingTolerance = new Tolerance(Length.FromMillimeters(drawingLower), Length.FromMillimeters(drawingUpper)),
                FitClass = fitClass,
                GeneralTolerance = generalTolerance,
                ManufacturingCapability = manufacturingCapability,
                InspectionMethod = inspectionMethod,
                Criticality = criticality,
                Provenance = new EngineeringProvenance(dto.SourceKind!.Trim(), dto.ProvenanceMethod!.Trim(), observedAtUtc),
                ApprovalState = approvalState,
            },
            Stackup = new ToleranceStackupRequest(dto.StackupId!.Trim(), constraintKind, method, terms),
        };

        return true;
    }

    private static bool RequiredIdentity(string? value, string name, out string? error)
    {
        if (!RequiredText(value, name, out error))
        {
            return false;
        }

        if (value!.Trim().Length > MaxIdentityLength)
        {
            error = $"{name} exceeds the bounded length policy.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool RequiredText(string? value, string name, out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            error = $"{name} is required.";
            return false;
        }

        error = null;
        return true;
    }

    private static string? OptionalText(string? value, string name, out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            error = null;
            return null;
        }

        if (value.Trim().Length > MaxTextLength)
        {
            error = $"{name} exceeds the bounded length policy.";
            return null;
        }

        error = null;
        return value.Trim();
    }

    private static bool TryFinite(double? value, string name, out double result, out string? error)
    {
        if (value is not double candidate || !double.IsFinite(candidate))
        {
            result = default;
            error = $"{name} must be a finite number.";
            return false;
        }

        result = candidate;
        error = null;
        return true;
    }

    private static bool TryParseEnum<TEnum>(string? value, string name, out TEnum result, out string? error)
        where TEnum : struct, Enum
    {
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

    private static bool TryParseUtc(string? value, string name, out DateTimeOffset result, out string? error)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result))
        {
            error = null;
            return true;
        }

        error = $"{name} must be a valid UTC timestamp.";
        return false;
    }

    private sealed class ToleranceRequestDto
    {
        public string? DimensionId { get; set; }
        public double? DesignNominalMillimeters { get; set; }
        public double? FunctionalMinimumMillimeters { get; set; }
        public double? FunctionalMaximumMillimeters { get; set; }
        public double? DrawingNominalMillimeters { get; set; }
        public double? DrawingLowerDeviationMillimeters { get; set; }
        public double? DrawingUpperDeviationMillimeters { get; set; }
        public string? FitClass { get; set; }
        public double? GeneralLowerDeviationMillimeters { get; set; }
        public double? GeneralUpperDeviationMillimeters { get; set; }
        public string? InspectionMethod { get; set; }
        public string? Criticality { get; set; }
        public string? SourceKind { get; set; }
        public string? ProvenanceMethod { get; set; }
        public string? ObservedAtUtc { get; set; }
        public string? ApprovalState { get; set; }
        public string? StackupId { get; set; }
        public string? ConstraintKind { get; set; }
        public string? Method { get; set; }
        public List<ToleranceTermDto?>? Terms { get; set; }
        public ManufacturingCapabilityDto? ManufacturingCapability { get; set; }
    }

    private sealed class ToleranceTermDto
    {
        public string? TermId { get; set; }
        public double? NominalMillimeters { get; set; }
        public double? LowerDeviationMillimeters { get; set; }
        public double? UpperDeviationMillimeters { get; set; }
        public string? Direction { get; set; }
        public string? SourceKind { get; set; }
        public string? ProvenanceMethod { get; set; }
        public string? ObservedAtUtc { get; set; }
    }

    private sealed class ManufacturingCapabilityDto
    {
        public string? CapabilityId { get; set; }
        public double? MinimumMillimeters { get; set; }
        public double? MaximumMillimeters { get; set; }
        public string? InspectionMethod { get; set; }
        public string? SourceKind { get; set; }
        public string? ProvenanceMethod { get; set; }
        public string? ObservedAtUtc { get; set; }
    }
}

/// <summary>Validated domain input kept separate from the untrusted MCP JSON DTO.</summary>
/// <summary>经校验的领域输入，与不可信 MCP JSON DTO 分离。</summary>
internal sealed record ToleranceExplainRequest
{
    public required FunctionalDimensionRequirement Requirement { get; init; }

    public required ToleranceStackupRequest Stackup { get; init; }
}
