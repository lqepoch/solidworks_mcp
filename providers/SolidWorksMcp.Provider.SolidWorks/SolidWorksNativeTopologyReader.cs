using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Provider.SolidWorks;

/// <summary>
/// Captures bounded, vendor-neutral topology evidence while all SOLIDWORKS RCWs remain on the provider STA.
/// 在 Provider STA 上 bounded 地 capture vendor-neutral topology evidence，保证所有 SOLIDWORKS RCW 都不跨线程。
/// </summary>
/// <remarks>
/// This reader is intentionally evidence-only. It does not expose enumeration positions as identity, and it treats
/// native persistent references as the preferred round-trip mechanism. Geometry signatures are emitted only when the
/// official API gives a meaningful, deterministic search key. 这个 reader 只负责 evidence，不把 enumeration position
/// 暴露成 identity；native persistent reference 优先，只有官方 API 提供有意义的确定性 search key 时才输出 geometry signature。
/// </remarks>
internal static class SolidWorksNativeTopologyReader
{
    private const int MaximumEntities = 4096;

    public static ImmutableArray<CadTopologyEntitySnapshot> ReadPart(
        ModelDoc2 model,
        SolidWorksDocumentDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(descriptor);

        var snapshots = ImmutableArray.CreateBuilder<CadTopologyEntitySnapshot>();
        object? rawBodies = null;
        try
        {
            if (model is not IPartDoc part)
            {
                return snapshots.ToImmutable();
            }

            rawBodies = part.GetBodies2((int)swBodyType_e.swSolidBody, false);
            if (rawBodies is not Array bodyArray)
            {
                return snapshots.ToImmutable();
            }

            foreach (object? value in bodyArray)
            {
                if (value is not IBody2 body)
                {
                    continue;
                }

                try
                {
                    string bodyName = body.Name?.Trim() ?? string.Empty;
                    ReadFaces(model, descriptor, body, bodyName, snapshots);
                    ReadEdges(model, descriptor, body, bodyName, snapshots);
                    ReadVertices(model, descriptor, body, bodyName, snapshots);
                    if (snapshots.Count >= MaximumEntities)
                    {
                        break;
                    }
                }
                finally
                {
                    SolidWorksDocumentRouting.Release(body);
                }
            }

            if (snapshots.Count < MaximumEntities)
            {
                ReadSketchSegments(model, descriptor, snapshots);
            }

            return snapshots.ToImmutable();
        }
        finally
        {
            SolidWorksDocumentRouting.Release(rawBodies);
        }
    }

    private static void ReadFaces(
        ModelDoc2 model,
        SolidWorksDocumentDescriptor descriptor,
        IBody2 body,
        string bodyName,
        ImmutableArray<CadTopologyEntitySnapshot>.Builder snapshots)
    {
        object? rawFaces = null;
        try
        {
            rawFaces = body.GetFaces();
            if (rawFaces is not Array faces)
            {
                return;
            }

            foreach (object? value in faces)
            {
                if (snapshots.Count >= MaximumEntities || value is not IFace2 face)
                {
                    break;
                }

                try
                {
                    int faceId = face.GetFaceId();
                    CadGeometrySignature? geometry = faceId > 0
                        ? Signature(CadEntityKind.Face, $"body={Encode(bodyName)}", $"face-id={faceId}")
                        : null;
                    AddSnapshot(model, descriptor, CadEntityKind.Face, face, geometry, snapshots);
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
        }
    }

    private static void ReadEdges(
        ModelDoc2 model,
        SolidWorksDocumentDescriptor descriptor,
        IBody2 body,
        string bodyName,
        ImmutableArray<CadTopologyEntitySnapshot>.Builder snapshots)
    {
        object? rawEdges = null;
        try
        {
            rawEdges = body.GetEdges();
            if (rawEdges is not Array edges)
            {
                return;
            }

            foreach (object? value in edges)
            {
                if (snapshots.Count >= MaximumEntities || value is not IEdge edge)
                {
                    break;
                }

                try
                {
                    int edgeId = edge.GetID();
                    // SOLIDWORKS documents GetID as an imported-body edge ID. Native parametric edges commonly
                    // return 0, which is not a unique selector and must therefore stay persistent-reference-only.
                    // 官方把 GetID 定义为 imported body edge ID；native parametric edge 常返回 0，不能作为唯一 selector。
                    CadGeometrySignature? geometry = edgeId > 0
                        ? Signature(
                            CadEntityKind.Edge,
                            $"body={Encode(bodyName)}",
                            $"edge-id={edgeId}")
                        : null;
                    AddSnapshot(model, descriptor, CadEntityKind.Edge, edge, geometry, snapshots);
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
        }
    }

    private static void ReadVertices(
        ModelDoc2 model,
        SolidWorksDocumentDescriptor descriptor,
        IBody2 body,
        string bodyName,
        ImmutableArray<CadTopologyEntitySnapshot>.Builder snapshots)
    {
        object? rawVertices = null;
        try
        {
            rawVertices = body.GetVertices();
            if (rawVertices is not Array vertices)
            {
                return;
            }

            foreach (object? value in vertices)
            {
                if (snapshots.Count >= MaximumEntities || value is not IVertex vertex)
                {
                    break;
                }

                try
                {
                    if (!TryReadPointMillimeters(vertex.GetPoint(), out (double X, double Y, double Z) point))
                    {
                        AddSnapshot(model, descriptor, CadEntityKind.Vertex, vertex, geometry: null, snapshots);
                        continue;
                    }

                    CadGeometrySignature geometry = Signature(
                        CadEntityKind.Vertex,
                        $"body={Encode(bodyName)}",
                        $"point-mm={FormatPoint(point)}");
                    AddSnapshot(model, descriptor, CadEntityKind.Vertex, vertex, geometry, snapshots);
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
        }
    }

    private static void ReadSketchSegments(
        ModelDoc2 model,
        SolidWorksDocumentDescriptor descriptor,
        ImmutableArray<CadTopologyEntitySnapshot>.Builder snapshots)
    {
        var current = model.FirstFeature() as IFeature;
        try
        {
            while (current is not null && snapshots.Count < MaximumEntities)
            {
                IFeature? next = null;
                try
                {
                    string sketchName = current.Name?.Trim() ?? string.Empty;
                    object? specific = null;
                    try
                    {
                        specific = current.GetSpecificFeature2();
                        if (specific is ISketch sketch
                            && sketch.GetSketchSegments() is Array segments)
                        {
                            foreach (object? value in segments)
                            {
                                if (snapshots.Count >= MaximumEntities || value is not ISketchSegment segment)
                                {
                                    break;
                                }

                                try
                                {
                                    if (!TryReadInt(segment.GetID(), out int segmentId))
                                    {
                                        AddSnapshot(model, descriptor, CadEntityKind.SketchEntity, segment, geometry: null, snapshots);
                                        continue;
                                    }

                                    CadGeometrySignature geometry = Signature(
                                        CadEntityKind.SketchEntity,
                                        $"sketch-name={Encode(sketchName)}",
                                        $"segment-id={segmentId}");
                                    AddSnapshot(model, descriptor, CadEntityKind.SketchEntity, segment, geometry, snapshots);
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
    }

    private static void AddSnapshot(
        ModelDoc2 model,
        SolidWorksDocumentDescriptor descriptor,
        CadEntityKind kind,
        object native,
        CadGeometrySignature? geometry,
        ImmutableArray<CadTopologyEntitySnapshot>.Builder snapshots)
    {
        byte[]? persistId = ReadPersistId(model, native);
        string identity = BuildIdentity(descriptor.DocumentId, kind, native, persistId);
        snapshots.Add(
            new CadTopologyEntitySnapshot
            {
                EntityKind = kind,
                Identity = identity,
                GeometrySignature = geometry,
                PersistentReference = persistId is null ? null : new CadPersistentReference
                {
                    Format = "solidworks.persist3",
                    Token = Convert.ToBase64String(persistId),
                },
            });
    }

    private static byte[]? ReadPersistId(ModelDoc2 model, object native)
    {
        object? raw = null;
        try
        {
            raw = model.Extension.GetPersistReference3(native);
            if (raw is byte[] bytes && bytes.Length > 0)
            {
                return bytes;
            }

            if (raw is not Array array || array.Length == 0)
            {
                return null;
            }

            byte[] converted = new byte[array.Length];
            for (int index = 0; index < array.Length; index++)
            {
                converted[index] = Convert.ToByte(array.GetValue(index), CultureInfo.InvariantCulture);
            }

            return converted.Length == 0 ? null : converted;
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException or FormatException or OverflowException)
        {
            return null;
        }
        finally
        {
            SolidWorksDocumentRouting.Release(raw);
        }
    }

    private static string BuildIdentity(
        DocumentId documentId,
        CadEntityKind kind,
        object native,
        byte[]? persistId)
    {
        try
        {
            return native switch
            {
                IFace2 face when face.GetFaceId() > 0 => $"{documentId.Value}:face:{face.GetFaceId()}",
                IEdge edge when edge.GetID() > 0 => $"{documentId.Value}:edge:{edge.GetID()}",
                IVertex vertex when TryReadPointMillimeters(vertex.GetPoint(), out (double X, double Y, double Z) point)
                    => $"{documentId.Value}:vertex:{FormatPoint(point)}",
                ISketchSegment segment when TryReadInt(segment.GetID(), out int segmentId)
                    => $"{documentId.Value}:sketch-entity:{segmentId}",
                _ => PersistIdentity(documentId, kind, persistId),
            };
        }
        catch (COMException)
        {
            return PersistIdentity(documentId, kind, persistId);
        }
    }

    private static string PersistIdentity(DocumentId documentId, CadEntityKind kind, byte[]? persistId)
    {
        string suffix = persistId is null
            ? "unavailable"
            : Convert.ToHexString(SHA256.HashData(persistId)).ToLowerInvariant();
        return $"{documentId.Value}:{kind.ToString().ToLowerInvariant()}:persist:{suffix}";
    }

    private static CadGeometrySignature Signature(CadEntityKind kind, params string[] fields) => new()
    {
        Value = $"solidworks.geometry.v1|kind={kind}|{string.Join('|', fields)}",
    };

    private static string Encode(string value) => Uri.EscapeDataString(value ?? string.Empty);

    private static bool TryReadInt(object? value, out int result)
    {
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
                // A COM SAFEARRAY with an unknown element type is unavailable evidence, never a selection guess.
                // COM SAFEARRAY 元素类型未知时只能视为 evidence unavailable，不能猜选实体。
            }
        }

        point = default;
        return false;
    }

    private static string FormatPoint((double X, double Y, double Z) point) =>
        string.Join(",", new[] { point.X, point.Y, point.Z }.Select(
            value => value.ToString("0.######", CultureInfo.InvariantCulture)));
}
