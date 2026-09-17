using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Core;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Provider.SolidWorks;

/// <summary>
/// Native export adapter for the small, explicitly allowlisted neutral-format slice.
/// 原生导出 adapter 只开放一组明确 allowlist 的中性格式。
/// </summary>
/// <remarks>
/// The adapter deliberately does not expose arbitrary SOLIDWORKS commands, macros, or user-preference mutation.
/// Every export is routed through the provider STA, resolves the registered document by path, verifies the expected
/// state hash, validates an approved output root, and proves a non-empty output file after <c>SaveAs</c> returns.
/// 这里故意不暴露任意 SOLIDWORKS command、macro 或用户偏好修改；每次导出都经过 Provider STA，按登记 path
/// 解析文档，校验 expected state hash，验证输出目录白名单，并在 SaveAs 返回后证明输出文件存在且非空。
///
/// <para>
/// This is a provider boundary, not an engineering export planner. Higher layers decide whether a PDF, STEP, IGES,
/// or STL is appropriate for a manufacturing/release workflow and must retain their own provenance and audit record.
/// 这里是 vendor boundary，不是工程导出规划器；上层必须决定格式是否适合制造/发布流程，并保存 provenance/audit。
/// </para>
/// </remarks>
internal sealed class SolidWorksNativeExportService(
    SolidWorksComSessionHost host,
    SolidWorksDocumentRegistry registry,
    SessionId sessionId,
    string attachmentGeneration,
    CadPathAllowlist pathAllowlist,
    Func<bool> isClosed) : ICadExportService
{
    private readonly SolidWorksComSessionHost host = host ?? throw new ArgumentNullException(nameof(host));
    private readonly SolidWorksDocumentRegistry registry = registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly SessionId sessionId = sessionId;
    private readonly string attachmentGeneration = string.IsNullOrWhiteSpace(attachmentGeneration)
        ? throw new ArgumentException("The native session attachment generation is required.", nameof(attachmentGeneration))
        : attachmentGeneration;
    private readonly CadPathAllowlist pathAllowlist = pathAllowlist ?? throw new ArgumentNullException(nameof(pathAllowlist));
    private readonly Func<bool> isClosed = isClosed ?? throw new ArgumentNullException(nameof(isClosed));

    /// <inheritdoc />
    public async Task<OperationResult<ExportReceipt>> ExportAsync(
        DocumentId documentId,
        CadExportRequest request,
        CancellationToken cancellationToken = default)
    {
        const string operation = "export";

        if (request is null)
        {
            return SolidWorksProviderResults.Failure<ExportReceipt>(
                operation,
                new OperationError(
                    ErrorCodes.InvalidRequest,
                    "An export request is required.",
                    ErrorCategories.Validation));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return SolidWorksProviderResults.Cancelled<ExportReceipt>(operation);
        }

        if (isClosed())
        {
            return SolidWorksProviderResults.Closed<ExportReceipt>(operation, sessionId);
        }

        if (!registry.TryGet(documentId, out SolidWorksDocumentDescriptor? descriptor) || descriptor is null)
        {
            return SolidWorksProviderResults.Failure<ExportReceipt>(
                operation,
                new OperationError(
                    ErrorCodes.NotFound,
                    "The requested document identity is not registered in this provider session.",
                    ErrorCategories.State,
                    remediation: "Create or register the exact document in this provider session before exporting."));
        }

        if (!SolidWorksExportFormatSpec.TryResolve(request.Format, out SolidWorksExportFormatSpec? format)
            || format is null)
        {
            return SolidWorksProviderResults.Failure<ExportReceipt>(
                operation,
                new OperationError(
                    ErrorCodes.InvalidRequest,
                    "The requested export format is not in the native provider allowlist.",
                    ErrorCategories.Validation,
                    details: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["requested-format"] = request.Format?.Trim() ?? string.Empty,
                        ["supported-formats"] = "PDF,STEP,IGES,STL",
                    },
                    remediation: "Use PDF for a drawing or STEP/IGES/STL for a part or assembly."));
        }

        SolidWorksExportFormatSpec selectedFormat = format;
        if (!selectedFormat.AllowedDocumentTypes.Contains(descriptor.DocumentType))
        {
            return SolidWorksProviderResults.Failure<ExportReceipt>(
                operation,
                new OperationError(
                    ErrorCodes.InvalidRequest,
                    $"Export format '{selectedFormat.CanonicalName}' is not valid for the registered document type.",
                    ErrorCategories.Validation,
                    details: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["format"] = selectedFormat.CanonicalName,
                        ["document-type"] = descriptor.DocumentType.ToString(),
                    },
                    remediation: "Use PDF for a drawing, or use a neutral model format for a part/assembly."));
        }

        CadPathValidationResult validation = ValidateTarget(request.TargetPath, selectedFormat);
        if (!validation.IsAllowed || validation.FullPath is null)
        {
            return SolidWorksProviderResults.Failure<ExportReceipt>(
                operation,
                new OperationError(
                    ErrorCodes.PathNotAllowed,
                    "The export target is outside the configured path or extension policy.",
                    ErrorCategories.Policy,
                    details: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["reason"] = validation.FailureReason ?? "invalid-target",
                        ["format"] = selectedFormat.CanonicalName,
                    },
                    remediation: "Use an absolute file path under an approved artifact root with the format extension."));
        }

        if (File.Exists(validation.FullPath) && !request.AllowOverwrite)
        {
            return SolidWorksProviderResults.Failure<ExportReceipt>(
                operation,
                new OperationError(
                    ErrorCodes.InvalidRequest,
                    "The export target already exists and overwrite was not explicitly allowed.",
                    ErrorCategories.Policy,
                    details: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["format"] = format.CanonicalName,
                        ["overwrite-allowed"] = bool.FalseString,
                    },
                    remediation: "Choose a new target or set AllowOverwrite=true as an explicit destructive-save policy decision."));
        }

        if (File.Exists(validation.FullPath)
            && (File.GetAttributes(validation.FullPath) & FileAttributes.ReadOnly) != 0)
        {
            return SolidWorksProviderResults.Failure<ExportReceipt>(
                operation,
                new OperationError(
                    ErrorCodes.PathNotAllowed,
                    "The existing export target is read-only.",
                    ErrorCategories.Policy,
                    remediation: "Choose a writable target or remove the read-only attribute through an operator-approved workflow."));
        }

        string targetPath = validation.FullPath;
        return await host.InvokeOnStaAsync(
            sessionId,
            attachmentGeneration,
            application => ExportOnSta(application, descriptor, selectedFormat, targetPath, request.AllowOverwrite),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Validates the output extension by passing each legal extension through the shared path policy.
    /// 对每个合法扩展名复用统一 path policy，避免导出 adapter 自己复制 allowlist 逻辑。
    /// </summary>
    private CadPathValidationResult ValidateTarget(string? rawPath, SolidWorksExportFormatSpec format)
    {
        CadPathValidationResult? first = null;
        foreach (string extension in format.Extensions)
        {
            CadPathValidationResult result = pathAllowlist.ValidateCreateTarget(rawPath, extension);
            first ??= result;
            if (result.IsAllowed)
            {
                return result;
            }
        }

        return first ?? new CadPathValidationResult
        {
            IsAllowed = false,
            FailureReason = "missing-extension-policy",
        };
    }

    /// <summary>Runs the verified native export on the owning STA and returns only provider-neutral evidence.</summary>
    private static OperationResult<ExportReceipt> ExportOnSta(
        ISldWorks application,
        SolidWorksDocumentDescriptor descriptor,
        SolidWorksExportFormatSpec format,
        string targetPath,
        bool allowOverwrite)
    {
        const string operation = "export";
        OperationResult<ModelDoc2> resolved = SolidWorksDocumentRouting.ResolveOpenDocument(
            application,
            descriptor,
            activate: true,
            verifyStateHash: true);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return OperationResults.Failure<ExportReceipt>(resolved.OperationId, resolved.Error!, resolved.Evidence);
        }

        ModelDoc2 model = resolved.Value;
        try
        {
            // SaveAs overwrites existing files by native design. We check the policy before entering COM and retain
            // the flag in evidence so an audit can distinguish a new output from an explicitly allowed overwrite.
            // SaveAs 原生行为会覆盖已有文件；COM 之前已检查 policy，证据保留该 flag 供 audit 区分。
            if (File.Exists(targetPath) && !allowOverwrite)
            {
                return SolidWorksProviderResults.Failure<ExportReceipt>(
                    operation,
                    new OperationError(
                        ErrorCodes.InvalidRequest,
                        "The export target appeared after preflight and overwrite is not allowed.",
                        ErrorCategories.Policy,
                        remediation: "Retry with a new target or an explicit overwrite decision."));
            }

            // The official SaveAs contract exports the entire model unless selection remains active. Clearing the
            // selection is therefore a required export invariant, not cosmetic UI cleanup.
            // 官方 SaveAs 约定在没有选择时导出整个模型；清空 selection 是导出 invariant，而不是 UI 清理。
            model.ClearSelection2(true);

            int saveErrors = 0;
            int saveWarnings = 0;
            bool saved = model.Extension.SaveAs(
                targetPath,
                (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                null!,
                ref saveErrors,
                ref saveWarnings);

            if (!saved || saveErrors != 0)
            {
                return SolidWorksProviderResults.Failure<ExportReceipt>(
                    operation,
                    new OperationError(
                        ErrorCodes.ProviderFailure,
                        "SOLIDWORKS SaveAs did not prove a successful export.",
                        ErrorCategories.Provider,
                        details: new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["format"] = format.CanonicalName,
                            ["save-returned"] = saved.ToString(),
                            ["save-errors"] = saveErrors.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            ["save-warnings"] = saveWarnings.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        },
                        remediation: "Preserve the artifact and inspect the native export error/warning codes."));
            }

            FileInfo output = new(targetPath);
            if (!output.Exists || output.Length <= 0)
            {
                return SolidWorksProviderResults.Failure<ExportReceipt>(
                    operation,
                    new OperationError(
                        ErrorCodes.InvariantViolation,
                        "SOLIDWORKS reported export success but no non-empty output file was observed.",
                        ErrorCategories.Invariant,
                        details: new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["format"] = format.CanonicalName,
                            ["file-exists"] = output.Exists.ToString(),
                            ["file-length"] = output.Exists
                                ? output.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
                                : "0",
                        },
                        remediation: "Preserve the session evidence and investigate the native export destination."));
            }

            // Export must not mutate the source CAD identity. A hash change means the caller's plan is no longer
            // truthful even though the target file exists, so the operation fails closed.
            // 导出不能改变 source CAD identity；即使目标存在，hash 变化也说明 plan 已不再真实，必须 fail closed。
            string stateHashAfterExport = SolidWorksDocumentRouting.ComputeStateHash(model);
            if (!stateHashAfterExport.Equals(descriptor.StateHash, StringComparison.Ordinal))
            {
                return SolidWorksProviderResults.Failure<ExportReceipt>(
                    operation,
                    new OperationError(
                        ErrorCodes.StateConflict,
                        "The source document state changed during export.",
                        ErrorCategories.State,
                        details: new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["expected-state-hash"] = descriptor.StateHash,
                            ["actual-state-hash"] = stateHashAfterExport,
                            ["format"] = format.CanonicalName,
                        },
                        remediation: "Re-inspect the source document and create a new export plan."));
            }

            return SolidWorksProviderResults.Success(
                operation,
                new ExportReceipt
                {
                    TargetPath = targetPath,
                    Format = format.CanonicalName,
                    SourceStateHash = descriptor.StateHash,
                },
                new EvidenceObservation("export.returned", saved.ToString()),
                new EvidenceObservation("export.format", format.CanonicalName),
                new EvidenceObservation("export.overwrite-allowed", allowOverwrite.ToString()),
                new EvidenceObservation("export.target.exists", output.Exists.ToString()),
                new EvidenceObservation("export.target.bytes", output.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new EvidenceObservation("export.source-state-hash", descriptor.StateHash),
                new EvidenceObservation("save.errors", saveErrors.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new EvidenceObservation("save.warnings", saveWarnings.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        catch (Exception exception)
        {
            return SolidWorksProviderResults.ProviderFailure<ExportReceipt>(
                operation,
                exception,
                "The native SOLIDWORKS export call failed before verified output evidence was returned.");
        }
        finally
        {
            SolidWorksDocumentRouting.Release(model);
        }
    }
}

/// <summary>Immutable provider allowlist entry for one native export family.</summary>
/// <remarks>
/// Format resolution is intentionally local and deterministic. It is not a pass-through to a SOLIDWORKS filename
/// extension because arbitrary extensions would become an undocumented escape hatch around policy and testing.
/// 格式解析是本地确定性的；不直接把任意扩展名透传给 SOLIDWORKS，避免形成绕过 policy/test 的逃生口。
/// </remarks>
internal sealed record SolidWorksExportFormatSpec(
    string CanonicalName,
    string[] Extensions,
    CadDocumentType[] AllowedDocumentTypes)
{
    public static bool TryResolve(string? requested, out SolidWorksExportFormatSpec? format)
    {
        string normalized = requested?.Trim().ToUpperInvariant() ?? string.Empty;
        format = normalized switch
        {
            "PDF" => new("PDF", [".pdf"], [CadDocumentType.Drawing]),
            "STEP" or "STP" => new("STEP", [".step", ".stp"], [CadDocumentType.Part, CadDocumentType.Assembly]),
            "IGES" or "IGS" => new("IGES", [".iges", ".igs"], [CadDocumentType.Part, CadDocumentType.Assembly]),
            "STL" => new("STL", [".stl"], [CadDocumentType.Part, CadDocumentType.Assembly]),
            _ => null,
        };
        return format is not null;
    }
}
