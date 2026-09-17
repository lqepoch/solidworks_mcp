using System.Collections.Immutable;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

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
            foreach (DrawingSeed seed in DrawingSeeds())
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
                Views = views.ToImmutable(),
                ModelDimensions = modelDimensions.Value,
                Pdf = pdf.Value,
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
                        new EvidenceObservation("drawing.annotation.count", drawingInspection.Value.Annotations.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
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

        return SketchProfileValidation.Validate(request.InitialSketchProfile)
            ?? string.Empty;
    }

    private static IEnumerable<DrawingSeed> DrawingSeeds()
    {
        yield return new DrawingSeed("Front", "Front", 90d, 125d);
        yield return new DrawingSeed("Top", "Top", 90d, 210d);
        yield return new DrawingSeed("Isometric", "Isometric", 210d, 125d);
    }

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

    /// <summary>Stable semantic extrusion feature name.</summary>
    public string ExtrusionFeatureName { get; init; } = "Profile-Extrusion";

    /// <summary>Stable semantic body name for providers that require an explicit empty body.</summary>
    public string BodyName { get; init; } = "Body-1";
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

    /// <summary>Views requested by the deterministic seed plan.</summary>
    public required ImmutableArray<DrawingViewSnapshot> Views { get; init; }

    /// <summary>Native model-dimension annotation result.</summary>
    public required DrawingAnnotationSnapshot ModelDimensions { get; init; }

    /// <summary>Verified PDF export receipt.</summary>
    public required ExportReceipt Pdf { get; init; }
}
