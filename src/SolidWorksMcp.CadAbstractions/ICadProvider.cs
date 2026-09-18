using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.CadAbstractions;

/// <summary>Entry point for a vendor-neutral CAD provider.</summary>
/// <remarks>
/// Native SOLIDWORKS and FakeCad implement this interface. No COM interface, RCW, enum or vendor assembly may cross it.
/// 原生 Provider 与 FakeCad 共用此入口；任何 COM 类型都不得穿过此边界。
/// </remarks>
public interface ICadProvider
{
    /// <summary>Gets the provider's declared capability snapshot.</summary>
    CadCapabilitySet Capabilities { get; }

    /// <summary>Starts or attaches to one provider session.</summary>
    ValueTask<OperationResult<ICadSession>> StartSessionAsync(
        CadSessionOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>One single-writer CAD session bound to a concrete process or deterministic fake instance.</summary>
public interface ICadSession : IAsyncDisposable
{
    /// <summary>Gets the stable session identity.</summary>
    SessionId SessionId { get; }

    /// <summary>Gets the capabilities available in this session.</summary>
    CadCapabilitySet Capabilities { get; }

    /// <summary>Gets the inspection facade for this session.</summary>
    ICadInspectionService Inspection { get; }

    /// <summary>Gets the export facade for this session.</summary>
    ICadExportService Export { get; }

    /// <summary>Gets the declarative selection facade for this session.</summary>
    ICadSelectionService Selection { get; }

    /// <summary>Creates a new part document.</summary>
    Task<OperationResult<ICadPartDocument>> CreatePartAsync(
        CreatePartRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a new assembly document.</summary>
    Task<OperationResult<ICadAssemblyDocument>> CreateAssemblyAsync(
        CreateAssemblyRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a new drawing document.</summary>
    Task<OperationResult<ICadDrawingDocument>> CreateDrawingAsync(
        CreateDrawingRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebinds an already registered drawing identity to a provider-neutral drawing facade.
    /// 将已注册的 drawing identity 重新绑定到无厂商依赖的 drawing facade。
    /// </summary>
    /// <remarks>
    /// High-level MCP operations must not depend on whichever document happens to be active. This lookup therefore
    /// accepts an explicit identity and lets the provider revalidate path, type, configuration and session affinity
    /// before a later mutation. 高层 MCP operation 不能依赖“恰好 active”的文档；此 lookup 必须接收显式 identity，
    /// 由 Provider 在后续 mutation 前重新校验 path、类型、配置和 session affinity。
    /// </remarks>
    Task<OperationResult<ICadDrawingDocument>> GetDrawingAsync(
        DocumentId documentId,
        CancellationToken cancellationToken = default);

    /// <summary>Closes the session and all provider-owned document handles.</summary>
    Task<OperationResult<MutationReceipt>> CloseAsync(CancellationToken cancellationToken = default);
}

/// <summary>Common document operations independent of part/assembly/drawing specialization.</summary>
public interface ICadDocument
{
    /// <summary>Gets the stable identity.</summary>
    DocumentId DocumentId { get; }

    /// <summary>Gets the vendor-neutral document type.</summary>
    CadDocumentType DocumentType { get; }

    /// <summary>Gets the current document path.</summary>
    string Path { get; }

    /// <summary>Gets the active configuration.</summary>
    string Configuration { get; }

    /// <summary>Gets the current deterministic state hash.</summary>
    string StateHash { get; }

    /// <summary>Gets whether in-memory changes are not persisted.</summary>
    bool IsDirty { get; }

    /// <summary>Rebuilds and returns evidence about the resulting state.</summary>
    Task<OperationResult<RebuildReceipt>> RebuildAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists the document according to provider path and overwrite policy.</summary>
    Task<OperationResult<SaveReceipt>> SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes this document handle without closing the owning CAD session.
    /// 关闭当前 document handle，但不关闭所属 CAD session。
    /// </summary>
    /// <remarks>
    /// Providers must verify that the exact persisted document is clean and close only that identity.  A native provider
    /// must not silently close whichever document happens to be active.  Provider 必须校验精确的持久化文档已保存，
    /// 只关闭该 identity；不能静默关闭当时恰好 active 的其它文档。
    /// </remarks>
    Task<OperationResult<MutationReceipt>> CloseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes and reopens this persisted document, then returns fresh inspection evidence.
    /// 关闭并重新打开持久化 document，然后返回新的 inspection evidence。
    /// </summary>
    /// <remarks>
    /// This is intentionally a high-level lifecycle proof rather than a client-controlled COM sequence.  The native
    /// provider performs CloseDoc/OpenDoc6 on its STA and revalidates path, type, configuration, feature identity and
    /// geometry after reopening.  这是高层生命周期证明，不把 COM 序列暴露给 MCP client；原生 Provider 在 STA 上
    /// 调用 CloseDoc/OpenDoc6，并在 reopen 后重新校验 path、type、configuration、feature identity 和 geometry。
    /// </remarks>
    Task<OperationResult<CadInspectionSnapshot>> ReopenAndInspectAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>Part-specific feature and body mutations.</summary>
public interface ICadPartDocument : ICadDocument
{
    /// <summary>Creates a body with a stable identity.</summary>
    Task<OperationResult<BodySnapshot>> CreateBodyAsync(
        CreateBodyRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a simple extrusion to a selected or active body.</summary>
    Task<OperationResult<FeatureSnapshot>> AddExtrusionAsync(
        ExtrusionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cuts one semantic group of through holes from the current solid and verifies the resulting native feature.
    /// 从当前 solid 切削一个具备工程语义的通孔组，并验证生成的 native feature。
    /// </summary>
    Task<OperationResult<FeatureSnapshot>> AddThroughHolePatternAsync(
        ThroughHolePatternRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates one native obround slot profile, cuts it through the verified solid and verifies the rebuilt body.
    /// 创建一个原生长圆槽 profile，贯穿已验证 solid 切除，并验证 rebuild 后的 body。
    /// </summary>
    Task<OperationResult<FeatureSnapshot>> AddSlotCutAsync(
        SlotCutRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Changes one named model dimension, rebuilds the part and returns read-back evidence.</summary>
    /// <remarks>
    /// Providers must resolve the exact dimension identity and active configuration before mutation.  A raw setter
    /// return code is never sufficient; the provider must read the value back and inspect resulting geometry.
    /// Provider 必须在 mutation 前解析精确的 dimension identity 和 active configuration；setter 返回码不是成功证明，
    /// 必须读回 value 并 inspection 几何结果。
    /// </remarks>
    Task<OperationResult<DimensionSnapshot>> SetDimensionValueAsync(
        DimensionUpdateRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Assembly-specific component and mate mutations.</summary>
public interface ICadAssemblyDocument : ICadDocument
{
    /// <summary>Inserts a component while preserving the requested loading state.</summary>
    Task<OperationResult<ComponentSnapshot>> InsertComponentAsync(
        InsertComponentRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a semantic mate between two component instances.</summary>
    Task<OperationResult<MateSnapshot>> AddMateAsync(
        MateRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Drawing-specific view and annotation mutations.</summary>
public interface ICadDrawingDocument : ICadDocument
{
    /// <summary>Adds a drawing view at an explicit paper-space position.</summary>
    Task<OperationResult<DrawingViewSnapshot>> AddViewAsync(
        DrawingViewRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a native section view from a declarative cutting-line request.</summary>
    Task<OperationResult<DrawingViewSnapshot>> AddSectionViewAsync(
        DrawingSectionViewRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a native circular detail view from an explicit parent-view region.
    /// 根据明确的父视图局部区域创建 native 圆形局部放大视图。
    /// </summary>
    Task<OperationResult<DrawingViewSnapshot>> AddDetailViewAsync(
        DrawingDetailViewRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Adds an annotation associated with an existing drawing view.</summary>
    Task<OperationResult<DrawingAnnotationSnapshot>> AddAnnotationAsync(
        DrawingAnnotationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds one native surface-finish symbol after provenance/approval and exact-view preflight.
    /// 在 provenance/approval 与精确 view preflight 通过后添加一个 native 表面粗糙度符号。
    /// </summary>
    Task<OperationResult<DrawingAnnotationSnapshot>> AddSurfaceFinishSymbolAsync(
        SurfaceFinishSymbolRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies one exact annotation-position repair after state and current-position preflight.
    /// 在 state 与当前坐标 preflight 通过后，应用一个精确的 annotation-position repair。
    /// </summary>
    Task<OperationResult<DrawingRepairReceipt>> RepositionAnnotationAsync(
        DrawingAnnotationPositionRepairRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Read-only inspection facade shared by all providers.</summary>
public interface ICadInspectionService
{
    /// <summary>Inspects a document and returns geometry/feature/drawing evidence.</summary>
    Task<OperationResult<CadInspectionSnapshot>> InspectAsync(
        DocumentId documentId,
        CancellationToken cancellationToken = default);
}

/// <summary>Export facade shared by all providers.</summary>
public interface ICadExportService
{
    /// <summary>Exports a document after validating the target request.</summary>
    Task<OperationResult<ExportReceipt>> ExportAsync(
        DocumentId documentId,
        CadExportRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Resolves declarative entity selectors without exposing vendor selection marks or RCWs.</summary>
public interface ICadSelectionService
{
    /// <summary>Resolves one selector against the current document state.</summary>
    Task<OperationResult<CadSelectionSnapshot>> ResolveAsync(
        CadEntitySelector selector,
        CancellationToken cancellationToken = default);
}
