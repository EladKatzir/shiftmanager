@echo off
echo ================================================================================
echo            SHIFTMANAGER v1.0.0 - First Time Setup Check
echo                      Build Date: 2025-11-12
echo ================================================================================
echo.

REM Check if ShiftManager is already running
tasklist /FI "IMAGENAME eq ShiftManager.exe" 2>NUL | find /I /N "ShiftManager.exe">NUL
if "%ERRORLEVEL%"=="0" (
    echo ================================================================================
    echo [WARNING] ShiftManager is already running!
    echo ================================================================================
    echo.
    echo An instance of ShiftManager.exe is currently running.
    echo This will cause conflicts:
    echo   - Port 5000 already in use
    echo   - Database file locked
    echo   - Cannot update files
    echo.
    echo You need to stop the existing process first.
    echo.
    echo OPTIONS:
    echo   1. Press 'S' to STOP the existing process and continue
    echo   2. Press 'C' to CANCEL and exit
    echo.
    choice /C SC /N /M "Your choice [S=Stop, C=Cancel]: "

    if errorlevel 2 (
        echo.
        echo Cancelled. The existing ShiftManager is still running.
        echo.
        pause
        exit /b 1
    )

    if errorlevel 1 (
        echo.
        echo Stopping existing ShiftManager process...
        taskkill /F /IM ShiftManager.exe >nul 2>&1

        REM Wait a moment for the process to fully terminate
        timeout /t 2 /nobreak >nul

        REM Verify it's stopped
        tasklist /FI "IMAGENAME eq ShiftManager.exe" 2>NUL | find /I /N "ShiftManager.exe">NUL
        if "%ERRORLEVEL%"=="0" (
            echo [ERROR] Failed to stop ShiftManager. Please close it manually.
            echo.
            pause
            exit /b 1
        )

        echo [OK] Existing process stopped successfully
        echo.

        REM Wait a bit more for file locks to release
        echo Waiting for file locks to release...
        timeout /t 2 /nobreak >nul
        echo.
    )
)

REM Check if port 5000 is in use (additional safety check)
netstat -ano | findstr ":5000" >nul 2>&1
if %errorlevel% equ 0 (
    echo [WARNING] Port 5000 is currently in use by another application.
    echo.
    echo This might cause ShiftManager to fail to start.
    echo You may need to:
    echo   1. Close the application using port 5000, or
    echo   2. Change the port in appsettings.json
    echo.
    echo Press any key to continue anyway, or Ctrl+C to cancel...
    pause >nul
    echo.
)

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
echo [OK] ShiftManager.exe found (v1.0.0)
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

REM Check if wwwroot folder exists
if not exist wwwroot (
    echo [WARNING] wwwroot folder not found!
    echo Web assets may be missing.
    echo.
) else (
    echo [OK] Web assets folder found
    echo.
)

REM Check if Hebrew localization exists
if not exist he-IL (
    echo [WARNING] Hebrew localization folder not found!
    echo.
) else (
    echo [OK] Hebrew localization found
    echo.
)

echo ================================================================================
echo All checks passed! Starting ShiftManager v1.0.0...
echo ================================================================================
echo.
echo The application will start in a few seconds.
echo.
echo What happens on first run:
echo   - Creates SQLite database (app.db)
echo   - Runs 22 database migrations
echo   - Seeds company "Demo Co"
echo   - Seeds 5 shift types: MORNING, NOON, NIGHT, MIDDLE, OFFLINE
echo   - Activates On-Duty types: Hakam (Chak"mko), Lead (Movilto)
echo   - Creates admin user with your password
echo   - Starts web server on port 5000
echo.
echo Once you see "Now listening on: http://localhost:5000"
echo Open your browser to: http://localhost:5000
echo.
echo Login with:
echo   Email: admin@local
echo   Password: [whatever you set in appsettings.json]
echo.
echo NEW FEATURES in v1.0.0:
echo   - OFFLINE shift type (automatically seeded)
echo   - On-Duty types: Hakam (shield) and Lead (star) - built-in, ready to use
echo   - Team Calendars
echo   - Hebrew language support with RTL
echo   - Complete API layer (27 endpoints)
echo.
echo Press CTRL+C to stop the application.
echo ================================================================================
echo.

REM Start the application
ShiftManager.exe
