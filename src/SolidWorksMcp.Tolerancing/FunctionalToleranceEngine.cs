using System.Collections.Immutable;
using System.Globalization;
using SolidWorksMcp.EngineeringModel;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Tolerancing;

/// <summary>
/// Analysis methods supported by the tolerance engine. Worst-case is the only method enabled by default; statistical
/// methods remain explicit opt-in so a drawing cannot silently change its safety basis.
/// 公差引擎支持的分析方法。默认只启用 Worst-Case；统计方法必须显式 opt-in，避免工程图悄悄改变安全依据。
/// </summary>
public enum ToleranceAnalysisMethod
{
    /// <summary>Deterministic arithmetic stack-up using the adverse limit of every term.</summary>
    /// <summary>对每个项采用不利极限的确定性算术公差链。</summary>
    WorstCase,

    /// <summary>Root-sum-square analysis; not enabled by the default release policy.</summary>
    /// <summary>均方根分析；默认 release policy 不启用。</summary>
    Rss,

    /// <summary>Monte-Carlo analysis; not enabled by the default release policy.</summary>
    /// <summary>Monte-Carlo 分析；默认 release policy 不启用。</summary>
    MonteCarlo,
}

/// <summary>Signed direction of one term in a one-dimensional stack-up.</summary>
/// <summary>一维公差链中单个项的有符号方向。</summary>
public enum ToleranceStackDirection
{
    /// <summary>The term increases the resulting functional dimension.</summary>
    /// <summary>该项增加最终功能尺寸。</summary>
    Add,

    /// <summary>The term decreases the resulting functional dimension.</summary>
    /// <summary>该项减少最终功能尺寸。</summary>
    Subtract,
}

/// <summary>Functional meaning of the stack-up result.</summary>
/// <summary>公差链结果的功能语义。</summary>
public enum ToleranceConstraintKind
{
    /// <summary>The result is the available positive clearance.</summary>
    /// <summary>结果表示可用的正间隙。</summary>
    Clearance,

    /// <summary>The result is the maximum permitted size of a mating part.</summary>
    /// <summary>结果表示配合零件允许的最大尺寸。</summary>
    MaximumAllowedPartSize,

    /// <summary>The result is the minimum required positive installation gap.</summary>
    /// <summary>结果表示必须保证的最小安装间隙。</summary>
    MinimumRequiredClearance,
}

/// <summary>Release significance of a functional dimension.</summary>
/// <summary>功能尺寸的 release 重要性。</summary>
public enum ToleranceCriticality
{
    Normal,
    Important,
    Critical,
}

/// <summary>Result state used by QA and the drawing release gate.</summary>
/// <summary>QA 与工程图 release gate 使用的结果状态。</summary>
public enum ToleranceAnalysisStatus
{
    Pass,
    Warning,
    ReviewRequired,
    Blocking,
}

/// <summary>
/// One explicit term in a tolerance stack-up. The term keeps nominal, deviations and provenance together so an
/// explanation can trace every contribution without relying on a drawing screenshot or a PDF text match.
/// 公差链中的一个显式项；nominal、偏差和 provenance 保持在一起，使 explain 不依赖截图或 PDF 文本匹配。
/// </summary>
public sealed record ToleranceStackTerm
{
    /// <summary>Stable identity independent of term order or regenerated CAD names.</summary>
    /// <summary>独立于项顺序和 CAD 重生成名称的稳定 identity。</summary>
    public required string TermId { get; init; }

    /// <summary>Nominal magnitude in canonical millimetres.</summary>
    /// <summary>使用统一毫米的名义量。</summary>
    public required Length Nominal { get; init; }

    /// <summary>Signed lower and upper deviations around the term nominal.</summary>
    /// <summary>相对于该项 nominal 的上下有符号偏差。</summary>
    public required Tolerance Tolerance { get; init; }

    /// <summary>Whether the term is added to or subtracted from the stack result.</summary>
    /// <summary>该项加入还是从 stack result 中扣除。</summary>
    public required ToleranceStackDirection Direction { get; init; }

    /// <summary>Redacted evidence source; raw drawing content is forbidden here.</summary>
    /// <summary>脱敏 evidence source；这里禁止保存图纸原文。</summary>
    public required EngineeringProvenance Provenance { get; init; }
}

/// <summary>One deterministic, versionable stack-up request.</summary>
/// <summary>一个确定性、可版本化的公差链请求。</summary>
public sealed class ToleranceStackupRequest
{
    /// <summary>Creates a validated stack-up with unique, non-empty term identities.</summary>
    /// <summary>创建并校验具有唯一非空项 identity 的公差链。</summary>
    public ToleranceStackupRequest(
        string stackupId,
        ToleranceConstraintKind constraintKind,
        ToleranceAnalysisMethod method,
        IEnumerable<ToleranceStackTerm> terms)
    {
        if (string.IsNullOrWhiteSpace(stackupId))
        {
            throw new ArgumentException("A stack-up identity is required.", nameof(stackupId));
        }

        StackupId = stackupId.Trim();
        ConstraintKind = constraintKind;
        Method = method;
        Terms = [.. (terms ?? throw new ArgumentNullException(nameof(terms)))];
        if (Terms.Length == 0)
        {
            throw new ArgumentException("A tolerance stack-up requires at least one term.", nameof(terms));
        }

        if (Terms.Any(term => term is null))
        {
            throw new ArgumentException("Tolerance stack terms cannot contain null values.", nameof(terms));
        }

        if (Terms.Select(term => term.TermId).Distinct(StringComparer.Ordinal).Count() != Terms.Length)
        {
            throw new ArgumentException("Tolerance stack term identities must be unique.", nameof(terms));
        }

        if (Terms.Any(term => string.IsNullOrWhiteSpace(term.TermId)))
        {
            throw new ArgumentException("Tolerance stack term identities cannot be blank.", nameof(terms));
        }
    }

    /// <summary>Stable stack-up identity.</summary>
    public string StackupId { get; }

    /// <summary>Functional meaning of the resulting interval.</summary>
    public ToleranceConstraintKind ConstraintKind { get; }

    /// <summary>Explicit analysis method selected by policy.</summary>
    public ToleranceAnalysisMethod Method { get; }

    /// <summary>Immutable stack terms in caller-declared order.</summary>
    public ImmutableArray<ToleranceStackTerm> Terms { get; }
}

/// <summary>Manufacturing capability evidence used to keep a released drawing physically achievable.</summary>
/// <summary>用于保证 release 图纸可制造的制造能力证据。</summary>
public sealed record ManufacturingCapability
{
    /// <summary>Stable capability/process identity.</summary>
    public required string CapabilityId { get; init; }

    /// <summary>Achievable inclusive interval for the functional dimension.</summary>
    public required DimensionLimits AchievableLimits { get; init; }

    /// <summary>Non-secret inspection method name.</summary>
    public required string InspectionMethod { get; init; }

    /// <summary>Evidence provenance for the capability claim.</summary>
    public required EngineeringProvenance Provenance { get; init; }
}

/// <summary>
/// First-class functional dimension contract. Functional limits and drawing representation intentionally remain
/// separate: the displayed tolerance is never allowed to weaken the functional constraint.
/// 一级功能尺寸 contract。功能极限和图纸表达故意分离；显示公差绝不能放宽功能约束。
/// </summary>
public sealed record FunctionalDimensionRequirement
{
    /// <summary>Stable engineering requirement identity.</summary>
    public required string DimensionId { get; init; }

    /// <summary>Design-intent nominal value.</summary>
    public required Length DesignNominal { get; init; }

    /// <summary>Functional minimum/maximum that the assembly must satisfy.</summary>
    public required DimensionLimits FunctionalLimits { get; init; }

    /// <summary>Nominal value chosen for the released drawing representation.</summary>
    public required Length DrawingNominal { get; init; }

    /// <summary>Upper/lower deviations displayed by the released drawing.</summary>
    public required Tolerance DrawingTolerance { get; init; }

    /// <summary>Optional fit class, for example an approved ISO fit designation.</summary>
    public string? FitClass { get; init; }

    /// <summary>Optional general tolerance inherited from the active RulePack.</summary>
    public Tolerance? GeneralTolerance { get; init; }

    /// <summary>Optional manufacturing capability evidence.</summary>
    public ManufacturingCapability? ManufacturingCapability { get; init; }

    /// <summary>Non-secret inspection method associated with this dimension.</summary>
    public string? InspectionMethod { get; init; }

    /// <summary>Release criticality.</summary>
    public ToleranceCriticality Criticality { get; init; } = ToleranceCriticality.Normal;

    /// <summary>Redacted provenance of the requirement.</summary>
    public required EngineeringProvenance Provenance { get; init; }

    /// <summary>Approval state; AI proposals and private-review observations remain gated.</summary>
    public required EngineeringRequirementStatus ApprovalState { get; init; }
}

/// <summary>One explainable arithmetic step in a tolerance derivation.</summary>
/// <summary>公差推导链中的一个可解释算术步骤。</summary>
public sealed record ToleranceDerivationStep
{
    /// <summary>Stable step identity.</summary>
    public required string StepId { get; init; }

    /// <summary>Referenced term identity or the resulting stack identity.</summary>
    public required string SourceId { get; init; }

    /// <summary>Signed contribution or accumulated value in millimetres.</summary>
    public required Length Value { get; init; }

    /// <summary>Short deterministic rationale with no source drawing text.</summary>
    public required string Rationale { get; init; }
}

/// <summary>Human-readable but structured explanation returned by <c>tolerance.explain</c> integrations.</summary>
/// <summary>供 <c>tolerance.explain</c> 集成使用的结构化可读解释。</summary>
public sealed record ToleranceExplanation
{
    /// <summary>Dimension being explained.</summary>
    public required string DimensionId { get; init; }

    /// <summary>Selected analysis method.</summary>
    public required ToleranceAnalysisMethod Method { get; init; }

    /// <summary>Functional limits preserved by the result.</summary>
    public required DimensionLimits FunctionalLimits { get; init; }

    /// <summary>Drawing limits implied by nominal plus displayed deviations.</summary>
    public required DimensionLimits DrawingLimits { get; init; }

    /// <summary>Worst-case stack result, when the selected method is enabled.</summary>
    public DimensionLimits? StackupLimits { get; init; }

    /// <summary>Ordered derivation steps suitable for an audit record.</summary>
    public ImmutableArray<ToleranceDerivationStep> Steps { get; init; } = [];

    /// <summary>Builds a compact deterministic explanation without embedding source drawing content.</summary>
    /// <summary>生成不嵌入源图纸内容的紧凑确定性解释。</summary>
    public string ToPlainText()
    {
        string steps = string.Join(
            "; ",
            Steps.Select(step =>
                $"{step.StepId}:{step.Rationale}={step.Value.Millimeters.ToString("G17", CultureInfo.InvariantCulture)} mm"));
        string stack = StackupLimits is DimensionLimits limits
            ? $" stack=[{limits.Minimum}, {limits.Maximum}]"
            : string.Empty;
        return $"{DimensionId} method={Method} functional=[{FunctionalLimits.Minimum}, {FunctionalLimits.Maximum}] drawing=[{DrawingLimits.Minimum}, {DrawingLimits.Maximum}]{stack}; steps={steps}";
    }
}

/// <summary>Result of one deterministic tolerance analysis.</summary>
/// <summary>一次确定性公差分析的结果。</summary>
public sealed record ToleranceAnalysisResult
{
    /// <summary>Stable analyzed dimension identity.</summary>
    public required string DimensionId { get; init; }

    /// <summary>Analysis status consumed by QA/release gates.</summary>
    public required ToleranceAnalysisStatus Status { get; init; }

    /// <summary>Functional dimension input, including provenance and approval.</summary>
    public required FunctionalDimensionRequirement Requirement { get; init; }

    /// <summary>Computed drawing interval.</summary>
    public required DimensionLimits DrawingLimits { get; init; }

    /// <summary>Computed stack interval, absent only when the selected method is not enabled.</summary>
    public DimensionLimits? StackupLimits { get; init; }

    /// <summary>Stable diagnostics explaining a warning/review/blocking result.</summary>
    public ImmutableArray<string> Diagnostics { get; init; } = [];

    /// <summary>Full derivation chain, suitable for audit and user explanation.</summary>
    public required ToleranceExplanation Explanation { get; init; }

    /// <summary>Only a clean Pass can cross the release gate.</summary>
    public bool CanRelease => Status == ToleranceAnalysisStatus.Pass;
}

/// <summary>
/// Deterministic worst-case tolerance engine. It performs arithmetic only; it does not call SOLIDWORKS or infer a
/// tolerance from a screenshot, AI text or an unapproved drawing observation.
/// 确定性 Worst-Case 公差引擎。这里只做算术，不调用 SOLIDWORKS，也不从截图、AI 文本或未批准图纸观察推断公差。
/// </summary>
public static class FunctionalToleranceEngine
{
    /// <summary>Analyzes one functional dimension and stack-up under the explicitly selected method.</summary>
    /// <summary>按显式选择的方法分析一个功能尺寸与公差链。</summary>
    public static ToleranceAnalysisResult Analyze(
        FunctionalDimensionRequirement requirement,
        ToleranceStackupRequest stackup)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(stackup);

        if (string.IsNullOrWhiteSpace(requirement.DimensionId))
        {
            throw new ArgumentException("A functional dimension identity is required.", nameof(requirement));
        }

        DimensionLimits drawingLimits = AddTolerance(requirement.DrawingNominal, requirement.DrawingTolerance);
        var diagnostics = ImmutableArray.CreateBuilder<string>();
        DimensionLimits? stackupLimits = null;
        ImmutableArray<ToleranceDerivationStep> steps = [];

        if (stackup.Method == ToleranceAnalysisMethod.WorstCase)
        {
            (DimensionLimits limits, ImmutableArray<ToleranceDerivationStep> derivation) = AnalyzeWorstCase(stackup);
            stackupLimits = limits;
            steps = derivation;
        }
        else
        {
            diagnostics.Add($"analysis-method-not-enabled:{stackup.Method}");
        }

        if (requirement.ApprovalState is not (EngineeringRequirementStatus.Approved or EngineeringRequirementStatus.Released))
        {
            diagnostics.Add("requirement-approval-required");
        }

        if (requirement.Provenance.SourceKind.Equals("ai_proposed", StringComparison.Ordinal)
            && requirement.ApprovalState is not (EngineeringRequirementStatus.Approved or EngineeringRequirementStatus.Released))
        {
            diagnostics.Add("ai-proposal-cannot-release");
        }

        if (stackupLimits is DimensionLimits computed)
        {
            if (computed.Minimum.Millimeters < requirement.FunctionalLimits.Minimum.Millimeters
                || computed.Maximum.Millimeters > requirement.FunctionalLimits.Maximum.Millimeters)
            {
                diagnostics.Add("stackup-exceeds-functional-limits");
            }

            if (!NearlyEqual(computed.Maximum, requirement.FunctionalLimits.Maximum))
            {
                diagnostics.Add("stackup-functional-limit-mismatch");
            }
        }

        if (drawingLimits.Minimum.Millimeters < requirement.FunctionalLimits.Minimum.Millimeters
            || drawingLimits.Maximum.Millimeters > requirement.FunctionalLimits.Maximum.Millimeters)
        {
            diagnostics.Add("drawing-tolerance-exceeds-functional-limits");
        }

        ManufacturingCapability? capability = requirement.ManufacturingCapability;
        if (capability is not null
            && (drawingLimits.Minimum.Millimeters < capability.AchievableLimits.Minimum.Millimeters
                || drawingLimits.Maximum.Millimeters > capability.AchievableLimits.Maximum.Millimeters))
        {
            diagnostics.Add("drawing-tolerance-exceeds-manufacturing-capability");
        }

        ToleranceAnalysisStatus status = Classify(diagnostics);
        var explanation = new ToleranceExplanation
        {
            DimensionId = requirement.DimensionId.Trim(),
            Method = stackup.Method,
            FunctionalLimits = requirement.FunctionalLimits,
            DrawingLimits = drawingLimits,
            StackupLimits = stackupLimits,
            Steps = steps,
        };

        return new ToleranceAnalysisResult
        {
            DimensionId = requirement.DimensionId.Trim(),
            Status = status,
            Requirement = requirement,
            DrawingLimits = drawingLimits,
            StackupLimits = stackupLimits,
            Diagnostics = [.. diagnostics.Distinct(StringComparer.Ordinal)],
            Explanation = explanation,
        };
    }

    /// <summary>Convenience entry point that makes the default Worst-Case policy explicit at the call site.</summary>
    /// <summary>便捷入口；让调用点显式表达默认 Worst-Case policy。</summary>
    public static ToleranceAnalysisResult AnalyzeWorstCase(
        FunctionalDimensionRequirement requirement,
        ToleranceStackupRequest stackup) =>
        Analyze(
            requirement,
            new ToleranceStackupRequest(
                stackup.StackupId,
                stackup.ConstraintKind,
                ToleranceAnalysisMethod.WorstCase,
                stackup.Terms));

    private static (DimensionLimits Limits, ImmutableArray<ToleranceDerivationStep> Steps) AnalyzeWorstCase(
        ToleranceStackupRequest stackup)
    {
        double minimum = 0d;
        double maximum = 0d;
        var steps = ImmutableArray.CreateBuilder<ToleranceDerivationStep>();

        foreach (ToleranceStackTerm term in stackup.Terms)
        {
            double termMinimum = term.Nominal.Millimeters + term.Tolerance.LowerDeviation.Millimeters;
            double termMaximum = term.Nominal.Millimeters + term.Tolerance.UpperDeviation.Millimeters;
            double signedMinimum;
            double signedMaximum;
            if (term.Direction == ToleranceStackDirection.Add)
            {
                signedMinimum = termMinimum;
                signedMaximum = termMaximum;
            }
            else
            {
                // Subtraction reverses the adverse bounds: -(largest term) is the smallest result.
                // 减法会反转不利极限：扣除最大的项得到最小结果。
                signedMinimum = -termMaximum;
                signedMaximum = -termMinimum;
            }

            minimum += signedMinimum;
            maximum += signedMaximum;
            steps.Add(
                new ToleranceDerivationStep
                {
                    StepId = $"step-{steps.Count + 1:D2}",
                    SourceId = term.TermId,
                    Value = Length.FromMillimeters(signedMaximum),
                    Rationale = $"{term.Direction.ToString().ToLowerInvariant()}:{term.TermId}",
                });
        }

        return (
            new DimensionLimits(Length.FromMillimeters(minimum), Length.FromMillimeters(maximum)),
            [.. steps]);
    }

    private static DimensionLimits AddTolerance(Length nominal, Tolerance tolerance) =>
        new(
            Length.FromMillimeters(nominal.Millimeters + tolerance.LowerDeviation.Millimeters),
            Length.FromMillimeters(nominal.Millimeters + tolerance.UpperDeviation.Millimeters));

    private static ToleranceAnalysisStatus Classify(ImmutableArray<string>.Builder diagnostics)
    {
        if (diagnostics.Any(value => value.Contains("exceeds", StringComparison.Ordinal)
                || value.Contains("mismatch", StringComparison.Ordinal)))
        {
            return ToleranceAnalysisStatus.Blocking;
        }

        if (diagnostics.Count > 0)
        {
            return ToleranceAnalysisStatus.ReviewRequired;
        }

        return ToleranceAnalysisStatus.Pass;
    }

    private static bool NearlyEqual(Length left, Length right) =>
        Math.Abs(left.Millimeters - right.Millimeters) <= 1e-9d;
}
