using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Server;

/// <summary>Converts our typed result envelope to an MCP CallToolResult without losing structured evidence.</summary>
/// <remarks>
/// Human-readable text is intentionally short while StructuredContent carries the complete versioned envelope for
/// machines, audit records and future support bundles. 文本仅用于快速阅读；StructuredContent 保留完整的版本化信封，
/// 供机器、审计记录和后续支持包使用。
/// </remarks>
internal static class McpToolResultWriter
{
    private static readonly JsonSerializerOptions serializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Writes a success or error result as both readable text and structured JSON.</summary>
    /// <remarks>
    /// The conversion applies string enum serialization so stable error/category values remain readable across client
    /// languages. 转换统一使用字符串枚举序列化，使错误码和错误类别在不同客户端语言中都保持可读和稳定。
    /// </remarks>
    public static CallToolResult Write<T>(OperationResult<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        string text = result.IsSuccess
            ? "CAD operation completed with verification evidence."
            : $"{result.Error!.Code}: {result.Error.Message}";
        return new CallToolResult
        {
            IsError = !result.IsSuccess,
            Content = [new TextContentBlock { Text = text }],
            StructuredContent = JsonSerializer.SerializeToElement(result, serializerOptions),
        };
    }
}
