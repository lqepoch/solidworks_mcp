namespace SolidWorksMcp.CadAbstractions;

/// <summary>
/// Severity of a provider diagnostic returned with inspection or rebuild evidence.
/// Provider inspection/rebuild evidence 的严重级别。
/// </summary>
public enum CadDiagnosticSeverity
{
    /// <summary>
    /// Informational diagnostic that does not affect release readiness.
    /// 不影响 release readiness 的信息性诊断。
    /// </summary>
    Info,

    /// <summary>
    /// Non-blocking warning that must remain visible to QA and audit.
    /// 不阻断当前操作、但必须对 QA 和 audit 保持可见的 warning。
    /// </summary>
    Warning,

    /// <summary>
    /// Model or feature error that prevents a verified healthy result.
    /// 阻止系统声明模型健康、已验证的 model 或 feature error。
    /// </summary>
    Error,
}

/// <summary>
/// Vendor-neutral diagnostic for one CAD entity or document scope.
/// 单个 CAD entity 或 document scope 的无厂商诊断记录。
/// </summary>
/// <remarks>
/// The message is a stable, privacy-safe classification. Providers may carry the numeric native code, but must not
/// leak arbitrary modal text, file contents or COM exception text into the shared business contract.
/// message 是稳定且隐私安全的分类；Provider 可以携带 native numeric code，但不能把任意 modal 文本、文件内容或
/// COM exception 原文泄露到共享业务契约。
/// </remarks>
public sealed record CadDiagnostic
{
    /// <summary>
    /// Stable diagnostic code, for example <c>feature.rebuild-error</c>.
    /// 稳定的诊断 code，例如 <c>feature.rebuild-error</c>。
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// Diagnostic severity.
    /// 诊断严重级别。
    /// </summary>
    public required CadDiagnosticSeverity Severity { get; init; }

    /// <summary>
    /// Stable human-readable classification, not raw vendor text.
    /// 稳定、可读的分类描述，不是 vendor 原始文本。
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Scope such as document, feature or body.
    /// 诊断 scope，例如 document、feature 或 body。
    /// </summary>
    public required string Scope { get; init; }

    /// <summary>
    /// Optional stable provider-neutral entity identity.
    /// 可选的稳定、无 provider 厂商耦合的 entity identity。
    /// </summary>
    public string? EntityIdentity { get; init; }

    /// <summary>
    /// Optional native numeric code retained for diagnostics and support correlation.
    /// 为诊断和 support correlation 保留的可选 native numeric code。
    /// </summary>
    public int? NativeCode { get; init; }
}
