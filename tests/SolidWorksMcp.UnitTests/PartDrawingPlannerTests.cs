using System.Globalization;
using SolidWorksMcp.AutoDrawing;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.EngineeringModel;
using SolidWorksMcp.Protocol;
using SolidWorksMcp.RuleEngine;

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

    [Fact]
    public void PlannerCarriesResolvedRulePackProjectionAndProvenance()
    {
        PartDrawingRequirementSet requirements = ApprovedRequirements(PartDrawingSemanticClass.OrthographicViews);
        DrawingRulePack standard = StandardRulePackCatalog.CreateGbRulePack();
        RulePackResolutionResult firstAngle = DrawingRulePackResolver.Resolve(standard);
        RulePackResolutionResult thirdAngle = DrawingRulePackResolver.Resolve(
            standard,
            [
                new DrawingRulePackOverride
                {
                    PackId = "enterprise-third-angle",
                    Layer = RulePackLayer.Enterprise,
                    Source = new RulePackSourceMetadata
                    {
                        SourceId = "enterprise-projection-policy",
                        SourceUrl = "https://example.invalid/enterprise/projection",
                        Status = "test-reviewed",
                        RetrievedAtUtc = DateTimeOffset.Parse("2026-09-18T00:00:00Z", CultureInfo.InvariantCulture),
                        LicenseNote = "Test metadata only.",
                    },
                    ProjectionMethod = DrawingProjectionMethod.ThirdAngle,
                },
            ]);

        PartDrawingPlan firstPlan = PartDrawingPlanner.Plan(
            new PartDrawingPlanRequest
            {
                Requirements = requirements,
                PreferredPrimaryOrientationApproved = true,
                RulePack = firstAngle.Pack,
            });
        PartDrawingPlan thirdPlan = PartDrawingPlanner.Plan(
            new PartDrawingPlanRequest
            {
                Requirements = requirements,
                PreferredPrimaryOrientationApproved = true,
                RulePack = thirdAngle.Pack,
            });

        Assert.Equal(PartDrawingProjectionMethod.FirstAngle, firstPlan.Projection);
        Assert.Equal(PartDrawingProjectionMethod.ThirdAngle, thirdPlan.Projection);
        Assert.NotEqual(firstPlan.Projection, thirdPlan.Projection);
        Assert.Equal("GB.rulepack", firstPlan.RulePack!.PackId);
        Assert.True(thirdPlan.RulePack!.TryExplain("projection.method", out RulePackExplanation? explanation));
        Assert.Equal("enterprise-third-angle", explanation!.PackId);
        Assert.Equal("enterprise-projection-policy", explanation.Source.SourceId);
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

public sealed class PartDrawingCoverageAnalyzerTests
{
    [Fact]
    public void ExplicitCoveragePassesApprovedRequirementsAndWarnsOnlyForOptionalMissingItems()
    {
        var draft = PartDrawingRequirementSet.FromReviewedClasses(
            "coverage-profile",
            [PartDrawingSemanticClass.OrthographicViews, PartDrawingSemanticClass.IsometricView],
            DateTimeOffset.Parse("2026-09-16T00:00:00Z", CultureInfo.InvariantCulture));
        PartDrawingRequirementSet approved = draft.Approve(draft.Requirements.Where(requirement => requirement.Required).Select(requirement => requirement.RequirementId));
        PartDrawingPlan plan = PartDrawingPlanner.Plan(new PartDrawingPlanRequest { Requirements = approved, PreferredPrimaryOrientationApproved = true });

        PartDrawingCoverageReport report = PartDrawingCoverageAnalyzer.Analyze(approved, plan, Snapshot(
            new DrawingAnnotationSnapshot
            {
                AnnotationId = new AnnotationId("annotation-01"),
                ViewId = new ViewId("view-primary"),
                Kind = "model-dimension",
                Text = "redacted",
                CoverageKeys = ["views.orthographic.coverage"],
                Position = new Coordinate2D(Length.FromMillimeters(0), Length.FromMillimeters(0)),
            }));

        Assert.True(report.CanRelease);
        Assert.Equal(PartDrawingCoverageStatus.Pass, report.Findings.Single(finding => finding.CoverageKey == "views.orthographic.coverage").Status);
        Assert.Equal(PartDrawingCoverageStatus.Warning, report.Findings.Single(finding => finding.CoverageKey == "views.isometric.coverage").Status);
    }

    [Fact]
    public void UnapprovedCoverageIsReviewRequiredAndUnknownKeysAreNotSilentlyAccepted()
    {
        var requirements = PartDrawingRequirementSet.FromReviewedClasses(
            "review-profile",
            [PartDrawingSemanticClass.OrthographicViews],
            DateTimeOffset.Parse("2026-09-16T00:00:00Z", CultureInfo.InvariantCulture));
        PartDrawingPlan plan = PartDrawingPlanner.Plan(new PartDrawingPlanRequest { Requirements = requirements });

        PartDrawingCoverageReport report = PartDrawingCoverageAnalyzer.Analyze(requirements, plan, Snapshot(
            new DrawingAnnotationSnapshot
            {
                AnnotationId = new AnnotationId("annotation-02"),
                ViewId = new ViewId("view-primary"),
                Kind = "note",
                Text = "redacted",
                CoverageKeys = ["views.orthographic.coverage", "unmodeled.coverage"],
                Position = new Coordinate2D(Length.FromMillimeters(0), Length.FromMillimeters(0)),
            }));

        Assert.False(report.CanRelease);
        Assert.Equal(PartDrawingCoverageStatus.ReviewRequired, report.Findings.Single(finding => finding.CoverageKey == "views.orthographic.coverage").Status);
        Assert.Equal(PartDrawingCoverageStatus.ReviewRequired, report.Findings.Single(finding => finding.CoverageKey == "plan").Status);
        Assert.Equal(PartDrawingCoverageStatus.Warning, report.Findings.Single(finding => finding.CoverageKey == "unmodeled.coverage").Status);
    }

    [Fact]
    public void ApprovedRequirementWithoutExplicitSemanticAnnotationIsBlocking()
    {
        var draft = PartDrawingRequirementSet.FromReviewedClasses(
            "missing-profile",
            [PartDrawingSemanticClass.OrthographicViews],
            DateTimeOffset.Parse("2026-09-16T00:00:00Z", CultureInfo.InvariantCulture));
        PartDrawingRequirementSet approved = draft.Approve(draft.Requirements.Select(requirement => requirement.RequirementId));
        PartDrawingPlan plan = PartDrawingPlanner.Plan(new PartDrawingPlanRequest { Requirements = approved, PreferredPrimaryOrientationApproved = true });

        PartDrawingCoverageReport report = PartDrawingCoverageAnalyzer.Analyze(approved, plan, Snapshot());

        Assert.False(report.CanRelease);
        Assert.Equal(PartDrawingCoverageStatus.Blocking, report.Findings.Single().Status);
        Assert.Equal("missing-required-coverage", report.Findings.Single().Code);
    }

    private static CadInspectionSnapshot Snapshot(params DrawingAnnotationSnapshot[] annotations) => new()
    {
        Document = new CadDocumentSummary
        {
            DocumentId = new DocumentId("drawing-01"),
            DocumentType = CadDocumentType.Drawing,
            Path = string.Empty,
            Configuration = "Default",
            StateHash = "sha256:test",
            IsDirty = false,
        },
        Annotations = [.. annotations],
    };
}
