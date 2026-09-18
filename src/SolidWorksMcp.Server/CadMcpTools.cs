using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SolidWorksMcp.AutoDrawing;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Core;
using SolidWorksMcp.EngineeringModel;
using SolidWorksMcp.Protocol;
using SolidWorksMcp.RuleEngine;
using SolidWorksMcp.Tolerancing;

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
    McpControlPlane controlPlane,
    SolidWorksMcpConfiguration configuration,
    DrawingReleaseService releaseService)
{
    private readonly ICadProvider provider = provider ?? throw new ArgumentNullException(nameof(provider));
    private readonly McpToolCatalog catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    private readonly McpOperationCorrelation correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
    private readonly CadSessionAccessor sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    private readonly McpCapabilityNegotiator capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
    private readonly McpControlPlane controlPlane = controlPlane ?? throw new ArgumentNullException(nameof(controlPlane));
    private readonly SolidWorksMcpConfiguration configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    private readonly DrawingReleaseService releaseService = releaseService ?? throw new ArgumentNullException(nameof(releaseService));

    /// <summary>Returns read-only server and provider health.</summary>
    [McpServerTool(Name = "cad.health")]
    [Description("Read-only health for SolidWorksMcp. Preconditions: none. Side effects: none.")]
    public async Task<CallToolResult> Health()
    {
        string operationId = correlation.Resolve(null);
        OperationResult<ServerHealth> result = await controlPlane.ExecuteAsync(
            new McpInvocationContext { OperationId = operationId, ToolName = "cad.health" },
            _ =>
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
                OperationEvidence evidence = new("server", [new EvidenceObservation("side-effects", "none")]);
                return Task.FromResult(OperationResults.Success(health, operationId, evidence));
            }).ConfigureAwait(false);
        return McpToolResultWriter.Write(result);
    }

    /// <summary>Returns provider capabilities and the compact tool metadata catalog.</summary>
    [McpServerTool(Name = "cad.capabilities")]
    [Description("Discover CAD capabilities and tool metadata. Preconditions: none. Side effects: none.")]
    public async Task<CallToolResult> Capabilities()
    {
        string operationId = correlation.Resolve(null);
        OperationResult<CapabilityDiscovery> result = await controlPlane.ExecuteAsync(
            new McpInvocationContext { OperationId = operationId, ToolName = "cad.capabilities" },
            _ =>
            {
                var discovery = new CapabilityDiscovery
                {
                    ProviderType = provider.GetType().Name,
                    Capabilities = provider.Capabilities,
                    Tools = catalog.Tools,
                    ControlPlaneOperations = controlPlane.ListOperations(),
                    ToolAvailability = capabilities.GetAvailability(),
                    Configuration = configuration,
                };
                OperationEvidence evidence = new(
                    "server",
                    [new EvidenceObservation("tool-count", catalog.Tools.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))]);
                return Task.FromResult(OperationResults.Success(discovery, operationId, evidence));
            }).ConfigureAwait(false);
        return McpToolResultWriter.Write(result);
    }

    /// <summary>
    /// Explains one tolerance stack-up without opening SOLIDWORKS or reading a source drawing.
    /// </summary>
    /// <remarks>
    /// This is deliberately a pure engineering-intelligence operation. It exposes the same deterministic engine used
    /// by release planning, while keeping AI proposals and private drawing observations review-gated.
    /// 这是纯工程智能操作；它复用 release planner 使用的确定性引擎，并继续对 AI proposal 和秘密图纸观察执行 review gate。
    /// </remarks>
    [McpServerTool(Name = "tolerance.explain")]
    [Description("Explain a bounded worst-case tolerance stack-up. Preconditions: schemaVersion=1.0 and redacted tolerance JSON. Side effects: none; no CAD session is started.")]
    public CallToolResult ExplainTolerance(
        [Description("Protocol schema version; currently 1.0.")] string schemaVersion,
        [Description("Bounded JSON containing functional limits, drawing tolerance, provenance and stack terms; never include PDF text or paths.")] string toleranceRequestJson,
        [Description("Optional application operation correlation key.")] string? operationId = null)
    {
        string correlationId = correlation.Resolve(operationId);
        if (!string.Equals(schemaVersion, ProtocolSchema.CurrentVersion, StringComparison.Ordinal))
        {
            return McpToolResultWriter.Write(
                OperationResults.Failure<ToleranceAnalysisResult>(
                    correlationId,
                    InvalidInput($"schemaVersion must be '{ProtocolSchema.CurrentVersion}'")));
        }

        if (!ToleranceExplainJsonCodec.TryParse(toleranceRequestJson, out ToleranceExplainRequest? request, out string? parseError))
        {
            return McpToolResultWriter.Write(
                OperationResults.Failure<ToleranceAnalysisResult>(
                    correlationId,
                    InvalidInput(parseError ?? "toleranceRequestJson-invalid")));
        }

        ToleranceAnalysisResult analysis = FunctionalToleranceEngine.Analyze(request!.Requirement, request.Stackup);
        OperationEvidence evidence = new(
            "tolerance-engine",
            [
                new EvidenceObservation("analysis.status", analysis.Status.ToString()),
                new EvidenceObservation("analysis.method", request.Stackup.Method.ToString()),
                new EvidenceObservation("analysis.stackup-id", request.Stackup.StackupId),
                new EvidenceObservation("analysis.dimension-id", analysis.DimensionId),
                new EvidenceObservation("analysis.can-release", analysis.CanRelease.ToString()),
            ]);
        return McpToolResultWriter.Write(OperationResults.Success(analysis, correlationId, evidence));
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

        OperationError? controlPlaneError = ValidateBound(
            "cad.create-part",
            correlationId,
            sessionResult.Value!.SessionId,
            targetPath: validInput.Path,
            requiredPathExtension: validInput.Path is null ? null : ".sldprt");
        if (controlPlaneError is not null)
        {
            return McpToolResultWriter.Write(OperationResults.Failure<CadDocumentSummary>(correlationId, controlPlaneError));
        }

        OperationResult<ICadPartDocument> created = await sessionResult.Value.CreatePartAsync(
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
    [Description("Build a draft part and engineering drawing under the versioned GB RulePack. Preconditions: schemaVersion=1.0, allowlisted part/drawing/PDF paths, and a connected closed line/arc profile JSON. The compiler selects an explicit A-series sheet/projection contract and the native provider reads it back before creating views. Optional throughHolePatternJson preserves one repeated-hole engineering group; optional slotCutJson creates one native obround slot cut; optional surfaceFinishJson and centerMarkJson are approval-gated native annotations; optional detailViewJson declares an exact parent view and source region. Semantic pattern/slot notes are draft review evidence, not native associative callouts and do not by themselves authorize drawing.release. Side effects: creates a real part, native views/annotations, and a PDF draft export.")]
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
        [Description("Optional JSON semantic through-hole group: {name, diameterMillimeters, centers:[{xMillimeters,yMillimeters}]}.")] string? throughHolePatternJson = null,
        [Description("Optional JSON native obround slot: {name,widthMillimeters,start:{xMillimeters,yMillimeters},end:{xMillimeters,yMillimeters},supportFaceProbe:{xMillimeters,yMillimeters}}.")] string? slotCutJson = null,
        [Description("Optional JSON approval-gated surface-finish symbol: {annotationId,viewId,positionXMillimeters,positionYMillimeters,symbolType,layDirection,leaderStyle,arrowStyle,maximumRoughness,provenanceKind,provenanceMethod,approvalState,coverageKeys[]}.")] string? surfaceFinishJson = null,
        [Description("Optional JSON approval-gated native center marks: {annotationId,viewId,target,connectionLines,minimumNewMarks,provenanceKind,provenanceMethod,approvalState,coverageKeys[]}. The provider activates the exact view and verifies native count/read-back; it never accepts a synthetic note as a center mark.")] string? centerMarkJson = null,
        [Description("Drawing scale denominator for the deterministic seed views.")] int scaleDenominator = 1,
        [Description("Optional JSON explicit detail view: {parentViewId,name,label,detailCenterXMillimeters,detailCenterYMillimeters,detailRadiusMillimeters,positionXMillimeters,positionYMillimeters,scaleNumerator,scaleDenominator,fullOutline,jaggedOutline}. Coordinates are paper-space millimetres; no screenshot or arbitrary selection is accepted.")] string? detailViewJson = null,
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
                throughHolePatternJson,
                slotCutJson,
                surfaceFinishJson,
                centerMarkJson,
                scaleDenominator,
                detailViewJson,
                out OperationError? validationError,
                out SketchProfileRequest? sketchProfile,
                out ThroughHolePatternRequest? holePattern,
                out SlotCutRequest? slotCut,
                out SurfaceFinishSymbolRequest? surfaceFinish,
                out DrawingCenterMarkRequest? centerMarks,
                out DrawingDetailViewRequest? detailView))
        {
            return McpToolResultWriter.Write(OperationResults.Failure<PartDrawingBuildResult>(correlationId, validationError!));
        }

        // Resolve policy before requesting a CAD session. A malformed standard pack is a product invariant failure,
        // never a reason to open SOLIDWORKS and discover the problem after a mutation has started.
        // 在请求 CAD session 之前先 resolve policy；标准 RulePack malformed 是产品 invariant failure，不能等到
        // 打开 SOLIDWORKS 并开始 mutation 后才发现。
        RulePackResolutionResult rulePackResolution = DrawingRulePackResolver.Resolve(
            StandardRulePackCatalog.CreateGbRulePack());
        if (!rulePackResolution.IsValid || rulePackResolution.Pack is null)
        {
            string diagnostics = string.Join(
                "; ",
                rulePackResolution.Diagnostics.Select(diagnostic => $"{diagnostic.Code}:{diagnostic.RulePath}"));
            return McpToolResultWriter.Write(
                OperationResults.Failure<PartDrawingBuildResult>(
                    correlationId,
                    new OperationError(
                        ErrorCodes.InvariantViolation,
                        $"The built-in GB drawing RulePack could not be resolved: {diagnostics}",
                        ErrorCategories.Invariant)));
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

        // The build service validates all three artifact paths against the provider-owned policy as one atomic input.
        // build service 会把三个 artifact path 作为一个原子输入交给 Provider-owned policy 校验。
        OperationError? controlPlaneError = ValidateBound(
            "cad.build-part-drawing",
            correlationId,
            sessionResult.Value.SessionId);
        if (controlPlaneError is not null)
        {
            return McpToolResultWriter.Write(OperationResults.Failure<PartDrawingBuildResult>(correlationId, controlPlaneError));
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
                RulePack = rulePackResolution.Pack,
                ThroughHolePattern = holePattern,
                SlotCut = slotCut,
                SurfaceFinish = surfaceFinish,
                CenterMarks = centerMarks,
                DetailView = detailView,
            },
            cancellationToken).ConfigureAwait(false);
        OperationResult<PartDrawingBuildResult> result = built.IsSuccess
            ? OperationResults.Success(built.Value!, correlationId, built.Evidence!)
            : OperationResults.Failure<PartDrawingBuildResult>(correlationId, built.Error!, built.Evidence);
        return McpToolResultWriter.Write(result);
    }

    /// <summary>
    /// Builds a part drawing from one versioned engineering-intent document.
    /// 根据一个版本化工程意图文档构建零件工程图。
    /// </summary>
    /// <remarks>
    /// This is the preferred AI-facing entry point. The intent codec lowers only supported semantic features into the
    /// existing deterministic compiler; it does not expose COM, ActiveDoc or a sequence of primitive tool calls.
    /// 这是推荐的 AI-facing 入口。intent codec 只把已支持的工程特征降低到既有确定性 compiler，不暴露 COM、ActiveDoc
    /// 或一串 primitive tool call。
    /// </remarks>
    [McpServerTool(Name = "cad.build-part-drawing-intent")]
    [Description("Preferred AI-facing part drawing compiler entry point. Preconditions: bounded schemaVersion=1.0 intent JSON with explicit part/drawing paths, closed millimetre profile, positive extrusion and supported semantic features. The intent is validated before any CAD session; native SOLIDWORKS mutation, rebuild, reopen inspection and PDF export then use the same deterministic compiler as cad.build-part-drawing. Do not put private PDF text, screenshots or arbitrary COM commands in the intent.")]
    public Task<CallToolResult> BuildPartDrawingIntentAsync(
        [Description("Versioned JSON intent: {schemaVersion:'1.0',part:{documentId,configuration,path,profile:{segments:[...]},extrusionDepthMillimeters,features:[{kind:'throughHolePattern|slot',payload:{...}}]},drawing:{documentId,path,pdfPath,scaleDenominator,surfaceFinish,centerMarks,detailView}}. All dimensions are millimetres.")] string intentJson,
        [Description("Optional application operation correlation key.")] string? operationId = null,
        CancellationToken cancellationToken = default)
    {
        string correlationId = correlation.Resolve(operationId);
        if (!PartDrawingIntentMcpCodec.TryParse(intentJson, out PartDrawingIntentInput? input, out string? parseError))
        {
            return Task.FromResult(
                McpToolResultWriter.Write(
                    OperationResults.Failure<PartDrawingBuildResult>(
                        correlationId,
                        InvalidInput(parseError ?? "partDrawingIntentJson-invalid"))));
        }

        // Keep one authoritative compiler implementation. The intent entry point only changes the AI-facing input
        // shape; it must not fork the provider sequence or silently bypass the existing validation gates.
        // 保持唯一的 compiler 实现：intent 入口只改变 AI-facing 输入形状，不能复制 Provider 流程或绕过既有门禁。
        return BuildPartDrawingAsync(
            input!.SchemaVersion,
            input.DocumentId,
            input.DrawingDocumentId,
            input.Configuration,
            input.PartPath,
            input.DrawingPath,
            input.PdfPath,
            input.ExtrusionDepthMillimeters,
            input.InitialSketchProfileJson,
            input.ThroughHolePatternJson,
            input.SlotCutJson,
            input.SurfaceFinishJson,
            input.CenterMarkJson,
            input.ScaleDenominator,
            input.DetailViewJson,
            operationId,
            cancellationToken);
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

        OperationError? controlPlaneError = ValidateBound(
            "cad.inspect",
            correlationId,
            sessionResult.Value!.SessionId,
            documentId: new DocumentId(validInput.DocumentId));
        if (controlPlaneError is not null)
        {
            return McpToolResultWriter.Write(OperationResults.Failure<CadInspectionSnapshot>(correlationId, controlPlaneError));
        }

        OperationResult<CadInspectionSnapshot> inspected = await sessionResult.Value.Inspection.InspectAsync(
            new DocumentId(validInput.DocumentId),
            cancellationToken).ConfigureAwait(false);
        OperationResult<CadInspectionSnapshot> result = inspected.IsSuccess
            ? OperationResults.Success(inspected.Value!, correlationId, inspected.Evidence!)
            : OperationResults.Failure<CadInspectionSnapshot>(correlationId, inspected.Error!, inspected.Evidence);
        return McpToolResultWriter.Write(result);
    }

    /// <summary>
    /// Validates a real drawing document against an explicit feature-level dimension graph.
    /// 将真实 drawing document 与显式逐特征 dimension graph 做只读 validation。
    /// </summary>
    /// <remarks>
    /// The JSON is parsed before provider session startup, then the exact document identity is inspected through the
    /// provider. This tool never creates, moves or deletes a dimension; it returns deterministic missing-definition
    /// evidence for a later compiler materialization pass. JSON 在 Provider session 启动前解析，随后只 inspection
    /// 精确 document identity；本 tool 不创建、移动或删除尺寸，只返回供后续 compiler materialization 使用的确定性缺失证据。
    /// </remarks>
    [McpServerTool(Name = "drawing.validate")]
    [Description("Validate a real drawing against a bounded feature-level dimension requirement graph. Preconditions: schemaVersion=1.0, stable drawing document ID, bounded requirement/evidence JSON and inspection capability. Side effects: none.")]
    public async Task<CallToolResult> ValidateDrawingAsync(
        [Description("Protocol schema version; currently 1.0.")] string schemaVersion,
        [Description("Stable registered drawing document identity.")] string documentId,
        [Description("JSON graph: {profileId,features:[{featureId,displayName,kind,status,sourceKind,method,observedAtUtc,definitions:[...],datums:[...]}]}.")] string dimensionRequirementJson,
        [Description("JSON evidence array with exact dimensionId, featureId, definitionKey, classification, association, visibility and provenance fields.")] string dimensionEvidenceJson,
        [Description("Optional application operation correlation key.")] string? operationId = null,
        CancellationToken cancellationToken = default)
    {
        string correlationId = correlation.Resolve(operationId);
        if (!string.Equals(schemaVersion, ProtocolSchema.CurrentVersion, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(documentId))
        {
            return McpToolResultWriter.Write(
                OperationResults.Failure<PartDrawingValidationResult>(
                    correlationId,
                    InvalidInput("schemaVersion must be 1.0 and documentId is required.")));
        }

        if (!DrawingDimensionJsonCodec.TryParseGraph(
                dimensionRequirementJson,
                out PartDrawingDimensionRequirementGraph? graph,
                out string? graphError)
            || graph is null)
        {
            return McpToolResultWriter.Write(
                OperationResults.Failure<PartDrawingValidationResult>(
                    correlationId,
                    InvalidInput(graphError ?? "dimensionRequirementJson-invalid")));
        }

        if (!DrawingDimensionJsonCodec.TryParseEvidence(
                dimensionEvidenceJson,
                out PartDrawingDimensionEvidence[] evidence,
                out string? evidenceError))
        {
            return McpToolResultWriter.Write(
                OperationResults.Failure<PartDrawingValidationResult>(
                    correlationId,
                    InvalidInput(evidenceError ?? "dimensionEvidenceJson-invalid")));
        }

        OperationError? capabilityError = capabilities.ValidateInvocation("drawing.validate");
        if (capabilityError is not null)
        {
            return McpToolResultWriter.Write(OperationResults.Failure<PartDrawingValidationResult>(correlationId, capabilityError));
        }

        OperationResult<ICadSession> sessionResult = await sessions.GetOrStartAsync(cancellationToken).ConfigureAwait(false);
        if (!sessionResult.IsSuccess || sessionResult.Value is null)
        {
            return McpToolResultWriter.Write(OperationResults.Failure<PartDrawingValidationResult>(correlationId, sessionResult.Error!, sessionResult.Evidence));
        }

        OperationError? controlPlaneError = ValidateBound(
            "drawing.validate",
            correlationId,
            sessionResult.Value.SessionId,
            documentId: new DocumentId(documentId.Trim()));
        if (controlPlaneError is not null)
        {
            return McpToolResultWriter.Write(OperationResults.Failure<PartDrawingValidationResult>(correlationId, controlPlaneError));
        }

        OperationResult<CadInspectionSnapshot> inspection = await sessionResult.Value.Inspection.InspectAsync(
            new DocumentId(documentId.Trim()),
            cancellationToken).ConfigureAwait(false);
        if (!inspection.IsSuccess || inspection.Value is null)
        {
            return McpToolResultWriter.Write(OperationResults.Failure<PartDrawingValidationResult>(correlationId, inspection.Error!, inspection.Evidence));
        }

        PartDrawingDimensionPlan dimensionPlan = PartDrawingDimensionPlanner.Plan(graph, evidence);
        PartDrawingValidationResult validation = new()
        {
            Drawing = inspection.Value,
            DimensionPlan = dimensionPlan,
        };
        OperationResult<PartDrawingValidationResult> result = OperationResults.Success(
            validation,
            correlationId,
            new OperationEvidence(
                "drawing.validate",
                [
                    new EvidenceObservation("document.id", inspection.Value.Document.DocumentId.Value),
                    new EvidenceObservation("document.state-hash", inspection.Value.Document.StateHash),
                    new EvidenceObservation("dimension.profile", graph.ProfileId),
                    new EvidenceObservation("dimension.status", dimensionPlan.Status.ToString()),
                    new EvidenceObservation("dimension.finding-count", dimensionPlan.Findings.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    new EvidenceObservation("dimension.can-release", dimensionPlan.CanRelease.ToString()),
                ]));
        return McpToolResultWriter.Write(result);
    }

    /// <summary>
    /// Applies one bounded, targeted drawing repair and proves it after save/reopen.
    /// 应用一个有界的定向工程图修复，并在保存/重开后证明结果。
    /// </summary>
    /// <remarks>
    /// The first public repair slice intentionally accepts exactly one supported layout action. This keeps the
    /// mutation atomic at the MCP boundary while the provider-native transaction/checkpoint adapter is completed.
    /// It never loops through arbitrary annotations, trusts ActiveDoc, or moves unrelated objects. 首个公开修复切片
    /// 刻意一次只接受一个已证明的布局 action，在 Provider transaction/checkpoint adapter 完成前保持边界原子性；
    /// 它不会遍历任意标注、信任 ActiveDoc 或移动无关对象。
    /// </remarks>
    [McpServerTool(Name = "drawing.repair")]
    [Description("Apply one deterministic drawing repair. Preconditions: schemaVersion=1.0, exact drawing identity, expected state hash, and one bounded layout.apply-planned-position action. Side effects: moves only the exact annotation, saves the drawing, reopens it, and verifies persisted position.")]
    public async Task<CallToolResult> RepairDrawingAsync(
        [Description("Protocol schema version; currently 1.0.")] string schemaVersion,
        [Description("Stable registered drawing document identity.")] string documentId,
        [Description("State hash captured immediately before planning the repair.")] string expectedStateHash,
        [Description("JSON plan: {schemaVersion:'1.0',fingerprint,actions:[{actionCode:'layout.apply-planned-position',targetId,findingCode,preconditionFingerprint,newPositionXMillimeters,newPositionYMillimeters}]}. Exactly one action is accepted in this bounded slice.")] string repairPlanJson,
        [Description("Optional application operation correlation key.")] string? operationId = null,
        CancellationToken cancellationToken = default)
    {
        string correlationId = correlation.Resolve(operationId);
        if (!string.Equals(schemaVersion, ProtocolSchema.CurrentVersion, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(documentId)
            || string.IsNullOrWhiteSpace(expectedStateHash))
        {
            return McpToolResultWriter.Write(
                OperationResults.Failure<DrawingRepairExecutionResult>(
                    correlationId,
                    InvalidInput("schemaVersion must be 1.0; documentId and expectedStateHash are required.")));
        }

        if (!DrawingRepairJsonCodec.TryParse(
                repairPlanJson,
                out DrawingRepairPlanInput? repairPlan,
                out string? repairPlanError)
            || repairPlan is null)
        {
            return McpToolResultWriter.Write(
                OperationResults.Failure<DrawingRepairExecutionResult>(
                    correlationId,
                    InvalidInput(repairPlanError ?? "repairPlanJson-invalid")));
        }

        if (repairPlan.Actions.Length != 1)
        {
            return McpToolResultWriter.Write(
                OperationResults.Failure<DrawingRepairExecutionResult>(
                    correlationId,
                    InvalidInput("The bounded public drawing.repair slice accepts exactly one action per invocation.")));
        }

        OperationError? capabilityError = capabilities.ValidateInvocation("drawing.repair");
        if (capabilityError is not null)
        {
            return McpToolResultWriter.Write(
                OperationResults.Failure<DrawingRepairExecutionResult>(correlationId, capabilityError));
        }

        OperationResult<ICadSession> sessionResult = await sessions.GetOrStartAsync(cancellationToken).ConfigureAwait(false);
        if (!sessionResult.IsSuccess || sessionResult.Value is null)
        {
            return McpToolResultWriter.Write(
                OperationResults.Failure<DrawingRepairExecutionResult>(
                    correlationId,
                    sessionResult.Error!,
                    sessionResult.Evidence));
        }

        DocumentId targetDocumentId = new(documentId.Trim());
        OperationError? controlPlaneError = ValidateBound(
            "drawing.repair",
            correlationId,
            sessionResult.Value.SessionId,
            documentId: targetDocumentId,
            expectedStateHash: expectedStateHash.Trim());
        if (controlPlaneError is not null)
        {
            return McpToolResultWriter.Write(OperationResults.Failure<DrawingRepairExecutionResult>(correlationId, controlPlaneError));
        }

        OperationResult<CadInspectionSnapshot> initialInspection = await sessionResult.Value.Inspection.InspectAsync(
            targetDocumentId,
            cancellationToken).ConfigureAwait(false);
        if (!initialInspection.IsSuccess || initialInspection.Value is null)
        {
            return McpToolResultWriter.Write(
                OperationResults.Failure<DrawingRepairExecutionResult>(
                    correlationId,
                    initialInspection.Error!,
                    initialInspection.Evidence));
        }

        CadInspectionSnapshot initial = initialInspection.Value;
        if (initial.Document.DocumentType is not CadDocumentType.Drawing)
        {
            return McpToolResultWriter.Write(
                OperationResults.Failure<DrawingRepairExecutionResult>(
                    correlationId,
                    new OperationError(
                        ErrorCodes.InvalidRequest,
                        "The repair target must be a drawing document.",
                        ErrorCategories.Validation)));
        }

        if (!initial.Document.StateHash.Equals(expectedStateHash.Trim(), StringComparison.Ordinal))
        {
            return McpToolResultWriter.Write(
                OperationResults.Failure<DrawingRepairExecutionResult>(
                    correlationId,
                    new OperationError(
                        ErrorCodes.StateConflict,
                        "The drawing state changed after the repair plan was created.",
                        ErrorCategories.State,
                        remediation: "Inspect the exact drawing again and create a new repair plan.")));
        }

        DrawingRepairInputAction action = repairPlan.Actions[0];
        DrawingAnnotationSnapshot? targetAnnotation = initial.Annotations
            .SingleOrDefault(annotation => annotation.AnnotationId.Value.Equals(action.TargetId, StringComparison.Ordinal));
        if (targetAnnotation is null)
        {
            return McpToolResultWriter.Write(
                OperationResults.Failure<DrawingRepairExecutionResult>(
                    correlationId,
                    new OperationError(
                        ErrorCodes.NotFound,
                        "The targeted drawing annotation was not found in the exact inspected drawing.",
                        ErrorCategories.State,
                        remediation: "Re-inspect the drawing and use the current stable annotation identity.")));
        }

        OperationResult<ICadDrawingDocument> drawingResult = await sessionResult.Value.GetDrawingAsync(
            targetDocumentId,
            cancellationToken).ConfigureAwait(false);
        if (!drawingResult.IsSuccess || drawingResult.Value is null)
        {
            return McpToolResultWriter.Write(
                OperationResults.Failure<DrawingRepairExecutionResult>(
                    correlationId,
                    drawingResult.Error!,
                    drawingResult.Evidence));
        }

        OperationResult<DrawingRepairReceipt> repaired = await drawingResult.Value.RepositionAnnotationAsync(
            new DrawingAnnotationPositionRepairRequest
            {
                AnnotationId = targetAnnotation.AnnotationId,
                ExpectedDocumentStateHash = initial.Document.StateHash,
                PreconditionFingerprint = action.PreconditionFingerprint,
                ExpectedCurrentPosition = targetAnnotation.Position,
                NewPosition = action.NewPosition,
            },
            cancellationToken).ConfigureAwait(false);
        if (!repaired.IsSuccess || repaired.Value is null)
        {
            return McpToolResultWriter.Write(
                OperationResults.Failure<DrawingRepairExecutionResult>(
                    correlationId,
                    repaired.Error!,
                    repaired.Evidence));
        }

        OperationResult<SaveReceipt> saved = await drawingResult.Value.SaveAsync(cancellationToken).ConfigureAwait(false);
        if (!saved.IsSuccess || saved.Value is null)
        {
            return McpToolResultWriter.Write(
                OperationResults.Failure<DrawingRepairExecutionResult>(
                    correlationId,
                    saved.Error!,
                    saved.Evidence));
        }

        OperationResult<CadInspectionSnapshot> reopened = await drawingResult.Value.ReopenAndInspectAsync(cancellationToken).ConfigureAwait(false);
        if (!reopened.IsSuccess || reopened.Value is null)
        {
            return McpToolResultWriter.Write(
                OperationResults.Failure<DrawingRepairExecutionResult>(
                    correlationId,
                    reopened.Error!,
                    reopened.Evidence));
        }

        DrawingAnnotationSnapshot? persistedTarget = reopened.Value.Annotations
            .SingleOrDefault(annotation => annotation.AnnotationId == targetAnnotation.AnnotationId);
        if (persistedTarget is null
            || !NearlyEqual(persistedTarget.Position, action.NewPosition)
            || !repaired.Value.Position.Equals(action.NewPosition))
        {
            return McpToolResultWriter.Write(
                OperationResults.Failure<DrawingRepairExecutionResult>(
                    correlationId,
                    new OperationError(
                        ErrorCodes.InvariantViolation,
                        "The targeted annotation position was not proven after save/reopen.",
                        ErrorCategories.Invariant,
                        remediation: "Preserve the drawing and inspect the native annotation before retrying.")));
        }

        var output = new DrawingRepairExecutionResult
        {
            DocumentId = targetDocumentId,
            InitialStateHash = initial.Document.StateHash,
            FinalStateHash = reopened.Value.Document.StateHash,
            RepairPlanFingerprint = repairPlan.Fingerprint,
            Repairs = [repaired.Value],
            Save = saved.Value,
            ReopenedDrawing = reopened.Value,
        };
        OperationResult<DrawingRepairExecutionResult> result = OperationResults.Success(
            output,
            correlationId,
            new OperationEvidence(
                "drawing.repair",
                [
                    new EvidenceObservation("document.id", targetDocumentId.Value),
                    new EvidenceObservation("repair.action", action.ActionCode),
                    new EvidenceObservation("repair.target-id", action.TargetId),
                    new EvidenceObservation("repair.finding-code", action.FindingCode),
                    new EvidenceObservation("repair.plan-fingerprint", repairPlan.Fingerprint),
                    new EvidenceObservation("repair.persistence", "save-reopen-verified"),
                    new EvidenceObservation("document.state-hash.before", initial.Document.StateHash),
                    new EvidenceObservation("document.state-hash.after", reopened.Value.Document.StateHash),
                ],
                [saved.Value.Path],
                reopened.Value.Document.StateHash));
        return McpToolResultWriter.Write(result);
    }

    /// <summary>
    /// Executes one governed single-part drawing release through the Core transaction engine.
    /// 通过 Core transaction engine 执行一次受治理的单零件工程图发布。
    /// </summary>
    /// <remarks>
    /// The endpoint accepts one high-level plan and never exposes a loop of COM primitives. Pure requirement, layout
    /// and manufacturing planners run before session startup; the final provider inspection, export checksums and
    /// manifest are performed inside the checkpointed transaction. endpoint 接受一个高层 plan，不暴露 COM primitive
    /// 循环；纯 requirement/layout/manufacturing planner 在启动 session 前完成，最终 provider inspection、export
    /// checksum 和 manifest 在 checkpointed transaction 内完成。
    /// </remarks>
    [McpServerTool(Name = "drawing.release")]
    [Description("Release one single-part drawing through a bounded QA/compiler plan. Preconditions: schemaVersion=1.0, exact drawing/state hash, approved redacted requirement graph, deterministic layout proof, manufacturing annotation evidence and explicit artifact policy. Side effects: checkpointed drawing save, configured native export, final QA, checksum evidence and a JSON release manifest. No private source drawing content is returned.")]
    public async Task<CallToolResult> ReleaseDrawingAsync(
        [Description("Protocol schema version; currently 1.0.")] string schemaVersion,
        [Description("Stable registered drawing document identity.")] string documentId,
        [Description("State hash captured immediately before semantic planning.")] string expectedStateHash,
        [Description("Redacted requirement graph JSON: {profileId,requirements:[{requirementId,semanticClass,coverageKey,required,status,sourceKind,method,observedAtUtc}]}. It must not contain private PDF text.")] string requirementGraphJson,
        [Description("High-level plan JSON: {preferredPrimaryOrientation,preferredPrimaryOrientationApproved,projection}.")] string planOptionsJson,
        [Description("Bounded dimension requirement graph JSON; same contract as drawing.validate.")] string dimensionRequirementJson,
        [Description("Bounded dimension evidence JSON; same contract as drawing.validate.")] string dimensionEvidenceJson,
        [Description("Bounded layout request JSON with sheetBounds, margins, views/annotations and reserved zones in millimetres.")] string layoutJson,
        [Description("Manufacturing annotation requirement/evidence JSON with approved provenance and exact native annotation identities.")] string manufacturingJson,
        [Description("Artifact policy JSON: {artifacts:[{format,targetPath,allowOverwrite}],manifestPath}. Native SLDDrw target must equal the registered drawing path.")] string artifactPolicyJson,
        [Description("Stable transaction identity used for audit and checkpoint correlation.")] string transactionId,
        [Description("Stable caller idempotency identity; retrying the same key reconciles the committed transaction.")] string idempotencyKey,
        [Description("Optional application operation correlation key.")] string? operationId = null,
        CancellationToken cancellationToken = default)
    {
        string correlationId = correlation.Resolve(operationId);
        if (!string.Equals(schemaVersion, ProtocolSchema.CurrentVersion, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(documentId)
            || string.IsNullOrWhiteSpace(expectedStateHash)
            || string.IsNullOrWhiteSpace(transactionId)
            || string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return McpToolResultWriter.Write(
                OperationResults.Failure<DrawingReleaseExecutionResult>(
                    correlationId,
                    InvalidInput("schemaVersion must be 1.0; documentId, expectedStateHash, transactionId and idempotencyKey are required.")));
        }

        if (!DrawingReleaseJsonCodec.TryParseRequirements(requirementGraphJson, out PartDrawingRequirementSet? requirements, out string? requirementError)
            || requirements is null)
        {
            return McpToolResultWriter.Write(
                OperationResults.Failure<DrawingReleaseExecutionResult>(correlationId, InvalidInput(requirementError ?? "requirementGraphJson-invalid")));
        }

        if (!DrawingReleaseJsonCodec.TryParsePlanOptions(planOptionsJson, out DrawingReleasePlanOptions? planOptions, out string? planOptionsError)
            || planOptions is null)
        {
            return McpToolResultWriter.Write(
                OperationResults.Failure<DrawingReleaseExecutionResult>(correlationId, InvalidInput(planOptionsError ?? "planOptionsJson-invalid")));
        }

        if (!DrawingDimensionJsonCodec.TryParseGraph(
                dimensionRequirementJson,
                out PartDrawingDimensionRequirementGraph? dimensionGraph,
                out string? dimensionGraphError)
            || dimensionGraph is null)
        {
            return McpToolResultWriter.Write(
                OperationResults.Failure<DrawingReleaseExecutionResult>(correlationId, InvalidInput(dimensionGraphError ?? "dimensionRequirementJson-invalid")));
        }

        if (!DrawingDimensionJsonCodec.TryParseEvidence(
                dimensionEvidenceJson,
                out PartDrawingDimensionEvidence[] dimensionEvidence,
                out string? dimensionEvidenceError))
        {
            return McpToolResultWriter.Write(
                OperationResults.Failure<DrawingReleaseExecutionResult>(correlationId, InvalidInput(dimensionEvidenceError ?? "dimensionEvidenceJson-invalid")));
        }

        if (!DrawingReleaseJsonCodec.TryParseLayout(layoutJson, out DrawingLayoutRequest? layoutRequest, out string? layoutError)
            || layoutRequest is null)
        {
            return McpToolResultWriter.Write(
                OperationResults.Failure<DrawingReleaseExecutionResult>(correlationId, InvalidInput(layoutError ?? "layoutJson-invalid")));
        }

        if (!DrawingReleaseJsonCodec.TryParseManufacturing(
                manufacturingJson,
                out DrawingManufacturingInput? manufacturingInput,
                out string? manufacturingError)
            || manufacturingInput is null)
        {
            return McpToolResultWriter.Write(
                OperationResults.Failure<DrawingReleaseExecutionResult>(correlationId, InvalidInput(manufacturingError ?? "manufacturingJson-invalid")));
        }

        if (!DrawingReleaseJsonCodec.TryParseArtifactPolicy(
                artifactPolicyJson,
                out DrawingArtifactPolicy? artifactPolicy,
                out string? artifactPolicyError)
            || artifactPolicy is null)
        {
            return McpToolResultWriter.Write(
                OperationResults.Failure<DrawingReleaseExecutionResult>(correlationId, InvalidInput(artifactPolicyError ?? "artifactPolicyJson-invalid")));
        }

        PartDrawingDimensionPlan dimensionPlan;
        PartDrawingPlan drawingPlan;
        DrawingLayoutPlan layoutPlan;
        DrawingManufacturingAnnotationPlan manufacturingPlan;
        try
        {
            dimensionPlan = PartDrawingDimensionPlanner.Plan(dimensionGraph, dimensionEvidence);
            drawingPlan = PartDrawingPlanner.Plan(
                new PartDrawingPlanRequest
                {
                    Requirements = requirements,
                    PreferredPrimaryOrientation = planOptions.PreferredPrimaryOrientation,
                    PreferredPrimaryOrientationApproved = planOptions.PreferredPrimaryOrientationApproved,
                    Projection = planOptions.Projection,
                });
            layoutPlan = PartDrawingLayoutPlanner.Plan(layoutRequest);
            manufacturingPlan = ManufacturingAnnotationPlanner.Plan(
                manufacturingInput.Requirements,
                manufacturingInput.Evidence);
        }
        catch (ArgumentException exception)
        {
            return McpToolResultWriter.Write(
                OperationResults.Failure<DrawingReleaseExecutionResult>(
                    correlationId,
                    InvalidInput($"release-plan-invalid: {exception.Message}")));
        }

        OperationError? capabilityError = capabilities.ValidateInvocation("drawing.release");
        if (capabilityError is not null)
        {
            return McpToolResultWriter.Write(
                OperationResults.Failure<DrawingReleaseExecutionResult>(correlationId, capabilityError));
        }

        OperationResult<ICadSession> sessionResult = await sessions.GetOrStartAsync(cancellationToken).ConfigureAwait(false);
        if (!sessionResult.IsSuccess || sessionResult.Value is null)
        {
            return McpToolResultWriter.Write(
                OperationResults.Failure<DrawingReleaseExecutionResult>(correlationId, sessionResult.Error!, sessionResult.Evidence));
        }

        DocumentId targetDocumentId = new(documentId.Trim());
        OperationError? controlPlaneError = ValidateBound(
            "drawing.release",
            correlationId,
            sessionResult.Value.SessionId,
            documentId: targetDocumentId,
            expectedStateHash: expectedStateHash.Trim());
        if (controlPlaneError is not null)
        {
            return McpToolResultWriter.Write(OperationResults.Failure<DrawingReleaseExecutionResult>(correlationId, controlPlaneError));
        }

        OperationResult<CadInspectionSnapshot> inspection = await sessionResult.Value.Inspection.InspectAsync(
            targetDocumentId,
            cancellationToken).ConfigureAwait(false);
        if (!inspection.IsSuccess || inspection.Value is null)
        {
            return McpToolResultWriter.Write(
                OperationResults.Failure<DrawingReleaseExecutionResult>(correlationId, inspection.Error!, inspection.Evidence));
        }

        if (!inspection.Value.Document.StateHash.Equals(expectedStateHash.Trim(), StringComparison.Ordinal))
        {
            return McpToolResultWriter.Write(
                OperationResults.Failure<DrawingReleaseExecutionResult>(
                    correlationId,
                    new OperationError(
                        ErrorCodes.StateConflict,
                        "The drawing state hash no longer matches the release plan.",
                        ErrorCategories.State,
                        remediation: "Inspect the exact drawing again and create a new release plan."),
                    inspection.Evidence));
        }

        foreach (DrawingManufacturingAnnotationPlanItem item in manufacturingPlan.Items.Where(item => item.Status == ManufacturingAnnotationFindingStatus.Pass))
        {
            if (item.ExistingAnnotationId is not AnnotationId annotationId)
            {
                return McpToolResultWriter.Write(
                    OperationResults.Failure<DrawingReleaseExecutionResult>(
                        correlationId,
                        new OperationError(
                            ErrorCodes.ReviewRequired,
                            "Approved manufacturing evidence must carry an exact native annotation identity.",
                            ErrorCategories.Policy,
                            remediation: "Re-inspect native annotations and provide exact associative identities."),
                        inspection.Evidence));
            }

            DrawingAnnotationSnapshot? observed = inspection.Value.Annotations.SingleOrDefault(annotation =>
                annotation.AnnotationId == annotationId);
            if (observed is null)
            {
                return McpToolResultWriter.Write(
                    OperationResults.Failure<DrawingReleaseExecutionResult>(
                        correlationId,
                        new OperationError(
                            ErrorCodes.SelectionStale,
                            "Manufacturing evidence references an annotation not present in the exact drawing inspection.",
                            ErrorCategories.State,
                            remediation: "Re-inspect the drawing and rebuild the manufacturing evidence graph."),
                        inspection.Evidence));
            }
        }

        PartDrawingCoverageReport coverage = PartDrawingCoverageAnalyzer.Analyze(
            requirements,
            drawingPlan,
            inspection.Value,
            dimensionPlan,
            layoutPlan);
        var preflightQa = new DrawingQaRequest
        {
            Drawing = inspection.Value,
            Coverage = coverage,
            ManufacturingAnnotations = manufacturingPlan,
            RequiredArtifactFormats = [.. artifactPolicy.Artifacts.Select(item => item.Format)],
            Artifacts = [],
            ExpectedDocumentStateHash = expectedStateHash.Trim(),
            IncludeArtifactFindings = false,
        };

        OperationResult<DrawingReleaseExecutionResult> released = await releaseService.ExecuteAsync(
            sessionResult.Value,
            new DrawingReleaseExecutionRequest
            {
                PreflightQa = preflightQa,
                ArtifactPolicy = artifactPolicy,
                TransactionId = new TransactionId(transactionId.Trim()),
                IdempotencyKey = new IdempotencyKey(idempotencyKey.Trim()),
            },
            cancellationToken).ConfigureAwait(false);
        OperationResult<DrawingReleaseExecutionResult> result = released.IsSuccess
            ? OperationResults.Success(released.Value!, correlationId, released.Evidence!)
            : OperationResults.Failure<DrawingReleaseExecutionResult>(correlationId, released.Error!, released.Evidence);
        return McpToolResultWriter.Write(result);
    }

    private static bool NearlyEqual(Coordinate2D first, Coordinate2D second) =>
        Math.Abs(first.X.Millimeters - second.X.Millimeters) <= 0.000001d
        && Math.Abs(first.Y.Millimeters - second.Y.Millimeters) <= 0.000001d;

    /// <summary>
    /// Re-checks the exact binding immediately before provider execution.
    /// 在进入 Provider 前重新核对精确 binding。
    /// </summary>
    /// <remarks>
    /// Capability negotiation remains a side-effect-free early check; this second gate is the authoritative session,
    /// document, state, idempotency and path check. 能力协商仍是无副作用的早期检查；这里的第二道 gate 才是权威的
    /// session、document、state、idempotency 和 path 检查。
    /// </remarks>
    private OperationError? ValidateBound(
        string toolName,
        string operationId,
        SessionId sessionId,
        DocumentId? documentId = null,
        string? expectedStateHash = null,
        string? targetPath = null,
        string? requiredPathExtension = null) =>
        controlPlane.Validate(
            new McpInvocationContext
            {
                OperationId = operationId,
                ToolName = toolName,
                IdempotencyKey = operationId,
                SessionId = sessionId,
                DocumentId = documentId,
                ExpectedStateHash = expectedStateHash,
                TargetPath = targetPath,
                RequiredPathExtension = requiredPathExtension,
            });

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
        string? throughHolePatternJson,
        string? slotCutJson,
        string? surfaceFinishJson,
        string? centerMarkJson,
        int scaleDenominator,
        string? detailViewJson,
        out OperationError? error,
        out SketchProfileRequest? sketchProfile,
        out ThroughHolePatternRequest? holePattern,
        out SlotCutRequest? slotCut,
        out SurfaceFinishSymbolRequest? surfaceFinish,
        out DrawingCenterMarkRequest? centerMarks,
        out DrawingDetailViewRequest? detailView)
    {
        error = null;
        sketchProfile = null;
        holePattern = null;
        slotCut = null;
        surfaceFinish = null;
        centerMarks = null;
        detailView = null;
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
        else if (!ThroughHolePatternMcpCodec.TryParse(throughHolePatternJson, out holePattern, out string? holePatternError))
        {
            error = InvalidInput(holePatternError ?? "throughHolePatternJson-invalid");
        }
        else if (!SlotCutMcpCodec.TryParse(slotCutJson, out slotCut, out string? slotCutError))
        {
            error = InvalidInput(slotCutError ?? "slotCutJson-invalid");
        }
        else if (!SurfaceFinishMcpCodec.TryParse(surfaceFinishJson, out surfaceFinish, out string? surfaceFinishError))
        {
            error = InvalidInput(surfaceFinishError ?? "surfaceFinishJson-invalid");
        }
        else if (!CenterMarkMcpCodec.TryParse(centerMarkJson, out centerMarks, out string? centerMarkError))
        {
            error = InvalidInput(centerMarkError ?? "centerMarkJson-invalid");
        }
        else if (!DetailViewMcpCodec.TryParse(detailViewJson, out detailView, out string? detailViewError))
        {
            error = InvalidInput(detailViewError ?? "detailViewJson-invalid");
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

    /// <summary>Typed control-plane descriptors consumed by policy and audit.</summary>
    public required System.Collections.Immutable.ImmutableArray<McpOperationDescriptor> ControlPlaneOperations { get; init; }

    /// <summary>Effective availability after provider and feature negotiation.</summary>
    public required System.Collections.Immutable.ImmutableArray<McpToolAvailability> ToolAvailability { get; init; }

    /// <summary>Effective settings without user-local paths or secrets.</summary>
    public required SolidWorksMcpConfiguration Configuration { get; init; }
}
