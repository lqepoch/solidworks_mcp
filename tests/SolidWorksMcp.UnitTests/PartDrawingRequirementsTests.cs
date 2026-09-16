using SolidWorksMcp.EngineeringModel;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.UnitTests;

/// <summary>
/// Locks the private-drawing-informed semantic contract without storing any source PDF or confidential drawing text.
/// 用不含源 PDF 和秘密图纸原文的方式锁定 private-drawing-informed semantic contract。
/// </summary>
public sealed class PartDrawingRequirementsTests
{
    /// <summary>Reviewed bracket-like classes become stable coverage requirements, not a pile of anonymous annotations.</summary>
    [Fact]
    public void ReviewedPartClassesCreateStableCoverageGraph()
    {
        var graph = PartDrawingRequirementSet.FromReviewedClasses(
            "sheet-metal-part-review-profile",
            [
                PartDrawingSemanticClass.OrthographicViews,
                PartDrawingSemanticClass.IsometricView,
                PartDrawingSemanticClass.HoleFeature,
                PartDrawingSemanticClass.BendRadius,
                PartDrawingSemanticClass.Thickness,
                PartDrawingSemanticClass.Material,
                PartDrawingSemanticClass.GeneralTolerance,
                PartDrawingSemanticClass.SurfaceFinish,
                PartDrawingSemanticClass.TitleBlock,
            ],
            new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(9, graph.Requirements.Length);
        Assert.False(graph.CanRelease);
        Assert.Equal(graph.Requirements.Length - 1, graph.BlockingRequirementIds.Length);
        Assert.All(graph.Requirements, requirement =>
        {
            Assert.Equal(EngineeringRequirementStatus.ReviewRequired, requirement.Status);
            Assert.Equal("private_drawing_review", requirement.Provenance.SourceKind);
            Assert.DoesNotContain(".pdf", requirement.RequirementId.Value, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>Only explicit approval can clear the release gate; the optional isometric view does not block it.</summary>
    [Fact]
    public void HumanApprovalIsRequiredBeforeRelease()
    {
        var graph = PartDrawingRequirementSet.FromReviewedClasses(
            "part-profile",
            [PartDrawingSemanticClass.IsometricView, PartDrawingSemanticClass.HoleFeature],
            DateTimeOffset.UtcNow);

        PartDrawingRequirementSet approved = graph.Approve(
            graph.Requirements
                .Where(requirement => requirement.Required)
                .Select(requirement => requirement.RequirementId));

        Assert.True(approved.CanRelease);
        Assert.Empty(approved.BlockingRequirementIds);
        Assert.Equal(EngineeringRequirementStatus.ReviewRequired, approved.Requirements.Single(requirement => !requirement.Required).Status);
    }

    /// <summary>Duplicate semantic classes are deterministic and cannot create duplicate requirement identities.</summary>
    [Fact]
    public void DuplicateObservedClassesAreCollapsedDeterministically()
    {
        var graph = PartDrawingRequirementSet.FromReviewedClasses(
            "part-profile",
            [PartDrawingSemanticClass.HoleFeature, PartDrawingSemanticClass.HoleFeature],
            DateTimeOffset.UtcNow);

        Assert.Single(graph.Requirements);
        Assert.Equal("features.hole.size-location-callout", graph.Requirements[0].CoverageKey);
    }
}
