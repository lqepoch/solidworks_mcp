using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Server;

/// <summary>Compact high-level MCP tools for discovery and the first controlled CAD workflow.</summary>
/// <remarks>
/// Every mutation tool validates its versioned input before asking the session accessor for a provider session.
/// 每个 mutation tool 都会先验证版本化输入，再触碰 provider，确保 malformed input 不会执行业务。
/// </remarks>
[McpServerToolType]
public sealed class CadMcpTools(
    ICadProvider provider,
    McpToolCatalog catalog,
    McpOperationCorrelation correlation,
    CadSessionAccessor sessions)
{
    private readonly ICadProvider provider = provider ?? throw new ArgumentNullException(nameof(provider));
    private readonly McpToolCatalog catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    private readonly McpOperationCorrelation correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
    private readonly CadSessionAccessor sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));

    /// <summary>Returns read-only server and provider health.</summary>
    [McpServerTool(Name = "cad.health")]
    [Description("Read-only health for SolidWorksMcp. Preconditions: none. Side effects: none.")]
    public CallToolResult Health()
    {
        var health = new ServerHealth
        {
            ServerName = "solidworks-mcp",
            Schema = ProtocolSchema.OperationResult,
            SchemaVersion = ProtocolSchema.CurrentVersion,
            ProviderType = provider.GetType().Name,
            ProviderConfigured = provider is not UnavailableCadProvider,
        };
        var result = OperationResults.Success(
            health,
            correlation.Resolve(null),
            new OperationEvidence("server", [new EvidenceObservation("side-effects", "none")]));
        return McpToolResultWriter.Write(result);
    }

    /// <summary>Returns provider capabilities and the compact tool metadata catalog.</summary>
    [McpServerTool(Name = "cad.capabilities")]
    [Description("Discover CAD capabilities and tool metadata. Preconditions: none. Side effects: none.")]
    public CallToolResult Capabilities()
    {
        var discovery = new CapabilityDiscovery
        {
            ProviderType = provider.GetType().Name,
            Capabilities = provider.Capabilities,
            Tools = catalog.Tools,
        };
        var result = OperationResults.Success(
            discovery,
            correlation.Resolve(null),
            new OperationEvidence("server", [new EvidenceObservation("tool-count", catalog.Tools.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))]));
        return McpToolResultWriter.Write(result);
    }

    /// <summary>Creates one part after schema/version and identity validation.</summary>
    [McpServerTool(Name = "cad.create-part")]
    [Description("Create one CAD part. Preconditions: schemaVersion=1.0 and valid document/configuration. Side effects: creates one part document; no drawing generation.")]
    public async Task<CallToolResult> CreatePartAsync(
        [Description("Protocol schema version; currently 1.0.")] string schemaVersion,
        [Description("Stable document identity; it is not a display title.")] string documentId,
        [Description("Active configuration name.")] string configuration,
        [Description("Optional application operation correlation key.")] string? operationId = null,
        CancellationToken cancellationToken = default)
    {
        // The official SDK maps each method argument to one top-level MCP property.
        // 官方 SDK 会把每个方法参数映射为一个顶层 MCP 属性。
        // Keeping the boundary flat makes the advertised schema directly usable by generic MCP clients.
        // 保持边界扁平，可让通用 MCP 客户端直接按 tools/list 返回的 schema 调用。
        var input = new CreatePartToolInput
        {
            SchemaVersion = schemaVersion,
            OperationId = operationId,
            DocumentId = documentId,
            Configuration = configuration,
        };
        string correlationId = correlation.Resolve(input?.OperationId);
        if (!TryValidate(input, out OperationError? validationError))
        {
            return McpToolResultWriter.Write(OperationResults.Failure<CadDocumentSummary>(correlationId, validationError!));
        }
        CreatePartToolInput validInput = input!;

        var sessionResult = await sessions.GetOrStartAsync(cancellationToken).ConfigureAwait(false);
        if (!sessionResult.IsSuccess)
        {
            return McpToolResultWriter.Write(OperationResults.Failure<CadDocumentSummary>(correlationId, sessionResult.Error!, sessionResult.Evidence));
        }

        OperationResult<ICadPartDocument> created = await sessionResult.Value!.CreatePartAsync(
            new CreatePartRequest
            {
                RequestedDocumentId = new DocumentId(validInput.DocumentId),
                Configuration = validInput.Configuration,
            },
            cancellationToken).ConfigureAwait(false);
        OperationResult<CadDocumentSummary> result = created.IsSuccess
            ? OperationResults.Success(ToSummary(created.Value!), correlationId, created.Evidence!)
            : OperationResults.Failure<CadDocumentSummary>(correlationId, created.Error!, created.Evidence);
        return McpToolResultWriter.Write(result);
    }

    /// <summary>Inspects one stable document identity and returns provider evidence.</summary>
    [McpServerTool(Name = "cad.inspect")]
    [Description("Inspect one CAD document. Preconditions: schemaVersion=1.0 and stable document ID. Side effects: none.")]
    public async Task<CallToolResult> InspectAsync(
        [Description("Protocol schema version; currently 1.0.")] string schemaVersion,
        [Description("Stable document identity to inspect.")] string documentId,
        [Description("Optional application operation correlation key.")] string? operationId = null,
        CancellationToken cancellationToken = default)
    {
        // The explicit parameter list is intentional: it prevents the SDK from advertising a nested "input" object.
        // 显式参数列表是有意设计：避免 SDK 把输入暴露成嵌套的 "input" 对象。
        var input = new InspectToolInput
        {
            SchemaVersion = schemaVersion,
            OperationId = operationId,
            DocumentId = documentId,
        };
        string correlationId = correlation.Resolve(input?.OperationId);
        if (!TryValidate(input, out OperationError? validationError))
        {
            return McpToolResultWriter.Write(OperationResults.Failure<CadInspectionSnapshot>(correlationId, validationError!));
        }
        InspectToolInput validInput = input!;

        var sessionResult = await sessions.GetOrStartAsync(cancellationToken).ConfigureAwait(false);
        if (!sessionResult.IsSuccess)
        {
            return McpToolResultWriter.Write(OperationResults.Failure<CadInspectionSnapshot>(correlationId, sessionResult.Error!, sessionResult.Evidence));
        }

        OperationResult<CadInspectionSnapshot> inspected = await sessionResult.Value!.Inspection.InspectAsync(
            new DocumentId(validInput.DocumentId),
            cancellationToken).ConfigureAwait(false);
        OperationResult<CadInspectionSnapshot> result = inspected.IsSuccess
            ? OperationResults.Success(inspected.Value!, correlationId, inspected.Evidence!)
            : OperationResults.Failure<CadInspectionSnapshot>(correlationId, inspected.Error!, inspected.Evidence);
        return McpToolResultWriter.Write(result);
    }

    private static CadDocumentSummary ToSummary(ICadDocument document) => new()
    {
        DocumentId = document.DocumentId,
        DocumentType = document.DocumentType,
        Path = document.Path,
        Configuration = document.Configuration,
        StateHash = document.StateHash,
        IsDirty = document.IsDirty,
    };

    private static bool TryValidate(CreatePartToolInput? input, out OperationError? error)
    {
        error = null;
        if (input is null)
        {
            error = InvalidInput("create-part input is required");
        }
        else if (!string.Equals(input.SchemaVersion, ProtocolSchema.CurrentVersion, StringComparison.Ordinal))
        {
            error = InvalidInput($"schemaVersion must be '{ProtocolSchema.CurrentVersion}'");
        }
        else if (string.IsNullOrWhiteSpace(input.DocumentId) || string.IsNullOrWhiteSpace(input.Configuration))
        {
            error = InvalidInput("documentId and configuration are required");
        }

        return error is null;
    }

    private static bool TryValidate(InspectToolInput? input, out OperationError? error)
    {
        error = null;
        if (input is null)
        {
            error = InvalidInput("inspect input is required");
        }
        else if (!string.Equals(input.SchemaVersion, ProtocolSchema.CurrentVersion, StringComparison.Ordinal))
        {
            error = InvalidInput($"schemaVersion must be '{ProtocolSchema.CurrentVersion}'");
        }
        else if (string.IsNullOrWhiteSpace(input.DocumentId))
        {
            error = InvalidInput("documentId is required");
        }

        return error is null;
    }

    private static OperationError InvalidInput(string message) => new(
        ErrorCodes.InvalidRequest,
        message,
        ErrorCategories.Validation,
        remediation: "Send a complete request matching the advertised JSON schema.");
}

/// <summary>Schema-visible input for the controlled create-part tool.</summary>
/// <remarks>
/// This DTO documents the domain shape used inside the tool. The reflected SDK boundary is intentionally flat, so the
/// public MCP schema has top-level properties rather than an opaque nested object. 该 DTO 描述 tool 内部的领域输入；
/// SDK 反射边界刻意使用扁平参数，让 MCP schema 暴露顶层属性而不是不透明的嵌套对象。
/// </remarks>
public sealed class CreatePartToolInput
{
    /// <summary>Must equal the advertised protocol schema version.</summary>
    [Description("Protocol schema version; currently 1.0.")]
    public required string SchemaVersion { get; init; }

    /// <summary>Optional caller correlation key.</summary>
    [Description("Optional application operation correlation key.")]
    public string? OperationId { get; init; }

    /// <summary>Stable document identity requested by the caller.</summary>
    [Description("Stable document identity; it is not a display title.")]
    public required string DocumentId { get; init; }

    /// <summary>Configuration name for the new part.</summary>
    [Description("Active configuration name.")]
    public required string Configuration { get; init; }
}

/// <summary>Schema-visible input for the read-only inspect tool.</summary>
/// <remarks>
/// Inspect shares the same version and identity discipline as mutation, but its provider operation is read-only.
/// Inspect 与 mutation 共享相同的版本和 identity 纪律，但 Provider 操作保持只读。
/// </remarks>
public sealed class InspectToolInput
{
    /// <summary>Must equal the advertised protocol schema version.</summary>
    [Description("Protocol schema version; currently 1.0.")]
    public required string SchemaVersion { get; init; }

    /// <summary>Optional caller correlation key.</summary>
    [Description("Optional application operation correlation key.")]
    public string? OperationId { get; init; }

    /// <summary>Stable document identity to inspect.</summary>
    [Description("Stable document identity to inspect.")]
    public required string DocumentId { get; init; }
}

/// <summary>Read-only server health payload.</summary>
/// <remarks>Health reports configuration state only; it never launches SOLIDWORKS or mutates a CAD document.</remarks>
/// <remarks>Health 只报告配置状态，不启动 SOLIDWORKS，也不修改任何 CAD 文档。</remarks>
public sealed class ServerHealth
{
    /// <summary>Server name.</summary>
    public required string ServerName { get; init; }

    /// <summary>Envelope schema identifier.</summary>
    public required string Schema { get; init; }

    /// <summary>Envelope schema version.</summary>
    public required string SchemaVersion { get; init; }

    /// <summary>Provider runtime type name.</summary>
    public required string ProviderType { get; init; }

    /// <summary>Whether native or test provider was explicitly configured.</summary>
    public required bool ProviderConfigured { get; init; }
}

/// <summary>Provider capability and tool metadata discovery payload.</summary>
/// <remarks>
/// Capability discovery is explicit negative knowledge: unsupported operations are advertised as unsupported instead of
/// being hidden. 能力发现也必须表达“不支持”：未实现的操作显式报告 unsupported，不能静默隐藏。
/// </remarks>
public sealed class CapabilityDiscovery
{
    /// <summary>Provider runtime type name.</summary>
    public required string ProviderType { get; init; }

    /// <summary>Explicit supported/unsupported capability map.</summary>
    public required CadCapabilitySet Capabilities { get; init; }

    /// <summary>Compact tool registry with precondition/side-effect metadata.</summary>
    public required System.Collections.Immutable.ImmutableArray<McpToolDescriptor> Tools { get; init; }
}
