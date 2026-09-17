using System.Globalization;
using SolidWorksMcp.EngineeringModel;
using SolidWorksMcp.Protocol;
using SolidWorksMcp.Tolerancing;

namespace SolidWorksMcp.UnitTests;

/// <summary>
/// Locks the functional-versus-drawing tolerance boundary and the canonical 100-to-98 worst-case example.
/// 锁定 functional tolerance 与 drawing tolerance 的边界，以及标准 100→98 Worst-Case 案例。
/// </summary>
public sealed class FunctionalToleranceEngineTests
{
    [Fact]
    public void WorstCaseInstallationSpaceMinusErrorMinusClearanceProduces98AndExplainsEveryTerm()
    {
        FunctionalDimensionRequirement requirement = ApprovedRequirement(
            functionalMaximum: 98d,
            drawingNominal: 97.5d,
            drawingTolerance: new Tolerance(Length.FromMillimeters(-0.5d), Length.FromMillimeters(0.5d)));
        ToleranceStackupRequest stackup = InstallationStackup(ToleranceAnalysisMethod.WorstCase);

        ToleranceAnalysisResult result = FunctionalToleranceEngine.AnalyzeWorstCase(requirement, stackup);

        Assert.Equal(ToleranceAnalysisStatus.Pass, result.Status);
        Assert.True(result.CanRelease);
        Assert.Equal(98d, result.StackupLimits!.Value.Maximum.Millimeters, precision: 9);
        Assert.Equal(98d, result.StackupLimits.Value.Minimum.Millimeters, precision: 9);
        Assert.Equal(97d, result.DrawingLimits.Minimum.Millimeters, precision: 9);
        Assert.Equal(98d, result.DrawingLimits.Maximum.Millimeters, precision: 9);
        Assert.Equal(3, result.Explanation.Steps.Length);
        Assert.Contains("functional=[0 mm, 98 mm]", result.Explanation.ToPlainText(), StringComparison.Ordinal);
        Assert.Contains("stack=[98 mm, 98 mm]", result.Explanation.ToPlainText(), StringComparison.Ordinal);
        Assert.Contains("assembly-error", result.Explanation.Steps.Select(step => step.SourceId));
        Assert.Contains("required-clearance", result.Explanation.Steps.Select(step => step.SourceId));
    }

    [Fact]
    public void PrivateReviewOrAiProposalCannotCrossReleaseWithoutApproval()
    {
        FunctionalDimensionRequirement requirement = ApprovedRequirement(
            functionalMaximum: 98d,
            drawingNominal: 97.5d,
            drawingTolerance: Tolerance.Symmetric(Length.FromMillimeters(0.5d))) with
        {
            ApprovalState = EngineeringRequirementStatus.ReviewRequired,
            Provenance = new EngineeringProvenance(
                "private_drawing_review",
                "redacted_visual_semantic_review",
                DateTimeOffset.Parse("2026-09-18T00:00:00Z", CultureInfo.InvariantCulture)),
        };

        ToleranceAnalysisResult result = FunctionalToleranceEngine.Analyze(
            requirement,
            InstallationStackup(ToleranceAnalysisMethod.WorstCase));

        Assert.Equal(ToleranceAnalysisStatus.ReviewRequired, result.Status);
        Assert.False(result.CanRelease);
        Assert.Contains("requirement-approval-required", result.Diagnostics);
    }

    [Fact]
    public void DrawingRepresentationOutsideFunctionalMaximumIsBlocking()
    {
        FunctionalDimensionRequirement requirement = ApprovedRequirement(
            functionalMaximum: 98d,
            drawingNominal: 98d,
            drawingTolerance: Tolerance.Symmetric(Length.FromMillimeters(0.5d)));

        ToleranceAnalysisResult result = FunctionalToleranceEngine.AnalyzeWorstCase(
            requirement,
            InstallationStackup(ToleranceAnalysisMethod.WorstCase));

        Assert.Equal(ToleranceAnalysisStatus.Blocking, result.Status);
        Assert.False(result.CanRelease);
        Assert.Contains("drawing-tolerance-exceeds-functional-limits", result.Diagnostics);
    }

    [Fact]
    public void StatisticalMethodIsExplicitlyReviewRequiredInsteadOfBeingSilentlyTreatedAsWorstCase()
    {
        FunctionalDimensionRequirement requirement = ApprovedRequirement(
            functionalMaximum: 98d,
            drawingNominal: 97.5d,
            drawingTolerance: Tolerance.Symmetric(Length.FromMillimeters(0.5d)));

        ToleranceAnalysisResult result = FunctionalToleranceEngine.Analyze(
            requirement,
            InstallationStackup(ToleranceAnalysisMethod.Rss));

        Assert.Equal(ToleranceAnalysisStatus.ReviewRequired, result.Status);
        Assert.Null(result.StackupLimits);
        Assert.Contains("analysis-method-not-enabled:Rss", result.Diagnostics);
        Assert.False(result.CanRelease);
    }

    private static FunctionalDimensionRequirement ApprovedRequirement(
        double functionalMaximum,
        double drawingNominal,
        Tolerance drawingTolerance) => new()
        {
            DimensionId = "functional-installation-size",
            DesignNominal = Length.FromMillimeters(drawingNominal),
            FunctionalLimits = new DimensionLimits(
                Length.FromMillimeters(0d),
                Length.FromMillimeters(functionalMaximum)),
            DrawingNominal = Length.FromMillimeters(drawingNominal),
            DrawingTolerance = drawingTolerance,
            FitClass = null,
            GeneralTolerance = null,
            InspectionMethod = "calibrated-length-inspection",
            Criticality = ToleranceCriticality.Critical,
            Provenance = new EngineeringProvenance(
                "assembly_stackup",
                "approved_worst_case_requirement",
                DateTimeOffset.Parse("2026-09-18T00:00:00Z", CultureInfo.InvariantCulture)),
            ApprovalState = EngineeringRequirementStatus.Approved,
        };

    private static ToleranceStackupRequest InstallationStackup(ToleranceAnalysisMethod method) =>
        new(
            "installation-space-stackup",
            ToleranceConstraintKind.MaximumAllowedPartSize,
            method,
            [
                Term("installation-space", 100d, ToleranceStackDirection.Add),
                Term("assembly-error", 1d, ToleranceStackDirection.Subtract),
                Term("required-clearance", 1d, ToleranceStackDirection.Subtract),
            ]);

    private static ToleranceStackTerm Term(string id, double nominal, ToleranceStackDirection direction) => new()
    {
        TermId = id,
        Nominal = Length.FromMillimeters(nominal),
        Tolerance = new Tolerance(Length.FromMillimeters(0d), Length.FromMillimeters(0d)),
        Direction = direction,
        Provenance = new EngineeringProvenance(
            "assembly_stackup",
            "approved_worst_case_requirement",
            DateTimeOffset.Parse("2026-09-18T00:00:00Z", CultureInfo.InvariantCulture)),
    };
}
