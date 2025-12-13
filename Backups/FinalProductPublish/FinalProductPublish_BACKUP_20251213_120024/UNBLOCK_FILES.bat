@echo off
setlocal EnableDelayedExpansion
REM ================================================================================
REM                      UNBLOCK FILES FOR AIR-GAPPED DEPLOYMENT
REM ================================================================================
REM
REM This script removes Windows Zone.Identifier alternate data streams that
REM cause "Could not load file or assembly" errors after USB transfer.
REM
REM IMPORTANT: Run this BEFORE starting the application for the first time!
REM ================================================================================

REM Ensure we are running from this script's directory
set "SCRIPT_DIR=%~dp0"
cd /d "%SCRIPT_DIR%" >nul 2>&1

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

REM ----------------------------------------------------------------------------
REM Detect PowerShell availability once
REM ----------------------------------------------------------------------------
set "HAS_PS=0"
where powershell.exe >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    set "HAS_PS=1"
)

REM ----------------------------------------------------------------------------
REM Method 1: PowerShell Unblock-File over entire tree
REM ----------------------------------------------------------------------------
if "%HAS_PS%"=="1" (
    echo [Method 1] PowerShell Unblock-File on all files (this may take some time)...
    powershell.exe -ExecutionPolicy Bypass -Command ^
        "Get-ChildItem -Path . -Recurse -File | ForEach-Object { try { Unblock-File -Path $_.FullName -ErrorAction SilentlyContinue } catch {} }" 2>nul

    if %ERRORLEVEL% EQU 0 (
        echo [SUCCESS] Files unblocked using PowerShell Unblock-File
        goto :verify
    ) else (
        echo [INFO] PowerShell Unblock-File method did not complete successfully.
        echo        Will try alternative methods.
        echo.
    )
) else (
    echo [INFO] PowerShell is not available - skipping Method 1.
    echo.
)

REM ----------------------------------------------------------------------------
REM Method 2: streams.exe (if available)
REM ----------------------------------------------------------------------------
echo [Method 2] Checking for streams.exe utility...
if exist streams.exe (
    echo [INFO] Found streams.exe, removing Zone.Identifier streams recursively...
    streams.exe -s -d . 2>nul
    if %ERRORLEVEL% EQU 0 (
        echo [SUCCESS] Files unblocked using streams.exe
        goto :verify
    ) else (
        echo [INFO] streams.exe did not complete successfully.
        echo.
    )
) else (
    echo [INFO] streams.exe not available - skipping Method 2.
    echo.
)

REM ----------------------------------------------------------------------------
REM Method 3: Manual unblock of critical DLLs (PowerShell only if available)
REM ----------------------------------------------------------------------------
echo [Method 3] Attempting manual unblock of critical DLL files...
echo.

REM List of critical DLLs that must be unblocked
set CRITICAL_DLLS=SixLabors.ImageSharp.dll ShiftManager.dll e_sqlite3.dll Microsoft.EntityFrameworkCore.dll Microsoft.EntityFrameworkCore.Sqlite.dll SQLitePCLRaw.provider.e_sqlite3.dll Microsoft.Data.Sqlite.dll

if "%HAS_PS%"=="1" (
    for %%D in (%CRITICAL_DLLS%) do (
        if exist "%%D" (
            powershell.exe -ExecutionPolicy Bypass -Command ^
                "if (Test-Path '%%D:Zone.Identifier') { Remove-Item '%%D:Zone.Identifier' -Force }" 2>nul
            if !ERRORLEVEL! EQU 0 (
                echo   [OK] Unblocked %%D (or no block was present)
            ) else (
                echo   [WARNING] Could not fully unblock %%D (check manually if issues persist)
            )
        ) else (
            echo   [WARNING] %%D not found
        )
    )
) else (
    echo   [INFO] PowerShell is not available, cannot run manual ADS removal.
    echo   [INFO] If you still get DLL load errors, follow the manual steps below.
)

echo.

:verify
echo ================================================================================
echo                    VERIFICATION
echo ================================================================================
echo.
echo Checking if critical DLL files are still blocked...
echo.

set "BLOCKED_COUNT=0"

REM If we have PowerShell, we can verify Zone.Identifier. Otherwise, we can only give guidance.
if "%HAS_PS%"=="1" (
    if exist SixLabors.ImageSharp.dll (
        powershell.exe -ExecutionPolicy Bypass -Command ^
            "if (Test-Path 'SixLabors.ImageSharp.dll:Zone.Identifier') { exit 1 } else { exit 0 }" 2>nul

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
) else (
    echo   [INFO] PowerShell is not available - cannot automatically detect blocked status.
    echo   [INFO] If you encounter 'Could not load file or assembly' errors, follow the manual unblock steps below.
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
    if "%HAS_PS%"=="1" (
        echo Open PowerShell as Administrator and run:
        echo   cd "%CD%"
        echo   Get-ChildItem -Recurse ^| Unblock-File
        echo.
    ) else (
        echo PowerShell is not available on this machine, so use the folder "Unblock"
        echo option in the Properties dialog as described above.
        echo.
    )
    echo ================================================================================
    pause
    exit /b 1
) else (
    echo ================================================================================
    echo                    SUCCESS!
    echo ================================================================================
    echo.
    if "%HAS_PS%"=="1" (
        echo All critical files appear to be unblocked.
    ) else (
        echo Automatic verification is limited (no PowerShell), but unblocking steps
        echo have been attempted. If you still get DLL errors, use the manual unblock
        echo instructions described above.
    )
    echo.
    echo You can now run your main startup script (e.g. START_HERE.bat).
    echo.
    pause
    exit /b 0
)

endlocal
