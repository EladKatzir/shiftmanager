@echo off
REM Griffin ADFS Diagnostic Script
REM This script checks the GriffinConfig database to identify the 404 redirect issue

echo ========================================
echo Griffin ADFS Configuration Diagnostic
echo ========================================
echo.

echo Checking if database exists...
if not exist "app.db" (
    echo ERROR: app.db not found in current directory
    echo Please run this script from the ShiftManager root directory
    pause
    exit /b 1
)

echo.
echo Querying GriffinConfig table...
echo.

powershell -NoProfile -Command "try { Add-Type -Path 'Microsoft.Data.Sqlite.dll' -ErrorAction Stop; $conn = New-Object Microsoft.Data.Sqlite.SqliteConnection('Data Source=app.db'); $conn.Open(); $cmd = $conn.CreateCommand(); $cmd.CommandText = 'SELECT Id, CompanyId, IsEnabled, BaseUrl, TokenConsumerUrl, TimeoutSeconds FROM GriffinConfigs'; $reader = $cmd.ExecuteReader(); Write-Host 'GriffinConfig Records:'; Write-Host '----------------------------------------'; $found = $false; while ($reader.Read()) { $found = $true; Write-Host ('ID: ' + $reader['Id']); Write-Host ('CompanyId: ' + $reader['CompanyId']); Write-Host ('Enabled: ' + $reader['IsEnabled']); Write-Host ('BaseUrl: ' + $reader['BaseUrl']); Write-Host ('TokenConsumerUrl: ' + $reader['TokenConsumerUrl']); Write-Host ('TimeoutSeconds: ' + $reader['TimeoutSeconds']); Write-Host '----------------------------------------'; } $reader.Close(); $conn.Close(); if (-not $found) { Write-Host 'No GriffinConfig records found!'; Write-Host 'You need to configure Griffin ADFS at http://localhost:5000/Owner/GriffinConfig'; } } catch { Write-Host 'ERROR: Could not query database using Microsoft.Data.Sqlite'; Write-Host $_.Exception.Message; Write-Host ''; Write-Host 'Trying alternative method with sqlite3.exe...'; }" 2>nul

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Attempting to use sqlite3.exe if available...
    where sqlite3.exe >nul 2>&1
    if %ERRORLEVEL% EQU 0 (
        echo.
        sqlite3.exe app.db "SELECT 'ID: ' || Id || char(10) || 'CompanyId: ' || CompanyId || char(10) || 'Enabled: ' || Enabled || char(10) || 'BaseUrl: ' || BaseUrl || char(10) || 'TokenConsumerUrl: ' || TokenConsumerUrl || char(10) || 'TimeoutSeconds: ' || TimeoutSeconds || char(10) || '----------------------------------------' FROM GriffinConfigs"
    ) else (
        echo.
        echo ALTERNATIVE: Manual database inspection required
        echo.
        echo Please check the TokenConsumerUrl value manually:
        echo 1. Navigate to http://localhost:5000/Owner/GriffinConfig
        echo 2. Check the "Callback URL" field
        echo.
        echo EXPECTED FORMAT: http://localhost:5000/Auth/GriffinCallback
        echo.
        echo Common issues to look for:
        echo   - Missing http:// or https:// scheme
        echo   - Typo in path (e.g., /auth instead of /Auth)
        echo   - Missing forward slash before GriffinCallback
        echo   - Extra spaces or special characters
    )
)

echo.
echo ========================================
echo Diagnostic Analysis
echo ========================================
echo.
echo Based on your error:
echo   /authentication/https%%3A%%2f%%2f7108dev.d8200.mil%%2fauth%%GriffinCallBack...
echo.
echo DIAGNOSIS: The redirect is being treated as a RELATIVE path instead of absolute.
echo.
echo LIKELY CAUSES (in order of probability):
echo.
echo 1. TokenConsumerUrl is MISSING the http:// or https:// scheme
echo    Example WRONG: /Auth/GriffinCallback
echo    Example RIGHT: http://localhost:5000/Auth/GriffinCallback
echo.
echo 2. TokenConsumerUrl has a TYPO in the path
echo    Your error shows: /auth%%GriffinCallBack  (lowercase 'a', missing '/')
echo    Should be:        /Auth/GriffinCallback    (capital 'A', with '/')
echo.
echo 3. TokenConsumerUrl uses wrong domain
echo    Should match your air-gapped machine: http://localhost:5000/Auth/GriffinCallback
echo.
echo ========================================
echo Recommended Fix
echo ========================================
echo.
echo STEP 1: Go to http://localhost:5000/Owner/GriffinConfig
echo.
echo STEP 2: Set the "Callback URL" field to EXACTLY:
echo         http://localhost:5000/Auth/GriffinCallback
echo.
echo         (Make sure it starts with http:// or https://)
echo.
echo STEP 3: Click "Test Connection" to verify Griffin ADFS is reachable
echo.
echo STEP 4: Click "Save" to update the configuration
echo.
echo STEP 5: Try "Login with ADFS" again
echo.
echo ========================================
pause
