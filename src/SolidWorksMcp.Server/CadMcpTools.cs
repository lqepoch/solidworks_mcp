using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SolidWorksMcp.AutoDrawing;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Core;
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
    CadSessionAccessor sessions,
    McpCapabilityNegotiator capabilities,
    SolidWorksMcpConfiguration configuration)
{
    private readonly ICadProvider provider = provider ?? throw new ArgumentNullException(nameof(provider));
    private readonly McpToolCatalog catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    private readonly McpOperationCorrelation correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
    private readonly CadSessionAccessor sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    private readonly McpCapabilityNegotiator capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
    private readonly SolidWorksMcpConfiguration configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

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
            ProviderMode = configuration.ProviderMode,
            Features = configuration.Features,
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
            ToolAvailability = capabilities.GetAvailability(),
            Configuration = configuration,
        };
        var result = OperationResults.Success(
            discovery,
            correlation.Resolve(null),
            new OperationEvidence("server", [new EvidenceObservation("tool-count", catalog.Tools.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))]));
        return McpToolResultWriter.Write(result);
    }

    /// <summary>Creates one part after schema/version and identity validation.</summary>
    [McpServerTool(Name = "cad.create-part")]
    [Description("Create one CAD part. Preconditions: schemaVersion=1.0, valid document/configuration, and for native mode an explicit path below the configured allowlist. Optional initialSketchProfileJson is a closed line/arc profile in millimetres. Side effects: creates one part document; no drawing generation.")]
    public async Task<CallToolResult> CreatePartAsync(
        [Description("Protocol schema version; currently 1.0.")] string schemaVersion,
        [Description("Stable document identity; it is not a display title.")] string documentId,
        [Description("Active configuration name.")] string configuration,
        [Description("Explicit absolute .sldprt output path below the configured path allowlist; omit only for FakeCad.")] string? path = null,
        [Description("Optional initial circle radius in millimetres; this is not a SOLIDWORKS metre value.")] double? initialCircleRadiusMillimeters = null,
        [Description("Optional JSON profile with segments [{kind: line|arc, startXMillimeters, startYMillimeters, throughXMillimeters, throughYMillimeters, endXMillimeters, endYMillimeters}]. It must be a connected closed loop; arc through fields are required only for arc segments.")] string? initialSketchProfileJson = null,
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
            Path = path,
            InitialCircleRadiusMillimeters = initialCircleRadiusMillimeters,
            InitialSketchProfileJson = initialSketchProfileJson,
        };
        string correlationId = correlation.Resolve(input?.OperationId);
        if (!TryValidate(input, out OperationError? validationError, out SketchProfileRequest? sketchProfile))
        {
            return McpToolResultWriter.Write(OperationResults.Failure<CadDocumentSummary>(correlationId, validationError!));
        }
        CreatePartToolInput validInput = input!;

        // Capability negotiation is a side-effect-free preflight and must precede session startup.
        // 能力协商是无副作用的 preflight，必须发生在 Session 启动之前。
        OperationError? capabilityError = capabilities.ValidateInvocation("cad.create-part");
        if (capabilityError is not null)
        {
            return McpToolResultWriter.Write(OperationResults.Failure<CadDocumentSummary>(correlationId, capabilityError));
        }

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
                Path = validInput.Path,
                InitialCircleRadius = validInput.InitialCircleRadiusMillimeters is double radius
                    ? Length.FromMillimeters(radius)
                    : null,
                InitialSketchProfile = sketchProfile,
            },
            cancellationToken).ConfigureAwait(false);
        OperationResult<CadDocumentSummary> result = created.IsSuccess
            ? OperationResults.Success(ToSummary(created.Value!), correlationId, created.Evidence!)
            : OperationResults.Failure<CadDocumentSummary>(correlationId, created.Error!, created.Evidence);
        return McpToolResultWriter.Write(result);
    }

    /// <summary>
    /// Builds one bounded real part-to-drawing artifact set through the provider boundary.
    /// 通过 Provider boundary 构建一组有界的真实零件到工程图 artifact。
    /// </summary>
    /// <remarks>
    /// This is the first high-level compiler-facing MCP operation. It intentionally does not expose one tool per COM
    /// primitive: the deterministic sequence is implemented by <see cref="PartDrawingBuildService"/>, while the native
    /// provider still owns all SOLIDWORKS calls and read-back invariants. 这是第一个面向 compiler 的高层 MCP operation，
    /// 不按每个 COM primitive 暴露 tool；确定性序列由工程服务编排，SOLIDWORKS 调用和 read-back invariant 仍归 Provider。
    /// </remarks>
    [McpServerTool(Name = "cad.build-part-drawing")]
    [Description("Build one part and its engineering drawing. Preconditions: schemaVersion=1.0, allowlisted part/drawing/PDF paths, and a connected closed line/arc profile JSON. Side effects: creates a real part, three views, a native model-dimension insertion, and a PDF export.")]
    public async Task<CallToolResult> BuildPartDrawingAsync(
        [Description("Protocol schema version; currently 1.0.")] string schemaVersion,
        [Description("Stable part document identity.")] string documentId,
        [Description("Stable drawing document identity.")] string drawingDocumentId,
        [Description("Active configuration name.")] string configuration,
        [Description("Explicit absolute .sldprt output path below the configured allowlist.")] string partPath,
        [Description("Explicit absolute .slddrw output path below the configured allowlist.")] string drawingPath,
        [Description("Explicit absolute PDF output path below the configured allowlist.")] string pdfPath,
        [Description("Positive extrusion depth in millimetres.")] double extrusionDepthMillimeters,
        [Description("JSON closed line/arc profile in millimetres; same wire format as cad.create-part.")] string initialSketchProfileJson,
        [Description("Drawing scale denominator for the deterministic seed views.")] int scaleDenominator = 1,
        [Description("Optional application operation correlation key.")] string? operationId = null,
        CancellationToken cancellationToken = default)
    {
        string correlationId = correlation.Resolve(operationId);
        if (!TryValidateBuild(
                schemaVersion,
                documentId,
                drawingDocumentId,
                configuration,
                partPath,
                drawingPath,
                pdfPath,
                extrusionDepthMillimeters,
                initialSketchProfileJson,
                scaleDenominator,
                out OperationError? validationError,
                out SketchProfileRequest? sketchProfile))
        {
            return McpToolResultWriter.Write(OperationResults.Failure<PartDrawingBuildResult>(correlationId, validationError!));
        }

        // Check both capability gates before a provider session is started. This keeps unsupported high-level builds
        // side-effect free and makes the capability claim explicit in tools/list and cad.capabilities.
        // 在启动 Provider session 前同时检查两项 capability，确保 unsupported build 无副作用。
        OperationError? drawingCapabilityError = capabilities.ValidateInvocation("cad.build-part-drawing");
        if (drawingCapabilityError is not null)
        {
            return McpToolResultWriter.Write(OperationResults.Failure<PartDrawingBuildResult>(correlationId, drawingCapabilityError));
        }

        OperationError? partCapabilityError = capabilities.ValidateInvocation("cad.create-part");
        if (partCapabilityError is not null)
        {
            return McpToolResultWriter.Write(OperationResults.Failure<PartDrawingBuildResult>(correlationId, partCapabilityError));
        }

        OperationResult<ICadSession> sessionResult = await sessions.GetOrStartAsync(cancellationToken).ConfigureAwait(false);
        if (!sessionResult.IsSuccess || sessionResult.Value is null)
        {
            return McpToolResultWriter.Write(OperationResults.Failure<PartDrawingBuildResult>(correlationId, sessionResult.Error!, sessionResult.Evidence));
        }

        OperationResult<PartDrawingBuildResult> built = await PartDrawingBuildService.ExecuteAsync(
            sessionResult.Value,
            new PartDrawingBuildRequest
            {
                DocumentId = new DocumentId(documentId),
                DrawingDocumentId = new DocumentId(drawingDocumentId),
                Configuration = configuration,
                PartPath = partPath,
                DrawingPath = drawingPath,
                PdfPath = pdfPath,
                InitialSketchProfile = sketchProfile!,
                ExtrusionDepth = Length.FromMillimeters(extrusionDepthMillimeters),
                ScaleDenominator = scaleDenominator,
            },
            cancellationToken).ConfigureAwait(false);
        OperationResult<PartDrawingBuildResult> result = built.IsSuccess
            ? OperationResults.Success(built.Value!, correlationId, built.Evidence!)
            : OperationResults.Failure<PartDrawingBuildResult>(correlationId, built.Error!, built.Evidence);
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

        // Read operations still negotiate explicitly so a missing inspection capability is never hidden by a null result.
        // 读操作同样必须显式协商，不能用 null 结果掩盖缺失的 inspection 能力。
        OperationError? capabilityError = capabilities.ValidateInvocation("cad.inspect");
        if (capabilityError is not null)
        {
            return McpToolResultWriter.Write(OperationResults.Failure<CadInspectionSnapshot>(correlationId, capabilityError));
        }

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

    private static bool TryValidate(
        CreatePartToolInput? input,
        out OperationError? error,
        out SketchProfileRequest? sketchProfile)
    {
        error = null;
        sketchProfile = null;
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
        else if (input.Path is not null && (input.Path.Contains('\r') || input.Path.Contains('\n')))
        {
            error = InvalidInput("path must not contain CR/LF characters");
        }
        else if (input.InitialCircleRadiusMillimeters is double radius
            && (!double.IsFinite(radius) || radius <= 0d))
        {
            error = InvalidInput("initialCircleRadiusMillimeters must be finite and greater than zero");
        }
        else if (input.InitialCircleRadiusMillimeters is not null
            && !string.IsNullOrWhiteSpace(input.InitialSketchProfileJson))
        {
            error = InvalidInput("initialCircleRadiusMillimeters and initialSketchProfileJson are mutually exclusive");
        }
        else if (!SketchProfileMcpCodec.TryParse(
                     input.InitialSketchProfileJson,
                     out sketchProfile,
                     out string? profileError))
        {
            error = InvalidInput(profileError ?? "initialSketchProfileJson-invalid");
        }

        return error is null;
    }

    private static bool TryValidateBuild(
        string? schemaVersion,
        string? documentId,
        string? drawingDocumentId,
        string? configuration,
        string? partPath,
        string? drawingPath,
        string? pdfPath,
        double extrusionDepthMillimeters,
        string? initialSketchProfileJson,
        int scaleDenominator,
        out OperationError? error,
        out SketchProfileRequest? sketchProfile)
    {
        error = null;
        sketchProfile = null;
        if (!string.Equals(schemaVersion, ProtocolSchema.CurrentVersion, StringComparison.Ordinal))
        {
            error = InvalidInput("schemaVersion is unsupported");
        }
        else if (string.IsNullOrWhiteSpace(documentId) || string.IsNullOrWhiteSpace(drawingDocumentId))
        {
            error = InvalidInput("documentId and drawingDocumentId are required");
        }
        else if (string.IsNullOrWhiteSpace(configuration)
            || string.IsNullOrWhiteSpace(partPath)
            || string.IsNullOrWhiteSpace(drawingPath)
            || string.IsNullOrWhiteSpace(pdfPath))
        {
            error = InvalidInput("configuration, partPath, drawingPath and pdfPath are required");
        }
        else if (!double.IsFinite(extrusionDepthMillimeters) || extrusionDepthMillimeters <= 0d)
        {
            error = InvalidInput("extrusionDepthMillimeters must be finite and greater than zero");
        }
        else if (scaleDenominator <= 0)
        {
            error = InvalidInput("scaleDenominator must be greater than zero");
        }
        else if (!SketchProfileMcpCodec.TryParse(initialSketchProfileJson, out sketchProfile, out string? profileError))
        {
            error = InvalidInput(profileError ?? "initialSketchProfileJson-invalid");
        }

        return error is null && sketchProfile is not null;
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

    /// <summary>Optional explicit persisted target; the native provider applies the path allowlist again.</summary>
    [Description("Explicit absolute .sldprt output path below the configured path allowlist.")]
    public string? Path { get; init; }

    /// <summary>Optional initial circle radius in millimetres.</summary>
    [Description("Optional initial circle radius in millimetres.")]
    public double? InitialCircleRadiusMillimeters { get; init; }

    /// <summary>Optional validated closed line/arc profile encoded as bounded JSON.</summary>
    [Description("Optional connected closed line/arc profile JSON in canonical millimetres.")]
    public string? InitialSketchProfileJson { get; init; }
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

    /// <summary>Selected allowlisted provider profile.</summary>
    public required string ProviderMode { get; init; }

    /// <summary>Default-off experimental feature state.</summary>
    public required FeatureFlagSet Features { get; init; }
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

    /// <summary>Effective availability after provider and feature negotiation.</summary>
    public required System.Collections.Immutable.ImmutableArray<McpToolAvailability> ToolAvailability { get; init; }

    /// <summary>Effective settings without user-local paths or secrets.</summary>
    public required SolidWorksMcpConfiguration Configuration { get; init; }
}
