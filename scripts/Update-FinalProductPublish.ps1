<#
.SYNOPSIS
    Automated update of FinalProductPublish from ProjectPublish

.DESCRIPTION
    Full automation of the FinalProductPublish update process:
    1. Checks git status
    2. Creates backup
    3. Builds fresh ProjectPublish
    4. Updates FinalProductPublish
    5. Verifies integrity

.PARAMETER Version
    Version number for the build (e.g., "2.1.0")

.PARAMETER SkipBackup
    Skip backup step (not recommended)

.PARAMETER SkipTests
    Pass to Build-Release.ps1 to skip tests (faster but less safe)

.PARAMETER CommitChanges
    Auto-commit uncommitted git changes before building

.EXAMPLE
    .\Update-FinalProductPublish.ps1 -Version "2.1.0"

.EXAMPLE
    .\Update-FinalProductPublish.ps1 -Version "2.1.1" -SkipTests

.NOTES
    Author: Claude Code
    Version: 1.0
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$Version,

    [Parameter()]
    [switch]$SkipBackup,

    [Parameter()]
    [switch]$SkipTests,

    [Parameter()]
    [switch]$CommitChanges
)

$ErrorActionPreference = "Stop"

# Paths
$ScriptRoot = Split-Path -Parent $PSScriptRoot
$SourcePath = Join-Path $ScriptRoot "ProjectPublish"
$DestPath = Join-Path $ScriptRoot "FinalProductPublish"
$BuildScript = Join-Path $ScriptRoot "Build-Release.ps1"
$BackupScript = Join-Path $PSScriptRoot "Backup-FinalProductPublish.ps1"

# Colors
$ColorGreen = "Green"
$ColorYellow = "Yellow"
$ColorRed = "Red"
$ColorCyan = "Cyan"

function Write-Success { param([string]$Message) Write-Host "  ✓ " -ForegroundColor $ColorGreen -NoNewline; Write-Host $Message }
function Write-Info { param([string]$Message) Write-Host "  ⏳ " -ForegroundColor $ColorCyan -NoNewline; Write-Host $Message }
function Write-WarningMsg { param([string]$Message) Write-Host "  ⚠️  " -ForegroundColor $ColorYellow -NoNewline; Write-Host $Message }
function Write-ErrorMsg { param([string]$Message) Write-Host "  ❌ " -ForegroundColor $ColorRed -NoNewline; Write-Host $Message }

function Write-StepHeader {
    param([string]$Step, [string]$Total, [string]$Title)
    Write-Host ""
    Write-Host "[STEP $Step/$Total] $Title..." -ForegroundColor $ColorCyan
}

try {
    Write-Host ""
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor $ColorCyan
    Write-Host "  FinalProductPublish Update Tool" -ForegroundColor White
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor $ColorCyan
    Write-Host "  Version: $Version" -ForegroundColor Gray
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor $ColorCyan

    # Step 1: Check git status
    Write-StepHeader "1" "6" "Checking git status"
    Push-Location $ScriptRoot
    try {
        $gitStatus = git status --porcelain

        if ($gitStatus) {
            Write-WarningMsg "Git working tree has uncommitted changes"
            Write-Host "    Uncommitted files:" -ForegroundColor Gray
            $gitStatus | ForEach-Object { Write-Host "      $_" -ForegroundColor Gray }

            if ($CommitChanges) {
                Write-Info "Auto-committing changes..."
                git add -A
                git commit -m "Auto-commit before FinalProductPublish update to v$Version"
                Write-Success "Changes committed"
            } else {
                Write-WarningMsg "Build may fail if Build-Release.ps1 requires clean working tree"
                Write-Host "    Use -CommitChanges to auto-commit, or commit manually first" -ForegroundColor Gray
            }
        } else {
            Write-Success "Working tree clean"
        }
    } finally {
        Pop-Location
    }

    # Step 2: Backup
    if (-not $SkipBackup) {
        Write-StepHeader "2" "6" "Backing up FinalProductPublish"
        & $BackupScript
        if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne $null) {
            throw "Backup failed"
        }
    } else {
        Write-StepHeader "2" "6" "Backing up FinalProductPublish (SKIPPED)"
        Write-WarningMsg "Backup skipped - no rollback available if update fails!"
    }

    # Step 3: Build ProjectPublish
    Write-StepHeader "3" "6" "Building ProjectPublish v$Version"

    if (Test-Path $BuildScript) {
        Write-Info "Running Build-Release.ps1..."
        Push-Location $ScriptRoot
        try {
            if ($SkipTests) {
                & $BuildScript -Version $Version -SkipTests
            } else {
                & $BuildScript -Version $Version
            }

            if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne $null) {
                throw "Build-Release.ps1 failed with exit code $LASTEXITCODE"
            }
        } finally {
            Pop-Location
        }
    } else {
        # Fallback to manual build
        Write-WarningMsg "Build-Release.ps1 not found, using manual build..."
        Write-Info "Running: dotnet publish..."

        Push-Location $ScriptRoot
        try {
            if (Test-Path $SourcePath) {
                Remove-Item -Path $SourcePath -Recurse -Force
            }

            dotnet publish ShiftManager.csproj -c Release -r win-x64 --self-contained true -o ProjectPublish

            if ($LASTEXITCODE -ne 0) {
                throw "dotnet publish failed"
            }

            # Copy deployment scripts
            $deploymentScripts = @(
                "UNBLOCK_FILES.bat",
                "VERIFY_FILES.bat",
                "QUICK_FIX.bat",
                "CRITICAL_BEFORE_DEMO.txt",
                "AIR_GAPPED_DEPLOYMENT_GUIDE.txt",
                "API_DOCUMENTATION.md",
                "appsettings.Production.template.json"
            )

            foreach ($script in $deploymentScripts) {
                if (Test-Path $script) {
                    Copy-Item -Path $script -Destination $SourcePath -Force
                }
            }

            if (Test-Path "clients") {
                Copy-Item -Path "clients" -Destination $SourcePath -Recurse -Force
            }
        } finally {
            Pop-Location
        }
    }

    Write-Success "Build completed"

    # Step 4: Verify ProjectPublish
    Write-StepHeader "4" "6" "Verifying ProjectPublish"

    if (-not (Test-Path $SourcePath)) {
        Write-ErrorMsg "ProjectPublish folder not found!"
        throw "Build output missing"
    }

    $sourceFiles = Get-ChildItem -Path $SourcePath -File -Recurse
    $sourceCount = $sourceFiles.Count
    $sourceSize = [math]::Round(($sourceFiles | Measure-Object -Property Length -Sum).Sum / 1MB, 2)

    Write-Success "File count: $sourceCount files"
    Write-Success "Size: $sourceSize MB"

    if ($sourceCount -lt 500) {
        Write-WarningMsg "File count seems low (expected ~560-570)"
    }

    # Check critical files
    $criticalFiles = @("ShiftManager.exe", "ShiftManager.dll", "VERIFY_FILES.bat", "appsettings.json")
    $missingFiles = @()

    foreach ($file in $criticalFiles) {
        if (-not (Test-Path (Join-Path $SourcePath $file))) {
            $missingFiles += $file
        }
    }

    if ($missingFiles.Count -gt 0) {
        Write-ErrorMsg "Missing critical files: $($missingFiles -join ', ')"
        throw "Build verification failed"
    }

    Write-Success "Critical files present"

    # Step 5: Update FinalProductPublish
    Write-StepHeader "5" "6" "Updating FinalProductPublish"

    Write-Info "Clearing FinalProductPublish contents..."
    if (Test-Path $DestPath) {
        Remove-Item -Path "$DestPath\*" -Recurse -Force
    } else {
        New-Item -ItemType Directory -Path $DestPath -Force | Out-Null
    }
    Write-Success "Contents cleared"

    Write-Info "Copying ProjectPublish → FinalProductPublish (this may take 1-2 minutes)..."
    Copy-Item -Path "$SourcePath\*" -Destination $DestPath -Recurse -Force
    Write-Success "Files copied"

    # Step 6: Verify FinalProductPublish
    Write-StepHeader "6" "6" "Verifying FinalProductPublish"

    $destFiles = Get-ChildItem -Path $DestPath -File -Recurse
    $destCount = $destFiles.Count
    $destSize = [math]::Round(($destFiles | Measure-Object -Property Length -Sum).Sum / 1MB, 2)

    if ($destCount -ne $sourceCount) {
        Write-ErrorMsg "File count mismatch! Source: $sourceCount, Dest: $destCount"
        throw "Copy verification failed"
    }

    Write-Success "File count: $destCount files"
    Write-Success "Size: $destSize MB"

    # Run VERIFY_FILES.bat if available
    $verifyScript = Join-Path $DestPath "VERIFY_FILES.bat"
    if (Test-Path $verifyScript) {
        Write-Info "Running VERIFY_FILES.bat..."
        Push-Location $DestPath
        try {
            cmd /c VERIFY_FILES.bat > verify_output.tmp 2>&1
            $verifyOutput = Get-Content verify_output.tmp -Raw
            Remove-Item verify_output.tmp -ErrorAction SilentlyContinue

            if ($verifyOutput -match "Verification PASSED" -or $verifyOutput -match "All checks passed") {
                Write-Success "VERIFY_FILES.bat passed"
            } else {
                Write-WarningMsg "VERIFY_FILES.bat output unclear - manual verification recommended"
            }
        } finally {
            Pop-Location
        }
    } else {
        Write-WarningMsg "VERIFY_FILES.bat not found - skip automated verification"
    }

    # Success summary
    Write-Host ""
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor $ColorGreen
    Write-Host "  UPDATE SUCCESSFUL!" -ForegroundColor $ColorGreen
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor $ColorGreen
    Write-Host ""
    Write-Host "Version:          v$Version" -ForegroundColor Gray
    Write-Host "Files:            $destCount" -ForegroundColor Gray
    Write-Host "Size:             $destSize MB" -ForegroundColor Gray
    if (-not $SkipBackup) {
        $latestBackup = Get-ChildItem -Path (Join-Path $ScriptRoot "Backups\FinalProductPublish") -Directory -Filter "FinalProductPublish_BACKUP_*" |
            Sort-Object Name -Descending |
            Select-Object -First 1
        if ($latestBackup) {
            Write-Host "Backup:           $($latestBackup.Name)" -ForegroundColor Gray
        }
    }
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Cyan
    Write-Host "  1. Review FinalProductPublish contents" -ForegroundColor Gray
    Write-Host "  2. Commit to git:" -ForegroundColor Gray
    Write-Host "       git add FinalProductPublish/" -ForegroundColor DarkGray
    Write-Host "       git add Backups/" -ForegroundColor DarkGray
    Write-Host "       git commit -m `"Update FinalProductPublish to v$Version`"" -ForegroundColor DarkGray
    Write-Host "  3. Create git tag:" -ForegroundColor Gray
    Write-Host "       git tag -a v$Version -m `"Version $Version`"" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor $ColorGreen
    Write-Host ""

} catch {
    Write-Host ""
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor $ColorRed
    Write-Host "  UPDATE FAILED!" -ForegroundColor $ColorRed
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor $ColorRed
    Write-Host ""
    Write-ErrorMsg "Update failed: $_"
    Write-Host ""
    Write-Host "Error Details:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host ""
    if (-not $SkipBackup) {
        Write-Host "To rollback, run:" -ForegroundColor Yellow
        Write-Host "  .\scripts\Restore-FinalProductPublish.ps1" -ForegroundColor Gray
    }
    Write-Host ""
    exit 1
}
