using System.Collections.Immutable;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Provider.Fake;

/// <summary>Deterministic in-memory part document with body, feature and inspection state.</summary>
internal sealed class FakeCadPartDocument(FakeCadSession session, DocumentId documentId, string path, string configuration) : FakeCadDocument(session, documentId, path, configuration, CadDocumentType.Part), ICadPartDocument
{
    private readonly Dictionary<BodyId, FakeBodyState> bodies = [];
    private readonly List<FeatureSnapshot> features = [];
    private int bodySequence;
    private int featureSequence;

    /// <inheritdoc />
    public Task<OperationResult<BodySnapshot>> CreateBodyAsync(
        CreateBodyRequest request,
        CancellationToken cancellationToken = default)
    {
        const string operation = "create-body";
        if (request is null)
        {
            return Task.FromResult(FakeCadResults.Invalid<BodySnapshot>(operation, "The body request is required."));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(FakeCadResults.Cancelled<BodySnapshot>(operation));
        }

        if (!Session.Supports(CadCapabilityNames.PartMutation, out CadCapability capability))
        {
            return Task.FromResult(FakeCadResults.Unsupported<BodySnapshot>(operation, capability));
        }

        if (Session.IsClosed)
        {
            return Task.FromResult(FakeCadResults.Closed<BodySnapshot>(operation));
        }

        if (Session.Failures.TryTake(FakeCadFailurePoints.CreateBody, out OperationError? injectedError))
        {
            return Task.FromResult(FakeCadResults.Failure<BodySnapshot>(operation, injectedError!));
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Task.FromResult(FakeCadResults.Invalid<BodySnapshot>(operation, "Body name is required."));
        }

        BodyId bodyId = request.RequestedBodyId ?? new BodyId($"body-{++bodySequence:000}");
        if (bodies.ContainsKey(bodyId))
        {
            return Task.FromResult(FakeCadResults.Invalid<BodySnapshot>(operation, $"Body identity '{bodyId.Value}' already exists."));
        }

        var body = new FakeBodyState(bodyId, request.Name.Trim());
        bodies.Add(bodyId, body);
        MarkMutated();
        BodySnapshot snapshot = body.ToSnapshot();
        return Task.FromResult(
            FakeCadResults.Success(
                snapshot,
                operation,
                new EvidenceObservation("body.id", bodyId.Value),
                new EvidenceObservation("body.count", bodies.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new EvidenceObservation("state.hash", StateHash)));
    }

    /// <inheritdoc />
    public Task<OperationResult<FeatureSnapshot>> AddExtrusionAsync(
        ExtrusionRequest request,
        CancellationToken cancellationToken = default)
    {
        const string operation = "add-extrusion";
        if (request is null)
        {
            return Task.FromResult(FakeCadResults.Invalid<FeatureSnapshot>(operation, "The extrusion request is required."));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(FakeCadResults.Cancelled<FeatureSnapshot>(operation));
        }

        if (!Session.Supports(CadCapabilityNames.PartMutation, out CadCapability capability))
        {
            return Task.FromResult(FakeCadResults.Unsupported<FeatureSnapshot>(operation, capability));
        }

        if (Session.IsClosed)
        {
            return Task.FromResult(FakeCadResults.Closed<FeatureSnapshot>(operation));
        }

        if (Session.Failures.TryTake(FakeCadFailurePoints.AddExtrusion, out OperationError? injectedError))
        {
            return Task.FromResult(FakeCadResults.Failure<FeatureSnapshot>(operation, injectedError!));
        }

        if (string.IsNullOrWhiteSpace(request.Name) || request.Depth.Millimeters <= 0d)
        {
            return Task.FromResult(FakeCadResults.Invalid<FeatureSnapshot>(operation, "Extrusion name and positive depth are required."));
        }

        BodyId bodyId = request.TargetBodyId ?? bodies.Keys.OrderBy(id => id.Value, StringComparer.Ordinal).FirstOrDefault();
        if (string.IsNullOrEmpty(bodyId.Value))
        {
            return Task.FromResult(FakeCadResults.NotFound<FeatureSnapshot>(operation, "active-body"));
        }

        if (!bodies.TryGetValue(bodyId, out FakeBodyState? body))
        {
            return Task.FromResult(FakeCadResults.NotFound<FeatureSnapshot>(operation, bodyId.Value));
        }

        FeatureId featureId = request.RequestedFeatureId ?? new FeatureId($"feature-extrusion-{++featureSequence:000}");
        if (features.Any(feature => feature.FeatureId == featureId))
        {
            return Task.FromResult(FakeCadResults.Invalid<FeatureSnapshot>(operation, $"Feature identity '{featureId.Value}' already exists."));
        }

        var feature = new FeatureSnapshot
        {
            FeatureId = featureId,
            Name = request.Name.Trim(),
            Kind = "extrusion",
            BodyId = bodyId,
            Depth = request.Depth,
        };
        features.Add(feature);
        body.AddExtrusion(request.Depth);
        MarkMutated();
        return Task.FromResult(
            FakeCadResults.Success(
                feature,
                operation,
                new EvidenceObservation("feature.id", featureId.Value),
                new EvidenceObservation("feature.kind", feature.Kind),
                new EvidenceObservation("state.hash", StateHash)));
    }

    /// <inheritdoc />
    internal override CadInspectionSnapshot BuildInspection() => new()
    {
        Document = CreateSummary(),
        Bodies = [.. bodies.Values
            .OrderBy(body => body.BodyId.Value, StringComparer.Ordinal)
            .Select(body => body.ToSnapshot())],
        Features = [.. features.OrderBy(feature => feature.FeatureId.Value, StringComparer.Ordinal)],
    };

    /// <inheritdoc />
    protected override string DescribeState() => string.Join(
        ";",
        bodies.Values
            .OrderBy(body => body.BodyId.Value, StringComparer.Ordinal)
            .Select(body => body.Describe()),
        features
            .OrderBy(feature => feature.FeatureId.Value, StringComparer.Ordinal)
            .Select(feature => $"feature:{feature.FeatureId.Value}:{feature.Kind}:{feature.Depth?.Millimeters:G17}"));

    private sealed class FakeBodyState(BodyId bodyId, string name)
    {
        private int featureCount;
        private Length height;

        public BodyId BodyId { get; } = bodyId;

        public string Name { get; } = name;

        public void AddExtrusion(Length depth)
        {
            featureCount++;
            height = Length.FromMillimeters(height.Millimeters + depth.Millimeters);
        }

        public BodySnapshot ToSnapshot()
        {
            var zero = Length.FromMillimeters(0d);
            var width = Length.FromMillimeters(100d);
            Length heightValue = height.Millimeters <= 0d ? Length.FromMillimeters(1d) : height;
            var volume = Volume.FromCubicMillimeters(width.Millimeters * width.Millimeters * heightValue.Millimeters);
            var mass = Mass.FromKilograms(volume.CubicMillimeters / 1_000_000_000d);
            return new BodySnapshot
            {
                BodyId = BodyId,
                Name = Name,
                FeatureCount = featureCount,
                BoundingBoxMinimum = new Coordinate3D(zero, zero, zero),
                BoundingBoxMaximum = new Coordinate3D(width, width, heightValue),
                Volume = volume,
                Mass = mass,
            };
        }

        public string Describe() => $"body:{BodyId.Value}:{Name}:{featureCount}:{height.Millimeters:G17}";
    }
}
