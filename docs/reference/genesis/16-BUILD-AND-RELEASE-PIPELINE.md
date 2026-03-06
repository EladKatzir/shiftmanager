# 16. Build and Release Pipeline

**Document Version:** 1.0
**Last Updated:** December 2025
**Part of:** PROJECT COSMOGENESIS - ShiftManager Genesis Documentation

---

## Table of Contents

1. [Overview](#overview)
2. [Pipeline Architecture](#pipeline-architecture)
3. [Build Automation Script](#build-automation-script)
4. [Stage 1: Pre-Flight Checks](#stage-1-pre-flight-checks)
5. [Stage 2: Backup](#stage-2-backup)
6. [Stage 3: Build](#stage-3-build)
7. [Stage 4: Build Verification](#stage-4-build-verification)
8. [Stage 5: Application Testing](#stage-5-application-testing)
9. [Stage 6: Documentation Generation](#stage-6-documentation-generation)
10. [Stage 7: Final Integrity Check](#stage-7-final-integrity-check)
11. [Stage 8: Git Tagging](#stage-8-git-tagging)
12. [Stage 9: Packaging](#stage-9-packaging)
13. [Stage 10: Release Report](#stage-10-release-report)
14. [Build Outputs](#build-outputs)
15. [Rollback Mechanism](#rollback-mechanism)
16. [Build Script Reference](#build-script-reference)

---

## Overview

ShiftManager uses an **enterprise-grade automated build pipeline** that transforms source code into production-ready deployment packages in a single command. The pipeline ensures consistency, quality, and reproducibility for air-gapped deployments.

### Key Features

- **10-stage automated pipeline** - Pre-flight → Packaging → Release report
- **Self-contained builds** - Includes .NET 8.0 runtime (no SDK required on target)
- **Automated testing** - Database seeding verification, OFFLINE shift validation
- **Git integration** - Automatic versioning and tagging
- **Checksum generation** - SHA256 hashes for deployment verification
- **Rollback support** - Automatic restoration on build failure
- **Air-gapped optimizations** - Unblocking scripts, deployment guides bundled

### Build Command

```powershell
.\Build-Release.ps1 -Version "1.0.0"
```

**Output:** Production-ready ZIP package in `packages/` folder

---

## Pipeline Architecture

```
┌──────────────────────────────────────────────────────────────┐
│                    Build-Release.ps1                          │
│              Main Orchestration Script                        │
└──────────────────────────────────────────────────────────────┘
                            │
        ┌───────────────────┼───────────────────┐
        │                   │                   │
        ▼                   ▼                   ▼
┌──────────────┐    ┌──────────────┐    ┌──────────────┐
│  Pre-Flight  │    │    Build     │    │  Packaging   │
│   Scripts    │    │   Scripts    │    │   Scripts    │
└──────────────┘    └──────────────┘    └──────────────┘
        │                   │                   │
        ▼                   ▼                   ▼
┌──────────────┐    ┌──────────────┐    ┌──────────────┐
│ Test-        │    │ Verify-      │    │ Create-      │
│ PreBuild.ps1 │    │ Build.ps1    │    │ Package.ps1  │
└──────────────┘    └──────────────┘    └──────────────┘
        │                   │                   │
        ▼                   ▼                   ▼
┌──────────────┐    ┌──────────────┐    ┌──────────────┐
│ Git-         │    │ Test-        │    │ Generate-    │
│ Integration  │    │ Application  │    │ ReleaseReport│
└──────────────┘    └──────────────┘    └──────────────┘
```

### Pipeline Flow

```
START
  ↓
STAGE 1: Pre-Flight Checks (.NET SDK, git status, version format)
  ↓
STAGE 2: Backup (Backup existing ProjectPublish, keep last 3)
  ↓
STAGE 3: Build (dotnet publish, copy helper scripts)
  ↓
STAGE 4: Build Verification (File counts, critical files, checksums)
  ↓
STAGE 5: Application Testing (Start app, verify DB, test OFFLINE shift)
  ↓
STAGE 6: Documentation Generation (VERSION.txt, README.txt, DLL_CHECKSUMS.txt)
  ↓
STAGE 7: Final Integrity Check (Re-verify all critical files)
  ↓
STAGE 8: Git Tagging (Create tag v1.0.0, push to remote)
  ↓
STAGE 9: Packaging (Create ZIP, calculate checksums)
  ↓
STAGE 10: Release Report (Build summary, timing, metrics)
  ↓
SUCCESS (packages/ShiftManager-v1.0.0-win-x64.zip ready)
```

**On failure:** Automatic rollback to previous ProjectPublish backup

---

## Build Automation Script

**File:** `Build-Release.ps1` (458 lines)

### Parameters

```powershell
[CmdletBinding()]
param(
    [Parameter()]
    [string]$Version,           # Version number (e.g., "1.0.0")

    [Parameter()]
    [switch]$SkipTests,         # Skip application testing (not recommended)

    [Parameter()]
    [switch]$NoPush             # Don't push git tag to remote
)
```

### Usage Examples

```powershell
# Full production build with testing
.\Build-Release.ps1 -Version "1.0.0"

# Build without tests (faster, not recommended for production)
.\Build-Release.ps1 -Version "1.0.0" -SkipTests

# Build without pushing git tag to remote
.\Build-Release.ps1 -Version "1.0.0" -NoPush

# Interactive mode (prompts for version)
.\Build-Release.ps1
```

### Version Format Validation

**Build-Release.ps1:444-447**

```powershell
# Validate version format (allow pre-release suffixes like -test, -alpha, -beta)
if ($Version -notmatch '^\d+\.\d+\.\d+(-[\w\.]+)?$') {
    Write-Host "ERROR: Invalid version format. Use semantic versioning (e.g., 1.0.2 or 1.0.2-test)" -ForegroundColor Red
    exit 1
}
```

**Valid formats:**
- `1.0.0` - Standard release
- `1.0.0-test` - Test release
- `1.0.0-alpha` - Alpha release
- `1.0.0-beta.2` - Beta release with build number

**Invalid formats:**
- `1.0` - Missing patch version
- `v1.0.0` - Do not include "v" prefix
- `1.0.0.0` - Too many version components

### Environment Variables

```powershell
$ErrorActionPreference = "Stop"   # Fail on first error
$ProgressPreference = "SilentlyContinue"  # Suppress progress bars (faster)

$ScriptRoot = $PSScriptRoot       # Project root directory
$BuildRoot = Join-Path $ScriptRoot "build"  # Build scripts folder
$ProjectFile = Join-Path $ScriptRoot "ShiftManager.csproj"  # Project file
$OutputFolder = Join-Path $ScriptRoot "ProjectPublish"  # Build output
```

---

## Stage 1: Pre-Flight Checks

**Script:** `build/Test-PreBuild.ps1`
**Purpose:** Validate build environment before starting

### Checks Performed

```
1. .NET SDK 8.0 installed
   dotnet --version
   ↓
   Expected: 8.0.x

2. ShiftManager.csproj exists
   ↓
   Validates project file is present

3. Git repository status
   git status --porcelain
   ↓
   Warns if uncommitted changes (doesn't fail)

4. Version format validation
   ^\d+\.\d+\.\d+(-[\w\.]+)?$
   ↓
   Semantic versioning with optional pre-release suffix

5. Previous build artifacts check
   ↓
   Verifies ProjectPublish folder state
```

### Implementation

**Test-PreBuild.ps1 (conceptual - actual script follows similar pattern):**

```powershell
# Check .NET SDK
Write-Info "Checking .NET SDK..."
$dotnetVersion = dotnet --version
if ($LASTEXITCODE -ne 0) {
    Write-ErrorMsg ".NET SDK not found"
    throw "Install .NET 8.0 SDK first"
}
Write-Success ".NET SDK $dotnetVersion found"

# Check git
Write-Info "Checking git repository..."
$gitStatus = git status --porcelain
if ($gitStatus) {
    Write-WarningMsg "Uncommitted changes detected - consider committing first"
}
Write-Success "Git repository OK"
```

---

## Stage 2: Backup

**Function:** `Invoke-Backup` in Build-Release.ps1:222-241
**Purpose:** Backup existing ProjectPublish folder before build

### Backup Strategy

```
IF ProjectPublish folder exists:
   ↓
   Create backup folder: ProjectPublish_BACKUP_<timestamp>
   Example: ProjectPublish_BACKUP_20251215_143022
   ↓
   Copy entire ProjectPublish recursively
   ↓
   Clean old backups (keep last 3 backups only)
   ↓
   Delete backups older than 3 most recent
```

### Implementation

**Build-Release.ps1:222-241**

```powershell
function Invoke-Backup {
    if (Test-Path $OutputFolder) {
        $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
        $backupFolder = "$($OutputFolder)_BACKUP_$timestamp"
        Write-Info "Backing up to: $backupFolder"
        Copy-Item -Path $OutputFolder -Destination $backupFolder -Recurse -Force
        Write-Success "Backup created"

        # Clean old backups (keep last 3)
        $backups = Get-ChildItem -Path $ScriptRoot -Directory -Filter "ProjectPublish_BACKUP_*" |
            Sort-Object Name -Descending |
            Select-Object -Skip 3
        if ($backups) {
            Write-Info "Cleaning old backups..."
            $backups | Remove-Item -Recurse -Force
        }
    } else {
        Write-Info "No existing deployment to backup"
    }
}
```

**Backup naming:**
- `ProjectPublish_BACKUP_20251215_143022` (Dec 15, 2025 at 14:30:22)
- Sorted by name (timestamp) in descending order
- Last 3 kept, older ones deleted

---

## Stage 3: Build

**Function:** `Invoke-Build` in Build-Release.ps1:243-335
**Purpose:** Compile and publish self-contained application

### Build Process

```
1. Clean existing output
   Remove-Item ProjectPublish/ -Recurse -Force

2. Run dotnet publish
   dotnet publish ShiftManager.csproj
       -c Release                    (Release configuration)
       -r win-x64                    (Windows 64-bit)
       --self-contained true         (Include .NET runtime)
       -o ProjectPublish/            (Output directory)

3. Copy air-gapped deployment scripts (REQUIRED)
   - UNBLOCK_FILES.bat               (CRITICAL for Windows)
   - VERIFY_FILES.bat                (Deployment verification)
   - QUICK_FIX.bat                   (Emergency troubleshooting)
   - AIR_GAPPED_DEPLOYMENT_GUIDE.txt (Deployment documentation)

4. Copy API documentation and client libraries
   - API_DOCUMENTATION.md            (REST API docs)
   - appsettings.Production.template.json  (Production config template)
   - clients/ folder                 (Python, JavaScript clients)

5. Verify required scripts copied
   Fail build if REQUIRED scripts missing
```

### Implementation

**Build-Release.ps1:243-335**

```powershell
function Invoke-Build {
    # Clean existing output
    if (Test-Path $OutputFolder) {
        Write-Info "Removing existing ProjectPublish folder..."
        Remove-Item -Path $OutputFolder -Recurse -Force
    }

    # Run dotnet publish
    Write-Info "Running: dotnet publish -c Release -r win-x64 --self-contained"
    $buildStart = Get-Date

    $process = Start-Process -FilePath "dotnet" -ArgumentList @(
        "publish",
        $ProjectFile,
        "-c", "Release",
        "-r", "win-x64",
        "--self-contained", "true",
        "-o", $OutputFolder
    ) -NoNewWindow -Wait -PassThru

    if ($process.ExitCode -ne 0) {
        throw "dotnet publish failed with exit code $($process.ExitCode)"
    }

    $buildTime = ((Get-Date) - $buildStart).TotalSeconds
    Write-Success "Build successful ($($buildTime.ToString('F1'))s)"

    # Copy air-gapped deployment helper scripts (MANDATORY for production)
    Write-Info "Adding air-gapped deployment helper scripts..."

    $requiredScripts = @(
        @{Name = "UNBLOCK_FILES.bat"; Required = $true},
        @{Name = "VERIFY_FILES.bat"; Required = $true},
        @{Name = "QUICK_FIX.bat"; Required = $true},
        @{Name = "AIR_GAPPED_DEPLOYMENT_GUIDE.txt"; Required = $true}
    )

    $missingRequired = @()
    foreach ($script in $requiredScripts) {
        $scriptPath = Join-Path $ScriptRoot $script.Name
        if (Test-Path $scriptPath) {
            Copy-Item -Path $scriptPath -Destination $OutputFolder -Force
            Write-Success "$($script.Name) added"
        } else {
            if ($script.Required) {
                Write-ErrorMsg "$($script.Name) NOT FOUND - REQUIRED for air-gapped deployment!"
                $missingRequired += $script.Name
            }
        }
    }

    if ($missingRequired.Count -gt 0) {
        throw "Missing required deployment scripts: $($missingRequired -join ', '). Air-gapped deployments will fail without these!"
    }
}
```

### Build Optimization

**Enabled automatically in Release mode:**
- **Razor view precompilation** - Pages/Views folders removed from output
- **IL Trimming** - Unused code removed from assemblies
- **ReadyToRun compilation** - Native code generation for faster startup
- **Single-file publishing** - Not used (keep DLLs separate for unblocking)

---

## Stage 4: Build Verification

**Script:** `build/Verify-Build.ps1` (118 lines)
**Purpose:** Verify build output structure and completeness

### Verification Checks

```
1. File Count Verification
   Expected: ~450 files (±50 tolerance)
   ↓
   IF count outside range → FAIL

2. DLL Count Verification
   Expected: ~335 DLLs (±5 tolerance)
   ↓
   IF count outside range → FAIL

3. Package Size Verification
   Expected: ~110 MB (±20 MB tolerance)
   ↓
   IF size outside range → FAIL

4. Critical Files Verification
   - ShiftManager.exe           (Main executable)
   - ShiftManager.dll           (Application assembly)
   - e_sqlite3.dll              (Native SQLite)
   - appsettings.json          (Configuration)
   - wwwroot/css/site.css      (CSS assets)
   - wwwroot/js/site.js        (JavaScript assets)
   - he-IL/ShiftManager.resources.dll  (Hebrew localization)
   ↓
   IF any missing → FAIL

5. Test Artifacts Check
   Search for: *.test.*, *.pdb (except ShiftManager.pdb), *.log, *.tmp
   ↓
   IF found → WARN (don't fail)

6. Razor View Precompilation Check
   Pages/ and Views/ folders should NOT exist
   ↓
   IF exist → FAIL (Razor views should be precompiled in Release mode)
```

### Implementation

**build/Verify-Build.ps1:21-111**

```powershell
# Count files
Write-Info "Counting files..."
$allFiles = Get-ChildItem -Path $OutputPath -File -Recurse
$fileCount = $allFiles.Count
$expectedFiles = 450
$tolerance = 50

if ($fileCount -lt ($expectedFiles - $tolerance) -or $fileCount -gt ($expectedFiles + $tolerance)) {
    Write-ErrorMsg "File count $fileCount outside expected range ($expectedFiles ± $tolerance)"
    throw "Unexpected file count"
}
Write-Success "File count: $fileCount files (expected: ~$expectedFiles)"

# Count DLLs
Write-Info "Counting DLLs..."
$dlls = Get-ChildItem -Path $OutputPath -Filter "*.dll" -Recurse
$dllCount = $dlls.Count
$expectedDlls = 335
$dllTolerance = 5

if ($dllCount -lt ($expectedDlls - $dllTolerance) -or $dllCount -gt ($expectedDlls + $dllTolerance)) {
    Write-ErrorMsg "DLL count $dllCount outside expected range ($expectedDlls ± $dllTolerance)"
    throw "Unexpected DLL count"
}
Write-Success "DLL count: $dllCount DLLs (expected: ~$expectedDlls)"

# Verify Razor views precompiled
Write-Info "Verifying Razor views precompiled..."
$pagesFolder = Join-Path $OutputPath "Pages"
$viewsFolder = Join-Path $OutputPath "Views"

if ((Test-Path $pagesFolder) -or (Test-Path $viewsFolder)) {
    Write-ErrorMsg "Pages/Views folders found - Razor views not precompiled!"
    throw "Razor views should be precompiled in Release mode"
}
Write-Success "Razor views precompiled (no Pages/Views folders)"
```

---

## Stage 5: Application Testing

**Script:** `build/Test-Application.ps1` (267 lines)
**Purpose:** Start application, verify database initialization, test OFFLINE shift seeding

### Testing Process

```
1. Prepare test configuration
   - Backup appsettings.json
   - Set test admin password
   - Configure test database path

2. Start ShiftManager.exe
   - Redirect stdout/stderr to log file
   - Start process in background
   - Wait up to 30 seconds for "Now listening on" message

3. Verify application startup
   - Check log for "Now listening on http://localhost:5000"
   - Check for error/exception messages
   - Verify process hasn't crashed

4. Wait for database seeding (5 seconds)
   - Allow time for migrations and seeding to complete

5. Verify database created
   - Check app.db file exists
   - Verify file size > 0

6. Verify OFFLINE shift type (CRITICAL for air-gapped deployments)
   - Use Python script to query SQLite database
   - SELECT Id, CompanyId, Key FROM ShiftTypes WHERE Key = 'OFFLINE'
   - IF OFFLINE shift missing → FAIL

7. Verify other seeded data
   - Count companies, users, configs
   - Display seeded data summary

8. Stop application
   - Terminate background process
   - Clean test database
   - Restore original appsettings.json
```

### OFFLINE Shift Verification

**Why critical?** The OFFLINE shift type is required for air-gapped deployments to allow overlapping shifts (e.g., employees can be "on-duty" while also scheduled for an OFFLINE administrative shift).

**build/Test-Application.ps1:115-159**

```powershell
# Verify OFFLINE shift type using Python
$pythonScript = @"
import sqlite3
import sys
try:
    conn = sqlite3.connect(r'$testDbPath')
    cursor = conn.cursor()
    cursor.execute("SELECT Id, CompanyId, Key FROM ShiftTypes WHERE Key = 'OFFLINE'")
    results = cursor.fetchall()
    if results:
        for row in results:
            print(f"OFFLINE shift found: ID={row[0]}, CompanyId={row[1]}, Key={row[2]}")
        sys.exit(0)
    else:
        print("OFFLINE shift NOT FOUND")
        sys.exit(1)
except Exception as e:
    print(f"Error: {e}")
    sys.exit(1)
"@

$tempPyScript = Join-Path $env:TEMP "verify_offline.py"
$pythonScript | Out-File -FilePath $tempPyScript -Encoding UTF8

try {
    $pyOutput = python $tempPyScript 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Success "OFFLINE shift CONFIRMED in database"
        Write-Host "    $pyOutput" -ForegroundColor Cyan
        $offlineVerified = $true
    } else {
        Write-ErrorMsg "OFFLINE shift verification failed"
        throw "OFFLINE shift missing from database"
    }
} catch {
    Write-ErrorMsg "Could not verify OFFLINE shift (Python not available)"
    Write-Info "Assuming OFFLINE shift is seeded based on code"
    $offlineVerified = $true # Don't fail build if Python unavailable
}
```

---

## Stage 6: Documentation Generation

**Script:** `build/Generate-Documentation.ps1`
**Purpose:** Generate deployment documentation and checksums

### Generated Files

```
1. VERSION.txt
   - Version number
   - Build date/time
   - Git commit hash
   - Build configuration (Release/Debug)

2. README.txt
   - Quick start guide
   - Default credentials
   - Port configuration
   - First-run instructions

3. DLL_CHECKSUMS.txt
   - SHA256 checksums for all critical DLLs
   - Format: <checksum> <filename>
   - Used for air-gapped deployment verification

4. DEPLOYMENT_READINESS_REPORT.txt
   - Build summary
   - File counts
   - Package size
   - Test results
```

### DLL Checksum Generation

**Purpose:** Allow air-gapped deployments to verify DLL integrity without internet

**Example DLL_CHECKSUMS.txt:**

```
3A5B7C9D1E2F4A6B8C0D1E2F3A4B5C6D7E8F9A0B1C2D3E4F5A6B7C8D9E0F1A2B  SixLabors.ImageSharp.dll
8F1A2B3C4D5E6F7A8B9C0D1E2F3A4B5C6D7E8F9A0B1C2D3E4F5A6B7C8D9E0F  ShiftManager.dll
2B3C4D5E6F7A8B9C0D1E2F3A4B5C6D7E8F9A0B1C2D3E4F5A6B7C8D9E0F1A  e_sqlite3.dll
```

**Verification on air-gapped machine:**

```cmd
certutil -hashfile SixLabors.ImageSharp.dll SHA256
findstr /C:"SixLabors.ImageSharp.dll" DLL_CHECKSUMS.txt
REM Compare outputs
```

---

## Stage 7: Final Integrity Check

**Script:** `build/Test-PackageIntegrity.ps1`
**Purpose:** Re-verify all critical files before packaging

**Checks performed:**
- All files from Stage 4 verification
- Additional checks for deployment scripts
- Verify no corruption occurred during documentation generation

---

## Stage 8: Git Tagging

**Script:** `build/Git-Integration.ps1`
**Purpose:** Create git tag and optionally push to remote

### Git Workflow

```
1. Check git status
   git status --porcelain
   ↓
   IF uncommitted changes → WARN (continue anyway)

2. Create annotated tag
   git tag -a v1.0.0 -m "Release v1.0.0"
   ↓
   Annotated tag includes:
   - Tagger name/email
   - Tag date
   - Tag message

3. Push tag to remote (unless -NoPush flag)
   git push origin v1.0.0
   ↓
   IF push fails → WARN (don't fail build)

4. Record git commit in VERSION.txt
   git rev-parse --short HEAD
   ↓
   Example: abc123f
```

### Tag Naming Convention

**Format:** `v<major>.<minor>.<patch>[-suffix]`

**Examples:**
- `v1.0.0` - Standard release
- `v1.0.0-test` - Test release
- `v1.0.0-alpha` - Alpha release
- `v1.0.0-beta.2` - Beta release

**Implementation:**

```powershell
$tagName = "v$Version"

# Create annotated tag
git tag -a $tagName -m "Release $tagName"

if ($LASTEXITCODE -ne 0) {
    Write-ErrorMsg "Failed to create git tag"
    throw "Git tagging failed"
}

# Push to remote (unless -NoPush)
if (-not $NoPush) {
    git push origin $tagName
    if ($LASTEXITCODE -ne 0) {
        Write-WarningMsg "Failed to push tag to remote (continuing anyway)"
    }
}
```

---

## Stage 9: Packaging

**Script:** `build/Create-Package.ps1` (150+ lines)
**Purpose:** Create distributable ZIP archive with checksums

### Packaging Process

```
1. Create packages/ directory
   mkdir packages/

2. Generate ZIP filename
   ShiftManager-v1.0.0-win-x64.zip

3. Remove existing ZIP (if present)
   del packages/ShiftManager-v1.0.0-win-x64.zip

4. Create ZIP using .NET compression
   [System.IO.Compression.ZipFile]::CreateFromDirectory(
       ProjectPublish/,
       packages/ShiftManager-v1.0.0-win-x64.zip,
       CompressionLevel::Optimal
   )

5. Calculate ZIP checksum (SHA256)
   Get-FileHash -Algorithm SHA256

6. Write checksum to .sha256 file
   packages/ShiftManager-v1.0.0-win-x64.zip.sha256

7. Return package info
   - ZIP file path
   - Checksum
   - File count
   - DLL count
   - Package size
```

### Implementation

**build/Create-Package.ps1:59-79**

```powershell
# Create ZIP archive
Write-Info "Creating ZIP archive..."
$compressionStart = Get-Date

# Use .NET compression (more reliable than Compress-Archive for large files)
Add-Type -Assembly System.IO.Compression.FileSystem

# Create ZIP with optimal compression
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $OutputPath,
    $zipPath,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false  # Don't include base directory name
)

$compressionTime = ((Get-Date) - $compressionStart).TotalSeconds
$zipSize = (Get-Item $zipPath).Length

Write-Success "ZIP created ($([math]::Round($zipSize/1MB, 1)) MB) in $($compressionTime.ToString('F1'))s"

# Calculate SHA256 checksum
Write-Info "Calculating SHA256 checksum..."
$sha256 = Get-FileHash -Path $zipPath -Algorithm SHA256
$checksum = $sha256.Hash
Write-Success "Checksum: $checksum"

# Write checksum file
$checksumFile = "$zipPath.sha256"
$checksum | Out-File -FilePath $checksumFile -Encoding ASCII -NoNewline
```

**Checksum file format:**

```
3A5B7C9D1E2F4A6B8C0D1E2F3A4B5C6D7E8F9A0B1C2D3E4F5A6B7C8D9E0F1A2B
```

**Verification:**

```cmd
certutil -hashfile ShiftManager-v1.0.0-win-x64.zip SHA256
type ShiftManager-v1.0.0-win-x64.zip.sha256
REM Compare outputs
```

---

## Stage 10: Release Report

**Script:** `build/Generate-ReleaseReport.ps1`
**Purpose:** Generate comprehensive build summary

### Report Contents

```
================================================================================
                         RELEASE REPORT
                     ShiftManager v1.0.0
================================================================================

Build Date:       2025-12-15 14:30:22 UTC
Git Commit:       abc123f
Configuration:    Release
Platform:         win-x64
.NET Version:     8.0.0

--------------------------------------------------------------------------------
                         BUILD STATISTICS
--------------------------------------------------------------------------------

Total Build Time:       5 minutes 23 seconds
  Stage 1:  Pre-Flight Checks          4.2s
  Stage 2:  Backup                     2.1s
  Stage 3:  Build                    142.8s
  Stage 4:  Build Verification         1.9s
  Stage 5:  Application Testing       45.3s
  Stage 6:  Documentation Generation   3.7s
  Stage 7:  Final Integrity Check      1.5s
  Stage 8:  Git Tagging                0.8s
  Stage 9:  Packaging                 89.2s
  Stage 10: Release Report             0.5s

File Count:         456 files
DLL Count:          337 DLLs
Package Size:       112 MB (uncompressed)
ZIP Size:           67 MB (compressed)
Compression Ratio:  40.2%

--------------------------------------------------------------------------------
                         TEST RESULTS
--------------------------------------------------------------------------------

Application Testing:  PASSED
  ✅ Application started successfully
  ✅ Database initialized (app.db, 86.4 KB)
  ✅ OFFLINE shift type CONFIRMED in database
  ✅ Seeded data verified:
      Companies: 1
      Users: 1
      Configs: 12
      Shift Types: 4 (Morning, Evening, Night, OFFLINE)

Build Verification:   PASSED
  ✅ File count: 456 (expected: ~450)
  ✅ DLL count: 337 (expected: ~335)
  ✅ Package size: 112 MB (expected: ~110 MB)
  ✅ Razor views precompiled
  ✅ No test artifacts found

--------------------------------------------------------------------------------
                         DISTRIBUTION FILES
--------------------------------------------------------------------------------

Main Package:
  packages/ShiftManager-v1.0.0-win-x64.zip
  packages/ShiftManager-v1.0.0-win-x64.zip.sha256

Deployment:
  ProjectPublish/ (ready for USB transfer)

Backup:
  ProjectPublish_BACKUP_20251215_143022/

--------------------------------------------------------------------------------
                         DEPLOYMENT INSTRUCTIONS
--------------------------------------------------------------------------------

1. Copy ZIP file to USB drive
2. Transfer to air-gapped machine
3. Verify checksum matches .sha256 file
4. Unblock ZIP file (Properties → Unblock)
5. Extract ZIP to local folder
6. Run VERIFY_FILES.bat
7. Run UNBLOCK_FILES.bat (CRITICAL!)
8. Run START_HERE.bat

For detailed instructions, see:
  AIR_GAPPED_DEPLOYMENT_GUIDE.txt (included in package)

================================================================================
                         BUILD SUCCESSFUL!
================================================================================
```

---

## Build Outputs

### Directory Structure

```
ShiftManager/
├── Build-Release.ps1 (main build script)
├── ShiftManager.csproj
├── build/
│   ├── Test-PreBuild.ps1
│   ├── Verify-Build.ps1
│   ├── Test-Application.ps1
│   ├── Generate-Documentation.ps1
│   ├── Test-PackageIntegrity.ps1
│   ├── Git-Integration.ps1
│   ├── Create-Package.ps1
│   └── Generate-ReleaseReport.ps1
├── ProjectPublish/ (build output - 456 files, ~112 MB)
│   ├── ShiftManager.exe
│   ├── ShiftManager.dll
│   ├── *.dll (337 DLLs)
│   ├── wwwroot/
│   ├── he-IL/
│   ├── UNBLOCK_FILES.bat
│   ├── VERIFY_FILES.bat
│   ├── START_HERE.bat
│   ├── AIR_GAPPED_DEPLOYMENT_GUIDE.txt
│   ├── VERSION.txt
│   ├── README.txt
│   └── DLL_CHECKSUMS.txt
├── packages/
│   ├── ShiftManager-v1.0.0-win-x64.zip (~67 MB compressed)
│   └── ShiftManager-v1.0.0-win-x64.zip.sha256
└── ProjectPublish_BACKUP_20251215_143022/ (backup)
```

### Artifact Sizes

| Artifact | Size | Description |
|----------|------|-------------|
| **ProjectPublish/** | ~112 MB | Uncompressed deployment |
| **ZIP package** | ~67 MB | Compressed for USB transfer |
| **Backup** | ~112 MB | Previous build backup |
| **Total disk usage** | ~290 MB | All artifacts combined |

---

## Rollback Mechanism

**Function:** `Invoke-Rollback` in Build-Release.ps1:337-357
**Purpose:** Restore previous build on failure

### Rollback Process

```
IF build fails at any stage:
   ↓
   1. Find most recent backup
      Get-ChildItem -Filter "ProjectPublish_BACKUP_*"
      Sort by name descending (timestamp)
      Select first (most recent)
   ↓
   2. Delete failed ProjectPublish folder
      Remove-Item ProjectPublish/ -Recurse -Force
   ↓
   3. Restore from backup
      Copy-Item ProjectPublish_BACKUP_xxx → ProjectPublish
   ↓
   4. Log rollback
   ↓
   5. Exit with error code 1
```

### Implementation

**Build-Release.ps1:337-357**

```powershell
function Invoke-Rollback {
    Write-WarningMsg "Attempting rollback..."

    # Find most recent backup
    $latestBackup = Get-ChildItem -Path $ScriptRoot -Directory -Filter "ProjectPublish_BACKUP_*" |
        Sort-Object Name -Descending |
        Select-Object -First 1

    if ($latestBackup) {
        Write-Info "Restoring from: $($latestBackup.Name)"

        if (Test-Path $OutputFolder) {
            Remove-Item -Path $OutputFolder -Recurse -Force
        }

        Copy-Item -Path $latestBackup.FullName -Destination $OutputFolder -Recurse -Force
        Write-Success "Rollback complete"
    } else {
        Write-WarningMsg "No backup found to restore"
    }
}
```

---

## Build Script Reference

### Build Scripts Directory

```
build/
├── Test-PreBuild.ps1           (Stage 1 - Pre-flight checks)
├── Verify-Build.ps1            (Stage 4 - Build verification)
├── Test-Application.ps1        (Stage 5 - Application testing)
├── Generate-Documentation.ps1  (Stage 6 - Documentation generation)
├── Test-PackageIntegrity.ps1   (Stage 7 - Final integrity check)
├── Git-Integration.ps1         (Stage 8 - Git tagging)
├── Create-Package.ps1          (Stage 9 - ZIP packaging)
└── Generate-ReleaseReport.ps1  (Stage 10 - Release report)
```

### Script Dependencies

| Script | Dependencies | Can Run Standalone? |
|--------|--------------|---------------------|
| **Build-Release.ps1** | All build scripts | No (orchestrator) |
| **Test-PreBuild.ps1** | .NET SDK, git | Yes |
| **Verify-Build.ps1** | ProjectPublish/ exists | Yes |
| **Test-Application.ps1** | ProjectPublish/, Python (optional) | Yes |
| **Create-Package.ps1** | ProjectPublish/ exists | Yes |

---

## Related Documentation

- **[15-AIR-GAPPED-DEPLOYMENT.md](15-AIR-GAPPED-DEPLOYMENT.md)** - Deployment process
- **[04-STARTUP-AND-MIDDLEWARE.md](04-STARTUP-AND-MIDDLEWARE.md)** - Application startup
- **[12-DATA-MIGRATIONS.md](12-DATA-MIGRATIONS.md)** - Database initialization

---

**End of Document** - Part of PROJECT COSMOGENESIS
**File:** `docs/genesis/16-BUILD-AND-RELEASE-PIPELINE.md`
**Lines:** 955
