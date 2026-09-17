using System.Collections.Immutable;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.AutoDrawing;

/// <summary>
/// Verified result of materializing planned native Model Items into one drawing.
/// 将规划的 native Model Items 物化到一张工程图后的、带证据的结果。
/// </summary>
/// <remarks>
/// The materializer is deliberately provider-neutral. It does not select by COM enumeration index and it never
/// manufactures a dimension, hole callout, GD&amp;T frame or datum from text. The provider must return a native
/// annotation snapshot for every mutation; only then is the corresponding plan item promoted from Warning to Pass.
/// 该 materializer 刻意保持 Provider-neutral：不使用 COM enumeration index，也绝不根据文字伪造尺寸、孔标注、GD&amp;T
/// 框或 datum。每个 mutation 都必须由 Provider 返回 native annotation snapshot，之后才能把 plan item 从 Warning
/// 提升为 Pass。
/// </remarks>
public sealed record DrawingManufacturingAnnotationMaterialization
{
    /// <summary>Plan after every requested import has a verified native identity.</summary>
    public required DrawingManufacturingAnnotationPlan Plan { get; init; }

    /// <summary>Native snapshots returned by the Provider during this call.</summary>
    public ImmutableArray<DrawingAnnotationSnapshot> MaterializedAnnotations { get; init; } = [];
}

/// <summary>
/// Executes an exact manufacturing-annotation plan without exposing vendor COM types to the compiler.
/// 在不向 compiler 暴露厂商 COM 类型的前提下执行精确制造标注 plan。
/// </summary>
public static class ManufacturingAnnotationMaterializer
{
    /// <summary>
    /// Materializes only approved import actions. All validation is completed before the first Provider mutation.
    /// 只物化已批准的 import action；所有验证在第一个 Provider mutation 前完成。
    /// </summary>
    public static async Task<OperationResult<DrawingManufacturingAnnotationMaterialization>> MaterializeAsync(
        ICadDrawingDocument drawing,
        DrawingManufacturingAnnotationPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(drawing);
        ArgumentNullException.ThrowIfNull(plan);

        DrawingManufacturingAnnotationPlanItem[] imports = [.. plan.Items
            .Where(item => item.Action == ManufacturingAnnotationPlanAction.ImportNativeModelItem)
            .OrderBy(item => item.RequirementId, StringComparer.Ordinal)];
        OperationError? preflightError = ValidateBeforeMutation(plan, imports);
        if (preflightError is not null)
        {
            return OperationResults.Failure<DrawingManufacturingAnnotationMaterialization>(
                "drawing.annotation.materialize",
                preflightError);
        }

        if (imports.Length == 0)
        {
            return OperationResults.Success(
                new DrawingManufacturingAnnotationMaterialization { Plan = plan },
                "drawing.annotation.materialize",
                new OperationEvidence(
                    "drawing.annotation.materialize",
                    [new EvidenceObservation("materialized.count", "0"), new EvidenceObservation("materialized.status", "no-op")]));
        }

        var materialized = ImmutableArray.CreateBuilder<DrawingAnnotationSnapshot>(imports.Length);
        var annotationIds = new HashSet<string>(StringComparer.Ordinal);
        var updatedByRequirement = new Dictionary<string, DrawingManufacturingAnnotationPlanItem>(StringComparer.Ordinal);
        foreach (DrawingManufacturingAnnotationPlanItem item in imports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DrawingAnnotationRequest request = item.NativeRequest!;
            OperationResult<DrawingAnnotationSnapshot> result = await drawing.AddAnnotationAsync(
                request,
                cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess || result.Value is null)
            {
                return OperationResults.Failure<DrawingManufacturingAnnotationMaterialization>(
                    result.OperationId,
                    result.Error ?? new OperationError(
                        ErrorCodes.InvariantViolation,
                        "The Provider returned no native annotation materialization result.",
                        ErrorCategories.Invariant),
                    result.Evidence);
            }

            DrawingAnnotationSnapshot snapshot = result.Value;
            if (!snapshot.ViewId.Equals(request.ViewId)
                || string.IsNullOrWhiteSpace(snapshot.AnnotationId.Value)
                || !annotationIds.Add(snapshot.AnnotationId.Value))
            {
                return OperationResults.Failure<DrawingManufacturingAnnotationMaterialization>(
                    "drawing.annotation.materialize",
                    new OperationError(
                        ErrorCodes.InvariantViolation,
                        "The Provider returned an ambiguous or incorrectly scoped native annotation identity.",
                        ErrorCategories.Invariant,
                        remediation: "Preserve the drawing, inspect the native annotation collection, and retry with one exact requirement."),
                    result.Evidence);
            }

            materialized.Add(snapshot);
            updatedByRequirement[item.RequirementId] = item with
            {
                Action = ManufacturingAnnotationPlanAction.RetainNative,
                Status = ManufacturingAnnotationFindingStatus.Pass,
                Code = "annotation-native-materialized",
                ExistingAnnotationId = snapshot.AnnotationId,
                Rationale = "The requested native Model Item was returned with a stable identity after provider materialization.",
            };
        }

        DrawingManufacturingAnnotationPlanItem[] updatedItems = [.. plan.Items.Select(item =>
            updatedByRequirement.TryGetValue(item.RequirementId, out DrawingManufacturingAnnotationPlanItem? updated)
                ? updated
                : item)];
        var updatedPlan = plan with
        {
            Items = [.. updatedItems],
            Fingerprint = ManufacturingAnnotationPlanner.ComputeFingerprint(updatedItems),
        };
        return OperationResults.Success(
            new DrawingManufacturingAnnotationMaterialization
            {
                Plan = updatedPlan,
                MaterializedAnnotations = materialized.ToImmutable(),
            },
            "drawing.annotation.materialize",
            new OperationEvidence(
                "drawing.annotation.materialize",
                [
                    new EvidenceObservation("materialized.count", materialized.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    new EvidenceObservation("materialized.status", "verified-native-identities"),
                    new EvidenceObservation("materialized.plan-fingerprint", updatedPlan.Fingerprint),
                ],
                [.. materialized.Select(item => item.AnnotationId.Value)]));
    }

    private static OperationError? ValidateBeforeMutation(
        DrawingManufacturingAnnotationPlan plan,
        IReadOnlyCollection<DrawingManufacturingAnnotationPlanItem> imports)
    {
        DrawingManufacturingAnnotationPlanItem? blocked = plan.Items.FirstOrDefault(item =>
            item.Status is ManufacturingAnnotationFindingStatus.Blocking or ManufacturingAnnotationFindingStatus.ReviewRequired);
        if (blocked is not null)
        {
            return new OperationError(
                ErrorCodes.ReviewRequired,
                $"Manufacturing annotation requirement '{blocked.RequirementId}' is not eligible for mutation.",
                ErrorCategories.Policy,
                remediation: "Resolve provenance, association and approval findings before materialization.");
        }

        if (imports.Any(item => item.Status != ManufacturingAnnotationFindingStatus.Warning
            || item.NativeRequest is null
            || item.NativeRequest.ModelItemKinds is DrawingModelAnnotationImportKinds.None
            || string.IsNullOrWhiteSpace(item.NativeRequest.FeatureIdentity)
            || item.NativeRequest.ApprovalState is not (DrawingAnnotationApprovalState.Approved or DrawingAnnotationApprovalState.Released)))
        {
            return new OperationError(
                ErrorCodes.InvalidRequest,
                "Every native Model Item import must be a Warning with a scoped, approved request.",
                ErrorCategories.Validation);
        }

        // InsertModelAnnotations3 can return a set for one view/category. The current contract returns one snapshot,
        // so multiple requirements sharing that scope would silently lose identity. Reject before the first write.
        // InsertModelAnnotations3 对一个 view/category 可能返回集合；当前 contract 只返回一个 snapshot，因此多个
        // requirement 共用同一 scope 会丢失 identity，必须在第一个写入前拒绝。
        string[] duplicateScopes = [.. imports
            .Select(item => $"{item.NativeRequest!.ViewId.Value}|{item.NativeRequest.ModelItemKinds}")
            .GroupBy(value => value, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)];
        if (duplicateScopes.Length != 0)
        {
            return new OperationError(
                ErrorCodes.InvariantViolation,
                "Multiple native Model Item requirements share one provider import scope.",
                ErrorCategories.Invariant,
                remediation: "Split the scope with a provider selector that proves each feature, or submit one grouped requirement.");
        }

        return null;
    }
}
