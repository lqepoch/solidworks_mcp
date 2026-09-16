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
