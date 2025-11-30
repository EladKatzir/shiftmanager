@echo off
setlocal
color 0E
title IIS 500 Error - Quick Fix Tool

echo.
echo ============================================================
echo IIS 500 ERROR - QUICK FIX
echo ============================================================
echo.
echo This script will apply common fixes for IIS 500 errors
echo.

set "APP_DIR=%~dp0"
cd /d "%APP_DIR%"

echo [FIX 1] Creating logs folder with proper permissions...
echo ============================================================
if not exist logs mkdir logs
icacls logs /grant "IIS_IUSRS:(OI)(CI)M" /T /Q
icacls logs /grant "IUSR:(OI)(CI)M" /T /Q
icacls logs /grant "Everyone:(OI)(CI)M" /T /Q
echo [OK] Logs folder configured
echo.

echo [FIX 2] Unblocking all DLLs and files...
echo ============================================================
echo This may take 30 seconds...
powershell -ExecutionPolicy Bypass -Command "Get-ChildItem -Path . -Recurse | Unblock-File"
echo [OK] All files unblocked
echo.

echo [FIX 3] Setting folder permissions for IIS...
echo ============================================================
icacls . /grant "IIS_IUSRS:(OI)(CI)RX" /T /Q
icacls . /grant "IIS_IUSRS:(OI)(CI)M" /Q
icacls . /grant "IUSR:(OI)(CI)RX" /T /Q
icacls . /grant "IUSR:(OI)(CI)M" /Q
echo [OK] Permissions set
echo.

echo [FIX 4] Setting database permissions (if exists)...
echo ============================================================
if exist app.db (
    icacls app.db /grant "IIS_IUSRS:M" /Q
    icacls app.db /grant "IUSR:M" /Q
    echo [OK] Database permissions set
) else (
    echo [INFO] No database yet (will be created on first run)
)
echo.

echo [FIX 5] Setting wwwroot permissions...
echo ============================================================
if exist wwwroot (
    icacls wwwroot /grant "IIS_IUSRS:(OI)(CI)RX" /T /Q
    icacls wwwroot\feedback /grant "IIS_IUSRS:(OI)(CI)M" /T /Q 2>nul
    echo [OK] wwwroot permissions set
)
echo.

echo [FIX 6] Restarting IIS Application Pool...
echo ============================================================
echo [INFO] Please manually restart your application pool in IIS Manager
echo        Or run: appcmd recycle apppool /apppool.name:"YourAppPoolName"
echo.

echo [FIX 7] Testing application startup...
echo ============================================================
start /B ShiftManager.exe > quick_fix_test.log 2>&1
timeout /t 8 /nobreak >nul
taskkill /F /IM ShiftManager.exe >nul 2>&1

findstr /I /C:"now listening" /C:"application started" quick_fix_test.log >nul 2>&1
if %errorlevel% equ 0 (
    echo [OK] Application can start successfully
) else (
    echo [WARNING] Application might have startup issues
    echo Check quick_fix_test.log for details
)
echo.

echo ============================================================
echo FIXES APPLIED
echo ============================================================
echo.
echo All common fixes have been applied:
echo  - Logs folder created and permissions set
echo  - All files unblocked
echo  - IIS permissions configured
echo  - Database permissions set
echo.
echo NEXT STEPS:
echo  1. Restart your IIS Application Pool
echo  2. Refresh your browser (F5)
echo  3. If still error 500, run IIS_500_ERROR_TROUBLESHOOTER.bat
echo  4. Check logs\stdout*.log for detailed errors
echo.

echo Press any key to exit...
pause >nul
