using System.Collections.Immutable;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.EngineeringModel;

/// <summary>
/// Manufacturing feature classes understood by the dimension planner.
/// 尺寸规划器理解的制造特征类别；它们是工程语义，不是 SOLIDWORKS COM 类型。
/// </summary>
public enum DrawingFeatureKind
{
    /// <summary>One through/blind/counterbored/countersunk hole.</summary>
    Hole,

    /// <summary>A repeated linear/circular/mirrored hole group.</summary>
    HolePattern,

    /// <summary>A slot or obround feature.</summary>
    Slot,

    /// <summary>An overall plate, flange or sheet profile.</summary>
    Plate,

    /// <summary>A rotational shaft or flange feature.</summary>
    Shaft,

    /// <summary>A fillet or formed radius requirement.</summary>
    Fillet,

    /// <summary>A chamfer requirement.</summary>
    Chamfer,

    /// <summary>A bounded feature class not yet specialized by the compiler.</summary>
    Generic,
}

/// <summary>Classification of a drawing dimension in the engineering contract.</summary>
/// <remarks>
/// Functional and manufacturing dimensions constrain production or fit. Reference dimensions communicate derived
/// information and must never be used to satisfy a missing functional definition. Functional 与 manufacturing 尺寸
/// 约束功能/制造；reference 尺寸只是派生信息，不能被用来掩盖缺失的功能定义。
/// </remarks>
public enum DrawingDimensionClassification
{
    Functional,
    Manufacturing,
    Reference,
}

/// <summary>Semantic definition that a feature may need on a manufacturing drawing.</summary>
public enum DrawingDimensionDefinitionKind
{
    Size,
    PositionX,
    PositionY,
    PositionZ,
    Orientation,
    Depth,
    Quantity,
    PatternPitch,
    PatternAngle,
    Radius,
    Tolerance,
    Gdt,
}

/// <summary>Role of one datum in a deterministic datum strategy.</summary>
public enum DrawingDatumRole
{
    Primary,
    Secondary,
    Tertiary,
}

/// <summary>One explicit size/position/orientation/depth definition for a manufacturing feature.</summary>
public sealed record PartDrawingDimensionDefinition
{
    /// <summary>Stable key within the owning feature, for example <c>x-position</c> or <c>y-pitch</c>.</summary>
    public required string DefinitionKey { get; init; }

    /// <summary>Non-secret human-readable label used in exact diagnostics.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Semantic kind of the definition.</summary>
    public required DrawingDimensionDefinitionKind Kind { get; init; }

    /// <summary>Expected engineering classification for this definition.</summary>
    public required DrawingDimensionClassification Classification { get; init; }

    /// <summary>Whether omission blocks a complete manufacturing definition.</summary>
    public bool Required { get; init; } = true;
}

/// <summary>One datum requirement declared by functional/model intent.</summary>
public sealed record PartDrawingDatumRequirement
{
    /// <summary>Stable datum identity such as <c>DATUM-A</c>; it is not an enumeration index.</summary>
    public required string DatumId { get; init; }

    /// <summary>Primary/secondary/tertiary datum role.</summary>
    public required DrawingDatumRole Role { get; init; }

    /// <summary>Whether the feature cannot be dimensioned completely without this datum.</summary>
    public bool Required { get; init; } = true;

    /// <summary>Approval state of the datum intent.</summary>
    public required EngineeringRequirementStatus Status { get; init; }

    /// <summary>Redacted provenance for the datum decision.</summary>
    public required EngineeringProvenance Provenance { get; init; }
}

/// <summary>Complete semantic dimension requirement for one manufacturable feature.</summary>
public sealed record PartDrawingFeatureRequirement
{
    /// <summary>Stable feature identity independent of drawing regeneration.</summary>
    public required FeatureId FeatureId { get; init; }

    /// <summary>Non-secret feature name used in diagnostics, for example <c>Slot S03</c>.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Semantic feature class.</summary>
    public required DrawingFeatureKind Kind { get; init; }

    /// <summary>Whether omission of the feature definition blocks release.</summary>
    public bool Required { get; init; } = true;

    /// <summary>Approval state of the feature requirement.</summary>
    public required EngineeringRequirementStatus Status { get; init; }

    /// <summary>Redacted provenance of the feature requirement.</summary>
    public required EngineeringProvenance Provenance { get; init; }

    /// <summary>Explicit dimensions that must be covered; no implicit “dimension everything” rule exists.</summary>
    public ImmutableArray<PartDrawingDimensionDefinition> Definitions { get; init; } = [];

    /// <summary>Functional datums needed by the feature's dimension scheme.</summary>
    public ImmutableArray<PartDrawingDatumRequirement> DatumRequirements { get; init; } = [];
}

/// <summary>
/// Immutable feature-level requirement graph consumed by the dimension planner.
/// 尺寸规划器消费的 immutable feature-level requirement graph。
/// </summary>
public sealed class PartDrawingDimensionRequirementGraph
{
    /// <summary>Persisted graph schema version.</summary>
    public const string SchemaVersion = "1.0";

    /// <summary>Creates and validates one graph without reading CAD or private drawing files.</summary>
    public PartDrawingDimensionRequirementGraph(
        string profileId,
        IEnumerable<PartDrawingFeatureRequirement> features)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ProfileId = profileId.Trim();
        Features = [.. (features ?? throw new ArgumentNullException(nameof(features)))];
        if (Features.Any(feature => feature is null))
        {
            throw new ArgumentException("Feature requirements cannot contain null values.", nameof(features));
        }

        if (Features.Select(feature => feature.FeatureId).Distinct().Count() != Features.Length)
        {
            throw new ArgumentException("Feature identities must be unique within one dimension graph.", nameof(features));
        }

        foreach (PartDrawingFeatureRequirement feature in Features)
        {
            ValidateFeature(feature);
        }
    }

    /// <summary>Non-secret profile identity.</summary>
    public string ProfileId { get; }

    /// <summary>Features in source order; planners apply their own stable ordering.</summary>
    public ImmutableArray<PartDrawingFeatureRequirement> Features { get; }

    /// <summary>Stable coverage key for one feature definition.</summary>
    public static string CoverageKey(FeatureId featureId, string definitionKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureId.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionKey);
        return $"feature:{featureId.Value.Trim()}:dimension:{definitionKey.Trim()}";
    }

    private static void ValidateFeature(PartDrawingFeatureRequirement feature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feature.FeatureId.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(feature.DisplayName);
        ArgumentNullException.ThrowIfNull(feature.Provenance);
        if (feature.Definitions.Any(definition => definition is null)
            || feature.Definitions.Select(definition => definition.DefinitionKey).Distinct(StringComparer.Ordinal).Count()
                != feature.Definitions.Length)
        {
            throw new ArgumentException($"Dimension definition keys must be unique for feature '{feature.FeatureId.Value}'.");
        }

        if (feature.DatumRequirements.Any(datum => datum is null)
            || feature.DatumRequirements.Select(datum => datum.DatumId).Distinct(StringComparer.Ordinal).Count()
                != feature.DatumRequirements.Length)
        {
            throw new ArgumentException($"Datum identities must be unique for feature '{feature.FeatureId.Value}'.");
        }

        foreach (PartDrawingDimensionDefinition definition in feature.Definitions)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(definition.DefinitionKey);
            ArgumentException.ThrowIfNullOrWhiteSpace(definition.DisplayName);
        }

        foreach (PartDrawingDatumRequirement datum in feature.DatumRequirements)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(datum.DatumId);
            ArgumentNullException.ThrowIfNull(datum.Provenance);
        }
    }
}

/// <summary>One dimension annotation read-back that can prove coverage of exactly one feature definition.</summary>
public sealed record PartDrawingDimensionEvidence
{
    /// <summary>Stable annotation/dimension identity from the provider.</summary>
    public required DimensionId DimensionId { get; init; }

    /// <summary>Exact semantic feature identity covered by this dimension.</summary>
    public required FeatureId FeatureId { get; init; }

    /// <summary>Exact definition key covered by this dimension.</summary>
    public required string DefinitionKey { get; init; }

    /// <summary>Classification read from the engineering plan or native provenance.</summary>
    public required DrawingDimensionClassification Classification { get; init; }

    /// <summary>Whether the dimension remains associated with model geometry.</summary>
    public bool IsAssociative { get; init; }

    /// <summary>Whether the dimension is actually visible on the released sheet.</summary>
    public bool IsVisible { get; init; } = true;

    /// <summary>Optional datum identity used by the dimension scheme.</summary>
    public string? DatumId { get; init; }

    /// <summary>Redacted source of this evidence, for example model_native or pmi.</summary>
    public required EngineeringProvenance Provenance { get; init; }
}
