using System.Collections.Immutable;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.RuleEngine;

/// <summary>
/// Creates the repository's public GB drawing-rule profile from metadata-only standard references.
/// 根据只含 metadata 的标准引用创建仓库公开的 GB 制图规则 profile。
/// </summary>
/// <remarks>
/// The catalog intentionally does not contain copied standard prose, tables or scanned pages. Values here are
/// deterministic compiler defaults and must be overridden by an enterprise/customer pack when a project contract
/// requires a more specific clause or tolerance table. This keeps licensing boundaries explicit.
/// 目录刻意不包含标准正文、表格或扫描页。这里的值是确定性的 compiler 默认值；当项目合同需要更具体的条款或
/// 公差表时，必须由 enterprise/customer pack 覆盖，从而保持许可边界清晰。
/// </remarks>
public static class StandardRulePackCatalog
{
    private static readonly DateTimeOffset MetadataRetrievedAtUtc =
        new(2026, 9, 18, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Creates the baseline GB RulePack used by the deterministic drawing compiler.
    /// 创建确定性 drawing compiler 使用的 GB baseline RulePack。
    /// </summary>
    public static DrawingRulePack CreateGbRulePack()
    {
        ImmutableArray<RulePackSourceMetadata> sources = CreateGbSourceMetadata();

        return new DrawingRulePack
        {
            PackId = "GB.rulepack",
            Layer = RulePackLayer.Standard,
            Source = sources[0],
            RelatedSources = sources,
            Values = new DrawingRuleValues
            {
                // GB manufacturing documentation commonly starts from first-angle projection; this is a compiler
                // default, not a claim that every customer drawing is first-angle.
                // GB 制造图纸通常以第一角投影为起点；这里是 compiler 默认值，不声称每张客户图纸都必须如此。
                ProjectionMethod = DrawingProjectionMethod.FirstAngle,
                AllowedSheetSizes =
                [
                    new SheetSizeRule { Name = "A4", Width = Length.FromMillimeters(297d), Height = Length.FromMillimeters(210d) },
                    new SheetSizeRule { Name = "A3", Width = Length.FromMillimeters(420d), Height = Length.FromMillimeters(297d) },
                    new SheetSizeRule { Name = "A2", Width = Length.FromMillimeters(594d), Height = Length.FromMillimeters(420d) },
                    new SheetSizeRule { Name = "A1", Width = Length.FromMillimeters(841d), Height = Length.FromMillimeters(594d) },
                    new SheetSizeRule { Name = "A0", Width = Length.FromMillimeters(1189d), Height = Length.FromMillimeters(841d) },
                ],
                AllowedScaleDenominators =
                [
                    ScaleDenominator.FromValue(1d),
                    ScaleDenominator.FromValue(2d),
                    ScaleDenominator.FromValue(2.5d),
                    ScaleDenominator.FromValue(4d),
                    ScaleDenominator.FromValue(5d),
                    ScaleDenominator.FromValue(10d),
                    ScaleDenominator.FromValue(20d),
                ],
                DefaultTextHeight = Length.FromMillimeters(3.5d),
                DimensionSpacing = Length.FromMillimeters(8d),
                ViewSpacing = Length.FromMillimeters(20d),
                // Derived from the local GB/T 14689 catalogue policy record; the official standard text is not copied.
                // 来源于本地 GB/T 14689 目录 policy 记录；仓库不复制标准正文。
                SheetMargin = Length.FromMillimeters(10d),
                SectionLabelPrefix = "A-",
                DetailLabelPrefix = "DETAIL ",
                RepeatedFeatureNotation = new RepeatedFeatureNotationRule
                {
                    // Keep the baseline notation aligned with the Chinese-language engineering deliverable. The
                    // templates are metadata for future compiler stages; no standard prose is redistributed here.
                    // baseline notation 与中文工程交付保持一致；这些是未来 compiler 使用的 metadata，不复制标准正文。
                    QuantitySizeTemplate = "{quantity}×⌀{size}",
                    UniformDistributionText = "均布",
                    LinearPitchTemplate = "孔距{pitch}",
                    CircularPitchTemplate = "节圆直径{pcd}，{angle}",
                },
                ReservedZones =
                [
                    new ReservedZoneRule
                    {
                        ZoneId = "title-block",
                        Left = Length.FromMillimeters(180d),
                        Bottom = Length.FromMillimeters(0d),
                        Width = Length.FromMillimeters(117d),
                        Height = Length.FromMillimeters(30d),
                    },
                ],
                // A neutral baseline only. General tolerance tables must be provided by a governed overlay.
                // 这里只是中性 baseline；一般公差表必须由受治理的 overlay 提供。
                DefaultGeneralTolerance = new GeneralToleranceRule
                {
                    Deviations = Tolerance.Symmetric(Length.FromMillimeters(0.2d)),
                },
            },
        };
    }

    /// <summary>
    /// Returns official catalogue metadata for the three standards currently in the project scope.
    /// 返回项目范围内三个标准的官方目录 metadata。
    /// </summary>
    public static ImmutableArray<RulePackSourceMetadata> CreateGbSourceMetadata() =>
    [
        Source(
            "GB/T 1800.1-2020",
            "https://openstd.samr.gov.cn/bzgk/std/newGbInfo?hcno=B2EA3A6454B903DCF466A0CE16F2ED26",
            "current-catalogue-metadata-verified; compiler subset derived"),
        Source(
            "GB/T 1804-2000",
            "https://openstd.samr.gov.cn/bzgk/std/newGbInfo?hcno=1BF8CFBC644315488F68433EEC2F9D58",
            "current-catalogue-metadata-verified; compiler subset derived"),
        Source(
            "GB/T 1182-2018",
            "https://openstd.samr.gov.cn/bzgk/std/newGbInfo?hcno=C87A687A21B36E2F2D3A09BDF03DCA01",
            "current-catalogue-metadata-verified; compiler subset derived"),
    ];

    private static RulePackSourceMetadata Source(string sourceId, string sourceUrl, string status) => new()
    {
        SourceId = sourceId,
        SourceUrl = sourceUrl,
        Status = status,
        RetrievedAtUtc = MetadataRetrievedAtUtc,
        LicenseNote = "Official catalogue metadata and derived compiler rules only; standard text is not redistributed.",
    };
}
