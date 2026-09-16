using System.Text.Json;
using SolidWorksMcp.Core;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.UnitTests;

/// <summary>Locks the four-layer runtime configuration precedence and fail-closed feature behavior.</summary>
/// <remarks>
/// These tests inject environment and local paths so they never depend on the developer machine.
/// 测试注入环境变量和本地路径，绝不依赖开发机的真实配置。
/// </remarks>
public sealed class RuntimeConfigurationTests
{
    /// <summary>CLI must override environment, user-local JSON and defaults in that order.</summary>
    [Fact]
    public void ConfigurationLayersUseDefaultsThenLocalEnvironmentAndCliPrecedence()
    {
        string path = Path.Combine(Path.GetTempPath(), $"solidworks-mcp-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                path,
                """
                {
                  "schemaVersion": "1.0",
                  "providerMode": "native",
                  "features": {
                    "experimentalDrawing": true,
                    "experimentalRecognition": false
                  }
                }
                """);

            var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["SOLIDWORKS_MCP_PROVIDER_MODE"] = ProviderModes.Fake,
                ["SOLIDWORKS_MCP_FEATURE_EXPERIMENTAL_DRAWING"] = "false",
                ["SOLIDWORKS_MCP_FEATURE_EXPERIMENTAL_RECOGNITION"] = "true",
            };
            ConfigurationLoadResult result = SolidWorksMcpConfigurationLoader.Load(
                ["--provider-mode", ProviderModes.Unavailable, "--feature", "experimental.drawing=true"],
                environment,
                path);

            Assert.Equal(ProviderModes.Unavailable, result.Configuration.ProviderMode);
            Assert.True(result.Configuration.Features.ExperimentalDrawing);
            Assert.True(result.Configuration.Features.ExperimentalRecognition);
            Assert.True(result.UserLocalFileLoaded);
            Assert.Equal("cli", result.Sources["providerMode"]);
            Assert.Equal("cli", result.Sources[FeatureFlagNames.ExperimentalDrawing]);
            Assert.Equal("environment", result.Sources[FeatureFlagNames.ExperimentalRecognition]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Unrecognized environment data must not become a secret or a configuration setting.</summary>
    [Fact]
    public void UnknownEnvironmentVariablesAreIgnoredAndDefaultsRemainSafe()
    {
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["SOLIDWORKS_MCP_SECRET"] = "must-never-be-copied",
            ["UNRELATED_SETTING"] = "ignored",
        };

        ConfigurationLoadResult result = SolidWorksMcpConfigurationLoader.Load([], environment, Path.Combine(Path.GetTempPath(), "missing-solidworks-mcp.json"));

        Assert.Equal(ProviderModes.Unavailable, result.Configuration.ProviderMode);
        Assert.False(result.Configuration.Features.ExperimentalDrawing);
        Assert.False(result.Configuration.Features.ExperimentalRecognition);
        Assert.DoesNotContain("SOLIDWORKS_MCP_SECRET", result.Sources.Keys);
        Assert.DoesNotContain("must-never-be-copied", JsonSerializer.Serialize(result));
    }

    /// <summary>Unknown CLI flags fail closed instead of silently changing deployment behavior.</summary>
    [Fact]
    public void UnknownCommandLineOptionFailsClosed()
    {
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        string missingPath = Path.Combine(Path.GetTempPath(), $"missing-solidworks-mcp-{Guid.NewGuid():N}.json");

        Assert.Throws<ConfigurationException>(() => SolidWorksMcpConfigurationLoader.Load(["--arbitrary-command"], environment, missingPath));
        Assert.Throws<ConfigurationException>(() => SolidWorksMcpConfigurationLoader.Load(["--feature", "unknown=true"], environment, missingPath));
        Assert.Throws<ConfigurationException>(() => SolidWorksMcpConfigurationLoader.Load(["--config", missingPath], environment, missingPath));
    }
}
