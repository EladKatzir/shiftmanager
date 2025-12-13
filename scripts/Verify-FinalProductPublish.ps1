<#
.SYNOPSIS
    Comprehensive verification of FinalProductPublish integrity

.DESCRIPTION
    Performs detailed checks on FinalProductPublish:
    - File count
    - Total size
    - Critical files present
    - Deployment scripts
    - Documentation files
    - Static files (wwwroot)
    - Localization (he-IL)
    - Optional: VERIFY_FILES.bat execution

.PARAMETER Path
    Path to verify (default: "FinalProductPublish")

.PARAMETER ExpectedVersion
    Expected version number (optional, checks VERSION.txt)

.EXAMPLE
    .\Verify-FinalProductPublish.ps1

.EXAMPLE
    .\Verify-FinalProductPublish.ps1 -Path "ProjectPublish"

.EXAMPLE
    .\Verify-FinalProductPublish.ps1 -ExpectedVersion "2.1.0"

.NOTES
    Author: Claude Code
    Version: 1.0
#>

[CmdletBinding()]
param(
    [Parameter()]
    [string]$Path = "FinalProductPublish",

    [Parameter()]
    [string]$ExpectedVersion
)

$ErrorActionPreference = "Stop"

# Paths
$ScriptRoot = Split-Path -Parent $PSScriptRoot
$TargetPath = Join-Path $ScriptRoot $Path

# Colors
$ColorGreen = "Green"
$ColorYellow = "Yellow"
$ColorRed = "Red"
$ColorCyan = "Cyan"

function Write-Success { param([string]$Message) Write-Host "  ✓ " -ForegroundColor $ColorGreen -NoNewline; Write-Host $Message }
function Write-Info { param([string]$Message) Write-Host "  ⏳ " -ForegroundColor $ColorCyan -NoNewline; Write-Host $Message }
function Write-WarningMsg { param([string]$Message) Write-Host "  ⚠️  " -ForegroundColor $ColorYellow -NoNewline; Write-Host $Message }
function Write-ErrorMsg { param([string]$Message) Write-Host "  ❌ " -ForegroundColor $ColorRed -NoNewline; Write-Host $Message }

function Write-CheckHeader {
    param([int]$CheckNum, [int]$Total, [string]$Title)
    Write-Host ""
    Write-Host "[CHECK $CheckNum/$Total] $Title..." -ForegroundColor $ColorCyan
}

$script:PassedChecks = 0
$script:FailedChecks = 0
$script:WarningChecks = 0

try {
    Write-Host ""
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor $ColorCyan
    Write-Host "  FinalProductPublish Verification" -ForegroundColor White
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor $ColorCyan
    Write-Host "  Target: $Path" -ForegroundColor Gray
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor $ColorCyan

    # Check if path exists
    if (-not (Test-Path $TargetPath)) {
        Write-ErrorMsg "Path not found: $TargetPath"
        throw "Target path missing"
    }

    # Check 1: File count
    Write-CheckHeader 1 8 "File count"
    $allFiles = Get-ChildItem -Path $TargetPath -File -Recurse
    $fileCount = $allFiles.Count

    $expectedMin = 560
    $expectedMax = 570

    Write-Host "    Found: $fileCount files" -ForegroundColor Gray
    Write-Host "    Expected: $expectedMin-$expectedMax files" -ForegroundColor Gray

    if ($fileCount -lt $expectedMin) {
        Write-ErrorMsg "FAIL - Too few files (expected $expectedMin-$expectedMax)"
        $script:FailedChecks++
    } elseif ($fileCount -gt $expectedMax) {
        Write-WarningMsg "WARNING - More files than expected (may include backups)"
        $script:WarningChecks++
    } else {
        Write-Success "PASS"
        $script:PassedChecks++
    }

    # Check 2: Total size
    Write-CheckHeader 2 8 "Total size"
    $totalSize = ($allFiles | Measure-Object -Property Length -Sum).Sum
    $sizeMB = [math]::Round($totalSize / 1MB, 2)

    $expectedSizeMin = 110
    $expectedSizeMax = 120

    Write-Host "    Found: $sizeMB MB" -ForegroundColor Gray
    Write-Host "    Expected: $expectedSizeMin-$expectedSizeMax MB" -ForegroundColor Gray

    if ($sizeMB -lt $expectedSizeMin) {
        Write-ErrorMsg "FAIL - Package too small (may be missing .NET runtime)"
        $script:FailedChecks++
    } elseif ($sizeMB -gt $expectedSizeMax) {
        Write-WarningMsg "WARNING - Package larger than expected"
        $script:WarningChecks++
    } else {
        Write-Success "PASS"
        $script:PassedChecks++
    }

    # Check 3: Critical executables
    Write-CheckHeader 3 8 "Critical executables"
    $criticalExes = @{
        "ShiftManager.exe" = @{ MinSize = 100KB; MaxSize = 200KB }
        "ShiftManager.dll" = @{ MinSize = 3MB; MaxSize = 5MB }
    }

    $exeCheckPassed = $true
    foreach ($exe in $criticalExes.Keys) {
        $exePath = Join-Path $TargetPath $exe
        if (Test-Path $exePath) {
            $exeSize = (Get-Item $exePath).Length
            $exeSizeKB = [math]::Round($exeSize / 1KB, 2)
            $exeSizeMB = [math]::Round($exeSize / 1MB, 2)

            if ($exeSize -ge $criticalExes[$exe].MinSize -and $exeSize -le $criticalExes[$exe].MaxSize) {
                if ($exeSizeMB -ge 1) {
                    Write-Host "    ✓ $exe found ($exeSizeMB MB)" -ForegroundColor Gray
                } else {
                    Write-Host "    ✓ $exe found ($exeSizeKB KB)" -ForegroundColor Gray
                }
            } else {
                Write-Host "    ⚠️  $exe found but size unusual" -ForegroundColor Yellow
                $exeCheckPassed = $false
            }
        } else {
            Write-Host "    ❌ $exe NOT FOUND" -ForegroundColor Red
            $exeCheckPassed = $false
        }
    }

    if ($exeCheckPassed) {
        Write-Success "PASS"
        $script:PassedChecks++
    } else {
        Write-ErrorMsg "FAIL - Missing or unusual executables"
        $script:FailedChecks++
    }

    # Check 4: Deployment scripts
    Write-CheckHeader 4 8 "Deployment scripts"
    $requiredScripts = @(
        "UNBLOCK_FILES.bat",
        "VERIFY_FILES.bat",
        "START_HERE.bat",
        "QUICK_FIX.bat"
    )

    $scriptCheckPassed = $true
    foreach ($script in $requiredScripts) {
        $scriptPath = Join-Path $TargetPath $script
        if (Test-Path $scriptPath) {
            Write-Host "    ✓ $script found" -ForegroundColor Gray
        } else {
            Write-Host "    ❌ $script NOT FOUND" -ForegroundColor Red
            $scriptCheckPassed = $false
        }
    }

    if ($scriptCheckPassed) {
        Write-Success "PASS"
        $script:PassedChecks++
    } else {
        Write-ErrorMsg "FAIL - Missing deployment scripts"
        $script:FailedChecks++
    }

    # Check 5: Documentation files
    Write-CheckHeader 5 8 "Documentation files"
    $requiredDocs = @(
        "README.txt",
        "DEPLOYMENT_GUIDE.txt",
        "appsettings.json"
    )

    $optionalDocs = @(
        "VERSION.txt",
        "QUICK_START.txt",
        "UPGRADE_GUIDE.txt",
        "AIR_GAPPED_DEPLOYMENT_GUIDE.txt",
        "API_DOCUMENTATION.md"
    )

    $docCheckPassed = $true
    foreach ($doc in $requiredDocs) {
        $docPath = Join-Path $TargetPath $doc
        if (Test-Path $docPath) {
            Write-Host "    ✓ $doc found" -ForegroundColor Gray
        } else {
            Write-Host "    ❌ $doc NOT FOUND (required)" -ForegroundColor Red
            $docCheckPassed = $false
        }
    }

    foreach ($doc in $optionalDocs) {
        $docPath = Join-Path $TargetPath $doc
        if (Test-Path $docPath) {
            Write-Host "    ✓ $doc found" -ForegroundColor DarkGray
        } else {
            Write-Host "    ⚠️  $doc not found (optional)" -ForegroundColor DarkYellow
        }
    }

    if ($docCheckPassed) {
        Write-Success "PASS"
        $script:PassedChecks++
    } else {
        Write-ErrorMsg "FAIL - Missing required documentation"
        $script:FailedChecks++
    }

    # Check 6: Static files (wwwroot)
    Write-CheckHeader 6 8 "Static files (wwwroot)"
    $wwwrootPath = Join-Path $TargetPath "wwwroot"

    if (Test-Path $wwwrootPath) {
        $cssPath = Join-Path $wwwrootPath "css"
        $jsPath = Join-Path $wwwrootPath "js"

        $cssCount = 0
        $jsCount = 0

        if (Test-Path $cssPath) {
            $cssCount = (Get-ChildItem -Path $cssPath -Filter "*.css").Count
            Write-Host "    ✓ wwwroot/css/ exists ($cssCount files)" -ForegroundColor Gray
        } else {
            Write-Host "    ❌ wwwroot/css/ NOT FOUND" -ForegroundColor Red
        }

        if (Test-Path $jsPath) {
            $jsCount = (Get-ChildItem -Path $jsPath -Filter "*.js").Count
            Write-Host "    ✓ wwwroot/js/ exists ($jsCount files)" -ForegroundColor Gray
        } else {
            Write-Host "    ❌ wwwroot/js/ NOT FOUND" -ForegroundColor Red
        }

        if ($cssCount -ge 2 -and $jsCount -ge 3) {
            Write-Success "PASS"
            $script:PassedChecks++
        } else {
            Write-WarningMsg "WARNING - Fewer static files than expected"
            $script:WarningChecks++
        }
    } else {
        Write-ErrorMsg "FAIL - wwwroot folder not found"
        $script:FailedChecks++
    }

    # Check 7: Localization (he-IL)
    Write-CheckHeader 7 8 "Localization (Hebrew)"
    $heILPath = Join-Path $TargetPath "he-IL"

    if (Test-Path $heILPath) {
        $resourceDll = Join-Path $heILPath "ShiftManager.resources.dll"
        if (Test-Path $resourceDll) {
            Write-Host "    ✓ he-IL/ folder exists" -ForegroundColor Gray
            Write-Host "    ✓ ShiftManager.resources.dll found" -ForegroundColor Gray
            Write-Success "PASS"
            $script:PassedChecks++
        } else {
            Write-Host "    ✓ he-IL/ folder exists" -ForegroundColor Gray
            Write-Host "    ❌ ShiftManager.resources.dll NOT FOUND" -ForegroundColor Red
            Write-ErrorMsg "FAIL - Missing Hebrew resources DLL"
            $script:FailedChecks++
        }
    } else {
        Write-WarningMsg "WARNING - he-IL folder not found (Hebrew not available)"
        $script:WarningChecks++
    }

    # Check 8: Version check (if specified)
    Write-CheckHeader 8 8 "Version verification"

    if ($ExpectedVersion) {
        $versionPath = Join-Path $TargetPath "VERSION.txt"
        if (Test-Path $versionPath) {
            $versionContent = Get-Content $versionPath -Raw
            if ($versionContent -match "VERSION:\s+$ExpectedVersion") {
                Write-Host "    ✓ VERSION.txt shows v$ExpectedVersion" -ForegroundColor Gray
                Write-Success "PASS"
                $script:PassedChecks++
            } else {
                Write-Host "    ⚠️  VERSION.txt doesn't match expected version" -ForegroundColor Yellow
                Write-WarningMsg "WARNING - Version mismatch"
                $script:WarningChecks++
            }
        } else {
            Write-Host "    ⚠️  VERSION.txt not found (can't verify version)" -ForegroundColor Yellow
            Write-WarningMsg "WARNING - No VERSION.txt file"
            $script:WarningChecks++
        }
    } else {
        Write-Host "    ⏭️  Version check skipped (no expected version provided)" -ForegroundColor DarkGray
        Write-Info "SKIPPED"
    }

    # Run VERIFY_FILES.bat if available (optional bonus check)
    $verifyBat = Join-Path $TargetPath "VERIFY_FILES.bat"
    if (Test-Path $verifyBat) {
        Write-Host ""
        Write-Host "[BONUS CHECK] Running VERIFY_FILES.bat..." -ForegroundColor $ColorCyan
        Push-Location $TargetPath
        try {
            cmd /c VERIFY_FILES.bat > verify_output.tmp 2>&1
            $verifyOutput = Get-Content verify_output.tmp -Raw
            Remove-Item verify_output.tmp -ErrorAction SilentlyContinue

            if ($verifyOutput -match "Verification PASSED" -or $verifyOutput -match "All checks passed") {
                Write-Success "VERIFY_FILES.bat passed"
            } else {
                Write-WarningMsg "VERIFY_FILES.bat output unclear"
                Write-Host "    Run manually for details: cd $Path && VERIFY_FILES.bat" -ForegroundColor DarkGray
            }
        } catch {
            Write-WarningMsg "Could not run VERIFY_FILES.bat"
        } finally {
            Pop-Location
        }
    }

    # Final summary
    Write-Host ""
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor $ColorCyan
    Write-Host "  VERIFICATION SUMMARY" -ForegroundColor White
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor $ColorCyan
    Write-Host ""
    Write-Host "  Passed:   $script:PassedChecks" -ForegroundColor $ColorGreen
    if ($script:WarningChecks -gt 0) {
        Write-Host "  Warnings: $script:WarningChecks" -ForegroundColor $ColorYellow
    }
    if ($script:FailedChecks -gt 0) {
        Write-Host "  Failed:   $script:FailedChecks" -ForegroundColor $ColorRed
    }
    Write-Host ""

    if ($script:FailedChecks -eq 0) {
        if ($script:WarningChecks -eq 0) {
            Write-Host "  RESULT: ✅ ALL CHECKS PASSED" -ForegroundColor $ColorGreen
        } else {
            Write-Host "  RESULT: ✅ PASSED WITH WARNINGS" -ForegroundColor $ColorYellow
        }
    } else {
        Write-Host "  RESULT: ❌ VERIFICATION FAILED" -ForegroundColor $ColorRed
    }

    Write-Host ""
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor $ColorCyan
    Write-Host ""

    if ($script:FailedChecks -gt 0) {
        exit 1
    }

} catch {
    Write-Host ""
    Write-ErrorMsg "Verification failed: $_"
    Write-Host ""
    Write-Host "Error Details:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host ""
    exit 1
}
