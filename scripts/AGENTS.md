# Script rules

PowerShell scripts must be deterministic, non-interactive by default, explicit about writes, and safe for public CI. Do not modify global registry/settings, write secrets, or delete broad paths. Machine-specific paths belong in user-local generated files and must be ignored by Git.

The private drawing sampler is an exception only for local, user-authorized research input: `Invoke-PrivateDrawingSample.ps1`
must sample exactly two single-part candidates, keep source paths/content/renders in user-local temporary evidence, and emit
only redacted slot status. Never add a PDF filename, extracted text, drawing value or rendered image to script output,
tracked fixtures, CI artifacts, Issues or PRs. 私密图纸抽样脚本只允许在本机处理用户授权的研究输入：每次必须恰好随机抽取
两个单零件候选，源路径、原文和渲染结果只留在 user-local 临时证据目录；脚本输出只能包含脱敏 slot 状态，禁止把 PDF
文件名、提取文本、图纸数值或渲染图片写入脚本输出、tracked fixture、CI artifact、Issue 或 PR。
