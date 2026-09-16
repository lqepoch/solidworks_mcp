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
    foreach ($root in @($rootMap.Values)) {
        Write-Verbose "Inspecting SOLIDWORKS root: $root"
        $executable = Get-ChildItem -LiteralPath $root -Filter 'SLDWORKS.exe' -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -eq $executable) {
            continue
        }
        if ($seenExecutables.ContainsKey($executable.FullName)) {
            continue
        }
        $seenExecutables[$executable.FullName] = $true
        Write-Verbose "Found SOLIDWORKS executable: $($executable.FullName)"

        # Use the executable directory as the canonical install root even when a registry value points to its parent.
        # 即使注册表值指向父目录，也使用可执行文件目录作为规范安装根目录。
        $canonicalRoot = $executable.DirectoryName
        $redist = Get-ChildItem -LiteralPath $canonicalRoot -Directory -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -ieq 'redist' -and $_.FullName -match 'api' } |
            Select-Object -First 1
        $interopPaths = @()
        foreach ($interopName in @('SolidWorks.Interop.sldworks.dll', 'SolidWorks.Interop.swconst.dll')) {
            $interopFile = Get-ChildItem -LiteralPath $canonicalRoot -Filter $interopName -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($interopFile) {
                $interopPaths += $interopFile.FullName
            }
        }
        $typeLibraryPaths = @(Get-ChildItem -LiteralPath $canonicalRoot -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match 'sldworks.*\.(tlb|olb)$' } |
            Select-Object -First 3 |
            ForEach-Object { $_.FullName })
        $templatePaths = @(Get-ChildItem -LiteralPath $canonicalRoot -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.Extension -in @('.prtdot', '.asmdot', '.drwdot') } |
            Select-Object -First 20 |
            ForEach-Object { $_.FullName })

        $installations += [pscustomobject]@{
            root = $canonicalRoot
            executable = $executable.FullName
            version = $executable.VersionInfo.ProductVersion
            apiRedist = if ($redist) { $redist.FullName } else { $null }
            interop = $interopPaths
            typeLibraries = $typeLibraryPaths
            templates = $templatePaths
        }
    }

    return $installations
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
$processes = @(Get-Process -Name SLDWORKS -ErrorAction SilentlyContinue | ForEach-Object {
        $processPath = $null
        try { $processPath = $_.Path } catch { }
        [pscustomobject]@{ id = $_.Id; path = $processPath; responding = $_.Responding }
    })
$checks += New-Check -Name 'solidworks-installation' -Status $(if ($installations.Count -gt 0) { 'pass' } else { 'warning' }) -Detail "$($installations.Count) installation(s) discovered." -Remediation 'Install or repair SOLIDWORKS, or run hosted-safe tests without the provider.'
$checks += New-Check -Name 'solidworks-session' -Status $(if ($processes.Count -gt 0) { 'pass' } else { 'info' }) -Detail "$($processes.Count) running SLDWORKS process(es)."

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
  </PropertyGroup>
</Project>
"@ | Set-Content -LiteralPath $propsPath -Encoding UTF8

    $resolvedServerPath = if ($ServerPath) { (Resolve-Path $ServerPath).Path } else { Join-Path (Get-Location) 'src\SolidWorksMcp.Server\bin\Release\net10.0\SolidWorksMcp.Server.exe' }
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
