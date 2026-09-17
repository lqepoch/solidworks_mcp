# GitHub Issue evidence baseline

Retrieved from the public repository on 2026-09-16 (Asia/Shanghai) using the GitHub Issue API and issue pages. The issue bodies and the root Epic comment are the authoritative scope; this file is an index and execution record, not a replacement for them.

- Repository: https://github.com/lqepoch/solidworks_mcp
- Root Epic: https://github.com/lqepoch/solidworks_mcp/issues/1
- Issue count at retrieval: 69 open issues

## Current evidence override / 当前证据覆盖记录

This section records the latest superseding evidence for the narrow native export and process-lifecycle slice. Older
historical entries below are retained for chronology; when they say native export is unsupported or that B03 did not
close the owned process, this section is the current state.

本节记录窄版 native export 和进程生命周期切片的最新覆盖证据。下方旧条目保留历史时间线；其中关于 native export
仍 unsupported、或 B03 不关闭 owned process 的描述，均以本节当前状态为准。

- Curved-profile implementation commit: `8ccd7d2` (`feat: support native curved sketch profiles`), verified by the
  Hosted-safe and fresh-process Live evidence recorded below.
- The current reference-driven iteration sampled exactly two local, single-part drawing candidates through the private
  fixture skill. Only generic classes were retained: curved/rounded plate family, orthographic/isometric views, hole
  features, thickness, tolerance/title-block consideration. No private PDF content, values, names or rendered images
  entered the repository, build inputs, logs, Issues or artifacts.
- `CreatePartRequest.InitialSketchProfile` now carries a vendor-neutral, validated connected profile made from lines
  and three-point arcs. The native provider maps it to `ISketchManager.CreateLine`/`Create3PointArc` on the Provider STA,
  then creates a real associative sketch dimension before extrusion so `InsertModelAnnotations3` can import native
  marked-for-drawing evidence. No display dimension is synthesized from request text.
- B03 now creates a D-shaped curved plate, changes a named extrusion dimension, cuts one real through-hole and saves
  a native part, drawing, STEP and PDF. B04 now creates a rounded plate, a semantic two-instance hole group, a native
  model dimension and a native note, then saves/reopens the part and drawing.
- The latest one-process fresh SOLIDWORKS run used the exact harness below and completed `Live 9 passed, 1 skipped,
  0 failed`; the skipped test is the explicit placeholder. Unit/Contract/FakeCad completed `65/7/8 passed` with no
  failures, and the post-run process inventory was `SLDWORKS_COUNT=0`.
- Retained local evidence (outside Git) includes non-empty B03 `.SLDPRT`, `.SLDDRW`, STEP and PDF plus B04 `.SLDPRT`,
  `.SLDDRW` and PDF under the isolated user-local test workspace. The latest B04 PDF rendered as one non-blank page;
  visual review showed the rounded plate, two holes, isometric view and native model dimension, and bundled `pypdf`
  extraction confirmed the rendered dimension text exists. These are real SOLIDWORKS 2022 artifacts and are not public
  fixtures.
- D04 native detail-view evidence is now real rather than label-only. The rounded-plate PDF shows the source detail circle
  around a native hole in Front and the enlarged hole/center marks in Detail A. The provider records the paper-to-active-view
  coordinate conversion, native `DetailCircle` parent/base binding, and projected geometry counts; the latest retained run
  read back `detail-polyline-count=2` and finished with `SLDWORKS_COUNT_AFTER=0`.
- Fresh-process command and result:

      `$env:SOLIDWORKS_MCP_LIVE_KEEP_ARTIFACT='1'; powershell -ExecutionPolicy Bypass -File .\scripts\Invoke-SolidWorksLiveTests.ps1 -RepositoryRoot D:\Program\solidworks_mcp -Workspace C:\Users\lqepo\AppData\Local\SolidWorksMcp\test-workspace -SolidWorksPath D:\Solidworks2022\SOLIDWORKS\SLDWORKS.exe -NoBuild`

      `exit 0; Unit 65 passed; Contract 7 passed; FakeCad 8 passed; Live 9 passed; 1 explicit placeholder skipped; 0 failed`
- Native provider now exposes a bounded `ICadExportService`: `PDF` for drawings and `STEP`/`IGES`/`STL` for parts/assemblies.
- `SolidWorksNativeExportService` performs path allowlist validation, document identity/state verification, selection
  clearing, `IModelDocExtension.SaveAs`, Boolean/error-code checks, non-empty output proof and source state-hash recheck.
- Official API evidence: `IModelDocExtension.SaveAs` signature and conversion behavior are recorded in
  `docs/research/solidworks-api-knowledge.md`, cross-checked against the [official API page](https://help.solidworks.com/2023/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IModelDocExtension~SaveAs.html).
- Fresh-process local Live command:

      powershell -ExecutionPolicy Bypass -File .\scripts\Invoke-SolidWorksLiveTests.ps1 -RepositoryRoot D:\Program\solidworks_mcp -Workspace C:\Users\lqepo\AppData\Local\SolidWorksMcp\test-workspace -SolidWorksPath D:\Solidworks2022\SOLIDWORKS\SLDWORKS.exe -NoBuild -Filter FullyQualifiedName~B03NativePartLiveTests

      # exit 0; Live 1 passed; 0 failed; 0 skipped; elapsed about 136 seconds

- The run started one fresh `SLDWORKS.exe` after graceful old-process handling, created native `.SLDPRT` and `.SLDDRW`,
  exported non-empty STEP and PDF files, reopened and inspected the drawing, then closed the exact owned process.
  Post-run inventory: `SLDWORKS_COUNT=0`. Successful-test cleanup removed only this run's four generated files from the
  isolated user-local workspace after verification. This is real SOLIDWORKS 2022 evidence, not FakeCad evidence.
- Hosted-safe build/test remains required after this evidence update. No complete B06, AutoDrawing compiler, drawing
  release, assembly or full Epic completion claim is made.

## MCP structured-profile contract evidence (partial)

The MCP boundary now exposes one bounded `initialSketchProfileJson` property on `cad.create-part`. The field accepts
only a connected, closed line/three-point-arc profile in canonical millimetres; malformed JSON, non-finite points,
unsupported curve kinds, open loops and disconnected segments fail before provider session startup. The server codec
contains no SOLIDWORKS COM reference and delegates topology validation to the shared `CadAbstractions` validator.

The FakeCad provider retains a privacy-safe profile summary in create/inspect evidence and state material (primitive
counts plus a SHA-256 digest), so acceptance cannot be satisfied by accepting then dropping the profile. Raw source
drawing coordinates are not emitted by this evidence path. This is a schema/provider contract slice; it does not claim
that `cad.create-part` itself is already a high-level drawing compiler.

Evidence command and result for this slice:

    dotnet format SolidWorksMcp.slnx --verify-no-changes --no-restore --verbosity minimal # exit 0
    dotnet build SolidWorksMcp.slnx -c Release --no-restore -p:SolidWorksInstallRoot='D:\\Solidworks2022\\SOLIDWORKS' # exit 0; 0 warnings; 0 errors
    dotnet test tests/SolidWorksMcp.ContractTests/SolidWorksMcp.ContractTests.csproj -c Release --no-build --no-restore --verbosity minimal # exit 0; 9 passed; 0 skipped
    dotnet test tests/SolidWorksMcp.FakeCadTests/SolidWorksMcp.FakeCadTests.csproj -c Release --no-build --no-restore --verbosity minimal # exit 0; 10 passed; 0 skipped

The next native step must invoke this MCP tool against the same one-process Live harness and then prove the resulting
part/drawing artifacts. Every Live invocation continues to close stale SOLIDWORKS processes before starting exactly one
fresh owned process; an existing user session is never reused implicitly.

## MCP-to-native part drawing evidence (partial)

`cad.build-part-drawing` is now the first high-level compiler-facing operation. Its bounded workflow is:

    validated profile -> native part -> verified extrusion -> persisted part
    -> Front/Top/Isometric views -> native Model Dimension insertion
    -> persisted drawing reopen -> PDF export

The orchestration lives in `src/SolidWorksMcp.AutoDrawing/PartDrawingBuildService.cs`; the MCP layer only validates the
compact request and starts the exact session. It does not expose one MCP tool per COM primitive. The live test binds
`CadSessionOptions.RequestedProcessId` to the harness-owned PID, so it cannot silently attach to another ROT session.

Fresh-process Live evidence:

    `$env:SOLIDWORKS_MCP_LIVE_KEEP_ARTIFACT='1'; powershell -ExecutionPolicy Bypass -File .\\scripts\\Invoke-SolidWorksLiveTests.ps1 -RepositoryRoot D:\\Program\\solidworks_mcp -Workspace C:\\Users\\lqepo\\AppData\\Local\\SolidWorksMcp\\test-workspace -SolidWorksPath D:\\Solidworks2022\\SOLIDWORKS\\SLDWORKS.exe -NoBuild -Filter FullyQualifiedName~McpBuildPartDrawingLiveTests`

    `exit 0; Live 1 passed; 0 failed; 0 skipped; elapsed 1m08s`

The run produced non-empty native `.SLDPRT` (67,894 bytes), `.SLDDRW` (35,097 bytes) and PDF (14,561 bytes) artifacts
in the isolated user-local workspace. The PDF has one page; rendered visual inspection showed the generated D-shaped
front view, isometric solid and native `40.00` dimension. The harness reported `SLDWORKS_COUNT=0` after graceful
shutdown. These are generic generated artifacts only; no private source drawing or private dimensions entered Git,
logs, Issues or public evidence. This proves the 3D-to-2D MCP/native path for the current bounded profile slice, not the
full requirement graph, tolerance engine, drawing QA or release gate.

Hosted-safe follow-up after this change: `scripts/build-hosted.ps1` exited 0 with 0 warnings/errors; Unit 65,
Contract 10 and FakeCad 10 passed, with no SOLIDWORKS dependency.

The private drawing sampler was rerun for this reference-driven iteration and selected exactly two single-part
candidates. Both slots were rendered locally and retained only as `single-part-candidate review-required`; source
filenames, PDF text, exact dimensions, title-block values and renders remain private and untracked.

The subsequent complete fresh-process harness run exited 0 with Unit 65 passed, Contract 10 passed, FakeCad 10 passed,
Live 10 passed, one explicit placeholder skipped and zero failed. It started one owned SOLIDWORKS process after stale
process cleanup and finished with `SLDWORKS_COUNT=0`.

## Authoritative execution DAG

The root Epic comment defines this critical path:

    #2 -> (#7, #8, #9, #10) -> #3 -> #4 -> #5 -> (#6, #11) -> release qualification

The same comment permits #4 to begin after A04/A03 while native extraction waits for #3; #7, #8, #9 and #10 start early; #8 grows with every real COM capability. A parent closes only after all children are complete or explicitly moved out of scope with a documented replacement. A skipped Live test is unqualified, never passed.

## Parent work packages

| Issue | Package | Scope / acceptance anchor |
| --- | --- | --- |
| #2 | P1 architecture/bootstrap | .NET 10 solution, MCP/provider boundary, hosted-safe build, architecture guard |
| #3 | P2 SOLIDWORKS provider | x64 typed COM provider, STA/session identity, diagnostics and verifiable 3D operations |
| #4 | P3 engineering intelligence | immutable semantic graphs, RulePack, tolerance, provenance and explainability |
| #5 | P4 part AutoDrawing | deterministic analyze/plan/generate/validate/repair/release compiler |
| #6 | P5 assembly drawing | assembly semantics, BOM/ItemIdentity, balloons, views and interface dimensions |
| #7 | P6 safety/governance | transaction, checkpoint/rollback, identity hashes, bounds, audit and release gate |
| #8 | P7 test/CI/release | Hosted CI, FakeCad/contract tests, trusted Live suite, packaging and security |
| #9 | P8 research/provenance | pinned references, API corpus, benchmark and license policy |
| #10 | P9 Codex UX | compact MCP tools, AGENTS workflow, doctor/bootstrap, observability and docs |
| #11 | P10 imported geometry | normalized B-Rep, recognition, ambiguity/review and drawing readiness |

## Child issue inventory

| Issue | Parent | Acceptance focus |
| --- | --- | --- |
| #12 A01 | #2 | solution/project boundaries, analyzers, architecture tests |
| #13 A02 | #2 | official MCP stdio host, DI, registry, schemas |
| #14 A03 | #2 | CadAbstractions and behaviorally faithful FakeCad |
| #15 A04 | #2 | explicit units, IDs, result envelopes, error/schema versions |
| #16 A05 | #2 | layered config, feature flags, capability negotiation, compatibility |
| #17 B01 | #3 | installed SOLIDWORKS/API/template discovery and generated local build inputs |
| #18 B02 | #3 | x64 STA session, ROT attach, single writer and lifecycle recovery |
| #19 B03 | #3 | document/sketch/feature/body/configuration/material/property services |
| #20 B04 | #3 | assembly components, mates, interference and large-assembly state |
| #21 B05 | #3 | declarative selection, persistent refs, stale topology handling |
| #22 B06 | #3 | inspection, rebuild/What's Wrong, modal detection, screenshot/export/errors |
| #23 C01 | #4 | graph schemas, immutable entities, serialization and invariants |
| #24 C02 | #4 | native FeatureManager/PMI/DimXpert/pattern semantic extraction |
| #25 C03 | #4 | versioned GB/enterprise RulePack and override provenance |
| #26 C04 | #4 | tolerance model, IDimensionTolerance mapping, 100-to-98 case |
| #27 C05 | #4 | deterministic assembly stack-up, clearance/interference and datum chains |
| #28 C06 | #4 | provenance, review/approval, explanations and release eligibility |
| #29 D01 | #5 | template/sheet/scale/projection/view planner with rationale |
| #30 D02 | #5 | associative Model Items/PMI and controlled AutoDimension fallback |
| #31 D03 | #5 | pattern/mirror/symmetry semantic compression |
| #32 D04 | #5 | section/detail/auxiliary/multi-sheet planning |
| #33 D05 | #5 | dimension planner, datum strategy and coverage graph |
| #34 D06 | #5 | deterministic layout, collisions, leaders, tiers and reflow |
| #35 D07 | #5 | hole/thread, center, GD&T, datum, surface, weld and notes |
| #36 D08 | #5 | drawing QA/repair/release and export evidence |
| #37 E01 | #6 | component/configuration identity, BOM classification and patterns |
| #38 E02 | #6 | native BOM, property mapping and stable ItemIdentity |
| #39 E03 | #6 | balloons, stable numbering, leaders and multi-sheet coverage |
| #40 E04 | #6 | assembly views and functional/interface/travel dimensions |
| #41 E05 | #6 | mirrors/opposite-hand, patterns, weldments/cut lists and edge cases |
| #42 E06 | #6 | assembly drawing QA/release gates |
| #43 F01 | #7 | whitelisted transaction plan, preconditions, idempotency and budgets |
| #44 F02 | #7 | fail-closed checkpoints, manifests, rollback and restore evidence |
| #45 F03 | #7 | target/state hashes, path allowlist, overwrite and writer conflicts |
| #46 F04 | #7 | bounded jobs, cancellation, hang/modal containment and finite recovery |
| #47 F05 | #7 | audit, metrics, error/remediation catalog and privacy-safe bundles |
| #48 F06 | #7 | release governance, approvals, evidence and destructive-save policy |
| #49 G01 | #8 | hosted-safe solution filter, GitHub Actions and test reports |
| #50 G02 | #8 | provider contracts, FakeCad scenarios, fault injection and evidence schema |
| #51 G03 | #8 | local Live harness, isolated workspace, geometry assertions and cleanup |
| #52 G04 | #8 | golden fixtures and measurable semantic regression metrics |
| #53 G05 | #8 | trusted Live workflow with public-fork isolation |
| #54 G06 | #8 | Windows packaging, rollback, SBOM/notices/checksums and compatibility |
| #55 H01 | #9 | reproducible local references, exact pins and upstream manifest |
| #56 H02 | #9 | source-level capability/design-gap matrix |
| #57 H03 | #9 | searchable official API/type-library knowledge corpus |
| #58 H04 | #9 | dated benchmark of official 2026 Auto-Generate Drawing/LEO |
| #59 H05 | #9 | licensing/provenance policy and proprietary binary exclusion |
| #60 I01 | #10 | compact high-level tools, tiers/search and capability filtering |
| #61 I02 | #10 | root/scoped AGENTS and issue/test/provenance evidence discipline |
| #62 I03 | #10 | Windows bootstrap/doctor and user-local MCP/build configuration |
| #63 I04 | #10 | health/capability/status, observability and redacted support bundles |
| #64 I05 | #10 | end-user/developer docs and Codex workflow examples |
| #65 J01 | #11 | normalized B-Rep/topology graph and geometric signatures |
| #66 J02 | #11 | deterministic imported feature recognition with evidence/confidence |
| #67 J03 | #11 | imported repetition/mirror/symmetry/datum candidates |
| #68 J04 | #11 | ambiguity sets, human review and safe propagation |
| #69 J05 | #11 | STEP/IGES fixtures, AutoDrawing integration and readiness metrics |

## Initial implementation record

This baseline intentionally starts with the requested order: #12, #55, #59, #61, #62 and #49. It creates structure and evidence controls only; no SOLIDWORKS COM behavior is claimed until provider research, API and Live-test gates are implemented.

## A04 local implementation evidence

Issue #15/A04 is implemented locally in commit `af4c662`. The implementation is limited to vendor-neutral protocol contracts:

- `src/SolidWorksMcp.Protocol/EngineeringUnits.cs` defines explicit `Length`, `Angle`, `Mass`, `Area`, `Volume`, `Tolerance`, `DimensionLimits`, `Coordinate2D` and `Coordinate3D` values. MCP/domain values are mm, degrees, kg, mm² and mm³; metre/radian conversions are explicit provider-boundary methods.
- `src/SolidWorksMcp.Protocol/Identifiers.cs` defines typed stable identities for sessions, documents, features, bodies, views, annotations, BOM items, transactions and idempotency keys.
- `src/SolidWorksMcp.Protocol/OperationContracts.cs` defines schema/version fields, stable error codes/categories, immutable evidence observations and generic success/failure envelopes.
- `tests/SolidWorksMcp.UnitTests/EngineeringUnitsTests.cs` covers 1000x, squared/cubed and radian conversion traps, invalid physical values, tolerance invariants and JSON unit shape.
- `tests/SolidWorksMcp.UnitTests/OperationContractTests.cs` covers success/error evidence, stable error codes, schema serialization and typed identifier equality.
- `tests/SolidWorksMcp.UnitTests/ArchitectureBoundaryTests.cs` prevents engineering-layer public APIs from declaring raw dimensional `double` values.

Evidence command and result:

    dotnet format SolidWorksMcp.hosted.slnx --no-restore --verify-no-changes --severity info   # exit 0
    dotnet build SolidWorksMcp.hosted.slnx -c Release --no-restore                             # exit 0; 0 warnings; 0 errors
    dotnet test SolidWorksMcp.hosted.slnx -c Release --no-build --logger "trx;LogFileName=a04-final.trx" # exit 0; 23 passed; 0 failed; 0 skipped

No Live SOLIDWORKS test was used for A04; this issue is vendor-neutral and the Live harness remains explicitly skipped under #51.

## A03 local implementation evidence

Issue #14/A03 is implemented locally in commit `ab0f313`.

- `src/SolidWorksMcp.CadAbstractions/ICadProvider.cs` defines vendor-neutral session, document, part, assembly, drawing, inspection and export interfaces. Only typed Protocol units/identities cross the boundary; no COM type is present.
- `src/SolidWorksMcp.CadAbstractions/CadCapabilities.cs`, `CadRequests.cs` and `CadSnapshots.cs` define explicit capability declarations, deterministic requests, stable identities, load states and evidence-bearing snapshots.
- `testing/SolidWorksMcp.Provider.Fake/` implements the same interfaces with deterministic session/document state, body/feature/component/mate/view/annotation identities, stable SHA-256 state hashes and explicit rebuild/save/inspect/export receipts.
- `FakeCadFailureInjector` provides thread-safe one-shot failures at session, document, part, assembly, drawing, rebuild, save, inspect and export operation points. Unsupported capabilities return `UNSUPPORTED_CAPABILITY` rather than silently doing nothing.
- `tests/SolidWorksMcp.ContractTests/CadProviderContractSuite.cs` is the reusable provider contract. Its part→body→extrusion→rebuild→inspect→drawing→view→annotation→inspect→export→save workflow currently runs against FakeCad.
- `tests/SolidWorksMcp.FakeCadTests/FakeCadProviderTests.cs` covers failure recovery, unsupported capability behavior and assembly loading/mate identity.

Evidence command and result:

    dotnet format SolidWorksMcp.hosted.slnx --no-restore --verify-no-changes --severity info   # exit 0
    dotnet build SolidWorksMcp.hosted.slnx -c Release --no-restore                             # exit 0; 0 warnings; 0 errors
    dotnet test SolidWorksMcp.hosted.slnx -c Release --no-build --logger "trx;LogFileName=a03-final.trx" # exit 0; 27 passed; 0 failed; 0 skipped

Live SOLIDWORKS was not used for A03; the native implementation and shared-suite execution are deferred to #17 and #51.

## A02 local implementation evidence

Issue #13/A02 is implemented locally in commit `a440996`.

- `src/SolidWorksMcp.Server/Program.cs` uses the official Model Context Protocol C# SDK with the standard DI host and stdio transport. Console logging is directed to stderr so stdout remains an unpolluted MCP wire.
- `src/SolidWorksMcp.Server/ServerComposition.cs` registers an explicitly supplied provider for tests/native hosting, or a safe unavailable provider that reports `UNSUPPORTED_CAPABILITY` instead of pretending that SOLIDWORKS is connected.
- `src/SolidWorksMcp.Server/McpToolCatalog.cs` keeps the default agent-facing surface compact and records tier, preconditions, side effects and required capability for each tool. The current registry contains `cad.health`, `cad.capabilities`, `cad.create-part` and `cad.inspect`.
- `src/SolidWorksMcp.Server/CadMcpTools.cs` validates the protocol schema version and required identities before asking `CadSessionAccessor` to start a provider session. Tool input is intentionally represented as flat top-level parameters because the official SDK maps reflected method parameters directly into the advertised MCP schema.
- `src/SolidWorksMcp.Server/CadSessionAccessor.cs` serializes lazy session startup and owns session disposal for the host lifetime. It is deliberately not a substitute for the STA/transaction engine required by Issues #18 and #43-#48.
- `tests/SolidWorksMcp.ContractTests/McpServerIntegrationTests.cs` uses the official SDK `McpClient` over paired in-memory streams and FakeCad to verify handshake, `tools/list` metadata, structured results, malformed-input rejection before provider execution and one valid controlled mutation path.

Evidence command and result:

    dotnet restore SolidWorksMcp.hosted.slnx                                                                  # exit 0
    dotnet format SolidWorksMcp.hosted.slnx --no-restore --verify-no-changes --severity info                 # exit 0
    dotnet build SolidWorksMcp.hosted.slnx -c Release --no-restore                                           # exit 0; 0 warnings; 0 errors
    dotnet test SolidWorksMcp.hosted.slnx -c Release --no-build --logger "console;verbosity=normal"          # exit 0; 30 passed; 0 failed; 0 skipped

The 30 Hosted-safe tests are 22 Unit, 4 FakeCad and 4 Contract tests (including the reusable provider workflow and three MCP SDK integration tests). No Live SOLIDWORKS test was used for A02; native COM behavior remains explicitly deferred to Issues #17-#22 and the local Live harness in #51.

## A05 local implementation evidence

Issue #16/A05 is implemented locally in commit `1868706`.

- `src/SolidWorksMcp.Core/RuntimeConfiguration.cs` defines the versioned user-local configuration contract, allowlisted provider modes, default-off experimental drawing/recognition flags, four-layer precedence (defaults → user-local JSON → environment → CLI), field-source labels and fail-closed handling for unknown CLI values or explicitly missing configuration files. `CadPathAllowlist` is loaded separately from the safe MCP configuration snapshot, denies all native creates when no root is configured, and rejects relative/out-of-root/wrong-extension targets before provider I/O. Effective MCP snapshots contain no machine path or secret.
- `src/SolidWorksMcp.Protocol/ProtocolCompatibility.cs` centralizes schema identity, major/minor parsing and explicit backward-compatibility rules. Current/older compatible versions are accepted; different schema, major versions, future minor versions and malformed versions are rejected.
- `src/SolidWorksMcp.Server/McpCapabilityNegotiator.cs` evaluates provider declarations and feature flags without starting a CAD session. `cad.capabilities` exposes effective availability; mutation and inspection tools return deterministic `UNSUPPORTED_CAPABILITY` before session startup when a provider/tool combination is unavailable.
- `scripts/Invoke-SolidWorksMcpDoctor.ps1` now keeps the diagnostic inventory in `doctor.json` and creates a separate safe-default `config.json`; the generated MCP configuration points at the runtime file under `%LOCALAPPDATA%`.
- `tests/SolidWorksMcp.UnitTests/RuntimeConfigurationTests.cs` covers all layer precedence, default-off behavior, unknown environment isolation, unknown CLI rejection and missing explicit-file rejection. `ProtocolCompatibilityTests.cs` covers schema/version break cases.
- `tests/SolidWorksMcp.ContractTests/McpCompatibilityContractTests.cs` locks the default tool names/order and safety metadata, and proves a disabled experimental flag fails capability negotiation. The SDK integration suite also proves capability discovery has no session side effect and unsupported provider combinations fail before session startup.

Evidence command and result:

    dotnet format SolidWorksMcp.hosted.slnx --verify-no-changes --severity info --no-restore                 # exit 0
    dotnet build SolidWorksMcp.hosted.slnx -c Release --no-restore                                           # exit 0; 0 warnings; 0 errors
    dotnet test SolidWorksMcp.hosted.slnx -c Release --no-build --logger "console;verbosity=minimal"          # exit 0; 41 passed; 0 failed; 0 skipped
    powershell -ExecutionPolicy Bypass -File .\scripts\Invoke-SolidWorksMcpDoctor.ps1 -Initialize -Json       # exit 0; config.json and doctor.json generated under user-local root

No Live SOLIDWORKS test was used for A05; capability negotiation and configuration are vendor-neutral, while native provider behavior and the isolated Live harness remain deferred to Issues #17-#22 and #51.

## B01 local implementation evidence

Issue #17/B01 is implemented locally in commit `2600ee8`.

- `scripts/Invoke-SolidWorksMcpDoctor.ps1` discovers installations through SolidWorks and Windows uninstall registry locations plus standard Program Files roots, then reports executable version/revision, API redist, both Interop DLL paths, type libraries and templates. It reports `complete` or `partial` with actionable missing entries.
- Installation roots are sorted before probing, and results are sorted by parsed product version descending then executable path ascending. The selected installation is therefore deterministic when multiple SolidWorks versions are present.
- `Directory.Build.props` imports only the optional user-local `SolidWorksMcp.local.props` generated by the doctor. No machine path is committed, and the import is harmless when the file is absent on Hosted CI.
- `providers/SolidWorksMcp.Provider.SolidWorks/SolidWorksMcp.Provider.SolidWorks.csproj` conditionally adds strong Interop references from the discovered local API redist. References are restricted to the native provider project and vendor binaries remain outside source control.
- Doctor initialization now separates `doctor.json` (diagnostic inventory) from `config.json` (safe runtime defaults) and points the generated MCP configuration at the latter.

Evidence command and result:

    powershell -ExecutionPolicy Bypass -File .\scripts\Invoke-SolidWorksMcpDoctor.ps1 -Json                  # exit 0; 1 complete SOLIDWORKS 2022 installation
    powershell -ExecutionPolicy Bypass -File .\scripts\Invoke-SolidWorksMcpDoctor.ps1 -Initialize -Json    # exit 0; user-local config/props/mcp files generated
    dotnet build SolidWorksMcp.slnx -c Release --no-restore                                                    # exit 0; 0 warnings; 0 errors, including native provider reference resolution

The current machine reports SOLIDWORKS `30.0.0.5041` at `D:\Solidworks2022\SOLIDWORKS`, API redist `D:\Solidworks2022\SOLIDWORKS\api\redist`, `sldworks.tlb`, both Interop DLLs and 13 templates. No Live mutation is claimed by B01; COM session behavior is deferred to B02/#18.

## B02 local implementation evidence

Issue #18/B02 is implemented locally in commit `61c587b`.

- `providers/SolidWorksMcp.Provider.SolidWorks/StaComDispatcher.cs` owns one background thread configured as STA, uses a bounded FIFO queue, serializes all queued work, rejects direct non-STA access through an executable affinity guard, and cancels only work that has not started. It never aborts an in-flight COM call during shutdown.
- `providers/SolidWorksMcp.Provider.SolidWorks/RotSolidWorksConnector.cs` enumerates the Windows Running Object Table and accepts only live `ISldWorks` objects whose official `GetProcessID()` matches the requested process. An unqualified attach is rejected when more than one eligible process exists; the provider does not launch SOLIDWORKS or use arbitrary active-object lookup.
- `providers/SolidWorksMcp.Provider.SolidWorks/SolidWorksComSessionHost.cs` owns the process-bound RCW on the STA, records `sw:{pid}:{revision}` identity, verifies PID/revision before reuse, and releases only the provider RCW on detach. It deliberately does not call `ISldWorks.ExitApp()` on an interactive user session.
- `providers/SolidWorksMcp.Provider.SolidWorks/SolidWorksCadProvider.cs` and `SolidWorksCadSession.cs` expose the vendor-neutral provider contract while keeping all document/model/drawing capabilities explicitly unsupported until their dependent issues implement verified COM operations. No half-implemented mutation is advertised.
- `docs/research/solidworks-api-knowledge.md` records the locally reflected SOLIDWORKS 2022 signatures for `GetProcessID`, `RevisionNumber`, `UserControl`, `Visible`, `IFrameObject` and `ExitApp`, with official API Help links and runtime decisions. No upstream code was adapted for B02.
- `tests/SolidWorksMcp.LiveSolidWorksTests/StaDispatcherTests.cs` proves 32 concurrent requests execute with maximum concurrency 1 on one STA thread, direct affinity violations fail, queued cancellation prevents execution, and an impossible PID returns `NOT_FOUND` without launching a SOLIDWORKS process. The geometry placeholder remains an explicit skip.

Evidence command and result:

    dotnet format SolidWorksMcp.slnx --no-restore --verify-no-changes --severity info             # exit 0
    powershell -ExecutionPolicy Bypass -File .\scripts\build-hosted.ps1                         # exit 0; hosted build 0 warnings/0 errors; Unit 30 + Contract 7 + FakeCad 4 passed
    dotnet build SolidWorksMcp.slnx -c Release --no-restore                                     # exit 0; 0 warnings; 0 errors, including native provider
    dotnet test SolidWorksMcp.slnx -c Release --no-restore --no-build --logger "console;verbosity=minimal" # exit 0; 45 passed; 0 failed; 1 skipped
    powershell -ExecutionPolicy Bypass -File .\scripts\Invoke-SolidWorksMcpDoctor.ps1 -Json    # exit 0; one complete SOLIDWORKS 2022 installation; no running session

Live status: dispatcher/process-binding tests passed locally, but no real SOLIDWORKS document mutation or geometry
test passed. The existing explicit Live placeholder remains `skipped` under #51, and B02 does not claim the final
Codex → MCP → Provider → SLDWORKS geometry loop. On failure, revert commit `61c587b`; no customer CAD or vendor DLL
was modified or committed.

## B05 selection foundation evidence (partial)

Issue #21/B05 is partially implemented in commit `1347316`; the native document-bound resolver remains gated on B03.

- `src/SolidWorksMcp.CadAbstractions/DeclarativeSelection.cs` defines provider-neutral entity kinds, opaque persistent
  references, deterministic geometry signatures, semantic selectors, resolution methods and evidence-bearing selection
  snapshots. No global selection mark or enumeration index crosses this boundary.
- `src/SolidWorksMcp.CadAbstractions/ICadProvider.cs` exposes `ICadSelectionService`; `CadCapabilityNames.Selection`
  makes support explicit. Native B02 reports the capability unsupported until B03 registers document handles; FakeCad
  reports it supported.
- `testing/SolidWorksMcp.Provider.Fake/FakeCadSelectionService.cs` implements persistent-reference → geometry-signature
  → semantic-name precedence, validates resolved identity against inspection data, fingerprints selectors and returns
  `SELECTION_STALE` when `ExpectedStateHash` no longer matches after a mutation.
- `tests/SolidWorksMcp.FakeCadTests/FakeCadProviderTests.cs` covers semantic and geometry resolution plus stale-state
  rejection. The native `GetObjectByPersistReference3` adapter is intentionally not claimed until B03 supplies a
  document registry and a minimal real Live test.

Evidence command and result:

    dotnet format SolidWorksMcp.slnx --no-restore --verify-no-changes --severity info             # exit 0
    powershell -ExecutionPolicy Bypass -File .\scripts\build-hosted.ps1                         # exit 0; 0 warnings/0 errors; Unit 30 + Contract 7 + FakeCad 6 passed
    dotnet build SolidWorksMcp.slnx -c Release --no-restore                                     # exit 0; 0 warnings; 0 errors
    dotnet test SolidWorksMcp.slnx -c Release --no-restore --no-build --logger "console;verbosity=minimal" # exit 0; 47 passed; 0 failed; 1 skipped

Status is intentionally partial: the native selection capability remains `UNSUPPORTED_CAPABILITY` until document
identity, persistent-reference resolution and minimal real SOLIDWORKS verification are delivered with B03/B05 follow-up.

## F01 transaction foundation evidence (partial)

Issue #43/F01 is implemented locally in commit `2ea6409` as the first transaction-policy layer; checkpoint and
rollback are intentionally deferred to F02.

- `src/SolidWorksMcp.Core/CadTransactionEngine.cs` defines a serializable `CadTransactionPlan` with process/session,
  document, path, type, configuration and expected-state target binding; risk levels; allowlisted operation codes;
  explicit preconditions/invariants; finite provider-call, retry and timeout budgets; and evidence-bearing receipts.
- `CadTransactionPlanValidator` rejects unknown operation codes, missing identities, unbounded/invalid budgets and
  model-or-higher mutation plans without an expected state hash. No arbitrary delegate, eval, PowerShell or macro input
  is represented in the plan.
- `CadTransactionEngine` serializes duplicate idempotency keys, reconciles an existing committed receipt, retries only
  registered retryable failures within the finite budget, maps timeout/cancellation deterministically and verifies the
  named `provider.inspect` strategy before commit. It also rejects a handler response for a different operation code.
- `tests/SolidWorksMcp.UnitTests/CadTransactionEngineTests.cs` covers pre-handler allowlist rejection, finite timeout
  retry, concurrent idempotency reconciliation, non-committed invariant failure and provider-call budget exhaustion.

Evidence command and result:

    dotnet format SolidWorksMcp.slnx --no-restore --verify-no-changes --severity info             # exit 0
    powershell -ExecutionPolicy Bypass -File .\scripts\build-hosted.ps1                         # exit 0; 0 warnings/0 errors; Unit 35 + Contract 7 + FakeCad 6 passed
    dotnet build SolidWorksMcp.slnx -c Release --no-restore                                     # exit 0; 0 warnings; 0 errors
    dotnet test SolidWorksMcp.slnx -c Release --no-restore --no-build --logger "console;verbosity=minimal" # exit 0; 52 passed; 0 failed; 1 skipped

Status is intentionally partial: no real COM mutation has been routed through the engine yet. F02 recovery contracts
below provide the fail-closed boundary, but the native provider still has to supply a SOLIDWORKS-aware state verifier.
Revert `2ea6409` to remove F01 without touching user CAD.

## F02 checkpoint and recovery foundation evidence (partial)

Issue #44/F02 is implemented locally as a vendor-neutral recovery boundary. The implementation is deliberately split
between policy, an injectable provider coordinator, and a durable local file coordinator so Core never references a
SOLIDWORKS COM type.

- `src/SolidWorksMcp.Core/CadRecoveryContracts.cs` defines versioned checkpoint manifests carrying transaction,
  idempotency, session/document, source state hash, provider/MCP versions, intended operation codes and retention
  metadata. `CadCheckpointPolicy` requires a checkpoint for model, assembly, drawing, release and destructive-save
  risk levels.
- `CadTransactionEngine` now calls the checkpoint coordinator before any high-risk handler. Missing/failed checkpoint
  creation returns `CHECKPOINT_FAILED` and the handler is not invoked. Operation, cancellation, timeout and invariant
  failures enter one bounded recovery path; only `PreStateVerified=true` allows the original error to be returned.
  Otherwise the terminal result is `ROLLBACK_FAILED` with explicit `recovery.status=Unrecovered` or an equivalent
  unverified state.
- `src/SolidWorksMcp.Core/FileCadCheckpointCoordinator.cs` creates GUID-scoped manifest/snapshot directories,
  copies and hashes a persisted source file, writes the manifest atomically, restores only the manifest source path,
  verifies restored bytes, and prunes only expired checkpoint children under its configured root. File-byte equality is
  intentionally reported as `file-bytes-only`, not as proof of opaque SOLIDWORKS model state.
- `InMemoryCadCheckpointCoordinator` is a deterministic test double only; it is not a production durability claim.
  A future native coordinator must use the provider's inspection/reopen path to prove the pre-state hash, and may use
  `ICadLogicalRollbackHandler` before snapshot restoration.
- `tests/SolidWorksMcp.UnitTests/CadRecoveryTests.cs` injects failures at checkpoint, operation and verification
  stages, checks fail-closed behavior for unverified recovery, proves read-only plans do not require a checkpoint, and
  verifies manifest/snapshot persistence plus retention cleanup.

Evidence command and result:

    dotnet format SolidWorksMcp.slnx --no-restore --verify-no-changes --severity info                 # exit 0
    dotnet build SolidWorksMcp.slnx -c Release --no-restore                                         # exit 0; 0 warnings; 0 errors
    dotnet test SolidWorksMcp.slnx -c Release --no-restore --no-build --logger "console;verbosity=minimal" # exit 0; 59 passed; 0 failed; 1 skipped

F02 remains intentionally partial until a native SOLIDWORKS checkpoint coordinator can restore/reopen an isolated
document and verify the provider state hash as part of rollback. The provider now has a separate native lifecycle proof;
it is not yet a checkpoint or rollback implementation. The Live suite has real B03 mutation evidence, while its explicit
placeholder remains skipped when no opt-in Live session is available.

## B03 native part foundation evidence (partial)

Issue #19/B03 now has a verified native-provider geometry and persisted-lifecycle slice plus an implemented named
dimension mutation path, but it is not complete: the named-dimension path still needs an opt-in native Live run in the
current session, configuration-specific coverage, the remaining part feature services, and F01/F02 transaction routing.

- `providers/SolidWorksMcp.Provider.SolidWorks/SolidWorksDocumentRegistry.cs` binds a document identity to path,
  type, configuration, state hash, dirty state and the verified profile feature name. Routing rejects a missing,
  wrong-type, wrong-configuration or stale document before mutation.
- The native session info now carries a per-attachment generation in addition to the PID/revision-derived `SessionId`.
  Every STA invocation and native document facade carries that generation, so a detached/re-attached COM attachment
  cannot satisfy an old facade merely because the operating system reused the same PID and SOLIDWORKS revision.
- `SolidWorksComSessionHost.InvokeOnStaAsync` is the only native mutation entry point. The callback runs on the
  dedicated STA and returns vendor-neutral results; COM child objects are released inside that callback so RCWs do
  not cross the provider boundary.
- `SolidWorksPartFactory` uses the discovered local part template, creates a part with `INewDocument2`, creates an
  optional sketch circle through `SketchManager.CreateCircleByRadius`, and persists it with the verified
  `SaveAs3(CurrentVersion, Silent)` argument order. No vendor DLL or template is committed.
- If post-save identity verification fails, the factory attempts one bounded compensation close only when the exact
  target path is confirmed and the new document is clean. Dirty or unknown documents are not blind-closed because a
  modal save prompt could deadlock the STA; the failed artifact remains available for diagnosis.
- `SolidWorksNativePartDocument` re-resolves and verifies the document before rebuild/save, calls
  `FeatureExtrusion3` only after a named sketch selection, and performs inspection after mutation. Inspection reads
  bodies, bounding box, feature identity/type and mass/volume through native APIs, then converts the evidence to
  `CadAbstractions` records. Feature metadata is copied before the inspection reader releases potentially shared
  feature RCWs, avoiding a real `InvalidComObjectException` found by Live testing.
- Inspection commits its refreshed state hash and dirty flag to the registry on the same STA callback that read the
  native model. Mutation results carry the exact pre-operation descriptor; the facade uses an exact registry CAS and
  returns `STATE_CONFLICT` if committing the result would overwrite newer routing state. The failure is explicitly
  non-blind-retry because native work may already have completed.
- `tests/SolidWorksMcp.LiveSolidWorksTests/NativeIdentityBoundaryTests.cs` verifies exact-CAS stale-state rejection
  and document-identity mismatch rejection without starting SOLIDWORKS; these are provider-boundary tests, not Live
  COM geometry evidence.
- `ICadDocument.CloseAsync` and `ICadDocument.ReopenAndInspectAsync` define a high-level persisted lifecycle contract.
  The native part facade performs a clean-state preflight, invokes `CloseDoc` with the registered canonical path,
  verifies the exact document is closed, invokes `OpenDoc6` on the provider STA with the registered part configuration,
  and rechecks path/type/configuration/profile identity/body/volume/feature/state-hash evidence. It never routes through
  `ActiveDoc` and never exposes a COM handle to the contract layer. FakeCad supplies a logical in-memory lifecycle double
  for Hosted-safe contract coverage; it is not a disk durability claim.
- `ICadPartDocument.SetDimensionValueAsync` accepts a full native parameter identity and a canonical `Length`, with an
  optional exact configuration. The native adapter resolves the registered document on the provider STA, uses the
  official `IDimension.SetSystemValue3` configuration setter, reads the value back through `GetSystemValue3`, rebuilds,
  inspects positive volume and updates the registered state hash. A setter return code without read-back and geometry
  evidence is not accepted as success. FakeCad covers the contract; it does not prove native COM behavior.
- `src/SolidWorksMcp.EngineeringModel/PartDrawingRequirements.cs` adds the first immutable, vendor-neutral drawing
  requirement set. It models orthographic/isometric coverage, holes, bend radius, thickness, section/detail need,
  material, general tolerance, surface finish and title-block requirements. Private visual review creates only
  `ReviewRequired` proposals with `private_drawing_review` provenance; explicit approval is required before release.

Official API evidence recorded in `docs/research/solidworks-api-knowledge.md` covers `GetDocumentTemplate`,
`INewDocument2`, `CreateCircleByRadius`, `FeatureExtrusion3`, `ForceRebuild3`, `SaveAs3`, `IActivateDoc3`,
`CloseDoc`, `OpenDoc6`, `Parameter`, `SetSystemValue3` and `GetSystemValue3`. The latest local Live run passed session attachment, sketch creation, persisted part creation,
non-null extrusion, rebuild, positive body/volume inspection, feature identity inspection, save, exact close, OpenDoc6
reopen, post-reopen inspection and explicit document close. The test used only an isolated user-local workspace and did
not close the interactive SOLIDWORKS process. No complete B03 release claim is made.

Evidence command and result:

    dotnet format SolidWorksMcp.slnx --no-restore --verify-no-changes --severity info --verbosity quiet # exit 0
    dotnet build SolidWorksMcp.slnx -c Release --no-restore -v:minimal                         # exit 0; 0 warnings; 0 errors
    dotnet test SolidWorksMcp.slnx -c Release --no-build -v:minimal --logger "console;verbosity=minimal" # exit 0; Unit 58 + Contract 7 + FakeCad 7 passed; Live 6 passed + 2 skipped

Live status: the earlier explicit opt-in B03 test passed in the local SOLIDWORKS environment, including native sketch,
extrusion, rebuild, inspection, save, exact document close, OpenDoc6 reopen, post-reopen inspection and final close.
That run predates the named-dimension assertion. The current explicit opt-in Live test is skipped when
`SOLIDWORKS_MCP_LIVE_PROCESS_ID` and `SOLIDWORKS_MCP_LIVE_WORKSPACE` are absent.

The current Hosted-safe run passed the FakeCad named-dimension mutation and the native provider build. No claim is made
that `SetSystemValue3` has passed against a real SOLIDWORKS session until the test is rerun with an explicitly supplied
user-started process and isolated workspace.

Additional local Live evidence:

    SOLIDWORKS_MCP_LIVE_PROCESS_ID=<explicit-local-pid>; SOLIDWORKS_MCP_LIVE_WORKSPACE=<isolated-user-local-workspace>
    dotnet test tests/SolidWorksMcp.LiveSolidWorksTests/SolidWorksMcp.LiveSolidWorksTests.csproj -c Release --no-build --filter FullyQualifiedName~B03NativePartLiveTests # exit 0; 1 passed; 0 failed; 0 skipped

The command above is shown with placeholders so a machine-specific PID or local workspace is not treated as a source
contract. The running SOLIDWORKS process was not closed by the test; only the provider-owned COM attachment was
detached. The native document itself was explicitly closed before test completion, allowing the successful isolated
artifact cleanup path to run.

## B06 structured native diagnostics evidence (partial)

Issue #22/B06 now has a narrow, fail-closed native diagnostics slice. It is not complete: modal detection and bounded
dialog recovery, screenshots, drawing export, and the remaining document-level error surfaces still require their own
provider contracts and Live evidence.

- `src/SolidWorksMcp.CadAbstractions/CadDiagnostics.cs` defines the vendor-neutral `CadDiagnostic` contract with a
  stable code, severity, privacy-safe classification, scope, entity identity and optional native numeric code.
  Raw SOLIDWORKS modal/UI text, file contents and COM exception text are deliberately outside the shared contract.
- `SolidWorksNativeInspection` reads the official `IFeature.GetErrorCode2` signal while traversing the feature tree on
  the provider STA. Non-zero warning/error codes become structured diagnostics associated with the stable document and
  feature identity; inspection evidence includes diagnostic totals and severity totals.
- Native part rebuild now reuses the complete inspection reader after `ForceRebuild3`, carries the diagnostics in
  `RebuildReceipt`, and fails closed when an error-level diagnostic is present. Extrusion and named-dimension mutation
  apply the same post-rebuild error gate, so a non-null COM result or a `true` rebuild return cannot alone claim health.
- `tests/SolidWorksMcp.UnitTests/CadDiagnosticsTests.cs` protects warning/error semantics and receipt propagation.

Official API evidence is recorded in `docs/research/solidworks-api-knowledge.md`:
[IFeature.GetErrorCode2](https://help.solidworks.com/2025/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SolidWorks.Interop.sldworks.IFeature~GetErrorCode2.html).
The locally installed SOLIDWORKS 2022 interop was inspected and its callable signature matches the documented API
shape used by the provider. The linked Help revision is 2025 because that is the public indexed page used for the
current API citation; this is not a claim that a newer vendor binary was copied into the repository.

Evidence command and result:

    dotnet format SolidWorksMcp.slnx --no-restore --verify-no-changes --severity info --verbosity quiet # exit 0
    dotnet build SolidWorksMcp.slnx -c Release --no-restore -v:minimal                         # exit 0; 0 warnings; 0 errors
    dotnet test SolidWorksMcp.slnx -c Release --no-build --logger "console;verbosity=minimal"     # exit 0; Unit 58 + Contract 7 + FakeCad 7 passed; Live 6 passed + 2 skipped
    dotnet build SolidWorksMcp.hosted.slnx --configuration Release --no-restore -p:SolidWorksMcpNativeProviderEnabled=false -p:SolidWorksMcpHostedBuild=true -v:minimal # exit 0; 0 warnings; 0 errors
    dotnet test SolidWorksMcp.hosted.slnx --configuration Release --no-build -p:SolidWorksMcpNativeProviderEnabled=false -p:SolidWorksMcpHostedBuild=true --logger "console;verbosity=minimal" # exit 0; Unit 58 + Contract 7 + FakeCad 7 passed

No new Live SOLIDWORKS run was claimed in this slice because the explicit local Live opt-in variables were absent at
execution time. Existing B03 Live evidence remains valid for the previously verified geometry/lifecycle path, but it
does not prove that every new diagnostic branch has observed a real feature error in SOLIDWORKS. No complete B06 claim
is made.

## Native stdio composition evidence (partial)

The native provider is now composed into the real MCP executable behind an explicit user-local opt-in. This is a
composition and transport proof, not a claim that every native mutation is complete.

- `src/SolidWorksMcp.Server/SolidWorksMcp.Server.csproj` targets `net10.0-windows` and references the provider only
  when `SolidWorksMcpNativeProviderEnabled=true`; the provider remains the only project that owns vendor Interop
  references. Hosted mode targets vendor-free `net10.0` and does not load the provider assembly.
- `src/SolidWorksMcp.Server/Program.cs` loads the complete doctor configuration, constructs
  `SolidWorksCadProvider` with the user-local `CadPathAllowlist` in native mode, and fails closed with an explicit
  diagnostic when a vendor-free executable is asked to run native mode.
- `Directory.Build.props` and `scripts/build-hosted.ps1` isolate Hosted `bin` and `obj` output per project under the
  ignored `artifacts/hosted` root. Explicit `bin/obj/artifacts` exclusions prevent generated source from older
  target/platform combinations being compiled again.
- `scripts/Invoke-SolidWorksMcpDoctor.ps1 -Initialize -Json` writes user-local provider opt-in properties, an MCP
  executable path, and the path allowlist. No machine-specific path is committed.

Evidence command and result:

    dotnet format SolidWorksMcp.slnx --no-restore --verify-no-changes --severity info --verbosity quiet # exit 0
    dotnet build SolidWorksMcp.slnx -c Release --no-restore -v:minimal                         # exit 0; 0 warnings; 0 errors
    dotnet test SolidWorksMcp.slnx -c Release --no-build -v:minimal --logger "console;verbosity=minimal" # exit 0; Unit 58 + Contract 7 + FakeCad 7 passed; Live 6 passed + 2 skipped
    dotnet build SolidWorksMcp.hosted.slnx --configuration Release --no-restore -p:SolidWorksMcpNativeProviderEnabled=false -p:SolidWorksMcpHostedBuild=true -v:minimal # exit 0; 0 warnings; 0 errors
    dotnet test SolidWorksMcp.hosted.slnx --configuration Release --no-build -p:SolidWorksMcpNativeProviderEnabled=false -p:SolidWorksMcpHostedBuild=true --logger "console;verbosity=minimal" # exit 0; Unit 58 + Contract 7 + FakeCad 7 passed

The native executable was started as a local stdio child and successfully completed MCP `initialize`, `tools/list`
and `cad.capabilities` requests. At that historical smoke-test revision, the response identified the native provider
and exposed four tools. The current bounded compiler-facing surface is superseded by the five-tool registry described
below. The smoke test
did not start or stop SOLIDWORKS and did not perform a CAD mutation; the current machine had zero running
`SLDWORKS.exe` sessions at the time of the check. Therefore native MCP create-part evidence and a full
`Codex -> MCP -> Provider -> SOLIDWORKS` mutation proof remain pending an explicitly user-started SOLIDWORKS
session and isolated workspace.

The official API references used by the native mutation slice include `IDimension.SetSystemValue3` and its required
read-back through `IDimension.GetSystemValue3`:
[SetSystemValue3](https://help.solidworks.com/2022/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IDimension~SetSystemValue3.html)
and
[GetSystemValue3](https://help.solidworks.com/2022/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IDimension~GetSystemValue3.html).

## Private drawing fixture review evidence (redacted)

The user-supplied local drawing corpus is treated as confidential research input, not as a repository fixture. The
corpus remains Git-ignored; source PDFs, extracted text, titles, part numbers, dimensions, screenshots, rendered
images, manifests and CAD artifacts are not committed, uploaded or written to Issues, PRs, logs or release notes.

- `skills/private-drawing-fixtures/SKILL.md` defines the disclosure boundary and requires every reference-driven
  iteration to sample exactly two PDFs at random, select only single-part candidates for the current scope, and
  record generic engineering classes instead of source content.
- `scripts/Invoke-PrivateDrawingSample.ps1` performs content/layout classification, uses a cryptographically seeded
  random selection, renders only into a user-local temporary evidence directory, and prints only redacted slot
  status. It rejects a corpus path that is not Git-ignored when it is under the repository.
- A local invocation completed with exit code 0 and reported exactly two selected/rendered single-part candidate
  slots. The redacted visual review identified recurring classes only: orthographic/isometric coverage, holes,
  formed/bend features, thickness, material/general-tolerance metadata, section/detail consideration and title-block
  requirements. This is design input and coverage planning evidence, not proof that any CAD drawing passed QA.
- `PartDrawingRequirementSet.FromReviewedClasses` turns those classes into deterministic requirement identities while
  preserving `ReviewRequired` status and `private_drawing_review` provenance. A release gate cannot be satisfied by
  visual review or AI inference alone.
- `src/SolidWorksMcp.AutoDrawing/PartDrawingPlanner.cs` now consumes that graph as a pure high-level compiler stage.
  It emits primary/projected/isometric roles, compares Section versus Detail candidates using the named normalized
  evidence score (coverage 50%, readability 30%, manufacturing exposure 20%), applies stable tie-breakers, and
  exposes `CanGenerate` separately from `CanRelease`. It never calls COM or writes a drawing.
- `NormalizedScore` is a protocol value object with range validation; the planner does not expose raw untyped
  engineering doubles across the AutoDrawing boundary. Missing orthographic coverage is `Blocked`; unapproved
  private-review requirements or unverified candidates remain `ReviewRequired`.
- `src/SolidWorksMcp.AutoDrawing/PartDrawingCoverageAnalyzer.cs` adds the next QA boundary: drawing annotations carry
  explicit semantic `CoverageKeys`, and the analyzer reports `Pass`, `Warning`, `ReviewRequired` or `Blocking` without
  OCR/text similarity. Required approved requirements without semantic annotation coverage block release; optional
  omissions remain warnings; orphan keys are visible warnings rather than silently accepted coverage.
- `CadAbstractions.DrawingAnnotationRequest` and `DrawingAnnotationSnapshot` preserve those coverage keys across the
  provider boundary. FakeCad round-trips them, and the native drawing provider supports explicit `Kind=note` plus the
  bounded `Kind=model-dimensions` insertion proven by the newer MCP Live slice; broad PMI normalization remains deferred.

The two sampled slots are intentionally not named here. A future iteration must run the same script again and sample
exactly two new candidates (or two new random candidates) before making a reference-driven design decision.

Planning evidence command and result:

    dotnet format SolidWorksMcp.slnx --no-restore --verify-no-changes --severity info --verbosity quiet # exit 0
    dotnet build SolidWorksMcp.slnx -c Release --no-restore -v:minimal                         # exit 0; 0 warnings; 0 errors
    dotnet test SolidWorksMcp.slnx -c Release --no-build -v:minimal --logger "console;verbosity=minimal" # exit 0; Unit 52 + Contract 7 + FakeCad 6 passed; Live 4 passed + 2 skipped

The planner tests cover approved section selection, deterministic tie-breaking, private-review gating and missing
orthographic blocking. Coverage tests cover explicit annotation keys, optional warnings, unresolved review state,
orphan keys and blocking missing coverage. These tests use only generic semantic classes and synthetic
provider-neutral candidates.

## Native reference-driven drawing annotation and selection evidence (partial)

The current native slice is intentionally narrow but real: it creates a non-cylindrical L-profile part, cuts a semantic
through-hole group, creates native drawing views, inserts one explicit native Note and one native model DisplayDimension,
saves, closes, reopens and verifies the drawing evidence through `IView.GetAnnotations`. It also resolves the actual
HolePattern feature through semantic and geometry-signature selectors on the same registered document. This is not a
release-ready drawing compiler and does not claim PMI, GD&T or manufacturing coverage.

- `SolidWorksNativeDrawingDocument.AddAnnotationAsync` accepts only explicit `Kind=note`, activates the exact view
  binding, calls `IDrawingDoc.CreateText2`, reads back `INote.GetText`, `IAnnotation.GetType`, `IAnnotation.GetPosition`
  and persists a stable annotation identity with `IAnnotation.SetName`.
- `Kind=model-dimensions` calls `IDrawingDoc.InsertModelAnnotations3` after selecting one exact drawing view and
  accepts only a returned `swDisplayDimension`. It reads legal `IDisplayDimension.GetText` parts and the associated
  native `IDimension.SystemValue`; it never calls the invalid `GetText(0)` path or synthesizes a dimension from request
  text.
- `SolidWorksNativeInspectionReader.ReadDrawing` classifies native Note/DisplayDimension/GDT/surface-finish/weld
  annotations without exposing COM objects. Coverage keys are not guessed from visible text after reopen.
- `SolidWorksNativeSelectionService` implements B05's fail-closed semantic and geometry-signature resolution for
  Feature/Body, validates registered document/state identity, and validates `solidworks.persist3` references without
  exposing COM objects. Other topology capture remains explicitly unsupported.
- `B04ReferenceDrivenPartLiveTests` verifies native note/model-dimension evidence before save and after persisted
  drawing reopen, plus semantic/geometry selector identity on the actual generated part. The test uses a generic
  redacted feature class and never reads or commits the confidential PDF corpus.

Evidence command and result from the current local machine:

    dotnet build SolidWorksMcp.slnx -c Release --no-restore -v:minimal                 # exit 0; 0 warnings; 0 errors
    dotnet test SolidWorksMcp.slnx -c Release --no-build --logger "console;verbosity=minimal" # historical direct-PID run; Unit 62 + Contract 7 + Fake 8 passed; Live 2 passed + 1 skipped
    dotnet test tests/SolidWorksMcp.LiveSolidWorksTests/SolidWorksMcp.LiveSolidWorksTests.csproj -c Release --no-build --filter FullyQualifiedName~B04ReferenceDrivenPartLiveTests # exit 0; 1 passed; 0 failed; 0 skipped
    dotnet test tests/SolidWorksMcp.LiveSolidWorksTests/SolidWorksMcp.LiveSolidWorksTests.csproj -c Release --no-build --logger "console;verbosity=minimal" # historical direct-PID run; 8 passed; 1 skipped

The supported local Live evidence now uses `scripts/Invoke-SolidWorksLiveTests.ps1`: it closed the previous SOLIDWORKS
process, started exactly one fresh process, ran the complete solution serially, then saved generated workspace artifacts
and requested `ISldWorks.ExitApp` for the exact owned PID. The fresh-process result was Unit 62 passed, Contract 7 passed,
FakeCad 8 passed, Live 9 passed, 1 explicit placeholder skipped; the post-run process inventory contained zero
`SLDWORKS.exe` processes. B03 and B04 also passed independently in fresh-process runs. This is process-lifecycle evidence,
not a claim that the entire Epic or all drawing/compiler issues are complete.

当前受支持的本机 Live 证据通过 `scripts/Invoke-SolidWorksLiveTests.ps1` 产生：先关闭旧 SOLIDWORKS，启动唯一的新进程，
串行执行完整 solution，随后保存 test workspace artifact，并对精确 owned PID 请求 `ISldWorks.ExitApp`。干净进程结果为
Unit 62 passed、Contract 7 passed、FakeCad 8 passed、Live 9 passed、1 个明确 placeholder skipped；结束后的进程清单为
零个 `SLDWORKS.exe`。B03、B04 也分别在干净进程中通过。这只是进程生命周期证据，不代表整个 Epic 或所有工程图/compiler
Issue 已完成。

Official API evidence for this slice is recorded in `docs/research/solidworks-api-knowledge.md`, including
`CreateText2`, `InsertModelAnnotations3`, `GetText`, `GetDimension`, `SystemValue`, `GetAnnotations`,
`GetSpecificAnnotation`, `CreateDrawViewFromModelView3`, `GetViews`, `GetOutline`, `GetPersistReference3` and
`GetObjectByPersistReference3`, plus the Live-only `GetDocuments`, `GetDocumentCount` and `ExitApp` lifecycle facts.
Model-item provenance, PMI normalization and coverage-aware release gating remain separate follow-ups.
## B05 native topology selector evidence / B05 native 拓扑 selector 证据

The native B05 slice now publishes bounded topology evidence from `ReadPart` and round-trips it through the same declarative selection boundary. `CadInspectionSnapshot.TopologyEntities` contains vendor-neutral `CadTopologyEntitySnapshot` values for Face, Edge, Vertex and SketchEntity when SOLIDWORKS exposes an opaque `solidworks.persist3` token. The token stays opaque outside the provider. Geometry signatures are optional and are emitted only for meaningful search keys: imported-body face IDs, imported-body edge IDs, canonical millimetre vertex points and sketch-feature-name plus segment ID. Native parametric face/edge IDs that read as zero are intentionally persistent-reference-only, because the official API documents those IDs for imported bodies rather than as universal topology identity.

The resolver scans current solid bodies/sketch features on the owning STA, rejects zero or multiple geometry matches, checks the registered document and expected state hash, validates entity kind after `GetObjectByPersistReference3`, and returns `SELECTION_STALE` instead of guessing. No enumeration ordinal or global Selection Mark crosses the abstraction boundary. This remains a selector/inspection slice, not a claim that all topology mutation, drawing annotation capture or release QA is complete.

当前 B05 native slice 已经从 `ReadPart` 输出 bounded topology evidence，并通过同一个声明式 selection boundary 做 round-trip。`CadInspectionSnapshot.TopologyEntities` 在 SOLIDWORKS 能提供 opaque `solidworks.persist3` token 时，输出 Face、Edge、Vertex、SketchEntity 的 vendor-neutral `CadTopologyEntitySnapshot`；token 在 Provider 外始终保持 opaque。Geometry signature 只有在官方 API 提供有意义的 search key 时才输出：imported-body face ID、imported-body edge ID、canonical millimetre vertex point、以及 sketch feature name + segment ID。native parametric face/edge ID 读为 0 时只允许 persistent-reference，因为官方定义这些 ID 主要服务 imported body，不能冒充通用 topology identity。

Resolver 在所属 STA 扫描当前 solid body/sketch feature，拒绝零个或多个 geometry match，校验 registered document 与 expected state hash，并在 `GetObjectByPersistReference3` 后再次校验 entity kind；无法证明时返回 `SELECTION_STALE`，不猜 enumeration ordinal，也不把全局 Selection Mark 穿过 abstraction boundary。该切片仍然只是 selector/inspection 能力，不代表所有 topology mutation、drawing annotation capture 或 release QA 已完成。

Final evidence on the local SOLIDWORKS 2022 machine:

    powershell -ExecutionPolicy Bypass -File .\scripts\build-hosted.ps1 # exit 0; Unit 65 + Contract 10 + FakeCad 10 passed; 0 failed
    dotnet format providers/SolidWorksMcp.Provider.SolidWorks/SolidWorksMcp.Provider.SolidWorks.csproj --no-restore --verify-no-changes --severity info # exit 0
    dotnet build SolidWorksMcp.slnx -c Release --no-restore -p:SolidWorksInstallRoot=D:\Solidworks2022\SOLIDWORKS # exit 0; 0 warnings; 0 errors
    powershell -ExecutionPolicy Bypass -File .\scripts\Invoke-SolidWorksLiveTests.ps1 -SolidWorksPath D:\Solidworks2022\SOLIDWORKS\SLDWORKS.exe -Filter FullyQualifiedName~B04ReferenceDrivenPartLiveTests -NoBuild # exit 0; 1 passed; 0 failed; 0 skipped; before SLDWORKS_COUNT=0; after SLDWORKS_COUNT=0

The focused Live run created and verified the actual rounded non-cylindrical part and drawing workflow while round-tripping native topology selectors. The harness closed any old SOLIDWORKS process before launch and closed the exact owned PID after the test; no extra SOLIDWORKS session was left running.

该 focused Live run 实际创建并验证了圆弧边界的非圆柱零件及其工程图流程，同时 round-trip native topology selector。Harness 启动前关闭旧 SOLIDWORKS，测试后关闭精确 owned PID；结束时没有遗留额外 SOLIDWORKS session。

## High-level MCP repeated-hole drawing evidence / 高层 MCP 重复孔组出图证据

The high-level `cad.build-part-drawing` MCP tool now accepts a bounded `throughHolePatternJson` value. The input is
validated before provider/session startup and becomes one `ThroughHolePatternRequest`; it is not expanded into a list
of unrelated primitive-hole tools. `PartDrawingBuildService` executes the native sketch-and-through-cut mutation after
the verified non-cylindrical profile extrusion, then carries the verified native feature snapshot and count/diameter
observations into the versioned MCP result envelope. The drawing stage remains deterministic and currently emits
Front/Top/Isometric seed views plus verified native model dimensions; native Hole Callout/BOM/Release QA are separate
follow-up slices.

高层 `cad.build-part-drawing` MCP tool 现在接收 bounded `throughHolePatternJson`。输入在 Provider/session 启动前完成
校验，并形成一个 `ThroughHolePatternRequest`；不会退化成一串互不相关的 primitive-hole tool。`PartDrawingBuildService`
在已验证的非圆柱 profile extrusion 后执行 native sketch + through-cut mutation，并把 native feature snapshot 以及
孔数量/直径 evidence 带入版本化 MCP result envelope。当前二维阶段仍是确定性的 Front/Top/Isometric seed views
加 native model dimensions；原生 Hole Callout、BOM 和 Release QA 属于后续切片。

Final evidence on the local SOLIDWORKS 2022 machine:

    dotnet build SolidWorksMcp.hosted.slnx -c Release --no-restore -p:ContinuousIntegrationBuild=true -v:minimal # exit 0; 0 warnings; 0 errors
    dotnet test SolidWorksMcp.hosted.slnx -c Release --no-build --no-restore --logger "console;verbosity=minimal" # exit 0; Unit 65 + Contract 11 + FakeCad 10 passed
    dotnet build tests/SolidWorksMcp.LiveSolidWorksTests/SolidWorksMcp.LiveSolidWorksTests.csproj -c Release --no-restore -p:SolidWorksInstallRoot=D:\Solidworks2022\SOLIDWORKS -v:minimal # exit 0; 0 warnings; 0 errors
    powershell -ExecutionPolicy Bypass -File .\scripts\Invoke-SolidWorksLiveTests.ps1 -SolidWorksPath D:\Solidworks2022\SOLIDWORKS\SLDWORKS.exe -Filter FullyQualifiedName~McpBuildPartDrawingLiveTests -NoBuild # exit 0; 1 passed; 0 failed; before 0; after 0

The successful preserved-artifact run created one actual native `.SLDPRT`, `.SLDDRW` and PDF in the isolated local test
workspace. The artifacts are intentionally not committed to the repository. The rendered PDF visibly contains the
non-cylindrical front view with two real through holes, an isometric view with the hole openings and a native 40.00
model dimension. The Live harness closed any old SOLIDWORKS process before launch and closed the exact owned PID after
the run; the final process inventory was zero `SLDWORKS.exe`.

成功的保留产物运行在隔离的本机 test workspace 生成了一套真实 `.SLDPRT`、`.SLDDRW` 与 PDF；这些产物刻意不提交到仓库。
渲染后的 PDF 可见非圆柱正视图、两个真实通孔、带孔开口的等轴测视图，以及 native 40.00 model dimension。Live harness
启动前关闭旧 SOLIDWORKS，结束后关闭精确 owned PID；最终进程清单为零个 `SLDWORKS.exe`。

## D03 repeated-feature semantic compression in the real drawing / D03 真实工程图中的重复特征语义压缩

This D03 slice moves repeated-hole semantics from the 3D feature evidence into one deterministic drawing annotation. The
vendor-neutral `RepeatedFeatureCalloutPlanner` proves the supplied center coordinates before emitting compact notation;
it does not loop over hole instances and create duplicate diameter dimensions. For the symmetric two-hole fixture, the
verified text is `2X Ø6 THRU; PITCH 20; SYMMETRIC`, with stable coverage keys for quantity, size, depth, pitch,
equal-spacing and symmetry. Irregular center sets remain `EXPLICIT CENTERS` instead of receiving guessed pattern claims.

该 D03 切片把重复孔的语义从三维 feature evidence 推进到一条确定性的二维图纸标注。vendor-neutral
`RepeatedFeatureCalloutPlanner` 只有在中心坐标数值证明关系后才生成紧凑表达式；不会遍历每个孔实例生成重复直径尺寸。
对称双孔 fixture 的 verified text 为 `2X Ø6 THRU; PITCH 20; SYMMETRIC`，并携带 quantity、size、depth、pitch、
equal-spacing、symmetry 的稳定 coverage keys。不规则中心点集合保留为 `EXPLICIT CENTERS`，不会猜测 pattern 语义。

The native provider persists the callout as a real `IDrawingDoc.CreateText2` / `INote` annotation with the stable identity
`<drawing>:pattern-callout:<feature>`, checks the exact returned text and position, rebuilds, saves, reopens and classifies
the persisted note by its identity. The note is placed in a deterministic lower reserved band so it does not overlap the
Top view. This is an actual native drawing note, not a screenshot or a FakeCad-only assertion. It is intentionally not yet
a native SOLIDWORKS Hole Callout: official `IDrawingDoc.AddHoleCallout2` has a selected-edge and dialog-confirmation
workflow, so it remains blocked behind the modal-dialog safety design rather than being automated with blind OK/Enter.

Native Provider 将 callout 作为真实的 `IDrawingDoc.CreateText2` / `INote` annotation 持久化，并使用稳定 identity
`<drawing>:pattern-callout:<feature>`；读取 exact text/position，rebuild、save、reopen 后再按 identity 分类。注释固定放在
下方 deterministic reserved band，避免与 Top view 碰撞。这是真实 native drawing note，不是截图，也不是仅 FakeCad 断言。
当前仍未宣称这是 native SOLIDWORKS Hole Callout：官方 `IDrawingDoc.AddHoleCallout2` 需要选中圆边并确认 dialog，
因此在 modal-dialog safety 完成前不会用 blind OK/Enter 自动化，native associative Hole Callout 仍是后续切片。

The required reference-driven sampler was run once for this iteration and returned exactly two selected/rendered
single-part candidate slots. Only generic redacted classes were used as design input; no confidential source filename,
text, dimension, screenshot or path is recorded in this repository.

本轮 reference-driven sampler 已运行一次并恰好返回两个 selected/rendered single-part candidate slot。设计输入只使用脱敏的
通用类别；仓库不记录任何机密源文件名、文字、尺寸、截图或路径。

Final evidence on the local SOLIDWORKS 2022 machine:

    dotnet format SolidWorksMcp.hosted.slnx --no-restore --verify-no-changes --severity info --verbosity quiet # exit 0
    dotnet build SolidWorksMcp.hosted.slnx -c Release --no-restore -p:ContinuousIntegrationBuild=true -v:minimal # exit 0; 0 warnings; 0 errors
    dotnet test SolidWorksMcp.hosted.slnx -c Release --no-build --no-restore --logger "console;verbosity=minimal" # exit 0; Unit 67 + Contract 11 + FakeCad 10 passed
    dotnet build tests/SolidWorksMcp.LiveSolidWorksTests/SolidWorksMcp.LiveSolidWorksTests.csproj -c Release --no-restore -p:SolidWorksInstallRoot=D:\Solidworks2022\SOLIDWORKS -v:minimal # exit 0; 0 warnings; 0 errors
    powershell -ExecutionPolicy Bypass -File .\scripts\Invoke-SolidWorksLiveTests.ps1 -SolidWorksPath D:\Solidworks2022\SOLIDWORKS\SLDWORKS.exe -Filter FullyQualifiedName~McpBuildPartDrawingLiveTests -NoBuild # exit 0; 1 passed; 0 failed; before 0; after 0

The preserved native evidence contains one `.SLDPRT`, one `.SLDDRW` and one PDF in the isolated user-local test
workspace. The rendered PDF was visually inspected and shows the curved plate with two real through holes, Front/Top/
Isometric native views, the native `40.00` model dimension and the compact pattern callout in the lower reserved band.
The artifacts are not committed. Issue #31 is only partially satisfied: deterministic quantity/pitch/symmetry compression
is now proven, while native Hole Callout association, position-dimension coverage and edit-driven regeneration remain open.

隔离 user-local test workspace 中保留了一套 native `.SLDPRT`、`.SLDDRW` 与 PDF。渲染后的 PDF 已视觉检查，包含带曲线边界和两个
真实通孔的零件、Front/Top/Isometric native views、native `40.00` model dimension，以及下方保留区内的紧凑 pattern callout。
产物未提交。Issue #31 目前只完成部分：quantity/pitch/symmetry 的确定性压缩已通过真实验证，native Hole Callout 关联、位置尺寸覆盖和
编辑后重新生成仍未完成。

## D04 native section-view slice / D04 原生剖视切片

D04 now has a bounded provider-neutral `DrawingSectionViewRequest` and a native implementation. The request binds the
section to an exact declarative parent `ViewId`, carries a deterministic paper-space cutting line and placement, and
does not expose SOLIDWORKS selection marks to MCP. The native adapter activates the registered parent view, creates and
selects the drawing sketch line on the owning STA, calls `IDrawingDoc.CreateSectionViewAt5`, removes the inherited child
alignment, applies `IView.SetXform`, rebuilds and verifies a positive native outline. FakeCad and Contract tests cover
the same request boundary; the high-level part compiler triggers the section only when the verified internal-hole group
is present. This is the first real section candidate, not a claim that the complete Section/Detail/Auxiliary/Multi-Sheet
planner is finished.

D04 目前已形成 bounded provider-neutral `DrawingSectionViewRequest` 和 native implementation。请求绑定精确的声明式
parent `ViewId`，携带确定性的纸面剖切线与位置，不把 SOLIDWORKS selection mark 暴露给 MCP。Native adapter 在所属
STA 激活已登记 parent view，创建并选中 drawing sketch line，调用 `IDrawingDoc.CreateSectionViewAt5`，移除创建后
继承的 child alignment，通过 `IView.SetXform` 定位，rebuild 后验证 native outline 为正值。FakeCad 和 Contract 覆盖
相同 request boundary；高层 part compiler 仅在已验证 internal-hole group 存在时生成 section。这里证明的是首个真实
剖视 candidate，不代表完整 Section/Detail/Auxiliary/Multi-Sheet planner 已完成。

The first Live attempt intentionally failed closed: SOLIDWORKS 2022's PDF SaveAs changed only `saveFlag` from `False`
to `True`; path, type, configuration, title, update stamp and feature count were unchanged. The export adapter now
allows only that exact clean-to-dirty transition, calls silent `IModelDoc2.Save3`, and requires the original fingerprint
to be restored. Any other export mutation remains `STATE_CONFLICT`.

首轮 Live 有意 fail closed：SOLIDWORKS 2022 的 PDF SaveAs 只把 `saveFlag` 从 `False` 变成 `True`，path、type、configuration、
title、update stamp、feature count 均未变化。Export adapter 现在只允许这一种精确的 clean-to-dirty 转换，调用 silent
`IModelDoc2.Save3`，并要求原始 fingerprint 恢复；任何其他 export mutation 仍返回 `STATE_CONFLICT`。

## D06 deterministic paper-space layout slice / D06 确定性纸空间布局切片

Issue #34 now has a vendor-neutral `PartDrawingLayoutPlanner`. It consumes explicit paper-space rectangles supplied by a
Provider/RulePack boundary: fixed drawing views, projected geometry, dimensions, callouts, labels, center marks, leaders,
and reserved zones such as title blocks or BOM areas. Rectangles and spacing use the protocol `Length` type, so the
engineering boundary does not expose unitless dimensional `double` values. The planner never reads private drawing PDFs,
never infers size from visible annotation text, and never calls SOLIDWORKS COM.

Issue #34 目前已经有 vendor-neutral `PartDrawingLayoutPlanner`。它从 Provider/RulePack boundary 接收显式纸空间矩形，覆盖
固定 drawing view、投影几何、尺寸、callout、label、center mark、leader，以及 title block/BOM 等 reserved zone。矩形与间距
使用 Protocol `Length`，工程边界不会暴露无单位 dimensional `double`。规划器不读取秘密图纸 PDF，不从可见文字猜尺寸，也不调用
SOLIDWORKS COM。

The pass is deterministic and identity-based. It sorts by semantic kind, view/anchor identity and stable item identity;
it attempts the requested position first, then a bounded deterministic reflow ring. Dimension items receive explicit tiers.
Moved callouts/labels/leaders retain their stable association and receive an orthogonal leader route. The QA output records
`view-view-collision`, `annotation-annotation-collision`, `annotation-geometry-collision`, `reserved-zone-collision`,
`off-sheet-*` and `layout-infeasible` findings. When the current sheet cannot satisfy the constraints, the plan records a
stable escalation rationale for dimension-tier spacing, scale reduction, sheet upgrade, detail view or additional sheet.

该 pass 按 identity 确定性执行：按语义类别、view/anchor identity、stable item identity 排序；先尝试请求位置，再执行有界且固定顺序
的 reflow ring。Dimension 会得到明确 tier。被移动的 callout/label/leader 保留 stable association，并生成正交 leader route。QA
输出明确记录 `view-view-collision`、`annotation-annotation-collision`、`annotation-geometry-collision`、
`reserved-zone-collision`、`off-sheet-*` 和 `layout-infeasible`。当前图幅无法满足约束时，plan 记录 dimension-tier spacing、缩小
比例、升级图幅、增加 detail view 或新增 sheet 的稳定升级理由。

`PartDrawingCoverageAnalyzer` accepts the optional layout proof and includes it in `CanRelease`; a blocking/review layout
finding therefore cannot be hidden by otherwise complete semantic dimension coverage. The planner's SHA-256 fingerprint is
stable across input enumeration order and is suitable for regeneration/audit comparison.

`PartDrawingCoverageAnalyzer` 现在可以接收可选 layout proof 并纳入 `CanRelease`；因此即使语义尺寸 coverage 完整，blocking/review
layout finding 也不能被掩盖。规划器输出的 SHA-256 fingerprint 不受输入 enumeration order 影响，可用于再生与 audit 对比。

Hosted evidence for this slice:

    dotnet format SolidWorksMcp.hosted.slnx --no-restore --verify-no-changes --severity info --verbosity quiet # exit 0
    dotnet build SolidWorksMcp.hosted.slnx -c Release --no-restore -p:ContinuousIntegrationBuild=true -v:minimal # exit 0; 0 warnings; 0 errors
    dotnet test SolidWorksMcp.hosted.slnx -c Release --no-build --no-restore --logger "console;verbosity=minimal" # exit 0; Unit 79 + Contract 12 + FakeCad 10 passed
    git diff --check # exit 0

Focused Unit cases cover stable regeneration, dimension-tier reflow, view-view collision, reserved-zone collision,
off-sheet geometry, annotation-geometry reflow with leader routing, infeasible-sheet escalation, and the coverage release
gate. Native Live was intentionally not started for this pure planner slice. Whenever the next native outline/materialization
slice runs, it must continue to use `scripts/Invoke-SolidWorksLiveTests.ps1`, which closes old SOLIDWORKS sessions before
launch, owns exactly one fresh PID, and verifies `SLDWORKS_COUNT=0` after cleanup.

本切片 focused Unit 覆盖稳定再生、dimension-tier reflow、view-view collision、reserved-zone collision、off-sheet geometry、带 leader
routing 的 annotation-geometry reflow、不可行图幅 escalation，以及 coverage release gate。本次纯 planner slice 有意不启动 native Live。
下一步 native outline/materialization 接入仍必须使用 `scripts/Invoke-SolidWorksLiveTests.ps1`：启动前关闭旧 SOLIDWORKS，只拥有一个新 PID，
清理后验证 `SLDWORKS_COUNT=0`。

Final evidence on the local SOLIDWORKS 2022 machine:

    dotnet format SolidWorksMcp.hosted.slnx --no-restore --verify-no-changes --severity info --verbosity quiet # exit 0
    dotnet build SolidWorksMcp.hosted.slnx -c Release --no-restore -p:ContinuousIntegrationBuild=true -v:minimal # exit 0; 0 warnings; 0 errors
    dotnet test SolidWorksMcp.hosted.slnx -c Release --no-build --no-restore --logger "console;verbosity=minimal" # exit 0; Unit 67 + Contract 11 + FakeCad 10 passed
    dotnet build tests/SolidWorksMcp.LiveSolidWorksTests/SolidWorksMcp.LiveSolidWorksTests.csproj -c Release --no-restore -p:SolidWorksInstallRoot=D:\Solidworks2022\SOLIDWORKS -v:minimal # exit 0; 0 warnings; 0 errors
    powershell -ExecutionPolicy Bypass -File .\scripts\Invoke-SolidWorksLiveTests.ps1 -Filter FullyQualifiedName~McpBuildPartDrawingLiveTests -NoBuild # exit 0; 1 passed; 0 failed; 1 placeholder skipped; before 0; after 0

The successful retained-artifact run produced one native `.SLDPRT`, `.SLDDRW` and PDF in the isolated user-local test
workspace. Visual inspection of the rendered PDF shows the curved plate, two real through holes, Front/Top/Isometric
views, a native `40.00` dimension, a compact repeated-hole callout, and `SECTION A-A` with a separated lower-right
section view. The artifacts are not committed. D04 remains partial: Detail A/B, Auxiliary View, multi-sheet planning,
general collision/reflow QA and richer section candidate scoring are follow-ups under D04/D06.

成功的保留产物运行在隔离 user-local test workspace 生成了一套 native `.SLDPRT`、`.SLDDRW` 与 PDF。渲染后的 PDF 已视觉检查，
包含曲线边界、两个真实通孔、Front/Top/Isometric views、native `40.00` 尺寸、紧凑重复孔 callout，以及与等轴视图分离的
右下 `SECTION A-A` 剖视。产物未提交。D04 仍是部分完成：Detail A/B、Auxiliary View、多 Sheet 规划、通用碰撞/重排 QA
和更丰富的 section candidate scoring 仍属于 D04/D06 后续范围。

## D05 feature-level dimension planning / D05 逐特征尺寸规划

Issue #33 requires coverage to be derived from manufacturable feature definitions rather than annotation count. The new
vendor-neutral `PartDrawingDimensionRequirementGraph` keeps geometry/CAD feature identity separate from explicit
dimension definitions, datum requirements and redacted provenance. Definitions are classified as Functional,
Manufacturing or Reference and use stable keys such as `x-position`, `y-pitch` and `diameter`. A Reference dimension can
never satisfy a missing Functional or Manufacturing definition.

Issue #33 要求 coverage 来源于可制造特征定义，而不是图纸上有多少个标注。新增的 vendor-neutral
`PartDrawingDimensionRequirementGraph` 将 geometry/CAD feature identity 与显式 dimension definition、datum requirement
和脱敏 provenance 分开保存。Definition 明确区分 Functional、Manufacturing、Reference，并使用 `x-position`、`y-pitch`、
`diameter` 等稳定 key。Reference 尺寸不能掩盖缺失的 Functional/Manufacturing 定义。

`PartDrawingDimensionPlanner` deterministically selects approved datums, chooses a stable associative/visible evidence
winner, emits `Create`, `RetainExisting`, `ReviewExisting` or `SuppressRedundant` actions, and produces one exact finding
per feature definition. Missing definitions identify the feature and definition, for example `Slot S03: X position is
missing.` and `HolePattern17: Y pitch is missing.` Duplicate evidence is warned and compressed rather than copied into
additional dimensions. The existing `PartDrawingCoverageAnalyzer` now accepts the dimension plan and aggregates its
feature-level findings into `CanRelease`; text similarity, OCR and screenshots are not used as proof.

`PartDrawingDimensionPlanner` 会确定性地选择 approved datum，选择 stable associative/visible evidence winner，输出 `Create`、
`RetainExisting`、`ReviewExisting`、`SuppressRedundant` action，并为每个 feature definition 输出一个精确 finding。缺失定义
会带出 feature 和 definition，例如 `Slot S03: X position is missing.`、`HolePattern17: Y pitch is missing.`。重复 evidence
只产生 warning 并压缩，不复制成额外尺寸。现有 `PartDrawingCoverageAnalyzer` 已能接收 dimension plan，并将逐特征 findings
纳入 `CanRelease`；不使用文字相似度、OCR 或截图作为证明。

Hosted evidence for this slice:

    dotnet format SolidWorksMcp.hosted.slnx --no-restore --verify-no-changes --severity info --verbosity quiet # exit 0
    dotnet build SolidWorksMcp.hosted.slnx -c Release --no-restore -p:ContinuousIntegrationBuild=true -v:minimal # exit 0; 0 warnings; 0 errors
    dotnet test SolidWorksMcp.hosted.slnx -c Release --no-build --no-restore --logger "console;verbosity=minimal" # exit 0; Unit 73 + Contract 12 + FakeCad 10 passed
    git diff --check # exit 0

The focused unit cases cover exact Slot X-position diagnostics, HolePattern Y-pitch diagnostics with approved primary
datum selection, Functional/Manufacturing/Reference mismatch, deterministic duplicate suppression and coverage-report
aggregation. The read-only public MCP `drawing.validate` tool now parses bounded requirement/evidence JSON before
starting a provider session, binds inspection to the exact document identity, and returns the deterministic plan. This
slice still does not materialize every missing dimension, execute an AutoDimension scheme, or create native datum/GD&T.

本轮 focused unit cases 覆盖 Slot X position 精确诊断、HolePattern Y pitch 诊断及 approved primary datum 选择、
Functional/Manufacturing/Reference mismatch、确定性重复抑制和 coverage-report 聚合。只读公开 MCP
`drawing.validate` 已能在启动 Provider session 前解析 bounded requirement/evidence JSON，再绑定精确 document identity
执行 inspection 并返回确定性 plan。本切片仍未 materialize 所有缺失尺寸、执行 AutoDimension scheme 或创建 native datum/GD&T。

## D07 manufacturing annotation provenance and native Model Items boundary / D07 制造标注来源与 native Model Items 边界

Issue #35 requires hole/thread callouts, center marks/centerlines, datums, GD&T, surface finish, weld symbols and
notes to remain associative and provenance-bearing. The new provider-neutral `ManufacturingAnnotationPlanner` keeps a
manufacturing requirement separate from its native annotation identity. Matching is exact on requirement identity,
feature identity, annotation kind and drawing view; visible text, screenshot similarity and arbitrary annotation count
are not evidence. One approved/released associative native annotation is retained. Missing evidence produces a native
import plan but remains non-release until the provider read-back proves the result.

Issue #35 要求 hole/thread callout、center mark/centerline、datum、GD&T、表面粗糙度、焊接符号和 note 保持关联并带有
provenance。新增的 vendor-neutral `ManufacturingAnnotationPlanner` 将制造要求与 native annotation identity 分离。
匹配必须同时满足 requirement identity、feature identity、annotation kind 和 drawing view；不使用可见文字、截图相似度或
任意 annotation 数量作为证据。只有一条 Approved/Released 且仍关联的 native 标注可以保留；缺少证据时只生成 native
import plan，在 Provider 回读证明之前不能 release。

The CadAbstractions request now exposes an allowlisted `DrawingModelAnnotationImportKinds` mask and an explicit
`DrawingAnnotationApprovalState`. The SOLIDWORKS Provider maps the mask to the verified `swInsertAnnotation_e` values
and calls `IDrawingDoc.InsertModelAnnotations3` on the exact registered drawing view. It accepts only the native
annotation types that correspond to the requested category, records native type/count, feature identity, provenance and
approval evidence, and fails closed when SOLIDWORKS returns no requested native annotation. Inspection now classifies
native datum, datum-target and cosmetic-thread annotations instead of leaking numeric vendor types.

本轮 CadAbstractions 增加 allowlisted `DrawingModelAnnotationImportKinds` mask 和显式
`DrawingAnnotationApprovalState`。SOLIDWORKS Provider 将 mask 映射到已核验的 `swInsertAnnotation_e`，在精确登记的
drawing view 上调用 `IDrawingDoc.InsertModelAnnotations3`。只接受与请求类别匹配的 native annotation type，记录 native
type/count、feature identity、provenance 和 approval evidence；若 SOLIDWORKS 没有返回请求类别则 fail-closed。Inspection
也能把 native datum、datum-target、cosmetic-thread 分类为稳定 vendor-neutral kind，而不是泄漏厂商数字 type。

This slice now has an explicit `ManufacturingAnnotationMaterializer`. High-level part drawing generation creates a
`ModelDimension` requirement, plans it through `ManufacturingAnnotationPlanner`, calls the provider-neutral
`ICadDrawingDocument.AddAnnotationAsync` contract, and promotes the item from Warning to Pass only after a native
annotation identity is returned. The materializer validates all import requests before the first mutation, rejects two
requirements sharing one provider view/category scope, and preserves the updated plan fingerprint. The native Provider
also fails closed when `InsertModelAnnotations3` returns more than one matching annotation instead of silently keeping
the first item. This is the first end-to-end D02 materialization slice; feature-level selectors for Hole Wizard/PMI and
controlled AutoDimension fallback remain subsequent work.

本切片现在具备明确的 `ManufacturingAnnotationMaterializer`。高层零件出图先创建 `ModelDimension` requirement，经过
`ManufacturingAnnotationPlanner`，调用 vendor-neutral `ICadDrawingDocument.AddAnnotationAsync` contract；只有 Provider
返回 native annotation identity 后，item 才从 Warning 提升为 Pass。materializer 在第一个 mutation 前完成所有 import
请求校验，拒绝两个 requirement 共用同一 provider view/category scope，并保存更新后的 plan fingerprint。native
Provider 在 `InsertModelAnnotations3` 返回多个匹配标注时也会 fail-closed，不再静默保留第一项。这是 D02 首个端到端
materialization slice；Hole Wizard/PMI 的 feature-level selector 与受控 AutoDimension fallback 仍属于后续工作。

Center marks and centerlines deliberately remain `annotation-provider-contract-required` in this slice. Their official
SOLIDWORKS APIs (`IDrawingDoc.InsertCenterMark3` / `InsertCenterLine2`) require a safe declarative geometry selector and
post-write association proof; the generic Model Items bitmask is not sufficient. The planner therefore blocks them for
review instead of synthesizing a symbol or silently selecting a transient enumeration index. Hole Callout live proof also
requires a real Hole Wizard feature; the existing baseline fixture uses a sketch `FeatureCut4`, so this slice does not
claim a native Hole Callout from that unrelated cut feature.

Center mark 与 centerline 在本切片中明确返回 `annotation-provider-contract-required`。官方
`IDrawingDoc.InsertCenterMark3` / `InsertCenterLine2` 需要安全的声明式 geometry selector 和写后关联证明，通用 Model
Items bitmask 不足以安全实现。因此 planner 会阻断并要求复核，不合成符号，也不静默使用临时 enumeration index。Hole
Callout 的 Live 证明还必须基于真实 Hole Wizard feature；当前 baseline fixture 是 sketch `FeatureCut4`，所以本切片不
宣称从无关的 cut feature 得到了 native Hole Callout。

Focused evidence for this slice:

    dotnet test tests/SolidWorksMcp.UnitTests/SolidWorksMcp.UnitTests.csproj -c Release --no-restore --logger "console;verbosity=minimal" # exit 0; 84 passed; 0 failed; 0 skipped
    dotnet build providers/SolidWorksMcp.Provider.SolidWorks/SolidWorksMcp.Provider.SolidWorks.csproj -c Release --no-restore -p:SolidWorksInstallRoot=D:\Solidworks2022\SOLIDWORKS -v:minimal # exit 0; 0 warnings; 0 errors

No native SOLIDWORKS process was started for the pure planner/provider compile slice; the machine inventory remained
`SLDWORKS_COUNT=0`. Any subsequent native Hole Wizard/Model Items run must use `scripts/Invoke-SolidWorksLiveTests.ps1`,
which closes existing sessions before launch, owns exactly one fresh process and verifies zero remaining processes after
cleanup. Private drawing-PDF content is not stored in this repository.

本轮纯 planner/provider compile slice 没有启动 native SOLIDWORKS；机器进程清单保持 `SLDWORKS_COUNT=0`。后续任何
Hole Wizard/Model Items native 运行都必须通过 `scripts/Invoke-SolidWorksLiveTests.ps1`：启动前关闭现有会话，只拥有一个
新进程，清理后验证进程数为零。秘密图纸 PDF 内容不会存入本仓库。

## D08 drawing QA, targeted repair and release evidence / D08 工程图 QA、定向修复与 Release evidence

Issue #36 requires `drawing.validate`, targeted deterministic repair and `drawing.release` to fail closed when rebuild
health, requirement coverage, annotation association/provenance, layout, tolerance evidence or export verification is
missing. The new vendor-neutral `DrawingQaReleasePlanner` is the first D08 foundation slice. It consumes the existing
inspection, feature-level coverage, layout and manufacturing-annotation plans and produces one unified finding stream
with `PASS`, `WARNING`, `REVIEW_REQUIRED` and `BLOCKING` states. It never uses screenshot similarity, OCR or private
drawing-PDF content as proof.

Issue #36 要求 `drawing.validate`、确定性的 targeted repair 和 `drawing.release` 在 rebuild 健康度、需求 coverage、标注关联/
provenance、布局、公差 evidence 或导出验证缺失时 fail closed。本轮新增 vendor-neutral `DrawingQaReleasePlanner` 作为
D08 foundation slice：它汇总现有 inspection、逐特征 coverage、layout 和制造标注 plan，输出统一的 `PASS`、`WARNING`、
`REVIEW_REQUIRED`、`BLOCKING` findings。它不使用截图相似度、OCR 或秘密图纸 PDF 内容作为证明。

The planner treats unavailable checks as blocking: no layout proof, no manufacturing annotation provenance plan, no
configured artifact format, missing artifact, unverified SHA-256, artifact/source-state mismatch, rebuild error, state
conflict, unresolved requirement or unsupported annotation cannot be released. Supported repair actions are deliberately
narrow: redundant dimension suppression and applying an already-planned annotation position. Each action carries a
stable target identity and precondition fingerprint, so a future Provider must re-inspect the exact drawing immediately
before mutation and cannot move unrelated annotations.

Planner 对 unavailable check 采取 blocking：没有 layout proof、没有制造标注 provenance plan、没有配置 artifact format、artifact
缺失、SHA-256 未验证、artifact 与 source state 不一致、rebuild error、state conflict、未解决 requirement 或不支持的标注均不能
release。当前支持的 repair action 刻意保持很窄：抑制重复尺寸、应用已经规划好的标注位置。每个 action 都携带 stable target
identity 和 precondition fingerprint，未来 Provider 必须在 mutation 前重新 inspection 精确 drawing，不能移动无关标注。

`DrawingQaReleasePlanner.VerifyArtifact` computes a lowercase SHA-256 without modifying the file. `CreateManifest` emits
the exact drawing identity, state hash, QA fingerprint, all findings, artifact paths/checksums and the release decision;
the creation timestamp is intentionally excluded from the stable manifest fingerprint. A blocked manifest is evidence of
refusal, not a released drawing.

`DrawingQaReleasePlanner.VerifyArtifact` 在不修改文件的情况下计算 lowercase SHA-256。`CreateManifest` 输出精确 drawing identity、
state hash、QA fingerprint、全部 findings、artifact path/checksum 和 release decision；创建时间刻意不参与稳定 manifest fingerprint。
Blocked manifest 只是拒绝证据，不能冒充已 release 的工程图。

Focused D08 unit evidence:

    dotnet test tests/SolidWorksMcp.UnitTests/SolidWorksMcp.UnitTests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~DrawingQaReleasePlannerTests --logger "console;verbosity=minimal" # exit 0; 5 passed; 0 failed; 0 skipped
    dotnet format SolidWorksMcp.hosted.slnx --no-restore --verify-no-changes --severity info --verbosity quiet # exit 0
    dotnet build SolidWorksMcp.hosted.slnx -c Release --no-restore -p:ContinuousIntegrationBuild=true -v:minimal # exit 0; 0 warnings; 0 errors
    dotnet build providers/SolidWorksMcp.Provider.SolidWorks/SolidWorksMcp.Provider.SolidWorks.csproj -c Release --no-restore -p:SolidWorksInstallRoot=D:\Solidworks2022\SOLIDWORKS -v:minimal # exit 0; 0 warnings; 0 errors
    dotnet test SolidWorksMcp.hosted.slnx -c Release --no-build --no-restore --logger "console;verbosity=minimal" # exit 0; Unit 89 + Contract 13 + FakeCad 11 passed
    git diff --check # exit 0

Native D08 repair evidence on the local SOLIDWORKS 2022 machine:

    dotnet build tests/SolidWorksMcp.LiveSolidWorksTests/SolidWorksMcp.LiveSolidWorksTests.csproj -c Release --no-restore -p:SolidWorksInstallRoot=D:\Solidworks2022\SOLIDWORKS -v:minimal # exit 0; 0 warnings; 0 errors
    powershell -ExecutionPolicy Bypass -File .\scripts\Invoke-SolidWorksLiveTests.ps1 -SolidWorksPath D:\Solidworks2022\SOLIDWORKS\SLDWORKS.exe -Filter FullyQualifiedName~D08AnnotationRepairLiveTests -NoBuild # exit 0; 1 passed; 0 failed; 0 skipped
    SLDWORKS_COUNT_AFTER=0

This slice also adds a narrow `ICadDrawingDocument.RepositionAnnotationAsync` contract. FakeCad proves exact
annotation identity, expected document state and expected current position are checked before mutation, and the new
position is read back. The native Provider uses the locally reflected `IAnnotation.SetPosition2(Double, Double, Double)`
signature on its STA, resolves a persisted annotation name rather than an enumeration index, rebuilds, reads the native
position back and commits the descriptor only after that proof. The isolated native Live fixture now proves the same
stale-precondition rejection and position persistence through save/reopen; it does not use private drawing PDFs.

The public `drawing.repair` MCP mutation endpoint is now present. It rebinds an exact registered drawing identity,
requires the expected state hash, accepts exactly one bounded `layout.apply-planned-position` action, invokes the
provider-native precondition/read-back contract, saves, reopens and verifies the persisted annotation position. The
endpoint deliberately does not yet claim multi-action checkpoint/rollback, PDF/DWG/DXF release export or durable
manifest writing; those remain the next D08 transaction/release slice. Any native run must continue through
`scripts/Invoke-SolidWorksLiveTests.ps1`, which closes old SOLIDWORKS before launch and verifies `SLDWORKS_COUNT=0`
after cleanup. The existing native baseline remains the actual curved-part, through-hole, multi-view, section-view
and PDF proof.

本切片同时增加了窄范围的 `ICadDrawingDocument.RepositionAnnotationAsync` contract。FakeCad 已证明 mutation 前会校验精确
annotation identity、expected document state 和 expected current position，并读回新位置。Native Provider 在 STA 上使用本机
反射核对的 `IAnnotation.SetPosition2(Double, Double, Double)` 签名，通过持久化 annotation name 而不是 enumeration index
解析对象，执行 rebuild、读回 native position，只有证据成功才提交 descriptor。隔离 Native Live fixture 已进一步证明 stale
precondition 会被拒绝，位置可以 save/reopen 后读回；测试不使用秘密图纸 PDF。

Public `drawing.repair` MCP mutation endpoint 已加入：它重新绑定精确 registered drawing identity，要求 expected state hash，
只接受一个有界 `layout.apply-planned-position` action，调用 Provider 的 precondition/read-back contract，保存、重开并
验证 annotation 位置持久化。本端点暂不宣称已完成多 action checkpoint/rollback、PDF/DWG/DXF release export 或持久化
manifest writing；这些仍是下一步 D08 transaction/release slice。任何 native run 仍必须通过
`scripts/Invoke-SolidWorksLiveTests.ps1`：启动前关闭旧 SOLIDWORKS，清理后验证 `SLDWORKS_COUNT=0`。现有 native baseline
仍是真实曲面零件、通孔、多视图、剖视和 PDF 证明。

Public MCP D08 repair evidence:

    dotnet test SolidWorksMcp.hosted.slnx -c Release --no-build --no-restore --logger "console;verbosity=minimal" # exit 0; Unit 89 + Contract 13 + FakeCad 11 passed
    dotnet build tests/SolidWorksMcp.LiveSolidWorksTests/SolidWorksMcp.LiveSolidWorksTests.csproj -c Release --no-restore -p:SolidWorksInstallRoot=D:\Solidworks2022\SOLIDWORKS -v:minimal # exit 0; 0 warnings; 0 errors
    powershell -ExecutionPolicy Bypass -File .\scripts\Invoke-SolidWorksLiveTests.ps1 -SolidWorksPath D:\Solidworks2022\SOLIDWORKS\SLDWORKS.exe -Filter FullyQualifiedName~McpDrawingRepairLiveTests -NoBuild # exit 0; 1 passed; 0 failed; 0 skipped
    SLDWORKS_COUNT_BEFORE=0
    SLDWORKS_COUNT_AFTER=0
## C04 functional tolerance engine foundation / C04 功能公差引擎基础

Issue #26/C04 has a vendor-neutral foundation in commit `a12e735`. It is intentionally a domain-layer slice, not a
claim that native SOLIDWORKS tolerance annotations or every enterprise RulePack standard are complete.

- `src/SolidWorksMcp.Tolerancing/FunctionalToleranceEngine.cs` keeps design nominal, functional limits, drawing
  nominal/deviations, fit class, general tolerance, manufacturing capability, inspection method, criticality,
  provenance and approval state as separate fields.
- `ToleranceStackupRequest` retains signed terms and provenance. The default `WorstCase` evaluator computes adverse
  limits and returns an ordered derivation chain suitable for audit and `tolerance.explain` integration.
- The canonical installation case is represented as `installation-space - assembly-error - required-clearance`,
  yielding a maximum allowed part size of `98` from an installation space of `100`; the drawing representation is
  checked independently and cannot widen the functional maximum.
- Unapproved `private_drawing_review`/AI-origin requirements remain `REVIEW_REQUIRED`; drawing limits outside the
  functional envelope are `BLOCKING`. RSS and Monte-Carlo are explicit enum values but remain review-gated rather
  than silently treated as Worst-Case.
- `tests/SolidWorksMcp.UnitTests/FunctionalToleranceEngineTests.cs` covers the 100-to-98 derivation, approval gate,
  drawing-limit violation and explicit statistical-method review state. The implementation contains no SOLIDWORKS
  COM reference and requires no Live test.

Evidence:

    dotnet format SolidWorksMcp.hosted.slnx --no-restore --verify-no-changes --severity info --verbosity quiet # exit 0
    dotnet build SolidWorksMcp.hosted.slnx -c Release --no-restore -v:minimal                         # exit 0; 0 warnings; 0 errors
    powershell -ExecutionPolicy Bypass -File .\scripts\build-hosted.ps1                              # exit 0; Unit 93 + Contract 14 + FakeCad 11 passed

The pure public MCP `tolerance.explain` endpoint is now available. It accepts only bounded redacted JSON, performs
schema/provenance/finite-value validation before any Provider session is requested, and returns the deterministic
analysis result plus the ordered derivation evidence. The canonical contract case proves `100 - 1 - 1 = 98` and the
malformed-input case proves `StartSessionCount=0`; the endpoint has no SOLIDWORKS or COM dependency.

    dotnet format SolidWorksMcp.hosted.slnx --no-restore --verify-no-changes --severity info --verbosity quiet # exit 0
    powershell -ExecutionPolicy Bypass -File .\scripts\build-hosted.ps1                              # exit 0; Unit 93 + Contract 16 + FakeCad 11 passed
    git diff --check                                                                                  # exit 0

This remains a C04 foundation. Native `IDimensionTolerance` mapping, enterprise RulePack resolution and multi-part
assembly stack-ups remain follow-up work and are not represented as passed here. The current public endpoint is a pure
engineering-intelligence explanation boundary; it does not infer tolerances from drawings or release a drawing by
itself.

## C01 immutable engineering graph diff and invariant evidence / C01 immutable 工程语义图 diff 与不变量证据

Issue #23 requires versioned immutable graph contracts that keep CAD entities, engineering requirements and drawing
annotations as distinct identities, together with round-trip/diff behavior and rejection of invalid cycles, orphan
requirements and duplicate identities. `EngineeringGraph` now provides `CreateDiff`, which compares stable node IDs and
edge endpoint/kind identities rather than insertion order. Node payload changes and edge provenance-rationale changes
are reported separately in immutable `EngineeringGraphNodeChange` and `EngineeringGraphEdgeChange` collections.

Issue #23 要求 versioned immutable graph contract 保持 CAD entity、engineering requirement 和 drawing annotation 的
identity 分离，同时支持 round-trip/diff，并拒绝非法环、孤立 requirement 和重复 identity。`EngineeringGraph` 现在提供
`CreateDiff`：按 stable node ID 及 edge endpoint/kind identity 比较，不依赖插入顺序；node payload 变化和 edge provenance
rationale 变化分别记录在 immutable `EngineeringGraphNodeChange` 与 `EngineeringGraphEdgeChange` 中。

The graph constructor now rejects duplicate relationship identities, directed dependency/provenance cycles and isolated
`EngineeringRequirement` nodes before a planner can consume the snapshot. JSON round-trip re-runs the same validation, and
the implementation remains in `SolidWorksMcp.EngineeringModel` with no SOLIDWORKS interop reference.

本次 graph constructor 在 planner 消费 snapshot 之前拒绝重复 relationship identity、有向 dependency/provenance cycle 以及
没有任何 semantic relationship 的孤立 `EngineeringRequirement`。JSON round-trip 会重新执行同一套验证；实现仍位于
`SolidWorksMcp.EngineeringModel`，没有 SOLIDWORKS interop 引用。

Focused evidence:

    dotnet format tests/SolidWorksMcp.UnitTests/SolidWorksMcp.UnitTests.csproj --no-restore --verify-no-changes --severity info --verbosity quiet # exit 0
    dotnet test tests/SolidWorksMcp.UnitTests/SolidWorksMcp.UnitTests.csproj -c Release --no-restore --filter FullyQualifiedName~EngineeringGraphTests --logger "console;verbosity=minimal" # exit 0; 8 passed; 0 failed; 0 skipped

## C03 versioned RulePack foundation and provenance evidence / C03 版本化 RulePack 基础与 provenance 证据

Issue #25 requires versioned GB/enterprise/customer drawing rules, source metadata, deterministic precedence, conflict
diagnostics and rule-level explain output. The new 'SolidWorksMcp.RuleEngine' slice provides immutable typed data for
projection, sheet sizes, scale denominators, text/view spacing, section/detail labels, repeated-feature notation,
reserved zones and general tolerance. 'DrawingRulePackResolver' applies Standard → Enterprise → Customer in fixed order,
rejects duplicate identifiers and same-layer conflicts, preserves field-level provenance, and fails closed when the base
pack or an overlay is malformed.

Issue #25 要求版本化 GB/enterprise/customer 制图规则、来源 metadata、确定性覆盖优先级、冲突诊断以及规则级 explain。
新的 'SolidWorksMcp.RuleEngine' slice 为投影、图幅、比例分母、字高/视图间距、剖视/局部放大标签、重复特征表达、
保留区和一般公差提供 immutable typed data。'DrawingRulePackResolver' 按 Standard → Enterprise → Customer 固定
顺序合并，拒绝重复标识和同层冲突，保留字段级 provenance，并在 base/overlay malformed 时 fail closed。

'StandardRulePackCatalog' records official catalogue metadata for 'GB/T 1800.1-2020', 'GB/T 1804-2000' and
'GB/T 1182-2018' using the National Standards Public Service Platform. It stores URLs, status, retrieval time and
licensing notes only; standard full text, scanned pages and copyrighted tables are not committed. The catalog values are
explicit compiler defaults and are not presented as a complete transcription of those standards. Enterprise/customer
packs remain the governed place for project-specific clauses and detailed tolerance tables.

本切片已经把 'ResolvedDrawingRulePack' 接入 part view planner、part-to-drawing build service 和 MCP 高层入口：
默认 native build 使用 GB profile 的第一角投影布局，并在请求 scale 不被 RulePack 允许时于 Provider session 之前 fail closed。
RulePack identity、projection 和 projection provenance 会进入 build evidence。它仍不是完整 Drawing Compiler：dimension
coverage、title block、section/detail 自动选择、layout/QA 和企业/customer RulePack 输入仍需后续 Issue 实现。
秘密 '图纸/' PDF 不进入仓库、日志、截图或 artifact。

Focused and Hosted evidence:

    dotnet format SolidWorksMcp.hosted.slnx --no-restore --verify-no-changes --severity info --verbosity quiet # exit 0
    dotnet build SolidWorksMcp.hosted.slnx -c Release --no-restore -v:minimal                         # exit 0; 0 warnings; 0 errors
    dotnet test tests/SolidWorksMcp.UnitTests/SolidWorksMcp.UnitTests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~RulePackTests --logger "console;verbosity=minimal" # exit 0; 7 passed; 0 failed; 0 skipped
    dotnet test tests/SolidWorksMcp.UnitTests/SolidWorksMcp.UnitTests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~PartDrawingPlannerTests --logger "console;verbosity=minimal" # exit 0; 5 passed; 0 failed; 0 skipped
    powershell -ExecutionPolicy Bypass -File .\scripts\build-hosted.ps1                               # exit 0; Unit 106 + Contract 16 + FakeCad 13 passed
    dotnet build tests/SolidWorksMcp.LiveSolidWorksTests/SolidWorksMcp.LiveSolidWorksTests.csproj -c Release --no-restore -p:SolidWorksInstallRoot=D:\Solidworks2022\SOLIDWORKS -v:minimal # exit 0; 0 warnings; 0 errors
    powershell -ExecutionPolicy Bypass -File .\scripts\Invoke-SolidWorksLiveTests.ps1 -SolidWorksPath D:\Solidworks2022\SOLIDWORKS\SLDWORKS.exe -Filter FullyQualifiedName~McpReferenceDrivenSinglePartClassesCreateVerifiedThreeDAndTwoDArtifacts -NoBuild # exit 0; Live 1 passed; 0 failed; 0 skipped
    SLDWORKS_COUNT_BEFORE=0
    SLDWORKS_COUNT_AFTER_OLD=0
    SLDWORKS_COUNT_AFTER=0
    git diff --check                                                                                  # exit 0

The official metadata pages used for this catalog are [GB/T 1800.1-2020](https://openstd.samr.gov.cn/bzgk/std/newGbInfo?hcno=B2EA3A6454B903DCF466A0CE16F2ED26),
[GB/T 1804-2000](https://openstd.samr.gov.cn/bzgk/std/newGbInfo?hcno=1BF8CFBC644315488F68433EEC2F9D58) and
[GB/T 1182-2018](https://openstd.samr.gov.cn/bzgk/std/newGbInfo?hcno=C87A687A21B36E2F2D3A09BDF03DCA01). The pages identify
the standards and their current catalogue status; the implementation intentionally uses metadata rather than copying
the standard text.

The retained native PDFs from the latest run were rendered and visually checked from the user-local Live workspace:
the rounded plate has a real solid body, front/top/isometric views, a native dimension and first-angle top-view placement;
the formed U-bracket has the corresponding curved wall geometry and native dimension. The generated artifacts remain
outside Git and contain no private source-PDF identifiers.

The focused tests cover round-trip fingerprint stability, insertion-order independence, duplicate node identity,
dangling edges, orphan requirements, directed cycles, added/removed/changed node and edge diff entries, and empty diffs.
The full hosted gate remains required before treating C01 as complete.

## Native reference-driven single-part 3D + 2D evidence / 原生参考驱动单零件三维+二维证据

The local reference-driven Live slice now exercises the public MCP path with exactly two redacted, non-assembly
semantic classes observed during private drawing review: a rounded plate and a formed U-bracket. The profiles are
generic test inputs; they do not contain private PDF filenames, title-block content or source drawing text. Each case
creates a real SOLIDWORKS part from a connected line/arc sketch, persists an `SLDPRT`, creates a native drawing with
orthographic and isometric views, persists an `SLDDRW`, exports a PDF, and verifies non-empty artifacts through the MCP
result contract. The high-level path now materializes the native Model Item through the manufacturing-annotation plan
and asserts its read-back identity after reopen. The strengthened result assertions also require one solid body, a native feature and topology entity,
positive measured volume, a non-degenerate three-axis body bounding box, the requested extrusion depth, at least three
persisted drawing views and at least one read-back annotation. The rendered PDF evidence was inspected locally; only the
generic semantic conclusion is recorded here.

The run is isolated under the user-local Live workspace and is not copied into the repository. The process harness
closed the previous SOLIDWORKS instance before launch and shut down the one owned instance afterward:

    powershell -ExecutionPolicy Bypass -File .\scripts\Invoke-SolidWorksLiveTests.ps1 -SolidWorksPath D:\Solidworks2022\SOLIDWORKS\SLDWORKS.exe -Filter FullyQualifiedName~McpReferenceDrivenSinglePartClassesCreateVerifiedThreeDAndTwoDArtifacts -NoBuild
    # first run: exit 0; Live 1 passed; 0 failed; 0 skipped; SLDWORKS_COUNT_BEFORE=1; SLDWORKS_COUNT_AFTER_OLD=0; SLDWORKS_COUNT_AFTER=0
    # artifact-retention run: exit 0; Live 1 passed; 0 failed; 0 skipped; SLDWORKS_COUNT_BEFORE=0; SLDWORKS_COUNT_AFTER_OLD=0; SLDWORKS_COUNT_AFTER=0
    # strengthened native-evidence run: exit 0; Live 1 passed; 0 failed; 0 skipped; SLDWORKS_COUNT_BEFORE=0; SLDWORKS_COUNT_AFTER_OLD=0; SLDWORKS_COUNT_AFTER=0

The retained artifacts are user-local evidence only. This proves the current MCP-to-native 3D/2D path for two generic
single-part classes and catches incorrect COM geometry interpretation: the failed pre-fix run exposed a degenerate Z box,
and the provider was corrected after checking the official `IBody2.GetBodyBox` contract `[X1,Y1,Z1,X2,Y2,Z2]`. It does
not claim that private drawing dimensions, annotations, tolerance provenance or full manufacturing coverage have been
reconstructed. Those remain gated by the Engineering Requirement Graph, provider inspection and the later Drawing Compiler
issues.

留存的 artifact 只存在于用户本地 evidence workspace。这证明了两个通用单零件类别当前从 MCP 到 native 三维/二维的链路，
并捕获了错误的 COM 几何解释：前一次失败暴露了退化 Z 包围盒，随后依据官方 `IBody2.GetBodyBox` contract
`[X1,Y1,Z1,X2,Y2,Z2]` 修正 Provider。它不声称已经重建秘密图纸的尺寸、全部标注、公差 provenance 或完整制造 coverage；
这些仍由 Engineering Requirement Graph、Provider inspection 和后续 Drawing Compiler issues 约束。
