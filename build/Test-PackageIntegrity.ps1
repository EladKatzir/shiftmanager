<#
.SYNOPSIS
    Final package integrity check before release

.DESCRIPTION
    Comprehensive validation of the complete package:
    - File structure integrity
    - Documentation completeness
    - Configuration validity
    - Executable integrity
    - No sensitive data leaks
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"

function Write-Success { param([string]$Message) Write-Host "  [OK] " -ForegroundColor Green -NoNewline; Write-Host $Message }
function Write-ErrorMsg { param([string]$Message) Write-Host "  [ERR] " -ForegroundColor Red -NoNewline; Write-Host $Message }
function Write-Info { param([string]$Message) Write-Host "  [..] " -ForegroundColor Blue -NoNewline; Write-Host $Message }

try {
    if (-not (Test-Path $OutputPath)) {
        Write-ErrorMsg "Output path not found: $OutputPath"
        throw "Output path missing"
    }

    # Check 1: Documentation files
    Write-Info "Checking documentation completeness..."
    $requiredDocs = @(
        "README.txt",
        "QUICK_START.txt",
        "DEPLOYMENT_GUIDE.txt",
        "VERSION.txt",
        "VERIFICATION_CHECKLIST.txt",
        "START_HERE.bat"
    )

    $missingDocs = @()
    foreach ($doc in $requiredDocs) {
        $docPath = Join-Path $OutputPath $doc
        if (-not (Test-Path $docPath)) {
            $missingDocs += $doc
        }
    }

    if ($missingDocs.Count -gt 0) {
        Write-ErrorMsg "Missing documentation: $($missingDocs -join ', ')"
        throw "Documentation incomplete"
    }
    Write-Success "All required documentation present"

    # Check 2: Executable and DLLs
    Write-Info "Verifying executable integrity..."
    $exePath = Join-Path $OutputPath "ShiftManager.exe"
    if (-not (Test-Path $exePath)) {
        Write-ErrorMsg "ShiftManager.exe not found"
        throw "Missing executable"
    }

    $exeSize = (Get-Item $exePath).Length
    if ($exeSize -lt 100KB) {
        Write-ErrorMsg "ShiftManager.exe is suspiciously small ($($exeSize/1KB) KB)"
        throw "Executable may be corrupted"
    }
    Write-Success "Executable present and valid size"

    # Check 3: SQLite library
    Write-Info "Checking SQLite library..."
    $sqlitePath = Join-Path $OutputPath "e_sqlite3.dll"
    if (-not (Test-Path $sqlitePath)) {
        Write-ErrorMsg "e_sqlite3.dll not found"
        throw "SQLite library missing"
    }
    Write-Success "SQLite library present"

    # Check 4: Configuration files
    Write-Info "Validating configuration files..."
    $appsettingsPath = Join-Path $OutputPath "appsettings.json"
    if (-not (Test-Path $appsettingsPath)) {
        Write-ErrorMsg "appsettings.json not found"
        throw "Configuration missing"
    }

    # Parse appsettings.json to check for sensitive data
    $appsettings = Get-Content $appsettingsPath | ConvertFrom-Json

    # Check that password fields are empty or placeholder
    if ($appsettings.PSObject.Properties.Name -contains "SEED_ADMIN_PASSWORD") {
        $adminPwd = $appsettings.SEED_ADMIN_PASSWORD
        if ($adminPwd -and $adminPwd -ne "" -and $adminPwd -notmatch "^(Your|Test|Change)") {
            Write-ErrorMsg "appsettings.json contains a real password: '$adminPwd'"
            Write-Host "    This is a security risk!" -ForegroundColor Red
            throw "Sensitive data in configuration"
        }
    }

    Write-Success "Configuration files validated"

    # Check 5: START_HERE.bat validity
    Write-Info "Checking START_HERE.bat..."
    $startHerePath = Join-Path $OutputPath "START_HERE.bat"
    $startHereContent = Get-Content $startHerePath -Raw

    # Check if it contains version info
    if ($startHereContent -notmatch "Version \d+\.\d+\.\d+") {
        Write-ErrorMsg "START_HERE.bat missing version information"
        throw "START_HERE.bat may be outdated"
    }

    # Check if it has process detection logic
    if ($startHereContent -notmatch "tasklist") {
        Write-ErrorMsg "START_HERE.bat missing process detection"
        throw "START_HERE.bat incomplete"
    }

    Write-Success "START_HERE.bat validated"

    # Check 6: wwwroot folder
    Write-Info "Checking web assets..."
    $wwwrootPath = Join-Path $OutputPath "wwwroot"
    if (-not (Test-Path $wwwrootPath)) {
        Write-ErrorMsg "wwwroot folder not found"
        throw "Web assets missing"
    }

    $cssPath = Join-Path $wwwrootPath "css"
    $jsPath = Join-Path $wwwrootPath "js"

    if (-not (Test-Path $cssPath)) {
        Write-ErrorMsg "wwwroot/css folder not found"
        throw "CSS assets missing"
    }

    if (-not (Test-Path $jsPath)) {
        Write-ErrorMsg "wwwroot/js folder not found"
        throw "JavaScript assets missing"
    }

    Write-Success "Web assets present"

    # Check 7: Localization
    Write-Info "Checking localization resources..."
    $heILPath = Join-Path $OutputPath "he-IL"
    if (-not (Test-Path $heILPath)) {
        Write-ErrorMsg "he-IL folder not found"
        throw "Hebrew localization missing"
    }

    $resourceDll = Join-Path $heILPath "ShiftManager.resources.dll"
    if (-not (Test-Path $resourceDll)) {
        Write-ErrorMsg "ShiftManager.resources.dll not found in he-IL"
        throw "Hebrew resources missing"
    }

    Write-Success "Localization resources present"

    # Check 8: No .pdb files (except ShiftManager.pdb for debugging)
    Write-Info "Checking for debug symbols..."
    $pdbFiles = @(Get-ChildItem -Path $OutputPath -Filter "*.pdb" -Recurse | Where-Object {
        $_.Name -ne "ShiftManager.pdb"
    })

    if ($pdbFiles.Count -gt 0) {
        Write-Host "    Found $($pdbFiles.Count) PDB files (debug symbols)" -ForegroundColor Yellow
        # Don't fail, just warn
    } else {
        Write-Success "No unwanted debug symbols"
    }

    # Check 9: No app.db (database should be created on first run)
    Write-Info "Checking for pre-existing database..."
    $dbPath = Join-Path $OutputPath "app.db"
    if (Test-Path $dbPath) {
        Write-ErrorMsg "app.db found in package"
        Write-Host "    Database should be created on first run, not included in package" -ForegroundColor Yellow
        Write-Host "    Removing app.db..." -ForegroundColor Yellow
        Remove-Item $dbPath -Force
        @("app.db-shm", "app.db-wal") | ForEach-Object {
            $file = Join-Path $OutputPath $_
            if (Test-Path $file) {
                Remove-Item $file -Force
            }
        }
        Write-Success "Removed pre-existing database"
    } else {
        Write-Success "No pre-existing database (correct)"
    }

    # Check 10: No logs or temp files
    Write-Info "Checking for logs and temp files..."
    $logFiles = @(Get-ChildItem -Path $OutputPath -Recurse | Where-Object {
        $_.Extension -match '\.(log|tmp|temp|bak)$'
    })

    if ($logFiles.Count -gt 0) {
        Write-Host "    Found $($logFiles.Count) log/temp files - removing..." -ForegroundColor Yellow
        $logFiles | ForEach-Object {
            Remove-Item $_.FullName -Force
        }
        Write-Success "Removed log/temp files"
    } else {
        Write-Success "No logs or temp files"
    }

    # Check 11: File permissions
    Write-Info "Checking file permissions..."
    $testFile = Join-Path $OutputPath "permission_test.tmp"
    try {
        "test" | Out-File -FilePath $testFile -Force
        Remove-Item $testFile -Force
        Write-Success "Write permissions verified"
    } catch {
        Write-ErrorMsg "Cannot write to output directory"
        throw "Insufficient permissions"
    }

    # Final summary
    Write-Host ""
    Write-Host "[OK] Package integrity verified" -ForegroundColor Green
    Write-Host ""
    Write-Host "Package is ready for:"
    Write-Host "  * ZIP archiving"
    Write-Host "  * Git tagging"
    Write-Host "  * Distribution"
    Write-Host "  * Production deployment"

    return $true

} catch {
    Write-Host ""
    Write-Host "[ERR] Package integrity check failed: $_" -ForegroundColor Red
    throw
}
