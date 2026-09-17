[CmdletBinding()]
param(
    [string]$DrawingRoot,
    [string]$OutputRoot = (Join-Path $env:LOCALAPPDATA 'SolidWorksMcp\private-drawing-samples'),
    [string]$PythonPath = 'python',
    [string]$PdftoppmPath = 'pdftoppm',
    [switch]$KeepArtifacts
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($DrawingRoot)) {
    # Resolve the repository-local confidential folder only after script scope is initialized in Windows PowerShell.
    # 在 Windows PowerShell 完成脚本作用域初始化后，再解析仓库内的机密文件夹。
    $DrawingRoot = Join-Path (Split-Path $PSScriptRoot -Parent) '图纸'
}

function Resolve-Executable {
    param([Parameter(Mandatory)][string]$Candidate, [Parameter(Mandatory)][string]$Name)

    # Resolve only an explicitly supplied path or a command on PATH; never guess a developer-specific installation.
    # 只接受显式路径或 PATH 中的命令，绝不猜测开发者个人安装路径。
    if (Test-Path -LiteralPath $Candidate -PathType Leaf) {
        return (Resolve-Path -LiteralPath $Candidate).Path
    }

    $command = Get-Command $Candidate -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        throw "$Name executable is unavailable. Supply -$Name`Path explicitly."
    }

    return $command.Source
}

function Invoke-PdfMetadataProbe {
    param(
        [Parameter(Mandatory)][string]$Python,
        [Parameter(Mandatory)][string]$Root
    )

    # This is an independent research helper, not a product runtime dependency.  It extracts only enough metadata to
    # select part drawings and returns structured records to PowerShell; raw PDF text never reaches normal output.
    # 这是独立 research helper，不是产品运行时依赖。它只提取用于选择零件图的元数据并返回结构化记录；PDF 原文不进入普通输出。
    $pythonCode = @'
import hashlib
import json
import sys
from pathlib import Path
from secrets import SystemRandom

try:
    from pypdf import PdfReader
except Exception as exc:
    raise RuntimeError("pypdf is required by the private drawing sampler") from exc

# Windows PowerShell 5.1 may encode a non-ASCII path as the console code page when it pipes source to Python stdin.
# Pass the private root as an ASCII-only UTF-8 Base64 token instead; the token is never printed or persisted.
# Windows PowerShell 5.1 把非 ASCII path 通过 stdin 传给 Python 时可能按控制台代码页编码；改用 ASCII-only 的
# UTF-8 Base64 token，token 不打印也不持久化。
import base64

root = Path(base64.b64decode(sys.argv[1]).decode("utf-8")).resolve()
if not root.is_dir():
    raise RuntimeError("private drawing root is not a directory")

assembly_terms = ("总成", "装配", "组件", "明细表", "bom", "assembly", "assy", "subassembly", "零件号", "装配图")
part_terms = ("零件", "部件", "part", "detail", "剖视", "材料", "技术要求", "未注公差", "表面粗糙度")

def semantic_classes(text: str) -> list[str]:
    folded = text.casefold()
    classes = ["single-sheet-drawing", "title-block-candidate"]
    if any(token in folded for token in ("孔", "hole", "⌀", "φ", "dia")):
        classes.append("hole-feature")
    if any(token in folded for token in ("钣金", "折弯", "板厚", "bracket", "sheet")):
        classes.append("sheet-metal-or-bracket")
    if any(token in folded for token in ("剖", "section", "detail", "局部")):
        classes.append("section-or-detail-candidate")
    if any(token in folded for token in ("公差", "tolerance", "±", "上偏差", "下偏差")):
        classes.append("tolerance-requirement")
    if any(token in folded for token in ("材料", "material", "q235", "steel")):
        classes.append("material-requirement")
    if any(token in folded for token in ("粗糙度", "surface finish", "ra")):
        classes.append("surface-finish-requirement")
    if len(classes) == 2:
        classes.append("visual-review-required")
    return sorted(set(classes))

records = []
for path in sorted(root.rglob("*.pdf")):
    try:
        reader = PdfReader(str(path))
        text = "\n".join(page.extract_text() or "" for page in reader.pages)
        folded = text.casefold()
        assembly_hits = sum(folded.count(token.casefold()) for token in assembly_terms)
        part_hits = sum(folded.count(token.casefold()) for token in part_terms)
        # Unclassified image-only documents are excluded because this phase is part-only and must not guess.
        # 图像型、无法分类的文档在当前 part-only 阶段排除，不能猜测其类型。
        if part_hits == 0 or assembly_hits >= 2 or (assembly_hits > part_hits and assembly_hits > 0):
            continue
        records.append({
            "source_sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
            "page_count": len(reader.pages),
            "classes": semantic_classes(text),
        })
    except Exception:
        # A corrupt/unreadable secret file is not selected and is not described in output.
        # 损坏或不可读的秘密文件不参与抽样，也不在输出中描述。
        continue

if len(records) < 2:
    raise RuntimeError("fewer than two content-classified single-part PDFs are available")

selected = SystemRandom().sample(records, 2)
print(json.dumps(selected, ensure_ascii=False))
'@

    # Windows PowerShell 5.1 can transcode both non-ASCII source code and paths when piping them to Python stdin.
    # Write this research-only probe as UTF-8 to a short-lived user-temp file and pass only an ASCII path token.
    # Windows PowerShell 5.1 可能转码通过 stdin 传入的非 ASCII 源码和 path；这里把 research-only probe 以 UTF-8
    # 写入短生命周期 user-temp 文件，并且只传递 ASCII path token。
    $previousErrorActionPreference = $ErrorActionPreference
    $probeScriptPath = Join-Path ([IO.Path]::GetTempPath()) ("SolidWorksMcp.PrivateDrawingProbe-{0}.py" -f [Guid]::NewGuid().ToString('N'))
    try {
        $ErrorActionPreference = 'Continue'
        [IO.File]::WriteAllText($probeScriptPath, $pythonCode, [Text.UTF8Encoding]::new($false))
        $rootToken = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($Root))
        $json = (& $Python $probeScriptPath $rootToken 2>$null | Out-String).Trim()
        $probeExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
        if (Test-Path -LiteralPath $probeScriptPath -PathType Leaf) {
            [IO.File]::Delete($probeScriptPath)
        }
    }
    if ($probeExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($json)) {
        # Keep diagnostics redacted: exit code and output length are safe; the probe output may contain source-derived
        # hashes/classes and must never be echoed as a troubleshooting shortcut.
        # 诊断保持脱敏：exit code 和 output length 安全；probe 输出可能含源文件派生 hash/class，绝不能直接打印。
        throw "The private PDF metadata probe failed (exit=$probeExitCode; output-length=$($json.Length)). Install pypdf or supply a compatible -PythonPath."
    }

    try {
        $parsed = $json | ConvertFrom-Json
        if ($parsed -is [Array]) {
            # Windows PowerShell 5.1 can preserve a JSON array as one function-output object. Enumerate explicitly so
            # the caller's exact-two invariant is stable across PowerShell generations.
            # Windows PowerShell 5.1 可能把 JSON array 作为一个函数输出对象保留；显式逐项枚举，确保不同版本的
            # PowerShell 都遵守 caller 的 exactly-two invariant。
            foreach ($item in $parsed) {
                Write-Output $item
            }
        }
        else {
            Write-Output $parsed
        }
    }
    catch {
        throw 'The private PDF metadata probe returned invalid structured metadata.'
    }
}

$root = (Resolve-Path -LiteralPath $DrawingRoot -ErrorAction Stop).Path
$python = Resolve-Executable -Candidate $PythonPath -Name 'Python'
$pdftoppm = Resolve-Executable -Candidate $PdftoppmPath -Name 'Pdftoppm'
$records = @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter '*.pdf' -ErrorAction Stop)
if ($records.Count -lt 2) {
    throw 'The private drawing root contains fewer than two PDFs.'
}

# The repository copy is ignored by policy.  If a caller points at a tracked PDF, stop before any render or manifest.
# 仓库内的 PDF 必须被 Git ignore；若调用方指向 tracked PDF，在渲染或写 manifest 前立即阻断。
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$git = Get-Command git -ErrorAction SilentlyContinue
if ($root.StartsWith($repositoryRoot, [StringComparison]::OrdinalIgnoreCase) -and $null -ne $git) {
    $repositoryUri = [Uri]::new(($repositoryRoot.TrimEnd('\') + '\'), [UriKind]::Absolute)
    foreach ($record in $records) {
        # Uri.MakeRelativeUri keeps this guard compatible with Windows PowerShell/.NET Framework hosts that lack
        # System.IO.Path.GetRelativePath.  用 Uri 相对路径兼容没有 Path.GetRelativePath 的 Windows PowerShell/.NET Framework。
        $relativeUri = $repositoryUri.MakeRelativeUri([Uri]::new($record.FullName, [UriKind]::Absolute))
        $relative = [Uri]::UnescapeDataString($relativeUri.ToString()).Replace('/', '\')
        & $git.Source -C $repositoryRoot check-ignore --no-index -q -- $relative 2>$null
        if ($LASTEXITCODE -ne 0) {
            throw 'Private drawing sampler refused a PDF that is not covered by the repository ignore policy.'
        }
    }
}

$selected = @(Invoke-PdfMetadataProbe -Python $python -Root $root)
if ($selected.Count -ne 2) {
    throw "Private drawing sampler selected $($selected.Count) records; exactly two are required."
}

# Match by content hash inside PowerShell instead of sending a confidential Unicode path through a native stdout pipe.
# 按内容 hash 在 PowerShell 内存中匹配，避免让秘密 Unicode path 穿过 native stdout 管道。
$sourceByDigest = @{}
foreach ($record in $records) {
    $digest = (Get-FileHash -LiteralPath $record.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $sourceByDigest[$digest] = $record.FullName
}

$output = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$manifestSamples = [System.Collections.Generic.List[object]]::new()
$completed = $false
try {
    for ($index = 0; $index -lt 2; $index++) {
        $slot = 'sample-{0:D2}' -f ($index + 1)
        $sourceDigest = ([string]$selected[$index].source_sha256).ToLowerInvariant()
        $source = $sourceByDigest[$sourceDigest]
        if ([string]::IsNullOrWhiteSpace($source)) {
            throw "Private drawing source resolution failed for $slot."
        }
        $prefix = Join-Path $output $slot
        # Poppler's Windows build does not accept a POSIX-style `--` delimiter; the source is already a resolved PDF
        # path from the private root.  Windows 版 Poppler 不接受 POSIX 风格的 `--` 分隔符；source 已来自 resolved private root。
        & $pdftoppm -png -singlefile -r 150 $source $prefix 2>$null | Out-Null
        $renderExitCode = $LASTEXITCODE
        $rendered = Test-Path -LiteralPath "$prefix.png" -PathType Leaf
        if ($renderExitCode -ne 0 -or -not $rendered) {
            throw "Private drawing render failed for $slot (exit=$renderExitCode, rendered=$rendered)."
        }

        $manifestSamples.Add([pscustomobject]@{
            slot = $slot
            sourceSha256 = $sourceDigest
            pageCount = [int]$selected[$index].page_count
            classes = @($selected[$index].classes)
            status = 'selected-rendered-review-required'
        })
    }

    $manifest = [pscustomobject]@{
        schemaVersion = '1.0'
        generatedAtUtc = [DateTime]::UtcNow.ToString('O')
        randomized = $true
        partOnly = $true
        selectedCount = 2
        samples = @($manifestSamples)
    }
    $manifestPath = Join-Path $output 'manifest.json'
    $utf8Bom = [Text.UTF8Encoding]::new($true)
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 8), $utf8Bom)
    $completed = $true

    # Do not print the private root, source paths, filenames or extracted text.  Slot labels are safe evidence keys.
    # 不打印秘密 root、源路径、文件名或提取文本；slot label 是安全的 evidence key。
    Write-Output 'private-drawing-sample status=selected-rendered'
    Write-Output 'selected=2'
    Write-Output 'sample-01=single-part-candidate review-required'
    Write-Output 'sample-02=single-part-candidate review-required'
    Write-Output 'source-content=redacted'
}
finally {
    if ($completed -and -not $KeepArtifacts) {
        # Successful sampling is disposable by default; -KeepArtifacts is an explicit local visual-review choice.
        # 抽样成功后默认清理；只有明确 -KeepArtifacts 才保留本地视觉复核材料。
        Remove-Item -LiteralPath $output -Recurse -Force -ErrorAction SilentlyContinue
    }
}
