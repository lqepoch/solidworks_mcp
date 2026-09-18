using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;
using SolidWorksMcp.Provider.Fake;

namespace SolidWorksMcp.FakeCadTests;

/// <summary>Exercises FakeCad state transitions, explicit unsupported behavior and injectable failures.</summary>
public sealed class FakeCadProviderTests
{
    /// <summary>One-shot injected failures must return a stable error and allow the next attempt to recover.</summary>
    [Fact]
    public async Task InjectedFailureIsOneShotAndRecoveryIsDeterministic()
    {
        var failures = new FakeCadFailureInjector();
        failures.Enqueue(
            FakeCadFailurePoints.AddExtrusion,
            new OperationError(
                ErrorCodes.ProviderFailure,
                "Synthetic extrusion failure.",
                ErrorCategories.Provider,
                retryable: true,
                remediation: "Retry once after provider recovery."));
        await using var provider = new FakeCadProvider(new FakeCadOptions { Failures = failures });
        await using ICadSession session = (await provider.StartSessionAsync(new CadSessionOptions())).RequireSuccess();
        ICadPartDocument part = (await session.CreatePartAsync(new CreatePartRequest())).RequireSuccess();
        BodySnapshot body = (await part.CreateBodyAsync(new CreateBodyRequest())).RequireSuccess();

        OperationResult<FeatureSnapshot> failed = await part.AddExtrusionAsync(new ExtrusionRequest
        {
            Name = "First attempt",
            Depth = Length.FromMillimeters(10d),
            TargetBodyId = body.BodyId,
        });
        Assert.False(failed.IsSuccess);
        Assert.Equal(ErrorCodes.ProviderFailure, failed.Error!.Code);

        FeatureSnapshot recovered = (await part.AddExtrusionAsync(new ExtrusionRequest
        {
            Name = "Second attempt",
            Depth = Length.FromMillimeters(10d),
            TargetBodyId = body.BodyId,
        })).RequireSuccess();
        Assert.Equal("feature-extrusion-001", recovered.FeatureId.Value);

        DimensionSnapshot changed = (await part.SetDimensionValueAsync(new DimensionUpdateRequest
        {
            ParameterName = $"D1@{recovered.Name}",
            Value = Length.FromMillimeters(15d),
        })).RequireSuccess();
        Assert.Equal("D1", changed.Name);
        Assert.Equal(15d, changed.Value.Millimeters, precision: 8);
    }

    /// <summary>Unsupported capability declarations must be observable and never silently mutate state.</summary>
    [Fact]
    public async Task UnsupportedCapabilityReturnsExplicitError()
    {
        var capabilities = new CadCapabilitySet(
        [
            new CadCapability(CadCapabilityNames.PartMutation, supported: false, "Part mutation disabled for this test."),
        ]);
        await using var provider = new FakeCadProvider(new FakeCadOptions { Capabilities = capabilities });
        await using ICadSession session = (await provider.StartSessionAsync(new CadSessionOptions())).RequireSuccess();

        OperationResult<ICadPartDocument> result = await session.CreatePartAsync(new CreatePartRequest());

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.UnsupportedCapability, result.Error!.Code);
        Assert.Equal(ErrorCategories.Capability, result.Error.Category);
    }

    /// <summary>Assembly snapshots preserve component load state, stable instances and mate identity.</summary>
    [Fact]
    public async Task AssemblyPreservesComponentStateAndMateIdentity()
    {
        await using var provider = new FakeCadProvider();
        await using ICadSession session = (await provider.StartSessionAsync(new CadSessionOptions())).RequireSuccess();
        ICadPartDocument part = (await session.CreatePartAsync(new CreatePartRequest())).RequireSuccess();
        _ = (await part.CreateBodyAsync(new CreateBodyRequest())).RequireSuccess();
        ICadAssemblyDocument assembly = (await session.CreateAssemblyAsync(new CreateAssemblyRequest())).RequireSuccess();

        ComponentSnapshot first = (await assembly.InsertComponentAsync(new InsertComponentRequest
        {
            RequestedComponentId = new ComponentId("component-left"),
            ReferencedDocumentId = part.DocumentId,
            Configuration = "Default",
            LoadState = CadLoadState.Lightweight,
        })).RequireSuccess();
        ComponentSnapshot second = (await assembly.InsertComponentAsync(new InsertComponentRequest
        {
            RequestedComponentId = new ComponentId("component-right"),
            ReferencedDocumentId = part.DocumentId,
            Configuration = "Default",
            LoadState = CadLoadState.Suppressed,
        })).RequireSuccess();

        MateSnapshot mate = (await assembly.AddMateAsync(new MateRequest
        {
            RequestedMateId = new FeatureId("mate-fixed"),
            FirstComponentId = first.ComponentId,
            SecondComponentId = second.ComponentId,
            MateType = "coincident",
        })).RequireSuccess();

        CadInspectionSnapshot inspection = (await session.Inspection.InspectAsync(assembly.DocumentId)).RequireSuccess();
        Assert.Equal(2, inspection.Components.Length);
        Assert.Single(inspection.Mates);
        Assert.Equal(CadLoadState.Lightweight, inspection.Components.Single(component => component.ComponentId == first.ComponentId).LoadState);
        Assert.Equal(mate.MateId, inspection.Mates[0].MateId);
    }

    /// <summary>Selectors resolve through semantic identity and never require a client-managed selection mark.</summary>
    [Fact]
    public async Task DeclarativeSelectionResolvesSemanticAndGeometryHints()
    {
        await using var provider = new FakeCadProvider();
        await using ICadSession session = (await provider.StartSessionAsync(new CadSessionOptions())).RequireSuccess();
        ICadPartDocument part = (await session.CreatePartAsync(new CreatePartRequest())).RequireSuccess();
        BodySnapshot body = (await part.CreateBodyAsync(new CreateBodyRequest { Name = "Plate" })).RequireSuccess();
        FeatureSnapshot feature = (await part.AddExtrusionAsync(new ExtrusionRequest
        {
            Name = "Plate thickness",
            Depth = Length.FromMillimeters(12d),
            TargetBodyId = body.BodyId,
        })).RequireSuccess();
        string stateHash = part.StateHash;

        CadSelectionSnapshot semantic = (await session.Selection.ResolveAsync(new CadEntitySelector
        {
            DocumentId = part.DocumentId,
            EntityKind = CadEntityKind.Feature,
            SemanticName = feature.Name,
            ExpectedStateHash = stateHash,
        })).RequireSuccess();
        Assert.Equal(feature.FeatureId.Value, semantic.Entity.Identity);
        Assert.Equal(CadSelectionResolution.SemanticSelector, semantic.Resolution);

        CadSelectionSnapshot geometry = (await session.Selection.ResolveAsync(new CadEntitySelector
        {
            DocumentId = part.DocumentId,
            EntityKind = CadEntityKind.Feature,
            GeometrySignature = new CadGeometrySignature { Value = $"fake|{part.DocumentId.Value}|Feature|{feature.FeatureId.Value}" },
            ExpectedStateHash = stateHash,
        })).RequireSuccess();
        Assert.Equal(feature.FeatureId.Value, geometry.Entity.Identity);
        Assert.Equal(CadSelectionResolution.GeometrySignature, geometry.Resolution);
    }

    /// <summary>A selector captured before a mutation must fail explicitly instead of following a changed topology.</summary>
    [Fact]
    public async Task DeclarativeSelectionRejectsStaleExpectedState()
    {
        await using var provider = new FakeCadProvider();
        await using ICadSession session = (await provider.StartSessionAsync(new CadSessionOptions())).RequireSuccess();
        ICadPartDocument part = (await session.CreatePartAsync(new CreatePartRequest())).RequireSuccess();
        BodySnapshot body = (await part.CreateBodyAsync(new CreateBodyRequest { Name = "Plate" })).RequireSuccess();
        string oldStateHash = part.StateHash;
        _ = (await part.AddExtrusionAsync(new ExtrusionRequest
        {
            Name = "Changed after selector capture",
            Depth = Length.FromMillimeters(12d),
            TargetBodyId = body.BodyId,
        })).RequireSuccess();

        OperationResult<CadSelectionSnapshot> result = await session.Selection.ResolveAsync(new CadEntitySelector
        {
            DocumentId = part.DocumentId,
            EntityKind = CadEntityKind.Body,
            SemanticName = body.Name,
            ExpectedStateHash = oldStateHash,
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.SelectionStale, result.Error!.Code);
    }

    /// <summary>
    /// A layout repair must resolve one stable annotation identity and reject stale state/position preconditions.
    /// 布局修复必须解析一个稳定 annotation identity，并拒绝过期的 state/position precondition。
    /// </summary>
    [Fact]
    public async Task DrawingAnnotationPositionRepairIsTargetedAndStateBound()
    {
        await using var provider = new FakeCadProvider();
        await using ICadSession session = (await provider.StartSessionAsync(new CadSessionOptions())).RequireSuccess();
        ICadDrawingDocument drawing = (await session.CreateDrawingAsync(new CreateDrawingRequest
        {
            RequestedDocumentId = new DocumentId("repair-drawing-001"),
        })).RequireSuccess();
        DrawingViewSnapshot view = (await drawing.AddViewAsync(new DrawingViewRequest
        {
            RequestedViewId = new ViewId("repair-view-001"),
            Name = "Front",
            Orientation = "Front",
            Position = new Coordinate2D(Length.FromMillimeters(100d), Length.FromMillimeters(80d)),
        })).RequireSuccess();
        _ = (await drawing.AddAnnotationAsync(new DrawingAnnotationRequest
        {
            RequestedAnnotationId = new AnnotationId("repair-annotation-001"),
            ViewId = view.ViewId,
            Kind = "note",
            Text = "repair target",
            Position = new Coordinate2D(Length.FromMillimeters(100d), Length.FromMillimeters(60d)),
        })).RequireSuccess();

        string plannedStateHash = drawing.StateHash;
        DrawingRepairReceipt receipt = (await drawing.RepositionAnnotationAsync(new DrawingAnnotationPositionRepairRequest
        {
            AnnotationId = new AnnotationId("repair-annotation-001"),
            ExpectedDocumentStateHash = plannedStateHash,
            PreconditionFingerprint = "layout-precondition-001",
            ExpectedCurrentPosition = new Coordinate2D(Length.FromMillimeters(100d), Length.FromMillimeters(60d)),
            NewPosition = new Coordinate2D(Length.FromMillimeters(120d), Length.FromMillimeters(70d)),
        })).RequireSuccess();

        Assert.Equal("layout.apply-planned-position", receipt.ActionCode);
        CadInspectionSnapshot inspected = (await session.Inspection.InspectAsync(drawing.DocumentId)).RequireSuccess();
        DrawingAnnotationSnapshot annotation = Assert.Single(inspected.Annotations);
        Assert.Equal(120d, annotation.Position.X.Millimeters, precision: 8);
        Assert.Equal(70d, annotation.Position.Y.Millimeters, precision: 8);

        OperationResult<DrawingRepairReceipt> stale = await drawing.RepositionAnnotationAsync(new DrawingAnnotationPositionRepairRequest
        {
            AnnotationId = annotation.AnnotationId,
            ExpectedDocumentStateHash = plannedStateHash,
            PreconditionFingerprint = "layout-precondition-001",
            ExpectedCurrentPosition = new Coordinate2D(Length.FromMillimeters(100d), Length.FromMillimeters(60d)),
            NewPosition = new Coordinate2D(Length.FromMillimeters(130d), Length.FromMillimeters(75d)),
        });
        Assert.False(stale.IsSuccess);
        Assert.Equal(ErrorCodes.StateConflict, stale.Error!.Code);
    }

    /// <summary>
    /// A view reflow uses the same stable state/position preconditions as annotation repair and returns outline proof.
    /// view reflow 与 annotation repair 使用相同的 stable state/position precondition，并返回 outline 证明。
    /// </summary>
    [Fact]
    public async Task DrawingViewPositionRepairIsTargetedAndStateBound()
    {
        await using var provider = new FakeCadProvider();
        await using ICadSession session = (await provider.StartSessionAsync(new CadSessionOptions())).RequireSuccess();
        ICadDrawingDocument drawing = (await session.CreateDrawingAsync(new CreateDrawingRequest
        {
            RequestedDocumentId = new DocumentId("view-repair-drawing-001"),
        })).RequireSuccess();
        DrawingViewSnapshot view = (await drawing.AddViewAsync(new DrawingViewRequest
        {
            RequestedViewId = new ViewId("view-repair-001"),
            Name = "Front",
            Orientation = "Front",
            Position = new Coordinate2D(Length.FromMillimeters(100d), Length.FromMillimeters(80d)),
        })).RequireSuccess();

        string plannedStateHash = drawing.StateHash;
        Coordinate2D newPosition = new(Length.FromMillimeters(120d), Length.FromMillimeters(90d));
        DrawingViewRepairReceipt receipt = (await drawing.RepositionViewAsync(new DrawingViewPositionRepairRequest
        {
            ViewId = view.ViewId,
            ExpectedDocumentStateHash = plannedStateHash,
            PreconditionFingerprint = "view-reflow-precondition-001",
            ExpectedCurrentPosition = view.Position,
            NewPosition = newPosition,
        })).RequireSuccess();

        Assert.Equal("layout.apply-planned-view-position", receipt.ActionCode);
        Assert.Equal(newPosition, receipt.Position);
        Assert.True(receipt.Outline.Width.Millimeters > 0d);

        OperationResult<DrawingViewRepairReceipt> stale = await drawing.RepositionViewAsync(new DrawingViewPositionRepairRequest
        {
            ViewId = view.ViewId,
            ExpectedDocumentStateHash = plannedStateHash,
            PreconditionFingerprint = "view-reflow-precondition-001",
            ExpectedCurrentPosition = view.Position,
            NewPosition = new Coordinate2D(Length.FromMillimeters(130d), Length.FromMillimeters(95d)),
        });
        Assert.False(stale.IsSuccess);
        Assert.Equal(ErrorCodes.StateConflict, stale.Error!.Code);
    }

    /// <summary>FakeCad exposes the same save/close/reopen contract while keeping private file I/O out of hosted tests.</summary>
    /// <remarks>FakeCad 在不接触私有文件 I/O 的 Hosted-safe 测试中，仍暴露与 native provider 相同的生命周期契约。</remarks>
    [Fact]
    public async Task PersistedLifecycleContractReturnsFreshInspectionEvidence()
    {
        await using var provider = new FakeCadProvider();
        await using ICadSession session = (await provider.StartSessionAsync(new CadSessionOptions())).RequireSuccess();
        ICadPartDocument part = (await session.CreatePartAsync(new CreatePartRequest())).RequireSuccess();
        BodySnapshot body = (await part.CreateBodyAsync(new CreateBodyRequest())).RequireSuccess();
        FeatureSnapshot feature = (await part.AddExtrusionAsync(new ExtrusionRequest
        {
            Name = "Persisted feature",
            Depth = Length.FromMillimeters(10d),
            TargetBodyId = body.BodyId,
        })).RequireSuccess();
        DimensionSnapshot changed = (await part.SetDimensionValueAsync(new DimensionUpdateRequest
        {
            ParameterName = $"D1@{feature.Name}",
            Value = Length.FromMillimeters(15d),
        })).RequireSuccess();
        Assert.Equal(15d, changed.Value.Millimeters, precision: 8);
        _ = (await part.RebuildAsync()).RequireSuccess();
        _ = (await part.SaveAsync()).RequireSuccess();

        CadInspectionSnapshot reopened = (await part.ReopenAndInspectAsync()).RequireSuccess();
        Assert.Contains(reopened.Features, candidate => candidate.FeatureId == feature.FeatureId);
        Assert.True(reopened.Bodies[0].Volume.CubicMillimeters > 0d);
        OperationResult<MutationReceipt> close = await part.CloseAsync();
        Assert.True(close.IsSuccess, $"Expected success but received {close.Error?.Code}: {close.Error?.Message}");
        Assert.Contains(
            close.Evidence!.Observations,
            observation => observation.Key == "document.closed" && observation.Value == bool.TrueString);
    }

    /// <summary>
    /// A repeated through-hole group remains one semantic feature in the provider contract.
    /// 重复通孔组在 Provider 契约中必须保持为一个工程语义特征，而不是退化成匿名几何集合。
    /// </summary>
    [Fact]
    public async Task ThroughHolePatternPreservesSemanticEvidence()
    {
        await using var provider = new FakeCadProvider();
        await using ICadSession session = (await provider.StartSessionAsync(new CadSessionOptions())).RequireSuccess();
        ICadPartDocument part = (await session.CreatePartAsync(new CreatePartRequest())).RequireSuccess();
        BodySnapshot body = (await part.CreateBodyAsync(new CreateBodyRequest { Name = "Reference plate" })).RequireSuccess();
        _ = (await part.AddExtrusionAsync(new ExtrusionRequest
        {
            Name = "Plate thickness",
            Depth = Length.FromMillimeters(8d),
            TargetBodyId = body.BodyId,
        })).RequireSuccess();

        OperationResult<FeatureSnapshot> patternResult = await part.AddThroughHolePatternAsync(new ThroughHolePatternRequest
        {
            Name = "Mounting hole group",
            Diameter = Length.FromMillimeters(6d),
            Centers =
            [
                new Coordinate2D(Length.FromMillimeters(-25d), Length.FromMillimeters(-15d)),
                new Coordinate2D(Length.FromMillimeters(25d), Length.FromMillimeters(15d)),
            ],
            TargetBodyId = body.BodyId,
        });
        FeatureSnapshot pattern = patternResult.RequireSuccess();

        Assert.Equal("through-hole-pattern", pattern.Kind);
        Assert.Equal("Mounting hole group", pattern.Name);
        Assert.NotNull(patternResult.Evidence);
        Assert.Equal("2", patternResult.Evidence!.Observations.Single(observation => observation.Key == "hole.count").Value);
        Assert.Equal("6", patternResult.Evidence.Observations.Single(observation => observation.Key == "hole.diameter-millimeters").Value);

        CadInspectionSnapshot inspection = (await session.Inspection.InspectAsync(part.DocumentId)).RequireSuccess();
        Assert.Contains(inspection.Features, feature => feature.FeatureId == pattern.FeatureId && feature.Kind == "through-hole-pattern");
    }

    /// <summary>
    /// An obround slot remains one semantic cut feature and reports its native-independent dimensions.
    /// 长圆槽在 fake contract 中也必须保持为一个语义切除 feature，并报告与 native 无关的尺寸证据。
    /// </summary>
    [Fact]
    public async Task SlotCutPreservesSemanticEvidence()
    {
        await using var provider = new FakeCadProvider();
        await using ICadSession session = (await provider.StartSessionAsync(new CadSessionOptions())).RequireSuccess();
        ICadPartDocument part = (await session.CreatePartAsync(new CreatePartRequest())).RequireSuccess();
        BodySnapshot body = (await part.CreateBodyAsync(new CreateBodyRequest { Name = "Bracket" })).RequireSuccess();
        _ = (await part.AddExtrusionAsync(new ExtrusionRequest
        {
            Name = "Bracket thickness",
            Depth = Length.FromMillimeters(12d),
            TargetBodyId = body.BodyId,
        })).RequireSuccess();

        OperationResult<FeatureSnapshot> slotResult = await part.AddSlotCutAsync(new SlotCutRequest
        {
            Name = "Access slot",
            Width = Length.FromMillimeters(4d),
            Start = new Coordinate2D(Length.FromMillimeters(-25d), Length.FromMillimeters(-20d)),
            End = new Coordinate2D(Length.FromMillimeters(-25d), Length.FromMillimeters(-8d)),
            SupportFaceProbe = new Coordinate2D(Length.FromMillimeters(-26d), Length.FromMillimeters(-14d)),
            TargetBodyId = body.BodyId,
        });
        FeatureSnapshot slot = slotResult.RequireSuccess();

        Assert.Equal("slot-cut", slot.Kind);
        Assert.Equal("Access slot", slot.Name);
        Assert.Equal("4", slotResult.Evidence!.Observations.Single(observation => observation.Key == "slot.width-millimeters").Value);
        Assert.Equal("12", slotResult.Evidence.Observations.Single(observation => observation.Key == "slot.centerline-length-millimeters").Value);

        CadInspectionSnapshot inspection = (await session.Inspection.InspectAsync(part.DocumentId)).RequireSuccess();
        Assert.Contains(inspection.Features, feature => feature.FeatureId == slot.FeatureId && feature.Kind == "slot-cut");
    }

}

/// <summary>Local assertion extension shared by FakeCad tests.</summary>
internal static class FakeCadTestResultExtensions
{
    /// <summary>Unwraps a result while preserving the provider error in the assertion message.</summary>
    public static T RequireSuccess<T>(this OperationResult<T> result)
    {
        Assert.True(result.IsSuccess, $"Expected success but received {result.Error?.Code}: {result.Error?.Message}");
        Assert.NotNull(result.Value);
        return result.Value!;
    }

}
