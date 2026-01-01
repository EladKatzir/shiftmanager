# ShiftManager - Complete Database Schema
## Comprehensive Data Model Documentation

**Document Version:** 1.1
**Last Updated:** 2026-01-01
**Database Provider:** SQLite (via EF Core 9.0.9)
**Total Tables:** 30 core entities

[⬅️ Back to Index](00-INDEX.md) | [➡️ Next: Startup & Middleware](04-STARTUP-AND-MIDDLEWARE.md)

---

## Table of Contents

1. [Overview](#overview)
2. [Entity-Relationship Diagram](#entity-relationship-diagram)
3. [Multi-Tenancy Architecture](#multi-tenancy-architecture)
4. [Table Catalog](#table-catalog)
5. [Core Domain Tables](#core-domain-tables)
6. [Request Workflow Tables](#request-workflow-tables)
7. [Task Assignment Tables](#task-assignment-tables)
8. [Team Collaboration Tables](#team-collaboration-tables)
9. [Notification Tables](#notification-tables)
10. [Audit & Compliance Tables](#audit--compliance-tables)
11. [Configuration Tables](#configuration-tables)
12. [API Infrastructure Tables](#api-infrastructure-tables)
13. [Multi-Tenancy Cross-Company Tables](#multi-tenancy-cross-company-tables)
14. [Gamification Tables](#gamification-tables)
15. [Indexes & Performance](#indexes--performance)
16. [Foreign Key Relationships](#foreign-key-relationships)
17. [Value Converters](#value-converters)
18. [Constraints & Validation](#constraints--validation)

---

## Overview

### Database Architecture Summary

**Provider:** SQLite 3.x (file-based, single-file database)
**File Location:** `app.db` (root directory)
**Schema Management:** EF Core Code-First migrations
**Total Migrations:** 36 migrations (Sept 2025 - Present)
**Storage Size:** ~50-200 MB (typical for 1000 users, 100k shift assignments)

### Key Design Decisions

1. **SQLite Choice:** Zero-config deployment, single-file simplicity, perfect for air-gapped environments
2. **Row-Level Multi-Tenancy:** CompanyId discriminator on 25 tables, global query filters for automatic isolation
3. **Soft Deletes:** Used for Chores, OnDuties, TeamCalendars, DirectorCompanies (preserve history)
4. **Denormalized Audit Data:** Email/DisplayName stored in AuditLogs (survive user deletion)
5. **Polymorphic References:** EntityType + EntityId pattern (AuditLogs, UserNotifications)
6. **No Cascading Deletes on Users:** RESTRICT on most FKs to prevent accidental data loss

---

## Entity-Relationship Diagram

**Complete ERD:** [View database-erd.mmd](diagrams/database-erd.mmd)

The diagram visualizes all 28 tables with:
- Primary keys (PK)
- Foreign keys (FK)
- Tenant-scoped markers [T] vs. Global [G]
- Cardinality (one-to-one, one-to-many, many-to-many)
- Relationship types (CASCADE, RESTRICT, SET NULL)

**Quick Navigation:**
- [Core Domain](#core-domain-tables): Companies, AppUsers, ShiftTypes, ShiftInstances, ShiftAssignments
- [Requests](#request-workflow-tables): TimeOffRequests, SwapRequests, UserJoinRequests
- [Tasks](#task-assignment-tables): Chores, OnDuties, OnDutyRoleSubscriptions, OnDutyTypeConfigs
- [Teams](#team-collaboration-tables): TeamCalendars, TeamCalendarMembers
- [Notifications](#notification-tables): UserNotifications, DailyNotificationPreferences
- [Audit](#audit--compliance-tables): AuditLogs, RoleAssignmentAudits, ProfileChangeAudits
- [Config](#configuration-tables): Configs, EmailConfigs, EmailApiLogs, GriffinConfigs, GriffinApiLogs
- [API](#api-infrastructure-tables): ApiKeys, ApiKeyRequests, ApiRequestLogs
- [Cross-Company](#multi-tenancy-cross-company-tables): DirectorCompanies
- [Gamification](#gamification-tables): GameScores, Feedbacks

---

## Multi-Tenancy Architecture

### CompanyId Scoping Rules

**Tenant-Scoped Tables (25 tables):**
All queries automatically filter by CompanyId via EF Core global query filters.

```csharp
// Applied in AppDbContext.OnModelCreating
modelBuilder.Entity<ShiftInstance>()
    .HasQueryFilter(e => e.CompanyId == _tenantResolver.GetCurrentTenantId());
```

**Tables WITH CompanyId:**
- Companies (root), AppUsers, ShiftTypes, ShiftInstances, ShiftAssignments
- TimeOffRequests, SwapRequests, UserJoinRequests
- Chores, OnDutyRoleSubscriptions
- TeamCalendars, TeamCalendarMembers
- UserNotifications, DailyNotificationPreferences
- AuditLogs, RoleAssignmentAudits, ProfileChangeAudits
- Configs, EmailConfigs, EmailApiLogs, GriffinConfigs, GriffinApiLogs
- GameScores, Feedbacks

**Tables WITHOUT CompanyId (3 tables):**
1. **OnDuties:** Cross-company visibility (business requirement for coordination)
2. **OnDutyTypeConfigs:** Global configuration (system-wide on-duty types)
3. **ApiKeys, ApiKeyRequests, ApiRequestLogs:** API infrastructure (intentionally cross-tenant for API management)

**Special Case: DirectorCompanies**
- Has both UserId and CompanyId
- NO query filter applied (intentionally cross-tenant mapping table)
- Allows directors to access multiple companies

### Tenant Isolation Verification

**Test Query:**
```sql
-- Should return ONLY current company's data (CompanyId=1)
SELECT * FROM ShiftInstances WHERE WorkDate = '2025-12-30';
-- EF Core automatically adds: AND CompanyId = 1
```

**Security Guarantee:** Impossible to access another company's data unless:
1. You're a Director with explicit DirectorCompany mapping
2. You're accessing OnDuty table (intentionally global)
3. EnforceCompanyScope feature flag is disabled (dev only)

---

## Table Catalog

### Quick Reference Table

| # | Table Name | Rows (Typical) | CompanyId? | Soft Delete? | Purpose |
|---|-----------|----------------|------------|--------------|---------|
| 1 | Companies | 1-10 | N/A (root) | No | Tenant root table |
| 2 | AppUsers | 50-2000 | Yes | No | Users/employees |
| 3 | ShiftTypes | 5-20 | Yes | No | Shift type definitions |
| 4 | ShiftInstances | 10k-100k | Yes | No | Shift occurrences |
| 5 | ShiftAssignments | 10k-100k | Yes | No | User-to-shift assignments |
| 6 | TimeOffRequests | 500-5000 | Yes | No | Vacation requests |
| 7 | SwapRequests | 100-1000 | Yes | No | Shift swap requests |
| 8 | UserJoinRequests | 10-100 | Yes | No | Pending signups |
| 9 | Chores | 1k-10k | Yes | Yes (CanceledAt) | Daily task assignments |
| 10 | OnDuties | 1k-10k | **NO** | Yes (CanceledAt) | On-duty assignments (global) |
| 11 | OnDutyRoleSubscriptions | 50-500 | Yes | No | On-duty type subscriptions |
| 12 | OnDutyTypeConfigs | 3-10 | **NO** | No | Global on-duty type definitions |
| 13 | TeamCalendars | 10-100 | Yes | Yes (IsDeleted) | User-created calendars |
| 14 | TeamCalendarMembers | 50-500 | Yes | No | Calendar membership |
| 15 | UserNotifications | 5k-50k | Yes | No | In-app notifications |
| 16 | DailyNotificationPreferences | 50-2000 | Yes | No | Notification settings |
| 17 | AuditLogs | 10k-100k | Yes | No | Comprehensive audit trail |
| 18 | RoleAssignmentAudits | 100-1000 | Yes | No | Role change tracking |
| 19 | ProfileChangeAudits | 500-5000 | Yes | No | Profile modification history |
| 20 | Configs | 10-50 | Yes | No | Key-value config store |
| 21 | EmailConfigs | 1-10 | Yes | No | Email provider config (one per company) |
| 22 | EmailApiLogs | 1k-10k | Yes | No | Email API call logs |
| 23 | GriffinConfigs | 1-10 | Yes | No | Griffin ADFS config |
| 24 | GriffinApiLogs | 1k-10k | Yes | No | Griffin API call logs |
| 25 | ApiKeys | 5-50 | Yes* | No | API key management |
| 26 | ApiKeyRequests | 10-100 | Yes* | No | API key approval workflow |
| 27 | ApiRequestLogs | 10k-100k | Yes* | No | API usage tracking |
| 28 | DirectorCompanies | 5-50 | **NO filter** | Yes (IsDeleted) | Cross-company director access |
| 29 | GameScores | 100-1000 | Yes | No | Leaderboard |
| 30 | Feedbacks | 10-100 | Yes | No | User feedback |

*Has CompanyId but no global query filter applied (API infrastructure tables)

---

## Core Domain Tables

### 1. Companies (Tenant Root)

**Purpose:** Multi-tenant root table. Each company represents an independent organization.

**File Reference:** `Models/Company.cs`

**Schema:**
```csharp
public class Company
{
    public int Id { get; set; }                    // PK, auto-increment
    public string Name { get; set; }               // Required, max 200 chars
    public string Slug { get; set; }               // Unique URL identifier (e.g., "alpha-company")
    public string? DisplayName { get; set; }       // Optional friendly name
    public string? SettingsJson { get; set; }      // JSON blob for company-specific settings
    public DateTime CreatedAt { get; set; }

    // Navigation Properties
    public ICollection<AppUser> Users { get; set; }
    public ICollection<ShiftType> ShiftTypes { get; set; }
    // ... (all tenant-scoped entities)
}
```

**Indexes:**
- `IX_Companies_Slug` (UNIQUE) - Fast slug-based company lookup

**Constraints:**
- Slug must be unique (URL routing)
- Name is required

**Business Rules:**
- Cannot delete company if it has users (RESTRICT FK)
- Slug is URL-safe (lowercase, hyphens only)

---

### 2. AppUsers (Users/Employees)

**Purpose:** User accounts with authentication, profile data, and role-based access control.

**File Reference:** `Models/AppUser.cs`

**Schema:**
```csharp
public class AppUser : IBelongsToCompany
{
    public int Id { get; set; }                        // PK
    public int CompanyId { get; set; }                 // FK → Companies, [T] Tenant-scoped

    // Authentication
    public string Email { get; set; }                  // Unique (global), max 256 chars
    public byte[] PasswordHash { get; set; }           // PBKDF2 hash (100k iterations)
    public byte[] PasswordSalt { get; set; }           // Random salt
    public int FailedLoginAttempts { get; set; }       // Account lockout tracking
    public DateTime? LockoutEnd { get; set; }          // Lockout until this time
    public DateTime? LastLoginAttempt { get; set; }

    // Profile
    public string DisplayName { get; set; }            // Required, max 200 chars
    public UserRole Role { get; set; }                 // Enum: Owner/Director/Manager/Assigner/Employee/Trainee
    public bool IsActive { get; set; }                 // Soft delete flag
    public string? PhoneNumber { get; set; }
    public DateOnly? DateOfBirth { get; set; }         // SQLite: stored as string "YYYY-MM-DD"
    public DateOnly? HireDate { get; set; }
    public string? AvatarFileName { get; set; }        // Avatar image filename (stored in wwwroot)

    // Personal Information (optional)
    public string? PersonalEmail { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }

    // Professional Information (optional)
    public string? Department { get; set; }
    public string? JobTitle { get; set; }
    public string? EmployeeId { get; set; }

    // Timestamps
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // Navigation Properties
    public Company Company { get; set; }
    public ICollection<ShiftAssignment> ShiftAssignments { get; set; }
    public ICollection<TimeOffRequest> TimeOffRequests { get; set; }
    // ... (many more)
}
```

**Indexes:**
- `IX_AppUsers_Email` (UNIQUE) - Fast email lookup for login
- `IX_AppUsers_CompanyId_IsActive` - Filter active users per company
- `IX_AppUsers_CompanyId_Role` - Filter by role

**Constraints:**
- Email must be unique (globally across all companies)
- DisplayName is required
- CompanyId is required (foreign key to Companies, RESTRICT delete)

**Business Rules:**
- Password must be hashed with PBKDF2 (100k iterations, SHA256)
- Lockout after 5 failed login attempts (configurable)
- Cannot delete user if they have shift assignments (RESTRICT FK)
- Role can only be changed by Owner or Director (enforced in service layer)

**Enum: UserRole**
```csharp
public enum UserRole
{
    Owner = 0,      // Full system control, company config
    Manager = 1,    // Approve requests, assign shifts, manage team
    Employee = 2,   // View schedule, submit requests
    Director = 3,   // Cross-company oversight (via DirectorCompanies table)
    Trainee = 4,    // Limited view, shadowing
    Assigner = 5    // Can edit Chores only (not On-Duty)
}
```

---

### 3. ShiftTypes (Shift Type Definitions)

**Purpose:** Define shift templates (e.g., MORNING 06:00-14:00, NIGHT 22:00-06:00).

**File Reference:** `Models/ShiftType.cs`

**Schema:**
```csharp
public class ShiftType : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }               // [T] Tenant-scoped

    public string Key { get; set; }                  // MORNING/NOON/NIGHT/MIDDLE/EVENING/OFFLINE
    public string? CustomName { get; set; }          // Override default name (e.g., "Early Shift")
    public TimeOnly Start { get; set; }              // Shift start time (SQLite: stored as "HH:mm")
    public TimeOnly End { get; set; }                // Shift end time
    public int SortOrder { get; set; }               // Display order (default: 0)
    public DateTime CreatedAt { get; set; }

    // Navigation Properties
    public Company Company { get; set; }
    public ICollection<ShiftInstance> ShiftInstances { get; set; }
}
```

**Indexes:**
- `IX_ShiftTypes_CompanyId_Key` (UNIQUE) - One shift type per key per company
- `IX_ShiftTypes_CompanyId_SortOrder` - Display ordering

**Constraints:**
- Key must be unique per company (composite unique: CompanyId + Key)
- Start and End times required
- CompanyId required (RESTRICT delete)

**Business Rules:**
- OFFLINE shift type created during seeding (used for time-off marking)
- OFFLINE shifts can overlap (no conflict detection)
- Custom shift types can be added by admins
- Start time can be after End time (for overnight shifts, e.g., 22:00 → 06:00)

**Common Shift Keys:**
- `MORNING` (06:00-14:00)
- `NOON` (14:00-22:00)
- `NIGHT` (22:00-06:00)
- `MIDDLE` (10:00-18:00)
- `EVENING` (16:00-00:00)
- `OFFLINE` (00:00-23:59) - Special: no conflict detection

---

### 4. ShiftInstances (Shift Occurrences)

**Purpose:** Concrete shift occurrences on specific dates (e.g., "MORNING shift on 2025-12-30").

**File Reference:** `Models/ShiftInstance.cs`

**Schema:**
```csharp
public class ShiftInstance : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }               // [T] Tenant-scoped
    public int ShiftTypeId { get; set; }             // FK → ShiftTypes

    public DateOnly WorkDate { get; set; }           // Shift date (SQLite: stored as "YYYY-MM-DD")
    public int StaffingRequired { get; set; }        // Number of people needed (default: 1)
    public string? Name { get; set; }                // Optional custom name override
    public byte[]? Concurrency { get; set; }         // Optimistic concurrency token
    public DateTime CreatedAt { get; set; }

    // Navigation Properties
    public Company Company { get; set; }
    public ShiftType ShiftType { get; set; }
    public ICollection<ShiftAssignment> ShiftAssignments { get; set; }
}
```

**Indexes:**
- `IX_ShiftInstances_CompanyId_WorkDate` - **CRITICAL** for calendar queries (composite)
- `IX_ShiftInstances_ShiftTypeId` - FK index

**Constraints:**
- CompanyId + ShiftTypeId + WorkDate can have duplicates (multiple instances per day allowed)
- StaffingRequired must be >= 0
- Concurrency token for optimistic locking

**Business Rules:**
- One shift instance can have multiple assignments (up to StaffingRequired count)
- Deleting shift instance cascades to shift assignments (CASCADE delete)
- Concurrency token prevents race conditions during assignment

---

### 5. ShiftAssignments (User-to-Shift Assignments)

**Purpose:** Assign users to shift instances (many-to-many relationship with support for unassigned slots).

**File Reference:** `Models/ShiftAssignment.cs`

**Schema:**
```csharp
public class ShiftAssignment : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }               // [T] Tenant-scoped
    public int ShiftInstanceId { get; set; }         // FK → ShiftInstances (CASCADE delete)

    public int? UserId { get; set; }                 // FK → AppUsers (NULLABLE - unassigned slot)
    public int? TraineeUserId { get; set; }          // FK → AppUsers (NULLABLE - shadowing trainee)

    public DateTime AssignedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation Properties
    public Company Company { get; set; }
    public ShiftInstance ShiftInstance { get; set; }
    public AppUser? User { get; set; }               // Nullable
    public AppUser? TraineeUser { get; set; }        // Nullable
}
```

**Indexes:**
- `IX_ShiftAssignments_CompanyId_ShiftInstanceId_UserId` (UNIQUE where UserId IS NOT NULL) - Prevent duplicate assignments
- `IX_ShiftAssignments_UserId` - Fast user lookup
- `IX_ShiftAssignments_TraineeUserId` - Trainee lookups

**Constraints:**
- CompanyId + ShiftInstanceId + UserId must be unique (cannot assign same user twice to same shift)
- UserId is nullable (allows unassigned roster slots)
- TraineeUserId is nullable (trainee shadowing is optional)

**Business Rules:**
- UserId can be NULL (unassigned slot, reserved for future assignment)
- TraineeUserId cannot equal UserId (cannot shadow yourself)
- Deleting shift instance cascades to assignments (CASCADE delete)
- Deleting user restricts if assignments exist (RESTRICT on UserId FK)
- ConflictChecker validates no overlapping shifts for UserId

---

## Request Workflow Tables

### 6. TimeOffRequests (Vacation/Time-Off Requests)

**Purpose:** Employee-initiated time-off requests with manager approval workflow.

**File Reference:** `Models/TimeOffRequest.cs`

**Schema:**
```csharp
public class TimeOffRequest : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }               // [T] Tenant-scoped
    public int UserId { get; set; }                  // FK → AppUsers (requester)

    public int? ApproverId { get; set; }             // FK → AppUsers (designated approver, nullable)
    public TimeOffType TimeOffType { get; set; }     // Enum: Vacation(0) or After(1)
    public DateOnly StartDate { get; set; }          // Start date (inclusive)
    public DateOnly EndDate { get; set; }            // End date (inclusive)
    public string? Reason { get; set; }

    public RequestStatus Status { get; set; }        // Enum: Pending(0)/Approved(1)/Declined(2)
    public string? DeclineReason { get; set; }
    public int? ReviewerId { get; set; }             // FK → AppUsers (who reviewed)
    public DateTime? ReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation Properties
    public Company Company { get; set; }
    public AppUser User { get; set; }
    public AppUser? Approver { get; set; }
    public AppUser? Reviewer { get; set; }
}
```

**Indexes:**
- `IX_TimeOffRequests_CompanyId_UserId_StartDate` - User's request history
- `IX_TimeOffRequests_CompanyId_Status` - Pending requests dashboard

**Constraints:**
- StartDate <= EndDate (validated in service layer)
- UserId required (RESTRICT delete)
- ReviewerId required if Status != Pending

**Business Rules:**
- Approved time-off creates OFFLINE shift instances (block calendar)
- Notifications sent to manager on creation, employee on approval/decline
- Cannot request time-off for past dates
- ConflictChecker validates no existing shifts during time-off period

**Enum: TimeOffType**
```csharp
public enum TimeOffType
{
    Vacation = 0,   // Full day off
    After = 1       // Half day off (starting 4:00 PM)
}
```

**Enum: RequestStatus**
```csharp
public enum RequestStatus
{
    Pending = 0,
    Approved = 1,
    Declined = 2
}
```

---

### 7. SwapRequests (Shift Swap Requests)

**Purpose:** Employee-initiated shift swap requests (trade shifts with colleague).

**File Reference:** `Models/SwapRequest.cs`

**Schema:**
```csharp
public class SwapRequest : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }               // [T] Tenant-scoped

    public int FromUserId { get; set; }              // FK → AppUsers (requester)
    public int FromAssignmentId { get; set; }        // FK → ShiftAssignments (shift to give away)

    public int? ToUserId { get; set; }               // FK → AppUsers (swap partner, nullable)
    public int? ToAssignmentId { get; set; }         // FK → ShiftAssignments (shift to receive, nullable)

    public string? Reason { get; set; }
    public RequestStatus Status { get; set; }        // Pending/Approved/Declined
    public string? DeclineReason { get; set; }
    public int? ReviewerId { get; set; }             // FK → AppUsers (manager who reviewed)
    public DateTime? ReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation Properties
    public Company Company { get; set; }
    public AppUser FromUser { get; set; }
    public ShiftAssignment FromAssignment { get; set; }
    public AppUser? ToUser { get; set; }
    public ShiftAssignment? ToAssignment { get; set; }
    public AppUser? Reviewer { get; set; }
}
```

**Indexes:**
- `IX_SwapRequests_CompanyId_Status_CreatedAt` - Pending requests list
- `IX_SwapRequests_FromUserId` - User's swap history

**Constraints:**
- FromUserId != ToUserId (cannot swap with yourself)
- FromAssignmentId required
- ToUserId and ToAssignmentId both nullable (allows "just need coverage" requests)

**Business Rules:**
- Approved swap reassigns both shift assignments
- Notifications sent to manager, both users
- Cannot swap shifts that already occurred (validated in service layer)
- ToAssignment must belong to ToUser (validated in service layer)

---

### 8. UserJoinRequests (Self-Service Signup)

**Purpose:** Self-service user registration with admin approval workflow.

**File Reference:** `Models/UserJoinRequest.cs`

**Schema:**
```csharp
public class UserJoinRequest : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }               // [T] Tenant-scoped

    public string Email { get; set; }                // Requested email
    public string DisplayName { get; set; }          // Requested display name
    public int RequestedRole { get; set; }           // Requested role (usually Employee)

    public byte[] PasswordHash { get; set; }         // Stored until approval
    public byte[] PasswordSalt { get; set; }

    public JoinRequestStatus Status { get; set; }    // Pending/Approved/Rejected
    public int? ReviewerId { get; set; }             // FK → AppUsers (admin who reviewed)
    public DateTime? ReviewedAt { get; set; }
    public int? CreatedUserId { get; set; }          // FK → AppUsers (created user ID after approval)
    public DateTime CreatedAt { get; set; }

    // Navigation Properties
    public Company Company { get; set; }
    public AppUser? Reviewer { get; set; }
    public AppUser? CreatedUser { get; set; }
}
```

**Indexes:**
- `IX_UserJoinRequests_CompanyId_Status` - Pending requests dashboard
- `IX_UserJoinRequests_Email` - Duplicate request detection

**Constraints:**
- Email must be unique among pending requests
- CompanyId required

**Business Rules:**
- On approval: Create AppUser, copy PasswordHash/Salt, set CreatedUserId
- On rejection: Delete request after notification
- Password must meet strength requirements (validated in service layer)
- AllowPublicSignup feature flag controls if endpoint is accessible

**Enum: JoinRequestStatus**
```csharp
public enum JoinRequestStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}
```

---

## Task Assignment Tables

### 9. Chores (Daily Task Assignments)

**Purpose:** Daily chore assignments (e.g., "Clean armory", "Inspect vehicles").

**File Reference:** `Models/Chore.cs`

**Schema:**
```csharp
public class Chore : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }               // [T] Tenant-scoped

    public DateOnly Date { get; set; }               // Chore date
    public int UserId { get; set; }                  // FK → AppUsers (assigned to)
    public string Title { get; set; }                // Chore description
    public string? Notes { get; set; }

    public int CreatedById { get; set; }             // FK → AppUsers (who created)
    public DateTime CreatedAt { get; set; }

    public DateTime? CanceledAt { get; set; }        // Soft delete timestamp
    public int? CanceledById { get; set; }           // FK → AppUsers (who canceled)

    // Navigation Properties
    public Company Company { get; set; }
    public AppUser User { get; set; }
    public AppUser CreatedBy { get; set; }
    public AppUser? CanceledBy { get; set; }
}
```

**Indexes:**
- `IX_Chores_CompanyId_Date` - Date range queries
- `IX_Chores_CompanyId_UserId_Date_CanceledAt` (UNIQUE where CanceledAt IS NULL) - One chore per user per day

**Constraints:**
- CompanyId + UserId + Date must be unique when CanceledAt IS NULL (one chore per user per day)
- Title is required
- Soft delete: CanceledAt + CanceledById

**Business Rules:**
- Mutually exclusive with shifts: User cannot have chore + shift on same day
- Soft delete preserves history
- Notifications sent on assignment and cancellation

---

### 10. OnDuties (On-Duty Role Assignments) **[GLOBAL TABLE]**

**Purpose:** On-duty role assignments (e.g., "Duty Officer", "Safety Officer") with cross-company visibility.

**File Reference:** `Models/OnDuty.cs`

**Schema:**
```csharp
public class OnDuty  // NO IBelongsToCompany - intentionally global
{
    public int Id { get; set; }
    // NO CompanyId - [G] Global visibility

    public int UserId { get; set; }                  // FK → AppUsers (cross-company reference)
    public DateOnly Date { get; set; }
    public OnDutyType OnDutyType { get; set; }       // Enum: Hakam(0)/Lead(1)

    public DateTime CreatedAt { get; set; }

    public DateTime? CanceledAt { get; set; }        // Soft delete
    public int? CanceledById { get; set; }           // FK → AppUsers

    // Navigation Properties
    public AppUser? User { get; set; }               // Nullable due to cross-company reference
    public AppUser? CanceledBy { get; set; }
}
```

**Indexes:**
- `IX_OnDuties_Date` - Date range queries
- `IX_OnDuties_Date_OnDutyType` - Type filtering
- `IX_OnDuties_UserId_Date_OnDutyType_CanceledAt` (UNIQUE where CanceledAt IS NULL) - One on-duty per user per type per day

**Constraints:**
- UserId + Date + OnDutyType must be unique when CanceledAt IS NULL
- NO CompanyId filter (global visibility)

**Business Rules:**
- **Cross-company visibility:** Users from Company A can see on-duty assignments for Company B
- Use case: Military base with multiple battalions, shared duty roster
- Soft delete preserves history
- Navigation properties marked nullable due to query filter issues (User may belong to different company)

**Enum: OnDutyType**
```csharp
public enum OnDutyType
{
    Hakam = 0,      // Hebrew: חק"מ (Duty Officer)
    Lead = 1        // Lead/Coordinator
}
```

---

### 11. OnDutyRoleSubscriptions (On-Duty Type Subscriptions)

**Purpose:** Users subscribe to specific on-duty types to receive notifications.

**File Reference:** `Models/OnDutyRoleSubscription.cs`

**Schema:**
```csharp
public class OnDutyRoleSubscription : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }               // [T] Tenant-scoped
    public int UserId { get; set; }                  // FK → AppUsers

    public int OnDutyTypeValue { get; set; }         // Which on-duty type (maps to OnDutyType enum)
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation Properties
    public Company Company { get; set; }
    public AppUser User { get; set; }
}
```

**Indexes:**
- `IX_OnDutyRoleSubscriptions_CompanyId_UserId_OnDutyTypeValue` (UNIQUE where IsActive=1) - One active subscription per user per type

**Constraints:**
- CompanyId + UserId + OnDutyTypeValue unique when IsActive=1
- UserId required (RESTRICT delete)

**Business Rules:**
- Notifications sent when user is assigned to subscribed on-duty type
- Multiple subscriptions allowed (subscribe to Hakam AND Lead)

---

### 12. OnDutyTypeConfigs (Global On-Duty Type Definitions) **[GLOBAL TABLE]**

**Purpose:** System-wide on-duty type configuration (names, icons, colors).

**File Reference:** `Models/OnDutyTypeConfig.cs`

**Schema:**
```csharp
public class OnDutyTypeConfig  // NO IBelongsToCompany - global config
{
    public int Id { get; set; }
    // NO CompanyId - [G] Global config

    public int TypeValue { get; set; }               // Unique - maps to OnDutyType enum
    public string NameEn { get; set; }               // English name
    public string NameHe { get; set; }               // Hebrew name
    public string? Icon { get; set; }                // UI icon (emoji or CSS class)
    public string? Color { get; set; }               // UI color (hex code)
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

**Indexes:**
- `IX_OnDutyTypeConfigs_TypeValue` (UNIQUE) - One config per type

**Constraints:**
- TypeValue must be unique
- NameEn and NameHe required

**Business Rules:**
- Seeded with default types (Hakam, Lead)
- Admins can add custom types
- Inactive types hidden in UI

---

## Team Collaboration Tables

### 13. TeamCalendars (User-Created Calendars)

**Purpose:** Users create custom calendars to aggregate team members' schedules.

**File Reference:** `Models/TeamCalendar.cs`

**Schema:**
```csharp
public class TeamCalendar : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }               // [T] Tenant-scoped
    public int OwnerId { get; set; }                 // FK → AppUsers (creator)

    public string Name { get; set; }                 // Calendar name
    public bool IsDeleted { get; set; }              // Soft delete
    public DateTime CreatedAt { get; set; }

    // Navigation Properties
    public Company Company { get; set; }
    public AppUser Owner { get; set; }
    public ICollection<TeamCalendarMember> Members { get; set; }
}
```

**Indexes:**
- `IX_TeamCalendars_CompanyId_OwnerId_Name` (UNIQUE where IsDeleted=0) - Unique name per owner

**Constraints:**
- CompanyId + OwnerId + Name unique when IsDeleted=0
- Name is required
- OwnerId required (RESTRICT delete)

**Business Rules:**
- Owner can add/remove members
- Calendar shows aggregated schedule of all members
- Soft delete preserves history

---

### 14. TeamCalendarMembers (Many-to-Many: Calendars to Users)

**Purpose:** Many-to-many relationship between calendars and member users.

**File Reference:** `Models/TeamCalendarMember.cs`

**Schema:**
```csharp
public class TeamCalendarMember : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }               // [T] Tenant-scoped
    public int TeamCalendarId { get; set; }          // FK → TeamCalendars (CASCADE delete)
    public int MemberUserId { get; set; }            // FK → AppUsers

    public int? SortOrder { get; set; }              // Optional manual ordering
    public DateTime CreatedAt { get; set; }

    // Navigation Properties
    public Company Company { get; set; }
    public TeamCalendar TeamCalendar { get; set; }
    public AppUser MemberUser { get; set; }
}
```

**Indexes:**
- `IX_TeamCalendarMembers_TeamCalendarId_MemberUserId` (UNIQUE) - Prevent duplicate memberships
- `IX_TeamCalendarMembers_MemberUserId` - Member's calendar list

**Constraints:**
- TeamCalendarId + MemberUserId must be unique
- Deleting calendar cascades to members (CASCADE delete)
- Deleting user restricts if memberships exist (RESTRICT on MemberUserId FK)

**Business Rules:**
- SortOrder allows manual member ordering in UI
- Deleting calendar removes all memberships

---

## Notification Tables

### 15. UserNotifications (In-App Notifications)

**Purpose:** In-app notification system (18 notification types).

**File Reference:** `Models/UserNotification.cs`

**Schema:**
```csharp
public class UserNotification : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }               // [T] Tenant-scoped
    public int UserId { get; set; }                  // FK → AppUsers (CASCADE delete)

    public NotificationType NotificationType { get; set; }  // Enum: 18 types
    public string Title { get; set; }
    public string Message { get; set; }

    public string? RelatedEntityType { get; set; }   // Polymorphic reference (e.g., "ShiftInstance")
    public int? RelatedEntityId { get; set; }

    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation Properties
    public Company Company { get; set; }
    public AppUser User { get; set; }
}
```

**Indexes:**
- `IX_UserNotifications_CompanyId_UserId_CreatedAt` - User's notification feed (sorted by date)
- `IX_UserNotifications_CompanyId_UserId_IsRead` - Unread count

**Constraints:**
- UserId required (CASCADE delete - removing user removes notifications)
- Title and Message required

**Business Rules:**
- Polymorphic reference allows linking to any entity type
- Notifications auto-deleted when user is deleted (CASCADE)
- Unread badge count: COUNT WHERE IsRead=0

**Enum: NotificationType (18 types)**
```csharp
public enum NotificationType
{
    ShiftAdded = 0,
    ShiftRemoved = 1,
    ShiftChanged = 2,
    TimeOffApproved = 3,
    TimeOffDeclined = 4,
    SwapRequestApproved = 5,
    SwapRequestDeclined = 6,
    ChoreAssigned = 7,
    ChoreCanceled = 8,
    OnDutyAssigned = 9,
    OnDutyCanceled = 10,
    RoleChanged = 11,
    ProfileUpdated = 12,
    NewSwapRequest = 13,
    NewTimeOffRequest = 14,
    TraineeShadowingAssigned = 15,
    AccountApproved = 16,
    AccountRejected = 17
}
```

---

### 16. DailyNotificationPreferences (Notification Digest Settings)

**Purpose:** User preferences for daily notification digest timing.

**File Reference:** `Models/DailyNotificationPreference.cs`

**Schema:**
```csharp
public class DailyNotificationPreference : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }               // [T] Tenant-scoped
    public int UserId { get; set; }                  // FK → AppUsers (CASCADE delete)

    public TimeOnly PreferredTime { get; set; }      // When to send digest (e.g., 07:00)
    public bool IsActive { get; set; }

    public bool IncludeUpcomingShifts { get; set; }
    public bool IncludePendingRequests { get; set; }
    public bool IncludeChores { get; set; }
    public bool IncludeOnDuty { get; set; }

    public DateTime CreatedAt { get; set; }

    // Navigation Properties
    public Company Company { get; set; }
    public AppUser User { get; set; }
}
```

**Indexes:**
- `IX_DailyNotificationPreferences_CompanyId_UserId` (UNIQUE where IsActive=1) - One active preference per user

**Constraints:**
- CompanyId + UserId unique when IsActive=1
- PreferredTime required

**Business Rules:**
- DailyNotificationJob runs on schedule, checks PreferredTime
- Multiple boolean flags control digest content
- Only one active preference per user

---

## Audit & Compliance Tables

### 17. AuditLogs (Comprehensive Audit Trail)

**Purpose:** Track all significant actions for compliance and forensics.

**File Reference:** `Models/AuditLog.cs`

**Schema:**
```csharp
public class AuditLog : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }               // [T] Tenant-scoped
    public int? UserId { get; set; }                 // FK → AppUsers (SET NULL on delete)

    // Denormalized user data (survives user deletion)
    public string? UserEmail { get; set; }
    public string? UserDisplayName { get; set; }

    public string Action { get; set; }               // What happened (e.g., "ShiftAssigned")
    public string? EntityType { get; set; }          // Polymorphic reference (e.g., "ShiftInstance")
    public int? EntityId { get; set; }
    public string? Details { get; set; }             // JSON - before/after state

    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    public DateTime Timestamp { get; set; }

    // Navigation Properties
    public Company Company { get; set; }
    public AppUser? User { get; set; }               // Nullable (SET NULL FK)
}
```

**Indexes:**
- `IX_AuditLogs_CompanyId_Timestamp` - Date range queries (most common)
- `IX_AuditLogs_CompanyId_Action` - Filter by action type
- `IX_AuditLogs_CompanyId_UserId_Timestamp` - User activity history

**Constraints:**
- Action is required
- Timestamp indexed for performance
- UserId is SET NULL on delete (preserve denormalized data)

**Business Rules:**
- Logged actions: Create shift, assign user, approve request, change role, update config, etc.
- Details field stores JSON (before/after state)
- IP address and User-Agent for forensics
- Retention: Keep indefinitely (compliance requirement)

---

### 18. RoleAssignmentAudits (Role Change Tracking)

**Purpose:** Track all role changes for security auditing.

**File Reference:** `Models/RoleAssignmentAudit.cs`

**Schema:**
```csharp
public class RoleAssignmentAudit : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }               // [T] Tenant-scoped

    public int TargetUserId { get; set; }            // FK → AppUsers (who was changed)
    public int FromRole { get; set; }                // Old role (UserRole enum)
    public int ToRole { get; set; }                  // New role
    public int ChangedById { get; set; }             // FK → AppUsers (who made change)

    public DateTime Timestamp { get; set; }

    // Navigation Properties
    public Company Company { get; set; }
    public AppUser TargetUser { get; set; }
    public AppUser ChangedBy { get; set; }
}
```

**Indexes:**
- `IX_RoleAssignmentAudits_CompanyId_TargetUserId_Timestamp` - User's role history
- `IX_RoleAssignmentAudits_Timestamp` - Recent changes

**Constraints:**
- TargetUserId, ChangedById, FromRole, ToRole all required
- Timestamp indexed

**Business Rules:**
- Created on every role change (automated via service layer)
- Cannot be deleted (permanent audit trail)
- Security auditors review for unauthorized privilege escalation

---

### 19. ProfileChangeAudits (Profile Modification History)

**Purpose:** Track changes to user profiles (email, display name, phone, etc.).

**File Reference:** `Models/ProfileChangeAudit.cs`

**Schema:**
```csharp
public class ProfileChangeAudit : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }               // [T] Tenant-scoped

    public int TargetUserId { get; set; }            // FK → AppUsers (whose profile changed)
    public int ChangedByUserId { get; set; }         // FK → AppUsers (who made change)

    public string FieldName { get; set; }            // Which field (e.g., "Email", "DisplayName")
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }

    public DateTime Timestamp { get; set; }

    // Navigation Properties
    public Company Company { get; set; }
    public AppUser TargetUser { get; set; }
    public AppUser ChangedByUser { get; set; }
}
```

**Indexes:**
- `IX_ProfileChangeAudits_CompanyId_TargetUserId_Timestamp` - User's change history
- `IX_ProfileChangeAudits_Timestamp` - Recent changes

**Constraints:**
- TargetUserId, ChangedByUserId, FieldName required
- OldValue and NewValue can be NULL

**Business Rules:**
- Created on profile update (automated via ProfileService)
- Tracks sensitive changes (email, phone, emergency contact)
- Retention: Keep indefinitely

---

## Configuration Tables

### 20. Configs (Key-Value Configuration Store)

**Purpose:** Tenant-specific configuration (e.g., RestHours=8, WeeklyHoursCap=40).

**File Reference:** `Models/AppConfig.cs` (named "Configs" in database)

**Schema:**
```csharp
public class AppConfig : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }               // [T] Tenant-scoped

    public string Key { get; set; }                  // Config key (e.g., "RestHours")
    public string Value { get; set; }                // Config value (e.g., "8")

    public DateTime CreatedAt { get; set; }

    // Navigation Properties
    public Company Company { get; set; }
}
```

**Indexes:**
- `IX_Configs_CompanyId_Key` - Fast key lookup per company

**Constraints:**
- Key and Value are required
- No unique constraint (allows duplicates - service layer handles)

**Business Rules:**
- Cached via AppConfigCacheService (5-minute TTL)
- Common keys: `RestHours`, `WeeklyHoursCap`, `GameEnabled`, `GameGridSize`, `GameMatchScore`

---

### 21. EmailConfigs (Email Service Configuration)

**Purpose:** Email provider configuration (one per company).

**File Reference:** `Models/EmailConfig.cs`

**Schema:**
```csharp
public class EmailConfig : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }               // [T] Tenant-scoped (UNIQUE)

    public string EncryptedApiKey { get; set; }      // API key encrypted at rest
    public string ApiUrl { get; set; }               // Email service endpoint
    public string FromAddress { get; set; }          // Sender email
    public bool Enabled { get; set; }

    public DateTime CreatedAt { get; set; }

    // Navigation Properties
    public Company Company { get; set; }
}
```

**Indexes:**
- `IX_EmailConfigs_CompanyId` (UNIQUE) - One config per company

**Constraints:**
- CompanyId must be unique (one-to-one relationship)
- EncryptedApiKey, ApiUrl, FromAddress required if Enabled=true

**Business Rules:**
- EncryptionService encrypts/decrypts ApiKey using ASP.NET Data Protection
- Email sending is optional (system works without email)
- MailService uses this config to send emails

---

### 22. EmailApiLogs (Email API Call Logs)

**Purpose:** Log all email API calls for debugging and compliance.

**File Reference:** `Models/EmailApiLog.cs`

**Schema:**
```csharp
public class EmailApiLog : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }               // [T] Tenant-scoped

    public string ToAddress { get; set; }
    public string Subject { get; set; }
    public string Body { get; set; }                 // Email HTML body

    public string? Request { get; set; }             // JSON request payload
    public string? Response { get; set; }            // JSON response
    public bool Success { get; set; }
    public string? ValidationErrors { get; set; }

    public DateTime CreatedAt { get; set; }

    // Navigation Properties
    public Company Company { get; set; }
}
```

**Indexes:**
- `IX_EmailApiLogs_CompanyId_CreatedAt` - Recent logs

**Constraints:**
- ToAddress, Subject, Body required
- Success is boolean

**Business Rules:**
- Created on every email send attempt
- Useful for debugging email delivery issues
- Retention: 90 days (configurable)

---

### 23. GriffinConfigs (Griffin ADFS Configuration)

**Purpose:** Griffin ADFS integration configuration for air-gapped SSO.

**File Reference:** `Models/GriffinConfig.cs`

**Schema:**
```csharp
public class GriffinConfig : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }               // [T] Tenant-scoped

    public string BaseUrl { get; set; }              // Griffin ADFS base URL
    public string TokenConsumerUrl { get; set; }     // Callback URL
    public bool AutoProvisionUsers { get; set; }     // Auto-create users on first login
    public int DefaultProvisionedRole { get; set; }  // UserRole enum (default: Employee)
    public int TimeoutSeconds { get; set; }          // HTTP timeout

    public DateTime CreatedAt { get; set; }

    // Navigation Properties
    public Company Company { get; set; }
}
```

**Indexes:**
- `IX_GriffinConfigs_CompanyId` - One config per company

**Constraints:**
- BaseUrl and TokenConsumerUrl required
- TimeoutSeconds default: 10

**Business Rules:**
- GriffinAuthenticationMiddleware uses this config
- AutoProvisionUsers creates user on first ADFS login
- DefaultProvisionedRole determines new user's role (usually Employee)

---

### 24. GriffinApiLogs (Griffin API Call Logs)

**Purpose:** Log all Griffin ADFS connection test calls for debugging, diagnostics, and compliance tracking.

**File Reference:** `Models/GriffinApiLog.cs`

**Schema:**
```csharp
public class GriffinApiLog : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }               // [T] Tenant-scoped

    public string RequestUrl { get; set; }           // Full URL contacted
    public string RequestMethod { get; set; }        // HTTP method (e.g., "GET", "POST")
    public string? RequestHeaders { get; set; }      // JSON serialized headers

    public int? ResponseStatusCode { get; set; }     // HTTP status (200, 401, 500, etc.)
    public string? ResponseBody { get; set; }        // Response content (truncated if large)
    public string? RedirectUrl { get; set; }         // Final URL after redirects

    public bool Success { get; set; }                // Test result
    public string? ErrorMessage { get; set; }        // Error details if failed
    public int DurationMs { get; set; }              // Request duration in milliseconds

    public string? ValidationErrors { get; set; }    // Configuration validation errors
    public DateTime Timestamp { get; set; }          // When test was performed

    // Navigation Properties
    public Company Company { get; set; }
}
```

**Indexes:**
- `IX_GriffinApiLogs_CompanyId_Timestamp` - Recent logs query performance
- `IX_GriffinApiLogs_CompanyId` - Company's all logs

**Constraints:**
- RequestUrl, RequestMethod required
- Success is boolean
- DurationMs >= 0

**Business Rules:**
- Created on every Griffin connection test (manual or automatic)
- Stores full diagnostic information for troubleshooting
- Used by Owner/GriffinConfig page to display recent tests
- Retention: 90 days (configurable, auto-cleanup recommended)
- Supports diagnostic export as .txt file for air-gapped troubleshooting

**Related Services:**
- `GriffinApiLogService.cs` - CRUD operations for log entries
- `GriffinConfigService.TestConnectionAsync()` - Creates log entries
- Owner/GriffinConfig page displays logs in diagnostic console

---

## API Infrastructure Tables

### 25. ApiKeys (API Key Management)

**Purpose:** API key authentication for external integrations.

**File Reference:** `Models/ApiKey.cs`

**Schema:**
```csharp
public class ApiKey  // Has CompanyId but NO query filter
{
    public int Id { get; set; }
    public int CompanyId { get; set; }               // FK → Companies (no query filter)
    public int CreatedById { get; set; }             // FK → AppUsers (RESTRICT delete)

    public string Name { get; set; }                 // Friendly name
    public string KeyHash { get; set; }              // Unique - bcrypt hash
    public string PlainTextKey { get; set; }         // WARNING: Security concern (owner access)

    public string Scopes { get; set; }               // Comma-separated (e.g., "user:read,shift:write")
    public int RateLimitPerMinute { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    // Navigation Properties (marked optional due to no query filter)
    public Company? Company { get; set; }
    public AppUser? CreatedBy { get; set; }
}
```

**Indexes:**
- `IX_ApiKeys_KeyHash` (UNIQUE) - Fast key lookup
- `IX_ApiKeys_CompanyId` - Company's API keys

**Constraints:**
- KeyHash must be unique
- Name, Scopes required
- CreatedById RESTRICT delete

**Business Rules:**
- PlainTextKey stored for owner retrieval (security concern noted)
- Scopes control endpoint access (validated by ApiAuthenticationMiddleware)
- RateLimitPerMinute enforced by ApiRateLimitingMiddleware
- LastUsedAt updated on every API call

---

### 26. ApiKeyRequests (API Key Approval Workflow)

**Purpose:** Request-approval workflow for API key generation.

**File Reference:** `Models/ApiKeyRequest.cs`

**Schema:**
```csharp
public class ApiKeyRequest  // Has CompanyId but NO query filter
{
    public int Id { get; set; }
    public int CompanyId { get; set; }               // FK → Companies (no query filter)
    public int RequestedById { get; set; }           // FK → AppUsers

    public string Name { get; set; }
    public string RequestedScopes { get; set; }      // Comma-separated
    public string? Reason { get; set; }

    public RequestStatus Status { get; set; }        // Pending/Approved/Rejected
    public int? ReviewedById { get; set; }           // FK → AppUsers
    public string? ApprovedScopes { get; set; }      // May differ from requested
    public int? ApprovedRateLimitPerMinute { get; set; }
    public int? GeneratedApiKeyId { get; set; }      // FK → ApiKeys (SET NULL on delete)

    public DateTime? ReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation Properties (marked optional)
    public Company? Company { get; set; }
    public AppUser? RequestedBy { get; set; }
    public AppUser? ReviewedBy { get; set; }
    public ApiKey? GeneratedApiKey { get; set; }
}
```

**Indexes:**
- `IX_ApiKeyRequests_CompanyId_Status` - Pending requests dashboard
- `IX_ApiKeyRequests_RequestedById` - User's request history

**Constraints:**
- Name, RequestedScopes required
- ReviewedById required if Status != Pending

**Business Rules:**
- On approval: Create ApiKey, set GeneratedApiKeyId
- ApprovedScopes can be subset of RequestedScopes (admin can reduce permissions)

---

### 27. ApiRequestLogs (API Usage Tracking)

**Purpose:** Log all API requests for analytics and debugging.

**File Reference:** `Models/ApiRequestLog.cs`

**Schema:**
```csharp
public class ApiRequestLog  // Has CompanyId but NO query filter
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }              // Nullable (not all requests have company)
    public int? ApiKeyId { get; set; }               // FK → ApiKeys (SET NULL on delete)

    public string Method { get; set; }               // GET/POST/PUT/DELETE
    public string Path { get; set; }                 // /api/v1/users
    public string? QueryString { get; set; }
    public string? RequestBody { get; set; }         // JSON (limited to 10KB)
    public string? ResponseBody { get; set; }        // JSON (limited to 10KB)
    public int StatusCode { get; set; }              // 200/400/401/404/500
    public int DurationMs { get; set; }              // Response time

    public string? CorrelationId { get; set; }       // Request tracing (GUID)
    public DateTime CreatedAt { get; set; }

    // Navigation Properties (marked optional)
    public Company? Company { get; set; }
    public ApiKey? ApiKey { get; set; }
}
```

**Indexes:**
- `IX_ApiRequestLogs_CreatedAt` - Recent logs
- `IX_ApiRequestLogs_ApiKeyId_CreatedAt` - API key usage history
- `IX_ApiRequestLogs_CorrelationId` - Request tracing

**Constraints:**
- Method, Path, StatusCode, DurationMs required
- CompanyId and ApiKeyId nullable

**Business Rules:**
- Created by ApiRequestLoggingMiddleware on every API call
- CorrelationId enables distributed tracing
- Retention: 30 days (configurable)

---

## Multi-Tenancy Cross-Company Tables

### 28. DirectorCompanies (Cross-Company Director Access)

**Purpose:** Many-to-many mapping allowing directors to access multiple companies.

**File Reference:** `Models/DirectorCompany.cs`

**Schema:**
```csharp
public class DirectorCompany  // NO query filter - intentionally cross-tenant
{
    public int Id { get; set; }
    // NO CompanyId scoping - this IS the cross-company mapping

    public int UserId { get; set; }                  // FK → AppUsers (Director user)
    public int CompanyId { get; set; }               // FK → Companies (accessible company)
    public int GrantedById { get; set; }             // FK → AppUsers (who granted access)

    public bool IsDeleted { get; set; }              // Soft delete
    public DateTime CreatedAt { get; set; }

    // Navigation Properties (marked optional due to no query filter)
    public AppUser? User { get; set; }
    public Company? Company { get; set; }
    public AppUser? GrantedBy { get; set; }
}
```

**Indexes:**
- `IX_DirectorCompanies_UserId_CompanyId` (UNIQUE where IsDeleted=0) - One mapping per user-company pair
- `IX_DirectorCompanies_UserId` - Director's accessible companies

**Constraints:**
- UserId + CompanyId unique when IsDeleted=0
- UserId must have Role=Director (validated in service layer)

**Business Rules:**
- Directors can switch company context via CompanyFilter page
- CompanyContext service uses this table to resolve accessible companies
- Only Owners can grant/revoke director access

---

## Gamification Tables

### 29. GameScores (Leaderboard)

**Purpose:** Shift swap game leaderboard (Match-3 easter egg).

**File Reference:** `Models/GameScore.cs`

**Schema:**
```csharp
public class GameScore : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }               // [T] Tenant-scoped
    public int UserId { get; set; }                  // FK → AppUsers

    public int Score { get; set; }
    public string CurrentMonth { get; set; }         // YYYY-MM format (e.g., "2025-12")
    public DateTime PlayedAt { get; set; }

    // Navigation Properties
    public Company Company { get; set; }
    public AppUser User { get; set; }
}
```

**Indexes:**
- `IX_GameScores_CompanyId_Score_DESC` - All-time leaderboard (sorted descending)
- `IX_GameScores_CompanyId_CurrentMonth_Score_DESC` - Monthly leaderboard

**Constraints:**
- Score must be >= 0
- CurrentMonth format: "YYYY-MM"

**Business Rules:**
- Game accessible via Ctrl+Click on logo (easter egg)
- Leaderboards: All-Time (top 10) and Monthly (top 10)
- Configurable by owners (game enabled, grid size, scoring)

---

### 30. Feedbacks (User Feedback)

**Purpose:** User feedback submission (bugs, features, general).

**File Reference:** `Models/Feedback.cs`

**Schema:**
```csharp
public class Feedback : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }               // [T] Tenant-scoped
    public int? UserId { get; set; }                 // FK → AppUsers (nullable - anonymous feedback)

    public int FeedbackType { get; set; }            // Bug/Feature/General
    public string Content { get; set; }
    public string? ImageFileName { get; set; }       // Optional screenshot
    public int Status { get; set; }                  // New/InProgress/Resolved/Closed

    public DateTime CreatedAt { get; set; }

    // Navigation Properties
    public Company Company { get; set; }
    public AppUser? User { get; set; }
}
```

**Indexes:**
- `IX_Feedbacks_CompanyId_Status` - Feedback dashboard
- `IX_Feedbacks_CreatedAt` - Recent feedback

**Constraints:**
- Content is required
- UserId nullable (allows anonymous feedback)

**Business Rules:**
- Accessible via /Public/Feedback page
- Screenshot upload optional (stored in wwwroot/feedback/)
- Owners can update status

---

## Indexes & Performance

### Critical Performance Indexes

**Most Critical (Calendar Queries):**
1. `IX_ShiftInstances_CompanyId_WorkDate` - **Used on every calendar page load**
2. `IX_ShiftAssignments_CompanyId_ShiftInstanceId_UserId` - Assignment lookups

**Audit Query Indexes:**
3. `IX_AuditLogs_CompanyId_Timestamp` - Date range audit queries
4. `IX_UserNotifications_CompanyId_UserId_CreatedAt` - Notification feed

**Foreign Key Indexes:**
- All foreign keys have indexes for join performance

### Index Coverage Analysis

**Query:** "Show me all shifts in December 2025 for Company 1"
```sql
SELECT * FROM ShiftInstances
WHERE CompanyId = 1
  AND WorkDate >= '2025-12-01'
  AND WorkDate <= '2025-12-31';
```
**Index Used:** `IX_ShiftInstances_CompanyId_WorkDate` ✅ Optimized

---

## Foreign Key Relationships

### Cascade Behavior Summary

| Parent Table | Child Table | Delete Behavior | Rationale |
|--------------|-------------|-----------------|-----------|
| Companies | AppUsers | RESTRICT | Prevent accidental company deletion |
| Companies | ShiftTypes | RESTRICT | Prevent accidental company deletion |
| ShiftInstances | ShiftAssignments | CASCADE | Deleting shift deletes assignments |
| ShiftTypes | ShiftInstances | RESTRICT | Prevent deleting in-use shift types |
| AppUsers | ShiftAssignments | RESTRICT | Prevent deleting users with shifts |
| AppUsers | TimeOffRequests | RESTRICT | Preserve request history |
| AppUsers | UserNotifications | CASCADE | Clean up notifications with user |
| AppUsers | AuditLogs | SET NULL | Preserve audit trail, denormalize user data |
| TeamCalendars | TeamCalendarMembers | CASCADE | Deleting calendar removes members |
| ApiKeys | ApiRequestLogs | SET NULL | Preserve logs after key deletion |

### Nullable Foreign Keys

**Intentionally Nullable FKs:**
- `ShiftAssignment.UserId` - Allows unassigned slots
- `ShiftAssignment.TraineeUserId` - Trainee shadowing is optional
- `TimeOffRequest.ApproverId` - Designated approver is optional
- `AuditLog.UserId` - User might be deleted (denormalized data preserved)
- `Feedback.UserId` - Anonymous feedback allowed

---

## Value Converters

### SQLite Type Conversions

**DateOnly → string ("YYYY-MM-DD")**
```csharp
modelBuilder.Entity<ShiftInstance>()
    .Property(e => e.WorkDate)
    .HasConversion(
        v => v.ToString("yyyy-MM-dd"),
        v => DateOnly.Parse(v)
    );
```

**TimeOnly → string ("HH:mm")**
```csharp
modelBuilder.Entity<ShiftType>()
    .Property(e => e.Start)
    .HasConversion(
        v => v.ToString("HH:mm"),
        v => TimeOnly.Parse(v)
    );
```

**Applied To:**
- ShiftType.Start, ShiftType.End
- ShiftInstance.WorkDate
- TimeOffRequest.StartDate, EndDate
- Chore.Date
- OnDuty.Date
- DailyNotificationPreference.PreferredTime

---

## Constraints & Validation

### Unique Constraints

**Simple Unique:**
- `Companies.Slug`
- `AppUsers.Email`
- `ApiKeys.KeyHash`
- `OnDutyTypeConfigs.TypeValue`

**Composite Unique:**
- `ShiftTypes(CompanyId, Key)`
- `ShiftAssignments(CompanyId, ShiftInstanceId, UserId)` where UserId NOT NULL
- `Chores(CompanyId, UserId, Date)` where CanceledAt IS NULL
- `OnDuties(UserId, Date, OnDutyType)` where CanceledAt IS NULL

**Filtered Unique (Soft Deletes):**
- `DirectorCompanies(UserId, CompanyId)` where IsDeleted=0
- `TeamCalendars(CompanyId, OwnerId, Name)` where IsDeleted=0
- `DailyNotificationPreferences(CompanyId, UserId)` where IsActive=1

### Data Annotations

**Common Validations:**
- `[Required]` - Non-nullable strings
- `[MaxLength(200)]` - DisplayName, Name fields
- `[MaxLength(256)]` - Email fields
- `[EmailAddress]` - Email format validation

---

## Summary

This comprehensive database schema supports:
- **Multi-Tenancy:** 25 tenant-scoped tables with automatic isolation
- **Shift Scheduling:** Complete hierarchy (Types → Instances → Assignments)
- **Request Workflows:** Time-off, swaps, signup approvals
- **Audit & Compliance:** Comprehensive audit trails for regulatory requirements
- **Cross-Company Coordination:** OnDuty global visibility, Director cross-company access
- **Gamification:** Easter egg game with leaderboards

**Total Storage:** 28 tables, 36 migrations, ~50-200 MB typical size

**Performance:** Optimized indexes for calendar queries (critical path), audit queries, and foreign key joins

**Security:** Row-level multi-tenancy, soft deletes, denormalized audit data, encrypted API keys

---

**Next Steps:**
- [➡️ Read Startup & Middleware](04-STARTUP-AND-MIDDLEWARE.md) to understand how the application bootstraps
- [➡️ Read Domain Models](06-DOMAIN-MODELS.md) for detailed entity business logic
- [➡️ Read Multi-Tenancy Deep Dive](05-MULTI-TENANCY-DEEP-DIVE.md) for tenant isolation implementation
- [⬅️ Return to Index](00-INDEX.md) for full documentation map

---

**Document Status:** ✅ Complete
**Cross-References:** [database-erd.mmd](diagrams/database-erd.mmd), [06-DOMAIN-MODELS.md](06-DOMAIN-MODELS.md), [05-MULTI-TENANCY-DEEP-DIVE.md](05-MULTI-TENANCY-DEEP-DIVE.md)
