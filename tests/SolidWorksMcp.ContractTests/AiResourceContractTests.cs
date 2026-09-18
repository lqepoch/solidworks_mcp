using ModelContextProtocol.Protocol;
using SolidWorksMcp.Server;

namespace SolidWorksMcp.ContractTests;

/// <summary>
/// Keeps the AI-facing MCP resources useful and non-secret. The resources are the stable teaching surface for future
/// Codex/LLM adapters; the executable compiler remains authoritative for actual mutation.
/// 保证面向 AI 的 MCP resource 有效且不包含秘密内容；resource 是未来 Codex/LLM adapter 的稳定教学面，真正 mutation
/// 仍由 executable compiler 负责。
/// </summary>
public sealed class AiResourceContractTests
{
    [Fact]
    public void ResourcesExposeIntentCompilerAndReleaseBoundaries()
    {
        TextResourceContents index = SolidWorksAiResources.UsageIndex();
        TextResourceContents drawing = SolidWorksAiResources.DrawingRecipe();
        TextResourceContents schema = SolidWorksAiResources.FeaturePlanSchema();
        TextResourceContents release = SolidWorksAiResources.ReleaseRecipe();

        Assert.Equal("recipe://solidworks/usage/index", index.Uri);
        Assert.Contains("cad.build-part-drawing", index.Text, StringComparison.Ordinal);
        Assert.Contains("IView.GetOutline", drawing.Text, StringComparison.Ordinal);
        Assert.Equal("application/json", schema.MimeType);
        Assert.Contains("schemaVersion", schema.Text, StringComparison.Ordinal);
        Assert.Contains("REVIEW_REQUIRED", release.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("图纸/", index.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("SLDWORKS.exe", drawing.Text, StringComparison.Ordinal);
    }
}
