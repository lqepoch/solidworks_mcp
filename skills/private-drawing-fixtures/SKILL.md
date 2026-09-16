# Private drawing fixtures / 私密图纸 fixtures

## Scope / 适用范围

Use this skill only for local, user-authorized review of the confidential PDFs under the repository's ignored `图纸/`
directory. The PDFs are engineering references, not distributable project assets. 本 skill 只用于本机、用户授权的 ignored
`图纸/` 目录秘密 PDF 复核；PDF 是工程参考，不是可再分发的项目资源。

## Non-disclosure contract / 不披露契约

- Never add source PDFs, copied pages, screenshots, extracted text, OCR output, filenames, title-block values, customer names,
  part numbers or exact dimensions to Git, Issues, PRs, chat messages, test snapshots or committed documentation.
- Never print raw PDF text or paths in normal command output. Use `sample-01` and `sample-02` slot labels and generic feature
  classes in evidence.
- Keep temporary renders, manifests and CAD outputs below `%LOCALAPPDATA%\SolidWorksMcp\private-drawing-samples` or the system
  temp directory. Delete successful-run artifacts; quarantine failures locally for diagnosis.
- Do not upload any private artifact to a remote service. Do not use private PDFs as web-search input.

- 禁止把源 PDF、页面复制品、截图、提取文本、OCR、文件名、标题栏值、客户名、料号或精确尺寸写入 Git、Issue、PR、聊天、
  测试快照或 tracked 文档。
- 普通命令输出禁止原文和路径，只能使用 `sample-01`、`sample-02` 与通用 feature class 记录证据。
- 临时渲染、manifest 和 CAD 输出只能写到 `%LOCALAPPDATA%\SolidWorksMcp\private-drawing-samples` 或系统 temp；成功时清理，
  失败时仅在本地 quarantine 供诊断。
- 禁止把任何秘密 artifact 上传到远端，也禁止把秘密 PDF 作为 web search 输入。

## Sampling contract / 抽样契约

1. Discover PDFs below the explicitly supplied local root.
2. Exclude assembly/ambiguous candidates using content/layout signals; do not trust filenames alone.
3. Select exactly two remaining single-part candidates with a cryptographically seeded random source on every run.
4. Render the selected pages locally for visual review because drawing layout matters.
5. Record only redacted slot IDs and generic semantic classes, such as `sheet-metal-bracket`, `hole-pattern`,
   `orthographic-views`, `section-or-detail-candidate`, `surface-finish`, `general-tolerance` and `title-block`.
6. Translate observations into immutable engineering requirements with `private_drawing_review` provenance. AI may propose;
   it may not silently promote inferred dimensions, fits, GD&T, datums or tolerances to Released.
7. Keep assembly drawings out of the current compiler/tests. Add them only under a separate issue and separate opt-in gate.

1. 在明确指定的本地 root 下发现 PDF。
2. 使用内容/版式信号排除总成或歧义项，不能只信文件名。
3. 每次运行使用加密随机源，从剩余单零件候选中恰好选择两张。
4. 因为图纸版式影响工程含义，必须在本地渲染后视觉复核。
5. 只记录脱敏 slot ID 和通用语义类别。
6. 将观察转换成带 `private_drawing_review` provenance 的 immutable engineering requirements。AI 只能提议，不能把推断的尺寸、
   配合、GD&T、基准或公差静默提升到 Released。
7. 当前编译器/测试排除总成图；总成必须在独立 issue 和独立 opt-in gate 下加入。

## Evidence / 证据

Evidence must distinguish `selected`, `rendered`, `reviewed`, `implemented`, `validated`, `passed`, `failed`, `skipped`
and `blocked`. A PDF selection or visual review is never itself a CAD geometry pass. Geometry claims require provider
inspection (body/feature/bounding box/mass/volume and other applicable invariants); drawing claims require requirement
coverage, association and layout QA. 证据必须区分 selected/rendered/reviewed/implemented/validated/passed/failed/skipped/blocked；
抽样或视觉复核本身绝不是 CAD 几何通过。几何必须由 Provider inspection 证明，工程图必须由 requirement coverage、关联性和布局 QA 证明。
