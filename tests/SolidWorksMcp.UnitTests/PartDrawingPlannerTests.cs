using System.Globalization;
using SolidWorksMcp.AutoDrawing;
using SolidWorksMcp.EngineeringModel;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.UnitTests;

public sealed class PartDrawingPlannerTests
{
    [Fact]
    public void PlannerChoosesSectionByWeightedManufacturingEvidence()
    {
        PartDrawingRequirementSet requirements = ApprovedRequirements(
            PartDrawingSemanticClass.OrthographicViews,
            PartDrawingSemanticClass.IsometricView,
            PartDrawingSemanticClass.SectionOrDetailCandidate,
            PartDrawingSemanticClass.HoleFeature);

        PartDrawingPlan plan = PartDrawingPlanner.Plan(
            new PartDrawingPlanRequest
            {
                Requirements = requirements,
                PreferredPrimaryOrientation = "NamedManufacturingView",
                PreferredPrimaryOrientationApproved = true,
                ViewCandidates =
                [
                    new PartDrawingViewCandidate
                    {
                        CandidateId = "detail-small-feature",
                        Role = PartDrawingViewRole.Detail,
                        Orientation = "DetailAnchor",
                        CoverageGain = Score(0.70),
                        ReadabilityGain = Score(0.95),
                        ManufacturingExposureGain = Score(0.60),
                        EvidenceSource = "provider.inspection",
                        IsProviderVerified = true,
                    },
                    new PartDrawingViewCandidate
                    {
                        CandidateId = "section-internal-feature",
                        Role = PartDrawingViewRole.Section,
                        Orientation = "SectionAnchor",
                        CoverageGain = Score(0.90),
                        ReadabilityGain = Score(0.80),
                        ManufacturingExposureGain = Score(0.90),
                        EvidenceSource = "provider.inspection",
                        IsProviderVerified = true,
                    },
                ],
            });

        Assert.Equal(PartDrawingPlanningStatus.ReadyForGeneration, plan.Status);
        Assert.True(plan.CanGenerate);
        Assert.True(plan.CanRelease);
        Assert.Equal(
            [PartDrawingViewRole.Primary, PartDrawingViewRole.Projected, PartDrawingViewRole.Isometric, PartDrawingViewRole.Section],
            plan.Items.Select(item => item.Role));
        Assert.Equal("section-internal-feature", plan.Items[^1].CandidateId);
    }

    [Fact]
    public void PrivateReviewAndUnverifiedCandidateRemainReviewRequired()
    {
        var requirements = PartDrawingRequirementSet.FromReviewedClasses(
            "private-profile",
            [
                PartDrawingSemanticClass.OrthographicViews,
                PartDrawingSemanticClass.SectionOrDetailCandidate,
            ],
            DateTimeOffset.Parse("2026-09-16T00:00:00Z", CultureInfo.InvariantCulture));

        PartDrawingPlan plan = PartDrawingPlanner.Plan(
            new PartDrawingPlanRequest
            {
                Requirements = requirements,
                ViewCandidates =
                [
                    new PartDrawingViewCandidate
                    {
                        CandidateId = "review-candidate",
                        Role = PartDrawingViewRole.Detail,
                        Orientation = "DetailAnchor",
                        CoverageGain = Score(0.8),
                        ReadabilityGain = Score(0.8),
                        ManufacturingExposureGain = Score(0.8),
                        EvidenceSource = "private_drawing_review",
                    },
                ],
            });

        Assert.Equal(PartDrawingPlanningStatus.ReviewRequired, plan.Status);
        Assert.True(plan.CanGenerate);
        Assert.False(plan.CanRelease);
        Assert.Contains("review-required:primary-orientation-evidence", plan.Diagnostics);
        Assert.DoesNotContain("features.hole.size-location-callout", requirements.Requirements.Select(value => value.CoverageKey), StringComparer.Ordinal);
        Assert.Contains("views.section-or-detail.coverage", plan.BlockingCoverageKeys);
        Assert.True(plan.Items[^1].RequiresReview);
    }

    [Fact]
    public void MissingOrthographicCoverageBlocksEvenAValidCandidate()
    {
        var requirements = PartDrawingRequirementSet.FromReviewedClasses(
            "invalid-profile",
            [PartDrawingSemanticClass.HoleFeature],
            DateTimeOffset.Parse("2026-09-16T00:00:00Z", CultureInfo.InvariantCulture));

        PartDrawingPlan plan = PartDrawingPlanner.Plan(
            new PartDrawingPlanRequest
            {
                Requirements = requirements,
                ViewCandidates =
                [
                    new PartDrawingViewCandidate
                    {
                        CandidateId = "unused-detail",
                        Role = PartDrawingViewRole.Detail,
                        Orientation = "DetailAnchor",
                        CoverageGain = Score(1),
                        ReadabilityGain = Score(1),
                        ManufacturingExposureGain = Score(1),
                        EvidenceSource = "provider.inspection",
                        IsProviderVerified = true,
                    },
                ],
            });

        Assert.Equal(PartDrawingPlanningStatus.Blocked, plan.Status);
        Assert.False(plan.CanGenerate);
        Assert.Contains("missing-requirement:views.orthographic.coverage", plan.Diagnostics);
        Assert.Empty(plan.Items);
    }

    [Fact]
    public void EqualScoresUseStableSectionThenCandidateIdTieBreak()
    {
        PartDrawingRequirementSet requirements = ApprovedRequirements(
            PartDrawingSemanticClass.OrthographicViews,
            PartDrawingSemanticClass.SectionOrDetailCandidate);

        PartDrawingPlan plan = PartDrawingPlanner.Plan(
            new PartDrawingPlanRequest
            {
                Requirements = requirements,
                PreferredPrimaryOrientationApproved = true,
                ViewCandidates =
                [
                    new PartDrawingViewCandidate
                    {
                        CandidateId = "z-detail",
                        Role = PartDrawingViewRole.Detail,
                        Orientation = "Detail",
                        CoverageGain = Score(0.5),
                        ReadabilityGain = Score(0.5),
                        ManufacturingExposureGain = Score(0.5),
                        EvidenceSource = "provider.inspection",
                        IsProviderVerified = true,
                    },
                    new PartDrawingViewCandidate
                    {
                        CandidateId = "z-section",
                        Role = PartDrawingViewRole.Section,
                        Orientation = "Section",
                        CoverageGain = Score(0.5),
                        ReadabilityGain = Score(0.5),
                        ManufacturingExposureGain = Score(0.5),
                        EvidenceSource = "provider.inspection",
                        IsProviderVerified = true,
                    },
                ],
            });

        Assert.Equal(PartDrawingViewRole.Section, plan.Items[^1].Role);
        Assert.Equal("z-section", plan.Items[^1].CandidateId);
    }

    private static PartDrawingRequirementSet ApprovedRequirements(params PartDrawingSemanticClass[] classes)
    {
        var draft = PartDrawingRequirementSet.FromReviewedClasses(
            "approved-profile",
            classes,
            DateTimeOffset.Parse("2026-09-16T00:00:00Z", CultureInfo.InvariantCulture));
        return draft.Approve(draft.Requirements.Select(value => value.RequirementId));
    }

    private static NormalizedScore Score(double value) => NormalizedScore.FromRatio(value);
}
