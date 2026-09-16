namespace SolidWorksMcp.Server;

/// <summary>Generates process-local correlation IDs when a client does not provide one.</summary>
/// <remarks>
/// The MCP SDK owns the JSON-RPC request ID; this service adds a stable application operation ID to our result envelope.
/// MCP SDK 管理 JSON-RPC request ID；本服务为工程审计信封补充应用级 operation ID。
/// </remarks>
public sealed class McpOperationCorrelation
{
    private long sequence;

    /// <summary>Returns a trimmed caller key or a deterministic process-local key.</summary>
    public string Resolve(string? requestedOperationId)
    {
        if (!string.IsNullOrWhiteSpace(requestedOperationId))
        {
            return requestedOperationId.Trim();
        }

        long next = Interlocked.Increment(ref sequence);
        return $"mcp:{next:0000000000}";
    }
}
