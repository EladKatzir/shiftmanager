# ShiftManager - Complete Database Schema
## Comprehensive Data Model Documentation

**Document Version:** 2.0 (V3 Update)
**Last Updated:** 2026-01-29
**Database Provider:** SQLite (via EF Core 9.0.9)
**Total Tables:** 71 entities (includes V3 hierarchy, authorization, localization, and post-V3 additions)

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
15. [V3 Organizational Hierarchy Tables](#v3-organizational-hierarchy-tables)
16. [V3 Authorization Tables](#v3-authorization-tables)
17. [V3 Scheduling Tables](#v3-scheduling-tables)
18. [V3 Setup and Workflow Tables](#v3-setup-and-workflow-tables)
19. [Additional Entities (Post-V3)](#additional-entities-post-v3)
20. [Indexes & Performance](#indexes--performance)
20. [Foreign Key Relationships](#foreign-key-relationships)
21. [Value Converters](#value-converters)
22. [Constraints & Validation](#constraints--validation)

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

### 24. EmailTemplateCustomizations (Email Template Overrides)

**Purpose:** Company-specific customizations to email templates.

**File Reference:** `Models/EmailTemplateCustomization.cs`

**Schema:**
```csharp
public class EmailTemplateCustomization : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }               // [T] Tenant-scoped

    public EmailTemplateType TemplateType { get; set; }  // Which template
    public string CustomMessage { get; set; }            // Max 2000 chars
    public bool IsEnabled { get; set; }                  // Use custom vs default

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int CreatedBy { get; set; }
    public int? UpdatedBy { get; set; }

    // Navigation Properties
    public Company Company { get; set; }
}
```

**Indexes:**
- `IX_EmailTemplateCustomizations_CompanyId_TemplateType` (UNIQUE) - One customization per template type per company

**Business Rules:**
- If IsEnabled=false, system uses default email template
- CustomMessage supports placeholders like {EmployeeName}, {Date}
- HTML-encoded for security

---

### 25. CompanyLanguageSettings (Language Configuration)

**Purpose:** Company-specific language configuration (default and alternate languages).

**File Reference:** `Models/CompanyLanguageSettings.cs`

**Schema:**
```csharp
public class CompanyLanguageSettings : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }               // [T] Tenant-scoped (UNIQUE)

    public string DefaultCulture { get; set; }       // "en-US" or "he-IL"
    public string AlternateCulture { get; set; }     // The other culture

    public DateTime CreatedAt { get; set; }
    public int CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int UpdatedBy { get; set; }

    // Navigation Properties
    public Company? Company { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public AppUser? UpdatedByUser { get; set; }
}
```

**Indexes:**
- `IX_CompanyLanguageSettings_CompanyId` (UNIQUE) - One setting per company

**Business Rules:**
- DefaultCulture shown to new users/users without preference
- AlternateCulture available via language toggle
- Both must be valid cultures ("en-US" or "he-IL")

---

### 26. CompanyLocalizationOverrides (Custom Translations)

**Purpose:** Company-specific custom translations for UI strings.

**File Reference:** `Models/CompanyLocalizationOverride.cs`

**Schema:**
```csharp
public class CompanyLocalizationOverride : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }               // [T] Tenant-scoped

    public string Culture { get; set; }              // "en-US" or "he-IL"
    public string ResourceKey { get; set; }          // e.g., "Button_Save", max 200 chars
    public string OverrideValue { get; set; }        // Custom translation, max 2000 chars

    public bool IsActive { get; set; }               // Soft delete
    public DateTime CreatedAt { get; set; }
    public int CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int UpdatedBy { get; set; }

    // Navigation Properties
    public Company? Company { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public AppUser? UpdatedByUser { get; set; }
}
```

**Indexes:**
- `IX_CompanyLocalizationOverrides_CompanyId_Culture_ResourceKey` (UNIQUE where IsActive=1) - One override per key per culture per company

**Business Rules:**
- Allows companies to customize any localization string
- OverrideValue is HTML-encoded for security
- Soft delete via IsActive flag
- Used by CompanyLocalizationService to override default translations

---

### 27. GriffinApiLogs (Griffin API Connection Test Logs)

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

### 28. ApiKeys (API Key Management)

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

### 29. ApiKeyRequests (API Key Approval Workflow)

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

### 30. ApiRequestLogs (API Usage Tracking)

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

### 31. DirectorCompanies (Cross-Company Director Access)

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

### 32. GameScores (Leaderboard)

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

### 33. Feedbacks (User Feedback)

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

## V3 Organizational Hierarchy Tables

> **See Also:** [21-V3-ORGANIZATIONAL-HIERARCHY.md](21-V3-ORGANIZATIONAL-HIERARCHY.md) for complete hierarchy documentation

### 34. Projects (Top-Level Organizational Container)

**Purpose:** Top-level container for the entire organizational hierarchy.

**File Reference:** `Models/Project.cs`

**Schema:**
```csharp
public class Project
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public List<Area> Areas { get; set; } = new();
}
```

---

### 35. Areas (Regional/Divisional Grouping)

**Purpose:** Regional or divisional grouping within a Project. Contains Molecules and defines shared JobTypes.

**File Reference:** `Models/Area.cs`

**Schema:**
```csharp
public class Area
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Project Project { get; set; } = null!;
    public List<Molecule> Molecules { get; set; } = new();
    public List<JobType> JobTypes { get; set; } = new();
    public AreaSettings? Settings { get; set; }
}
```

---

### 36. Molecules (Operational Units)

**Purpose:** Operational unit within an Area. Type determines structure (Workforce/Tech/Helper/System).

**File Reference:** `Models/Molecule.cs`

**Schema:**
```csharp
public class Molecule
{
    public int Id { get; set; }
    public int AreaId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public MoleculeType Type { get; set; }  // Workforce/Tech/Helper/System
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Area Area { get; set; } = null!;
    public List<Company> Companies { get; set; } = new();       // Workforce molecules
    public List<Department> Departments { get; set; } = new();  // Tech molecules
    public List<ShiftGrouping> ShiftGroupings { get; set; } = new();
    public MoleculeSettings? Settings { get; set; }
}
```

**Enum: MoleculeType**
```csharp
public enum MoleculeType
{
    Workforce = 0,  // Operational staff with Companies
    Tech = 1,       // Technical staff with Departments
    Helper = 2,     // Support/chore assignments
    System = 3      // Administrative/system users
}
```

---

### 37. Departments (Tech Unit)

**Purpose:** Technical department within a Tech molecule.

**File Reference:** `Models/Department.cs`

**Schema:**
```csharp
public class Department
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Molecule Molecule { get; set; } = null!;
    public List<AppUser> Users { get; set; } = new();
}
```

---

### 38. JobTypes (Job Role Categories)

**Purpose:** Job role category defined at the Area level. Users have a JobType for shift eligibility.

**File Reference:** `Models/JobType.cs`

**Schema:**
```csharp
public class JobType
{
    public int Id { get; set; }
    public int AreaId { get; set; }  // Area-scoped
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Color { get; set; }  // Hex color for UI
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Area Area { get; set; } = null!;
    public List<AppUser> Users { get; set; } = new();
}
```

---

### 39-41. Settings Tables

**AreaSettings, MoleculeSettings, CompanySettings:** Hierarchy-level configuration.

```csharp
public class AreaSettings
{
    public int Id { get; set; }
    public int AreaId { get; set; }
    public int DefaultRestHours { get; set; } = 8;
    public int DefaultWeeklyCap { get; set; } = 56;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
```

---

## V3 Authorization Tables

> **See Also:** [22-V3-GRANT-AUTHORIZATION.md](22-V3-GRANT-AUTHORIZATION.md) for complete grant documentation

### 42. Grants (Individual Permissions)

**Purpose:** Individual permission granted to a user with hierarchical scope.

**File Reference:** `Models/Grant.cs`

**Schema:**
```csharp
public class Grant
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int GrantTypeId { get; set; }

    // Hierarchical Scope
    public int? ProjectId { get; set; }
    public int? AreaId { get; set; }
    public int? MoleculeId { get; set; }
    public int? DepartmentId { get; set; }
    public int? CompanyId { get; set; }
    public int? JobTypeId { get; set; }

    // Capabilities
    public bool CanOwn { get; set; }   // Can perform the action
    public bool CanGive { get; set; }  // Can grant to others

    // Audit
    public int? GrantedByUserId { get; set; }
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
    public bool IsAutoGrant { get; set; }  // from role template
}
```

---

### 43. GrantTypes (Permission Definitions)

**Purpose:** Definition of a permission/capability that can be granted. 90+ system grant types.

**File Reference:** `Models/GrantType.cs`

**Schema:**
```csharp
public class GrantType
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;  // "AssignAlhutShifts"
    public string NameKey { get; set; } = string.Empty;
    public string DescriptionKey { get; set; } = string.Empty;
    public GrantCategory Category { get; set; }
    public GrantScopeLevel DefaultScope { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

**Enum: GrantCategory**
```csharp
public enum GrantCategory
{
    Shift = 0, Duty = 1, Chore = 2, Vacation = 3, Swap = 4,
    UserManagement = 5, GrantManagement = 6, Hierarchy = 7,
    Settings = 8, Analytics = 9, Email = 10, System = 11, Custom = 99
}
```

---

### 44. RoleTemplates (Role Bundles)

**Purpose:** Bundle of grants that can be assigned together. 11 system role templates.

**File Reference:** `Models/RoleTemplate.cs`

**Schema:**
```csharp
public class RoleTemplate
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;  // "BRDirector", "AlhutLead"
    public string NameKey { get; set; } = string.Empty;
    public string DescriptionKey { get; set; } = string.Empty;
    public RoleScopeLevel ScopeLevel { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

---

### 45. RoleTemplateGrants (Role → Grant Mapping)

**Purpose:** Join table defining which grants a RoleTemplate provides.

**File Reference:** `Models/RoleTemplateGrant.cs`

---

### 46. UserRoleAssignments (User → Role Mapping)

**Purpose:** Assigns a RoleTemplate to a user with a specific scope.

**File Reference:** `Models/UserRoleAssignment.cs`

**Schema:**
```csharp
public class UserRoleAssignment
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int RoleTemplateId { get; set; }

    // Scope of this role assignment
    public int? CompanyId { get; set; }
    public int? DepartmentId { get; set; }
    public int? MoleculeId { get; set; }
    public int? AreaId { get; set; }
    public int? JobTypeId { get; set; }

    // Audit
    public int AssignedByUserId { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}
```

---

## V3 Scheduling Tables

> **See Also:** [23-V3-SCHEDULING-SYSTEM.md](23-V3-SCHEDULING-SYSTEM.md) for complete scheduling documentation

### 47. ShiftGroupings (Shift Organization)

**Purpose:** Groups companies within a Molecule for coordinated shift scheduling.

**File Reference:** `Models/ShiftGrouping.cs`

**Schema:**
```csharp
public class ShiftGrouping
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

---

### 48-49. ShiftGrouping Join Tables

**ShiftGroupingCompany:** Links companies to shift groupings.
**ShiftGroupingJobType:** Links job types to shift groupings.

---

### 50. ShiftPrograms (Weekly Templates)

**Purpose:** Weekly template for generating shift instances.

**File Reference:** `Models/ShiftProgram.cs`

**Schema:**
```csharp
public class ShiftProgram
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int ShiftTypeId { get; set; }

    // V3 Scope
    public int? JobTypeId { get; set; }
    public int? ShiftGroupingId { get; set; }
    public string? TechShiftType { get; set; }

    public string Name { get; set; } = string.Empty;
    public int DefaultStaffingRequired { get; set; } = 1;
    public bool IsActive { get; set; } = true;
}
```

---

### 51. ProgramDays (Day-of-Week Config)

**Purpose:** Defines which days of the week a Program runs.

**File Reference:** `Models/ProgramDay.cs`

---

### 52. MasterPrograms (Program Collections)

**Purpose:** Collection of Programs forming a complete weekly schedule.

**File Reference:** `Models/MasterProgram.cs`

---

### 53. MasterProgramItems (Program Membership)

**Purpose:** Join table linking MasterProgram to Programs.

**File Reference:** `Models/MasterProgramItem.cs`

---

## V3 Setup and Workflow Tables

### 54. SetupTasks (Onboarding Tasks)

**Purpose:** Guided onboarding tasks for setting up new organizational units.

**File Reference:** `Models/SetupTask.cs`

**Schema:**
```csharp
public class SetupTask
{
    public int Id { get; set; }
    public SetupTaskType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // Context
    public int? MoleculeId { get; set; }
    public int? CompanyId { get; set; }
    public int? JobTypeId { get; set; }

    // Assignment
    public int AssignedToUserId { get; set; }
    public SetupTaskStatus Status { get; set; }

    // Audit
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
```

---

### 55. UserFriendships (Social Features)

**Purpose:** User-to-user friendship connections for social features.

**File Reference:** `Models/UserFriendship.cs`

---

## V3 Entity Summary

| Category | Tables Added | Purpose |
|----------|--------------|---------|
| **Hierarchy** | Projects, Areas, Molecules, Departments, JobTypes, Settings (6) | Organizational structure |
| **Authorization** | Grants, GrantTypes, RoleTemplates, RoleTemplateGrants, UserRoleAssignments (5) | Grant-based permissions |
| **Scheduling** | ShiftGroupings, ShiftGroupingCompany, ShiftGroupingJobType, ShiftPrograms, ProgramDays, MasterPrograms, MasterProgramItems (7) | Enhanced scheduling |
| **Workflow** | SetupTasks, UserFriendships (2) | Onboarding and social |

**Total V3 Tables Added:** 23 new tables (hierarchy, authorization, scheduling, workflow)

---

## Additional Entities (Post-V3)

The following 16 entities were added after the V3 launch to support home rotation, chore categorization, duty rotation automation, telemetry, announcements, vacation workflow rules, and feature flags.

---

### 56. HomeTypes (Home Rotation Templates)

**Purpose:** Named rotation template defining when users are home. Admin paints home days on a monthly calendar; the system derives a recurrence rule. Users assigned to a HomeType follow its rotation pattern for HOME shift generation.

**File Reference:** `Models/HomeType.cs`

**Schema:**
```csharp
public class HomeType : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int MoleculeId { get; set; }
    public string Name { get; set; }          // e.g., "סבב א"
    public string? NameHe { get; set; }
    public string? PatternJson { get; set; }   // JSON array of painted dates (yyyy-MM-dd)
    public string? DerivedRule { get; set; }    // JSON: { cycleWeeks, homeDays, weekOffsets }
    public TimeOnly? DefaultStartTime { get; set; }
    public TimeOnly? DefaultEndTime { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public int CreatedBy { get; set; }
}
```

**Relationships:** FK to Molecules (MoleculeId), FK to AppUsers (CreatedBy). Tenant-scoped via IBelongsToCompany.

---

### 57. HomeTypeOverrides (Per-User Pattern Deviations)

**Purpose:** Per-user pattern deviation from a HomeType template. Created when admin explicitly overrides a specific user's home pattern. Most users have no override.

**File Reference:** `Models/HomeTypeOverride.cs`

**Schema:**
```csharp
public class HomeTypeOverride : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int HomeTypeId { get; set; }
    public int UserId { get; set; }
    public string OverridePatternJson { get; set; } // JSON array of date strings
    public DateTime CreatedAt { get; set; }
    public int CreatedBy { get; set; }
}
```

**Relationships:** FK to HomeTypes (HomeTypeId), FK to AppUsers (UserId, CreatedBy). Tenant-scoped via IBelongsToCompany.

---

### 58. ChoreTypes (Chore Category Definitions)

**Purpose:** Categorizes chores by type within a molecule. Each ChoreType has a display name, optional color, and sort order.

**File Reference:** `Models/ChoreType.cs`

**Schema:**
```csharp
public class ChoreType
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }
    public string Name { get; set; }
    public string DisplayName { get; set; }
    public string? Color { get; set; }          // Hex color e.g. "#F0C14B"
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public int CreatedByUserId { get; set; }
}
```

**Relationships:** FK to Molecules (MoleculeId), FK to AppUsers (CreatedByUserId). One-to-many to Chores.

**Note:** Not tenant-scoped (no IBelongsToCompany) -- scoped via Molecule instead.

---

### 59. ShiftCapacityOverrides (Date-Specific Capacity)

**Purpose:** Overrides the default staffing capacity for a specific shift type on a specific date. Allows managers to increase or decrease capacity for holidays, special events, etc.

**File Reference:** `Models/ShiftCapacityOverride.cs`

**Schema:**
```csharp
public class ShiftCapacityOverride
{
    public int Id { get; set; }
    public int ShiftTypeId { get; set; }
    public int MoleculeId { get; set; }
    public int? JobTypeId { get; set; }
    public DateOnly Date { get; set; }
    public int Capacity { get; set; }
    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

**Relationships:** FK to ShiftTypes (ShiftTypeId), FK to Molecules (MoleculeId), FK to JobTypes (JobTypeId, nullable), FK to AppUsers (CreatedByUserId).

---

### 60. UserDayNotes (Calendar Day Notes)

**Purpose:** Free-text notes attached to a specific user on a specific date. Displayed as overlays on the calendar grid.

**File Reference:** `Models/UserDayNote.cs`

**Schema:**
```csharp
public class UserDayNote : IBelongsToCompany
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateOnly Date { get; set; }
    public int CompanyId { get; set; }
    public string Note { get; set; }
    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
```

**Relationships:** FK to AppUsers (UserId, CreatedByUserId), FK to Companies (CompanyId). Tenant-scoped via IBelongsToCompany.

---

### 61. FeatureFlags (Feature Toggle System)

**Purpose:** Controls feature availability with hierarchical scoping: global, per-company, or per-user. Resolution priority: User+Company > Company > Global.

**File Reference:** `Models/FeatureFlag.cs`

**Schema:**
```csharp
public class FeatureFlag
{
    public int Id { get; set; }
    public string Name { get; set; }           // e.g., "FF_WIDGETS_ENABLED"
    public bool IsEnabled { get; set; }
    public string? Description { get; set; }
    public int? CompanyId { get; set; }        // null = global flag
    public int? UserId { get; set; }           // null = all users in scope
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

**Relationships:** Optional FK to Companies (CompanyId), optional FK to AppUsers (UserId).

**Note:** Not tenant-scoped (no IBelongsToCompany) -- service uses IgnoreQueryFilters() with explicit userId/companyId params for resolution.

---

### 62-64. Telemetry Tables (Client-Side Observability)

**Purpose:** Air-gapped local observability -- no external analytics dependencies. Three tables capture UI interactions, JavaScript errors, and Core Web Vitals.

**File References:** `Models/Telemetry/ClientAnalyticsEvent.cs`, `Models/Telemetry/ClientError.cs`, `Models/Telemetry/PerformanceMetric.cs`

#### 62. ClientAnalyticsEvents

```csharp
public class ClientAnalyticsEvent
{
    public long Id { get; set; }
    public string EventType { get; set; }      // e.g., "calendar_view_changed", "scope_changed"
    public string? EventData { get; set; }     // JSON payload
    public string UserIdHash { get; set; }     // SHA256 hash -- no PII stored
    public string? SessionId { get; set; }
    public string PageUrl { get; set; }        // Path only, no PII in query params
    public string? UserAgent { get; set; }
    public DateTime Timestamp { get; set; }
}
```

#### 63. ClientErrors

```csharp
public class ClientError
{
    public long Id { get; set; }
    public string Message { get; set; }        // PII scrubbed
    public string? StackTrace { get; set; }
    public string? Source { get; set; }
    public int? LineNumber { get; set; }
    public int? ColumnNumber { get; set; }
    public string? ErrorType { get; set; }     // e.g., "TypeError", "unhandledrejection"
    public string PageUrl { get; set; }
    public string? UserAgent { get; set; }
    public string UserIdHash { get; set; }
    public DateTime Timestamp { get; set; }
    public string? BrowserInfo { get; set; }
}
```

#### 64. PerformanceMetrics

```csharp
public class PerformanceMetric
{
    public long Id { get; set; }
    public string MetricName { get; set; }     // LCP, FID, INP, CLS, TTFB
    public double Value { get; set; }          // ms for timing, unitless for CLS
    public string? Rating { get; set; }        // "good", "needs-improvement", "poor"
    public string PageUrl { get; set; }
    public string? UserAgent { get; set; }
    public string? ConnectionType { get; set; }
    public string? EffectiveType { get; set; }
    public double? DeviceMemory { get; set; }
    public int? HardwareConcurrency { get; set; }
    public DateTime Timestamp { get; set; }
    public string? BrowserInfo { get; set; }
}
```

**Note:** All three telemetry tables are global (no CompanyId, no IBelongsToCompany). Primary keys use `long` for high-volume writes.

---

### 65-67. DutyRotation Tables (Automated On-Duty Assignment)

**Purpose:** Automates on-duty shift assignments via configurable rotation queues. Supports daily/weekly/biweekly/monthly frequencies, skip logic for vacations/conflicts, and full audit logging.

**File References:** `Models/DutyRotation.cs`, `Models/DutyRotationEntry.cs`, `Models/DutyRotationLog.cs`

#### 65. DutyRotations

```csharp
public class DutyRotation : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public OnDutyType DutyType { get; set; }
    public RotationFrequency Frequency { get; set; }  // Daily=0, Weekly=1, Biweekly=2, Monthly=3
    public string Name { get; set; }
    public bool IsActive { get; set; }
    public bool IncludeWeekends { get; set; }
    public int MaxConsecutiveDays { get; set; }
    public int CurrentQueuePosition { get; set; }
    public DateOnly? LastAssignedDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public int CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
```

**Relationships:** FK to Companies (CompanyId). One-to-many to DutyRotationEntries and DutyRotationLogs.

#### 66. DutyRotationEntries

```csharp
public class DutyRotationEntry
{
    public int Id { get; set; }
    public int DutyRotationId { get; set; }
    public int UserId { get; set; }
    public int Position { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

**Relationships:** FK to DutyRotations (DutyRotationId), FK to AppUsers (UserId).

#### 67. DutyRotationLogs

```csharp
public class DutyRotationLog
{
    public int Id { get; set; }
    public int DutyRotationId { get; set; }
    public int? AssignedUserId { get; set; }
    public int? SkippedUserId { get; set; }
    public string? SkipReason { get; set; }    // VACATION, CONFLICT, INACTIVE, RANK
    public DateOnly AssignmentDate { get; set; }
    public int? OnDutyId { get; set; }         // Links to created OnDuty record
    public bool WasAutoAssigned { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

**Relationships:** FK to DutyRotations (DutyRotationId), FK to AppUsers (AssignedUserId, SkippedUserId), FK to OnDuties (OnDutyId).

---

### 68. Announcements (Company Announcements Feed)

**Purpose:** Company-wide announcements with scoped visibility (all employees, specific department, or specific role). Supports markdown content, pinning, and expiration.

**File Reference:** `Models/Announcement.cs`

**Schema:**
```csharp
public class Announcement : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string Title { get; set; }
    public string Content { get; set; }        // Supports markdown
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }   // null = never expires
    public AnnouncementScope Scope { get; set; } // All=0, Department=1, Role=2
    public int? TargetDepartmentId { get; set; }
    public string? TargetRole { get; set; }
    public bool IsPinned { get; set; }
    public bool IsActive { get; set; }
}
```

**Enum -- AnnouncementScope:**
```csharp
public enum AnnouncementScope
{
    All = 0,           // Visible to all employees in company
    Department = 1,    // Visible to specific department
    Role = 2           // Visible to specific role
}
```

**Relationships:** FK to Companies (CompanyId), FK to AppUsers (CreatedBy), optional FK to Departments (TargetDepartmentId). Tenant-scoped via IBelongsToCompany.

---

### 69. VacationApprovalRules (Vacation Workflow Configuration)

**Purpose:** Configures vacation approval workflows per company and optionally per job type. Supports auto-approval thresholds, two-level approval for extended leave, and specific approver routing.

**File Reference:** `Models/VacationApprovalRule.cs`

**Schema:**
```csharp
public class VacationApprovalRule : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int? JobTypeId { get; set; }           // null = default rule for company
    public int? ApproverUserId { get; set; }      // null = any user with grant
    public string ApproverGrantKey { get; set; }  // Default: "ApproveVacations"
    public int MaxAutoApproveDays { get; set; }   // 0 = no auto-approve
    public bool RequiresSecondApproval { get; set; }
    public int ExtendedLeaveDaysThreshold { get; set; } // Default: 5
    public string? SecondApproverGrantKey { get; set; }
    public int Priority { get; set; }             // Higher = checked first
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public int CreatedBy { get; set; }
}
```

**Relationships:** FK to Companies (CompanyId), optional FK to JobTypes (JobTypeId), optional FK to AppUsers (ApproverUserId). Tenant-scoped via IBelongsToCompany.

---

### 70. RoleTemplateJobTypeLabels (Role Display Name Overrides)

**Purpose:** Optional per-job-type display name overrides for role templates. Allows the same role template to show different labels depending on the job type context (e.g., "Manager" displays as "Mapotz" for Alhut job type).

**File Reference:** `Models/RoleTemplateJobTypeLabel.cs`

**Schema:**
```csharp
public class RoleTemplateJobTypeLabel
{
    public int Id { get; set; }
    public int RoleTemplateId { get; set; }
    public int JobTypeId { get; set; }
    public string DisplayNameEN { get; set; }
    public string DisplayNameHE { get; set; }
}
```

**Relationships:** FK to RoleTemplates (RoleTemplateId), FK to JobTypes (JobTypeId).

**Note:** Cross-company entity (does NOT implement IBelongsToCompany).

---

### Post-V3 Entity Summary

| Category | Tables Added | Purpose |
|----------|--------------|---------|
| **Home Rotation** | HomeTypes, HomeTypeOverrides (2) | HOME shift rotation templates and per-user overrides |
| **Chore Management** | ChoreTypes (1) | Chore categorization with colors and sort order |
| **Calendar Enhancements** | ShiftCapacityOverrides, UserDayNotes (2) | Date-specific capacity and per-user day notes |
| **Feature Flags** | FeatureFlags (1) | Hierarchical feature toggle system |
| **Telemetry** | ClientAnalyticsEvents, ClientErrors, PerformanceMetrics (3) | Client-side observability (air-gapped) |
| **Duty Rotation** | DutyRotations, DutyRotationEntries, DutyRotationLogs (3) | Automated on-duty assignment queues |
| **Announcements** | Announcements (1) | Company-wide announcements feed |
| **Vacation Workflow** | VacationApprovalRules (1) | Configurable approval routing and auto-approval |
| **Authorization** | RoleTemplateJobTypeLabels (1) | Per-job-type role display names |
| **Social** | UserFriendships (1) | Already documented in V3, now with full schema |

**Total Post-V3 Tables Added:** 16 new tables

**Grand Total:** 71 entities (55 V3 + 16 post-V3)

---

## Summary

This comprehensive database schema supports:
- **Multi-Tenancy:** 25 tenant-scoped tables with automatic isolation
- **Shift Scheduling:** Complete hierarchy (Types → Instances → Assignments)
- **Request Workflows:** Time-off, swaps, signup approvals
- **Audit & Compliance:** Comprehensive audit trails for regulatory requirements
- **Cross-Company Coordination:** OnDuty global visibility, Director cross-company access
- **Gamification:** Easter egg game with leaderboards

**Total Storage:** 71 tables, 40+ migrations, ~50-200 MB typical size

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
