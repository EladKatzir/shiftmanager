<#
.SYNOPSIS
    Automated Release Builder for ShiftManager

.DESCRIPTION
    Builds a production-ready ProjectPublish folder.

    Pipeline (best-effort; steps run if scripts exist):
      1) Pre-build checks (build\Test-PreBuild.ps1)
      2) Backup existing ProjectPublish (retention policy)
      3) dotnet publish -> ProjectPublish
      4) Copy mandatory air-gapped deployment assets
      5) Optional verify/test/docs/package/report steps (if scripts exist)

.PARAMETER Version
    Version number (e.g., "1.0.2"). If not provided, prompts.

.PARAMETER SkipTests
    Skip test-related stages (also passed into Test-PreBuild.ps1).

.PARAMETER NoPush
    Passed to build\Git-Integration.ps1 if present.

.PARAMETER SkipGit
    Skip git integration stage (tag/push).

.PARAMETER SkipPackaging
    Skip packaging stage (zip/package).

.PARAMETER SkipReport
    Skip release report stage.

.NOTES
    - ASCII-only output to avoid encoding/parser issues.
#>

[CmdletBinding()]
param(
    [Parameter()]
    [string]$Version,

    [Parameter()]
    [switch]$SkipTests,

    [Parameter()]
    [switch]$NoPush,

    [Parameter()]
    [switch]$SkipGit,

    [Parameter()]
    [switch]$SkipPackaging,

    [Parameter()]
    [switch]$SkipReport,

    [Parameter()]
    [ValidateRange(1, 25)]
    [int]$KeepBackups = 3
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# -----------------------------
# Paths
# -----------------------------
$ScriptRoot   = $PSScriptRoot
$BuildRoot    = Join-Path $ScriptRoot 'build'
$ProjectFile  = Join-Path $ScriptRoot 'ShiftManager.csproj'
$SolutionFile = Join-Path $ScriptRoot 'ShiftManager.sln'
$OutputFolder = Join-Path $ScriptRoot 'ProjectPublish'

# Logging (best-effort)
$LogDir = Join-Path $ScriptRoot 'logs'
$null = New-Item -ItemType Directory -Path $LogDir -Force -ErrorAction SilentlyContinue
$LogFile = Join-Path $LogDir ("Build-Release_{0:yyyyMMdd_HHmmss}.log" -f (Get-Date))

$script:StartTime = Get-Date

function Write-Log {
    param(
        [Parameter(Mandatory=$true)][string]$Message,
        [ValidateSet('INFO','OK','WARN','ERROR','STAGE')][string]$Level = 'INFO'
    )
    $ts = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    $line = "[{0}] [{1}] {2}" -f $ts, $Level, $Message

    switch ($Level) {
        'STAGE' { Write-Host $line -ForegroundColor Cyan }
        'OK'    { Write-Host $line -ForegroundColor Green }
        'WARN'  { Write-Host $line -ForegroundColor Yellow }
        'ERROR' { Write-Host $line -ForegroundColor Red }
        default { Write-Host $line }
    }

    try { Add-Content -Path $LogFile -Value $line -Encoding UTF8 } catch { }
}

function Require-File {
    param([string]$Path, [string]$Label)
    if (-not (Test-Path -LiteralPath $Path)) {
        throw ("Missing {0}: {1}" -f $Label, $Path)
    }
}

function Get-Tool {
    param([string]$Name)
    Get-Command $Name -ErrorAction SilentlyContinue
}

function Invoke-StepScript {
    param(
        [Parameter(Mandatory=$true)][string]$Path,
        [Parameter()][hashtable]$Params = @{},
        [Parameter()][string]$WorkingDirectory = $ScriptRoot,
        [Parameter()][switch]$Optional,
        [Parameter()][switch]$CaptureOutput
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        if ($Optional) {
            Write-Log -Level WARN -Message ("Step script not found (skipping): {0}" -f $Path)
            return $null
        }
        throw ("Step script not found: {0}" -f $Path)
    }

    $argPreview = if ($Params.Count -gt 0) {
        ($Params.Keys | Sort-Object | ForEach-Object { "-{0} {1}" -f $_, $Params[$_] }) -join ' '
    } else { '' }

    Write-Log -Level INFO -Message ("Running: {0} {1} (wd={2})" -f $Path, $argPreview, $WorkingDirectory)

    Push-Location $WorkingDirectory
    try {
        if ($CaptureOutput) {
            $result = & $Path @Params
        } else {
            & $Path @Params
            $result = $null
        }
        $exit = $LASTEXITCODE
    } finally {
        Pop-Location
    }

    if ($exit -ne $null -and $exit -ne 0) {
        throw ("Step failed (exit={0}): {1}" -f $exit, $Path)
    }
    if (-not $?) {
        throw ("Step failed (PowerShell error): {0}" -f $Path)
    }

    return $result
}

function Copy-Folder {
    param([Parameter(Mandatory=$true)][string]$From,
          [Parameter(Mandatory=$true)][string]$To)

    if (-not (Test-Path -LiteralPath $From)) {
        throw ("Source folder missing: {0}" -f $From)
    }
    if (-not (Test-Path -LiteralPath $To)) {
        $null = New-Item -ItemType Directory -Path $To -Force
    }

    $robocopy = Get-Tool 'robocopy'
    if ($robocopy) {
        # robocopy: 0-7 success, 8+ failure
        $args = @(
            $From, $To,
            '/MIR',
            '/COPY:DAT', '/DCOPY:DAT',
            '/R:2', '/W:1',
            '/NFL', '/NDL', '/NP', '/NJH', '/NJS'
        )
        & robocopy @args | Out-Null
        $rc = $LASTEXITCODE
        if ($rc -ge 8) { throw ("robocopy failed with code {0}" -f $rc) }
    } else {
        Copy-Item -LiteralPath (Join-Path $From '*') -Destination $To -Recurse -Force
    }
}

function Get-FolderStats {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        return [pscustomobject]@{ Exists = $false; FileCount = 0; SizeMB = 0.0 }
    }
    $files = Get-ChildItem -LiteralPath $Path -File -Recurse -Force
    $count = $files.Count
    $sum = if ($count -gt 0) { ($files | Measure-Object Length -Sum).Sum } else { 0 }
    [pscustomobject]@{
        Exists    = $true
        FileCount = $count
        SizeMB    = [math]::Round(($sum / 1MB), 2)
    }
}

function Backup-ProjectPublish {
    if (-not (Test-Path -LiteralPath $OutputFolder)) {
        Write-Log -Level INFO -Message "No existing ProjectPublish to backup."
        return $null
    }

    $timestamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $backupPath = Join-Path $ScriptRoot ("ProjectPublish_BACKUP_{0}" -f $timestamp)

    Write-Log -Level INFO -Message ("Backing up ProjectPublish -> {0}" -f $backupPath)
    Copy-Folder -From $OutputFolder -To $backupPath
    Write-Log -Level OK -Message "Backup created."

    # Retention
    $all = Get-ChildItem -LiteralPath $ScriptRoot -Directory -Filter 'ProjectPublish_BACKUP_*' |
        Sort-Object Name -Descending
    $old = $all | Select-Object -Skip $KeepBackups
    if ($old) {
        Write-Log -Level INFO -Message ("Removing {0} old backup(s) (keep last {1})..." -f $old.Count, $KeepBackups)
        foreach ($d in $old) {
            try { Remove-Item -LiteralPath $d.FullName -Recurse -Force } catch { }
        }
        Write-Log -Level OK -Message "Retention cleanup done."
    }

    return $backupPath
}

function Restore-FromBackup {
    param([string]$BackupPath)

    if (-not $BackupPath -or -not (Test-Path -LiteralPath $BackupPath)) {
        Write-Log -Level WARN -Message "No backup available for rollback."
        return
    }

    Write-Log -Level WARN -Message ("Rollback: restoring ProjectPublish from {0}" -f $BackupPath)

    if (Test-Path -LiteralPath $OutputFolder) {
        Remove-Item -LiteralPath $OutputFolder -Recurse -Force -ErrorAction SilentlyContinue
    }
    $null = New-Item -ItemType Directory -Path $OutputFolder -Force
    Copy-Folder -From $BackupPath -To $OutputFolder

    Write-Log -Level OK -Message "Rollback complete."
}

function Invoke-DotnetPublish {
    $dotnet = Get-Tool 'dotnet'
    if (-not $dotnet) { throw "dotnet not found on PATH." }

    if (Test-Path -LiteralPath $OutputFolder) {
        Remove-Item -LiteralPath $OutputFolder -Recurse -Force
    }

    Write-Log -Level INFO -Message "Running dotnet publish (Release, win-x64, self-contained)..."
    & dotnet publish $ProjectFile -c Release -r win-x64 --self-contained true -o $OutputFolder
    $exit = $LASTEXITCODE
    if ($exit -ne $null -and $exit -ne 0) {
        throw ("dotnet publish failed with exit code {0}" -f $exit)
    }
    Write-Log -Level OK -Message "dotnet publish completed."
}

function Copy-DeploymentAssets {
    Write-Log -Level INFO -Message "Copying required deployment assets into ProjectPublish..."

    $requiredScripts = @(
        'UNBLOCK_FILES.bat',
        'VERIFY_FILES.bat',
        'QUICK_FIX.bat',
        'CRITICAL_BEFORE_DEMO.txt',
        'AIR_GAPPED_DEPLOYMENT_GUIDE.txt'
    )

    $optionalScripts = @(
        'START_HERE.bat',
        'PRE_DEMO_CHECKLIST.txt',
        'TROUBLESHOOT_DEMO.txt'
    )

    $requiredAssets = @(
        'API_DOCUMENTATION.md',
        'appsettings.Production.template.json'
    )

    $missing = @()

    foreach ($f in $requiredScripts) {
        $src = Join-Path $ScriptRoot $f
        if (Test-Path -LiteralPath $src) {
            Copy-Item -LiteralPath $src -Destination $OutputFolder -Force
        } else {
            $missing += $f
        }
    }

    foreach ($f in $optionalScripts) {
        $src = Join-Path $ScriptRoot $f
        if (Test-Path -LiteralPath $src) {
            Copy-Item -LiteralPath $src -Destination $OutputFolder -Force
        }
    }

    foreach ($f in $requiredAssets) {
        $src = Join-Path $ScriptRoot $f
        if (Test-Path -LiteralPath $src) {
            Copy-Item -LiteralPath $src -Destination $OutputFolder -Force
        } else {
            $missing += $f
        }
    }

    $clients = Join-Path $ScriptRoot 'clients'
    if (Test-Path -LiteralPath $clients) {
        Copy-Item -LiteralPath $clients -Destination $OutputFolder -Recurse -Force
    } else {
        $missing += 'clients (directory)'
    }

    if ($missing.Count -gt 0) {
        throw ("Missing required deployment assets: {0}" -f ($missing -join ', '))
    }

    Write-Log -Level OK -Message "Deployment assets copied."
}

function Show-Summary {
    param([string]$Version, [object]$PackageInfo)

    $elapsed = (Get-Date) - $script:StartTime
    $stats = Get-FolderStats -Path $OutputFolder

    Write-Log -Level OK -Message "============================================================"
    Write-Log -Level OK -Message "BUILD SUCCESSFUL"
    Write-Log -Level OK -Message ("Version: {0}" -f $Version)
    Write-Log -Level OK -Message ("ProjectPublish: {0} files, {1} MB" -f $stats.FileCount, $stats.SizeMB)
    Write-Log -Level OK -Message ("Elapsed: {0}m {1}s" -f $elapsed.Minutes, $elapsed.Seconds)

    if ($PackageInfo) {
        try {
            if ($PackageInfo.Commit)     { Write-Log -Level OK -Message ("Git Commit: {0}" -f $PackageInfo.Commit) }
            if ($PackageInfo.ZipFile)    { Write-Log -Level OK -Message ("Package: {0}" -f $PackageInfo.ZipFile) }
            if ($PackageInfo.ReportFile) { Write-Log -Level OK -Message ("Report:  {0}" -f $PackageInfo.ReportFile) }
        } catch { }
    }

    Write-Log -Level OK -Message ("Log: {0}" -f $LogFile)
    Write-Log -Level OK -Message "============================================================"
}

# -----------------------------
# Entry
# -----------------------------
$backupMade = $null

try {
    Require-File -Path $ProjectFile -Label 'project file (ShiftManager.csproj)'

    if (-not $Version) {
        Write-Host ""
        Write-Host "Enter version number (e.g., 1.0.2): " -NoNewline -ForegroundColor Cyan
        $Version = Read-Host
        if (-not $Version) { throw "Version is required." }
    }

    if ($Version -notmatch '^\d+\.\d+\.\d+(-[\w\.]+)?$') {
        throw "Invalid version format. Use semantic versioning (e.g., 1.0.2 or 1.0.2-test)."
    }

    Write-Log -Level INFO -Message "============================================================"
    Write-Log -Level INFO -Message ("ShiftManager Build-Release - Version {0}" -f $Version)
    Write-Log -Level INFO -Message ("Root: {0}" -f $ScriptRoot)
    Write-Log -Level INFO -Message "============================================================"

    # STAGE 1: Pre-build checks (optional but recommended)
    Write-Log -Level STAGE -Message "STAGE 1/8: Pre-build checks"
    $pre = Join-Path $BuildRoot 'Test-PreBuild.ps1'
    Invoke-StepScript -Path $pre -Params @{
        Version    = $Version
        SkipTests  = [bool]$SkipTests
        # AllowDirtyGit not enabled by default
    } -WorkingDirectory $ScriptRoot -Optional

    # STAGE 2: Backup
    Write-Log -Level STAGE -Message "STAGE 2/8: Backup existing ProjectPublish"
    $backupMade = Backup-ProjectPublish

    # STAGE 3: Build (dotnet publish)
    Write-Log -Level STAGE -Message "STAGE 3/8: Build ProjectPublish (dotnet publish)"
    Invoke-DotnetPublish

    # STAGE 4: Copy required assets
    Write-Log -Level STAGE -Message "STAGE 4/8: Add deployment assets"
    Copy-DeploymentAssets

    # STAGE 4.5: Generate documentation
    Write-Log -Level STAGE -Message "STAGE 4.5/8: Generate documentation"
    $genDocs = Join-Path $BuildRoot 'Generate-Documentation.ps1'
    Invoke-StepScript -Path $genDocs -Params @{
        Version    = $Version
        OutputPath = $OutputFolder
    } -WorkingDirectory $ScriptRoot -Optional

    # STAGE 5: Verify build output (optional)
    Write-Log -Level STAGE -Message "STAGE 5/8: Verify build output"
    $verifyBuild = Join-Path $BuildRoot 'Verify-Build.ps1'
    Invoke-StepScript -Path $verifyBuild -Params @{ OutputPath = $OutputFolder } -WorkingDirectory $ScriptRoot -Optional

    # STAGE 6: Application testing (optional; gated by -SkipTests)
    Write-Log -Level STAGE -Message "STAGE 6/8: Application testing"
    if ($SkipTests) {
        Write-Log -Level WARN -Message "Skipping application testing (-SkipTests)."
    } else {
        $testApp = Join-Path $BuildRoot 'Test-Application.ps1'
        Invoke-StepScript -Path $testApp -Params @{ OutputPath = $OutputFolder } -WorkingDirectory $ScriptRoot -Optional
    }

    # STAGE 7: Integrity + git (optional)
    Write-Log -Level STAGE -Message "STAGE 7/8: Integrity + Git integration"
    $pkgIntegrity = Join-Path $BuildRoot 'Test-PackageIntegrity.ps1'
    Invoke-StepScript -Path $pkgIntegrity -Params @{ OutputPath = $OutputFolder } -WorkingDirectory $ScriptRoot -Optional

    if ($SkipGit) {
        Write-Log -Level WARN -Message "Skipping git integration (-SkipGit)."
    } else {
        $gitStep = Join-Path $BuildRoot 'Git-Integration.ps1'
        Invoke-StepScript -Path $gitStep -Params @{
            Version = $Version
            NoPush  = [bool]$NoPush
        } -WorkingDirectory $ScriptRoot -Optional
    }

    # STAGE 8: Packaging + report (optional)
    Write-Log -Level STAGE -Message "STAGE 8/8: Packaging + Report"
    $packageInfo = $null

    if ($SkipPackaging) {
        Write-Log -Level WARN -Message "Skipping packaging (-SkipPackaging)."
    } else {
        $pkg = Join-Path $BuildRoot 'Create-Package.ps1'
        $packageInfo = Invoke-StepScript -Path $pkg -Params @{
            Version    = $Version
            OutputPath = $OutputFolder
        } -WorkingDirectory $ScriptRoot -Optional -CaptureOutput
    }

    if ($SkipReport) {
        Write-Log -Level WARN -Message "Skipping release report (-SkipReport)."
    } else {
        $report = Join-Path $BuildRoot 'Generate-ReleaseReport.ps1'
        if ($packageInfo) {
            Invoke-StepScript -Path $report -Params @{ Version = $Version; PackageInfo = $packageInfo } -WorkingDirectory $ScriptRoot -Optional
        } else {
            Invoke-StepScript -Path $report -Params @{ Version = $Version } -WorkingDirectory $ScriptRoot -Optional
        }
    }

    Show-Summary -Version $Version -PackageInfo $packageInfo
    exit 0
}
catch {
    Write-Log -Level ERROR -Message "============================================================"
    Write-Log -Level ERROR -Message "BUILD FAILED"
    Write-Log -Level ERROR -Message ("Error: {0}" -f $_.Exception.Message)
    Write-Log -Level ERROR -Message ("Log:  {0}" -f $LogFile)
    Write-Log -Level ERROR -Message "============================================================"

    # rollback only if we created a backup this run
    try { Restore-FromBackup -BackupPath $backupMade } catch { }

    exit 1
}
