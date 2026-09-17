[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$SolidWorksPath,
    [string]$Workspace = (Join-Path $env:LOCALAPPDATA 'SolidWorksMcp\test-workspace'),
    [string]$Solution = 'SolidWorksMcp.slnx',
    [string]$Configuration = 'Release',
    [string]$Filter,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    # Resolve the repository only after script scope initialization; use MyInvocation as a Windows PowerShell 5.1
    # fallback because that host can leave $PSScriptRoot empty during parameter binding.  Windows PowerShell 5.1
    # 在参数绑定阶段可能暂时没有 $PSScriptRoot，因此使用 MyInvocation 的脚本路径回退。
    $scriptPath = $MyInvocation.MyCommand.Path
    if ([string]::IsNullOrWhiteSpace($scriptPath)) {
        $scriptPath = $PSCommandPath
    }
    $RepositoryRoot = if ([string]::IsNullOrWhiteSpace($scriptPath)) {
        (Get-Location).Path
    }
    else {
        Split-Path -Parent (Split-Path -Parent $scriptPath)
    }
}

<##
.SYNOPSIS
    Closes old SOLIDWORKS sessions, starts exactly one fresh session and runs the opt-in Live tests.

.DESCRIPTION
    This is the supported local Live-test entry point.  It intentionally owns the full process lifecycle: every
    existing SLDWORKS process receives a graceful close request before a new process is started, and the exact new PID
    is passed to the test assembly.  A non-responding process or an unclosed modal dialog blocks the run; the script
    never calls Stop-Process or sends blind keyboard input.

    这是本地 Live 测试的标准入口。它显式拥有完整进程生命周期：启动新进程前，先对已有 SLDWORKS 逐个请求正常
    退出，再把新进程的精确 PID 传给测试程序集。无响应进程或未关闭的模态对话框会阻断运行；脚本绝不 Stop-Process，
    也不发送 blind keyboard input。
##>

function Get-RunningSolidWorks {
    @(
        Get-Process -Name SLDWORKS -ErrorAction SilentlyContinue |
            Sort-Object Id
    )
}

function Close-SolidWorksGracefully {
    param([Parameter(Mandatory)][System.Diagnostics.Process]$Process)

    $Process.Refresh()
    if ($Process.HasExited) {
        return
    }

    $pidText = $Process.Id.ToString([Globalization.CultureInfo]::InvariantCulture)
    if (-not $Process.Responding) {
        throw "BLOCKED_HUMAN_ACTION_REQUIRED: SLDWORKS PID $pidText is not responding; no blind termination was attempted."
    }

    if (-not $Process.CloseMainWindow()) {
        throw "BLOCKED_HUMAN_ACTION_REQUIRED: SLDWORKS PID $pidText has no closable main window; no blind termination was attempted."
    }

    if (-not $Process.WaitForExit(45000)) {
        throw "BLOCKED_HUMAN_ACTION_REQUIRED: SLDWORKS PID $pidText did not exit after a graceful close request; inspect the visible dialog and close it manually."
    }
}

function Resolve-SolidWorksExecutable {
    param([string]$Candidate)

    if ($Candidate) {
        $resolved = Resolve-Path -LiteralPath $Candidate -ErrorAction Stop
        if ((Get-Item -LiteralPath $resolved.Path).PSIsContainer) {
            $resolved = Get-ChildItem -LiteralPath $resolved.Path -Filter 'SLDWORKS.exe' -File -Recurse -ErrorAction Stop |
                Select-Object -First 1
        }
        if ($null -eq $resolved -or -not (Test-Path -LiteralPath $resolved.FullName -PathType Leaf)) {
            throw "SolidWorksPath did not resolve to SLDWORKS.exe."
        }
        return $resolved.FullName
    }

    $doctor = Join-Path $RepositoryRoot 'scripts\Invoke-SolidWorksMcpDoctor.ps1'
    $doctorResult = & $doctor -Json | ConvertFrom-Json
    $installation = @($doctorResult.installations |
            Where-Object { $_.status -eq 'complete' -and $_.executable } |
            Sort-Object version -Descending |
            Select-Object -First 1)
    if ($installation.Count -ne 1) {
        throw 'No complete SOLIDWORKS installation was discovered. Supply -SolidWorksPath explicitly.'
    }
    return [string]$installation[0].executable
}

$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot -ErrorAction Stop).Path
$solutionPath = if ([IO.Path]::IsPathRooted($Solution)) {
    (Resolve-Path -LiteralPath $Solution -ErrorAction Stop).Path
}
else {
    (Resolve-Path -LiteralPath (Join-Path $RepositoryRoot $Solution) -ErrorAction Stop).Path
}
$workspacePath = [IO.Path]::GetFullPath($Workspace)
New-Item -ItemType Directory -Force -Path $workspacePath | Out-Null

# The user explicitly requested one fresh SOLIDWORKS process per run.  Close only the concrete processes returned by
# Get-Process now; if one cannot close cleanly, stop before launching another process so sessions never accumulate.
# 用户明确要求每轮使用一个全新的 SOLIDWORKS。这里只处理当前 Get-Process 返回的具体进程；若无法正常关闭，
# 在启动新进程前直接阻断，避免 session 累积。
$oldProcesses = @(Get-RunningSolidWorks)
foreach ($oldProcess in $oldProcesses) {
    try {
        Close-SolidWorksGracefully -Process $oldProcess
    }
    finally {
        $oldProcess.Dispose()
    }
}

$executable = Resolve-SolidWorksExecutable -Candidate $SolidWorksPath
$started = Start-Process -FilePath $executable -WorkingDirectory (Split-Path -Parent $executable) -PassThru
$started.Refresh()
$startDeadline = [DateTime]::UtcNow.AddSeconds(120)
while (-not $started.HasExited -and [DateTime]::UtcNow -lt $startDeadline) {
    if ($started.Responding -and $started.MainWindowHandle -ne [IntPtr]::Zero) {
        break
    }
    Start-Sleep -Milliseconds 500
    $started.Refresh()
}
if ($started.HasExited) {
    throw "Fresh SOLIDWORKS process exited during startup (pid=$($started.Id))."
}

$env:SOLIDWORKS_MCP_LIVE_PROCESS_ID = $started.Id.ToString([Globalization.CultureInfo]::InvariantCulture)
$env:SOLIDWORKS_MCP_LIVE_WORKSPACE = $workspacePath
$env:SOLIDWORKS_MCP_LIVE_CLOSE_PROCESS = '1'
$exitCode = 1
try {
    Write-Output "live-process=started"
    Write-Output "live-tests=single-owned-process"

    if (-not $NoBuild) {
        & dotnet build $solutionPath -c $Configuration -v:minimal
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed with exit code $LASTEXITCODE."
        }
    }

    $testArgs = @('test', $solutionPath, '-c', $Configuration, '--no-build', '--logger', 'console;verbosity=minimal')
    if ($Filter) {
        $testArgs += @('--filter', $Filter)
    }
    & dotnet @testArgs
    $exitCode = $LASTEXITCODE
}
finally {
    # The xUnit collection fixture normally performs this exact close after all Live tests.  This fallback covers
    # build failures and test-discovery failures before the fixture is constructed.
    # xUnit collection fixture 通常会在所有 Live test 后关闭精确进程；这里的 fallback 覆盖 build/test discovery
    # 在 fixture 创建前失败的情况。
    $started.Refresh()
    if (-not $started.HasExited) {
        Close-SolidWorksGracefully -Process $started
    }
    $started.Dispose()
    Remove-Item Env:SOLIDWORKS_MCP_LIVE_PROCESS_ID -ErrorAction SilentlyContinue
    Remove-Item Env:SOLIDWORKS_MCP_LIVE_WORKSPACE -ErrorAction SilentlyContinue
    Remove-Item Env:SOLIDWORKS_MCP_LIVE_CLOSE_PROCESS -ErrorAction SilentlyContinue
}

exit $exitCode
