[CmdletBinding()]
param(
    [switch]$Initialize,
    [switch]$Json,
    [switch]$FailOnMissing,
    [string]$ServerPath,
    [string]$UserLocalRoot = (Join-Path $env:LOCALAPPDATA 'SolidWorksMcp')
)

$ErrorActionPreference = 'Stop'

function New-Check {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][ValidateSet('pass', 'missing', 'warning', 'info')][string]$Status,
        [Parameter(Mandatory)][string]$Detail,
        [string]$Path,
        [string]$Remediation
    )

    # A stable check record is shared by human and JSON output.
    # 人类可读输出与 JSON 输出共用同一稳定的检查记录结构。
    [pscustomobject]@{
        name = $Name
        status = $Status
        detail = $Detail
        path = $Path
        remediation = $Remediation
    }
}

function Get-CommandCheck {
    param([string]$Name, [string]$Remediation)

    # Command checks are non-mutating and work in both Windows PowerShell and PowerShell 7.
    # 命令检查不修改系统，并兼容 Windows PowerShell 与 PowerShell 7。
    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        return New-Check -Name $Name -Status missing -Detail 'Command was not found.' -Remediation $Remediation
    }
    return New-Check -Name $Name -Status pass -Detail $command.Source -Path $command.Source
}

function Get-SolidWorksInstallations {
    # Discover roots from registry metadata and standard Program Files locations.
    # 从注册表安装元数据和标准 Program Files 位置发现安装根目录。
    $rootMap = @{}
    $registryPaths = @(
        'HKLM:\SOFTWARE\SolidWorks\Applications\SldWorks\*',
        'HKLM:\SOFTWARE\WOW6432Node\SolidWorks\Applications\SldWorks\*',
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
    )

    foreach ($registryPath in $registryPaths) {
        foreach ($item in @(Get-ChildItem -Path $registryPath -ErrorAction SilentlyContinue)) {
            $property = Get-ItemProperty -LiteralPath $item.PSPath -ErrorAction SilentlyContinue
            if ($null -eq $property) {
                continue
            }

            # Read only known installation fields; never infer a path from an arbitrary registry value.
            # 只读取已知安装字段，不从任意注册表值猜测路径。
            foreach ($value in @($property.InstallationDirectory, $property.InstallLocation, $property.SldWorksPath)) {
                if (-not $value -or $value -notmatch 'SolidWorks|SLDWORKS') {
                    continue
                }
                $fullPath = [IO.Path]::GetFullPath([string]$value)
                if (Test-Path -LiteralPath $fullPath) {
                    $rootMap[$fullPath] = $fullPath
                }
            }
        }
    }

    $programFilesX86 = [Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
    foreach ($programFiles in @($env:ProgramFiles, $programFilesX86)) {
        if (-not $programFiles) {
            continue
        }
        foreach ($directory in @(Get-ChildItem -LiteralPath $programFiles -Directory -Filter 'SOLIDWORKS Corp*' -ErrorAction SilentlyContinue)) {
            foreach ($yearDirectory in @(Get-ChildItem -LiteralPath $directory.FullName -Directory -Filter 'SOLIDWORKS *' -ErrorAction SilentlyContinue)) {
                $fullPath = [IO.Path]::GetFullPath($yearDirectory.FullName)
                $rootMap[$fullPath] = $fullPath
            }
        }
    }

    $seenExecutables = @{}
    $installations = @()
    # Sort roots before probing so installation selection is stable across PowerShell/hash-table order changes.
    # 先排序安装根目录再探测，避免 PowerShell 哈希表顺序变化导致选择不稳定。
    foreach ($root in @($rootMap.Values | Sort-Object)) {
        Write-Verbose "Inspecting SOLIDWORKS root: $root"
        # Probe only the root and one child directory.  A full recursive scan of a SOLIDWORKS installation can
        # traverse thousands of files or a mounted library and make the doctor appear hung.
        # 只探测安装根目录及其一层子目录。完整递归扫描可能遍历成千上万个文件或挂载库，导致 doctor 假死。
        $executableCandidates = @()
        $directExecutable = Join-Path $root 'SLDWORKS.exe'
        if (Test-Path -LiteralPath $directExecutable -PathType Leaf) {
            $executableCandidates += Get-Item -LiteralPath $directExecutable -ErrorAction SilentlyContinue
        }
        if ($executableCandidates.Count -eq 0) {
            foreach ($childDirectory in @(Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue)) {
                $childExecutable = Join-Path $childDirectory.FullName 'SLDWORKS.exe'
                if (Test-Path -LiteralPath $childExecutable -PathType Leaf) {
                    $executableCandidates += Get-Item -LiteralPath $childExecutable -ErrorAction SilentlyContinue
                }
            }
        }
        if ($executableCandidates.Count -eq 0) {
            continue
        }
        $executable = $executableCandidates[0]
        if ($seenExecutables.ContainsKey($executable.FullName)) {
            continue
        }
        $seenExecutables[$executable.FullName] = $true
        Write-Verbose "Found SOLIDWORKS executable: $($executable.FullName)"

        # Use the executable directory as the canonical install root even when a registry value points to its parent.
        # 即使注册表值指向父目录，也使用可执行文件目录作为规范安装根目录。
        $canonicalRoot = $executable.DirectoryName
        $redistCandidates = @(
            (Join-Path $canonicalRoot 'api\redist'),
            (Join-Path $canonicalRoot 'API\redist')
        )
        $redist = [string](@($redistCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Container } | Select-Object -First 1))
        $interopPaths = @()
        foreach ($interopName in @('SolidWorks.Interop.sldworks.dll', 'SolidWorks.Interop.swconst.dll')) {
            $interopCandidates = @(
                (Join-Path $canonicalRoot $interopName),
                (Join-Path $canonicalRoot ("api\redist\" + $interopName)),
                (Join-Path $canonicalRoot ("API\redist\" + $interopName))
            )
            $interopFile = @($interopCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1)
            if ($interopFile) {
                $interopPaths += [string]$interopFile
            }
        }
        $typeLibraryCandidates = @(
            (Join-Path $canonicalRoot 'sldworks.tlb'),
            (Join-Path $canonicalRoot 'sldworks.olb'),
            (Join-Path $canonicalRoot 'api\redist\sldworks.tlb'),
            (Join-Path $canonicalRoot 'API\redist\sldworks.tlb')
        )
        $typeLibraryPaths = @($typeLibraryCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 3)
        $templateDirectories = @(
            (Join-Path $canonicalRoot 'data\templates'),
            (Join-Path $canonicalRoot 'lang\chinese\Tutorial'),
            (Join-Path $canonicalRoot 'lang\english\Tutorial')
        )
        $templatePaths = @(
            foreach ($templateDirectory in $templateDirectories) {
                if (Test-Path -LiteralPath $templateDirectory -PathType Container) {
                    Get-ChildItem -LiteralPath $templateDirectory -File -ErrorAction SilentlyContinue |
                        Where-Object { $_.Extension -in @('.prtdot', '.asmdot', '.drwdot') } |
                        ForEach-Object { $_.FullName }
                }
            }
        )

        # Partial installations remain visible and actionable instead of disappearing from diagnostics.
        # 不完整安装仍要出现在诊断中并列出缺失项，不能直接从结果中消失。
        $missing = @()
        if (-not $redist) { $missing += 'api\redist' }
        if ($interopPaths.Count -lt 2) { $missing += 'SolidWorks.Interop.sldworks.dll/SolidWorks.Interop.swconst.dll' }
        if ($typeLibraryPaths.Count -eq 0) { $missing += 'sldworks.tlb' }
        if ($templatePaths.Count -eq 0) { $missing += 'part/assembly/drawing templates' }

        $installations += [pscustomobject]@{
            root = $canonicalRoot
            executable = $executable.FullName
            version = $executable.VersionInfo.ProductVersion
            status = if ($missing.Count -eq 0) { 'complete' } else { 'partial' }
            missing = @($missing)
            apiRedist = if ($redist) { $redist } else { $null }
            interop = $interopPaths
            typeLibraries = $typeLibraryPaths
            templates = $templatePaths
        }
    }

    # Prefer the highest version, then executable path, for deterministic multi-version selection.
    # 多版本时优先最高版本，再按可执行文件路径排序，保证 selected 配置可重复。
    return @($installations | Sort-Object `
        @{ Expression = { try { [version]$_.version } catch { [version]'0.0' } }; Descending = $true }, `
        @{ Expression = { $_.executable }; Descending = $false })
}

$checks = @()
$checks += Get-CommandCheck -Name 'dotnet' -Remediation 'Install the .NET 10 SDK.'
$checks += Get-CommandCheck -Name 'git' -Remediation 'Install Git for Windows.'

$dotnetVersion = $null
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    $dotnetVersion = (& dotnet --version).Trim()
    $dotnetMajor = 0
    [void][int]::TryParse(($dotnetVersion -split '\.')[0], [ref]$dotnetMajor)
    $checks += New-Check -Name 'dotnet-sdk-10' -Status $(if ($dotnetMajor -ge 10) { 'pass' } else { 'missing' }) -Detail $dotnetVersion -Remediation 'Use the .NET 10 SDK required by this repository.'
}

$installations = @(Get-SolidWorksInstallations)
# A process can be alive while any UI/property query blocks (for example a modal COM or startup state).
# 进程存活时 UI/属性查询可能阻塞（例如 COM 模态框或启动阶段），因此 doctor 只读取 PID。
$processes = @(Get-Process -Name SLDWORKS -ErrorAction SilentlyContinue | ForEach-Object {
        $processId = $null
        try { $processId = [int]$_.Id } catch { }
        [pscustomobject]@{
            id = $processId
            path = $null
            processProbe = 'pid-only-safe-probe'
            hasExited = $null
            mainWindowHandle = $null
            gracefulCloseReady = $false
        }
    })
$checks += New-Check -Name 'solidworks-installation' -Status $(if ($installations.Count -gt 0) { 'pass' } else { 'warning' }) -Detail "$($installations.Count) installation(s) discovered." -Remediation 'Install or repair SOLIDWORKS, or run hosted-safe tests without the provider.'
# Live-test cleanup is fail-closed: an unclosable process is a warning with remediation, never an invitation to kill it.
# Live 测试清理必须 fail-closed：无法安全关闭的进程只产生告警和处置建议，绝不诱导强杀进程。
$unclosableSessions = @($processes | Where-Object { -not $_.gracefulCloseReady })
$sessionStatus = if ($processes.Count -eq 0) { 'info' } elseif ($unclosableSessions.Count -eq 0) { 'pass' } else { 'warning' }
$sessionDetail = if ($processes.Count -eq 0) {
    'No running SLDWORKS process was discovered.'
} elseif ($unclosableSessions.Count -eq 0) {
    "$($processes.Count) running SLDWORKS process(es); doctor used a PID-only safe probe; the bounded Live harness must perform the final UI/close check."
} else {
    "$($processes.Count) running SLDWORKS process(es); $($unclosableSessions.Count) is not safely closable by the bounded Live harness."
}
$sessionRemediation = if ($unclosableSessions.Count -gt 0) {
    'Close the affected SOLIDWORKS session through its normal UI or operator-approved graceful path before Live tests; do not force-terminate or blind-dismiss dialogs.'
} else { '' }
$checks += New-Check -Name 'solidworks-session' -Status $sessionStatus -Detail $sessionDetail -Remediation $sessionRemediation

$paths = [pscustomobject]@{
    userLocalRoot = $UserLocalRoot
    testWorkspace = Join-Path $UserLocalRoot 'test-workspace'
    evidence = Join-Path $UserLocalRoot 'evidence'
    output = Join-Path $UserLocalRoot 'output'
}

$initialization = $null
if ($Initialize) {
    # Initialization creates only user-local directories/configuration; it never changes global registry or settings.
    # 初始化只创建用户本地目录和配置，不修改全局注册表或系统设置。
    foreach ($path in @($paths.userLocalRoot, $paths.testWorkspace, $paths.evidence, $paths.output)) {
        New-Item -ItemType Directory -Path $path -Force | Out-Null
    }

    $selected = $installations | Select-Object -First 1
    # Keep the diagnostic inventory separate from the runtime configuration consumed by the MCP host.
    # 将诊断清单与 MCP Host 消费的运行时配置分离，避免把机器探测结果误当成业务配置。
    $doctorReportPath = Join-Path $UserLocalRoot 'doctor.json'
    [pscustomobject]@{
        schemaVersion = '1.0'
        generatedAt = (Get-Date).ToUniversalTime().ToString('O')
        repository = 'SolidWorksMcp'
        solidWorks = $selected
        paths = $paths
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $doctorReportPath -Encoding UTF8

    # Runtime defaults are safe and explicit; native activation remains a separate provider decision.
    # 运行时默认值安全且显式；是否启用原生 Provider 仍由独立的 Provider 配置决定。
    $configurationPath = Join-Path $UserLocalRoot 'config.json'
    [pscustomobject]@{
        schemaVersion = '1.0'
        providerMode = 'unavailable'
        features = [pscustomobject]@{
            experimentalDrawing = $false
            experimentalRecognition = $false
        }
        # Native CAD writes are limited to user-local output/test roots until an operator adds another explicit root.
        # Native CAD 写入默认只允许用户本地 output/test 根目录；操作者必须显式添加其它根目录。
        pathAllowlist = [pscustomobject]@{
            roots = @($paths.output, $paths.testWorkspace)
        }
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $configurationPath -Encoding UTF8

    $propsPath = Join-Path $UserLocalRoot 'SolidWorksMcp.local.props'
    $installRoot = if ($selected) { [Security.SecurityElement]::Escape([string]$selected.root) } else { '' }
    $redistRoot = if ($selected) { [Security.SecurityElement]::Escape([string]$selected.apiRedist) } else { '' }
    @"
<Project>
  <!-- Generated by Invoke-SolidWorksMcpDoctor.ps1; user-local and intentionally untracked. -->
  <!-- 由 doctor 生成，仅保存于用户目录，故意不纳入版本控制。 -->
  <PropertyGroup>
    <SolidWorksInstallRoot>$installRoot</SolidWorksInstallRoot>
    <SolidWorksApiRedist>$redistRoot</SolidWorksApiRedist>
    <SolidWorksMcpNativeProviderEnabled>$(if ($selected -and $selected.status -eq 'complete') { 'true' } else { 'false' })</SolidWorksMcpNativeProviderEnabled>
  </PropertyGroup>
</Project>
"@ | Set-Content -LiteralPath $propsPath -Encoding UTF8

    $resolvedServerPath = if ($ServerPath) { (Resolve-Path $ServerPath).Path } else { Join-Path (Get-Location) 'src\SolidWorksMcp.Server\bin\Release\net10.0-windows\SolidWorksMcp.Server.exe' }
    $mcpConfigPath = Join-Path $UserLocalRoot 'mcp.json'
    [pscustomobject]@{
        mcpServers = [pscustomobject]@{
            solidworks = [pscustomobject]@{
                command = $resolvedServerPath
                args = @()
                env = [pscustomobject]@{ SOLIDWORKS_MCP_CONFIG = $configurationPath }
            }
        }
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $mcpConfigPath -Encoding UTF8
    $initialization = [pscustomobject]@{ configuration = $configurationPath; doctorReport = $doctorReportPath; msbuild = $propsPath; mcp = $mcpConfigPath }
}

$result = [pscustomobject]@{
    schemaVersion = '1.0'
    generatedAt = (Get-Date).ToUniversalTime().ToString('O')
    repository = (Get-Location).Path
    dotnetVersion = $dotnetVersion
    checks = @($checks)
    installations = @($installations)
    sessions = @($processes)
    userLocalPaths = $paths
    initialized = $initialization
}

if ($Json) {
    $result | ConvertTo-Json -Depth 12
}
else {
    Write-Host 'SolidWorksMcp Windows doctor'
    Write-Host "Repository: $($result.repository)"
    Write-Host "User-local root: $($paths.userLocalRoot)"
    foreach ($check in $checks) {
        Write-Host ("[{0}] {1}: {2}" -f $check.status.ToUpperInvariant(), $check.name, $check.detail)
        if ($check.remediation -and $check.status -in @('missing', 'warning')) {
            Write-Host "       remediation: $($check.remediation)"
        }
    }
    foreach ($installation in $installations) {
        Write-Host "SOLIDWORKS $($installation.version): $($installation.executable)"
        Write-Host "  API redist: $($installation.apiRedist)"
        Write-Host "  Interop files: $($installation.interop -join ', ')"
        Write-Host "  Type libraries: $($installation.typeLibraries -join ', ')"
        Write-Host "  Templates discovered: $($installation.templates.Count)"
    }
    if ($initialization) {
        Write-Host "Generated user-local files: $($initialization | ConvertTo-Json -Compress)"
    }
}

if ($FailOnMissing -and @($checks | Where-Object status -eq 'missing').Count -gt 0) {
    exit 2
}
