# GitHub Issue evidence baseline

Retrieved from the public repository on 2026-09-16 (Asia/Shanghai) using the GitHub Issue API and issue pages. The issue bodies and the root Epic comment are the authoritative scope; this file is an index and execution record, not a replacement for them.

- Repository: https://github.com/lqepoch/solidworks_mcp
- Root Epic: https://github.com/lqepoch/solidworks_mcp/issues/1
- Issue count at retrieval: 69 open issues

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
and `cad.capabilities` requests. The response identified the native provider and exposed four tools. The smoke test
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
  provider boundary. FakeCad now round-trips them, while the native drawing provider remains intentionally deferred
  until the drawing COM implementation is researched and verified.

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
