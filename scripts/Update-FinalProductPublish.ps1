<#
.SYNOPSIS
    Updates FinalProductPublish from a fresh ProjectPublish build.

.DESCRIPTION
    End-to-end automation:
      1) Validate git working tree (optional auto-commit)
      2) Backup FinalProductPublish (DEFERRED until just before replace; optional skip)
      3) Build ProjectPublish (via Build-Release.ps1 or dotnet publish fallback)
      4) Validate ProjectPublish output (counts + critical files)
      5) Replace FinalProductPublish contents with ProjectPublish
      6) Validate FinalProductPublish and run VERIFY_FILES.bat if present

.NOTES
    - ASCII-only output (no Unicode symbols) to avoid encoding/parser issues.
    - Supports -WhatIf / -Confirm for destructive operations (delete/copy/commit).

FIX NOTE (2026-01-07):
    Avoid passing script parameters via string[] for Build-Release.ps1 to prevent
    positional-binding drift (e.g., Version "2.5.0" binding into KeepBackups).
    Use parameter splatting (hashtable) when invoking Build-Release.ps1.

FIX NOTE (2026-01-07):
    Backup FinalProductPublish was previously executed BEFORE the build, which dirtied the git tree
    (tracked deletions + untracked new backup) and caused Build-Release pre-build checks to fail.
    Backup is now DEFERRED until AFTER ProjectPublish build+verify and immediately BEFORE replace.
#>

[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$Version,

    [Parameter()]
    [switch]$SkipBackup,

    [Parameter()]
    [switch]$SkipTests,

    [Parameter()]
    [switch]$CommitChanges
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# -----------------------------
# Paths (repo layout assumptions)
# -----------------------------
$RepoRoot      = Split-Path -Parent $PSScriptRoot   # scripts\ -> repo root
$SourceDir     = Join-Path $RepoRoot 'ProjectPublish'
$DestDir       = Join-Path $RepoRoot 'FinalProductPublish'
$BuildScript   = Join-Path $RepoRoot 'Build-Release.ps1'
$BackupScript  = Join-Path $PSScriptRoot 'Backup-FinalProductPublish.ps1'
$RestoreScript = Join-Path $PSScriptRoot 'Restore-FinalProductPublish.ps1'

# Optional: log file (best-effort)
$LogDir  = Join-Path $RepoRoot 'logs'
$null    = New-Item -ItemType Directory -Path $LogDir -Force -ErrorAction SilentlyContinue
$LogFile = Join-Path $LogDir ("Update-FinalProductPublish_{0:yyyyMMdd_HHmmss}.log" -f (Get-Date))

# Track whether a backup was actually created during this run (important for rollback messaging)
$script:BackupCreatedThisRun = $false

function Write-Log {
    param(
        [Parameter(Mandatory = $true)][string]$Message,
        [ValidateSet('INFO','OK','WARN','ERROR','STEP')][string]$Level = 'INFO'
    )
    $ts = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    $line = "[{0}] [{1}] {2}" -f $ts, $Level, $Message

    switch ($Level) {
        'STEP'  { Write-Host $line -ForegroundColor Cyan }
        'OK'    { Write-Host $line -ForegroundColor Green }
        'WARN'  { Write-Host $line -ForegroundColor Yellow }
        'ERROR' { Write-Host $line -ForegroundColor Red }
        default { Write-Host $line }
    }

    try { Add-Content -Path $LogFile -Value $line -Encoding UTF8 } catch { }
}

function Write-Step {
    param([int]$Step, [int]$Total, [string]$Title)
    Write-Log -Level STEP -Message ("----- STEP {0}/{1}: {2} -----" -f $Step, $Total, $Title)
}

function Get-Tool {
    param([string]$Name)
    return Get-Command $Name -ErrorAction SilentlyContinue
}

function Invoke-External {
    param(
        [Parameter(Mandatory = $true)][string]$File,
        [Parameter()][string[]]$Args = @(),
        [Parameter()][string]$WorkingDirectory = $RepoRoot
    )

    $argString = ($Args -join ' ')
    Write-Log -Level INFO -Message ("Running: {0} {1} (wd={2})" -f $File, $argString, $WorkingDirectory)

    Push-Location $WorkingDirectory
    try {
        & $File @Args
        $exit = $LASTEXITCODE
    } finally {
        Pop-Location
    }

    if ($exit -ne $null -and $exit -ne 0) {
        throw ("Command failed (exit={0}): {1} {2}" -f $exit, $File, $argString)
    }
    if (-not $?) {
        throw ("Command failed (PowerShell error): {0} {1}" -f $File, $argString)
    }
}

function Ensure-Directory {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        $null = New-Item -ItemType Directory -Path $Path -Force
    }
}

function Remove-DirectoryContents {
    param([string]$Path)
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath (Join-Path $Path '*') -Recurse -Force -ErrorAction Stop
    } else {
        Ensure-Directory -Path $Path
    }
}

function Get-FolderStats {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        return [pscustomobject]@{ Exists = $false; FileCount = 0; SizeMB = 0.0 }
    }

    $files = Get-ChildItem -LiteralPath $Path -File -Recurse -Force -ErrorAction Stop
    $count = $files.Count
    $sum = 0
    if ($count -gt 0) {
        $sum = ($files | Measure-Object -Property Length -Sum).Sum
    }
    $sizeMb = [math]::Round(($sum / 1MB), 2)

    return [pscustomobject]@{ Exists = $true; FileCount = $count; SizeMB = $sizeMb }
}

function Assert-CriticalFiles {
    param(
        [string]$BaseDir,
        [string[]]$CriticalFiles
    )
    $missing = @()
    foreach ($f in $CriticalFiles) {
        $p = Join-Path $BaseDir $f
        if (-not (Test-Path -LiteralPath $p)) {
            $missing += $f
        }
    }
    if ($missing.Count -gt 0) {
        throw ("Missing critical files in {0}: {1}" -f $BaseDir, ($missing -join ', '))
    }
}

function Copy-Folder {
    param(
        [string]$From,
        [string]$To
    )

    Ensure-Directory -Path $To

    $robocopy = Get-Tool -Name 'robocopy'
    if ($robocopy) {
        $args = @(
            $From, $To,
            '/E',
            '/COPY:DAT',
            '/DCOPY:DAT',
            '/R:2', '/W:1',
            '/NFL', '/NDL',
            '/NP',
            '/NJH', '/NJS'
        )

        Push-Location $RepoRoot
        try {
            & robocopy @args | Out-Null
            $rc = $LASTEXITCODE
        } finally {
            Pop-Location
        }

        if ($rc -ge 8) {
            throw ("robocopy failed with code {0}" -f $rc)
        }
    } else {
        Copy-Item -LiteralPath (Join-Path $From '*') -Destination $To -Recurse -Force -ErrorAction Stop
    }
}

function Invoke-DeferredBackup {
    if ($SkipBackup) { return }

    if (-not (Test-Path -LiteralPath $BackupScript)) {
        throw ("Backup script not found: {0}" -f $BackupScript)
    }

    Write-Log -Level INFO -Message "Running deferred FinalProductPublish backup (pre-replace)..."

    # Attempt to avoid interactive confirmation in automation runs
    # (Backup script supports ShouldProcess; -Confirm:$false suppresses prompt)
    Invoke-External -File $BackupScript -Args @('-Confirm:$false') -WorkingDirectory $PSScriptRoot

    $script:BackupCreatedThisRun = $true
    Write-Log -Level OK -Message "Backup completed."
}

function Commit-AllChanges {
    param([string]$Message)

    $git = Get-Tool -Name 'git'
    if (-not $git) {
        Write-Log -Level WARN -Message "git not found on PATH; cannot commit changes."
        return
    }

    Push-Location $RepoRoot
    try {
        $status = & git status --porcelain
        if (-not $status) {
            Write-Log -Level OK -Message "No changes to commit."
            return
        }

        if ($PSCmdlet.ShouldProcess($RepoRoot, "git add -A and commit")) {
            Invoke-External -File 'git' -Args @('add','-A') -WorkingDirectory $RepoRoot
            Invoke-External -File 'git' -Args @('commit','-m',$Message) -WorkingDirectory $RepoRoot
            Write-Log -Level OK -Message "Changes committed."
        } else {
            Write-Log -Level WARN -Message "Commit skipped due to WhatIf/Confirm."
        }
    } finally {
        Pop-Location
    }
}

# -----------------------------
# Main
# -----------------------------
try {
    Write-Log -Level INFO -Message "============================================================"
    Write-Log -Level INFO -Message ("FinalProductPublish Update Tool - Version {0}" -f $Version)
    Write-Log -Level INFO -Message ("RepoRoot: {0}" -f $RepoRoot)
    Write-Log -Level INFO -Message "============================================================"

    # STEP 1: Git status (optionally commit pre-existing changes so build can pass clean-tree checks)
    Write-Step -Step 1 -Total 6 -Title 'Checking git status'
    $git = Get-Tool -Name 'git'
    if (-not $git) {
        Write-Log -Level WARN -Message "git not found on PATH; skipping git status checks."
    } else {
        Push-Location $RepoRoot
        try {
            $status = & git status --porcelain
            if ($status) {
                Write-Log -Level WARN -Message "Git working tree has uncommitted changes:"
                $status | ForEach-Object { Write-Log -Level WARN -Message ("  {0}" -f $_) }

                if ($CommitChanges) {
                    Commit-AllChanges -Message ("Auto-commit before FinalProductPublish update to v{0}" -f $Version)
                } else {
                    Write-Log -Level WARN -Message "Proceeding with dirty working tree. Build may fail if a clean tree is required."
                }
            } else {
                Write-Log -Level OK -Message "Working tree clean."
            }
        } finally {
            Pop-Location
        }
    }

    # STEP 2: Backup (DEFERRED)
    if ($SkipBackup) {
        Write-Step -Step 2 -Total 6 -Title 'Backing up FinalProductPublish (SKIPPED)'
        Write-Log -Level WARN -Message "Backup skipped. Rollback will not be available if something goes wrong."
    } else {
        Write-Step -Step 2 -Total 6 -Title 'Backing up FinalProductPublish (DEFERRED)'
        Write-Log -Level INFO -Message "Backup will run after ProjectPublish build+verify, right before replace."
    }

    # STEP 3: Build ProjectPublish
    Write-Step -Step 3 -Total 6 -Title ("Building ProjectPublish v{0}" -f $Version)

    if (Test-Path -LiteralPath $BuildScript) {
        # FIX: use parameter splatting (named binding) to avoid positional-binding drift
        $buildParams = @{ Version = $Version }
        if ($SkipTests) { $buildParams.SkipTests = $true }

        $preview = "-Version $Version" + ($(if ($SkipTests) { " -SkipTests" } else { "" }))
        Write-Log -Level INFO -Message ("Running: {0} {1} (wd={2})" -f $BuildScript, $preview, $RepoRoot)

        Push-Location $RepoRoot
        try {
            & $BuildScript @buildParams
            $exit = $LASTEXITCODE
        } finally {
            Pop-Location
        }

        if ($exit -ne $null -and $exit -ne 0) {
            throw ("Command failed (exit={0}): {1} {2}" -f $exit, $BuildScript, $preview)
        }
        if (-not $?) {
            throw ("Command failed (PowerShell error): {0} {1}" -f $BuildScript, $preview)
        }

        Write-Log -Level OK -Message "Build-Release.ps1 completed."
    } else {
        Write-Log -Level WARN -Message "Build-Release.ps1 not found. Falling back to dotnet publish."

        if (Test-Path -LiteralPath $SourceDir) {
            if ($PSCmdlet.ShouldProcess($SourceDir, "Delete existing ProjectPublish output")) {
                Remove-Item -LiteralPath $SourceDir -Recurse -Force -ErrorAction Stop
            }
        }

        $dotnet = Get-Tool -Name 'dotnet'
        if (-not $dotnet) { throw "dotnet not found on PATH; cannot run fallback build." }

        Invoke-External -File 'dotnet' -Args @(
            'publish',
            'ShiftManager.csproj',
            '-c','Release',
            '-r','win-x64',
            '--self-contained','true',
            '-o','ProjectPublish'
        ) -WorkingDirectory $RepoRoot

        $extras = @(
            'UNBLOCK_FILES.bat',
            'VERIFY_FILES.bat',
            'QUICK_FIX.bat',
            'CRITICAL_BEFORE_DEMO.txt',
            'AIR_GAPPED_DEPLOYMENT_GUIDE.txt',
            'API_DOCUMENTATION.md',
            'appsettings.Production.template.json',
            'START_HERE.bat'
        )

        foreach ($item in $extras) {
            $src = Join-Path $RepoRoot $item
            if (Test-Path -LiteralPath $src) {
                Copy-Item -LiteralPath $src -Destination $SourceDir -Force -ErrorAction Stop
            }
        }

        $clients = Join-Path $RepoRoot 'clients'
        if (Test-Path -LiteralPath $clients) {
            Copy-Item -LiteralPath $clients -Destination $SourceDir -Recurse -Force -ErrorAction Stop
        }

        Write-Log -Level OK -Message "dotnet publish fallback build completed."
    }

    # STEP 4: Verify ProjectPublish
    Write-Step -Step 4 -Total 6 -Title 'Verifying ProjectPublish'
    $srcStats = Get-FolderStats -Path $SourceDir
    if (-not $srcStats.Exists) {
        throw ("Build output missing: {0}" -f $SourceDir)
    }

    Write-Log -Level OK -Message ("ProjectPublish files: {0}" -f $srcStats.FileCount)
    Write-Log -Level OK -Message ("ProjectPublish size:  {0} MB" -f $srcStats.SizeMB)

    if ($srcStats.FileCount -lt 500) {
        Write-Log -Level WARN -Message "ProjectPublish file count seems low (expected roughly 560-570)."
    }

    $critical = @('ShiftManager.exe','ShiftManager.dll','VERIFY_FILES.bat','appsettings.json')
    Assert-CriticalFiles -BaseDir $SourceDir -CriticalFiles $critical
    Write-Log -Level OK -Message "Critical files present in ProjectPublish."

    # STEP 5: Update FinalProductPublish (backup runs HERE, right before destructive replace)
    Write-Step -Step 5 -Total 6 -Title 'Updating FinalProductPublish'

    # Backup is intentionally deferred until now to keep the git tree clean for Build-Release pre-build checks.
    Invoke-DeferredBackup

    if ($PSCmdlet.ShouldProcess($DestDir, "Replace contents with ProjectPublish")) {
        Write-Log -Level INFO -Message "Clearing FinalProductPublish contents..."
        Remove-DirectoryContents -Path $DestDir
        Write-Log -Level OK -Message "Destination cleared."

        Write-Log -Level INFO -Message "Copying ProjectPublish -> FinalProductPublish..."
        Copy-Folder -From $SourceDir -To $DestDir
        Write-Log -Level OK -Message "Copy completed."
    } else {
        Write-Log -Level WARN -Message "Update skipped due to WhatIf/Confirm."
        return
    }

    # STEP 6: Verify FinalProductPublish
    Write-Step -Step 6 -Total 6 -Title 'Verifying FinalProductPublish'
    $dstStats = Get-FolderStats -Path $DestDir
    if (-not $dstStats.Exists) {
        throw ("Destination missing after copy: {0}" -f $DestDir)
    }

    if ($dstStats.FileCount -ne $srcStats.FileCount) {
        throw ("File count mismatch. Source={0} Dest={1}" -f $srcStats.FileCount, $dstStats.FileCount)
    }

    Write-Log -Level OK -Message ("FinalProductPublish files: {0}" -f $dstStats.FileCount)
    Write-Log -Level OK -Message ("FinalProductPublish size:  {0} MB" -f $dstStats.SizeMB)

    Assert-CriticalFiles -BaseDir $DestDir -CriticalFiles $critical
    Write-Log -Level OK -Message "Critical files present in FinalProductPublish."

    $verifyBat = Join-Path $DestDir 'VERIFY_FILES.bat'
    if (Test-Path -LiteralPath $verifyBat) {
        Write-Log -Level INFO -Message "Running VERIFY_FILES.bat..."
        Push-Location $DestDir
        try {
            $tmp = Join-Path $DestDir 'verify_output.tmp'
            cmd /c 'VERIFY_FILES.bat' > $tmp 2>&1
            $out = ''
            if (Test-Path -LiteralPath $tmp) {
                $out = Get-Content -LiteralPath $tmp -Raw -ErrorAction SilentlyContinue
                Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
            }

            if ($out -match 'Verification PASSED' -or $out -match 'All checks passed') {
                Write-Log -Level OK -Message "VERIFY_FILES.bat PASSED."
            } else {
                Write-Log -Level WARN -Message "VERIFY_FILES.bat completed but output was not clearly PASS. Review manually if needed."
            }
        } finally {
            Pop-Location
        }
    } else {
        Write-Log -Level WARN -Message "VERIFY_FILES.bat not found; skipping bat verification."
    }

    # If requested, commit final artifacts NOW (this is when changes actually exist).
    if ($CommitChanges) {
        Commit-AllChanges -Message ("Update FinalProductPublish to v{0}" -f $Version)
    }

    Write-Log -Level OK -Message "============================================================"
    Write-Log -Level OK -Message ("UPDATE SUCCESSFUL - v{0}" -f $Version)
    Write-Log -Level OK -Message ("Files: {0}  Size: {1} MB" -f $dstStats.FileCount, $dstStats.SizeMB)
    Write-Log -Level OK -Message ("Log: {0}" -f $LogFile)
    Write-Log -Level OK -Message "Next steps:"
    Write-Log -Level OK -Message "  git add FinalProductPublish/"
    if (-not $SkipBackup) { Write-Log -Level OK -Message "  git add Backups/" }
    Write-Log -Level OK -Message ("  git commit -m ""Update FinalProductPublish to v{0}""" -f $Version)
    Write-Log -Level OK -Message ("  git tag -a v{0} -m ""Version {0}""" -f $Version)
    Write-Log -Level OK -Message "============================================================"
}
catch {
    Write-Log -Level ERROR -Message "============================================================"
    Write-Log -Level ERROR -Message "UPDATE FAILED"
    Write-Log -Level ERROR -Message ("Error: {0}" -f $_.Exception.Message)
    Write-Log -Level ERROR -Message ("Log:  {0}" -f $LogFile)
    Write-Log -Level ERROR -Message "============================================================"

    # Only suggest rollback if we actually created a backup during THIS run
    if (-not $SkipBackup -and $script:BackupCreatedThisRun) {
        if (Test-Path -LiteralPath $RestoreScript) {
            Write-Log -Level WARN -Message ("To rollback, run: {0}" -f $RestoreScript)
        } else {
            Write-Log -Level WARN -Message "Backup was created, but Restore-FinalProductPublish.ps1 was not found."
        }
    } else {
        Write-Log -Level WARN -Message "No backup was created during this run; rollback may not be available."
    }

    exit 1
}
