@echo off
echo ================================================================
echo   ShiftManager - IIS HTTP 500.30 Fix Script
echo   Run this script AS ADMINISTRATOR on the IIS server
echo ================================================================
echo.

:: Check for administrator privileges
net session >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] This script requires Administrator privileges.
    echo         Right-click and select "Run as administrator"
    echo.
    pause
    exit /b 1
)

REM Detect the app directory (where this script lives)
set "APP_DIR=%~dp0"
echo App directory: %APP_DIR%
echo.

echo [Step 1/5] Creating required directories...
if not exist "%APP_DIR%logs" (
    mkdir "%APP_DIR%logs"
    echo   Created: %APP_DIR%logs
) else (
    echo   Already exists: %APP_DIR%logs
)
if not exist "%APP_DIR%Backups" (
    mkdir "%APP_DIR%Backups"
    echo   Created: %APP_DIR%Backups
) else (
    echo   Already exists: %APP_DIR%Backups
)
if not exist "%APP_DIR%DataProtection-Keys" (
    mkdir "%APP_DIR%DataProtection-Keys"
    echo   Created: %APP_DIR%DataProtection-Keys
) else (
    echo   Already exists: %APP_DIR%DataProtection-Keys
)
echo.

echo [Step 2/5] Unblocking DLL files (USB transfer protection)...
powershell.exe -ExecutionPolicy Bypass -Command "Get-ChildItem -Path '%APP_DIR%' -Recurse | Unblock-File" 2>nul
if %ERRORLEVEL% EQU 0 (
    echo   DLLs unblocked successfully
) else (
    echo   WARNING: Could not unblock files via PowerShell
    echo   Try manually: Right-click folder ^> Properties ^> Unblock
)
echo.

echo [Step 3/5] Setting IIS App Pool permissions...
echo   Granting write permissions to IIS_IUSRS on app directory...
icacls "%APP_DIR%" /grant "IIS_IUSRS:(OI)(CI)M" /T /Q 2>nul
if %ERRORLEVEL% EQU 0 (
    echo   Permissions set for IIS_IUSRS
) else (
    echo   WARNING: Could not set permissions for IIS_IUSRS
)
icacls "%APP_DIR%" /grant "IUSR:(OI)(CI)M" /T /Q 2>nul
echo   Also granted permissions for IUSR
echo.
echo   NOTE: If your App Pool uses a custom identity, also run:
echo   icacls "%APP_DIR%" /grant "IIS AppPool\YOUR_POOL_NAME:(OI)(CI)M" /T
echo.

echo [Step 4/5] Checking configuration...
echo   Verifying appsettings.Production.json...
if exist "%APP_DIR%appsettings.Production.json" (
    echo   Found appsettings.Production.json
    findstr /C:"C:\\ShiftManager\\Data\\app.db" "%APP_DIR%appsettings.Production.json" >nul 2>&1
    if %ERRORLEVEL% EQU 0 (
        echo.
        echo   *** WARNING: Database path points to C:\ShiftManager\Data\app.db ***
        echo   *** This directory may not exist! ***
        echo   *** SOLUTION: Edit appsettings.Production.json and change the connection string to: ***
        echo   ***   "Default": "Data Source=app.db"  ***
        echo   *** This will create the database in the app folder itself. ***
        echo.
    ) else (
        echo   Connection string looks OK
    )
) else (
    echo   WARNING: appsettings.Production.json not found!
    echo   The app will use default settings which may not work in production.
)
echo.

echo [Step 5/5] Checking critical files...
set "MISSING=0"
if not exist "%APP_DIR%ShiftManager.exe" (
    echo   MISSING: ShiftManager.exe
    set "MISSING=1"
)
if not exist "%APP_DIR%ShiftManager.dll" (
    echo   MISSING: ShiftManager.dll
    set "MISSING=1"
)
if not exist "%APP_DIR%web.config" (
    echo   MISSING: web.config
    set "MISSING=1"
)
if not exist "%APP_DIR%appsettings.json" (
    echo   MISSING: appsettings.json
    set "MISSING=1"
)
if not exist "%APP_DIR%e_sqlite3.dll" (
    echo   MISSING: e_sqlite3.dll (SQLite native library)
    set "MISSING=1"
)
if not exist "%APP_DIR%SixLabors.ImageSharp.dll" (
    echo   MISSING: SixLabors.ImageSharp.dll
    set "MISSING=1"
)
if "%MISSING%"=="0" (
    echo   All critical files present
)
echo.

echo ================================================================
echo   Fix complete! Now:
echo.
echo   1. Open IIS Manager
echo   2. Select the ShiftManager site
echo   3. Click "Restart" on the right panel
echo      (or run: iisreset)
echo.
echo   If the error persists, check:
echo   - %APP_DIR%logs\  for stdout log files
echo   - %APP_DIR%startup-error.txt  for startup crash details
echo   - Windows Event Viewer ^> Application for .NET errors
echo ================================================================
echo.
pause
