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
