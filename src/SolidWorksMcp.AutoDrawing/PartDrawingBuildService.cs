using System.Collections.Immutable;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;
using SolidWorksMcp.RuleEngine;

namespace SolidWorksMcp.AutoDrawing;

/// <summary>
/// High-level deterministic part-to-drawing workflow used by the MCP façade.
/// 供 MCP façade 使用的高层、确定性“零件到工程图”工作流。
/// </summary>
/// <remarks>
/// This is deliberately an orchestration boundary, not a COM wrapper. It sequences vendor-neutral provider
/// contracts, keeps the source part and drawing identities explicit, and requires read-back evidence before returning
/// success. The future full compiler will replace the fixed view seed with model analysis and a rule-pack plan.
/// 这里刻意保持为编排边界而不是 COM wrapper：它串联厂商无关的 Provider contract，显式保留零件与工程图 identity，
/// 并要求在返回成功前取得读回证据。未来完整 compiler 会用 model analyze 和 RulePack plan 替换固定视图 seed。
/// </remarks>
public static class PartDrawingBuildService
{
    /// <summary>Executes one bounded 3D-to-2D part workflow in the supplied single-writer session.</summary>
    public static async Task<OperationResult<PartDrawingBuildResult>> ExecuteAsync(
        ICadSession session,
        PartDrawingBuildRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        string validationError = Validate(request);
        if (validationError.Length != 0)
        {
            return OperationResults.Failure<PartDrawingBuildResult>(
                "drawing.build",
                new OperationError(ErrorCodes.InvalidRequest, validationError, ErrorCategories.Validation));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return OperationResults.Failure<PartDrawingBuildResult>(
                "drawing.build",
                new OperationError(
                    ErrorCodes.Cancelled,
                    "The part-to-drawing build was cancelled before provider execution.",
                    ErrorCategories.Execution));
        }

        ICadPartDocument? part = null;
        ICadDrawingDocument? drawing = null;
        try
        {
            // The provider owns the actual geometry transaction. This layer only supplies a complete, explicit plan.
            // 真实几何 transaction 由 Provider 负责；本层只提交完整且显式的编排 plan。
            OperationResult<ICadPartDocument> created = await session.CreatePartAsync(
                new CreatePartRequest
                {
                    RequestedDocumentId = request.DocumentId,
                    Configuration = request.Configuration,
                    Path = request.PartPath,
                    InitialSketchProfile = request.InitialSketchProfile,
                },
                cancellationToken).ConfigureAwait(false);
            if (!created.IsSuccess || created.Value is null)
            {
                return Failure(created);
            }

            part = created.Value;
            OperationResult<BodySnapshot> body = await part.CreateBodyAsync(
                new CreateBodyRequest { Name = request.BodyName },
                cancellationToken).ConfigureAwait(false);
            if (!body.IsSuccess && body.Error?.Code != ErrorCodes.UnsupportedCapability)
            {
                return Failure(body);
            }

            OperationResult<FeatureSnapshot> extrusion = await part.AddExtrusionAsync(
                new ExtrusionRequest
                {
                    Name = request.ExtrusionFeatureName,
                    Depth = request.ExtrusionDepth,
                },
                cancellationToken).ConfigureAwait(false);
            if (!extrusion.IsSuccess || extrusion.Value is null)
            {
                return Failure(extrusion);
            }

            FeatureSnapshot? holePattern = null;
            if (request.ThroughHolePattern is not null)
            {
                // Keep the repeated-hole group as one engineering feature. The native provider creates the actual
                // sketch/cut and verifies its rebuilt solid; this orchestration layer must not flatten it into
                // anonymous cylinders. 重复孔组始终保留为一个工程 feature；native Provider 负责创建真实 sketch/cut
                // 并验证 rebuild 后 solid，本层不能把它退化成匿名圆柱。
                OperationResult<FeatureSnapshot> holeResult = await part.AddThroughHolePatternAsync(
                    request.ThroughHolePattern,
                    cancellationToken).ConfigureAwait(false);
                if (!holeResult.IsSuccess || holeResult.Value is null)
                {
                    return Failure(holeResult);
                }

                holePattern = holeResult.Value;
            }

            OperationResult<SaveReceipt> partSave = await part.SaveAsync(cancellationToken).ConfigureAwait(false);
            if (!partSave.IsSuccess)
            {
                return Failure(partSave);
            }

            OperationResult<ICadDrawingDocument> createdDrawing = await session.CreateDrawingAsync(
                new CreateDrawingRequest
                {
                    RequestedDocumentId = request.DrawingDocumentId,
                    Configuration = request.Configuration,
                    Path = request.DrawingPath,
                    SourceDocumentId = part.DocumentId,
                },
                cancellationToken).ConfigureAwait(false);
            if (!createdDrawing.IsSuccess || createdDrawing.Value is null)
            {
                return Failure(createdDrawing);
            }

            drawing = createdDrawing.Value;
            ImmutableArray<DrawingViewSnapshot>.Builder views = ImmutableArray.CreateBuilder<DrawingViewSnapshot>(3);
            foreach (DrawingSeed seed in DrawingSeeds(request.RulePack))
            {
                OperationResult<DrawingViewSnapshot> view = await drawing.AddViewAsync(
                    new DrawingViewRequest
                    {
                        RequestedViewId = new ViewId($"{request.DrawingDocumentId.Value}:{seed.Name.ToLowerInvariant()}"),
                        Name = seed.Name,
                        Orientation = seed.Orientation,
                        Position = new Coordinate2D(
                            Length.FromMillimeters(seed.XMillimeters),
                            Length.FromMillimeters(seed.YMillimeters)),
                        ScaleDenominator = request.ScaleDenominator,
                    },
                    cancellationToken).ConfigureAwait(false);
                if (!view.IsSuccess || view.Value is null)
                {
                    return Failure(view);
                }

                views.Add(view.Value);
            }

            // Model-dimensions intentionally uses a non-empty semantic request label only for FakeCad validation.
            // Native SOLIDWORKS ignores that label and returns the associative dimension text it actually inserted.
            // model-dimensions 只为 FakeCad validation 携带非空语义 label；native SOLIDWORKS 会忽略它并返回真正
            // 插入的 associative dimension text，绝不把请求文字伪装成原生尺寸。
            DrawingViewSnapshot? sectionView = null;
            if (request.ThroughHolePattern is not null && views.Count > 0)
            {
                // A verified internal-hole group is the first deterministic section trigger. The vertical cutting line
                // passes through the two symmetric hole centers in the Front view and the generated A-A view occupies
                // the free upper-right region. 已验证的内部孔组是首个确定性剖视触发条件；竖直剖切线穿过 Front view
                // 中两个对称孔中心，A-A view 放置在右上方空闲区域。
                OperationResult<DrawingViewSnapshot> section = await drawing.AddSectionViewAsync(
                    new DrawingSectionViewRequest
                    {
                        RequestedViewId = new ViewId($"{request.DrawingDocumentId.Value}:section-a-a"),
                        ParentViewId = views[0].ViewId,
                        Name = "Section A-A",
                        Label = "A",
                        // Place the section in the lower-right view band.  Y=210 mm is the sheet upper boundary for
                        // the current landscape template and clips the section label/geometry; keeping this explicit
                        // coordinate below the seed views makes the first deterministic layout useful before D06's
                        // general collision/reflow planner takes ownership.
                        // 剖视放在右下视图区。当前横向模板的 Y=210 mm 接近图幅上边界，会裁切剖视；在 D06 通用碰撞/重排
                        // planner 接管前，先用明确的下方坐标保证首个剖视具备工程可读性。
                        Position = new Coordinate2D(Length.FromMillimeters(210d), Length.FromMillimeters(75d)),
                        CutLineStart = new Coordinate2D(Length.FromMillimeters(90d), Length.FromMillimeters(90d)),
                        CutLineEnd = new Coordinate2D(Length.FromMillimeters(90d), Length.FromMillimeters(160d)),
                        ScaleDenominator = request.ScaleDenominator,
                        ScaleWithModel = true,
                    },
                    cancellationToken).ConfigureAwait(false);
                if (!section.IsSuccess || section.Value is null)
                {
                    return Failure(section);
                }

                sectionView = section.Value;
                views.Add(section.Value);
            }

            OperationResult<DrawingAnnotationSnapshot> modelDimensions = await drawing.AddAnnotationAsync(
                new DrawingAnnotationRequest
                {
                    RequestedAnnotationId = new AnnotationId($"{request.DrawingDocumentId.Value}:model-dimensions"),
                    ViewId = views[0].ViewId,
                    Kind = "model-dimensions",
                    Text = "native-model-dimensions",
                    CoverageKeys = ["part.profile.native-model-dimension"],
                    Position = new Coordinate2D(Length.FromMillimeters(45d), Length.FromMillimeters(225d)),
                },
                cancellationToken).ConfigureAwait(false);
            if (!modelDimensions.IsSuccess || modelDimensions.Value is null)
            {
                return Failure(modelDimensions);
            }

            DrawingAnnotationSnapshot? patternCallout = null;
            RepeatedFeatureCalloutPlan? patternCalloutPlan = null;
            if (request.ThroughHolePattern is not null && holePattern is not null)
            {
                // The compiler emits one semantic callout for the verified group. It never loops over centers and
                // creates one independent diameter annotation per hole. 编译器为已验证孔组生成一条语义标注，绝不
                // 遍历每个中心点生成互不关联的直径标注。
                patternCalloutPlan = RepeatedFeatureCalloutPlanner.Plan(request.ThroughHolePattern);
                OperationResult<DrawingAnnotationSnapshot> callout = await drawing.AddAnnotationAsync(
                    new DrawingAnnotationRequest
                    {
                        RequestedAnnotationId = new AnnotationId(
                            $"{request.DrawingDocumentId.Value}:pattern-callout:{holePattern.FeatureId.Value}"),
                        ViewId = views[0].ViewId,
                        Kind = "pattern-callout",
                        Text = patternCalloutPlan.Text,
                        CoverageKeys = patternCalloutPlan.CoverageKeys,
                        // Keep the deterministic group callout in the lower reserved note band, away from the Top view
                        // at Y=210 mm and the provider-selected model dimension. 将重复组语义标注固定在下方保留
                        // note band，避开 Y=210 mm 的 Top view 以及 Provider 自己布置的原生模型尺寸。
                        Position = new Coordinate2D(Length.FromMillimeters(45d), Length.FromMillimeters(35d)),
                    },
                    cancellationToken).ConfigureAwait(false);
                if (!callout.IsSuccess || callout.Value is null)
                {
                    return Failure(callout);
                }

                patternCallout = callout.Value;
            }

            OperationResult<RebuildReceipt> drawingRebuild = await drawing.RebuildAsync(cancellationToken).ConfigureAwait(false);
            if (!drawingRebuild.IsSuccess || drawingRebuild.Value is null || drawingRebuild.Value.HasErrors)
            {
                return Failure(drawingRebuild.IsSuccess
                    ? OperationResults.Failure<RebuildReceipt>(
                        drawingRebuild.OperationId,
                        new OperationError(
                            ErrorCodes.InvariantViolation,
                            "The generated drawing rebuild reported errors.",
                            ErrorCategories.Invariant),
                        drawingRebuild.Evidence)
                    : drawingRebuild);
            }

            OperationResult<SaveReceipt> drawingSave = await drawing.SaveAsync(cancellationToken).ConfigureAwait(false);
            if (!drawingSave.IsSuccess)
            {
                return Failure(drawingSave);
            }

            // Reopen is a persistence proof. A successful in-memory generation alone is not release evidence.
            // Reopen 是持久化证明；只在内存中生成成功不能作为 Release evidence。
            OperationResult<CadInspectionSnapshot> drawingInspection = await drawing.ReopenAndInspectAsync(cancellationToken).ConfigureAwait(false);
            if (!drawingInspection.IsSuccess || drawingInspection.Value is null)
            {
                return Failure(drawingInspection);
            }

            if (patternCallout is not null
                && !drawingInspection.Value.Annotations.Any(annotation =>
                    annotation.AnnotationId == patternCallout.AnnotationId
                    && annotation.Kind.Equals("pattern-callout", StringComparison.Ordinal)
                    && annotation.Text.Equals(patternCallout.Text, StringComparison.Ordinal)))
            {
                // Reopen evidence must prove the same persisted semantic note, not merely the in-memory request result.
                // Reopen evidence 必须证明同一条已持久化语义 note，而不是只证明内存中的 request result。
                return OperationResults.Failure<PartDrawingBuildResult>(
                    drawingInspection.OperationId,
                    new OperationError(
                        ErrorCodes.InvariantViolation,
                        "The persisted drawing did not contain the verified repeated-feature callout identity and text.",
                        ErrorCategories.Invariant,
                        remediation: "Preserve the drawing artifact and inspect native annotation identity before retrying."),
                    drawingInspection.Evidence);
            }

            OperationResult<ExportReceipt> pdf = await session.Export.ExportAsync(
                drawing.DocumentId,
                new CadExportRequest
                {
                    Format = "PDF",
                    TargetPath = request.PdfPath,
                },
                cancellationToken).ConfigureAwait(false);
            if (!pdf.IsSuccess || pdf.Value is null)
            {
                return Failure(pdf);
            }

            OperationResult<CadInspectionSnapshot> partInspection = await session.Inspection.InspectAsync(
                part.DocumentId,
                cancellationToken).ConfigureAwait(false);
            if (!partInspection.IsSuccess || partInspection.Value is null)
            {
                return Failure(partInspection);
            }

            var result = new PartDrawingBuildResult
            {
                Part = partInspection.Value,
                Drawing = drawingInspection.Value,
                Extrusion = extrusion.Value,
                HolePattern = holePattern,
                Views = views.ToImmutable(),
                SectionView = sectionView,
                ModelDimensions = modelDimensions.Value,
                PatternCallout = patternCallout,
                Pdf = pdf.Value,
                RulePackId = request.RulePack?.PackId,
                Projection = request.RulePack is null
                    ? null
                    : ToPlannerProjection(request.RulePack.Values.ProjectionMethod),
            };
            return OperationResults.Success(
                result,
                "drawing.build",
                new OperationEvidence(
                    "auto-drawing.build",
                    [
                        new EvidenceObservation("workflow", "part-profile-extrusion-drawing-pdf"),
                        new EvidenceObservation("part.document.id", part.DocumentId.Value),
                        new EvidenceObservation("drawing.document.id", drawing.DocumentId.Value),
                        new EvidenceObservation("drawing.view.count", drawingInspection.Value.Views.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        new EvidenceObservation("drawing.section-view", sectionView?.Name ?? "none"),
                        new EvidenceObservation("drawing.section-view.trigger", sectionView is null ? "not-requested" : "verified-internal-hole-group"),
                        new EvidenceObservation("drawing.annotation.count", drawingInspection.Value.Annotations.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        new EvidenceObservation("part.hole-pattern", holePattern?.Name ?? "none"),
                        new EvidenceObservation("part.hole-pattern.kind", holePattern?.Kind ?? "none"),
                        new EvidenceObservation(
                            "part.hole-pattern.count",
                            request.ThroughHolePattern?.Centers.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0"),
                        new EvidenceObservation(
                            "part.hole-pattern.diameter-millimeters",
                            request.ThroughHolePattern?.Diameter.Millimeters.ToString("G17", System.Globalization.CultureInfo.InvariantCulture) ?? "0"),
                        new EvidenceObservation("drawing.pattern-callout", patternCallout?.Text ?? "none"),
                        new EvidenceObservation("drawing.pattern-callout.distribution", patternCalloutPlan?.Distribution ?? "none"),
                        new EvidenceObservation("drawing.pattern-callout.coverage-count", patternCalloutPlan?.CoverageKeys.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0"),
                        new EvidenceObservation("drawing.pattern-callout.reopened", patternCallout is null ? "not-requested" : "verified"),
                        new EvidenceObservation("drawing.rule-pack.id", request.RulePack?.PackId ?? "legacy-unresolved"),
                        new EvidenceObservation(
                            "drawing.rule-pack.projection",
                            request.RulePack?.Values.ProjectionMethod.ToString() ?? "legacy-request"),
                        new EvidenceObservation(
                            "drawing.rule-pack.provenance.projection",
                            request.RulePack is null
                                ? "legacy-unresolved"
                                : ProjectionSourceId(request.RulePack)),
                        new EvidenceObservation("export.format", pdf.Value.Format),
                    ],
                    [part.Path, drawing.Path, pdf.Value.TargetPath]));
        }
        finally
        {
            // Close only the exact documents created by this bounded workflow. If a provider cannot close a dirty
            // document, it returns structured evidence; this cleanup never guesses an ActiveDoc or hides the failure.
            // 只关闭本 workflow 创建且 identity 精确的文档；若 Provider 无法关闭 dirty document，会返回结构化证据，
            // cleanup 绝不猜 ActiveDoc，也不吞掉前面的权威失败。
            if (drawing is not null)
            {
                await drawing.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }

            if (part is not null)
            {
                await part.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private static string Validate(PartDrawingBuildRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DocumentId.Value)
            || string.IsNullOrWhiteSpace(request.DrawingDocumentId.Value))
        {
            return "Part and drawing document identities are required.";
        }

        if (string.IsNullOrWhiteSpace(request.Configuration)
            || string.IsNullOrWhiteSpace(request.PartPath)
            || string.IsNullOrWhiteSpace(request.DrawingPath)
            || string.IsNullOrWhiteSpace(request.PdfPath))
        {
            return "Configuration, part path, drawing path and PDF path are required.";
        }

        if (request.ExtrusionDepth.Millimeters <= 0d || !double.IsFinite(request.ExtrusionDepth.Millimeters))
        {
            return "Extrusion depth must be finite and greater than zero.";
        }

        if (request.ScaleDenominator <= 0)
        {
            return "Drawing scale denominator must be greater than zero.";
        }

        if (request.RulePack is not null)
        {
            // The provider contract currently accepts integral 1:N denominators. The RulePack gate therefore rejects
            // an unapproved request before any part/drawing document is created.
            // 当前 Provider contract 接受整数 1:N 分母，因此 RulePack gate 必须在创建任何文档前拒绝未批准比例。
            if (string.IsNullOrWhiteSpace(request.RulePack.PackId))
            {
                return "Resolved drawing RulePack identity is required.";
            }

            if (!request.RulePack.Values.AllowedScaleDenominators.Any(value =>
                    Math.Abs(value.Value - request.ScaleDenominator) < 1e-9))
            {
                return $"Drawing scale denominator {request.ScaleDenominator} is not permitted by RulePack '{request.RulePack.PackId}'.";
            }
        }

        return SketchProfileValidation.Validate(request.InitialSketchProfile)
            ?? string.Empty;
    }

    private static IEnumerable<DrawingSeed> DrawingSeeds(ResolvedDrawingRulePack? rulePack)
    {
        yield return new DrawingSeed("Front", "Front", 90d, 125d);
        // First-angle places the projected top view below the front view; third-angle places it above. This is a
        // deliberate paper-space consequence of the resolved rule, not an ActiveDoc/UI guess.
        // 第一角把俯视图放在主视图下方，第三角放在上方；这是 resolved rule 的纸空间结果，不猜 ActiveDoc/UI。
        double projectedY = rulePack?.Values.ProjectionMethod switch
        {
            DrawingProjectionMethod.FirstAngle => 50d,
            DrawingProjectionMethod.ThirdAngle => 210d,
            _ => 210d,
        };
        yield return new DrawingSeed("Top", "Top", 90d, projectedY);
        yield return new DrawingSeed("Isometric", "Isometric", 210d, 125d);
    }

    private static PartDrawingProjectionMethod ToPlannerProjection(DrawingProjectionMethod method) =>
        method switch
        {
            DrawingProjectionMethod.FirstAngle => PartDrawingProjectionMethod.FirstAngle,
            DrawingProjectionMethod.ThirdAngle => PartDrawingProjectionMethod.ThirdAngle,
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown RulePack projection method."),
        };

    private static string ProjectionSourceId(ResolvedDrawingRulePack rulePack) =>
        rulePack.Provenance.TryGetValue("projection.method", out RuleValueProvenance? provenance)
            ? provenance.Source.SourceId
            : "unavailable";

    private static OperationResult<PartDrawingBuildResult> Failure<T>(OperationResult<T> failure) =>
        OperationResults.Failure<PartDrawingBuildResult>(failure.OperationId, failure.Error!, failure.Evidence);

    private readonly record struct DrawingSeed(string Name, string Orientation, double XMillimeters, double YMillimeters);
}

/// <summary>Input for one explicit part-to-drawing build.</summary>
public sealed record PartDrawingBuildRequest
{
    /// <summary>Stable source part document identity.</summary>
    public required DocumentId DocumentId { get; init; }

    /// <summary>Stable drawing document identity.</summary>
    public required DocumentId DrawingDocumentId { get; init; }

    /// <summary>Active configuration asserted for both documents.</summary>
    public string Configuration { get; init; } = "Default";

    /// <summary>Allowlisted persisted part path.</summary>
    public required string PartPath { get; init; }

    /// <summary>Allowlisted persisted drawing path.</summary>
    public required string DrawingPath { get; init; }

    /// <summary>Allowlisted PDF artifact path.</summary>
    public required string PdfPath { get; init; }

    /// <summary>Closed line/arc profile supplied by the engineering model.</summary>
    public required SketchProfileRequest InitialSketchProfile { get; init; }

    /// <summary>Verified extrusion thickness/depth in canonical millimetres.</summary>
    public required Length ExtrusionDepth { get; init; }

    /// <summary>Readable 1:N drawing scale denominator.</summary>
    public int ScaleDenominator { get; init; } = 1;

    /// <summary>
    /// Resolved vendor-neutral drawing rules used by this build.
    /// 本次 build 使用的已 resolve 厂商无关制图规则。
    /// </summary>
    public ResolvedDrawingRulePack? RulePack { get; init; }

    /// <summary>Stable semantic extrusion feature name.</summary>
    public string ExtrusionFeatureName { get; init; } = "Profile-Extrusion";

    /// <summary>Stable semantic body name for providers that require an explicit empty body.</summary>
    public string BodyName { get; init; } = "Body-1";

    /// <summary>
    /// Optional repeated through-hole group retained as one engineering feature.
    /// 可选的重复通孔组；作为一个工程 feature 保留，而不是展开成匿名孔面。
    /// </summary>
    public ThroughHolePatternRequest? ThroughHolePattern { get; init; }
}

/// <summary>Verified outputs of the bounded part-to-drawing workflow.</summary>
public sealed record PartDrawingBuildResult
{
    /// <summary>Post-build part inspection.</summary>
    public required CadInspectionSnapshot Part { get; init; }

    /// <summary>Post-reopen drawing inspection.</summary>
    public required CadInspectionSnapshot Drawing { get; init; }

    /// <summary>Verified native extrusion feature.</summary>
    public required FeatureSnapshot Extrusion { get; init; }

    /// <summary>Verified native repeated-hole group, when requested.</summary>
    public FeatureSnapshot? HolePattern { get; init; }

    /// <summary>Views requested by the deterministic seed plan.</summary>
    public required ImmutableArray<DrawingViewSnapshot> Views { get; init; }

    /// <summary>Verified native section view generated for the internal-hole trigger, when requested.</summary>
    public DrawingViewSnapshot? SectionView { get; init; }

    /// <summary>Native model-dimension annotation result.</summary>
    public required DrawingAnnotationSnapshot ModelDimensions { get; init; }

    /// <summary>One compressed semantic callout for the optional repeated feature group.</summary>
    public DrawingAnnotationSnapshot? PatternCallout { get; init; }

    /// <summary>Verified PDF export receipt.</summary>
    public required ExportReceipt Pdf { get; init; }

    /// <summary>Resolved RulePack identity recorded for audit.</summary>
    public string? RulePackId { get; init; }

    /// <summary>Projection method actually selected from the resolved RulePack.</summary>
    public PartDrawingProjectionMethod? Projection { get; init; }
}
