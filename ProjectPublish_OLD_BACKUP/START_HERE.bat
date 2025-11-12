@echo off
echo ================================================================================
echo            SHIFTMANAGER - First Time Setup Check
echo ================================================================================
echo.

REM Check if appsettings.json has a password set
findstr /C:"\"SEED_ADMIN_PASSWORD\": \"\"" appsettings.json >nul 2>&1
if %errorlevel% equ 0 (
    echo [ERROR] Admin password not configured!
    echo.
    echo You need to set a password before running the application.
    echo.
    echo QUICK FIX:
    echo 1. Open appsettings.json in Notepad
    echo 2. Find this line:    "SEED_ADMIN_PASSWORD": "",
    echo 3. Change it to:      "SEED_ADMIN_PASSWORD": "YourPassword123!",
    echo 4. Save the file
    echo 5. Run this script again
    echo.
    echo Or press any key to open appsettings.json now...
    pause >nul
    notepad appsettings.json
    echo.
    echo After saving, press any key to try starting the application...
    pause >nul
)

findstr /C:"\"SEED_ADMIN_PASSWORD\": \"\"" appsettings.json >nul 2>&1
if %errorlevel% equ 0 (
    echo [ERROR] Password still empty! Please set a password in appsettings.json
    echo.
    pause
    exit /b 1
)

echo [OK] Admin password is configured
echo.

REM Check if exe exists
if not exist ShiftManager.exe (
    echo [ERROR] ShiftManager.exe not found!
    echo Make sure you're running this script from the ProjectPublish folder.
    pause
    exit /b 1
)
echo [OK] ShiftManager.exe found
echo.

REM Check if SQLite DLL exists
if not exist e_sqlite3.dll (
    echo [ERROR] e_sqlite3.dll not found!
    echo This is required for the database to work.
    pause
    exit /b 1
)
echo [OK] SQLite library found
echo.

echo ================================================================================
echo All checks passed! Starting ShiftManager...
echo ================================================================================
echo.
echo The application will start in a few seconds.
echo.
echo Once you see "Now listening on: http://localhost:5000"
echo Open your browser to: http://localhost:5000
echo.
echo Login with:
echo   Email: admin@local
echo   Password: [whatever you set in appsettings.json]
echo.
echo Press CTRL+C to stop the application.
echo ================================================================================
echo.

REM Start the application
ShiftManager.exe
