# Provider rules

SolidWorksMcp.Provider.SolidWorks is the sole production boundary for SolidWorks.Interop.sldworks and SolidWorks.Interop.swconst. Discover local installations instead of hard-coding a developer path. Bind each SOLIDWORKS session to one STA worker and serialize writes. Do not expose COM objects, global selection marks or RCWs across the abstraction boundary.

Before implementing an unfamiliar API call, consult references, the local type library/API corpus and official help, then add a minimal Live test. A COM boolean/null return is not success evidence by itself.
