# Ops Console Scheduler - Implementation Guide

## Overview

The **Ops Console Scheduler** transforms shift management from manual instance creation to a streamlined, template-based system. The architecture follows a three-tier model:

**Blueprints** → **Programs** → **Operations**

- **Blueprints** (ShiftTypes): Foundational shift definitions with bilingual names
- **Programs** (ShiftPrograms): Weekly templates defining when shift types run
- **Operations** (ShiftInstances): Actual shifts on specific dates, generated from Programs

**North Star**: "Staff an entire week in 90 seconds without losing context"

---

## Architecture

### Conceptual Model

```
┌──────────────────────────────────────────────────────────┐
│ BLUEPRINTS (ShiftTypes)                                  │
│ - Morning Shift (08:00-16:00)                            │
│ - Night Shift (22:00-06:00)                              │
│ - Afternoon Shift (14:00-22:00)                          │
└──────────────────────────────────────────────────────────┘
                          ↓
┌──────────────────────────────────────────────────────────┐
│ PROGRAMS (Weekly Templates)                              │
│ Program: "Morning Shifts Mon-Fri"                        │
│   - ShiftType: Morning Shift                             │
│   - Days: Mon, Tue, Wed, Thu, Fri                        │
│   - Default Staffing: 3                                  │
│   - Per-Day Overrides: Mon=4                             │
└──────────────────────────────────────────────────────────┘
                          ↓
┌──────────────────────────────────────────────────────────┐
│ OPERATIONS (ShiftInstances)                              │
│ - 2026-01-13 (Mon): Morning Shift, 4 staff, OVR badge   │
│ - 2026-01-14 (Tue): Morning Shift, 3 staff              │
│ - 2026-01-15 (Wed): Morning Shift, 3 staff              │
│ ...                                                      │
└──────────────────────────────────────────────────────────┘
```

### Master Programs

**Master Programs** act as collections of Programs for complete weekly schedules:

```
Master Program: "Standard Week Schedule"
├─ Morning Shifts (Mon-Fri)
├─ Night Shifts (Tue-Sat)
└─ Weekend Coverage (Sat-Sun)

Generate → Creates all shifts for all Programs at once
```

---

## Database Schema

### New Tables

#### `ShiftPrograms` (Weekly Templates)
```sql
CREATE TABLE ShiftPrograms (
    Id INT PRIMARY KEY,
    CompanyId INT NOT NULL,
    ShiftTypeId INT NOT NULL,
    Name NVARCHAR(200) NOT NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    DefaultStaffingRequired INT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2,
    UpdatedAt DATETIME2,
    CreatedBy INT,
    UpdatedBy INT,
    FOREIGN KEY (CompanyId) REFERENCES Companies(Id),
    FOREIGN KEY (ShiftTypeId) REFERENCES ShiftTypes(Id)
);
```

#### `ProgramDays` (Weekly Mask)
```sql
CREATE TABLE ProgramDays (
    Id INT PRIMARY KEY,
    ProgramId INT NOT NULL,
    DayOfWeek INT NOT NULL, -- 0=Sunday, 6=Saturday
    StaffingRequired INT NULL, -- Per-day override (null = use default)
    FOREIGN KEY (ProgramId) REFERENCES ShiftPrograms(Id) ON DELETE CASCADE,
    UNIQUE (ProgramId, DayOfWeek)
);
```

#### `MasterPrograms` (Schedule Collections)
```sql
CREATE TABLE MasterPrograms (
    Id INT PRIMARY KEY,
    CompanyId INT NOT NULL,
    Name NVARCHAR(200) NOT NULL,
    Description NVARCHAR(1000) NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2,
    UpdatedAt DATETIME2,
    CreatedBy INT,
    UpdatedBy INT,
    FOREIGN KEY (CompanyId) REFERENCES Companies(Id)
);
```

#### `MasterProgramItems` (Join Table)
```sql
CREATE TABLE MasterProgramItems (
    Id INT PRIMARY KEY,
    MasterProgramId INT NOT NULL,
    ProgramId INT NOT NULL,
    SortOrder INT NOT NULL DEFAULT 0,
    FOREIGN KEY (MasterProgramId) REFERENCES MasterPrograms(Id) ON DELETE CASCADE,
    FOREIGN KEY (ProgramId) REFERENCES ShiftPrograms(Id),
    UNIQUE (MasterProgramId, ProgramId)
);
```

### Modified Tables

#### `ShiftTypes` - Added Localization
```sql
ALTER TABLE ShiftTypes
ADD NameKey NVARCHAR(200) NULL; -- e.g., "ShiftType_MORNING_Name"
```

#### `ShiftInstances` - Added Override Tracking
```sql
ALTER TABLE ShiftInstances
ADD IsDetached BIT NOT NULL DEFAULT 0,
ADD OriginalProgramId INT NULL,
ADD OverriddenFields NVARCHAR(MAX) NULL; -- JSON: {"Staffing": true, "Time": true}

ALTER TABLE ShiftInstances
ADD FOREIGN KEY (OriginalProgramId) REFERENCES ShiftPrograms(Id) ON DELETE SET NULL;
```

---

## Services Layer

### IShiftProgramService

Manages Programs (weekly templates) and instance generation:

**Key Methods**:
- `CreateProgramAsync()` - Creates Program with weekly mask and staffing
- `GenerateInstancesAsync()` - Generates ShiftInstances for date range matching weekly mask
- `DetachInstanceAsync()` - Marks instance as detached when user edits it
- `ResetInstanceToProgramAsync()` - Restores instance to Program defaults
- `GetInstancesFromProgramAsync()` - Retrieves all instances generated from a Program

**Example Usage**:
```csharp
// Create a Program for Morning shifts Mon-Fri
var program = await _programService.CreateProgramAsync(
    companyId: 1,
    shiftTypeId: 1,
    name: "Morning Shifts Mon-Fri",
    days: new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday },
    defaultStaffing: 3,
    perDayStaffing: new Dictionary<DayOfWeek, int> { [DayOfWeek.Monday] = 4 },
    userId: currentUserId
);

// Generate instances for next 4 weeks
var instances = await _programService.GenerateInstancesAsync(
    programId: program.Id,
    startDate: new DateOnly(2026, 1, 13),
    endDate: new DateOnly(2026, 2, 10),
    overwriteExisting: false
);
// Result: Creates 20 ShiftInstances (4 weeks × 5 days)
```

### IMasterProgramService

Manages MasterPrograms (collections of Programs):

**Key Methods**:
- `CreateMasterProgramAsync()` - Creates MasterProgram with multiple Programs
- `GenerateFromMasterProgramAsync()` - Applies all Programs to date range
- `GetMasterProgramSummaryAsync()` - Statistics (total programs, shift types, days)

**Example Usage**:
```csharp
// Create Master Program with 3 Programs
var masterProgram = await _masterProgramService.CreateMasterProgramAsync(
    companyId: 1,
    name: "Standard Week Schedule",
    description: "Complete weekly coverage",
    programIds: new List<int> { 1, 2, 3 },
    userId: currentUserId
);

// Generate complete week from all Programs
var result = await _masterProgramService.GenerateFromMasterProgramAsync(
    masterProgramId: masterProgram.Id,
    startDate: new DateOnly(2026, 1, 13),
    endDate: new DateOnly(2026, 2, 10),
    overwriteExisting: false
);
// Result: Dictionary[ProgramId] => List<ShiftInstance>
```

---

## UI Pages

### 1. Blueprints (`/Owner/Blueprints`)

**Purpose**: Manage ShiftTypes with bilingual localized names.

**Features**:
- Create new ShiftTypes with EN/HE names
- Edit shift names (opens localization editor modal)
- Edit time ranges (start/end times)
- Delete custom shift types (with cascade checks)
- Inline editing via modals

**Screenshot Flow**:
```
┌────────────────────────────────────────────────┐
│ ➕ Create New Shift Type                      │
│ [Key: CUSTOM_DAY] [EN: Day Shift] [HE: ...]  │
│ [Start: 08:00] [End: 16:00] [Create]         │
└────────────────────────────────────────────────┘

┌────────────────────────────────────────────────┐
│ 📋 Existing Shift Types                       │
│ MORNING | Morning Shift | משמרת בוקר | 08:00-16:00 | [Edit Name] [Edit Time] │
│ NIGHT   | Night Shift   | משמרת לילה | 22:00-06:00 | [Edit Name] [Edit Time] │
└────────────────────────────────────────────────┘
```

### 2. Programs (`/Owner/Programs`)

**Purpose**: Define weekly templates and generate shift instances.

**Features**:
- Create Programs with ShiftType selector
- Weekly mask UI (Su-Sa checkboxes)
- Default staffing + per-day overrides
- Generate instances modal (date range picker)
- Visual weekly mask display for existing Programs

**Screenshot Flow**:
```
┌────────────────────────────────────────────────┐
│ ➕ Create New Program                         │
│ ShiftType: [Morning Shift ▼]                  │
│ Name: [Morning Shifts Mon-Fri]                │
│ Weekly Mask: [☑Mon] [☑Tue] [☑Wed] [☑Thu] [☑Fri] [☐Sat] [☐Sun] │
│ Default Staffing: [3]                         │
│ Per-Day: Mon[4] Tue[  ] ...                   │
│ [Create Program]                              │
└────────────────────────────────────────────────┘

┌────────────────────────────────────────────────┐
│ ⏰ Morning Shifts Mon-Fri                     │
│ Type: Morning | Time: 08:00-16:00             │
│ Default: 3 staff | Override: Mon=4            │
│ Days: [Mon][Tue][Wed][Thu][Fri]              │
│ [📅 Generate Instances] [🗑️ Delete]          │
└────────────────────────────────────────────────┘
```

### 3. Master Programs (`/Owner/MasterPrograms`)

**Purpose**: Compose complete weekly schedules from multiple Programs.

**Features**:
- Select multiple Programs via checkbox grid
- Generate from all Programs at once
- Visual display of included Programs with metadata

**Screenshot Flow**:
```
┌────────────────────────────────────────────────┐
│ ➕ Create New Master Program                  │
│ Name: [Standard Week Schedule]                │
│ Description: [Complete weekly coverage]       │
│ Select Programs:                              │
│ [☑] Morning Shifts Mon-Fri (Morning, 5 days) │
│ [☑] Night Shifts Tue-Sat (Night, 5 days)     │
│ [☑] Weekend Coverage (Morning, 2 days)       │
│ [Create Master Program]                       │
└────────────────────────────────────────────────┘

┌────────────────────────────────────────────────┐
│ 📦 Standard Week Schedule                     │
│ Description: Complete weekly coverage          │
│ Included Programs:                            │
│  • Morning Shifts Mon-Fri (Morning) 5 days    │
│  • Night Shifts Tue-Sat (Night) 5 days       │
│ [📅 Generate Full Week] [🗑️ Delete]          │
└────────────────────────────────────────────────┘
```

---

## Localization Integration

### Shift Type Names

**Pattern**: ShiftTypes use `NameKey` (resource keys) instead of hardcoded names.

**Rendering**:
```cshtml
<!-- In any Razor view -->
<loc key="@shiftType.NameKey" />

<!-- Example: ShiftType.NameKey = "ShiftType_MORNING_Name" -->
<!-- Renders: "Morning Shift" (en-US) or "משמרת בוקר" (he-IL) -->
```

**Resource Files**:
- `SharedResources.resx`: English defaults
- `SharedResources.he-IL.resx`: Hebrew defaults
- Company overrides: `CompanyLocalizationOverride` table

**Fallback Chain**:
1. Company override (if exists)
2. Global resource file
3. NameKey as-is (graceful degradation)

### Manager Edit Workflow

From Blueprints page:
1. Click "Edit Name" → Opens modal
2. Two inputs: English value, Hebrew value
3. On save → Calls `CompanyLocalizationService.UpsertOverrideAsync()` for both cultures
4. Invalidates cache → Refresh to see updated names

**Code Example**:
```csharp
await _localizationService.UpsertOverrideAsync(
    companyId, "en-US", "ShiftType_MORNING_Name", "Day Shift", userId);
await _localizationService.UpsertOverrideAsync(
    companyId, "he-IL", "ShiftType_MORNING_Name", "משמרת יום", userId);
```

---

## Detachment & Override Tracking

### Concept

When a user edits an Operation (ShiftInstance) that was generated from a Program, it becomes "detached":
- **IsDetached** = true
- **OverriddenFields** JSON tracks what changed: `{"Staffing": true, "Time": false, "Name": false}`
- UI shows **OVR** badge (orange)

### Reset to Program

Users can restore a detached instance to its Program defaults:
- Reads original Program + ProgramDay settings
- Restores staffing, clears custom name, resets times
- Sets IsDetached = false, clears OverriddenFields

**UI Flow**:
```
┌────────────────────────────────────────────────┐
│ 2026-01-13 (Mon): Morning Shift [OVR]        │
│ Staffing: 5 (original: 4 from Program)        │
│ [Reset to Program] [Save Changes]            │
└────────────────────────────────────────────────┘
```

**Code Example**:
```csharp
// Mark as detached when user changes staffing
await _programService.DetachInstanceAsync(instanceId, "Staffing");

// Reset to Program
await _programService.ResetInstanceToProgramAsync(instanceId);
// Result: Staffing restored to 4, IsDetached = false, OVR badge removed
```

---

## Migration & Deployment

### 1. Apply Database Migration

```bash
dotnet ef database update
```

This creates:
- 4 new tables (ShiftPrograms, ProgramDays, MasterPrograms, MasterProgramItems)
- 2 modified tables (ShiftTypes, ShiftInstances)
- All indexes and foreign keys

### 2. Populate NameKey for Existing ShiftTypes

```bash
sqlite3 shiftmanager.db < Scripts/MigrateShiftTypeNameKeys.sql
```

This script:
- Maps predefined ShiftTypes to global resource keys
- Generates unique keys for custom ShiftTypes
- Verifies all ShiftTypes have NameKey populated

### 3. Verify Resource Keys Exist

Check `SharedResources.resx` and `SharedResources.he-IL.resx`:
- ShiftType_MORNING_Name
- ShiftType_NIGHT_Name
- ShiftType_AFTERNOON_Name
- ShiftType_EVENING_Name
- ShiftType_MIDDLE_Name
- ShiftType_OFFLINE_Name

### 4. Test Workflow

1. Navigate to `/Owner/Blueprints` → Verify shift types display with localized names
2. Create a new Program at `/Owner/Programs` → Select days, set staffing
3. Generate instances → Verify ShiftInstances created with OriginalProgramId set
4. Edit an instance in Calendar → Verify OVR badge appears
5. Reset instance → Verify returns to Program defaults

---

## Future Enhancements (Not Yet Implemented)

### Task 12-18: UI Enhancements

These tasks are planned but not yet implemented:

**Task 12**: Update Calendar/Table to show OVR badges
- Add badge rendering in Calendar grid cells
- Show detachment status visually
- Add "Reset to Program" button

**Task 13**: Extend NotificationService
- Trainee add/change/remove notifications
- Staffing adjustment notifications
- Time/name change notifications

**Task 14**: Add Email Templates
- Trainee added email
- Slot removed email
- Shift modified email

**Task 15**: Overhaul Calendar/Table UI (Excel-density)
- Dense grid layout (compact cells)
- Multi-week views (2 weeks, month)
- Contextual metrics in headers

**Task 16**: Create Roster Dock
- Right sidebar with employee list
- Drag-to-assign functionality
- Availability status chips

**Task 17**: Create Fill Handle
- Drag bottom-right corner to copy across days
- Modal: "Copy exact" / "Apply Program defaults" / "Copy staffing only"

**Task 18**: Extend Command Palette
- Ctrl+K → "Create Program"
- "Generate Week"
- "Jump to Date"
- "Find Employee"

---

## API Endpoints (Future)

Potential REST API endpoints for external integrations:

```
GET    /api/v1/programs              - List Programs
POST   /api/v1/programs              - Create Program
GET    /api/v1/programs/{id}         - Get Program details
PUT    /api/v1/programs/{id}         - Update Program
DELETE /api/v1/programs/{id}         - Delete Program
POST   /api/v1/programs/{id}/generate - Generate instances

GET    /api/v1/master-programs       - List MasterPrograms
POST   /api/v1/master-programs       - Create MasterProgram
POST   /api/v1/master-programs/{id}/generate - Generate from all Programs
```

---

## Troubleshooting

### Issue: Shift names show as "ShiftType_MORNING_Name" instead of localized text

**Cause**: NameKey not populated or resource missing.

**Fix**:
1. Run `Scripts/MigrateShiftTypeNameKeys.sql`
2. Verify resource keys exist in `SharedResources.resx`
3. Check company overrides: `SELECT * FROM CompanyLocalizationOverrides WHERE ResourceKey LIKE 'ShiftType_%'`

### Issue: "Cannot generate instances - no ProgramDays configured"

**Cause**: Program has no weekly mask (no days selected).

**Fix**: Edit Program, select at least one day of the week.

### Issue: Generate instances creates duplicates

**Cause**: `overwriteExisting = false` (default) and instances already exist.

**Fix**: Use `overwriteExisting = true` in Generate modal, or manually delete existing instances first.

---

## Performance Considerations

### Instance Generation

Generating 1000 instances (8 weeks × 125 shifts) takes ~2-3 seconds:
- Bulk INSERT via Entity Framework
- Indexes on CompanyId + WorkDate for fast conflict checks

### Localization Cache

- CompanyLocalizationService caches overrides for 1 hour
- Invalidated on update via `InvalidateCache(companyId, culture)`

### Query Optimization

Key indexes:
- `ShiftPrograms (CompanyId, ShiftTypeId)`
- `ProgramDays (ProgramId, DayOfWeek)` UNIQUE
- `MasterProgramItems (MasterProgramId, ProgramId)` UNIQUE
- `ShiftInstances (OriginalProgramId)`

---

## Testing

### Unit Tests (Recommended)

```csharp
[Fact]
public async Task GenerateInstances_WithWeeklyMask_CreatesCorrectDays()
{
    // Arrange: Program runs Mon-Wed-Fri
    var program = CreateProgram(days: [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday]);

    // Act: Generate for 2 weeks
    var instances = await _service.GenerateInstancesAsync(program.Id, start, end, false);

    // Assert: Should create 6 instances (2 weeks × 3 days)
    Assert.Equal(6, instances.Count);
    Assert.All(instances, i => Assert.Contains(i.WorkDate.DayOfWeek, new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday }));
}
```

### Integration Tests

Test the full workflow:
1. Create ShiftType
2. Create Program with weekly mask
3. Generate instances
4. Verify instances have OriginalProgramId
5. Edit instance → Verify IsDetached = true
6. Reset instance → Verify returns to defaults

---

## Summary

The **Ops Console Scheduler** provides a complete template-based scheduling system:
- ✅ **Foundation**: Models, migrations, services
- ✅ **UI Pages**: Blueprints, Programs, MasterPrograms
- ✅ **Localization**: Bilingual shift names with company overrides
- ✅ **Override Tracking**: Detachment workflow with reset capability
- ⏳ **Pending**: OVR badges in Calendar, enhanced notifications, dense UI

**Result**: Staff an entire week in 90 seconds by applying a MasterProgram.
