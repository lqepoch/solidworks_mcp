# Source module rules

Keep domain types immutable where practical, use explicit dimensional types at architectural boundaries, and keep provider-neutral projects free of SOLIDWORKS vendor references. Business logic belongs in Core/EngineeringModel/RuleEngine/Tolerancing/AutoDrawing/AssemblyDrawing, not in COM adapters.

Any new public contract needs a schema/version story, stable error code or result envelope, cancellation behavior and a test. Any provider mutation needs target identity, expected state hash, rebuild/inspect/verify evidence and audit integration.
