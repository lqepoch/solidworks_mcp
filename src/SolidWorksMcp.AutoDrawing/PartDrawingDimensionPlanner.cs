using System.Collections.Immutable;
using SolidWorksMcp.EngineeringModel;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.AutoDrawing;

/// <summary>Action selected for one explicit feature-definition requirement.</summary>
public enum PartDrawingDimensionPlanAction
{
    /// <summary>Retain one verified associative dimension.</summary>
    RetainExisting,

    /// <summary>Create one missing dimension through a later provider materialization stage.</summary>
    Create,

    /// <summary>Keep the evidence out of release until association/classification/visibility is reviewed.</summary>
    ReviewExisting,

    /// <summary>Do not create a duplicate; one deterministic winner already covers the definition.</summary>
    SuppressRedundant,
}

/// <summary>Machine-readable result severity for one dimension coverage finding.</summary>
public enum PartDrawingDimensionFindingStatus
{
    Pass,
    Warning,
    ReviewRequired,
    Blocking,
}

/// <summary>Overall state of a dimension planning/coverage pass.</summary>
public enum PartDrawingDimensionPlanStatus
{
    Ready,
    ReviewRequired,
    Blocked,
}

/// <summary>One exact feature-definition coverage finding.</summary>
public sealed record PartDrawingDimensionCoverageFinding
{
    /// <summary>Stable graph coverage key.</summary>
    public required string CoverageKey { get; init; }

    /// <summary>Stable feature identity.</summary>
    public required FeatureId FeatureId { get; init; }

    /// <summary>Diagnostic feature name, never raw source drawing text.</summary>
    public required string FeatureName { get; init; }

    /// <summary>Definition key inside the feature.</summary>
    public required string DefinitionKey { get; init; }

    /// <summary>Exact missing/covered definition label.</summary>
    public required string DefinitionName { get; init; }

    /// <summary>Expected Functional/Manufacturing/Reference class.</summary>
    public required DrawingDimensionClassification Classification { get; init; }

    /// <summary>Finding status.</summary>
    public required PartDrawingDimensionFindingStatus Status { get; init; }

    /// <summary>Stable diagnostic code.</summary>
    public required string Code { get; init; }

    /// <summary>Number of exact evidence records matched to this definition.</summary>
    public required int EvidenceCount { get; init; }

    /// <summary>Exact non-secret explanation, for example <c>Slot S03: X position is missing.</c>.</summary>
    public required string Explanation { get; init; }
}

/// <summary>One deterministic provider-neutral dimension plan item.</summary>
public sealed record PartDrawingDimensionPlanItem
{
    /// <summary>Stable feature identity.</summary>
    public required FeatureId FeatureId { get; init; }

    /// <summary>Feature display name.</summary>
    public required string FeatureName { get; init; }

    /// <summary>Definition key.</summary>
    public required string DefinitionKey { get; init; }

    /// <summary>Expected engineering classification.</summary>
    public required DrawingDimensionClassification Classification { get; init; }

    /// <summary>Chosen action.</summary>
    public required PartDrawingDimensionPlanAction Action { get; init; }

    /// <summary>Existing native dimension identity when one was selected as the deterministic winner.</summary>
    public DimensionId? ExistingDimensionId { get; init; }

    /// <summary>Selected datum identity, if the requirement has one.</summary>
    public string? DatumId { get; init; }

    /// <summary>Stable explanation suitable for an audit event.</summary>
    public required string Rationale { get; init; }
}

/// <summary>One datum selected by the deterministic functional datum strategy.</summary>
public sealed record PartDrawingSelectedDatum
{
    /// <summary>Feature whose requirement selected this datum.</summary>
    public required FeatureId FeatureId { get; init; }

    /// <summary>Stable datum identity.</summary>
    public required string DatumId { get; init; }

    /// <summary>Datum role.</summary>
    public required DrawingDatumRole Role { get; init; }

    /// <summary>Why this datum won deterministic selection.</summary>
    public required string Rationale { get; init; }
}

/// <summary>
/// Immutable dimension plan and feature-by-feature coverage graph result.
/// immutable dimension plan 与逐特征 coverage graph 的结果；它不调用 COM，也不读取 PDF。
/// </summary>
public sealed record PartDrawingDimensionPlan
{
    /// <summary>Persisted result schema version.</summary>
    public const string SchemaVersion = "1.0";

    /// <summary>Requirement graph profile.</summary>
    public required string ProfileId { get; init; }

    /// <summary>Overall plan status.</summary>
    public required PartDrawingDimensionPlanStatus Status { get; init; }

    /// <summary>Explicit action for every required/optional dimension definition.</summary>
    public ImmutableArray<PartDrawingDimensionPlanItem> Items { get; init; } = [];

    /// <summary>One finding per feature definition, deterministically ordered.</summary>
    public ImmutableArray<PartDrawingDimensionCoverageFinding> Findings { get; init; } = [];

    /// <summary>Selected datum strategy.</summary>
    public ImmutableArray<PartDrawingSelectedDatum> SelectedDatums { get; init; } = [];

    /// <summary>Stable planner diagnostics, never raw source text.</summary>
    public ImmutableArray<string> Diagnostics { get; init; } = [];

    /// <summary>True only when no blocking or review finding remains.</summary>
    public bool CanRelease => Status is PartDrawingDimensionPlanStatus.Ready;
}

/// <summary>
/// Deterministic planner for Functional/Manufacturing/Reference dimensions and datum selection.
/// Functional/Manufacturing/Reference 尺寸和 datum 选择的确定性 planner。
/// </summary>
public static class PartDrawingDimensionPlanner
{
    /// <summary>
    /// Plans explicit definitions against native/PMI evidence. It never treats dimension count or visible text as proof.
    /// 根据 native/PMI evidence 对显式 definition 做规划；绝不把尺寸数量或可见文字当作工程覆盖证明。
    /// </summary>
    public static PartDrawingDimensionPlan Plan(
        PartDrawingDimensionRequirementGraph graph,
        IEnumerable<PartDrawingDimensionEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(evidence);

        ImmutableArray<PartDrawingDimensionEvidence> evidenceItems = [.. evidence];
        ValidateEvidence(evidenceItems);
        var items = ImmutableArray.CreateBuilder<PartDrawingDimensionPlanItem>();
        var findings = ImmutableArray.CreateBuilder<PartDrawingDimensionCoverageFinding>();
        var selectedDatums = ImmutableArray.CreateBuilder<PartDrawingSelectedDatum>();
        var diagnostics = ImmutableArray.CreateBuilder<string>();

        foreach (PartDrawingFeatureRequirement feature in graph.Features.OrderBy(value => value.FeatureId.Value, StringComparer.Ordinal))
        {
            PlanDatums(feature, selectedDatums, diagnostics);
            foreach (PartDrawingDimensionDefinition definition in feature.Definitions
                         .OrderBy(value => value.DefinitionKey, StringComparer.Ordinal))
            {
                string coverageKey = PartDrawingDimensionRequirementGraph.CoverageKey(feature.FeatureId, definition.DefinitionKey);
                PartDrawingDimensionEvidence[] matches = [.. evidenceItems.Where(item =>
                    item.FeatureId.Equals(feature.FeatureId)
                    && item.DefinitionKey.Equals(definition.DefinitionKey, StringComparison.Ordinal))];
                PartDrawingDimensionEvidence? winner = matches
                    .OrderByDescending(item => item.IsAssociative)
                    .ThenByDescending(item => item.IsVisible)
                    .ThenBy(item => item.DimensionId.Value, StringComparer.Ordinal)
                    .FirstOrDefault();

                PartDrawingDimensionCoverageFinding finding = CreateFinding(feature, definition, coverageKey, matches, winner);
                findings.Add(finding);
                items.Add(
                    new PartDrawingDimensionPlanItem
                    {
                        FeatureId = feature.FeatureId,
                        FeatureName = feature.DisplayName.Trim(),
                        DefinitionKey = definition.DefinitionKey.Trim(),
                        Classification = definition.Classification,
                        Action = winner is null
                            ? PartDrawingDimensionPlanAction.Create
                            : finding.Status is PartDrawingDimensionFindingStatus.Pass or PartDrawingDimensionFindingStatus.Warning
                                ? PartDrawingDimensionPlanAction.RetainExisting
                                : PartDrawingDimensionPlanAction.ReviewExisting,
                        ExistingDimensionId = winner?.DimensionId,
                        DatumId = winner?.DatumId,
                        Rationale = finding.Explanation,
                    });

                if (matches.Length > 1)
                {
                    diagnostics.Add($"redundant-evidence:{feature.FeatureId.Value}:{definition.DefinitionKey}");
                    foreach (PartDrawingDimensionEvidence duplicate in matches.Where(item => item != winner))
                    {
                        items.Add(
                            new PartDrawingDimensionPlanItem
                            {
                                FeatureId = feature.FeatureId,
                                FeatureName = feature.DisplayName.Trim(),
                                DefinitionKey = definition.DefinitionKey.Trim(),
                                Classification = definition.Classification,
                                Action = PartDrawingDimensionPlanAction.SuppressRedundant,
                                ExistingDimensionId = duplicate.DimensionId,
                                DatumId = duplicate.DatumId,
                                Rationale = "duplicate-exact-definition;retain-stable-winner",
                            });
                    }
                }
            }
        }

        AddOrphanEvidenceFindings(graph, evidenceItems, findings, diagnostics);
        PartDrawingDimensionPlanStatus status = findings.Any(finding => finding.Status is PartDrawingDimensionFindingStatus.Blocking)
            ? PartDrawingDimensionPlanStatus.Blocked
            : findings.Any(finding => finding.Status is PartDrawingDimensionFindingStatus.ReviewRequired)
                || diagnostics.Any(value =>
                    value.StartsWith("missing-approved-datum:", StringComparison.Ordinal)
                    || value.StartsWith("multiple-approved-datum-candidates:", StringComparison.Ordinal))
                ? PartDrawingDimensionPlanStatus.ReviewRequired
                : PartDrawingDimensionPlanStatus.Ready;

        return new PartDrawingDimensionPlan
        {
            ProfileId = graph.ProfileId,
            Status = status,
            Items = [.. items
                .OrderBy(item => item.FeatureId.Value, StringComparer.Ordinal)
                .ThenBy(item => item.DefinitionKey, StringComparer.Ordinal)
                .ThenBy(item => item.Action)],
            Findings = [.. findings
                .OrderBy(finding => finding.FeatureId.Value, StringComparer.Ordinal)
                .ThenBy(finding => finding.DefinitionKey, StringComparer.Ordinal)],
            SelectedDatums = [.. selectedDatums
                .OrderBy(datum => datum.FeatureId.Value, StringComparer.Ordinal)
                .ThenBy(datum => datum.Role)
                .ThenBy(datum => datum.DatumId, StringComparer.Ordinal)],
            Diagnostics = [.. diagnostics.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal)],
        };
    }

    private static PartDrawingDimensionCoverageFinding CreateFinding(
        PartDrawingFeatureRequirement feature,
        PartDrawingDimensionDefinition definition,
        string coverageKey,
        PartDrawingDimensionEvidence[] matches,
        PartDrawingDimensionEvidence? winner)
    {
        bool approved = feature.Status is EngineeringRequirementStatus.Approved or EngineeringRequirementStatus.Released;
        string prefix = $"{feature.DisplayName.Trim()}: {definition.DisplayName.Trim()}";
        if (winner is null)
        {
            PartDrawingDimensionFindingStatus status = !definition.Required
                ? PartDrawingDimensionFindingStatus.Warning
                : approved && feature.Required
                    ? PartDrawingDimensionFindingStatus.Blocking
                    : PartDrawingDimensionFindingStatus.ReviewRequired;
            return Finding(feature, definition, coverageKey, status, definition.Required ? "missing-definition" : "missing-optional-definition", 0,
                $"{prefix} is missing.");
        }

        if (winner.Classification != definition.Classification)
        {
            return Finding(feature, definition, coverageKey, PartDrawingDimensionFindingStatus.ReviewRequired,
                "classification-mismatch", matches.Length, $"{prefix} has an incompatible dimension classification.");
        }

        if (!winner.IsAssociative || !winner.IsVisible)
        {
            return Finding(feature, definition, coverageKey, PartDrawingDimensionFindingStatus.ReviewRequired,
                !winner.IsAssociative ? "non-associative-dimension" : "dimension-not-visible", matches.Length,
                $"{prefix} exists but association/visibility evidence is incomplete.");
        }

        return Finding(feature, definition, coverageKey,
            matches.Length > 1 ? PartDrawingDimensionFindingStatus.Warning : PartDrawingDimensionFindingStatus.Pass,
            matches.Length > 1 ? "duplicate-dimension-evidence" : "dimension-covered", matches.Length,
            matches.Length > 1 ? $"{prefix} is covered; redundant definitions will be suppressed." : $"{prefix} is covered.");
    }

    private static PartDrawingDimensionCoverageFinding Finding(
        PartDrawingFeatureRequirement feature,
        PartDrawingDimensionDefinition definition,
        string coverageKey,
        PartDrawingDimensionFindingStatus status,
        string code,
        int evidenceCount,
        string explanation) =>
        new()
        {
            CoverageKey = coverageKey,
            FeatureId = feature.FeatureId,
            FeatureName = feature.DisplayName.Trim(),
            DefinitionKey = definition.DefinitionKey.Trim(),
            DefinitionName = definition.DisplayName.Trim(),
            Classification = definition.Classification,
            Status = status,
            Code = code,
            EvidenceCount = evidenceCount,
            Explanation = explanation,
        };

    private static void PlanDatums(
        PartDrawingFeatureRequirement feature,
        ImmutableArray<PartDrawingSelectedDatum>.Builder selected,
        ImmutableArray<string>.Builder diagnostics)
    {
        foreach (IGrouping<DrawingDatumRole, PartDrawingDatumRequirement> group in feature.DatumRequirements
                     .Where(value => value.Required)
                     .GroupBy(value => value.Role)
                     .OrderBy(value => value.Key))
        {
            PartDrawingDatumRequirement[] approved = [.. group
                .Where(value => value.Status is EngineeringRequirementStatus.Approved or EngineeringRequirementStatus.Released)
                .OrderBy(value => value.DatumId, StringComparer.Ordinal)];
            if (approved.Length == 0)
            {
                diagnostics.Add($"missing-approved-datum:{feature.FeatureId.Value}:{group.Key}");
                continue;
            }

            PartDrawingDatumRequirement winner = approved[0];
            selected.Add(
                new PartDrawingSelectedDatum
                {
                    FeatureId = feature.FeatureId,
                    DatumId = winner.DatumId.Trim(),
                    Role = winner.Role,
                    Rationale = approved.Length == 1
                        ? "approved-functional-datum"
                        : "stable-datum-id-tiebreak-after-multiple-approved-candidates",
                });
            if (approved.Length > 1)
            {
                diagnostics.Add($"multiple-approved-datum-candidates:{feature.FeatureId.Value}:{group.Key}");
            }
        }
    }

    private static void AddOrphanEvidenceFindings(
        PartDrawingDimensionRequirementGraph graph,
        IReadOnlyCollection<PartDrawingDimensionEvidence> evidence,
        ImmutableArray<PartDrawingDimensionCoverageFinding>.Builder findings,
        ImmutableArray<string>.Builder diagnostics)
    {
        ImmutableHashSet<string> known = [.. graph.Features.SelectMany(feature => feature.Definitions.Select(definition =>
            $"{feature.FeatureId.Value}\u001f{definition.DefinitionKey}"))];
        foreach (PartDrawingDimensionEvidence item in evidence
                     .Where(value => !known.Contains($"{value.FeatureId.Value}\u001f{value.DefinitionKey}"))
                     .OrderBy(value => value.FeatureId.Value, StringComparer.Ordinal)
                     .ThenBy(value => value.DefinitionKey, StringComparer.Ordinal)
                     .ThenBy(value => value.DimensionId.Value, StringComparer.Ordinal))
        {
            diagnostics.Add($"orphan-dimension-evidence:{item.FeatureId.Value}:{item.DefinitionKey}");
            findings.Add(
                new PartDrawingDimensionCoverageFinding
                {
                    CoverageKey = $"orphan:feature:{item.FeatureId.Value}:dimension:{item.DefinitionKey}",
                    FeatureId = item.FeatureId,
                    FeatureName = item.FeatureId.Value,
                    DefinitionKey = item.DefinitionKey,
                    DefinitionName = item.DefinitionKey,
                    Classification = item.Classification,
                    Status = PartDrawingDimensionFindingStatus.Warning,
                    Code = "orphan-dimension-evidence",
                    EvidenceCount = 1,
                    Explanation = "The dimension evidence references no definition in the current requirement graph.",
                });
        }
    }

    private static void ValidateEvidence(ImmutableArray<PartDrawingDimensionEvidence> evidence)
    {
        if (evidence.Any(item => item is null))
        {
            throw new ArgumentException("Dimension evidence cannot contain null values.", nameof(evidence));
        }

        foreach (PartDrawingDimensionEvidence item in evidence)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(item.DimensionId.Value);
            ArgumentException.ThrowIfNullOrWhiteSpace(item.FeatureId.Value);
            ArgumentException.ThrowIfNullOrWhiteSpace(item.DefinitionKey);
            ArgumentNullException.ThrowIfNull(item.Provenance);
        }
    }
}
