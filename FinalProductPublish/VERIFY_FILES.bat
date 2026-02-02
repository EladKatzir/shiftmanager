@echo off
REM ================================================================================
REM                    FILE VERIFICATION SCRIPT
REM ================================================================================
REM
REM This script verifies that all critical DLL files are present and correct
REM after transferring to an air-gapped machine.
REM
REM Run this BEFORE starting the application for the first time!
REM ================================================================================

echo.
echo ================================================================================
echo                    FILE VERIFICATION
echo ================================================================================
echo.
echo Checking critical DLL files...
echo.

set ERROR_COUNT=0
set WARNING_COUNT=0

REM ================================================================================
REM Check Critical DLL Files
REM ================================================================================

echo [1/5] Checking SixLabors.ImageSharp.dll...
if exist SixLabors.ImageSharp.dll (
    echo   [OK] SixLabors.ImageSharp.dll found
    for %%A in (SixLabors.ImageSharp.dll) do (
        echo   [INFO] Size: %%~zA bytes
    )
) else (
    echo   [ERROR] SixLabors.ImageSharp.dll MISSING!
    echo   [ERROR] This file is REQUIRED for image processing!
    set /a ERROR_COUNT+=1
)
echo.

echo [2/5] Checking ShiftManager.dll...
if exist ShiftManager.dll (
    echo   [OK] ShiftManager.dll found
    for %%A in (ShiftManager.dll) do (
        echo   [INFO] Size: %%~zA bytes
    )
) else (
    echo   [ERROR] ShiftManager.dll MISSING!
    echo   [ERROR] This is the main application file!
    set /a ERROR_COUNT+=1
)
echo.

echo [3/5] Checking e_sqlite3.dll...
if exist e_sqlite3.dll (
    echo   [OK] e_sqlite3.dll found
    for %%A in (e_sqlite3.dll) do (
        echo   [INFO] Size: %%~zA bytes
    )
) else (
    echo   [ERROR] e_sqlite3.dll MISSING!
    echo   [ERROR] This file is REQUIRED for database operations!
    set /a ERROR_COUNT+=1
)
echo.

echo [4/5] Checking ShiftManager.exe...
if exist ShiftManager.exe (
    echo   [OK] ShiftManager.exe found
) else (
    echo   [ERROR] ShiftManager.exe MISSING!
    echo   [ERROR] Cannot start application without this file!
    set /a ERROR_COUNT+=1
)
echo.

echo [5/5] Checking Microsoft.EntityFrameworkCore.Sqlite.dll...
if exist Microsoft.EntityFrameworkCore.Sqlite.dll (
    echo   [OK] Microsoft.EntityFrameworkCore.Sqlite.dll found
) else (
    echo   [WARNING] Microsoft.EntityFrameworkCore.Sqlite.dll MISSING!
    set /a WARNING_COUNT+=1
)
echo.

REM ================================================================================
REM Count Total DLL Files
REM ================================================================================

echo ================================================================================
echo Counting total DLL files...
echo.

REM Count DLLs
for /f %%A in ('dir /s /b *.dll 2^>nul ^| %SystemRoot%\System32\find.exe /c ".dll"') do set DLL_COUNT=%%A

echo   Total DLL files found: %DLL_COUNT%
echo   Expected range: 330-340 DLL files
echo.

if %DLL_COUNT% LSS 330 (
    echo   [WARNING] DLL count is lower than expected!
    echo   [WARNING] Some files may be missing!
    set /a WARNING_COUNT+=1
)

REM ================================================================================
REM Check if files are blocked (Windows Zone.Identifier)
REM ================================================================================

echo ================================================================================
echo Checking if files are blocked by Windows...
echo.

REM Try to check if SixLabors.ImageSharp.dll is blocked
powershell.exe -ExecutionPolicy Bypass -Command "if (Test-Path 'SixLabors.ImageSharp.dll:Zone.Identifier') { exit 1 } else { exit 0 }" 2>nul
if %ERRORLEVEL% EQU 1 (
    echo   [CRITICAL] Files are BLOCKED by Windows!
    echo   [CRITICAL] This will cause "Could not load file or assembly" errors!
    echo.
    echo   YOU MUST run UNBLOCK_FILES.bat before starting the application!
    echo.
    set /a ERROR_COUNT+=1
) else if %ERRORLEVEL% EQU 0 (
    echo   [OK] Files appear to be unblocked
    echo.
) else (
    echo   [INFO] Cannot check block status (PowerShell not available)
    echo   [INFO] If you get DLL errors, run UNBLOCK_FILES.bat
    echo.
)

REM ================================================================================
REM Check DLL Checksums (if available)
REM ================================================================================

if exist DLL_CHECKSUMS.txt (
    echo ================================================================================
    echo Checksum Verification Available
    echo ================================================================================
    echo.
    echo DLL_CHECKSUMS.txt found - you can verify file integrity!
    echo.
    echo To verify a DLL manually:
    echo   CertUtil -hashfile SixLabors.ImageSharp.dll SHA256
    echo   Then compare output with DLL_CHECKSUMS.txt
    echo.
    echo Critical DLLs with checksums available:
    echo   - SixLabors.ImageSharp.dll
    echo   - ShiftManager.dll
    echo   - e_sqlite3.dll
    echo   - Microsoft.EntityFrameworkCore.Sqlite.dll
    echo   - SQLitePCLRaw.provider.e_sqlite3.dll
    echo.
    echo If checksums don't match, recopy from USB!
    echo.
) else (
    echo ================================================================================
    echo [INFO] DLL_CHECKSUMS.txt not found (older deployment)
    echo [INFO] Cannot verify file integrity automatically
    echo.
)

REM ================================================================================
REM Summary and Recommendations
REM ================================================================================

echo ================================================================================
echo                    VERIFICATION SUMMARY
echo ================================================================================
echo.

if %ERROR_COUNT% GTR 0 (
    echo   [FAILED] %ERROR_COUNT% critical error(s) found!
    echo.
    echo   RECOMMENDATIONS:
    echo   1. Files appear to be missing or corrupted
    echo   2. Recopy the entire ProjectPublish folder from USB
    echo   3. Ensure you extract the ZIP file completely
    echo   4. Check if antivirus quarantined any files
    echo.
) else if %WARNING_COUNT% GTR 0 (
    echo   [WARNING] %WARNING_COUNT% warning(s) found
    echo.
    echo   The application may work, but some features might not be available.
    echo.
) else (
    echo   [SUCCESS] All critical files verified!
    echo.
    echo   Next steps:
    if not exist UNBLOCK_FILES.bat (
        echo   1. Run UNBLOCK_FILES.bat if available
        echo   2. Run START_HERE.bat to start the application
    ) else (
        echo   1. [IMPORTANT] Run UNBLOCK_FILES.bat FIRST!
        echo   2. Then run START_HERE.bat to start the application
    )
    echo.
)

echo ================================================================================
pause
exit /b %ERROR_COUNT%
