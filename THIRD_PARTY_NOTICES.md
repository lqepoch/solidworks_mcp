# Third-party notices and provenance policy

This file records third-party material used or consulted by the project. The bootstrap commit does not copy source code from any upstream repository into product assemblies.

The product repository itself is distributed under the MIT License in LICENSE. That license does not grant rights to SOLIDWORKS vendor binaries or copyrighted standards/manuals.

## Upstream research repositories

Exact commit pins, URLs, license evidence, reviewed files and adoption status are tracked in references/manifest.json. The source checkouts are local-only and excluded from normal product build.

| Upstream | License | Current use | Direct code adaptation |
| --- | --- | --- | --- |
| jaylamping/solidworks-mcp | MIT | Research reference for STA/session safety and checkpoints | None |
| czuryk/SolidworksMCP | MIT | Research reference for transactions and invariant verification | None |
| Slacker-LLC/solidworks-mcp | Apache-2.0 | Research reference for drawing/selection/unit boundaries | None |
| hjbaard/SolidWorks-MCP | MIT | Research reference for live verification workflows | None |
| modelcontextprotocol/csharp-sdk | Apache-2.0 | Official MCP SDK dependency and API reference | None beyond package use |

When code is adapted, the contributor must add the exact source repository, commit, file/symbol, license and notice text to the manifest and this document before merging.

## Product dependencies

The official ModelContextProtocol NuGet package is Apache-2.0 licensed. The test-only xUnit and Microsoft.NET.Test.Sdk packages are not shipped as runtime product payload; their versions remain centrally pinned for reproducible CI.

## SOLIDWORKS binaries and documentation

SolidWorks.Interop.*, SLDWORKS.exe, type libraries, vendor templates and official help are discovered from the user's licensed installation. They are not committed or redistributed by this public repository. Local paths may be written only to user-local generated configuration/MSBuild properties.

## Standards and enterprise rules

The project stores rule metadata, identifiers, effective dates, URLs and derived legal rule data. It does not redistribute copyrighted standards or official manuals. Enterprise/customer rule packs remain external inputs.
