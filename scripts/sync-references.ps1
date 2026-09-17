[CmdletBinding()]
param(
    [string]$ManifestPath,
    [string]$ReferenceRoot,
    [switch]$PruneUnlisted
)

$ErrorActionPreference = 'Stop'

# Upstream repositories are kept as ignored working checkouts and copied as tracked, license-preserving snapshots.
# Upstream repositories remain in ignored working checkouts; tracked snapshots preserve source and licenses.
if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = Join-Path -Path $PSScriptRoot -ChildPath '..\references\manifest.json'
}
if ([string]::IsNullOrWhiteSpace($ReferenceRoot)) {
    $ReferenceRoot = Join-Path -Path $PSScriptRoot -ChildPath '..\references'
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path -Path $PSScriptRoot -ChildPath '..'))
$referenceRootFull = [IO.Path]::GetFullPath($ReferenceRoot)
$checkoutRoot = Join-Path $referenceRootFull '.checkouts'
$stagingRoot = Join-Path $referenceRootFull '.staging'

function Assert-UnderRoot {
    param([Parameter(Mandatory)][string]$Target, [Parameter(Mandatory)][string]$Root)

    $targetFull = [IO.Path]::GetFullPath($Target).TrimEnd('\') + '\'
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    if (-not $targetFull.StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing a reference path outside the configured reference root: $Target"
    }
}

function Invoke-Git {
    param([Parameter(Mandatory)][string[]]$Arguments)

    & git @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'Git is required to synchronize the reference corpus.'
}

$manifest = Get-Content -LiteralPath (Resolve-Path $ManifestPath) -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Path $referenceRootFull -Force | Out-Null
New-Item -ItemType Directory -Path $checkoutRoot -Force | Out-Null
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null

$expectedSnapshotPaths = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $manifest.repositories) {
    $name = Split-Path ([string]$entry.path) -Leaf
    $destination = Join-Path $referenceRootFull $name
    $checkout = Join-Path $checkoutRoot $name
    Assert-UnderRoot -Target $destination -Root $referenceRootFull
    Assert-UnderRoot -Target $checkout -Root $checkoutRoot
    [void]$expectedSnapshotPaths.Add([IO.Path]::GetFullPath($destination))

    # Migrate the old local-only clone layout once, without losing its verified Git metadata.
    # Migrate the legacy local-only clone layout once without losing verified Git metadata.
    $legacyGit = Join-Path $destination '.git'
    if (Test-Path -LiteralPath $legacyGit) {
        if (Test-Path -LiteralPath $checkout) {
            throw "Both legacy and working checkout paths exist for $name; resolve the ambiguity manually."
        }
        Move-Item -LiteralPath $destination -Destination $checkout
    }

    if (Test-Path -LiteralPath $checkout) {
        if (-not (Test-Path -LiteralPath (Join-Path $checkout '.git'))) {
            throw "Reference working checkout exists but is not a Git repository: $checkout"
        }

        $status = & git -C $checkout status --porcelain
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to inspect reference working checkout: $checkout"
        }
        if ($status) {
            throw "Reference working checkout has local changes; refusing to overwrite: $checkout"
        }

        $remote = (& git -C $checkout remote get-url origin).Trim()
        if ($remote -ne [string]$entry.cloneUrl) {
            throw "Remote mismatch for $checkout. Expected '$($entry.cloneUrl)', found '$remote'."
        }

        Invoke-Git @('-C', $checkout, 'fetch', '--prune', 'origin', [string]$entry.commit)
    }
    else {
        Invoke-Git @('clone', '--no-checkout', [string]$entry.cloneUrl, $checkout)
    }

    Invoke-Git @('-C', $checkout, 'checkout', '--detach', [string]$entry.commit)
    $actual = (& git -C $checkout rev-parse HEAD).Trim()
    if ($actual -ne [string]$entry.commit) {
        throw "Pinned commit verification failed for $($entry.name): expected $($entry.commit), found $actual."
    }

    # A parent-worktree change means the tracked snapshot may contain user edits; fail closed before replacement.
    # Fail closed if the parent repository already contains snapshot edits.
    $relativeSnapshot = [string]$entry.path
    $parentChanges = & git -C $repositoryRoot status --porcelain -- $relativeSnapshot
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to inspect tracked snapshot status for $relativeSnapshot."
    }
    if ($parentChanges) {
        throw "Tracked reference snapshot has local changes; commit or quarantine it before synchronization: $relativeSnapshot"
    }

    $staging = Join-Path $stagingRoot ("{0}-{1}" -f $name, [Guid]::NewGuid().ToString('N'))
    Assert-UnderRoot -Target $staging -Root $stagingRoot
    New-Item -ItemType Directory -Path $staging -Force | Out-Null
    try {
        # Copy source, documentation, manifests, and license files, while excluding nested Git metadata and generated output.
        # Copy source, documentation, manifests, and licenses while excluding nested Git metadata and generated output.
        $excludedDirectories = @('.git', 'bin', 'obj', 'node_modules', '.venv', 'dist', 'build', 'TestResults') |
            ForEach-Object {
                Get-ChildItem -LiteralPath $checkout -Recurse -Directory -Force -ErrorAction SilentlyContinue |
                    Where-Object Name -eq $_ |
                    Select-Object -ExpandProperty FullName
            }
        $robocopyArguments = @(
            $checkout,
            $staging,
            '/E',
            '/R:1',
            '/W:1',
            '/NFL',
            '/NDL',
            '/NJH',
            '/NJS',
            '/NP',
            '/XF',
            '*.dll',
            '*.exe',
            '*.pdb',
            '*.nupkg',
            '*.zip',
            '*.7z',
            '*.bin',
            '*.pt',
            '*.pth',
            '*.onnx',
            '*.safetensors',
            '*.user'
        )
        if ($excludedDirectories) {
            $robocopyArguments += '/XD'
            $robocopyArguments += $excludedDirectories
        }
        & robocopy @robocopyArguments | Out-Null
        if ($LASTEXITCODE -gt 7) {
            throw "robocopy failed while staging $name with exit code $LASTEXITCODE."
        }

        if (Test-Path -LiteralPath $destination) {
            Assert-UnderRoot -Target $destination -Root $referenceRootFull
            Remove-Item -LiteralPath $destination -Recurse -Force
        }
        Move-Item -LiteralPath $staging -Destination $destination
    }
    finally {
        if (Test-Path -LiteralPath $staging) {
            Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    Write-Host ("OK {0} {1} {2} tracked-snapshot" -f $entry.name, $actual, $entry.license)
}

if ($PruneUnlisted) {
    Get-ChildItem -LiteralPath $referenceRootFull -Directory |
        Where-Object { $_.Name -notin @('.checkouts', '.staging') } |
        ForEach-Object {
            if (-not $expectedSnapshotPaths.Contains($_.FullName)) {
                Write-Warning "Unlisted tracked reference snapshot retained (not deleted): $($_.FullName)"
            }
        }
}

Write-Host "Reference source snapshots synchronized from $ManifestPath."
