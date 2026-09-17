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
| `ISldWorks.ExitApp` | `System.Void ExitApp()` | SOLIDWORKS 2022 Interop reflection | [ExitApp Method](https://help.solidworks.com/2025/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ISldWorks~ExitApp.html) | Explicitly not called on detach; an attached interactive process belongs to the user and may contain unsaved work. |

## Discovery decision / 发现策略

- `RotSolidWorksConnector` enumerates COM's Running Object Table on the provider STA and accepts only objects that cast
  to `ISldWorks`, report a live `SLDWORKS` process through `GetProcessID()`, and satisfy the requested PID.
- An unqualified attach is allowed only when exactly one eligible process remains. Multiple eligible processes return
  `STATE_CONFLICT`; the provider never guesses from “active document” or filename.
- The provider does not call `new SldWorks()`, `Activator.CreateInstance`, `Marshal.GetActiveObject` or `ExitApp`.
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
| `ISldWorks.CloseDoc` | `System.Void CloseDoc(System.String FileName)` | SOLIDWORKS 2022 Interop reflection | [ISldWorks Interface](https://help.solidworks.com/2022/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.ISldWorks.html) | B03 passes the registered canonical path, preflights the document as clean, and verifies `GetOpenDocument` returns null after close. |
| `ISldWorks.OpenDoc6` | `ModelDoc2 OpenDoc6(System.String FileName, System.Int32 Type, System.Int32 Options, System.String Configuration, ref System.Int32 Errors, ref System.Int32 Warnings)` | SOLIDWORKS 2022 Interop reflection; `swDocPART=1`, `swOpenDocOptions_Silent=1` | [Open Document Example (C#)](https://help.solidworks.com/2022/english/api/sldworksapi/Open_Document_Example_CSharp.htm) | B03 uses the documented replacement for obsolete silent-open calls, passes the registered part configuration, rejects a null model or non-zero load error, then revalidates identity and inspection invariants. |
| `IModelDoc2.Parameter` | `System.Object Parameter(System.String Name)` | SOLIDWORKS 2022 Interop reflection | [IParameter Method (IFeature)](https://help.solidworks.com/2022/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFeature~IParameter.html) | The provider accepts only an explicit full parameter identity such as `D1@FeatureName`; a missing object is reported as `SELECTION_STALE`, never guessed from an enumeration index. |
| `IDimension.SetSystemValue3` | `System.Int32 SetSystemValue3(System.Double NewValue, System.Int32 WhichConfigurations, System.Object Config_names)` | SOLIDWORKS 2022 Interop reflection; `swSetValue_InThisConfiguration=1`, successful return `swSetValue_Successful=0` | [SetSystemValue3 Method](https://help.solidworks.com/2022/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IDimension~SetSystemValue3.html) | The thin adapter converts canonical millimetres to SOLIDWORKS metres, writes only the registered configuration, checks the status code, then reads back and rebuilds before reporting success. |
| `IDimension.GetSystemValue3` | `System.Object GetSystemValue3(System.Int32 WhichConfigurations, System.Object Config_names)` | SOLIDWORKS 2022 Interop reflection; read option `swThisConfiguration=1` | [GetSystemValue3 Method](https://help.solidworks.com/2022/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IDimension~GetSystemValue3.html) | Read-back is mandatory evidence: the setter's return code alone is not proof. The returned system value is normalized from metres to the vendor-neutral `Length` contract. |
| `IFeature.GetErrorCode2` | `System.Int32 GetErrorCode2(out System.Boolean IsWarning)` | SOLIDWORKS 2022 Interop reflection; non-zero feature code indicates a What's Wrong diagnostic | [GetErrorCode2 Method](https://help.solidworks.com/2025/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IFeature~GetErrorCode2.html) | The official signature is unchanged across the locally installed 2022 interop and the linked API Help revision. Native inspection traverses the feature tree on the STA, records non-zero warning/error codes with stable feature identity, and never copies arbitrary UI/modal text. |

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
- Provider 不调用 `new SldWorks()`、`Activator.CreateInstance`、`Marshal.GetActiveObject` 或 `ExitApp`。
- 所有 COM RCW 只在其所属 STA 释放；`SolidWorksMcp.CadAbstractions` 和 MCP payload 不出现 COM 接口。

## B04 non-cylindrical part and native drawing proof / B04 非圆柱零件与原生工程图证据

| Interface / method | Verified signature | Version evidence | Official source | Runtime note |
| --- | --- | --- | --- | --- |
| `ISketchManager.CreateCornerRectangle` | `System.Object CreateCornerRectangle(Double X1, Double Y1, Double Z1, Double X2, Double Y2, Double Z2)` | SOLIDWORKS 2022 Interop assembly `30.0.0.5041` | [CreateCornerRectangle Method](https://help.solidworks.com/2022/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.ISketchManager~CreateCornerRectangle.html) | The provider creates a centered closed profile in metres, then checks the resulting `ProfileFeature` identity before extrusion. The returned COM collection is released on the provider STA. |
| `ISketchManager.CreateLine` | `SketchSegment CreateLine(Double X1, Double Y1, Double Z1, Double X2, Double Y2, Double Z2)` | SOLIDWORKS 2022 Interop assembly `30.0.0.5041` | [CreateLine Method](https://help.solidworks.com/2022/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.ISketchManager~CreateLine.html) | The provider closes the polygon explicitly by connecting the last vertex to the first; it does not assume SOLIDWORKS will infer missing topology. |
| `IModelDocExtension.SelectByRay` | `System.Boolean SelectByRay(Double X, Double Y, Double Z, Double DirX, Double DirY, Double DirZ, Double Radius, Int32 Type, Boolean Append, Int32 Mark, Int32 Action)` | SOLIDWORKS 2022 Interop assembly `30.0.0.5041` | [SelectByRay Method](https://help.solidworks.com/2022/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IModelDocExtension~SelectByRay.html) | B04 uses a bounded ray to select the verified top planar face of the centered L-profile fixture. This is a proof-of-API path, not yet the general geometry selector; arbitrary orientations must move to persistent/reference-backed face selection before release. |
| `IFeatureManager.FeatureCut4` | `Feature FeatureCut4(Boolean Sd, Boolean Flip, Boolean Dir, Int32 T1, Int32 T2, Double D1, Double D2, Boolean Dchk1, Boolean Dchk2, Boolean Ddir1, Boolean Ddir2, Double Dang1, Double Dang2, Boolean OffsetReverse1, Boolean OffsetReverse2, Boolean TranslateSurface1, Boolean TranslateSurface2, Boolean NormalCut, Boolean UseFeatScope, Boolean UseAutoSelect, Boolean AssemblyFeatureScope, Boolean AutoSelectComponents, Boolean PropagateFeatureToParts, Int32 T0, Double StartOffset, Boolean FlipStartOffset, Boolean OptimizeGeometry)` | SOLIDWORKS 2022 Interop assembly `30.0.0.5041`; `swEndCondThroughAll` verified from `swconst` | [FeatureCut4 Method](https://help.solidworks.com/2022/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IFeatureManager~FeatureCut4.html) | A non-null return is not accepted as proof alone. B04 also forces rebuild, inspects body count/positive volume/feature identity, and retains semantic hole-count and diameter evidence. |
| `IDrawingDoc.CreateDrawViewFromModelView3` | `View CreateDrawViewFromModelView3(String ModelName, String ModelViewName, Double X, Double Y, Double Scale)` | SOLIDWORKS 2022 Interop assembly `30.0.0.5041` | [CreateDrawViewFromModelView3 Method](https://help.solidworks.com/2011/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IDrawingDoc~CreateDrawViewFromModelView3.html) | The provider creates native Front/Top/Isometric views from the saved part and validates a positive `IView.GetOutline()` before persisting the drawing. |
| `IDrawingDoc.GetViews` | `System.Object GetViews()` | SOLIDWORKS 2022 Interop assembly `30.0.0.5041` | [GetViews Method](https://help.solidworks.com/2023/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SolidWorks.Interop.sldworks.IDrawingDoc~GetViews.html) | The COM result is a jagged array whose first element per sheet is the sheet sentinel. The adapter excludes that sentinel and records stable view evidence without exposing COM objects to domain code. |
| `IView.GetOrientationName` | `System.String GetOrientationName()` | SOLIDWORKS 2022 Interop assembly `30.0.0.5041` | [IView Interface](https://help.solidworks.com/2022/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SolidWorks.Interop.sldworks.IView.html) | `GetName2()` is a generated drawing label such as `Drawing View1`, not the model orientation. Reopen inspection therefore uses `GetOrientationName()` for semantic Front/Top/Isometric verification. |
| `IView.GetOutline` | `System.Object GetOutline()` | SOLIDWORKS 2022 Interop assembly `30.0.0.5041` | [GetOutline Method](https://help.solidworks.com/2016/english/api/sldworksapi/solidworks.interop.sldworks~solidworks.interop.sldworks.iview~getoutline.html) | The returned four-value paper-space bounding box is an invariant used to reject empty or invalid native views; it is not used to guess-move source geometry. |

### B04 evidence boundary / B04 证据边界

The B04 Live fixture is deliberately generic and redacted: it proves an L-profile, non-cylindrical bracket with a semantic repeated through-hole group can be created, rebuilt, saved, reopened, and represented by native drawing views. It does not copy dimensions, text, title blocks, or filenames from the confidential local drawing corpus. The private corpus is sampled by its local skill and remains outside Git, build inputs, logs, screenshots, and artifacts.

B04 Live fixture 是刻意脱敏的通用案例：它证明 L 形非圆柱支架、带工程语义的重复通孔组可以真实创建、重建、保存、重新打开，并由原生工程图视图表达。它不复制机密本地图纸的尺寸、文字、标题栏、文件名；机密 corpus 由本地 skill 抽样，始终不进入 Git、构建输入、日志、截图或 artifact。

## Provenance / 来源

The signatures above were obtained by reflection against the locally installed `SolidWorks.Interop.sldworks.dll` and
cross-checked against the linked SOLIDWORKS API Help pages on 2026-09-16. No upstream source code was adapted for B02
or the B03 named-dimension slice.

以上签名于 2026-09-16 对本机 `SolidWorks.Interop.sldworks.dll` 做反射取得，并与链接的 SOLIDWORKS API Help 交叉核对。
B02 和 B03 命名尺寸切片均没有复用任何 upstream 源码。
