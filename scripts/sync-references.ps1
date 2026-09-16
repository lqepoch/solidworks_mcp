[CmdletBinding()]
param(
    [string]$ManifestPath,
    [string]$ReferenceRoot,
    [switch]$PruneUnlisted
)

$ErrorActionPreference = 'Stop'

# This script only updates the ignored local research corpus; product source is never copied into it.
# 此脚本只更新被忽略的本地研究仓库，不把产品源码复制到参考仓库中。

if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = Join-Path $PSScriptRoot '..\references\manifest.json'
}
if ([string]::IsNullOrWhiteSpace($ReferenceRoot)) {
    $ReferenceRoot = Join-Path $PSScriptRoot '..\references'
}

function Invoke-Git {
    param([Parameter(Mandatory)][string[]]$Arguments)

    & git @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'Git is required to synchronize the local reference corpus.'
}

$manifest = Get-Content -LiteralPath (Resolve-Path $ManifestPath) -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Path $ReferenceRoot -Force | Out-Null

$expectedPaths = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $manifest.repositories) {
    $relativePath = [string]$entry.path
    $destination = Join-Path (Resolve-Path $ReferenceRoot) (Split-Path $relativePath -Leaf)
    [void]$expectedPaths.Add([IO.Path]::GetFullPath($destination))

    if (Test-Path -LiteralPath $destination) {
        if (-not (Test-Path -LiteralPath (Join-Path $destination '.git'))) {
            throw "Reference destination exists but is not a Git repository: $destination"
        }

        $status = & git -C $destination status --porcelain
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to inspect reference repository: $destination"
        }
        if ($status) {
            throw "Reference repository has local changes; refusing to overwrite: $destination"
        }

        $remote = (& git -C $destination remote get-url origin).Trim()
        if ($remote -ne [string]$entry.cloneUrl) {
            throw "Remote mismatch for $destination. Expected '$($entry.cloneUrl)', found '$remote'."
        }

        Invoke-Git @('-C', $destination, 'fetch', '--prune', 'origin', [string]$entry.commit)
    }
    else {
        Invoke-Git @('clone', '--no-checkout', [string]$entry.cloneUrl, $destination)
    }

    Invoke-Git @('-C', $destination, 'checkout', '--detach', [string]$entry.commit)
    $actual = (& git -C $destination rev-parse HEAD).Trim()
    if ($actual -ne [string]$entry.commit) {
        throw "Pinned commit verification failed for $($entry.name): expected $($entry.commit), found $actual."
    }

    Write-Host ("OK {0} {1} {2}" -f $entry.name, $actual, $entry.license)
}

if ($PruneUnlisted) {
    Get-ChildItem -LiteralPath $ReferenceRoot -Directory | Where-Object { $_.Name -notin @('.git') } | ForEach-Object {
        if (-not $expectedPaths.Contains($_.FullName)) {
            Write-Warning "Unlisted reference checkout retained (not deleted): $($_.FullName)"
        }
    }
}

Write-Host "Reference corpus synchronized from $ManifestPath."
