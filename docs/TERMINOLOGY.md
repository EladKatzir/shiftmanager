# ShiftManager Terminology Guide

## Historical Context

Originally, the system used "Shifts" for time-bounded work and "On-Duty" for day-length work. This created an artificial hierarchy that made non-official workers doing day-length work feel excluded.

## Current User-Facing Terminology (as of v1.2+)

### English
- **Scheduled Shifts** (formerly "Shifts"): Work assignments with specific start/end times (typically 8 hours)
- **Day Shifts** (formerly "On-Duty"): Work assignments covering full day-length without specific times

### Hebrew
- **משמרות הפקה וב"ר** (formerly "משמרות מתוזמנות"): Scheduled shifts
- **משמרות רוחב** (formerly "משמרות יומיות"): Day shifts

## Historical Evolution

**v1.0** - Original terminology (hierarchical):
- English: "Shifts" / "On-Duty"
- Hebrew: "משמרות" / "תפקידנים/תורנות"

**v1.1** - First inclusive update:
- English: "Scheduled Shifts" / "Day Shifts"
- Hebrew: "משמרות מתוזמנות" / "משמרות יומיות"

**v1.2** - Organization-specific terminology (current):
- English: "Scheduled Shifts" / "Day Shifts" (unchanged)
- Hebrew: "משמרות הפקה וב\"ר" / "משמרות רוחב"

## Backend Code Terminology (DO NOT CHANGE)

For technical/code purposes, the original names are preserved:

### Models
- `ShiftType`, `ShiftInstance`, `ShiftAssignment` → Scheduled shifts
- `OnDuty`, `OnDutyType`, `OnDutyTypeConfig` → Day shifts

### Services
- `IShiftService` → Scheduled shifts operations
- `IOnDutyService` → Day shifts operations

### Database Tables
- `ShiftTypes`, `ShiftInstances`, `ShiftAssignments` → Scheduled shifts
- `OnDuties`, `OnDutyTypeConfigs` → Day shifts

## Rationale

The unified "shifts" terminology reflects that:
1. All workers (readers, transcribers, summarizers, Hakams, leads) perform "shifts"
2. The distinction is scheduling (fixed-time vs. day-length), not job hierarchy
3. People can hold multiple roles and do both types of shifts
4. Avoids making anyone feel excluded based on their role

## For Developers

When working with "OnDuty" code:
- Remember this refers to "Day Shifts" in the UI
- The name "OnDuty" is historical - it no longer implies official roles only
- Day shifts have no specific times (just dates), scheduled shifts have Start/End times
- Day shifts are global/cross-company, scheduled shifts are company-scoped

## Resource Key Mapping

### Core Terms
| Resource Key | English UI | Hebrew UI | Backend |
|--------------|-----------|-----------|---------|
| `ScheduledShifts` | Scheduled Shifts | משמרות הפקה וב"ר | ShiftType/ShiftInstance |
| `DayShifts` | Day Shifts | משמרות רוחב | OnDuty |
| `OnDuty` | Day Shift | משמרת רוחב | OnDuty (model) |
| `MyShifts` | My Scheduled Shifts | משמרות ההפקה וב"ר שלי | User's shifts |
| `OnDutyHakam` | Day Shift - Hakam | משמרת רוחב - חק״מכו | OnDutyType.Hakam |
| `OnDutyLead` | Day Shift - Lead | משמרת רוחב - מוביל | OnDutyType.Lead |

### Navigation & Management
| Resource Key | English UI | Hebrew UI | Purpose |
|--------------|-----------|-----------|---------|
| `MyWorkSchedule` | My Work Schedule | סידור העבודה שלי | Calendar page title |
| `Calendar_MyCalendar` | My Shifty | השיפטי שלי | Navigation header |
| `WorkScheduleManagement` | Work Schedule Management | ניהול סידור עבודה | Navigation header |
| `Calendar_ShiftsManagement` | Scheduled Shifts | משמרות הפקה וב"ר | Nav link to shift table |
| `Calendar_OnDutyManagement` | Day Shifts | משמרות רוחב | Nav link to day shifts |
| `Calendar_ChoresManagement` | Chores | מטלות | Nav link to chores |

## See Also

- README.md - Quick reference for UI vs. code terminology
- Dev comments in `Services/OnDutyService.cs`
- Dev comments in `Models/OnDuty.cs`
