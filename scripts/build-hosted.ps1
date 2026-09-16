[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipFormat
)

$ErrorActionPreference = 'Stop'
# The hosted solution deliberately excludes vendor-dependent provider/live projects.
# Hosted solution 故意排除依赖厂商安装的 Provider/Live 项目。
$repositoryRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$solution = Join-Path $repositoryRoot 'SolidWorksMcp.hosted.slnx'

function Invoke-Step {
    param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][scriptblock]$Action)

    # Stop on the first non-zero exit code so CI cannot hide a failed stage.
    # 任一步骤返回非零码立即停止，避免 CI 掩盖失败阶段。
    Write-Host "==> $Name"
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

Invoke-Step 'restore hosted solution' { dotnet restore $solution }

if (-not $SkipFormat) {
    Invoke-Step 'verify formatting' { dotnet format $solution --no-restore --verify-no-changes --severity info }
}

Invoke-Step 'build hosted solution' { dotnet build $solution --configuration $Configuration --no-restore }
Invoke-Step 'run hosted tests' { dotnet test $solution --configuration $Configuration --no-build --logger "trx;LogFileName=hosted.trx" }
