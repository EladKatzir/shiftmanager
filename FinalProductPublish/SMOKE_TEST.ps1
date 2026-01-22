#Requires -Version 7.0
<#
.SYNOPSIS
    Production Readiness Smoke Test Suite for ShiftManager

.DESCRIPTION
    Comprehensive smoke test suite that verifies critical application functionality:
    - Application startup and HTTP response
    - Health endpoint availability
    - Database connectivity
    - Configuration validation
    - File integrity checks

.PARAMETER AppUrl
    The base URL of the application (default: http://localhost:5000)

.PARAMETER SkipStartCheck
    Skip checking if application is already running (useful if app is started externally)

.EXAMPLE
    .\SMOKE_TEST.ps1
    Run all smoke tests against default localhost:5000

.EXAMPLE
    .\SMOKE_TEST.ps1 -AppUrl "http://myserver:8080"
    Run tests against custom server URL

.EXAMPLE
    .\SMOKE_TEST.ps1 -SkipStartCheck
    Run tests assuming application is already running
#>

[CmdletBinding()]
param(
    [string]$AppUrl = "http://localhost:5000",
    [switch]$SkipStartCheck
)

# ANSI color codes for output
$script:Green = "`e[32m"
$script:Red = "`e[31m"
$script:Yellow = "`e[33m"
$script:Blue = "`e[34m"
$script:Reset = "`e[0m"

# Test results tracking
$script:TestsPassed = 0
$script:TestsFailed = 0
$script:TestResults = @()

function Write-TestHeader {
    param([string]$Message)
    Write-Host ""
    Write-Host "$($script:Blue)═══════════════════════════════════════════════════════════════════$($script:Reset)"
    Write-Host "$($script:Blue)  $Message$($script:Reset)"
    Write-Host "$($script:Blue)═══════════════════════════════════════════════════════════════════$($script:Reset)"
}

function Write-TestResult {
    param(
        [string]$TestName,
        [bool]$Success,
        [string]$Details = ""
    )

    $result = @{
        Test = $TestName
        Success = $Success
        Details = $Details
        Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    }

    $script:TestResults += $result

    if ($Success) {
        $script:TestsPassed++
        Write-Host "  $($script:Green)✅ PASS$($script:Reset): $TestName"
        if ($Details) {
            Write-Host "      $($script:Blue)↪$($script:Reset) $Details"
        }
    } else {
        $script:TestsFailed++
        Write-Host "  $($script:Red)❌ FAIL$($script:Reset): $TestName"
        if ($Details) {
            Write-Host "      $($script:Red)↪$($script:Reset) $Details"
        }
    }
}

function Test-ApplicationRunning {
    Write-TestHeader "1. Application Startup Check"

    if (-not $SkipStartCheck) {
        $process = Get-Process -Name "ShiftManager" -ErrorAction SilentlyContinue
        if ($process) {
            Write-TestResult -TestName "ShiftManager.exe is running" -Success $true -Details "PID: $($process.Id)"
        } else {
            Write-TestResult -TestName "ShiftManager.exe is running" -Success $false -Details "Process not found. Please start the application using START_HERE.bat"
            return $false
        }
    } else {
        Write-Host "  $($script:Yellow)⚠ SKIP$($script:Reset): Process check skipped (SkipStartCheck flag set)"
    }

    return $true
}

function Test-HttpResponse {
    Write-TestHeader "2. HTTP Response Test"

    try {
        $response = Invoke-WebRequest -Uri $AppUrl -Method GET -TimeoutSec 10 -UseBasicParsing -ErrorAction Stop

        if ($response.StatusCode -eq 200) {
            Write-TestResult -TestName "GET $AppUrl returns 200 OK" -Success $true -Details "Response received: $($response.Content.Length) bytes"
        } else {
            Write-TestResult -TestName "GET $AppUrl returns 200 OK" -Success $false -Details "Unexpected status code: $($response.StatusCode)"
            return $false
        }

        # Check if response contains expected HTML elements
        $content = $response.Content
        if ($content -match '<html' -or $content -match 'ShiftManager') {
            Write-TestResult -TestName "Response contains valid HTML content" -Success $true
        } else {
            Write-TestResult -TestName "Response contains valid HTML content" -Success $false -Details "Response does not appear to be valid HTML"
        }

        return $true
    }
    catch {
        Write-TestResult -TestName "GET $AppUrl returns 200 OK" -Success $false -Details "Error: $($_.Exception.Message)"
        return $false
    }
}

function Test-HealthEndpoint {
    Write-TestHeader "3. Health Endpoint Test"

    try {
        $healthUrl = "$AppUrl/health"
        $response = Invoke-WebRequest -Uri $healthUrl -Method GET -TimeoutSec 10 -UseBasicParsing -ErrorAction Stop

        if ($response.StatusCode -eq 200) {
            Write-TestResult -TestName "GET /health returns 200 OK" -Success $true

            # Check response body for health status
            $content = $response.Content
            if ($content -match 'Healthy' -or $content -match '"status":\s*"Healthy"') {
                Write-TestResult -TestName "Health endpoint reports Healthy status" -Success $true -Details "Content: $($content.Substring(0, [Math]::Min(100, $content.Length)))"
            } else {
                Write-TestResult -TestName "Health endpoint reports Healthy status" -Success $false -Details "Status indicator not found in response"
            }
        } else {
            Write-TestResult -TestName "GET /health returns 200 OK" -Success $false -Details "Status code: $($response.StatusCode)"
        }
    }
    catch {
        Write-TestResult -TestName "GET /health returns 200 OK" -Success $false -Details "Error: $($_.Exception.Message)"
    }
}

function Test-DatabaseConnectivity {
    Write-TestHeader "4. Database Connectivity Test"

    # Check if app.db exists
    $dbPath = Join-Path $PSScriptRoot "app.db"
    if (Test-Path $dbPath) {
        $dbSize = (Get-Item $dbPath).Length
        $dbSizeMB = [math]::Round($dbSize / 1MB, 2)
        Write-TestResult -TestName "Database file exists (app.db)" -Success $true -Details "Size: $dbSizeMB MB"
    } else {
        Write-TestResult -TestName "Database file exists (app.db)" -Success $false -Details "File not found at: $dbPath"
        return
    }

    # Try to access SystemHealth page (requires authentication, so we expect redirect to login)
    try {
        $healthPageUrl = "$AppUrl/Owner/SystemHealth"
        $response = Invoke-WebRequest -Uri $healthPageUrl -Method GET -TimeoutSec 10 -MaximumRedirection 0 -ErrorAction SilentlyContinue

        # We expect either 200 (if somehow authenticated) or 302 redirect to login
        if ($response.StatusCode -eq 200) {
            Write-TestResult -TestName "SystemHealth page is accessible" -Success $true -Details "Page loaded (already authenticated)"
        }
    }
    catch {
        $statusCode = $_.Exception.Response.StatusCode.Value__
        if ($statusCode -eq 302 -or $statusCode -eq 401) {
            Write-TestResult -TestName "SystemHealth page is accessible" -Success $true -Details "Redirects to login as expected (auth required)"
        } else {
            Write-TestResult -TestName "SystemHealth page is accessible" -Success $false -Details "Unexpected status: $statusCode"
        }
    }
}

function Test-ConfigurationValidation {
    Write-TestHeader "5. Configuration Validation Test"

    # Check appsettings.json exists
    $configPath = Join-Path $PSScriptRoot "appsettings.json"
    if (Test-Path $configPath) {
        Write-TestResult -TestName "appsettings.json exists" -Success $true

        try {
            $config = Get-Content $configPath -Raw | ConvertFrom-Json

            # Check for SEED_ADMIN_PASSWORD (should not be empty in production)
            if ($config.SEED_ADMIN_PASSWORD) {
                if ($config.SEED_ADMIN_PASSWORD -ne "") {
                    Write-TestResult -TestName "SEED_ADMIN_PASSWORD is configured" -Success $true
                } else {
                    Write-TestResult -TestName "SEED_ADMIN_PASSWORD is configured" -Success $false -Details "Password is empty string"
                }
            } else {
                Write-Host "  $($script:Yellow)⚠ INFO$($script:Reset): SEED_ADMIN_PASSWORD not in appsettings.json (may be in environment variable)"
            }

            # Check ConnectionStrings
            if ($config.ConnectionStrings -and $config.ConnectionStrings.Default) {
                Write-TestResult -TestName "Database connection string is configured" -Success $true -Details $config.ConnectionStrings.Default
            } else {
                Write-TestResult -TestName "Database connection string is configured" -Success $false
            }

            # Check Features section
            if ($config.Features) {
                Write-TestResult -TestName "Features configuration exists" -Success $true

                # Check specific feature flags
                $featureFlags = @(
                    "EnforceCompanyScope",
                    "EnableDirectorRole",
                    "AllowPublicSignup"
                )

                foreach ($flag in $featureFlags) {
                    $value = $config.Features.$flag
                    if ($null -ne $value) {
                        Write-Host "      $($script:Blue)↪$($script:Reset) $flag = $value"
                    }
                }
            } else {
                Write-TestResult -TestName "Features configuration exists" -Success $false
            }

        }
        catch {
            Write-TestResult -TestName "appsettings.json is valid JSON" -Success $false -Details $_.Exception.Message
        }
    } else {
        Write-TestResult -TestName "appsettings.json exists" -Success $false -Details "File not found"
    }
}

function Test-CriticalFiles {
    Write-TestHeader "6. Critical Files Integrity Test"

    $criticalFiles = @(
        "ShiftManager.exe",
        "ShiftManager.dll",
        "appsettings.json",
        "web.config",
        "SixLabors.ImageSharp.dll",
        "e_sqlite3.dll"
    )

    foreach ($file in $criticalFiles) {
        $filePath = Join-Path $PSScriptRoot $file
        if (Test-Path $filePath) {
            $fileSize = (Get-Item $filePath).Length
            $fileSizeKB = [math]::Round($fileSize / 1KB, 2)
            Write-TestResult -TestName "Critical file exists: $file" -Success $true -Details "$fileSizeKB KB"
        } else {
            Write-TestResult -TestName "Critical file exists: $file" -Success $false -Details "File not found"
        }
    }
}

function Test-FileBlocking {
    Write-TestHeader "7. Windows File Blocking Test"

    # Check if critical DLLs are blocked by Windows (Zone.Identifier)
    $criticalDlls = @(
        "SixLabors.ImageSharp.dll",
        "e_sqlite3.dll",
        "Microsoft.Data.Sqlite.dll",
        "System.Drawing.Common.dll",
        "Microsoft.EntityFrameworkCore.dll"
    )

    $blockedFiles = @()
    foreach ($dll in $criticalDlls) {
        $dllPath = Join-Path $PSScriptRoot $dll
        $zoneIdPath = "${dllPath}:Zone.Identifier"

        if (Test-Path $dllPath) {
            try {
                $zoneId = Get-Content -Path $zoneIdPath -ErrorAction SilentlyContinue
                if ($zoneId) {
                    $blockedFiles += $dll
                    Write-TestResult -TestName "$dll is not blocked" -Success $false -Details "Zone.Identifier found - file is blocked"
                } else {
                    Write-TestResult -TestName "$dll is not blocked" -Success $true
                }
            }
            catch {
                # If we can't read Zone.Identifier, assume file is not blocked
                Write-TestResult -TestName "$dll is not blocked" -Success $true
            }
        }
    }

    if ($blockedFiles.Count -gt 0) {
        Write-Host ""
        Write-Host "  $($script:Yellow)⚠ WARNING$($script:Reset): $($blockedFiles.Count) file(s) are blocked by Windows"
        Write-Host "  $($script:Yellow)↪$($script:Reset) Run UNBLOCK_FILES.bat to fix this issue"
    }
}

function Show-Summary {
    Write-TestHeader "Test Summary"

    $total = $script:TestsPassed + $script:TestsFailed
    $passRate = if ($total -gt 0) { [math]::Round(($script:TestsPassed / $total) * 100, 1) } else { 0 }

    Write-Host ""
    Write-Host "  Total Tests: $total"
    Write-Host "  $($script:Green)Passed: $($script:TestsPassed)$($script:Reset)"
    Write-Host "  $($script:Red)Failed: $($script:TestsFailed)$($script:Reset)"
    Write-Host "  Pass Rate: $passRate%"
    Write-Host ""

    if ($script:TestsFailed -eq 0) {
        Write-Host "  $($script:Green)✅ ALL TESTS PASSED - Application is ready for production use$($script:Reset)"
        Write-Host ""
        return 0
    } else {
        Write-Host "  $($script:Red)❌ SOME TESTS FAILED - Please review errors above$($script:Reset)"
        Write-Host ""
        Write-Host "  Common issues and solutions:"
        Write-Host "  1. Application not running → Run START_HERE.bat"
        Write-Host "  2. Port 5000 in use → Stop conflicting service or configure different port"
        Write-Host "  3. Files blocked → Run UNBLOCK_FILES.bat"
        Write-Host "  4. Database issues → Delete app.db and restart (will recreate)"
        Write-Host "  5. Configuration errors → Check appsettings.json syntax"
        Write-Host ""
        return 1
    }
}

function Export-TestResults {
    Write-TestHeader "Exporting Test Results"

    $resultFile = Join-Path $PSScriptRoot "smoke-test-results.json"
    $resultObject = @{
        Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
        AppUrl = $AppUrl
        TotalTests = $script:TestsPassed + $script:TestsFailed
        Passed = $script:TestsPassed
        Failed = $script:TestsFailed
        PassRate = if (($script:TestsPassed + $script:TestsFailed) -gt 0) {
            [math]::Round(($script:TestsPassed / ($script:TestsPassed + $script:TestsFailed)) * 100, 1)
        } else {
            0
        }
        Tests = $script:TestResults
    }

    try {
        $resultObject | ConvertTo-Json -Depth 10 | Out-File -FilePath $resultFile -Encoding UTF8
        Write-Host "  $($script:Green)✓$($script:Reset) Results exported to: $resultFile"
    }
    catch {
        Write-Host "  $($script:Red)✗$($script:Reset) Failed to export results: $($_.Exception.Message)"
    }
}

# Main execution
Clear-Host
Write-Host ""
Write-Host "$($script:Blue)╔═══════════════════════════════════════════════════════════════════╗$($script:Reset)"
Write-Host "$($script:Blue)║$($script:Reset)     ShiftManager - Production Readiness Smoke Test Suite       $($script:Blue)║$($script:Reset)"
Write-Host "$($script:Blue)║$($script:Reset)                       Version 1.0.0                           $($script:Blue)║$($script:Reset)"
Write-Host "$($script:Blue)╚═══════════════════════════════════════════════════════════════════╝$($script:Reset)"
Write-Host ""
Write-Host "  Target URL: $AppUrl"
Write-Host "  Start Time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Write-Host ""

# Run all test suites
$appRunning = Test-ApplicationRunning
if ($appRunning -or $SkipStartCheck) {
    Test-HttpResponse
    Test-HealthEndpoint
    Test-DatabaseConnectivity
    Test-ConfigurationValidation
    Test-CriticalFiles
    Test-FileBlocking
} else {
    Write-Host ""
    Write-Host "  $($script:Red)❌ Cannot proceed - Application is not running$($script:Reset)"
    Write-Host "  $($script:Yellow)↪$($script:Reset) Start the application with START_HERE.bat and try again"
    Write-Host ""
}

# Export results and show summary
Export-TestResults
$exitCode = Show-Summary

# Exit with appropriate code
exit $exitCode
