# Deployment Export/Restore — Design Spec

**Date:** 2026-03-29
**Status:** Draft
**Purpose:** Enable safe air-gapped updates by exporting all persistent data to a single folder that survives a full app directory replacement.

## Problem

ShiftManager is deployed air-gapped on Windows/IIS. Updates are performed by stopping IIS, deleting all application files, and replacing them with the new `FinalProductPublish` folder. This destroys:

- Avatar images (`wwwroot\avatars\{companyId}\`)
- Feedback images (`wwwroot\feedback\{companyId}\`)
- DataProtection keys (`AppContext.BaseDirectory\DataProtection-Keys\`)
- Customized `appsettings.Production.json` (email config, Griffin/ADFS, API keys)

The database (`C:\ShiftManager\Data\app.db`) and backups (`C:\ShiftManager\Backups\`) already live outside the app directory and are safe.

## Solution

A two-phase export/restore system:

1. **Export** — Owner page button creates `C:\ShiftManager\DeploymentExport\` containing all persistent data
2. **Auto-Restore** — On startup, the app detects the export folder and restores everything automatically

The update process becomes: **Export → Stop IIS → Delete files → Copy new publish → Start IIS → Done.**

## Architecture

### New Files

| File | Purpose |
|------|---------|
| `Services/DeploymentExportService.cs` | Export + restore logic |
| `Services/IDeploymentExportService.cs` | Interface |

### Modified Files

| File | Change |
|------|--------|
| `Program.cs` | Phase 1 pre-builder restore call (before line 26), Phase 2 post-build restore call (before line 462), service registration |
| `Pages/Owner/Backup.cshtml.cs` | New "Prepare for Update" handler |
| `Pages/Owner/Backup.cshtml` | New export UI section |

---

## Section 1: Export

Triggered by "Prepare for Update" button on `/Owner/Backup` → calls `DeploymentExportService.ExportAsync()`.

### Export Folder Structure

```
C:\ShiftManager\DeploymentExport\
├── app.db                              ← VACUUM INTO (atomic, WAL-free snapshot)
├── avatars\                            ← recursive copy from wwwroot\avatars\
│   └── {companyId}\
│       ├── {userId}.jpg
│       └── {userId}_thumb.jpg
├── feedback\                           ← recursive copy from wwwroot\feedback\
│   └── {companyId}\
│       └── {guid}.jpg
├── DataProtection-Keys\               ← copy from AppContext.BaseDirectory
│   └── key-{guid}.xml
├── appsettings.Production.json         ← copy from content root
└── manifest.json                       ← written LAST (commit signal)
```

### manifest.json

```json
{
  "exportedAt": "2026-03-29T14:30:00Z",
  "appVersion": "1.0.0",
  "lastMigrationId": "20260326225317_AddStoresAndQuickInfoConfig",
  "exportedBy": "admin@d8200.mil",
  "machineName": "SHIFTMGR-PROD",
  "database": {
    "fileName": "app.db",
    "sizeBytes": 2048000,
    "sha256": "abc123..."
  },
  "avatarCount": 47,
  "feedbackImageCount": 3,
  "dataProtectionKeyCount": 2,
  "dataProtectionMachineScoped": true,
  "configIncluded": true
}
```

### Export Behaviors

**Atomicity — four-step sequence:**
1. Write everything to `DeploymentExport.new-{guid}\` (manifest written last as commit signal)
2. If existing `DeploymentExport\` exists: rename it to `DeploymentExport.old-{guid}\`
3. Rename `DeploymentExport.new-{guid}\` → `DeploymentExport\`
4. Delete `DeploymentExport.old-{guid}\`

If the process dies between steps 2 and 3, no `DeploymentExport\` exists — the auto-restore won't trigger, and the orphaned `.old-{guid}` is cleaned up on the next export. The previous good export is never destroyed by a failed new export.

**Concurrency:** `SemaphoreSlim(1,1)` singleton in the service. A second concurrent request receives an error message.

**Path resolution:**
- Avatars/feedback: `IWebHostEnvironment.WebRootPath`
- DataProtection keys: `AppContext.BaseDirectory + "DataProtection-Keys"`
- appsettings.Production.json: `IWebHostEnvironment.ContentRootPath`

**VACUUM INTO:** Uses `Path.GetFullPath()` on the target path (matching `Backup.cshtml.cs:97`). SQLite resolves relative paths from its own working directory (often `C:\Windows\System32` under IIS), not from the app directory.

**Validation:**
- Missing `appsettings.Production.json` is a **hard error** — export is blocked with a clear message
- Missing avatars/feedback folders are OK (empty export sections, counts = 0)
- SHA256 hash is computed on the exported `app.db` and stored in the manifest

**Audit:** Export is logged with the exporting user's identity and a summary of exported data.

**UI:** The export button shows a confirmation dialog noting the bundle contains credentials and should be treated as sensitive material.

### Explicitly Excluded from Export

| Data | Reason |
|------|--------|
| `Archives\` folder | Ephemeral operator-initiated CSV/NDJSON exports, regenerable on demand |
| `Uploads\` folder | Temporary import staging area, cleaned after processing |
| `Backups\` folder | Already external at `C:\ShiftManager\Backups\`, not at risk |
| `app.db` (at external path) | The export creates its own VACUUM INTO copy — the live DB is not touched |
| Log files | Ephemeral, not operational data |

---

## Section 2: Auto-Restore on Startup

The restore is split into two phases to solve the config chicken-and-egg problem: ASP.NET Core's `IConfiguration` is frozen at `builder.Build()`, and DataProtection keys are loaded into the in-memory key ring at the same time.

### Startup Order

```
 1.  ▶ Phase 1: DeploymentExportService.RestoreConfigAndKeys()  ← static, pre-builder
 2.    var builder = WebApplication.CreateBuilder(args);          ← reads RESTORED config + DP keys
 3.    ... service registration, validation ...
 4.    var app = builder.Build();
 5.  ▶ Phase 2: DeploymentExportService.RestoreDataAsync()       ← DB + avatars + feedback
 6.    Pre-migration backup (existing, File.Copy)
 7.    db.Database.MigrateAsync() (existing)
 8.    SeedData.EnsureSeedDataAsync() (existing)
 9.    app.RunAsync() → DatabaseBackupService starts
```

### Phase 1: RestoreConfigAndKeys() — Static, Pre-Builder

Runs as plain C# before `WebApplication.CreateBuilder(args)`. No DI, no logging framework — uses `Console.WriteLine` for diagnostics (these appear in IIS stdout logs and the F-01 crash log file).

**Error handling contract:** Phase 1 wraps ALL logic in a top-level `try/catch(Exception)`. On any failure — JSON parse error, I/O error, permissions error — it writes the error to `Console.Error`, sets `PendingDataRestore = false`, and **returns normally**. Phase 1 must NEVER propagate exceptions. A crash in Phase 1 kills the entire application before the DI logging system is available, which is worse than a skipped restore.

**Logic:**
1. Check if `C:\ShiftManager\DeploymentExport\manifest.json` exists
2. If not → return immediately (normal startup, no-op)
3. Parse `manifest.json` — if unparseable, write error to console and return (never restore from a corrupt export)
4. Copy `appsettings.Production.json` → `AppContext.BaseDirectory` (overwrite: true)
5. Copy `DataProtection-Keys\*.xml` → `AppContext.BaseDirectory\DataProtection-Keys\` (overwrite: true, creating directory if needed)
6. If `manifest.machineName != Environment.MachineName`: write a prominent console warning that DP keys are DPAPI machine-scoped and may not decrypt on a different machine
7. Set static flag: `DeploymentExportService.PendingDataRestore = true`

Phase 1 does NOT rename or delete the export folder — that happens in Phase 2 after the DB is restored.

### Phase 2: RestoreDataAsync() — Post-Build, Pre-Migration

Runs after `builder.Build()` but before the existing pre-migration backup and `MigrateAsync()`. Has access to DI (logging, AppDbContext for migration list).

**Logic:**
1. Check `DeploymentExportService.PendingDataRestore` flag — if false, return (no-op)
2. Read `manifest.json` from `C:\ShiftManager\DeploymentExport\`
3. Validate `app.db` exists and SHA256 matches manifest → if mismatch, log error and **skip entire restore** (all-or-nothing at validation stage)
4. **Migration compatibility check:** Read `manifest.lastMigrationId`, compare against `db.Database.GetMigrations()` (the assembly's compiled migration list — NOT `GetAppliedMigrationsAsync()` which queries the database). This check is assembly-only and must not touch the database, because the export DB has not yet been copied to the target path. If the manifest's migration ID is not in the assembly list (DB is newer than the app — downgrade scenario), log error and skip restore
5. Call `SqliteConnection.ClearAllPools()` as a defensive measure to ensure no pooled connections hold the target DB file. Then copy `app.db` → target path from connection string (resolved via `DatabaseBackupService.ExtractDbPath()` using the now-correct restored config). Use `Path.GetFullPath()`. Create target directory if needed. **No EF Core database-touching operation** (query, `CanConnect()`, `OpenConnection()`, `GetAppliedMigrationsAsync()`) may be called against the target database path before this copy completes
6. Recursive copy `avatars\` → `IWebHostEnvironment.WebRootPath + "\avatars\"` (creating subdirectories, overwrite: true)
7. Recursive copy `feedback\` → `IWebHostEnvironment.WebRootPath + "\feedback\"` (creating subdirectories, overwrite: true)
8. Validate avatar count: compare actual copied files against `manifest.avatarCount`. If mismatch → log warning (not a hard error — partial avatars is better than no avatars)
9. Rename `DeploymentExport\` → `DeploymentExport.restored-{timestamp}`
10. Clean up old `.restored-*` folders: keep 2 most recent (sorted by `DirectoryInfo.LastWriteTime`, not by name — creation time is unreliable on Windows when folders are renamed), delete the rest
11. Also clean up any orphaned `.old-{guid}` and `.new-{guid}` folders from interrupted exports
12. Set static flag: `DeploymentExportService.RestoreJustCompleted = true` (for UI banner)
13. Log summary: "Deployment restore completed. Restored: db (X MB), N avatars, N feedback images, N DP keys, config."

**Error handling:**
- Validation failures (hash mismatch, migration incompatibility, missing manifest) → hard stop, nothing is touched, prominent error log
- Individual file copy failures during avatars/feedback → logged as warnings, do not abort the restore. A missing avatar is cosmetic; a missing database is fatal
- DB copy failure → hard stop (the database is the most critical piece)

### Important: Pre-Migration Backup Interaction

After Phase 2 restores the database, the existing pre-migration backup at Program.cs line 478 runs next. It backs up the **restored** DB — this is correct behavior, providing a rollback point before migrations alter the restored schema.

### Important: No Race with DatabaseBackupService

`DatabaseBackupService.ExecuteAsync()` starts when `app.RunAsync()` is called (step 9 in the startup order), which is after all restore + migration + seed work is complete. The 10-second delay inside the service is additional buffer. There is no race condition.

---

## Section 3: Owner Page UI

### Location

New section on the existing `/Owner/Backup` page, above the existing backup list.

### UI Elements

**"Prepare for Update" card:**
- Header: "Prepare for Update" with a brief explanation
- Status line showing last export info (read from `DeploymentExport\manifest.json` if it exists): "Last export: 2026-03-29 14:30 by admin@d8200.mil — 47 avatars, 2.0 MB database"
- "Export Now" button with confirmation dialog: "This will create a deployment export bundle at C:\ShiftManager\DeploymentExport\. The bundle contains database credentials and should be treated as sensitive material. Proceed?"
- Progress/result: success message with summary, or error message

**Post-restore indicator:**
- If the app just restored from an export (detected via the static flag `DeploymentExportService.RestoreJustCompleted`), show an info banner: "Data restored from deployment export (timestamp). All data is intact." The flag is cleared after the banner is displayed once.

### No Separate Page

The export functionality lives on the existing Backup page — it's a natural extension of the "protect your data" workflow. No new Razor page needed.

---

## Section 4: Update Procedure (Operator Instructions)

The complete update procedure for the air-gapped environment:

### Before the Update

1. Log into ShiftManager as Owner
2. Navigate to **Owner Hub → Backup**
3. Click **"Export Now"** in the "Prepare for Update" section
4. Wait for the success message confirming all data was exported
5. (Optional) Also click "Create Backup" for an additional safety net

### Performing the Update

6. **Stop the IIS App Pool** for ShiftManager (or stop the Windows Service)
7. Delete all files in the application directory (e.g., `C:\inetpub\ShiftManager\`)
8. Copy the contents of `FinalProductPublish` into the application directory
9. **Start the IIS App Pool**

### What Happens Automatically

10. Phase 1 detects `C:\ShiftManager\DeploymentExport\`, restores config + DP keys
11. ASP.NET Core starts with the correct production configuration
12. Phase 2 restores the database, avatars, and feedback images
13. Pre-migration backup is created (safety net)
14. EF Core migrations run (applies any new schema changes)
15. Seed data ensures all grant types, role templates, and feature flags are present
16. The export folder is renamed to `DeploymentExport.restored-{timestamp}`
17. The application is fully operational

### No Second Restart Required

Because config and DP keys are restored before the builder runs (Phase 1), everything takes effect on the first startup. No second IIS recycle needed.

---

## Section 5: Service Design

### IDeploymentExportService

```csharp
public interface IDeploymentExportService
{
    Task<ExportResult> ExportAsync(int userId, string userEmail);
    ExportManifest? GetLastExportInfo();
}
```

### DeploymentExportService

Registered as a **singleton** (owns the `SemaphoreSlim`).

**Static members** (used by Phase 1 pre-builder, no DI):
- `RestoreConfigAndKeys()` — Phase 1 pre-builder restore (top-level try/catch, never throws)
- `PendingDataRestore` — static flag set by Phase 1, read by Phase 2
- `RestoreJustCompleted` — static flag set by Phase 2, read by Backup page UI for the post-restore banner

**Instance members** (used by export + Phase 2, resolved from DI via `app.Services.GetRequiredService<IDeploymentExportService>()`):
- `ExportAsync(userId, userEmail)` — creates the export bundle
- `RestoreDataAsync(IServiceProvider)` — Phase 2 post-build restore
- `GetLastExportInfo()` — reads manifest for UI display

**Constants:**
- `ExportPath = @"C:\ShiftManager\DeploymentExport"`
- `MaxRestoredFoldersRetained = 2`

### ExportManifest

A simple POCO deserialized from `manifest.json`. Used for validation and UI display.

### ExportResult

```csharp
public record ExportResult(
    bool Success,
    string? ErrorMessage,
    ExportManifest? Manifest
);
```

---

## Section 6: Edge Cases and Constraints

### Disk Space

Each `.restored-*` folder contains a full copy of the DB + avatars + feedback. With 2 retained folders, the maximum disk overhead is approximately 3x the export size (1 active export + 2 archives). Operators should monitor `C:\ShiftManager\` free space. The existing `DiskSpaceHealthCheck` monitors overall disk space but not the ShiftManager data root specifically.

### DPAPI Machine Scope

DataProtection keys are encrypted with DPAPI scoped to the local machine (`protectToLocalMachine: true`). The export/restore is designed for same-machine updates (delete files, replace, restart). Cross-machine migration is not a supported scenario. If `manifest.machineName` differs from `Environment.MachineName`, Phase 1 logs a prominent warning.

### Concurrent Users During Export

The database is exported via `VACUUM INTO`, which produces a point-in-time consistent snapshot even while the app is serving requests. Avatar and feedback files are copied after the DB snapshot. A user uploading an avatar between the DB snapshot and the file copy could result in:
- The DB references a file that exists in the export (file copied after DB snapshot) — no issue
- The DB does not reference a file that exists in the export — harmless orphan

This is the same trade-off the existing backup system makes and is acceptable for an air-gapped deployment with controlled update windows.

### Failed Export Recovery

If export fails mid-write (disk full, process crash), the temp folder `DeploymentExport.new-{guid}\` has no `manifest.json` (written last). The auto-restore will not find `DeploymentExport\` (the old one was not renamed yet). The previous good export survives if one existed. The orphaned `.new-{guid}` folder is cleaned up on the next successful export.

### Antivirus / File Lock on Windows

`Directory.Delete(path, recursive: true)` can fail on Windows if an antivirus scanner has a file open. The export handles this by catching `IOException` on the cleanup of `.old-{guid}` folders and logging a warning. The primary export (write new → rename) is not affected.

### Migration Downgrade Protection

Phase 2 compares `manifest.lastMigrationId` against the assembly's migration list. If the manifest references a migration that doesn't exist in the current assembly (the exported DB is newer than the app), restore is refused with a clear error. This prevents silent schema incompatibility.

### Zero Avatars / Zero Feedback

If no users have uploaded avatars or feedback images, the export still succeeds. The `avatars\` and `feedback\` folders in the export will be empty (or not created). The manifest shows `avatarCount: 0` and `feedbackImageCount: 0`. The restore handles this gracefully — nothing to copy.

---

## Decisions Log

| Decision | Choice | Reasoning |
|----------|--------|-----------|
| Export location | Hardcoded `C:\ShiftManager\DeploymentExport\` | Config is what we're restoring — circular dependency. All deployments use `C:\ShiftManager\` |
| Export format | Folder on disk (not ZIP) | Simplest for air-gapped environment. No zip/unzip overhead, no upload size limits |
| Restore trigger | Auto on startup | No manual copy-back steps, no chance of forgetting. Rename-after-restore prevents double-restore |
| Config handling | No special encryption | Air-gapped, physically secured machine. Encryption adds failure modes without meaningful security gain |
| Folder cleanup | Keep 2 most recent `.restored-*` | Safety net matching existing backup retention pattern |
| Archives/Uploads exclusion | Excluded | Ephemeral, regenerable. Not operational state |
| Two-phase restore | Phase 1 (static, pre-builder) + Phase 2 (DI, post-build) | Config and DP keys must be on disk before `WebApplication.CreateBuilder()` reads them |
