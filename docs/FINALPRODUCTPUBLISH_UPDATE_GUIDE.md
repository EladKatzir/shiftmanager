# FinalProductPublish Update Guide

**Complete Guide for Updating Your Production Deployment Package**

Version: 1.0
Last Updated: December 13, 2025
For: ShiftManager Project

---

## Table of Contents

1. [Introduction](#introduction)
2. [When to Update](#when-to-update)
3. [Quick Reference Card](#quick-reference-card)
4. [Step-by-Step Update Process](#step-by-step-update-process)
5. [Understanding the Build System](#understanding-the-build-system)
6. [Verification Checklist](#verification-checklist)
7. [Rollback Procedure](#rollback-procedure)
8. [Troubleshooting Guide](#troubleshooting-guide)
9. [Automation Scripts Reference](#automation-scripts-reference)
10. [Glossary](#glossary)

---

## Introduction

### What is FinalProductPublish?

**Everyday Terms:**
FinalProductPublish is your "golden master" deployment package - like the final CD you burn before shipping a product. It's the approved, tested version of your application that's ready to be copied to USB drives and deployed to offline (air-gapped) computers.

**Technical Terms:**
FinalProductPublish is a production-ready, self-contained deployment artifact containing the compiled .NET 8.0 application, all dependencies, deployment scripts, and documentation. It represents a blessed release snapshot suitable for air-gapped deployment environments.

### What is ProjectPublish?

**Everyday Terms:**
ProjectPublish is your "factory output" - the folder where the automated build system puts freshly-built versions of your application. Think of it as the production line that creates new versions.

**Technical Terms:**
ProjectPublish is the output directory of the `Build-Release.ps1` automation pipeline (or manual `dotnet publish` command). It contains the result of compiling your source code into a deployable package.

### Why Update FinalProductPublish?

You update FinalProductPublish when:
- You've added new features (like the Griffin ADFS authentication)
- You've fixed important bugs
- You're preparing for a new production deployment
- You want to create a new release version

---

## When to Update

Update FinalProductPublish in these situations:

### ✅ **DO Update When:**

1. **After completing a new feature**
   - Example: "I just finished adding Griffin ADFS authentication"
   - You want to create a deployment package with the new feature

2. **Before deploying to production**
   - You're about to copy files to USB for air-gapped deployment
   - You want to ensure you have the latest version

3. **After fixing critical bugs**
   - Security vulnerabilities fixed
   - Crash bugs resolved
   - Data integrity issues corrected

4. **Creating a new release version**
   - Moving from v2.0.0 to v2.1.0
   - Preparing for customer delivery

### ❌ **DON'T Update When:**

1. **In the middle of development**
   - You're still coding and testing
   - Features aren't complete yet

2. **Code doesn't compile**
   - Build errors present
   - Tests are failing

3. **Changes aren't committed to git**
   - Uncommitted code changes
   - Working directory is dirty

---

## Quick Reference Card

```
┌─────────────────────────────────────────────────────────────────┐
│               QUICK UPDATE CHECKLIST                            │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  PREREQUISITES:                                                 │
│  ☐ All code changes are committed to git                       │
│  ☐ Code compiles without errors                                │
│  ☐ .NET 8.0 SDK installed                                      │
│  ☐ At least 500 MB free disk space                             │
│                                                                 │
│  MANUAL PROCESS (Step-by-Step):                                │
│  ☐ 1. Backup FinalProductPublish (run backup script)           │
│  ☐ 2. Build fresh ProjectPublish (dotnet publish or script)    │
│  ☐ 3. Copy ProjectPublish → FinalProductPublish                │
│  ☐ 4. Verify with VERIFY_FILES.bat                             │
│  ☐ 5. Commit to git                                            │
│  ☐ 6. Create git tag (e.g., v2.1.0)                            │
│                                                                 │
│  AUTOMATED PROCESS (One Command):                              │
│  ☐ Run: .\scripts\Update-FinalProductPublish.ps1 -Version "X.Y.Z"│
│                                                                 │
│  VERIFICATION:                                                  │
│  ☐ File count: ~560-570 files                                  │
│  ☐ Size: ~115 MB                                               │
│  ☐ ShiftManager.exe present                                    │
│  ☐ All .bat helper scripts present                             │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## Step-by-Step Update Process

### Step 1: Backup Current FinalProductPublish

**What:** Create a safety copy before making changes
**Why:** If something goes wrong, you can restore the old version
**How:** Run the backup script or copy manually

#### Option A: Automated Script (Recommended)

```powershell
.\scripts\Backup-FinalProductPublish.ps1
```

**What it does:**
- Creates timestamp (e.g., `20251213_143000`)
- Copies `FinalProductPublish` → `Backups/FinalProductPublish/FinalProductPublish_BACKUP_20251213_143000`
- Verifies backup integrity
- Shows confirmation message

#### Option B: Manual Backup

**Using File Explorer:**
1. Open File Explorer
2. Navigate to: `C:\Users\[YourName]\Downloads\ShiftManager`
3. Right-click the "FinalProductPublish" folder
4. Select "Copy"
5. Navigate to: `Backups\FinalProductPublish\`
6. Right-click → "Paste"
7. Rename the copied folder to: `FinalProductPublish_BACKUP_YYYYMMDD_HHMMSS`
   - Use current date/time (e.g., `FinalProductPublish_BACKUP_20251213_143000`)

**Using PowerShell:**
```powershell
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
Copy-Item -Path "FinalProductPublish" -Destination "Backups\FinalProductPublish\FinalProductPublish_BACKUP_$timestamp" -Recurse -Force
```

#### Verify Backup Success

**Check:**
- ✓ Backup folder exists in `Backups/FinalProductPublish/`
- ✓ Size should be ~115 MB
- ✓ Should contain ~560-570 files

**Troubleshoot:**
- **Error "Access Denied":** Run PowerShell as Administrator
- **Error "Not enough space":** Free up disk space (need ~120 MB free)
- **Backup seems incomplete:** Check file count matches original

---

### Step 2: Commit Your Code Changes

**What:** Save all your code changes to git
**Why:** The build system requires a clean git working tree
**How:** Stage and commit all changes

#### Check Current Status

```bash
git status
```

**What you'll see:**
- **Modified files:** Files you've changed (shown with `M`)
- **Untracked files:** New files you've added (shown with `??`)
- **Deleted files:** Files you've removed (shown with `D`)

#### Stage Changes

```bash
# Stage specific files
git add Data/AppDbContext.cs
git add Middleware/GriffinAuthenticationMiddleware.cs
git add Services/GriffinService.cs
# ... add all your changed files

# OR stage everything at once
git add -A
```

**Everyday Explanation:** "Staging" means telling git "these are the files I want to save in my next snapshot"

#### Create Commit

```bash
git commit -m "Your commit message here

Describe what you changed and why.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

**Good commit message examples:**
- `"Add Griffin ADFS authentication integration"`
- `"Fix database connection timeout issue"`
- `"Update Hebrew translations for new features"`

**Bad commit message examples:**
- `"Changes"`
- `"Fix"`
- `"Updated stuff"`

---

### Step 3: Build Fresh ProjectPublish

**What:** Create a new build with all your latest changes
**Why:** This compiles your code into a deployable package
**How:** Run the build script or manual command

#### Option A: Automated Build Script (Recommended)

```powershell
.\Build-Release.ps1 -Version "2.1.0"
```

**What it does (10 stages):**
1. Pre-Flight Checks (git clean, .NET SDK installed, tests pass)
2. Backup (creates `ProjectPublish_BACKUP_[timestamp]`)
3. Build (`dotnet publish` with all dependencies)
4. Build Verification (file count, DLL count)
5. Application Testing (startup test, database seeding)
6. Documentation Generation (README, VERSION.txt, guides)
7. Final Integrity Check
8. Git Tagging (creates `v2.1.0` tag)
9. Packaging (creates ZIP file)
10. Release Report

**Time:** 3-8 minutes

**Output:** Fresh `ProjectPublish/` folder (~563 files, ~115 MB)

#### Option B: Manual Build (If Script Fails)

```bash
# Remove old ProjectPublish
rm -rf ProjectPublish

# Build fresh version
dotnet publish ShiftManager.csproj -c Release -r win-x64 --self-contained true -o ProjectPublish

# Copy deployment helper scripts
cp UNBLOCK_FILES.bat ProjectPublish/
cp VERIFY_FILES.bat ProjectPublish/
cp QUICK_FIX.bat ProjectPublish/
cp CRITICAL_BEFORE_DEMO.txt ProjectPublish/
cp AIR_GAPPED_DEPLOYMENT_GUIDE.txt ProjectPublish/
cp API_DOCUMENTATION.md ProjectPublish/
cp appsettings.Production.template.json ProjectPublish/
cp -r clients ProjectPublish/
```

**Time:** 2-5 minutes

**What `dotnet publish` does:**
- **Compiles** your C# code into DLL files
- **Includes** .NET runtime (no .NET installation needed on target computer)
- **Copies** static files (CSS, JS, images)
- **Prepares** database files and configuration

#### Verify Build Success

```bash
# Check file count
ls ProjectPublish/ | wc -l

# Check size
du -sh ProjectPublish/

# Check build timestamp (should be today)
ls -lh ProjectPublish/ShiftManager.dll
```

**Expected Results:**
- File count: ~560-570 files
- Size: ~115 MB
- ShiftManager.dll timestamp: Today's date
- No error messages in build output

**Troubleshoot:**
- **Error "dotnet: command not found":** Install .NET 8.0 SDK
- **Error "Build FAILED":** Check code for compilation errors
- **Error "Git working tree has uncommitted changes":** Go back to Step 2
- **Warning messages:** Usually safe to ignore if build succeeds

---

### Step 4: Verify ProjectPublish Build

**What:** Double-check the build is complete and correct
**Why:** Catch problems before updating FinalProductPublish
**How:** Run verification checks

#### Automated Verification

```powershell
.\scripts\Verify-FinalProductPublish.ps1 -Path "ProjectPublish"
```

#### Manual Verification

**Critical Files Checklist:**
```bash
# Check main executable
ls ProjectPublish/ShiftManager.exe
ls ProjectPublish/ShiftManager.dll

# Check deployment scripts
ls ProjectPublish/UNBLOCK_FILES.bat
ls ProjectPublish/VERIFY_FILES.bat
ls ProjectPublish/START_HERE.bat

# Check configuration
ls ProjectPublish/appsettings.json
ls ProjectPublish/web.config

# Check documentation
ls ProjectPublish/README.txt
ls ProjectPublish/DEPLOYMENT_GUIDE.txt

# Check static files
ls ProjectPublish/wwwroot/css/
ls ProjectPublish/wwwroot/js/

# Check localization
ls ProjectPublish/he-IL/
```

**All should exist - no "file not found" errors**

#### File Count Verification

```bash
find ProjectPublish/ -type f | wc -l
```

**Expected:** 560-570 files

**If too low (<500):** Missing dependencies, rebuild
**If too high (>600):** May include backup folders, check for nested backups

#### Size Verification

```bash
du -sh ProjectPublish/
```

**Expected:** 110-120 MB

**If too small (<90 MB):** Missing .NET runtime, rebuild with `--self-contained`
**If too large (>150 MB):** May include unnecessary files, check build output

---

### Step 5: Update FinalProductPublish

**What:** Replace FinalProductPublish contents with fresh ProjectPublish
**Why:** This updates your production deployment package
**How:** Delete old contents, copy new contents

#### Automated Update

```powershell
# After backing up and building, run:
.\scripts\Update-FinalProductPublish.ps1 -Version "2.1.0"
```

#### Manual Update

**Using PowerShell:**
```powershell
# Step 1: Clear FinalProductPublish
Remove-Item -Path "FinalProductPublish\*" -Recurse -Force

# Step 2: Copy ProjectPublish contents
Copy-Item -Path "ProjectPublish\*" -Destination "FinalProductPublish\" -Recurse -Force

# Step 3: Verify
Write-Host "Files copied: $((Get-ChildItem -Path FinalProductPublish -Recurse -File).Count)"
```

**Using File Explorer:**
1. Open `FinalProductPublish` folder
2. Select all contents (Ctrl+A)
3. Delete (Shift+Delete for permanent deletion)
4. Confirm deletion
5. Open `ProjectPublish` folder
6. Select all contents (Ctrl+A)
7. Copy (Ctrl+C)
8. Navigate to `FinalProductPublish` folder
9. Paste (Ctrl+V)
10. Wait for copy to complete (~1-2 minutes)

**IMPORTANT:** Copy the CONTENTS of ProjectPublish, not the folder itself!

#### Verify Copy Success

```bash
# File count should match ProjectPublish
find FinalProductPublish/ -type f | wc -l

# Size should match ProjectPublish
du -sh FinalProductPublish/

# Check modification time (should be recent)
ls -lh FinalProductPublish/ShiftManager.dll
```

---

### Step 6: Verify FinalProductPublish

**What:** Confirm FinalProductPublish is complete and valid
**Why:** Ensure deployment package is ready for production
**How:** Run verification scripts and manual checks

#### Run VERIFY_FILES.bat

**In Command Prompt:**
```cmd
cd FinalProductPublish
VERIFY_FILES.bat
```

**Expected Output:**
```
Checking DLL files...
Found 336 DLL files (expected 330-340)
✓ DLL count OK

Checking critical files...
✓ ShiftManager.exe found
✓ ShiftManager.dll found
✓ e_sqlite3.dll found
...
✓ All critical files present

Verification PASSED
```

**If verification fails:** Check error messages, may need to rebuild

#### Manual Spot Checks

```bash
# Check file count
find FinalProductPublish/ -type f | wc -l
# Expected: ~560-570

# Check for Griffin ADFS changes (new in v2.1.0)
ls -lh FinalProductPublish/ShiftManager.dll
# Timestamp should be today

# Check deployment scripts present
ls FinalProductPublish/*.bat
# Should see: UNBLOCK_FILES.bat, VERIFY_FILES.bat, START_HERE.bat, QUICK_FIX.bat

# Check documentation files
ls FinalProductPublish/*.txt
# Should see: README.txt, VERSION.txt, DEPLOYMENT_GUIDE.txt, etc.
```

---

### Step 7: Commit to Git

**What:** Save the updated FinalProductPublish to version control
**Why:** Track changes, enable rollback if needed
**How:** Stage and commit the updated folder

```bash
# Stage FinalProductPublish changes
git add FinalProductPublish/

# Review what's being committed
git status
git diff --stat --cached

# Commit
git commit -m "Update FinalProductPublish to v2.1.0 with Griffin ADFS integration

Updates production deployment package to include Griffin ADFS authentication.
Previous version (v2.0.0) backed up to Backups/FinalProductPublish/FinalProductPublish_BACKUP_[timestamp]

Changes:
- Complete rebuild with Griffin ADFS middleware and services
- Updated deployment documentation (v2.1.0)
- Griffin configuration pages and callback handling
- Database migration for GriffinConfig storage
- Hebrew localization for ADFS UI

Files updated: 563 in FinalProductPublish/
Build output: ProjectPublish/ → FinalProductPublish/ (115 MB)

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

**Notes:**
- Replace `[timestamp]` with actual backup timestamp
- Adjust file count if different
- Describe what changed in this version

---

### Step 8: Create Git Tag

**What:** Mark this version in git history
**Why:** Easy reference point for this release
**How:** Create annotated tag

```bash
git tag -a v2.1.0 -m "Version 2.1.0 - Griffin ADFS Integration

Production release with enterprise SSO authentication via Griffin ADFS.

Major features:
- Griffin ADFS authentication middleware
- Owner configuration interface for ADFS setup
- Seamless SSO login flow with callback handling
- Database-backed configuration storage
- Hebrew localization support

Deployment package: FinalProductPublish/ (115 MB, self-contained .NET 8.0)
Air-gapped ready: Includes UNBLOCK_FILES.bat and complete deployment guides"
```

**Verify tag created:**
```bash
git tag -l "v2.1.0"
git show v2.1.0
```

**Optional - Push to remote:**
```bash
git push origin v2.1.0
```

---

## Understanding the Build System

### System Diagram

```
┌────────────────────────────────────────────────────────────┐
│  Build System Overview                                     │
├────────────────────────────────────────────────────────────┤
│                                                            │
│  Source Code                                               │
│  (C# files, .cshtml, etc.)                                 │
│         │                                                  │
│         ↓                                                  │
│  Build-Release.ps1  ──────→  ProjectPublish/              │
│  (automated build)           (build output)               │
│         │                           │                      │
│         │ creates                   ↓                      │
│         ↓                    FinalProductPublish/          │
│  ProjectPublish_BACKUP_*     (production releases)        │
│  (keeps last 3)                                           │
│                                                            │
│  Backups/FinalProductPublish/                             │
│  └── FinalProductPublish_BACKUP_YYYYMMDD_HHMMSS/          │
│      (keeps last 5)                                        │
│                                                            │
└────────────────────────────────────────────────────────────┘
```

### Folder Purposes

| Folder | Purpose (Everyday) | Purpose (Technical) |
|--------|-------------------|---------------------|
| **Source Files** | Your code you're editing | C# source code, Razor pages, configuration |
| **Build-Release.ps1** | The "factory" that builds deployment packages | Automated CI/CD pipeline script |
| **ProjectPublish** | Fresh build output from the "factory" | dotnet publish output directory |
| **ProjectPublish_BACKUP_*** | Safety copies of recent builds | Automatic rollback points (3 retained) |
| **FinalProductPublish** | Your "approved for release" version | Blessed production deployment artifact |
| **Backups/FinalProductPublish/** | Archive of old approved versions | Manual backup storage (5 retained) |

### Build Process Flow

```
1. Pre-Flight Checks
   ├─ Is git working tree clean? ✓
   ├─ Is .NET 8.0 SDK installed? ✓
   ├─ Do tests pass? ✓
   └─ Is disk space sufficient? ✓

2. Backup
   └─ Create ProjectPublish_BACKUP_[timestamp]

3. Build
   ├─ Run: dotnet publish -c Release -r win-x64 --self-contained
   ├─ Copy deployment helper scripts
   └─ Copy API documentation and client libraries

4. Verification
   ├─ File count check (450-500 expected)
   ├─ DLL count check (330-340 expected)
   └─ Critical files present check

5. Testing
   ├─ Startup test (application launches)
   ├─ Database seeding test
   └─ OFFLINE shift type verification

6. Documentation
   ├─ Generate VERSION.txt
   ├─ Generate README.txt
   └─ Generate DEPLOYMENT_GUIDE.txt

7. Integrity
   ├─ SHA256 checksums for critical DLLs
   └─ Package structure validation

8. Git Tagging
   └─ Create v[version] tag

9. Packaging
   ├─ Create ZIP: packages/ShiftManager-v[version]-win-x64.zip
   └─ Generate DLL_CHECKSUMS.txt

10. Report
    └─ Display build summary and next steps
```

### Key Build Commands

| Command | What It Does (Everyday) | What It Does (Technical) |
|---------|------------------------|--------------------------|
| `dotnet publish` | Compiles your code into a runnable program | Compiles C# to IL, includes runtime, packages dependencies |
| `-c Release` | Build the "final version" (not debug) | Compile with optimizations, no debug symbols |
| `-r win-x64` | Make it work on 64-bit Windows | Target Windows x64 runtime identifier |
| `--self-contained true` | Include everything (no install needed) | Bundle .NET runtime with application |
| `-o ProjectPublish` | Put output in ProjectPublish folder | Specify output directory path |

---

## Verification Checklist

Use this checklist after updating FinalProductPublish:

### File Structure Verification

```
☐ FinalProductPublish/
  ☐ ShiftManager.exe (main executable) ~148 KB
  ☐ ShiftManager.dll (main application) ~3.7 MB
  ☐ e_sqlite3.dll (SQLite database) ~1.7 MB
  ☐ SixLabors.ImageSharp.dll (image processing) ~2.1 MB
  ☐ app.db (pre-seeded database) ~400 KB
  ☐ appsettings.json (configuration)
  ☐ web.config (IIS configuration)

  ☐ Deployment Scripts:
    ☐ START_HERE.bat
    ☐ UNBLOCK_FILES.bat
    ☐ VERIFY_FILES.bat
    ☐ QUICK_FIX.bat

  ☐ Documentation:
    ☐ README.txt
    ☐ VERSION.txt
    ☐ DEPLOYMENT_GUIDE.txt
    ☐ QUICK_START.txt
    ☐ UPGRADE_GUIDE.txt
    ☐ AIR_GAPPED_DEPLOYMENT_GUIDE.txt
    ☐ API_DOCUMENTATION.md

  ☐ Folders:
    ☐ wwwroot/ (static files: CSS, JS, images)
    ☐ he-IL/ (Hebrew localization)
    ☐ clients/ (API client libraries)
```

### Content Verification

```
☐ VERSION.txt shows correct version number
☐ ShiftManager.dll timestamp is recent (today)
☐ File count: 560-570 files
☐ Total size: 110-120 MB
☐ No "BACKUP" folders nested inside
☐ wwwroot/css/ contains site.css, rtl.css, shift-swap-game.css
☐ wwwroot/js/ contains site.js, myteam.js, shift-swap-game.js
☐ he-IL/ contains ShiftManager.resources.dll
```

### Functional Verification (Optional)

```
☐ Run VERIFY_FILES.bat - passes all checks
☐ Run START_HERE.bat - application launches without errors
☐ Open http://localhost:5000 - login page appears
☐ Check application version in UI matches expected
```

---

## Rollback Procedure

If something goes wrong after updating FinalProductPublish, follow this procedure to restore the previous version.

### When to Rollback

Rollback if:
- ❌ VERIFY_FILES.bat fails
- ❌ Application won't start
- ❌ Critical files are missing
- ❌ File count is way off (< 500 or > 600)
- ❌ You discover a critical bug after updating

### Automated Rollback

```powershell
# List available backups
.\scripts\Restore-FinalProductPublish.ps1

# Restore specific backup
.\scripts\Restore-FinalProductPublish.ps1 -BackupTimestamp "20251213_120024"
```

### Manual Rollback (PowerShell)

**Step 1: Find Your Backup**
```powershell
ls Backups/FinalProductPublish/
```

Example output:
```
FinalProductPublish_BACKUP_20251201_003438  (old backup from Dec 1)
FinalProductPublish_BACKUP_20251213_120024  (backup from today)
```

**Step 2: Choose the Correct Backup**
- Use the MOST RECENT backup from before your update
- Check timestamp in folder name: `YYYYMMDD_HHMMSS`
- Example: `20251213_120024` = December 13, 2025 at 12:00:24

**Step 3: Restore**
```powershell
# Delete current FinalProductPublish
Remove-Item -Path "FinalProductPublish" -Recurse -Force

# Copy backup to FinalProductPublish
Copy-Item -Path "Backups\FinalProductPublish\FinalProductPublish_BACKUP_20251213_120024" -Destination "FinalProductPublish" -Recurse -Force
```

**Step 4: Verify Restoration**
```bash
# Check file count
find FinalProductPublish/ -type f | wc -l

# Run verification
cd FinalProductPublish
./VERIFY_FILES.bat
```

### Manual Rollback (File Explorer)

1. Open `Backups\FinalProductPublish\` folder
2. Find the backup you want to restore (check timestamp)
3. Right-click the backup folder → Copy
4. Navigate to project root directory
5. **Delete the current `FinalProductPublish` folder**
6. Paste the backup folder
7. Rename it from `FinalProductPublish_BACKUP_YYYYMMDD_HHMMSS` to `FinalProductPublish`

### Git Rollback (If Committed)

If you already committed the bad update to git:

```bash
# See recent commits
git log --oneline -5

# Revert to previous commit (soft - keeps files)
git reset --soft HEAD~1

# OR hard reset (WARNING: loses uncommitted work)
git reset --hard HEAD~1

# OR create a revert commit (safe, preserves history)
git revert HEAD
```

**Which to use:**
- `--soft`: Keep your files, undo the commit only
- `--hard`: Completely undo the commit and file changes (dangerous!)
- `revert`: Create a new commit that undoes the previous one (safest for shared repos)

---

## Troubleshooting Guide

### Problem: Build-Release.ps1 Fails with "Git Working Tree Has Uncommitted Changes"

**Symptom:**
```
❌ Git working tree has uncommitted changes
Uncommitted files:
 M Data/AppDbContext.cs
 M Pages/Auth/Login.cshtml.cs
 ?? Models/GriffinConfig.cs
```

**Cause:**
You have unsaved changes in git

**Solution:**
```bash
# Option 1: Commit the changes
git add -A
git commit -m "Your commit message"

# Option 2: Temporarily stash changes
git stash
# ... run build ...
git stash pop
```

---

### Problem: Build-Release.ps1 Fails with "Unexpected File Count"

**Symptom:**
```
❌ File count 565 outside expected range (450 ± 50)
```

**Cause:**
Extra files in build output (often nested backup folders)

**Solution:**
```bash
# Check for backup folders in ProjectPublish
find ProjectPublish/ -name "*BACKUP*" -type d

# Remove nested backups if found
rm -rf ProjectPublish/ProjectPublish_BACKUP_*
rm -rf ProjectPublish/FinalProductPublish_BACKUP_*

# Rebuild manually
rm -rf ProjectPublish
dotnet publish ShiftManager.csproj -c Release -r win-x64 --self-contained true -o ProjectPublish
```

---

### Problem: "dotnet: command not found"

**Symptom:**
```
bash: dotnet: command not found
```

**Cause:**
.NET SDK not installed or not in PATH

**Solution:**
1. Download .NET 8.0 SDK from: https://dotnet.microsoft.com/download/dotnet/8.0
2. Run installer
3. Restart terminal/PowerShell
4. Verify: `dotnet --version` (should show 8.0.x)

---

### Problem: VERIFY_FILES.bat Reports Missing DLLs

**Symptom:**
```
❌ Missing critical DLL: Microsoft.EntityFrameworkCore.dll
Found 280 DLLs (expected 330-340)
```

**Cause:**
Build didn't include all dependencies (likely not self-contained)

**Solution:**
```bash
# Rebuild with explicit self-contained flag
dotnet publish ShiftManager.csproj -c Release -r win-x64 --self-contained true -o ProjectPublish

# Verify self-contained
ls ProjectPublish/*.dll | wc -l
# Should be ~336 DLLs
```

---

### Problem: FinalProductPublish Size is Too Small

**Symptom:**
- Size shows ~40 MB instead of ~115 MB
- Missing many DLL files

**Cause:**
Build created framework-dependent deployment instead of self-contained

**Solution:**
```bash
# Rebuild with correct flags
dotnet publish ShiftManager.csproj -c Release -r win-x64 --self-contained true -o ProjectPublish

# Self-contained includes .NET runtime (~70 MB additional)
```

---

### Problem: "Access Denied" When Deleting/Copying Files

**Symptom:**
```
Remove-Item : Access to the path is denied
```

**Cause:**
- Files are read-only
- Files are in use by another process
- Insufficient permissions

**Solution:**
```powershell
# Option 1: Run PowerShell as Administrator
# Right-click PowerShell → "Run as Administrator"

# Option 2: Close programs using the files
# Check if ShiftManager.exe is running:
tasklist | findstr ShiftManager
# Kill if running:
taskkill /F /IM ShiftManager.exe

# Option 3: Remove read-only attributes
Get-ChildItem -Path FinalProductPublish -Recurse | ForEach-Object { $_.Attributes = 'Normal' }
```

---

### Problem: Git Won't Commit - "Files Too Large"

**Symptom:**
```
error: File FinalProductPublish/coreclr.dll is 5.00 MB; this exceeds GitHub's file size limit of 100.00 MB
```

**Cause:**
Some DLL files are large, and git is warning about size

**Solution:**
```bash
# This is normal for FinalProductPublish
# The files are supposed to be in git (see .gitignore exceptions)
# GitHub allows files up to 100 MB - our largest is ~5 MB
# Just proceed with the commit

git add FinalProductPublish/
git commit -m "Update FinalProductPublish to v2.1.0"
```

**Note:** .gitignore has explicit exceptions to allow FinalProductPublish:
```
!FinalProductPublish/
!FinalProductPublish/**
```

---

### Problem: Application Won't Start After Update

**Symptom:**
- Double-clicking START_HERE.bat shows error
- Application crashes immediately
- "Failed to load configuration"

**Diagnosis:**
```bash
# Check appsettings.json is present
ls FinalProductPublish/appsettings.json

# Check for SEED_ADMIN_PASSWORD setting
grep SEED_ADMIN_PASSWORD FinalProductPublish/appsettings.json
```

**Solution:**
```bash
# If appsettings.json is missing:
cp appsettings.json FinalProductPublish/

# If SEED_ADMIN_PASSWORD is not set:
# Edit FinalProductPublish/appsettings.json
# Set: "SEED_ADMIN_PASSWORD": "YourStrongPassword123!"
```

---

### Problem: Hebrew Text Shows as Gibberish

**Symptom:**
- Hebrew text displays as ���� or boxes
- RTL layout doesn't work

**Diagnosis:**
```bash
# Check he-IL folder exists
ls FinalProductPublish/he-IL/

# Check resource DLL is present
ls FinalProductPublish/he-IL/ShiftManager.resources.dll
```

**Solution:**
```bash
# If he-IL folder is missing, rebuild:
dotnet publish ShiftManager.csproj -c Release -r win-x64 --self-contained true -o ProjectPublish

# Localization files are embedded during build
```

---

## Automation Scripts Reference

The `scripts/` folder contains PowerShell scripts to automate the update process.

### Backup-FinalProductPublish.ps1

**Purpose:** Automated backup with timestamp

**Usage:**
```powershell
.\scripts\Backup-FinalProductPublish.ps1
```

**What it does:**
1. Generates timestamp (e.g., `20251213_143000`)
2. Creates `Backups/FinalProductPublish/` if missing
3. Copies `FinalProductPublish` → `Backups/FinalProductPublish/FinalProductPublish_BACKUP_[timestamp]`
4. Verifies backup integrity (file count, size)
5. Implements retention policy (keeps last 5 backups, deletes older)
6. Shows success message with backup location

**Output:**
```
Creating backup of FinalProductPublish...
  Timestamp: 20251213_143000
  Destination: Backups/FinalProductPublish/FinalProductPublish_BACKUP_20251213_143000

Copying files... Done!
Verifying backup... ✓ OK (563 files, 115 MB)

Cleaning old backups... Removed 2 old backups (keeping last 5)

✓ Backup completed successfully!
  Location: Backups/FinalProductPublish/FinalProductPublish_BACKUP_20251213_143000
```

---

### Update-FinalProductPublish.ps1

**Purpose:** Full automation of the update process

**Usage:**
```powershell
# Basic usage
.\scripts\Update-FinalProductPublish.ps1 -Version "2.1.0"

# Skip backup step (not recommended)
.\scripts\Update-FinalProductPublish.ps1 -Version "2.1.0" -SkipBackup

# Skip tests (faster but risky)
.\scripts\Update-FinalProductPublish.ps1 -Version "2.1.0" -SkipTests
```

**Parameters:**
- `-Version` (required): Version number for build (e.g., "2.1.0")
- `-SkipBackup`: Skip backup step (not recommended)
- `-SkipTests`: Pass to Build-Release.ps1 (faster but less safe)
- `-CommitChanges`: Auto-commit git changes first

**What it does:**
1. Checks for uncommitted changes (warns if found, unless `-CommitChanges` used)
2. Runs `Backup-FinalProductPublish.ps1`
3. Runs `Build-Release.ps1 -Version $Version`
4. Verifies ProjectPublish build
5. Clears FinalProductPublish contents
6. Copies ProjectPublish → FinalProductPublish
7. Verifies FinalProductPublish
8. Shows summary report

**Output:**
```
═══════════════════════════════════════════════
  FinalProductPublish Update Tool
═══════════════════════════════════════════════

[STEP 1/6] Checking git status...
  ✓ Working tree clean

[STEP 2/6] Backing up FinalProductPublish...
  ✓ Backup created: Backups/FinalProductPublish/FinalProductPublish_BACKUP_20251213_143000

[STEP 3/6] Building ProjectPublish v2.1.0...
  ... (Build-Release.ps1 output) ...
  ✓ Build completed successfully

[STEP 4/6] Verifying ProjectPublish...
  ✓ File count: 563 files
  ✓ Size: 115 MB
  ✓ Critical files present

[STEP 5/6] Updating FinalProductPublish...
  ✓ Contents cleared
  ✓ Files copied from ProjectPublish

[STEP 6/6] Verifying FinalProductPublish...
  ✓ File count: 563 files
  ✓ Size: 115 MB
  ✓ VERIFY_FILES.bat passed

═══════════════════════════════════════════════
  UPDATE SUCCESSFUL!
═══════════════════════════════════════════════

Version:          v2.1.0
Files:            563
Size:             115 MB
Backup:           Backups/FinalProductPublish/FinalProductPublish_BACKUP_20251213_143000

Next steps:
  1. Verify FinalProductPublish contents
  2. Commit to git: git add FinalProductPublish/
  3. Create tag: git tag -a v2.1.0

═══════════════════════════════════════════════
```

---

### Restore-FinalProductPublish.ps1

**Purpose:** Rollback helper if update fails

**Usage:**
```powershell
# List available backups
.\scripts\Restore-FinalProductPublish.ps1

# Restore specific backup
.\scripts\Restore-FinalProductPublish.ps1 -BackupTimestamp "20251213_120024"

# Force restore without confirmation
.\scripts\Restore-FinalProductPublish.ps1 -BackupTimestamp "20251213_120024" -Force
```

**Parameters:**
- `-BackupTimestamp`: Timestamp of backup to restore (e.g., "20251213_120024")
- `-Force`: Skip confirmation prompt

**What it does:**
1. If no timestamp provided, lists available backups and exits
2. Validates backup exists at `Backups/FinalProductPublish/FinalProductPublish_BACKUP_[timestamp]`
3. Shows confirmation prompt (unless `-Force`)
4. Deletes current FinalProductPublish
5. Copies backup → FinalProductPublish
6. Verifies restoration (file count, size)
7. Shows success message

**Output (list mode):**
```
Available FinalProductPublish backups:

  20251201_003438  (Dec 1, 2025 00:34:38)  - 378 files, 111 MB
  20251213_120024  (Dec 13, 2025 12:00:24) - 563 files, 115 MB

Usage:
  .\scripts\Restore-FinalProductPublish.ps1 -BackupTimestamp "20251213_120024"
```

**Output (restore mode):**
```
Restoring FinalProductPublish from backup...

Source: Backups/FinalProductPublish/FinalProductPublish_BACKUP_20251213_120024
Target: FinalProductPublish

This will DELETE the current FinalProductPublish contents.
Are you sure? (y/N): y

Deleting current FinalProductPublish... Done
Copying backup... Done
Verifying restoration... ✓ OK (563 files, 115 MB)

✓ Restoration completed successfully!
```

---

### Verify-FinalProductPublish.ps1

**Purpose:** Comprehensive integrity check

**Usage:**
```powershell
# Verify FinalProductPublish (default)
.\scripts\Verify-FinalProductPublish.ps1

# Verify a specific folder
.\scripts\Verify-FinalProductPublish.ps1 -Path "ProjectPublish"
```

**Parameters:**
- `-Path`: Path to verify (default: "FinalProductPublish")

**What it does:**
1. Checks file count (expects ~560-570 files)
2. Checks size (expects ~115 MB)
3. Verifies critical files present:
   - `ShiftManager.exe`, `ShiftManager.dll`
   - All deployment helper scripts (.bat files)
   - Documentation files (.txt files)
   - `wwwroot/` folder
   - `he-IL/` folder (Hebrew localization)
4. Checks VERSION.txt (if `-ExpectedVersion` provided)
5. Runs VERIFY_FILES.bat
6. Shows detailed report (pass/fail for each check)

**Output:**
```
═══════════════════════════════════════════════
  FinalProductPublish Verification
═══════════════════════════════════════════════

[CHECK 1/7] File count...
  Found: 563 files
  Expected: 560-570 files
  ✓ PASS

[CHECK 2/7] Total size...
  Found: 115 MB
  Expected: 110-120 MB
  ✓ PASS

[CHECK 3/7] Critical executables...
  ✓ ShiftManager.exe found (148 KB)
  ✓ ShiftManager.dll found (3.7 MB)
  ✓ PASS

[CHECK 4/7] Deployment scripts...
  ✓ UNBLOCK_FILES.bat found
  ✓ VERIFY_FILES.bat found
  ✓ START_HERE.bat found
  ✓ QUICK_FIX.bat found
  ✓ PASS

[CHECK 5/7] Documentation files...
  ✓ README.txt found
  ✓ VERSION.txt found
  ✓ DEPLOYMENT_GUIDE.txt found
  ✓ PASS

[CHECK 6/7] Static files...
  ✓ wwwroot/css/ exists (3 files)
  ✓ wwwroot/js/ exists (5 files)
  ✓ PASS

[CHECK 7/7] Localization...
  ✓ he-IL/ folder exists
  ✓ he-IL/ShiftManager.resources.dll found
  ✓ PASS

═══════════════════════════════════════════════
  VERIFICATION RESULT: ✓ ALL CHECKS PASSED
═══════════════════════════════════════════════
```

---

## Glossary

**Technical → Everyday Translation**

| Technical Term | Everyday Explanation |
|----------------|---------------------|
| **Self-contained deployment** | Package that includes everything - no extra software needed on the target computer |
| **Build artifact** | The final output files from compiling your code |
| **Git working tree** | The current state of your files in the project folder |
| **Staged changes** | Files you've told git "I want to save these in my next snapshot" |
| **Commit** | A snapshot of your code at a specific point in time |
| **Git tag** | A bookmark in your code history marking a specific version |
| **Backup retention policy** | How many old backups to keep before deleting them |
| **DLL (Dynamic Link Library)** | A file containing code that the application needs to run |
| **ProjectPublish** | The folder where the build system puts freshly-built versions |
| **FinalProductPublish** | Your approved, ready-for-deployment version |
| **Air-gapped deployment** | Installing on a computer with no internet connection |
| **Rollback** | Undoing recent changes and going back to a previous version |
| **Build pipeline** | Automated series of steps to create a deployment package |
| **Verification** | Checking that everything is correct and complete |
| **Integrity check** | Making sure files haven't been corrupted or lost |
| **dotnet publish** | Command that compiles C# code into a runnable application |
| **Release build** | Optimized version for production (not for debugging) |
| **Debug build** | Version with extra information for finding bugs |
| **Runtime** | Software that makes your application run (like .NET) |
| **Dependency** | Other software/libraries your application needs to work |
| **Localization** | Translating the application to different languages |
| **RTL (Right-to-Left)** | Text direction for languages like Hebrew and Arabic |
| **Migration** | Database schema change/update |
| **Seeding** | Pre-populating database with initial data |

---

## Quick Tips

### Speed Up the Process

1. **Use automation scripts** - Much faster than manual steps
2. **Keep git clean** - Commit regularly to avoid pre-flight check delays
3. **Run builds in background** - Continue working while building
4. **Use VS Code terminal** - Switch between tabs instead of multiple windows

### Avoid Common Mistakes

1. **Always backup first** - Never skip the backup step
2. **Check git status** - Commit before building
3. **Verify before deploying** - Run VERIFY_FILES.bat
4. **Keep documentation updated** - Update VERSION.txt
5. **Test on dev machine first** - Don't deploy untested builds

### Best Practices

1. **Semantic versioning** - Use X.Y.Z format (Major.Minor.Patch)
   - Major: Breaking changes (v1.0.0 → v2.0.0)
   - Minor: New features (v2.0.0 → v2.1.0)
   - Patch: Bug fixes (v2.1.0 → v2.1.1)

2. **Clear commit messages** - Describe WHAT and WHY
3. **Tag every release** - Easy to find versions in git history
4. **Keep backups** - At least last 5 versions
5. **Document changes** - Update VERSION.txt with changes

---

## Support & Help

### Getting Help

1. **Check this guide first** - Most common issues covered
2. **Check troubleshooting section** - Known problems and solutions
3. **Check git history** - See what changed recently
4. **Check backup** - You can always rollback

### Useful Commands

```bash
# Check current version
cat FinalProductPublish/VERSION.txt | head -10

# Count files
find FinalProductPublish/ -type f | wc -l

# Check size
du -sh FinalProductPublish/

# List backups
ls -lh Backups/FinalProductPublish/

# Check git status
git status

# See recent commits
git log --oneline -10

# See recent tags
git tag -l | sort -V | tail -5
```

---

**End of Guide**

Last Updated: December 13, 2025
Guide Version: 1.0
For: ShiftManager v2.1.0+
