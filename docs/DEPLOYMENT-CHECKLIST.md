# ShiftManager Deployment Checklist

> **Environment**: Air-gapped (no external CDNs or internet access)
> **Database**: SQLite
> **Version**: Updated for UI Overhaul (Phases 0-2)
> **Last Updated**: 2026-02-01

---

## Table of Contents

1. [Pre-Deployment Checks](#1-pre-deployment-checks)
2. [Static Asset Verification](#2-static-asset-verification)
3. [Localization Resource Verification](#3-localization-resource-verification)
4. [Database Migration Steps](#4-database-migration-steps)
5. [Configuration Settings](#5-configuration-settings)
6. [Post-Deployment Verification](#6-post-deployment-verification)
7. [Rollback Procedures](#7-rollback-procedures)
8. [Troubleshooting Common Issues](#8-troubleshooting-common-issues)

---

## 1. Pre-Deployment Checks

### 1.1 Development Machine Preparation

Before building the deployment package:

- [ ] **Verify clean git status**
  ```powershell
  git status
  # Should show: nothing to commit, working tree clean
  ```

- [ ] **Run pre-build tests** (if not skipping)
  ```powershell
  dotnet test
  ```

- [ ] **Check .NET SDK version**
  ```powershell
  dotnet --version
  # Required: .NET 8.0 or later
  ```

- [ ] **Verify NuGet package restore**
  ```powershell
  dotnet restore
  # Ensure packages.lock.json exists for reproducible builds
  ```

### 1.2 Build the Deployment Package

```powershell
# Run the automated build script
.\Build-Release.ps1 -Version "X.Y.Z"

# Build stages:
# 1. Pre-build checks
# 2. Backup existing ProjectPublish
# 3. dotnet publish (Release, win-x64, self-contained)
# 4. Copy deployment assets
# 5. Generate documentation
# 6. Verify build output
# 7. Application testing (optional)
# 8. Packaging + Report
```

### 1.3 Air-Gapped Transfer Preparation

- [ ] **Locate deployment package**
  ```
  packages\ShiftManager-vX.Y.Z-win-x64.zip
  packages\ShiftManager-vX.Y.Z-win-x64.zip.sha256
  ```

- [ ] **Copy to USB drive**
  - ZIP file
  - SHA256 checksum file
  - This checklist (printed or digital copy)

- [ ] **Safely eject USB drive**

---

## 2. Static Asset Verification

### 2.1 CSS Files (Critical for UI)

All CSS must be bundled locally. Verify these files exist in `wwwroot/css/`:

| File | Purpose | Critical |
|------|---------|----------|
| `site.css` | Main application styles | Yes |
| `tokens.css` | CSS custom properties/design tokens | Yes |
| `components.css` | Reusable UI components | Yes |
| `navigation.css` | Navbar and sidebar styles | Yes |
| `calendar.css` | Calendar view styles | Yes |
| `calendar-skeleton.css` | Loading skeleton states | Yes |
| `widgets.css` | Dashboard widgets | Yes |
| `rtl.css` | Right-to-left language support | Yes |
| `print.css` | Print stylesheet | No |
| `icons.css` | Icon definitions | Yes |
| `language-edit-mode.css` | Translation editing UI | No |
| `shift-swap-game.css` | Gamification feature | No |

**Verification command:**
```powershell
$cssFiles = @(
    "site.css", "tokens.css", "components.css", "navigation.css",
    "calendar.css", "calendar-skeleton.css", "widgets.css", "rtl.css",
    "icons.css"
)
foreach ($file in $cssFiles) {
    $path = "ProjectPublish\wwwroot\css\$file"
    if (Test-Path $path) {
        Write-Host "[OK] $file" -ForegroundColor Green
    } else {
        Write-Host "[MISSING] $file" -ForegroundColor Red
    }
}
```

### 2.2 JavaScript Files (Critical for UI)

All JavaScript must be bundled locally. Verify these files exist in `wwwroot/js/`:

| File | Purpose | Critical |
|------|---------|----------|
| `site.js` | Main application JavaScript | Yes |
| `api-client.js` | API communication layer | Yes |
| `toast-notifications.js` | User notifications | Yes |
| `error-boundary.js` | Error handling | Yes |
| `error-states.js` | Error state UI | Yes |
| `modal-focus.js` | Modal accessibility | Yes |
| `modal-loader.js` | Dynamic modal loading | Yes |
| `form-validation.js` | Client-side validation | Yes |
| `keyboard-nav.js` | Keyboard navigation | Yes |
| `date-format.js` | Date formatting utilities | Yes |
| `session-check.js` | Session timeout handling | Yes |
| `calendar-skeleton.js` | Calendar loading states | Yes |
| `calendar-print.js` | Calendar print functionality | No |
| `calendar-inline-edit.js` | Inline editing in calendar | Yes |
| `calendar-fill-handle.js` | Drag-to-fill functionality | No |
| `calendar-radar.js` | Conflict detection radar | No |
| `reduced-motion.js` | Accessibility: reduced motion | Yes |
| `mobile-nav.js` | Mobile navigation | Yes |
| `lazy-loader.js` | Lazy loading utilities | Yes |
| `cache-management.js` | Client-side caching | Yes |
| `widget-persistence.js` | Widget state persistence | Yes |
| `offline-handler.js` | Offline detection | Yes |
| `telemetry.js` | Client telemetry | No |
| `roster-dock.js` | Roster dock feature | No |
| `myteam.js` | My Team page functionality | Yes |
| `localization-api.js` | Localization API client | Yes |
| `localization-attributes.js` | Translation attribute handling | Yes |
| `language-edit-mode.js` | Translation editing | No |
| `shift-swap-game.js` | Gamification feature | No |

**Verification command:**
```powershell
$jsFiles = @(
    "site.js", "api-client.js", "toast-notifications.js", "error-boundary.js",
    "form-validation.js", "modal-focus.js", "keyboard-nav.js", "session-check.js",
    "calendar-skeleton.js", "reduced-motion.js", "mobile-nav.js", "offline-handler.js",
    "myteam.js", "localization-api.js"
)
foreach ($file in $jsFiles) {
    $path = "ProjectPublish\wwwroot\js\$file"
    if (Test-Path $path) {
        Write-Host "[OK] $file" -ForegroundColor Green
    } else {
        Write-Host "[MISSING] $file" -ForegroundColor Red
    }
}
```

### 2.3 Third-Party Libraries (Bundled Locally)

Verify bundled libraries in `wwwroot/lib/`:

| Library | Location | Purpose |
|---------|----------|---------|
| Lucide Icons | `lib/lucide/lucide.min.js` | Icon library |

**IMPORTANT**: No external CDN references allowed. All libraries must be bundled.

### 2.4 Static Asset Integrity

Run the built-in verification:
```powershell
# In ProjectPublish folder
.\VERIFY_FILES.bat
```

Expected output:
- All critical DLLs found
- Total DLL count: 330-340 files
- No missing files

---

## 3. Localization Resource Verification

### 3.1 Resource Files

Verify localization resources in deployment:

| File | Purpose |
|------|---------|
| `Resources/SharedResources.resx` | English (default) strings |
| `Resources/SharedResources.he-IL.resx` | Hebrew translations |
| `he-IL/ShiftManager.resources.dll` | Compiled Hebrew resources |

**Verification:**
```powershell
# Check resource DLL
if (Test-Path "ProjectPublish\he-IL\ShiftManager.resources.dll") {
    Write-Host "[OK] Hebrew resources compiled" -ForegroundColor Green
} else {
    Write-Host "[MISSING] Hebrew resources DLL" -ForegroundColor Red
}
```

### 3.2 UI String Categories

The following localization categories must have complete translations:

- [ ] **Navigation** - Menu items, breadcrumbs
- [ ] **Forms** - Labels, placeholders, validation messages
- [ ] **Buttons** - Action buttons, submit/cancel
- [ ] **Tables** - Column headers, empty states
- [ ] **Modals** - Titles, confirmation messages
- [ ] **Notifications** - Success, error, warning messages
- [ ] **Calendar** - Day names, month names, shift labels
- [ ] **Dashboard** - Widget titles, statistics labels
- [ ] **Shift Types** - Morning, Night, Afternoon, Evening, Mid, Offline

### 3.3 RTL (Right-to-Left) Support

For Hebrew language:

- [ ] `wwwroot/css/rtl.css` is present
- [ ] `dir="rtl"` attribute applied correctly when Hebrew is selected
- [ ] All UI components render correctly in RTL mode
- [ ] Calendar displays correctly (days flow right-to-left)
- [ ] Forms align correctly
- [ ] Icons do not flip inappropriately

---

## 4. Database Migration Steps

### 4.1 Pre-Migration Checklist

- [ ] **Stop the application** (if running)
- [ ] **Backup the database**
  ```powershell
  # SQLite backup
  $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
  Copy-Item "app.db" "app.db.backup.$timestamp"

  # Verify backup
  sqlite3 "app.db.backup.$timestamp" "PRAGMA integrity_check;"
  ```

- [ ] **Check current migration status**
  ```powershell
  sqlite3 app.db "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 5;"
  ```

### 4.2 Apply Migrations

**Option A: Automatic (via application startup)**

The application applies pending migrations on startup automatically.

**Option B: Manual (recommended for production)**

```powershell
# Generate SQL script for review
dotnet ef migrations script <LAST_MIGRATION> <NEW_MIGRATION> --context AppDbContext --output migration.sql

# Review the generated SQL
# Then apply:
sqlite3 app.db < migration.sql

# Verify migration applied
sqlite3 app.db "SELECT * FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 1;"
```

### 4.3 Post-Migration Verification

```powershell
# Check database integrity
sqlite3 app.db "PRAGMA integrity_check;"

# Check foreign key constraints
sqlite3 app.db "PRAGMA foreign_key_check;"

# Verify expected tables exist
sqlite3 app.db ".tables"
```

### 4.4 Known Migration Considerations

**UI Overhaul Related Migrations:**
- `AddFeatureFlags` - Feature flag system
- `AddClientTelemetry` - Client-side telemetry data
- `AddV3OrganizationalHierarchy` - Organization structure updates

**PRAGMA Warning**: Some migrations contain PRAGMA operations that cannot run in transactions. If migration fails mid-execution, restore from backup.

---

## 5. Configuration Settings

### 5.1 appsettings.json Critical Settings

```json
{
  "App": {
    "BaseUrl": "http://localhost:5000"
  },
  "ConnectionStrings": {
    "Default": "Data Source=app.db"
  },
  "AllowedHosts": "localhost",
  "Features": {
    "EnforceCompanyScope": true,
    "EnableDirectorRole": true,
    "AllowPublicSignup": true,
    "EnableApiKeyManagement": true
  },
  "Seeding": {
    "Owner": {
      "Email": "admin@local",
      "Password": "CHANGE_THIS_PASSWORD",
      "DisplayName": "System Administrator"
    }
  }
}
```

### 5.2 Pre-Deployment Configuration Checklist

- [ ] **Set admin password**
  - Edit `appsettings.json`
  - Set `Seeding.Owner.Password` to a secure value
  - **CRITICAL**: Do not leave as empty or default!

- [ ] **Verify port configuration**
  - Default: `http://localhost:5000`
  - Change if port conflicts exist

- [ ] **Review feature flags**
  - `EnforceCompanyScope`: Multi-tenancy enforcement
  - `EnableDirectorRole`: Director role features
  - `AllowPublicSignup`: User self-registration
  - `EnableApiKeyManagement`: API key features

- [ ] **Email configuration** (if applicable)
  ```json
  "Email": {
    "Enabled": false,
    "ApiKey": "",
    "ApiUrl": "",
    "FromAddress": "noreply@example.com"
  }
  ```
  Note: Email typically disabled in air-gapped environments.

- [ ] **Griffin/ADFS configuration** (if applicable)
  ```json
  "Griffin": {
    "Enabled": false
  }
  ```
  Note: External authentication disabled in air-gapped environments.

### 5.3 UI-Specific Configuration

No additional UI configuration required. All UI assets are bundled and work offline.

---

## 6. Post-Deployment Verification

### 6.1 Application Startup

1. **Transfer to air-gapped machine**
   - Copy ZIP to local folder (e.g., `C:\ShiftManager\`)
   - Right-click ZIP > Properties > Unblock
   - Extract completely

2. **Unblock DLL files (CRITICAL)**
   ```powershell
   cd C:\ShiftManager\ProjectPublish
   .\UNBLOCK_FILES.bat
   ```

3. **Start the application**
   ```powershell
   .\START_HERE.bat
   ```

4. **Verify startup output**
   - "Now listening on: http://localhost:5000"
   - No red error messages
   - Database migrations applied (if any pending)

### 6.2 UI Smoke Tests

After startup, verify in browser:

#### Basic Navigation
- [ ] Login page loads at `http://localhost:5000`
- [ ] Login with admin credentials works
- [ ] Dashboard loads successfully
- [ ] All navigation menu items visible
- [ ] Sidebar expands/collapses
- [ ] Mobile menu works (resize browser to test)

#### UI Components (Phases 0-2)
- [ ] **Toast notifications** - Create a shift, verify success toast appears
- [ ] **Modal dialogs** - Open any modal, verify focus trap works
- [ ] **Form validation** - Submit empty form, verify error messages appear
- [ ] **Loading skeletons** - Refresh calendar page, observe skeleton loading
- [ ] **Error states** - Network errors display gracefully (disconnect temporarily)
- [ ] **Keyboard navigation** - Tab through forms, verify focus indicators
- [ ] **Tables** - Verify pagination controls work
- [ ] **Icons** - Lucide icons render correctly

#### Localization
- [ ] Switch language to Hebrew
- [ ] Verify RTL layout applies
- [ ] All strings display in Hebrew
- [ ] Switch back to English
- [ ] Verify LTR layout restores

#### Calendar Features
- [ ] Calendar month view loads
- [ ] Week view loads
- [ ] Day names display correctly
- [ ] Shift assignments visible
- [ ] Create shift modal opens
- [ ] Inline editing works (if enabled)

#### Dashboard Widgets
- [ ] All widgets load
- [ ] Widget data displays correctly
- [ ] Widget collapse/expand works
- [ ] Widget order persists on refresh

### 6.3 Performance Verification

- [ ] Page load time < 3 seconds
- [ ] Calendar renders < 2 seconds
- [ ] No JavaScript errors in browser console
- [ ] No 404 errors for static assets
- [ ] Memory usage stable (no leaks)

### 6.4 Accessibility Verification

- [ ] Keyboard navigation works throughout
- [ ] Focus indicators visible
- [ ] Screen reader landmarks present (ARIA)
- [ ] Color contrast sufficient
- [ ] Reduced motion preference respected

---

## 7. Rollback Procedures

### 7.1 Quick Rollback (Same Version, Config Issue)

If configuration causes issues:

1. Stop application (Ctrl+C in command window)
2. Restore `appsettings.json` from backup
3. Restart application

### 7.2 Database Rollback

If migration causes issues:

1. **Stop application**
   ```powershell
   taskkill /F /IM ShiftManager.exe
   ```

2. **Restore database from backup**
   ```powershell
   # Find latest backup
   Get-ChildItem app.db.backup.* | Sort-Object LastWriteTime -Descending | Select-Object -First 1

   # Restore (replace timestamp)
   Copy-Item "app.db.backup.YYYYMMDD_HHMMSS" "app.db" -Force
   ```

3. **Verify integrity**
   ```powershell
   sqlite3 app.db "PRAGMA integrity_check;"
   ```

4. **Restart application**

### 7.3 Full Application Rollback

If new version is problematic:

1. **Stop application**
2. **Rename current deployment**
   ```powershell
   Rename-Item "C:\ShiftManager\ProjectPublish" "C:\ShiftManager\ProjectPublish_FAILED"
   ```

3. **Restore previous version**
   - Extract previous version ZIP
   - Or rename backup folder back:
   ```powershell
   Rename-Item "C:\ShiftManager\ProjectPublish_BACKUP_YYYYMMDD" "C:\ShiftManager\ProjectPublish"
   ```

4. **Restore database backup** (if needed)

5. **Restart application**

### 7.4 Rollback Using Build Script

The `Build-Release.ps1` script automatically creates backups:
```
ProjectPublish_BACKUP_YYYYMMDD_HHMMSS
```

To rollback:
```powershell
# List backups
Get-ChildItem -Directory -Filter "ProjectPublish_BACKUP_*" | Sort-Object Name -Descending

# Restore from specific backup
Remove-Item "ProjectPublish" -Recurse -Force
Rename-Item "ProjectPublish_BACKUP_YYYYMMDD_HHMMSS" "ProjectPublish"
```

---

## 8. Troubleshooting Common Issues

### 8.1 DLL Load Errors

**Error:** "Could not load file or assembly 'SixLabors.ImageSharp'"

**Cause:** Windows Zone.Identifier blocking DLLs

**Solution:**
```powershell
# Run the unblock script
.\UNBLOCK_FILES.bat

# Or manually in PowerShell
Get-ChildItem -Recurse | Unblock-File
```

### 8.2 Port Already in Use

**Error:** "Port 5000 is already in use"

**Solution:**
```powershell
# Find what's using the port
netstat -ano | findstr ":5000"

# Kill the process (replace PID)
taskkill /PID <PID> /F

# Or change port in appsettings.json
"App": { "BaseUrl": "http://localhost:5001" }
```

### 8.3 Database Locked

**Error:** "Database is locked"

**Cause:** Multiple processes accessing database, or previous crash

**Solution:**
```powershell
# Find and kill all ShiftManager processes
taskkill /F /IM ShiftManager.exe

# Check for locks
Get-Process | Where-Object { $_.Modules.FileName -like "*app.db*" }

# Wait and retry
Start-Sleep -Seconds 5
.\START_HERE.bat
```

### 8.4 Static Assets 404

**Error:** CSS/JS files return 404

**Cause:** Files missing or path incorrect

**Solution:**
```powershell
# Verify wwwroot structure
Get-ChildItem "ProjectPublish\wwwroot" -Recurse | Select-Object FullName

# Ensure files exist
Test-Path "ProjectPublish\wwwroot\css\site.css"
Test-Path "ProjectPublish\wwwroot\js\site.js"
```

### 8.5 JavaScript Errors

**Error:** Console shows JavaScript errors

**Common Causes:**
1. Missing `credentials: 'same-origin'` in fetch calls
2. CSRF token not included in AJAX requests
3. API endpoint not whitelisted in middleware

**Debug Steps:**
1. Open browser Developer Tools (F12)
2. Check Console tab for errors
3. Check Network tab for failed requests
4. Verify 401 errors indicate auth issues

### 8.6 Localization Not Working

**Error:** Strings show keys instead of translations

**Solution:**
```powershell
# Verify resource DLL exists
Test-Path "ProjectPublish\he-IL\ShiftManager.resources.dll"

# Check resource files in source
Test-Path "Resources\SharedResources.he-IL.resx"
```

### 8.7 RTL Layout Issues

**Error:** Hebrew text displays incorrectly

**Solution:**
1. Verify `rtl.css` is loaded
2. Check `dir="rtl"` on `<html>` tag
3. Clear browser cache
4. Test in different browser

### 8.8 Calendar Not Loading

**Error:** Calendar shows empty or error

**Solution:**
1. Check browser console for errors
2. Verify `calendar.css` and `calendar-skeleton.js` exist
3. Check API responses in Network tab
4. Verify user has permission to view calendar

### 8.9 Session Timeout Issues

**Error:** User logged out unexpectedly

**Solution:**
1. Check `session-check.js` is loaded
2. Verify cookie settings in browser
3. Check server session timeout configuration

### 8.10 Build Verification Failed

**Error:** `Verify-Build.ps1` reports issues

**Common Issues:**
| Issue | Expected | Solution |
|-------|----------|----------|
| File count wrong | ~400 files | Rebuild from clean |
| DLL count wrong | ~335 DLLs | Check NuGet restore |
| Package size wrong | ~110 MB | Check publish options |
| Critical file missing | See list | Rebuild |

---

## Quick Reference

### Essential Commands

```powershell
# Build deployment
.\Build-Release.ps1 -Version "X.Y.Z"

# Verify files
.\VERIFY_FILES.bat

# Unblock DLLs
.\UNBLOCK_FILES.bat

# Start application
.\START_HERE.bat

# Stop application
taskkill /F /IM ShiftManager.exe

# Check port
netstat -ano | findstr ":5000"

# Database backup
Copy-Item "app.db" "app.db.backup.$(Get-Date -Format 'yyyyMMdd_HHmmss')"

# Database integrity
sqlite3 app.db "PRAGMA integrity_check;"
```

### Critical Files Checklist

```
ProjectPublish/
  ShiftManager.exe          [REQUIRED]
  ShiftManager.dll          [REQUIRED]
  e_sqlite3.dll             [REQUIRED]
  appsettings.json          [REQUIRED]
  wwwroot/
    css/
      site.css              [REQUIRED]
      tokens.css            [REQUIRED]
      components.css        [REQUIRED]
      rtl.css               [REQUIRED]
    js/
      site.js               [REQUIRED]
      api-client.js         [REQUIRED]
    lib/
      lucide/
        lucide.min.js       [REQUIRED]
  he-IL/
    ShiftManager.resources.dll [REQUIRED for Hebrew]
  START_HERE.bat            [REQUIRED]
  UNBLOCK_FILES.bat         [REQUIRED]
  VERIFY_FILES.bat          [REQUIRED]
```

### Support Information

For deployment issues:
1. Run `VERIFY_FILES.bat` and save output
2. Check application logs
3. Review this checklist
4. Consult `AIR_GAPPED_DEPLOYMENT_GUIDE.txt` for detailed procedures

---

**Document Version**: 1.0
**Applies To**: ShiftManager with UI Overhaul (Phases 0-2)
**Environment**: Air-gapped Windows deployment with SQLite
