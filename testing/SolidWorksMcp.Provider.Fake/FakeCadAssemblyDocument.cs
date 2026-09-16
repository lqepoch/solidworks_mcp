using System.Collections.Immutable;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Provider.Fake;

/// <summary>Deterministic in-memory assembly document with explicit component loading states and mates.</summary>
internal sealed class FakeCadAssemblyDocument(FakeCadSession session, DocumentId documentId, string path, string configuration) : FakeCadDocument(session, documentId, path, configuration, CadDocumentType.Assembly), ICadAssemblyDocument
{
    private readonly List<ComponentSnapshot> components = [];
    private readonly List<MateSnapshot> mates = [];
    private int componentSequence;
    private int mateSequence;

    /// <inheritdoc />
    public Task<OperationResult<ComponentSnapshot>> InsertComponentAsync(
        InsertComponentRequest request,
        CancellationToken cancellationToken = default)
    {
        const string operation = "insert-component";
        if (request is null)
        {
            return Task.FromResult(FakeCadResults.Invalid<ComponentSnapshot>(operation, "The component request is required."));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(FakeCadResults.Cancelled<ComponentSnapshot>(operation));
        }

        if (!Session.Supports(CadCapabilityNames.AssemblyMutation, out CadCapability capability))
        {
            return Task.FromResult(FakeCadResults.Unsupported<ComponentSnapshot>(operation, capability));
        }

        if (Session.IsClosed)
        {
            return Task.FromResult(FakeCadResults.Closed<ComponentSnapshot>(operation));
        }

        if (Session.Failures.TryTake(FakeCadFailurePoints.InsertComponent, out OperationError? injectedError))
        {
            return Task.FromResult(FakeCadResults.Failure<ComponentSnapshot>(operation, injectedError!));
        }

        if (!Session.ContainsDocument(request.ReferencedDocumentId))
        {
            return Task.FromResult(FakeCadResults.NotFound<ComponentSnapshot>(operation, request.ReferencedDocumentId.Value));
        }

        if (string.IsNullOrWhiteSpace(request.Configuration))
        {
            return Task.FromResult(FakeCadResults.Invalid<ComponentSnapshot>(operation, "Component configuration is required."));
        }

        ComponentId componentId = request.RequestedComponentId ?? new ComponentId($"component-{++componentSequence:000}");
        if (components.Any(component => component.ComponentId == componentId))
        {
            return Task.FromResult(FakeCadResults.Invalid<ComponentSnapshot>(operation, $"Component identity '{componentId.Value}' already exists."));
        }

        var snapshot = new ComponentSnapshot
        {
            ComponentId = componentId,
            ReferencedDocumentId = request.ReferencedDocumentId,
            Configuration = request.Configuration.Trim(),
            Origin = request.Origin,
            LoadState = request.LoadState,
        };
        components.Add(snapshot);
        MarkMutated();
        return Task.FromResult(
            FakeCadResults.Success(
                snapshot,
                operation,
                new EvidenceObservation("component.id", componentId.Value),
                new EvidenceObservation("component.load-state", snapshot.LoadState.ToString()),
                new EvidenceObservation("state.hash", StateHash)));
    }

    /// <inheritdoc />
    public Task<OperationResult<MateSnapshot>> AddMateAsync(
        MateRequest request,
        CancellationToken cancellationToken = default)
    {
        const string operation = "add-mate";
        if (request is null)
        {
            return Task.FromResult(FakeCadResults.Invalid<MateSnapshot>(operation, "The mate request is required."));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(FakeCadResults.Cancelled<MateSnapshot>(operation));
        }

        if (!Session.Supports(CadCapabilityNames.AssemblyMutation, out CadCapability capability))
        {
            return Task.FromResult(FakeCadResults.Unsupported<MateSnapshot>(operation, capability));
        }

        if (Session.IsClosed)
        {
            return Task.FromResult(FakeCadResults.Closed<MateSnapshot>(operation));
        }

        if (Session.Failures.TryTake(FakeCadFailurePoints.AddMate, out OperationError? injectedError))
        {
            return Task.FromResult(FakeCadResults.Failure<MateSnapshot>(operation, injectedError!));
        }

        if (request.FirstComponentId == request.SecondComponentId)
        {
            return Task.FromResult(FakeCadResults.Invalid<MateSnapshot>(operation, "A mate requires two distinct component instances."));
        }

        bool firstExists = components.Any(component => component.ComponentId == request.FirstComponentId);
        bool secondExists = components.Any(component => component.ComponentId == request.SecondComponentId);
        if (!firstExists)
        {
            return Task.FromResult(FakeCadResults.NotFound<MateSnapshot>(operation, request.FirstComponentId.Value));
        }

        if (!secondExists)
        {
            return Task.FromResult(FakeCadResults.NotFound<MateSnapshot>(operation, request.SecondComponentId.Value));
        }

        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.MateType))
        {
            return Task.FromResult(FakeCadResults.Invalid<MateSnapshot>(operation, "Mate name and type are required."));
        }

        FeatureId mateId = request.RequestedMateId ?? new FeatureId($"mate-{++mateSequence:000}");
        if (mates.Any(mate => mate.MateId == mateId))
        {
            return Task.FromResult(FakeCadResults.Invalid<MateSnapshot>(operation, $"Mate identity '{mateId.Value}' already exists."));
        }

        var snapshot = new MateSnapshot
        {
            MateId = mateId,
            Name = request.Name.Trim(),
            FirstComponentId = request.FirstComponentId,
            SecondComponentId = request.SecondComponentId,
            MateType = request.MateType.Trim(),
            Distance = request.Distance,
        };
        mates.Add(snapshot);
        MarkMutated();
        return Task.FromResult(
            FakeCadResults.Success(
                snapshot,
                operation,
                new EvidenceObservation("mate.id", mateId.Value),
                new EvidenceObservation("mate.type", snapshot.MateType),
                new EvidenceObservation("state.hash", StateHash)));
    }

    /// <inheritdoc />
    internal override CadInspectionSnapshot BuildInspection() => new()
    {
        Document = CreateSummary(),
        Components = [.. components.OrderBy(component => component.ComponentId.Value, StringComparer.Ordinal)],
        Mates = [.. mates.OrderBy(mate => mate.MateId.Value, StringComparer.Ordinal)],
    };

    /// <inheritdoc />
    protected override string DescribeState() => string.Join(
        ";",
        components
            .OrderBy(component => component.ComponentId.Value, StringComparer.Ordinal)
            .Select(component => $"component:{component.ComponentId.Value}:{component.ReferencedDocumentId.Value}:{component.LoadState}"),
        mates
            .OrderBy(mate => mate.MateId.Value, StringComparer.Ordinal)
            .Select(mate => $"mate:{mate.MateId.Value}:{mate.FirstComponentId.Value}:{mate.SecondComponentId.Value}:{mate.MateType}"));
}
