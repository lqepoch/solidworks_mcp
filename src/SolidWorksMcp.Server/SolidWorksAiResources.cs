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
        + "2. Read `schema://solidworks/feature-plan` before proposing model features.\n"
        + "3. Keep user/AI proposals separate from approved engineering requirements.\n"
        + "4. Prefer `cad.build-part-drawing` for a bounded part-to-drawing compiler run; do not loop over raw COM calls.\n"
        + "5. Inspect native geometry and drawing state after every mutation.\n"
        + "6. Use `drawing.validate` and `drawing.release`; unresolved provenance, missing coverage or wrong sheet data must block release.\n\n"
        + "Do not send private PDF text, private drawing paths, screenshots as geometry proof, or arbitrary PowerShell/macros.\n\n"
        + "## 使用索引\n\n"
        + "先调用 `cad.health` / `cad.capabilities`，再读取 feature-plan schema。AI 只提交工程意图和 proposal；模型、图幅、"
        + "尺寸、公差和 BOM 由确定性 compiler 验证。任何缺少 provenance、覆盖不完整或 native read-back 不一致的结果都不能 release。",
        "AI usage guidance / AI 使用边界");

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
        + "AI may propose dimensions or datums, but it cannot silently approve tolerance, GD&T, fit or release data.",
        "Modeling recipe / 建模 recipe");

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
        + " Chinese notation from the resolved RulePack before exporting PDF.",
        "Drawing compiler recipe / 工程图编译 recipe");

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
        + " the displayed drawing notation cannot weaken the functional limit.",
        "Release recipe / 发布门禁 recipe");

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
        "Feature plan schema / Feature plan schema",
        "application/json");

    private static TextResourceContents Text(string uri, string text, string name, string mimeType = "text/markdown") => new()
    {
        Uri = uri,
        MimeType = mimeType,
        Text = text,
    };
}
