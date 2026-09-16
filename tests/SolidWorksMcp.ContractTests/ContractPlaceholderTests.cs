namespace SolidWorksMcp.ContractTests;

public sealed class ContractPlaceholderTests
{
    /// <summary>FakeCad must complete the representative part-to-drawing workflow without vendor DLLs.</summary>
    [Fact]
    public async Task FakeCadSatisfiesPartToDrawingProviderContract()
    {
        await using var provider = new SolidWorksMcp.Provider.Fake.FakeCadProvider();
        await CadProviderContractSuite.RunPartToDrawingWorkflowAsync(provider);
    }
}
