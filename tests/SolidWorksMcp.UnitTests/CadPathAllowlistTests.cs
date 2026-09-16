using System.Text.Json;
using SolidWorksMcp.Core;

namespace SolidWorksMcp.UnitTests;

/// <summary>Proves persisted CAD targets are constrained by an immutable, fail-closed path policy.</summary>
/// <remarks>
/// These tests use disposable temp directories only; they never touch the private drawing corpus.
/// 这些测试只使用可清理的临时目录，绝不接触秘密图纸 corpus。
/// </remarks>
public sealed class CadPathAllowlistTests
{
    /// <summary>An empty policy rejects a native-looking path instead of treating no policy as allow-all.</summary>
    [Fact]
    public void EmptyAllowlistDeniesEveryTarget()
    {
        string path = Path.Combine(Path.GetTempPath(), "solidworks-mcp-denied", "part.sldprt");

        CadPathValidationResult result = CadPathAllowlist.DenyAll.ValidateCreateTarget(path, ".sldprt");

        Assert.False(result.IsAllowed);
        Assert.Equal("outside-allowlist", result.FailureReason);
        Assert.Null(result.FullPath);
    }

    /// <summary>Containment uses a directory boundary and rejects relative targets and wrong extensions.</summary>
    [Fact]
    public void AllowlistUsesCanonicalBoundaryAndExtension()
    {
        string workspace = Directory.CreateTempSubdirectory("solidworks-mcp-path-").FullName;
        string allowed = Path.Combine(workspace, "allowed");
        string adjacent = Path.Combine(workspace, "allowed-old");
        Directory.CreateDirectory(allowed);
        Directory.CreateDirectory(adjacent);

        try
        {
            var policy = new CadPathAllowlist([allowed]);

            CadPathValidationResult accepted = policy.ValidateCreateTarget(Path.Combine(allowed, "part.sldprt"), ".sldprt");
            CadPathValidationResult adjacentResult = policy.ValidateCreateTarget(Path.Combine(adjacent, "part.sldprt"), ".sldprt");
            CadPathValidationResult relativeResult = policy.ValidateCreateTarget("allowed\\part.sldprt", ".sldprt");
            CadPathValidationResult wrongExtension = policy.ValidateCreateTarget(Path.Combine(allowed, "part.step"), ".sldprt");

            Assert.True(accepted.IsAllowed);
            Assert.Equal(Path.GetFullPath(Path.Combine(allowed, "part.sldprt")), accepted.FullPath);
            Assert.Equal("outside-allowlist", adjacentResult.FailureReason);
            Assert.Equal("relative-path", relativeResult.FailureReason);
            Assert.Equal("wrong-extension", wrongExtension.FailureReason);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    /// <summary>Local path roots load through the user-local layer but are hidden from serialized safe configuration.</summary>
    [Fact]
    public void UserLocalPathRootsArePrivateAndDoNotLeakThroughConfigurationSerialization()
    {
        string workspace = Directory.CreateTempSubdirectory("solidworks-mcp-config-").FullName;
        string configPath = Path.Combine(workspace, "config.json");
        try
        {
            File.WriteAllText(
                configPath,
                JsonSerializer.Serialize(
                    new
                    {
                        schemaVersion = "1.0",
                        pathAllowlist = new { roots = new[] { workspace } },
                    }));

            ConfigurationLoadResult result = SolidWorksMcpConfigurationLoader.Load(
                [],
                new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase),
                configPath);

            Assert.Single(result.PathAllowlist.AllowedRoots);
            Assert.Equal(Path.GetFullPath(workspace), result.PathAllowlist.AllowedRoots[0]);
            Assert.Equal("user-local", result.Sources["pathAllowlist"]);
            Assert.DoesNotContain(workspace, JsonSerializer.Serialize(result), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }
}
