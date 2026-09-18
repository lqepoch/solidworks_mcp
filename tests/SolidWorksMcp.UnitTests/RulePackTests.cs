using System.Collections.Immutable;
using SolidWorksMcp.Protocol;
using SolidWorksMcp.RuleEngine;

namespace SolidWorksMcp.UnitTests;

/// <summary>
/// Protects the versioned drawing-rule merge and provenance boundary.
/// 保护版本化制图规则合并与 provenance boundary。
/// </summary>
public sealed class RulePackTests
{
    [Fact]
    public void ResolvesEnterpriseAndCustomerOverridesWithFieldProvenance()
    {
        DrawingRulePack standard = CreateStandardPack();
        RulePackResolutionResult result = DrawingRulePackResolver.Resolve(
            standard,
            [
                new DrawingRulePackOverride
                {
                    PackId = "enterprise-mechanical",
                    Layer = RulePackLayer.Enterprise,
                    Source = Source("enterprise-source"),
                    ProjectionMethod = DrawingProjectionMethod.ThirdAngle,
                    DimensionSpacing = Length.FromMillimeters(4d),
                },
                new DrawingRulePackOverride
                {
                    PackId = "customer-line-a",
                    Layer = RulePackLayer.Customer,
                    Source = Source("customer-source"),
                    DimensionSpacing = Length.FromMillimeters(6d),
                    SectionLabelPrefix = "SEC-",
                },
            ]);

        Assert.True(result.IsValid);
        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Pack);
        Assert.Equal(DrawingProjectionMethod.ThirdAngle, result.Pack!.Values.ProjectionMethod);
        Assert.Equal(6d, result.Pack.Values.DimensionSpacing.Millimeters);
        Assert.Equal("SEC-", result.Pack.Values.SectionLabelPrefix);

        Assert.True(result.Pack.TryExplain("dimension.spacing", out RulePackExplanation? spacing));
        Assert.NotNull(spacing);
        Assert.Equal("customer-line-a", spacing!.PackId);
        Assert.Equal(RulePackLayer.Customer, spacing.Layer);
        Assert.Equal("customer-source", spacing.Source.SourceId);

        Assert.True(result.Pack.TryExplain("projection.method", out RulePackExplanation? projection));
        Assert.Equal("enterprise-mechanical", projection!.PackId);
        Assert.Equal(RulePackLayer.Enterprise, projection.Layer);
    }

    [Fact]
    public void SameLayerConflictingOverridesFailClosed()
    {
        RulePackResolutionResult result = DrawingRulePackResolver.Resolve(
            CreateStandardPack(),
            [
                new DrawingRulePackOverride
                {
                    PackId = "enterprise-a",
                    Layer = RulePackLayer.Enterprise,
                    Source = Source("enterprise-a-source"),
                    DimensionSpacing = Length.FromMillimeters(4d),
                },
                new DrawingRulePackOverride
                {
                    PackId = "enterprise-b",
                    Layer = RulePackLayer.Enterprise,
                    Source = Source("enterprise-b-source"),
                    DimensionSpacing = Length.FromMillimeters(5d),
                },
            ]);

        Assert.False(result.IsValid);
        Assert.Null(result.Pack);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "rulepack.override.conflict"
            && diagnostic.RulePath == "dimension.spacing");
    }

    [Fact]
    public void DifferentCustomerPacksProduceDeterministicRuleDifferences()
    {
        RulePackResolutionResult compact = DrawingRulePackResolver.Resolve(
            CreateStandardPack(),
            [
                new DrawingRulePackOverride
                {
                    PackId = "customer-compact",
                    Layer = RulePackLayer.Customer,
                    Source = Source("compact-source"),
                    DimensionSpacing = Length.FromMillimeters(4d),
                },
            ]);
        RulePackResolutionResult spacious = DrawingRulePackResolver.Resolve(
            CreateStandardPack(),
            [
                new DrawingRulePackOverride
                {
                    PackId = "customer-spacious",
                    Layer = RulePackLayer.Customer,
                    Source = Source("spacious-source"),
                    DimensionSpacing = Length.FromMillimeters(7d),
                },
            ]);

        Assert.True(compact.IsValid);
        Assert.True(spacious.IsValid);
        Assert.NotEqual(
            compact.Pack!.Values.DimensionSpacing,
            spacious.Pack!.Values.DimensionSpacing);
        Assert.True(compact.Pack.TryExplain("dimension.spacing", out RulePackExplanation? compactExplanation));
        Assert.True(spacious.Pack.TryExplain("dimension.spacing", out RulePackExplanation? spaciousExplanation));
        Assert.Equal("customer-compact", compactExplanation!.PackId);
        Assert.Equal("customer-spacious", spaciousExplanation!.PackId);
        Assert.NotEqual(compactExplanation.Value, spaciousExplanation.Value);
    }

    [Fact]
    public void InvalidBaseRulesProduceStructuredDiagnostics()
    {
        DrawingRulePack invalid = CreateStandardPack() with
        {
            Values = CreateStandardPack().Values with
            {
                AllowedSheetSizes = [],
                AllowedScaleDenominators = [default, default],
                DefaultGeneralTolerance = null!,
            },
        };

        RulePackResolutionResult result = DrawingRulePackResolver.Resolve(invalid);

        Assert.False(result.IsValid);
        Assert.Null(result.Pack);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "rule.sheet-sizes.empty");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "rule.scales.invalid");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "rule.general-tolerance.invalid");
    }

    [Fact]
    public void RulePackJsonRoundTripPreservesVersionAndSourceMetadata()
    {
        DrawingRulePack original = CreateStandardPack();

        var roundTrip = DrawingRulePack.FromJson(original.ToJson());
        RulePackResolutionResult result = DrawingRulePackResolver.Resolve(roundTrip);

        Assert.True(result.IsValid);
        Assert.Equal(original.SchemaVersion, roundTrip.SchemaVersion);
        Assert.Equal(original.PackId, roundTrip.PackId);
        Assert.Equal(original.Source.SourceId, roundTrip.Source.SourceId);
        Assert.Equal(original.ToJson(), roundTrip.ToJson());
    }

    [Fact]
    public void MissingTypedValuesFailClosedWithStructuredDiagnostic()
    {
        DrawingRulePack malformed = CreateStandardPack() with { Values = null! };

        RulePackResolutionResult result = DrawingRulePackResolver.Resolve(malformed);

        Assert.False(result.IsValid);
        Assert.Null(result.Pack);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "rule.values.missing");
    }

    [Fact]
    public void GbCatalogCarriesOfficialMetadataWithoutRedistributingStandardText()
    {
        DrawingRulePack pack = StandardRulePackCatalog.CreateGbRulePack();

        RulePackResolutionResult result = DrawingRulePackResolver.Resolve(pack);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Pack);
        Assert.Equal(3, pack.RelatedSources.Length);
        Assert.Contains(pack.RelatedSources, source => source.SourceId == "GB/T 1800.1-2020");
        Assert.Contains(pack.RelatedSources, source => source.SourceId == "GB/T 1804-2000");
        Assert.Contains(pack.RelatedSources, source => source.SourceId == "GB/T 1182-2018");
        Assert.Equal(pack.RelatedSources, result.Pack!.RelatedSources);
        Assert.DoesNotContain("全文", pack.ToJson(), StringComparison.Ordinal);
    }

    private static DrawingRulePack CreateStandardPack() => new()
    {
        PackId = "GB.rulepack",
        Layer = RulePackLayer.Standard,
        Source = Source("GB-T-verified-metadata"),
        Values = new DrawingRuleValues
        {
            ProjectionMethod = DrawingProjectionMethod.FirstAngle,
            AllowedSheetSizes =
            [
                new SheetSizeRule { Name = "A4", Width = Length.FromMillimeters(297d), Height = Length.FromMillimeters(210d) },
                new SheetSizeRule { Name = "A3", Width = Length.FromMillimeters(420d), Height = Length.FromMillimeters(297d) },
            ],
            AllowedScaleDenominators =
            [
                ScaleDenominator.FromValue(1d),
                ScaleDenominator.FromValue(2d),
                ScaleDenominator.FromValue(5d),
                ScaleDenominator.FromValue(10d),
            ],
            DefaultTextHeight = Length.FromMillimeters(3.5d),
            DimensionSpacing = Length.FromMillimeters(8d),
            ViewSpacing = Length.FromMillimeters(20d),
            SheetMargin = Length.FromMillimeters(10d),
            SectionLabelPrefix = "A-",
            DetailLabelPrefix = "DETAIL ",
            RepeatedFeatureNotation = new RepeatedFeatureNotationRule
            {
                QuantitySizeTemplate = "N-{size}",
                UniformDistributionText = "EQ SP",
                LinearPitchTemplate = "P={pitch}",
                CircularPitchTemplate = "PCD={pcd};{angle}",
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
            DefaultGeneralTolerance = new GeneralToleranceRule
            {
                Deviations = Tolerance.Symmetric(Length.FromMillimeters(0.2d)),
            },
        },
    };

    private static RulePackSourceMetadata Source(string sourceId) => new()
    {
        SourceId = sourceId,
        SourceUrl = $"https://example.invalid/rule-source/{sourceId}",
        Status = "metadata-reviewed",
        RetrievedAtUtc = new DateTimeOffset(2026, 9, 18, 0, 0, 0, TimeSpan.Zero),
        LicenseNote = "Metadata and derived rule values only; source standard text is not redistributed.",
    };
}
