@echo off
color 0B
title Enable Detailed IIS Errors

echo.
echo ============================================================
echo ENABLE DETAILED IIS ERROR MESSAGES
echo ============================================================
echo.
echo This will help you see the ACTUAL error instead of just "500"
echo.

REM Check if running as admin
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] This script must be run as Administrator
    echo.
    echo Right-click this file and select "Run as administrator"
    echo.
    pause
    exit /b 1
)

echo [STEP 1] Enabling detailed errors in web.config...
echo ============================================================

REM Backup current web.config
copy /Y web.config web.config.backup >nul

REM Create a temporary web.config with detailed errors
(
echo ^<?xml version="1.0" encoding="utf-8"?^>
echo ^<configuration^>
echo   ^<location path="." inheritInChildApplications="false"^>
echo     ^<system.webServer^>
echo       ^<handlers^>
echo         ^<add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" /^>
echo       ^</handlers^>
echo       ^<aspNetCore processPath=".\ShiftManager.exe"
echo                   stdoutLogEnabled="true"
echo                   stdoutLogFile=".\logs\stdout"
echo                   hostingModel="inprocess"^>
echo         ^<environmentVariables^>
echo           ^<environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Development" /^>
echo         ^</environmentVariables^>
echo       ^</aspNetCore^>
echo       ^<httpErrors existingResponse="PassThrough" /^>
echo     ^</system.webServer^>
echo   ^</location^>
echo   ^<system.web^>
echo     ^<customErrors mode="Off" /^>
echo   ^</system.web^>
echo ^</configuration^>
) > web.config.detailed

move /Y web.config.detailed web.config >nul

echo [OK] web.config updated to show detailed errors
echo.

echo [STEP 2] Creating logs folder...
echo ============================================================
if not exist logs mkdir logs
icacls logs /grant "IIS_IUSRS:(OI)(CI)M" /T /Q
icacls logs /grant "Everyone:(OI)(CI)M" /T /Q
echo [OK] Logs folder ready
echo.

echo ============================================================
echo DETAILED ERRORS ENABLED
echo ============================================================
echo.
echo Changes made:
echo  1. Changed ASPNETCORE_ENVIRONMENT to "Development"
echo  2. Enabled httpErrors PassThrough
echo  3. Disabled customErrors
echo  4. Backup saved as: web.config.backup
echo.
echo NOW DO THIS:
echo  1. Restart IIS Application Pool (or run: iisreset)
echo  2. Refresh your browser
echo  3. You should now see detailed error instead of generic 500
echo  4. Take a screenshot or copy the error message
echo.
echo IMPORTANT: After fixing the issue, restore production config:
echo  - Run: copy /Y web.config.backup web.config
echo  - Or: copy /Y web.config.enhanced web.config
echo.
echo Press any key to restart IIS...
pause >nul

echo.
echo Restarting IIS...
iisreset /restart

echo.
echo IIS restarted. Now refresh your browser to see detailed errors.
echo.
pause
