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

            FeatureSnapshot? slotCut = null;
            ImmutableArray<EvidenceObservation> slotEvidence = [];
            if (request.SlotCut is not null)
            {
                // A slot remains one semantic feature from engineering input through native sketch creation and the
                // drawing callout. It is not represented as four unrelated lines and two unrelated arcs in this layer.
                // 长圆槽从工程输入到 native sketch 创建和工程图 callout 始终保持一个语义 feature；本层不会把它拆成
                // 四条互不关联的线和两条互不关联的弧。
                OperationResult<FeatureSnapshot> slotResult = await part.AddSlotCutAsync(
                    request.SlotCut,
                    cancellationToken).ConfigureAwait(false);
                if (!slotResult.IsSuccess || slotResult.Value is null)
                {
                    return Failure(slotResult);
                }

                slotCut = slotResult.Value;
                if (slotResult.Evidence is not null)
                {
                    // Preserve native width/length/body evidence under a high-level namespace so MCP callers can
                    // audit the exact COM read-back without depending on provider-internal operation IDs.
                    // 将 native width/length/body evidence 放入高层 namespace，使 MCP caller 能审计 COM 读回结果，
                    // 同时不依赖 Provider 内部 operation ID。
                    slotEvidence =
                    [
                        .. slotResult.Evidence.Observations.Select(observation => new EvidenceObservation(
                            $"part.slot-cut.native.{observation.Key}",
                            observation.Value,
                            observation.ExpectedValue)),
                    ];
                }
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
            foreach (DrawingSeed seed in DrawingSeeds(request.RulePack, request.DetailView is not null))
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

            DrawingViewSnapshot? sectionView = null;
            ImmutableArray<EvidenceObservation> detailEvidence = [];
            DrawingViewSnapshot? detailView = null;

            if (request.ThroughHolePattern is not null && views.Count > 0)
            {
                // A verified internal-hole group is the first deterministic section trigger. The vertical cutting line
                // passes through the two symmetric hole centers in the Front view and the generated A-A view occupies
                // the free upper-right region. 已验证的内部孔组是首个确定性剖视触发条件；竖直剖切线穿过 Front view
                // 中两个对称孔中心，A-A view 放置在右上方空闲区域；Detail 的显式 parent binding 不受该 section
                // 的业务触发条件影响。The detail itself is materialized after all annotation writes below so its
                // final native display cache is the one that gets persisted.
                // Detail 的 native display cache 会在下方所有 annotation 写入之后 materialize 并持久化。
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

            // Route the model dimension through the same requirement -> plan -> native materializer path used by the
            // future PMI/Hole Wizard compiler. The old direct primitive call made a drawing, but bypassed the
            // manufacturing graph and could not prove that the imported annotation was release evidence. 这里把模型
            // 尺寸走与未来 PMI/Hole Wizard compiler 相同的 requirement -> plan -> native materializer 链路；旧的
            // 直接 primitive 虽然能画图，却绕过了制造图，也不能证明导入标注已经成为 release evidence。
            DrawingManufacturingAnnotationRequirement modelDimensionRequirement = new()
            {
                RequirementId = $"{request.DrawingDocumentId.Value}:profile-depth",
                FeatureIdentity = extrusion.Value.FeatureId.Value,
                Kind = ManufacturingAnnotationKind.ModelDimension,
                ViewId = views[0].ViewId,
                CoverageKeys = ["part.profile.native-model-dimension"],
            };
            DrawingManufacturingAnnotationPlan modelDimensionPlan = ManufacturingAnnotationPlanner.Plan(
                [modelDimensionRequirement],
                []);
            OperationResult<DrawingManufacturingAnnotationMaterialization> modelDimensionMaterialization =
                await ManufacturingAnnotationMaterializer.MaterializeAsync(
                    drawing,
                    modelDimensionPlan,
                    cancellationToken).ConfigureAwait(false);
            if (!modelDimensionMaterialization.IsSuccess
                || modelDimensionMaterialization.Value is null
                || modelDimensionMaterialization.Value.MaterializedAnnotations.Length != 1)
            {
                return Failure(modelDimensionMaterialization);
            }

            DrawingAnnotationSnapshot modelDimensions = modelDimensionMaterialization.Value.MaterializedAnnotations[0];
            DrawingManufacturingAnnotationPlan manufacturingAnnotations = modelDimensionMaterialization.Value.Plan;

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

            DrawingAnnotationSnapshot? slotCallout = null;
            if (request.SlotCut is not null && slotCut is not null)
            {
                double centerlineLengthMillimeters = Math.Sqrt(
                    Math.Pow(request.SlotCut.End.X.Millimeters - request.SlotCut.Start.X.Millimeters, 2d)
                    + Math.Pow(request.SlotCut.End.Y.Millimeters - request.SlotCut.Start.Y.Millimeters, 2d));
                string slotText = $"SLOT W{request.SlotCut.Width.Millimeters.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}; "
                    + $"C-C {centerlineLengthMillimeters.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}";
                OperationResult<DrawingAnnotationSnapshot> callout = await drawing.AddAnnotationAsync(
                    new DrawingAnnotationRequest
                    {
                        RequestedAnnotationId = new AnnotationId(
                            $"{request.DrawingDocumentId.Value}:slot-callout:{slotCut.FeatureId.Value}"),
                        ViewId = views[0].ViewId,
                        Kind = "slot-callout",
                        Text = slotText,
                        CoverageKeys =
                        [
                            $"feature.{NormalizeSemanticKey(request.SlotCut.Name)}.width",
                            $"feature.{NormalizeSemanticKey(request.SlotCut.Name)}.centerline-length",
                            $"feature.{NormalizeSemanticKey(request.SlotCut.Name)}.profile",
                        ],
                        Position = new Coordinate2D(Length.FromMillimeters(45d), Length.FromMillimeters(25d)),
                    },
                    cancellationToken).ConfigureAwait(false);
                if (!callout.IsSuccess || callout.Value is null)
                {
                    return Failure(callout);
                }

                slotCallout = callout.Value;
            }

            DrawingAnnotationSnapshot? surfaceFinish = null;
            ImmutableArray<EvidenceObservation> surfaceFinishEvidence = [];
            if (request.SurfaceFinish is not null)
            {
                // Surface finish is a first-class manufacturing requirement. The native provider owns the COM call,
                // while this compiler preserves approved provenance and coverage rather than inventing a roughness
                // value from geometry or visual similarity. 表面粗糙度是一级制造要求；native Provider 负责 COM 调用，
                // compiler 只保留已批准 provenance 与 coverage，不从几何或视觉相似性臆造粗糙度。
                OperationResult<DrawingAnnotationSnapshot> symbol = await drawing.AddSurfaceFinishSymbolAsync(
                    request.SurfaceFinish,
                    cancellationToken).ConfigureAwait(false);
                if (!symbol.IsSuccess || symbol.Value is null)
                {
                    return Failure(symbol);
                }

                surfaceFinish = symbol.Value;
                if (symbol.Evidence is not null)
                {
                    surfaceFinishEvidence =
                    [
                        .. symbol.Evidence.Observations.Select(observation => new EvidenceObservation(
                            $"drawing.surface-finish.native.{observation.Key}",
                            observation.Value,
                            observation.ExpectedValue)),
                    ];
                }
            }

            if (request.DetailView is not null)
            {
                // Create the derived Detail View after model-item and semantic-note writes. Some SOLIDWORKS 2022
                // drawing mutations invalidate an already-created derived-view display cache during rebuild; making
                // the detail the last view mutation lets its own read-back and the final rebuild prove its geometry.
                // 在 model-item 和语义 note 写入之后再创建 derived Detail View。SOLIDWORKS 2022 某些 drawing
                // mutation 会在 rebuild 时使已创建 derived view 的 display cache 失效；让 detail 成为最后一个
                // view mutation，才能由自身 read-back 与最终 rebuild 共同证明其几何仍然存在。
                OperationResult<DrawingViewSnapshot> detail = await drawing.AddDetailViewAsync(
                    request.DetailView,
                    cancellationToken).ConfigureAwait(false);
                if (!detail.IsSuccess || detail.Value is null)
                {
                    return Failure(detail);
                }

                detailView = detail.Value;
                views.Add(detail.Value);
                if (detail.Evidence is not null)
                {
                    // Preserve provider-native detail proof inside the compiler result, but namespace it under the
                    // high-level workflow so callers can distinguish it from ordinary view observations. 将 Provider
                    // 的 native detail proof 保留到 compiler result，并加上 workflow namespace，避免与普通 view
                    // observation 混淆；这些 facts 仍然来自真实 SOLIDWORKS read-back。
                    detailEvidence =
                    [
                        .. detail.Evidence.Observations.Select(observation => new EvidenceObservation(
                            $"drawing.detail.{observation.Key}",
                            observation.Value,
                            observation.ExpectedValue)),
                    ];
                }
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

            if (slotCallout is not null
                && !drawingInspection.Value.Annotations.Any(annotation =>
                    annotation.AnnotationId == slotCallout.AnnotationId
                    && annotation.Kind.Equals("slot-callout", StringComparison.Ordinal)
                    && annotation.Text.Equals(slotCallout.Text, StringComparison.Ordinal)))
            {
                return OperationResults.Failure<PartDrawingBuildResult>(
                    drawingInspection.OperationId,
                    new OperationError(
                        ErrorCodes.InvariantViolation,
                        "The persisted drawing did not contain the verified obround-slot callout identity and text.",
                        ErrorCategories.Invariant,
                        remediation: "Preserve the drawing artifact and inspect native annotation identity before retrying."),
                    drawingInspection.Evidence);
            }

            if (surfaceFinish is not null
                && !drawingInspection.Value.Annotations.Any(annotation =>
                    annotation.AnnotationId == surfaceFinish.AnnotationId
                    && annotation.Kind.Equals("surface-finish", StringComparison.Ordinal)))
            {
                return OperationResults.Failure<PartDrawingBuildResult>(
                    drawingInspection.OperationId,
                    new OperationError(
                        ErrorCodes.InvariantViolation,
                        "The persisted drawing did not contain the verified native surface-finish identity and kind.",
                        ErrorCategories.Invariant,
                        remediation: "Preserve the drawing artifact and inspect native surface-finish annotations before retrying."),
                    drawingInspection.Evidence);
            }

            if (!drawingInspection.Value.Annotations.Any(annotation => annotation.AnnotationId == modelDimensions.AnnotationId))
            {
                return OperationResults.Failure<PartDrawingBuildResult>(
                    drawingInspection.OperationId,
                    new OperationError(
                        ErrorCodes.InvariantViolation,
                        "The persisted drawing did not contain the verified native Model Item identity.",
                        ErrorCategories.Invariant,
                        remediation: "Preserve the drawing artifact and inspect native Model Items before retrying."),
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
                SlotCut = slotCut,
                Views = views.ToImmutable(),
                SectionView = sectionView,
                DetailView = detailView,
                ModelDimensions = modelDimensions,
                ManufacturingAnnotations = manufacturingAnnotations,
                PatternCallout = patternCallout,
                SlotCallout = slotCallout,
                SurfaceFinish = surfaceFinish,
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
                        new EvidenceObservation("drawing.detail-view", detailView?.Name ?? "none"),
                        new EvidenceObservation("drawing.detail-view.trigger", detailView is null ? "not-requested" : "explicit-approved-region"),
                        .. detailEvidence,
                        new EvidenceObservation("drawing.annotation.count", drawingInspection.Value.Annotations.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        new EvidenceObservation("drawing.manufacturing.native-import.count", manufacturingAnnotations.Items.Count(item => item.Code == "annotation-native-materialized").ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        new EvidenceObservation("drawing.manufacturing.plan-fingerprint", manufacturingAnnotations.Fingerprint),
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
                        new EvidenceObservation("part.slot-cut", slotCut?.Name ?? "none"),
                        new EvidenceObservation("part.slot-cut.kind", slotCut?.Kind ?? "none"),
                        new EvidenceObservation(
                            "part.slot-cut.width-millimeters",
                            request.SlotCut?.Width.Millimeters.ToString("G17", System.Globalization.CultureInfo.InvariantCulture) ?? "0"),
                        new EvidenceObservation(
                            "part.slot-cut.centerline-length-millimeters",
                            request.SlotCut is null
                                ? "0"
                                : Math.Sqrt(
                                    Math.Pow(request.SlotCut.End.X.Millimeters - request.SlotCut.Start.X.Millimeters, 2d)
                                    + Math.Pow(request.SlotCut.End.Y.Millimeters - request.SlotCut.Start.Y.Millimeters, 2d))
                                    .ToString("G17", System.Globalization.CultureInfo.InvariantCulture)),
                        new EvidenceObservation("drawing.slot-callout", slotCallout?.Text ?? "none"),
                        new EvidenceObservation("drawing.slot-callout.reopened", slotCallout is null ? "not-requested" : "verified"),
                        .. slotEvidence,
                        new EvidenceObservation("drawing.surface-finish", surfaceFinish?.Kind ?? "none"),
                        new EvidenceObservation("drawing.surface-finish.maximum-roughness", request.SurfaceFinish?.MaximumRoughness?.Trim() ?? "none"),
                        new EvidenceObservation("drawing.surface-finish.reopened", surfaceFinish is null ? "not-requested" : "verified"),
                        .. surfaceFinishEvidence,
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

        if (request.SlotCut is not null)
        {
            double centerlineLength = Math.Sqrt(
                Math.Pow(request.SlotCut.End.X.Millimeters - request.SlotCut.Start.X.Millimeters, 2d)
                + Math.Pow(request.SlotCut.End.Y.Millimeters - request.SlotCut.Start.Y.Millimeters, 2d));
            if (string.IsNullOrWhiteSpace(request.SlotCut.Name))
            {
                return "Slot semantic name is required.";
            }

            if (!double.IsFinite(request.SlotCut.Width.Millimeters) || request.SlotCut.Width.Millimeters <= 0d)
            {
                return "Slot width must be finite and greater than zero.";
            }

            if (!double.IsFinite(centerlineLength) || centerlineLength <= 0d)
            {
                return "Slot centerline endpoints must be finite and distinct.";
            }
        }

        if (request.SurfaceFinish is not null)
        {
            if (string.IsNullOrWhiteSpace(request.SurfaceFinish.RequestedAnnotationId.Value)
                || string.IsNullOrWhiteSpace(request.SurfaceFinish.ViewId.Value))
            {
                return "Surface-finish annotation and view identities are required.";
            }

            if (string.IsNullOrWhiteSpace(request.SurfaceFinish.MaximumRoughness)
                || string.IsNullOrWhiteSpace(request.SurfaceFinish.ProvenanceKind)
                || string.IsNullOrWhiteSpace(request.SurfaceFinish.ProvenanceMethod)
                || request.SurfaceFinish.ApprovalState is not (DrawingAnnotationApprovalState.Approved or DrawingAnnotationApprovalState.Released))
            {
                return "Surface-finish roughness requires approved provenance and a stable engineering value.";
            }
        }

        return SketchProfileValidation.Validate(request.InitialSketchProfile)
            ?? string.Empty;
    }

    private static IEnumerable<DrawingSeed> DrawingSeeds(
        ResolvedDrawingRulePack? rulePack,
        bool detailViewRequested)
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
        // Reserve the right-side label corridor for an explicit Detail View. The deterministic shift is part of the
        // seed plan, so it does not depend on screen pixels or a live ActiveDoc layout guess. 对显式 Detail View 预留
        // 右侧标签通道；这个确定性偏移属于 seed plan，不依赖屏幕像素或 live ActiveDoc 的布局猜测。
        yield return new DrawingSeed("Isometric", "Isometric", detailViewRequested ? 175d : 210d, 125d);
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

    private static string NormalizeSemanticKey(string value) => string.Concat(
            value.Trim().ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-'))
        .Trim('-');

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

    /// <summary>
    /// Optional native obround slot cut retained as one engineering feature.
    /// 可选的原生长圆槽切除；作为一个工程 feature 保留。
    /// </summary>
    public SlotCutRequest? SlotCut { get; init; }

    /// <summary>
    /// Optional approval-gated native surface-finish symbol retained as a manufacturing requirement.
    /// 可选的、经过审批门禁的 native 表面粗糙度符号；作为制造要求保留。
    /// </summary>
    public SurfaceFinishSymbolRequest? SurfaceFinish { get; init; }

    /// <summary>
    /// Optional explicit detail-view request. The request names the parent view, source circle and enlarged-view
    /// position; it is never inferred from a screenshot or an arbitrary active selection.
    /// 可选的显式局部放大视图请求；请求明确父视图、源圆和放大视图位置，绝不从截图或任意 active selection 猜测。
    /// </summary>
    public DrawingDetailViewRequest? DetailView { get; init; }
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

    /// <summary>Verified native obround slot cut, when requested.</summary>
    public FeatureSnapshot? SlotCut { get; init; }

    /// <summary>Views requested by the deterministic seed plan.</summary>
    public required ImmutableArray<DrawingViewSnapshot> Views { get; init; }

    /// <summary>Verified native section view generated for the internal-hole trigger, when requested.</summary>
    public DrawingViewSnapshot? SectionView { get; init; }

    /// <summary>Verified native detail view generated from an explicit approved source region, when requested.</summary>
    public DrawingViewSnapshot? DetailView { get; init; }

    /// <summary>Native model-dimension annotation result.</summary>
    public required DrawingAnnotationSnapshot ModelDimensions { get; init; }

    /// <summary>Verified manufacturing-annotation plan after native Model Item materialization.</summary>
    public DrawingManufacturingAnnotationPlan? ManufacturingAnnotations { get; init; }

    /// <summary>One compressed semantic callout for the optional repeated feature group.</summary>
    public DrawingAnnotationSnapshot? PatternCallout { get; init; }

    /// <summary>One deterministic slot callout, when an obround slot was requested.</summary>
    public DrawingAnnotationSnapshot? SlotCallout { get; init; }

    /// <summary>Verified native surface-finish symbol, when requested.</summary>
    public DrawingAnnotationSnapshot? SurfaceFinish { get; init; }

    /// <summary>Verified PDF export receipt.</summary>
    public required ExportReceipt Pdf { get; init; }

    /// <summary>Resolved RulePack identity recorded for audit.</summary>
    public string? RulePackId { get; init; }

    /// <summary>Projection method actually selected from the resolved RulePack.</summary>
    public PartDrawingProjectionMethod? Projection { get; init; }
}
