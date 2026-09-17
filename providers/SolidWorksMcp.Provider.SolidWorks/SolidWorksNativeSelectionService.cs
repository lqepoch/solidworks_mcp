using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Provider.SolidWorks;

/// <summary>
/// Resolves declarative selectors against one registered SOLIDWORKS document on the owning STA.
/// 在所属 STA 上针对一个已登记 SOLIDWORKS document 解析声明式 selector。
/// </summary>
/// <remarks>
/// The service never calls SelectByID2, never returns a COM object and never uses an enumeration index as identity.
/// Persistent references are resolved first; provider-defined geometry signatures and exact semantic names are weaker
/// fallbacks.  这里不调用 SelectByID2，不返回 COM 对象，也不把 enumeration index 当 identity。Persistent reference 优先，
/// provider 定义的 geometry signature 和精确 semantic name 只能作为较弱 fallback。
/// </remarks>
internal sealed class SolidWorksNativeSelectionService(
    SolidWorksComSessionHost host,
    SolidWorksDocumentRegistry registry,
    SessionId sessionId,
    string attachmentGeneration,
    CadCapabilitySet capabilities,
    Func<bool> isClosed) : ICadSelectionService
{
    private readonly SolidWorksComSessionHost host = host ?? throw new ArgumentNullException(nameof(host));
    private readonly SolidWorksDocumentRegistry registry = registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly SessionId sessionId = sessionId;
    private readonly string attachmentGeneration = string.IsNullOrWhiteSpace(attachmentGeneration)
        ? throw new ArgumentException("The native session attachment generation is required.", nameof(attachmentGeneration))
        : attachmentGeneration;
    private readonly CadCapabilitySet capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
    private readonly Func<bool> isClosed = isClosed ?? throw new ArgumentNullException(nameof(isClosed));

    /// <inheritdoc />
    public async Task<OperationResult<CadSelectionSnapshot>> ResolveAsync(
        CadEntitySelector selector,
        CancellationToken cancellationToken = default)
    {
        const string operation = "selection.resolve";
        if (selector is null)
        {
            return SolidWorksProviderResults.Failure<CadSelectionSnapshot>(
                operation,
                new OperationError(
                    ErrorCodes.InvalidRequest,
                    "The declarative entity selector is required.",
                    ErrorCategories.Validation));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return SolidWorksProviderResults.Cancelled<CadSelectionSnapshot>(operation);
        }

        if (isClosed())
        {
            return SolidWorksProviderResults.Closed<CadSelectionSnapshot>(operation, sessionId);
        }

        CadCapability capability = capabilities.Find(CadCapabilityNames.Selection)
            ?? new CadCapability(CadCapabilityNames.Selection, supported: false, "The native capability was not declared.");
        if (!capability.Supported)
        {
            return SolidWorksProviderResults.Unsupported<CadSelectionSnapshot>(operation, capability);
        }

        if (!selector.HasResolutionHint)
        {
            return SolidWorksProviderResults.Failure<CadSelectionSnapshot>(
                operation,
                new OperationError(
                    ErrorCodes.InvalidRequest,
                    "A selector must contain a persistent reference, geometry signature or semantic name.",
                    ErrorCategories.Validation));
        }

        if (!registry.TryGet(selector.DocumentId, out SolidWorksDocumentDescriptor? descriptor) || descriptor is null)
        {
            return SolidWorksProviderResults.Failure<CadSelectionSnapshot>(
                operation,
                new OperationError(
                    ErrorCodes.NotFound,
                    "The selector document is not registered in this native provider session.",
                    ErrorCategories.State,
                    remediation: "Create or register the exact document before resolving a selector."));
        }

        OperationResult<NativeSelectionResolution> result = await host.InvokeOnStaAsync(
            sessionId,
            attachmentGeneration,
            application => ResolveOnSta(application, descriptor, selector),
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Value is null)
        {
            return OperationResults.Failure<CadSelectionSnapshot>(result.OperationId, result.Error!, result.Evidence);
        }

        CadSelectionSnapshot snapshot = new()
        {
            DocumentId = selector.DocumentId,
            Entity = new CadEntityReference
            {
                EntityKind = selector.EntityKind,
                Identity = result.Value.Identity,
            },
            Resolution = result.Value.Resolution,
            StateHash = result.Value.StateHash,
            SelectorFingerprint = Fingerprint(selector),
            PersistentReference = result.Value.PersistentReference,
        };
        return SolidWorksProviderResults.Success(
            operation,
            snapshot,
            new EvidenceObservation("document.id", selector.DocumentId.Value),
            new EvidenceObservation("selection.entity-kind", selector.EntityKind.ToString()),
            new EvidenceObservation("selection.entity-id", snapshot.Entity.Identity),
            new EvidenceObservation("selection.resolution", snapshot.Resolution.ToString()),
            new EvidenceObservation("selection.selector-fingerprint", snapshot.SelectorFingerprint),
            new EvidenceObservation("state.hash", snapshot.StateHash));
    }

    private static OperationResult<NativeSelectionResolution> ResolveOnSta(
        ISldWorks application,
        SolidWorksDocumentDescriptor descriptor,
        CadEntitySelector selector)
    {
        const string operation = "selection.resolve";
        OperationResult<ModelDoc2> resolved = SolidWorksDocumentRouting.ResolveOpenDocument(
            application,
            descriptor,
            verifyStateHash: true);
        if (!resolved.IsSuccess || resolved.Value is null)
        {
            return OperationResults.Failure<NativeSelectionResolution>(resolved.OperationId, resolved.Error!, resolved.Evidence);
        }

        try
        {
            string stateHash = SolidWorksDocumentRouting.ComputeStateHash(resolved.Value);
            if (!string.IsNullOrWhiteSpace(selector.ExpectedStateHash)
                && !selector.ExpectedStateHash.Equals(stateHash, StringComparison.Ordinal))
            {
                return Stale(operation, selector.ExpectedStateHash!, stateHash);
            }

            if (selector.PersistentReference is not null)
            {
                if (!selector.PersistentReference.Format.Equals("solidworks.persist3", StringComparison.Ordinal))
                {
                    return SolidWorksProviderResults.Failure<NativeSelectionResolution>(
                        operation,
                        new OperationError(
                            ErrorCodes.InvalidRequest,
                            "The persistent reference format is not supported by the native SOLIDWORKS provider.",
                            ErrorCategories.Validation,
                            remediation: "Use a persistent reference captured from this SOLIDWORKS provider generation."));
                }

                if (!TryDecode(selector.PersistentReference.Token, out byte[]? persistId))
                {
                    return SolidWorksProviderResults.Failure<NativeSelectionResolution>(
                        operation,
                        new OperationError(
                            ErrorCodes.InvalidRequest,
                            "The SOLIDWORKS persistent reference token is not valid base64.",
                            ErrorCategories.Validation));
                }

                object? native = null;
                try
                {
                    native = resolved.Value.Extension.GetObjectByPersistReference3(persistId, out int errorCode);
                    if (errorCode != 0 || native is null)
                    {
                        return Stale(operation, selector, stateHash, errorCode);
                    }

                    if (!MatchesEntityKind(native, selector.EntityKind))
                    {
                        return Stale(operation, selector, stateHash, errorCode: 0, wrongKind: true);
                    }

                    string identity = PersistentIdentity(selector, persistId!, native);
                    return SolidWorksProviderResults.Success(
                        operation,
                        new NativeSelectionResolution(
                            identity,
                            CadSelectionResolution.PersistentReference,
                            stateHash,
                            ToPersistentReference(persistId!)),
                        new EvidenceObservation("selection.persistent-format", "solidworks.persist3"));
                }
                finally
                {
                    SolidWorksDocumentRouting.Release(native);
                }
            }

            if (selector.GeometrySignature is not null
                && TryResolveGeometrySignature(resolved.Value, descriptor, selector, stateHash, out NativeSelectionResolution? geometry))
            {
                return SolidWorksProviderResults.Success(operation, geometry!);
            }

            if (!string.IsNullOrWhiteSpace(selector.SemanticName))
            {
                NativeSelectionResolution? semantic = ResolveSemanticName(
                    resolved.Value,
                    descriptor,
                    selector.EntityKind,
                    selector.SemanticName!.Trim(),
                    stateHash);
                if (semantic is not null)
                {
                    return SolidWorksProviderResults.Success(operation, semantic);
                }
            }

            return SolidWorksProviderResults.Failure<NativeSelectionResolution>(
                operation,
                new OperationError(
                    ErrorCodes.NotFound,
                    "The declarative selector did not resolve exactly one current native entity.",
                    ErrorCategories.State,
                    remediation: "Inspect the document and provide a fresh persistent reference or exact semantic name."));
        }
        finally
        {
            SolidWorksDocumentRouting.Release(resolved.Value);
        }
    }

    private static NativeSelectionResolution? ResolveSemanticName(
        ModelDoc2 model,
        SolidWorksDocumentDescriptor descriptor,
        CadEntityKind entityKind,
        string semanticName,
        string stateHash)
    {
        return entityKind switch
        {
            CadEntityKind.Feature => ResolveFeatureByName(model, descriptor, semanticName, stateHash),
            CadEntityKind.Body => ResolveBodyByName(model, descriptor, semanticName, stateHash),
            _ => null,
        };
    }

    private static NativeSelectionResolution? ResolveFeatureByName(
        ModelDoc2 model,
        SolidWorksDocumentDescriptor descriptor,
        string semanticName,
        string stateHash)
    {
        var matches = new List<NativeSelectionResolution>();
        var current = model.FirstFeature() as IFeature;
        while (current is not null)
        {
            IFeature feature = current;
            current = null;
            try
            {
                string name = feature.Name?.Trim() ?? string.Empty;
                string identity = $"{descriptor.DocumentId.Value}:feature:{name}";
                if (name.Equals(semanticName, StringComparison.Ordinal)
                    || identity.Equals(semanticName, StringComparison.Ordinal))
                {
                    NativeSelectionResolution? resolution = CapturePersistentReference(
                        model,
                        feature,
                        identity,
                        CadSelectionResolution.SemanticSelector,
                        stateHash);
                    if (resolution is not null)
                    {
                        matches.Add(resolution);
                    }
                }

                current = feature.GetNextFeature() as IFeature;
            }
            finally
            {
                SolidWorksDocumentRouting.Release(feature);
            }
        }

        return matches.Count == 1
            ? matches[0]
            : null;
    }

    private static NativeSelectionResolution? ResolveBodyByName(
        ModelDoc2 model,
        SolidWorksDocumentDescriptor descriptor,
        string semanticName,
        string stateHash)
    {
        if (model is not IPartDoc part)
        {
            return null;
        }

        object? rawBodies = part.GetBodies2((int)swBodyType_e.swSolidBody, false);
        if (rawBodies is not Array bodyArray)
        {
            return null;
        }

        var matches = new List<NativeSelectionResolution>();
        for (int index = 0; index < bodyArray.Length; index++)
        {
            if (bodyArray.GetValue(index) is not IBody2 body)
            {
                continue;
            }

            try
            {
                string name = body.Name?.Trim() ?? string.Empty;
                string identity = $"{descriptor.DocumentId.Value}:body:{name}";
                if (name.Equals(semanticName, StringComparison.Ordinal)
                    || identity.Equals(semanticName, StringComparison.Ordinal))
                {
                    NativeSelectionResolution? resolution = CapturePersistentReference(
                        model,
                        body,
                        identity,
                        CadSelectionResolution.SemanticSelector,
                        stateHash);
                    if (resolution is not null)
                    {
                        matches.Add(resolution);
                    }
                }
            }
            finally
            {
                SolidWorksDocumentRouting.Release(body);
            }
        }

        return matches.Count == 1
            ? matches[0]
            : null;
    }

    private static bool TryResolveGeometrySignature(
        ModelDoc2 model,
        SolidWorksDocumentDescriptor descriptor,
        CadEntitySelector selector,
        string stateHash,
        out NativeSelectionResolution? resolution)
    {
        resolution = null;
        string value = selector.GeometrySignature?.Value?.Trim() ?? string.Empty;
        string prefix = $"solidworks.geometry.v1|kind={selector.EntityKind}|name=";
        if (!value.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        string name = value[prefix.Length..].Trim();
        if (name.Length == 0)
        {
            return false;
        }

        NativeSelectionResolution? semantic = ResolveSemanticName(
            model,
            descriptor,
            selector.EntityKind,
            name,
            stateHash);
        if (semantic is null)
        {
            return false;
        }

        resolution = semantic with { Resolution = CadSelectionResolution.GeometrySignature };
        return true;
    }

    private static bool MatchesEntityKind(object native, CadEntityKind entityKind) => entityKind switch
    {
        CadEntityKind.Plane => native is IRefPlane,
        CadEntityKind.Face => native is IFace2,
        CadEntityKind.Edge => native is IEdge,
        CadEntityKind.Vertex => native is IVertex,
        CadEntityKind.SketchEntity => native is ISketchSegment,
        CadEntityKind.Feature => native is IFeature,
        CadEntityKind.Body => native is IBody2,
        CadEntityKind.Component => native is IComponent2,
        CadEntityKind.DrawingView => native is View,
        CadEntityKind.Annotation => native is Annotation,
        _ => false,
    };

    private static string PersistentIdentity(CadEntitySelector selector, byte[] persistId, object native) =>
        native switch
        {
            IFeature feature when !string.IsNullOrWhiteSpace(feature.Name) =>
                $"{selector.DocumentId.Value}:feature:{feature.Name.Trim()}",
            IBody2 body when !string.IsNullOrWhiteSpace(body.Name) =>
                $"{selector.DocumentId.Value}:body:{body.Name.Trim()}",
            _ => $"{selector.DocumentId.Value}:{selector.EntityKind.ToString().ToLowerInvariant()}:persist:{Convert.ToHexString(SHA256.HashData(persistId)).ToLowerInvariant()}",
        };

    private static CadPersistentReference ToPersistentReference(byte[] persistId) => new()
    {
        Format = "solidworks.persist3",
        Token = Convert.ToBase64String(persistId),
    };

    private static NativeSelectionResolution? CapturePersistentReference(
        ModelDoc2 model,
        object native,
        string identity,
        CadSelectionResolution resolution,
        string stateHash)
    {
        object? rawReference = model.Extension.GetPersistReference3(native);
        try
        {
            if (!TryReadPersistId(rawReference, out byte[]? persistId))
            {
                return null;
            }

            return new NativeSelectionResolution(
                identity,
                resolution,
                stateHash,
                ToPersistentReference(persistId!));
        }
        finally
        {
            SolidWorksDocumentRouting.Release(rawReference);
        }
    }

    private static bool TryReadPersistId(object? rawReference, out byte[]? persistId)
    {
        if (rawReference is byte[] bytes && bytes.Length > 0)
        {
            persistId = bytes;
            return true;
        }

        if (rawReference is not Array array || array.Length == 0)
        {
            persistId = null;
            return false;
        }

        try
        {
            byte[] converted = new byte[array.Length];
            for (int index = 0; index < array.Length; index++)
            {
                converted[index] = Convert.ToByte(array.GetValue(index), System.Globalization.CultureInfo.InvariantCulture);
            }

            persistId = converted;
            return true;
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            persistId = null;
            return false;
        }
    }

    private static bool TryDecode(string token, out byte[]? bytes)
    {
        try
        {
            bytes = Convert.FromBase64String(token);
            return bytes.Length > 0;
        }
        catch (FormatException)
        {
            bytes = null;
            return false;
        }
    }

    private static OperationResult<NativeSelectionResolution> Stale(
        string operation,
        string expected,
        string actual,
        int? nativeErrorCode = null) =>
        SolidWorksProviderResults.Failure<NativeSelectionResolution>(
            operation,
            new OperationError(
                ErrorCodes.SelectionStale,
                "The selector was captured against an older or unresolved SOLIDWORKS topology.",
                ErrorCategories.State,
                details: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["expected-state-hash"] = expected,
                    ["actual-state-hash"] = actual,
                    ["native-persist-reference-error"] = nativeErrorCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none",
                },
                remediation: "Inspect the document and capture a fresh declarative selector before retrying."));

    private static OperationResult<NativeSelectionResolution> Stale(
        string operation,
        CadEntitySelector selector,
        string stateHash,
        int errorCode,
        bool wrongKind = false) =>
        SolidWorksProviderResults.Failure<NativeSelectionResolution>(
            operation,
            new OperationError(
                ErrorCodes.SelectionStale,
                wrongKind
                    ? "The persistent reference resolved to a different native entity kind."
                    : "The SOLIDWORKS persistent reference no longer resolves to a current native entity.",
                ErrorCategories.State,
                details: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["expected-state-hash"] = selector.ExpectedStateHash ?? "unspecified",
                    ["actual-state-hash"] = stateHash,
                    ["native-persist-reference-error"] = errorCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
                },
                remediation: "Inspect the document and capture a fresh provider persistent reference."));

    private static string Fingerprint(CadEntitySelector selector)
    {
        string canonical = JsonSerializer.Serialize(selector);
        return $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant()}";
    }

    private sealed record NativeSelectionResolution(
        string Identity,
        CadSelectionResolution Resolution,
        string StateHash,
        CadPersistentReference? PersistentReference);
}
