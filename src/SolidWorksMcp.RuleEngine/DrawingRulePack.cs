using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.RuleEngine;

/// <summary>Projection policy stored as data in a versioned drawing RulePack.</summary>
/// <summary>版本化 drawing RulePack 中以数据形式保存的投影策略。</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DrawingProjectionMethod
{
    /// <summary>First-angle projection.</summary>
    FirstAngle,

    /// <summary>Third-angle projection.</summary>
    ThirdAngle,
}

/// <summary>Override precedence in the standard → enterprise → customer hierarchy.</summary>
/// <summary>standard → enterprise → customer 覆盖层级中的优先级。</summary>
public enum RulePackLayer
{
    Standard,
    Enterprise,
    Customer,
}

/// <summary>Severity of a RulePack validation or merge diagnostic.</summary>
/// <summary>RulePack 校验或合并诊断的严重级别。</summary>
public enum RulePackDiagnosticSeverity
{
    Warning,
    Error,
}

/// <summary>
/// Public metadata about a standard or company rule source. It deliberately stores provenance, not copyrighted text.
/// 标准或企业规则来源的公开元数据；这里只保存 provenance，不保存受版权保护的标准全文。
/// </summary>
public sealed record RulePackSourceMetadata
{
    public required string SourceId { get; init; }

    public required string SourceUrl { get; init; }

    public required string Status { get; init; }

    public required DateTimeOffset RetrievedAtUtc { get; init; }

    /// <summary>Short redistribution/licensing note, never the source document itself.</summary>
    /// <summary>简短的再分发/许可说明，绝不是源文件正文。</summary>
    public required string LicenseNote { get; init; }
}

/// <summary>One allowed paper sheet size expressed in canonical millimetres.</summary>
/// <summary>以标准毫米单位表达的一个允许图幅。</summary>
public sealed record SheetSizeRule
{
    public required string Name { get; init; }

    public required Length Width { get; init; }

    public required Length Height { get; init; }
}

/// <summary>Deterministic notation policy for repeated manufacturing features.</summary>
/// <summary>重复制造特征的确定性标注表达策略。</summary>
public sealed record RepeatedFeatureNotationRule
{
    /// <summary>Quantity/size template, for example <c>N-{size}</c>.</summary>
    public required string QuantitySizeTemplate { get; init; }

    /// <summary>Template used when the group is uniformly distributed.</summary>
    public required string UniformDistributionText { get; init; }

    /// <summary>Template used to express a linear pitch.</summary>
    public required string LinearPitchTemplate { get; init; }

    /// <summary>Template used to express a circular pitch circle/angle.</summary>
    public required string CircularPitchTemplate { get; init; }
}

/// <summary>Reserved title-block/BOM area in paper space.</summary>
/// <summary>纸空间中的标题栏/BOM 保留区域。</summary>
public sealed record ReservedZoneRule
{
    public required string ZoneId { get; init; }

    public required Length Left { get; init; }

    public required Length Bottom { get; init; }

    public required Length Width { get; init; }

    public required Length Height { get; init; }
}

/// <summary>RulePack-provided general tolerance expressed as asymmetric deviations in millimetres.</summary>
/// <summary>RulePack 提供的一般公差，以毫米非对称偏差表示。</summary>
public sealed record GeneralToleranceRule
{
    public required Tolerance Deviations { get; init; }
}

/// <summary>
/// Typed drawing rules. A compiler consumes this object instead of scattering standard constants through services.
/// 有类型的制图规则；compiler 消费此对象，不在各个 service 中散落标准常量。
/// </summary>
public sealed record DrawingRuleValues
{
    public required DrawingProjectionMethod ProjectionMethod { get; init; }

    public required ImmutableArray<SheetSizeRule> AllowedSheetSizes { get; init; }

    /// <summary>Scale denominators for a 1:n drawing scale.</summary>
    /// <summary>1:n 工程图比例中的 n。</summary>
    public required ImmutableArray<ScaleDenominator> AllowedScaleDenominators { get; init; }

    public required Length DefaultTextHeight { get; init; }

    public required Length DimensionSpacing { get; init; }

    public required Length ViewSpacing { get; init; }

    /// <summary>Minimum clear sheet margin from the printable border in millimetres.</summary>
    /// <summary>距可打印边界的最小图纸留边，单位毫米。</summary>
    public required Length SheetMargin { get; init; }

    public required string SectionLabelPrefix { get; init; }

    public required string DetailLabelPrefix { get; init; }

    public required RepeatedFeatureNotationRule RepeatedFeatureNotation { get; init; }

    public required ImmutableArray<ReservedZoneRule> ReservedZones { get; init; }

    public required GeneralToleranceRule DefaultGeneralTolerance { get; init; }

    /// <summary>Returns validation diagnostics without throwing, so MCP can explain invalid data.</summary>
    /// <summary>返回校验诊断而不抛异常，使 MCP 能解释无效规则数据。</summary>
    internal ImmutableArray<RulePackDiagnostic> Validate(string packId)
    {
        var diagnostics = ImmutableArray.CreateBuilder<RulePackDiagnostic>();
        string prefix = string.IsNullOrWhiteSpace(packId) ? "<unknown>" : packId.Trim();

        if (AllowedSheetSizes.IsDefaultOrEmpty)
        {
            Add("rule.sheet-sizes.empty", "AllowedSheetSizes", "At least one sheet size is required.");
        }
        else
        {
            if (AllowedSheetSizes.Any(value => value is null)
                || AllowedSheetSizes.Select(value => value?.Name).Distinct(StringComparer.Ordinal).Count() != AllowedSheetSizes.Length)
            {
                Add("rule.sheet-sizes.duplicate", "AllowedSheetSizes", "Sheet size names must be unique.");
            }

            foreach (SheetSizeRule sheet in AllowedSheetSizes)
            {
                if (sheet is null
                    || string.IsNullOrWhiteSpace(sheet.Name)
                    || !IsPositive(sheet.Width)
                    || !IsPositive(sheet.Height))
                {
                    Add("rule.sheet-size.invalid", "AllowedSheetSizes", "Sheet names and dimensions must be non-empty and positive.");
                }
            }
        }

        if (AllowedScaleDenominators.IsDefaultOrEmpty
            || AllowedScaleDenominators.Any(value => !double.IsFinite(value.Value) || value.Value <= 0d)
            || AllowedScaleDenominators.Distinct().Count() != AllowedScaleDenominators.Length)
        {
            Add("rule.scales.invalid", "AllowedScaleDenominators", "Scale denominators must be finite, positive and unique.");
        }

        if (!IsPositive(DefaultTextHeight) || !IsPositive(DimensionSpacing) || !IsPositive(ViewSpacing) || !IsPositive(SheetMargin))
        {
            Add("rule.spacing.invalid", "DrawingRuleValues", "Text height and spacing rules must be finite and positive.");
        }

        if (string.IsNullOrWhiteSpace(SectionLabelPrefix))
        {
            Add("rule.section-prefix.empty", "SectionLabelPrefix", "Section label prefix is required.");
        }

        if (string.IsNullOrWhiteSpace(DetailLabelPrefix))
        {
            Add("rule.detail-prefix.empty", "DetailLabelPrefix", "Detail label prefix is required.");
        }

        if (RepeatedFeatureNotation is null
            || string.IsNullOrWhiteSpace(RepeatedFeatureNotation.QuantitySizeTemplate)
            || string.IsNullOrWhiteSpace(RepeatedFeatureNotation.UniformDistributionText)
            || string.IsNullOrWhiteSpace(RepeatedFeatureNotation.LinearPitchTemplate)
            || string.IsNullOrWhiteSpace(RepeatedFeatureNotation.CircularPitchTemplate))
        {
            Add("rule.repeated-feature.invalid", "RepeatedFeatureNotation", "Every repeated-feature notation template is required.");
        }

        if (ReservedZones.IsDefault)
        {
            Add("rule.reserved-zones.invalid", "ReservedZones", "ReservedZones must be an initialized collection.");
        }
        else if (ReservedZones.Any(value => value is null)
            || ReservedZones.Select(value => value?.ZoneId).Distinct(StringComparer.Ordinal).Count() != ReservedZones.Length
            || ReservedZones.Any(zone => zone is null
                || string.IsNullOrWhiteSpace(zone.ZoneId)
                || !IsNonNegative(zone.Left)
                || !IsNonNegative(zone.Bottom)
                || !IsPositive(zone.Width)
                || !IsPositive(zone.Height)))
        {
            Add("rule.reserved-zones.invalid", "ReservedZones", "Reserved zone identities must be unique and dimensions must be valid.");
        }

        if (DefaultGeneralTolerance is null
            || !double.IsFinite(DefaultGeneralTolerance.Deviations.LowerDeviation.Millimeters)
            || !double.IsFinite(DefaultGeneralTolerance.Deviations.UpperDeviation.Millimeters)
            || DefaultGeneralTolerance.Deviations.LowerDeviation.Millimeters > 0d
            || DefaultGeneralTolerance.Deviations.UpperDeviation.Millimeters < 0d)
        {
            Add("rule.general-tolerance.invalid", "DefaultGeneralTolerance", "General tolerance deviations must be finite with lower <= 0 <= upper.");
        }

        return [.. diagnostics];

        void Add(string code, string path, string message) => diagnostics.Add(
            new RulePackDiagnostic(code, RulePackDiagnosticSeverity.Error, path, message, prefix));
    }

    private static bool IsPositive(Length value) => double.IsFinite(value.Millimeters) && value.Millimeters > 0d;

    private static bool IsNonNegative(Length value) => double.IsFinite(value.Millimeters) && value.Millimeters >= 0d;
}

/// <summary>One versioned standard/enterprise/customer RulePack.</summary>
/// <summary>一个版本化的 standard/enterprise/customer RulePack。</summary>
public sealed record DrawingRulePack
{
    public const string CurrentSchemaVersion = "1.0";

    public string SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required string PackId { get; init; }

    public required RulePackLayer Layer { get; init; }

    public required RulePackSourceMetadata Source { get; init; }

    /// <summary>
    /// Additional catalogue records that contribute metadata to this pack without redistributing source-standard text.
    /// 附加的标准目录记录；只保存来源 metadata，不把标准正文再分发到仓库。
    /// </summary>
    public ImmutableArray<RulePackSourceMetadata> RelatedSources { get; init; } = [];

    public required DrawingRuleValues Values { get; init; }

    /// <summary>Serializes rule data and metadata, never the full external standard text.</summary>
    /// <summary>序列化规则数据和 metadata，不序列化外部标准全文。</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Loads a versioned RulePack; semantic validation happens during resolution.</summary>
    /// <summary>加载 versioned RulePack；语义校验在 resolve 阶段执行。</summary>
    public static DrawingRulePack FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<DrawingRulePack>(json, JsonOptions)
            ?? throw new JsonException("RulePack JSON produced no object.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };
}

/// <summary>Typed overlay applied above a base RulePack.</summary>
/// <summary>应用在基础 RulePack 之上的有类型覆盖层。</summary>
public sealed record DrawingRulePackOverride
{
    public const string CurrentSchemaVersion = DrawingRulePack.CurrentSchemaVersion;

    public string SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required string PackId { get; init; }

    public required RulePackLayer Layer { get; init; }

    public required RulePackSourceMetadata Source { get; init; }

    public DrawingProjectionMethod? ProjectionMethod { get; init; }

    public ImmutableArray<SheetSizeRule>? AllowedSheetSizes { get; init; }

    public ImmutableArray<ScaleDenominator>? AllowedScaleDenominators { get; init; }

    public Length? DefaultTextHeight { get; init; }

    public Length? DimensionSpacing { get; init; }

    public Length? ViewSpacing { get; init; }

    public Length? SheetMargin { get; init; }

    public string? SectionLabelPrefix { get; init; }

    public string? DetailLabelPrefix { get; init; }

    public RepeatedFeatureNotationRule? RepeatedFeatureNotation { get; init; }

    public ImmutableArray<ReservedZoneRule>? ReservedZones { get; init; }

    public GeneralToleranceRule? DefaultGeneralTolerance { get; init; }
}

/// <summary>Structured diagnostic emitted while validating or merging RulePacks.</summary>
/// <summary>校验或合并 RulePack 时输出的结构化诊断。</summary>
public sealed record RulePackDiagnostic(
    string Code,
    RulePackDiagnosticSeverity Severity,
    string RulePath,
    string Message,
    string PackId);

/// <summary>Provenance of one final RulePack field.</summary>
/// <summary>最终 RulePack 某个字段的来源 provenance。</summary>
public sealed record RuleValueProvenance(
    string RulePath,
    string PackId,
    RulePackLayer Layer,
    RulePackSourceMetadata Source,
    string CanonicalValue);

/// <summary>Explain output for one resolved rule value.</summary>
/// <summary>某个 resolved rule value 的 explain 输出。</summary>
public sealed record RulePackExplanation(
    string RulePath,
    string Value,
    string PackId,
    RulePackLayer Layer,
    RulePackSourceMetadata Source);

/// <summary>Resolved RulePack plus field-level provenance.</summary>
/// <summary>带字段级 provenance 的最终 RulePack。</summary>
public sealed record ResolvedDrawingRulePack
{
    public required string PackId { get; init; }

    public required string SchemaVersion { get; init; }

    public required DrawingRuleValues Values { get; init; }

    /// <summary>Preserves the standard catalogue metadata through resolution.</summary>
    /// <summary>resolve 后保留标准目录 metadata，便于审计和 explain。</summary>
    public ImmutableArray<RulePackSourceMetadata> RelatedSources { get; init; } = [];

    public required ImmutableDictionary<string, RuleValueProvenance> Provenance { get; init; }

    /// <summary>Explains the final value and the exact source layer for one rule path.</summary>
    /// <summary>解释某条规则的最终值及其确切来源层级。</summary>
    public bool TryExplain(string rulePath, out RulePackExplanation? explanation)
    {
        if (string.IsNullOrWhiteSpace(rulePath)
            || !Provenance.TryGetValue(rulePath.Trim(), out RuleValueProvenance? source))
        {
            explanation = null;
            return false;
        }

        explanation = new RulePackExplanation(
            source.RulePath,
            source.CanonicalValue,
            source.PackId,
            source.Layer,
            source.Source);
        return true;
    }
}

/// <summary>Result of standard/enterprise/customer RulePack resolution.</summary>
/// <summary>standard/enterprise/customer RulePack resolve 的结果。</summary>
public sealed record RulePackResolutionResult
{
    public required ResolvedDrawingRulePack? Pack { get; init; }

    public required ImmutableArray<RulePackDiagnostic> Diagnostics { get; init; }

    public bool IsValid => Pack is not null
        && Diagnostics.All(diagnostic => diagnostic.Severity is not RulePackDiagnosticSeverity.Error);
}

/// <summary>
/// Deterministic resolver for versioned standard, enterprise and customer drawing rules.
/// versioned standard、enterprise、customer 制图规则的确定性 resolver。
/// </summary>
public static class DrawingRulePackResolver
{
    private static readonly string[] RulePaths =
    [
        "projection.method",
        "sheet.sizes",
        "scale.denominators",
        "text.default-height",
        "dimension.spacing",
        "view.spacing",
        "sheet.margin",
        "section.label-prefix",
        "detail.label-prefix",
        "repeated-feature.notation",
        "sheet.reserved-zones",
        "tolerance.general",
    ];

    /// <summary>Resolves overlays in fixed layer/order and fails closed on invalid or conflicting data.</summary>
    /// <summary>按固定层级/顺序 resolve overlays；遇到无效或冲突数据时 fail closed。</summary>
    public static RulePackResolutionResult Resolve(
        DrawingRulePack standard,
        IEnumerable<DrawingRulePackOverride>? overrides = null)
    {
        ArgumentNullException.ThrowIfNull(standard);
        var diagnostics = ImmutableArray.CreateBuilder<RulePackDiagnostic>();
        ValidatePackHeader(standard.PackId, standard.SchemaVersion, standard.Layer, standard.Source, diagnostics, allowOnlyStandard: true);
        if (standard.Values is null)
        {
            diagnostics.Add(new RulePackDiagnostic(
                "rule.values.missing",
                RulePackDiagnosticSeverity.Error,
                "Values",
                "The standard RulePack must contain typed rule values.",
                standard.PackId));
        }
        else
        {
            diagnostics.AddRange(standard.Values.Validate(standard.PackId));
        }

        ValidateRelatedSources(standard.PackId, standard.RelatedSources, diagnostics);

        // Do not attempt canonicalization after a malformed base header/value set. This is important for JSON-loaded
        // data where required reference properties can still be null at runtime; invalid data must return diagnostics,
        // not crash the MCP host before it can explain the problem.
        // 对 JSON 加载数据也不在 malformed base 后继续 canonicalize，避免 MCP host 因运行时 null 直接崩溃。
        if (standard.Values is null
            || standard.Source is null
            || diagnostics.Any(diagnostic => diagnostic.Severity is RulePackDiagnosticSeverity.Error))
        {
            return new RulePackResolutionResult
            {
                Pack = null,
                Diagnostics = [.. diagnostics],
            };
        }

        DrawingRuleValues values = standard.Values;
        var provenance = new Dictionary<string, RuleValueProvenance>(StringComparer.Ordinal);
        foreach (string path in RulePaths)
        {
            provenance[path] = new RuleValueProvenance(
                path,
                standard.PackId,
                standard.Layer,
                standard.Source,
                CanonicalValue(path, values));
        }

        ImmutableArray<DrawingRulePackOverride> ordered = [.. (overrides ?? [])
            .Where(value => value is not null)
            .OrderBy(value => value.Layer)
            .ThenBy(value => value.PackId, StringComparer.Ordinal)];
        var packIds = new HashSet<string>(StringComparer.Ordinal) { standard.PackId };
        foreach (DrawingRulePackOverride patch in ordered)
        {
            int diagnosticCountBeforeHeader = diagnostics.Count;
            ValidatePackHeader(patch.PackId, patch.SchemaVersion, patch.Layer, patch.Source, diagnostics, allowOnlyStandard: false);
            if (diagnostics.Skip(diagnosticCountBeforeHeader).Any(diagnostic => diagnostic.Severity is RulePackDiagnosticSeverity.Error))
            {
                continue;
            }

            if (!packIds.Add(patch.PackId))
            {
                diagnostics.Add(new RulePackDiagnostic(
                    "rulepack.id.duplicate",
                    RulePackDiagnosticSeverity.Error,
                    "PackId",
                    "RulePack identifiers must be unique within one resolution.",
                    patch.PackId));
                continue;
            }

            values = ApplyPatch(patch, values, provenance, diagnostics);
        }

        diagnostics.AddRange(values.Validate(standard.PackId));
        ImmutableArray<RulePackDiagnostic> finalDiagnostics = [.. diagnostics];
        if (finalDiagnostics.Any(diagnostic => diagnostic.Severity is RulePackDiagnosticSeverity.Error))
        {
            return new RulePackResolutionResult
            {
                Pack = null,
                Diagnostics = finalDiagnostics,
            };
        }

        return new RulePackResolutionResult
        {
            Pack = new ResolvedDrawingRulePack
            {
                PackId = ordered.Length == 0 ? standard.PackId : ordered[^1].PackId,
                SchemaVersion = standard.SchemaVersion,
                Values = values,
                RelatedSources = standard.RelatedSources,
                Provenance = provenance.ToImmutableDictionary(StringComparer.Ordinal),
            },
            Diagnostics = finalDiagnostics,
        };
    }

    private static DrawingRuleValues ApplyPatch(
        DrawingRulePackOverride patch,
        DrawingRuleValues values,
        Dictionary<string, RuleValueProvenance> provenance,
        ImmutableArray<RulePackDiagnostic>.Builder diagnostics)
    {
        DrawingRuleValues current = values;

        void Apply(string path, object candidate, Action update)
        {
            string canonical = CanonicalValue(path, candidate);
            if (provenance.TryGetValue(path, out RuleValueProvenance? previous)
                && previous.Layer == patch.Layer)
            {
                if (!previous.CanonicalValue.Equals(canonical, StringComparison.Ordinal))
                {
                    diagnostics.Add(new RulePackDiagnostic(
                        "rulepack.override.conflict",
                        RulePackDiagnosticSeverity.Error,
                        path,
                        $"Two {patch.Layer} RulePacks set different values for the same rule.",
                        patch.PackId));
                }

                return;
            }

            update();
            provenance[path] = new RuleValueProvenance(path, patch.PackId, patch.Layer, patch.Source, canonical);
        }

        if (patch.ProjectionMethod is DrawingProjectionMethod projection)
        {
            Apply("projection.method", projection, () => current = current with { ProjectionMethod = projection });
        }

        if (patch.AllowedSheetSizes is ImmutableArray<SheetSizeRule> sheets)
        {
            Apply("sheet.sizes", sheets, () => current = current with { AllowedSheetSizes = sheets });
        }

        if (patch.AllowedScaleDenominators is ImmutableArray<ScaleDenominator> scales)
        {
            Apply("scale.denominators", scales, () => current = current with { AllowedScaleDenominators = scales });
        }

        if (patch.DefaultTextHeight is Length textHeight)
        {
            Apply("text.default-height", textHeight, () => current = current with { DefaultTextHeight = textHeight });
        }

        if (patch.DimensionSpacing is Length dimensionSpacing)
        {
            Apply("dimension.spacing", dimensionSpacing, () => current = current with { DimensionSpacing = dimensionSpacing });
        }

        if (patch.ViewSpacing is Length viewSpacing)
        {
            Apply("view.spacing", viewSpacing, () => current = current with { ViewSpacing = viewSpacing });
        }

        if (patch.SheetMargin is Length sheetMargin)
        {
            Apply("sheet.margin", sheetMargin, () => current = current with { SheetMargin = sheetMargin });
        }

        if (patch.SectionLabelPrefix is not null)
        {
            Apply("section.label-prefix", patch.SectionLabelPrefix, () => current = current with { SectionLabelPrefix = patch.SectionLabelPrefix });
        }

        if (patch.DetailLabelPrefix is not null)
        {
            Apply("detail.label-prefix", patch.DetailLabelPrefix, () => current = current with { DetailLabelPrefix = patch.DetailLabelPrefix });
        }

        if (patch.RepeatedFeatureNotation is not null)
        {
            Apply("repeated-feature.notation", patch.RepeatedFeatureNotation, () => current = current with { RepeatedFeatureNotation = patch.RepeatedFeatureNotation });
        }

        if (patch.ReservedZones is ImmutableArray<ReservedZoneRule> zones)
        {
            Apply("sheet.reserved-zones", zones, () => current = current with { ReservedZones = zones });
        }

        if (patch.DefaultGeneralTolerance is not null)
        {
            Apply("tolerance.general", patch.DefaultGeneralTolerance, () => current = current with { DefaultGeneralTolerance = patch.DefaultGeneralTolerance });
        }

        return current;
    }

    private static void ValidatePackHeader(
        string packId,
        string schemaVersion,
        RulePackLayer layer,
        RulePackSourceMetadata source,
        ImmutableArray<RulePackDiagnostic>.Builder diagnostics,
        bool allowOnlyStandard)
    {
        if (string.IsNullOrWhiteSpace(packId))
        {
            diagnostics.Add(new RulePackDiagnostic("rulepack.id.empty", RulePackDiagnosticSeverity.Error, "PackId", "PackId is required.", "<unknown>"));
        }

        if (!string.Equals(schemaVersion, DrawingRulePack.CurrentSchemaVersion, StringComparison.Ordinal))
        {
            diagnostics.Add(new RulePackDiagnostic("rulepack.schema.unsupported", RulePackDiagnosticSeverity.Error, "SchemaVersion", "Unsupported RulePack schema version.", packId));
        }

        if (allowOnlyStandard && layer is not RulePackLayer.Standard)
        {
            diagnostics.Add(new RulePackDiagnostic("rulepack.base-layer.invalid", RulePackDiagnosticSeverity.Error, "Layer", "The base RulePack must use the Standard layer.", packId));
        }

        if (!allowOnlyStandard && layer is RulePackLayer.Standard)
        {
            diagnostics.Add(new RulePackDiagnostic("rulepack.override-layer.invalid", RulePackDiagnosticSeverity.Error, "Layer", "An overlay must use Enterprise or Customer layer.", packId));
        }

        if (source is null
            || string.IsNullOrWhiteSpace(source.SourceId)
            || string.IsNullOrWhiteSpace(source.SourceUrl)
            || string.IsNullOrWhiteSpace(source.Status)
            || string.IsNullOrWhiteSpace(source.LicenseNote))
        {
            diagnostics.Add(new RulePackDiagnostic("rulepack.source.invalid", RulePackDiagnosticSeverity.Error, "Source", "RulePack source metadata is incomplete.", packId));
        }
    }

    private static void ValidateRelatedSources(
        string packId,
        ImmutableArray<RulePackSourceMetadata> relatedSources,
        ImmutableArray<RulePackDiagnostic>.Builder diagnostics)
    {
        if (relatedSources.IsDefault)
        {
            diagnostics.Add(new RulePackDiagnostic(
                "rulepack.related-sources.invalid",
                RulePackDiagnosticSeverity.Error,
                "RelatedSources",
                "RelatedSources must be an initialized collection.",
                packId));
            return;
        }

        if (relatedSources.Select(source => source?.SourceId).Where(id => id is not null).Distinct(StringComparer.Ordinal).Count()
            != relatedSources.Length)
        {
            diagnostics.Add(new RulePackDiagnostic(
                "rulepack.related-sources.duplicate",
                RulePackDiagnosticSeverity.Error,
                "RelatedSources",
                "Related source identifiers must be unique and every source must be complete.",
                packId));
        }

        foreach (RulePackSourceMetadata? source in relatedSources)
        {
            if (source is null
                || string.IsNullOrWhiteSpace(source.SourceId)
                || string.IsNullOrWhiteSpace(source.SourceUrl)
                || string.IsNullOrWhiteSpace(source.Status)
                || string.IsNullOrWhiteSpace(source.LicenseNote))
            {
                diagnostics.Add(new RulePackDiagnostic(
                    "rulepack.related-sources.invalid",
                    RulePackDiagnosticSeverity.Error,
                    "RelatedSources",
                    "Every related source must have an identifier, URL, status and license note.",
                    packId));
                break;
            }
        }
    }

    private static string CanonicalValue(string path, DrawingRuleValues values) => path switch
    {
        "projection.method" => values.ProjectionMethod.ToString(),
        "sheet.sizes" => CanonicalSheets(values.AllowedSheetSizes),
        "scale.denominators" => CanonicalDoubles(values.AllowedScaleDenominators),
        "text.default-height" => CanonicalLength(values.DefaultTextHeight),
        "dimension.spacing" => CanonicalLength(values.DimensionSpacing),
        "view.spacing" => CanonicalLength(values.ViewSpacing),
        "sheet.margin" => CanonicalLength(values.SheetMargin),
        "section.label-prefix" => values.SectionLabelPrefix,
        "detail.label-prefix" => values.DetailLabelPrefix,
        "repeated-feature.notation" => CanonicalNotation(values.RepeatedFeatureNotation),
        "sheet.reserved-zones" => CanonicalZones(values.ReservedZones),
        "tolerance.general" => CanonicalTolerance(values.DefaultGeneralTolerance),
        _ => throw new ArgumentOutOfRangeException(nameof(path), path, "Unknown RulePack path."),
    };

    private static string CanonicalValue(string path, object value) => path switch
    {
        "projection.method" => ((DrawingProjectionMethod)value).ToString(),
        "sheet.sizes" => CanonicalSheets((ImmutableArray<SheetSizeRule>)value),
        "scale.denominators" => CanonicalDoubles((ImmutableArray<ScaleDenominator>)value),
        "text.default-height" or "dimension.spacing" or "view.spacing" => CanonicalLength((Length)value),
        "sheet.margin" => CanonicalLength((Length)value),
        "section.label-prefix" or "detail.label-prefix" => (string)value,
        "repeated-feature.notation" => CanonicalNotation((RepeatedFeatureNotationRule)value),
        "sheet.reserved-zones" => CanonicalZones((ImmutableArray<ReservedZoneRule>)value),
        "tolerance.general" => CanonicalTolerance((GeneralToleranceRule)value),
        _ => throw new ArgumentOutOfRangeException(nameof(path), path, "Unknown RulePack path."),
    };

    private static string CanonicalLength(Length value) => value.Millimeters.ToString("G17", CultureInfo.InvariantCulture);

    private static string CanonicalDoubles(IEnumerable<ScaleDenominator> values) => string.Join(",", values.Select(value => value.Value.ToString("G17", CultureInfo.InvariantCulture)));

    private static string CanonicalSheets(IEnumerable<SheetSizeRule> values) => string.Join(";", values.Select(value =>
        $"{value.Name}:{CanonicalLength(value.Width)}x{CanonicalLength(value.Height)}"));

    private static string CanonicalNotation(RepeatedFeatureNotationRule value) =>
        string.Join("|", value.QuantitySizeTemplate, value.UniformDistributionText, value.LinearPitchTemplate, value.CircularPitchTemplate);

    private static string CanonicalZones(IEnumerable<ReservedZoneRule> values) => string.Join(";", values.Select(value =>
        $"{value.ZoneId}:{CanonicalLength(value.Left)},{CanonicalLength(value.Bottom)},{CanonicalLength(value.Width)},{CanonicalLength(value.Height)}"));

    private static string CanonicalTolerance(GeneralToleranceRule value) =>
        $"{value.Deviations.LowerDeviation.Millimeters.ToString("G17", CultureInfo.InvariantCulture)},{value.Deviations.UpperDeviation.Millimeters.ToString("G17", CultureInfo.InvariantCulture)}";
}
