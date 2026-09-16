using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.EngineeringModel;

/// <summary>
/// Generic semantic classes observed while reviewing a single-part engineering drawing.
/// 本枚举描述单零件工程图复核得到的通用语义类别，不保存秘密图纸原文、文件名或客户信息。
/// </summary>
public enum PartDrawingSemanticClass
{
    /// <summary>Required orthographic view coverage.</summary>
    OrthographicViews,

    /// <summary>Optional but common isometric communication view.</summary>
    IsometricView,

    /// <summary>Hole or hole-pattern size/location/callout coverage.</summary>
    HoleFeature,

    /// <summary>Sheet-metal bend radius or formed-corner coverage.</summary>
    BendRadius,

    /// <summary>Material thickness coverage.</summary>
    Thickness,

    /// <summary>Section or detail view candidate.</summary>
    SectionOrDetailCandidate,

    /// <summary>Material/specification coverage.</summary>
    Material,

    /// <summary>General or feature-specific tolerance coverage.</summary>
    GeneralTolerance,

    /// <summary>Surface-finish requirement coverage.</summary>
    SurfaceFinish,

    /// <summary>Title-block and release metadata coverage.</summary>
    TitleBlock,
}

/// <summary>Lifecycle status of an engineering requirement.</summary>
/// <remarks>
/// ReviewRequired is deliberate for observations derived from a drawing review.  A proposal or AI inference cannot enter
/// Released without an explicit approval transition.  从图纸复核得到的要求故意进入 ReviewRequired；proposal/AI inference
/// 必须经过显式批准才能进入 Released。
/// </remarks>
public enum EngineeringRequirementStatus
{
    Proposal,
    ReviewRequired,
    Approved,
    Released,
}

/// <summary>Redacted provenance attached to a requirement without retaining confidential source content.</summary>
public sealed record EngineeringProvenance
{
    /// <summary>Creates a provenance record with a public method and non-secret source class.</summary>
    public EngineeringProvenance(string sourceKind, string method, DateTimeOffset observedAtUtc)
    {
        SourceKind = RequireNonBlank(sourceKind, nameof(sourceKind));
        Method = RequireNonBlank(method, nameof(method));
        ObservedAtUtc = observedAtUtc;
    }

    /// <summary>Gets the controlled provenance class, for example private_drawing_review or model_native.</summary>
    public string SourceKind { get; }

    /// <summary>Gets the non-secret observation method.</summary>
    public string Method { get; }

    /// <summary>Gets the UTC time at which the observation was made.</summary>
    public DateTimeOffset ObservedAtUtc { get; }

    private static string RequireNonBlank(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}

/// <summary>One stable requirement that a part drawing compiler must cover or explicitly waive.</summary>
public sealed record PartDrawingRequirement
{
    /// <summary>Stable identity independent of feature/view/annotation regeneration.</summary>
    public required EngineeringRequirementId RequirementId { get; init; }

    /// <summary>Semantic class of the requirement.</summary>
    public required PartDrawingSemanticClass SemanticClass { get; init; }

    /// <summary>Deterministic coverage key consumed by a planner and QA gate.</summary>
    public required string CoverageKey { get; init; }

    /// <summary>Whether missing coverage blocks release.</summary>
    public required bool Required { get; init; }

    /// <summary>Current approval lifecycle state.</summary>
    public required EngineeringRequirementStatus Status { get; init; }

    /// <summary>Redacted source/provenance; it never contains the original PDF text.</summary>
    public required EngineeringProvenance Provenance { get; init; }
}

/// <summary>
/// Immutable requirement graph for one single-part drawing profile.
/// 单个零件图的 immutable requirement graph；它是 Drawing Compiler 的输入，不是 PDF 内容存储器。
/// </summary>
public sealed record PartDrawingRequirementSet
{
    /// <summary>Schema version for persisted or exchanged requirement graphs.</summary>
    public const string SchemaVersion = "1.0";

    /// <summary>Creates a graph and validates uniqueness/identity invariants.</summary>
    public PartDrawingRequirementSet(
        string profileId,
        IEnumerable<PartDrawingRequirement> requirements)
    {
        ProfileId = RequireNonBlank(profileId, nameof(profileId));
        Requirements = [.. (requirements ?? throw new ArgumentNullException(nameof(requirements)))];
        if (Requirements.Any(requirement => requirement is null))
        {
            throw new ArgumentException("Requirement collections cannot contain null values.", nameof(requirements));
        }

        if (Requirements.Select(requirement => requirement.RequirementId).Distinct().Count() != Requirements.Length)
        {
            throw new ArgumentException("Requirement identities must be unique within one profile.", nameof(requirements));
        }
    }

    /// <summary>Gets the public, non-secret profile identifier.</summary>
    public string ProfileId { get; }

    /// <summary>Gets the immutable requirements in deterministic order.</summary>
    public ImmutableArray<PartDrawingRequirement> Requirements { get; }

    /// <summary>Gets whether every required item has reached an approved state.</summary>
    public bool CanRelease => Requirements.All(requirement => !requirement.Required || requirement.Status is EngineeringRequirementStatus.Approved or EngineeringRequirementStatus.Released);

    /// <summary>Gets the stable IDs that currently block release.</summary>
    public ImmutableArray<EngineeringRequirementId> BlockingRequirementIds =>
        [.. Requirements
            .Where(requirement => requirement.Required && requirement.Status is not (EngineeringRequirementStatus.Approved or EngineeringRequirementStatus.Released))
            .Select(requirement => requirement.RequirementId)];

    /// <summary>
    /// Builds a redacted graph from generic classes observed in a private drawing review.
    /// 从秘密图纸复核得到的通用类别构建脱敏 graph；所有结果先进入 ReviewRequired。
    /// </summary>
    public static PartDrawingRequirementSet FromReviewedClasses(
        string profileId,
        IEnumerable<PartDrawingSemanticClass> observedClasses,
        DateTimeOffset observedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(observedClasses);
        ImmutableArray<PartDrawingSemanticClass> classes = [.. observedClasses.Distinct().OrderBy(value => value)];
        if (classes.Length == 0)
        {
            throw new ArgumentException("At least one reviewed semantic class is required.", nameof(observedClasses));
        }

        EngineeringProvenance provenance = new("private_drawing_review", "redacted_visual_semantic_review", observedAtUtc);
        var requirements = ImmutableArray.CreateBuilder<PartDrawingRequirement>();
        foreach (PartDrawingSemanticClass semanticClass in classes)
        {
            string coverageKey = semanticClass switch
            {
                PartDrawingSemanticClass.OrthographicViews => "views.orthographic.coverage",
                PartDrawingSemanticClass.IsometricView => "views.isometric.coverage",
                PartDrawingSemanticClass.HoleFeature => "features.hole.size-location-callout",
                PartDrawingSemanticClass.BendRadius => "features.bend-radius.coverage",
                PartDrawingSemanticClass.Thickness => "features.thickness.coverage",
                PartDrawingSemanticClass.SectionOrDetailCandidate => "views.section-or-detail.coverage",
                PartDrawingSemanticClass.Material => "requirements.material.coverage",
                PartDrawingSemanticClass.GeneralTolerance => "requirements.general-tolerance.coverage",
                PartDrawingSemanticClass.SurfaceFinish => "requirements.surface-finish.coverage",
                PartDrawingSemanticClass.TitleBlock => "metadata.title-block.coverage",
                _ => throw new ArgumentOutOfRangeException(nameof(observedClasses), semanticClass, "Unknown drawing semantic class."),
            };

            requirements.Add(
                new PartDrawingRequirement
                {
                    RequirementId = new EngineeringRequirementId(BuildRequirementId(profileId, coverageKey)),
                    SemanticClass = semanticClass,
                    CoverageKey = coverageKey,
                    Required = semanticClass is not PartDrawingSemanticClass.IsometricView,
                    Status = EngineeringRequirementStatus.ReviewRequired,
                    Provenance = provenance,
                });
        }

        return new PartDrawingRequirementSet(profileId, requirements);
    }

    /// <summary>
    /// Applies an explicit human approval to selected requirements while preserving their original provenance.
    /// 对选定 requirement 应用显式人工批准，同时保留原始 provenance；不允许 AI 自动批准。
    /// </summary>
    public PartDrawingRequirementSet Approve(IEnumerable<EngineeringRequirementId> approvedIds)
    {
        ArgumentNullException.ThrowIfNull(approvedIds);
        ImmutableHashSet<EngineeringRequirementId> approved = [.. approvedIds];
        PartDrawingRequirement[] updated = [.. Requirements
            .Select(requirement => approved.Contains(requirement.RequirementId)
                ? requirement with { Status = EngineeringRequirementStatus.Approved }
                : requirement)];
        return new PartDrawingRequirementSet(ProfileId, updated);
    }

    private static string BuildRequirementId(string profileId, string coverageKey)
    {
        string canonical = $"{profileId.Trim()}|{coverageKey}";
        return $"requirement:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant()}";
    }

    private static string RequireNonBlank(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}
