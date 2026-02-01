<#
.SYNOPSIS
    Comprehensive verification of FinalProductPublish integrity

.DESCRIPTION
    Performs detailed checks on a publish folder:
      1) File count range
      2) Total size range
      3) Critical binaries present + size sanity
      4) Deployment scripts present (aligned to current pipeline)
      5) Documentation files present (aligned; supports legacy naming)
      6) Static assets (wwwroot/css + wwwroot/js) present with minimum counts
      7) Localization (he-IL) presence + resources dll
      8) Optional version check (VERSION.txt)

    Bonus: optionally runs VERIFY_FILES.bat and reports whether it clearly passes.

.PARAMETER Path
    Target folder name or relative path from repo root (default: "FinalProductPublish")

.PARAMETER ExpectedVersion
    Expected version string to match inside VERSION.txt (expects line like "VERSION: <x>")

.PARAMETER Strict
    Treat warnings as failures (exit 1 if any warnings).

.PARAMETER NoRunBat
    Skip running VERIFY_FILES.bat even if present.

.NOTES
    ASCII-only output to avoid encoding/parser issues.
#>

[CmdletBinding()]
param(
    [Parameter()]
    [string]$Path = 'FinalProductPublish',

    [Parameter()]
    [string]$ExpectedVersion,

    [Parameter()]
    [switch]$Strict,

    [Parameter()]
    [switch]$NoRunBat
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Repo root assumed one level above scripts\
$RepoRoot = Split-Path -Parent $PSScriptRoot

# If user passed an absolute path, use it; otherwise resolve from repo root.
if ([System.IO.Path]::IsPathRooted($Path)) {
    $TargetPath = $Path
} else {
    $TargetPath = Join-Path $RepoRoot $Path
}

# =========================
# Expectations (ALIGNED)
# =========================

# These ranges were in your original script and match what you already expect (~560-570).
$ExpectedFileCountMin = 560
$ExpectedFileCountMax = 570

# Size range from your original script.
$ExpectedSizeMinMB = 110
$ExpectedSizeMaxMB = 120

# Critical binaries (as in your original script)
$CriticalBinaries = @(
    @{ Name = 'ShiftManager.exe'; Min = 100KB; Max = 200KB },
    @{ Name = 'ShiftManager.dll'; Min = 3MB;    Max = 5MB }
)

# Deployment scripts aligned to your current publish-copy list (Update script fallback)
$RequiredDeploymentScripts = @(
    'UNBLOCK_FILES.bat',
    'VERIFY_FILES.bat',
    'QUICK_FIX.bat',
    'START_HERE.bat'
)

# Keep START_HERE as optional (it may exist in some builds, but isn't in the current copy list)
$OptionalDeploymentScripts = @(
    'START_HERE.bat'
)

# Documentation alignment:
# - appsettings.json is required (your updater also considers it critical)
# - require ONE of the deployment guides (legacy + new)
# - require ONE of the operator notes docs (legacy + new)
$RequiredDocsAlways = @(
    'appsettings.json'
)

$RequireOneOfDeploymentGuides = @(
    'AIR_GAPPED_DEPLOYMENT_GUIDE.txt',
    'DEPLOYMENT_GUIDE.txt'
)

$RequireOneOfOperatorNotes = @(
    'CRITICAL_BEFORE_DEMO.txt',
    'README.txt',
    'README.md'
)

# Optional docs aligned to your current pipeline and some legacy naming
$OptionalDocs = @(
    'VERSION.txt',
    'QUICK_START.txt',
    'UPGRADE_GUIDE.txt',
    'API_DOCUMENTATION.md',
    'appsettings.Production.template.json'
)

# Additional artifacts that are commonly shipped
$OptionalDirs = @(
    'clients'
)

# Static assets minimums (from your original)
$MinCssFiles = 2
$MinJsFiles  = 3

# =========================
# Counters + helpers
# =========================
$script:Passed = 0
$script:Warned = 0
$script:Failed = 0

function Write-Info    { param([string]$Message) Write-Host ("[INFO] {0}" -f $Message) -ForegroundColor Cyan }
function Write-Ok      { param([string]$Message) Write-Host ("[ OK ] {0}" -f $Message) -ForegroundColor Green }
function Write-Warn    { param([string]$Message) Write-Host ("[WARN] {0}" -f $Message) -ForegroundColor Yellow }
function Write-Err     { param([string]$Message) Write-Host ("[FAIL] {0}" -f $Message) -ForegroundColor Red }

function Write-CheckHeader {
    param([int]$Num, [int]$Total, [string]$Title)
    Write-Host ""
    Write-Info ("CHECK {0}/{1}: {2}" -f $Num, $Total, $Title)
}

function Pass { param([string]$Msg = 'PASS'); Write-Ok $Msg; $script:Passed++ }
function Warn { param([string]$Msg); Write-Warn $Msg; $script:Warned++ }
function Fail { param([string]$Msg); Write-Err $Msg; $script:Failed++ }

function Get-AllFiles {
    param([string]$Root)
    Get-ChildItem -LiteralPath $Root -File -Recurse -Force -ErrorAction Stop
}

function Get-SizeMb {
    param([System.IO.FileInfo[]]$Files)
    $sum = 0
    if ($Files.Count -gt 0) {
        $sum = ($Files | Measure-Object -Property Length -Sum).Sum
    }
    [math]::Round(($sum / 1MB), 2)
}

function Test-Exists {
    param([string]$Root, [string]$Relative)
    Test-Path -LiteralPath (Join-Path $Root $Relative)
}

function Require-OneOf {
    param(
        [string]$Root,
        [string]$Label,
        [string[]]$Candidates
    )

    $found = @()
    foreach ($c in $Candidates) {
        if (Test-Exists -Root $Root -Relative $c) { $found += $c }
    }

    if ($found.Count -gt 0) {
        Write-Host ("  OK: {0}: {1}" -f $Label, ($found -join ', ')) -ForegroundColor Gray
        return $true
    } else {
        Write-Host ("  MISSING: {0} (need one of: {1})" -f $Label, ($Candidates -join ', ')) -ForegroundColor Red
        return $false
    }
}

try {
    Write-Host ""
    Write-Info "FinalProductPublish Verification"
    Write-Host ("[INFO] Target: {0}" -f $TargetPath) -ForegroundColor Gray

    if (-not (Test-Path -LiteralPath $TargetPath)) {
        throw ("Target path not found: {0}" -f $TargetPath)
    }

    # Precompute file list once (checks 1 & 2)
    $allFiles  = Get-AllFiles -Root $TargetPath
    $fileCount = $allFiles.Count
    $sizeMB    = Get-SizeMb -Files $allFiles

    # 1) File count
    Write-CheckHeader 1 8 "File count"
    Write-Host ("  Found:    {0} files" -f $fileCount) -ForegroundColor Gray
    Write-Host ("  Expected: {0}-{1} files" -f $ExpectedFileCountMin, $ExpectedFileCountMax) -ForegroundColor Gray

    if ($fileCount -lt $ExpectedFileCountMin) {
        Fail ("Too few files (expected {0}-{1})" -f $ExpectedFileCountMin, $ExpectedFileCountMax)
    }
    elseif ($fileCount -gt $ExpectedFileCountMax) {
        Warn "More files than expected (may include extras/backups)."
    }
    else {
        Pass
    }

    # 2) Total size
    Write-CheckHeader 2 8 "Total size"
    Write-Host ("  Found:    {0} MB" -f $sizeMB) -ForegroundColor Gray
    Write-Host ("  Expected: {0}-{1} MB" -f $ExpectedSizeMinMB, $ExpectedSizeMaxMB) -ForegroundColor Gray

    if ($sizeMB -lt $ExpectedSizeMinMB) {
        Fail "Package too small (may be missing .NET runtime/self-contained bits)."
    }
    elseif ($sizeMB -gt $ExpectedSizeMaxMB) {
        Warn "Package larger than expected."
    }
    else {
        Pass
    }

    # 3) Critical executables
    Write-CheckHeader 3 8 "Critical executables"
    $exeOk = $true

    foreach ($c in $CriticalBinaries) {
        $p = Join-Path $TargetPath $c.Name
        if (-not (Test-Path -LiteralPath $p)) {
            Write-Host ("  MISSING: {0}" -f $c.Name) -ForegroundColor Red
            $exeOk = $false
            continue
        }

        $len = (Get-Item -LiteralPath $p).Length
        $human = if ($len -ge 1MB) { "{0} MB" -f ([math]::Round($len/1MB, 2)) } else { "{0} KB" -f ([math]::Round($len/1KB, 2)) }

        $inRange = ($len -ge $c.Min -and $len -le $c.Max)
        if ($inRange) {
            Write-Host ("  OK:  {0} ({1})" -f $c.Name, $human) -ForegroundColor Gray
        } else {
            Write-Host ("  ODD: {0} ({1}) expected [{2}..{3} bytes]" -f $c.Name, $human, $c.Min, $c.Max) -ForegroundColor Yellow
            $exeOk = $false
        }
    }

    if ($exeOk) { Pass } else { Fail "Missing or unusual executables." }

    # 4) Deployment scripts (aligned)
    Write-CheckHeader 4 8 "Deployment scripts"
    $scriptsOk = $true

    foreach ($s in $RequiredDeploymentScripts) {
        if (Test-Exists -Root $TargetPath -Relative $s) {
            Write-Host ("  OK:      {0}" -f $s) -ForegroundColor Gray
        } else {
            Write-Host ("  MISSING: {0}" -f $s) -ForegroundColor Red
            $scriptsOk = $false
        }
    }

    foreach ($s in $OptionalDeploymentScripts) {
        if (Test-Exists -Root $TargetPath -Relative $s) {
            Write-Host ("  OK:      {0} (optional)" -f $s) -ForegroundColor DarkGray
        } else {
            Write-Host ("  NOTE:    {0} (optional not found)" -f $s) -ForegroundColor DarkYellow
        }
    }

    if ($scriptsOk) { Pass } else { Fail "Missing required deployment scripts." }

    # 5) Documentation files (aligned)
    Write-CheckHeader 5 8 "Documentation files"
    $docsOk = $true

    foreach ($d in $RequiredDocsAlways) {
        if (Test-Exists -Root $TargetPath -Relative $d) {
            Write-Host ("  OK:      {0}" -f $d) -ForegroundColor Gray
        } else {
            Write-Host ("  MISSING: {0} (required)" -f $d) -ForegroundColor Red
            $docsOk = $false
        }
    }

    if (-not (Require-OneOf -Root $TargetPath -Label 'Deployment guide' -Candidates $RequireOneOfDeploymentGuides)) {
        $docsOk = $false
    }

    if (-not (Require-OneOf -Root $TargetPath -Label 'Operator notes' -Candidates $RequireOneOfOperatorNotes)) {
        $docsOk = $false
    }

    foreach ($d in $OptionalDocs) {
        if (Test-Exists -Root $TargetPath -Relative $d) {
            Write-Host ("  OK:      {0} (optional)" -f $d) -ForegroundColor DarkGray
        } else {
            Write-Host ("  NOTE:    {0} (optional not found)" -f $d) -ForegroundColor DarkYellow
        }
    }

    if ($docsOk) { Pass } else { Fail "Missing required documentation set." }

    # 6) Static files (wwwroot)
    Write-CheckHeader 6 8 "Static files (wwwroot)"
    $wwwroot = Join-Path $TargetPath 'wwwroot'

    if (-not (Test-Path -LiteralPath $wwwroot)) {
        Fail "wwwroot folder not found."
    } else {
        $cssDir = Join-Path $wwwroot 'css'
        $jsDir  = Join-Path $wwwroot 'js'

        $cssCount = 0
        $jsCount  = 0

        if (Test-Path -LiteralPath $cssDir) {
            $cssCount = (Get-ChildItem -LiteralPath $cssDir -Filter '*.css' -File -ErrorAction SilentlyContinue).Count
            Write-Host ("  OK: wwwroot\css ({0} .css files)" -f $cssCount) -ForegroundColor Gray
        } else {
            Write-Host "  MISSING: wwwroot\css" -ForegroundColor Red
        }

        if (Test-Path -LiteralPath $jsDir) {
            $jsCount = (Get-ChildItem -LiteralPath $jsDir -Filter '*.js' -File -ErrorAction SilentlyContinue).Count
            Write-Host ("  OK: wwwroot\js  ({0} .js files)" -f $jsCount) -ForegroundColor Gray
        } else {
            Write-Host "  MISSING: wwwroot\js" -ForegroundColor Red
        }

        if ($cssCount -ge $MinCssFiles -and $jsCount -ge $MinJsFiles) {
            Pass
        } else {
            Warn ("Fewer static files than expected (min css={0}, min js={1})." -f $MinCssFiles, $MinJsFiles)
        }
    }

    # 7) Localization (he-IL)
    Write-CheckHeader 7 8 "Localization (he-IL)"
    $heIL = Join-Path $TargetPath 'he-IL'

    if (-not (Test-Path -LiteralPath $heIL)) {
        Warn "he-IL folder not found (Hebrew localization not present)."
    } else {
        $resDll = Join-Path $heIL 'ShiftManager.resources.dll'
        if (Test-Path -LiteralPath $resDll) {
            Write-Host "  OK: he-IL folder exists" -ForegroundColor Gray
            Write-Host "  OK: ShiftManager.resources.dll found" -ForegroundColor Gray
            Pass
        } else {
            Write-Host "  OK: he-IL folder exists" -ForegroundColor Gray
            Write-Host "  MISSING: ShiftManager.resources.dll" -ForegroundColor Red
            Fail "Missing Hebrew resources DLL."
        }
    }

    # 8) Version check (optional)
    Write-CheckHeader 8 8 "Version verification"

    if ([string]::IsNullOrWhiteSpace($ExpectedVersion)) {
        Write-Info "Version check skipped (no -ExpectedVersion provided)."
        # Not counted as pass/warn/fail.
    } else {
        $versionFile = Join-Path $TargetPath 'VERSION.txt'
        if (-not (Test-Path -LiteralPath $versionFile)) {
            Warn "VERSION.txt not found (cannot verify version)."
        } else {
            $content = Get-Content -LiteralPath $versionFile -Raw -ErrorAction Stop
            if ($content -match ("VERSION:\s+{0}" -f [regex]::Escape($ExpectedVersion))) {
                Write-Host ("  OK: VERSION.txt matches {0}" -f $ExpectedVersion) -ForegroundColor Gray
                Pass
            } else {
                Warn ("VERSION.txt does not match expected version ({0})." -f $ExpectedVersion)
            }
        }
    }

    # Extra: presence of commonly shipped directories (warning only)
    foreach ($d in $OptionalDirs) {
        Write-Host ""
        Write-Info ("Extra: directory check ({0})" -f $d)
        if (Test-Path -LiteralPath (Join-Path $TargetPath $d)) {
            Write-Host ("  OK: {0}\ exists" -f $d) -ForegroundColor Gray
        } else {
            Warn ("{0}\ not found (may be expected depending on deployment)." -f $d)
        }
    }

    # Bonus: run VERIFY_FILES.bat (optional)
    if (-not $NoRunBat) {
        $verifyBat = Join-Path $TargetPath 'VERIFY_FILES.bat'
        if (Test-Path -LiteralPath $verifyBat) {
            Write-Host ""
            Write-Info "BONUS: Running VERIFY_FILES.bat..."
            Push-Location $TargetPath
            try {
                $tmp = Join-Path $TargetPath 'verify_output.tmp'
                cmd /c 'VERIFY_FILES.bat' > $tmp 2>&1
                $out = ''
                if (Test-Path -LiteralPath $tmp) {
                    $out = Get-Content -LiteralPath $tmp -Raw -ErrorAction SilentlyContinue
                    Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
                }

                if ($out -match 'Verification PASSED' -or $out -match 'All checks passed') {
                    Write-Ok "VERIFY_FILES.bat PASSED"
                } else {
                    Warn "VERIFY_FILES.bat ran but output was not clearly PASS. Consider running it manually for details."
                    Write-Host ("  Manual: cd ""{0}""; VERIFY_FILES.bat" -f $TargetPath) -ForegroundColor DarkGray
                }
            } catch {
                Warn "Could not run VERIFY_FILES.bat."
            } finally {
                Pop-Location
            }
        }
    } else {
        Write-Info "BONUS: VERIFY_FILES.bat skipped (-NoRunBat)."
    }

    # Summary / exit code
    Write-Host ""
    Write-Info "VERIFICATION SUMMARY"
    Write-Host ("  Passed:   {0}" -f $script:Passed) -ForegroundColor Green
    if ($script:Warned -gt 0) { Write-Host ("  Warnings: {0}" -f $script:Warned) -ForegroundColor Yellow }
    if ($script:Failed -gt 0) { Write-Host ("  Failed:   {0}" -f $script:Failed) -ForegroundColor Red }
    Write-Host ""

    $effectiveFailure = ($script:Failed -gt 0) -or ($Strict -and $script:Warned -gt 0)

    if (-not $effectiveFailure) {
        if ($script:Warned -eq 0) {
            Write-Ok "RESULT: ALL CHECKS PASSED"
        } else {
            Write-Warn "RESULT: PASSED WITH WARNINGS"
        }
        exit 0
    } else {
        if ($script:Failed -gt 0) {
            Write-Err "RESULT: VERIFICATION FAILED"
        } else {
            Write-Err "RESULT: STRICT MODE FAILED DUE TO WARNINGS"
        }
        exit 1
    }
}
catch {
    Write-Host ""
    Write-Err ("Verification failed: {0}" -f $_.Exception.Message)
    exit 1
}
