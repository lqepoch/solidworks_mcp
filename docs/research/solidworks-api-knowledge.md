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

- `RotSolidWorksConnector` 在 Provider STA 上枚举 COM ROT，只接受能转换为 `ISldWorks`、通过 `GetProcessID()` 报告
  存活的 `SLDWORKS` 进程、且符合请求 PID 的对象。
- 未限定 PID 时只有恰好一个候选才附着；多个候选返回 `STATE_CONFLICT`，绝不根据 ActiveDoc 或文件名猜测。
- Provider 不调用 `new SldWorks()`、`Activator.CreateInstance`、`Marshal.GetActiveObject` 或 `ExitApp`。
- 所有 COM RCW 只在其所属 STA 释放；`SolidWorksMcp.CadAbstractions` 和 MCP payload 不出现 COM 接口。

## Provenance / 来源

The signatures above were obtained by reflection against the locally installed `SolidWorks.Interop.sldworks.dll` and
cross-checked against the linked SOLIDWORKS API Help pages on 2026-09-16. No upstream source code was adapted for B02.

以上签名于 2026-09-16 对本机 `SolidWorks.Interop.sldworks.dll` 做反射取得，并与链接的 SOLIDWORKS API Help 交叉核对。
B02 没有复用任何 upstream 源码。
