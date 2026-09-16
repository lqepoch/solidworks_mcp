using System.Collections.Immutable;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.EngineeringModel;

namespace SolidWorksMcp.AutoDrawing;

/// <summary>Machine-readable severity emitted by the part drawing coverage gate.</summary>
public enum PartDrawingCoverageStatus
{
    /// <summary>The approved requirement has explicit annotation evidence.</summary>
    Pass,

    /// <summary>An optional requirement or non-fatal hygiene issue needs attention.</summary>
    Warning,

    /// <summary>The annotation exists, but provenance or planning approval is unresolved.</summary>
    ReviewRequired,

    /// <summary>A required requirement has no explicit coverage, or the plan is unusable.</summary>
    Blocking,
}

/// <summary>One deterministic, text-independent coverage finding.</summary>
public sealed record PartDrawingCoverageFinding
{
    /// <summary>Requirement coverage key or a stable plan-level diagnostic key.</summary>
    public required string CoverageKey { get; init; }

    /// <summary>Finding severity.</summary>
    public required PartDrawingCoverageStatus Status { get; init; }

    /// <summary>Stable diagnostic code suitable for release logs and API responses.</summary>
    public required string Code { get; init; }

    /// <summary>Number of annotations that explicitly claim this coverage key.</summary>
    public required int AnnotationCount { get; init; }

    /// <summary>Short non-secret explanation; source text is intentionally excluded.</summary>
    public required string Explanation { get; init; }
}

/// <summary>
/// Immutable result of one single-part drawing coverage analysis.
/// 单零件工程图 coverage analysis 的 immutable 结果；它不读取或解析标注显示文本。
/// </summary>
public sealed record PartDrawingCoverageReport
{
    /// <summary>Schema version for persisted QA reports.</summary>
    public const string SchemaVersion = "1.0";

    /// <summary>Requirement profile under inspection.</summary>
    public required string ProfileId { get; init; }

    /// <summary>Deterministically ordered findings.</summary>
    public ImmutableArray<PartDrawingCoverageFinding> Findings { get; init; } = [];

    /// <summary>True only when no blocking or unresolved review finding remains.</summary>
    public bool CanRelease => Findings.All(finding => finding.Status is PartDrawingCoverageStatus.Pass or PartDrawingCoverageStatus.Warning);
}

/// <summary>
/// Performs requirement coverage QA from explicit semantic keys.
/// 使用显式语义 key 执行 requirement coverage QA；不会用 OCR、标注文本相似度或截图作为工程证据。
/// </summary>
public static class PartDrawingCoverageAnalyzer
{
    /// <summary>
    /// Analyzes one drawing inspection against its requirement graph and high-level plan.
    /// 将 drawing inspection 与 requirement graph、高层 plan 对照；该阶段仍是纯 vendor-neutral 计算。
    /// </summary>
    public static PartDrawingCoverageReport Analyze(
        PartDrawingRequirementSet requirements,
        PartDrawingPlan plan,
        CadInspectionSnapshot drawingInspection)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(drawingInspection);

        var findings = ImmutableArray.CreateBuilder<PartDrawingCoverageFinding>();
        if (plan.Status is PartDrawingPlanningStatus.Blocked)
        {
            findings.Add(
                new PartDrawingCoverageFinding
                {
                    CoverageKey = "plan",
                    Status = PartDrawingCoverageStatus.Blocking,
                    Code = "drawing-plan-blocked",
                    AnnotationCount = 0,
                    Explanation = "The high-level drawing plan has a mandatory missing input.",
                });
        }
        else if (plan.Status is PartDrawingPlanningStatus.ReviewRequired)
        {
            findings.Add(
                new PartDrawingCoverageFinding
                {
                    CoverageKey = "plan",
                    Status = PartDrawingCoverageStatus.ReviewRequired,
                    Code = "drawing-plan-review-required",
                    AnnotationCount = 0,
                    Explanation = "The plan contains an unapproved orientation, candidate or requirement.",
                });
        }

        ImmutableHashSet<string> requirementKeys = [.. requirements.Requirements.Select(requirement => requirement.CoverageKey)];
        foreach (PartDrawingRequirement requirement in requirements.Requirements.OrderBy(value => value.CoverageKey, StringComparer.Ordinal))
        {
            int annotationCount = drawingInspection.Annotations.Count(annotation => annotation.CoverageKeys.Contains(requirement.CoverageKey, StringComparer.Ordinal));
            bool approved = requirement.Status is EngineeringRequirementStatus.Approved or EngineeringRequirementStatus.Released;
            PartDrawingCoverageFinding finding = annotationCount == 0
                ? MissingFinding(requirement, approved)
                : CoveredFinding(requirement, annotationCount, approved);
            findings.Add(finding);
        }

        foreach (string orphanKey in drawingInspection.Annotations
                     .SelectMany(annotation => annotation.CoverageKeys)
                     .Where(key => !requirementKeys.Contains(key))
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(key => key, StringComparer.Ordinal))
        {
            findings.Add(
                new PartDrawingCoverageFinding
                {
                    CoverageKey = orphanKey,
                    Status = PartDrawingCoverageStatus.Warning,
                    Code = "orphan-coverage-key",
                    AnnotationCount = drawingInspection.Annotations.Count(annotation => annotation.CoverageKeys.Contains(orphanKey, StringComparer.Ordinal)),
                    Explanation = "The annotation references a coverage key absent from this requirement graph.",
                });
        }

        return new PartDrawingCoverageReport
        {
            ProfileId = requirements.ProfileId,
            Findings = [.. findings
                .OrderBy(finding => finding.Status)
                .ThenBy(finding => finding.CoverageKey, StringComparer.Ordinal)],
        };
    }

    private static PartDrawingCoverageFinding MissingFinding(PartDrawingRequirement requirement, bool approved) =>
        new()
        {
            CoverageKey = requirement.CoverageKey,
            Status = requirement.Required
                ? approved ? PartDrawingCoverageStatus.Blocking : PartDrawingCoverageStatus.ReviewRequired
                : PartDrawingCoverageStatus.Warning,
            Code = requirement.Required ? "missing-required-coverage" : "missing-optional-coverage",
            AnnotationCount = 0,
            Explanation = requirement.Required
                ? approved
                    ? "A required approved engineering requirement has no explicit annotation coverage."
                    : "A required requirement is unresolved and has no explicit annotation coverage."
                : "An optional requirement has no explicit annotation coverage.",
        };

    private static PartDrawingCoverageFinding CoveredFinding(PartDrawingRequirement requirement, int annotationCount, bool approved) =>
        new()
        {
            CoverageKey = requirement.CoverageKey,
            Status = approved ? PartDrawingCoverageStatus.Pass : PartDrawingCoverageStatus.ReviewRequired,
            Code = approved ? "coverage-present" : "coverage-source-review-required",
            AnnotationCount = annotationCount,
            Explanation = approved
                ? "Explicit semantic annotation coverage is present."
                : "Coverage exists, but the engineering requirement still needs approval.",
        };
}
