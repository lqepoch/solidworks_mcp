using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Provider.SolidWorks;

/// <summary>Provider-owned document metadata; no COM RCW is stored in this registry.</summary>
/// <remarks>
/// A registry entry is a routing assertion, not a cached document object.  Before every operation the native adapter
/// reacquires the open document by its canonical path and verifies type/configuration.  registry entry 只是 routing
/// assertion，不保存 document COM object；每次操作前都按 canonical path 重新获取并校验 type/configuration。
/// </remarks>
internal sealed record SolidWorksDocumentDescriptor(
    DocumentId DocumentId,
    CadDocumentType DocumentType,
    string Path,
    string Title,
    string Configuration,
    string StateHash,
    bool IsDirty,
    string? ProfileFeatureName);

/// <summary>Thread-safe document identity map scoped to one provider session lifetime.</summary>
internal sealed class SolidWorksDocumentRegistry
{
    private readonly ConcurrentDictionary<string, SolidWorksDocumentDescriptor> documents = new(StringComparer.Ordinal);

    /// <summary>Registers the exact descriptor returned by a native create/open operation.</summary>
    public void Add(SolidWorksDocumentDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!documents.TryAdd(descriptor.DocumentId.Value, descriptor))
        {
            throw new InvalidOperationException($"Document identity '{descriptor.DocumentId.Value}' is already registered.");
        }
    }

    /// <summary>Looks up a document identity without returning a COM object.</summary>
    public bool TryGet(DocumentId documentId, out SolidWorksDocumentDescriptor? descriptor) =>
        documents.TryGetValue(documentId.Value, out descriptor);

    /// <summary>Updates provider-neutral state metadata after a verified operation.</summary>
    /// <remarks>
    /// The descriptor update is an exact compare-and-swap: the caller must provide the descriptor observed before
    /// the native operation. A stale operation therefore cannot overwrite a newer document state. 使用 exact
    /// compare-and-swap 更新；调用方必须提供 native operation 前观察到的 descriptor，防止过期操作覆盖更新后的
    /// document state。
    /// </remarks>
    public bool TryUpdateDescriptor(
        SolidWorksDocumentDescriptor expected,
        SolidWorksDocumentDescriptor candidate,
        out SolidWorksDocumentDescriptor? updated)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(candidate);
        if (!expected.DocumentId.Equals(candidate.DocumentId))
        {
            updated = null;
            return false;
        }

        bool replaced = documents.TryUpdate(expected.DocumentId.Value, candidate, expected);
        updated = replaced ? candidate : null;
        return replaced;
    }

    /// <summary>Removes a document after the provider has closed it explicitly.</summary>
    public bool Remove(DocumentId documentId) => documents.TryRemove(documentId.Value, out _);
}

/// <summary>Small vendor-neutral DTO returned from the STA after a new part is created and verified.</summary>
internal sealed record SolidWorksCreatedPart(
    SolidWorksDocumentDescriptor Descriptor);

/// <summary>Helpers for routing and deterministic native state fingerprints.</summary>
internal static class SolidWorksDocumentRouting
{
    /// <summary>
    /// Resolves an open document by path and checks identity before the caller is allowed to mutate it.
    /// 按 path 找到已打开文档，并在 mutation 前校验 identity。
    /// </summary>
    public static OperationResult<ModelDoc2> ResolveOpenDocument(
        ISldWorks application,
        SolidWorksDocumentDescriptor descriptor,
        bool activate = false,
        bool verifyStateHash = false)
    {
        // Mutation paths activate the exact extension-qualified document first.  This is not a shortcut to ActiveDoc:
        // IActivateDoc3 returns the named document and the identity checks below still run before mutation.
        // mutation path 先激活精确的带扩展名 document；这不是依赖 ActiveDoc，下面仍会对返回对象做 identity 校验。
        int activationErrors = 0;
        ModelDoc2? model = activate
            ? application.IActivateDoc3(descriptor.Path, true, ref activationErrors)
            : application.GetOpenDocument(descriptor.Path);
        if (model is null)
        {
            return OperationResults.Failure<ModelDoc2>(
                $"document.resolve:{descriptor.DocumentId.Value}",
                new OperationError(
                    ErrorCodes.NotFound,
                    "The registered SOLIDWORKS document is not currently open.",
                    ErrorCategories.State,
                    retryable: true,
                    remediation: "Open the exact persisted document or re-register a provider-owned document handle."));
        }

        if (activate && activationErrors != 0)
        {
            Release(model);
            return OperationResults.Failure<ModelDoc2>(
                $"document.resolve:{descriptor.DocumentId.Value}",
                new OperationError(
                    ErrorCodes.StateConflict,
                    "SOLIDWORKS could not activate the registered document without an activation error.",
                    ErrorCategories.State,
                    details: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["activation-errors"] = activationErrors.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    },
                    remediation: "Resolve the SOLIDWORKS activation warning/error before retrying the mutation."));
        }

        string actualPath = model.GetPathName()?.Trim() ?? string.Empty;
        string canonicalActualPath = actualPath.Length == 0 ? string.Empty : Path.GetFullPath(actualPath);
        if (!canonicalActualPath.Equals(descriptor.Path, StringComparison.OrdinalIgnoreCase))
        {
            Release(model);
            return OperationResults.Failure<ModelDoc2>(
                $"document.resolve:{descriptor.DocumentId.Value}",
                new OperationError(
                    ErrorCodes.StateConflict,
                    "The open SOLIDWORKS document path does not match the registered target.",
                    ErrorCategories.State,
                    details: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["expected-document-path"] = descriptor.Path,
                        ["actual-document-path"] = canonicalActualPath,
                    },
                    remediation: "Stop the operation and re-inspect the intended document identity."));
        }

        if (!MatchesType(model, descriptor.DocumentType))
        {
            Release(model);
            return OperationResults.Failure<ModelDoc2>(
                $"document.resolve:{descriptor.DocumentId.Value}",
                new OperationError(
                    ErrorCodes.StateConflict,
                    "The open SOLIDWORKS document type does not match the registered target.",
                    ErrorCategories.State,
                    remediation: "Re-inspect the intended document before retrying."));
        }

        string configuration = ReadConfiguration(model);
        if (!configuration.Equals(descriptor.Configuration, StringComparison.Ordinal))
        {
            Release(model);
            return OperationResults.Failure<ModelDoc2>(
                $"document.resolve:{descriptor.DocumentId.Value}",
                new OperationError(
                    ErrorCodes.StateConflict,
                    "The active SOLIDWORKS configuration does not match the registered target.",
                    ErrorCategories.State,
                    details: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["expected-configuration"] = descriptor.Configuration,
                        ["actual-configuration"] = configuration,
                    },
                    remediation: "Activate and re-inspect the intended configuration before mutation."));
        }

        if (verifyStateHash)
        {
            string actualStateHash = ComputeStateHash(model);
            if (!actualStateHash.Equals(descriptor.StateHash, StringComparison.Ordinal))
            {
                Release(model);
                return OperationResults.Failure<ModelDoc2>(
                    $"document.resolve:{descriptor.DocumentId.Value}",
                    new OperationError(
                        ErrorCodes.StateConflict,
                        "The native document state hash changed since the operation was planned.",
                        ErrorCategories.State,
                        details: new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["expected-state-hash"] = descriptor.StateHash,
                            ["actual-state-hash"] = actualStateHash,
                        },
                        remediation: "Re-inspect the document and create a new operation plan before mutation."));
            }
        }

        return OperationResults.Success(
            model,
            $"document.resolve:{descriptor.DocumentId.Value}",
            new OperationEvidence(
                "solidworks-document-routing",
                [
                    new EvidenceObservation("document.id", descriptor.DocumentId.Value),
                    new EvidenceObservation("document.path", descriptor.Path),
                    new EvidenceObservation("document.configuration", configuration),
                ]));
    }

    /// <summary>Builds a state hash from identity and explicit native update markers.</summary>
    public static string ComputeStateHash(ModelDoc2 model)
    {
        ArgumentNullException.ThrowIfNull(model);
        string canonical = string.Join(
            "|",
            model.GetPathName()?.Trim() ?? string.Empty,
            model.GetType().ToString(System.Globalization.CultureInfo.InvariantCulture),
            ReadConfiguration(model),
            model.GetTitle()?.Trim() ?? string.Empty,
            model.GetUpdateStamp().ToString(System.Globalization.CultureInfo.InvariantCulture),
            model.GetSaveFlag().ToString(System.Globalization.CultureInfo.InvariantCulture),
            model.GetFeatureCount().ToString(System.Globalization.CultureInfo.InvariantCulture));
        return $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))}";
    }

    /// <summary>Reads the active configuration and releases the temporary configuration RCW immediately.</summary>
    public static string ReadConfiguration(ModelDoc2 model)
    {
        IConfiguration? configuration = null;
        try
        {
            configuration = model.GetActiveConfiguration() as IConfiguration;
            return configuration?.Name?.Trim() ?? "Default";
        }
        finally
        {
            Release(configuration);
        }
    }

    /// <summary>Converts the native document type enum to the vendor-neutral contract.</summary>
    public static bool MatchesType(ModelDoc2 model, CadDocumentType expected) =>
        model.GetType() == (expected switch
        {
            CadDocumentType.Part => (int)swDocumentTypes_e.swDocPART,
            CadDocumentType.Assembly => (int)swDocumentTypes_e.swDocASSEMBLY,
            CadDocumentType.Drawing => (int)swDocumentTypes_e.swDocDRAWING,
            _ => -1,
        });

    /// <summary>Releases a provider-owned COM child on the dispatcher STA.</summary>
    public static void Release(object? value)
    {
        if (value is not null && System.Runtime.InteropServices.Marshal.IsComObject(value))
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(value);
        }
    }
}
