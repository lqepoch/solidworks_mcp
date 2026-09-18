using System.Text.Json;

namespace SolidWorksMcp.Server;

/// <summary>
/// Normalized input produced by the high-level part drawing intent codec.
/// 高层零件工程图意图 codec 生成的规范化输入。
/// </summary>
/// <remarks>
/// This record deliberately contains JSON fragments only at the MCP boundary. The existing bounded codecs validate
/// each fragment into vendor-neutral CadAbstractions before a provider session is requested. 这样可以让公共 MCP
/// schema 表达工程意图，同时继续复用既有的 profile、hole、slot、annotation 和 detail-view 校验，不把 JSON
/// 直接传入 COM。
/// </remarks>
internal sealed record PartDrawingIntentInput
{
    public required string SchemaVersion { get; init; }

    public required string DocumentId { get; init; }

    public required string DrawingDocumentId { get; init; }

    public required string Configuration { get; init; }

    public required string PartPath { get; init; }

    public required string DrawingPath { get; init; }

    public required string PdfPath { get; init; }

    public required double ExtrusionDepthMillimeters { get; init; }

    public required string InitialSketchProfileJson { get; init; }

    public string? ThroughHolePatternJson { get; init; }

    public string? SlotCutJson { get; init; }

    public string? SurfaceFinishJson { get; init; }

    public string? CenterMarkJson { get; init; }

    public string? DetailViewJson { get; init; }

    public int ScaleDenominator { get; init; } = 1;
}

/// <summary>
/// Parses the bounded, intent-oriented JSON contract for a single-part drawing build.
/// 解析单个零件工程图构建使用的 bounded 工程意图 JSON 契约。
/// </summary>
/// <remarks>
/// The codec is intentionally strict about the top-level shape and feature kinds. It accepts engineering features that
/// the current compiler can lower (profile, extrusion, through-hole pattern and obround slot), and rejects unsupported
/// feature kinds before any CAD session starts. It never infers geometry from prose, filenames or screenshots.
/// codec 对顶层结构和 feature kind 执行严格校验，只接受当前 compiler 能降低的工程特征；不支持的 feature 会在
/// CAD session 启动前拒绝。它绝不从自然语言、文件名或截图推断几何。
/// </remarks>
internal static class PartDrawingIntentMcpCodec
{
    private const int MaximumJsonLength = 512 * 1024;
    private const int MaximumFeatureCount = 64;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    /// <summary>Parses and validates one complete part drawing intent.</summary>
    public static bool TryParse(
        string? json,
        out PartDrawingIntentInput? input,
        out string? error)
    {
        input = null;
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "partDrawingIntentJson-required";
            return false;
        }

        if (json.Length > MaximumJsonLength)
        {
            error = "partDrawingIntentJson-too-large";
            return false;
        }

        WireIntent? wire;
        try
        {
            wire = JsonSerializer.Deserialize<WireIntent>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            error = "partDrawingIntentJson-invalid-json";
            return false;
        }

        if (wire is null)
        {
            error = "partDrawingIntentJson-empty";
            return false;
        }

        if (!string.Equals(wire.SchemaVersion, Protocol.ProtocolSchema.CurrentVersion, StringComparison.Ordinal))
        {
            error = "partDrawingIntentJson-schemaVersion-unsupported";
            return false;
        }

        if (!TryReadPart(wire.Part, out PartDrawingIntentInput? part, out error)
            || !TryReadDrawing(wire.Drawing, part!, out PartDrawingIntentInput? complete, out error))
        {
            return false;
        }

        input = complete! with { SchemaVersion = wire.SchemaVersion! };
        return true;
    }

    private static bool TryReadPart(
        WirePart? wire,
        out PartDrawingIntentInput? input,
        out string? error)
    {
        input = null;
        error = null;
        if (wire is null)
        {
            error = "partDrawingIntentJson-part-required";
            return false;
        }

        if (!RequiredText(wire.DocumentId, "part.documentId", out error)
            || !RequiredText(wire.Configuration, "part.configuration", out error)
            || !RequiredText(wire.Path, "part.path", out error))
        {
            return false;
        }

        if (!PositiveFinite(wire.ExtrusionDepthMillimeters, out double extrusionDepth))
        {
            error = "partDrawingIntentJson-part-extrusionDepthMillimeters-invalid";
            return false;
        }

        if (wire.Profile is null || wire.Profile.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            error = "partDrawingIntentJson-part-profile-required";
            return false;
        }

        string profileJson = wire.Profile.Value.GetRawText();
        if (!SketchProfileMcpCodec.TryParse(profileJson, out _, out string? profileError))
        {
            error = $"partDrawingIntentJson-part-profile-{profileError ?? "invalid"}";
            return false;
        }

        if (wire.Features is null || wire.Features.Count > MaximumFeatureCount)
        {
            error = "partDrawingIntentJson-part-features-count-invalid";
            return false;
        }

        string? holeJson = null;
        string? slotJson = null;
        foreach (WireFeature feature in wire.Features)
        {
            if (!RequiredText(feature.Kind, "part.features.kind", out error))
            {
                return false;
            }

            if (feature.Payload is null || feature.Payload.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                error = "partDrawingIntentJson-feature-payload-required";
                return false;
            }

            string kind = feature.Kind!.Trim();
            if (kind.Equals("throughHolePattern", StringComparison.OrdinalIgnoreCase)
                || kind.Equals("holePattern", StringComparison.OrdinalIgnoreCase))
            {
                if (holeJson is not null)
                {
                    error = "partDrawingIntentJson-duplicate-throughHolePattern";
                    return false;
                }

                holeJson = feature.Payload.Value.GetRawText();
            }
            else if (kind.Equals("slot", StringComparison.OrdinalIgnoreCase)
                || kind.Equals("obroundSlot", StringComparison.OrdinalIgnoreCase))
            {
                if (slotJson is not null)
                {
                    error = "partDrawingIntentJson-duplicate-slot";
                    return false;
                }

                slotJson = feature.Payload.Value.GetRawText();
            }
            else
            {
                error = $"partDrawingIntentJson-feature-kind-unsupported:{kind}";
                return false;
            }
        }

        input = new PartDrawingIntentInput
        {
            SchemaVersion = Protocol.ProtocolSchema.CurrentVersion,
            DocumentId = wire.DocumentId!.Trim(),
            Configuration = wire.Configuration!.Trim(),
            PartPath = wire.Path!.Trim(),
            ExtrusionDepthMillimeters = extrusionDepth,
            InitialSketchProfileJson = profileJson,
            ThroughHolePatternJson = holeJson,
            SlotCutJson = slotJson,
            ScaleDenominator = 1,
            DrawingDocumentId = string.Empty,
            DrawingPath = string.Empty,
            PdfPath = string.Empty,
        };
        return true;
    }

    private static bool TryReadDrawing(
        WireDrawing? wire,
        PartDrawingIntentInput part,
        out PartDrawingIntentInput? input,
        out string? error)
    {
        input = null;
        error = null;
        if (wire is null)
        {
            error = "partDrawingIntentJson-drawing-required";
            return false;
        }

        if (!RequiredText(wire.DocumentId, "drawing.documentId", out error)
            || !RequiredText(wire.Path, "drawing.path", out error)
            || !RequiredText(wire.PdfPath, "drawing.pdfPath", out error))
        {
            return false;
        }

        int scale = wire.ScaleDenominator ?? 1;
        if (scale <= 0)
        {
            error = "partDrawingIntentJson-drawing-scaleDenominator-invalid";
            return false;
        }

        input = part with
        {
            DrawingDocumentId = wire.DocumentId!.Trim(),
            DrawingPath = wire.Path!.Trim(),
            PdfPath = wire.PdfPath!.Trim(),
            ScaleDenominator = scale,
            SurfaceFinishJson = Raw(wire.SurfaceFinish),
            CenterMarkJson = Raw(wire.CenterMarks),
            DetailViewJson = Raw(wire.DetailView),
        };
        return true;
    }

    private static string? Raw(JsonElement? value) => value is null || value.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
        ? null
        : value.Value.GetRawText();

    private static bool RequiredText(string? value, string field, out string? error)
    {
        error = string.IsNullOrWhiteSpace(value) ? $"partDrawingIntentJson-{field}-required" : null;
        return error is null;
    }

    private static bool PositiveFinite(double? value, out double result)
    {
        result = value.GetValueOrDefault();
        return value.HasValue && double.IsFinite(result) && result > 0d;
    }

    private sealed class WireIntent
    {
        public string? SchemaVersion { get; init; }

        public WirePart? Part { get; init; }

        public WireDrawing? Drawing { get; init; }
    }

    private sealed class WirePart
    {
        public string? DocumentId { get; init; }

        public string? Configuration { get; init; }

        public string? Path { get; init; }

        public JsonElement? Profile { get; init; }

        public double? ExtrusionDepthMillimeters { get; init; }

        public List<WireFeature>? Features { get; init; } = [];
    }

    private sealed class WireFeature
    {
        public string? Kind { get; init; }

        public JsonElement? Payload { get; init; }
    }

    private sealed class WireDrawing
    {
        public string? DocumentId { get; init; }

        public string? Path { get; init; }

        public string? PdfPath { get; init; }

        public int? ScaleDenominator { get; init; }

        public JsonElement? SurfaceFinish { get; init; }

        public JsonElement? CenterMarks { get; init; }

        public JsonElement? DetailView { get; init; }
    }
}
