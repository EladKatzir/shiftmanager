<#
.SYNOPSIS
    Validates the dotnet publish output structure before packaging.

.DESCRIPTION
    This script runs after `dotnet publish` (Stage 5) and verifies that the
    output folder contains everything needed for air-gapped Windows/IIS deployment.

    Checks performed and WHY each matters:

    1. Core Application Files
       ShiftManager.exe, .dll, web.config, appsettings.json — without these the app
       simply cannot start. The .exe is the Kestrel host; web.config is required by IIS
       to proxy to Kestrel; appsettings.json holds runtime configuration.

    2. .NET Runtime (self-contained)
       coreclr.dll, hostfxr.dll, hostpolicy.dll, System.Runtime.dll — because the
       target machine is air-gapped, the full runtime must be included. If any of
       these are missing, the publish was NOT self-contained and the app will fail
       to start on machines without the SDK.

    3. SQLite Native Library
       e_sqlite3.dll + Microsoft.Data.Sqlite.dll — the app uses SQLite as its
       database. Without the native interop DLL the app will throw a
       DllNotFoundException at startup.

    4. Localization
       he-IL/ShiftManager.resources.dll — Hebrew satellite assembly. The app is
       used primarily in Hebrew; missing resources cause raw key names in the UI.

    5. Static Assets (wwwroot)
       CSS, JS, images, and bundled libraries (SignalR, Lucide icons). Because the
       deployment is air-gapped, CDN-hosted libraries are NOT available — they must
       be bundled in wwwroot/lib/.

    6. No Dev Artifacts Leaked
       appsettings.Development.json should not ship. PDB and test-result files are
       noted but not blocking.

    7. File Count Sanity
       Informational counts to help operators spot anomalies (e.g., an empty
       wwwroot folder or a suspiciously low DLL count).

    8. Connection String Validation
       The app reads ConnectionStrings:Default — if that key is missing or malformed,
       the app will fail to connect to SQLite on first run.

.PARAMETER OutputPath
    Path to the publish output folder (e.g., ProjectPublish/).
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"

# --- Helper functions (matching existing build script conventions) ---
function Write-Pass { param([string]$Message) Write-Host "  [PASS] " -ForegroundColor Green -NoNewline; Write-Host $Message }
function Write-Fail { param([string]$Message) Write-Host "  [FAIL] " -ForegroundColor Red -NoNewline; Write-Host $Message }
function Write-Warn { param([string]$Message) Write-Host "  [WARN] " -ForegroundColor Yellow -NoNewline; Write-Host $Message }
function Write-Info { param([string]$Message) Write-Host "  [INFO] " -ForegroundColor Blue -NoNewline; Write-Host $Message }

# Accumulate failures so all checks run before we throw
$failures = [System.Collections.Generic.List[string]]::new()

try {
    Write-Host ""
    Write-Host "=== Verify-Build ===" -ForegroundColor Cyan
    Write-Host "OutputPath: $OutputPath"
    Write-Host ""

    if (-not (Test-Path $OutputPath)) {
        throw "Output path does not exist: $OutputPath"
    }

    # ---------------------------------------------------------------
    # 1. Core Application Files
    # ---------------------------------------------------------------
    Write-Host "--- 1. Core Application Files ---" -ForegroundColor Cyan

    $exePath = Join-Path $OutputPath "ShiftManager.exe"
    if (Test-Path $exePath) {
        $exeSize = (Get-Item $exePath).Length
        if ($exeSize -gt 0) {
            Write-Pass "ShiftManager.exe exists ($([math]::Round($exeSize / 1KB)) KB)"
        } else {
            Write-Fail "ShiftManager.exe is 0 bytes"
            $failures.Add("ShiftManager.exe is 0 bytes")
        }
    } else {
        Write-Fail "ShiftManager.exe not found at: $exePath"
        $failures.Add("ShiftManager.exe missing")
    }

    $dllPath = Join-Path $OutputPath "ShiftManager.dll"
    if (Test-Path $dllPath) {
        $dllSize = (Get-Item $dllPath).Length
        if ($dllSize -gt 100KB) {
            Write-Pass "ShiftManager.dll exists ($([math]::Round($dllSize / 1KB)) KB)"
        } else {
            Write-Fail "ShiftManager.dll is only $([math]::Round($dllSize / 1KB)) KB (expected > 100 KB)"
            $failures.Add("ShiftManager.dll too small ($([math]::Round($dllSize / 1KB)) KB)")
        }
    } else {
        Write-Fail "ShiftManager.dll not found at: $dllPath"
        $failures.Add("ShiftManager.dll missing")
    }

    $webConfigPath = Join-Path $OutputPath "web.config"
    if (Test-Path $webConfigPath) {
        Write-Pass "web.config exists"
    } else {
        Write-Fail "web.config not found at: $webConfigPath"
        $failures.Add("web.config missing")
    }

    $appsettingsPath = Join-Path $OutputPath "appsettings.json"
    if (Test-Path $appsettingsPath) {
        Write-Pass "appsettings.json exists"
    } else {
        Write-Fail "appsettings.json not found at: $appsettingsPath"
        $failures.Add("appsettings.json missing")
    }

    Write-Host ""

    # ---------------------------------------------------------------
    # 2. .NET Runtime (self-contained)
    # ---------------------------------------------------------------
    Write-Host "--- 2. .NET Runtime (self-contained) ---" -ForegroundColor Cyan

    $runtimeFiles = @("coreclr.dll", "hostfxr.dll", "hostpolicy.dll", "System.Runtime.dll")
    foreach ($rtFile in $runtimeFiles) {
        $rtPath = Join-Path $OutputPath $rtFile
        if (Test-Path $rtPath) {
            Write-Pass "$rtFile exists"
        } else {
            Write-Fail "$rtFile not found — publish may not be self-contained"
            $failures.Add("$rtFile missing (runtime incomplete)")
        }
    }

    Write-Host ""

    # ---------------------------------------------------------------
    # 3. SQLite Native Library
    # ---------------------------------------------------------------
    Write-Host "--- 3. SQLite Native Library ---" -ForegroundColor Cyan

    $sqliteNative = Join-Path $OutputPath "e_sqlite3.dll"
    if (Test-Path $sqliteNative) {
        Write-Pass "e_sqlite3.dll exists"
    } else {
        Write-Fail "e_sqlite3.dll not found at: $sqliteNative"
        $failures.Add("e_sqlite3.dll missing (SQLite will not work)")
    }

    $sqliteManaged = Join-Path $OutputPath "Microsoft.Data.Sqlite.dll"
    if (Test-Path $sqliteManaged) {
        Write-Pass "Microsoft.Data.Sqlite.dll exists"
    } else {
        Write-Fail "Microsoft.Data.Sqlite.dll not found at: $sqliteManaged"
        $failures.Add("Microsoft.Data.Sqlite.dll missing")
    }

    Write-Host ""

    # ---------------------------------------------------------------
    # 4. Localization
    # ---------------------------------------------------------------
    Write-Host "--- 4. Localization ---" -ForegroundColor Cyan

    $heResourceDll = Join-Path $OutputPath "he-IL" "ShiftManager.resources.dll"
    if (Test-Path $heResourceDll) {
        Write-Pass "he-IL/ShiftManager.resources.dll exists"
    } else {
        Write-Fail "he-IL/ShiftManager.resources.dll not found at: $heResourceDll"
        $failures.Add("Hebrew satellite assembly missing (he-IL/ShiftManager.resources.dll)")
    }

    Write-Host ""

    # ---------------------------------------------------------------
    # 5. Static Assets (wwwroot)
    # ---------------------------------------------------------------
    Write-Host "--- 5. Static Assets (wwwroot) ---" -ForegroundColor Cyan

    $staticAssets = @(
        "wwwroot/css/site.css",
        "wwwroot/css/tokens.css",
        "wwwroot/js/site.js",
        "wwwroot/js/calendar-realtime.js",
        "wwwroot/lib/signalr/signalr.min.js",
        "wwwroot/lib/lucide/lucide.min.js",
        "wwwroot/images/shifty-logo.svg"
    )

    foreach ($asset in $staticAssets) {
        $assetPath = Join-Path $OutputPath $asset
        if (Test-Path $assetPath) {
            Write-Pass "$asset exists"
        } else {
            Write-Fail "$asset not found at: $assetPath"
            $failures.Add("$asset missing")
        }
    }

    Write-Host ""

    # ---------------------------------------------------------------
    # 6. No Dev Artifacts Leaked
    # ---------------------------------------------------------------
    Write-Host "--- 6. No Dev Artifacts Leaked ---" -ForegroundColor Cyan

    $devSettings = Join-Path $OutputPath "appsettings.Development.json"
    if (Test-Path $devSettings) {
        Write-Warn "appsettings.Development.json found — should not ship to production"
    } else {
        Write-Pass "No appsettings.Development.json (correct)"
    }

    $pdbFiles = @(Get-ChildItem -Path $OutputPath -Filter "*.pdb" -Recurse -ErrorAction SilentlyContinue)
    Write-Info "PDB files found: $($pdbFiles.Count)"

    $testResultFiles = @(Get-ChildItem -Path $OutputPath -Filter "test-results*.json" -ErrorAction SilentlyContinue)
    if ($testResultFiles.Count -gt 0) {
        Write-Warn "Found $($testResultFiles.Count) test-results*.json file(s) in root — should not ship"
    } else {
        Write-Pass "No test-results*.json in root (correct)"
    }

    Write-Host ""

    # ---------------------------------------------------------------
    # 7. File Count Sanity
    # ---------------------------------------------------------------
    Write-Host "--- 7. File Count Sanity ---" -ForegroundColor Cyan

    $allFiles = @(Get-ChildItem -Path $OutputPath -Recurse -File -ErrorAction SilentlyContinue)
    $totalCount = $allFiles.Count
    Write-Info "Total files: $totalCount"

    $wwwrootDir = Join-Path $OutputPath "wwwroot"
    if (Test-Path $wwwrootDir) {
        $wwwrootFiles = @(Get-ChildItem -Path $wwwrootDir -Recurse -File -ErrorAction SilentlyContinue)
        Write-Info "wwwroot files: $($wwwrootFiles.Count)"
    } else {
        Write-Info "wwwroot files: 0 (folder missing)"
    }

    $dllFiles = @(Get-ChildItem -Path $OutputPath -Filter "*.dll" -Recurse -ErrorAction SilentlyContinue)
    Write-Info "DLL count: $($dllFiles.Count)"

    Write-Host ""

    # ---------------------------------------------------------------
    # 8. Connection String Validation
    # ---------------------------------------------------------------
    Write-Host "--- 8. Connection String Validation ---" -ForegroundColor Cyan

    if (Test-Path $appsettingsPath) {
        try {
            $appConfig = Get-Content $appsettingsPath -Raw | ConvertFrom-Json

            $hasConnStrings = $null -ne $appConfig.ConnectionStrings
            $hasDefault = $hasConnStrings -and $null -ne $appConfig.ConnectionStrings.Default

            if (-not $hasConnStrings) {
                Write-Fail "appsettings.json missing ConnectionStrings section"
                $failures.Add("appsettings.json missing ConnectionStrings section")
            } elseif (-not $hasDefault) {
                Write-Fail "appsettings.json missing ConnectionStrings:Default key"
                $failures.Add("appsettings.json missing ConnectionStrings:Default key")
            } else {
                $connStr = $appConfig.ConnectionStrings.Default
                if ($connStr -match "Data Source") {
                    Write-Pass "ConnectionStrings:Default contains 'Data Source'"
                } else {
                    Write-Fail "ConnectionStrings:Default does not contain 'Data Source': $connStr"
                    $failures.Add("ConnectionStrings:Default malformed (no 'Data Source')")
                }
            }
        } catch {
            Write-Fail "Failed to parse appsettings.json: $_"
            $failures.Add("appsettings.json is not valid JSON")
        }
    } else {
        Write-Info "Skipping connection string check (appsettings.json missing - already reported)"
    }

    Write-Host ""

    # ---------------------------------------------------------------
    # DEBUGGING — summary info for operators
    # ---------------------------------------------------------------
    Write-Host "--- DEBUGGING ---" -ForegroundColor Cyan

    Write-Info "OutputPath: $OutputPath"
    Write-Info "Total files: $totalCount"

    $totalSizeBytes = ($allFiles | Measure-Object -Property Length -Sum).Sum
    if ($null -eq $totalSizeBytes) { $totalSizeBytes = 0 }
    $totalSizeMB = [math]::Round($totalSizeBytes / 1MB, 1)
    Write-Info "Total size: $totalSizeMB MB"

    Write-Host ""

    # ---------------------------------------------------------------
    # Final verdict
    # ---------------------------------------------------------------
    if ($failures.Count -gt 0) {
        Write-Host "=== RESULT ===" -ForegroundColor Red
        Write-Host ""
        Write-Fail "Verify-Build FAILED: $($failures.Count) critical issue(s) found:"
        foreach ($f in $failures) {
            Write-Host "     - $f" -ForegroundColor Red
        }
        Write-Host ""
        throw "Verify-Build FAILED: $($failures.Count) critical issue(s) found: $($failures -join '; ')"
    }

    Write-Host "=== RESULT ===" -ForegroundColor Green
    Write-Host ""
    Write-Pass "All build verification checks passed"
    Write-Host ""

    return $true

} catch {
    # Re-throw if it's our own summary throw; otherwise wrap it
    if ($_.Exception.Message -match "^Verify-Build FAILED") {
        throw
    }
    Write-Host ""
    Write-Host "[FAIL] Verify-Build encountered an unexpected error: $_" -ForegroundColor Red
    throw
}
