# Third-party notices and provenance policy

This file records third-party material used or consulted by the project. The bootstrap commit does not copy source code from any upstream repository into product assemblies.

The product repository itself is distributed under the MIT License in LICENSE. That license does not grant rights to SOLIDWORKS vendor binaries or copyrighted standards/manuals. The tracked `references/` directories are source snapshots under their respective upstream licenses; they are not product dependencies.

## Upstream research repositories

Exact commit pins, URLs, license evidence, reviewed files and adoption status are tracked in references/manifest.json. The
source snapshots are intentionally committed for auditability but remain excluded from normal product build/package/runtime.

| Upstream | License | Current use | Direct code adaptation |
| --- | --- | --- | --- |
| jaylamping/solidworks-mcp | MIT | Research reference for STA/session safety and checkpoints | None |
| czuryk/SolidworksMCP | MIT | Research reference for transactions and invariant verification | None |
| Slacker-LLC/solidworks-mcp | Apache-2.0 | Research reference for drawing/selection/unit boundaries | None |
| hjbaard/SolidWorks-MCP | MIT | Research reference for live verification workflows | None |
| eyfel/mcp-server-solidworks | MIT | Research reference for intent-oriented MCP tool composition and prompt-facing operation descriptions | None |
| modelcontextprotocol/csharp-sdk | Apache-2.0 | Official MCP SDK dependency and API reference | None beyond package use |

When code is adapted, the contributor must add the exact source repository, commit, file/symbol, license and notice text to the manifest and this document before merging.

## Product dependencies

The official ModelContextProtocol NuGet package is Apache-2.0 licensed. The test-only xUnit and Microsoft.NET.Test.Sdk packages are not shipped as runtime product payload; their versions remain centrally pinned for reproducible CI.

## SOLIDWORKS binaries and documentation

SolidWorks.Interop.*, SLDWORKS.exe, type libraries, vendor templates and official help are discovered from the user's licensed installation. They are not committed or redistributed by this public repository. Local paths may be written only to user-local generated configuration/MSBuild properties.

The reproducible local-only API corpus is maintained by `scripts/sync-official-solidworks-api.ps1` under `%LOCALAPPDATA%\SolidWorksMcp\official-api`. It may copy the licensed 2022 `api` directory and cache official 2022/2026 web entry points, but it deliberately records missing or unavailable resources instead of claiming that a 2026 offline package exists. See `docs/research/official-solidworks-api-local.md` for the source URLs and redistribution boundary. This local corpus is not a product dependency and is not included in packages or Git history.

`scripts/sync-official-solidworks-api.ps1` 只在 `%LOCALAPPDATA%\SolidWorksMcp\official-api` 维护本地 API 研究资料；它可以复制获授权的 2022 `api` 目录并缓存 2022/2026 官方网页入口，但会明确记录缺失资源，不会把不存在的 2026 离线包伪装成成功。详见 `docs/research/official-solidworks-api-local.md`。

## Standards and enterprise rules

The project stores rule metadata, identifiers, effective dates, URLs and derived legal rule data. It does not redistribute copyrighted standards or official manuals. Enterprise/customer rule packs remain external inputs.
