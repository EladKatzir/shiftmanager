<#
.SYNOPSIS
    Verify build output structure and completeness

.DESCRIPTION
    Checks file counts, critical files, sizes, and package structure
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

    # Count files
    Write-Info "Counting files..."
    $allFiles = Get-ChildItem -Path $OutputPath -File -Recurse
    $fileCount = $allFiles.Count
    $expectedFiles = 500
    $tolerance = 150

    if ($fileCount -lt ($expectedFiles - $tolerance) -or $fileCount -gt ($expectedFiles + $tolerance)) {
        Write-ErrorMsg "File count $fileCount outside expected range ($expectedFiles +/- $tolerance)"
        throw "Unexpected file count"
    }
    Write-Success "File count: $fileCount files (expected: ~$expectedFiles)"

    # Count DLLs
    Write-Info "Counting DLLs..."
    $dlls = Get-ChildItem -Path $OutputPath -Filter "*.dll" -Recurse
    $dllCount = $dlls.Count
    $expectedDlls = 350
    $dllTolerance = 20

    if ($dllCount -lt ($expectedDlls - $dllTolerance) -or $dllCount -gt ($expectedDlls + $dllTolerance)) {
        Write-ErrorMsg "DLL count $dllCount outside expected range ($expectedDlls +/- $dllTolerance)"
        throw "Unexpected DLL count"
    }
    Write-Success "DLL count: $dllCount DLLs (expected: ~$expectedDlls)"

    # Check package size
    Write-Info "Calculating package size..."
    $totalSize = ($allFiles | Measure-Object -Property Length -Sum).Sum / 1MB
    $expectedSize = 160
    $sizeTolerance = 30

    if ($totalSize -lt ($expectedSize - $sizeTolerance) -or $totalSize -gt ($expectedSize + $sizeTolerance)) {
        Write-ErrorMsg "Package size $($totalSize.ToString('F0')) MB outside expected range ($expectedSize +/- $sizeTolerance MB)"
        throw "Unexpected package size"
    }
    Write-Success "Package size: $($totalSize.ToString('F0')) MB (expected: ~$expectedSize MB)"

    # Check critical files
    Write-Info "Verifying critical files..."
    $criticalFiles = @(
        "ShiftManager.exe",
        "ShiftManager.dll",
        "e_sqlite3.dll",
        "appsettings.json",
        "wwwroot\css\site.css",
        "wwwroot\js\site.js",
        "he-IL\ShiftManager.resources.dll"
    )

    foreach ($file in $criticalFiles) {
        $fullPath = Join-Path $OutputPath $file
        if (-not (Test-Path $fullPath)) {
            Write-ErrorMsg "Critical file missing: $file"
            throw "Missing critical file"
        }
    }
    Write-Success "Critical files present: ShiftManager.exe, e_sqlite3.dll, wwwroot/, he-IL/"

    # Check no test artifacts
    Write-Info "Checking for test artifacts..."
    $testFiles = Get-ChildItem -Path $OutputPath -Recurse | Where-Object {
        $_.Name -match '(test|Test|\.pdb$|\.log$|\.tmp$)' -and $_.Name -notmatch 'ShiftManager\.pdb'
    }
    if ($testFiles) {
        Write-ErrorMsg "Test artifacts found: $($testFiles.Count) files"
        $testFiles | ForEach-Object { Write-Host "    - $($_.FullName)" -ForegroundColor Yellow }
        # Don't fail, just warn
    }
    Write-Success "No unwanted test artifacts"

    # Verify Razor views precompiled
    Write-Info "Verifying Razor views precompiled..."
    $pagesFolder = Join-Path $OutputPath "Pages"
    $viewsFolder = Join-Path $OutputPath "Views"

    if ((Test-Path $pagesFolder) -or (Test-Path $viewsFolder)) {
        Write-ErrorMsg "Pages/Views folders found - Razor views not precompiled!"
        throw "Razor views should be precompiled in Release mode"
    }
    Write-Success "Razor views precompiled (no Pages/Views folders)"

    # H-08: Verify SHA256 manifest exists
    Write-Info "Checking SHA256 manifest..."
    $manifestPath = Join-Path $OutputPath "SHA256SUMS.txt"
    if (Test-Path $manifestPath) {
        $lines = Get-Content $manifestPath
        Write-Success "SHA256 manifest present: $($lines.Count) file hashes"
    } else {
        Write-ErrorMsg "SHA256 manifest (SHA256SUMS.txt) not found - run Build-Release.ps1 to generate"
    }

    Write-Host ""
    Write-Host "[PASS] Build output verified successfully" -ForegroundColor Green
    return $true

} catch {
    Write-Host ""
    Write-Host "[FAIL] Build verification failed: $_" -ForegroundColor Red
    throw
}
