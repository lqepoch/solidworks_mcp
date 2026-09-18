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

            // Layout is planned before the drawing is opened, but only from the RulePack's sheet contract and the
            // actual requested model profile. The native provider must still read back the effective sheet before any
            // view or annotation mutation; this is a plan, never a visual-success assumption.
            // 图幅布局在打开 drawing 前生成，但只消费 RulePack 图幅契约和真实 profile；Provider 仍必须在任何视图/标注
            // mutation 前读回有效图纸。这里是 plan，不是“看起来成功”的假设。
            DrawingViewPlacementPlan layoutPlan = DrawingViewPlacementPlanner.Plan(
                request.InitialSketchProfile,
                request.ExtrusionDepth,
                request.ScaleDenominator,
                request.RulePack,
                needsSectionView: request.ThroughHolePattern is not null);
            OperationResult<ICadDrawingDocument> createdDrawing = await session.CreateDrawingAsync(
                new CreateDrawingRequest
                {
                    RequestedDocumentId = request.DrawingDocumentId,
                    Configuration = request.Configuration,
                    Path = request.DrawingPath,
                    SourceDocumentId = part.DocumentId,
                    // The GB RulePack selects the installed GB title-block/template family. The request carries only
                    // a semantic profile; the native provider discovers the actual template from the running
                    // SOLIDWORKS installation. GB RulePack 选择安装的 GB 标题栏/模板族；request 只携带语义 profile，
                    // 实际模板由 native provider 从当前 SOLIDWORKS 安装中发现。
                    TemplateProfile = request.RulePack?.PackId.Equals("GB.rulepack", StringComparison.OrdinalIgnoreCase) == true
                        ? DrawingTemplateProfile.GbMechanical
                        : DrawingTemplateProfile.SolidWorksDefault,
                    Sheet = layoutPlan.Sheet,
                },
                cancellationToken).ConfigureAwait(false);
            if (!createdDrawing.IsSuccess || createdDrawing.Value is null)
            {
                return Failure(createdDrawing);
            }

            drawing = createdDrawing.Value;
            OperationResult<CadInspectionSnapshot> initialDrawingInspection = await session.Inspection.InspectAsync(
                drawing.DocumentId,
                cancellationToken).ConfigureAwait(false);
            if (!initialDrawingInspection.IsSuccess || initialDrawingInspection.Value is null)
            {
                return Failure(initialDrawingInspection);
            }

            if (!SheetMatches(layoutPlan.Sheet, initialDrawingInspection.Value.Sheet))
            {
                return OperationResults.Failure<PartDrawingBuildResult>(
                    initialDrawingInspection.OperationId,
                    new OperationError(
                        ErrorCodes.InvariantViolation,
                        "The native drawing sheet did not match the RulePack sheet contract before view creation.",
                        ErrorCategories.Invariant,
                        details: new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["requested-paper-size"] = layoutPlan.Sheet.PaperSize,
                            ["actual-paper-size"] = initialDrawingInspection.Value.Sheet?.PaperSize ?? "missing",
                            ["actual-template"] = initialDrawingInspection.Value.Sheet?.TemplateName ?? "missing",
                        },
                        remediation: "Preserve the drawing and inspect the installed template/sheet setup; do not continue with guessed view coordinates."),
                    initialDrawingInspection.Evidence);
            }

            ImmutableArray<DrawingViewSnapshot>.Builder views = ImmutableArray.CreateBuilder<DrawingViewSnapshot>(3);
            foreach (PlannedDrawingView seed in layoutPlan.Views)
            {
                OperationResult<DrawingViewSnapshot> view = await drawing.AddViewAsync(
                    new DrawingViewRequest
                    {
                        RequestedViewId = new ViewId($"{request.DrawingDocumentId.Value}:{seed.Name.ToLowerInvariant()}"),
                        Name = seed.Name,
                        Orientation = seed.Orientation,
                        Position = seed.Position,
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
                        // The section position is derived from the same sheet/profile plan as the primary views. It
                        // therefore stays outside the GB title-block corridor when the model size changes.
                        // 剖视位置与主视图使用同一套图幅/profile plan，模型尺寸变化时仍避开 GB 标题栏保留区。
                        Position = layoutPlan.SectionViewPosition,
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
                        Position = layoutPlan.FeatureNotePosition,
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
                // Use a concise Chinese engineering note for the current semantic slot requirement. This is still a
                // compiler-owned note, not a claim that SOLIDWORKS has generated a native slot callout; release QA
                // must keep that distinction explicit until the native association path is implemented.
                // 当前语义长圆槽要求使用简洁中文工程注记；它仍是 compiler note，不冒充 SOLIDWORKS native slot callout，
                // 在 native 关联路径完成前，Release QA 必须保留这个区别。
                string slotText = $"长圆槽：槽宽{request.SlotCut.Width.Millimeters.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}，"
                    + $"中心距{centerlineLengthMillimeters.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}";
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
                        Position = layoutPlan.SlotNotePosition,
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

            ImmutableArray<DrawingAnnotationSnapshot> centerMarks = [];
            ImmutableArray<EvidenceObservation> centerMarkEvidence = [];
            if (request.CenterMarks is not null)
            {
                // Center marks remain native view annotations. The compiler sends one approved semantic request;
                // the provider performs SOLIDWORKS view recognition and returns every newly materialized mark. 中心
                // 标记保持为 native view annotation；compiler 只发送一个已批准语义请求，Provider 负责 SOLIDWORKS
                // view recognition，并返回每一个真正 materialize 的 mark。
                OperationResult<ImmutableArray<DrawingAnnotationSnapshot>> marks = await drawing.AddCenterMarksAsync(
                    request.CenterMarks,
                    cancellationToken).ConfigureAwait(false);
                if (!marks.IsSuccess || marks.Value.IsDefaultOrEmpty)
                {
                    return Failure(marks);
                }

                centerMarks = marks.Value;
                if (marks.Evidence is not null)
                {
                    centerMarkEvidence =
                    [
                        .. marks.Evidence.Observations.Select(observation => new EvidenceObservation(
                            $"drawing.center-mark.native.{observation.Key}",
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

            if (!centerMarks.IsDefaultOrEmpty
                && !centerMarks.All(centerMark => drawingInspection.Value.Annotations.Any(annotation =>
                    annotation.AnnotationId == centerMark.AnnotationId
                    && annotation.Kind.Equals("center-mark", StringComparison.Ordinal))))
            {
                return OperationResults.Failure<PartDrawingBuildResult>(
                    drawingInspection.OperationId,
                    new OperationError(
                        ErrorCodes.InvariantViolation,
                        "The persisted drawing did not contain every verified native center-mark identity.",
                        ErrorCategories.Invariant,
                        remediation: "Preserve the drawing artifact and inspect native center-mark annotations before retrying."),
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

            // The seed coordinates are only an initial constraint.  The release-facing layout proof must consume
            // the native sheet and every persisted IView.GetOutline() read-back after reopen.  This prevents a
            // visually plausible but geometrically invalid draft from being mistaken for a deterministic layout.
            // 初始坐标只能作为约束；面向 release 的布局证明必须消费 reopen 后 native sheet 与每个持久化
            // IView.GetOutline() 的读回结果，防止“看起来像图”但实际越界/重叠的草稿被误认为确定性布局。
            DrawingLayoutPlan? nativeLayoutPlan = BuildNativeLayoutPlan(
                drawingInspection.Value,
                request.RulePack);

            DrawingViewReflowPlan? nativeReflowPlan = null;
            ImmutableArray<DrawingViewRepairReceipt> reflowReceipts = [];
            if (NeedsViewReflow(nativeLayoutPlan))
            {
                // Reflow is a bounded second compiler pass. It consumes only persisted native outlines and sends
                // exact view-position repairs through the provider contract; it never edits COM from this layer.
                // 重排是一个有界的第二次 compiler pass，只消费 persisted native outline，并通过 Provider contract
                // 发送精确 view-position repair；本层绝不直接操作 COM。
                nativeReflowPlan = BuildNativeViewReflowPlan(drawingInspection.Value, request.RulePack);
                if (nativeReflowPlan is not null && nativeReflowPlan.CanApply)
                {
                    var receipts = ImmutableArray.CreateBuilder<DrawingViewRepairReceipt>();
                    string expectedStateHash = drawingInspection.Value.Document.StateHash;
                    foreach (DrawingViewReflowPlacement placement in nativeReflowPlan.Placements.Where(value => value.WasRepositioned))
                    {
                        OperationResult<DrawingViewRepairReceipt> repaired = await drawing.RepositionViewAsync(
                            new DrawingViewPositionRepairRequest
                            {
                                ViewId = placement.ViewId,
                                ExpectedDocumentStateHash = expectedStateHash,
                                PreconditionFingerprint = nativeReflowPlan.Fingerprint,
                                ExpectedCurrentPosition = placement.ExpectedCurrentPosition,
                                NewPosition = placement.NewPosition,
                            },
                            cancellationToken).ConfigureAwait(false);
                        if (!repaired.IsSuccess || repaired.Value is null)
                        {
                            // A native view may be alignment-constrained or may have changed after inspection. Do not
                            // claim a clean drawing; preserve the provider error and its current-state evidence.
                            // native view 可能受 alignment constraint 限制，也可能在 inspection 后被外部修改；不能宣称
                            // 图纸干净，直接保留 Provider error 与当前 state evidence。
                            return Failure(repaired);
                        }

                        receipts.Add(repaired.Value);
                        expectedStateHash = repaired.Value.StateHash;
                    }

                    reflowReceipts = receipts.ToImmutable();
                    if (!reflowReceipts.IsDefaultOrEmpty)
                    {
                        OperationResult<SaveReceipt> reflowSave = await drawing.SaveAsync(cancellationToken).ConfigureAwait(false);
                        if (!reflowSave.IsSuccess)
                        {
                            return Failure(reflowSave);
                        }

                        OperationResult<CadInspectionSnapshot> reflowInspection = await drawing.ReopenAndInspectAsync(cancellationToken).ConfigureAwait(false);
                        if (!reflowInspection.IsSuccess || reflowInspection.Value is null)
                        {
                            return Failure(reflowInspection);
                        }

                        foreach (DrawingViewRepairReceipt receipt in reflowReceipts)
                        {
                            DrawingViewSnapshot? persistedView = reflowInspection.Value.Views.FirstOrDefault(view => view.ViewId == receipt.ViewId);
                            if (persistedView is null || !NearlyEqual(persistedView.Position, receipt.Position) || persistedView.Outline is null)
                            {
                                return OperationResults.Failure<PartDrawingBuildResult>(
                                    reflowInspection.OperationId,
                                    new OperationError(
                                        ErrorCodes.InvariantViolation,
                                        "The persisted drawing did not contain the verified reflowed view position and outline.",
                                        ErrorCategories.Invariant,
                                        remediation: "Preserve the drawing artifact and inspect the exact native view binding."),
                                    reflowInspection.Evidence);
                            }
                        }

                        drawingInspection = reflowInspection;
                        nativeLayoutPlan = BuildNativeLayoutPlan(drawingInspection.Value, request.RulePack);
                    }
                }
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
                CenterMarks = centerMarks,
                Pdf = pdf.Value,
                LayoutPlan = nativeLayoutPlan,
                RulePackId = request.RulePack?.PackId,
                Projection = request.RulePack is null
                    ? null
                    : ToPlannerProjection(request.RulePack.Values.ProjectionMethod),
                Disposition = "DRAFT_REVIEW_REQUIRED",
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
                        new EvidenceObservation("drawing.center-mark.reopened", centerMarks.IsDefaultOrEmpty ? "not-requested" : "verified"),
                        .. centerMarkEvidence,
                        new EvidenceObservation("drawing.rule-pack.id", request.RulePack?.PackId ?? "legacy-unresolved"),
                        new EvidenceObservation("drawing.disposition", "DRAFT_REVIEW_REQUIRED"),
                        new EvidenceObservation("drawing.release-authority", "drawing.release"),
                        new EvidenceObservation(
                            "drawing.layout.native-proof",
                            nativeLayoutPlan is null ? "unavailable-review-required" : "verified-from-reopen-outlines"),
                        new EvidenceObservation(
                            "drawing.layout.can-release",
                            nativeLayoutPlan?.CanRelease.ToString() ?? "False"),
                        new EvidenceObservation(
                            "drawing.layout.fingerprint",
                            nativeLayoutPlan?.Fingerprint ?? "missing-native-outline-proof"),
                        new EvidenceObservation(
                            "drawing.layout.findings",
                            nativeLayoutPlan is null
                                ? "missing-native-outline"
                                : string.Join(",", nativeLayoutPlan.Findings.Select(finding => finding.Code).Distinct(StringComparer.Ordinal))),
                        new EvidenceObservation(
                            "drawing.layout.reflow.plan",
                            nativeReflowPlan?.Fingerprint ?? "not-required"),
                        new EvidenceObservation(
                            "drawing.layout.reflow.mutations",
                            reflowReceipts.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        new EvidenceObservation(
                            "drawing.layout.reflow.status",
                            nativeReflowPlan is null
                                ? "not-required"
                                : nativeReflowPlan.CanApply
                                    ? reflowReceipts.IsDefaultOrEmpty ? "planned-no-mutation" : "applied-and-reopened"
                                    : "blocked-review-required"),
                        new EvidenceObservation("drawing.sheet.plan.name", layoutPlan.SheetName),
                        new EvidenceObservation(
                            "drawing.sheet.plan.size-millimeters",
                            $"{layoutPlan.SheetWidth.Millimeters.ToString("G17", System.Globalization.CultureInfo.InvariantCulture)}x{layoutPlan.SheetHeight.Millimeters.ToString("G17", System.Globalization.CultureInfo.InvariantCulture)}"),
                        new EvidenceObservation("drawing.layout.plan.rationale", string.Join(" | ", layoutPlan.Rationale)),
                        new EvidenceObservation("drawing.sheet.actual.name", drawingInspection.Value.Sheet?.Name ?? "missing"),
                        new EvidenceObservation("drawing.sheet.actual.paper-size", drawingInspection.Value.Sheet?.PaperSize ?? "missing"),
                        new EvidenceObservation("drawing.sheet.actual.size-millimeters", drawingInspection.Value.Sheet is null
                            ? "missing"
                            : $"{drawingInspection.Value.Sheet.Width.Millimeters:G17}x{drawingInspection.Value.Sheet.Height.Millimeters:G17}"),
                        new EvidenceObservation("drawing.sheet.actual.projection", drawingInspection.Value.Sheet?.ProjectionMethod ?? "missing"),
                        new EvidenceObservation("drawing.sheet.actual.template", drawingInspection.Value.Sheet?.TemplateName ?? "missing"),
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

    /// <summary>Compares the compiler contract with native sheet read-back before any view mutation.</summary>
    /// <summary>在任何视图 mutation 前比较 compiler contract 与 native sheet 读回结果。</summary>
    private static bool SheetMatches(DrawingSheetRequest expected, DrawingSheetSnapshot? actual)
    {
        if (actual is null
            || !actual.PaperSize.Equals(expected.PaperSize, StringComparison.OrdinalIgnoreCase)
            || !actual.ProjectionMethod.Equals(expected.ProjectionMethod, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Math.Abs(actual.Width.Millimeters - expected.Width.Millimeters) <= 0.01d
            && Math.Abs(actual.Height.Millimeters - expected.Height.Millimeters) <= 0.01d;
    }

    private static string NormalizeSemanticKey(string value) => string.Concat(
            value.Trim().ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-'))
        .Trim('-');

    /// <summary>
    /// Builds a provider-neutral layout proof from persisted native outlines.
    /// 从持久化 native outline 构造厂商无关的布局证明。
    /// </summary>
    /// <remarks>
    /// A missing outline is intentionally represented by null rather than a guessed rectangle.  FakeCad and older
    /// providers may still produce a draft without this evidence; the release gate must treat that state as review
    /// required. 缺失 outline 时故意返回 null，而不是猜一个矩形。FakeCad 或旧 Provider 可以继续生成 draft，
    /// 但 release gate 必须把这种状态当作 review required。
    /// </remarks>
    private static DrawingLayoutPlan? BuildNativeLayoutPlan(
        CadInspectionSnapshot inspection,
        ResolvedDrawingRulePack? rulePack)
    {
        DrawingSheetSnapshot? sheet = inspection.Sheet;
        if (sheet is null || inspection.Views.IsDefaultOrEmpty)
        {
            return null;
        }

        DrawingViewSnapshot[] views = [.. inspection.Views];
        if (views.Any(view => view.Outline is null))
        {
            return null;
        }

        Length margin = rulePack?.Values.SheetMargin ?? Length.FromMillimeters(0d);
        Length annotationSpacing = rulePack?.Values.DimensionSpacing ?? Length.FromMillimeters(2d);
        Length tierSpacing = rulePack?.Values.DimensionSpacing ?? Length.FromMillimeters(6d);
        ReservedZoneRule[] reservedZones = rulePack?.Values.ReservedZones.ToArray() ?? [];

        DrawingLayoutRequest request = new()
        {
            SheetId = sheet.Name,
            SheetBounds = new DrawingLayoutRect(
                Length.FromMillimeters(0d),
                Length.FromMillimeters(0d),
                sheet.Width,
                sheet.Height),
            Margins = new DrawingLayoutMargins(margin, margin, margin, margin),
            MinimumAnnotationSpacing = annotationSpacing,
            DimensionTierSpacing = tierSpacing,
            Items =
            [
                .. views.Select(view => new DrawingLayoutItem
                {
                    ItemId = $"view:{view.ViewId.Value}",
                    Kind = DrawingLayoutItemKind.View,
                    RequestedBounds = ToLayoutRect(view.Outline!),
                    ViewId = view.ViewId.Value,
                    IsFixed = true,
                }),
            ],
            ReservedZones =
            [
                .. reservedZones.Select(zone => new DrawingLayoutReservedZone
                {
                    ZoneId = zone.ZoneId,
                    ZoneKind = "rule-pack-reserved-zone",
                    Bounds = new DrawingLayoutRect(zone.Left, zone.Bottom, zone.Width, zone.Height),
                }),
            ],
            AllowScaleReduction = true,
            AllowSheetUpgrade = true,
            AllowAdditionalSheet = true,
            AllowDetailView = true,
        };

        return PartDrawingLayoutPlanner.Plan(request);
    }

    /// <summary>
    /// Creates a view-only reflow plan only when native layout evidence reports a geometry blocker.
    /// 只有 native layout evidence 报告几何 blocker 时，才创建 view-only reflow plan。
    /// </summary>
    private static DrawingViewReflowPlan? BuildNativeViewReflowPlan(
        CadInspectionSnapshot inspection,
        ResolvedDrawingRulePack? rulePack)
    {
        DrawingSheetSnapshot? sheet = inspection.Sheet;
        if (sheet is null || inspection.Views.IsDefaultOrEmpty || inspection.Views.Any(view => view.Outline is null))
        {
            return null;
        }

        Length margin = rulePack?.Values.SheetMargin ?? Length.FromMillimeters(0d);
        Length spacing = rulePack?.Values.DimensionSpacing ?? Length.FromMillimeters(2d);
        ReservedZoneRule[] reservedZones = rulePack?.Values.ReservedZones.ToArray() ?? [];
        return DrawingViewReflowPlanner.Plan(
            new DrawingViewReflowRequest
            {
                SheetId = sheet.Name,
                SheetBounds = new DrawingLayoutRect(Length.FromMillimeters(0d), Length.FromMillimeters(0d), sheet.Width, sheet.Height),
                Margins = new DrawingLayoutMargins(margin, margin, margin, margin),
                Views = inspection.Views,
                ReservedZones =
                [
                    .. reservedZones.Select(zone => new DrawingLayoutReservedZone
                    {
                        ZoneId = zone.ZoneId,
                        ZoneKind = "rule-pack-reserved-zone",
                        Bounds = new DrawingLayoutRect(zone.Left, zone.Bottom, zone.Width, zone.Height),
                    }),
                ],
                MinimumViewSpacing = spacing,
                MaxReflowRings = 8,
            });
    }

    private static bool NeedsViewReflow(DrawingLayoutPlan? plan) =>
        plan is not null
        && plan.Findings.Any(finding => finding.Status is DrawingLayoutFindingStatus.Blocking
            && (finding.Code is "view-view-collision" or "off-sheet-view" or "reserved-zone-collision"));

    private static bool NearlyEqual(Coordinate2D first, Coordinate2D second) =>
        Math.Abs(first.X.Millimeters - second.X.Millimeters) <= 0.000001d
        && Math.Abs(first.Y.Millimeters - second.Y.Millimeters) <= 0.000001d;

    private static DrawingLayoutRect ToLayoutRect(DrawingViewOutlineSnapshot outline)
    {
        double width = outline.Right.Millimeters - outline.Left.Millimeters;
        double height = outline.Top.Millimeters - outline.Bottom.Millimeters;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0d || height <= 0d)
        {
            throw new InvalidOperationException("A native drawing view returned a non-positive paper-space outline.");
        }

        return new DrawingLayoutRect(outline.Left, outline.Bottom, Length.FromMillimeters(width), Length.FromMillimeters(height));
    }

    private static OperationResult<PartDrawingBuildResult> Failure<T>(OperationResult<T> failure) =>
        OperationResults.Failure<PartDrawingBuildResult>(failure.OperationId, failure.Error!, failure.Evidence);

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
    /// Optional approval-gated native center-mark request for one exact drawing view.
    /// 可选的、经过审批门禁的精确 drawing view native center-mark 请求。
    /// </summary>
    public DrawingCenterMarkRequest? CenterMarks { get; init; }

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

    /// <summary>Verified native center marks materialized by the provider.</summary>
    public ImmutableArray<DrawingAnnotationSnapshot> CenterMarks { get; init; } = [];

    /// <summary>Verified PDF export receipt.</summary>
    public required ExportReceipt Pdf { get; init; }

    /// <summary>
    /// Layout QA plan generated from persisted native sheet/view outlines, when the provider supplied them.
    /// 基于持久化 native 图幅/视图 outline 生成的布局 QA plan；Provider 未提供证据时为空。
    /// </summary>
    public DrawingLayoutPlan? LayoutPlan { get; init; }

    /// <summary>Resolved RulePack identity recorded for audit.</summary>
    public string? RulePackId { get; init; }

    /// <summary>Projection method actually selected from the resolved RulePack.</summary>
    public PartDrawingProjectionMethod? Projection { get; init; }

    /// <summary>
    /// High-level build disposition. This compiler endpoint produces a persisted draft; only drawing.release can
    /// convert it to a release artifact after the independent QA gate passes.
    /// 高层构建状态；本 compiler endpoint 只生成已持久化 draft，必须通过独立 QA gate 的 drawing.release 才能发布。
    /// </summary>
    public string Disposition { get; init; } = "DRAFT_REVIEW_REQUIRED";
}
