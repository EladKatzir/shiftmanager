# Deferred Task: Fresh Database Migration Failure in Test-Application.ps1

**Status:** RESOLVED
**Priority:** LOW
**Discovered:** v3.6.0 publish debugging session
**Last Updated:** 2026-04-04
**Resolved:** 2026-04-04

---

## Summary

The `build/Test-Application.ps1` script fails when the published `ShiftManager.exe` application attempts to run `db.Database.MigrateAsync()` on a fresh SQLite database at startup.

### Root Cause (RESOLVED)

The failure was NOT caused by `ShiftScopeRedesign` (that was a red herring — the chain broke before it was reached). The actual root cause was **4 manually-backdated migrations** (identifiable by artificial timestamps and missing `.Designer.cs` files) that duplicated tables/columns already created by later EF-generated migrations:

1. `20251101000000_AddCustomNameToShiftType` — duplicate `CustomName` column on ShiftTypes
2. `20260206200000_AddDutyRotationTables` — duplicate DutyRotations/DutyRotationEntries/DutyRotationLogs tables
3. `20260206200100_AddVacationApprovalRules` — duplicate VacationApprovalRules table
4. `20260228120000_AddDepartmentIdToUserJoinRequests` — duplicate DepartmentId column on UserJoinRequests

**Fix:** All 4 backdated migrations were made no-ops (empty Up/Down methods). The files are kept because existing databases reference them in `__EFMigrationsHistory`. Additional hardening: `ShiftScopeRedesign` got a `DROP TABLE IF EXISTS` guard, `Program.cs` got try-catch around `MigrateAsync()`, and `Test-Application.ps1` got improved error detection (reads stderr, expanded fatal patterns, removed PRAGMA mask).

**Key fact:** The migration applies cleanly when run via `dotnet ef database update` at design-time, but fails at runtime in the published application.

### Current Workaround

Run the publish pipeline with `-SkipTests` flag:

```powershell
scripts/Update-FinalProductPublish.ps1 -SkipTests
```

This bypasses `build/Test-Application.ps1` entirely (Stage 6 of `Build-Release.ps1`). Production deployments and existing databases are **unaffected** because they apply migrations incrementally, not from scratch.

---

## The Problem in Detail

### What Test-Application.ps1 Does

1. Starts the published `ShiftManager.exe` from `ProjectPublish/`
2. The running application initializes `Program.cs`, which:
   - Creates a fresh `app.db` SQLite database
   - Calls `db.Database.MigrateAsync()` (line ~468-472)
   - Applies ALL migrations from scratch, starting with `001_InitialCreate`
3. The test waits for successful startup and verifies:
   - The application started without crashing
   - `app.db` was created
   - Seed data exists (tables populated)
4. Crashes are detected and logged

### The Failing Migration

**File:** `Migrations/20260324030331_ShiftScopeRedesign.cs`

This migration was converted from EF Core fluent API to raw SQL because EF Core 9's SQLite provider triggers conflicting table rebuilds when multiple schema changes (AddColumn + RenameColumn + AlterColumn) are combined in a single migration step.

**What it does:**
- Disables foreign key constraints (`PRAGMA foreign_keys = 0`)
- Creates a temporary table `ef_temp_ShiftTypes` with the new schema
- Copies data from the old `ShiftTypes` table
- Drops the old table
- Renames the temp table back to `ShiftTypes`
- Re-enables foreign key constraints

**Why it works via `dotnet ef database update`:**
- The EF Core design-time migration executor has specific handling for raw SQL
- Connection pooling and transaction management are known/controlled
- The tool validates the migration before execution

**Why it fails in `MigrateAsync()` at runtime:**
- Unknown — the exact SQLite error is NOT captured in logs
- Only a generic "Failed executing DbCommand" message appears at Error level

---

## Symptoms

1. **In logs:** Single error line, no stack trace or SQLite error code
   ```
   [ERROR] Failed executing DbCommand (..."CREATE TABLE "ef_temp_ShiftTypes"...)
   ```

2. **Application behavior:** The app crashes silently during startup, exits with non-zero code

3. **When it occurs:** Only on fresh database creation; does NOT affect incremental migrations on existing databases

4. **Repeatability:** Consistent — every fresh database creation via `MigrateAsync()`

---

## Root Cause Analysis (Hypotheses)

### Hypothesis 1: Connection/Transaction Handling Difference

**What:** `dotnet ef database update` and runtime `MigrateAsync()` may use different:
- Connection pooling configuration
- Transaction isolation levels
- Journal mode settings
- Connection timeout or retry logic

**Impact:** Different SQLite connection state could cause `CREATE TABLE` to fail if the connection is in an unexpected state (e.g., committed transaction, pending rollback).

**Check:** Compare the connection string and SQLite pragma settings used by each path.

---

### Hypothesis 2: Temporary Table Name Collision

**What:** A prior migration's EF Core table rebuild (e.g., when adding/dropping FK constraints on `ShiftTypes`) also creates `ef_temp_ShiftTypes` internally. If that table is not fully cleaned up, our `CREATE TABLE "ef_temp_ShiftTypes"` fails with "table already exists".

**Why it might happen at runtime but not via design-time tools:** The design-time migration executor may force cleanup between migrations, while runtime execution does not.

**Check:** Add a query before the CREATE TABLE:
```sql
SELECT name FROM sqlite_master WHERE type='table' AND name='ef_temp_ShiftTypes'
```

---

### Hypothesis 3: `suppressTransaction: true` Behavior Difference

**What:** The migration uses `suppressTransaction: true` on all `Sql()` calls. The runtime migration executor might interpret this differently than the design-time tools.

**Impact:** Without transaction suppression, SQLite's autocommit behavior could change, causing the temporary table operations to be rolled back unexpectedly.

**Check:** Run the migration with transaction tracing enabled to see if `BEGIN`/`COMMIT` statements are being issued differently.

---

### Hypothesis 4: PRAGMA Foreign Keys Not Persisting

**What:** The `PRAGMA foreign_keys = 0` is set via a separate `Sql()` call, which may be executed in a separate connection/context than the table rebuild operations.

**Impact:** If the PRAGMA is set on Connection A and the CREATE TABLE runs on Connection B (due to connection pooling), the pragma setting is lost, causing FK-related errors during the table creation.

**Check:** Verify that all operations in the migration use the same connection context.

---

### Hypothesis 5: Concurrent Connection Access

**What:** The published application may open health check endpoints or SignalR connections during startup, which open additional database connections. These concurrent connections could interfere with the migration's table operations.

**Impact:** SQLite uses file-level locking; concurrent write access could cause "database is locked" or "table is locked" errors.

**Check:** Ensure database access is single-threaded during migration execution, or implement proper locking.

---

## What Needs to Be Done

### Phase 1: Capture the Root Cause

#### Task 1.1: Enhance Error Logging

**What:** Modify the migration error handling to capture the full exception details.

**Where:** `Program.cs` around lines 468-472 in the `MigrateAsync()` call.

**Action:**
```csharp
try
{
    await db.Database.MigrateAsync();
}
catch (Exception ex)
{
    // Capture full exception, including InnerException and SQLite error code
    _logger.LogError(ex, "Database migration failed. SQLite Error: {SqliteError}",
        ex.InnerException?.Message ?? "Unknown");
    throw;
}
```

**Expected outcome:** Logs will contain the full SQLite error message, allowing diagnosis of the actual failure.

---

#### Task 1.2: Create a Standalone Reproduction

**What:** Build a minimal console app that reproduces the issue in isolation.

**Where:** Create `test/ShiftManager.MigrationTest/Program.cs` or similar.

**Action:**
```csharp
// Minimal repro:
// 1. Create a fresh SQLite database
// 2. Apply migrations via MigrateAsync()
// 3. Capture detailed error information
// 4. Log the connection string, pragmas, and journal mode

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlite("Data Source=:memory:")
    .LogTo(Console.WriteLine, LogLevel.Debug)
    .EnableSensitiveDataLogging()
    .Build();

using (var db = new AppDbContext(options))
{
    try
    {
        await db.Database.MigrateAsync();
        Console.WriteLine("SUCCESS: Migration applied");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAILED: {ex}");
        Console.WriteLine($"Inner: {ex.InnerException}");
    }
}
```

**Expected outcome:** Reproduction shows the exact error and allows testing fixes without rebuilding the entire application.

---

#### Task 1.3: Compare Connection Handling

**What:** Inspect how `dotnet ef database update` and runtime `MigrateAsync()` configure SQLite connections.

**Where:**
- `Program.cs` - the EF Core DbContext configuration
- `appsettings.json` - connection string and any SQLite-specific options

**Action:**
- Verify that both paths use the same connection string
- Check if connection pooling is enabled for `MigrateAsync()`; if so, disable it for migration-only context
- Verify journal mode (`PRAGMA journal_mode`) is the same (default is "delete", prefer "wal" for better concurrency)
- Check if foreign key enforcement is enabled globally (`PRAGMA foreign_keys`)

**Expected outcome:** Identify connection configuration differences that could explain the runtime failure.

---

### Phase 2: Implement a Fix

#### Option 2.1: Use `CREATE TABLE IF NOT EXISTS` for Temp Table

**Least disruptive fix.** If the issue is temp table collision, changing the CREATE TABLE statement is safe.

**Change:** In `20260324030331_ShiftScopeRedesign.cs`:

```csharp
Sql("CREATE TABLE IF NOT EXISTS \"ef_temp_ShiftTypes\" (...)");
```

**Pros:**
- One-line fix
- No new migration required
- Handles collision gracefully

**Cons:**
- Doesn't address the root cause if the issue is not collision
- May mask underlying problem

**Risk:** LOW — CREATE TABLE IF NOT EXISTS is idempotent and safe.

---

#### Option 2.2: Use a Unique Temp Table Name

**Safe alternative.** Rename the temp table to avoid EF Core's internal naming convention.

**Change:** In `20260324030331_ShiftScopeRedesign.cs`:

```csharp
// Before: ef_temp_ShiftTypes
// After:  _scope_rebuild_ShiftTypes_temp_20260324

Sql("CREATE TABLE \"_scope_rebuild_ShiftTypes_temp_20260324\" (...)");
Sql("INSERT INTO \"_scope_rebuild_ShiftTypes_temp_20260324\" SELECT ... FROM \"ShiftTypes\"");
Sql("DROP TABLE \"ShiftTypes\"");
Sql("ALTER TABLE \"_scope_rebuild_ShiftTypes_temp_20260324\" RENAME TO \"ShiftTypes\"");
```

**Pros:**
- Guarantees no collision with EF Core's naming
- Easy to implement

**Cons:**
- Still a symptom fix, not a root cause fix
- Creates inconsistency with EF Core conventions

**Risk:** LOW — Only affects temp table naming, not schema logic.

---

#### Option 2.3: Split the Migration Into Two Steps

**More robust fix.** Separate column additions from renames/alters.

**What:** Create two migrations:
1. Migration 1: Add new columns
2. Migration 2: Rename/alter columns (raw SQL table rebuild)

**Why:** Reduces the scope of the raw SQL operation, making it less likely to conflict with EF Core's internal table rebuilds.

**Pros:**
- Addresses potential root cause (conflicting table rebuild logic)
- Cleaner separation of concerns
- Easier to debug if either migration fails independently

**Cons:**
- Requires new migration (more code to review)
- More complex to implement (need to update Designer snapshot)
- Users with pending migrations may need to delete and regenerate migrations (migration tooling limitation)

**Risk:** MEDIUM — Requires careful migration ordering and testing.

---

#### Option 2.4: Disable Connection Pooling During Migration

**Targeted fix for connection handling hypothesis.**

**Change:** In `Program.cs`:

```csharp
var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlite(connectionString, sqliteOptions =>
    {
        sqliteOptions.CommandTimeout(60);
        // Disable pooling for initial migration only
        sqliteOptions.MaxBatchSize(1);
    })
    .Build();

using (var db = new AppDbContext(options))
{
    await db.Database.MigrateAsync();
}
```

**Pros:**
- Isolates each SQL statement to its own connection
- Guarantees no concurrent access during migration

**Cons:**
- Slower migration on large databases
- Temporary workaround, not a permanent fix

**Risk:** LOW — Connection pooling is application-level, migration only happens once at startup.

---

### Phase 3: Testing & Validation

#### Task 3.1: Test All Fixes on Fresh Database

**For each option above:**
1. Apply the fix
2. Delete `app.db`
3. Run `build/Test-Application.ps1`
4. Verify: app starts, `app.db` is created, no errors in logs

---

#### Task 3.2: Test on Existing Database (Incremental Migration)

**Ensure the fix doesn't break incremental migrations:**
1. Create a database at v3.5.0 (before the broken migration)
2. Publish v3.6.0 with the fix
3. Start the application (runs `MigrateAsync()` incrementally)
4. Verify: migration applies cleanly, no data loss

---

#### Task 3.3: Integration Test

**Ensure the fix doesn't introduce new issues:**
1. Run all existing unit and integration tests
2. Verify seed data is created correctly
3. Spot-check core functionality (calendar, assignments, etc.)

---

## Files Involved

| File | Role |
|------|------|
| `build/Test-Application.ps1` | The automated test that detects the crash |
| `scripts/Build-Release.ps1` | Stage 6 calls `Test-Application.ps1` |
| `scripts/Update-FinalProductPublish.ps1` | Calls `Build-Release.ps1`; offers `-SkipTests` flag |
| `Migrations/20260324030331_ShiftScopeRedesign.cs` | The failing migration |
| `Migrations/20260324030331_ShiftScopeRedesign.Designer.cs` | EF Core model snapshot |
| `Program.cs` | Lines ~468-472: `MigrateAsync()` call |

---

## Current Impact

- **Automated tests:** BROKEN — cannot verify fresh database creation during build
- **Production deployments:** UNAFFECTED — apply migrations incrementally
- **Existing databases:** UNAFFECTED — incremental migrations work fine
- **Workaround:** Use `-SkipTests` flag on publish pipeline

---

## Next Steps

1. **Immediate (if blocking a release):**
   - Use `-SkipTests` workaround
   - Document in release notes if fresh database creation is needed

2. **Short term (next maintenance window):**
   - Implement Phase 1 (capture root cause)
   - Pick the most promising hypothesis and test it

3. **Medium term (after root cause is known):**
   - Implement Phase 2 fix (choose one of the options based on diagnosis)
   - Complete Phase 3 testing

4. **Long term:**
   - Add automated fresh-database test to CI/CD pipeline (once it passes reliably)
   - Monitor migration logs for similar issues in future releases

---

## References

- **EF Core 9 SQLite Provider:** https://learn.microsoft.com/en-us/ef/core/providers/sqlite/
- **SQLite Pragma Documentation:** https://www.sqlite.org/pragma.html
- **Migration Design:** `Migrations/20260324030331_ShiftScopeRedesign.cs`
- **Test Script:** `build/Test-Application.ps1`
- **Related Commits:**
  - `1f0aa1d` - added new tests, added how to test guide, added new features (v3.6.0)
  - `81046f4` - fix: address deep review findings — Phase 2 crash protection, WAL cleanup safety

---

## Deferred Work Items

This task itself is deferred. The following work items must be completed to resolve it:

- [ ] **Phase 1.1:** Enhance error logging in `Program.cs` to capture full SQLite error
- [ ] **Phase 1.2:** Create standalone migration reproduction test
- [ ] **Phase 1.3:** Compare connection handling between design-time and runtime
- [ ] **Phase 2.x:** Implement fix (choose option after root cause is identified)
- [ ] **Phase 3.1:** Test fix on fresh database
- [ ] **Phase 3.2:** Test fix on existing database (incremental)
- [ ] **Phase 3.3:** Run integration tests

---

## Questions for Discussion

1. Is `-SkipTests` acceptable as a long-term workaround, or is fresh-database testing a requirement?
2. Should Phase 1 investigation be prioritized, or should we go straight to the simplest fix (Option 2.1)?
3. Is there a specific SQLite configuration that differs between the design-time tools and the published app?
