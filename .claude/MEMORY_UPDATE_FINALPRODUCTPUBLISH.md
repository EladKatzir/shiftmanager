# /memory: Updating FinalProductPublish

## Quick Reference: Correct Way to Update FinalProductPublish

**FinalProductPublish** = Blessed production release (tracked in git)
**ProjectPublish** = Build staging area (temporary, overwritten each build)

---

## Standard Workflow (6 Steps)

### 1. Build New Version
```powershell
.\Build-Release.ps1 -Version "X.X.X" -SkipTests -NoPush
```
**Output**: ProjectPublish/ updated, ZIP created, git tag vX.X.X created
**Note**: Ignore Stage 10 errors (non-critical encoding issue)

### 2. Backup FinalProductPublish
```bash
timestamp=$(date +%Y%m%d_%H%M%S)
cp -r FinalProductPublish "Backups/FinalProductPublish/FinalProductPublish_BACKUP_$timestamp"
```

### 3. Copy ProjectPublish → FinalProductPublish
```bash
rm -rf FinalProductPublish/*
cp -r ProjectPublish/* FinalProductPublish/
```

### 4. Create VERSION.txt
```bash
cat > FinalProductPublish/VERSION.txt << 'EOF'
ShiftManager vX.X.X
Build Date: YYYY-MM-DD HH:MM:00 UTC
Git Commit: <git rev-parse --short HEAD>
Configuration: Release
Platform: win-x64

Features in vX.X.X:
- [List changes here]

For deployment instructions, see AIR_GAPPED_DEPLOYMENT_GUIDE.txt
EOF
```

### 5. Verify Integrity
```bash
find FinalProductPublish -type f | wc -l  # Should be ~367
du -sh FinalProductPublish                 # Should be ~111 MB
ls FinalProductPublish/ShiftManager.exe ShiftManager.dll e_sqlite3.dll VERSION.txt
```

### 6. Commit to Git
```bash
git add FinalProductPublish/
git commit -m "Release FinalProductPublish vX.X.X - [Description]"
```

---

## Quick Verification Checklist
- [ ] 367 files, 111 MB total
- [ ] ShiftManager.exe (148 KB), .dll (4.0 MB), e_sqlite3.dll (1.7 MB) present
- [ ] wwwroot/ assets, he-IL/ localization, deployment .bat scripts present
- [ ] VERSION.txt created with correct version
- [ ] No .cshtml files (precompiled)

---

## Common Issues (Already Fixed)
- ✅ **Stage 4 failure**: File count verification (fixed in build/Verify-Build.ps1)
- ✅ **Stage 8 failure**: Git tagging blocked by ProjectPublish changes (fixed in build/Git-Integration.ps1)
- ⚠️ **Stage 10 error**: Release report encoding (non-critical, ignore - package already built)

---

## One-Liner Command Sequence
```bash
./Build-Release.ps1 -Version "X.X.X" -SkipTests -NoPush && \
timestamp=$(date +%Y%m%d_%H%M%S) && \
cp -r FinalProductPublish "Backups/FinalProductPublish/FinalProductPublish_BACKUP_$timestamp" && \
rm -rf FinalProductPublish/* && \
cp -r ProjectPublish/* FinalProductPublish/ && \
echo "Now create VERSION.txt and commit to git"
```

---

**Rule**: Only update FinalProductPublish after successful build verification.
**Full Documentation**: See `docs/UPDATE_FINALPRODUCTPUBLISH.md`
