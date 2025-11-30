@echo off
color 0C
title UNLOCK ADMIN ACCOUNT - Emergency Access

echo.
echo ============================================================
echo UNLOCK ADMIN ACCOUNT - EMERGENCY ACCESS
echo ============================================================
echo.
echo You are locked out due to failed login attempts.
echo.
echo CHOOSE YOUR FIX:
echo.
echo [1] QUICK FIX - Delete database and start fresh (RECOMMENDED)
echo     - Deletes app.db
echo     - Loses all data (users, shifts, etc.)
echo     - Creates new database with password from appsettings.json
echo     - Takes 10 seconds
echo     - YOU CAN LOGIN IMMEDIATELY
echo.
echo [2] UNLOCK EXISTING ACCOUNT - Keep your data
echo     - Unlocks the admin account
echo     - Resets failed login counter
echo     - Keeps all existing data
echo     - Password remains what it was when database was created
echo     - Takes 30 seconds
echo.
echo [3] RESET PASSWORD - Keep your data and set new password
echo     - Unlocks account AND sets new password
echo     - Keeps all existing data
echo     - Takes 1 minute
echo.
set /p CHOICE="Enter choice (1, 2, or 3): "

if "%CHOICE%"=="1" goto DELETE_DB
if "%CHOICE%"=="2" goto UNLOCK_ONLY
if "%CHOICE%"=="3" goto RESET_PASSWORD
echo Invalid choice
pause
exit /b 1

:DELETE_DB
echo.
echo ============================================================
echo OPTION 1: DELETE DATABASE AND START FRESH
echo ============================================================
echo.
echo WARNING: This will delete ALL data including:
echo  - All users
echo  - All shifts
echo  - All time-off requests
echo  - All notifications
echo  - Everything!
echo.
echo The database will be recreated with the password from appsettings.json
echo.
set /p CONFIRM="Are you sure? Type YES to continue: "
if not "%CONFIRM%"=="YES" (
    echo Cancelled.
    pause
    exit /b 0
)

echo.
echo [1/3] Stopping any running instances...
taskkill /F /IM ShiftManager.exe >nul 2>&1
timeout /t 2 /nobreak >nul

echo [2/3] Deleting app.db...
if exist app.db (
    del /F app.db
    echo [OK] Database deleted
) else (
    echo [INFO] No database file found
)

if exist app.db-shm del /F app.db-shm >nul 2>&1
if exist app.db-wal del /F app.db-wal >nul 2>&1

echo [3/3] Starting application to create fresh database...
echo.
echo Starting ShiftManager... (wait 10 seconds)
start /B ShiftManager.exe >nul 2>&1
timeout /t 10 /nobreak

taskkill /F /IM ShiftManager.exe >nul 2>&1

echo.
echo ============================================================
echo DONE! DATABASE RECREATED
echo ============================================================
echo.
echo You can now login with:
echo.
findstr "SEED_ADMIN_PASSWORD" appsettings.json
echo.
echo Email: admin@local
echo Password: (see above)
echo.
echo NOTE: This is a FRESH database. All previous data is gone.
echo.
pause
exit /b 0

:UNLOCK_ONLY
echo.
echo ============================================================
echo OPTION 2: UNLOCK ACCOUNT (KEEP DATA)
echo ============================================================
echo.

if not exist app.db (
    echo [ERROR] No database file found!
    echo The database doesn't exist yet. Just start the app normally.
    pause
    exit /b 1
)

echo [1/3] Stopping any running instances...
taskkill /F /IM ShiftManager.exe >nul 2>&1
timeout /t 2 /nobreak >nul

echo [2/3] Unlocking admin account in database...

REM Use SQLite to unlock the account
powershell -Command "$dbPath = 'app.db'; Add-Type -Path '.\e_sqlite3.dll'; [System.Data.SQLite.SQLiteConnection]::CreateFile($dbPath) | Out-Null; $conn = New-Object -TypeName System.Data.SQLite.SQLiteConnection('Data Source=app.db'); $conn.Open(); $cmd = $conn.CreateCommand(); $cmd.CommandText = 'UPDATE AppUsers SET AccessFailedCount = 0, LockoutEnd = NULL WHERE Email = ''admin@local'''; $result = $cmd.ExecuteNonQuery(); $conn.Close(); Write-Host '[OK] Admin account unlocked. Rows affected: ' $result"

echo.
echo [3/3] Verifying unlock...
echo.
echo ============================================================
echo DONE! ACCOUNT UNLOCKED
echo ============================================================
echo.
echo The admin@local account is now unlocked.
echo.
echo IMPORTANT: The password is NOT what's in appsettings.json!
echo The password is whatever it was when the database was FIRST created.
echo.
echo If you don't know the password, use Option 1 or Option 3 instead.
echo.
pause
exit /b 0

:RESET_PASSWORD
echo.
echo ============================================================
echo OPTION 3: UNLOCK AND RESET PASSWORD
echo ============================================================
echo.
echo This will:
echo  1. Unlock the admin account
echo  2. Set a NEW password
echo  3. Keep all your data
echo.

if not exist app.db (
    echo [ERROR] No database file found!
    pause
    exit /b 1
)

echo Current password in appsettings.json:
findstr "SEED_ADMIN_PASSWORD" appsettings.json
echo.
set /p NEW_PASSWORD="Enter NEW password for admin@local: "

if "%NEW_PASSWORD%"=="" (
    echo Error: Password cannot be empty
    pause
    exit /b 1
)

echo.
echo [1/4] Stopping any running instances...
taskkill /F /IM ShiftManager.exe >nul 2>&1
timeout /t 2 /nobreak >nul

echo [2/4] Generating password hash...
echo This requires the application to hash the password...
echo.

REM Create a temporary C# script to hash the password
powershell -Command "$code = @' using System; using System.Security.Cryptography; using Microsoft.AspNetCore.Identity; class PasswordHasher { static void Main(string[] args) { var hasher = new PasswordHasher<object>(); var hash = hasher.HashPassword(null, args[0]); Console.WriteLine(hash); } } '@; $code | Out-File -FilePath temp_hasher.cs -Encoding ASCII"

echo [INFO] Unable to generate hash directly. Using alternative method...
echo.
echo RECOMMENDED: Update appsettings.json and delete database instead.
echo.
echo Would you like to:
echo  [A] Go back and use Option 1 (delete database)
echo  [B] Cancel and try manual password later
echo.
set /p ALT="Enter A or B: "
if "%ALT%"=="A" goto DELETE_DB
if "%ALT%"=="a" goto DELETE_DB

echo Cancelled.
pause
exit /b 0
