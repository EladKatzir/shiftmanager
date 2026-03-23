@echo off
REM ================================================================================
REM                      UNBLOCK FILES FOR AIR-GAPPED DEPLOYMENT
REM ================================================================================
REM
REM This script removes Windows Zone.Identifier alternate data streams that
REM cause "Could not load file or assembly" errors after USB transfer.
REM
REM IMPORTANT: Run this BEFORE starting the application for the first time!
REM ================================================================================

echo.
echo ================================================================================
echo                    UNBLOCKING FILES
echo ================================================================================
echo.
echo This will remove Windows security blocks from DLL files transferred via USB.
echo This is required for the application to run correctly.
echo.
echo Press any key to continue, or close this window to cancel...
pause >nul

echo.
echo Attempting to unblock files...
echo.

REM Method 1: Try PowerShell inline command (works even if script execution is disabled)
echo [Method 1] Trying PowerShell Unblock-File command...
echo Unblocking all files (DLLs, CSS, JS, etc.)...
powershell.exe -ExecutionPolicy Bypass -Command "Get-ChildItem -Path . -Recurse -File | ForEach-Object { try { Unblock-File -Path $_.FullName -ErrorAction SilentlyContinue; if ($_.Extension -in '.dll','.css','.js') { Write-Host '  Unblocked: ' $_.Name -ForegroundColor Yellow } } catch {} }" 2>nul

if %ERRORLEVEL% EQU 0 (
    echo [SUCCESS] Files unblocked using PowerShell
    goto :verify
)

echo [INFO] PowerShell method not available
echo.

REM Method 2: Try using streams.exe if available
echo [Method 2] Checking for streams.exe utility...
if exist streams.exe (
    echo [INFO] Found streams.exe, removing Zone.Identifier streams...
    streams.exe -s -d . 2>nul
    if %ERRORLEVEL% EQU 0 (
        echo [SUCCESS] Files unblocked using streams.exe
        goto :verify
    )
)

echo [INFO] streams.exe not available
echo.

REM Method 3: Pure cmd.exe approach — delete Zone.Identifier ADS without PowerShell (G-08 fix)
echo [Method 3] Attempting pure cmd unblock of critical DLL files...
echo.

REM List of critical DLLs that must be unblocked
set CRITICAL_DLLS=SixLabors.ImageSharp.dll ShiftManager.dll e_sqlite3.dll Microsoft.EntityFrameworkCore.dll Microsoft.EntityFrameworkCore.Sqlite.dll SQLitePCLRaw.provider.e_sqlite3.dll Microsoft.Data.Sqlite.dll

for %%D in (%CRITICAL_DLLS%) do (
    if exist "%%D" (
        REM G-08: Use cmd-native ADS deletion — works even when PowerShell is blocked by Group Policy
        echo. > "%%D:Zone.Identifier" 2>nul
        del /f "%%D:Zone.Identifier" 2>nul
        REM Verify removal
        dir /r "%%D" 2>nul | findstr /i "Zone.Identifier" >nul 2>&1
        if errorlevel 1 (
            echo   [OK] Processed: %%~nxD
        ) else (
            echo   [WARN] May still be blocked: %%~nxD
        )
    ) else (
        echo   [WARNING] %%D not found
    )
)

REM Also try to unblock all DLLs in directory using cmd approach
echo.
echo Attempting to unblock all DLL files...
for /r %%F in (*.dll) do (
    echo. > "%%F:Zone.Identifier" 2>nul
    del /f "%%F:Zone.Identifier" 2>nul
)

echo.

:verify
echo ================================================================================
echo                    VERIFICATION
echo ================================================================================
echo.
echo Checking critical files...
echo.
echo [1/2] Verifying CSS files (required for UI)...
if exist "wwwroot\css\site.css" (
    echo   [OK] site.css found
) else (
    echo   [ERROR] site.css MISSING! Sidebar UI will not work.
)

if exist "wwwroot\css\rtl.css" (
    echo   [OK] rtl.css found
) else (
    echo   [WARNING] rtl.css not found ^(optional, needed for Hebrew^)
)

if exist "wwwroot\js\site.js" (
    echo   [OK] site.js found
) else (
    echo   [ERROR] site.js MISSING! Core functionality will fail.
)

echo.
echo [2/2] Checking if critical DLL files are still blocked...
echo.

REM Check SixLabors.ImageSharp.dll specifically
set BLOCKED_COUNT=0

if exist SixLabors.ImageSharp.dll (
    powershell.exe -ExecutionPolicy Bypass -Command "if (Test-Path 'SixLabors.ImageSharp.dll:Zone.Identifier') { exit 1 } else { exit 0 }" 2>nul
    if %ERRORLEVEL% EQU 0 (
        echo   [OK] SixLabors.ImageSharp.dll is UNBLOCKED
    ) else (
        echo   [BLOCKED] SixLabors.ImageSharp.dll is still BLOCKED
        set /a BLOCKED_COUNT+=1
    )
) else (
    echo   [ERROR] SixLabors.ImageSharp.dll NOT FOUND!
    set /a BLOCKED_COUNT+=1
)

echo.

if %BLOCKED_COUNT% GTR 0 (
    echo ================================================================================
    echo                    MANUAL UNBLOCK REQUIRED
    echo ================================================================================
    echo.
    echo Some files are still blocked. Please unblock them manually:
    echo.
    echo 1. Right-click on the ProjectPublish folder
    echo 2. Select Properties
    echo 3. Check "Unblock" at the bottom of the General tab
    echo 4. Click "Apply to all files in this folder"
    echo 5. Click OK
    echo.
    echo OR
    echo.
    echo Open PowerShell as Administrator and run:
    echo   cd "%CD%"
    echo   Get-ChildItem -Recurse ^| Unblock-File
    echo.
    echo ================================================================================
    pause
    exit /b 1
) else (
    echo ================================================================================
    echo                    SUCCESS!
    echo ================================================================================
    echo.
    echo All critical files have been unblocked.
    echo You can now run START_HERE.bat to launch the application.
    echo.
    pause
    exit /b 0
)
