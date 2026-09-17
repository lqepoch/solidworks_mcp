using System.Text.Json;
using System.Text.Json.Serialization;
using SolidWorksMcp.EngineeringModel;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Server;

/// <summary>
/// Bounded JSON codec for the public read-only drawing.validate boundary.
/// 公开只读 drawing.validate boundary 使用的 bounded JSON codec。
/// </summary>
/// <remarks>
/// The product keeps JSON parsing at the MCP edge and passes immutable domain objects to the planner. The codec does
/// not accept arbitrary code, file paths, PDF payloads or COM values. 产品把 JSON parsing 限制在 MCP edge，再把 immutable
/// domain object 交给 planner；codec 不接受任意代码、文件路径、PDF payload 或 COM value。
/// </remarks>
internal static class DrawingDimensionJsonCodec
{
    private const int MaxJsonCharacters = 256 * 1024;
    private const int MaxFeatures = 256;
    private const int MaxDefinitionsPerFeature = 64;
    private const int MaxDatumsPerFeature = 32;
    private const int MaxEvidence = 4096;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        MaxDepth = 16,
    };

    public static bool TryParseGraph(
        string? json,
        out PartDrawingDimensionRequirementGraph? graph,
        out string? error)
    {
        graph = null;
        error = null;
        if (!TryDeserialize(json, out DimensionGraphDto? dto, out error)
            || dto is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(dto.ProfileId)
            || dto.Features is null
            || dto.Features.Count == 0
            || dto.Features.Count > MaxFeatures)
        {
            error = "dimensionRequirementJson requires a non-empty bounded profileId/features array.";
            return false;
        }

        try
        {
            graph = new PartDrawingDimensionRequirementGraph(
                dto.ProfileId,
                dto.Features.Select(ToFeature));
            return true;
        }
        catch (ArgumentException exception)
        {
            error = $"dimensionRequirementJson is invalid: {exception.Message}";
            return false;
        }
    }

    public static bool TryParseEvidence(
        string? json,
        out PartDrawingDimensionEvidence[] evidence,
        out string? error)
    {
        evidence = [];
        error = null;
        if (!TryDeserialize(json, out DimensionEvidenceDto[]? dto, out error)
            || dto is null)
        {
            return false;
        }

        if (dto.Length > MaxEvidence)
        {
            error = $"dimensionEvidenceJson cannot contain more than {MaxEvidence} records.";
            return false;
        }

        try
        {
            evidence = [.. dto.Select(ToEvidence)];
            return true;
        }
        catch (ArgumentException exception)
        {
            error = $"dimensionEvidenceJson is invalid: {exception.Message}";
            return false;
        }
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

    private static PartDrawingFeatureRequirement ToFeature(DimensionFeatureDto? dto)
    {
        if (dto is null)
        {
            throw new ArgumentException("Feature arrays cannot contain null values.");
        }

        if (dto.Definitions is null || dto.Definitions.Count > MaxDefinitionsPerFeature)
        {
            throw new ArgumentException("Every feature must contain a bounded definitions array.");
        }

        if (dto.Datums is null || dto.Datums.Count > MaxDatumsPerFeature)
        {
            throw new ArgumentException("Every feature must contain a bounded datums array.");
        }

        return new PartDrawingFeatureRequirement
        {
            FeatureId = new FeatureId(Required(dto.FeatureId, "featureId")),
            DisplayName = Required(dto.DisplayName, "displayName"),
            Kind = dto.Kind,
            Required = dto.Required,
            Status = dto.Status,
            Provenance = Provenance(dto.SourceKind, dto.Method, dto.ObservedAtUtc),
            Definitions = [.. dto.Definitions.Select(definition => ToDefinition(definition))],
            DatumRequirements = [.. dto.Datums.Select(datum => ToDatum(datum))],
        };
    }

    private static PartDrawingDimensionDefinition ToDefinition(DimensionDefinitionDto? definition)
    {
        if (definition is null)
        {
            throw new ArgumentException("Definition arrays cannot contain null values.");
        }

        return new PartDrawingDimensionDefinition
        {
            DefinitionKey = Required(definition.DefinitionKey, "definitionKey"),
            DisplayName = Required(definition.DisplayName, "displayName"),
            Kind = definition.Kind,
            Classification = definition.Classification,
            Required = definition.Required,
        };
    }

    private static PartDrawingDatumRequirement ToDatum(DatumDto? datum)
    {
        if (datum is null)
        {
            throw new ArgumentException("Datum arrays cannot contain null values.");
        }

        return new PartDrawingDatumRequirement
        {
            DatumId = Required(datum.DatumId, "datumId"),
            Role = datum.Role,
            Required = datum.Required,
            Status = datum.Status,
            Provenance = Provenance(datum.SourceKind, datum.Method, datum.ObservedAtUtc),
        };
    }

    private static PartDrawingDimensionEvidence ToEvidence(DimensionEvidenceDto? dto)
    {
        if (dto is null)
        {
            throw new ArgumentException("Dimension evidence arrays cannot contain null values.");
        }

        return new PartDrawingDimensionEvidence
        {
            DimensionId = new DimensionId(Required(dto.DimensionId, "dimensionId")),
            FeatureId = new FeatureId(Required(dto.FeatureId, "featureId")),
            DefinitionKey = Required(dto.DefinitionKey, "definitionKey"),
            Classification = dto.Classification,
            IsAssociative = dto.IsAssociative,
            IsVisible = dto.IsVisible,
            DatumId = string.IsNullOrWhiteSpace(dto.DatumId) ? null : dto.DatumId.Trim(),
            Provenance = Provenance(dto.SourceKind, dto.Method, dto.ObservedAtUtc),
        };
    }

    private static EngineeringProvenance Provenance(string? sourceKind, string? method, DateTimeOffset? observedAtUtc) =>
        new(
            Required(sourceKind, "sourceKind"),
            Required(method, "method"),
            observedAtUtc ?? throw new ArgumentException("observedAtUtc is required."));

    private static string Required(string? value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return value.Trim();
    }

    private sealed class DimensionGraphDto
    {
        public string? ProfileId { get; set; }

        public List<DimensionFeatureDto>? Features { get; set; }
    }

    private sealed class DimensionFeatureDto
    {
        public string? FeatureId { get; set; }

        public string? DisplayName { get; set; }

        public DrawingFeatureKind Kind { get; set; }

        public bool Required { get; set; } = true;

        public EngineeringRequirementStatus Status { get; set; }

        public string? SourceKind { get; set; }

        public string? Method { get; set; }

        public DateTimeOffset? ObservedAtUtc { get; set; }

        public List<DimensionDefinitionDto>? Definitions { get; set; }

        public List<DatumDto>? Datums { get; set; }
    }

    private sealed class DimensionDefinitionDto
    {
        public string? DefinitionKey { get; set; }

        public string? DisplayName { get; set; }

        public DrawingDimensionDefinitionKind Kind { get; set; }

        public DrawingDimensionClassification Classification { get; set; }

        public bool Required { get; set; } = true;
    }

    private sealed class DatumDto
    {
        public string? DatumId { get; set; }

        public DrawingDatumRole Role { get; set; }

        public bool Required { get; set; } = true;

        public EngineeringRequirementStatus Status { get; set; }

        public string? SourceKind { get; set; }

        public string? Method { get; set; }

        public DateTimeOffset? ObservedAtUtc { get; set; }
    }

    private sealed class DimensionEvidenceDto
    {
        public string? DimensionId { get; set; }

        public string? FeatureId { get; set; }

        public string? DefinitionKey { get; set; }

        public DrawingDimensionClassification Classification { get; set; }

        public bool IsAssociative { get; set; }

        public bool IsVisible { get; set; } = true;

        public string? DatumId { get; set; }

        public string? SourceKind { get; set; }

        public string? Method { get; set; }

        public DateTimeOffset? ObservedAtUtc { get; set; }
    }
}
