using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.UnitTests;

/// <summary>Locks the explicit schema compatibility policy against accidental breaking changes.</summary>
/// <remarks>
/// Version policy is intentionally tested independently from the Server so every consumer shares the same decision.
/// 版本策略独立于 Server 测试，确保所有消费者共享同一兼容性判断。
/// </remarks>
public sealed class ProtocolCompatibilityTests
{
    /// <summary>Current and older same-major versions are compatible.</summary>
    [Fact]
    public void CurrentOperationSchemaIsCompatible()
    {
        CompatibilityReport report = ProtocolCompatibility.Check(ProtocolSchema.OperationResult, ProtocolSchema.CurrentVersion);

        Assert.True(report.IsCompatible);
        Assert.Contains("backward-compatible", report.Reason, StringComparison.Ordinal);
    }

    /// <summary>Different schemas, major versions, future minors and malformed versions are rejected.</summary>
    [Theory]
    [InlineData("other-schema", "1.0")]
    [InlineData("solidworks-mcp.operation-result", "2.0")]
    [InlineData("solidworks-mcp.operation-result", "1.1")]
    [InlineData("solidworks-mcp.operation-result", "not-a-version")]
    public void BreakingOrUnknownVersionsFailClosed(string schema, string version)
    {
        CompatibilityReport report = ProtocolCompatibility.Check(schema, version);

        Assert.False(report.IsCompatible);
        Assert.False(string.IsNullOrWhiteSpace(report.Reason));
    }
}
