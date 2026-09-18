# Upstream reference review / 上游参考复核

This file records the architectural conclusions from the pinned source snapshots in this directory. The snapshots are
source references, not product dependencies. They are excluded from every product project, package, runtime probe and
Hosted-safe build. No upstream implementation is copied into the production assemblies by this bootstrap change.

本文记录本目录固定源码快照的架构复核结论。快照只用于源码参考，不是产品依赖；产品项目、打包、运行时探针和
Hosted-safe build 均不会引用本目录。本次 bootstrap 没有把任何上游实现复制进生产程序集。

## Reference matrix / 参考矩阵

| Snapshot | Reviewed implementation areas | Reusable architectural lesson | Explicit rejection |
| --- | --- | --- | --- |
| `jaylamping-solidworks-mcp` | `WorkerSessionHost`, `RunningObjectTable`, `ComCallGuard`, `CommandSafety`, `PathGuard`, tool manifest | Process/session identity, explicit safety metadata, guarded COM calls and allowlisted paths | Node/JavaScript as a required runtime; screenshot-only evidence; direct source adaptation |
| `czuryk-SolidworksMCP` | `runtime.py`, `transactions.py`, `com_utils.py`, `geometry_probe.py`, `sw_finder.py`, `units.py`, `features.py` | Inspect/rebuild/verify sequencing, structured error categories, unit boundary and installation discovery | Python/pywin32 runtime dependency; fail-open checkpoint behavior; unbounded dynamic dispatch |
| `Slacker-LLC-solidworks-mcp` | `sw_core.py`, `sw_drawing.py`, `sw_inspect.py`, `sw_file.py`, `tlb_probe.py`, `NOTICE` | Separate drawing/lifecycle concerns, type-library probing first, explicit Apache NOTICE handling | Python product chain; unverified drawing mutation; visual screenshot as geometry proof |
| `hjbaard-SolidWorks-MCP` | `com_worker.py`, `session.py`, `binding.py`, `units.py`, hole/pattern/bracket fixtures | Single STA worker, early-binding compatibility investigation, small non-trivial live fixtures | Python/pywin32 runtime dependency; force-cancelling in-flight COM; scripts as production code |
| `eyfel-mcp-server-solidworks` | MCP server entry point, tool/prompt definitions, drawing adapter/read-model areas | Intent-oriented MCP surface, tool descriptions that teach an AI client the supported operation boundary | Python as product runtime; direct unbounded COM exposure; prompt/screenshot-only engineering proof |
| `modelcontextprotocol-csharp-sdk` | `StdioServerTransport`, server builder/DI extensions, stdio integration tests and notices | Official C# MCP boundary, DI composition, transport conformance tests and notice discipline | Forking SDK source into the product or treating the reference snapshot as a build dependency |

## Design decisions / 设计决策

1. The product uses pure .NET/C# at runtime. Python and Node material in these snapshots is research input only.
   产品运行时坚持纯 .NET/C#；快照中的 Python、Node 内容只作为研究输入。
2. COM calls remain behind `SolidWorksMcp.Provider.SolidWorks`, on one dedicated STA dispatcher, with process/document
   identity and state checks. COM wrappers do not own engineering semantics.
   COM 调用继续限制在 `SolidWorksMcp.Provider.SolidWorks`，通过专用 STA dispatcher 执行，并绑定进程、文档和状态；
   COM wrapper 不承载工程语义。
3. Feature examples from the Python references are used only to shape vendor-neutral `PartDefinition` and requirement
   contracts. A reference-driven part must be represented as a feature plan, not as a generic cylinder smoke test.
   上游 Python 示例只用于塑造 vendor-neutral 的 `PartDefinition` 与 requirement contract；参考驱动的零件必须表现为
   feature plan，不能退化为通用圆柱 smoke test。
4. Any direct reuse in a future change requires a new manifest entry containing the exact commit, file/symbol, license,
   notice obligation and a test proving the adaptation. Until then `directCodeAdaptation` remains `null`.
   后续若直接复用代码，必须先在 manifest 写入精确 commit、文件/符号、许可证、notice 义务和适配测试；在此之前
   `directCodeAdaptation` 保持 `null`。

## Confidential drawing boundary / 机密图纸边界

The local confidential drawing corpus is not part of this snapshot directory. Its PDFs, filenames, extracted text,
renders, dimensions, title blocks and CAD artifacts remain ignored and user-local. The private fixture workflow samples
exactly two single-part drawings per iteration and records only redacted engineering feature classes.

本地机密图纸 corpus 不属于本目录。PDF、文件名、提取文本、渲染结果、尺寸、标题栏和 CAD artifact 必须保持 ignored
及 user-local。私密 fixture 工作流每轮只抽取恰好两个单零件图，并且只记录脱敏后的工程特征类别。
