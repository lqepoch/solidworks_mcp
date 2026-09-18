# SolidWorks MCP Control Plane

> 本文定义项目的 MCP 产品边界。工程图、三维建模和 SOLIDWORKS API 都是后端能力，不是 MCP Server 的边界本身。

## 1. Product boundary / 产品边界

The product is a safe, stateful, provider-neutral MCP control plane for one or more explicit SOLIDWORKS sessions.
The server accepts a bounded engineering intent, binds it to an exact session/document/configuration, delegates to a
provider adapter, and returns a versioned result with evidence. It is not a natural-language-to-CAD macro runner.

产品是一个安全、有状态、Provider-neutral 的 MCP control plane，用于控制一个或多个显式 SOLIDWORKS session。Server
接收有界工程意图，把它绑定到精确的 session/document/configuration，交给 Provider adapter 执行，并返回带证据的
版本化结果。它不是“自然语言转 CAD 宏”的执行器。

```text
Codex / MCP Client
        |
        | stdio JSON-RPC (official MCP C# SDK)
        v
SolidWorksMcp.Server                 transport adapter only
        |
        v
McpControlPlane                      registry / policy / binding / audit
        |
        +--> operation planner         immutable, bounded engineering request
        +--> CadTransactionEngine      checkpoint / retry / rollback / idempotency
        +--> session + document router exact identity, configuration, state hash
        +--> evidence + diagnostics    structured, redacted, deterministic
        v
SolidWorksMcp.CadAbstractions        vendor-neutral provider contract
        |
        +--> FakeCad                   hosted-safe contract and regression provider
        +--> Provider.SolidWorks       single STA dispatcher and COM adapter
                                               |
                                               v
                                         SLDWORKS.exe
```

### Responsibilities

| Layer | Owns | Must not own |
|---|---|---|
| MCP Server | MCP schema, stdio, tool/resource registration, result conversion | COM calls, geometry algorithms, hidden ActiveDoc routing |
| Core Control Plane | risk admission, capability gate, exact binding, idempotency, audit | drawing layout, SOLIDWORKS enum values |
| Core Transaction | plan validation, finite budget, checkpoint, recovery, verification | vendor-specific save/reopen implementation |
| Engineering modules | requirement graph, tolerance, drawing/assembly planning | MCP transport and COM RCW lifetime |
| CadAbstractions | stable CAD identities, requests, snapshots, capability contracts | `SolidWorks.Interop.*` types |
| SolidWorks Provider | STA thread, COM lifecycle, native API calls, read-back inspection | MCP tool definitions, business policy, AI prompting |

## 2. MCP surface / MCP 工具表面

The default surface stays compact. A client should normally call one high-level operation, not hundreds of primitive
`add_dimension`, `select_mark` or `run_macro` tools.

默认 surface 保持紧凑。客户端通常调用一个高层 operation，而不是数百个 `add_dimension`、`select_mark` 或
`run_macro` primitive tool。

### Tiers

- `read`: health, capability discovery, inspection, requirement/tolerance explanation. No CAD mutation.
- `mutation`: create or change one bounded model/drawing target. Requires exact session binding, replay key and the
  state contract appropriate to the target.
- `release`: drawing release, export and manifest. Requires QA pass, approval/provenance evidence, checkpoint and
  final inspection.
- `advanced/debug`: diagnostic primitives may exist for controlled development, but are not in the default Agent
  surface and never accept arbitrary code or PowerShell.

`McpToolCatalog` remains the human/Agent discovery view. It now also produces typed `McpOperationDescriptor` values for
the Core gate; descriptive strings do not replace executable policy.

## 3. Invocation lifecycle / 请求生命周期

Every operation follows the same control-plane sequence. A read operation may omit checkpointing, but it still uses the
same identity and capability checks.

每个 operation 遵循同一控制面顺序。只读 operation 可以不创建 checkpoint，但仍使用相同的 identity 和 capability
检查。

1. `tools/list` and `cad.capabilities` expose a compact, deterministic allowlist.
2. The client sends a versioned request with an application `operationId`; a mutation also carries an idempotency key.
3. The Core gate resolves the exact descriptor and checks risk, feature flags, provider capability and path policy.
4. The request starts or selects a known provider session, never an arbitrary active document.
5. The request is enriched with `sessionId`, `documentId`, configuration and expected `stateHash`.
6. A mutation enters `CadTransactionEngine`: preflight, single-writer acquisition, checkpoint, execution, rebuild,
   inspection, invariant verification, commit; failure enters bounded recovery and proves restoration.
7. The provider returns vendor-neutral `OperationResult<T>` evidence. A bare COM boolean is not success evidence.
8. The audit sink records redacted admission and terminal events. Paths, PDF text, credentials and COM exception text
   are not part of the public audit record.

The current first integration applies the second, bound control-plane gate immediately before the existing high-level
provider calls. `drawing.release` already uses the complete Core transaction engine. The next migration slices move
`cad.create-part`, drawing build, repair and assembly mutations to the same transaction executor one operation at a time.

当前第一阶段已经在现有高层 Provider call 前加入第二道 bound control-plane gate；`drawing.release` 已使用完整 Core
transaction engine。下一步按小切片把 `cad.create-part`、工程图 build、repair 和装配 mutation 逐个迁移到同一事务执行器。

## 4. Identity and state / Identity 与状态

The following values are separate and are never inferred from the current UI:

```text
MCP operationId
  -> provider sessionId
      -> documentId + document type + path policy
          -> configuration
              -> expected stateHash
                  -> transactionId + idempotencyKey
```

If any value is missing or stale, the operation stops with a stable error such as `STATE_CONFLICT`, `PATH_NOT_ALLOWED`,
`SELECTION_STALE` or `UNSUPPORTED_CAPABILITY`. The server does not “try the current ActiveDoc” as a fallback.

## 5. Engineering capabilities behind MCP / MCP 后端工程能力

The backend modules are intentionally callable through bounded operations:

- `EngineeringModel`: design intent, manufacturing requirements, assembly interfaces and provenance.
- `RuleEngine`: versioned GB/enterprise RulePack data and release policy.
- `Tolerancing`: functional limits, worst-case stack-up and explainable 100 → 98 clearance reasoning.
- `AutoDrawing`: view planning, native annotation planning, coverage, deterministic layout and repair planning.
- `AssemblyDrawing`: BOM, stable `ItemIdentity`, balloons, interfaces and assembly-specific QA.
- `Provider.SolidWorks`: model/drawing/assembly COM execution and read-back proof.

AI may propose intent or a review item. It cannot silently promote an inferred tolerance, datum, fit, GD&T or drawing
dimension to a released artifact. Release remains a policy decision over provenance, QA and approval evidence.

## 6. Private drawing references / 秘密图纸参考

The local `图纸/` directory is research input only. It is not an MCP payload, test fixture, log source, resource, or
public reference artifact. Any extracted engineering knowledge must be reduced to redacted, versioned rule/requirement
data with no PDF path, title block text, screenshot or proprietary geometry. The repository may contain public upstream
code and provenance metadata under `references/`, but never private drawings.

本机 `图纸/` 目录只用于研究，不得成为 MCP payload、测试 fixture、日志、resource 或公开参考文件。提取的工程知识只能
转化为脱敏的、版本化的 rule/requirement 数据，不能包含 PDF 路径、标题栏文字、截图或专有几何。`references/` 可保留
公开 upstream 代码及 provenance metadata，但绝不提交秘密图纸。

## 7. Migration order / 迁移顺序

1. Keep hosted-safe build green and preserve the current `FakeCad` contract evidence.
2. Finish the control-plane contract and audit sink in Core; add architecture tests forbidding vendor references.
3. Introduce one session/document router and route all model mutations through one transaction adapter.
4. Add provider capability negotiation and explicit unsupported responses before expanding tools.
5. Add compact high-level engineering operations: `part.create`, `part.inspect`, `assembly.inspect`,
   `drawing.compile`, `drawing.validate`, `drawing.release`.
6. Add advanced/debug primitives only behind explicit tier and feature policy.
7. Add real SOLIDWORKS Live tests using an isolated temporary workspace and a safe process lease. Never launch a second
   session while an existing session is blocked by a modal dialog.

This order makes the MCP boundary testable without SOLIDWORKS and lets the native provider grow behind a stable contract.

## 8. Non-goals / 明确不做

- No arbitrary COM expression, macro, PowerShell or shell execution tool.
- No implicit ActiveDoc mutation.
- No “AI guessed the dimensions” release path.
- No pixel screenshot as the only geometry proof.
- No provider COM assembly reference in Core, Server, EngineeringModel, RuleEngine, Tolerancing or AutoDrawing.
- No automatic copying of private PDF contents into source, tests, artifacts or diagnostics.
