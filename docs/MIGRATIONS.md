# Database Migration Safety Guidelines

This document provides guidelines for safely applying and rolling back database migrations in the ShiftManager application.

## Overview

ShiftManager uses Entity Framework Core migrations to manage database schema changes. As of October 2025, there are **11 migrations** that have been applied to the production database.

## Known Issues

### ISSUE-005: Non-Transactional PRAGMA Operations

**Severity:** 🟡 Medium

Three migrations contain SQLite PRAGMA operations that cannot be executed within transactions. This means if a migration fails mid-execution, the database could be left in an inconsistent state.

**Affected Migrations:**
- `20250928201142_AddNavigationProperties` (Migration #3)
- `20250930222145_UpdateJoinRequestPasswordTypes` (Migration #8)
- `20251004215820_AddTraineeRoleAndShadowing` (Migration #11)

**Warning Message:**
```
warning: The migration operation 'PRAGMA foreign_keys = 0;' from migration
'AddNavigationProperties' cannot be executed in a transaction. If the app is
terminated or an unrecoverable error occurs while this operation is being
executed then the migration will be left in a partially applied state and
would need to be reverted manually before it can be applied again.
```

**Root Cause:**
- EF Core generates `PRAGMA foreign_keys = 0;` and `PRAGMA foreign_keys = 1;` operations for table recreations
- SQLite PRAGMA commands cannot be executed within transactions
- If migration fails mid-execution, database could be left with foreign keys disabled

**Observed Behavior:**
- ✅ All migrations tested successfully (up/down/up cycle completed)
- ✅ Rollback from `AddTraineeRoleAndShadowing` to `AuditRoleAssignments` succeeded
- ✅ Re-applying migration succeeded
- ⚠️ Warning does not prevent migration from completing

**Risk Level:** Medium (migrations work, but recovery from failure requires manual intervention)

## Production Migration Safety Protocol

### Before Applying Migrations

#### 1. Pre-Migration Checklist

- [ ] **Backup database** - Critical for recovery if migration fails
- [ ] **Schedule maintenance window** - Application should be stopped during migration
- [ ] **Test on staging environment** - Apply migration to staging first
- [ ] **Review migration SQL** - Generate and review SQL before applying
- [ ] **Verify rollback script** - Ensure rollback script exists in `Migrations/rollback/`
- [ ] **Monitor disk space** - Ensure sufficient space for migration and backup
- [ ] **Alert stakeholders** - Notify team of upcoming maintenance

#### 2. Backup Procedure

```bash
# For SQLite (Development/Staging)
cp app.db app.db.backup.$(date +%Y%m%d_%H%M%S)

# Verify backup
sqlite3 app.db.backup.* "PRAGMA integrity_check;"
```

For production PostgreSQL:
```bash
# Full database backup
pg_dump -Fc shiftmanager > shiftmanager_backup_$(date +%Y%m%d_%H%M%S).dump

# Verify backup
pg_restore --list shiftmanager_backup_*.dump
```

#### 3. Generate Migration SQL for Review

```bash
# Generate SQL for the migration
dotnet ef migrations script <LAST_APPLIED_MIGRATION> <NEW_MIGRATION> --context AppDbContext

# Example: Generate SQL for migration #12
dotnet ef migrations script AddTraineeRoleAndShadowing NewMigration --context AppDbContext --output migration_12.sql
```

Review the generated SQL:
- Check for PRAGMA operations (indicates non-transactional operations)
- Verify data transformations are correct
- Ensure no unexpected table drops
- Validate constraint changes

### Applying Migrations Safely

#### Option 1: Using EF Core CLI (Recommended for Development)

```bash
# Apply all pending migrations
dotnet ef database update --context AppDbContext

# Apply specific migration
dotnet ef database update <MIGRATION_NAME> --context AppDbContext
```

#### Option 2: Using SQL Scripts (Recommended for Production)

```bash
# 1. Generate SQL script
dotnet ef migrations script <FROM> <TO> --context AppDbContext --output migration.sql

# 2. Review SQL script thoroughly

# 3. Backup database
cp app.db app.db.backup.$(date +%Y%m%d_%H%M%S)

# 4. Stop application
systemctl stop shiftmanager  # or appropriate command

# 5. Apply SQL script
sqlite3 app.db < migration.sql

# 6. Verify migration applied
sqlite3 app.db "SELECT * FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 5;"

# 7. Restart application
systemctl start shiftmanager

# 8. Run smoke tests
curl http://localhost:5000/health
```

### Migration Execution Monitoring

While migration is running:

1. **Monitor application logs:**
   ```bash
   tail -f /var/log/shiftmanager/app.log
   ```

2. **Monitor database:**
   ```bash
   # Check active connections
   sqlite3 app.db "PRAGMA database_list;"

   # Check for locks
   lsof app.db
   ```

3. **Watch for errors:**
   - Foreign key constraint violations
   - Unique constraint violations
   - Data type conversion errors
   - Timeout errors

### Post-Migration Verification

#### 1. Verify Migration History

```bash
# Check migration was recorded
sqlite3 app.db "SELECT MigrationId, ProductVersion FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 5;"
```

Expected output:
```
20251004215820_AddTraineeRoleAndShadowing|9.0.9
20251003232434_AuditRoleAssignments|9.0.9
...
```

#### 2. Verify Database Schema

```bash
# Check table structure
sqlite3 app.db ".schema Users"

# Check constraints
sqlite3 app.db "PRAGMA foreign_key_list(Users);"

# Check indexes
sqlite3 app.db "PRAGMA index_list(Users);"
```

#### 3. Verify Data Integrity

```bash
# Run integrity check
sqlite3 app.db "PRAGMA integrity_check;"

# Check foreign key constraints
sqlite3 app.db "PRAGMA foreign_key_check;"
```

#### 4. Application Smoke Tests

```bash
# Health check
curl http://localhost:5000/health

# Login test
curl -X POST http://localhost:5000/Auth/Login \
  -d "email=admin@local&password=admin123" \
  -c cookies.txt

# Basic functionality test
curl -b cookies.txt http://localhost:5000/Calendar/Month
```

## Migration Failure Recovery

### If Migration Fails Before PRAGMA Operations

If the migration fails before reaching PRAGMA operations, it can be rolled back automatically by EF Core.

1. **Check migration history:**
   ```bash
   sqlite3 app.db "SELECT * FROM __EFMigrationsHistory;"
   ```

2. **If migration is NOT in history:**
   - Database is in previous state
   - Safe to retry migration after fixing issue

### If Migration Fails During PRAGMA Operations

⚠️ **CRITICAL:** Database may be in inconsistent state with foreign keys disabled.

1. **Stop application immediately:**
   ```bash
   systemctl stop shiftmanager
   ```

2. **Check foreign key status:**
   ```bash
   sqlite3 app.db "PRAGMA foreign_keys;"
   ```

   If result is `0`, foreign keys are DISABLED.

3. **Restore from backup:**
   ```bash
   # Backup current (corrupted) state for investigation
   mv app.db app.db.failed.$(date +%Y%m%d_%H%M%S)

   # Restore from backup
   cp app.db.backup.<timestamp> app.db

   # Verify integrity
   sqlite3 app.db "PRAGMA integrity_check;"
   ```

4. **Investigate failure:**
   - Review application logs
   - Review migration SQL
   - Test migration on staging environment
   - Fix underlying issue

5. **Retry migration** after issue is resolved

### If Migration Succeeds But Application Fails

1. **Check application logs:**
   ```bash
   tail -n 100 /var/log/shiftmanager/app.log
   ```

2. **Verify migration applied correctly:**
   ```bash
   sqlite3 app.db "SELECT * FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 1;"
   ```

3. **If migration applied but app broken:**
   - Database schema is correct
   - Issue is likely in application code
   - Consider rolling back migration if cannot be fixed quickly

4. **Rollback procedure:**
   ```bash
   # Use rollback script
   sqlite3 app.db < Migrations/rollback/<NN>_MigrationName_Rollback.sql

   # Update migration history
   sqlite3 app.db "DELETE FROM __EFMigrationsHistory WHERE MigrationId = '<MIGRATION_ID>';"
   ```

## Best Practices

### 1. Migration Development

- **Small, focused migrations** - Each migration should do one thing
- **Avoid data transformations** - Separate data migrations from schema migrations
- **Test rollback** - Always test the Down() method works
- **Idempotent SQL** - Generated SQL should be safely re-runnable

### 2. Testing Migrations

```bash
# Test full migration cycle
dotnet ef database update 0 --context AppDbContext  # Drop all
dotnet ef database update --context AppDbContext    # Apply all
dotnet ef database update <PREVIOUS> --context AppDbContext  # Rollback one
dotnet ef database update --context AppDbContext    # Re-apply
```

### 3. Production Migrations

- **Always backup** - Before every migration
- **Maintenance window** - Stop application during migration
- **Staging first** - Test on staging before production
- **Rollback plan** - Have tested rollback procedure ready
- **Monitor closely** - Watch logs and metrics during migration

### 4. PRAGMA Migration Handling

For migrations containing PRAGMA operations:

- **Extra caution** - Higher risk of corruption
- **Larger maintenance window** - Allow time for recovery if needed
- **Database administrator present** - Have DBA on standby
- **Practice recovery** - Test backup/restore procedure beforehand

## Emergency Contacts

In case of migration failure in production:

- **Database Administrator:** [Contact Info]
- **DevOps Lead:** [Contact Info]
- **Application Owner:** [Contact Info]

## References

- [ISSUE-005](tasks.md#issue-005-non-transactional-migration-operations) - Non-transactional PRAGMA operations
- [Rollback Scripts](Migrations/rollback/README.md) - Complete rollback documentation
- [Database Schema](project.md#database-schema) - Current database schema documentation
- [EF Core Migrations Documentation](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/)
- [SQLite PRAGMA Documentation](https://www.sqlite.org/pragma.html)
