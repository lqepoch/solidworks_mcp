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

    /// <summary>Adds an annotation associated with an existing drawing view.</summary>
    Task<OperationResult<DrawingAnnotationSnapshot>> AddAnnotationAsync(
        DrawingAnnotationRequest request,
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
