# SolidWorksMcp

SolidWorksMcp is a planned .NET 10/C# enterprise MCP server and deterministic Engineering Drawing Compiler for licensed, locally installed SOLIDWORKS. The project is intentionally being built in small, testable Issue-driven increments.

## Current bootstrap status

The repository now contains the approved module boundaries, a Hosted-safe solution filter, architecture guards, pinned research manifest, licensing policy, AGENTS instructions, a read-only Windows doctor and the official C# MCP stdio host. The host exposes a compact `cad.health`, `cad.capabilities`, `cad.create-part` and `cad.inspect` registry. On a doctor-enabled Windows machine, the full solution can opt into the native SOLIDWORKS provider; the default build remains vendor-free and fails closed when native mode is requested without that opt-in.

The MCP host keeps stdout reserved for the protocol wire and sends console logs to stderr. Its mutation boundary advertises flat, versioned input schemas, validates the schema before starting a CAD session, and returns the typed operation envelope as structured content. Contract tests exercise the official SDK client against FakeCad; they do not claim a live SOLIDWORKS COM result.

Runtime configuration follows defaults → user-local `%LOCALAPPDATA%\SolidWorksMcp\config.json` (or `SOLIDWORKS_MCP_CONFIG`) → allowlisted `SOLIDWORKS_MCP_*` variables → allowlisted CLI options. Supported switches are provider mode, the default-off `experimental.drawing` and `experimental.recognition` flags, and the native CAD artifact `pathAllowlist.roots`. Native create operations deny all paths when no roots are configured; the doctor adds only user-local output/test roots. Machine paths remain outside the repository and are not included in MCP capability payloads.

Read the Issue evidence baseline before selecting work. The critical path is defined by GitHub Epic #1: #2 -> (#7,#8,#9,#10) -> #3 -> #4 -> #5 -> (#6,#11).

## Hosted-safe build

Requires the .NET 10 SDK and Git. SOLIDWORKS is not required for the hosted solution.

    powershell -ExecutionPolicy Bypass -File .\scripts\build-hosted.ps1

Equivalent direct commands are restore, dotnet format --verify-no-changes, Release build and test against SolidWorksMcp.hosted.slnx. The complete solution is SolidWorksMcp.slnx; it includes the Windows provider and opt-in Live test project for local qualification.

The Hosted script forces `SolidWorksMcpNativeProviderEnabled=false` and writes project-scoped ignored artifacts below `artifacts/hosted`, so a Hosted-safe build cannot overwrite local native assets. After running the Windows doctor with `-Initialize`, build the complete solution on the local Windows machine to produce the opt-in `net10.0-windows` server and provider sidecar. The doctor-generated MCP configuration points at that native executable; it never starts or stops `SLDWORKS.exe` automatically.

## Windows doctor

The doctor detects .NET/Git, SOLIDWORKS installations and running sessions, API redist/type-library candidates, templates and safe user-local test/output roots. It does not modify global registry or settings and is read-only unless -Initialize is supplied.

    powershell -ExecutionPolicy Bypass -File .\scripts\Invoke-SolidWorksMcpDoctor.ps1
    powershell -ExecutionPolicy Bypass -File .\scripts\Invoke-SolidWorksMcpDoctor.ps1 -Initialize -Json

Generated files live under %LOCALAPPDATA%\SolidWorksMcp and are ignored by Git. SOLIDWORKS vendor DLLs are discovered from the local installation; they are never copied into this repository.

The native server accepts only the user-local CAD output/test roots written by the doctor until an operator explicitly adds another allowlisted root. A native create request outside that policy returns `PATH_NOT_ALLOWED`; the allowlist is not serialized into MCP capability payloads.

## Reference corpus and provenance

Refresh the exact upstream pins with:

    powershell -ExecutionPolicy Bypass -File .\scripts\sync-references.ps1

See references/manifest.json, THIRD_PARTY_NOTICES.md and research Issues #55-#59 for the license/provenance rules. Reference source is local-only and excluded from product build.

## Architecture boundary

Only providers/SolidWorksMcp.Provider.SolidWorks may reference SolidWorks.Interop.*. Core, Server, semantic graphs, RuleEngine, Tolerancing and Drawing Compilers depend on vendor-neutral contracts. The Unit architecture tests intentionally scan project files to keep this boundary enforceable.

## Development workflow

Follow AGENTS.md and the scoped instructions under src/, providers/, tests/ and scripts/. A future mutation is required to use the transaction lifecycle plan -> preflight -> single writer -> target/state verification -> checkpoint -> execute -> rebuild -> inspect -> verify -> commit/rollback. Live results must be reported separately from Hosted-safe results.
