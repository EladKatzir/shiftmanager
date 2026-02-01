# 06-DOMAIN-MODELS.md

**ShiftManager - Genesis Documentation**
**Document 6 of 23: Domain Models and Entities**
**Version:** 2.0 (V3 Update)
**Last Updated:** 2026-01-29

---

## Table of Contents

1. [Overview](#overview)
2. [Entity Categories](#entity-categories)
3. [Core Domain Entities](#core-domain-entities)
4. [Shift Management Entities](#shift-management-entities)
5. [Request Workflow Entities](#request-workflow-entities)
6. [Task Assignment Entities](#task-assignment-entities)
7. [Team Collaboration Entities](#team-collaboration-entities)
8. [Notification Entities](#notification-entities)
9. [Audit & Compliance Entities](#audit--compliance-entities)
10. [Configuration Entities](#configuration-entities)
11. [API Infrastructure Entities](#api-infrastructure-entities)
12. [Multi-Tenancy Entities](#multi-tenancy-entities)
13. [Gamification Entities](#gamification-entities)
14. [V3 Organizational Hierarchy Entities](#v3-organizational-hierarchy-entities)
15. [V3 Authorization Entities](#v3-authorization-entities)
16. [V3 Scheduling Entities](#v3-scheduling-entities)
17. [Enums Reference](#enums-reference)
18. [Entity Relationships](#entity-relationships)
19. [Business Rules](#business-rules)

---

## Overview

ShiftManager's domain model consists of **54 entities** (tables) organized into logical categories. V3 adds 23 new entities for organizational hierarchy, grant-based authorization, enhanced scheduling, and localization.

**Design Principles:**
- **Multi-tenancy:** Entities implement `IBelongsToCompany` for tenant isolation
- **Hierarchical organization:** Project → Area → Molecule → Company/Department (V3)
- **Grant-based authorization:** Fine-grained permissions with scope inheritance (V3)
- **Soft deletes:** CanceledAt/IsDeleted pattern for audit trails
- **Optimistic concurrency:** Concurrency tokens on frequently-updated entities
- **Type safety:** DateOnly/TimeOnly for dates and times (no DateTime confusion)
- **Nullable references:** Explicit nullability for optional relationships

**See Also:**
- [03-DATABASE-SCHEMA.md](03-DATABASE-SCHEMA.md) - Database schema with indexes and constraints
- [05-MULTI-TENANCY-DEEP-DIVE.md](05-MULTI-TENANCY-DEEP-DIVE.md) - Multi-tenancy architecture
- [21-V3-ORGANIZATIONAL-HIERARCHY.md](21-V3-ORGANIZATIONAL-HIERARCHY.md) - V3 hierarchy details
- [22-V3-GRANT-AUTHORIZATION.md](22-V3-GRANT-AUTHORIZATION.md) - V3 grant system
- [23-V3-SCHEDULING-SYSTEM.md](23-V3-SCHEDULING-SYSTEM.md) - V3 scheduling enhancements

---

## Entity Categories

ShiftManager's 50+ entities are organized into 13 functional categories:

| Category | Entities | Purpose |
|----------|----------|---------|
| **Core Domain** | Company, AppUser (2) | Root entities for multi-tenancy and users |
| **Shift Management** | ShiftType, ShiftInstance, ShiftAssignment (3) | Shift scheduling and assignments |
| **Request Workflows** | TimeOffRequest, SwapRequest, UserJoinRequest (3) | User-initiated approval workflows |
| **Task Assignments** | Chore, OnDuty, OnDutyRoleSubscription, OnDutyTypeConfig (4) | Daily task assignments |
| **Team Collaboration** | TeamCalendar, TeamCalendarMember (2) | Team calendar views |
| **Notifications** | UserNotification, DailyNotificationPreference (2) | In-app notifications and digests |
| **Audit & Compliance** | AuditLog, RoleAssignmentAudit, ProfileChangeAudit (3) | Audit trails and compliance |
| **Configuration** | AppConfig, EmailConfig, EmailApiLog, EmailTemplateCustomization, GriffinConfig, GriffinApiLog, CompanyLanguageSettings, CompanyLocalizationOverride (8) | System configuration, email, and localization |
| **API Infrastructure** | ApiKey, ApiKeyRequest, ApiRequestLog (3) | External API access |
| **Multi-Tenancy** | DirectorCompany (1) | Cross-company access for Directors |
| **Gamification** | GameScore, Feedback (2) | User engagement features |

---

## Core Domain Entities

### Company

**Purpose:** Root entity representing a tenant/organization in the multi-tenancy system.

**File:** `Models/Company.cs`

**Schema:**
```csharp
public class Company
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // Multitenancy Phase 1: URL routing and branding
    public string? Slug { get; set; }
    public string? DisplayName { get; set; }

    // JSON column for company-specific settings overrides
    public string? SettingsJson { get; set; }
}
```

**Properties:**
| Property | Type | Nullable | Description |
|----------|------|----------|-------------|
| Id | int | No | Primary key |
| Name | string | No | Company name (e.g., "Demo Co") |
| Slug | string? | Yes | URL-friendly identifier (e.g., "demo-co") for sub-domain routing |
| DisplayName | string? | Yes | Branded display name (e.g., "Demo Corporation") |
| SettingsJson | string? | Yes | JSON object for company-specific configuration overrides |

**Business Rules:**
- Slug must be unique (unique index)
- All data in other tables is scoped to CompanyId (multi-tenancy)

**Usage:**
```csharp
var company = new Company
{
    Name = "Acme Corp",
    Slug = "acme",
    DisplayName = "Acme Corporation",
    SettingsJson = "{\"theme\": \"dark\", \"logo\": \"acme-logo.png\"}"
};
```

---

### AppUser

**Purpose:** Represents a user in the system with authentication, profile, and role information.

**File:** `Models/AppUser.cs` (47 lines)

**Schema:**
```csharp
public class AppUser : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Employee;
    public bool IsActive { get; set; } = true;

    // Local password auth (no external integrations)
    public byte[] PasswordHash { get; set; } = Array.Empty<byte>();
    public byte[] PasswordSalt { get; set; } = Array.Empty<byte>();

    // Profile Enhancements - Personal Information
    public string? PreferredName { get; set; }      // What they prefer to be called
    public string? Phone { get; set; }              // Mobile phone
    public string? City { get; set; }               // City of residence
    public DateOnly? DateOfBirth { get; set; }      // For age verification, birthday greetings

    // Profile Enhancements - Professional Information
    public string? Department { get; set; }         // e.g., "Kitchen", "Front of House", "Management"
    public string? JobTitle { get; set; }           // e.g., "Line Cook", "Server", "Shift Manager"
    public DateOnly? HireDate { get; set; }         // When they started
    public string? Skills { get; set; }             // JSON array, e.g., ["Grill", "Prep", "Cleaning"]
    public string? Certifications { get; set; }     // JSON array, e.g., ["Food Safety", "First Aid"]

    // Profile Enhancements - Emergency Contact
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? EmergencyContactRelation { get; set; } // e.g., "Spouse", "Parent", "Friend"

    // Profile Enhancements - Avatar & Metadata
    public string? AvatarFileName { get; set; }     // Filename in wwwroot/avatars/{companyId}/
    public DateTime? ProfileLastUpdated { get; set; }
    public int? ProfileLastUpdatedBy { get; set; }  // User ID who made the change

    // Security - Account Lockout Protection
    public int FailedLoginAttempts { get; set; } = 0;
    public DateTime? LockoutEnd { get; set; }
    public DateTime? LastLoginAttempt { get; set; }
}
```

**Key Properties:**

| Property | Type | Purpose |
|----------|------|---------|
| Email | string | Unique login identifier (unique index) |
| PasswordHash | byte[] | PBKDF2 hash (100,000 iterations, SHA256) |
| PasswordSalt | byte[] | Unique salt per user |
| Role | UserRole | Owner(0), Director(3), Manager(1), Assigner(5), Employee(2), Trainee(4) |
| IsActive | bool | Account enabled/disabled |
| AvatarFileName | string? | Avatar image filename (stored in wwwroot/avatars/{CompanyId}/) |
| Skills | string? | JSON array of skills (e.g., `["Cooking", "Cleaning"]`) |
| Certifications | string? | JSON array of certifications |

**Business Rules:**
- Email must be unique globally (across all companies)
- CompanyId isolates users to their company (multi-tenancy)
- Account locked for 15 minutes after 5 failed login attempts
- Directors can have access to multiple companies (via DirectorCompany table)

**Security:**
- Passwords hashed with PBKDF2 (100,000 iterations)
- Unique salt per user (prevents rainbow table attacks)
- FailedLoginAttempts/LockoutEnd for brute-force protection

**Profile Sections:**
1. **Personal:** PreferredName, Phone, City, DateOfBirth
2. **Professional:** Department, JobTitle, HireDate, Skills, Certifications
3. **Emergency Contact:** Name, Phone, Relation
4. **Avatar:** AvatarFileName (uploaded image)

---

## Shift Management Entities

### ShiftType

**Purpose:** Defines a type of shift (e.g., Morning, Noon, Night) with start/end times.

**File:** `Models/ShiftType.cs` (82 lines)

**Schema:**
```csharp
public class ShiftType : IBelongsToCompany
{
    // Predefined shift type keys
    public const string KEY_MORNING = "MORNING";
    public const string KEY_MIDDLE = "MIDDLE";
    public const string KEY_AFTERNOON = "AFTERNOON";
    public const string KEY_NOON = "NOON"; // Legacy alias for AFTERNOON
    public const string KEY_NIGHT = "NIGHT";
    public const string KEY_OFFLINE = "OFFLINE";
    public const string KEY_EVENING = "EVENING";

    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string Key { get; set; } = string.Empty; // MORNING, NOON, NIGHT, MIDDLE, OFFLINE, or CUSTOM_*

    /// <summary>
    /// Custom display name for this shift type (company-specific).
    /// If null/empty, falls back to predefined names based on Key.
    /// </summary>
    public string? CustomName { get; set; }

    [NotMapped]
    public string Name
    {
        get
        {
            // Use CustomName if provided (company-specific override)
            if (!string.IsNullOrWhiteSpace(CustomName))
                return CustomName;

            // Otherwise use predefined names
            return Key switch
            {
                KEY_MORNING => "Morning Shift",
                KEY_NOON => "Afternoon Shift",
                KEY_AFTERNOON => "Afternoon Shift",
                KEY_NIGHT => "Night Shift",
                KEY_MIDDLE => "Mid Shift",
                KEY_EVENING => "Evening Shift",
                KEY_OFFLINE => "Offline",
                _ => Key // fallback to key if no match
            };
        }
    }

    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; } // if End <= Start => wraps to next day

    [NotMapped]
    public bool IsOffline => Key == KEY_OFFLINE;

    [NotMapped]
    public int SortOrder
    {
        get
        {
            return Key switch
            {
                KEY_MORNING => 1,
                KEY_MIDDLE => 2,
                KEY_AFTERNOON => 3,
                KEY_NOON => 3,
                KEY_NIGHT => 4,
                KEY_OFFLINE => 99, // Always last
                _ => 50 // Custom shifts in the middle
            };
        }
    }
}
```

**Predefined Shift Types:**

| Key | Default Name | Default Times | Description |
|-----|--------------|---------------|-------------|
| MORNING | Morning Shift | 08:00-16:00 | Standard day shift |
| MIDDLE | Mid Shift | 12:00-20:00 | Afternoon/evening overlap |
| NOON (or AFTERNOON) | Afternoon Shift | 16:00-00:00 | Evening shift (crosses midnight) |
| NIGHT | Night Shift | 00:00-08:00 | Overnight shift |
| OFFLINE | Offline | 00:00-00:00 | Special shift that can overlap (administrative leave) |

**Computed Properties:**
- **Name:** Returns CustomName if set, otherwise default name for Key
- **IsOffline:** Returns true if Key == "OFFLINE"
- **SortOrder:** Consistent ordering (Morning=1, Middle=2, Afternoon=3, Night=4, Offline=99)

**Business Rules:**
- ShiftTypes are company-specific (CompanyId scoping)
- Key + CompanyId is indexed (fast lookup)
- If End ≤ Start, shift wraps to next day (e.g., 16:00-00:00 is 8 hours)
- OFFLINE shifts can overlap with other shifts (special case for conflict detection)

**Customization:**
```csharp
var morningShift = new ShiftType
{
    CompanyId = 1,
    Key = ShiftType.KEY_MORNING,
    CustomName = "Day Shift", // Override default "Morning Shift"
    Start = new TimeOnly(7, 0), // 7:00 AM
    End = new TimeOnly(15, 0)   // 3:00 PM
};
```

---

### ShiftInstance

**Purpose:** Represents a specific occurrence of a shift type on a date (e.g., "Morning shift on Jan 15, 2025").

**File:** `Models/ShiftInstance.cs` (22 lines)

**Schema:**
```csharp
public class ShiftInstance : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int ShiftTypeId { get; set; }
    public ShiftType ShiftType { get; set; } = null!;
    public DateOnly WorkDate { get; set; }
    public string Name { get; set; } = string.Empty; // Custom name for this specific shift instance

    public int StaffingRequired { get; set; } = 0;

    [ConcurrencyCheck]
    public int Concurrency { get; set; } = 0;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
```

**Key Properties:**

| Property | Type | Purpose |
|----------|------|---------|
| ShiftTypeId | int | FK to ShiftType (defines shift times) |
| WorkDate | DateOnly | Date of the shift occurrence |
| Name | string | Optional custom name for this specific instance |
| StaffingRequired | int | How many people needed for this shift |
| Concurrency | int | Optimistic concurrency token (detects concurrent edits) |
| UpdatedAt | DateTime | Last update timestamp |

**Business Rules:**
- Composite index on (CompanyId, WorkDate) for calendar queries
- StaffingRequired determines how many ShiftAssignment records allowed
- Concurrency token prevents race conditions when multiple users edit simultaneously

**Optimistic Concurrency:**
```csharp
// EF Core automatically checks Concurrency token on update
var shift = await db.ShiftInstances.FindAsync(id);
shift.StaffingRequired = 5;
await db.SaveChangesAsync(); // Throws DbUpdateConcurrencyException if Concurrency changed
```

**Usage:**
```csharp
var morningShift = new ShiftInstance
{
    CompanyId = 1,
    ShiftTypeId = 2, // Morning shift type
    WorkDate = new DateOnly(2025, 12, 30),
    StaffingRequired = 3, // Need 3 people
    Name = "Holiday Morning Shift"
};
```

---

### ShiftAssignment

**Purpose:** Assigns a user to a specific shift instance (or leaves slot unassigned).

**File:** `Models/ShiftAssignment.cs` (23 lines)

**Schema:**
```csharp
public class ShiftAssignment : IBelongsToCompany
{
    public int Id { get; set; }

    // Multitenancy Phase 1: Tenant scoping
    public int CompanyId { get; set; }

    public int ShiftInstanceId { get; set; }
    public ShiftInstance ShiftInstance { get; set; } = null!;

    // Nullable to support unassigned slots (roster grid feature)
    public int? UserId { get; set; }
    public AppUser? User { get; set; }

    // Trainee shadowing support
    public int? TraineeUserId { get; set; }
    public AppUser? Trainee { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

**Key Features:**

| Feature | Implementation | Purpose |
|---------|----------------|---------|
| **Nullable UserId** | `int? UserId` | Supports unassigned slots (placeholder for future assignment) |
| **Trainee Shadowing** | `int? TraineeUserId` | Trainee can shadow another employee on shift |
| **Unique Constraint** | Index on (CompanyId, ShiftInstanceId, UserId) | Prevents duplicate assignments |

**Business Rules:**
- UserId can be null (unassigned slot for rostering grid)
- TraineeUserId allows trainee to shadow employee without taking a staffing slot
- Maximum assignments per shift = ShiftInstance.StaffingRequired
- Trainee must have Role = UserRole.Trainee

**Trainee Shadowing Example:**
```csharp
// Employee assigned to morning shift
var assignment = new ShiftAssignment
{
    CompanyId = 1,
    ShiftInstanceId = 100,
    UserId = 5 // Employee
};

// Trainee shadows this employee
assignment.TraineeUserId = 12; // Trainee user ID
```

---

## Request Workflow Entities

### TimeOffRequest

**Purpose:** User requests time off (vacation or half-day), pending manager approval.

**File:** `Models/TimeOffRequest.cs` (57 lines)

**Schema:**
```csharp
public class TimeOffRequest : IBelongsToCompany
{
    public int Id { get; set; }

    // Multitenancy Phase 1: Tenant scoping
    public int CompanyId { get; set; }

    public int UserId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public TimeOffType Type { get; set; } = TimeOffType.Vacation;
    public string? Reason { get; set; }
    public RequestStatus Status { get; set; } = RequestStatus.Pending;

    /// <summary>
    /// Optional: Specific approver user ID (must be Manager, Director, or Owner).
    /// If null, any manager can approve.
    /// </summary>
    public int? ApproverId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Review tracking
    public DateTime? ReviewedAt { get; set; }
    public int? ReviewedBy { get; set; }
    public string? DeclineReason { get; set; }

    // Business logic methods
    public DateTime GetActualStartDateTime() { /* ... */ }
    public DateTime GetActualEndDateTime() { /* ... */ }
}
```

**TimeOffType Enum:**

| Type | Value | Duration | Actual Time |
|------|-------|----------|-------------|
| Vacation | 0 | Full days | StartDate 00:00 → EndDate+1 13:00 |
| After | 1 | Half day | StartDate 16:00 → StartDate+1 13:00 |

**Workflow:**
```plaintext
1. Employee creates TimeOffRequest (Status = Pending)
2. Manager reviews request
   ├─ Approved: Status = Approved, shift assignments auto-removed
   └─ Declined: Status = Declined, DeclineReason set
3. If approved, conflicting shift assignments are removed
4. Notification sent to employee
```

**Business Rules:**
- Composite index on (CompanyId, UserId, StartDate) for user's time-off history
- ApproverId can designate specific manager (otherwise any manager can approve)
- Conflict detection: No shift assignments allowed during time-off period
- GetActualStartDateTime() / GetActualEndDateTime() for precise conflict checking

**Example:**
```csharp
// Employee requests 3-day vacation (Dec 20-22)
var request = new TimeOffRequest
{
    CompanyId = 1,
    UserId = 5,
    Type = TimeOffType.Vacation,
    StartDate = new DateOnly(2025, 12, 20),
    EndDate = new DateOnly(2025, 12, 22),
    Reason = "Family vacation",
    Status = RequestStatus.Pending
};

// Actual time off: Dec 20 00:00 → Dec 23 13:00 (covers 3.5 days)
var start = request.GetActualStartDateTime(); // Dec 20, 2025 00:00:00
var end = request.GetActualEndDateTime();     // Dec 23, 2025 13:00:00
```

---

### SwapRequest

**Purpose:** User requests to swap shifts with another user, pending manager approval.

**File:** `Models/SwapRequest.cs` (73 lines)

**Schema:**
```csharp
public class SwapRequest : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }

    /// <summary>
    /// The assignment the requester wants to give away
    /// </summary>
    public int FromAssignmentId { get; set; }

    /// <summary>
    /// Optional: The assignment the requester wants to take
    /// </summary>
    public int? ToAssignmentId { get; set; }

    /// <summary>
    /// The user requesting the swap
    /// </summary>
    public int FromUserId { get; set; }

    /// <summary>
    /// The target user to swap with (optional if ToAssignmentId is specified)
    /// </summary>
    public int? ToUserId { get; set; }

    /// <summary>
    /// Status of the swap request
    /// </summary>
    public RequestStatus Status { get; set; } = RequestStatus.Pending;

    /// <summary>
    /// Reason for the swap request
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Reason for declining (if declined)
    /// </summary>
    public string? DeclineReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
    public int? ReviewedBy { get; set; }

    // Navigation properties
    public ShiftAssignment? FromAssignment { get; set; }
    public ShiftAssignment? ToAssignment { get; set; }
    public AppUser? FromUser { get; set; }
    public AppUser? ToUser { get; set; }
    public AppUser? Reviewer { get; set; }
}
```

**Swap Types:**

| Type | FromAssignmentId | ToAssignmentId | Description |
|------|------------------|----------------|-------------|
| **Simple Swap** | Set | Set | User A swaps shift with User B |
| **Give Away** | Set | Null | User A gives away shift (manager assigns to someone) |
| **Take Shift** | Set | Set | User A takes User B's shift (User B gets nothing) |

**Workflow:**
```plaintext
1. Employee creates SwapRequest (Status = Pending)
2. Manager reviews request
   ├─ Approved: Status = Approved, assignments swapped
   └─ Declined: Status = Declined, DeclineReason set
3. If approved, FromAssignment.UserId ↔ ToAssignment.UserId swapped
4. Notifications sent to both users
```

**Business Rules:**
- Composite index on (CompanyId, Status, CreatedAt) for pending swaps view
- Conflict detection: Swap must not violate rest hours or overlap rules
- Both users must be from same company (CompanyId check)

**Example:**
```csharp
// User 5 wants to swap their Dec 30 morning shift with User 8's Dec 31 morning shift
var swap = new SwapRequest
{
    CompanyId = 1,
    FromUserId = 5,
    FromAssignmentId = 100, // User 5's Dec 30 morning shift
    ToUserId = 8,
    ToAssignmentId = 105,   // User 8's Dec 31 morning shift
    Reason = "Need to attend family event on Dec 30",
    Status = RequestStatus.Pending
};
```

---

### UserJoinRequest

**Purpose:** New user requests to join a company, pending owner/manager approval.

**File:** `Models/UserJoinRequest.cs`

**Schema:**
```csharp
public class UserJoinRequest : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public UserRole RequestedRole { get; set; }
    public byte[] PasswordHash { get; set; } = Array.Empty<byte>();
    public byte[] PasswordSalt { get; set; } = Array.Empty<byte>();
    public JoinRequestStatus Status { get; set; } = JoinRequestStatus.Pending;
    public int? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public int? CreatedUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Company? Company { get; set; }
    public AppUser? ReviewedByUser { get; set; }
    public AppUser? CreatedUser { get; set; }
}
```

**JoinRequestStatus Enum:**

| Status | Value | Description |
|--------|-------|-------------|
| Pending | 0 | Awaiting review |
| Approved | 1 | Approved, user created |
| Rejected | 2 | Rejected, request declined |

**Workflow:**
```plaintext
1. Visitor fills out signup form (email, name, password, requested role)
2. UserJoinRequest created (Status = Pending, password already hashed)
3. Owner/Manager reviews request
   ├─ Approved: AppUser created, CreatedUserId set, Status = Approved
   └─ Rejected: Status = Rejected, request remains for audit
4. User can login if approved
```

**Business Rules:**
- Password is hashed immediately on signup (stored in UserJoinRequest until approval)
- Once approved, AppUser is created and CreatedUserId points to it
- Composite index on (Email, CompanyId, Status) for duplicate detection
- Email must not already exist in AppUsers table (checked before approval)

**Security Note:**
- Password stored in UserJoinRequest (hashed) prevents need for password reset flow
- If request rejected, password is discarded with the request

---

## Task Assignment Entities

### Chore

**Purpose:** Represents a task/chore assigned to a user on a specific date (mutually exclusive with shifts).

**File:** `Models/Chore.cs` (66 lines)

**Schema:**
```csharp
public class Chore : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }

    /// <summary>
    /// The user assigned to this chore
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// Date of the chore (stored as DateOnly, no time component)
    /// </summary>
    public DateOnly Date { get; set; }

    /// <summary>
    /// Short title/description of the chore (required)
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Optional additional notes/details
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// User who created/assigned this chore
    /// </summary>
    public int CreatedBy { get; set; }

    /// <summary>
    /// When the chore was created
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the chore was canceled (null = active, not null = canceled/soft deleted)
    /// </summary>
    public DateTime? CanceledAt { get; set; }

    /// <summary>
    /// User who canceled this chore (null if not canceled)
    /// </summary>
    public int? CanceledBy { get; set; }

    // Navigation properties
    public Company? Company { get; set; }
    public AppUser? User { get; set; }
    public AppUser? Creator { get; set; }
    public AppUser? Canceler { get; set; }

    /// <summary>
    /// Whether this chore is active (not canceled)
    /// </summary>
    public bool IsActive => CanceledAt == null;
}
```

**Business Rules:**
- Unique index: (CompanyId, UserId, Date, CanceledAt) with filter `[CanceledAt] IS NULL`
- Only ONE active chore per user per day
- Canceled chores don't count toward uniqueness (can create new chore on same date)
- Chores are mutually exclusive with shift assignments (user can't have both on same day)

**Soft Delete Pattern:**
```csharp
// Cancel a chore (soft delete)
chore.CanceledAt = DateTime.UtcNow;
chore.CanceledBy = currentUserId;
await db.SaveChangesAsync();

// Query active chores only
var activeChores = await db.Chores
    .Where(c => c.CanceledAt == null)
    .ToListAsync();
```

**Authorization:**
- **Create:** Manager, Owner, Director, Assigner roles
- **Cancel:** Manager, Owner, Director roles

---

### OnDuty

**Purpose:** Represents a "day shift" assignment (Hakam or Lead) for a user on a date.

**File:** `Models/OnDuty.cs` (82 lines)

**IMPORTANT TERMINOLOGY NOTE:**
- **Code:** Named `OnDuty` (historical reasons)
- **UI:** Called "Day Shifts" (Hebrew: "משמרות יומיות")
- **Types:** Hakam (חק"מכו) and Lead (מובילתו)

**Schema:**
```csharp
public class OnDuty
{
    public int Id { get; set; }

    /// <summary>
    /// User assigned to on-duty (can be from any company)
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// Date of the on-duty assignment
    /// </summary>
    public DateOnly Date { get; set; }

    /// <summary>
    /// Type of on-duty: Hakam or Lead
    /// </summary>
    public OnDutyType Type { get; set; }

    /// <summary>
    /// Optional notes about this assignment
    /// </summary>
    public string? Notes { get; set; }

    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CanceledAt { get; set; }
    public int? CanceledBy { get; set; }

    // Navigation properties
    public AppUser? User { get; set; }
    public AppUser? Creator { get; set; }
    public AppUser? Canceler { get; set; }

    public bool IsActive => CanceledAt == null;
}
```

**CRITICAL: No CompanyId!**
- OnDuty is a **global/public table** (no CompanyId column)
- Visible across all companies (cross-company assignments)
- Used for organization-wide roles (Hakam, Lead)

**OnDutyType Enum:**

| Type | Value | Hebrew | Description |
|------|-------|--------|-------------|
| Hakam | 0 | חק"מכו | Day Shift: Hakam |
| Lead | 1 | מובילתו | Day Shift: Lead |

**Business Rules:**
- Unique index: (UserId, Date, Type, CanceledAt) with filter `[CanceledAt] IS NULL`
- Only ONE active on-duty per user per type per day
- Users can have multiple on-duty types on same day (Hakam + Lead both allowed)
- No CompanyId filter (cross-company visibility intentional)

**Authorization:**
- **Create/Cancel:** Manager, Owner, Director roles only

---

### OnDutyRoleSubscription

**Purpose:** Users subscribe to receive notifications for specific on-duty types.

**Schema:**
```csharp
public class OnDutyRoleSubscription : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int UserId { get; set; }
    public int OnDutyTypeValue { get; set; } // 0 = Hakam, 1 = Lead
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

**Business Rules:**
- Unique index: (CompanyId, UserId, OnDutyTypeValue) with filter `[IsActive] = 1`
- User can subscribe to both Hakam and Lead (separate records)
- Notifications sent when on-duty assignments created/canceled for subscribed types

---

### OnDutyTypeConfig

**Purpose:** Global configuration for on-duty types (names, icons, colors).

**Schema:**
```csharp
public class OnDutyTypeConfig
{
    public int Id { get; set; }
    public int TypeValue { get; set; }  // Unique - Hakam(0), Lead(1)
    public string NameEn { get; set; } = string.Empty;
    public string NameHe { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

**CRITICAL: No CompanyId!**
- Global configuration (not company-specific)
- All companies share same on-duty type definitions

**Predefined Types:**

| TypeValue | NameEn | NameHe | Icon | Color |
|-----------|--------|--------|------|-------|
| 0 | Hakam | חק"מכו | shield | blue |
| 1 | Lead | מובילתו | star | gold |

---

## Team Collaboration Entities

### TeamCalendar

**Purpose:** User-created calendar grouping multiple team members for shared view.

**File:** `Models/TeamCalendar.cs`

**Schema:**
```csharp
public class TeamCalendar : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int OwnerId { get; set; } // User who created the calendar
    public string Name { get; set; } = string.Empty;
    public bool IsDeleted { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Company? Company { get; set; }
    public AppUser? Owner { get; set; }
    public List<TeamCalendarMember> Members { get; set; } = new();
}
```

**Business Rules:**
- Unique index: (CompanyId, OwnerId, Name) with filter `[IsDeleted] = 0`
- Owner can have multiple calendars with different names
- Deleted calendars (IsDeleted = true) don't count toward uniqueness

**Soft Delete Pattern:**
```csharp
// Soft delete calendar
calendar.IsDeleted = true;
await db.SaveChangesAsync();

// Query active calendars only
var activeCalendars = await db.TeamCalendars
    .Where(tc => !tc.IsDeleted)
    .ToListAsync();
```

---

### TeamCalendarMember

**Purpose:** Associates a user with a team calendar.

**Schema:**
```csharp
public class TeamCalendarMember : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int TeamCalendarId { get; set; }
    public int MemberUserId { get; set; }
    public int? SortOrder { get; set; } // Optional manual ordering
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public TeamCalendar TeamCalendar { get; set; } = null!;
    public AppUser Member { get; set; } = null!;
}
```

**Business Rules:**
- Unique index: (TeamCalendarId, MemberUserId) - one member per calendar
- Cascade delete: When TeamCalendar deleted, all members auto-deleted
- SortOrder allows manual member ordering in UI

---

## Notification Entities

### UserNotification

**Purpose:** In-app notification for user actions (shift added, time-off approved, etc.).

**File:** `Models/UserNotification.cs` (40 lines)

**Schema:**
```csharp
public class UserNotification : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }

    [Required]
    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;

    [Required]
    public NotificationType Type { get; set; }

    [Required]
    [StringLength(500)]
    public string Title { get; set; } = "";

    [Required]
    [StringLength(1000)]
    public string Message { get; set; } = "";

    public bool IsRead { get; set; } = false;

    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ReadAt { get; set; }

    // Optional reference fields for linking back to specific entities
    public int? RelatedEntityId { get; set; }

    [StringLength(50)]
    public string? RelatedEntityType { get; set; } // "TimeOffRequest", "SwapRequest", "ShiftAssignment"
}
```

**NotificationType Enum (18 types):**

| Type | Value | Description |
|------|-------|-------------|
| ShiftAdded | 0 | User assigned to shift |
| ShiftRemoved | 1 | User removed from shift |
| TimeOffApproved | 2 | Time-off request approved |
| TimeOffDeclined | 3 | Time-off request declined |
| SwapRequestApproved | 4 | Swap request approved |
| SwapRequestDeclined | 5 | Swap request declined |
| TraineeShadowingAdded | 6 | Trainee added to shadow shift |
| TraineeShadowingRemoved | 7 | Trainee removed from shadowing |
| EmployeeTraineeAdded | 8 | Employee promoted from Trainee |
| EmployeeTraineeRemoved | 9 | Employee demoted to Trainee |
| TraineeShadowingCanceledTimeOff | 10 | Shadowing canceled due to time-off |
| TraineeShadowingCanceledRoleChange | 11 | Shadowing canceled due to role change |
| ChoreAssigned | 12 | Chore assigned to user |
| ChoreCanceled | 13 | Chore canceled |
| OnDutyAssigned | 14 | On-duty assigned to user |
| OnDutyCanceled | 15 | On-duty canceled |
| TimeOffDeleted | 16 | Time-off request deleted |
| FeedbackSubmitted | 17 | Feedback submitted |

**Business Rules:**
- Composite index on (CompanyId, UserId, CreatedAt) for user's notifications
- IsRead tracks if user has seen notification
- RelatedEntityId + RelatedEntityType for polymorphic reference (optional)

**Usage:**
```csharp
var notification = new UserNotification
{
    CompanyId = 1,
    UserId = 5,
    Type = NotificationType.TimeOffApproved,
    Title = "Time-Off Approved",
    Message = "Your time-off request for Dec 20-22 has been approved.",
    RelatedEntityType = "TimeOffRequest",
    RelatedEntityId = 42
};
```

---

### DailyNotificationPreference

**Purpose:** User's preference for daily digest email notifications.

**Schema:**
```csharp
public class DailyNotificationPreference : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int UserId { get; set; } // Unique per user when IsActive=1
    public TimeOnly PreferredTime { get; set; } // When to send digest (e.g., 08:00)
    public bool IsActive { get; set; } = true;
    public bool IncludeUpcomingShifts { get; set; } = true;
    public bool IncludePendingRequests { get; set; } = true;
    public bool IncludeChores { get; set; } = true;
    public bool IncludeOnDuty { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

**Business Rules:**
- Unique index: (CompanyId, UserId) with filter `[IsActive] = 1`
- Only ONE active preference per user
- DailyNotificationJob background service sends emails at PreferredTime

**Digest Content Flags:**
- **IncludeUpcomingShifts:** Next 7 days of shifts
- **IncludePendingRequests:** Pending time-off/swap requests (if manager)
- **IncludeChores:** Today's chores
- **IncludeOnDuty:** Today's on-duty assignments

---

## Audit & Compliance Entities

### AuditLog

**Purpose:** General-purpose audit log for all significant actions.

**Schema:**
```csharp
public class AuditLog : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int? UserId { get; set; } // Nullable - denormalized email/name preserved
    public string UserEmail { get; set; } = string.Empty; // Denormalized
    public string UserDisplayName { get; set; } = string.Empty; // Denormalized
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty; // "ShiftAssignment", "TimeOffRequest", etc.
    public int? EntityId { get; set; }
    public string? Details { get; set; } // JSON - before/after state
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public AppUser? User { get; set; }
    public Company? Company { get; set; }
}
```

**Key Features:**
- **Denormalized user info:** UserEmail, UserDisplayName preserved even if user deleted
- **Polymorphic entity reference:** EntityType + EntityId (e.g., "ShiftAssignment", 42)
- **JSON details:** Before/after state for change tracking
- **IP + UserAgent:** Security audit trail

**Indexes:**
- (CompanyId, Timestamp) - time-based queries
- (CompanyId, UserId, Timestamp) - user's actions
- (CompanyId, Action) - action-based filtering

---

### RoleAssignmentAudit

**Purpose:** Specific audit log for role changes (Owner → Manager, Employee → Trainee, etc.).

**Schema:**
```csharp
public class RoleAssignmentAudit : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int TargetUserId { get; set; }
    public UserRole FromRole { get; set; }
    public UserRole ToRole { get; set; }
    public int ChangedBy { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public AppUser? TargetUser { get; set; }
    public AppUser? ChangedByUser { get; set; }
}
```

**Business Rules:**
- Records every role change (critical for compliance)
- Immutable (insert-only, never deleted)
- Composite index on (CompanyId, TargetUserId, Timestamp)

---

### ProfileChangeAudit

**Purpose:** Specific audit log for profile field changes (name, email, phone, etc.).

**Schema:**
```csharp
public class ProfileChangeAudit : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int TargetUserId { get; set; }
    public int ChangedBy { get; set; }
    public string FieldName { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public AppUser? TargetUser { get; set; }
    public AppUser? ChangedByUser { get; set; }
    public Company? Company { get; set; }
}
```

**Tracked Fields:**
- Email, DisplayName, Phone, City, Department, JobTitle, etc.
- Sensitive fields (PasswordHash) NOT logged in plain text

**Business Rules:**
- Records field-level changes (before/after values)
- FieldName stores property name (e.g., "Email", "DisplayName")
- Composite index on (CompanyId, TargetUserId, Timestamp)

---

## Configuration Entities

### AppConfig

**Purpose:** Company-specific configuration key-value pairs.

**Schema:**
```csharp
public class AppConfig : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

**Predefined Keys:**

| Key | Default Value | Purpose |
|-----|---------------|---------|
| RestHours | 8 | Minimum hours between shifts (conflict detection) |
| WeeklyHoursCap | 40 | Maximum hours per week (conflict detection) |
| GameEnabled | true | Enable shift-swap game feature |
| GameGridSize | 6 | Game grid dimensions (6x6) |
| GamePointsPer3Match | 40 | Points for matching 3 items |
| GamePointsPer4Match | 100 | Points for matching 4 items |
| GamePointsPer5PlusMatch | 200 | Points for matching 5+ items |
| GameMegaComboMultiplier | 2 | Multiplier for mega combos |
| GameMilestones | 1000,2500,... | Score milestones for achievements |

**Usage:**
```csharp
var restHours = await db.Configs
    .Where(c => c.Key == "RestHours")
    .Select(c => c.Value)
    .FirstOrDefaultAsync();

int hours = int.Parse(restHours ?? "8");
```

---

### EmailConfig

**Purpose:** Company-specific email API configuration (one per company).

**Schema:**
```csharp
public class EmailConfig : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; } // Unique - one config per company
    public string EncryptedApiKey { get; set; } = string.Empty;
    public string ApiUrl { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Company? Company { get; set; }
}
```

**Business Rules:**
- Unique index on CompanyId (one-to-one relationship with Company)
- EncryptedApiKey encrypted using ASP.NET Core Data Protection API
- ApiUrl points to email service endpoint (e.g., SendGrid, Mailgun)

**Security:**
- ApiKey encrypted at rest (Data Protection API)
- Decrypted only when sending emails

---

### EmailApiLog

**Purpose:** Log all email API requests/responses for debugging and auditing.

**Schema:**
```csharp
public class EmailApiLog : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string ToAddress { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? Request { get; set; } // JSON request payload
    public string? Response { get; set; } // JSON response
    public bool Success { get; set; }
    public string? ValidationErrors { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Company? Company { get; set; }
}
```

**Business Rules:**
- Logs every email send attempt (success or failure)
- Request/Response JSON for debugging API issues
- Composite index on (CompanyId, CreatedAt), (CompanyId, Success)

---

### GriffinConfig

**Purpose:** Griffin ADFS SSO configuration for air-gapped authentication.

**Schema:**
```csharp
public class GriffinConfig : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string BaseUrl { get; set; } = string.Empty; // Griffin ADFS base URL
    public string TokenConsumerUrl { get; set; } = string.Empty; // Callback URL
    public bool AutoProvisionUsers { get; set; } = false;
    public UserRole DefaultProvisionedRole { get; set; } = UserRole.Employee;
    public int TimeoutSeconds { get; set; } = 30;
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

**Griffin ADFS:**
- Air-gapped Active Directory Federation Services
- SSO for military/government networks
- GriffinAuthenticationMiddleware uses this config

**Business Rules:**
- AutoProvisionUsers: Create AppUser automatically on first Griffin login
- DefaultProvisionedRole: Role assigned to auto-provisioned users
- TimeoutSeconds: HTTP timeout for Griffin API calls

---

### GriffinApiLog

**Purpose:** Log all Griffin ADFS connection test requests/responses for debugging, diagnostics, and compliance.

**File:** `Models/GriffinApiLog.cs`

**Schema:**
```csharp
public class GriffinApiLog : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string RequestUrl { get; set; } = string.Empty;
    public string RequestMethod { get; set; } = string.Empty;
    public string? RequestHeaders { get; set; }
    public int? ResponseStatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public string? RedirectUrl { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int DurationMs { get; set; }
    public string? ValidationErrors { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public Company? Company { get; set; }
}
```

**Business Rules:**
- Created on every Griffin connection test (manual test button or automatic health check)
- Stores full HTTP diagnostic information (URL, method, headers, status, response body, duration)
- RedirectUrl captures final URL after HTTP redirects (common in ADFS)
- Success indicates whether connection test passed validation
- ValidationErrors stores configuration validation failures (e.g., invalid URL format)
- Composite indexes: (CompanyId, Timestamp) for recent logs, (CompanyId) for all company logs

**Usage:**
```csharp
// GriffinConfigService creates logs during connection tests
var log = new GriffinApiLog
{
    CompanyId = config.CompanyId,
    RequestUrl = config.BaseUrl,
    RequestMethod = "GET",
    Success = response.IsSuccessStatusCode,
    ResponseStatusCode = (int)response.StatusCode,
    DurationMs = (int)stopwatch.ElapsedMilliseconds,
    Timestamp = DateTime.UtcNow
};
await _logService.CreateLogAsync(log);
```

**Related:**
- GriffinApiLogService provides CRUD operations
- Owner/GriffinConfig page displays recent logs in diagnostic console
- Supports export to .txt file for air-gapped troubleshooting

---

### EmailTemplateCustomization

**Purpose:** Company-specific customizations to email templates.

**File:** `Models/EmailTemplateCustomization.cs`

**Schema:**
```csharp
public class EmailTemplateCustomization : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public EmailTemplateType TemplateType { get; set; }  // Which template
    public string CustomMessage { get; set; } = string.Empty;  // Max 2000 chars
    public bool IsEnabled { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }
    public int? UpdatedBy { get; set; }

    public Company Company { get; set; } = null!;
}
```

**Business Rules:**
- Unique index on (CompanyId, TemplateType) - one customization per template type per company
- If IsEnabled=false, system uses default email template
- CustomMessage supports placeholders like {EmployeeName}, {Date}

---

### CompanyLanguageSettings

**Purpose:** Company-specific language configuration (default and alternate languages).

**File:** `Models/CompanyLanguageSettings.cs`

**Schema:**
```csharp
public class CompanyLanguageSettings : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }  // Unique - one per company
    public string DefaultCulture { get; set; } = "en-US";  // "en-US" or "he-IL"
    public string AlternateCulture { get; set; } = "he-IL";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int UpdatedBy { get; set; }

    public Company? Company { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public AppUser? UpdatedByUser { get; set; }
}
```

**Business Rules:**
- Unique index on CompanyId (one-to-one with Company)
- DefaultCulture shown to new users/users without preference
- AlternateCulture available via language toggle
- Both must be valid cultures ("en-US" or "he-IL")

---

### CompanyLocalizationOverride

**Purpose:** Company-specific custom translations for UI strings.

**File:** `Models/CompanyLocalizationOverride.cs`

**Schema:**
```csharp
public class CompanyLocalizationOverride : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string Culture { get; set; } = string.Empty;  // "en-US" or "he-IL"
    public string ResourceKey { get; set; } = string.Empty;  // e.g., "Button_Save", max 200 chars
    public string OverrideValue { get; set; } = string.Empty;  // Custom translation, max 2000 chars
    public bool IsActive { get; set; } = true;  // Soft delete
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int UpdatedBy { get; set; }

    public Company? Company { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public AppUser? UpdatedByUser { get; set; }
}
```

**Business Rules:**
- Unique index on (CompanyId, Culture, ResourceKey) where IsActive=1
- Allows companies to customize any localization string
- OverrideValue is HTML-encoded for security
- Soft delete via IsActive flag
- Used by CompanyLocalizationService to override default translations

---

## API Infrastructure Entities

### ApiKey

**Purpose:** API key for external integrations (REST API access).

**File:** `Models/Api/ApiKey.cs` (94 lines)

**Schema:**
```csharp
public class ApiKey
{
    public int Id { get; set; }
    public string KeyHash { get; set; } = string.Empty; // SHA256 hash (unique index)
    public string? PlainTextKey { get; set; } // WARNING: Security concern
    public int CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Scopes { get; set; } = string.Empty; // Comma-separated
    public bool IsActive { get; set; } = true;
    public int RateLimitPerMinute { get; set; } = 100;
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }

    // Navigation properties
    public Company? Company { get; set; }
    public AppUser? CreatedByUser { get; set; }

    // Business logic methods
    public bool HasScope(string scope) { /* ... */ }
    public bool IsValid() { /* ... */ }
}
```

**CRITICAL: No Query Filter!**
- Has CompanyId but NO global query filter
- ApiAuthenticationMiddleware needs to query by KeyHash (not CompanyId)

**Security:**
- KeyHash: SHA256 hash of API key (stored in database)
- PlainTextKey: Original key (stored for owner retrieval - security warning)
- Scopes: Comma-separated (e.g., "user:read,shift:write")

**Scopes:**
```csharp
// Check if API key has specific scope
bool canReadUsers = apiKey.HasScope("user:read");
bool canWriteShifts = apiKey.HasScope("shift:write");
bool hasWildcard = apiKey.HasScope("*"); // Admin scope
```

**Validation:**
```csharp
// Check if API key is valid
bool isValid = apiKey.IsValid();
// Returns false if:
// - IsActive == false
// - ExpiresAt < DateTime.UtcNow
```

---

### ApiKeyRequest

**Purpose:** User requests API key, pending owner approval.

**Schema:**
```csharp
public class ApiKeyRequest
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int RequestedBy { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RequestedScopes { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public RequestStatus Status { get; set; } = RequestStatus.Pending;
    public int? ReviewedBy { get; set; }
    public string? ApprovedScopes { get; set; } // May differ from requested
    public int? ApprovedRateLimitPerMinute { get; set; }
    public int? GeneratedApiKeyId { get; set; } // FK to generated ApiKey
    public DateTime? ReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Company? Company { get; set; }
    public AppUser? RequestedByUser { get; set; }
    public AppUser? ReviewedByUser { get; set; }
    public ApiKey? GeneratedApiKey { get; set; }
}
```

**Workflow:**
```plaintext
1. User creates ApiKeyRequest (Status = Pending, RequestedScopes)
2. Owner reviews request
   ├─ Approved: ApiKey created, GeneratedApiKeyId set, ApprovedScopes may differ
   └─ Rejected: Status = Declined, request remains for audit
3. User can use API key if approved
```

**Business Rules:**
- Owner can modify requested scopes (ApprovedScopes may differ)
- Owner can set custom rate limit (ApprovedRateLimitPerMinute)
- GeneratedApiKeyId points to created ApiKey (for tracking)

---

### ApiRequestLog

**Purpose:** Log all API requests for auditing and debugging.

**Schema:**
```csharp
public class ApiRequestLog
{
    public int Id { get; set; }
    public int? CompanyId { get; set; } // Nullable - not all requests have company context
    public int? ApiKeyId { get; set; } // Nullable - anonymous requests
    public string Method { get; set; } = string.Empty; // GET/POST/PUT/DELETE
    public string Path { get; set; } = string.Empty; // /api/v1/users
    public string? QueryString { get; set; }
    public string? RequestBody { get; set; } // JSON
    public string? ResponseBody { get; set; } // JSON
    public int StatusCode { get; set; } // 200/400/401/404/500
    public int DurationMs { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ApiKey? ApiKey { get; set; }
}
```

**Business Rules:**
- Logs every API request (authenticated or anonymous)
- CorrelationId for request tracing (matches RequestLoggingMiddleware)
- Composite index on (CompanyId, Timestamp), (ApiKeyId, Timestamp), (CorrelationId)

---

## Multi-Tenancy Entities

### DirectorCompany

**Purpose:** Maps Directors to companies they can access (cross-tenant access).

**Schema:**
```csharp
public class DirectorCompany
{
    public int Id { get; set; }
    public int UserId { get; set; } // Director user
    public int CompanyId { get; set; } // Company they can access
    public int GrantedBy { get; set; } // Owner who granted access
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; } = false;

    // Navigation properties
    public AppUser? User { get; set; }
    public Company? Company { get; set; }
    public AppUser? GrantedByUser { get; set; }
}
```

**CRITICAL: No Query Filter!**
- Has UserId and CompanyId but NO global query filter
- Directors need to query "Which companies can I access?" (cross-tenant)

**Business Rules:**
- Unique index: (UserId, CompanyId) with filter `[IsDeleted] = 0`
- Only Directors and Owners can have cross-company access
- GrantedBy must be Owner of the target company

**Usage:**
```csharp
// Director switches companies
var accessibleCompanies = await db.DirectorCompanies
    .Where(dc => dc.UserId == currentUserId && !dc.IsDeleted)
    .Select(dc => dc.Company)
    .ToListAsync();
```

---

## Gamification Entities

### GameScore

**Purpose:** Leaderboard scores for shift-swap game.

**Schema:**
```csharp
public class GameScore : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int UserId { get; set; }
    public int Score { get; set; }
    public string CurrentMonth { get; set; } = string.Empty; // YYYY-MM format
    public DateTime PlayedAt { get; set; } = DateTime.UtcNow;

    public AppUser? User { get; set; }
    public Company? Company { get; set; }
}
```

**Business Rules:**
- Composite index on (CompanyId, Score) descending - all-time leaderboard
- Composite index on (CompanyId, CurrentMonth, Score) descending - monthly leaderboard
- CurrentMonth format: "2025-12" (string for easy filtering)

**Leaderboard Queries:**
```csharp
// All-time leaderboard (top 10)
var allTime = await db.GameScores
    .OrderByDescending(gs => gs.Score)
    .Take(10)
    .ToListAsync();

// Monthly leaderboard
var monthly = await db.GameScores
    .Where(gs => gs.CurrentMonth == "2025-12")
    .OrderByDescending(gs => gs.Score)
    .Take(10)
    .ToListAsync();
```

---

### Feedback

**Purpose:** User-submitted feedback (bugs, features, general comments).

**Schema:**
```csharp
public class Feedback : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int? SubmittedBy { get; set; } // Nullable - anonymous feedback allowed
    public FeedbackType Type { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? ImageFileName { get; set; } // Optional screenshot
    public FeedbackStatus Status { get; set; } = FeedbackStatus.New;
    public int? StatusUpdatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StatusUpdatedAt { get; set; }

    // Navigation properties
    public AppUser? Submitter { get; set; }
    public AppUser? StatusUpdater { get; set; }
}
```

**FeedbackType Enum:**

| Type | Value | Description |
|------|-------|-------------|
| Bug | 0 | Bug report |
| Feature | 1 | Feature request |
| General | 2 | General feedback |

**FeedbackStatus Enum:**

| Status | Value | Description |
|--------|-------|-------------|
| New | 0 | Newly submitted |
| InProgress | 1 | Being worked on |
| Resolved | 2 | Fixed/implemented |
| Closed | 3 | Closed without action |

**Business Rules:**
- SubmittedBy nullable (anonymous feedback allowed)
- ImageFileName: Screenshot stored in wwwroot/feedback/{CompanyId}/
- Composite index on (CompanyId, Status, CreatedAt)

---

## V3 Organizational Hierarchy Entities

> **Full Documentation:** [21-V3-ORGANIZATIONAL-HIERARCHY.md](21-V3-ORGANIZATIONAL-HIERARCHY.md)

V3 introduces a hierarchical organizational structure that replaces the flat Company-centric model.

### Project

**Purpose:** Top-level organizational container.

**File:** `Models/Project.cs`

```csharp
public class Project
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<Area> Areas { get; set; } = new();
}
```

---

### Area

**Purpose:** Regional/divisional grouping containing Molecules and defining JobTypes.

**File:** `Models/Area.cs`

```csharp
public class Area
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public Project Project { get; set; } = null!;
    public List<Molecule> Molecules { get; set; } = new();
    public List<JobType> JobTypes { get; set; } = new();
    public AreaSettings? Settings { get; set; }
}
```

---

### Molecule

**Purpose:** Operational unit with type-specific structure (Workforce/Tech/Helper/System).

**File:** `Models/Molecule.cs`

```csharp
public class Molecule
{
    public int Id { get; set; }
    public int AreaId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public MoleculeType Type { get; set; }  // Workforce/Tech/Helper/System
    public bool IsActive { get; set; } = true;
    public List<Company> Companies { get; set; } = new();      // Workforce
    public List<Department> Departments { get; set; } = new(); // Tech
    public List<ShiftGrouping> ShiftGroupings { get; set; } = new();
}
```

---

### Department

**Purpose:** Technical department within Tech molecules.

**File:** `Models/Department.cs`

```csharp
public class Department
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public Molecule Molecule { get; set; } = null!;
    public List<AppUser> Users { get; set; } = new();
}
```

---

### JobType

**Purpose:** Job role category at Area level for shift eligibility.

**File:** `Models/JobType.cs`

```csharp
public class JobType
{
    public int Id { get; set; }
    public int AreaId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Color { get; set; }  // Hex color
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public Area Area { get; set; } = null!;
    public List<AppUser> Users { get; set; } = new();
}
```

---

## V3 Authorization Entities

> **Full Documentation:** [22-V3-GRANT-AUTHORIZATION.md](22-V3-GRANT-AUTHORIZATION.md)

V3 replaces the enum-based UserRole with a grant-based authorization system.

### Grant

**Purpose:** Individual permission with hierarchical scope.

**File:** `Models/Grant.cs`

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
    public bool CanOwn { get; set; }   // Can perform action
    public bool CanGive { get; set; }  // Can delegate

    // Audit
    public int? GrantedByUserId { get; set; }
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    public bool IsAutoGrant { get; set; }  // From role template
}
```

---

### GrantType

**Purpose:** Permission definition. 90+ system types across 12 categories.

**File:** `Models/GrantType.cs`

```csharp
public class GrantType
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string NameKey { get; set; } = string.Empty;
    public GrantCategory Category { get; set; }
    public GrantScopeLevel DefaultScope { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
}
```

**Categories:** Shift, Duty, Chore, Vacation, Swap, UserManagement, GrantManagement, Hierarchy, Settings, Analytics, Email, System

---

### RoleTemplate

**Purpose:** Bundle of grants for assignment. 11 system role templates.

**File:** `Models/RoleTemplate.cs`

```csharp
public class RoleTemplate
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;  // "Owner", "BRDirector", "AlhutLead"
    public string NameKey { get; set; } = string.Empty;
    public RoleScopeLevel ScopeLevel { get; set; }
    public bool IsSystem { get; set; }
    public int SortOrder { get; set; }
    public List<RoleTemplateGrant> AutoGrants { get; set; } = new();
}
```

**System Roles:** Owner, AreaAdmin, MoleculeAdmin, AlhutDirector, TextDirector, BRDirector, AlhutLead, TextLead, DepartmentLead, Assigner, Employee

---

### UserRoleAssignment

**Purpose:** Assigns RoleTemplate to user with scope.

**File:** `Models/UserRoleAssignment.cs`

```csharp
public class UserRoleAssignment
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int RoleTemplateId { get; set; }

    // Scope
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

## V3 Scheduling Entities

> **Full Documentation:** [23-V3-SCHEDULING-SYSTEM.md](23-V3-SCHEDULING-SYSTEM.md)

### ShiftGrouping

**Purpose:** Groups companies for coordinated shift scheduling.

**File:** `Models/ShiftGrouping.cs`

```csharp
public class ShiftGrouping
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public List<ShiftGroupingCompany> Companies { get; set; } = new();
    public List<ShiftGroupingJobType> JobTypes { get; set; } = new();
}
```

---

### ShiftProgram

**Purpose:** Weekly template for shift generation.

**File:** `Models/ShiftProgram.cs`

```csharp
public class ShiftProgram
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int ShiftTypeId { get; set; }
    public int? JobTypeId { get; set; }
    public int? ShiftGroupingId { get; set; }
    public string? TechShiftType { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DefaultStaffingRequired { get; set; } = 1;
    public List<ProgramDay> ProgramDays { get; set; } = new();
}
```

---

### MasterProgram

**Purpose:** Collection of Programs for complete weekly schedules.

**File:** `Models/MasterProgram.cs`

```csharp
public class MasterProgram
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public List<MasterProgramItem> Items { get; set; } = new();
}
```

---

### SetupTask

**Purpose:** Guided onboarding for new organizational units.

**File:** `Models/SetupTask.cs`

```csharp
public class SetupTask
{
    public int Id { get; set; }
    public SetupTaskType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public int? MoleculeId { get; set; }
    public int? CompanyId { get; set; }
    public int AssignedToUserId { get; set; }
    public SetupTaskStatus Status { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
```

---

## Enums Reference

### UserRole

**File:** `Models/Support/Enums.cs`

```csharp
public enum UserRole
{
    Owner = 0,      // Company owner, full access
    Manager = 1,    // Shift planning, user management
    Employee = 2,   // Standard user
    Director = 3,   // Cross-company visibility
    Trainee = 4,    // Limited access, shadowing
    Assigner = 5    // Can edit Chores only, not On-Duty
}
```

**Role Hierarchy:**
- **Owner:** Full access (company settings, billing, all features)
- **Director:** Cross-company visibility, management access
- **Manager:** Shift planning, approve requests, manage users
- **Assigner:** Assign chores (but not on-duty)
- **Employee:** View shifts, request time-off, swap shifts
- **Trainee:** Shadow employees, limited shift access

---

### RequestStatus

```csharp
public enum RequestStatus
{
    Pending = 0,
    Approved = 1,
    Declined = 2
}
```

**Used By:**
- TimeOffRequest
- SwapRequest
- ApiKeyRequest

---

### TimeOffType

```csharp
public enum TimeOffType
{
    Vacation = 0,  // Full vacation: StartDate 00:00 to EndDate+1 13:00
    After = 1      // Half day: StartDate 16:00 to StartDate+1 13:00
}
```

**Time Calculations:**

| Type | Actual Start | Actual End |
|------|--------------|------------|
| Vacation | StartDate 00:00 | EndDate+1 13:00 |
| After | StartDate 16:00 | StartDate+1 13:00 |

---

### NotificationType

```csharp
public enum NotificationType
{
    ShiftAdded = 0,
    ShiftRemoved = 1,
    TimeOffApproved = 2,
    TimeOffDeclined = 3,
    SwapRequestApproved = 4,
    SwapRequestDeclined = 5,
    TraineeShadowingAdded = 6,
    TraineeShadowingRemoved = 7,
    EmployeeTraineeAdded = 8,
    EmployeeTraineeRemoved = 9,
    TraineeShadowingCanceledTimeOff = 10,
    TraineeShadowingCanceledRoleChange = 11,
    ChoreAssigned = 12,
    ChoreCanceled = 13,
    OnDutyAssigned = 14,
    OnDutyCanceled = 15,
    TimeOffDeleted = 16,
    FeedbackSubmitted = 17
}
```

**18 notification types** covering all user-facing actions.

---

### OnDutyType

```csharp
public enum OnDutyType
{
    Hakam = 0,  // חק"מכו - Day Shift: Hakam
    Lead = 1    // מובילתו - Day Shift: Lead
}
```

**Note:** UI displays as "Day Shifts" (not "On-Duty").

---

## Entity Relationships

### Relationship Summary

```plaintext
Company (1) ─────< (M) AppUser
                        │
                        ├─< (M) ShiftAssignment (as User)
                        ├─< (M) TimeOffRequest
                        ├─< (M) SwapRequest (as FromUser/ToUser)
                        ├─< (M) Chore
                        ├─< (M) OnDuty (GLOBAL, cross-company)
                        ├─< (M) UserNotification
                        └─< (M) TeamCalendar (as Owner)

ShiftType (1) ──< (M) ShiftInstance ──< (M) ShiftAssignment
                                               │
                                               └─< (M) SwapRequest (as FromAssignment/ToAssignment)

TeamCalendar (1) ──< (M) TeamCalendarMember ──> (1) AppUser (as Member)

DirectorCompany (M) ──> (1) AppUser (Director)
                   └──> (1) Company (accessible company)
```

**Key Relationships:**
- **Company → AppUser:** One-to-many (company has employees)
- **ShiftType → ShiftInstance:** One-to-many (shift type has occurrences)
- **ShiftInstance → ShiftAssignment:** One-to-many (shift has assignments)
- **AppUser → ShiftAssignment:** One-to-many (user has assignments)
- **AppUser → OnDuty:** One-to-many (GLOBAL, cross-company)
- **DirectorCompany:** Many-to-many mapping (Directors access multiple companies)

---

## Business Rules

### Multi-Tenancy Rules

1. **CompanyId Scoping:**
   - All entities implementing `IBelongsToCompany` are scoped to CompanyId
   - Global query filters automatically add `WHERE CompanyId = {current}`
   - CompanyIdInterceptor auto-sets CompanyId on new entities

2. **Exceptions to Multi-Tenancy:**
   - **OnDuty:** No CompanyId (global/public table)
   - **OnDutyTypeConfig:** No CompanyId (global configuration)
   - **DirectorCompany:** No query filter (cross-tenant mapping)
   - **ApiKey, ApiKeyRequest, ApiRequestLog:** Have CompanyId but no query filter

### Soft Delete Rules

3. **Soft Delete Pattern:**
   - **Chore:** CanceledAt/CanceledBy
   - **OnDuty:** CanceledAt/CanceledBy
   - **TeamCalendar:** IsDeleted
   - **DirectorCompany:** IsDeleted

4. **Unique Constraints with Soft Deletes:**
   - Filtered unique indexes exclude soft-deleted records
   - Example: `(CompanyId, UserId, Date) WHERE [CanceledAt] IS NULL`

### Concurrency Rules

5. **Optimistic Concurrency:**
   - **ShiftInstance:** Concurrency token prevents concurrent edits
   - EF Core throws `DbUpdateConcurrencyException` if token changed

### Validation Rules

6. **Email Uniqueness:**
   - AppUser.Email must be unique globally (across all companies)
   - Prevents user from having accounts in multiple companies with same email

7. **One Chore Per User Per Day:**
   - Unique index: (CompanyId, UserId, Date, CanceledAt) WHERE [CanceledAt] IS NULL
   - Active chores only (canceled chores don't count)

8. **One Active Notification Preference Per User:**
   - Unique index: (CompanyId, UserId) WHERE [IsActive] = 1

### Security Rules

9. **Password Hashing:**
   - PBKDF2 with 100,000 iterations, SHA256
   - Unique salt per user (stored in PasswordSalt)

10. **API Key Hashing:**
    - KeyHash: SHA256 hash of API key
    - PlainTextKey: Original key (security warning - consider encryption)

11. **Account Lockout:**
    - 5 failed login attempts → 15-minute lockout
    - LockoutEnd timestamp

### Relationship Rules

12. **Director Multi-Company Access:**
    - Directors can access multiple companies via DirectorCompany table
    - Must be granted by Owner of target company

13. **Trainee Shadowing:**
    - ShiftAssignment.TraineeUserId for shadowing
    - Trainee doesn't count toward StaffingRequired

14. **TimeOffRequest Conflict Detection:**
    - No shift assignments allowed during time-off period
    - GetActualStartDateTime() / GetActualEndDateTime() for precise checking

---

## Summary

ShiftManager's domain model consists of **28 entities** across 10 functional categories:

**Entity Count by Category:**
- Core Domain: 2 (Company, AppUser)
- Shift Management: 3 (ShiftType, ShiftInstance, ShiftAssignment)
- Request Workflows: 3 (TimeOffRequest, SwapRequest, UserJoinRequest)
- Task Assignments: 4 (Chore, OnDuty, OnDutyRoleSubscription, OnDutyTypeConfig)
- Team Collaboration: 2 (TeamCalendar, TeamCalendarMember)
- Notifications: 2 (UserNotification, DailyNotificationPreference)
- Audit & Compliance: 3 (AuditLog, RoleAssignmentAudit, ProfileChangeAudit)
- Configuration: 4 (AppConfig, EmailConfig, EmailApiLog, GriffinConfig)
- API Infrastructure: 3 (ApiKey, ApiKeyRequest, ApiRequestLog)
- Multi-Tenancy: 1 (DirectorCompany)
- Gamification: 2 (GameScore, Feedback)

**Key Design Patterns:**
- **Multi-tenancy:** 24 entities implement IBelongsToCompany
- **Soft deletes:** CanceledAt/IsDeleted for audit trails
- **Optimistic concurrency:** Concurrency tokens on frequently-updated entities
- **Type safety:** DateOnly/TimeOnly for dates and times
- **Denormalization:** User info in audit logs preserved after user deletion

**Next Steps:**
- [07-SERVICE-LAYER.md](07-SERVICE-LAYER.md) - Services that operate on these entities
- [14-WORKFLOWS-AND-BUSINESS-LOGIC.md](14-WORKFLOWS-AND-BUSINESS-LOGIC.md) - Workflows using these entities
- [03-DATABASE-SCHEMA.md](03-DATABASE-SCHEMA.md) - Database schema with indexes

---

**Document Metadata:**
- **Created:** Phase 2 - Core Systems
- **Lines:** 2,500+
- **Related Files:** Models/*.cs, Models/Api/*.cs, Models/Support/Enums.cs
- **See Also:** 03-DATABASE-SCHEMA.md, 05-MULTI-TENANCY-DEEP-DIVE.md, 07-SERVICE-LAYER.md
