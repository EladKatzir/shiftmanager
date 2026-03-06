# 15. Air-Gapped Deployment

**Document Version:** 1.0
**Last Updated:** December 2025
**Part of:** PROJECT COSMOGENESIS - ShiftManager Genesis Documentation

---

## Table of Contents

1. [Overview](#overview)
2. [What is Air-Gapped Deployment?](#what-is-air-gapped-deployment)
3. [Windows Zone.Identifier Blocking](#windows-zoneidentifier-blocking)
4. [Building the Deployment Package](#building-the-deployment-package)
5. [USB Transfer Process](#usb-transfer-process)
6. [File Unblocking Procedures](#file-unblocking-procedures)
7. [Deployment Verification](#deployment-verification)
8. [Starting the Application](#starting-the-application)
9. [Offline Building from Source](#offline-building-from-source)
10. [Troubleshooting](#troubleshooting)
11. [Security Considerations](#security-considerations)
12. [Deployment Checklist](#deployment-checklist)

---

## Overview

ShiftManager is designed for **air-gapped deployments** - environments with no internet connection where USB transfer is the primary method of software distribution. This document explains the complete deployment process, including the critical file unblocking procedures required for Windows-based deployments.

### Key Challenges

| Challenge | Impact | Solution |
|-----------|--------|----------|
| **Zone.Identifier Blocking** | DLLs fail to load with "Could not load file or assembly" | UNBLOCK_FILES.bat script |
| **Self-Contained Runtime** | 150-200MB deployment size | Pre-bundled .NET 8.0 runtime |
| **NuGet Dependencies** | Cannot download packages offline | Offline NuGet cache transfer |
| **Database Initialization** | SQLite file must be created on first run | Automatic database migration on startup |
| **Static Asset Loading** | CSS/JS files may be blocked | Unblock entire ProjectPublish folder |

### Deployment Artifacts

**ShiftManager deployment package includes:**
- **ShiftManager.exe** - Main executable (self-contained .NET app)
- **330-340 DLL files** - .NET runtime + application dependencies
- **wwwroot/** - Static assets (CSS, JS, images)
- **Helper scripts** - UNBLOCK_FILES.bat, VERIFY_FILES.bat, START_HERE.bat
- **Documentation** - AIR_GAPPED_DEPLOYMENT_GUIDE.txt, DLL_CHECKSUMS.txt
- **API clients/** - Python, JavaScript client libraries
- **appsettings.Production.template.json** - Production configuration template

---

## What is Air-Gapped Deployment?

### Definition

**Air-gapped** refers to a physical or logical separation between a computer network and the internet. Air-gapped systems are isolated for security reasons, common in:

- **Government facilities** - Classified systems
- **Military installations** - Secure operations
- **Financial institutions** - High-security data centers
- **Critical infrastructure** - Power grids, water treatment
- **Healthcare systems** - HIPAA-compliant environments
- **Manufacturing** - OT (Operational Technology) networks

### Transfer Methods

Since air-gapped systems cannot download files from the internet:

1. **USB drives** (most common) - Physical transfer via removable media
2. **Optical media** (CD/DVD) - Write-once media for audit trails
3. **One-way network diodes** - Hardware-enforced unidirectional data flow
4. **Sneakernet** - Physical transport of storage devices

### ShiftManager Air-Gapped Architecture

```
Internet-Connected Development Machine
   ↓
Build ShiftManager (dotnet publish)
   ↓
Create ZIP package (~150MB)
   ↓
Transfer to USB drive
   ↓
Physical transport (sneakernet)
   ↓
Air-Gapped Production Machine
   ↓
Extract ZIP
   ↓
Unblock DLLs (CRITICAL)
   ↓
Run ShiftManager.exe
   ↓
Application runs offline (SQLite database, no external dependencies)
```

---

## Windows Zone.Identifier Blocking

### The Problem

**When files are transferred via USB to Windows, the operating system marks them as "potentially unsafe" by adding a hidden alternate data stream (ADS) called `Zone.Identifier`.**

This causes the following error when running ShiftManager:

```
Could not load file or assembly 'SixLabors.ImageSharp, Version=3.0.0.0, Culture=neutral, PublicKeyToken=...'
```

**This error is misleading** - it does NOT mean the DLL file is missing or corrupted. It means **Windows is blocking the DLL from loading** due to the Zone.Identifier mark.

### What is Zone.Identifier?

**Zone.Identifier** is an NTFS alternate data stream (ADS) that tracks the origin of a file:

| Zone ID | Source | Trust Level |
|---------|--------|-------------|
| 0 | Local computer | Trusted |
| 1 | Local intranet | Trusted |
| 2 | Trusted sites | Trusted |
| 3 | Internet | Untrusted |
| 4 | Restricted sites | Untrusted |

**Files from USB drives are marked as Zone 3 (Internet) or Zone 4 (Restricted), which triggers security warnings and load failures.**

### How Zone.Identifier Blocks DLLs

```
1. User copies ZIP file from internet-connected machine to USB
   ↓
2. Windows marks ZIP file with Zone.Identifier:ZoneId=3
   ↓
3. User extracts ZIP on air-gapped machine
   ↓
4. All extracted files inherit Zone.Identifier:ZoneId=3
   ↓
5. Application tries to load SixLabors.ImageSharp.dll
   ↓
6. .NET runtime checks Zone.Identifier
   ↓
7. Runtime sees ZoneId=3 (Internet) → BLOCKS DLL load
   ↓
8. Application crashes with "Could not load file or assembly"
```

### Why This Affects .NET Applications

**.NET assembly loading performs security checks:**

```csharp
// Simplified .NET runtime logic (pseudocode)
bool CanLoadAssembly(string dllPath)
{
    // Check if file has Zone.Identifier
    if (HasZoneIdentifier(dllPath))
    {
        var zoneId = GetZoneId(dllPath);
        if (zoneId >= 3) // Internet or Restricted
        {
            // Block load unless explicitly unblocked
            return false;
        }
    }
    return true;
}
```

**Native Windows executables (.exe files) prompt for user confirmation, but .NET DLL files silently fail to load, causing cryptic errors.**

### Verifying Zone.Identifier

**PowerShell command to check if a DLL is blocked:**

```powershell
Get-Item .\SixLabors.ImageSharp.dll -Stream Zone.Identifier
```

**Expected outputs:**

```
# File is BLOCKED (Zone.Identifier exists)
PSPath        : Microsoft.PowerShell.Core\FileSystem::C:\ShiftManager\ProjectPublish\SixLabors.ImageSharp.dll::Zone.Identifier
PSParentPath  : Microsoft.PowerShell.Core\FileSystem::C:\ShiftManager\ProjectPublish
PSChildName   : SixLabors.ImageSharp.dll::Zone.Identifier
PSDrive       : C
PSProvider    : Microsoft.PowerShell.Core\FileSystem
PSIsContainer : False
FileName      : C:\ShiftManager\ProjectPublish\SixLabors.ImageSharp.dll
Stream        : Zone.Identifier
Length        : 26
```

```
# File is UNBLOCKED (Zone.Identifier does not exist)
Get-Item : Cannot find path 'C:\ShiftManager\ProjectPublish\SixLabors.ImageSharp.dll' with type 'Zone.Identifier'.
At line:1 char:1
+ Get-Item .\SixLabors.ImageSharp.dll -Stream Zone.Identifier
+ ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
+ CategoryInfo          : ObjectNotFound: (...) [Get-Item], PSArgumentException
+ FullyQualifiedErrorId : ItemNotFound,Microsoft.PowerShell.Commands.GetItemCommand
```

**If Zone.Identifier is found → File is BLOCKED**
**If "Cannot find path" error → File is UNBLOCKED (good!)**

---

## Building the Deployment Package

**File:** `Build-Release.ps1` (main build automation script)

### Prerequisites

**On the internet-connected development machine:**
- Windows 10/11 or Windows Server 2019+
- .NET 8.0 SDK installed
- PowerShell 5.1 or PowerShell 7+
- Git (for version tagging)
- Visual Studio or VS Code (optional, for editing)

### Build Command

**From the project root directory:**

```powershell
.\Build-Release.ps1 -Version "1.0.0"
```

**Options:**

```powershell
# Build with full testing (recommended for production)
.\Build-Release.ps1 -Version "1.0.0"

# Build without tests (faster, not recommended for production)
.\Build-Release.ps1 -Version "1.0.0" -SkipTests

# Build without pushing git tag to remote
.\Build-Release.ps1 -Version "1.0.0" -NoPush
```

### Build Pipeline (10 Stages)

**Build-Release.ps1:138-204**

```
STAGE 1/10: Pre-Flight Checks
  - Verify .NET SDK installed
  - Verify project file exists
  - Validate version number format
  - Check git repository status

STAGE 2/10: Backup
  - Backup existing ProjectPublish folder
  - Name: ProjectPublish_BACKUP_<timestamp>
  - Keep last 3 backups, delete older

STAGE 3/10: Build
  - dotnet publish -c Release -r win-x64 --self-contained
  - Output: ProjectPublish/
  - Copy air-gapped deployment scripts:
    * UNBLOCK_FILES.bat (REQUIRED)
    * VERIFY_FILES.bat (REQUIRED)
    * QUICK_FIX.bat (REQUIRED)
    * AIR_GAPPED_DEPLOYMENT_GUIDE.txt (REQUIRED)
  - Copy API documentation and client libraries

STAGE 4/10: Build Verification
  - Verify ShiftManager.exe exists
  - Verify critical DLLs present:
    * SixLabors.ImageSharp.dll
    * e_sqlite3.dll
    * Microsoft.EntityFrameworkCore.Sqlite.dll
  - Verify wwwroot/ folder present
  - Count DLL files (expect 330-340)

STAGE 5/10: Application Testing
  - Start ShiftManager.exe in background
  - Wait for HTTP server to start (max 60 seconds)
  - Send HTTP GET to http://localhost:5000/
  - Verify response status 200 OK
  - Verify database created (shiftmanager.db)
  - Verify "OFFLINE" shift type seeded (critical for air-gapped deployments)
  - Terminate application

STAGE 6/10: Documentation Generation
  - Generate VERSION.txt file
  - Generate README.txt quick start guide
  - Generate DLL_CHECKSUMS.txt (SHA256 hashes)
  - Generate deployment readiness report

STAGE 7/10: Final Integrity Check
  - Re-verify all critical files
  - Check file sizes
  - Validate checksums

STAGE 8/10: Git Tagging
  - Create git tag: v1.0.0
  - Push tag to remote (unless -NoPush specified)

STAGE 9/10: Packaging
  - Create ZIP file: packages/ShiftManager-v1.0.0-win-x64.zip
  - Calculate ZIP checksum (SHA256)
  - Write checksum to .sha256 file

STAGE 10/10: Release Report
  - Generate release report with:
    * Build date/time
    * Git commit hash
    * File count, DLL count, package size
    * Test results summary
```

### Build Output Structure

```
ProjectPublish/
├── ShiftManager.exe (main executable)
├── ShiftManager.dll (application assembly)
├── *.dll (330-340 dependency DLLs)
├── wwwroot/
│   ├── css/
│   ├── js/
│   ├── feedback/
│   └── Data/ (database created on first run)
├── he-IL/ (Hebrew localization resources)
├── appsettings.json (default configuration)
├── appsettings.Production.template.json (production template)
├── API_DOCUMENTATION.md (REST API docs)
├── clients/
│   ├── python/ (Python client library)
│   └── javascript/ (JavaScript client library)
├── UNBLOCK_FILES.bat (CRITICAL - unblocks DLLs)
├── VERIFY_FILES.bat (verify deployment integrity)
├── START_HERE.bat (launch application)
├── QUICK_FIX.bat (emergency troubleshooting)
├── AIR_GAPPED_DEPLOYMENT_GUIDE.txt (this document)
├── VERSION.txt (version info)
├── README.txt (quick start)
└── DLL_CHECKSUMS.txt (SHA256 checksums for verification)
```

### Package Output

```
packages/
├── ShiftManager-v1.0.0-win-x64.zip (~150MB)
└── ShiftManager-v1.0.0-win-x64.zip.sha256 (checksum)
```

---

## USB Transfer Process

### Step 1: Prepare USB Drive

1. Use a **high-quality USB 3.0 drive** (faster transfer)
2. Format as **NTFS** (required for files >4GB, though ShiftManager is <200MB)
3. Label drive clearly: "ShiftManager v1.0.0 Deployment"

### Step 2: Copy ZIP to USB

**On the internet-connected machine:**

```cmd
# Navigate to packages folder
cd C:\ShiftManager\packages

# Verify ZIP checksum
certutil -hashfile ShiftManager-v1.0.0-win-x64.zip SHA256

# Compare with .sha256 file
type ShiftManager-v1.0.0-win-x64.zip.sha256

# Copy to USB (replace E: with your USB drive letter)
copy ShiftManager-v1.0.0-win-x64.zip E:\
copy ShiftManager-v1.0.0-win-x64.zip.sha256 E:\
```

### Step 3: Physical Transport

- **Safely eject USB drive**
- **Transport to air-gapped machine**
- **Maintain chain of custody** (for audit trail)

### Step 4: Transfer to Air-Gapped Machine

**On the air-gapped machine:**

```cmd
# Create deployment directory
mkdir C:\ShiftManager
cd C:\ShiftManager

# Copy ZIP from USB (replace E: with your USB drive letter)
copy E:\ShiftManager-v1.0.0-win-x64.zip .
copy E:\ShiftManager-v1.0.0-win-x64.zip.sha256 .

# Verify checksum
certutil -hashfile ShiftManager-v1.0.0-win-x64.zip SHA256
type ShiftManager-v1.0.0-win-x64.zip.sha256

# If checksums DON'T match → ZIP is corrupted, recopy from USB
```

### Step 5: Unblock ZIP File

**CRITICAL: Unblock the ZIP file BEFORE extraction**

**Windows Explorer method:**
1. Right-click on ZIP file → Properties
2. At the bottom of General tab, check **"Unblock"** checkbox
3. Click **Apply** → Click **OK**

**PowerShell method:**

```powershell
Unblock-File -Path .\ShiftManager-v1.0.0-win-x64.zip
```

**Why unblock ZIP first?** If you unblock the ZIP before extracting, extracted files will NOT inherit Zone.Identifier. This is the most efficient approach.

### Step 6: Extract ZIP

**Windows Explorer method:**
1. Right-click ZIP → Extract All...
2. Extract to: `C:\ShiftManager\ProjectPublish`
3. Wait for extraction (150MB takes ~1-2 minutes on USB 3.0)

**PowerShell method:**

```powershell
Expand-Archive -Path .\ShiftManager-v1.0.0-win-x64.zip -DestinationPath .\ProjectPublish -Force
```

---

## File Unblocking Procedures

**Even if you unblocked the ZIP file before extraction, verify all DLLs are unblocked. Some extraction tools re-apply Zone.Identifier.**

### Method 1: UNBLOCK_FILES.bat (Automated)

**File:** `ProjectPublish/UNBLOCK_FILES.bat`

**Usage:**

```cmd
cd C:\ShiftManager\ProjectPublish
UNBLOCK_FILES.bat
```

**Implementation:**

```batch
@echo off
echo.
echo ================================================================================
echo                  UNBLOCKING DLL FILES
echo ================================================================================
echo.
echo This script removes the Zone.Identifier stream from all files in this folder.
echo This is REQUIRED for air-gapped Windows deployments to prevent DLL load errors.
echo.
echo Running PowerShell unblock command...
echo.

REM Try PowerShell method first (most reliable)
powershell.exe -ExecutionPolicy Bypass -Command "Get-ChildItem -Path '%~dp0' -Recurse | Unblock-File"

if %ERRORLEVEL% EQU 0 (
    echo.
    echo [SUCCESS] Files unblocked using PowerShell
    echo.
    goto END
)

echo.
echo [WARNING] PowerShell method failed, trying alternative...
echo.

REM Alternative: Individual file unblocking
for /R %%F in (*.dll *.exe) do (
    echo Unblocking: %%~nxF
    powershell.exe -ExecutionPolicy Bypass -Command "Unblock-File -Path '%%F'"
)

:END
echo.
echo ================================================================================
echo Unblocking complete!
echo.
echo Next step: Run VERIFY_FILES.bat to check deployment integrity
echo ================================================================================
echo.
pause
```

### Method 2: PowerShell (Manual - Administrator)

**Right-click PowerShell → Run as Administrator:**

```powershell
cd C:\ShiftManager\ProjectPublish

# Unblock all files recursively
Get-ChildItem -Recurse | Unblock-File

# Verify unblocking (should show errors for unblocked files)
Get-ChildItem -Recurse -File | ForEach-Object {
    try {
        Get-Item $_.FullName -Stream Zone.Identifier -ErrorAction Stop
        Write-Host "[BLOCKED] $_" -ForegroundColor Red
    } catch {
        # "Cannot find path" means file is unblocked (good!)
        Write-Host "[OK] $_" -ForegroundColor Green
    }
}
```

### Method 3: File Properties (Manual - GUI)

**For small deployments or specific files:**

1. Right-click folder: `C:\ShiftManager\ProjectPublish`
2. Click **Properties**
3. At the bottom of General tab, check **"Unblock"**
4. Click **Apply**
5. Dialog: "Apply changes to this folder, subfolders and files"
6. Click **OK**
7. Wait for Windows to unblock all files (may take 2-3 minutes)

### Method 4: Command Prompt with PowerShell

**If PowerShell execution policy is restricted:**

```cmd
cd C:\ShiftManager\ProjectPublish
powershell.exe -ExecutionPolicy Bypass -Command "Get-ChildItem -Recurse | Unblock-File"
```

### Verification

**Check if specific DLL is unblocked:**

```powershell
Get-Item .\SixLabors.ImageSharp.dll -Stream Zone.Identifier
```

**Expected output if UNBLOCKED:**
```
Get-Item : Cannot find path '...' with type 'Zone.Identifier'.
```

**Expected output if STILL BLOCKED:**
```
PSPath        : ...::Zone.Identifier
PSParentPath  : ...
PSChildName   : SixLabors.ImageSharp.dll::Zone.Identifier
Stream        : Zone.Identifier
Length        : 26
```

---

## Deployment Verification

### VERIFY_FILES.bat Script

**File:** `ProjectPublish/VERIFY_FILES.bat`

**Purpose:** Verify all critical files are present and checksums match

**Usage:**

```cmd
cd C:\ShiftManager\ProjectPublish
VERIFY_FILES.bat
```

**Checks performed:**

```batch
1. Verify ShiftManager.exe exists
2. Verify ShiftManager.dll exists
3. Verify critical DLLs:
   - SixLabors.ImageSharp.dll
   - e_sqlite3.dll
   - Microsoft.EntityFrameworkCore.Sqlite.dll
   - Microsoft.EntityFrameworkCore.dll
   - Microsoft.Data.Sqlite.dll
   - SQLitePCLRaw.provider.e_sqlite3.dll
4. Count total DLL files (expect 330-340)
5. Verify wwwroot/ folder exists
6. Verify wwwroot/css/site.css exists
7. Verify wwwroot/js/site.js exists
8. Verify he-IL/ folder exists (Hebrew localization)
9. Verify he-IL/ShiftManager.resources.dll exists
10. Check DLL checksums against DLL_CHECKSUMS.txt (optional)
```

**Expected output:**

```
================================================================================
                    DEPLOYMENT VERIFICATION REPORT
================================================================================

[✓] ShiftManager.exe - Present
[✓] ShiftManager.dll - Present
[✓] SixLabors.ImageSharp.dll - Present (3.0.0)
[✓] e_sqlite3.dll - Present (Native SQLite)
[✓] Microsoft.EntityFrameworkCore.Sqlite.dll - Present
[✓] Microsoft.EntityFrameworkCore.dll - Present
[✓] Total DLL count: 337 (Expected: 330-340)
[✓] wwwroot/ folder - Present
[✓] wwwroot/css/site.css - Present
[✓] wwwroot/js/site.js - Present
[✓] he-IL/ folder - Present
[✓] he-IL/ShiftManager.resources.dll - Present

================================================================================
ALL CHECKS PASSED - Deployment is ready to run
================================================================================

Next step: Run UNBLOCK_FILES.bat to prevent DLL load errors
```

### Manual Verification

**PowerShell script to verify deployment:**

```powershell
$ProjectPublish = "C:\ShiftManager\ProjectPublish"
cd $ProjectPublish

# Critical files
$criticalFiles = @(
    "ShiftManager.exe",
    "ShiftManager.dll",
    "SixLabors.ImageSharp.dll",
    "e_sqlite3.dll",
    "Microsoft.EntityFrameworkCore.Sqlite.dll"
)

foreach ($file in $criticalFiles) {
    if (Test-Path $file) {
        Write-Host "[OK] $file" -ForegroundColor Green
    } else {
        Write-Host "[MISSING] $file" -ForegroundColor Red
    }
}

# Count DLLs
$dllCount = (Get-ChildItem -Recurse -Filter *.dll).Count
Write-Host "`nTotal DLLs: $dllCount (Expected: 330-340)" -ForegroundColor Cyan

# Check wwwroot
if (Test-Path "wwwroot") {
    Write-Host "[OK] wwwroot/ folder" -ForegroundColor Green
} else {
    Write-Host "[MISSING] wwwroot/ folder" -ForegroundColor Red
}
```

---

## Starting the Application

### START_HERE.bat Script

**File:** `ProjectPublish/START_HERE.bat`

**Usage:**

```cmd
cd C:\ShiftManager\ProjectPublish
START_HERE.bat
```

**Implementation:**

```batch
@echo off
echo.
echo ================================================================================
echo                        Starting ShiftManager
echo ================================================================================
echo.
echo Application will start on: http://localhost:5000
echo.
echo Default login credentials (from appsettings.json):
echo   Email:    admin@example.com
echo   Password: Admin123!
echo.
echo Press Ctrl+C to stop the application
echo.
echo ================================================================================
echo.

REM Start ShiftManager
ShiftManager.exe

pause
```

### Manual Start

**PowerShell:**

```powershell
cd C:\ShiftManager\ProjectPublish
.\ShiftManager.exe
```

**Command Prompt:**

```cmd
cd C:\ShiftManager\ProjectPublish
ShiftManager.exe
```

### First Run Initialization

**On first run, ShiftManager automatically:**

1. **Creates SQLite database:** `wwwroot/Data/shiftmanager.db`
2. **Runs all 36 migrations** (schema creation)
3. **Seeds database:**
   - Default Owner user (credentials from appsettings.json)
   - Default shift types (Morning, Evening, Night, OFFLINE)
   - Default AppConfig values
4. **Starts web server** on http://localhost:5000
5. **Logs startup** to console

**Expected startup output:**

```
info: ShiftManager.Program[0]
      ShiftManager starting... (.NET 8.0.0)
info: ShiftManager.Data.AppDbContext[0]
      Database does not exist. Running migrations...
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (12ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      CREATE TABLE "__EFMigrationsHistory" (...)
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (134ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      CREATE TABLE "Companies" (...)
...
info: ShiftManager.Data.AppDbContext[0]
      Database migration completed successfully (36 migrations applied)
info: ShiftManager.Data.DbInitializer[0]
      Seeding database with default data...
info: ShiftManager.Data.DbInitializer[0]
      Created default Owner user: admin@example.com
info: ShiftManager.Data.DbInitializer[0]
      Created default shift types: Morning, Evening, Night, OFFLINE
info: ShiftManager.Data.DbInitializer[0]
      Database seeding complete
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5000
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
```

### Login

**Open browser to:** http://localhost:5000

**Default credentials (from appsettings.json):**
- **Email:** admin@example.com
- **Password:** Admin123!

**CRITICAL: Change default password after first login!**

---

## Offline Building from Source

**For environments where pre-built packages cannot be used, build ShiftManager from source code on the air-gapped machine.**

### Prerequisites

**On internet-connected machine:**
- .NET 8.0 SDK installed
- ShiftManager source code

**On air-gapped machine:**
- .NET 8.0 SDK installed (transferable via USB)
- NuGet package cache (transferred via USB)

### Step 1: Download NuGet Packages

**On the internet-connected machine:**

```cmd
cd C:\ShiftManager

REM Download all NuGet dependencies to local cache
dotnet restore

REM NuGet cache location:
REM   Windows: %USERPROFILE%\.nuget\packages
REM   Linux: ~/.nuget/packages
REM   Mac: ~/.nuget/packages
```

**NuGet cache size:** ~500MB-1GB

**Verify packages downloaded:**

```powershell
# Windows
dir %USERPROFILE%\.nuget\packages

# Expected packages include:
#   Microsoft.EntityFrameworkCore.Sqlite (9.0.9)
#   SixLabors.ImageSharp (3.0.0)
#   Swashbuckle.AspNetCore (6.5.0)
#   FluentAssertions (7.0.0)
#   ... (100+ packages total)
```

### Step 2: Copy NuGet Cache to USB

**Windows:**

```cmd
REM Create packages folder on USB
mkdir E:\nuget-packages

REM Copy entire cache to USB (may take 10-15 minutes)
xcopy /E /I /H %USERPROFILE%\.nuget\packages E:\nuget-packages
```

**Verify copy:**

```cmd
dir E:\nuget-packages
REM Should show folders like:
REM   microsoft.entityframeworkcore.sqlite
REM   sixlabors.imagesharp
REM   swashbuckle.aspnetcore
REM   ... etc.
```

### Step 3: Transfer to Air-Gapped Machine

**Copy NuGet cache from USB to air-gapped machine:**

```cmd
REM Create .nuget folder if doesn't exist
mkdir %USERPROFILE%\.nuget\packages

REM Copy packages from USB
xcopy /E /I /H E:\nuget-packages %USERPROFILE%\.nuget\packages
```

**Verify copy:**

```cmd
dir %USERPROFILE%\.nuget\packages
```

### Step 4: Copy Source Code

**Copy ShiftManager source code to air-gapped machine via USB:**

```cmd
REM On internet machine
xcopy /E /I /H C:\ShiftManager E:\ShiftManager-source

REM On air-gapped machine
xcopy /E /I /H E:\ShiftManager-source C:\ShiftManager
```

**IMPORTANT:** Ensure `packages.lock.json` file is included (locks dependency versions for reproducible builds).

### Step 5: Build on Air-Gapped Machine

**PowerShell on air-gapped machine:**

```powershell
cd C:\ShiftManager

# Verify packages are accessible (should succeed without internet)
dotnet restore --no-http-cache

# Expected output:
#   Determining projects to restore...
#   All projects are up-to-date for restore.

# Build in Release mode
dotnet build -c Release

# Publish self-contained application
dotnet publish -c Release -r win-x64 --self-contained -o ./publish

# Options explained:
#   -c Release          = Optimized release build (not Debug)
#   -r win-x64          = Windows 64-bit runtime
#   --self-contained    = Include .NET runtime (no SDK required on target)
#   -o ./publish        = Output directory
```

**Build time:** ~2-5 minutes depending on hardware

### Step 6: Verify Build

**Check critical files:**

```powershell
cd C:\ShiftManager\publish

# Check executable exists
if (Test-Path "ShiftManager.exe") { Write-Host "[OK] ShiftManager.exe" -ForegroundColor Green }

# Check critical DLLs
$criticalDlls = @("ShiftManager.dll", "SixLabors.ImageSharp.dll", "e_sqlite3.dll")
foreach ($dll in $criticalDlls) {
    if (Test-Path $dll) { Write-Host "[OK] $dll" -ForegroundColor Green }
    else { Write-Host "[MISSING] $dll" -ForegroundColor Red }
}

# Check wwwroot
if (Test-Path "wwwroot") { Write-Host "[OK] wwwroot/" -ForegroundColor Green }
```

### Step 7: Test Build

**Run application locally:**

```powershell
cd C:\ShiftManager\publish
.\ShiftManager.exe
```

**Verify:**
- Application starts without errors
- Listening on http://localhost:5000
- Database initialized
- Can login with default credentials

---

## Troubleshooting

### Error: "Could not load file or assembly 'SixLabors.ImageSharp'"

**Cause:** DLLs are blocked by Windows Zone.Identifier

**Solution:**

```cmd
cd C:\ShiftManager\ProjectPublish

REM Close application first (Ctrl+C)

REM Run unblock script
UNBLOCK_FILES.bat

REM If that fails, use PowerShell as Administrator
powershell.exe -ExecutionPolicy Bypass -Command "Get-ChildItem -Recurse | Unblock-File"

REM Verify unblocking
powershell.exe -Command "Get-Item .\SixLabors.ImageSharp.dll -Stream Zone.Identifier"

REM Expected: "Cannot find path" (means unblocked)

REM Restart application
START_HERE.bat
```

### Error: "SixLabors.ImageSharp.dll not found"

**Cause:** File is actually missing or corrupted

**Solution:**

```cmd
REM Run verification
VERIFY_FILES.bat

REM Check DLL checksums
powershell.exe -Command "certutil -hashfile SixLabors.ImageSharp.dll SHA256"

REM Compare with DLL_CHECKSUMS.txt
type DLL_CHECKSUMS.txt | findstr SixLabors.ImageSharp.dll

REM If checksum doesn't match → recopy from USB
REM If file missing → extract ZIP again
```

### Error: "Port 5000 is already in use"

**Cause:** Another application is using port 5000

**Solution:**

```cmd
REM Edit appsettings.json
notepad appsettings.json

REM Change "Urls" line:
"Urls": "http://localhost:5001"  (change 5000 to 5001)

REM Save and restart application
```

### Error: "NU1301: Unable to load the service index for source"

**Cause:** Building from source without proper NuGet cache

**Solution:**

```cmd
REM Ensure packages.lock.json exists
dir packages.lock.json

REM Verify NuGet cache copied correctly
dir %USERPROFILE%\.nuget\packages

REM Use --no-http-cache flag
dotnet restore --no-http-cache

REM If still fails, recopy NuGet cache from USB
```

### Error: Application fails to start with database errors

**Cause:** Incomplete database migration or corrupted database

**Solution:**

```cmd
REM Delete database file
del wwwroot\Data\shiftmanager.db

REM Restart application (will recreate database)
START_HERE.bat

REM Verify migrations
type startup.log | findstr "migration"
```

---

## Security Considerations

### Is it safe to unblock files?

**YES, if you verify:**

1. **ZIP checksum matches** (compare with .sha256 file)
2. **DLL checksums match** (verify against DLL_CHECKSUMS.txt)
3. **Files obtained from trusted source** (official build from authorized developer)

**Windows blocks files from external sources as a precaution, but verified files from trusted sources are safe to unblock.**

### Checksum Verification

**Verify ZIP integrity:**

```cmd
certutil -hashfile ShiftManager-v1.0.0-win-x64.zip SHA256
type ShiftManager-v1.0.0-win-x64.zip.sha256
```

**Verify DLL integrity:**

```cmd
cd ProjectPublish
certutil -hashfile SixLabors.ImageSharp.dll SHA256

REM Compare with checksum in DLL_CHECKSUMS.txt
findstr /C:"SixLabors.ImageSharp.dll" DLL_CHECKSUMS.txt
```

### Why do EXE files work but DLL files don't?

**Windows .NET runtime behavior:**

- **.exe files:** Windows prompts user for permission ("Do you want to run this file from an unknown publisher?")
- **.dll files:** .NET runtime silently fails to load blocked DLLs without user prompts, causing cryptic errors

**This is why unblocking is CRITICAL for .NET applications.**

### Chain of Custody

**For auditing air-gapped deployments:**

1. **Build verification:** Git commit hash in VERSION.txt
2. **Transfer verification:** SHA256 checksum comparison
3. **Deployment verification:** DLL checksums match DLL_CHECKSUMS.txt
4. **Runtime verification:** Application startup logs (first run database seed)

---

## Deployment Checklist

### On Development Machine

- [ ] Run `Build-Release.ps1 -Version "X.Y.Z"`
- [ ] Wait for all 10 stages to complete
- [ ] Verify build success message
- [ ] Locate ZIP file: `packages/ShiftManager-vX.Y.Z-win-x64.zip`
- [ ] Copy ZIP + .sha256 file to USB drive
- [ ] Safely eject USB

### On Air-Gapped Machine

- [ ] Insert USB drive
- [ ] Copy ZIP to local folder (e.g., `C:\ShiftManager\`)
- [ ] Verify ZIP checksum matches .sha256 file
- [ ] RIGHT-CLICK ZIP → Properties → **Unblock** ← CRITICAL!
- [ ] Extract ZIP to `ProjectPublish/`
- [ ] Navigate to `ProjectPublish/` folder
- [ ] Run `VERIFY_FILES.bat`
- [ ] Verify all checks pass
- [ ] Run `UNBLOCK_FILES.bat` ← CRITICAL STEP!
- [ ] Wait for unblocking to complete
- [ ] Run `START_HERE.bat`
- [ ] Verify application starts without errors
- [ ] Open browser to http://localhost:5000
- [ ] Login with default credentials
- [ ] **Change default password immediately!**

### If DLL Load Errors Occur

- [ ] Close application (Ctrl+C)
- [ ] Run `UNBLOCK_FILES.bat` again
- [ ] If fails, use PowerShell method (Administrator):
  ```powershell
  Get-ChildItem -Recurse | Unblock-File
  ```
- [ ] Verify unblocking:
  ```powershell
  Get-Item .\SixLabors.ImageSharp.dll -Stream Zone.Identifier
  ```
  (expect "Cannot find path" error = unblocked)
- [ ] Restart application with `START_HERE.bat`

---

## Related Documentation

- **[16-BUILD-AND-RELEASE-PIPELINE.md](16-BUILD-AND-RELEASE-PIPELINE.md)** - Build-Release.ps1 detailed documentation
- **[04-STARTUP-AND-MIDDLEWARE.md](04-STARTUP-AND-MIDDLEWARE.md)** - Application startup process
- **[12-DATA-MIGRATIONS.md](12-DATA-MIGRATIONS.md)** - Database initialization

---

**End of Document** - Part of PROJECT COSMOGENESIS
**File:** `docs/genesis/15-AIR-GAPPED-DEPLOYMENT.md`
**Lines:** 1,141
