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
            ImmutableArray<FeatureSnapshot> features = ReadFeatures(model, descriptor, bodies);
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
        ImmutableArray<BodySnapshot> bodies)
    {
        var snapshots = ImmutableArray.CreateBuilder<FeatureSnapshot>();
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

    private static (Coordinate3D Minimum, Coordinate3D Maximum) ToBoundingBox(double[] values)
    {
        if (values.Length < 6)
        {
            Coordinate3D zero = new(Length.FromMillimeters(0d), Length.FromMillimeters(0d), Length.FromMillimeters(0d));
            return (zero, zero);
        }

        return (
            new Coordinate3D(Length.FromMeters(values[0]), Length.FromMeters(values[2]), Length.FromMeters(values[4])),
            new Coordinate3D(Length.FromMeters(values[1]), Length.FromMeters(values[3]), Length.FromMeters(values[5])));
    }
}

/// <summary>Inspection service that resolves a registered path on the STA before reading native geometry evidence.</summary>
internal sealed class SolidWorksInspectionService(
    SolidWorksComSessionHost host,
    SolidWorksDocumentRegistry registry,
    SessionId sessionId,
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
            application =>
            {
                OperationResult<ModelDoc2> resolved = SolidWorksDocumentRouting.ResolveOpenDocument(application, descriptor);
                if (!resolved.IsSuccess || resolved.Value is null)
                {
                    return OperationResults.Failure<CadInspectionSnapshot>(resolved.OperationId, resolved.Error!, resolved.Evidence);
                }

                try
                {
                    return descriptor.DocumentType == CadDocumentType.Part
                        ? SolidWorksNativeInspectionReader.ReadPart(resolved.Value, descriptor)
                        : SolidWorksProviderResults.Unsupported<CadInspectionSnapshot>(
                            "inspect",
                            new CadCapability(
                                CadCapabilityNames.Inspection,
                                supported: false,
                                "B03 inspection currently supports native part documents only."));
                }
                finally
                {
                    SolidWorksDocumentRouting.Release(resolved.Value);
                }
            },
            cancellationToken).ConfigureAwait(false);
    }
}
