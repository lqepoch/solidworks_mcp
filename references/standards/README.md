# Chinese mechanical drawing standards / 中国机械制图标准

This directory contains only locally reproducible catalogue metadata and derived compiler policy. It does not contain
the text, scans or screenshots of GB/T standards. The official SAMR/SAC catalogue distinguishes standards that may be
read/downloaded from adopted standards for which only bibliographic information is exposed; the project therefore
stores provenance and hashes, not a copied standard manual.

本目录只保存可重复获取的标准题录 metadata 和派生 compiler policy，不保存 GB/T 标准正文、扫描页或截图。国家
标准全文公开系统区分可公开下载的标准和采标标准；因此项目保存来源、抓取时间和哈希，不把标准手册复制进仓库。

Refresh the local metadata and user-local official-page cache with:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/sync-chinese-drawing-standards.ps1 -RefreshOfficialPages
```

The current compiler baseline is:

- `GB/T 14689-2008`: A-series sheet size/layout metadata; default `A4 landscape`.
- `GB/T 14690-1993`: scale metadata.
- `GB/T 14692-2008`: projection metadata; baseline `first angle`, subject to enterprise/customer override.
- `GB/T 10609.1-2008`: title-block provenance; the native installed `gb.drwdot` is required for production output.
- `GB/T 4458.1-2002`, `GB/T 4458.3-2013`, `GB/T 4458.4-2003`, `GB/T 4458.5-2003`, `GB/T 4458.6-2002`: view, axonometric, dimensioning, tolerance/fit notation and section-view metadata.
- `GB/T 1800.1-2020`, `GB/T 1804-2000`, `GB/T 1182-2018`: tolerance and GPS provenance already used by the RulePack baseline.

The compiler must never infer a dimension, tolerance, fit, datum or surface-finish requirement from visual similarity.
Native/model-associated annotations or an approved engineering requirement graph are required before release.
