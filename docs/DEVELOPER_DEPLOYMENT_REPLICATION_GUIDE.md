# Developer Deployment Replication Guide
## Creating Clean Air-Gapped Deployment Packages for ShiftManager

**Version**: 2.1.0
**Date**: December 16, 2025
**Purpose**: Step-by-step guide to create clean deployment packages without nested junk folders

---

## Prerequisites

### Required Software
- **.NET 8.0 SDK** or later
- **Git** for version control
- **PowerShell** 5.1 or later (for Windows)
- **Bash** (Git Bash on Windows, or native on Linux/Mac)

### Knowledge Requirements
- Basic understanding of .NET publishing
- Familiarity with command-line operations
- Understanding of self-contained deployments

---

## Problem Background

### The Issue
When publishing .NET applications, build artifacts can accumulate in the project directory. If these artifacts (like `Backups/`, `FinalProductPublish/`, or `ProjectPublish/` folders) are not excluded from the build, they get copied into new deployment packages, creating:

- **Nested folder structures** (folders within folders)
- **Bloated packages** (hundreds of unnecessary files)
- **Deployment confusion** (unclear what should be deployed)

### The Solution
1. Explicitly exclude deployment artifacts in the `.csproj` file
2. Clean existing artifacts before building
3. Use automated verification to ensure clean builds

---

## Step-by-Step Process

### Step 1: Configure Project to Exclude Deployment Artifacts

**File**: `ShiftManager.csproj`

**What to Add**: Exclusions for deployment-related folders

**Location**: Add this `<ItemGroup>` after the existing test exclusions:

```xml
<ItemGroup>
  <Content Remove="Backups/**" />
  <Content Remove="FinalProductPublish/**" />
  <Content Remove="ProjectPublish/**" />
  <Content Remove="packages/**" />
  <Content Remove=".claude/**" />
  <None Remove="Backups/**" />
  <None Remove="FinalProductPublish/**" />
  <None Remove="ProjectPublish/**" />
  <None Remove="packages/**" />
  <None Remove=".claude/**" />
</ItemGroup>
```

**Why This Works**:
- `<Content Remove>` - Prevents files from being copied as content
- `<None Remove>` - Prevents files from being included as miscellaneous items
- `/**` glob pattern - Matches all files and subdirectories

**Full Example** (after adding):
```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
  </PropertyGroup>

  <ItemGroup>
    <Compile Remove="ShiftManager.Tests/**" />
    <Content Remove="ShiftManager.Tests/**" />
    <EmbeddedResource Remove="ShiftManager.Tests/**" />
    <None Remove="ShiftManager.Tests/**" />
  </ItemGroup>

  <!-- ADD THIS ITEMGROUP -->
  <ItemGroup>
    <Content Remove="Backups/**" />
    <Content Remove="FinalProductPublish/**" />
    <Content Remove="ProjectPublish/**" />
    <Content Remove="packages/**" />
    <Content Remove=".claude/**" />
    <None Remove="Backups/**" />
    <None Remove="FinalProductPublish/**" />
    <None Remove="ProjectPublish/**" />
    <None Remove="packages/**" />
    <None Remove=".claude/**" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="9.0.9">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="9.0.9" />
    <PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore" Version="8.0.0" />
    <PackageReference Include="SixLabors.ImageSharp" Version="3.1.11" />
  </ItemGroup>
</Project>
```

---

### Step 2: Clean Existing Deployment Artifacts

**Goal**: Remove all contaminated deployment folders and build artifacts.

**Commands** (run in project root):

```bash
# Delete deployment folders
rm -rf ProjectPublish
rm -rf FinalProductPublish
rm -rf ProjectPublish_BACKUP_*

# Optional: Delete old release packages
rm -rf packages

# Clean bin folder nested artifacts
rm -rf bin/Release/net8.0/win-x64/Backups
rm -rf bin/Release/net8.0/win-x64/FinalProductPublish
rm -rf bin/Release/net8.0/win-x64/ProjectPublish
```

**Verification**:
```bash
# Should return "clean" or no output
ls -la | grep -E "ProjectPublish|FinalProductPublish"
```

---

### Step 3: Run Clean Build

**Goal**: Create a fresh, clean deployment package.

**Option A: Using dotnet publish Directly** (Recommended for clarity)

```bash
# Clean previous build
dotnet clean ShiftManager.csproj -c Release

# Build and publish
dotnet publish ShiftManager.csproj -c Release -r win-x64 --self-contained true -o ProjectPublish
```

**Option B: Using Build-Release.ps1 Script** (Automated pipeline)

```powershell
.\Build-Release.ps1 -Version "2.1.0"
```

**What Gets Created**:
- `ProjectPublish/` - Clean deployment folder (357 files)
- `bin/Release/net8.0/win-x64/` - Intermediate build output

**Expected Output**:
```
  Determining projects to restore...
  Restored ShiftManager.csproj (in X ms).
  ShiftManager -> .../bin/Release/net8.0/win-x64/ShiftManager.dll
  ShiftManager -> .../ProjectPublish/
```

---

### Step 4: Verify Clean Build

**Goal**: Ensure no nested junk folders were created.

**Automated Verification Script** (run in project root):

```bash
#!/bin/bash
# verify-clean-build.sh

echo "=== Verification Report ==="
echo ""

# Test 1: File count
FILE_COUNT=$(find ProjectPublish -type f | wc -l)
echo "Test 1: File Count"
echo "  Expected: 350-370 files"
echo "  Actual:   $FILE_COUNT files"
if [ $FILE_COUNT -gt 350 ] && [ $FILE_COUNT -lt 370 ]; then
  echo "  Status:   ✅ PASS"
else
  echo "  Status:   ❌ FAIL"
fi
echo ""

# Test 2: No Backups folder
echo "Test 2: No Backups Folder"
if [ ! -d "ProjectPublish/Backups" ]; then
  echo "  Status:   ✅ PASS (No Backups folder)"
else
  echo "  Status:   ❌ FAIL (Backups folder exists!)"
fi
echo ""

# Test 3: No FinalProductPublish folder
echo "Test 3: No FinalProductPublish Folder"
if [ ! -d "ProjectPublish/FinalProductPublish" ]; then
  echo "  Status:   ✅ PASS (No FinalProductPublish folder)"
else
  echo "  Status:   ❌ FAIL (FinalProductPublish folder exists!)"
fi
echo ""

# Test 4: No ProjectPublish folder
echo "Test 4: No ProjectPublish Folder"
if [ ! -d "ProjectPublish/ProjectPublish" ]; then
  echo "  Status:   ✅ PASS (No ProjectPublish folder)"
else
  echo "  Status:   ❌ FAIL (ProjectPublish folder exists!)"
fi
echo ""

# Test 5: Critical files exist
echo "Test 5: Critical Files Exist"
CRITICAL_FILES=(
  "ShiftManager.exe"
  "ShiftManager.dll"
  "e_sqlite3.dll"
  "SixLabors.ImageSharp.dll"
  "wwwroot/css/site.css"
  "wwwroot/js/site.js"
  "he-IL/ShiftManager.resources.dll"
)

ALL_PRESENT=true
for file in "${CRITICAL_FILES[@]}"; do
  if [ -f "ProjectPublish/$file" ]; then
    echo "  ✓ $file"
  else
    echo "  ✗ $file - MISSING!"
    ALL_PRESENT=false
  fi
done

if [ "$ALL_PRESENT" = true ]; then
  echo "  Status:   ✅ PASS (All critical files present)"
else
  echo "  Status:   ❌ FAIL (Missing critical files)"
fi
echo ""

# Test 6: DLL count
DLL_COUNT=$(find ProjectPublish -name "*.dll" | wc -l)
echo "Test 6: DLL Count"
echo "  Expected: 330-340 DLLs"
echo "  Actual:   $DLL_COUNT DLLs"
if [ $DLL_COUNT -gt 330 ] && [ $DLL_COUNT -lt 340 ]; then
  echo "  Status:   ✅ PASS"
else
  echo "  Status:   ⚠️  WARNING (DLL count outside expected range)"
fi
echo ""

echo "=== Verification Complete ==="
```

**Save this as** `verify-clean-build.sh` and run:
```bash
chmod +x verify-clean-build.sh
./verify-clean-build.sh
```

**Manual Verification** (quick checks):

```bash
# Check for nested folders (should return nothing)
ls -la ProjectPublish/ | grep -E "Backups|FinalProductPublish|ProjectPublish"

# Count files
find ProjectPublish -type f | wc -l
# Expected: ~357 files

# Count DLLs
find ProjectPublish -name "*.dll" | wc -l
# Expected: ~336 DLLs
```

---

### Step 5: Create FinalProductPublish Golden Copy

**Goal**: Create a "golden master" deployment copy for production.

**Command**:
```bash
cp -r ProjectPublish FinalProductPublish
```

**Verification**:
```bash
# Both should show same file count
echo "ProjectPublish files: $(find ProjectPublish -type f | wc -l)"
echo "FinalProductPublish files: $(find FinalProductPublish -type f | wc -l)"
```

**Expected Output**:
```
ProjectPublish files: 357
FinalProductPublish files: 357
```

---

### Step 6: Create Distributable ZIP Package (Optional)

**Goal**: Create a ZIP file for air-gapped deployment.

**Method 1: Using PowerShell** (Windows)

```powershell
# Create packages folder
if (-not (Test-Path "packages")) {
  New-Item -ItemType Directory -Path "packages"
}

# Create ZIP
$version = "2.1.0"
Compress-Archive -Path "ProjectPublish\*" -DestinationPath "packages\ShiftManager-v$version-win-x64.zip" -Force

# Generate SHA256 checksum
$hash = Get-FileHash "packages\ShiftManager-v$version-win-x64.zip" -Algorithm SHA256
$hash.Hash | Out-File "packages\ShiftManager-v$version-win-x64.zip.sha256"

Write-Host "✅ Package created: packages\ShiftManager-v$version-win-x64.zip"
Write-Host "✅ Checksum: packages\ShiftManager-v$version-win-x64.zip.sha256"
```

**Method 2: Using zip Command** (Linux/Mac)

```bash
version="2.1.0"
mkdir -p packages

# Create ZIP
cd ProjectPublish
zip -r "../packages/ShiftManager-v$version-win-x64.zip" .
cd ..

# Generate SHA256 checksum
sha256sum "packages/ShiftManager-v$version-win-x64.zip" > "packages/ShiftManager-v$version-win-x64.zip.sha256"

echo "✅ Package created: packages/ShiftManager-v$version-win-x64.zip"
echo "✅ Checksum: packages/ShiftManager-v$version-win-x64.zip.sha256"
```

---

### Step 7: Version Control and Tagging

**Goal**: Commit the clean deployment and create a version tag.

**Commands**:

```bash
# Stage FinalProductPublish only (NOT ProjectPublish)
git add FinalProductPublish

# Stage the .csproj file with exclusions
git add ShiftManager.csproj

# Verify what's staged
git status --short

# Commit
git commit -m "Release v2.1.0 - Clean air-gapped deployment package

- Added .csproj exclusions for deployment folders
- Removed nested Backups and duplicate folders
- Complete self-contained .NET 8.0 deployment
- 357 files total (336 DLLs)
- Helper scripts included (VERIFY_FILES.bat, UNBLOCK_FILES.bat, QUICK_FIX.bat)
- Deployment guides included
- All latest features and fixes applied

🤖 Generated with Claude Code (https://claude.com/claude-code)

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"

# Create tag
git tag -a v2.1.0 -m "Release v2.1.0 - Air-gapped deployment package"

# Push (optional)
git push origin main --tags
```

**Important Notes**:
- ✅ **DO** commit `FinalProductPublish/` (golden deployment copy)
- ✅ **DO** commit `ShiftManager.csproj` (with exclusions)
- ❌ **DO NOT** commit `ProjectPublish/` (intermediate build output)
- ❌ **DO NOT** commit `bin/` or `obj/` folders
- ❌ **DO NOT** commit `packages/` folder (ZIP files are large)

---

## Deployment Package Contents

### What Should Be in the Package

**Total Files**: ~357 files
**Total DLLs**: ~336 DLLs
**Total Size**: ~113 MB

**Structure**:
```
ProjectPublish/
├── ShiftManager.exe                    # Main executable
├── ShiftManager.dll                    # Application code
├── appsettings.json                    # Configuration
├── e_sqlite3.dll                       # SQLite native library
├── SixLabors.ImageSharp.dll            # Image processing
├── Microsoft.*.dll                     # .NET runtime & libraries (300+ files)
├── System.*.dll                        # .NET base class libraries
├── wwwroot/                            # Static web assets
│   ├── css/
│   │   ├── site.css
│   │   ├── rtl.css
│   │   └── shift-swap-game.css
│   └── js/
│       ├── site.js
│       ├── shift-swap-game.js
│       └── session-check.js
├── he-IL/                              # Hebrew localization
│   └── ShiftManager.resources.dll
├── clients/                            # API client libraries
│   ├── javascript/
│   └── python/
├── VERIFY_FILES.bat                    # Pre-deployment verification
├── UNBLOCK_FILES.bat                   # Windows unblocking
├── QUICK_FIX.bat                       # Emergency troubleshooting
├── AIR_GAPPED_DEPLOYMENT_GUIDE.txt     # Deployment instructions
└── API_DOCUMENTATION.md                # API usage guide
```

### What Should NOT Be in the Package

❌ **Backups/** folder
❌ **FinalProductPublish/** folder (nested copy)
❌ **ProjectPublish/** folder (nested copy)
❌ **packages/** folder (ZIP files)
❌ **.claude/** folder (AI assistant data)
❌ **bin/** or **obj/** folders
❌ **.git/** folder (if distributing as ZIP)

---

## Troubleshooting

### Problem: Nested folders still appear in build

**Symptom**: `ProjectPublish/Backups/` or `ProjectPublish/FinalProductPublish/` exists after build.

**Cause**: The `.csproj` exclusions were not added or are not working.

**Solution**:
1. Verify `.csproj` contains the `<ItemGroup>` with exclusions (see Step 1)
2. Clean the `bin/` folder completely:
   ```bash
   rm -rf bin/Release/net8.0/win-x64/Backups
   rm -rf bin/Release/net8.0/win-x64/FinalProductPublish
   ```
3. Delete `ProjectPublish/` and rebuild:
   ```bash
   rm -rf ProjectPublish
   dotnet publish ShiftManager.csproj -c Release -r win-x64 --self-contained true -o ProjectPublish
   ```

---

### Problem: Build succeeds but files are missing

**Symptom**: Critical files like `ShiftManager.exe` or `e_sqlite3.dll` are missing.

**Cause**: Antivirus quarantine, incomplete restore, or corrupted NuGet packages.

**Solution**:
1. Check antivirus quarantine
2. Restore NuGet packages:
   ```bash
   dotnet restore --force
   ```
3. Clear NuGet cache and restore:
   ```bash
   dotnet nuget locals all --clear
   dotnet restore
   ```
4. Rebuild from scratch

---

### Problem: File count is wrong (too many or too few files)

**Symptom**: File count is not ~357 files.

**Expected Counts**:
- **Clean build**: 357 files, 336 DLLs
- **With nested junk**: 500-600 files (indicates contamination)

**Solution**:
1. Run verification script from Step 4
2. Check for nested folders:
   ```bash
   ls -la ProjectPublish/ | grep -E "Backups|FinalProductPublish|ProjectPublish"
   ```
3. If nested folders exist, follow Step 2 to clean and rebuild

---

### Problem: ZIP package is too large

**Symptom**: ZIP file is > 120 MB.

**Cause**: Debug symbols (`.pdb` files) included, or nested folders.

**Solution**:
1. Verify no nested folders (see above)
2. Exclude `.pdb` files from ZIP (optional):
   ```powershell
   # PowerShell: Create ZIP without .pdb files
   Get-ChildItem -Path "ProjectPublish" -Recurse |
     Where-Object { $_.Extension -ne ".pdb" } |
     Compress-Archive -DestinationPath "packages\ShiftManager-v2.1.0-win-x64.zip"
   ```

---

## Quick Reference Checklist

Use this checklist when creating a clean deployment:

### Pre-Build Checklist
- [ ] `.csproj` contains exclusions for deployment folders (Step 1)
- [ ] Deleted all `ProjectPublish*` and `FinalProductPublish` folders (Step 2)
- [ ] Cleaned `bin/Release/net8.0/win-x64/` of nested artifacts (Step 2)
- [ ] Committed all code changes to git

### Build Checklist
- [ ] Ran `dotnet clean ShiftManager.csproj -c Release`
- [ ] Ran `dotnet publish ShiftManager.csproj -c Release -r win-x64 --self-contained true -o ProjectPublish`
- [ ] Build succeeded with 0 errors (warnings are OK)

### Verification Checklist
- [ ] File count is ~350-370 files
- [ ] DLL count is ~330-340 files
- [ ] NO `ProjectPublish/Backups/` folder exists
- [ ] NO `ProjectPublish/FinalProductPublish/` folder exists
- [ ] NO `ProjectPublish/ProjectPublish/` folder exists
- [ ] Critical files exist (ShiftManager.exe, ShiftManager.dll, e_sqlite3.dll, etc.)
- [ ] `verify-clean-build.sh` passes all tests

### Post-Build Checklist
- [ ] Created `FinalProductPublish/` as copy of `ProjectPublish/`
- [ ] Both folders have same file count
- [ ] Created ZIP package in `packages/` folder (optional)
- [ ] Generated SHA256 checksum for ZIP (optional)
- [ ] Staged and committed `FinalProductPublish/` and `.csproj` to git
- [ ] Created git tag for version (e.g., `v2.1.0`)
- [ ] Pushed to remote (optional)

---

## Automation Script (Complete Workflow)

**File**: `build-clean-deployment.sh`

```bash
#!/bin/bash
set -e  # Exit on error

VERSION="2.1.0"

echo "========================================="
echo " ShiftManager Clean Deployment Builder"
echo " Version: $VERSION"
echo "========================================="
echo ""

# Step 1: Clean existing artifacts
echo "[1/7] Cleaning existing deployment artifacts..."
rm -rf ProjectPublish
rm -rf FinalProductPublish
rm -rf ProjectPublish_BACKUP_*
rm -rf bin/Release/net8.0/win-x64/Backups
rm -rf bin/Release/net8.0/win-x64/FinalProductPublish
rm -rf bin/Release/net8.0/win-x64/ProjectPublish
echo "✅ Cleanup complete"
echo ""

# Step 2: Clean build
echo "[2/7] Running dotnet clean..."
dotnet clean ShiftManager.csproj -c Release
echo "✅ Clean complete"
echo ""

# Step 3: Build and publish
echo "[3/7] Building and publishing..."
dotnet publish ShiftManager.csproj -c Release -r win-x64 --self-contained true -o ProjectPublish
echo "✅ Publish complete"
echo ""

# Step 4: Verify build
echo "[4/7] Verifying clean build..."
FILE_COUNT=$(find ProjectPublish -type f | wc -l)
DLL_COUNT=$(find ProjectPublish -name "*.dll" | wc -l)

echo "  Total files: $FILE_COUNT"
echo "  DLL files:   $DLL_COUNT"

if [ -d "ProjectPublish/Backups" ]; then
  echo "  ❌ ERROR: Backups folder exists!"
  exit 1
fi

if [ -d "ProjectPublish/FinalProductPublish" ]; then
  echo "  ❌ ERROR: FinalProductPublish folder exists!"
  exit 1
fi

echo "✅ Verification passed"
echo ""

# Step 5: Create golden copy
echo "[5/7] Creating FinalProductPublish golden copy..."
cp -r ProjectPublish FinalProductPublish
echo "✅ Golden copy created"
echo ""

# Step 6: Create ZIP package
echo "[6/7] Creating ZIP package..."
mkdir -p packages
cd ProjectPublish
zip -r "../packages/ShiftManager-v$VERSION-win-x64.zip" .
cd ..
sha256sum "packages/ShiftManager-v$VERSION-win-x64.zip" > "packages/ShiftManager-v$VERSION-win-x64.zip.sha256"
echo "✅ Package created: packages/ShiftManager-v$VERSION-win-x64.zip"
echo ""

# Step 7: Summary
echo "[7/7] Deployment package ready!"
echo ""
echo "========================================="
echo " SUMMARY"
echo "========================================="
echo "  Version:               v$VERSION"
echo "  Total files:           $FILE_COUNT"
echo "  DLL files:             $DLL_COUNT"
echo "  Package:               packages/ShiftManager-v$VERSION-win-x64.zip"
echo "  Checksum:              packages/ShiftManager-v$VERSION-win-x64.zip.sha256"
echo ""
echo "Next steps:"
echo "  1. Run: ./verify-clean-build.sh"
echo "  2. Commit: git add FinalProductPublish ShiftManager.csproj"
echo "  3. Commit: git commit -m 'Release v$VERSION'"
echo "  4. Tag:    git tag -a v$VERSION -m 'Release v$VERSION'"
echo "========================================="
```

**Usage**:
```bash
chmod +x build-clean-deployment.sh
./build-clean-deployment.sh
```

---

## Best Practices

### DO:
- ✅ Add deployment folder exclusions to `.csproj`
- ✅ Clean artifacts before each build
- ✅ Verify builds using automated scripts
- ✅ Use version tags in git
- ✅ Generate SHA256 checksums for packages
- ✅ Document any deviations from this guide

### DON'T:
- ❌ Manually copy files to create deployments (use `dotnet publish`)
- ❌ Commit `ProjectPublish/` to git (only commit `FinalProductPublish/`)
- ❌ Skip verification steps (always verify clean builds)
- ❌ Build without cleaning first
- ❌ Include debug symbols (`.pdb` files) in production packages

---

## Support and Maintenance

### When to Use This Guide
- Creating new release versions
- Setting up CI/CD pipelines
- Training new team members on deployment
- Troubleshooting contaminated builds
- Establishing deployment best practices

### Updating This Guide
When the deployment process changes, update:
1. Version number at top of document
2. File count expectations (if package size changes)
3. `.csproj` example (if new exclusions needed)
4. Automation scripts (if workflow changes)

---

## Contact and Feedback

**Document Version**: 1.0
**Last Updated**: December 16, 2025
**Maintained By**: Development Team

For questions or issues with this guide:
1. Check the Troubleshooting section
2. Review git history for changes: `git log --all -- FinalProductPublish/`
3. Consult the Build-Release.ps1 script for automation details

---

**End of Guide**
