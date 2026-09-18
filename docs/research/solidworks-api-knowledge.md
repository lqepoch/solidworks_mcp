# SOLIDWORKS API knowledge corpus / SOLIDWORKS API 知识库

This file records verified API facts used by the native provider. It stores signatures, metadata and links, not a
copy of the vendor help manual. The corpus is intentionally append-only by API fact so future version changes can be
reviewed instead of silently replacing an assumption.

本文件记录原生 Provider 使用过且已核对的 API 事实，只保存签名、元数据和链接，不复制厂商帮助全文。知识库按
API fact 追加，未来版本变化必须经过审查，不能静默覆盖旧假设。

## B02 session foundation / B02 Session 基础

| Interface / method | Verified signature | Version evidence | Official source | Runtime note |
| --- | --- | --- | --- | --- |
| `ISldWorks.GetProcessID` | `System.Int32 GetProcessID()` | SOLIDWORKS 2022 Interop assembly `30.0.0.5041`; local Type Library `D:\Solidworks2022\SOLIDWORKS\sldworks.tlb` | [GetProcessID Method](https://help.solidworks.com/2025/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ISldWorks~GetProcessID.html) | Binds a COM object to its real OS process; the provider rejects a PID mismatch. |
| `ISldWorks.RevisionNumber` | `System.String RevisionNumber()` | SOLIDWORKS 2022 Interop assembly `30.0.0.5041` | [RevisionNumber Method](https://help.solidworks.com/2019/english/api/sldworksapi/solidworks.interop.sldworks~solidworks.interop.sldworks.isldworks~revisionnumber.html) | Sampled at attach and included in the session identity; a changed revision is a state conflict. |
| `ISldWorks.UserControl` | `System.Boolean UserControl { get; set; }` | SOLIDWORKS 2022 Interop reflection | [UserControl Property](https://help.solidworks.com/2019/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ISldWorks~UserControl.html) | Read-only sampling in B02; provider never changes ownership of an interactive process. |
| `ISldWorks.Visible` | `System.Boolean Visible { get; set; }` | SOLIDWORKS 2022 Interop reflection | [Visible Property](https://help.solidworks.com/2024/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.ISldWorks~Visible.html) | Read-only sampling in B02; no UI hiding/showing is performed during attach. |
| `ISldWorks.IFrameObject` | `SolidWorks.Interop.sldworks.Frame IFrameObject()` | SOLIDWORKS 2022 Interop reflection | [ISldWorks Interface](https://help.solidworks.com/2025/english/api/sldworksapi/solidworks.interop.sldworks~solidworks.interop.sldworks.isldworks.html) | Verified for future window/session diagnostics; B02 uses `GetProcessID()` directly, so no extra frame RCW is retained. |
| `ISldWorks.GetDocumentCount` | `System.Int32 GetDocumentCount()` | SOLIDWORKS 2022 Interop reflection | [GetDocumentCount Method](https://help.solidworks.com/2022/english/api/sldworksapi/solidworks.interop.sldworks~solidworks.interop.sldworks.isldworks~getdocumentcount.html) | Live cleanup uses it as bounded close visibility evidence because `GetOpenDocument` can briefly expose a stale RCW after `CloseDoc`. |
| `ISldWorks.GetDocuments` | `System.Object GetDocuments()` | SOLIDWORKS 2022 Interop reflection | [GetDocuments Method](https://help.solidworks.com/2022/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ISldWorks~GetDocuments.html) | The Live-only cleanup adapter enumerates documents, canonicalizes paths and scopes all cleanup to the exact test workspace. |
| `ISldWorks.ExitApp` | `System.Void ExitApp()` | SOLIDWORKS 2022 Interop reflection; official 2026 API page checked | [ExitApp Method](https://help.solidworks.com/2026/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ISldWorks~ExitApp.html) | Never called by production detach. The Live harness calls it only after exact PID ownership, workspace scope and generated-artifact save checks succeed. |

## Discovery decision / 发现策略

- `RotSolidWorksConnector` enumerates COM's Running Object Table on the provider STA and accepts only objects that cast
  to `ISldWorks`, report a live `SLDWORKS` process through `GetProcessID()`, and satisfy the requested PID.
- An unqualified attach is allowed only when exactly one eligible process remains. Multiple eligible processes return
  `STATE_CONFLICT`; the provider never guesses from “active document” or filename.
- The production attach/detach path does not call `new SldWorks()`, `Activator.CreateInstance`, `Marshal.GetActiveObject` or `ExitApp`; the separate Live-only owned-process harness is the sole explicit `ExitApp` caller after its workspace checks.
- All COM RCW release occurs on the owning STA. No COM interface appears in `SolidWorksMcp.CadAbstractions` or MCP
  payloads.

## B03 native part loop / B03 原生零件闭环

| Interface / method | Verified signature | Version evidence | Official source | Runtime note |
| --- | --- | --- | --- | --- |
| `ISldWorks.GetDocumentTemplate` | `System.String GetDocumentTemplate(System.Int32 DocumentType, System.String TemplateName, System.Int32 PaperSize, System.Double Width, System.Double Height)` | SOLIDWORKS 2022 Interop reflection; `swDocPART=1` | [GetDocumentTemplate Method](https://help.solidworks.com/2017/english/api/sldworksapi/solidworks.interop.sldworks~solidworks.interop.sldworks.isldworks~getdocumenttemplate.html) | The provider validates the returned template path before creating a document. |
| `ISldWorks.INewDocument2` | `ModelDoc2 INewDocument2(System.String TemplateName, System.Int32 Options, System.Double Width, System.Double Height)` | SOLIDWORKS 2022 Interop reflection | [INewDocument2 Method](https://help.solidworks.com/2020/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SolidWorks.Interop.sldworks.ISldWorks~INewDocument2.html) | The provider uses the discovered part template and does not depend on ActiveDoc. |
| `ISketchManager.CreateCircleByRadius` | `SketchSegment CreateCircleByRadius(System.Double X, System.Double Y, System.Double Z, System.Double Radius)` | SOLIDWORKS 2022 Interop reflection; coordinates/radius converted from mm to meters | [ISketchManager Interface](https://help.solidworks.com/2022/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SolidWorks.Interop.sldworks.ISketchManager.html) | The first B03 fixture uses a circle on the verified Front Plane. |
| `IFeatureManager.FeatureExtrusion3` | `Feature FeatureExtrusion3(Boolean Sd, Boolean Flip, Boolean Dir, Int32 T1, Int32 T2, Double D1, Double D2, Boolean Dchk1, Boolean Dchk2, Boolean Ddir1, Boolean Ddir2, Double Dang1, Double Dang2, Boolean OffsetReverse1, Boolean OffsetReverse2, Boolean TranslateSurface1, Boolean TranslateSurface2, Boolean Merge, Boolean UseFeatScope, Boolean UseAutoSelect, Int32 T0, Double StartOffset, Boolean FlipStartOffset)` | SOLIDWORKS 2022 Interop reflection; `swEndCondBlind=0` | [FeatureExtrusion3 Method](https://help.solidworks.com/2022/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFeatureManager~FeatureExtrusion3.html) | The adapter checks the returned feature, rebuild result and positive measured volume. |
| `IModelDoc2.SaveAs3` | `System.Int32 SaveAs3(System.String NewName, System.Int32 SaveAsVersion, System.Int32 Options)` | SOLIDWORKS 2022 Interop reflection; `swSaveAsCurrentVersion=0`, `swSaveAsOptions_Silent=1` | [SaveAs3 Method](https://help.solidworks.com/2024/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IModelDoc2~SaveAs3.html) | The second argument is the save version and the third is the options bitmask; swapping them produced verified error 32 (`swFileSaveFormatNotAvailable`) during the first Live attempt. |
| `IModelDoc2.ForceRebuild3` | `System.Boolean ForceRebuild3(System.Boolean TopOnly)` | SOLIDWORKS 2022 Interop reflection | [ForceRebuild3 Method](https://help.solidworks.com/2022/english/api/sldworksapi/solidworks.interop.sldworks~solidworks.interop.sldworks.imodeldoc2~forcerebuild3.html) | A `true` return is necessary but not sufficient; B03 follows it with native inspection and invariant checks. |
| `ISldWorks.IActivateDoc3` | `ModelDoc2 IActivateDoc3(System.String Name, System.Boolean Silent, out System.Int32 Errors)` | SOLIDWORKS 2022 Interop reflection | [IActivateDoc3 Method](https://help.solidworks.com/2022/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ISldWorks~IActivateDoc3.html) | Mutation paths activate the exact extension-qualified registered path, reject activation errors, and then re-check path/type/configuration/state hash. |
| `ISldWorks.CloseDoc` | `System.Void CloseDoc(System.String FileName)` | SOLIDWORKS 2022 Interop reflection | [ISldWorks Interface](https://help.solidworks.com/2022/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.ISldWorks.html) | B03 passes the registered canonical path, preflights the document as clean, and uses bounded `GetOpenDocument` plus `GetDocumentCount` verification after close. |
| `ISldWorks.OpenDoc6` | `ModelDoc2 OpenDoc6(System.String FileName, System.Int32 Type, System.Int32 Options, System.String Configuration, ref System.Int32 Errors, ref System.Int32 Warnings)` | SOLIDWORKS 2022 Interop reflection; `swDocPART=1`, `swOpenDocOptions_Silent=1` | [Open Document Example (C#)](https://help.solidworks.com/2022/english/api/sldworksapi/Open_Document_Example_CSharp.htm) | B03 uses the documented replacement for obsolete silent-open calls, passes the registered part configuration, rejects a null model or non-zero load error, then revalidates identity and inspection invariants. |
| `IModelDoc2.Parameter` | `System.Object Parameter(System.String Name)` | SOLIDWORKS 2022 Interop reflection | [IParameter Method (IFeature)](https://help.solidworks.com/2022/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFeature~IParameter.html) | The provider accepts only an explicit full parameter identity such as `D1@FeatureName`; a missing object is reported as `SELECTION_STALE`, never guessed from an enumeration index. |
| `IDimension.SetSystemValue3` | `System.Int32 SetSystemValue3(System.Double NewValue, System.Int32 WhichConfigurations, System.Object Config_names)` | SOLIDWORKS 2022 Interop reflection; `swSetValue_InThisConfiguration=1`, successful return `swSetValue_Successful=0` | [SetSystemValue3 Method](https://help.solidworks.com/2022/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IDimension~SetSystemValue3.html) | The thin adapter converts canonical millimetres to SOLIDWORKS metres, writes only the registered configuration, checks the status code, then reads back and rebuilds before reporting success. |
| `IModelDoc2.IAddDimension2` | `DisplayDimension IAddDimension2(Double X, Double Y, Double Z)` | SOLIDWORKS 2022 Interop reflection; selection-dependent sketch dimension path | [AddDimension2 Method](https://help.solidworks.com/2022/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IModelDoc2~AddDimension2.html) | The provider selects one explicit native sketch segment, calls the strongly typed overload on the owning STA, requires a non-null `DisplayDimension`, and releases only the temporary RCW. It does not synthesize a drawing dimension from request text. |
| `ISldWorks.GetUserPreferenceToggle` / `SetUserPreferenceToggle` | `Boolean GetUserPreferenceToggle(Int32 UserPreference)` / `Void SetUserPreferenceToggle(Int32 UserPreference, Boolean Value)` | SOLIDWORKS 2022 Interop reflection | [ISldWorks Interface](https://help.solidworks.com/2022/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SolidWorks.Interop.sldworks.ISldWorks.html) | A bounded preflight temporarily disables the documented input-dimension-value-on-create preference and restores the exact prior value in `finally`; unknown dialogs are never dismissed blindly. |
| `swUserPreferenceToggle_e.swInputDimValOnCreate` | `10` | SOLIDWORKS 2022 `SolidWorks.Interop.swconst` reflection | [AddDimension2 Method](https://help.solidworks.com/2022/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IModelDoc2~AddDimension2.html) | The preference is treated as a modal-risk control around native dimension creation. The restoration failure is surfaced rather than silently changing the user's interactive SOLIDWORKS state. |
| `IDimension.GetSystemValue3` | `System.Object GetSystemValue3(System.Int32 WhichConfigurations, System.Object Config_names)` | SOLIDWORKS 2022 Interop reflection; read option `swThisConfiguration=1` | [GetSystemValue3 Method](https://help.solidworks.com/2022/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IDimension~GetSystemValue3.html) | Read-back is mandatory evidence: the setter's return code alone is not proof. The returned system value is normalized from metres to the vendor-neutral `Length` contract. |
| `IFeature.GetErrorCode2` | `System.Int32 GetErrorCode2(out System.Boolean IsWarning)` | SOLIDWORKS 2022 Interop reflection; non-zero feature code indicates a What's Wrong diagnostic | [GetErrorCode2 Method](https://help.solidworks.com/2025/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IFeature~GetErrorCode2.html) | The official signature is unchanged across the locally installed 2022 interop and the linked API Help revision. Native inspection traverses the feature tree on the STA, records non-zero warning/error codes with stable feature identity, and never copies arbitrary UI/modal text. |
| `IBody2.GetBodyBox` | `System.Object GetBodyBox()` | SOLIDWORKS 2022 Interop assembly `30.0.0.5041`; official 2022/2026 API Help cross-check | [GetBodyBox Method](https://help.solidworks.com/2023/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SolidWorks.Interop.sldworks.IBody2~GetBodyBox.html) / [2026 IGetBodyBox Method](https://help.solidworks.com/2026/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IBody~IGetBodyBox.html) | Returns two XYZ diagonal corners `[X1,Y1,Z1,X2,Y2,Z2]` in system units (metres), not interleaved axis min/max values. The provider normalizes each coordinate, then Live evidence requires a non-degenerate three-axis box after rebuild/reopen. |

- B03 initially passed `(SaveAsOptions=Silent, Options=0)` to `SaveAs3`, which returned `swFileSaveFormatNotAvailable=32`. The
  correction is now `(SaveAsVersion=swSaveAsCurrentVersion, Options=swSaveAsOptions_Silent)` and is protected by the
  real Live test.
- B03 首次把 `(SaveAsOptions=Silent, Options=0)` 传给 `SaveAs3`，真实返回 `swFileSaveFormatNotAvailable=32`；现已修正为
  `(SaveAsVersion=swSaveAsCurrentVersion, Options=swSaveAsOptions_Silent)`，并由真实 Live 测试保护。
- The first mutation retry showed that a document obtained with `GetOpenDocument` was not necessarily the foreground
  document required by feature creation.  B03 now uses `IActivateDoc3` for mutation, while inspection remains able to
  read by path without changing the user's active document.
- 第二次 mutation retry 证明 `GetOpenDocument` 得到的文档不一定是 feature creation 所需的前台文档；B03 现对 mutation
  使用 `IActivateDoc3`，而 inspection 仍可按 path 读取，不改变用户的 active document。

- `RotSolidWorksConnector` 在 Provider STA 上枚举 COM ROT，只接受能转换为 `ISldWorks`、通过 `GetProcessID()` 报告
  存活的 `SLDWORKS` 进程、且符合请求 PID 的对象。
- 未限定 PID 时只有恰好一个候选才附着；多个候选返回 `STATE_CONFLICT`，绝不根据 ActiveDoc 或文件名猜测。
- 生产 attach/detach path 不调用 `new SldWorks()`、`Activator.CreateInstance`、`Marshal.GetActiveObject` 或 `ExitApp`；只有独立的 Live owned-process harness 在完成 workspace 检查后才显式调用 `ExitApp`。
- 所有 COM RCW 只在其所属 STA 释放；`SolidWorksMcp.CadAbstractions` 和 MCP payload 不出现 COM 接口。

## B04 non-cylindrical part and native drawing proof / B04 非圆柱零件与原生工程图证据

| Interface / method | Verified signature | Version evidence | Official source | Runtime note |
| --- | --- | --- | --- | --- |
| `ISketchManager.CreateCornerRectangle` | `System.Object CreateCornerRectangle(Double X1, Double Y1, Double Z1, Double X2, Double Y2, Double Z2)` | SOLIDWORKS 2022 Interop assembly `30.0.0.5041` | [CreateCornerRectangle Method](https://help.solidworks.com/2022/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.ISketchManager~CreateCornerRectangle.html) | The provider creates a centered closed profile in metres, then checks the resulting `ProfileFeature` identity before extrusion. The returned COM collection is released on the provider STA. |
| `ISketchManager.CreateLine` | `SketchSegment CreateLine(Double X1, Double Y1, Double Z1, Double X2, Double Y2, Double Z2)` | SOLIDWORKS 2022 Interop assembly `30.0.0.5041` | [CreateLine Method](https://help.solidworks.com/2022/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.ISketchManager~CreateLine.html) | The provider closes the polygon explicitly by connecting the last vertex to the first; it does not assume SOLIDWORKS will infer missing topology. |
| `ISketchManager.Create3PointArc` | `SketchSegment Create3PointArc(Double X1, Double Y1, Double Z1, Double X2, Double Y2, Double Z2, Double X3, Double Y3, Double Z3)` | SOLIDWORKS 2022 Interop assembly `30.0.0.5041`; official 2026 API page cross-checked | [Create3PointArc Method](https://help.solidworks.com/2026/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.ISketchManager~Create3PointArc.html) | The provider passes start, end and through-arc points in metres after vendor-neutral profile validation. The Live fixtures use arcs as real sketch boundaries; they are not approximated by circles or tessellated polygons. |
| `IModelDocExtension.SelectByRay` | `System.Boolean SelectByRay(Double X, Double Y, Double Z, Double DirX, Double DirY, Double DirZ, Double Radius, Int32 Type, Boolean Append, Int32 Mark, Int32 Action)` | SOLIDWORKS 2022 Interop assembly `30.0.0.5041` | [SelectByRay Method](https://help.solidworks.com/2022/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IModelDocExtension~SelectByRay.html) | B04 uses a bounded ray to select the verified top planar face of the centered L-profile fixture. This is a proof-of-API path, not yet the general geometry selector; arbitrary orientations must move to persistent/reference-backed face selection before release. |
| `IFeatureManager.FeatureCut4` | `Feature FeatureCut4(Boolean Sd, Boolean Flip, Boolean Dir, Int32 T1, Int32 T2, Double D1, Double D2, Boolean Dchk1, Boolean Dchk2, Boolean Ddir1, Boolean Ddir2, Double Dang1, Double Dang2, Boolean OffsetReverse1, Boolean OffsetReverse2, Boolean TranslateSurface1, Boolean TranslateSurface2, Boolean NormalCut, Boolean UseFeatScope, Boolean UseAutoSelect, Boolean AssemblyFeatureScope, Boolean AutoSelectComponents, Boolean PropagateFeatureToParts, Int32 T0, Double StartOffset, Boolean FlipStartOffset, Boolean OptimizeGeometry)` | SOLIDWORKS 2022 Interop assembly `30.0.0.5041`; `swEndCondThroughAll` verified from `swconst` | [FeatureCut4 Method](https://help.solidworks.com/2022/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IFeatureManager~FeatureCut4.html) | A non-null return is not accepted as proof alone. B04 also forces rebuild, inspects body count/positive volume/feature identity, and retains semantic hole-count and diameter evidence. |
| `IDrawingDoc.CreateDrawViewFromModelView3` | `View CreateDrawViewFromModelView3(String ModelName, String ModelViewName, Double X, Double Y, Double Scale)` | SOLIDWORKS 2022 Interop assembly `30.0.0.5041` | [CreateDrawViewFromModelView3 Method](https://help.solidworks.com/2011/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IDrawingDoc~CreateDrawViewFromModelView3.html) | The provider creates native Front/Top/Isometric views from the saved part and validates a positive `IView.GetOutline()` before persisting the drawing. |
| `IDrawingDoc.CreateText2` | `System.Object CreateText2(String TextString, Double TextX, Double TextY, Double TextZ, Double TextHeight, Double TextAngle)` | SOLIDWORKS 2022 Interop assembly `30.0.0.5041` | [CreateText2 Method](https://help.solidworks.com/2017/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IDrawingDoc~CreateText2.html) | The provider uses this only for an explicit `Kind=note` request, reads back `INote.GetText()` and `IAnnotation.GetPosition()`, and does not label the note as a model dimension. |
| `IDrawingDoc.InsertModelAnnotations3` | `System.Object InsertModelAnnotations3(Int32 Option, Int32 Types, Boolean AllViews, Boolean DuplicateDims, Boolean HiddenFeatureDims, Boolean UsePlacementInSketch)` | SOLIDWORKS 2022 Interop assembly `30.0.0.5041`; `swImportModelItemsFromEntireModel`, `swInsertDimensionsMarkedForDrawing` and `swInsertDimensionsNotMarkedForDrawing` verified from `swconst` | [InsertModelAnnotations3 Method](https://help.solidworks.com/2026/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SolidWorks.Interop.sldworks.IDrawingDoc~InsertModelAnnotations3.html) | The provider selects and activates one exact drawing view, imports only native returned annotations, and fails closed unless at least one returned object is a verified `swDisplayDimension`. |
| `IDrawingDoc.GetViews` | `System.Object GetViews()` | SOLIDWORKS 2022 Interop assembly `30.0.0.5041` | [GetViews Method](https://help.solidworks.com/2023/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SolidWorks.Interop.sldworks.IDrawingDoc~GetViews.html) | The COM result is a jagged array whose first element per sheet is the sheet sentinel. The adapter excludes that sentinel and records stable view evidence without exposing COM objects to domain code. |
| `IView.GetOrientationName` | `System.String GetOrientationName()` | SOLIDWORKS 2022 Interop assembly `30.0.0.5041` | [IView Interface](https://help.solidworks.com/2022/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SolidWorks.Interop.sldworks.IView.html) | `GetName2()` is a generated drawing label such as `Drawing View1`, not the model orientation. Reopen inspection therefore uses `GetOrientationName()` for semantic Front/Top/Isometric verification. |
| `IView.GetOutline` | `System.Object GetOutline()` | SOLIDWORKS 2022 Interop assembly `30.0.0.5041` | [GetOutline Method](https://help.solidworks.com/2016/english/api/sldworksapi/solidworks.interop.sldworks~solidworks.interop.sldworks.iview~getoutline.html) | The returned four-value paper-space bounding box is an invariant used to reject empty or invalid native views; it is not used to guess-move source geometry. |
| `IView.GetAnnotations` | `System.Object GetAnnotations()` | SOLIDWORKS 2022 Interop assembly `30.0.0.5041` | [GetAnnotations Method](https://help.solidworks.com/2024/english/api/sldworksapi/solidworks.interop.sldworks~solidworks.interop.sldworks.iview~getannotations.html) | Reopen inspection reads the view-owned annotation array, classifies each `IAnnotation.GetType()`, and extracts only the type-specific Note/DisplayDimension text. |
| `IAnnotation.GetSpecificAnnotation` | `System.Object GetSpecificAnnotation()` | SOLIDWORKS 2022 Interop assembly `30.0.0.5041` | [GetSpecificAnnotation Method](https://help.solidworks.com/2024/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IAnnotation~GetSpecificAnnotation.html) | Native note verification obtains `INote` through this accessor, then reads text after creation; no annotation is accepted from a request string alone. |
| `IAnnotation.SetPosition2` / `IAnnotation.GetPosition` | `System.Boolean SetPosition2(System.Double X, System.Double Y, System.Double Z)` / `System.Object GetPosition()` | SOLIDWORKS 2022 Interop reflection; official 2026 example cross-checked; `SetPosition2` is available from SOLIDWORKS 2014 SP3 | [SetPosition2 Method](https://help.solidworks.com/2017/english/api/sldworksapi/solidworks.interop.sldworks~solidworks.interop.sldworks.iannotation~setposition2.html) / [2026 Insert Note Example](https://help.solidworks.com/2026/english/api/sldworksapi/insert_a_note_example_vb.htm) | D08 uses this only for an exact stable annotation name after expected-state/current-position preflight; the provider rebuilds and reads the position back. Coordinates are converted from canonical millimetres to SOLIDWORKS metres exactly once. |

| `IDisplayDimension.GetText` | `System.String GetText(Int32 WhichText)` | SOLIDWORKS 2022 Interop assembly `30.0.0.5041`; `swDimensionTextAll=0` is invalid for this getter | [GetText Method](https://help.solidworks.com/2022/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IDisplayDimension~GetText.html) | The provider reads only the legal prefix/suffix/callout parts. It never calls `GetText(0)`; when those parts are empty it reads the associated native `IDimension.SystemValue` as evidence. |
| `IDisplayDimension.GetDimension` / `IDimension.SystemValue` | `System.Object GetDimension()` / `System.Double SystemValue { get; set; }` | SOLIDWORKS 2022 Interop reflection; `SystemValue` is the stable installed-interop read-back path | [SystemValue Property](https://help.solidworks.com/2022/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IDimension~SystemValue.html) | Drawing inspection releases the temporary associated Dimension RCW on the Provider STA and converts native metres to the vendor-neutral millimetre contract. This is read-only evidence, not a request-derived dimension. |

| `IDisplayDimension.MarkedForDrawing` | `System.Boolean MarkedForDrawing { get; set; }` | SOLIDWORKS 2022 Interop assembly `30.0.0.5041`; official 2019 API page cross-checked | [MarkedForDrawing Property](https://help.solidworks.com/2019/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IDisplayDimension~MarkedForDrawing.html) | Model Items eligibility is a native DisplayDimension state, not merely a value set through `IDimension.SetSystemValue3`. The structured-profile path creates an associative sketch dimension before extrusion; drawing insertion remains fail-closed on the returned native annotation. |

### B04 evidence boundary / B04 证据边界

The B03/B04 Live fixtures are deliberately generic and redacted: they prove a curved non-cylindrical plate family, a
three-point-arc boundary, a semantic repeated through-hole group, native associative model dimensions and native
drawing views can be created, rebuilt, saved, reopened and exported without copying confidential drawing content. They
do not copy dimensions, text, title blocks or filenames from the confidential local drawing corpus. The private corpus
is sampled by its local skill and remains outside Git, build inputs, logs, screenshots and artifacts.

B03/B04 Live fixture 是刻意脱敏的通用案例：它证明曲线非圆柱板件、三点圆弧边界、工程语义重复通孔组、原生
关联模型尺寸和原生工程图视图可以真实创建、重建、保存、重新打开并导出，不复制机密图纸内容。它不复制
机密本地图纸的尺寸、文字、标题栏、文件名；机密 corpus 由本地 skill 抽样，始终不进入 Git、构建输入、日志、
截图或 artifact。

## B05 declarative selection slice / B05 声明式选择切片

| Interface / method | Verified signature | Version evidence | Official source | Runtime note |
| --- | --- | --- | --- | --- |
| `IModelDocExtension.GetPersistReference3` | `System.Object GetPersistReference3(System.Object DispObj)` | SOLIDWORKS 2022 Interop assembly `30.0.0.5041` | [GetPersistReference3 Method](https://help.solidworks.com/2021/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IModelDocExtension~GetPersistReference3.html) | The provider accepts only the `solidworks.persist3` token format and keeps the SAFEARRAY/RCW on the Provider STA. The opaque token is never parsed by the vendor-neutral boundary. |
| `IModelDocExtension.GetObjectByPersistReference3` | `System.Object GetObjectByPersistReference3(System.Object PersistId, out System.Int32 ErrorCode)` | SOLIDWORKS 2022 Interop assembly `30.0.0.5041` | [GetObjectByPersistReference3 Method](https://help.solidworks.com/2023/english/api/sldworksapi/solidworks.interop.sldworks~solidworks.interop.sldworks.imodeldocextension~getobjectbypersistreference3.html) | A non-zero native error or wrong entity kind returns `SELECTION_STALE`; the provider never falls back to an unrelated active selection. The returned COM object is released before the vendor-neutral result leaves the STA. |
| `IFeature.Name` / `IFeature.GetNextFeature` | `System.String Name { get; }` / `System.Object GetNextFeature()` | SOLIDWORKS 2022 Interop reflection; used only for exact semantic-name resolution | [IFeature Interface](https://help.solidworks.com/2022/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFeature.html) | The semantic fallback traverses the native feature tree and requires exactly one matching name. It does not expose the traversal ordinal as identity. |
| `IBody2.GetFaces` / `IBody2.GetEdges` / `IBody2.GetVertices` | `System.Object GetFaces()` / `System.Object GetEdges()` / `System.Object GetVertices()` | SOLIDWORKS 2022 Interop reflection; topology fallback remains Provider STA-only | [GetFaces Method](https://help.solidworks.com/2020/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IBody2~GetFaces.html) / [GetFaceCount Method](https://help.solidworks.com/2022/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IBody2~GetFaceCount.html) | The provider scans all current solid bodies and rejects zero/multiple matches. The traversal ordinal is never serialized as selector identity. |
| `IFace2.GetFaceId` | `System.Int32 GetFaceId()` | SOLIDWORKS 2022 Interop reflection | [GetFaceId Method](https://help.solidworks.com/2023/english/api/sldworksapi/solidworks.interop.sldworks~solidworks.interop.sldworks.iface2~getfaceid.html) | Officially intended for imported bodies; IDs are removed on rebuild. Therefore `face-id` is only a geometry fallback and is never presented as a permanent reference. |
| `IEdge.GetID` / `IVertex.GetPoint` | `System.Int32 GetID()` / `System.Object GetPoint()` | SOLIDWORKS 2022 Interop reflection | [IVertex Interface](https://help.solidworks.com/2022/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SolidWorks.Interop.sldworks.IVertex.html) | Edge IDs and quantized canonical millimetre vertex points are search evidence only; persistent references remain the primary cross-save mechanism. |
| `ISketch.GetSketchSegments` / `ISketchSegment.GetID` | `System.Object GetSketchSegments()` / `System.Object GetID()` | SOLIDWORKS 2022 Interop reflection | [GetSketchSegments Method](https://help.solidworks.com/2020/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ISketch~GetSketchSegments.html) | A sketch entity fallback is scoped by its owning sketch feature name and segment ID; sketch errors may omit segments per official remarks, so unresolved/ambiguous results stay stale/review-required. |

The current native B05 implementation supports Feature/Body semantic resolution, the provider geometry-signature format
`solidworks.geometry.v1|kind=Feature|name=...`, topology signatures for Face/Edge/Vertex/SketchEntity, and
persistent-reference validation for all currently recognized native entity kinds. Topology signatures are deliberately
search evidence: face IDs are only valid for the imported-body use case documented by SOLIDWORKS, edge IDs can change
with topology edits, and vertex points can be coincident. The resolver scans and rejects ambiguity instead of choosing
an enumeration index. Drawing annotation capture remains an explicit follow-up capability and is not reported as complete.

当前 native B05 实现支持 Feature/Body semantic resolution、Provider geometry-signature 格式
`solidworks.geometry.v1|kind=Feature|name=...`、Face/Edge/Vertex/SketchEntity 拓扑签名，以及当前识别实体类型的
persistent-reference 校验。拓扑签名只是搜索证据：官方说明 face-id 主要用于 imported body，edge id 可能随拓扑
修改变化，vertex 点可能重合；解析器会扫描并拒绝歧义，不会选择 enumeration index。Drawing annotation capture
仍是后续能力，不会被误报为完成。

## B06 verified native export slice / B06 已验证原生导出切片

| Interface / method | Verified signature | Version evidence | Official source | Runtime note |
| --- | --- | --- | --- | --- |
| `IModelDocExtension.SaveAs` | `Boolean SaveAs(String Name, Int32 Version, Int32 Options, Object ExportData, ref Int32 Errors, ref Int32 Warnings)` | SOLIDWORKS 2022 Interop assembly `30.0.0.5041`; `swSaveAsCurrentVersion=0`, `swSaveAsOptions_Silent=1` | [IModelDocExtension.SaveAs Method](https://help.solidworks.com/2023/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IModelDocExtension~SaveAs.html) | The native provider clears selection, activates and state-checks the exact registered document, calls this method on the owning STA, checks the Boolean plus native error code, verifies a non-empty file and re-hashes the source document. The verified Live slice exports a part to STEP and a drawing to PDF. |
| `IModelDoc2.ClearSelection2` | `Boolean ClearSelection2(Boolean All)` | SOLIDWORKS 2022 Interop reflection | [IModelDoc2 Interface](https://help.solidworks.com/2022/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~Solidworks.Interop.sldworks.IModelDoc2.html) | Export clears all selections before `SaveAs`; the provider does not export an incidental global selection. |

The provider allowlist currently supports `PDF` for drawings and `STEP`/`IGES`/`STL` for parts or assemblies. It does not
claim native BOM, release export, sheet selection, or arbitrary extension/macro execution. A real SOLIDWORKS 2022
fresh-process Live run passed the B03 part-to-STEP and drawing-to-PDF file evidence checks; the generated files were
deleted by the isolated successful-test cleanup policy after verification. The current machine has no verified 2026
installation, so this fact is not a 2026 runtime claim.

Provider 当前 allowlist 仅支持 drawing 的 `PDF` 和 part/assembly 的 `STEP`/`IGES`/`STL`。它不宣称原生 BOM、release export、
sheet 选择或任意扩展名/macro 执行。真实 SOLIDWORKS 2022 fresh-process Live 运行已通过零件→STEP 和工程图→PDF 的非空
文件证据检查；成功测试结束后按隔离清理策略删除临时文件。本机没有可验证的 SOLIDWORKS 2026 安装，因此不能把该结果
表述为 2026 runtime 证据。

## Provenance / 来源

The signatures above were obtained by reflection against the locally installed `SolidWorks.Interop.sldworks.dll` and
cross-checked against the linked SOLIDWORKS API Help pages on 2026-09-16/17. No upstream source code was adapted for
B02, the B03 named-dimension slice, or the B06 export adapter.

以上签名于 2026-09-16/17 对本机 `SolidWorks.Interop.sldworks.dll` 做反射取得，并与链接的 SOLIDWORKS API Help 交叉核对。
B02、B03 命名尺寸切片和 B06 导出 adapter 均没有复用 upstream 源码。

### D03 repeated-feature callout API boundary / D03 重复特征标注 API 边界

`IDrawingDoc.CreateText2` is used for the deterministic semantic `Kind=pattern-callout` note. The provider reads back
`INote.GetText()` and `IAnnotation.GetPosition()` and preserves the stable annotation identity; it does not label the
note as a model dimension. The official `IDrawingDoc.AddHoleCallout2(Double X, Double Y, Double Z)` API was reviewed
against the SOLIDWORKS 2022 help page on 2026-09-17. Its documented workflow requires a selected circular edge and
user confirmation of the resulting dialog. The unattended compiler therefore does not invoke it yet; native associative
Hole Callout remains behind the modal-dialog safety gate, with no blind OK/Enter recovery.

`IDrawingDoc.CreateText2` 用于确定性的 `Kind=pattern-callout` 语义 note。Provider 会读取 `INote.GetText()`、
`IAnnotation.GetPosition()` 并保留稳定 annotation identity；它不会把 note 冒充成模型尺寸。官方
`IDrawingDoc.AddHoleCallout2(Double X, Double Y, Double Z)` API 已于 2026-09-17 对照 SOLIDWORKS 2022 help page
核对：官方流程要求选中圆边并由用户确认随后出现的 dialog。因此 unattended compiler 当前不调用它；native
关联 Hole Callout 仍受 modal-dialog safety gate 约束，禁止 blind OK/Enter recovery。

Official source: [AddHoleCallout2 Method](https://help.solidworks.com/2022/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IDrawingDoc~AddHoleCallout2.html).

### D04 section-view API boundary / D04 剖视 API 边界

The locally installed SOLIDWORKS 2022 interop assembly (`30.0.0.5041`) was reflected before implementation. The
verified signatures are:

| Interface / method | Verified signature | Official source | Runtime note |
| --- | --- | --- | --- |
| `IDrawingDoc.CreateSectionViewAt5` | `View CreateSectionViewAt5(Double X, Double Y, Double Z, String SectionLabel, Int32 Options, Object ExcludedComponents, Double SectionDepth)` | [CreateSectionViewAt5 Method](https://help.solidworks.com/2016/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IDrawingDoc~CreateSectionViewAt5.html) | Requires a selected drawing section line; the provider creates/selects that line on the owning STA and rejects a null return. |
| `ISketchManager.CreateLine` | `SketchSegment CreateLine(Double X1, Double Y1, Double Z1, Double X2, Double Y2, Double Z2)` | [CreateLine Method](https://help.solidworks.com/2023/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.ISketchManager~CreateLine.html) | When a drawing view is active, the sketch line is authored in that view's local coordinates; the provider converts the compiler's paper-space cut line through the exact parent position/angle first. |
| `IView.RemoveAlignment` | `Void RemoveAlignment()` | [RemoveAlignment Method](https://help.solidworks.com/2019/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IView~RemoveAlignment.html) | A created section may inherit parent alignment; the provider removes it before independent compiler placement. |
| `IView.SetXform` | `Boolean SetXform(Object Transform)` | [SetXform Method](https://help.solidworks.com/2015/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IView~SetXform.html) | The transform is three doubles: X, Y and scale. The provider checks the return value and rebuilds before reading position/outline. |
| `IView.GetAlignment` | `Int32 GetAlignment()` | [GetAlignment Method](https://help.solidworks.com/2018/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IView~GetAlignment.html) | Captured as evidence before `RemoveAlignment`; it is not used as a guessed placement policy. |
| `IModelDoc2.Save3` | `Boolean Save3(Int32 Options, ref Int32 Errors, ref Int32 Warnings)` | [IModelDoc2 Interface](https://help.solidworks.com/2022/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IModelDoc2.html) | Used only after PDF export when an exact fingerprint proves that the only transition was clean `saveFlag=False` to transient `saveFlag=True`; restoration is required. |

The official `CreateSectionViewAt5` documentation states that X/Y/Z are the section-view center on the sheet and that a
section line must be selected before the call. The official `IView` documentation also states that aligned views can
move only along their alignment vector; this is why the implementation uses `RemoveAlignment` before `SetXform` instead
of trusting the initial CreateSectionViewAt5 coordinates. The official `CreateLine` contract is the complementary
coordinate boundary: with a drawing view active, its sketch geometry uses view-local coordinates. The provider therefore
applies the inverse parent-view transform before creating the line and records both coordinate spaces as evidence. This
was verified in a fresh SOLIDWORKS 2022 process through
the MCP `cad.build-part-drawing` workflow and a retained PDF artifact. No 2026 runtime was available locally, so this is
not a 2026 runtime claim.

官方 `CreateSectionViewAt5` 文档说明 X/Y/Z 是图纸上的剖视中心，并要求调用前选中 section line。官方 `IView` 文档还说明
aligned view 只能沿 alignment vector 移动，因此实现先 `RemoveAlignment` 再 `SetXform`，不盲信 CreateSectionViewAt5 初始
坐标。官方 `CreateLine` 文档构成互补坐标边界：当 drawing view active 时，sketch geometry 使用 view-local 坐标，
因此 provider 先通过 parent view 的精确 position/angle 执行逆变换，再创建剖切线，并把两套坐标都写入 evidence。
该流程已在全新 SOLIDWORKS 2022 进程中通过 MCP `cad.build-part-drawing` 和保留 PDF artifact 真实验证。本机没有
可验证的 2026 runtime，因此不把它表述为 2026 运行时证据。

### D04 detail-view API boundary / D04 局部放大 API 边界

The locally installed SOLIDWORKS 2022 interop assembly (`30.0.0.5041`) was reflected before implementation. The
verified detail-view signatures and runtime facts are:

| Interface / method | Verified signature | Official source | Runtime note |
| --- | --- | --- | --- |
| `ISketchManager.CreateCircle` | `SketchSegment CreateCircle(Double X1, Double Y1, Double Z1, Double X2, Double Y2, Double Z2)` | [CreateCircle Method](https://help.solidworks.com/2024/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ISketchManager~CreateCircle.html) | Creates the source detail profile from center and circumference point. The active drawing view owns the sketch coordinate system. |
| `IDrawingDoc.CreateDetailViewAt4` | `Object CreateDetailViewAt4(Double X, Double Y, Double Z, Int32 Style, Int32 ScaleNumerator, Int32 ScaleDenominator, String Label, Int32 ShowType, Boolean FullOutline, Boolean JaggedOutline, Boolean UseDocTextFormat, Int32 LineStyle)` | [CreateDetailViewAt4 Method](https://help.solidworks.com/2024/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IDrawingDoc~CreateDetailViewAt4.html) | `swDetViewSTANDARD=0` and `swDetCircleCIRCLE=1` are passed explicitly. The target view position is sheet coordinates; the selected source circle must already exist. |
| `IView.GetDetail` / `IView.GetBaseView` | `Object GetDetail()` / `Object GetBaseView()` | [GetDetail Method](https://help.solidworks.com/2022/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IView~GetDetail.html) / [IView Interface](https://help.solidworks.com/2022/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IView.html) | Read-back proves the returned view is a native detail view and that its `DetailCircle` parent/base binding is exact. |
| `IView.GetPolyLineCount5` / `IView.GetPolyLinesAndCurvesCount` | `Int32 GetPolyLineCount5(Int16 Filter, out Int32 PointCount)` / `Int32 GetPolyLinesAndCurvesCount(Int16 Filter, out Int32 PointCount)` | [GetPolyLineCount5 Method](https://help.solidworks.com/2023/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IView~GetPolyLineCount5.html) / [GetPolyLinesAndCurves Method](https://help.solidworks.com/2022/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IView~GetPolyLinesAndCurves.html) | Projected model edges are verified through these APIs; `GetLineCount`/`GetArcCount` alone only describe drawing sketch entities and are insufficient proof. |
| `IView.UpdateViewDisplayGeometry` | `Void UpdateViewDisplayGeometry()` | [UpdateViewDisplayGeometry Method](https://help.solidworks.com/2022/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IView~UpdateViewDisplayGeometry.html) | Called after rebuild and explicit HLR mode before projected-geometry read-back and PDF export. |

The provider contract accepts the detail center in paper coordinates. Before `CreateCircle`, the native adapter resolves
the exact parent `IView`, reads its paper-space `Position` and `Angle`, and converts the requested center into the active
view-local sketch space. This conversion is mandatory: passing paper coordinates directly creates a valid circle and a
valid `CreateDetailViewAt4` return value, but the native detail contains only its boundary and exports as a blank label-only
view. The adapter now fails closed unless the returned view has the exact parent/detail association, a native profile,
positive outline, and projected polyline/curve evidence.

Provider contract 接受纸空间 detail center。native adapter 先解析精确的 parent `IView`，读取纸空间 `Position` 和 `Angle`，
再把请求中心转换到 active view 的局部 sketch 坐标后调用 `CreateCircle`。这个转换是必须的：直接传纸空间坐标虽然
会得到合法圆和合法的 `CreateDetailViewAt4` 返回值，但 native detail 只包含边界，导出后会变成只有标签的空视图。
因此 adapter 只有在 parent/detail 关联、native profile、正 outline 以及 projected polyline/curve evidence 均读回成功时
才返回成功。

Fresh-process Live evidence from two redacted single-part classes:

    `SLDWORKS_COUNT_BEFORE=0`
    `SLDWORKS_COUNT_AFTER_OLD=0`
    `drawing.detail.view.detail-local-circle-center.meters=0,0.01`
    `drawing.detail.view.detail-source-circle-params.meters=0,0.01,0,0,0,1,0.012...`
    `drawing.detail.view.detail-polyline-count=2`
    `drawing.detail.view.detail-parent-native-name=Drawing View1`
    `SLDWORKS_COUNT_AFTER=0`

The retained rounded-plate PDF was rendered and visually inspected: the parent Front view contains the native detail
circle/label, and the Detail A view contains the enlarged hole and center marks. The second formed U-bracket case remains
a real curved non-cylindrical single-part drawing without a detail request. These are generic local fixtures; private source
PDFs, source filenames, exact private dimensions and source renders remain outside Git, logs and artifacts.

Official sample sequence cross-check: [Create Detail Circle and Detail View Example (C#)](https://help.solidworks.com/2023/english/api/sldworksapi/Create_Detail_Circle_and_Detail_View_Example_CSharp.htm).

### D05 native obround-slot API boundary / D05 原生长圆槽 API 边界

Before implementation, the installed SOLIDWORKS 2022 Interop assemblies were reflected and the official 2026 help
page was checked for the same public signature. The verified contract is:

| Interface / method | Verified signature | Official source | Runtime note |
| --- | --- | --- | --- |
| ISketchManager.CreateSketchSlot | SketchSlot CreateSketchSlot(Int32 SlotCreationType, Int32 SlotLengthType, Double Width, Double X1, Double Y1, Double Z1, Double X2, Double Y2, Double Z2, Double X3, Double Y3, Double Z3, Int32 CenterArcDirection, Boolean AddDimension) | [CreateSketchSlot Method](https://help.solidworks.com/2026/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ISketchManager~CreateSketchSlot.html) | The provider uses swSketchSlotCreationType_line=0, swSketchSlotLengthType_CenterCenter=0, CenterArcDirection=1 and AddDimension=false. |
| ISketchSlot.Width / ISketchSlot.Length | Double Width { get; set; } / Double Length { get; } | [ISketchSlot Interface Members](https://help.solidworks.com/2025/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ISketchSlot_members.html) | Native width and centerline length are read back before FeatureCut4; the evidence is not inferred from the drawing PDF. |
| IFeatureManager.FeatureCut4 | See the B04 signature above | [FeatureCut4 Method](https://help.solidworks.com/2022/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IFeatureManager~FeatureCut4.html) | The slot sketch is cut with swEndCondThroughAll in both directions, followed by rebuild, body/volume/diagnostic inspection and save/reopen proof. |

The public CreateSketchSlot documentation defines X1/Y1/Z1 and X2/Y2/Z2 as the two centerline points, width as the slot
width, and CenterArcDirection as -1 clockwise or 1 counter-clockwise. The abstraction therefore keeps the
centerline endpoints and an explicit SupportFaceProbe separately: for a slot in a narrow bracket leg, the centerline
may lie inside the material to be removed and cannot safely double as a face-selection ray.

官方 CreateSketchSlot 文档定义 X1/Y1/Z1 与 X2/Y2/Z2 为中心线两端点，Width 为槽宽，CenterArcDirection 为
-1 顺时针或 1 逆时针。抽象层因此把中心线端点与显式 SupportFaceProbe 分开：窄支架腿上的槽中心线可能
完全落在待切材料内，不能同时作为安全的面选择射线。

Fresh-process Live proof uses one generated generic formed U-bracket case in the two-case redacted single-part run:

    part.slot-cut.kind=slot-cut
    part.slot-cut.native.slot.width-millimeters=4.000...
    part.slot-cut.native.slot.centerline-length-millimeters=12
    part.slot-cut.native.slot.native-length-millimeters=12
    part.slot-cut.native.body.count=1
    drawing.slot-callout.reopened=verified
    SLDWORKS_COUNT_BEFORE=0
    SLDWORKS_COUNT_AFTER=0

The rendered native PDF shows the actual obround opening in the bracket leg, not just a semantic note. This is a
focused D05 slice; Hole Wizard semantics, sheet-metal bend features, slot dimension association and general layout
reflow remain separate compiler/provider work.

### D07 native surface-finish symbol boundary / D07 原生表面粗糙度符号边界

Before implementation, the installed SOLIDWORKS 2022 Interop assembly was reflected and the public 2026 API Help was
cross-checked. The verified contract is:

| Interface / method | Verified signature | Version evidence | Official source | Runtime note |
| --- | --- | --- | --- | --- |
| `IDrawingDoc.InsertSurfaceFinishSymbol` | `Boolean InsertSurfaceFinishSymbol(Int32 SymType, Int32 LeaderType, Double LocX, Double LocY, Double LocZ, Int32 LaySymbol, Int32 ArrowType, String MachAllowance, String OtherVals, String ProdMethod, String SampleLen, String MaxRoughness, String MinRoughness, String RoughnessSpacing)` | SOLIDWORKS 2022 Interop reflection; public signature cross-checked against SOLIDWORKS 2026 API Help | [InsertSurfaceFinishSymbol Method](https://help.solidworks.com/2026/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IDrawingDoc~InsertSurfaceFinishSymbol.html) | The provider passes canonical paper-space metres, then reads the resulting `SFSymbol` and `IAnnotation` back on the owning STA. |
| `ISFSymbol.GetText` | `String GetText(Int32 Type)` | SOLIDWORKS 2022 Interop reflection; `swSurfaceFinishSymbolText_e.swSFSymbolMaximumRoughness` verified locally | [ISFSymbol Interface](https://help.solidworks.com/2026/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ISFSymbol.html) | Maximum roughness is compared with the approved request; a successful insertion return value alone is not accepted as evidence. |
| `ISFSymbol.GetSymbolType` / `GetDirectionOfLay` | `Int32 GetSymbolType()` / `Int32 GetDirectionOfLay()` | SOLIDWORKS 2022 Interop reflection | [ISFSymbol Interface](https://help.solidworks.com/2022/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ISFSymbol.html) | Native symbol style and lay direction are verified before the compiler commits the annotation descriptor. |
| `IView.GetSFSymbols` | `Object GetSFSymbols()` | SOLIDWORKS 2022 Interop reflection; `GetSFSymbolCount`/`IGetSFSymbols` fallback also verified | [Get Annotations Arrays Example (C#)](https://help.solidworks.com/2026/english/api/sldworksapi/Get_Annotations_Arrays_Example_CSharp.htm) | The provider compares the exact target view's symbol collection before/after insertion and requires exactly one new native symbol. |

The public Help explicitly states that the `LocX/LocY/LocZ` location is used only when a leader is present. The compiler
therefore defaults the wire contract to `Straight` leader plus `Open` arrow for deterministic paper-space placement;
`NoLeader` remains an explicit public enum option but is not used by the reference Live fixture because SOLIDWORKS may
place it at its default location. 官方帮助明确说明只有存在 leader 时才使用 `LocX/LocY/LocZ`；因此 compiler 默认使用
`Straight` leader 与 `Open` arrow 来获得确定性纸空间 placement。`NoLeader` 仍是公开 enum 选项，但参考 Live fixture
不使用它，因为 SOLIDWORKS 可能把符号放到默认位置。

The provider-neutral request requires stable annotation/view identities, approved or released provenance, an explicit
roughness value and coverage keys. The current native association is recorded as `view-scoped-unattached`: it is a
deliberate fail-closed boundary until a persistent model-edge selector is available. An AI proposal or a roughness value
guessed from a screenshot cannot pass this mutation contract. vendor-neutral request 强制要求稳定 annotation/view identity、
Approved/Released provenance、明确粗糙度值和 coverage keys。当前 native association 明确记录为
`view-scoped-unattached`；在 persistent model-edge selector 完成前保持这个 fail-closed boundary。AI proposal 或从截图
猜出的粗糙度不能通过该 mutation contract。

Fresh-process Live evidence from the two redacted single-part classes:

    drawing.surface-finish=surface-finish
    drawing.surface-finish.maximum-roughness=0.8
    drawing.surface-finish.reopened=verified
    drawing.surface-finish.native.surface-finish.association=view-scoped-unattached
    drawing.surface-finish.native.surface-finish.maximum-roughness=0.8
    SLDWORKS_COUNT_BEFORE=0
    SLDWORKS_COUNT_AFTER=0

The rounded-plate PDF was rendered and visually checked after the first placement defect was corrected: the native symbol
is visible inside the sheet boundary, separate from the pattern callout and title area. The formed U-bracket remains the
second generic non-cylindrical case and does not request a surface-finish symbol. Private source PDFs, names and source
values are not copied into this corpus.
## Drawing sheet properties / 工程图图纸属性

| interface | method | signature / return | versions | official source | runtime notes |
| --- | --- | --- | --- | --- | --- |
| `ISheet` | `GetProperties2` | `object GetProperties2()`; eight packed doubles: paper size, template index, scale numerator, scale denominator, first-angle flag, width, height, same-custom-property flag | 2022, 2026 | https://help.solidworks.com/2023/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ISheet~GetProperties2.html | Treat the returned object as a numeric array and verify width/height/projection by read-back. Do not infer sheet size from the template filename. |
| `ISheet` | `GetTemplateName` | `string GetTemplateName()` | 2022, 2026 | https://help.solidworks.com/2022/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ISheet~GetTemplateName.html | Template identity is evidence only; it does not prove paper size or title-block placement. |
| `ISheet` | `GetSize` | `int GetSize(out double width, out double height)` | 2022, 2026 | https://help.solidworks.com/2022/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ISheet~GetSize.html | Use as an independent size read-back beside `GetProperties2`. |
| `IDrawingDoc` | `SetupSheet3` | `bool SetupSheet3(string name, int paperSize, int templateIn, double scale1, double scale2, bool firstAngle, string templateName, double width, double height)` | 2022, 2026 | https://help.solidworks.com/2022/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IDrawingDoc~SetupSheet3.html | Use only with an explicit provider-neutral sheet contract, then force rebuild and inspect. A true return value is not sufficient evidence. |
| `swDwgPaperSizes_e` | `swDwgPaperA4size` | enum value `6`; `swDwgPaperA4sizeVertical` is `7` | 2022, 2026 | https://help.solidworks.com/2022/english/api/swconst/SolidWorks.Interop.swconst~SolidWorks.Interop.swconst.swDwgPaperSizes_e.html | Chinese baseline uses A4 landscape unless an approved RulePack selects another A-series sheet. |
