# PowerShell Script Analysis & Root Cause Report
**Date:** 2026-01-07
**Script:** `.\scripts\Update-FinalProductPublish.ps1 -Version "2.5.0" -SkipTests -CommitChanges`
**Status:** ❌ FAILING - Root cause identified

---

## Executive Summary

The script fails during the pre-build check phase due to a **dirty Git working tree** combined with **missing `-AllowDirtyGit` flag propagation** through the build chain. Additionally, **backup folder patterns are missing from `.gitignore`**, causing auto-commit to stage unwanted files.

### Current Git State (Causing Failure)
```
M  packages.lock.json                        ← Modified file (needs commit/stash)
?? ProjectPublish_BACKUP_20260107_171255/   ← Untracked backup folder (leftover)
```

---

## Root Cause Analysis

### 1. Pre-Build Check Failure (PRIMARY ISSUE)

**File:** `build/Test-PreBuild.ps1` (Lines 110-124)

**Logic:**
```powershell
if ($status) {
    if ($AllowDirtyGit) {
        Write-Log -Level WARN -Message "Git working tree has uncommitted changes (allowed)."
    } else {
        throw "Please commit or stash changes (or rerun with -AllowDirtyGit)."  ← FAILS HERE
    }
}
```

**Problem:** Test-PreBuild.ps1 **requires clean Git tree** unless `-AllowDirtyGit` flag is passed.

---

### 2. Missing Flag Propagation (CRITICAL DESIGN FLAW)

**Build-Release.ps1** (Lines 381-385):
```powershell
Invoke-StepScript -Path $pre -Params @{
    Version    = $Version
    SkipTests  = [bool]$SkipTests
    # AllowDirtyGit not enabled by default  ← NEVER PASSES FLAG
}
```

**Update-FinalProductPublish.ps1** (Lines 303-304):
```powershell
$buildParams = @{ Version = $Version }
if ($SkipTests) { $buildParams.SkipTests = $true }
# ← NEVER ADDS AllowDirtyGit PARAMETER
```

**Result:** Even though Update-FinalProductPublish.ps1 has `-CommitChanges` flag logic (lines 276-277), Build-Release.ps1 is called **BEFORE** the auto-commit can clean the tree, and it doesn't pass `-AllowDirtyGit` forward.

---

### 3. Backup Folders Not in `.gitignore` (SECONDARY ISSUE)

**Two Backup Systems Exist:**

| Script | Backup Location | Pattern | In .gitignore? |
|--------|----------------|---------|----------------|
| Build-Release.ps1 | `<repo>/ProjectPublish_BACKUP_<timestamp>/` | `ProjectPublish_BACKUP_*` | ❌ NO |
| scripts/Backup-FinalProductPublish.ps1 | `<repo>/Backups/FinalProductPublish/FinalProductPublish_BACKUP_<timestamp>/` | `Backups/` | ❌ NO |

**Impact:** When `Commit-AllChanges` runs `git add -A` (line 243 of Update-FinalProductPublish.ps1), it stages backup folders, which:
- Bloats the repository
- Violates best practices (backups should not be versioned)
- May cause commit to include hundreds of files unnecessarily

---

## Execution Flow (Why It Fails)

```
1. User runs: .\scripts\Update-FinalProductPublish.ps1 -Version "2.5.0" -SkipTests -CommitChanges

2. Update-FinalProductPublish.ps1 (Line 271):
   ├─ Checks Git status → DIRTY (packages.lock.json + backup folder)
   ├─ Detects -CommitChanges flag (Line 276)
   └─ SHOULD auto-commit here (Line 277) BUT...

3. Problem: Commit-AllChanges (Line 243):
   ├─ Runs: git add -A
   ├─ Stages: packages.lock.json + ProjectPublish_BACKUP_20260107_171255/ (ENTIRE FOLDER)
   └─ git commit -m "Auto-commit before FinalProductPublish update to v2.5.0"

4. Update-FinalProductPublish.ps1 (Line 303-309):
   ├─ Calls Build-Release.ps1 with: @{ Version = "2.5.0"; SkipTests = $true }
   └─ Does NOT pass AllowDirtyGit flag

5. Build-Release.ps1 (Line 381):
   ├─ Calls Test-PreBuild.ps1 with: @{ Version = "2.5.0"; SkipTests = $true }
   └─ Does NOT pass AllowDirtyGit flag

6. Test-PreBuild.ps1 (Line 105):
   ├─ Runs: git status --porcelain
   ├─ Finds dirty tree (if auto-commit failed or new changes appeared)
   └─ Throws error: "Please commit or stash changes (or rerun with -AllowDirtyGit)"

7. SCRIPT FAILS ❌
```

---

## Why Auto-Commit May Not Help

Even if the auto-commit in Update-FinalProductPublish.ps1 succeeds, the tree could still be dirty because:

1. **Backup folder added to Git:** The untracked `ProjectPublish_BACKUP_20260107_171255/` folder gets staged, which is incorrect.
2. **Post-commit changes:** If any file (like `packages.lock.json`) is modified AFTER the commit but BEFORE Build-Release.ps1 runs, the tree becomes dirty again.
3. **Build-generated changes:** dotnet build/restore might modify `packages.lock.json` during the build, re-dirtying the tree.

---

## Specific Fixes Required

### Fix #1: Add Backup Patterns to `.gitignore` (CRITICAL)

**File:** `C:\Users\katzi\Downloads\ShiftManager\.gitignore`

**Add these lines after line 105 (before "# Publish output"):**
```gitignore
# Build script backups (should not be versioned)
ProjectPublish_BACKUP_*/
Backups/
```

**Why:** Prevents `git add -A` from staging backup folders, keeping them out of the repository.

---

### Fix #2: Clean Up Existing Untracked Backup Folder (IMMEDIATE)

**Command:**
```powershell
Remove-Item "C:\Users\katzi\Downloads\ShiftManager\ProjectPublish_BACKUP_20260107_171255" -Recurse -Force
```

**Why:** This leftover backup folder is causing the dirty tree. It should be in `Backups/` folder, not repo root.

---

### Fix #3: Commit `packages.lock.json` (IMMEDIATE)

**Command:**
```bash
cd C:\Users\katzi\Downloads\ShiftManager
git add packages.lock.json
git commit -m "Update packages.lock.json before FinalProductPublish v2.5.0 build"
```

**Why:** This modified file needs to be committed to achieve clean tree.

---

### Fix #4: Pass `-AllowDirtyGit` Through Build Chain (OPTIONAL BUT RECOMMENDED)

**Option A: Modify Build-Release.ps1** (Add parameter support)

Add this parameter to Build-Release.ps1 (after line 36):
```powershell
[Parameter()]
[switch]$AllowDirtyGit
```

Then modify the Test-PreBuild call (lines 381-385):
```powershell
Invoke-StepScript -Path $pre -Params @{
    Version       = $Version
    SkipTests     = [bool]$SkipTests
    AllowDirtyGit = [bool]$AllowDirtyGit  # ← ADD THIS
} -WorkingDirectory $ScriptRoot -Optional
```

**Option B: Use `-SkipGit` flag in Build-Release.ps1**

Check if Build-Release.ps1 has a `-SkipGit` flag that bypasses pre-build checks entirely.

**Option C: Don't fix this, just ensure clean tree**

If you always run from clean tree, this isn't needed. But it adds robustness for future runs.

---

### Fix #5: Enhanced Error Handling in Update-FinalProductPublish.ps1 (OPTIONAL)

Add verification after auto-commit (after line 277):
```powershell
if ($CommitChanges) {
    Commit-AllChanges -Message ("Auto-commit before FinalProductPublish update to v{0}" -f $Version)

    # ✅ VERIFY tree is now clean
    $statusAfterCommit = & git status --porcelain
    if ($statusAfterCommit) {
        Write-Log -Level ERROR -Message "Git tree still dirty after auto-commit!"
        $statusAfterCommit | ForEach-Object { Write-Log -Level ERROR -Message ("  {0}" -f $_) }
        throw "Unable to achieve clean Git tree. Build-Release.ps1 will likely fail."
    }
    Write-Log -Level OK -Message "Git tree now clean after auto-commit."
}
```

---

## Recommended Action Plan

### Immediate Actions (Run These Now):

```powershell
# 1. Navigate to repo
cd C:\Users\katzi\Downloads\ShiftManager

# 2. Delete leftover backup folder
Remove-Item "ProjectPublish_BACKUP_20260107_171255" -Recurse -Force

# 3. Update .gitignore
Add-Content .gitignore "`n# Build script backups (should not be versioned)`nProjectPublish_BACKUP_*/`nBackups/"

# 4. Commit changes
git add packages.lock.json .gitignore
git commit -m "Clean up Git tree and add backup patterns to .gitignore before v2.5.0 build"

# 5. Verify clean tree
git status
# Expected output: "nothing to commit, working tree clean"

# 6. Run the script again
.\scripts\Update-FinalProductPublish.ps1 -Version "2.5.0" -SkipTests -CommitChanges
```

### Long-term Improvements (Optional):

1. Add `-AllowDirtyGit` parameter support to Build-Release.ps1
2. Consolidate backup systems (use only one backup strategy)
3. Add pre-execution check in Update-FinalProductPublish.ps1 that verifies tree is clean BEFORE calling Build-Release.ps1
4. Add `Backups/` folder to `.gitignore` globally

---

## Testing Strategy

After applying fixes, test these scenarios:

1. ✅ **Clean tree build:** Run from clean tree with `-CommitChanges` flag
2. ✅ **Dirty tree with auto-commit:** Modify a file, run with `-CommitChanges`, verify auto-commit works
3. ✅ **Backup folder exclusion:** Verify backup folders don't appear in `git status`
4. ✅ **Full workflow:** Build → Verify → Update FinalProductPublish → Verify

---

## Files Analyzed

| File | Lines | Purpose |
|------|-------|---------|
| `scripts/Update-FinalProductPublish.ps1` | 487 | Main orchestration script |
| `Build-Release.ps1` | 800+ | Builds ProjectPublish (dotnet publish) |
| `build/Test-PreBuild.ps1` | 208 | Pre-build checks (Git, dotnet, tests) |
| `scripts/Backup-FinalProductPublish.ps1` | 200 | Dedicated backup script |
| `scripts/Restore-FinalProductPublish.ps1` | 245 | Rollback mechanism |
| `.gitignore` | 131 | Git exclusion patterns |

---

## Conclusion

**Root Cause:** Dirty Git working tree (modified `packages.lock.json` + untracked backup folder) combined with missing `-AllowDirtyGit` flag propagation through the build chain.

**Immediate Fix:** Clean up the Git tree manually (delete backup folder, commit changes, update .gitignore).

**Long-term Fix:** Add `-AllowDirtyGit` parameter support to Build-Release.ps1, or ensure scripts always run from clean tree.

**Success Criteria:** Script executes successfully from a clean Git working tree without requiring manual intervention.

---

**Report Generated:** 2026-01-07
**Author:** Senior Developer Analysis (Claude Code)
