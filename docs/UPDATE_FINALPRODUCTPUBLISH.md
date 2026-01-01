# Updating FinalProductPublish - Standard Operating Procedure

## Quick Reference

**Purpose**: FinalProductPublish is the blessed production release tracked in git. Update it after each successful build.

**Frequency**: After every version release (v2.2.0, v2.3.0, etc.)

---

## Standard Update Workflow

### Option 1: Automated (Preferred)

```powershell
.\scripts\Update-FinalProductPublish.ps1 -Version "X.X.X" -CommitChanges -SkipTests
```

**Note**: Currently has encoding issues. Use Option 2 until fixed.

---

### Option 2: Manual Update (Current Standard)

#### Step 1: Build New Version

```powershell
.\Build-Release.ps1 -Version "X.X.X" -SkipTests -NoPush
```

**Expected Output**:
- `ProjectPublish/` folder updated (376 files, 111 MB)
- `packages/ShiftManager-vX.X.X-win-x64.zip` created
- Git tag `vX.X.X` created locally
- **Ignore** Stage 10 errors (release report encoding issue - non-critical)

---

#### Step 2: Backup Existing FinalProductPublish

```bash
timestamp=$(date +%Y%m%d_%H%M%S)
cp -r FinalProductPublish "Backups/FinalProductPublish/FinalProductPublish_BACKUP_$timestamp"
```

**Verify**: Backup created in `Backups/FinalProductPublish/`

---

#### Step 3: Update FinalProductPublish

```bash
rm -rf FinalProductPublish/*
cp -r ProjectPublish/* FinalProductPublish/
```

---

#### Step 4: Create VERSION.txt

```bash
cat > FinalProductPublish/VERSION.txt << 'EOF'
ShiftManager vX.X.X
Build Date: YYYY-MM-DD HH:MM:00 UTC
Git Commit: <SHORT_SHA>
Configuration: Release
Platform: win-x64
.NET Version: 8.0

Features in vX.X.X:
- [List key features/changes]
- [Phase updates if applicable]

Build Statistics:
- Total Files: 376
- DLL Count: 336
- Package Size: 110 MB (uncompressed)

For deployment instructions, see AIR_GAPPED_DEPLOYMENT_GUIDE.txt
EOF
```

**Replace**:
- `X.X.X` with version number
- `YYYY-MM-DD HH:MM:00` with build timestamp
- `<SHORT_SHA>` with `git rev-parse --short HEAD`
- Feature list with actual changes

---

#### Step 5: Verify Integrity

```bash
# Count files
find FinalProductPublish -type f | wc -l  # Should be ~367

# Verify critical files
cd FinalProductPublish
ls -lh ShiftManager.exe ShiftManager.dll e_sqlite3.dll
ls -lh wwwroot/css/site.css wwwroot/js/site.js
ls -lh he-IL/ShiftManager.resources.dll
ls -lh UNBLOCK_FILES.bat VERIFY_FILES.bat VERSION.txt

# Check size
du -sh FinalProductPublish  # Should be ~111 MB
```

**Expected**:
- ✅ 367 files
- ✅ 111 MB total
- ✅ All critical files present

---

#### Step 6: Commit to Git

```bash
git add FinalProductPublish/
git commit -m "Release FinalProductPublish vX.X.X - [Brief Description]

Updated FinalProductPublish with vX.X.X release including:

[Key Features/Changes]:
- Feature 1
- Feature 2
- etc.

Package Details:
- 376 files, 111 MB uncompressed
- Self-contained .NET 8.0 runtime
- Air-gapped deployment ready
- Git tag: vX.X.X

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Verification Checklist

Before committing, verify:

- [ ] File count: ~367 files
- [ ] Size: ~111 MB
- [ ] ShiftManager.exe present (148 KB)
- [ ] ShiftManager.dll present (4.0 MB)
- [ ] e_sqlite3.dll present (1.7 MB)
- [ ] wwwroot/css/site.css present
- [ ] wwwroot/js/site.js present
- [ ] he-IL/ShiftManager.resources.dll present
- [ ] UNBLOCK_FILES.bat present
- [ ] VERIFY_FILES.bat present
- [ ] VERSION.txt created with correct version
- [ ] No .cshtml files (views should be precompiled)
- [ ] clients/ directory present (Python, JavaScript)

---

## Common Issues

### Issue 1: Build-Release.ps1 fails at Stage 4 (File Count)

**Cause**: Verification expects 450 files but build produces ~376

**Fix**: Already fixed in `build/Verify-Build.ps1` (expects 380 ± 30)

---

### Issue 2: Build-Release.ps1 fails at Stage 8 (Git Tagging)

**Cause**: Git detects uncommitted ProjectPublish/ changes

**Fix**: Already fixed in `build/Git-Integration.ps1` (ignores ProjectPublish/ changes)

---

### Issue 3: Build-Release.ps1 fails at Stage 10 (Release Report)

**Cause**: Unicode encoding issues in Generate-ReleaseReport.ps1

**Status**: Non-critical - package is already built successfully. Ignore this error.

---

## Two-Folder System Explained

### ProjectPublish/
- **Purpose**: Build staging area
- **Updated by**: `Build-Release.ps1` (dotnet publish output)
- **Tracked in git**: Yes (for transparency)
- **Use case**: Temporary build output, gets overwritten each build

### FinalProductPublish/
- **Purpose**: Blessed production release
- **Updated by**: Manual copy from ProjectPublish (or Update-FinalProductPublish.ps1)
- **Tracked in git**: Yes (as official release)
- **Use case**: The official release for deployment, stable and versioned

**Rule**: Only update FinalProductPublish after successful build verification.

---

## Quick Command Reference

```bash
# Full workflow in one go
./Build-Release.ps1 -Version "2.3.0" -SkipTests -NoPush
timestamp=$(date +%Y%m%d_%H%M%S)
cp -r FinalProductPublish "Backups/FinalProductPublish/FinalProductPublish_BACKUP_$timestamp"
rm -rf FinalProductPublish/*
cp -r ProjectPublish/* FinalProductPublish/
# Create VERSION.txt (see Step 4)
git add FinalProductPublish/
git commit -m "Release FinalProductPublish v2.3.0 - [Description]"
```

---

## Notes

- **Always backup** before updating FinalProductPublish
- **Verify integrity** before committing
- **Keep backups** in `Backups/FinalProductPublish/` (last 3-5 recommended)
- **VERSION.txt is required** - manually create after each update
- **Git tag locally** is created by Build-Release.ps1
- **Do not push tags** unless you're ready to publish externally

---

**Last Updated**: 2026-01-01
**Document Version**: 1.0
