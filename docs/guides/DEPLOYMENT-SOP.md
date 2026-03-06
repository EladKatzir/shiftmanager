# ShiftManager IIS Deployment SOP

## Prerequisites

1. **Server Requirements**
   - Windows Server 2019+ with IIS 10+
   - .NET 8.0 Runtime (ASP.NET Core Hosting Bundle)
   - Minimum 2GB RAM, 10GB disk
   - Server timezone: **Israel Standard Time** (verified via `tzutil /g`)

2. **Pre-Deployment Checks**
   - [ ] .NET 8.0 ASP.NET Core Hosting Bundle installed
   - [ ] IIS URL Rewrite Module installed
   - [ ] Server timezone is `Israel Standard Time`
   - [ ] Target disk has >1GB free space
   - [ ] Database path is on **local disk** (NOT network share/UNC path)
   - [ ] Backup destination directory exists and is writable
   - [ ] IIS Static Content Compression enabled
   - [ ] IIS Dynamic Content Compression feature installed and enabled

## Fresh Deployment

### Step 1: Create Application Directory
```
mkdir C:\ShiftManager
mkdir C:\ShiftManager\Data
mkdir C:\ShiftManager\Backups
mkdir C:\ShiftManager\Keys
```

### Step 2: Publish Application
```
dotnet publish -c Release -o C:\ShiftManager\App
```

### Step 3: Configure IIS
1. Create IIS Application Pool: `ShiftManagerPool`
   - .NET CLR Version: **No Managed Code**
   - Pipeline Mode: **Integrated**
   - Identity: **ApplicationPoolIdentity** (or a service account)
   - Set memory limit: **1024 MB** (Private Memory)
2. Create IIS Site pointing to `C:\ShiftManager\App`
3. Set `ASPNETCORE_ENVIRONMENT` = `Production` in IIS Configuration Editor
   - Path: system.webServer > aspNetCore > environmentVariables

### Step 4: Configure Production Settings
1. Copy `appsettings.Production.json` to deployment directory
2. **CRITICAL**: Change the Owner password from default:
   - Edit `Seeding.Owner.Password` in `appsettings.Production.json`
   - Use a strong password (12+ characters, mixed case, numbers, symbols)
3. Set `ApiKeyHmacSecret` to a random 64-character string
4. Configure `ConnectionStrings.Default` to point to local DB path
5. Configure `Backup.Directory` to a local backup path
6. Configure email settings if email notifications are needed

### Step 4b: Enable IIS Compression (Performance)
1. Open IIS Manager > Server level > **Compression**
2. Enable **Static content compression** (checkbox)
3. Enable **Dynamic content compression** (checkbox)
4. If not available, install the IIS **Dynamic Content Compression** feature:
   ```
   dism /online /enable-feature /featurename:IIS-HttpCompressionDynamic
   ```
5. The application includes ASP.NET Core response compression as well — IIS compression provides an additional layer for static assets.

### Step 5: Start and Verify
1. Start the IIS Application Pool
2. Browse to the application URL
3. Log in with the configured Owner credentials
4. **Immediately change the Owner password** via the profile page
5. Verify System Health page shows all green

### Step 6: Post-Deployment Validation
- [ ] Owner password changed from default
- [ ] `AllowPublicSignup` is `false`
- [ ] System Health page accessible and showing healthy status
- [ ] Backup directory is writable (test via manual backup page)
- [ ] No test users exist in the database
- [ ] Server timezone displays correctly in System Health
- [ ] Database file is on local disk (not network share)

## Upgrade Deployment

### Step 1: Pre-Upgrade Backup
1. **CRITICAL**: Backup `app.db` manually before upgrading
   ```
   copy C:\ShiftManager\Data\app.db C:\ShiftManager\Backups\app.db.pre-upgrade
   ```
2. Backup Data Protection keys: `C:\ShiftManager\Keys\*`
3. Backup `appsettings.Production.json`

### Step 2: Stop Application
1. Stop the IIS Application Pool (`ShiftManagerPool`)
2. Wait 10 seconds for graceful shutdown

### Step 3: Deploy New Version
1. Publish new version to a staging directory
2. Replace application files (preserve `appsettings.Production.json`, `app.db`, `Keys/`)
3. Start the IIS Application Pool

### Step 4: Verify
1. Check application starts successfully
2. Verify database migration ran (check startup logs)
3. Test basic operations (login, view calendar, etc.)
4. Check System Health page

## Rollback Procedure

If issues are discovered after upgrade:

1. Stop IIS Application Pool
2. Restore previous application files from backup
3. Restore `app.db` from pre-upgrade backup:
   ```
   copy C:\ShiftManager\Backups\app.db.pre-upgrade C:\ShiftManager\Data\app.db
   ```
4. Restore Data Protection keys if needed
5. Start IIS Application Pool
6. Verify rollback successful

## Server Migration

When moving to a new server:

1. **CRITICAL**: Copy Data Protection keys (`C:\ShiftManager\Keys\*`)
2. Copy `app.db` database file
3. Copy `appsettings.Production.json`
4. Copy backup directory contents
5. Follow Fresh Deployment steps 1-6 on new server
6. Verify encrypted data (email config) still decrypts correctly

## Runbooks

### SQLite BUSY Error
- **Symptom**: "database is locked" errors in logs
- **Cause**: Write contention during peak usage
- **Fix**: Verify WAL mode is enabled. Check `PRAGMA journal_mode;` should return `wal`.
  Increase `busy_timeout` if needed. Reduce concurrent write paths.

### Migration Failure
- **Symptom**: Application crashes on startup after upgrade
- **Cause**: Database migration failed mid-execution
- **Fix**: Restore from pre-migration backup (`app.db.pre-migration-*`).
  Report the migration error. Do not attempt the upgrade again until fixed.

### High Memory Usage
- **Symptom**: IIS recycles app pool, users get disconnected
- **Cause**: Large export, rate limiter growth, or memory leak
- **Fix**: Check for large concurrent exports. Monitor memory trend.
  IIS memory limit should be 1024MB. Restart app pool during off-hours.

### Data Protection Key Loss
- **Symptom**: Email configuration shows garbled/empty values after server change
- **Cause**: Data Protection keys not copied to new server
- **Fix**: Copy keys from old server `C:\ShiftManager\Keys\` to new location.
  If keys are lost, re-enter email configuration values.

### Account Lockout Storm
- **Symptom**: Multiple users locked out simultaneously
- **Cause**: Brute force attack or shared credential issue
- **Fix**: Use OwnerHub > Locked Users page to unlock affected accounts.
  Check IP addresses in audit log for suspicious patterns.

### Disk Space Low
- **Symptom**: System Health shows disk space warning
- **Cause**: Backup accumulation, log growth, or database growth
- **Fix**: Clean old backups (keep last 14 days). Archive old logs.
  Check `Backup.RetentionDays` setting.

### SignalR Disconnection Storm
- **Symptom**: High CPU after server restart, many reconnection attempts
- **Cause**: All clients reconnecting simultaneously
- **Fix**: Normal behavior — clients use exponential backoff with jitter.
  CPU should stabilize within 30-60 seconds. Monitor and wait.

## Staged Rollout

For major version upgrades:

1. **Phase 1**: Deploy to one molecule's managers first
2. **Monitor 24 hours**: Check for errors, data issues, user complaints
3. **Phase 2**: If Phase 1 is clean, deploy to all users
4. **Rollback gate**: If any data integrity issues are found, rollback immediately

## Monitoring

- **IIS**: Configure Application Pool monitoring for crash alerts
- **Disk**: Windows Server disk space alerts at 10% free
- **Health**: Periodically check `/health` and `/ready` endpoints
- **Logs**: Review application logs daily during first week after deployment
