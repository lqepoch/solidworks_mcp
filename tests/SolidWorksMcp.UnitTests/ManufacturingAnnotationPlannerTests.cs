using SolidWorksMcp.AutoDrawing;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.UnitTests;

/// <summary>
/// Verifies D07 provenance/approval behavior without starting SOLIDWORKS or reading confidential drawing PDFs.
/// 在不启动 SOLIDWORKS、也不读取机密图纸 PDF 的情况下验证 D07 来源与批准状态行为。
/// </summary>
public sealed class ManufacturingAnnotationPlannerTests
{
    [Fact]
    public void ApprovedAssociativeNativeEvidenceIsRetainedAndCanRelease()
    {
        DrawingManufacturingAnnotationPlan plan = ManufacturingAnnotationPlanner.Plan(
            [Requirement("hole-callout-17", "HolePattern17", ManufacturingAnnotationKind.HoleCallout)],
            [Evidence(
                "native-hole-callout-17",
                "hole-callout-17",
                "HolePattern17",
                ManufacturingAnnotationKind.HoleCallout,
                DrawingAnnotationApprovalState.Approved,
                associative: true)]);

        DrawingManufacturingAnnotationPlanItem item = Assert.Single(plan.Items);
        Assert.Equal(ManufacturingAnnotationPlanAction.RetainNative, item.Action);
        Assert.Equal(ManufacturingAnnotationFindingStatus.Pass, item.Status);
        Assert.Equal("annotation-native-approved", item.Code);
        Assert.True(plan.CanRelease);
    }

    [Fact]
    public void MissingHoleEvidencePlansNativeImportButDoesNotPassRelease()
    {
        DrawingManufacturingAnnotationPlan plan = ManufacturingAnnotationPlanner.Plan(
            [Requirement("hole-callout-17", "HolePattern17", ManufacturingAnnotationKind.HoleCallout)],
            []);

        DrawingManufacturingAnnotationPlanItem item = Assert.Single(plan.Items);
        Assert.Equal(ManufacturingAnnotationPlanAction.ImportNativeModelItem, item.Action);
        Assert.Equal(ManufacturingAnnotationFindingStatus.Warning, item.Status);
        Assert.NotNull(item.NativeRequest);
        Assert.Equal(DrawingModelAnnotationImportKinds.HoleCallouts, item.NativeRequest!.ModelItemKinds);
        Assert.Equal("insert_model_annotations3", item.NativeRequest.ProvenanceMethod);
        Assert.Equal(DrawingAnnotationApprovalState.Approved, item.NativeRequest.ApprovalState);
        Assert.False(plan.CanRelease);
    }

    [Fact]
    public void ProposalAndNonAssociativeEvidenceBecomeBlockingReviewFindings()
    {
        DrawingManufacturingAnnotationPlan proposal = ManufacturingAnnotationPlanner.Plan(
            [Requirement("gdt-01", "MountingFace", ManufacturingAnnotationKind.GeometricTolerance)],
            [Evidence(
                "gdt-native-01",
                "gdt-01",
                "MountingFace",
                ManufacturingAnnotationKind.GeometricTolerance,
                DrawingAnnotationApprovalState.Proposal,
                associative: true)]);
        DrawingManufacturingAnnotationPlanItem proposalItem = Assert.Single(proposal.Items);
        Assert.Equal("annotation-approval-required", proposalItem.Code);
        Assert.Equal(ManufacturingAnnotationFindingStatus.Blocking, proposalItem.Status);
        Assert.False(proposal.CanRelease);

        DrawingManufacturingAnnotationPlan nonAssociative = ManufacturingAnnotationPlanner.Plan(
            [Requirement("datum-a", "Datum-A", ManufacturingAnnotationKind.Datum)],
            [Evidence(
                "datum-native-a",
                "datum-a",
                "Datum-A",
                ManufacturingAnnotationKind.Datum,
                DrawingAnnotationApprovalState.Approved,
                associative: false)]);
        DrawingManufacturingAnnotationPlanItem nonAssociativeItem = Assert.Single(nonAssociative.Items);
        Assert.Equal("annotation-not-associative", nonAssociativeItem.Code);
        Assert.Equal(ManufacturingAnnotationFindingStatus.Blocking, nonAssociativeItem.Status);
    }

    [Fact]
    public void CenterMarkRequiresDedicatedProviderContractInsteadOfSyntheticText()
    {
        DrawingManufacturingAnnotationPlan plan = ManufacturingAnnotationPlanner.Plan(
            [Requirement("center-mark-01", "HolePattern17", ManufacturingAnnotationKind.CenterMark)],
            []);

        DrawingManufacturingAnnotationPlanItem item = Assert.Single(plan.Items);
        Assert.Equal(ManufacturingAnnotationPlanAction.ReviewRequired, item.Action);
        Assert.Equal("annotation-provider-contract-required", item.Code);
        Assert.Equal(ManufacturingAnnotationFindingStatus.Blocking, item.Status);
        Assert.Null(item.NativeRequest);
        Assert.False(plan.CanRelease);
    }

    [Fact]
    public void DuplicateEvidenceIsBlockingAndPlanFingerprintIsStable()
    {
        DrawingManufacturingAnnotationRequirement requirement = Requirement(
            "surface-01",
            "MachinedFace",
            ManufacturingAnnotationKind.SurfaceFinish);
        DrawingManufacturingAnnotationEvidence first = Evidence(
            "surface-native-01",
            "surface-01",
            "MachinedFace",
            ManufacturingAnnotationKind.SurfaceFinish,
            DrawingAnnotationApprovalState.Released,
            associative: true);
        DrawingManufacturingAnnotationEvidence second = first with { EvidenceId = "surface-native-02" };

        DrawingManufacturingAnnotationPlan firstPlan = ManufacturingAnnotationPlanner.Plan([requirement], [first, second]);
        DrawingManufacturingAnnotationPlan secondPlan = ManufacturingAnnotationPlanner.Plan([requirement], [second, first]);

        DrawingManufacturingAnnotationPlanItem item = Assert.Single(firstPlan.Items);
        Assert.Equal(ManufacturingAnnotationFindingStatus.Blocking, item.Status);
        Assert.Equal("annotation-evidence-ambiguous", item.Code);
        Assert.Equal(firstPlan.Fingerprint, secondPlan.Fingerprint);
    }

    private static DrawingManufacturingAnnotationRequirement Requirement(
        string requirementId,
        string featureIdentity,
        ManufacturingAnnotationKind kind) => new()
        {
            RequirementId = requirementId,
            FeatureIdentity = featureIdentity,
            Kind = kind,
            ViewId = new ViewId("Front")
        };

    private static DrawingManufacturingAnnotationEvidence Evidence(
        string evidenceId,
        string requirementId,
        string featureIdentity,
        ManufacturingAnnotationKind kind,
        DrawingAnnotationApprovalState approvalState,
        bool associative) => new()
        {
            EvidenceId = evidenceId,
            RequirementId = requirementId,
            FeatureIdentity = featureIdentity,
            Kind = kind,
            ViewId = new ViewId("Front"),
            AnnotationId = new AnnotationId(evidenceId),
            ProvenanceKind = "model_native",
            ProvenanceMethod = "insert_model_annotations3",
            ApprovalState = approvalState,
            IsAssociative = associative,
        };
}
