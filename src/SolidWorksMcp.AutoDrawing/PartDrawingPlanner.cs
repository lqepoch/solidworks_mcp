using System.Collections.Immutable;
using SolidWorksMcp.EngineeringModel;
using SolidWorksMcp.Protocol;
using SolidWorksMcp.RuleEngine;

namespace SolidWorksMcp.AutoDrawing;

/// <summary>
/// High-level roles emitted by the deterministic part drawing planner.
/// 零件工程图规划器输出的高层视图角色；这些角色不是 SOLIDWORKS COM API 的一一映射。
/// </summary>
public enum PartDrawingViewRole
{
    /// <summary>Primary manufacturing view selected for the part.</summary>
    Primary,

    /// <summary>Orthographic projected view used to close visibility/measurement gaps.</summary>
    Projected,

    /// <summary>Communication view; it never substitutes for manufacturing coverage.</summary>
    Isometric,

    /// <summary>Section view selected when internal feature exposure is the best evidence.</summary>
    Section,

    /// <summary>Detail view selected when a local feature is too small or crowded at the parent scale.</summary>
    Detail,
}

/// <summary>Outcome of planning before any provider mutation is allowed.</summary>
public enum PartDrawingPlanningStatus
{
    /// <summary>All required planning inputs are approved and a provider may consume the plan.</summary>
    ReadyForGeneration,

    /// <summary>A draft may be inspected, but review/approval is still required.</summary>
    ReviewRequired,

    /// <summary>A mandatory planning input is absent, so even a draft plan is not actionable.</summary>
    Blocked,
}

/// <summary>Projection method selected by the active RulePack; the default is deliberately explicit.</summary>
public enum PartDrawingProjectionMethod
{
    /// <summary>First-angle projection.</summary>
    FirstAngle,

    /// <summary>Third-angle projection.</summary>
    ThirdAngle,
}

/// <summary>
/// Provider-neutral evidence for comparing a section and a detail candidate.
/// 用于比较剖视与局部放大候选的无厂商证据；候选只是规划输入，不会自行调用 SOLIDWORKS。
/// </summary>
public sealed record PartDrawingViewCandidate
{
    /// <summary>Stable candidate identity; it must not be a PDF path or source filename.</summary>
    public required string CandidateId { get; init; }

    /// <summary>Candidate role; only Section and Detail are currently selectable here.</summary>
    public required PartDrawingViewRole Role { get; init; }

    /// <summary>Named orientation or semantic view anchor.</summary>
    public required string Orientation { get; init; }

    /// <summary>Normalized manufacturing coverage gain in the inclusive range [0, 1].</summary>
    public required NormalizedScore CoverageGain { get; init; }

    /// <summary>Normalized readability gain in the inclusive range [0, 1].</summary>
    public required NormalizedScore ReadabilityGain { get; init; }

    /// <summary>Normalized internal-feature/manufacturing exposure gain in the inclusive range [0, 1].</summary>
    public required NormalizedScore ManufacturingExposureGain { get; init; }

    /// <summary>Non-secret evidence class such as provider.inspection or human_review.</summary>
    public required string EvidenceSource { get; init; }

    /// <summary>True only when provider inspection or an explicit human review verified the candidate.</summary>
    public bool IsProviderVerified { get; init; }

    internal NormalizedScore Score => NormalizedScore.FromPlanningEvidence(CoverageGain, ReadabilityGain, ManufacturingExposureGain);
}

/// <summary>Inputs to one pure, deterministic part drawing planning pass.</summary>
public sealed record PartDrawingPlanRequest
{
    /// <summary>Redacted requirement graph derived from model/PMI/rules/private review.</summary>
    public required PartDrawingRequirementSet Requirements { get; init; }

    /// <summary>
    /// A confirmed named orientation wins over the deterministic Front fallback.
    /// 已批准的 Named Orientation 优先于默认 Front；未批准时只能形成 review draft。
    /// </summary>
    public string PreferredPrimaryOrientation { get; init; } = "Front";

    /// <summary>Whether the preferred orientation was confirmed by model/provider evidence.</summary>
    public bool PreferredPrimaryOrientationApproved { get; init; }

    /// <summary>Projection method supplied by a versioned RulePack.</summary>
    public PartDrawingProjectionMethod Projection { get; init; } = PartDrawingProjectionMethod.FirstAngle;

    /// <summary>
    /// Resolved rule policy carried into the plan with field-level provenance.
    /// 将已 resolve 且带字段级 provenance 的规则策略带入 plan。
    /// </summary>
    /// <remarks>
    /// Null is retained only for legacy unit callers that explicitly exercise the low-level planner contract. The
    /// high-level compiler path should always resolve a RulePack before planning.
    /// null 只为兼容显式测试低层 planner contract；高层 compiler 路径应始终先 resolve RulePack。
    /// </remarks>
    public ResolvedDrawingRulePack? RulePack { get; init; }

    /// <summary>Comparable section/detail candidates; source paths and raw drawing text are forbidden.</summary>
    public ImmutableArray<PartDrawingViewCandidate> ViewCandidates { get; init; } = [];
}

/// <summary>One high-level plan item that a provider adapter may later materialize.</summary>
public sealed record PartDrawingPlanItem
{
    /// <summary>Stable role identity inside the plan.</summary>
    public required string ItemId { get; init; }

    /// <summary>High-level drawing role.</summary>
    public required PartDrawingViewRole Role { get; init; }

    /// <summary>Semantic orientation selected for the view.</summary>
    public required string Orientation { get; init; }

    /// <summary>Requirement coverage keys intentionally visible to QA.</summary>
    public ImmutableArray<string> CoverageKeys { get; init; } = [];

    /// <summary>Deterministic explanation suitable for audit logs.</summary>
    public required string Rationale { get; init; }

    /// <summary>Candidate identity when the item came from a scored comparison.</summary>
    public string? CandidateId { get; init; }

    /// <summary>Whether this item must remain in review before drawing release.</summary>
    public bool RequiresReview { get; init; }
}

/// <summary>
/// Immutable output of the part Drawing Compiler planning stage.
/// 零件 Drawing Compiler planning 阶段的 immutable 输出；它只规划，不修改 CAD 文档。
/// </summary>
public sealed record PartDrawingPlan
{
    /// <summary>Schema version for future persisted plan migration.</summary>
    public const string SchemaVersion = "1.0";

    /// <summary>Requirement profile used to produce the plan.</summary>
    public required string ProfileId { get; init; }

    /// <summary>Planning status.</summary>
    public required PartDrawingPlanningStatus Status { get; init; }

    /// <summary>Projection method chosen by policy.</summary>
    public required PartDrawingProjectionMethod Projection { get; init; }

    /// <summary>Resolved rule policy and provenance used by this plan.</summary>
    /// <summary>本 plan 使用的 resolved rule policy 与 provenance。</summary>
    public ResolvedDrawingRulePack? RulePack { get; init; }

    /// <summary>Deterministically ordered view plan items.</summary>
    public ImmutableArray<PartDrawingPlanItem> Items { get; init; } = [];

    /// <summary>Stable diagnostic codes, never raw source content.</summary>
    public ImmutableArray<string> Diagnostics { get; init; } = [];

    /// <summary>Required requirements that still block release/generation readiness.</summary>
    public ImmutableArray<string> BlockingCoverageKeys { get; init; } = [];

    /// <summary>True only when the plan has enough inputs for an actionable provider generation call.</summary>
    public bool CanGenerate => Status is not PartDrawingPlanningStatus.Blocked;

    /// <summary>True only when every required requirement and every plan decision is approved.</summary>
    public bool CanRelease => Status is PartDrawingPlanningStatus.ReadyForGeneration && Diagnostics.Length == 0;
}

/// <summary>
/// Pure planner for a single-part drawing.  It intentionally emits a small high-level plan rather than a sequence of
/// low-level add-view/add-dimension calls.  单零件纯规划器只输出少量高层决策，不把出图退化成数百个 primitive tool 调用。
/// </summary>
public static class PartDrawingPlanner
{
    /// <summary>
    /// Builds a deterministic plan from approved engineering semantics and provider-neutral evidence.
    /// 根据已批准工程语义与无厂商证据生成确定性计划；该方法没有 COM、副作用或文件访问。
    /// </summary>
    public static PartDrawingPlan Plan(PartDrawingPlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Requirements);

        var diagnostics = ImmutableArray.CreateBuilder<string>();
        var items = ImmutableArray.CreateBuilder<PartDrawingPlanItem>();
        var blockingCoverageKeys = ImmutableArray.CreateBuilder<string>();
        PartDrawingRequirementSet requirements = request.Requirements;

        ValidateCandidates(request.ViewCandidates);

        PartDrawingRequirement? orthographic = Find(requirements, PartDrawingSemanticClass.OrthographicViews);
        if (orthographic is null)
        {
            diagnostics.Add("missing-requirement:views.orthographic.coverage");
            blockingCoverageKeys.Add("views.orthographic.coverage");
            return BuildPlan(request, PartDrawingPlanningStatus.Blocked, items, diagnostics, blockingCoverageKeys);
        }

        string primaryOrientation = RequireNonBlank(request.PreferredPrimaryOrientation, nameof(request.PreferredPrimaryOrientation));
        items.Add(
            new PartDrawingPlanItem
            {
                ItemId = "view.primary",
                Role = PartDrawingViewRole.Primary,
                Orientation = primaryOrientation,
                CoverageKeys = [orthographic.CoverageKey],
                Rationale = request.PreferredPrimaryOrientationApproved
                    ? "approved-named-orientation"
                    : "deterministic-front-orientation-fallback",
                RequiresReview = !request.PreferredPrimaryOrientationApproved,
            });
        items.Add(
            new PartDrawingPlanItem
            {
                ItemId = "view.projected-01",
                Role = PartDrawingViewRole.Projected,
                Orientation = ProjectionPeerOrientation(primaryOrientation),
                CoverageKeys = [orthographic.CoverageKey],
                Rationale = "orthographic-coverage-and-dimensionability",
                RequiresReview = false,
            });

        PartDrawingRequirement? isometric = Find(requirements, PartDrawingSemanticClass.IsometricView);
        if (isometric is not null)
        {
            items.Add(
                new PartDrawingPlanItem
                {
                    ItemId = "view.isometric",
                    Role = PartDrawingViewRole.Isometric,
                    Orientation = "Isometric",
                    CoverageKeys = [isometric.CoverageKey],
                    Rationale = "communication-view-only; never-replaces-manufacturing-coverage",
                    RequiresReview = isometric.Status is not (EngineeringRequirementStatus.Approved or EngineeringRequirementStatus.Released),
                });
        }

        PartDrawingRequirement? sectionOrDetail = Find(requirements, PartDrawingSemanticClass.SectionOrDetailCandidate);
        if (sectionOrDetail is not null)
        {
            PartDrawingViewCandidate? candidate = SelectCandidate(request.ViewCandidates);
            if (candidate is null)
            {
                diagnostics.Add("missing-evidence:view-section-or-detail-candidate");
                blockingCoverageKeys.Add(sectionOrDetail.CoverageKey);
            }
            else
            {
                items.Add(
                    new PartDrawingPlanItem
                    {
                        ItemId = candidate.Role is PartDrawingViewRole.Section ? "view.section-01" : "view.detail-01",
                        Role = candidate.Role,
                        Orientation = candidate.Orientation,
                        CoverageKeys = [sectionOrDetail.CoverageKey],
                        CandidateId = candidate.CandidateId,
                        Rationale = $"weighted-score:{candidate.Score.Value:0.000};evidence:{candidate.EvidenceSource}",
                        RequiresReview = !candidate.IsProviderVerified || sectionOrDetail.Status is not (EngineeringRequirementStatus.Approved or EngineeringRequirementStatus.Released),
                    });
            }
        }

        foreach (PartDrawingRequirement requirement in requirements.Requirements.Where(value => value.Required &&
                     value.Status is not (EngineeringRequirementStatus.Approved or EngineeringRequirementStatus.Released)))
        {
            blockingCoverageKeys.Add(requirement.CoverageKey);
        }

        if (!request.PreferredPrimaryOrientationApproved)
        {
            diagnostics.Add("review-required:primary-orientation-evidence");
        }

        PartDrawingPlanningStatus status = diagnostics.Count > 0 || blockingCoverageKeys.Count > 0
            ? PartDrawingPlanningStatus.ReviewRequired
            : PartDrawingPlanningStatus.ReadyForGeneration;

        return BuildPlan(request, status, items, diagnostics, blockingCoverageKeys);
    }

    private static PartDrawingPlan BuildPlan(
        PartDrawingPlanRequest request,
        PartDrawingPlanningStatus status,
        ImmutableArray<PartDrawingPlanItem>.Builder items,
        ImmutableArray<string>.Builder diagnostics,
        ImmutableArray<string>.Builder blockingCoverageKeys) =>
        new()
        {
            ProfileId = request.Requirements.ProfileId,
            Status = status,
            Projection = request.RulePack is null
                ? request.Projection
                : ToPlannerProjection(request.RulePack.Values.ProjectionMethod),
            RulePack = request.RulePack,
            Items = [.. items],
            Diagnostics = [.. diagnostics.Distinct(StringComparer.Ordinal)],
            BlockingCoverageKeys = [.. blockingCoverageKeys.Distinct(StringComparer.Ordinal)],
        };

    private static PartDrawingProjectionMethod ToPlannerProjection(DrawingProjectionMethod method) =>
        method switch
        {
            DrawingProjectionMethod.FirstAngle => PartDrawingProjectionMethod.FirstAngle,
            DrawingProjectionMethod.ThirdAngle => PartDrawingProjectionMethod.ThirdAngle,
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown RulePack projection method."),
        };

    private static PartDrawingRequirement? Find(PartDrawingRequirementSet requirements, PartDrawingSemanticClass semanticClass) =>
        requirements.Requirements.FirstOrDefault(requirement => requirement.SemanticClass == semanticClass);

    private static PartDrawingViewCandidate? SelectCandidate(ImmutableArray<PartDrawingViewCandidate> candidates) =>
        candidates
            .Where(candidate => candidate.Role is PartDrawingViewRole.Section or PartDrawingViewRole.Detail)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Role == PartDrawingViewRole.Section ? 0 : 1)
            .ThenBy(candidate => candidate.CandidateId, StringComparer.Ordinal)
            .FirstOrDefault();

    private static void ValidateCandidates(ImmutableArray<PartDrawingViewCandidate> candidates)
    {
        if (candidates.IsDefault)
        {
            return;
        }

        if (candidates.Any(candidate => candidate is null))
        {
            throw new ArgumentException("View candidates cannot contain null values.", nameof(candidates));
        }

        if (candidates.Select(candidate => candidate.CandidateId).Distinct(StringComparer.Ordinal).Count() != candidates.Length)
        {
            throw new ArgumentException("View candidate identities must be unique.", nameof(candidates));
        }

        foreach (PartDrawingViewCandidate candidate in candidates)
        {
            RequireNonBlank(candidate.CandidateId, nameof(candidate.CandidateId));
            RequireNonBlank(candidate.Orientation, nameof(candidate.Orientation));
            RequireNonBlank(candidate.EvidenceSource, nameof(candidate.EvidenceSource));
            if (candidate.Role is not (PartDrawingViewRole.Section or PartDrawingViewRole.Detail))
            {
                throw new ArgumentException("Only section/detail candidates may be scored by this planner.", nameof(candidates));
            }

        }
    }

    private static string ProjectionPeerOrientation(string primaryOrientation) =>
        primaryOrientation.Equals("Front", StringComparison.OrdinalIgnoreCase) ? "Top" : "Projected";

    private static string RequireNonBlank(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}
