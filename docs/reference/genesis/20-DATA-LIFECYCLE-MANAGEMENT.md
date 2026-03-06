# 20: Data Lifecycle Management (Archive, Purge, Import)

**Document Version:** 1.0
**Last Updated:** 2026-01-06
**Feature Status:** ✅ Complete
**Codebase Version:** v2.2.0+ (Branch: newestversionpriorpl)

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [Business Context](#business-context)
3. [Architecture Overview](#architecture-overview)
4. [Service Layer Implementation](#service-layer-implementation)
5. [Safety Mechanisms](#safety-mechanisms)
6. [Export Formats](#export-formats)
7. [Database Deletion Order](#database-deletion-order)
8. [User Interface](#user-interface)
9. [Security & Authorization](#security--authorization)
10. [File Storage Strategy](#file-storage-strategy)
11. [Testing Strategy](#testing-strategy)
12. [Usage Examples](#usage-examples)
13. [Implementation Details](#implementation-details)
14. [Design Decisions & Tradeoffs](#design-decisions--tradeoffs)
15. [Future Enhancements](#future-enhancements)

---

## Executive Summary

The **Data Lifecycle Management** feature provides Owner-level tools for managing historical data in ShiftManager through a complete archive-purge-import cycle. This feature is critical for:

- **Compliance**: Long-term data archival for regulatory requirements
- **Performance**: Database size management by purging old data
- **Disaster Recovery**: Export/import capabilities for backup restoration
- **Data Migration**: Moving data between ShiftManager instances

### Key Capabilities

| Capability | Description | Access Level |
|------------|-------------|--------------|
| **Archive Creation** | Export historical data in two formats (CSV for humans, NDJSON for re-import) | Owner Only |
| **Data Purge** | Permanently delete historical data with safety mechanisms | Owner Only |
| **Data Re-Import** | Import previously archived data with duplicate detection | Owner Only |

### Safety Philosophy

This feature implements **defense-in-depth** safety mechanisms:

1. **Fresh Archive Requirement**: Must create archive before purge
2. **Typed Confirmation**: Must type exact confirmation string
3. **Pre-Purge Backup**: Automatic database backup before deletion
4. **Transaction Rollback**: All-or-nothing deletion
5. **Audit Logging**: Complete audit trail of all operations

---

## Business Context

### Problem Statement

ShiftManager databases grow continuously as companies use the system:
- Shift instances accumulate (365+ days × multiple shifts = 1000+ records/year)
- Swap requests, time-off requests, chores, and on-duty records add up
- SQLite file size increases, potentially affecting performance on air-gapped systems with limited storage

**Business Requirements:**
1. Delete old data without losing it permanently (compliance)
2. Export data in human-readable format for analysis (CSV)
3. Re-import data if needed (disaster recovery)
4. Prevent accidental deletion (Owner-only, multiple safety checks)
5. Support air-gapped environments (no cloud backup)

### Target Users

- **IT Administrators** (military, government facilities)
- **Compliance Officers** (need historical data exports)
- **System Owners** (database maintenance)

### Use Cases

#### Use Case 1: Annual Data Archival
**Scenario**: Military base archives data older than 1 year for compliance, purges from active database to improve performance.

**Steps**:
1. Owner creates archive of all data before 2025-01-01
2. Downloads CSV archive for long-term storage
3. Downloads NDJSON archive for disaster recovery
4. Purges data from database (with typed confirmation)
5. Database size reduced by 60%

#### Use Case 2: Disaster Recovery
**Scenario**: Database corrupted, need to restore from archive.

**Steps**:
1. Owner uploads previously created NDJSON archive
2. System validates archive (checks company match, missing users)
3. Owner imports data with "Skip Duplicates" policy
4. Historical shifts and requests restored

#### Use Case 3: Company Merger Data Migration
**Scenario**: Two companies merge, need to migrate historical data.

**Steps**:
1. Company A exports data as NDJSON
2. Admin creates users in Company B matching Company A emails
3. Company B imports archive (user matching by email)
4. Historical data migrated

---

## Architecture Overview

### Three-Service Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    DataLifecycle.cshtml                      │
│                    (Owner-Only Page)                         │
│                    3 Tabs: Archive | Purge | Import          │
└────────┬───────────────────┬──────────────────┬─────────────┘
         │                   │                  │
         ▼                   ▼                  ▼
┌────────────────┐  ┌────────────────┐  ┌────────────────┐
│ ArchiveService │  │  PurgeService  │  │ ImportService  │
├────────────────┤  ├────────────────┤  ├────────────────┤
│• Preview       │  │• Validate      │  │• Validate      │
│• Create CSV    │  │• Backup DB     │  │• Import Data   │
│• Create NDJSON │  │• Delete Data   │  │• Detect Dups   │
│• SHA-256 Hash  │  │• VACUUM        │  │• Skip Missing  │
└────────┬───────┘  └────────┬───────┘  └────────┬───────┘
         │                   │                    │
         └───────────────────┴────────────────────┘
                             │
                             ▼
                     ┌───────────────┐
                     │  AppDbContext │
                     │  (EF Core)    │
                     └───────────────┘
```

### Technology Stack

| Component | Technology | Purpose |
|-----------|-----------|---------|
| **ZIP Creation** | System.IO.Compression.ZipFile | Archive packaging |
| **CSV Generation** | StreamWriter (manual) | Human-readable export |
| **JSON Serialization** | System.Text.Json | NDJSON export/import |
| **Hashing** | SHA256 (System.Security.Cryptography) | Archive integrity verification |
| **Transaction Management** | EF Core BeginTransactionAsync | Atomic purge operations |
| **Database Optimization** | SQLite VACUUM | Reclaim space after purge |

---

## Service Layer Implementation

### IArchiveService / ArchiveService

**Location**: `Services/IArchiveService.cs`, `Services/ArchiveService.cs`
**Lifetime**: Scoped
**Dependencies**: AppDbContext, ITenantResolver, IAuditLogService, ILogger, IConfiguration, IHttpContextAccessor

#### Key Methods

```csharp
// Preview archive counts before creation
Task<ArchivePreview> PreviewArchiveAsync(DateOnly cutoffDate, ArchiveDataTypes types);

// Create archives (CSV + NDJSON ZIPs)
Task<ArchiveResult> CreateArchiveAsync(ArchiveRequest request);

// Get latest archive metadata (for fresh archive validation)
Task<ArchiveMetadata?> GetLatestArchiveAsync();

// Validate fresh archive exists (for purge safety check)
Task<bool> ValidateFreshArchiveAsync(DateOnly cutoffDate, ArchiveDataTypes types);
```

#### Archive Data Types (Flags Enum)

```csharp
[Flags]
public enum ArchiveDataTypes
{
    None = 0,
    Shifts = 1,           // ShiftInstance + ShiftAssignment
    SwapRequests = 2,     // SwapRequest
    TimeOff = 4,          // TimeOffRequest + OFFLINE shifts
    Chores = 8,           // Chore
    OnDuty = 16,          // OnDuty
    All = 31              // All flags combined
}
```

**Usage**: Allows selecting which data types to archive/purge (bitwise OR):
```csharp
var types = ArchiveDataTypes.Shifts | ArchiveDataTypes.TimeOff | ArchiveDataTypes.Chores;
```

#### Implementation Highlights

**CSV Generation Pattern**:
```csharp
var csvEntry = zipArchive.CreateEntry("csv/shift_instances.csv");
using var csvWriter = new StreamWriter(csvEntry.Open());

// Write header
await csvWriter.WriteLineAsync("Id,CompanyId,ShiftTypeKey,WorkDate,Name,...");

// Write rows
foreach (var si in shiftInstances)
{
    await csvWriter.WriteLineAsync($"{si.Id},{si.CompanyId},{si.ShiftType.Key},{si.WorkDate},...");
}
```

**NDJSON Generation Pattern**:
```csharp
var dataEntry = zipArchive.CreateEntry("data.ndjson");
using var dataWriter = new StreamWriter(dataEntry.Open());

foreach (var si in shiftInstances)
{
    var record = new
    {
        type = "ShiftInstance",
        data = new
        {
            si.Id,
            si.CompanyId,
            ShiftTypeKey = si.ShiftType.Key,
            si.WorkDate,
            // ... all properties
        }
    };
    await dataWriter.WriteLineAsync(JsonSerializer.Serialize(record));
}
```

**SHA-256 Hash Calculation**:
```csharp
using var sha256 = SHA256.Create();
using var stream = File.OpenRead(zipPath);
var hashBytes = await sha256.ComputeHashAsync(stream);
return Convert.ToHexString(hashBytes).ToLowerInvariant();
```

**User Email Lookup** (for entities without User navigation property):
```csharp
// TimeOffRequest, Chore, OnDuty don't have User navigation property
var allUserIds = data.TimeOffRequests.Select(t => t.UserId)
    .Concat(data.Chores.Select(c => c.UserId))
    .Concat(data.OnDuties.Select(od => od.UserId))
    .Distinct()
    .ToList();

var userEmailLookup = await _db.Users
    .Where(u => allUserIds.Contains(u.Id))
    .ToDictionaryAsync(u => u.Id, u => u.Email);

// Usage
if (userEmailLookup.TryGetValue(timeOffRequest.UserId, out var userEmail))
{
    await csvWriter.WriteLineAsync($"{timeOffRequest.Id},{userEmail},...");
}
```

---

### IPurgeService / PurgeService

**Location**: `Services/IPurgeService.cs`, `Services/PurgeService.cs`
**Lifetime**: Scoped
**Dependencies**: AppDbContext, ITenantResolver, IArchiveService, IAuditLogService, ILogger, IConfiguration, IHttpContextAccessor

#### Key Methods

```csharp
// Validate typed confirmation matches expected format
bool ValidateConfirmation(string confirmation, string companyName, DateOnly cutoffDate);

// Purge historical data with safety checks
Task<PurgeResult> PurgeDataAsync(PurgeRequest request);
```

#### PurgeRequest Structure

```csharp
public class PurgeRequest
{
    public DateOnly CutoffDate { get; set; }              // Delete data before this date
    public ArchiveDataTypes Types { get; set; }           // Which data types to purge
    public string TypedConfirmation { get; set; }         // Must match exact format
    public bool ArchiveConfirmed { get; set; }            // "I have archived" checkbox
    public bool RunVacuum { get; set; }                   // Run VACUUM after purge
    public bool HardDeleteOnDuty { get; set; }            // Hard vs. soft delete OnDuty
}
```

#### Implementation Highlights

**Confirmation Validation**:
```csharp
var expected = $"DELETE {companyName.ToUpperInvariant()} BEFORE {cutoffDate:yyyy-MM-dd}";
return confirmation.Trim() == expected;
// Example: "DELETE ACME CORP BEFORE 2025-07-01"
```

**Pre-Purge Backup**:
```csharp
var dbPath = _configuration.GetConnectionString("DefaultConnection")?.Replace("Data Source=", "").Trim();
var backupPath = Path.Combine("Backups", $"pre_purge_{DateTime.Now:yyyyMMdd_HHmmss}.db");

using (var sourceStream = new FileStream(dbPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
using (var destinationStream = new FileStream(backupPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
{
    await sourceStream.CopyToAsync(destinationStream);
}
```

**Transaction-Wrapped Deletion**:
```csharp
using var transaction = await _db.Database.BeginTransactionAsync();

try
{
    await PurgeDataInOrderAsync(request, companyId, currentUserId, result);
    await _db.SaveChangesAsync();
    await transaction.CommitAsync();

    // VACUUM runs OUTSIDE transaction (SQLite requirement)
    if (request.RunVacuum)
    {
        await _db.Database.ExecuteSqlRawAsync("VACUUM;");
    }
}
catch (Exception ex)
{
    await transaction.RollbackAsync();
    throw;
}
```

---

### IImportService / ImportService

**Location**: `Services/IImportService.cs`, `Services/ImportService.cs`
**Lifetime**: Scoped
**Dependencies**: AppDbContext, ITenantResolver, IAuditLogService, ILogger, IHttpContextAccessor

#### Key Methods

```csharp
// Validate archive before import (company match, missing users, missing shift types)
Task<ImportValidationResult> ValidateArchiveAsync(string zipPath);

// Import data from archive with duplicate detection
Task<ImportResult> ImportArchiveAsync(ImportRequest request);
```

#### ImportRequest Structure

```csharp
public class ImportRequest
{
    public string ZipPath { get; set; }                       // Path to uploaded archive
    public ImportConflictPolicy ConflictPolicy { get; set; }  // Skip / Overwrite / Fail
    public bool SkipMissingUsers { get; set; }                // Skip records with missing users
}

public enum ImportConflictPolicy
{
    SkipDuplicates,   // Default: Skip existing records
    Overwrite,        // Update existing records
    Fail              // Abort on first duplicate
}
```

#### Natural Key Strategy

Import uses **natural keys** (business keys) to match entities instead of database IDs:

| Entity | Natural Key | Rationale |
|--------|-------------|-----------|
| **User** | Email | Email is globally unique, persists across systems |
| **ShiftType** | ShiftType.Key | Unique per company, semantic meaning (e.g., "MORNING") |
| **ShiftInstance** | (CompanyId, WorkDate, ShiftTypeKey) | Composite key uniquely identifies a shift |
| **TimeOffRequest** | (UserId, StartDate, EndDate) | One time-off request per user per date range |
| **Chore** | (UserId, Date, Title) | One chore per user per day per title |
| **OnDuty** | (UserId, Date) | One on-duty record per user per day |

#### Implementation Highlights

**Archive Validation**:
```csharp
// 1. Read manifest.json
var manifestEntry = zipArchive.GetEntry("manifest.json");
var metadata = JsonSerializer.Deserialize<ArchiveMetadata>(manifestJson);

// 2. Validate company match
var currentCompany = await _db.Companies.FindAsync(_tenantResolver.GetCurrentTenantId());
result.CompanyMatch = currentCompany?.Id == metadata.CompanyId;

// 3. Extract referenced users and shift types from NDJSON
var referencedUserEmails = new HashSet<string>();
var referencedShiftTypeKeys = new HashSet<string>();

string? line;
while ((line = await dataReader.ReadLineAsync()) != null)
{
    var record = JsonSerializer.Deserialize<NdjsonRecord>(line);
    // Extract emails and shift type keys from data
}

// 4. Check for missing users
var existingUserEmails = await _db.Users
    .Where(u => referencedUserEmails.Contains(u.Email))
    .Select(u => u.Email)
    .ToListAsync();

result.MissingUsers = referencedUserEmails.Except(existingUserEmails).ToList();
```

**Duplicate Detection** (ShiftInstance example):
```csharp
// Pre-load lookups for performance
var shiftTypeLookup = await _db.ShiftTypes
    .Where(st => st.CompanyId == companyId)
    .ToDictionaryAsync(st => st.Key, st => st.Id);

// Process NDJSON line-by-line
foreach (var line in ndjsonLines)
{
    var record = JsonSerializer.Deserialize<NdjsonRecord>(line);
    var workDate = DateOnly.Parse(dataElement.GetProperty("workDate").GetString());
    var shiftTypeKey = dataElement.GetProperty("shiftTypeKey").GetString();

    // Check for duplicate using natural key
    var exists = await _db.ShiftInstances.AnyAsync(si =>
        si.CompanyId == companyId &&
        si.WorkDate == workDate &&
        si.ShiftTypeId == shiftTypeLookup[shiftTypeKey]);

    if (exists)
    {
        if (request.ConflictPolicy == ImportConflictPolicy.SkipDuplicates)
        {
            stats["ShiftInstance"].Skipped++;
            continue;
        }
        else if (request.ConflictPolicy == ImportConflictPolicy.Fail)
        {
            throw new InvalidOperationException("Duplicate found");
        }
    }

    // Insert new record
    var shiftInstance = new ShiftInstance { /* ... */ };
    _db.ShiftInstances.Add(shiftInstance);
    stats["ShiftInstance"].Inserted++;
}
```

---

## Safety Mechanisms

### 1. Fresh Archive Enforcement

**Problem**: Prevent purge without backup.

**Solution**: Require a "fresh" archive before purge:

```csharp
// ArchiveService stores metadata in AuditLog
await _auditLogService.LogUserActionAsync(
    currentUserId,
    "ArchiveCreated",
    "DataArchive",
    null,
    $"Created archive before {request.CutoffDate}",
    JsonSerializer.Serialize(metadata)); // Metadata includes cutoffDate and types

// PurgeService validates fresh archive exists
var latestArchive = await _archiveService.GetLatestArchiveAsync();
if (latestArchive == null ||
    latestArchive.CutoffDate != request.CutoffDate ||
    latestArchive.Types != request.Types)
{
    return new PurgeResult { Success = false, Error = "No fresh archive found" };
}
```

**Fresh Definition**: Archive created with **exact same** cutoff date and data type selection.

---

### 2. Typed Confirmation

**Problem**: Prevent accidental click-through on purge button.

**Solution**: Require exact typed confirmation:

```csharp
var expected = $"DELETE {companyName.ToUpperInvariant()} BEFORE {cutoffDate:yyyy-MM-dd}";
// Example: "DELETE ACME CORP BEFORE 2025-07-01"

if (typedConfirmation.Trim() != expected)
{
    return new PurgeResult { Success = false, Error = $"Typed confirmation incorrect. Expected: {expected}" };
}
```

**UI Implementation**:
```html
<div class="alert alert-danger">
    <strong>Type the following to confirm:</strong>
    <pre>DELETE ACME CORP BEFORE 2025-07-01</pre>
</div>
<input type="text" asp-for="TypedConfirmation" class="form-control" />
```

---

### 3. Pre-Purge Database Backup

**Problem**: Purge is destructive, need recovery option.

**Solution**: Automatic backup before any deletion:

```csharp
var backupPath = await CreatePrePurgeBackupAsync();
result.BackupPath = backupPath;
// Example: C:\...\Backups\pre_purge_20260106_143022.db

// Backup pattern (async file copy)
using (var sourceStream = new FileStream(dbPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
using (var destinationStream = new FileStream(backupFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
{
    await sourceStream.CopyToAsync(destinationStream);
}
```

**Backup Location**: `Backups/pre_purge_{timestamp}.db`
**Recovery**: Replace `app.db` with backup file, restart application

---

### 4. Transaction Rollback

**Problem**: Partial deletion leaves database in inconsistent state.

**Solution**: All-or-nothing deletion using EF Core transactions:

```csharp
using var transaction = await _db.Database.BeginTransactionAsync();

try
{
    // Delete in FK-safe order
    await DeleteSwapRequestsAsync(...);      // 1. SwapRequest
    await DeleteTimeOffRequestsAsync(...);   // 2. TimeOffRequest
    await DeleteOfflineShiftsAsync(...);     // 2b. OFFLINE shifts
    await DeleteShiftsAsync(...);            // 3. ShiftInstance (cascades to ShiftAssignment)
    await DeleteChoresAsync(...);            // 4. Chore
    await DeleteOnDutyAsync(...);            // 5. OnDuty

    await _db.SaveChangesAsync();
    await transaction.CommitAsync();
}
catch (Exception ex)
{
    await transaction.RollbackAsync();
    _logger.LogError(ex, "Purge failed, rolled back");
    throw;
}
```

**Atomicity Guarantee**: Either all data is deleted, or none is.

---

### 5. Audit Logging

**Problem**: Need complete audit trail for compliance.

**Solution**: Log all operations with metadata:

```csharp
// Archive creation
await _auditLogService.LogUserActionAsync(
    currentUserId,
    "ArchiveCreated",
    "DataArchive",
    null,
    $"Created archive before {cutoffDate}",
    JsonSerializer.Serialize(metadata));

// Purge operation
await _auditLogService.LogUserActionAsync(
    currentUserId,
    "DataPurged",
    "DataArchive",
    null,
    $"Purged {totalDeleted} records before {cutoffDate}",
    JsonSerializer.Serialize(new
    {
        request.CutoffDate,
        request.Types,
        result.DeletedCounts,
        result.DbSizeBeforeBytes,
        result.DbSizeAfterBytes,
        SizeSavedBytes = result.DbSizeBeforeBytes - result.DbSizeAfterBytes
    }));

// Import operation
await _auditLogService.LogUserActionAsync(
    currentUserId,
    "DataImported",
    "DataArchive",
    null,
    $"Imported {totalInserted} records from archive",
    JsonSerializer.Serialize(result));
```

**Audit Events**:
- `ArchivePreviewGenerated`
- `ArchiveCreated`
- `ArchiveDownloaded`
- `DataPurged`
- `ArchiveValidated`
- `DataImported`

---

## Export Formats

### CSV ZIP Format (Human-Readable)

**Filename**: `archive_csv_{companyId}_{timestamp}.zip`
**Purpose**: Long-term archival, compliance, data analysis

**Structure**:
```
archive_csv_1_20260106_143022.zip
├── README.txt (metadata + instructions)
└── csv/
    ├── shift_instances.csv
    ├── shift_assignments.csv
    ├── swap_requests.csv
    ├── timeoff_requests.csv
    ├── chores.csv
    └── onduty.csv
```

**README.txt Example**:
```
ShiftManager Data Archive (CSV Format)

Company: Acme Corp (ID: 1)
Archive Created: 2026-01-06 14:30:22 UTC
Created By: admin@acme.com (User ID: 5)
Cutoff Date: 2025-07-01
Data Types: Shifts, TimeOff, Chores

RECORD COUNTS:
- Shift Instances: 1,250
- Shift Assignments: 3,400
- Time-Off Requests: 120
- Chores: 450
- On-Duty: 180

INSTRUCTIONS:
This archive is in CSV format for human readability and data analysis.
To re-import data into ShiftManager, use the NDJSON archive (archive_import_*.zip).

CSV FILES:
- shift_instances.csv: All shift instances before 2025-07-01
- shift_assignments.csv: User assignments to shifts
- swap_requests.csv: Shift swap requests
- timeoff_requests.csv: Time-off requests
- chores.csv: Daily chore assignments
- onduty.csv: On-duty scheduling records

NOTE: Foreign key relationships are preserved via email and shift type keys.
```

**CSV Header Examples**:

*shift_instances.csv*:
```csv
Id,CompanyId,ShiftTypeKey,WorkDate,Name,StaffingRequired,UpdatedAt
1,1,MORNING,2025-06-15,Morning Guard Duty,2,2025-06-01 08:00:00
2,1,EVENING,2025-06-15,Evening Patrol,3,2025-06-01 08:00:00
```

*shift_assignments.csv*:
```csv
Id,CompanyId,ShiftInstanceId,UserEmail,TraineeEmail,CreatedAt
1,1,1,john@acme.com,jane@acme.com,2025-06-05 10:00:00
2,1,1,bob@acme.com,,2025-06-05 10:00:00
```

*swap_requests.csv*:
```csv
Id,FromAssignmentId,ToAssignmentId,Status,CreatedAt,ReviewedAt
1,1,2,Approved,2025-06-10 14:00:00,2025-06-11 09:00:00
```

---

### NDJSON ZIP Format (Re-Importable)

**Filename**: `archive_import_{companyId}_{timestamp}.zip`
**Purpose**: Disaster recovery, data migration, re-import

**Structure**:
```
archive_import_1_20260106_143022.zip
├── manifest.json (metadata + SHA-256 hash)
└── data.ndjson (newline-delimited JSON)
```

**manifest.json Example**:
```json
{
  "schemaVersion": 1,
  "companyId": 1,
  "companyName": "Acme Corp",
  "cutoffDate": "2025-07-01",
  "types": 31,
  "createdAt": "2026-01-06T14:30:22Z",
  "createdBy": 5,
  "rowCounts": {
    "ShiftInstances": 1250,
    "ShiftAssignments": 3400,
    "SwapRequests": 85,
    "TimeOffRequests": 120,
    "Chores": 450,
    "OnDuty": 180
  },
  "sha256": "a3c5e7b9d4f6a1c8e2b5d7f9c3a6e8b1d4f7c9a2e5b8d1f4c7a9e2b5d8f1c4a7"
}
```

**data.ndjson Format**:

Each line is a JSON object with `type` and `data` fields:

```json
{"type":"ShiftInstance","data":{"id":1,"companyId":1,"shiftTypeKey":"MORNING","workDate":"2025-06-15","name":"Morning Guard Duty","staffingRequired":2,"updatedAt":"2025-06-01T08:00:00Z"}}
{"type":"ShiftAssignment","data":{"id":1,"companyId":1,"shiftInstanceId":1,"userEmail":"john@acme.com","traineeEmail":"jane@acme.com","createdAt":"2025-06-05T10:00:00Z"}}
{"type":"SwapRequest","data":{"id":1,"fromAssignmentId":1,"toAssignmentId":2,"status":"Approved","createdAt":"2025-06-10T14:00:00Z","reviewedAt":"2025-06-11T09:00:00Z"}}
```

**Natural Key Usage**:
- User references use `email` instead of `userId`
- ShiftType references use `shiftTypeKey` instead of `shiftTypeId`
- ShiftInstance composite key: `(companyId, workDate, shiftTypeKey)`

**Import Process**:
1. Read `manifest.json` to get metadata
2. Validate company match, schema version
3. Parse `data.ndjson` line-by-line (streaming, low memory)
4. Resolve natural keys to database IDs
5. Check for duplicates using natural keys
6. Insert records in dependency order

---

### SHA-256 Integrity Verification

**Purpose**: Ensure archive hasn't been tampered with during USB transfer.

**Generation** (ArchiveService.cs):
```csharp
private async Task<string> ComputeSha256Async(string zipPath)
{
    using var sha256 = SHA256.Create();
    using var stream = File.OpenRead(zipPath);
    var hashBytes = await sha256.ComputeHashAsync(stream);
    return Convert.ToHexString(hashBytes).ToLowerInvariant();
}
```

**Verification** (manual, in README):
```bash
# Windows PowerShell
Get-FileHash -Path archive_import_1_20260106_143022.zip -Algorithm SHA256

# Linux/Mac
sha256sum archive_import_1_20260106_143022.zip

# Compare with hash in manifest.json
```

---

## Database Deletion Order

### Critical: Foreign Key Safe Order

SQLite enforces foreign key constraints. Delete order must respect dependency graph:

```
┌─────────────────────────────────────────────────────────────┐
│                   FK-Safe Delete Order                       │
│                   (Purge Implementation)                     │
└─────────────────────────────────────────────────────────────┘

1. SwapRequest
   ├─ FK → ShiftAssignment (FromAssignmentId)
   ├─ FK → ShiftAssignment (ToAssignmentId)
   └─ Filter: FromAssignment.ShiftInstance.WorkDate < cutoffDate

2. TimeOffRequest
   ├─ No FK dependencies
   └─ Filter: EndDate < cutoffDate

3. OFFLINE ShiftInstance (auto-cleanup with TimeOff)
   ├─ FK ← ShiftAssignment (CASCADE DELETE)
   └─ Filter: ShiftTypeKey = "OFFLINE" AND WorkDate < cutoffDate

4. ShiftInstance
   ├─ FK ← ShiftAssignment (CASCADE DELETE)
   └─ Filter: WorkDate < cutoffDate (excluding OFFLINE)

5. Chore
   ├─ No FK dependencies
   └─ Filter: Date < cutoffDate

6. OnDuty
   ├─ No FK dependencies (GLOBAL table)
   └─ Filter: Date < cutoffDate AND UserId IN (company users)
```

### Implementation (PurgeService.cs)

```csharp
private async Task PurgeDataInOrderAsync(PurgeRequest request, int companyId, int currentUserId, PurgeResult result)
{
    // 1. SwapRequest (by FromAssignment.ShiftInstance.WorkDate < cutoff)
    if ((request.Types & ArchiveDataTypes.SwapRequests) != 0)
    {
        var deletedSwaps = await DeleteSwapRequestsAsync(companyId, request.CutoffDate);
        result.DeletedCounts["SwapRequests"] = deletedSwaps;
    }

    // 2. TimeOffRequest + OFFLINE shifts auto-cleanup
    if ((request.Types & ArchiveDataTypes.TimeOff) != 0)
    {
        var deletedTimeOff = await DeleteTimeOffRequestsAsync(companyId, request.CutoffDate);
        result.DeletedCounts["TimeOffRequests"] = deletedTimeOff;

        var deletedOffline = await DeleteOfflineShiftsAsync(companyId, request.CutoffDate);
        result.DeletedCounts["OfflineShifts"] = deletedOffline;
    }

    // 3. ShiftInstance (cascades to ShiftAssignment)
    if ((request.Types & ArchiveDataTypes.Shifts) != 0)
    {
        var (deletedShifts, deletedAssignments) = await DeleteShiftsAsync(companyId, request.CutoffDate);
        result.DeletedCounts["ShiftInstances"] = deletedShifts;
        result.DeletedCounts["ShiftAssignments"] = deletedAssignments;
    }

    // 4. Chore
    if ((request.Types & ArchiveDataTypes.Chores) != 0)
    {
        var deletedChores = await DeleteChoresAsync(companyId, request.CutoffDate);
        result.DeletedCounts["Chores"] = deletedChores;
    }

    // 5. OnDuty (soft or hard delete)
    if ((request.Types & ArchiveDataTypes.OnDuty) != 0)
    {
        var deletedOnDuty = await DeleteOnDutyAsync(companyId, currentUserId, request.CutoffDate, request.HardDeleteOnDuty);
        result.DeletedCounts["OnDuty"] = deletedOnDuty;
    }
}
```

### Deletion Method Examples

**SwapRequest Deletion** (complex FK traversal):
```csharp
private async Task<int> DeleteSwapRequestsAsync(int companyId, DateOnly cutoffDate)
{
    // Filter by FromAssignment.ShiftInstance.WorkDate < cutoff
    var swapRequestIds = await _db.SwapRequests
        .Include(sr => sr.FromAssignment)
        .ThenInclude(fa => fa.ShiftInstance)
        .Where(sr => sr.FromAssignment.CompanyId == companyId &&
                     sr.FromAssignment.ShiftInstance.WorkDate < cutoffDate)
        .Select(sr => sr.Id)
        .ToListAsync();

    if (swapRequestIds.Count == 0)
        return 0;

    var swapsToDelete = await _db.SwapRequests
        .Where(sr => swapRequestIds.Contains(sr.Id))
        .ToListAsync();

    _db.SwapRequests.RemoveRange(swapsToDelete);

    _logger.LogInformation("Marked {Count} SwapRequests for deletion", swapsToDelete.Count);
    return swapsToDelete.Count;
}
```

**ShiftInstance Deletion** (cascade counting):
```csharp
private async Task<(int shifts, int assignments)> DeleteShiftsAsync(int companyId, DateOnly cutoffDate)
{
    // Get OFFLINE shift type ID
    var offlineShiftTypeId = await _db.ShiftTypes
        .Where(st => st.CompanyId == companyId && st.Key == ShiftType.KEY_OFFLINE)
        .Select(st => st.Id)
        .FirstOrDefaultAsync();

    // Get ShiftInstances to delete (excluding OFFLINE - handled separately)
    var shiftsToDelete = await _db.ShiftInstances
        .Where(si => si.CompanyId == companyId &&
                     si.WorkDate < cutoffDate &&
                     si.ShiftTypeId != offlineShiftTypeId)
        .ToListAsync();

    // Count assignments that will be cascaded
    var shiftIds = shiftsToDelete.Select(si => si.Id).ToList();
    var assignmentCount = await _db.ShiftAssignments
        .Where(sa => shiftIds.Contains(sa.ShiftInstanceId))
        .CountAsync();

    _db.ShiftInstances.RemoveRange(shiftsToDelete);

    _logger.LogInformation("Marked {ShiftCount} ShiftInstances and {AssignmentCount} ShiftAssignments for deletion (cascade)",
        shiftsToDelete.Count, assignmentCount);

    return (shiftsToDelete.Count, assignmentCount);
}
```

**OnDuty Deletion** (soft vs. hard delete):
```csharp
private async Task<int> DeleteOnDutyAsync(int companyId, int currentUserId, DateOnly cutoffDate, bool hardDelete)
{
    // OnDuty is GLOBAL table - must scope by UserId IN companyUserIds
    var companyUserIds = await _db.Users
        .Where(u => u.CompanyId == companyId)
        .Select(u => u.Id)
        .ToListAsync();

    var onDutyToDelete = await _db.OnDuties
        .Where(od => companyUserIds.Contains(od.UserId) && od.Date < cutoffDate)
        .ToListAsync();

    if (hardDelete)
    {
        _db.OnDuties.RemoveRange(onDutyToDelete);
        _logger.LogInformation("Marked {Count} OnDuty records for HARD deletion", onDutyToDelete.Count);
    }
    else
    {
        // Soft delete
        foreach (var od in onDutyToDelete)
        {
            od.CanceledAt = DateTime.UtcNow;
            od.CanceledBy = currentUserId;
        }
        _logger.LogInformation("Marked {Count} OnDuty records for SOFT deletion", onDutyToDelete.Count);
    }

    return onDutyToDelete.Count;
}
```

### Critical Edge Cases

**OnDuty Global Table**:
- OnDuty table has NO `CompanyId` column (cross-company visibility)
- Must scope deletion by `UserId IN (companyUserIds)`
- Prevents accidental deletion of other companies' on-duty records

**OFFLINE Shift Auto-Cleanup**:
- When purging TimeOff, also purge associated OFFLINE shifts
- OFFLINE shifts are created automatically when time-off is approved
- Prevents orphaned OFFLINE shifts after time-off purge

**Cascade Deletion**:
- ShiftInstance → ShiftAssignment (CASCADE DELETE configured in EF Core)
- When ShiftInstance is deleted, all ShiftAssignments are automatically deleted
- Must count assignments separately before deletion (for reporting)

---

## User Interface

### Single Page, Three Tabs

**Page**: `/Owner/DataLifecycle`
**Authorization**: `[Authorize(Policy = "IsAdmin")]` (Owner only)
**Layout Pattern**: Bootstrap 5 nav-tabs (same as EmailConfig.cshtml)

```
┌─────────────────────────────────────────────────────────────┐
│  ShiftManager - Data Lifecycle                               │
├─────────────────────────────────────────────────────────────┤
│  [📦 Create Archive] [🗑️ Purge Data] [📥 Re-Import Archive] │
├─────────────────────────────────────────────────────────────┤
│                                                               │
│  Tab Content Here                                             │
│                                                               │
└─────────────────────────────────────────────────────────────┘
```

### Tab 1: Create Archive

**UI Elements**:
```html
<form method="post" asp-page-handler="CreateArchive">
    <!-- Cutoff Date -->
    <div class="form-group">
        <label asp-for="ArchiveCutoffDate">Cutoff Date</label>
        <input type="date" asp-for="ArchiveCutoffDate" class="form-control" />
        <small class="form-text text-muted">Archive data before this date (default: 6 months ago)</small>
    </div>

    <!-- Data Type Selection -->
    <div class="form-group">
        <label>Data Types to Archive</label>
        <div class="form-check">
            <input type="checkbox" asp-for="IncludeShifts" class="form-check-input" />
            <label asp-for="IncludeShifts" class="form-check-label">Shifts & Assignments</label>
        </div>
        <div class="form-check">
            <input type="checkbox" asp-for="IncludeSwapRequests" class="form-check-input" />
            <label asp-for="IncludeSwapRequests" class="form-check-label">Swap Requests</label>
        </div>
        <div class="form-check">
            <input type="checkbox" asp-for="IncludeTimeOff" class="form-check-input" />
            <label asp-for="IncludeTimeOff" class="form-check-label">Time-Off Requests</label>
        </div>
        <div class="form-check">
            <input type="checkbox" asp-for="IncludeChores" class="form-check-input" />
            <label asp-for="IncludeChores" class="form-check-label">Chores</label>
        </div>
        <div class="form-check">
            <input type="checkbox" asp-for="IncludeOnDuty" class="form-check-input" />
            <label asp-for="IncludeOnDuty" class="form-check-label">On-Duty</label>
        </div>
    </div>

    <!-- Preview Button -->
    <button type="submit" asp-page-handler="Preview" class="btn btn-secondary">Preview Counts</button>

    <!-- Create Archive Button -->
    <button type="submit" class="btn btn-primary">Create Archive</button>
</form>

<!-- Preview Results (if available) -->
@if (Model.Preview != null)
{
    <div class="alert alert-info">
        <h5>Preview Results</h5>
        <ul>
            <li>Shift Instances: @Model.Preview.Counts["ShiftInstances"]</li>
            <li>Shift Assignments: @Model.Preview.Counts["ShiftAssignments"]</li>
            <li>Swap Requests: @Model.Preview.Counts["SwapRequests"]</li>
            <li>Time-Off Requests: @Model.Preview.Counts["TimeOffRequests"]</li>
            <li>Chores: @Model.Preview.Counts["Chores"]</li>
            <li>On-Duty: @Model.Preview.Counts["OnDuty"]</li>
        </ul>
        <p><strong>Total Records:</strong> @Model.Preview.Counts.Values.Sum()</p>
        <p><strong>Estimated Size:</strong> @Model.FormatBytes(Model.Preview.EstimatedSizeBytes)</p>
    </div>
}

<!-- Download Links (if archive created) -->
@if (Model.LastArchive != null && Model.LastArchive.Success)
{
    <div class="alert alert-success">
        <h5>Archive Created Successfully!</h5>
        <p>
            <a href="@Url.Page("/Owner/DataLifecycle", "DownloadCsv")" class="btn btn-success">
                Download CSV Archive (@Model.FormatBytes(Model.LastArchive.CsvZipSizeBytes))
            </a>
            <a href="@Url.Page("/Owner/DataLifecycle", "DownloadNdjson")" class="btn btn-success">
                Download NDJSON Archive (@Model.FormatBytes(Model.LastArchive.NdjsonZipSizeBytes))
            </a>
        </p>
        <p><strong>SHA-256 Hash:</strong> <code>@Model.LastArchive.Sha256Hash</code></p>
    </div>
}
```

### Tab 2: Purge Data

**UI Elements** (safety-focused):
```html
<form method="post" asp-page-handler="Purge">
    <!-- Fresh Archive Indicator -->
    @if (Model.HasFreshArchive)
    {
        <div class="alert alert-success">
            ✅ Fresh archive exists for this cutoff date and selection
        </div>
    }
    else
    {
        <div class="alert alert-danger">
            ⚠️ No fresh archive found. You must create an archive before purging.
        </div>
    }

    <!-- Cutoff Date -->
    <div class="form-group">
        <label asp-for="PurgeCutoffDate">Cutoff Date</label>
        <input type="date" asp-for="PurgeCutoffDate" class="form-control" />
    </div>

    <!-- Data Type Selection (same as archive tab) -->
    <!-- ... -->

    <!-- Safety Checkboxes -->
    <div class="form-group">
        <div class="form-check">
            <input type="checkbox" asp-for="ArchiveConfirmed" class="form-check-input" required />
            <label asp-for="ArchiveConfirmed" class="form-check-label">
                <strong>I confirm that I have exported/archived this data</strong>
            </label>
        </div>
        <div class="form-check">
            <input type="checkbox" asp-for="RunVacuum" class="form-check-input" checked />
            <label asp-for="RunVacuum" class="form-check-label">
                Run VACUUM after purge (reclaim disk space)
            </label>
        </div>
        <div class="form-check">
            <input type="checkbox" asp-for="HardDeleteOnDuty" class="form-check-input" />
            <label asp-for="HardDeleteOnDuty" class="form-check-label">
                Hard delete On-Duty records (default: soft delete)
            </label>
        </div>
    </div>

    <!-- Typed Confirmation -->
    <div class="form-group">
        <label>Typed Confirmation</label>
        <div class="alert alert-danger">
            <strong>Type the following to confirm:</strong>
            <pre>@Model.ExpectedConfirmation</pre>
        </div>
        <input type="text" asp-for="TypedConfirmation" class="form-control" autocomplete="off" />
    </div>

    <!-- Purge Button (disabled if no fresh archive) -->
    <button type="submit" class="btn btn-danger" disabled="@(!Model.HasFreshArchive)">
        Purge Data
    </button>
</form>

<!-- Purge Results -->
@if (Model.LastPurgeResult != null && Model.LastPurgeResult.Success)
{
    <div class="alert alert-success">
        <h5>Purge Completed!</h5>
        <p><strong>Deleted Records:</strong></p>
        <ul>
            @foreach (var kvp in Model.LastPurgeResult.DeletedCounts)
            {
                <li>@kvp.Key: @kvp.Value</li>
            }
        </ul>
        <p><strong>Total Deleted:</strong> @Model.LastPurgeResult.DeletedCounts.Values.Sum()</p>
        <p><strong>Database Size Before:</strong> @Model.FormatBytes(Model.LastPurgeResult.DbSizeBeforeBytes)</p>
        <p><strong>Database Size After:</strong> @Model.FormatBytes(Model.LastPurgeResult.DbSizeAfterBytes)</p>
        <p><strong>Space Saved:</strong> @Model.FormatBytes(Model.LastPurgeResult.DbSizeBeforeBytes - Model.LastPurgeResult.DbSizeAfterBytes)</p>
        <p><strong>Pre-Purge Backup:</strong> <code>@Path.GetFileName(Model.LastPurgeResult.BackupPath)</code></p>
    </div>
}
```

### Tab 3: Re-Import Archive

**UI Elements**:
```html
<form method="post" asp-page-handler="ValidateArchive" enctype="multipart/form-data">
    <!-- File Upload -->
    <div class="form-group">
        <label asp-for="UploadedArchive">Select Archive ZIP</label>
        <input type="file" asp-for="UploadedArchive" class="form-control" accept=".zip" />
        <small class="form-text text-muted">Upload NDJSON archive (archive_import_*.zip)</small>
    </div>

    <!-- Validate Button -->
    <button type="submit" class="btn btn-secondary">Validate Archive</button>
</form>

<!-- Validation Results -->
@if (Model.ValidationResult != null)
{
    @if (Model.ValidationResult.IsValid)
    {
        <div class="alert alert-success">
            ✅ Archive validated successfully!
            <ul>
                <li>Company Match: @Model.ValidationResult.CompanyMatch</li>
                <li>Row Count: @Model.ValidationResult.RowCount</li>
                @if (Model.ValidationResult.Warnings.Count > 0)
                {
                    <li>Warnings: @Model.ValidationResult.Warnings.Count</li>
                    <ul>
                        @foreach (var warning in Model.ValidationResult.Warnings)
                        {
                            <li>@warning</li>
                        }
                    </ul>
                }
            </ul>
        </div>

        <!-- Import Form (only shown after successful validation) -->
        <form method="post" asp-page-handler="Import">
            <div class="form-group">
                <label>Conflict Policy</label>
                <select asp-for="ConflictPolicy" class="form-control">
                    <option value="SkipDuplicates">Skip Duplicates (Recommended)</option>
                    <option value="Overwrite">Overwrite Existing</option>
                    <option value="Fail">Fail on Duplicate</option>
                </select>
            </div>

            @if (Model.ValidationResult.MissingUsers.Count > 0)
            {
                <div class="alert alert-warning">
                    <strong>Missing Users Found:</strong>
                    <ul>
                        @foreach (var email in Model.ValidationResult.MissingUsers)
                        {
                            <li>@email</li>
                        }
                    </ul>
                    <div class="form-check">
                        <input type="checkbox" asp-for="SkipMissingUsers" class="form-check-input" />
                        <label asp-for="SkipMissingUsers" class="form-check-label">
                            Skip records with missing users
                        </label>
                    </div>
                </div>
            }

            <button type="submit" class="btn btn-primary">Import Archive</button>
        </form>
    }
    else
    {
        <div class="alert alert-danger">
            ❌ Archive validation failed:
            <ul>
                @foreach (var error in Model.ValidationResult.Errors)
                {
                    <li>@error</li>
                }
            </ul>
        </div>
    }
}

<!-- Import Results -->
@if (Model.LastImportResult != null && Model.LastImportResult.Success)
{
    <div class="alert alert-success">
        <h5>Import Completed!</h5>
        <p><strong>Import Statistics:</strong></p>
        <table class="table table-sm">
            <thead>
                <tr>
                    <th>Entity Type</th>
                    <th>Inserted</th>
                    <th>Skipped</th>
                    <th>Failed</th>
                </tr>
            </thead>
            <tbody>
                @foreach (var kvp in Model.LastImportResult.Stats)
                {
                    <tr>
                        <td>@kvp.Key</td>
                        <td>@kvp.Value.Inserted</td>
                        <td>@kvp.Value.Skipped</td>
                        <td>@kvp.Value.Failed</td>
                    </tr>
                }
            </tbody>
        </table>
        <p><strong>Total Inserted:</strong> @Model.LastImportResult.Stats.Values.Sum(s => s.Inserted)</p>
        <p><strong>Duration:</strong> @Model.LastImportResult.Duration.TotalSeconds.ToString("F1")s</p>
    </div>
}
```

### Tab Navigation (Active Tab Persistence)

```csharp
// DataLifecycle.cshtml.cs
public string ActiveTab { get; set; } = "archive";

public async Task OnGetAsync(string? tab = null)
{
    ActiveTab = tab ?? "archive";
    // ... load data
}

public async Task<IActionResult> OnPostCreateArchiveAsync()
{
    // ... create archive
    ActiveTab = "archive";
    await OnGetAsync();
    return Page();
}
```

```html
<!-- Razor Page -->
<ul class="nav nav-tabs">
    <li class="nav-item">
        <a class="nav-link @(Model.ActiveTab == "archive" ? "active" : "")"
           asp-page="/Owner/DataLifecycle" asp-route-tab="archive">
            📦 Create Archive
        </a>
    </li>
    <li class="nav-item">
        <a class="nav-link @(Model.ActiveTab == "purge" ? "active" : "")"
           asp-page="/Owner/DataLifecycle" asp-route-tab="purge">
            🗑️ Purge Data
        </a>
    </li>
    <li class="nav-item">
        <a class="nav-link @(Model.ActiveTab == "import" ? "active" : "")"
           asp-page="/Owner/DataLifecycle" asp-route-tab="import">
            📥 Re-Import Archive
        </a>
    </li>
</ul>
```

---

## Security & Authorization

### Owner-Only Access

```csharp
[Authorize(Policy = "IsAdmin")]
public class DataLifecycleModel : LocalizedPageModel
{
    // Only users with Role = Owner (0) can access this page
}
```

**Rationale**: Data purge is a destructive operation that should only be performed by system administrators (Owners).

**Alternative Considered**: Allow Director access
**Rejected**: Directors may have access to multiple companies; purge must be limited to single-company owners

### Multi-Tenancy Scoping

All operations are automatically scoped to current company via:

```csharp
var companyId = _tenantResolver.GetCurrentTenantId();

// Archive: Only export current company's data
var shifts = await _db.ShiftInstances
    .Where(si => si.CompanyId == companyId && si.WorkDate < cutoffDate)
    .ToListAsync();

// Purge: Only delete current company's data
var shiftsToDelete = await _db.ShiftInstances
    .Where(si => si.CompanyId == companyId && si.WorkDate < cutoffDate)
    .ToListAsync();

// Import: Only import into current company
shiftInstance.CompanyId = companyId;
```

**OnDuty Exception**: OnDuty table has NO `CompanyId`. Must scope by `UserId IN (companyUserIds)`:

```csharp
var companyUserIds = await _db.Users
    .Where(u => u.CompanyId == companyId)
    .Select(u => u.Id)
    .ToListAsync();

var onDutyToDelete = await _db.OnDuties
    .Where(od => companyUserIds.Contains(od.UserId) && od.Date < cutoffDate)
    .ToListAsync();
```

### File Access Control

**Archive Storage**: `Archives/` directory (created by ArchiveService)

```csharp
var archivesDir = Path.Combine(Directory.GetCurrentDirectory(), "Archives");
Directory.CreateDirectory(archivesDir);

var csvZipPath = Path.Combine(archivesDir, $"archive_csv_{companyId}_{timestamp}.zip");
```

**Backup Storage**: `Backups/` directory (created by PurgeService)

```csharp
var backupsDir = Path.Combine(Directory.GetCurrentDirectory(), "Backups");
Directory.CreateDirectory(backupsDir);

var backupPath = Path.Combine(backupsDir, $"pre_purge_{timestamp}.db");
```

**Upload Storage**: `Uploads/` directory (temp files, auto-deleted after import)

```csharp
var uploadsDir = Path.Combine(_environment.ContentRootPath, "Uploads");
Directory.CreateDirectory(uploadsDir);

var tempFilePath = Path.Combine(uploadsDir, $"temp_import_{timestamp}.zip");

// Auto-cleanup after import
try
{
    if (System.IO.File.Exists(tempFilePath))
    {
        System.IO.File.Delete(tempFilePath);
    }
}
catch (Exception cleanupEx)
{
    _logger.LogWarning(cleanupEx, "Failed to delete temporary import file");
}
```

**Security Considerations**:
- Archives stored in server file system (no user-specified paths)
- Download handlers validate file existence before serving
- No directory traversal vulnerability (all paths constructed programmatically)
- Temp files auto-deleted after import (no sensitive data leakage)

---

## File Storage Strategy

### Directory Structure

```
C:\Users\...\ShiftManager\
├── Archives\                                  (Created by ArchiveService)
│   ├── archive_csv_1_20260106_143022.zip
│   ├── archive_import_1_20260106_143022.zip
│   ├── archive_csv_1_20260206_100500.zip
│   └── ... (retention: manual cleanup, recommend 90 days)
├── Backups\                                   (Existing - used by PurgeService)
│   ├── pre_purge_20260106_143500.db
│   ├── pre_purge_20260206_101200.db
│   └── ... (retention: keep indefinitely, manual cleanup)
└── Uploads\                                   (Created by ImportService)
    ├── temp_import_20260106_144000.zip (auto-delete after import)
    └── ... (temp files only, auto-cleanup)
```

### File Retention Policy

| Directory | Purpose | Retention | Cleanup Method |
|-----------|---------|-----------|----------------|
| **Archives/** | Historical data exports | 90 days (recommended) | Manual (Owner deletes old archives) |
| **Backups/** | Pre-purge database backups | Indefinite | Manual (Owner manages disk space) |
| **Uploads/** | Temp import files | Auto-delete after import | Automatic (code-driven) |

### Disk Space Management

**Archive Size Estimation**:
- 1 year of data (500 shifts, 1000 assignments, 100 requests): ~2-5 MB per archive
- 5 companies × 12 archives/year = 60 archives = ~200 MB/year

**Backup Size**:
- Full database copy per purge
- Example: 50 MB database → 50 MB backup
- 12 backups/year = 600 MB/year

**Total Storage Requirement**: ~1 GB/year for 5 companies

### Air-Gapped Transfer Strategy

**Scenario**: Create archive on server, transfer to external storage via USB

**Steps**:
1. Owner creates archive in `/Owner/DataLifecycle`
2. Owner downloads CSV + NDJSON ZIPs
3. Owner copies ZIPs to USB drive
4. Owner stores USB in secure location (compliance)
5. Optional: Copy ZIPs to external storage server (air-gapped network)

**Re-Import Scenario**:
1. Locate archive ZIP on USB/external storage
2. Copy ZIP to local machine
3. Upload ZIP via `/Owner/DataLifecycle` import tab
4. Validate + import

---

## Testing Strategy

### Unit Tests (xUnit + Moq)

**ArchiveService Tests** (`ShiftManager.Tests/Services/ArchiveServiceTests.cs`):

```csharp
[Fact]
public async Task PreviewArchiveAsync_ShouldReturnCorrectCounts()
{
    // Arrange
    var mockDb = CreateMockDbContext();
    var service = new ArchiveService(mockDb, ...);

    // Add test data
    mockDb.ShiftInstances.Add(new ShiftInstance { WorkDate = new DateOnly(2025, 6, 15), ... });
    await mockDb.SaveChangesAsync();

    // Act
    var preview = await service.PreviewArchiveAsync(new DateOnly(2025, 7, 1), ArchiveDataTypes.Shifts);

    // Assert
    preview.Counts["ShiftInstances"].Should().Be(1);
}

[Fact]
public async Task CreateArchiveAsync_ShouldGenerateValidZipFiles()
{
    // Arrange
    var service = new ArchiveService(...);
    var request = new ArchiveRequest { CutoffDate = new DateOnly(2025, 7, 1), Types = ArchiveDataTypes.All };

    // Act
    var result = await service.CreateArchiveAsync(request);

    // Assert
    result.Success.Should().BeTrue();
    File.Exists(result.CsvZipPath).Should().BeTrue();
    File.Exists(result.NdjsonZipPath).Should().BeTrue();
    result.Sha256Hash.Should().NotBeNullOrEmpty();
}

[Fact]
public async Task ValidateFreshArchiveAsync_ShouldReturnTrueForMatchingArchive()
{
    // Arrange
    var mockAuditLogService = new Mock<IAuditLogService>();
    mockAuditLogService.Setup(s => s.GetLatestAuditLogAsync(...))
        .ReturnsAsync(new AuditLog
        {
            Action = "ArchiveCreated",
            Details = JsonSerializer.Serialize(new ArchiveMetadata { CutoffDate = new DateOnly(2025, 7, 1), Types = ArchiveDataTypes.Shifts })
        });

    var service = new ArchiveService(..., mockAuditLogService.Object);

    // Act
    var isValid = await service.ValidateFreshArchiveAsync(new DateOnly(2025, 7, 1), ArchiveDataTypes.Shifts);

    // Assert
    isValid.Should().BeTrue();
}
```

**PurgeService Tests** (`ShiftManager.Tests/Services/PurgeServiceTests.cs`):

```csharp
[Fact]
public void ValidateConfirmation_ShouldReturnTrueForExactMatch()
{
    // Arrange
    var service = new PurgeService(...);
    var companyName = "Acme Corp";
    var cutoffDate = new DateOnly(2025, 7, 1);
    var confirmation = "DELETE ACME CORP BEFORE 2025-07-01";

    // Act
    var isValid = service.ValidateConfirmation(confirmation, companyName, cutoffDate);

    // Assert
    isValid.Should().BeTrue();
}

[Fact]
public async Task PurgeDataAsync_ShouldRollbackOnError()
{
    // Arrange
    var mockDb = CreateMockDbContext();
    mockDb.SaveChangesAsync().Throws(new Exception("Database error"));

    var service = new PurgeService(mockDb, ...);
    var request = new PurgeRequest { ... };

    // Act
    var result = await service.PurgeDataAsync(request);

    // Assert
    result.Success.Should().BeFalse();
    result.Error.Should().Contain("Database error");
}

[Fact]
public async Task PurgeDataAsync_ShouldDeleteInCorrectOrder()
{
    // Arrange
    var deletionOrder = new List<string>();
    var mockDb = CreateMockDbContextWithOrderTracking(deletionOrder);

    var service = new PurgeService(mockDb, ...);

    // Act
    await service.PurgeDataAsync(new PurgeRequest { Types = ArchiveDataTypes.All, ... });

    // Assert
    deletionOrder.Should().ContainInOrder(
        "SwapRequests",
        "TimeOffRequests",
        "OfflineShifts",
        "ShiftInstances",
        "Chores",
        "OnDuty"
    );
}
```

**ImportService Tests** (`ShiftManager.Tests/Services/ImportServiceTests.cs`):

```csharp
[Fact]
public async Task ValidateArchiveAsync_ShouldDetectMissingUsers()
{
    // Arrange
    var mockDb = CreateMockDbContext();
    mockDb.Users.Add(new AppUser { Email = "existing@acme.com" });

    var zipPath = CreateTestArchiveWithUser("missing@acme.com");
    var service = new ImportService(mockDb, ...);

    // Act
    var result = await service.ValidateArchiveAsync(zipPath);

    // Assert
    result.MissingUsers.Should().Contain("missing@acme.com");
}

[Fact]
public async Task ImportArchiveAsync_ShouldSkipDuplicates()
{
    // Arrange
    var mockDb = CreateMockDbContext();
    mockDb.ShiftInstances.Add(new ShiftInstance { WorkDate = new DateOnly(2025, 6, 15), ShiftType = new ShiftType { Key = "MORNING" } });

    var zipPath = CreateTestArchiveWithShift(new DateOnly(2025, 6, 15), "MORNING");
    var service = new ImportService(mockDb, ...);

    var request = new ImportRequest { ZipPath = zipPath, ConflictPolicy = ImportConflictPolicy.SkipDuplicates };

    // Act
    var result = await service.ImportArchiveAsync(request);

    // Assert
    result.Stats["ShiftInstance"].Skipped.Should().Be(1);
    result.Stats["ShiftInstance"].Inserted.Should().Be(0);
}
```

### Integration Tests

**Full Roundtrip Test** (`ShiftManager.Tests/Integration/DataLifecycleIntegrationTests.cs`):

```csharp
[Fact]
public async Task FullRoundtrip_ArchivePurgeImport_ShouldRestoreData()
{
    // Arrange
    var factory = new WebApplicationFactory<Program>();
    var client = factory.CreateClient();
    var db = factory.Services.GetRequiredService<AppDbContext>();

    // Seed data
    db.ShiftInstances.Add(new ShiftInstance { Id = 1, WorkDate = new DateOnly(2025, 6, 15), ... });
    await db.SaveChangesAsync();

    // Act 1: Archive
    var archiveRequest = new ArchiveRequest { CutoffDate = new DateOnly(2025, 7, 1), Types = ArchiveDataTypes.Shifts };
    var archiveService = factory.Services.GetRequiredService<IArchiveService>();
    var archiveResult = await archiveService.CreateArchiveAsync(archiveRequest);

    // Act 2: Purge
    var purgeRequest = new PurgeRequest { CutoffDate = new DateOnly(2025, 7, 1), Types = ArchiveDataTypes.Shifts, ... };
    var purgeService = factory.Services.GetRequiredService<IPurgeService>();
    await purgeService.PurgeDataAsync(purgeRequest);

    // Verify purge
    var shiftsAfterPurge = await db.ShiftInstances.CountAsync();
    shiftsAfterPurge.Should().Be(0);

    // Act 3: Import
    var importRequest = new ImportRequest { ZipPath = archiveResult.NdjsonZipPath, ConflictPolicy = ImportConflictPolicy.SkipDuplicates };
    var importService = factory.Services.GetRequiredService<IImportService>();
    await importService.ImportArchiveAsync(importRequest);

    // Assert: Data restored
    var shiftsAfterImport = await db.ShiftInstances.CountAsync();
    shiftsAfterImport.Should().Be(1);

    var restoredShift = await db.ShiftInstances.FirstAsync();
    restoredShift.WorkDate.Should().Be(new DateOnly(2025, 6, 15));
}
```

### Manual Testing Checklist

**Archive Tab**:
- [ ] Preview counts match database queries
- [ ] CSV ZIP downloads successfully
- [ ] NDJSON ZIP downloads successfully
- [ ] CSV files are human-readable (open in Excel)
- [ ] NDJSON file has correct format (one JSON per line)
- [ ] SHA-256 hash matches manual verification
- [ ] All data types (Shifts, TimeOff, Chores, OnDuty) export correctly
- [ ] User emails appear in CSV (not user IDs)
- [ ] Shift type keys appear (not shift type IDs)

**Purge Tab**:
- [ ] Purge disabled if no fresh archive
- [ ] Typed confirmation validation works (case-sensitive, exact match)
- [ ] Pre-purge backup created before deletion
- [ ] Transaction rollback works on error
- [ ] FK-safe deletion order (no constraint violations)
- [ ] Cascade deletion (ShiftInstance → ShiftAssignment)
- [ ] OnDuty soft-delete works (CanceledAt set, record not deleted)
- [ ] OnDuty hard-delete works (record deleted)
- [ ] VACUUM reduces database file size
- [ ] Deleted counts accurate
- [ ] Database size metrics accurate

**Import Tab**:
- [ ] Archive validation detects company mismatch
- [ ] Archive validation detects missing users
- [ ] Archive validation detects missing shift types
- [ ] Missing users modal shows correct emails
- [ ] Skip duplicates policy works (existing records not updated)
- [ ] Overwrite policy works (existing records updated)
- [ ] Fail policy works (import aborts on first duplicate)
- [ ] Skip missing users works (records without users skipped)
- [ ] Import statistics accurate (inserted/skipped/failed counts)
- [ ] Temp file auto-deleted after import

**Multi-Tenancy**:
- [ ] Cannot export another company's data
- [ ] Cannot purge another company's data
- [ ] Cannot import into another company
- [ ] OnDuty scoped correctly by company users (not all on-duty records)

**Air-Gapped Workflow**:
- [ ] Create archive on development machine
- [ ] Download ZIPs, copy to USB drive
- [ ] Transfer USB to air-gapped machine
- [ ] Extract ZIPs, verify SHA-256 hash
- [ ] Import NDJSON archive
- [ ] Data restored correctly

---

## Usage Examples

### Example 1: Annual Data Archival

**Scenario**: IT administrator needs to archive 2024 data for compliance, then purge to improve performance.

**Steps**:

1. **Navigate to Data Lifecycle page**:
   ```
   Login as Owner → Owner menu → Data Lifecycle
   ```

2. **Create Archive** (Tab 1):
   ```
   Cutoff Date: 2025-01-01
   Data Types: ✅ All selected
   Click "Preview Counts"

   Preview Results:
   - Shift Instances: 1,250
   - Shift Assignments: 3,400
   - Swap Requests: 85
   - Time-Off Requests: 120
   - Chores: 450
   - On-Duty: 180
   Total: 5,485 records
   Estimated Size: 4.2 MB

   Click "Create Archive"

   Archive Created Successfully!
   Download CSV Archive (3.8 MB)
   Download NDJSON Archive (2.1 MB)
   SHA-256: a3c5e7b9d4f6a1c8e2b5d7f9c3a6e8b1...
   ```

3. **Save Archives to External Storage**:
   ```
   Copy both ZIPs to USB drive
   Store USB in secure location (compliance requirement)
   Optional: Copy to air-gapped backup server
   ```

4. **Purge Data** (Tab 2):
   ```
   Cutoff Date: 2025-01-01
   Data Types: ✅ All selected
   ✅ Fresh archive exists

   Checkboxes:
   ✅ I confirm that I have exported/archived this data
   ✅ Run VACUUM after purge
   ⬜ Hard delete On-Duty records

   Typed Confirmation:
   DELETE ACME CORP BEFORE 2025-01-01

   Click "Purge Data"

   Purge Completed!
   Deleted Records:
   - SwapRequests: 85
   - TimeOffRequests: 120
   - OfflineShifts: 120
   - ShiftInstances: 1,250
   - ShiftAssignments: 3,400
   - Chores: 450
   - OnDuty: 180 (soft-deleted)
   Total Deleted: 5,605

   Database Size Before: 142.5 MB
   Database Size After: 58.3 MB
   Space Saved: 84.2 MB

   Pre-Purge Backup: pre_purge_20260106_143500.db
   ```

5. **Verify Application Still Works**:
   ```
   Navigate to Calendar/Month
   Verify only 2025+ shifts visible
   Verify no broken functionality
   ```

---

### Example 2: Disaster Recovery

**Scenario**: Database corrupted after server crash, need to restore from archive.

**Steps**:

1. **Locate Latest Archive**:
   ```
   Find: archive_import_1_20260106_143022.zip (on USB or backup server)
   ```

2. **Stop Application**:
   ```
   Stop ShiftManager.exe (or Windows Service)
   ```

3. **Restore Database** (if partially corrupted):
   ```
   Option A: Delete app.db, restore from pre_purge_*.db backup
   Option B: Keep corrupted database, attempt import
   ```

4. **Start Application**:
   ```
   Start ShiftManager.exe
   Login as Owner
   ```

5. **Import Archive** (Tab 3):
   ```
   Select Archive ZIP: archive_import_1_20260106_143022.zip
   Click "Validate Archive"

   Validation Results:
   ✅ Archive validated successfully!
   - Company Match: True
   - Row Count: 5,485
   - Warnings: 0

   Conflict Policy: Skip Duplicates (Recommended)

   Click "Import Archive"

   Import Completed!
   Import Statistics:
   - ShiftInstance: Inserted 1,250 / Skipped 0 / Failed 0
   - ShiftAssignment: Inserted 3,400 / Skipped 0 / Failed 0
   - SwapRequest: Inserted 85 / Skipped 0 / Failed 0
   - TimeOffRequest: Inserted 120 / Skipped 0 / Failed 0
   - Chore: Inserted 450 / Skipped 0 / Failed 0
   - OnDuty: Inserted 180 / Skipped 0 / Failed 0
   Total Inserted: 5,485
   Duration: 12.3s
   ```

6. **Verify Data Restored**:
   ```
   Navigate to Calendar/Month
   Verify historical shifts visible
   Check user notifications, swap requests, etc.
   ```

---

### Example 3: Company Merger Data Migration

**Scenario**: Company A merges with Company B, need to migrate historical shift data.

**Steps**:

1. **Company A: Export Data**:
   ```
   Login as Owner (Company A)
   Navigate to Owner → Data Lifecycle
   Create Archive (all data types)
   Download NDJSON Archive
   ```

2. **Company B: Prepare Users**:
   ```
   Login as Owner (Company B)
   Navigate to Admin → Users
   Create users matching Company A emails:
   - john@companya.com → john@companya.com (same email)
   - jane@companya.com → jane@companya.com
   ```

3. **Company B: Create Matching Shift Types**:
   ```
   Navigate to Admin → Shift Types
   Create shift types matching Company A keys:
   - MORNING (same key as Company A)
   - EVENING
   ```

4. **Company B: Import Archive**:
   ```
   Navigate to Owner → Data Lifecycle → Import Tab
   Upload archive_import_1_*.zip
   Click "Validate Archive"

   Validation Results:
   ⚠️ Company Match: False (ID mismatch - expected)
   - Missing Users: 0 (all users created)
   - Missing Shift Types: 0 (all types created)

   Conflict Policy: Skip Duplicates

   Click "Import Archive"

   Import Completed!
   Total Inserted: 5,485
   ```

5. **Verify Merged Data**:
   ```
   Navigate to Calendar/Month
   Verify Company A's historical shifts visible
   Verify shift assignments mapped to correct users
   ```

---

## Implementation Details

### Service Registration (Program.cs)

```csharp
// Data Lifecycle Services
builder.Services.AddScoped<IArchiveService, ArchiveService>();
builder.Services.AddScoped<IPurgeService, PurgeService>();
builder.Services.AddScoped<IImportService, ImportService>();
```

**Lifetime**: `Scoped` (per HTTP request)
**Rationale**: Services access EF Core DbContext (scoped), must match lifetime

### Current User ID Retrieval Pattern

All three services use the same pattern to get the current user ID from HTTP context:

```csharp
private int GetCurrentUserId()
{
    var httpContext = _httpContextAccessor.HttpContext;
    if (httpContext == null)
    {
        return 0;
    }

    var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier);
    if (userIdClaim != null && int.TryParse(userIdClaim.Value, out int userId))
    {
        return userId;
    }

    return 0;
}
```

**Dependencies**:
- `IHttpContextAccessor` injected in constructor
- `using System.Security.Claims;`

### Navigation Property Fixes

During implementation, several model property corrections were required:

**ShiftAssignment**:
```csharp
// ❌ WRONG (old code)
sa.TraineeUser?.Email

// ✅ CORRECT (fixed)
sa.Trainee?.Email         // Navigation property name
sa.TraineeUserId          // Foreign key property name
```

**TimeOffRequest**:
```csharp
// ❌ WRONG (TimeOffRequest has no User navigation property)
tor.User?.Email

// ✅ CORRECT (use lookup dictionary)
var userEmailLookup = await _db.Users
    .Where(u => allUserIds.Contains(u.Id))
    .ToDictionaryAsync(u => u.Id, u => u.Email);

if (userEmailLookup.TryGetValue(tor.UserId, out var email))
{
    // Use email
}
```

**SwapRequest**:
```csharp
// ❌ WRONG (properties don't exist)
sr.CreatedBy
sr.ResolvedAt

// ✅ CORRECT (actual model properties)
// CreatedBy property doesn't exist
sr.ReviewedAt             // Not ResolvedAt
sr.Status                 // RequestStatus enum, not string
```

**Chore**:
```csharp
// ❌ WRONG
c.Description

// ✅ CORRECT
c.Title
c.CreatedBy               // int property
```

**OnDuty**:
```csharp
// ✅ CORRECT (has CreatedBy property)
od.CreatedBy              // int property
```

### Enum Conversion Pattern

**RequestStatus** (SwapRequest, TimeOffRequest):

```csharp
// Export (enum → string)
var statusString = swapRequest.Status.ToString();
// "Pending", "Approved", "Declined"

// Import (string → enum)
var statusString = dataElement.GetProperty("status").GetString()!;
var status = Enum.Parse<RequestStatus>(statusString);

swapRequest.Status = status;
```

---

## Design Decisions & Tradeoffs

### Decision 1: Owner-Only Access (Not Director)

**Decision**: Data Lifecycle page restricted to Owner role only.

**Rationale**:
- Purge is irreversible (even with backup, restoration is manual)
- Directors may have access to multiple companies (purge ambiguity)
- Archive/Purge should be performed by single-company administrators

**Alternative Considered**: Allow Director access with company selection.
**Rejected**: Too risky for multi-company scenarios.

---

### Decision 2: Two Archive Formats (CSV + NDJSON)

**Decision**: Generate both CSV (human-readable) and NDJSON (re-importable) archives.

**Rationale**:
- **CSV**: Compliance officers, auditors, data analysts need human-readable format (Excel)
- **NDJSON**: Disaster recovery, data migration need re-importable format

**Alternative Considered**: Single format (JSON or CSV).
**Rejected**: CSV loses data fidelity (relationships), JSON not human-friendly.

**Tradeoff**: Doubles archive creation time (~2x), doubles storage space.
**Mitigated**: Small dataset (5k records = 4 MB), archive creation is infrequent (quarterly/yearly).

---

### Decision 3: Fresh Archive Requirement

**Decision**: Require "fresh" archive (exact cutoff date + data type match) before purge.

**Rationale**:
- Prevents purge without backup
- Forces Owner to create archive as part of workflow
- Audit trail (ArchiveCreated event before DataPurged event)

**Alternative Considered**: Allow purge without archive (Owner assumes responsibility).
**Rejected**: Too risky, no backup guarantee.

**Implementation**: Store archive metadata in AuditLog, validate on purge.

---

### Decision 4: Typed Confirmation (Not Modal Dialog)

**Decision**: Require exact typed confirmation string instead of simple "Yes/No" modal.

**Rationale**:
- Prevents accidental click-through
- Forces Owner to read company name and cutoff date
- Industry standard for destructive operations (e.g., GitHub repository deletion)

**Example**: `DELETE ACME CORP BEFORE 2025-07-01`

**Alternative Considered**: Modal dialog with "Are you sure?" button.
**Rejected**: Too easy to click through without reading.

---

### Decision 5: OnDuty Soft-Delete Default

**Decision**: OnDuty records soft-deleted by default (hard-delete optional checkbox).

**Rationale**:
- OnDuty table is global (cross-company visibility)
- Soft-delete preserves history for compliance
- Hard-delete available for space-constrained environments

**Implementation**:
```csharp
if (request.HardDeleteOnDuty)
{
    _db.OnDuties.RemoveRange(onDutyToDelete);  // Hard delete
}
else
{
    foreach (var od in onDutyToDelete)
    {
        od.CanceledAt = DateTime.UtcNow;        // Soft delete
        od.CanceledBy = currentUserId;
    }
}
```

**Tradeoff**: Soft-delete doesn't reclaim space (VACUUM ineffective).
**Mitigated**: Checkbox allows Owner to choose.

---

### Decision 6: Natural Keys for Import (Not Database IDs)

**Decision**: Use natural keys (email, shift type key) for import instead of database IDs.

**Rationale**:
- Database IDs are auto-increment (different across systems)
- Email and shift type keys are stable, unique, portable
- Enables cross-system data migration (Company A → Company B)

**Natural Key Examples**:
- User: Email
- ShiftType: ShiftType.Key
- ShiftInstance: (CompanyId, WorkDate, ShiftTypeKey)

**Alternative Considered**: Use database IDs, require ID mapping file.
**Rejected**: Too complex, error-prone.

**Tradeoff**: Requires pre-import user creation (matching emails).
**Mitigated**: Validation step warns about missing users.

---

### Decision 7: VACUUM Outside Transaction

**Decision**: Run SQLite VACUUM after transaction commit (not inside).

**Rationale**:
- SQLite VACUUM cannot run inside transaction (technical limitation)
- VACUUM must run in separate connection

**Implementation**:
```csharp
await transaction.CommitAsync();

// VACUUM runs OUTSIDE transaction
if (request.RunVacuum)
{
    await _db.Database.ExecuteSqlRawAsync("VACUUM;");
}
```

**Risk**: If VACUUM fails, data is still deleted (committed).
**Mitigated**: VACUUM failure is non-critical (space not reclaimed, but data is safe).

---

### Decision 8: OFFLINE Shifts Auto-Cleanup

**Decision**: When purging TimeOff, also purge associated OFFLINE shifts.

**Rationale**:
- OFFLINE shifts are created automatically when time-off is approved
- OFFLINE shifts have no independent business value (markers only)
- Prevents orphaned OFFLINE shifts after time-off purge

**Implementation**:
```csharp
// Purge TimeOff
await DeleteTimeOffRequestsAsync(companyId, cutoffDate);

// Auto-cleanup OFFLINE shifts
await DeleteOfflineShiftsAsync(companyId, cutoffDate);
```

**Alternative Considered**: Purge OFFLINE shifts separately (require Shifts checkbox).
**Rejected**: Confusing for Owner (OFFLINE shifts not user-visible).

---

### Decision 9: SwapRequest Filtering by Shift WorkDate

**Decision**: Filter swap requests by `FromAssignment.ShiftInstance.WorkDate < cutoffDate` (not CreatedAt).

**Rationale**:
- Swap requests are tied to specific shifts (date-based)
- Filtering by CreatedAt could purge swaps for future shifts (data loss)

**Implementation**:
```csharp
var swapRequestIds = await _db.SwapRequests
    .Include(sr => sr.FromAssignment)
    .ThenInclude(fa => fa.ShiftInstance)
    .Where(sr => sr.FromAssignment.ShiftInstance.WorkDate < cutoffDate)
    .Select(sr => sr.Id)
    .ToListAsync();
```

**Alternative Considered**: Filter by CreatedAt.
**Rejected**: Logical inconsistency (purge swaps for active shifts).

---

### Decision 10: Pre-Purge Backup Stored in Backups/ (Not Archives/)

**Decision**: Store pre-purge database backups in `Backups/` directory (not `Archives/`).

**Rationale**:
- `Archives/` contains data exports (CSV/NDJSON ZIPs)
- `Backups/` contains database files (`.db` files)
- Separation of concerns (export vs. backup)

**Retention**:
- Archives: Manual cleanup (recommend 90 days)
- Backups: Keep indefinitely (manual cleanup)

**File Naming**:
- Archive: `archive_csv_1_20260106_143022.zip`
- Backup: `pre_purge_20260106_143500.db`

---

## Future Enhancements

### Priority 1: Scheduled Purge (Background Job)

**Problem**: Owner must manually purge data quarterly/yearly.

**Solution**: Background job that auto-purges data older than X days.

**Implementation**:
```csharp
public class AutoPurgeJob : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromDays(1), stoppingToken);

            // Check if auto-purge enabled (per company config)
            var companies = await _db.Companies.Where(c => c.Settings.AutoPurgeEnabled).ToListAsync();

            foreach (var company in companies)
            {
                var cutoffDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-company.Settings.AutoPurgeRetentionDays));

                // 1. Create archive
                await _archiveService.CreateArchiveAsync(new ArchiveRequest { CutoffDate = cutoffDate, Types = ArchiveDataTypes.All });

                // 2. Purge data
                await _purgeService.PurgeDataAsync(new PurgeRequest { CutoffDate = cutoffDate, Types = ArchiveDataTypes.All, ArchiveConfirmed = true });
            }
        }
    }
}
```

**Configuration** (per company):
```json
{
  "autoPurgeEnabled": true,
  "autoPurgeRetentionDays": 365,
  "autoPurgeTypes": 31  // ArchiveDataTypes.All
}
```

**Safety**: Still creates archive before purge, still creates pre-purge backup.

---

### Priority 2: Archive Compression Options

**Problem**: Large datasets (10k+ records) create large ZIP files (50+ MB).

**Solution**: Add compression level selection (Fastest, Optimal, Maximum).

**Implementation**:
```csharp
zipArchive = ZipFile.Open(zipPath, ZipArchiveMode.Create);

foreach (var entry in zipArchive.Entries)
{
    entry.CompressionLevel = request.CompressionLevel switch
    {
        CompressionLevel.Fastest => System.IO.Compression.CompressionLevel.Fastest,
        CompressionLevel.Optimal => System.IO.Compression.CompressionLevel.Optimal,
        CompressionLevel.Maximum => System.IO.Compression.CompressionLevel.SmallestSize,
        _ => System.IO.Compression.CompressionLevel.Optimal
    };
}
```

**UI**:
```html
<select asp-for="CompressionLevel">
    <option value="Fastest">Fastest (larger file)</option>
    <option value="Optimal" selected>Optimal (recommended)</option>
    <option value="Maximum">Maximum (smaller file, slower)</option>
</select>
```

**Tradeoff**: Maximum compression increases archive creation time (2-3x slower).

---

### Priority 3: Incremental Archives

**Problem**: Full archive every time is redundant (same old data).

**Solution**: Incremental archives (only new data since last archive).

**Implementation**:
```csharp
// Get last archive metadata
var lastArchive = await _archiveService.GetLatestArchiveAsync();

// Archive only data between last archive and current cutoff
var startDate = lastArchive?.CutoffDate ?? DateOnly.MinValue;
var endDate = request.CutoffDate;

var shifts = await _db.ShiftInstances
    .Where(si => si.WorkDate >= startDate && si.WorkDate < endDate)
    .ToListAsync();
```

**Manifest**:
```json
{
  "schemaVersion": 2,
  "archiveType": "incremental",
  "startDate": "2025-01-01",
  "endDate": "2025-07-01",
  "previousArchive": "archive_import_1_20260106_143022.zip"
}
```

**Import**: Restore all incremental archives in order.

**Complexity**: Import order validation, dependency tracking.

---

### Priority 4: Import Progress Bar

**Problem**: Large imports (10k+ records) take 30+ seconds, no feedback.

**Solution**: WebSocket-based progress updates.

**Implementation**:
```csharp
// ImportService.cs
public async Task<ImportResult> ImportArchiveAsync(ImportRequest request, IProgress<ImportProgress> progress)
{
    var totalRecords = GetTotalRecordsFromManifest(request.ZipPath);
    var processed = 0;

    foreach (var line in ndjsonLines)
    {
        // Import record
        processed++;

        // Report progress
        progress?.Report(new ImportProgress
        {
            TotalRecords = totalRecords,
            ProcessedRecords = processed,
            PercentComplete = (int)((processed / (double)totalRecords) * 100)
        });
    }
}
```

**UI** (JavaScript + SignalR):
```html
<div id="import-progress" style="display: none;">
    <div class="progress">
        <div class="progress-bar" id="import-progress-bar" role="progressbar" style="width: 0%">0%</div>
    </div>
    <p id="import-status">Importing 0 / 5,485 records...</p>
</div>

<script>
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/importHub")
    .build();

connection.on("ImportProgress", (progress) => {
    document.getElementById("import-progress-bar").style.width = progress.percentComplete + "%";
    document.getElementById("import-progress-bar").textContent = progress.percentComplete + "%";
    document.getElementById("import-status").textContent = `Importing ${progress.processedRecords} / ${progress.totalRecords} records...`;
});
</script>
```

---

### Priority 5: Archive Encryption

**Problem**: Archives contain sensitive data, stored unencrypted.

**Solution**: Encrypt archives with password (AES-256).

**Implementation**:
```csharp
// ArchiveService.cs
public async Task<ArchiveResult> CreateArchiveAsync(ArchiveRequest request)
{
    // Create unencrypted ZIP
    var zipPath = await CreateZipAsync(request);

    // Encrypt with password
    if (!string.IsNullOrEmpty(request.EncryptionPassword))
    {
        var encryptedZipPath = zipPath.Replace(".zip", ".encrypted.zip");
        await EncryptZipAsync(zipPath, encryptedZipPath, request.EncryptionPassword);
        File.Delete(zipPath);  // Delete unencrypted
        zipPath = encryptedZipPath;
    }

    return new ArchiveResult { CsvZipPath = zipPath, ... };
}

private async Task EncryptZipAsync(string sourcePath, string destPath, string password)
{
    using var aes = Aes.Create();
    aes.Key = DeriveKeyFromPassword(password);
    aes.GenerateIV();

    using var sourceStream = File.OpenRead(sourcePath);
    using var destStream = File.Create(destPath);

    // Write IV to start of file
    await destStream.WriteAsync(aes.IV, 0, aes.IV.Length);

    // Encrypt
    using var cryptoStream = new CryptoStream(destStream, aes.CreateEncryptor(), CryptoStreamMode.Write);
    await sourceStream.CopyToAsync(cryptoStream);
}
```

**UI**:
```html
<div class="form-group">
    <label asp-for="EncryptionPassword">Encryption Password (optional)</label>
    <input type="password" asp-for="EncryptionPassword" class="form-control" />
    <small class="form-text text-muted">Leave blank for no encryption. Use strong password (12+ chars).</small>
</div>
```

**Import**: Prompt for password, decrypt before import.

**Tradeoff**: Adds complexity, user must remember password (no recovery).

---

## Conclusion

The **Data Lifecycle Management** feature provides a comprehensive solution for historical data management in ShiftManager:

- **Archive**: Export data in two formats (CSV for humans, NDJSON for machines)
- **Purge**: Safely delete old data with multiple safety mechanisms
- **Import**: Restore data from archives with duplicate detection

**Key Strengths**:
- Defense-in-depth safety (fresh archive requirement, typed confirmation, pre-purge backup, transaction rollback)
- Multi-format exports (compliance + disaster recovery)
- Air-gapped friendly (USB transfer workflow)
- FK-safe deletion order (no constraint violations)
- Complete audit trail (all operations logged)

**Limitations**:
- Owner-only access (no delegation to Directors)
- Manual operation (no scheduled auto-purge)
- No encryption (sensitive data stored unencrypted)
- No incremental archives (full export every time)

**Recommended Workflow**:
1. Quarterly/yearly: Create archive, download both ZIPs, store on USB
2. Verify fresh archive exists, purge data with typed confirmation
3. Test application functionality after purge
4. Store pre-purge backup for emergency recovery

**For Future Development**: See [Future Enhancements](#future-enhancements) for roadmap.

---

**Document End**
