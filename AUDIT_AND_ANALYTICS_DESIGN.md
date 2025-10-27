# Audit Log & Analytics Dashboard - Design Document

**Date**: 2025-10-19
**Status**: Design Phase
**Author**: Claude Code

---

## 1. Audit Log System

### 1.1 Overview

**Purpose**: Track every significant action in the system for compliance, debugging, and security auditing.

**Access**: Manager, Director, Admin (Owner) only

**Multi-Tenant**: Fully isolated by CompanyId

### 1.2 Entity Design

**Table**: `AuditLogs`

```csharp
public class AuditLog : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }

    // Who performed the action
    public int? UserId { get; set; }  // Nullable for system actions
    public string UserEmail { get; set; }  // Denormalized for deleted users
    public string UserDisplayName { get; set; }  // Denormalized for deleted users

    // What action was performed
    public string Action { get; set; }  // e.g., "ShiftAssigned", "UserCreated", "TimeOffApproved"
    public string EntityType { get; set; }  // e.g., "ShiftAssignment", "User", "TimeOffRequest"
    public int? EntityId { get; set; }  // ID of affected entity
    public string Description { get; set; }  // Human-readable description
    public string Details { get; set; }  // JSON with before/after state (optional)

    // When it happened
    public DateTime Timestamp { get; set; }

    // Additional context
    public string IpAddress { get; set; }  // User's IP
    public string UserAgent { get; set; }  // Browser info

    // Navigation
    public Company Company { get; set; }
    public AppUser User { get; set; }
}
```

### 1.3 Actions to Log

**User Management**:
- UserCreated
- UserUpdated
- UserDeactivated
- UserReactivated
- RoleChanged

**Shift Management**:
- ShiftAssigned
- ShiftUnassigned
- ShiftInstanceCreated
- ShiftInstanceDeleted
- StaffingAdjusted

**Request Workflows**:
- TimeOffRequested
- TimeOffApproved
- TimeOffDeclined
- SwapRequested
- SwapApproved
- SwapDeclined

**Trainee Management**:
- TraineeAssignedToShift
- TraineeRemovedFromShift

**Company Settings**:
- ShiftTypeCreated
- ShiftTypeUpdated
- ShiftTypeDeleted
- ConfigUpdated

**Director Actions**:
- DirectorAccessGranted
- DirectorAccessRevoked
- CompanyCreated
- CompanyUpdated

### 1.4 Capture Strategy

**Option 1: Manual Logging** (Recommended)
- Call `IAuditLogService.LogAsync()` in each service method
- Pros: Explicit, complete control, can capture before/after state
- Cons: Must remember to add to each action

**Option 2: Middleware + Interceptor**
- EF Core SaveChanges interceptor for database changes
- Middleware for HTTP requests
- Pros: Automatic, harder to miss
- Cons: Less context, harder to generate meaningful descriptions

**Decision**: Use **Manual Logging** for better descriptions and context.

### 1.5 Service Interface

```csharp
public interface IAuditLogService
{
    Task LogAsync(string action, string entityType, int? entityId, string description, string details = null);
    Task LogUserActionAsync(int userId, string action, string entityType, int? entityId, string description);
    Task LogSystemActionAsync(string action, string entityType, int? entityId, string description);
}
```

### 1.6 Admin Page Features

**URL**: `/Admin/AuditLog`

**Authorization**: `[Authorize(Policy = "IsManagerOrAdmin")]`

**Features**:
- Pagination (50 entries per page)
- Date range filter
- User filter (dropdown)
- Action type filter (dropdown)
- Entity type filter (dropdown)
- Search box (description search)
- Export to CSV
- Real-time updates (optional: SignalR)

**UI Layout**:
```
┌─────────────────────────────────────────────────────────────┐
│                    Audit Log                                 │
├─────────────────────────────────────────────────────────────┤
│ Filters:                                                     │
│ [Date Range] [User] [Action Type] [Entity Type] [Search]    │
│ [Apply Filters] [Clear] [Export CSV]                        │
├─────────────────────────────────────────────────────────────┤
│ Timestamp        │ User           │ Action        │ Details  │
├──────────────────┼────────────────┼───────────────┼─────────┤
│ 2025-10-19 14:32 │ John Doe       │ ShiftAssigned │ Morning  │
│ 2025-10-19 14:30 │ Jane Smith     │ UserCreated   │ Bob J.   │
│ 2025-10-19 14:25 │ Admin User     │ RoleChanged   │ Mgr→Emp  │
│ ...              │ ...            │ ...           │ ...      │
└──────────────────┴────────────────┴───────────────┴─────────┘
│ << Previous | Page 1 of 20 | Next >>                        │
└─────────────────────────────────────────────────────────────┘
```

### 1.7 Performance Considerations

**Indexes**:
```csharp
// AppDbContext.cs
modelBuilder.Entity<AuditLog>()
    .HasIndex(a => new { a.CompanyId, a.Timestamp })
    .HasDatabaseName("IX_AuditLogs_CompanyId_Timestamp");

modelBuilder.Entity<AuditLog>()
    .HasIndex(a => new { a.CompanyId, a.UserId, a.Timestamp })
    .HasDatabaseName("IX_AuditLogs_CompanyId_UserId_Timestamp");

modelBuilder.Entity<AuditLog>()
    .HasIndex(a => new { a.CompanyId, a.Action })
    .HasDatabaseName("IX_AuditLogs_CompanyId_Action");
```

**Data Retention**:
- Keep all audit logs (never delete)
- Archive old logs (>1 year) to separate table (future enhancement)
- Current approach: Keep everything in main table (SQLite can handle millions of rows)

### 1.8 Security Considerations

**Authorization**:
- Only Manager, Director, Owner can view audit logs
- Directors can only view logs for companies they manage
- No ability to edit or delete audit logs (append-only)

**Privacy**:
- Don't log sensitive data (passwords, API keys) in Details field
- Mask IP addresses in GDPR-compliant regions (future)

**Integrity**:
- Consider adding cryptographic hash of entries (future)
- Tamper detection (future)

---

## 2. Analytics Dashboard

### 2.1 Overview

**Purpose**: Provide insights into workforce utilization, shift coverage, and operational efficiency.

**Access**: Manager, Director, Admin (Owner) only

**Multi-Tenant**: Fully isolated by CompanyId

### 2.2 Metrics Categories

#### A. Employee Analytics

**Metrics**:
1. **Hours Worked** (past 30 days)
   - Query: Sum of shift hours per employee
   - Grouping: By employee
   - Display: Table with employee name, total hours, avg hours/week

2. **Upcoming Shifts** (next 7 days)
   - Query: Count of upcoming shifts per employee
   - Grouping: By employee
   - Display: Table with employee name, shift count

3. **Back-to-Back Shifts**
   - Query: Shifts with <8 hours rest between them
   - Grouping: By employee
   - Display: Warning list with employee, shift dates, rest hours

4. **Most Active Employees**
   - Query: Top 10 employees by shift count (past 30 days)
   - Display: Bar chart

#### B. Team Analytics

**Metrics**:
1. **Hours by Role** (past 30 days)
   - Query: Sum of shift hours grouped by UserRole
   - Display: Pie chart

2. **Hours by Department** (if implemented in Profile Enhancements)
   - Query: Sum of shift hours grouped by Department
   - Display: Pie chart

3. **Understaffing Report**
   - Query: ShiftInstances where assigned < required
   - Display: Table with date, shift type, assigned, required, deficit

4. **Overstaffing Report**
   - Query: ShiftInstances where assigned > required
   - Display: Table with date, shift type, assigned, required, surplus

5. **Coverage Rate**
   - Query: (Total assigned / Total required) * 100
   - Display: Gauge chart (0-100%)

#### C. Swap Analytics

**Metrics**:
1. **Swap Request Stats**
   - Total requests (past 30 days)
   - Approved count
   - Declined count
   - Approval rate %
   - Display: Summary cards

2. **Top Swappers**
   - Query: Top 10 employees by swap request count
   - Display: Table

3. **Average Approval Time**
   - Query: Avg(ReviewedAt - CreatedAt) for approved swaps
   - Display: Summary card (e.g., "2.3 hours")

#### D. Time-Off Analytics

**Metrics**:
1. **Time-Off Request Stats**
   - Total requests (past 30 days)
   - Approved count
   - Declined count
   - Approval rate %
   - Display: Summary cards

2. **Average Days Off** (per employee)
   - Query: Sum of approved time-off days / employee count
   - Display: Summary card (e.g., "5.2 days/employee")

3. **Time-Off by Month**
   - Query: Count of time-off days per month (past 12 months)
   - Display: Line chart

### 2.3 Service Interface

```csharp
public interface IAnalyticsService
{
    // Employee Analytics
    Task<List<EmployeeHoursDto>> GetEmployeeHoursAsync(DateOnly startDate, DateOnly endDate);
    Task<List<EmployeeShiftCountDto>> GetUpcomingShiftsAsync(int days = 7);
    Task<List<BackToBackShiftDto>> GetBackToBackShiftsAsync();

    // Team Analytics
    Task<Dictionary<UserRole, decimal>> GetHoursByRoleAsync(DateOnly startDate, DateOnly endDate);
    Task<List<StaffingIssueDto>> GetUnderstaffingReportAsync(DateOnly startDate, DateOnly endDate);
    Task<List<StaffingIssueDto>> GetOverstaffingReportAsync(DateOnly startDate, DateOnly endDate);
    Task<decimal> GetCoverageRateAsync(DateOnly startDate, DateOnly endDate);

    // Swap Analytics
    Task<SwapStatsDto> GetSwapStatsAsync(DateOnly startDate, DateOnly endDate);
    Task<List<TopSwapperDto>> GetTopSwappersAsync(int topN = 10);
    Task<TimeSpan> GetAverageSwapApprovalTimeAsync();

    // Time-Off Analytics
    Task<TimeOffStatsDto> GetTimeOffStatsAsync(DateOnly startDate, DateOnly endDate);
    Task<decimal> GetAverageDaysOffPerEmployeeAsync();
    Task<Dictionary<string, int>> GetTimeOffByMonthAsync(int months = 12);
}
```

### 2.4 Admin Page Layout

**URL**: `/Admin/Analytics`

**Authorization**: `[Authorize(Policy = "IsManagerOrAdmin")]`

**UI Layout**:
```
┌─────────────────────────────────────────────────────────────┐
│                  Analytics Dashboard                         │
├─────────────────────────────────────────────────────────────┤
│ Date Range: [Last 30 Days ▼] [Custom Date Range...]         │
│ [Export Report]                                              │
├─────────────────────────────────────────────────────────────┤
│ ┌─────────────────────────────────────────────────────────┐ │
│ │ Employee Analytics                                       │ │
│ ├─────────────────────────────────────────────────────────┤ │
│ │ Hours Worked (Past 30 Days)                             │ │
│ │ ┌──────────────┬──────────┬──────────┬──────────┐      │ │
│ │ │ Employee     │ Hours    │ Avg/Week │ Shifts   │      │ │
│ │ ├──────────────┼──────────┼──────────┼──────────┤      │ │
│ │ │ John Doe     │ 160.0    │ 40.0     │ 20       │      │ │
│ │ │ Jane Smith   │ 152.0    │ 38.0     │ 19       │      │ │
│ │ └──────────────┴──────────┴──────────┴──────────┘      │ │
│ │                                                          │ │
│ │ Back-to-Back Shifts (⚠️ Warnings)                       │ │
│ │ • John Doe: 2025-10-20 Night → 2025-10-21 Morning (6h)  │ │
│ │ • Jane Smith: 2025-10-22 Mid → 2025-10-22 Night (4h)   │ │
│ └─────────────────────────────────────────────────────────┘ │
├─────────────────────────────────────────────────────────────┤
│ ┌─────────────────────────────────────────────────────────┐ │
│ │ Team Analytics                                           │ │
│ ├─────────────────────────────────────────────────────────┤ │
│ │ Coverage Rate: [████████░░] 82%                         │ │
│ │                                                          │ │
│ │ Understaffing Report                                     │ │
│ │ ┌──────────┬────────┬──────────┬──────────┬────────┐   │ │
│ │ │ Date     │ Shift  │ Assigned │ Required │ Deficit│   │ │
│ │ ├──────────┼────────┼──────────┼──────────┼────────┤   │ │
│ │ │ 10/25    │ Morning│ 3        │ 5        │ -2     │   │ │
│ │ │ 10/26    │ Night  │ 2        │ 4        │ -2     │   │ │
│ │ └──────────┴────────┴──────────┴──────────┴────────┘   │ │
│ └─────────────────────────────────────────────────────────┘ │
├─────────────────────────────────────────────────────────────┤
│ ┌───────────────────┬───────────────────────────────────┐   │
│ │ Swap Stats        │ Time-Off Stats                    │   │
│ ├───────────────────┼───────────────────────────────────┤   │
│ │ Total: 45         │ Total: 28                         │   │
│ │ Approved: 38      │ Approved: 24                      │   │
│ │ Declined: 7       │ Declined: 4                       │   │
│ │ Rate: 84%         │ Rate: 86%                         │   │
│ │ Avg Time: 2.3h    │ Avg Days/Emp: 5.2                 │   │
│ └───────────────────┴───────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

### 2.5 Export Functionality

**Format**: CSV

**Filename**: `Analytics_Report_{CompanyName}_{Date}.csv`

**Contents**:
- All metrics from dashboard
- Date range
- Generated timestamp
- Company name

**Example**:
```csv
Analytics Report - Acme Corp
Generated: 2025-10-19 14:30:00
Date Range: 2025-09-19 to 2025-10-19

Employee Hours (Past 30 Days)
Employee,Total Hours,Avg Hours/Week,Shift Count
John Doe,160.0,40.0,20
Jane Smith,152.0,38.0,19

Understaffing Report
Date,Shift Type,Assigned,Required,Deficit
2025-10-25,Morning,3,5,-2
2025-10-26,Night,2,4,-2

...
```

### 2.6 Performance Optimization

**Caching**:
- Cache analytics results for 5 minutes (IMemoryCache)
- Cache key: `Analytics_{CompanyId}_{DateRange}_{Metric}`
- Invalidate on data changes (shift assignments, approvals)

**Indexes** (already exist in AppDbContext.cs):
- ShiftAssignments: (CompanyId, ShiftInstanceId, UserId)
- ShiftInstances: (CompanyId, WorkDate)
- TimeOffRequests: (CompanyId, UserId, StartDate)
- SwapRequests: (CompanyId, Status, CreatedAt)

**Query Optimization**:
- Use `.AsNoTracking()` for read-only queries
- Use `.Include()` for eager loading related entities
- Avoid N+1 queries

### 2.7 Charts and Visualization

**Library**: Chart.js (JavaScript, MIT license)

**Chart Types**:
1. **Bar Chart**: Most Active Employees
2. **Pie Chart**: Hours by Role
3. **Line Chart**: Time-Off by Month
4. **Gauge Chart**: Coverage Rate

**Implementation**:
- Render charts in Razor Page using Chart.js
- Pass data from PageModel as JSON
- Responsive design (mobile-friendly)

---

## 3. Database Schema Changes

### 3.1 Migration: AddAuditLogAndAnalytics

**New Tables**:
- `AuditLogs` (13 columns)

**Indexes**:
- `IX_AuditLogs_CompanyId_Timestamp`
- `IX_AuditLogs_CompanyId_UserId_Timestamp`
- `IX_AuditLogs_CompanyId_Action`

**No changes to existing tables** (analytics uses existing data)

### 3.2 Rollback Script

**File**: `Migrations/rollback/12_AddAuditLogAndAnalytics_Rollback.sql`

```sql
-- Drop indexes
DROP INDEX IF EXISTS IX_AuditLogs_CompanyId_Timestamp;
DROP INDEX IF EXISTS IX_AuditLogs_CompanyId_UserId_Timestamp;
DROP INDEX IF EXISTS IX_AuditLogs_CompanyId_Action;

-- Drop table
DROP TABLE IF EXISTS AuditLogs;
```

---

## 4. Authorization Matrix

| Role      | View Audit Log | View Analytics | Export Reports |
|-----------|----------------|----------------|----------------|
| Owner     | ✅ All companies | ✅ All companies | ✅             |
| Director  | ✅ Assigned companies | ✅ Assigned companies | ✅             |
| Manager   | ✅ Own company | ✅ Own company | ✅             |
| Employee  | ❌             | ❌             | ❌             |
| Trainee   | ❌             | ❌             | ❌             |

---

## 5. Implementation Plan

### Phase 1: Audit Log (4-6 hours)
1. Create `AuditLog` entity
2. Create migration `AddAuditLog`
3. Implement `AuditLogService`
4. Add audit logging to key service methods (10-15 locations)
5. Create `Admin/AuditLog.cshtml` page
6. Test audit logging

### Phase 2: Analytics Service (4-6 hours)
1. Create `IAnalyticsService` interface
2. Implement `AnalyticsService` with all metrics
3. Create DTO classes for analytics results
4. Add caching layer
5. Test analytics queries

### Phase 3: Analytics Dashboard (3-4 hours)
1. Create `Admin/Analytics.cshtml` page
2. Integrate Chart.js library
3. Implement export to CSV
4. Test dashboard display
5. Mobile responsive design

### Phase 4: Testing & Documentation (2-3 hours)
1. Manual testing of all features
2. Performance testing (large datasets)
3. Update `context.md`
4. Create usage documentation
5. Build verification

**Total Estimated Time**: 13-19 hours

---

## 6. Open Questions

1. **Chart Library**: Use Chart.js (free, MIT) or ApexCharts (more features)?
   - **Decision**: Chart.js (lighter, simpler)

2. **Real-time Updates**: Add SignalR for live audit log updates?
   - **Decision**: No (future enhancement)

3. **Data Retention**: Archive old audit logs (>1 year)?
   - **Decision**: No (keep all, SQLite can handle it)

4. **Export Format**: CSV only or also Excel/PDF?
   - **Decision**: CSV for now (Excel in future)

---

## 7. Success Criteria

**Audit Log**:
- ✅ All major actions logged automatically
- ✅ Logs visible only to Manager+
- ✅ Pagination and filtering work correctly
- ✅ Export to CSV successful
- ✅ No performance degradation (<100ms overhead per action)

**Analytics Dashboard**:
- ✅ All metrics calculate correctly
- ✅ Dashboard loads in <3 seconds
- ✅ Charts render properly
- ✅ Export generates valid CSV
- ✅ Mobile-responsive design

---

## 8. Next Steps

1. Review and approve this design
2. Begin Phase 1: Audit Log implementation
3. Create migration and entity
4. Implement service and integrate with existing code
5. Build admin pages
6. Test and document

---

**Ready to proceed with implementation?**
