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

## Safety and release rules

All future mutating CAD operations must use the transaction engine: plan, preflight, acquire single writer, verify target/state, checkpoint, execute, rebuild, inspect, verify invariants, commit or rollback. High-risk checkpoint failure is fail-closed. Unknown SOLIDWORKS modal dialogs return a human-action-required state; never blind-click.

The MCP surface stays compact and high-level. Low-level CAD primitives are internal/debug-tier operations with explicit read/write/destructive metadata. Do not add a generic eval, PowerShell or macro execution escape hatch.
