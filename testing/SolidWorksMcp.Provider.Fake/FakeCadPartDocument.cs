using System.Collections.Immutable;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Provider.Fake;

/// <summary>Deterministic in-memory part document with body, feature and inspection state.</summary>
internal sealed class FakeCadPartDocument(
    FakeCadSession session,
    DocumentId documentId,
    string path,
    string configuration,
    SketchProfileRequest? profile = null) : FakeCadDocument(session, documentId, path, configuration, CadDocumentType.Part), ICadPartDocument
{
    private readonly SketchProfileRequest? initialSketchProfile = profile;
    private readonly Dictionary<BodyId, FakeBodyState> bodies = [];
    private readonly List<FeatureSnapshot> features = [];
    private readonly Dictionary<string, FakeDimensionState> dimensions = CreateDimensionMap();
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
        string fullParameterName = $"D1@{feature.Name}";
        dimensions.Add(fullParameterName, new FakeDimensionState(feature.FeatureId, fullParameterName, request.Depth));
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
    public Task<OperationResult<FeatureSnapshot>> AddThroughHolePatternAsync(
        ThroughHolePatternRequest request,
        CancellationToken cancellationToken = default)
    {
        const string operation = "add-through-hole-pattern";
        if (request is null)
        {
            return Task.FromResult(FakeCadResults.Invalid<FeatureSnapshot>(operation, "The through-hole pattern request is required."));
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

        if (string.IsNullOrWhiteSpace(request.Name)
            || request.Diameter.Millimeters <= 0d
            || request.Centers.Length < 1)
        {
            return Task.FromResult(
                FakeCadResults.Invalid<FeatureSnapshot>(
                    operation,
                    "A semantic name, positive diameter and at least one hole center are required."));
        }

        BodyId bodyId = request.TargetBodyId ?? bodies.Keys.OrderBy(id => id.Value, StringComparer.Ordinal).FirstOrDefault();
        if (string.IsNullOrEmpty(bodyId.Value))
        {
            return Task.FromResult(FakeCadResults.NotFound<FeatureSnapshot>(operation, "active-body"));
        }

        if (!bodies.ContainsKey(bodyId))
        {
            return Task.FromResult(FakeCadResults.NotFound<FeatureSnapshot>(operation, bodyId.Value));
        }

        FeatureId featureId = request.RequestedFeatureId ?? new FeatureId($"feature-hole-pattern-{++featureSequence:000}");
        if (features.Any(feature => feature.FeatureId == featureId))
        {
            return Task.FromResult(FakeCadResults.Invalid<FeatureSnapshot>(operation, $"Feature identity '{featureId.Value}' already exists."));
        }

        var feature = new FeatureSnapshot
        {
            FeatureId = featureId,
            Name = request.Name.Trim(),
            Kind = "through-hole-pattern",
            BodyId = bodyId,
        };
        features.Add(feature);
        MarkMutated();
        return Task.FromResult(
            FakeCadResults.Success(
                feature,
                operation,
                new EvidenceObservation("feature.id", featureId.Value),
                new EvidenceObservation("feature.kind", feature.Kind),
                new EvidenceObservation("hole.count", request.Centers.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new EvidenceObservation("hole.diameter-millimeters", request.Diameter.Millimeters.ToString("G17", System.Globalization.CultureInfo.InvariantCulture)),
                new EvidenceObservation("state.hash", StateHash)));
    }

    /// <inheritdoc />
    public Task<OperationResult<DimensionSnapshot>> SetDimensionValueAsync(
        DimensionUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        const string operation = "set-dimension";
        if (request is null)
        {
            return Task.FromResult(FakeCadResults.Invalid<DimensionSnapshot>(operation, "The dimension update request is required."));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(FakeCadResults.Cancelled<DimensionSnapshot>(operation));
        }

        if (!Session.Supports(CadCapabilityNames.PartMutation, out CadCapability capability))
        {
            return Task.FromResult(FakeCadResults.Unsupported<DimensionSnapshot>(operation, capability));
        }

        if (Session.IsClosed)
        {
            return Task.FromResult(FakeCadResults.Closed<DimensionSnapshot>(operation));
        }

        if (Session.Failures.TryTake(FakeCadFailurePoints.SetDimension, out OperationError? injectedError))
        {
            return Task.FromResult(FakeCadResults.Failure<DimensionSnapshot>(operation, injectedError!));
        }

        if (string.IsNullOrWhiteSpace(request.ParameterName) || request.Value.Millimeters <= 0d)
        {
            return Task.FromResult(FakeCadResults.Invalid<DimensionSnapshot>(operation, "A full dimension name and positive value are required."));
        }

        if (request.Configuration is not null
            && !request.Configuration.Trim().Equals(Configuration, StringComparison.Ordinal))
        {
            return Task.FromResult(
                FakeCadResults.Failure<DimensionSnapshot>(
                    operation,
                    new OperationError(
                        ErrorCodes.StateConflict,
                        "The requested dimension configuration does not match the fake document configuration.",
                        ErrorCategories.State,
                        remediation: "Use the document's registered active configuration.")));
        }

        if (!dimensions.TryGetValue(request.ParameterName.Trim(), out FakeDimensionState? dimension))
        {
            return Task.FromResult(FakeCadResults.NotFound<DimensionSnapshot>(operation, request.ParameterName.Trim()));
        }

        Length previousValue = dimension.Value;
        dimension.Value = request.Value;
        if (features.FindIndex(feature => feature.FeatureId == dimension.FeatureId) is int featureIndex && featureIndex >= 0)
        {
            FeatureSnapshot feature = features[featureIndex];
            if (feature.Depth is Length oldDepth
                && bodies.TryGetValue(feature.BodyId, out FakeBodyState? body))
            {
                body.ReplaceExtrusion(oldDepth, request.Value);
            }

            features[featureIndex] = feature with { Depth = request.Value };
        }

        MarkMutated();
        DimensionSnapshot snapshot = dimension.ToSnapshot(DocumentId, Configuration);
        return Task.FromResult(
            FakeCadResults.Success(
                snapshot,
                operation,
                new EvidenceObservation("dimension.name", snapshot.FullName),
                new EvidenceObservation("dimension.previous-millimeters", previousValue.Millimeters.ToString("G17", System.Globalization.CultureInfo.InvariantCulture)),
                new EvidenceObservation("dimension.value-millimeters", snapshot.Value.Millimeters.ToString("G17", System.Globalization.CultureInfo.InvariantCulture)),
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

    /// <summary>Returns profile evidence for the inspection facade without exposing provider-specific geometry.</summary>
    /// <remarks>Inspection 也必须能证明 profile 没有被静默丢弃，但只返回稳定摘要而不是原始点坐标。</remarks>
    internal IReadOnlyCollection<EvidenceObservation> GetInitialSketchProfileEvidence() =>
        initialSketchProfile is null
            ? []
            : FakeCadSketchProfileEvidence.AcceptedObservations(initialSketchProfile);

    /// <inheritdoc />
    protected override string DescribeState() => string.Join(
        ";",
        bodies.Values
            .OrderBy(body => body.BodyId.Value, StringComparer.Ordinal)
            .Select(body => body.Describe()),
        features
            .OrderBy(feature => feature.FeatureId.Value, StringComparer.Ordinal)
            .Select(feature => $"feature:{feature.FeatureId.Value}:{feature.Kind}:{feature.Depth?.Millimeters:G17}"),
        FakeCadSketchProfileEvidence.StateToken(initialSketchProfile));

    // Keep the comparer explicit; replacing this with a collection expression would silently make lookups case-sensitive.
    // 保留显式 comparer；若改成 collection expression 会悄悄丢失不区分大小写的查找语义。
#pragma warning disable IDE0028
    private static Dictionary<string, FakeDimensionState> CreateDimensionMap() =>
        new(StringComparer.OrdinalIgnoreCase);
#pragma warning restore IDE0028

    private sealed class FakeDimensionState(FeatureId featureId, string fullName, Length value)
    {
        public FeatureId FeatureId { get; } = featureId;

        public string FullName { get; } = fullName;

        public Length Value { get; set; } = value;

        public DimensionSnapshot ToSnapshot(DocumentId documentId, string configuration) => new()
        {
            DimensionId = new DimensionId($"{documentId.Value}:dimension:{FullName}"),
            Name = FullName.Split('@', 2)[0],
            FullName = FullName,
            Configuration = configuration,
            Value = Value,
            IsReadOnly = false,
        };
    }

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

        public void ReplaceExtrusion(Length oldDepth, Length newDepth)
        {
            height = Length.FromMillimeters(height.Millimeters + newDepth.Millimeters - oldDepth.Millimeters);
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
