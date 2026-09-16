# SolidWorksMcp

SolidWorksMcp is a planned .NET 10/C# enterprise MCP server and deterministic Engineering Drawing Compiler for licensed, locally installed SOLIDWORKS. The project is intentionally being built in small, testable Issue-driven increments.

## Current bootstrap status

The repository now contains the approved module boundaries, a Hosted-safe solution filter, architecture guards, pinned research manifest, licensing policy, AGENTS instructions and a read-only Windows doctor. SOLIDWORKS COM mutation and the MCP tool host are not claimed complete yet; they are tracked by Issues #13 and #17-#22.

Read the Issue evidence baseline before selecting work. The critical path is defined by GitHub Epic #1: #2 -> (#7,#8,#9,#10) -> #3 -> #4 -> #5 -> (#6,#11).

## Hosted-safe build

Requires the .NET 10 SDK and Git. SOLIDWORKS is not required for the hosted solution.

    powershell -ExecutionPolicy Bypass -File .\scripts\build-hosted.ps1

Equivalent direct commands are restore, dotnet format --verify-no-changes, Release build and test against SolidWorksMcp.hosted.slnx. The complete solution is SolidWorksMcp.slnx; it includes the Windows provider and opt-in Live test project for local qualification.

## Windows doctor

The doctor detects .NET/Git, SOLIDWORKS installations and running sessions, API redist/type-library candidates, templates and safe user-local test/output roots. It does not modify global registry or settings and is read-only unless -Initialize is supplied.

    powershell -ExecutionPolicy Bypass -File .\scripts\Invoke-SolidWorksMcpDoctor.ps1
    powershell -ExecutionPolicy Bypass -File .\scripts\Invoke-SolidWorksMcpDoctor.ps1 -Initialize -Json

Generated files live under %LOCALAPPDATA%\SolidWorksMcp and are ignored by Git. SOLIDWORKS vendor DLLs are discovered from the local installation; they are never copied into this repository.

## Reference corpus and provenance

Refresh the exact upstream pins with:

    powershell -ExecutionPolicy Bypass -File .\scripts\sync-references.ps1

See references/manifest.json, THIRD_PARTY_NOTICES.md and research Issues #55-#59 for the license/provenance rules. Reference source is local-only and excluded from product build.

## Architecture boundary

Only providers/SolidWorksMcp.Provider.SolidWorks may reference SolidWorks.Interop.*. Core, Server, semantic graphs, RuleEngine, Tolerancing and Drawing Compilers depend on vendor-neutral contracts. The Unit architecture tests intentionally scan project files to keep this boundary enforceable.

## Development workflow

Follow AGENTS.md and the scoped instructions under src/, providers/, tests/ and scripts/. A future mutation is required to use the transaction lifecycle plan -> preflight -> single writer -> target/state verification -> checkpoint -> execute -> rebuild -> inspect -> verify -> commit/rollback. Live results must be reported separately from Hosted-safe results.
