using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace SolidWorksMcp.Server;

/// <summary>
/// Static MCP resources that teach an AI client how to use the high-level SolidWorks surface safely.
/// 通过 MCP resource 向 AI 客户端说明如何安全使用高层 SolidWorks 工具。
/// </summary>
/// <remarks>
/// These resources deliberately describe contracts, sequencing and refusal conditions rather than embedding a private
/// drawing or vendor manual. A client may cache them; the executable compiler remains the authority for validation.
/// 这些 resource 只描述契约、顺序和拒绝条件，不嵌入秘密图纸或厂商手册；客户端可以缓存，但 executable compiler 仍是
/// 最终校验权威。
/// </remarks>
[McpServerResourceType]
public sealed class SolidWorksAiResources
{
    [McpServerResource(
        UriTemplate = "recipe://solidworks/usage/index",
        Name = "SolidWorks MCP usage index",
        MimeType = "text/markdown")]
    [Description("Read this index before calling a mutating SolidWorks MCP tool. It explains intent, compiler and release boundaries.")]
    public static TextResourceContents UsageIndex() => Text(
        "recipe://solidworks/usage/index",
        "# SolidWorks MCP usage index\n\n"
        + "1. Discover with `cad.health` and `cad.capabilities`.\n"
        + "2. Read `schema://solidworks/feature-plan` and `schema://solidworks/part-drawing-intent` before proposing model features.\n"
        + "3. Keep user/AI proposals separate from approved engineering requirements.\n"
        + "4. Prefer `cad.build-part-drawing-intent` with one versioned engineering-intent JSON document; use `cad.build-part-drawing` only for the compatibility/advanced flat contract. Do not loop over raw COM calls.\n"
        + "5. Inspect native geometry and drawing state after every mutation.\n"
        + "6. Use `drawing.validate` and `drawing.release`; unresolved provenance, missing coverage or wrong sheet data must block release.\n\n"
        + "Do not send private PDF text, private drawing paths, screenshots as geometry proof, or arbitrary PowerShell/macros.\n\n"
        + "## 使用索引\n\n"
        + "先调用 `cad.health` / `cad.capabilities`，再读取 feature-plan schema。AI 只提交工程意图和 proposal；模型、图幅、"
        + "尺寸、公差和 BOM 由确定性 compiler 验证。任何缺少 provenance、覆盖不完整或 native read-back 不一致的结果都不能 release。");

    [McpServerResource(
        UriTemplate = "recipe://solidworks/usage/modeling",
        Name = "SolidWorks modeling recipe",
        MimeType = "text/markdown")]
    [Description("Explain the model-first workflow for AI planning without exposing COM method details.")]
    public static TextResourceContents ModelingRecipe() => Text(
        "recipe://solidworks/usage/modeling",
        "# Modeling recipe\n\n"
        + "The AI describes a feature plan in canonical millimetres: closed profile, support plane/face semantic selector,"
        + " feature kind, dimensions, pattern semantics and approval state. The provider lowers it to SOLIDWORKS operations.\n\n"
        + "Required sequence: plan -> preflight -> create sketch/feature -> rebuild -> inspect body/feature/bounding box/mass"
        + " -> verify invariants -> save -> reopen -> inspect again. A COM bool or screenshot is not success evidence.\n\n"
        + "Selections must use persistent references or semantic geometry signatures. Enumeration indexes become stale after"
        + " model changes and must return `SELECTION_STALE`.\n\n"
        + "AI may propose dimensions or datums, but it cannot silently approve tolerance, GD&T, fit or release data.");

    [McpServerResource(
        UriTemplate = "recipe://solidworks/usage/drawing",
        Name = "SolidWorks drawing compiler recipe",
        MimeType = "text/markdown")]
    [Description("Explain how an AI request becomes a standards-driven drawing compiler run.")]
    public static TextResourceContents DrawingRecipe() => Text(
        "recipe://solidworks/usage/drawing",
        "# Drawing compiler recipe\n\n"
        + "The drawing path is: model.analyze -> requirements.analyze -> tolerance.analyze -> drawing.plan -> native"
        + " sheet setup -> native views -> associative Model Items/PMI -> native callouts/center marks -> layout QA ->"
        + " rebuild -> reopen inspection -> export.\n\n"
        + "The RulePack selects projection, A-series sheet, scale, reserved zones, text and spacing. The provider must read"
        + " back effective sheet properties (`ISheet.GetProperties2`), view outlines (`IView.GetOutline`) and annotation"
        + " associations. A planner coordinate is only an initial constraint; it is never a release proof.\n\n"
        + "Use native associative APIs when available. A compiler-owned text note is not a native hole/slot/pattern callout"
        + " and must remain REVIEW_REQUIRED until its feature association is verified. Never use `AutoDimension(all)` as"
        + " a substitute for an explicit manufacturing coverage graph.\n\n"
        + "For GB output, verify A-series sheet dimensions, first/third-angle projection, title-block reserved zone and"
        + " Chinese notation from the resolved RulePack before exporting PDF. The preferred mutation boundary is"
        + " `cad.build-part-drawing-intent`; its feature payloads are lowered into the same compiler and cannot contain"
        + " private source-drawing text or arbitrary COM commands.");

    [McpServerResource(
        UriTemplate = "recipe://solidworks/usage/release",
        Name = "SolidWorks release gate recipe",
        MimeType = "text/markdown")]
    [Description("Explain the evidence required before a drawing can be released.")]
    public static TextResourceContents ReleaseRecipe() => Text(
        "recipe://solidworks/usage/release",
        "# Release gate\n\n"
        + "A drawing is releasable only when: native rebuild has no errors; required dimensions and tolerances have exact"
        + " coverage keys; every critical requirement has approved provenance; no annotation is dangling, duplicated, off-sheet"
        + " or colliding; sheet/template/projection/scale match the RulePack; BOM/Balloon identities are stable; required"
        + " exports succeed; and the persisted artifact passes reopen inspection.\n\n"
        + "Statuses mean PASS, WARNING, REVIEW_REQUIRED or BLOCKING. Skipped is never passed. AI proposals remain"
        + " REVIEW_REQUIRED until a human or trusted native source approves them.\n\n"
        + "The 100-to-98 clearance example must retain the complete derivation chain and must still enforce part <= 98;"
        + " the displayed drawing notation cannot weaken the functional limit.");

    [McpServerResource(
        UriTemplate = "schema://solidworks/feature-plan",
        Name = "SolidWorks feature plan schema",
        MimeType = "application/json")]
    [Description("Machine-readable, provider-neutral feature-plan contract for AI proposals.")]
    public static TextResourceContents FeaturePlanSchema() => Text(
        "schema://solidworks/feature-plan",
        "{\n"
        + "  \"schemaVersion\": \"1.0\",\n"
        + "  \"document\": { \"kind\": \"part|assembly\", \"configuration\": \"Default\" },\n"
        + "  \"features\": [\n"
        + "    {\n"
        + "      \"id\": \"feature:plate\",\n"
        + "      \"kind\": \"profile|extrusion|hole|holePattern|slot|fillet|chamfer|mirror|linearPattern|circularPattern\",\n"
        + "      \"support\": { \"selectorKind\": \"plane|face|feature|persistentReference|geometrySignature\", \"value\": \"...\" },\n"
        + "      \"parameters\": { \"name\": \"semantic name\", \"values\": { \"key\": \"canonical millimetre value\" } },\n"
        + "      \"provenance\": { \"kind\": \"model_native|pmi|rule_pack|geometry_inferred|ai_proposed|human_approved\", \"state\": \"proposal|review_required|approved|released\" },\n"
        + "      \"dependsOn\": [\"feature:plate\"]\n"
        + "    }\n"
        + "  ],\n"
        + "  \"requirements\": [\n"
        + "    { \"id\": \"requirement:...\", \"featureId\": \"feature:...\", \"coverageKeys\": [\"...\"], \"status\": \"proposal|review_required|approved\" }\n"
        + "  ]\n"
        + "}\n\n"
        + "The value is an intent proposal, not raw SOLIDWORKS COM and not permission to release. Keep identities stable"
        + " across regeneration; do not encode source PDF filenames or copied private drawing text.",
        "application/json");

    [McpServerResource(
        UriTemplate = "schema://solidworks/part-drawing-intent",
        Name = "SolidWorks part drawing intent schema",
        MimeType = "application/json")]
    [Description("Machine-readable high-level intent contract for one single-part 3D-to-2D compiler run.")]
    public static TextResourceContents PartDrawingIntentSchema() => Text(
        "schema://solidworks/part-drawing-intent",
        "{\n"
        + "  \"schemaVersion\": \"1.0\",\n"
        + "  \"part\": {\n"
        + "    \"documentId\": \"part:example\",\n"
        + "    \"configuration\": \"Default\",\n"
        + "    \"path\": \"C:/allowed/generated/example.sldprt\",\n"
        + "    \"profile\": { \"segments\": [\n"
        + "      { \"kind\": \"line\", \"startXMillimeters\": 0, \"startYMillimeters\": 0, \"endXMillimeters\": 10, \"endYMillimeters\": 0 },\n"
        + "      { \"kind\": \"line\", \"startXMillimeters\": 10, \"startYMillimeters\": 0, \"endXMillimeters\": 10, \"endYMillimeters\": 5 },\n"
        + "      { \"kind\": \"line\", \"startXMillimeters\": 10, \"startYMillimeters\": 5, \"endXMillimeters\": 0, \"endYMillimeters\": 5 },\n"
        + "      { \"kind\": \"line\", \"startXMillimeters\": 0, \"startYMillimeters\": 5, \"endXMillimeters\": 0, \"endYMillimeters\": 0 }\n"
        + "    ] },\n"
        + "    \"extrusionDepthMillimeters\": 5,\n"
        + "    \"features\": [\n"
        + "      { \"kind\": \"throughHolePattern\", \"payload\": { \"name\": \"mounting-holes\", \"diameterMillimeters\": 6, \"centers\": [ { \"xMillimeters\": 0, \"yMillimeters\": 0 } ] } },\n"
        + "      { \"kind\": \"slot\", \"payload\": { \"name\": \"access-slot\", \"widthMillimeters\": 4, \"start\": { \"xMillimeters\": 0, \"yMillimeters\": 0 }, \"end\": { \"xMillimeters\": 10, \"yMillimeters\": 0 }, \"supportFaceProbe\": { \"xMillimeters\": 0, \"yMillimeters\": 0 } } }\n"
        + "    ]\n"
        + "  },\n"
        + "  \"drawing\": {\n"
        + "    \"documentId\": \"drawing:example\",\n"
        + "    \"path\": \"C:/allowed/generated/example.slddrw\",\n"
        + "    \"pdfPath\": \"C:/allowed/generated/example.pdf\",\n"
        + "    \"scaleDenominator\": 1\n"
        + "  }\n"
        + "}\n\n"
        + "The profile shown is illustrative only and must be a connected closed line/arc loop in a real request."
        + " Feature payloads are lowered only when their kind is supported; unsupported kinds fail before CAD startup."
        + " All dimensions are canonical millimetres. Paths must pass the user-local allowlist. Never include private"
        + " source drawing text, screenshots, arbitrary COM, PowerShell or macro commands.",
        "application/json");

    private static TextResourceContents Text(string uri, string text, string mimeType = "text/markdown") => new()
    {
        Uri = uri,
        MimeType = mimeType,
        Text = text,
    };
}
