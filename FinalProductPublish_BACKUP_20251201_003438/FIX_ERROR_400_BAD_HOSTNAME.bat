@echo off
color 0E
title Fix Error 400 - Invalid Hostname

echo.
echo ============================================================
echo FIX ERROR 400 - INVALID HOSTNAME
echo ============================================================
echo.
echo This error happens when the DNS name is not in AllowedHosts
echo.

set "APP_DIR=%~dp0"
cd /d "%APP_DIR%"

echo [INFO] Backing up appsettings.json...
copy /Y appsettings.json appsettings.json.backup >nul
echo [OK] Backup created
echo.

echo [INFO] Current AllowedHosts setting:
findstr "AllowedHosts" appsettings.json
echo.

echo What would you like to do?
echo.
echo [1] Allow specific host: 7108dev.d8200.mil (RECOMMENDED)
echo [2] Allow all hosts (less secure, but works)
echo [3] Allow localhost only (current setting)
echo [4] Custom hostname (enter manually)
echo.
set /p CHOICE="Enter choice (1-4): "

if "%CHOICE%"=="1" goto SPECIFIC
if "%CHOICE%"=="2" goto WILDCARD
if "%CHOICE%"=="3" goto LOCALHOST
if "%CHOICE%"=="4" goto CUSTOM
goto INVALID

:SPECIFIC
echo.
echo [INFO] Setting AllowedHosts to: localhost;7108dev.d8200.mil
powershell -Command "(Get-Content appsettings.json) -replace '\"AllowedHosts\": \".*\"', '\"AllowedHosts\": \"localhost;7108dev.d8200.mil\"' | Set-Content appsettings.json"
goto DONE

:WILDCARD
echo.
echo [WARNING] This allows ANY hostname (less secure)
echo [INFO] Setting AllowedHosts to: *
powershell -Command "(Get-Content appsettings.json) -replace '\"AllowedHosts\": \".*\"', '\"AllowedHosts\": \"*\"' | Set-Content appsettings.json"
goto DONE

:LOCALHOST
echo.
echo [INFO] Keeping AllowedHosts as: localhost
echo [WARNING] This will only work for localhost access
goto DONE

:CUSTOM
echo.
set /p CUSTOM_HOST="Enter hostname (e.g., myapp.example.com): "
echo [INFO] Setting AllowedHosts to: localhost;%CUSTOM_HOST%
powershell -Command "(Get-Content appsettings.json) -replace '\"AllowedHosts\": \".*\"', '\"AllowedHosts\": \"localhost;%CUSTOM_HOST%\"' | Set-Content appsettings.json"
goto DONE

:INVALID
echo.
echo [ERROR] Invalid choice
pause
exit /b 1

:DONE
echo.
echo [OK] Configuration updated!
echo.
echo New AllowedHosts setting:
findstr "AllowedHosts" appsettings.json
echo.
echo ============================================================
echo NEXT STEPS
echo ============================================================
echo.
echo 1. Recycle your IIS Application Pool:
echo    - Open IIS Manager
echo    - Find your Application Pool
echo    - Right-click ^> Recycle
echo.
echo 2. Refresh your browser (F5)
echo.
echo 3. The 400 error should be fixed!
echo.
echo If you need to revert:
echo    copy /Y appsettings.json.backup appsettings.json
echo.

pause
