using System.Collections.Immutable;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.AutoDrawing;

/// <summary>
/// Manufacturing annotation categories understood by the provider-neutral drawing compiler.
/// 工程图编译器理解的、与 Provider 无关的制造标注类别。
/// </summary>
public enum ManufacturingAnnotationKind
{
    /// <summary>Associative model dimension imported from the native model/PMI store.</summary>
    ModelDimension,

    /// <summary>Hole Wizard or approved native hole callout.</summary>
    HoleCallout,

    /// <summary>Native center mark attached to a circular feature.</summary>
    CenterMark,

    /// <summary>Native centerline attached to explicit geometry.</summary>
    CenterLine,

    /// <summary>Datum feature symbol.</summary>
    Datum,

    /// <summary>Datum target symbol.</summary>
    DatumTarget,

    /// <summary>Geometric dimensioning and tolerancing frame.</summary>
    GeometricTolerance,

    /// <summary>Surface-finish symbol.</summary>
    SurfaceFinish,

    /// <summary>Weld symbol.</summary>
    WeldSymbol,

    /// <summary>Approved model or PMI note.</summary>
    Note,

    /// <summary>Cosmetic thread annotation.</summary>
    CosmeticThread,

    /// <summary>Associative model dimension with a native tolerance.</summary>
    TolerancedDimension,
}

/// <summary>Action selected for one manufacturing-annotation requirement.</summary>
public enum ManufacturingAnnotationPlanAction
{
    /// <summary>Keep one exact native annotation as release evidence.</summary>
    RetainNative,

    /// <summary>Ask the Provider to import one exact native Model Item category.</summary>
    ImportNativeModelItem,

    /// <summary>Do not mutate; the evidence needs human engineering review.</summary>
    ReviewRequired,
}

/// <summary>Finding severity for manufacturing annotation planning.</summary>
public enum ManufacturingAnnotationFindingStatus
{
    /// <summary>Requirement is covered by one approved associative native annotation.</summary>
    Pass,

    /// <summary>Requirement is executable but is not release evidence yet.</summary>
    Warning,

    /// <summary>Human approval, association, or an explicit Provider contract is required.</summary>
    ReviewRequired,

    /// <summary>Conflicting or duplicate evidence prevents a deterministic release decision.</summary>
    Blocking,
}

/// <summary>
/// One explicit manufacturing annotation requirement. It is an engineering identity, not a drawing-text request.
/// 一个明确的制造标注要求；它是工程 identity，而不是让系统照抄图纸文字的请求。
/// </summary>
public sealed record DrawingManufacturingAnnotationRequirement
{
    /// <summary>Stable requirement identity inside the Drawing Semantic Graph.</summary>
    public required string RequirementId { get; init; }

    /// <summary>Stable feature identity, for example HolePattern17 or Datum-A.</summary>
    public required string FeatureIdentity { get; init; }

    /// <summary>Required annotation category.</summary>
    public required ManufacturingAnnotationKind Kind { get; init; }

    /// <summary>Exact drawing view that owns the requirement.</summary>
    public required ViewId ViewId { get; init; }

    /// <summary>Requirement-graph coverage keys that the native annotation must prove.</summary>
    public ImmutableArray<string> CoverageKeys { get; init; } = [];

    /// <summary>Whether absence or ambiguity blocks a drawing release.</summary>
    public bool IsCritical { get; init; } = true;
}

/// <summary>
/// Evidence already observed in the native drawing/model. Matching is exact; visible text is never used as a key.
/// 已从 native drawing/model 观察到的 evidence；匹配必须 exact，绝不使用可见文字作为 key。
/// </summary>
public sealed record DrawingManufacturingAnnotationEvidence
{
    /// <summary>Stable evidence identity.</summary>
    public required string EvidenceId { get; init; }

    /// <summary>Requirement identity claimed by the higher-level graph.</summary>
    public required string RequirementId { get; init; }

    /// <summary>Stable feature identity used to prevent cross-feature promotion.</summary>
    public required string FeatureIdentity { get; init; }

    /// <summary>Native annotation category.</summary>
    public required ManufacturingAnnotationKind Kind { get; init; }

    /// <summary>Exact drawing view that owns the native annotation.</summary>
    public required ViewId ViewId { get; init; }

    /// <summary>Provider/native annotation identity when the object is persisted.</summary>
    public AnnotationId? AnnotationId { get; init; }

    /// <summary>Provenance class such as model_native, pmi, hole_wizard or human_approved.</summary>
    public required string ProvenanceKind { get; init; }

    /// <summary>Concrete provenance method such as insert_model_annotations3.</summary>
    public required string ProvenanceMethod { get; init; }

    /// <summary>Approval gate for this engineering evidence.</summary>
    public required DrawingAnnotationApprovalState ApprovalState { get; init; }

    /// <summary>Whether the native object remains associatively attached to model/PMI geometry.</summary>
    public bool IsAssociative { get; init; }

    /// <summary>Whether more than one candidate or an unresolved association was observed.</summary>
    public bool IsAmbiguous { get; init; }

    /// <summary>Coverage keys explicitly carried by the evidence.</summary>
    public ImmutableArray<string> CoverageKeys { get; init; } = [];
}

/// <summary>One deterministic action and finding for a manufacturing annotation requirement.</summary>
public sealed record DrawingManufacturingAnnotationPlanItem
{
    /// <summary>Stable requirement identity.</summary>
    public required string RequirementId { get; init; }

    /// <summary>Feature identity copied from the requirement.</summary>
    public required string FeatureIdentity { get; init; }

    /// <summary>Required annotation category.</summary>
    public required ManufacturingAnnotationKind Kind { get; init; }

    /// <summary>Selected action.</summary>
    public required ManufacturingAnnotationPlanAction Action { get; init; }

    /// <summary>Finding status.</summary>
    public required ManufacturingAnnotationFindingStatus Status { get; init; }

    /// <summary>Stable diagnostic code.</summary>
    public required string Code { get; init; }

    /// <summary>Existing native annotation identity when evidence was retained.</summary>
    public AnnotationId? ExistingAnnotationId { get; init; }

    /// <summary>Provider-neutral request for the native Model Items path, when supported.</summary>
    public DrawingAnnotationRequest? NativeRequest { get; init; }

    /// <summary>Short non-secret rationale suitable for audit and explain output.</summary>
    public required string Rationale { get; init; }
}

/// <summary>Complete deterministic manufacturing-annotation plan.</summary>
public sealed record DrawingManufacturingAnnotationPlan
{
    /// <summary>Persisted result schema version.</summary>
    public const string SchemaVersion = "1.0";

    /// <summary>Ordered plan items.</summary>
    public ImmutableArray<DrawingManufacturingAnnotationPlanItem> Items { get; init; } = [];

    /// <summary>True only when every critical requirement has approved associative native evidence.</summary>
    public bool CanRelease => Items.All(item => item.Status is ManufacturingAnnotationFindingStatus.Pass);

    /// <summary>Stable fingerprint input used by audit/checkpoint layers.</summary>
    public string Fingerprint { get; init; } = string.Empty;
}

/// <summary>
/// Deterministic planner for native manufacturing annotations.
/// 制造标注 native materialization 的确定性 planner。
/// </summary>
/// <remarks>
/// The planner intentionally knows only the provider-neutral Model Items contract. It never invents a hole diameter,
/// GD&amp;T frame, datum, fit or surface-finish value from geometry or text. Unsupported categories become an explicit
/// review finding until a provider API with a selector and read-back proof exists. planner 只知道 vendor-neutral
/// Model Items contract；绝不从几何或文字猜孔径、GD&amp;T、datum、配合或粗糙度。尚无安全 Provider API 和回读证明的
/// 类别必须显式进入人工复核。
/// </remarks>
public static class ManufacturingAnnotationPlanner
{
    /// <summary>Builds a deterministic plan from exact requirements and exact observed evidence.</summary>
    public static DrawingManufacturingAnnotationPlan Plan(
        IEnumerable<DrawingManufacturingAnnotationRequirement> requirements,
        IEnumerable<DrawingManufacturingAnnotationEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(evidence);

        DrawingManufacturingAnnotationRequirement[] orderedRequirements = [..
            requirements.OrderBy(item => item.RequirementId, StringComparer.Ordinal)];
        DrawingManufacturingAnnotationEvidence[] evidenceItems = [.. evidence];
        ValidateRequirements(orderedRequirements);
        ValidateEvidence(evidenceItems);

        var items = ImmutableArray.CreateBuilder<DrawingManufacturingAnnotationPlanItem>();
        foreach (DrawingManufacturingAnnotationRequirement requirement in orderedRequirements)
        {
            DrawingManufacturingAnnotationEvidence[] matches = [.. evidenceItems.Where(item =>
                item.RequirementId.Equals(requirement.RequirementId, StringComparison.Ordinal)
                && item.FeatureIdentity.Equals(requirement.FeatureIdentity, StringComparison.Ordinal)
                && item.Kind == requirement.Kind
                && item.ViewId == requirement.ViewId)];

            items.Add(PlanOne(requirement, matches));
        }

        ImmutableArray<DrawingManufacturingAnnotationPlanItem> planItems = items.ToImmutable();
        string fingerprint = ComputeFingerprint(planItems);
        return new DrawingManufacturingAnnotationPlan
        {
            Items = planItems,
            Fingerprint = fingerprint,
        };
    }

    private static DrawingManufacturingAnnotationPlanItem PlanOne(
        DrawingManufacturingAnnotationRequirement requirement,
        DrawingManufacturingAnnotationEvidence[] matches)
    {
        if (matches.Length > 1 || matches.Any(item => item.IsAmbiguous))
        {
            return Review(
                requirement,
                "annotation-evidence-ambiguous",
                ManufacturingAnnotationFindingStatus.Blocking,
                "More than one native candidate or an explicit ambiguity flag matched the exact requirement.");
        }

        DrawingManufacturingAnnotationEvidence? match = matches.SingleOrDefault();
        if (match is not null)
        {
            if (!match.IsAssociative)
            {
                return Review(
                    requirement,
                    "annotation-not-associative",
                    requirement.IsCritical ? ManufacturingAnnotationFindingStatus.Blocking : ManufacturingAnnotationFindingStatus.ReviewRequired,
                    "The native annotation exists, but its association to model/PMI geometry was not proved.");
            }

            if (match.ApprovalState is not (DrawingAnnotationApprovalState.Approved or DrawingAnnotationApprovalState.Released))
            {
                return Review(
                    requirement,
                    "annotation-approval-required",
                    requirement.IsCritical ? ManufacturingAnnotationFindingStatus.Blocking : ManufacturingAnnotationFindingStatus.ReviewRequired,
                    "Native annotation provenance exists, but engineering approval is not Approved or Released.");
            }

            if (string.IsNullOrWhiteSpace(match.ProvenanceKind) || string.IsNullOrWhiteSpace(match.ProvenanceMethod))
            {
                return Review(
                    requirement,
                    "annotation-provenance-missing",
                    requirement.IsCritical ? ManufacturingAnnotationFindingStatus.Blocking : ManufacturingAnnotationFindingStatus.ReviewRequired,
                    "Native annotation is present but its provenance class and method are incomplete.");
            }

            return new DrawingManufacturingAnnotationPlanItem
            {
                RequirementId = requirement.RequirementId,
                FeatureIdentity = requirement.FeatureIdentity,
                Kind = requirement.Kind,
                Action = ManufacturingAnnotationPlanAction.RetainNative,
                Status = ManufacturingAnnotationFindingStatus.Pass,
                Code = "annotation-native-approved",
                ExistingAnnotationId = match.AnnotationId,
                Rationale = "One exact associative native annotation has approved provenance and can remain in the drawing.",
            };
        }

        if (!TryGetModelItemKinds(requirement.Kind, out DrawingModelAnnotationImportKinds modelItemKinds))
        {
            return Review(
                requirement,
                "annotation-provider-contract-required",
                requirement.IsCritical ? ManufacturingAnnotationFindingStatus.Blocking : ManufacturingAnnotationFindingStatus.ReviewRequired,
                "This annotation requires a dedicated native selector/materializer; the generic Model Items import path is not sufficient.");
        }

        if (string.IsNullOrWhiteSpace(requirement.FeatureIdentity))
        {
            return Review(
                requirement,
                "annotation-feature-identity-required",
                ManufacturingAnnotationFindingStatus.Blocking,
                "Native annotation import cannot be safely scoped without a stable feature identity.");
        }

        var nativeRequest = new DrawingAnnotationRequest
        {
            ViewId = requirement.ViewId,
            Kind = "model-items",
            ModelItemKinds = modelItemKinds,
            FeatureIdentity = requirement.FeatureIdentity,
            ProvenanceKind = "model_native",
            ProvenanceMethod = "insert_model_annotations3",
            ApprovalState = DrawingAnnotationApprovalState.Approved,
            CoverageKeys = requirement.CoverageKeys,
        };
        return new DrawingManufacturingAnnotationPlanItem
        {
            RequirementId = requirement.RequirementId,
            FeatureIdentity = requirement.FeatureIdentity,
            Kind = requirement.Kind,
            Action = ManufacturingAnnotationPlanAction.ImportNativeModelItem,
            Status = ManufacturingAnnotationFindingStatus.Warning,
            Code = "annotation-native-import-planned",
            NativeRequest = nativeRequest,
            Rationale = "No evidence was observed; an approved native Model Items import is planned and must be read back before release.",
        };
    }

    private static DrawingManufacturingAnnotationPlanItem Review(
        DrawingManufacturingAnnotationRequirement requirement,
        string code,
        ManufacturingAnnotationFindingStatus status,
        string rationale) => new()
        {
            RequirementId = requirement.RequirementId,
            FeatureIdentity = requirement.FeatureIdentity,
            Kind = requirement.Kind,
            Action = ManufacturingAnnotationPlanAction.ReviewRequired,
            Status = status,
            Code = code,
            Rationale = rationale,
        };

    private static bool TryGetModelItemKinds(
        ManufacturingAnnotationKind kind,
        out DrawingModelAnnotationImportKinds modelItemKinds)
    {
        modelItemKinds = kind switch
        {
            ManufacturingAnnotationKind.ModelDimension => DrawingModelAnnotationImportKinds.Dimensions,
            ManufacturingAnnotationKind.HoleCallout => DrawingModelAnnotationImportKinds.HoleCallouts,
            ManufacturingAnnotationKind.Datum => DrawingModelAnnotationImportKinds.Datums,
            ManufacturingAnnotationKind.DatumTarget => DrawingModelAnnotationImportKinds.DatumTargets,
            ManufacturingAnnotationKind.GeometricTolerance => DrawingModelAnnotationImportKinds.GdAndTolerances,
            ManufacturingAnnotationKind.SurfaceFinish => DrawingModelAnnotationImportKinds.SurfaceFinish,
            ManufacturingAnnotationKind.WeldSymbol => DrawingModelAnnotationImportKinds.WeldSymbols,
            ManufacturingAnnotationKind.Note => DrawingModelAnnotationImportKinds.Notes,
            ManufacturingAnnotationKind.CosmeticThread => DrawingModelAnnotationImportKinds.CosmeticThreads,
            ManufacturingAnnotationKind.TolerancedDimension => DrawingModelAnnotationImportKinds.TolerancedDimensions,
            _ => DrawingModelAnnotationImportKinds.None,
        };
        return modelItemKinds is not DrawingModelAnnotationImportKinds.None;
    }

    private static void ValidateRequirements(DrawingManufacturingAnnotationRequirement[] requirements)
    {
        if (requirements.Any(item => string.IsNullOrWhiteSpace(item.RequirementId)))
        {
            throw new ArgumentException("Manufacturing annotation requirements need stable identities.", nameof(requirements));
        }

        if (requirements.Select(item => item.RequirementId).Distinct(StringComparer.Ordinal).Count() != requirements.Length)
        {
            throw new ArgumentException("Manufacturing annotation requirement identities must be unique.", nameof(requirements));
        }

        if (requirements.Any(item => string.IsNullOrWhiteSpace(item.FeatureIdentity) || string.IsNullOrWhiteSpace(item.ViewId.Value)))
        {
            throw new ArgumentException("Manufacturing annotation requirements need feature and view identities.", nameof(requirements));
        }
    }

    private static void ValidateEvidence(IEnumerable<DrawingManufacturingAnnotationEvidence> evidence)
    {
        DrawingManufacturingAnnotationEvidence[] items = [.. evidence];
        if (items.Any(item => string.IsNullOrWhiteSpace(item.EvidenceId) || string.IsNullOrWhiteSpace(item.RequirementId)))
        {
            throw new ArgumentException("Manufacturing annotation evidence needs stable identities.", nameof(evidence));
        }
    }

    /// <summary>
    /// Recomputes the deterministic plan fingerprint after a provider has materialized an import.
    /// Provider 物化 import 后重新计算确定性 plan fingerprint。
    /// </summary>
    internal static string ComputeFingerprint(IEnumerable<DrawingManufacturingAnnotationPlanItem> items)
    {
        string canonical = string.Join(
            "\n",
            items.Select(item => string.Join(
                "|",
                item.RequirementId,
                item.FeatureIdentity,
                item.Kind,
                item.Action,
                item.Status,
                item.Code,
                item.ExistingAnnotationId?.Value ?? string.Empty)));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
