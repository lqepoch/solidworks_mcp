# SolidWorksMcp contribution instructions

This repository implements GitHub Epic #1. GitHub Issue bodies, their comments and the tracked issue evidence baseline are the authority for scope and acceptance. Work in the single checkout; do not create parallel worktrees for agents.

## Execution discipline

For every child Issue:

1. Read the parent, dependency issues and docs/research/issue-evidence-baseline.md.
2. Inspect the current project and pinned references corpus.
3. For unfamiliar SOLIDWORKS behavior, inspect the installed type library and official API source before writing a COM call. Never guess a signature, enum or return-value contract.
4. Add or update Unit/Contract/FakeCad tests before claiming behavior. Run the Hosted-safe command for every change.
5. Run Live SOLIDWORKS tests only when the doctor reports the capability. Record passed, failed, skipped and blocked separately; skipped is never passed.
6. Record upstream SHA/license/provenance for adapted material. Do not commit SOLIDWORKS binaries, type libraries, copyrighted manuals or customer CAD.
7. Commit small, buildable changes and report exact commands, exit codes, artifacts, compatibility and known limitations.

## Ownership and dependency direction

- src/SolidWorksMcp.Protocol: versioned MCP-facing schemas and envelopes.
- src/SolidWorksMcp.Core: policy, orchestration, transaction and audit contracts; never vendor COM.
- src/SolidWorksMcp.CadAbstractions: vendor-neutral CAD contracts and identities.
- src/SolidWorksMcp.EngineeringModel: immutable engineering semantic graphs and provenance.
- src/SolidWorksMcp.RuleEngine: versioned RulePack evaluation and overrides.
- src/SolidWorksMcp.Tolerancing: deterministic tolerance/stack-up math and explanations.
- src/SolidWorksMcp.AutoDrawing: part Drawing Compiler; consumes semantic contracts.
- src/SolidWorksMcp.AssemblyDrawing: assembly Drawing Compiler; consumes semantic contracts.
- src/SolidWorksMcp.Server: MCP composition root and compact high-level tools.
- providers/SolidWorksMcp.Provider.SolidWorks: the only production project allowed to reference SolidWorks.Interop.*; keep COM wrappers thin and STA-bound.
- testing/SolidWorksMcp.Provider.Fake: hosted-safe deterministic provider and fault injection.
- tests/: Unit, Contract, FakeCad and opt-in LiveSolidWorks evidence.

Architecture tests must fail if any non-provider production project references SolidWorks.Interop.* or if product projects reference tests/testing projects.

## Required commands

From the repository root:

    powershell -ExecutionPolicy Bypass -File .\scripts\build-hosted.ps1
    powershell -ExecutionPolicy Bypass -File .\scripts\Invoke-SolidWorksMcpDoctor.ps1
    powershell -ExecutionPolicy Bypass -File .\scripts\sync-references.ps1

The doctor is read-only by default. Use -Initialize only to create user-local configuration under %LOCALAPPDATA%\SolidWorksMcp; never commit generated properties or machine paths.

For real local Live testing, use `scripts/Invoke-SolidWorksLiveTests.ps1`. It is the only supported fresh-process harness:
it gracefully closes every currently running `SLDWORKS.exe` before launching one new process, passes that exact PID to the
Live assembly, and requests graceful exit after the run. It never calls `Stop-Process`, never kills an unresponsive process,
and never answers an unknown modal dialog. If cleanup reports `BLOCKED_HUMAN_ACTION_REQUIRED`, inspect the visible
SOLIDWORKS dialog manually before retrying; do not start another session. 真实本机 Live 测试必须使用该脚本：它在启动
新 SOLIDWORKS 前正常关闭旧进程，测试结束后关闭本轮自有进程；遇到未知模态框或未保存提示会阻断而不是盲点确认。

Native persisted-artifact writes are deny-by-default. Configure `pathAllowlist.roots` in the user-local runtime config,
keep the roots dedicated to generated/test CAD, and require the Provider to validate the target again immediately before
`SaveAs3`. Never weaken the policy by accepting relative paths, string-prefix-only containment, or an MCP caller's
request as proof of authorization. 原生持久化 artifact 写入默认拒绝；在用户本地配置中显式设置
`pathAllowlist.roots`，根目录应专用于生成/测试 CAD，Provider 必须在 `SaveAs3` 前再次校验。禁止用相对路径、
仅字符串前缀判断，或 MCP caller 的请求本身替代授权证明。

## Safety and release rules

All future mutating CAD operations must use the transaction engine: plan, preflight, acquire single writer, verify target/state, checkpoint, execute, rebuild, inspect, verify invariants, commit or rollback. High-risk checkpoint failure is fail-closed. Unknown SOLIDWORKS modal dialogs return a human-action-required state; never blind-click.

The MCP surface stays compact and high-level. Low-level CAD primitives are internal/debug-tier operations with explicit read/write/destructive metadata. Do not add a generic eval, PowerShell or macro execution escape hatch.

## Private drawing fixture policy / 私密图纸 fixture 规则

The local `图纸/` directory is a confidential, user-supplied reference corpus. It is ignored by Git and must never be
copied, renamed into a tracked file, embedded as a test resource, uploaded, attached to an Issue/PR, rendered into a
committed image, or quoted in logs, comments, commit messages or final reports. The same prohibition applies to title
block text, part numbers, customer names, exact dimensions, screenshots and extracted PDF text unless the user explicitly
authorizes publication.  `图纸/` 是用户提供的机密参考 corpus，已由 Git 忽略；禁止复制、改名后纳入 tracked file、嵌入
测试资源、上传、附加到 Issue/PR、提交渲染图片，或把标题栏、料号、客户名、精确尺寸、截图、PDF 提取文本写入日志、
评论、commit message 或最终报告。除非用户明确授权，不得公开任何派生原文。

Each local reference-driven iteration must randomly sample exactly two PDFs, classify them from document content/layout,
and use only single-part drawings until the assembly drawing compiler is explicitly enabled. A sample is not a passing
test merely because it renders: record only redacted slot IDs and engineering feature classes (for example, orthographic
coverage, hole pattern, section/detail need, tolerance/provenance and title-block requirements). Do not use a filename or
an assembly-looking title as CAD model classification. 每轮本地参考驱动迭代必须随机抽取恰好两张 PDF，依据内容/版式分类；在
明确开启总成编译器前，只允许单零件图。渲染成功不等于测试通过；只记录脱敏 slot ID 和工程特征类别（如正投影视图覆盖、
孔系、剖视/局部放大需求、公差来源和标题栏要求）。不得依据文件名或标题外观判定 CAD 模型类型。

Use the private drawing sampling skill/script for local review. Keep the random selection manifest, extracted text, rendered
PNG/PDF and any CAD artifacts under user-local temp/evidence folders, never under tracked `docs/`, `tests/`, `references/`
or `artifacts/`. Every generated requirement must carry `provenance=private_drawing_review` and remain a proposal until
validated against provider inspection or human approval. 使用私密图纸抽样 skill/script 做本地复核；随机 manifest、提取文本、PNG/PDF
和 CAD artifact 必须留在 user-local temp/evidence 目录，不能写入 tracked 目录。派生工程要求必须标注
`provenance=private_drawing_review`，在 Provider inspection 或人工批准前只能作为 proposal。
