using SolidWorksMcp.AutoDrawing;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.EngineeringModel;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.UnitTests;

/// <summary>
/// D08 release-gate tests use deliberately broken and approved semantic snapshots.
/// D08 release gate tests 使用故意损坏和已批准的 semantic snapshot。
/// </summary>
public sealed class DrawingQaReleasePlannerTests
{
    /// <summary>A missing critical feature definition is a precise blocking finding.</summary>
    [Fact]
    public void MissingCriticalDimensionBlocksReleaseWithStableTarget()
    {
        DrawingQaRequest request = CreateRequest(
            dimensionFindings:
            [
                new PartDrawingDimensionCoverageFinding
                {
                    CoverageKey = "feature:HolePattern/diameter",
                    FeatureId = new FeatureId("HolePattern"),
                    FeatureName = "Mounting holes",
                    DefinitionKey = "diameter",
                    DefinitionName = "Diameter",
                    Classification = DrawingDimensionClassification.Functional,
                    Status = PartDrawingDimensionFindingStatus.Blocking,
                    Code = "missing-definition",
                    EvidenceCount = 0,
                    Explanation = "Mounting holes: Diameter is missing.",
                },
            ]);

        DrawingQaPlan plan = DrawingQaReleasePlanner.Analyze(request);

        Assert.False(plan.CanRelease);
        DrawingQaFinding finding = Assert.Single(plan.Findings.Where(value => value.Code == "dimension.missing-definition"));
        Assert.Equal(DrawingQaFindingStatus.Blocking, finding.Status);
        Assert.Equal("feature:HolePattern/definition:diameter", finding.TargetId);
        Assert.Empty(plan.RepairPlan.Actions);
    }

    /// <summary>Duplicate dimensions produce only a targeted suppression action for that semantic definition.</summary>
    [Fact]
    public void DuplicateDimensionEvidenceProducesTargetedRepairOnly()
    {
        DrawingQaRequest request = CreateRequest(
            dimensionFindings:
            [
                new PartDrawingDimensionCoverageFinding
                {
                    CoverageKey = "feature:Slot-S03/length",
                    FeatureId = new FeatureId("Slot-S03"),
                    FeatureName = "Slot S03",
                    DefinitionKey = "length",
                    DefinitionName = "Length",
                    Classification = DrawingDimensionClassification.Manufacturing,
                    Status = PartDrawingDimensionFindingStatus.Warning,
                    Code = "duplicate-dimension-evidence",
                    EvidenceCount = 2,
                    Explanation = "Slot S03: redundant evidence will be suppressed.",
                },
            ]);

        DrawingQaPlan plan = DrawingQaReleasePlanner.Analyze(request);

        Assert.True(plan.CanRelease);
        DrawingRepairAction action = Assert.Single(plan.RepairPlan.Actions);
        Assert.Equal("dimension.suppress-redundant", action.ActionCode);
        Assert.Equal("feature:Slot-S03/definition:length", action.TargetId);
        Assert.DoesNotContain(plan.RepairPlan.Actions, value => value.TargetId.Contains("unrelated", StringComparison.Ordinal));
    }

    /// <summary>A layout reflow action carries only the moved annotation identity and its planned paper position.</summary>
    [Fact]
    public void LayoutReflowProducesTargetedPositionRepair()
    {
        DrawingQaRequest request = CreateRequest(
            layout: new DrawingLayoutPlan
            {
                SheetId = "sheet-1",
                Fingerprint = "layout-reflow-001",
                Placements =
                [
                    new DrawingLayoutPlacement
                    {
                        ItemId = "annotation-001",
                        Kind = DrawingLayoutItemKind.Label,
                        Bounds = new DrawingLayoutRect(
                            Length.FromMillimeters(118d),
                            Length.FromMillimeters(68d),
                            Length.FromMillimeters(8d),
                            Length.FromMillimeters(4d)),
                        WasRepositioned = true,
                        Rationale = "shift-right",
                    },
                ],
                Findings =
                [
                    new DrawingLayoutFinding
                    {
                        Status = DrawingLayoutFindingStatus.Warning,
                        Code = "annotation-repositioned",
                        ItemIds = ["annotation-001"],
                        Explanation = "The annotation was moved by deterministic reflow.",
                    },
                ],
            });

        DrawingQaPlan plan = DrawingQaReleasePlanner.Analyze(request);

        DrawingRepairAction action = Assert.Single(plan.RepairPlan.Actions);
        Assert.Equal("layout.apply-planned-position", action.ActionCode);
        Assert.Equal("annotation-001", action.TargetId);
        Assert.NotNull(action.PlannedPosition);
        Assert.Equal(122d, action.PlannedPosition.Value.X.Millimeters, precision: 8);
        Assert.Equal(70d, action.PlannedPosition.Value.Y.Millimeters, precision: 8);
    }

    /// <summary>Missing layout or annotation proofs are unavailable checks and fail closed.</summary>
    [Fact]
    public void SkippedLayoutAndAnnotationChecksBlockRelease()
    {
        DrawingQaRequest request = CreateRequest(
            omitLayout: true,
            omitManufacturingAnnotations: true);

        DrawingQaPlan plan = DrawingQaReleasePlanner.Analyze(request);

        Assert.False(plan.CanRelease);
        Assert.Contains(plan.Findings, value => value.Code == "layout-check-unavailable" && value.Status == DrawingQaFindingStatus.Blocking);
        Assert.Contains(plan.Findings, value => value.Code == "annotation-check-unavailable" && value.Status == DrawingQaFindingStatus.Blocking);
    }

    /// <summary>A checksum-verified drawing and PDF produce a stable release manifest.</summary>
    [Fact]
    public void VerifiedArtifactsProduceStableReleaseManifest()
    {
        string drawingPath = Path.Combine(Path.GetTempPath(), $"drawing-qa-{Guid.NewGuid():N}.slddrw");
        string pdfPath = Path.Combine(Path.GetTempPath(), $"drawing-qa-{Guid.NewGuid():N}.pdf");
        try
        {
            File.WriteAllText(drawingPath, "native drawing fixture");
            File.WriteAllText(pdfPath, "pdf fixture");
            DrawingQaRequest request = CreateRequest(
                requiredFormats: ["SLDDRW", "PDF"],
                artifacts:
                [
                    DrawingQaReleasePlanner.VerifyArtifact("SLDDRW", drawingPath, "state-001"),
                    DrawingQaReleasePlanner.VerifyArtifact("PDF", pdfPath, "state-001"),
                ]);

            DrawingQaPlan first = DrawingQaReleasePlanner.Analyze(request);
            DrawingQaPlan second = DrawingQaReleasePlanner.Analyze(request);
            DrawingReleaseEvidenceManifest firstManifest = DrawingQaReleasePlanner.CreateManifest(
                request,
                first,
                new DateTimeOffset(2026, 9, 17, 0, 0, 0, TimeSpan.Zero));
            DrawingReleaseEvidenceManifest secondManifest = DrawingQaReleasePlanner.CreateManifest(
                request,
                second,
                new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));

            Assert.True(first.CanRelease);
            Assert.True(firstManifest.Released);
            Assert.Equal(first.Fingerprint, second.Fingerprint);
            Assert.Equal(firstManifest.Fingerprint, secondManifest.Fingerprint);
            Assert.Equal(64, firstManifest.Artifacts.Single(value => value.Format == "PDF").Sha256.Length);
            Assert.All(firstManifest.Artifacts, artifact => Assert.Equal("artifact-verified", artifact.VerificationCode));
        }
        finally
        {
            File.Delete(drawingPath);
            File.Delete(pdfPath);
        }
    }

    private static DrawingQaRequest CreateRequest(
        IEnumerable<PartDrawingDimensionCoverageFinding>? dimensionFindings = null,
        DrawingLayoutPlan? layout = null,
        DrawingManufacturingAnnotationPlan? manufacturingAnnotations = null,
        IEnumerable<string>? requiredFormats = null,
        IEnumerable<DrawingArtifactProof>? artifacts = null,
        bool omitLayout = false,
        bool omitManufacturingAnnotations = false) => new()
        {
            Drawing = new CadInspectionSnapshot
            {
                Document = new CadDocumentSummary
                {
                    DocumentId = new DocumentId("drawing-qa-001"),
                    DocumentType = CadDocumentType.Drawing,
                    Path = "C:\\qa\\drawing-qa-001.slddrw",
                    Configuration = "Default",
                    StateHash = "state-001",
                    IsDirty = false,
                },
            },
            Coverage = new PartDrawingCoverageReport
            {
                ProfileId = "single-part-approved",
                DimensionFindings = [.. dimensionFindings ?? []],
                LayoutPlan = omitLayout ? null : layout ?? ApprovedLayout(),
            },
            ManufacturingAnnotations = omitManufacturingAnnotations ? null : manufacturingAnnotations ?? ApprovedAnnotations(),
            RequiredArtifactFormats = [.. requiredFormats ?? ["SLDDRW", "PDF"]],
            Artifacts = [.. artifacts ?? ApprovedArtifacts()],
        };

    private static DrawingLayoutPlan ApprovedLayout() => new()
    {
        SheetId = "sheet-1",
        Fingerprint = "layout-proof-001",
        Findings =
        [
            new DrawingLayoutFinding
            {
                Status = DrawingLayoutFindingStatus.Pass,
                Code = "layout-clear",
                ItemIds = ["view-front"],
                Explanation = "The drawing view is clear.",
            },
        ],
    };

    private static DrawingManufacturingAnnotationPlan ApprovedAnnotations() => new()
    {
        Fingerprint = "annotation-proof-001",
        Items =
        [
            new DrawingManufacturingAnnotationPlanItem
            {
                RequirementId = "hole-callout-001",
                FeatureIdentity = "HolePattern",
                Kind = ManufacturingAnnotationKind.HoleCallout,
                Action = ManufacturingAnnotationPlanAction.RetainNative,
                Status = ManufacturingAnnotationFindingStatus.Pass,
                Code = "annotation-native-approved",
                Rationale = "Approved native associative hole callout.",
            },
        ],
    };

    private static DrawingArtifactProof[] ApprovedArtifacts() =>
    [
        new DrawingArtifactProof
        {
            Format = "SLDDRW",
            TargetPath = "C:\\qa\\drawing-qa-001.slddrw",
            Exists = true,
            IsVerified = true,
            Sha256 = new string('a', 64),
            SourceStateHash = "state-001",
            VerificationCode = "artifact-verified",
        },
        new DrawingArtifactProof
        {
            Format = "PDF",
            TargetPath = "C:\\qa\\drawing-qa-001.pdf",
            Exists = true,
            IsVerified = true,
            Sha256 = new string('b', 64),
            SourceStateHash = "state-001",
            VerificationCode = "artifact-verified",
        },
    ];
}
