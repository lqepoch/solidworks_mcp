using System.Collections.Immutable;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Provider.SolidWorks;

/// <summary>STA-only native inspection reader for the first verifiable part workflow.</summary>
/// <remarks>
/// Inspection intentionally collects engineering evidence rather than trusting a COM success flag: body/feature
/// identity, bounding box, mass, volume and the provider state hash are returned together.  inspection 不只看 COM
/// bool 返回值，而是同时采集 body/feature identity、包围盒、质量、体积和 state hash 作为工程证据。
/// </remarks>
internal static class SolidWorksNativeInspectionReader
{
    /// <summary>Builds a part snapshot while all temporary COM objects remain on the dispatcher STA.</summary>
    public static OperationResult<CadInspectionSnapshot> ReadPart(
        ModelDoc2 model,
        SolidWorksDocumentDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(descriptor);

        try
        {
            if (!SolidWorksDocumentRouting.MatchesType(model, CadDocumentType.Part))
            {
                return SolidWorksProviderResults.Failure<CadInspectionSnapshot>(
                    "inspect",
                    new OperationError(
                        ErrorCodes.StateConflict,
                        "The native document is not a part at inspection time.",
                        ErrorCategories.State));
            }

            string stateHash = SolidWorksDocumentRouting.ComputeStateHash(model);
            ImmutableArray<BodySnapshot> bodies = ReadBodies(model, descriptor, out double totalMassKg, out double totalVolumeM3);
            ImmutableArray<FeatureSnapshot> features = ReadFeatures(model, descriptor, bodies, out ImmutableArray<CadDiagnostic> diagnostics);
            ImmutableArray<CadTopologyEntitySnapshot> topology = SolidWorksNativeTopologyReader.ReadPart(model, descriptor);
            var snapshot = new CadInspectionSnapshot
            {
                Document = new CadDocumentSummary
                {
                    DocumentId = descriptor.DocumentId,
                    DocumentType = CadDocumentType.Part,
                    Path = descriptor.Path,
                    Configuration = SolidWorksDocumentRouting.ReadConfiguration(model),
                    StateHash = stateHash,
                    IsDirty = model.GetSaveFlag(),
                },
                Bodies = bodies,
                Features = features,
                TopologyEntities = topology,
                Diagnostics = diagnostics,
            };
            return OperationResults.Success(
                snapshot,
                $"solidworks:inspect:{descriptor.DocumentId.Value}",
                new OperationEvidence(
                    "solidworks-inspection",
                    [
                        new EvidenceObservation("document.id", descriptor.DocumentId.Value),
                        new EvidenceObservation("body.count", bodies.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        new EvidenceObservation("feature.count", features.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        new EvidenceObservation("topology.entity.count", topology.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        new EvidenceObservation("diagnostic.count", diagnostics.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        new EvidenceObservation("diagnostic.error.count", diagnostics.Count(diagnostic => diagnostic.Severity is CadDiagnosticSeverity.Error).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        new EvidenceObservation("diagnostic.warning.count", diagnostics.Count(diagnostic => diagnostic.Severity is CadDiagnosticSeverity.Warning).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        new EvidenceObservation("mass.kg", totalMassKg.ToString("G17", System.Globalization.CultureInfo.InvariantCulture)),
                        new EvidenceObservation("volume.millimeters3", (totalVolumeM3 * 1_000_000_000d).ToString("G17", System.Globalization.CultureInfo.InvariantCulture)),
                        new EvidenceObservation("state.hash", stateHash),
                    ],
                    stateHash: stateHash));
        }
        catch (Exception exception)
        {
            return SolidWorksProviderResults.ProviderFailure<CadInspectionSnapshot>(
                "inspect",
                exception,
                "SOLIDWORKS part inspection failed before a complete evidence snapshot was returned.");
        }
    }

    /// <summary>
    /// Reads persisted drawing evidence, including sheet count, native drawing-view names and paper-space outlines.
    /// 读取已持久化工程图 evidence，包括 sheet 数量、原生 drawing-view 名称和纸空间包围盒。
    /// </summary>
    /// <remarks>
    /// SOLIDWORKS returns an array of arrays from IDrawingDoc.GetViews; the first item in each inner array is the
    /// sheet, followed by its drawing views. We intentionally keep this traversal in the provider and expose only
    /// vendor-neutral snapshots. SOLIDWORKS 的 IDrawingDoc.GetViews 返回数组套数组，每个内层数组第一项是 sheet，
    /// 后续才是 drawing views；这个遍历只留在 Provider 内部，向上只暴露 vendor-neutral snapshot。
    /// </remarks>
    public static OperationResult<CadInspectionSnapshot> ReadDrawing(
        ModelDoc2 model,
        SolidWorksDocumentDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(descriptor);

        try
        {
            if (!SolidWorksDocumentRouting.MatchesType(model, CadDocumentType.Drawing))
            {
                return SolidWorksProviderResults.Failure<CadInspectionSnapshot>(
                    "inspect",
                    new OperationError(
                        ErrorCodes.StateConflict,
                        "The native document is not a drawing at inspection time.",
                        ErrorCategories.State));
            }

            var drawing = (IDrawingDoc)model;
            var views = ImmutableArray.CreateBuilder<DrawingViewSnapshot>();
            var annotations = ImmutableArray.CreateBuilder<DrawingAnnotationSnapshot>();
            var annotationIdentities = new HashSet<string>(StringComparer.Ordinal);
            var outlines = ImmutableArray.CreateBuilder<string>();
            DrawingSheetSnapshot? sheetSnapshot = ReadSheetSnapshot(drawing);
            int ordinal = 0;
            foreach (View view in EnumerateDrawingViews(drawing))
            {
                ordinal++;
                try
                {
                    string name = view.GetName2()?.Trim() ?? $"View-{ordinal}";
                    string orientation = view.GetOrientationName()?.Trim() ?? name;
                    double[] position = ReadNumbers(view.Position);
                    double[] outline = ReadNumbers(view.GetOutline());
                    if (outline.Length >= 4)
                    {
                        outlines.Add(
                            $"{name}:{outline[0].ToString("G17", System.Globalization.CultureInfo.InvariantCulture)},"
                            + $"{outline[1].ToString("G17", System.Globalization.CultureInfo.InvariantCulture)},"
                            + $"{outline[2].ToString("G17", System.Globalization.CultureInfo.InvariantCulture)},"
                            + $"{outline[3].ToString("G17", System.Globalization.CultureInfo.InvariantCulture)}");
                    }

                    double x = position.Length > 0 ? position[0] : 0d;
                    double y = position.Length > 1 ? position[1] : 0d;
                    double scale = view.ScaleDecimal;
                    int? denominator = scale > 0d
                        ? Math.Max(1, (int)Math.Round(1d / scale, MidpointRounding.AwayFromZero))
                        : null;
                    views.Add(
                        new DrawingViewSnapshot
                        {
                            ViewId = new ViewId($"{descriptor.DocumentId.Value}:view:{ordinal}"),
                            Name = name,
                            // GetOrientationName is the native semantic orientation; GetName2 is only the generated
                            // drawing-view label (for example, "Drawing View1"). Do not infer orientation from the
                            // label because SOLIDWORKS renames labels during regeneration. GetOrientationName 是原生
                            // 语义方向；GetName2 只是生成的 drawing-view label，重建时可能被 SOLIDWORKS 改名。
                            Orientation = orientation,
                            Position = new Coordinate2D(Length.FromMeters(x), Length.FromMeters(y)),
                            ScaleDenominator = denominator,
                            Outline = outline.Length >= 4
                                ? new DrawingViewOutlineSnapshot
                                {
                                    Left = Length.FromMeters(outline[0]),
                                    Bottom = Length.FromMeters(outline[1]),
                                    Right = Length.FromMeters(outline[2]),
                                    Top = Length.FromMeters(outline[3]),
                                }
                                : null,
                        });
                    ReadDrawingAnnotations(
                        view,
                        descriptor.DocumentId.Value,
                        ordinal,
                        annotations,
                        annotationIdentities);
                }
                finally
                {
                    SolidWorksDocumentRouting.Release(view);
                }
            }

            string stateHash = SolidWorksDocumentRouting.ComputeStateHash(model);
            var snapshot = new CadInspectionSnapshot
            {
                Document = new CadDocumentSummary
                {
                    DocumentId = descriptor.DocumentId,
                    DocumentType = CadDocumentType.Drawing,
                    Path = descriptor.Path,
                    Configuration = SolidWorksDocumentRouting.ReadConfiguration(model),
                    StateHash = stateHash,
                    IsDirty = model.GetSaveFlag(),
                },
                Views = views.ToImmutable(),
                Sheet = sheetSnapshot,
                Annotations = annotations.ToImmutable(),
            };
            return OperationResults.Success(
                snapshot,
                $"solidworks:inspect:{descriptor.DocumentId.Value}",
                new OperationEvidence(
                    "solidworks-drawing-inspection",
                    [
                        new EvidenceObservation("document.id", descriptor.DocumentId.Value),
                        new EvidenceObservation("sheet.count", drawing.GetSheetCount().ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        new EvidenceObservation("sheet.name", sheetSnapshot?.Name ?? "missing"),
                        new EvidenceObservation("sheet.paper-size", sheetSnapshot?.PaperSize ?? "missing"),
                        new EvidenceObservation("sheet.size-millimeters", sheetSnapshot is null
                            ? "missing"
                            : $"{sheetSnapshot.Width.Millimeters:G17}x{sheetSnapshot.Height.Millimeters:G17}"),
                        new EvidenceObservation("sheet.projection", sheetSnapshot?.ProjectionMethod ?? "missing"),
                        new EvidenceObservation("sheet.template", sheetSnapshot?.TemplateName ?? "missing"),
                        new EvidenceObservation("view.count", views.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        new EvidenceObservation("annotation.count", annotations.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        new EvidenceObservation("view.outlines", string.Join('|', outlines)),
                        new EvidenceObservation("state.hash", stateHash),
                    ],
                    stateHash: stateHash));
        }
        catch (Exception exception)
        {
            return SolidWorksProviderResults.ProviderFailure<CadInspectionSnapshot>(
                "inspect",
                exception,
                "SOLIDWORKS drawing inspection failed before a complete view evidence snapshot was returned.");
        }
    }

    /// <summary>Reads native annotations exposed by each view without converting them into guessed text.</summary>
    /// <remarks>
    /// SOLIDWORKS exposes the high-level IAnnotation plus a type-specific Note or DisplayDimension. We preserve the
    /// native annotation kind and stable native name; coverage keys are deliberately empty after reopen because they
    /// belong to the higher-level requirement graph, not to arbitrary visible text. SOLIDWORKS 同时暴露通用
    /// IAnnotation 和具体 Note/DisplayDimension；这里保留 native kind/name，重开后不从可见文字猜 coverage。
    /// </remarks>
    private static void ReadDrawingAnnotations(
        View view,
        string documentIdentity,
        int viewOrdinal,
        ImmutableArray<DrawingAnnotationSnapshot>.Builder annotations,
        HashSet<string> annotationIdentities)
    {
        object? raw = view.GetAnnotations();
        if (raw is not Array nativeAnnotations)
        {
            return;
        }

        int localOrdinal = 0;
        for (int index = 0; index < nativeAnnotations.Length; index++)
        {
            if (nativeAnnotations.GetValue(index) is not Annotation annotation)
            {
                continue;
            }

            object? specific = null;
            try
            {
                localOrdinal++;
                string nativeName = annotation.GetName()?.Trim() ?? string.Empty;
                string identity = string.IsNullOrWhiteSpace(nativeName)
                    ? $"{documentIdentity}:annotation:view-{viewOrdinal}:{localOrdinal}"
                    : nativeName;
                if (!annotationIdentities.Add(identity))
                {
                    continue;
                }

                int nativeType = annotation.GetType();
                specific = annotation.GetSpecificAnnotation();
                string text = specific switch
                {
                    Note note => note.GetText()?.Trim() ?? string.Empty,
                    DisplayDimension dimension => SolidWorksNativeDimensionText.Read(dimension),
                    SFSymbol symbol => symbol.GetText((int)swSurfaceFinishSymbolText_e.swSFSymbolMaximumRoughness)?.Trim() ?? string.Empty,
                    _ => string.Empty,
                };
                double[] position = ReadNumbers(annotation.GetPosition());
                annotations.Add(
                    new DrawingAnnotationSnapshot
                    {
                        AnnotationId = new AnnotationId(identity),
                        ViewId = new ViewId($"{documentIdentity}:view:{viewOrdinal}"),
                        Kind = ToAnnotationKind(nativeType, identity),
                        Text = text,
                        Position = new Coordinate2D(
                            Length.FromMeters(position.Length > 0 ? position[0] : 0d),
                            Length.FromMeters(position.Length > 1 ? position[1] : 0d)),
                    });
            }
            finally
            {
                SolidWorksDocumentRouting.Release(specific);
                SolidWorksDocumentRouting.Release(annotation);
            }
        }
    }

    private static string ToAnnotationKind(int nativeType, string identity) => nativeType switch
    {
        (int)swAnnotationType_e.swNote when identity.Contains(":pattern-callout:", StringComparison.Ordinal) => "pattern-callout",
        (int)swAnnotationType_e.swNote when identity.Contains(":slot-callout:", StringComparison.Ordinal) => "slot-callout",
        (int)swAnnotationType_e.swNote => "note",
        (int)swAnnotationType_e.swDisplayDimension => "model-dimension",
        (int)swAnnotationType_e.swDatumTag => "datum",
        (int)swAnnotationType_e.swDatumTargetSym => "datum-target",
        (int)swAnnotationType_e.swGTol => "gdt",
        (int)swAnnotationType_e.swSFSymbol => "surface-finish",
        (int)swAnnotationType_e.swWeldSymbol => "weld",
        (int)swAnnotationType_e.swCThread => "cosmetic-thread",
        (int)swAnnotationType_e.swCenterMarkSym => "center-mark",
        (int)swAnnotationType_e.swCenterLine => "center-line",
        _ => $"native:{nativeType.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
    };

    /// <summary>Enumerates drawing views without exposing the sheet sentinel returned by GetViews.</summary>
    private static IEnumerable<View> EnumerateDrawingViews(IDrawingDoc drawing)
    {
        object? raw = drawing.GetViews();
        if (raw is not Array sheets)
        {
            yield break;
        }

        for (int sheetIndex = 0; sheetIndex < sheets.Length; sheetIndex++)
        {
            if (sheets.GetValue(sheetIndex) is not Array sheetViews)
            {
                continue;
            }

            // The first element is the sheet sentinel documented by SOLIDWORKS; only View RCWs are drawing views.
            // 官方文档规定第一项是 sheet sentinel；这里只 yield 真正的 View RCW。
            for (int viewIndex = 0; viewIndex < sheetViews.Length; viewIndex++)
            {
                if (sheetViews.GetValue(viewIndex) is View view)
                {
                    yield return view;
                }
            }
        }
    }

    private static ImmutableArray<BodySnapshot> ReadBodies(
        ModelDoc2 model,
        SolidWorksDocumentDescriptor descriptor,
        out double totalMassKg,
        out double totalVolumeM3)
    {
        totalMassKg = 0d;
        totalVolumeM3 = 0d;
        var snapshots = ImmutableArray.CreateBuilder<BodySnapshot>();
        var part = (IPartDoc)model;
        object? rawBodies = part.GetBodies2((int)swBodyType_e.swSolidBody, false);
        if (rawBodies is Array bodyArray)
        {
            for (int index = 0; index < bodyArray.Length; index++)
            {
                if (bodyArray.GetValue(index) is not IBody2 body)
                {
                    continue;
                }

                try
                {
                    double[] box = ReadNumbers(body.GetBodyBox());
                    (Coordinate3D minimum, Coordinate3D maximum) = ToBoundingBox(box);
                    string name = body.Name?.Trim() ?? $"Body-{index + 1}";
                    snapshots.Add(
                        new BodySnapshot
                        {
                            BodyId = new BodyId($"{descriptor.DocumentId.Value}:body:{index + 1}:{name}"),
                            Name = name,
                            FeatureCount = body.GetFeatureCount(),
                            BoundingBoxMinimum = minimum,
                            BoundingBoxMaximum = maximum,
                            Volume = Volume.FromCubicMillimeters(0d),
                            Mass = Mass.FromKilograms(0d),
                        });
                }
                finally
                {
                    SolidWorksDocumentRouting.Release(body);
                }
            }
        }

        MassProperty? massProperty = null;
        try
        {
            massProperty = model.Extension.CreateMassProperty();
            totalMassKg = Math.Max(0d, massProperty.Mass);
            totalVolumeM3 = Math.Max(0d, massProperty.Volume);
        }
        finally
        {
            SolidWorksDocumentRouting.Release(massProperty);
        }

        if (snapshots.Count > 0)
        {
            // The first B03 fixture is single-body.  Keep the aggregate measurement explicit until a later provider
            // version adds per-body mass-property extraction without guessing a COM return-array layout.
            // B03 首个 fixture 是单 body；在不猜 COM 返回数组布局前，先明确记录 aggregate measurement。
            BodySnapshot first = snapshots[0];
            snapshots[0] = first with
            {
                Volume = Volume.FromCubicMeters(totalVolumeM3),
                Mass = Mass.FromKilograms(totalMassKg),
            };
        }

        return snapshots.ToImmutable();
    }

    private static ImmutableArray<FeatureSnapshot> ReadFeatures(
        ModelDoc2 model,
        SolidWorksDocumentDescriptor descriptor,
        ImmutableArray<BodySnapshot> bodies,
        out ImmutableArray<CadDiagnostic> diagnostics)
    {
        var snapshots = ImmutableArray.CreateBuilder<FeatureSnapshot>();
        var diagnosticBuilder = ImmutableArray.CreateBuilder<CadDiagnostic>();
        var current = model.FirstFeature() as IFeature;
        BodyId owner = bodies.Length > 0
            ? bodies[0].BodyId
            : new BodyId($"{descriptor.DocumentId.Value}:body:unresolved");
        try
        {
            while (current is not null)
            {
                IFeature? next = null;
                try
                {
                    string name = current.Name?.Trim() ?? "UnnamedFeature";
                    string kind = current.GetTypeName2()?.Trim() ?? "Unknown";
                    int nativeErrorCode = current.GetErrorCode2(out bool isWarning);
                    if (nativeErrorCode != 0)
                    {
                        // GetErrorCode2 is the documented What's Wrong signal.  Preserve only a stable classification,
                        // the numeric code and the provider-stable feature identity; never copy modal/UI text.
                        // GetErrorCode2 是官方 What's Wrong 信号。这里只保留稳定分类、numeric code 和 Provider 稳定
                        // feature identity，绝不复制 modal/UI 原文。
                        diagnosticBuilder.Add(
                            new CadDiagnostic
                            {
                                Code = isWarning ? "feature.rebuild-warning" : "feature.rebuild-error",
                                Severity = isWarning ? CadDiagnosticSeverity.Warning : CadDiagnosticSeverity.Error,
                                Message = isWarning
                                    ? "SOLIDWORKS reported a rebuild warning on this feature."
                                    : "SOLIDWORKS reported a rebuild error on this feature.",
                                Scope = "feature",
                                EntityIdentity = $"{descriptor.DocumentId.Value}:feature:{name}",
                                NativeCode = nativeErrorCode,
                            });
                    }
                    snapshots.Add(
                        new FeatureSnapshot
                        {
                            FeatureId = new FeatureId($"{descriptor.DocumentId.Value}:feature:{name}"),
                            Name = name,
                            Kind = kind,
                            BodyId = owner,
                        });
                    next = current.GetNextFeature() as IFeature;
                }
                finally
                {
                    SolidWorksDocumentRouting.Release(current);
                }

                current = next;
            }
        }
        finally
        {
            SolidWorksDocumentRouting.Release(current);
        }

        diagnostics = diagnosticBuilder.ToImmutable();
        return snapshots.ToImmutable();
    }

    private static double[] ReadNumbers(object? value)
    {
        if (value is not Array array)
        {
            return [];
        }

        double[] values = new double[array.Length];
        for (int index = 0; index < array.Length; index++)
        {
            object? item = array.GetValue(index);
            values[index] = item is null ? 0d : Convert.ToDouble(item, System.Globalization.CultureInfo.InvariantCulture);
        }

        return values;
    }

    /// <summary>
    /// Reads ISheet::GetProperties2 plus GetSize and template name. The native sheet is the source of truth for layout;
    /// a template filename or a planner estimate is never substituted for this read-back.
    /// 读取 ISheet::GetProperties2、GetSize 和 template name；native sheet 才是布局事实来源，不能用模板文件名或
    /// planner 估算替代。
    /// </summary>
    private static DrawingSheetSnapshot? ReadSheetSnapshot(IDrawingDoc drawing)
    {
        object? raw = drawing.IGetCurrentSheet();
        if (raw is not ISheet sheet)
        {
            return null;
        }

        try
        {
            double[] properties = ReadNumbers(sheet.GetProperties2());
            if (properties.Length < 7)
            {
                return null;
            }

            double width = properties[5];
            double height = properties[6];
            if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0d || height <= 0d)
            {
                return null;
            }

            double sheetWidth = 0d;
            double sheetHeight = 0d;
            sheet.GetSize(ref sheetWidth, ref sheetHeight);
            if (sheetWidth > 0d && sheetHeight > 0d)
            {
                width = sheetWidth;
                height = sheetHeight;
            }

            int paperSize = Convert.ToInt32(properties[0], System.Globalization.CultureInfo.InvariantCulture);
            bool firstAngle = Math.Abs(properties[4]) > 0.5d;
            return new DrawingSheetSnapshot
            {
                Name = sheet.GetName()?.Trim() ?? "Sheet1",
                PaperSize = PaperSizeName(paperSize, width, height),
                Width = Length.FromMeters(width),
                Height = Length.FromMeters(height),
                IsLandscape = width >= height,
                ProjectionMethod = firstAngle ? "FirstAngle" : "ThirdAngle",
                TemplateName = Path.GetFileName(sheet.GetTemplateName()?.Trim() ?? string.Empty),
            };
        }
        finally
        {
            SolidWorksDocumentRouting.Release(sheet);
        }
    }

    private static string PaperSizeName(int paperSize, double widthMeters, double heightMeters) =>
        paperSize switch
        {
            (int)swDwgPaperSizes_e.swDwgPaperA4size or (int)swDwgPaperSizes_e.swDwgPaperA4sizeVertical => "A4",
            (int)swDwgPaperSizes_e.swDwgPaperA3size => "A3",
            (int)swDwgPaperSizes_e.swDwgPaperA2size => "A2",
            (int)swDwgPaperSizes_e.swDwgPaperA1size => "A1",
            (int)swDwgPaperSizes_e.swDwgPaperA0size => "A0",
            _ => $"custom:{widthMeters * 1000d:0.###}x{heightMeters * 1000d:0.###}",
        };

    private static (Coordinate3D Minimum, Coordinate3D Maximum) ToBoundingBox(double[] values)
    {
        if (values.Length < 6)
        {
            Coordinate3D zero = new(Length.FromMillimeters(0d), Length.FromMillimeters(0d), Length.FromMillimeters(0d));
            return (zero, zero);
        }

        // IBody2.GetBodyBox returns two XYZ corners, not six interleaved min/max values:
        // [XCorner1, YCorner1, ZCorner1, XCorner2, YCorner2, ZCorner2].  The previous
        // implementation treated the array as [XMin, XMax, YMin, YMax, ZMin, ZMax].
        // That happened to produce plausible values for some symmetric fixtures, but it
        // could make one axis appear zero-length and therefore was not valid geometry evidence.
        //
        // IBody2.GetBodyBox 返回的是两个 XYZ 角点，而不是六个交错的 min/max 值：
        // [XCorner1, YCorner1, ZCorner1, XCorner2, YCorner2, ZCorner2]。旧实现把数组
        // 错当成 [XMin, XMax, YMin, YMax, ZMin, ZMax]；对于某些对称 fixture 看起来仍然合理，
        // 但会导致某个轴被错误地报告为零长度，因此不能作为有效几何证据。
        // SOLIDWORKS only promises approximate corners; normalize each coordinate so that
        // the domain snapshot remains valid even when a rotated/negative-coordinate body is read.
        // SOLIDWORKS 只保证角点是近似值；这里逐坐标归一化，确保旋转或负坐标实体仍能形成有效快照。
        double x1 = values[0];
        double y1 = values[1];
        double z1 = values[2];
        double x2 = values[3];
        double y2 = values[4];
        double z2 = values[5];

        return (
            new Coordinate3D(
                Length.FromMeters(Math.Min(x1, x2)),
                Length.FromMeters(Math.Min(y1, y2)),
                Length.FromMeters(Math.Min(z1, z2))),
            new Coordinate3D(
                Length.FromMeters(Math.Max(x1, x2)),
                Length.FromMeters(Math.Max(y1, y2)),
                Length.FromMeters(Math.Max(z1, z2))));
    }
}

/// <summary>Inspection service that resolves a registered path on the STA before reading native geometry evidence.</summary>
internal sealed class SolidWorksInspectionService(
    SolidWorksComSessionHost host,
    SolidWorksDocumentRegistry registry,
    SessionId sessionId,
    string attachmentGeneration,
    Func<bool> isClosed) : ICadInspectionService
{
    /// <inheritdoc />
    public async Task<OperationResult<CadInspectionSnapshot>> InspectAsync(
        DocumentId documentId,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return SolidWorksProviderResults.Cancelled<CadInspectionSnapshot>("inspect");
        }

        if (isClosed())
        {
            return SolidWorksProviderResults.Closed<CadInspectionSnapshot>("inspect", sessionId);
        }

        if (!registry.TryGet(documentId, out SolidWorksDocumentDescriptor? descriptor) || descriptor is null)
        {
            return SolidWorksProviderResults.Failure<CadInspectionSnapshot>(
                "inspect",
                new OperationError(
                    ErrorCodes.NotFound,
                    "The requested native document identity is not registered in this provider session.",
                    ErrorCategories.State));
        }

        return await host.InvokeOnStaAsync(
            sessionId,
            attachmentGeneration,
            application =>
            {
                OperationResult<ModelDoc2> resolved = SolidWorksDocumentRouting.ResolveOpenDocument(application, descriptor);
                if (!resolved.IsSuccess || resolved.Value is null)
                {
                    return OperationResults.Failure<CadInspectionSnapshot>(resolved.OperationId, resolved.Error!, resolved.Evidence);
                }

                try
                {
                    OperationResult<CadInspectionSnapshot> inspection = descriptor.DocumentType switch
                    {
                        CadDocumentType.Part => SolidWorksNativeInspectionReader.ReadPart(resolved.Value, descriptor),
                        CadDocumentType.Drawing => SolidWorksNativeInspectionReader.ReadDrawing(resolved.Value, descriptor),
                        _ => SolidWorksProviderResults.Unsupported<CadInspectionSnapshot>(
                            "inspect",
                            new CadCapability(
                                CadCapabilityNames.Inspection,
                                supported: false,
                                "Native inspection currently covers parts and drawings; assembly evidence is a later slice.")),
                    };
                    if (!inspection.IsSuccess || inspection.Value is null)
                    {
                        return inspection;
                    }

                    // Refresh the registry on the same STA callback that read the model. This keeps the state hash and
                    // dirty flag ordered with the inspected native state before another queued operation can begin.
                    // 在读取 model 的同一个 STA callback 中刷新 registry，确保 state hash/dirty 与 inspection 顺序一致，
                    // 并在下一个 queued operation 开始前完成登记。
                    var refreshed = descriptor with
                    {
                        Configuration = inspection.Value.Document.Configuration,
                        StateHash = inspection.Value.Document.StateHash,
                        IsDirty = inspection.Value.Document.IsDirty,
                    };
                    if (!registry.TryUpdateDescriptor(descriptor, refreshed, out _))
                    {
                        registry.TryGet(descriptor.DocumentId, out SolidWorksDocumentDescriptor? latest);
                        return SolidWorksProviderResults.Failure<CadInspectionSnapshot>(
                            "inspect",
                            new OperationError(
                                ErrorCodes.StateConflict,
                                "The document registry changed while native inspection was being committed.",
                                ErrorCategories.State,
                                remediation: "Discard this inspection result and request a fresh inspection."),
                            new EvidenceObservation("expected-state-hash", descriptor.StateHash),
                            new EvidenceObservation("inspected-state-hash", refreshed.StateHash),
                            new EvidenceObservation("registered-state-hash", latest?.StateHash ?? "missing"));
                    }

                    return inspection;
                }
                finally
                {
                    SolidWorksDocumentRouting.Release(resolved.Value);
                }
            },
            cancellationToken).ConfigureAwait(false);
    }
}
