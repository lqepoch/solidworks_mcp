using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.AutoDrawing;

/// <summary>
/// Unified release-gate severity used by the drawing QA compiler.
/// 工程图 QA compiler 使用的统一 release-gate 严重级别。
/// </summary>
public enum DrawingQaFindingStatus
{
    /// <summary>The inspected condition is verified and does not block release.</summary>
    Pass,

    /// <summary>The condition is visible to QA but does not block release.</summary>
    Warning,

    /// <summary>Engineering approval or a missing proof is required before release.</summary>
    ReviewRequired,

    /// <summary>The drawing cannot be released in its current state.</summary>
    Blocking,
}

/// <summary>One privacy-safe, deterministic drawing QA finding.</summary>
/// <remarks>
/// Finding targets are stable engineering identities, never OCR text or PDF payloads. This keeps the release report
/// useful for automation while ensuring private reference drawings do not leak through diagnostics.
/// finding target 是稳定的工程 identity，而不是 OCR 文本或 PDF payload，避免秘密图纸内容通过诊断泄露。
/// </remarks>
public sealed record DrawingQaFinding
{
    /// <summary>Stable diagnostic code.</summary>
    public required string Code { get; init; }

    /// <summary>Release-gate severity.</summary>
    public required DrawingQaFindingStatus Status { get; init; }

    /// <summary>Semantic scope such as rebuild, coverage, layout or artifact.</summary>
    public required string Scope { get; init; }

    /// <summary>Stable target identity; it is not a rendered annotation string.</summary>
    public required string TargetId { get; init; }

    /// <summary>Safe explanation suitable for logs and an audit manifest.</summary>
    public required string Explanation { get; init; }

    /// <summary>Whether a deterministic targeted repair can address this finding.</summary>
    public bool IsRepairable { get; init; }
}

/// <summary>One provider-neutral targeted repair action proposed by the QA planner.</summary>
/// <remarks>
/// The action is a plan, not a COM mutation. A provider must re-check the precondition fingerprint and exact target
/// identity immediately before applying it. 该 action 只是 plan，不是 COM mutation；Provider 应在执行前重新校验
/// precondition fingerprint 和 exact target identity。
/// </remarks>
public sealed record DrawingRepairAction
{
    /// <summary>Stable action code, for example dimension.suppress-redundant.</summary>
    public required string ActionCode { get; init; }

    /// <summary>Stable annotation/requirement identity affected by this action.</summary>
    public required string TargetId { get; init; }

    /// <summary>Finding code that justified the action.</summary>
    public required string FindingCode { get; init; }

    /// <summary>Fingerprint of the finding state expected by the later provider mutation.</summary>
    public required string PreconditionFingerprint { get; init; }

    /// <summary>Safe rationale for the targeted action.</summary>
    public required string Rationale { get; init; }

    /// <summary>
    /// Planned paper-space position when this is a layout-position action; null for other action classes.
    /// 当 action 是布局位置 action 时的纸空间目标坐标；其它 action 为 null。
    /// </summary>
    public Coordinate2D? PlannedPosition { get; init; }
}

/// <summary>Deterministic set of targeted repair actions.</summary>
public sealed record DrawingRepairPlan
{
    /// <summary>Persisted repair-plan schema version.</summary>
    public const string SchemaVersion = "1.0";

    /// <summary>Actions sorted by stable action code and target identity.</summary>
    public ImmutableArray<DrawingRepairAction> Actions { get; init; } = [];

    /// <summary>Stable hash of the action set.</summary>
    public required string Fingerprint { get; init; }

    /// <summary>True when at least one supported targeted action is available.</summary>
    public bool HasActions => Actions.Length != 0;
}

/// <summary>Verified evidence for one drawing artifact required by release.</summary>
/// <remarks>
/// The provider remains responsible for path allowlists and overwrite policy. This value only records post-export
/// verification, so a path existing by itself is never treated as a successful release artifact.
/// Provider 仍负责 path allowlist 与 overwrite policy；本类型只记录导出后的验证结果，文件存在本身不是成功证明。
/// </remarks>
public sealed record DrawingArtifactProof
{
    /// <summary>Canonical artifact format, for example SLDDrw or PDF.</summary>
    public required string Format { get; init; }

    /// <summary>Verified artifact path returned by the provider/export boundary.</summary>
    public required string TargetPath { get; init; }

    /// <summary>Whether the file existed when verification ran.</summary>
    public required bool Exists { get; init; }

    /// <summary>Whether the checksum and source-state binding were verified.</summary>
    public required bool IsVerified { get; init; }

    /// <summary>Lowercase SHA-256 checksum, or empty when verification could not read the file.</summary>
    public required string Sha256 { get; init; }

    /// <summary>Document state hash used to bind the artifact to the inspected drawing.</summary>
    public required string SourceStateHash { get; init; }

    /// <summary>Stable verification result code.</summary>
    public required string VerificationCode { get; init; }
}

/// <summary>Inputs to one complete drawing QA/release planning pass.</summary>
public sealed record DrawingQaRequest
{
    /// <summary>Exact post-rebuild inspection of the drawing document.</summary>
    public required CadInspectionSnapshot Drawing { get; init; }

    /// <summary>Semantic requirement coverage report.</summary>
    public required PartDrawingCoverageReport Coverage { get; init; }

    /// <summary>Manufacturing annotation evidence/plan; omission is an unavailable release check.</summary>
    public DrawingManufacturingAnnotationPlan? ManufacturingAnnotations { get; init; }

    /// <summary>Exported artifact proofs, including the native drawing and configured neutral deliverables.</summary>
    public ImmutableArray<DrawingArtifactProof> Artifacts { get; init; } = [];

    /// <summary>Formats that this release profile explicitly requires.</summary>
    public ImmutableArray<string> RequiredArtifactFormats { get; init; } = [];

    /// <summary>
    /// Controls whether artifact findings are evaluated in this pass. A preflight pass may deliberately defer this
    /// check until after the transaction has materialized the requested files; the final release pass must leave this
    /// value true. 预检阶段可以暂缓 artifact 检查，等事务真正生成文件后再执行最终 gate；最终 release pass 必须为 true。
    /// </summary>
    public bool IncludeArtifactFindings { get; init; } = true;

    /// <summary>
    /// Expected document state hash captured before export. Null means the caller did not supply a prior-state
    /// assertion; the inspected document hash is still bound to every artifact proof.
    /// </summary>
    public string? ExpectedDocumentStateHash { get; init; }
}

/// <summary>Complete deterministic output of one drawing QA pass.</summary>
public sealed record DrawingQaPlan
{
    /// <summary>Persisted QA-plan schema version.</summary>
    public const string SchemaVersion = "1.0";

    /// <summary>Exact drawing identity inspected by the planner.</summary>
    public required DocumentId DocumentId { get; init; }

    /// <summary>State hash inspected after rebuild and before release.</summary>
    public required string DocumentStateHash { get; init; }

    /// <summary>All findings in deterministic order.</summary>
    public ImmutableArray<DrawingQaFinding> Findings { get; init; } = [];

    /// <summary>Supported targeted repair actions derived from the findings.</summary>
    public required DrawingRepairPlan RepairPlan { get; init; }

    /// <summary>Stable hash of document state, findings and repair plan.</summary>
    public required string Fingerprint { get; init; }

    /// <summary>True only when no check is blocking or unresolved.</summary>
    public bool CanRelease => Findings.All(finding => finding.Status is DrawingQaFindingStatus.Pass or DrawingQaFindingStatus.Warning);
}

/// <summary>Auditable release manifest with artifact checksums and the QA decision.</summary>
public sealed record DrawingReleaseEvidenceManifest
{
    /// <summary>Persisted release-manifest schema version.</summary>
    public const string SchemaVersion = "1.0";

    /// <summary>Exact drawing identity covered by this manifest.</summary>
    public required DocumentId DocumentId { get; init; }

    /// <summary>State hash bound to the inspection and exports.</summary>
    public required string DocumentStateHash { get; init; }

    /// <summary>QA plan fingerprint used for the release decision.</summary>
    public required string QaFingerprint { get; init; }

    /// <summary>Whether the release gate passed.</summary>
    public required bool Released { get; init; }

    /// <summary>Complete QA findings, including pass evidence and blocking reasons.</summary>
    public ImmutableArray<DrawingQaFinding> Findings { get; init; } = [];

    /// <summary>Artifact existence, checksum and state binding evidence.</summary>
    public ImmutableArray<DrawingArtifactProof> Artifacts { get; init; } = [];

    /// <summary>Manifest creation time; excluded from the stable fingerprint.</summary>
    public required DateTimeOffset GeneratedAtUtc { get; init; }

    /// <summary>Stable hash of all manifest evidence except GeneratedAtUtc.</summary>
    public required string Fingerprint { get; init; }
}

/// <summary>
/// Deterministic QA and release planner for single-part drawings.
/// 单零件工程图的确定性 QA 与 Release planner。
/// </summary>
/// <remarks>
/// This class intentionally does not reference SOLIDWORKS COM. Native providers produce inspections and artifact
/// proofs; this planner decides whether those proofs are sufficient. That separation makes missing/unavailable checks
/// fail closed in Hosted CI and in the real SOLIDWORKS path alike. 本类刻意不引用 SOLIDWORKS COM；Provider 提供
/// inspection 与 artifact proof，本 planner 只判断证据是否足够，从而在 Hosted CI 和真实 SOLIDWORKS 中都 fail closed。
/// </remarks>
public static class DrawingQaReleasePlanner
{
    /// <summary>Builds the complete QA plan from explicit inspection, semantic and export evidence.</summary>
    public static DrawingQaPlan Analyze(DrawingQaRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var findings = new List<DrawingQaFinding>();
        AddDocumentFindings(request, findings);
        AddCoverageFindings(request.Coverage, findings);
        AddLayoutFindings(request.Coverage.LayoutPlan, findings);
        AddManufacturingAnnotationFindings(request.ManufacturingAnnotations, findings);
        if (request.IncludeArtifactFindings)
        {
            AddArtifactFindings(request, findings);
        }

        DrawingQaFinding[] orderedFindings = [.. findings
            .OrderBy(finding => finding.Status)
            .ThenBy(finding => finding.Scope, StringComparer.Ordinal)
            .ThenBy(finding => finding.Code, StringComparer.Ordinal)
            .ThenBy(finding => finding.TargetId, StringComparer.Ordinal)];
        DrawingRepairPlan repairPlan = BuildRepairPlan(orderedFindings, request.Coverage.LayoutPlan);
        string fingerprint = Fingerprint(request.Drawing.Document, orderedFindings, repairPlan);
        return new DrawingQaPlan
        {
            DocumentId = request.Drawing.Document.DocumentId,
            DocumentStateHash = request.Drawing.Document.StateHash,
            Findings = [.. orderedFindings],
            RepairPlan = repairPlan,
            Fingerprint = fingerprint,
        };
    }

    /// <summary>
    /// Creates a release evidence manifest even when the gate is blocked. A blocked manifest is evidence of refusal,
    /// not a release artifact. 即使 gate 被阻断也生成 manifest；blocked manifest 只证明拒绝原因，不能冒充 release。
    /// </summary>
    public static DrawingReleaseEvidenceManifest CreateManifest(
        DrawingQaRequest request,
        DrawingQaPlan plan,
        DateTimeOffset generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(plan);
        ValidateRequest(request);
        if (plan.DocumentId != request.Drawing.Document.DocumentId
            || !plan.DocumentStateHash.Equals(request.Drawing.Document.StateHash, StringComparison.Ordinal))
        {
            throw new ArgumentException("The QA plan is not bound to the requested drawing inspection.", nameof(plan));
        }

        DrawingArtifactProof[] artifacts = [.. request.Artifacts
            .OrderBy(artifact => artifact.Format, StringComparer.Ordinal)
            .ThenBy(artifact => artifact.TargetPath, StringComparer.Ordinal)];
        DrawingQaFinding[] findings = [.. plan.Findings];
        string manifestFingerprint = ManifestFingerprint(
            plan.DocumentId,
            plan.DocumentStateHash,
            plan.Fingerprint,
            plan.CanRelease,
            findings,
            artifacts);
        return new DrawingReleaseEvidenceManifest
        {
            DocumentId = plan.DocumentId,
            DocumentStateHash = plan.DocumentStateHash,
            QaFingerprint = plan.Fingerprint,
            Released = plan.CanRelease,
            Findings = [.. findings],
            Artifacts = [.. artifacts],
            GeneratedAtUtc = generatedAtUtc.ToUniversalTime(),
            Fingerprint = manifestFingerprint,
        };
    }

    /// <summary>
    /// Verifies one exported file without changing it. The caller must have already enforced its path allowlist.
    /// 在不修改文件的前提下验证一个导出 artifact；path allowlist 必须由调用方先行执行。
    /// </summary>
    public static DrawingArtifactProof VerifyArtifact(string format, string targetPath, string sourceStateHash)
    {
        string canonicalFormat = CanonicalFormat(format);
        string safePath = targetPath?.Trim() ?? string.Empty;
        string safeStateHash = sourceStateHash?.Trim() ?? string.Empty;
        if (canonicalFormat.Length == 0 || safePath.Length == 0 || safeStateHash.Length == 0)
        {
            return new DrawingArtifactProof
            {
                Format = canonicalFormat,
                TargetPath = safePath,
                Exists = false,
                IsVerified = false,
                Sha256 = string.Empty,
                SourceStateHash = safeStateHash,
                VerificationCode = "artifact-input-invalid",
            };
        }

        try
        {
            string fullPath = Path.GetFullPath(safePath);
            if (!File.Exists(fullPath))
            {
                return new DrawingArtifactProof
                {
                    Format = canonicalFormat,
                    TargetPath = fullPath,
                    Exists = false,
                    IsVerified = false,
                    Sha256 = string.Empty,
                    SourceStateHash = safeStateHash,
                    VerificationCode = "artifact-missing",
                };
            }

            using FileStream stream = File.OpenRead(fullPath);
            string sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            return new DrawingArtifactProof
            {
                Format = canonicalFormat,
                TargetPath = fullPath,
                Exists = true,
                IsVerified = true,
                Sha256 = sha256,
                SourceStateHash = safeStateHash,
                VerificationCode = "artifact-verified",
            };
        }
        catch (IOException)
        {
            return Unreadable(canonicalFormat, safePath, safeStateHash, "artifact-read-failed");
        }
        catch (UnauthorizedAccessException)
        {
            return Unreadable(canonicalFormat, safePath, safeStateHash, "artifact-access-denied");
        }
        catch (ArgumentException)
        {
            return Unreadable(canonicalFormat, safePath, safeStateHash, "artifact-path-invalid");
        }
    }

    private static DrawingArtifactProof Unreadable(string format, string path, string stateHash, string code) => new()
    {
        Format = format,
        TargetPath = path,
        Exists = true,
        IsVerified = false,
        Sha256 = string.Empty,
        SourceStateHash = stateHash,
        VerificationCode = code,
    };

    private static void ValidateRequest(DrawingQaRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Drawing);
        ArgumentNullException.ThrowIfNull(request.Coverage);
        if (string.IsNullOrWhiteSpace(request.Drawing.Document.StateHash))
        {
            throw new ArgumentException("Drawing inspection must contain a state hash.", nameof(request));
        }

        if (request.RequiredArtifactFormats.Any(value => string.IsNullOrWhiteSpace(value)))
        {
            throw new ArgumentException("Required artifact formats cannot be blank.", nameof(request));
        }

        string[] formats = [.. request.RequiredArtifactFormats.Select(CanonicalFormat)];
        if (formats.Distinct(StringComparer.Ordinal).Count() != formats.Length)
        {
            throw new ArgumentException("Required artifact formats must be unique.", nameof(request));
        }
    }

    private static void AddDocumentFindings(DrawingQaRequest request, List<DrawingQaFinding> findings)
    {
        CadInspectionSnapshot drawing = request.Drawing;
        if (drawing.Document.DocumentType != CadDocumentType.Drawing)
        {
            findings.Add(Finding("document-type-invalid", DrawingQaFindingStatus.Blocking, "document", drawing.Document.DocumentId.Value,
                "The release target is not a drawing document."));
        }

        foreach (CadDiagnostic diagnostic in drawing.Diagnostics
                     .OrderBy(value => value.Code, StringComparer.Ordinal)
                     .ThenBy(value => value.Scope, StringComparer.Ordinal)
                     .ThenBy(value => value.EntityIdentity, StringComparer.Ordinal))
        {
            DrawingQaFindingStatus status = diagnostic.Severity switch
            {
                CadDiagnosticSeverity.Error => DrawingQaFindingStatus.Blocking,
                CadDiagnosticSeverity.Warning => DrawingQaFindingStatus.Warning,
                _ => DrawingQaFindingStatus.Pass,
            };
            findings.Add(Finding(
                diagnostic.Code,
                status,
                "rebuild",
                diagnostic.EntityIdentity ?? diagnostic.Scope,
                diagnostic.Message));
        }

        if (request.ExpectedDocumentStateHash is not null
            && !request.ExpectedDocumentStateHash.Trim().Equals(drawing.Document.StateHash, StringComparison.Ordinal))
        {
            findings.Add(Finding(
                "drawing-state-conflict",
                DrawingQaFindingStatus.Blocking,
                "document",
                drawing.Document.DocumentId.Value,
                "The inspected drawing state hash differs from the caller's expected state."));
        }
    }

    private static void AddCoverageFindings(PartDrawingCoverageReport coverage, List<DrawingQaFinding> findings)
    {
        foreach (PartDrawingCoverageFinding finding in coverage.Findings)
        {
            findings.Add(Finding(
                finding.Code,
                Map(finding.Status),
                "coverage",
                finding.CoverageKey,
                finding.Explanation));
        }

        foreach (PartDrawingDimensionCoverageFinding finding in coverage.DimensionFindings)
        {
            findings.Add(Finding(
                $"dimension.{finding.Code}",
                Map(finding.Status),
                "dimension",
                $"feature:{finding.FeatureId.Value}/definition:{finding.DefinitionKey}",
                finding.Explanation,
                finding.Code == "duplicate-dimension-evidence"));
        }
    }

    private static void AddLayoutFindings(DrawingLayoutPlan? layout, List<DrawingQaFinding> findings)
    {
        if (layout is null)
        {
            findings.Add(Finding(
                "layout-check-unavailable",
                DrawingQaFindingStatus.Blocking,
                "layout",
                "drawing-layout",
                "No deterministic paper-space layout proof was supplied; release cannot skip layout QA."));
            return;
        }

        foreach (DrawingLayoutFinding finding in layout.Findings)
        {
            string target = finding.ItemIds.Length == 0
                ? finding.ZoneId ?? layout.SheetId
                : string.Join(",", finding.ItemIds);
            findings.Add(Finding(
                finding.Code,
                Map(finding.Status),
                "layout",
                target,
                finding.Explanation,
                finding.Code == "annotation-repositioned"));
        }
    }

    private static void AddManufacturingAnnotationFindings(
        DrawingManufacturingAnnotationPlan? plan,
        List<DrawingQaFinding> findings)
    {
        if (plan is null)
        {
            findings.Add(Finding(
                "annotation-check-unavailable",
                DrawingQaFindingStatus.Blocking,
                "annotation",
                "manufacturing-annotations",
                "No manufacturing annotation provenance/association plan was supplied; release cannot skip annotation QA."));
            return;
        }

        foreach (DrawingManufacturingAnnotationPlanItem item in plan.Items)
        {
            // A planned native import is executable work, not persisted evidence. It therefore blocks release until the
            // provider reads the native annotation back and the planner is run again.
            // planned native import 只是可执行工作，不是已持久化 evidence；Provider 必须读回后重新规划。
            DrawingQaFindingStatus status = item.Status switch
            {
                ManufacturingAnnotationFindingStatus.Pass => DrawingQaFindingStatus.Pass,
                ManufacturingAnnotationFindingStatus.Warning => DrawingQaFindingStatus.Blocking,
                ManufacturingAnnotationFindingStatus.ReviewRequired => DrawingQaFindingStatus.ReviewRequired,
                ManufacturingAnnotationFindingStatus.Blocking => DrawingQaFindingStatus.Blocking,
                _ => DrawingQaFindingStatus.Blocking,
            };
            findings.Add(Finding(
                item.Code,
                status,
                "annotation",
                item.RequirementId,
                item.Rationale));
        }
    }

    private static void AddArtifactFindings(DrawingQaRequest request, List<DrawingQaFinding> findings)
    {
        if (request.RequiredArtifactFormats.Length == 0)
        {
            findings.Add(Finding(
                "artifact-policy-unavailable",
                DrawingQaFindingStatus.Blocking,
                "artifact",
                "required-formats",
                "No configured release artifact formats were supplied."));
            return;
        }

        DrawingArtifactProof[] artifacts = [.. request.Artifacts];
        foreach (string format in request.RequiredArtifactFormats.Select(CanonicalFormat).OrderBy(value => value, StringComparer.Ordinal))
        {
            DrawingArtifactProof[] matches = [.. artifacts.Where(artifact => CanonicalFormat(artifact.Format).Equals(format, StringComparison.Ordinal))];
            if (matches.Length == 0)
            {
                findings.Add(Finding(
                    "artifact-missing",
                    DrawingQaFindingStatus.Blocking,
                    "artifact",
                    format,
                    $"Required {format} artifact has no verification proof."));
                continue;
            }

            if (matches.Length > 1)
            {
                findings.Add(Finding(
                    "artifact-proof-ambiguous",
                    DrawingQaFindingStatus.Blocking,
                    "artifact",
                    format,
                    $"More than one proof was supplied for required {format}; release identity is ambiguous."));
                continue;
            }

            DrawingArtifactProof artifact = matches[0];
            if (!artifact.Exists)
            {
                findings.Add(Finding("artifact-missing", DrawingQaFindingStatus.Blocking, "artifact", format,
                    $"Required {format} artifact does not exist."));
            }
            else if (!artifact.IsVerified || !artifact.VerificationCode.Equals("artifact-verified", StringComparison.Ordinal))
            {
                findings.Add(Finding("artifact-checksum-unverified", DrawingQaFindingStatus.Blocking, "artifact", format,
                    $"Required {format} artifact exists but its checksum was not verified."));
            }
            else if (!artifact.SourceStateHash.Equals(request.Drawing.Document.StateHash, StringComparison.Ordinal))
            {
                findings.Add(Finding("artifact-source-state-mismatch", DrawingQaFindingStatus.Blocking, "artifact", format,
                    $"Required {format} artifact is not bound to the inspected drawing state."));
            }
            else
            {
                findings.Add(Finding("artifact-verified", DrawingQaFindingStatus.Pass, "artifact", format,
                    $"Required {format} artifact exists, has a verified checksum and matches the inspected state."));
            }
        }
    }

    private static DrawingRepairPlan BuildRepairPlan(
        IEnumerable<DrawingQaFinding> findings,
        DrawingLayoutPlan? layout)
    {
        DrawingRepairAction[] actions = [.. findings
            .Where(finding => finding.IsRepairable)
            .Select(finding => new DrawingRepairAction
            {
                ActionCode = finding.Code switch
                {
                    "dimension.duplicate-dimension-evidence" => "dimension.suppress-redundant",
                    "annotation-repositioned" => "layout.apply-planned-position",
                    _ => "review.targeted-repair",
                },
                TargetId = finding.TargetId,
                FindingCode = finding.Code,
                PreconditionFingerprint = FindingFingerprint(finding),
                Rationale = finding.Explanation,
                PlannedPosition = finding.Code == "annotation-repositioned"
                    ? layout?.Placements
                        .FirstOrDefault(placement => placement.ItemId.Equals(finding.TargetId, StringComparison.Ordinal))
                        ?.Bounds.Center is DrawingLayoutPoint center
                            ? new Coordinate2D(center.X, center.Y)
                            : null
                    : null,
            })
            .OrderBy(action => action.ActionCode, StringComparer.Ordinal)
            .ThenBy(action => action.TargetId, StringComparer.Ordinal)];
        return new DrawingRepairPlan
        {
            Actions = [.. actions],
            Fingerprint = RepairFingerprint(actions),
        };
    }

    private static DrawingQaFinding Finding(
        string code,
        DrawingQaFindingStatus status,
        string scope,
        string targetId,
        string explanation,
        bool isRepairable = false) => new()
        {
            Code = code,
            Status = status,
            Scope = scope,
            TargetId = targetId,
            Explanation = explanation,
            IsRepairable = isRepairable,
        };

    private static DrawingQaFindingStatus Map(PartDrawingCoverageStatus status) => status switch
    {
        PartDrawingCoverageStatus.Pass => DrawingQaFindingStatus.Pass,
        PartDrawingCoverageStatus.Warning => DrawingQaFindingStatus.Warning,
        PartDrawingCoverageStatus.ReviewRequired => DrawingQaFindingStatus.ReviewRequired,
        PartDrawingCoverageStatus.Blocking => DrawingQaFindingStatus.Blocking,
        _ => DrawingQaFindingStatus.Blocking,
    };

    private static DrawingQaFindingStatus Map(PartDrawingDimensionFindingStatus status) => status switch
    {
        PartDrawingDimensionFindingStatus.Pass => DrawingQaFindingStatus.Pass,
        PartDrawingDimensionFindingStatus.Warning => DrawingQaFindingStatus.Warning,
        PartDrawingDimensionFindingStatus.ReviewRequired => DrawingQaFindingStatus.ReviewRequired,
        PartDrawingDimensionFindingStatus.Blocking => DrawingQaFindingStatus.Blocking,
        _ => DrawingQaFindingStatus.Blocking,
    };

    private static DrawingQaFindingStatus Map(DrawingLayoutFindingStatus status) => status switch
    {
        DrawingLayoutFindingStatus.Pass => DrawingQaFindingStatus.Pass,
        DrawingLayoutFindingStatus.Warning => DrawingQaFindingStatus.Warning,
        DrawingLayoutFindingStatus.ReviewRequired => DrawingQaFindingStatus.ReviewRequired,
        DrawingLayoutFindingStatus.Blocking => DrawingQaFindingStatus.Blocking,
        _ => DrawingQaFindingStatus.Blocking,
    };

    private static string FindingFingerprint(DrawingQaFinding finding) => Hash(string.Join(
        "|",
        finding.Code,
        finding.Status,
        finding.Scope,
        finding.TargetId,
        finding.Explanation,
        finding.IsRepairable));

    private static string RepairFingerprint(IEnumerable<DrawingRepairAction> actions) => Hash(string.Join(
        "\n",
        actions.Select(action => string.Join(
            "|",
            action.ActionCode,
            action.TargetId,
            action.FindingCode,
            action.PreconditionFingerprint,
            action.Rationale,
            action.PlannedPosition?.ToString() ?? string.Empty))));

    private static string Fingerprint(
        CadDocumentSummary document,
        IEnumerable<DrawingQaFinding> findings,
        DrawingRepairPlan repairPlan) => Hash(string.Join(
        "\n",
        new[]
        {
            document.DocumentId.Value,
            document.DocumentType.ToString(),
            document.Configuration,
            document.StateHash,
        }
        .Concat(findings.Select(finding => string.Join("|", finding.Code, finding.Status, finding.Scope, finding.TargetId, finding.Explanation, finding.IsRepairable)))
        .Append(repairPlan.Fingerprint)));

    private static string ManifestFingerprint(
        DocumentId documentId,
        string stateHash,
        string qaFingerprint,
        bool released,
        IEnumerable<DrawingQaFinding> findings,
        IEnumerable<DrawingArtifactProof> artifacts) => Hash(string.Join(
        "\n",
        new[]
        {
            documentId.Value,
            stateHash,
            qaFingerprint,
            released.ToString(CultureInfo.InvariantCulture),
        }
        .Concat(findings.Select(finding => string.Join("|", finding.Code, finding.Status, finding.Scope, finding.TargetId, finding.Explanation, finding.IsRepairable)))
        .Concat(artifacts.Select(artifact => string.Join("|", artifact.Format, artifact.TargetPath, artifact.Exists, artifact.IsVerified, artifact.Sha256, artifact.SourceStateHash, artifact.VerificationCode)))));

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string CanonicalFormat(string? format) => format?.Trim().ToUpperInvariant() ?? string.Empty;
}
