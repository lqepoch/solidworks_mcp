using System.Text.Json;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.UnitTests;

/// <summary>Verifies that result envelopes carry evidence and stable machine-readable failures.</summary>
public sealed class OperationContractTests
{
    /// <summary>Success must be backed by an operation ID, schema version and inspection evidence.</summary>
    [Fact]
    public void SuccessContainsSchemaIdentityAndEvidence()
    {
        var evidence = new OperationEvidence(
            source: "fake-cad",
            observations: [new EvidenceObservation("body.count", "1", "1")],
            stateHash: "sha256:example");
        var result = OperationResults.Success("verified", "operation-001", evidence);

        Assert.True(result.IsSuccess);
        Assert.Equal(ProtocolSchema.OperationResult, result.Schema);
        Assert.Equal(ProtocolSchema.CurrentVersion, result.SchemaVersion);
        Assert.Equal("operation-001", result.OperationId);
        Assert.Equal("verified", result.Value);
        Assert.Null(result.Error);
        Assert.Single(result.Evidence!.Observations);
    }

    /// <summary>Failure must preserve stable error code/category and never masquerade as success.</summary>
    [Fact]
    public void FailureContainsStableErrorCodeAndRemediation()
    {
        var error = new OperationError(
            ErrorCodes.StateConflict,
            "The expected document state no longer matches.",
            ErrorCategories.State,
            retryable: false,
            remediation: "Re-inspect the document and create a new plan.");
        var result = OperationResults.Failure<string>("operation-002", error);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(ErrorCodes.StateConflict, result.Error!.Code);
        Assert.Equal(ErrorCategories.State, result.Error.Category);
        Assert.False(result.Error.Retryable);
        Assert.Contains("new plan", result.Error.Remediation, StringComparison.Ordinal);
    }

    /// <summary>Serialized envelopes must expose schema/version and stable error fields to MCP clients.</summary>
    [Fact]
    public void FailureJsonContainsSchemaVersionAndStableCode()
    {
        var result = OperationResults.Failure<string>(
            "operation-003",
            new OperationError(ErrorCodes.SelectionStale, "Selection is stale.", ErrorCategories.State));

        string json = JsonSerializer.Serialize(result);

        Assert.Contains("Schema", json, StringComparison.Ordinal);
        Assert.Contains(ProtocolSchema.CurrentVersion, json, StringComparison.Ordinal);
        Assert.Contains(ErrorCodes.SelectionStale, json, StringComparison.Ordinal);
        Assert.Contains(ErrorCategories.State, json, StringComparison.Ordinal);
    }

    /// <summary>Strong IDs with identical text compare equal, while different identity kinds remain distinct types.</summary>
    [Fact]
    public void StableIdentifiersPreserveValueAndEquality()
    {
        var first = new DocumentId("doc-001");
        var second = new DocumentId("doc-001");

        Assert.Equal(first, second);
        Assert.Equal("doc-001", first.Value);
        Assert.Throws<ArgumentException>(() => new ItemIdentity("  "));
    }
}
