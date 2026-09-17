using System.Globalization;
using System.Runtime.InteropServices;
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
            // Topology names use an explicit provider grammar (for example face-id:42); they never mean
            // "the Nth face" and never call SelectByID2. 拓扑名称使用明确的 Provider 语法，绝不表示第 N 个面，
            // 也绝不调用 SelectByID2。
            CadEntityKind.Face or CadEntityKind.Edge or CadEntityKind.Vertex or CadEntityKind.SketchEntity
                => ResolveTopologySemanticName(model, descriptor, entityKind, semanticName, stateHash),
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
        if (!TryParseGeometrySignature(value, selector.EntityKind, out Dictionary<string, string> fields))
        {
            return false;
        }

        if (fields.TryGetValue("name", out string? name) && !string.IsNullOrWhiteSpace(name))
        {
            NativeSelectionResolution? semantic = ResolveSemanticName(
                model,
                descriptor,
                selector.EntityKind,
                name,
                stateHash);
            if (semantic is not null)
            {
                resolution = semantic with { Resolution = CadSelectionResolution.GeometrySignature };
                return true;
            }
        }

        NativeSelectionResolution? topology = selector.EntityKind switch
        {
            CadEntityKind.Face => ResolveFaceByGeometry(model, descriptor, fields, stateHash),
            CadEntityKind.Edge => ResolveEdgeByGeometry(model, descriptor, fields, stateHash),
            CadEntityKind.Vertex => ResolveVertexByGeometry(model, descriptor, fields, stateHash),
            CadEntityKind.SketchEntity => ResolveSketchEntityByGeometry(model, descriptor, fields, stateHash),
            _ => null,
        };
        if (topology is null)
        {
            return false;
        }

        resolution = topology with { Resolution = CadSelectionResolution.GeometrySignature };
        return true;
    }

    /// <summary>
    /// Parses the provider-owned geometry signature grammar. URI decoding keeps names with spaces and punctuation
    /// unambiguous. 解析 Provider 自己拥有的 geometry signature 语法；URI decode 保证名字中的空格和标点不会
    /// 破坏 pipe 分隔结构。
    /// </summary>
    private static bool TryParseGeometrySignature(
        string value,
        CadEntityKind expectedKind,
        out Dictionary<string, string> fields)
    {
        fields = [with(StringComparer.Ordinal)];
        string[] segments = value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 3
            || !segments[0].Equals("solidworks.geometry.v1", StringComparison.Ordinal)
            || !segments[1].Equals($"kind={expectedKind}", StringComparison.Ordinal))
        {
            return false;
        }

        var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 2; index < segments.Length; index++)
        {
            int separator = segments[index].IndexOf('=');
            if (separator <= 0 || separator == segments[index].Length - 1)
            {
                return false;
            }

            string key = segments[index][..separator].Trim();
            string encoded = segments[index][(separator + 1)..].Trim();
            if (key.Length == 0 || parsed.ContainsKey(key))
            {
                return false;
            }

            try
            {
                parsed.Add(key, Uri.UnescapeDataString(encoded));
            }
            catch (UriFormatException)
            {
                return false;
            }
        }

        fields = parsed;
        return true;
    }

    private static NativeSelectionResolution? ResolveTopologySemanticName(
        ModelDoc2 model,
        SolidWorksDocumentDescriptor descriptor,
        CadEntityKind entityKind,
        string semanticName,
        string stateHash)
    {
        string[] tokens = semanticName.Split(':', StringSplitOptions.TrimEntries);
        if (tokens.Length < 2)
        {
            return null;
        }

        Dictionary<string, string> fields = [with(StringComparer.Ordinal)];
        switch (entityKind)
        {
            case CadEntityKind.Face when tokens[0].Equals("face-id", StringComparison.OrdinalIgnoreCase):
                fields["face-id"] = tokens[1];
                break;
            case CadEntityKind.Edge when tokens[0].Equals("edge-id", StringComparison.OrdinalIgnoreCase):
                fields["edge-id"] = tokens[1];
                break;
            case CadEntityKind.Vertex when tokens[0].Equals("vertex-mm", StringComparison.OrdinalIgnoreCase):
                fields["point-mm"] = semanticName[(semanticName.IndexOf(':') + 1)..];
                break;
            case CadEntityKind.SketchEntity
                when tokens[0].Equals("sketch-segment", StringComparison.OrdinalIgnoreCase)
                    && tokens.Length >= 3:
                fields["sketch-name"] = tokens[1];
                fields["segment-id"] = tokens[2];
                break;
            default:
                return null;
        }

        return entityKind switch
        {
            CadEntityKind.Face => ResolveFaceByGeometry(model, descriptor, fields, stateHash),
            CadEntityKind.Edge => ResolveEdgeByGeometry(model, descriptor, fields, stateHash),
            CadEntityKind.Vertex => ResolveVertexByGeometry(model, descriptor, fields, stateHash),
            CadEntityKind.SketchEntity => ResolveSketchEntityByGeometry(model, descriptor, fields, stateHash),
            _ => null,
        };
    }

    /// <summary>Resolves an imported-body face by its explicit face id and rejects ambiguous matches.</summary>
    private static NativeSelectionResolution? ResolveFaceByGeometry(
        ModelDoc2 model,
        SolidWorksDocumentDescriptor descriptor,
        Dictionary<string, string> fields,
        string stateHash)
    {
        if (!TryReadInt(fields, "face-id", out int faceId))
        {
            return null;
        }

        var matches = new List<NativeSelectionResolution>();
        foreach (IBody2 body in EnumerateSolidBodies(model))
        {
            object? rawFaces = null;
            try
            {
                if (!MatchesBody(fields, body.Name))
                {
                    continue;
                }

                rawFaces = body.GetFaces();
                if (rawFaces is not Array faces)
                {
                    continue;
                }

                foreach (object? value in faces)
                {
                    if (value is not IFace2 face)
                    {
                        continue;
                    }

                    try
                    {
                        if (face.GetFaceId() == faceId)
                        {
                            NativeSelectionResolution? match = CapturePersistentReference(
                                model,
                                face,
                                $"{descriptor.DocumentId.Value}:face:{faceId}",
                                CadSelectionResolution.GeometrySignature,
                                stateHash);
                            if (match is not null)
                            {
                                matches.Add(match);
                            }
                        }
                    }
                    finally
                    {
                        SolidWorksDocumentRouting.Release(face);
                    }
                }
            }
            finally
            {
                SolidWorksDocumentRouting.Release(rawFaces);
                SolidWorksDocumentRouting.Release(body);
            }
        }

        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>Resolves an edge by native edge id; the id is a search key, never an exported list ordinal.</summary>
    private static NativeSelectionResolution? ResolveEdgeByGeometry(
        ModelDoc2 model,
        SolidWorksDocumentDescriptor descriptor,
        Dictionary<string, string> fields,
        string stateHash)
    {
        if (!TryReadInt(fields, "edge-id", out int edgeId))
        {
            return null;
        }

        var matches = new List<NativeSelectionResolution>();
        foreach (IBody2 body in EnumerateSolidBodies(model))
        {
            object? rawEdges = null;
            try
            {
                if (!MatchesBody(fields, body.Name))
                {
                    continue;
                }

                rawEdges = body.GetEdges();
                if (rawEdges is not Array edges)
                {
                    continue;
                }

                foreach (object? value in edges)
                {
                    if (value is not IEdge edge)
                    {
                        continue;
                    }

                    try
                    {
                        if (edge.GetID() == edgeId)
                        {
                            NativeSelectionResolution? match = CapturePersistentReference(
                                model,
                                edge,
                                $"{descriptor.DocumentId.Value}:edge:{edgeId}",
                                CadSelectionResolution.GeometrySignature,
                                stateHash);
                            if (match is not null)
                            {
                                matches.Add(match);
                            }
                        }
                    }
                    finally
                    {
                        SolidWorksDocumentRouting.Release(edge);
                    }
                }
            }
            finally
            {
                SolidWorksDocumentRouting.Release(rawEdges);
                SolidWorksDocumentRouting.Release(body);
            }
        }

        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>Resolves a vertex by canonical millimetre coordinates and rejects coincident ambiguity.</summary>
    private static NativeSelectionResolution? ResolveVertexByGeometry(
        ModelDoc2 model,
        SolidWorksDocumentDescriptor descriptor,
        Dictionary<string, string> fields,
        string stateHash)
    {
        if (!fields.TryGetValue("point-mm", out string? pointText)
            || !TryReadPointMillimeters(pointText, out (double X, double Y, double Z) expected))
        {
            return null;
        }

        var matches = new List<NativeSelectionResolution>();
        foreach (IBody2 body in EnumerateSolidBodies(model))
        {
            object? rawVertices = null;
            try
            {
                if (!MatchesBody(fields, body.Name))
                {
                    continue;
                }

                rawVertices = body.GetVertices();
                if (rawVertices is not Array vertices)
                {
                    continue;
                }

                foreach (object? value in vertices)
                {
                    if (value is not IVertex vertex)
                    {
                        continue;
                    }

                    try
                    {
                        if (!TryReadPointMillimeters(vertex.GetPoint(), out (double X, double Y, double Z) actual)
                            || !SamePoint(expected, actual))
                        {
                            continue;
                        }

                        NativeSelectionResolution? match = CapturePersistentReference(
                            model,
                            vertex,
                            $"{descriptor.DocumentId.Value}:vertex:{FormatPoint(expected)}",
                            CadSelectionResolution.GeometrySignature,
                            stateHash);
                        if (match is not null)
                        {
                            matches.Add(match);
                        }
                    }
                    finally
                    {
                        SolidWorksDocumentRouting.Release(vertex);
                    }
                }
            }
            finally
            {
                SolidWorksDocumentRouting.Release(rawVertices);
                SolidWorksDocumentRouting.Release(body);
            }
        }

        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>Resolves a sketch segment by owning sketch feature name and native segment id.</summary>
    private static NativeSelectionResolution? ResolveSketchEntityByGeometry(
        ModelDoc2 model,
        SolidWorksDocumentDescriptor descriptor,
        Dictionary<string, string> fields,
        string stateHash)
    {
        if (!TryReadInt(fields, "segment-id", out int segmentId))
        {
            return null;
        }

        fields.TryGetValue("sketch-name", out string? requestedSketchName);
        var matches = new List<NativeSelectionResolution>();
        var current = model.FirstFeature() as IFeature;
        try
        {
            while (current is not null)
            {
                IFeature? next = null;
                try
                {
                    string sketchName = current.Name?.Trim() ?? string.Empty;
                    if (requestedSketchName is null
                        || sketchName.Equals(requestedSketchName, StringComparison.Ordinal))
                    {
                        object? specific = null;
                        try
                        {
                            specific = current.GetSpecificFeature2();
                            if (specific is ISketch sketch
                                && sketch.GetSketchSegments() is Array segments)
                            {
                                foreach (object? value in segments)
                                {
                                    if (value is not ISketchSegment segment)
                                    {
                                        continue;
                                    }

                                    try
                                    {
                                        if (!TryReadInt(segment.GetID(), out int actualId) || actualId != segmentId)
                                        {
                                            continue;
                                        }

                                        NativeSelectionResolution? match = CapturePersistentReference(
                                            model,
                                            segment,
                                            $"{descriptor.DocumentId.Value}:sketch-entity:{sketchName}:{segmentId}",
                                            CadSelectionResolution.GeometrySignature,
                                            stateHash);
                                        if (match is not null)
                                        {
                                            matches.Add(match);
                                        }
                                    }
                                    finally
                                    {
                                        SolidWorksDocumentRouting.Release(segment);
                                    }
                                }
                            }
                        }
                        finally
                        {
                            SolidWorksDocumentRouting.Release(specific);
                        }
                    }

                    next = current.GetNextFeature() as IFeature;
                }
                finally
                {
                    SolidWorksDocumentRouting.Release(current);
                }

                current = next;
            }
        }
        finally
        {
            SolidWorksDocumentRouting.Release(current);
        }

        return matches.Count == 1 ? matches[0] : null;
    }

    private static IEnumerable<IBody2> EnumerateSolidBodies(ModelDoc2 model)
    {
        object? rawBodies = null;
        try
        {
            if (model is not IPartDoc part)
            {
                yield break;
            }

            rawBodies = part.GetBodies2((int)swBodyType_e.swSolidBody, false);
            if (rawBodies is not Array bodies)
            {
                yield break;
            }

            for (int index = 0; index < bodies.Length; index++)
            {
                if (bodies.GetValue(index) is IBody2 body)
                {
                    yield return body;
                }
            }
        }
        finally
        {
            SolidWorksDocumentRouting.Release(rawBodies);
        }
    }

    private static bool TryReadInt(Dictionary<string, string> fields, string key, out int value)
    {
        value = 0;
        return fields.TryGetValue(key, out string? text)
            && TryReadInt(text, out value);
    }

    private static bool MatchesBody(Dictionary<string, string> fields, string? bodyName) =>
        !fields.TryGetValue("body", out string? requested)
        || string.IsNullOrWhiteSpace(requested)
        || string.Equals(requested, bodyName?.Trim() ?? string.Empty, StringComparison.Ordinal);

    private static bool TryReadInt(object? value, out int result)
    {
        switch (value)
        {
            case int integer:
                result = integer;
                return true;
            case short shortValue:
                result = shortValue;
                return true;
            case long longValue when longValue is >= int.MinValue and <= int.MaxValue:
                result = (int)longValue;
                return true;
            case string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed):
                result = parsed;
                return true;
            default:
                try
                {
                    result = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                    return true;
                }
                catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
                {
                    result = 0;
                    return false;
                }
        }
    }

    private static bool TryReadPointMillimeters(
        string text,
        out (double X, double Y, double Z) point)
    {
        string[] values = text.Split(',', StringSplitOptions.TrimEntries);
        if (values.Length != 3
            || !double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
            || !double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
            || !double.TryParse(values[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double z)
            || !double.IsFinite(x)
            || !double.IsFinite(y)
            || !double.IsFinite(z))
        {
            point = default;
            return false;
        }

        point = (x, y, z);
        return true;
    }

    private static bool TryReadPointMillimeters(
        object? value,
        out (double X, double Y, double Z) point)
    {
        if (value is Array array && array.Length >= 3)
        {
            try
            {
                double x = Convert.ToDouble(array.GetValue(0), CultureInfo.InvariantCulture) * 1000d;
                double y = Convert.ToDouble(array.GetValue(1), CultureInfo.InvariantCulture) * 1000d;
                double z = Convert.ToDouble(array.GetValue(2), CultureInfo.InvariantCulture) * 1000d;
                point = (x, y, z);
                return double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(z);
            }
            catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
            {
                // SAFEARRAY element types vary across interop versions; an unavailable point must not select by
                // guess. SAFEARRAY 元素类型可能随 Interop 版本变化；点不可用时绝不能猜选 vertex。
            }
        }

        point = default;
        return false;
    }

    private static bool SamePoint((double X, double Y, double Z) left, (double X, double Y, double Z) right) =>
        Math.Abs(left.X - right.X) <= 0.00001d
        && Math.Abs(left.Y - right.Y) <= 0.00001d
        && Math.Abs(left.Z - right.Z) <= 0.00001d;

    private static string FormatPoint((double X, double Y, double Z) point) =>
        string.Join(",", new[] { point.X, point.Y, point.Z }.Select(
            value => value.ToString("0.######", CultureInfo.InvariantCulture)));

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
            IFace2 face => FaceIdentity(selector.DocumentId, persistId, face),
            IEdge edge => EdgeIdentity(selector.DocumentId, persistId, edge),
            IVertex vertex when TryReadPointMillimeters(vertex.GetPoint(), out (double X, double Y, double Z) point) =>
                $"{selector.DocumentId.Value}:vertex:{FormatPoint(point)}",
            ISketchSegment segment when TryReadInt(segment.GetID(), out int id) =>
                $"{selector.DocumentId.Value}:sketch-entity:{id}",
            _ => $"{selector.DocumentId.Value}:{selector.EntityKind.ToString().ToLowerInvariant()}:persist:{Convert.ToHexString(SHA256.HashData(persistId)).ToLowerInvariant()}",
        };

    private static string FaceIdentity(DocumentId documentId, byte[] persistId, IFace2 face)
    {
        try
        {
            int faceId = face.GetFaceId();
            return faceId > 0
                ? $"{documentId.Value}:face:{faceId}"
                : PersistIdentity(documentId, CadEntityKind.Face, persistId);
        }
        catch (COMException)
        {
            return PersistIdentity(documentId, CadEntityKind.Face, persistId);
        }
    }

    private static string EdgeIdentity(DocumentId documentId, byte[] persistId, IEdge edge)
    {
        try
        {
            int edgeId = edge.GetID();
            return edgeId > 0
                ? $"{documentId.Value}:edge:{edgeId}"
                : PersistIdentity(documentId, CadEntityKind.Edge, persistId);
        }
        catch (COMException)
        {
            return PersistIdentity(documentId, CadEntityKind.Edge, persistId);
        }
    }

    private static string PersistIdentity(DocumentId documentId, CadEntityKind kind, byte[] persistId) =>
        $"{documentId.Value}:{kind.ToString().ToLowerInvariant()}:persist:{Convert.ToHexString(SHA256.HashData(persistId)).ToLowerInvariant()}";

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
