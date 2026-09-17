[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [int[]]$MajorVersion = @(2022, 2026),
    [string]$CacheRoot = (Join-Path $env:LOCALAPPDATA 'SolidWorksMcp\official-api'),
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'

<##
.SYNOPSIS
    Inventories and, only with -Apply, copies licensed SOLIDWORKS API material to a user-local cache.

.DESCRIPTION
    The repository must not redistribute SOLIDWORKS binaries or copyrighted vendor help. This script keeps the
    research corpus outside Git under %LOCALAPPDATA%, records exact provenance and SHA-256 values, and never turns a
    missing 2026 installation or unavailable web page into a false success.

    仓库不得重新分发 SOLIDWORKS 二进制文件或受版权保护的厂商帮助。本脚本只在 %LOCALAPPDATA% 下维护研究缓存，
    记录准确来源和 SHA-256，并且不会把缺少的 2026 安装或不可访问的网页伪装成成功。
##>

if ($WhatIfPreference) { $Apply = $true }

function Get-FileRecord {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Kind,
        [Parameter(Mandatory)][string]$LicenseReview
    )

    $file = Get-Item -LiteralPath $Path -ErrorAction Stop
    # Uri.MakeRelativeUri keeps this script compatible with Windows PowerShell 5.1, where Path.GetRelativePath is
    # unavailable. Uri.MakeRelativeUri 让脚本兼容 Windows PowerShell 5.1；该版本没有 Path.GetRelativePath。
    $rootUri = New-Object System.Uri(([IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'))
    $fileUri = New-Object System.Uri([IO.Path]::GetFullPath($file.FullName))
    $relative = [Uri]::UnescapeDataString($rootUri.MakeRelativeUri($fileUri).ToString())
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    [pscustomobject]@{
        kind = $Kind
        path = $relative.Replace('\', '/')
        bytes = $file.Length
        sha256 = $hash
        lastWriteTimeUtc = $file.LastWriteTimeUtc.ToString('O')
        licenseReview = $LicenseReview
    }
}

function Find-SolidWorksRoots {
    # Keep discovery aligned with the Windows doctor but return every installation for multi-version review.
    # 与 Windows doctor 保持相同的发现规则，但返回所有版本以支持多版本审查。
    $roots = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $registryPaths = @(
        'HKLM:\SOFTWARE\SolidWorks\Applications\SldWorks\*',
        'HKLM:\SOFTWARE\WOW6432Node\SolidWorks\Applications\SldWorks\*',
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
    )

    foreach ($registryPath in $registryPaths) {
        foreach ($item in @(Get-ChildItem -Path $registryPath -ErrorAction SilentlyContinue)) {
            $property = Get-ItemProperty -LiteralPath $item.PSPath -ErrorAction SilentlyContinue
            if ($null -eq $property) { continue }
            foreach ($candidate in @($property.InstallationDirectory, $property.InstallLocation, $property.SldWorksPath)) {
                if (-not $candidate -or $candidate -notmatch 'SolidWorks|SLDWORKS') { continue }
                if (Test-Path -LiteralPath $candidate -PathType Container) { [void]$roots.Add([IO.Path]::GetFullPath([string]$candidate)) }
            }
        }
    }

    foreach ($programFiles in @($env:ProgramFiles, [Environment]::GetEnvironmentVariable('ProgramFiles(x86)'))) {
        if (-not $programFiles) { continue }
        foreach ($vendor in @(Get-ChildItem -LiteralPath $programFiles -Directory -Filter 'SOLIDWORKS Corp*' -ErrorAction SilentlyContinue)) {
            foreach ($year in @(Get-ChildItem -LiteralPath $vendor.FullName -Directory -Filter 'SOLIDWORKS *' -ErrorAction SilentlyContinue)) {
                [void]$roots.Add($year.FullName)
            }
        }
    }

    $seenExecutables = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $results = foreach ($root in $roots) {
        $executable = Get-ChildItem -LiteralPath $root -Filter 'SLDWORKS.exe' -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -eq $executable) { continue }
        if (-not $seenExecutables.Add($executable.FullName)) { continue }
        $canonicalRoot = $executable.DirectoryName
        $apiRoot = Join-Path $canonicalRoot 'api'
        $productVersion = $executable.VersionInfo.ProductVersion
        $interopMajorVersion = $null
        try { $interopMajorVersion = ([version]$productVersion).Major } catch { }
        $releaseYear = $null
        if ($canonicalRoot -match '(?<!\d)(20\d{2})(?!\d)') {
            $releaseYear = [int]$Matches[1]
        } elseif ($interopMajorVersion -ge 30 -and $interopMajorVersion -le 60) {
            # SOLIDWORKS 2022 uses file major 30; retain this fallback for custom installation roots.
            # SOLIDWORKS 2022 的文件主版本是 30；对于自定义安装目录保留这个回退映射。
            $releaseYear = $interopMajorVersion + 1992
        }
        [pscustomobject]@{
            root = $canonicalRoot
            executable = $executable.FullName
            apiRoot = if (Test-Path -LiteralPath $apiRoot) { $apiRoot } else { $null }
            productVersion = $productVersion
            releaseYear = $releaseYear
            interopMajorVersion = $interopMajorVersion
            status = if (Test-Path -LiteralPath $apiRoot) { 'available' } else { 'missing-api-root' }
        }
    }
    @($results | Sort-Object @{Expression = { $_.releaseYear }; Descending = $true }, root)
}

function Get-WebResources {
    param([Parameter(Mandatory)][int]$Version)

    # The vendor site is case-sensitive for some binary help assets even when HTML routes are case-insensitive.
    # 厂商站点的 HTML 路由可能不区分大小写，但部分二进制帮助资源会区分大小写，因此统一使用官方小写路径。
    $base = "https://help.solidworks.com/$Version/english/api"
    @(
        [pscustomobject]@{ name = 'welcome'; uri = "$base/sldworksapiprogguide/Welcome.htm"; file = 'welcome.html' }
        [pscustomobject]@{ name = 'object-model-list'; uri = "$base/help_list.htm?id=1.3"; file = 'object-model-list.html' }
        [pscustomobject]@{ name = 'object-model-overview'; uri = "$base/sldworksapiprogguide/GettingStarted/SolidWorks_API_Object_Model_Overview.htm"; file = 'object-model-overview.html' }
        [pscustomobject]@{ name = 'context-sensitive-help'; uri = "$base/sldworksapiprogguide/gettingstarted/contextsensitivehelp.htm"; file = 'context-sensitive-help.html' }
        [pscustomobject]@{ name = 'functional-categories'; uri = "$base/sldworksapi/FunctionalCategories-sldworksapi.html"; file = 'functional-categories.html' }
        [pscustomobject]@{ name = 'release-notes'; uri = "$base/sldworksapi/ReleaseNotes-sldworksapi.html"; file = 'release-notes.html' }
        # The official site currently serves the PDF through the print-format query; the bare URL may return 404.
        # 官方站点当前通过 print-format query 提供 PDF；不带 query 的 URL 可能返回 404。
        [pscustomobject]@{ name = 'object-model-pdf'; uri = "$base/sldworksapi/SWObjectModel.pdf?format=P&value="; file = 'SWObjectModel.pdf' }
    )
}

if (-not (Test-Path -LiteralPath $CacheRoot)) {
    if ($Apply) { New-Item -ItemType Directory -Path $CacheRoot -Force | Out-Null }
}

$installations = @(Find-SolidWorksRoots)
$installationRecords = @()
$webRecords = @()
$requestedVersions = @($MajorVersion | Sort-Object -Unique)

foreach ($version in $requestedVersions) {
    $versionRoot = Join-Path $CacheRoot ([string]$version)
    $webRoot = Join-Path $versionRoot 'web'
    $installedRoot = Join-Path $versionRoot 'installed'
    if ($Apply) { New-Item -ItemType Directory -Force -Path $webRoot, $installedRoot | Out-Null }

    $matchingInstalls = @($installations | Where-Object { $_.releaseYear -eq $version })
    foreach ($installation in $matchingInstalls) {
        $target = Join-Path $installedRoot 'api'
        $source = $installation.apiRoot
        if (-not $source) {
            $installationRecords += [pscustomobject]@{ version = $version; status = 'missing-api-root'; source = $installation.root; target = $target }
            continue
        }
        if ($Apply) {
            # Copy the contents into the stable installed\api directory. Copying the api directory itself onto an
            # already existing target would create installed\api\api on the second run.
            # 复制目录内容到稳定的 installed\api；把 api 目录本身复制到已存在的目标会在第二次运行时形成
            # installed\api\api，因此这里明确使用通配内容复制。
            Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse -Force
        }
        $fileRecords = if (Test-Path -LiteralPath $target) {
            @(Get-ChildItem -LiteralPath $target -Recurse -File | ForEach-Object {
                    Get-FileRecord -Path $_.FullName -Root $installedRoot -Kind 'installed-api-material' -LicenseReview 'vendor-local-only'
                })
        } else { @() }
        $installationRecords += [pscustomobject]@{
            version = $version
            status = 'copied'
            source = $source
            target = $target
            productVersion = $installation.productVersion
            files = $fileRecords
        }
    }

    foreach ($resource in @(Get-WebResources -Version $version)) {
        $target = Join-Path $webRoot $resource.file
        $record = [ordered]@{
            name = $resource.name
            url = $resource.uri
            localPath = $target
            status = 'not-downloaded'
            bytes = $null
            sha256 = $null
            retrievedAtUtc = $null
            error = $null
            redistribution = 'link-only/vendor-local-only'
        }
        if ($Apply) {
            try {
                Invoke-WebRequest -Uri $resource.uri -OutFile $target -UseBasicParsing -ErrorAction Stop
                $file = Get-Item -LiteralPath $target
                $record.status = 'downloaded'
                $record.bytes = $file.Length
                $record.sha256 = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
                $record.retrievedAtUtc = (Get-Date).ToUniversalTime().ToString('O')
            } catch {
                if (Test-Path -LiteralPath $target) {
                    # Preserve and hash a previously downloaded official artifact, but distinguish it from a fresh fetch.
                    # 保留并校验之前下载的官方文件，但明确区分“已有缓存”和“本次新抓取”。
                    $file = Get-Item -LiteralPath $target
                    $record.status = 'previously-cached'
                    $record.bytes = $file.Length
                    $record.sha256 = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
                    $record.retrievedAtUtc = $file.LastWriteTimeUtc.ToUniversalTime().ToString('O')
                } else {
                    $record.status = 'missing-or-unavailable'
                }
                $record.error = $_.Exception.Message
            }
        }
        $webRecords += [pscustomobject]$record
    }
}

$manifest = [ordered]@{
    schemaVersion = '1.0'
    product = 'SOLIDWORKS API'
    requestedMajorVersions = $requestedVersions
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString('O')
    cacheRoot = $CacheRoot
    sourcePolicy = 'Licensed local installation and official help.solidworks.com only.'
    redistribution = 'Do not commit or redistribute vendor binaries, CHM/CAB/MSHA/HXS/PDF/full HTML mirrors without explicit vendor permission.'
    discoveredInstallations = $installations
    installationCopies = $installationRecords
    webResources = $webRecords
}

$manifestPath = Join-Path $CacheRoot 'manifest.json'
if ($Apply) { $manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $manifestPath -Encoding UTF8; Write-Output "manifest=$manifestPath" }
$manifest | ConvertTo-Json -Depth 3
