# Tracked upstream reference snapshots

The repositories listed in `manifest.json` are real upstream source snapshots pinned to exact commits. They are tracked
in this repository for auditable design review, but are never referenced by a product project, included in a package, or
loaded by the MCP runtime. Upstream `.git` metadata lives in the ignored `.checkouts/` directory.

`sync-references.ps1` refreshes the checkouts and replaces only the corresponding tracked snapshot after verifying the
remote URL, clean checkout and exact commit:

    powershell -ExecutionPolicy Bypass -File .\scripts\sync-references.ps1

The sync script refuses dirty upstream working checkouts and refuses to overwrite modified tracked snapshots. Each
upstream license/copyright/notice file is retained in its snapshot; provenance and any direct adaptation must be
recorded in `manifest.json` and `THIRD_PARTY_NOTICES.md` before product code is derived from it.

The confidential local `图纸/` corpus is unrelated to this directory, remains ignored, and must never be copied here.
