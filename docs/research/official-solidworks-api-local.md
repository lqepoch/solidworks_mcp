# Local SOLIDWORKS API corpus / 本地 SOLIDWORKS API 资料库

This project uses vendor-neutral contracts in the repository and keeps licensed SOLIDWORKS API material outside Git.
The local corpus is a research input, never a product dependency and never a public-repository payload.

本项目在仓库中只保留与厂商无关的契约；获授权的 SOLIDWORKS API 资料留在 Git 之外的用户本地缓存中。
本地资料库只用于研究，不是产品运行时依赖，也不是公共仓库发布物。

## Reproducible sync / 可重复同步

Run from the repository root in Windows PowerShell or PowerShell 7:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\sync-official-solidworks-api.ps1 -MajorVersion 2022,2026 -Apply
```

The default cache is:

```text
%LOCALAPPDATA%\SolidWorksMcp\official-api\
├── manifest.json
├── 2022\
│   ├── installed\api\       # copied from the licensed local installation when found
│   └── web\                  # official web pages/PDF fetched with provenance
└── 2026\
    ├── installed\api\       # present only when a licensed 2026 installation is discovered
    └── web\
```

The script records the source URL, retrieval time, local path, byte count, SHA-256, installation version and
availability status. A 404, missing installation or blocked download is recorded as `missing-or-unavailable`; it is
never reported as a successful local API copy.

脚本记录来源 URL、抓取时间、本地路径、字节数、SHA-256、安装版本和可用状态。404、没有本地安装或下载受阻时，
状态会写为 `missing-or-unavailable`，绝不会冒充已成功拉取。

## Current machine evidence / 当前机器证据

The machine has a licensed SOLIDWORKS 2022 installation. Its `api` directory contains native interop assemblies,
type-library material, CHM/HTMLHelp2x/HelpViewer content and API examples. The sync command copies this directory to
the user-local cache and writes hashes into `manifest.json`.

当前机器有获授权的 SOLIDWORKS 2022 安装。其 `api` 目录包含原生 interop 程序集、类型库资料、CHM/HTMLHelp2x/HelpViewer
内容以及 API 示例。同步命令会将该目录复制到用户本地缓存，并把哈希写入 `manifest.json`。

The machine does not currently provide a verifiable SOLIDWORKS 2026 installation or offline HelpViewer package. The
script therefore caches official 2026 web entry points when reachable and records unavailable resources explicitly.
Obtaining a complete 2026 offline corpus requires a licensed 2026 installation or authorized installation media; the
project must not bypass Customer Portal access or claim that web pages are equivalent to the offline vendor package.

当前机器没有可验证的 SOLIDWORKS 2026 安装或离线 HelpViewer 包。因此脚本只在官方网页可访问时缓存 2026 官方入口，
并明确记录不可用资源。完整的 2026 离线资料需要获授权的 2026 安装或安装介质；项目不得绕过 Customer Portal，
也不得把网页入口伪称为完整离线厂商包。

## Official sources / 官方来源

- [SOLIDWORKS API Help 2022](https://help.solidworks.com/2022/english/api/sldworksapiprogguide/Welcome.htm)
- [SOLIDWORKS API Help 2026](https://help.solidworks.com/2026/english/api/sldworksapiprogguide/Welcome.htm)
- [SOLIDWORKS API Object Model 2022](https://help.solidworks.com/2022/english/api/help_list.htm?id=1.3)
- [SOLIDWORKS API Object Model 2026](https://help.solidworks.com/2026/english/api/help_list.htm?id=1.3)
- [SOLIDWORKS API Object Model Overview 2026](https://help.solidworks.com/2026/English/api/sldworksapiprogguide/GettingStarted/SolidWorks_API_Object_Model_Overview.htm?id=cf0a69cbb1704813a45f3a54103bad30)
- [Context-Sensitive SOLIDWORKS API Help 2026](https://help.solidworks.com/2026/english/api/sldworksapiprogguide/gettingstarted/contextsensitivehelp.htm)
- [SOLIDWORKS API Functional Categories 2022](https://help.solidworks.com/2022/english/api/sldworksapi/FunctionalCategories-sldworksapi.html)
- [SOLIDWORKS API Functional Categories 2026](https://help.solidworks.com/2026/english/api/sldworksapi/FunctionalCategories-sldworksapi.html)
- [SOLIDWORKS API Release Notes 2022](https://help.solidworks.com/2022/english/api/sldworksapi/ReleaseNotes-sldworksapi.html)
- [SOLIDWORKS API Release Notes 2026](https://help.solidworks.com/2026/english/api/sldworksapi/ReleaseNotes-sldworksapi.html)
- [SOLIDWORKS API Object Model PDF 2022](https://help.solidworks.com/2022/english/api/sldworksapi/SWObjectModel.pdf?format=P&value=)
- [SOLIDWORKS API Object Model PDF 2026](https://help.solidworks.com/2026/english/api/sldworksapi/SWObjectModel.pdf?format=P&value=)

The object-model PDF endpoints require the official `format=P&value=` query. The sync script preserves that exact
source URL in the manifest so a future refresh can distinguish a valid PDF retrieval from a bare-path 404.

对象模型 PDF 的官方端点需要 `format=P&value=` 查询参数。同步脚本会在 manifest 中保留完整来源 URL，确保后续刷新能够
区分有效 PDF 抓取与不带查询参数的路径 404。

The official documentation states that local API Help is delivered through `install_dir\api`, including CHM,
HTMLHelp2x and HelpViewer content. It also states that supported interfaces and enumerators are documented, while
undocumented APIs are unsupported. We therefore require an official URL or a locally discovered licensed installation
for every API fact recorded in `docs/research/solidworks-api-knowledge.md`.

官方文档说明本地 API Help 位于 `install_dir\api`，包括 CHM、HTMLHelp2x 和 HelpViewer 内容；同时说明受支持的
接口与枚举会被记录，未公开 API 不受支持。因此 `docs/research/solidworks-api-knowledge.md` 中的每一条 API 事实都必须
具备官方 URL 或本机获授权安装的来源。

## Redistribution boundary / 再分发边界

Do not commit or package the following into this public repository:

```text
SolidWorks.Interop.*.dll
*.tlb / *.olb
*.chm / *.hxs / *.cab / *.msha
SWObjectModel.pdf
SOLIDWORKS API SDK.msi
full official HTML mirrors or vendor examples
```

The repository stores only source links, schema-level provenance, API signatures independently verified against the
installed type library, and generic research notes. The local cache is intentionally ignored by `.gitignore`.

公共仓库不得提交或打包以上厂商资料。仓库只保存来源链接、schema 级 provenance、通过本机类型库独立核对的 API 签名
以及通用研究笔记；本地缓存已由 `.gitignore` 排除。
