# 09-API-LAYER.md - REST and Internal APIs

**Part of the ShiftManager Genesis Documentation**
**Document 9 of 19 - Complete API endpoint catalog and authentication patterns**

---

## Table of Contents

1. [Overview](#overview)
2. [API Architecture](#api-architecture)
3. [REST API v1 (External)](#rest-api-v1-external)
4. [Internal API Endpoints](#internal-api-endpoints)
5. [API Authentication](#api-authentication)
6. [Request/Response Patterns](#requestresponse-patterns)
7. [Error Handling](#error-handling)
8. [Rate Limiting](#rate-limiting)
9. [Feature Flags](#feature-flags)
10. [Security Considerations](#security-considerations)

---

## Overview

ShiftManager exposes **two distinct API layers**:

1. **REST API v1** (`/api/v1/*`) - External machine-to-machine API requiring **API key authentication**
2. **Internal API Endpoints** (`/Api/*` and `/api/*`) - Browser-based endpoints using **cookie authentication**

**Total API Surface:**
- **11 REST API v1 controllers** (~38 endpoints)
- **9 Internal API endpoints** (game, calendar, session)
- **2 authentication mechanisms** (API keys vs. cookies)
- **Dual middleware pipeline** (ApiAuthenticationMiddleware + cookie auth)

**Key Design Decisions:**
- ✅ **No JWT tokens** - Uses API keys for external, cookies for internal
- ✅ **Scope-based permissions** - Fine-grained access control (e.g., `user:read`, `shift:write`)
- ✅ **Feature flag gating** - All endpoints can be toggled via configuration
- ✅ **Multi-tenant isolation** - CompanyId enforced on all tenant-scoped resources
- ✅ **Global on-duty entities** - OnDuty API explicitly bypasses company filtering
- ✅ **Structured error responses** - ApiProblemDetails for consistency

**Why This Matters:**
This dual-API design allows ShiftManager to serve both external integrations (mobile apps, scripts) via REST API v1 and internal browser-based UI via lightweight internal endpoints, without mixing authentication concerns.

---

## API Architecture

### Dual Authentication Model

```
┌─────────────────────────────────────────────────────────────┐
│                    Incoming HTTP Request                     │
└──────────────────────┬──────────────────────────────────────┘
                       │
           ┌───────────▼────────────┐
           │   Path-Based Routing   │
           └───────────┬────────────┘
                       │
        ┌──────────────┴───────────────┐
        │                              │
┌───────▼───────┐            ┌─────────▼────────┐
│  /api/v1/*    │            │  /Api/* or       │
│               │            │  /api/team-*     │
│ (External)    │            │  (Internal)      │
└───────┬───────┘            └─────────┬────────┘
        │                              │
        │                              │
┌───────▼──────────────┐     ┌─────────▼─────────────┐
│ ApiAuthentication    │     │ Cookie Authentication │
│ Middleware           │     │ [AllowAnonymous] or   │
│                      │     │ [Authorize(Policy)]   │
│ Validates:           │     │                       │
│ • X-API-Key header   │     │ Validates:            │
│ • API key hash       │     │ • .AspNetCore.Cookies │
│ • Expiration         │     │ • Session timeout     │
│ • Scope permissions  │     │ • Policy requirements │
│ • Rate limits        │     │                       │
└───────┬──────────────┘     └─────────┬─────────────┘
        │                              │
        └──────────────┬───────────────┘
                       │
            ┌──────────▼──────────┐
            │  Controller/Handler │
            │                     │
            │  Uses Claims:       │
            │  • CompanyId        │
            │  • UserId           │
            │  • ApiKeyId         │
            └──────────┬──────────┘
                       │
            ┌──────────▼───────────┐
            │  Service Layer       │
            │  (Multi-tenant logic)│
            └──────────────────────┘
```

**Internal API Whitelist (Bypasses API Key Requirement):**

In `Middleware/ApiAuthenticationMiddleware.cs:108-119`:
```csharp
private bool IsInternalWebUiEndpoint(string path)
{
    // These endpoints use cookie authentication instead of API keys
    return path.StartsWith("/api/team-calendars", StringComparison.OrdinalIgnoreCase) ||
           path.Equals("/Api/SessionStatus", StringComparison.OrdinalIgnoreCase) ||
           path.StartsWith("/Api/Calendar", StringComparison.OrdinalIgnoreCase) ||
           path.StartsWith("/Api/Game", StringComparison.OrdinalIgnoreCase);
}
```

**Why Two APIs?**
- **External API** - Machine-to-machine, long-lived API keys, explicit scopes, rate-limited
- **Internal API** - Browser-first, session cookies, policy-based authorization, no rate limits
- **Separation of concerns** - External clients never touch cookie sessions; internal UI never manages API keys

---

## REST API v1 (External)

### Base URL Pattern
```
https://{host}/api/v1/{resource}
```

**Authentication:** All endpoints require `X-API-Key` header
**Content-Type:** `application/json`
**Tenant Isolation:** Automatic via CompanyId claim (except OnDuty which is global)

### Controller Catalog

#### 1. UsersController
**Location:** `Controllers/Api/V1/UsersController.cs` (partial implementation)
**Route:** `/api/v1/users`
**Scope Required:** `user:read`, `user:write`

**Endpoints:**
| Method | Path | Scope | Description |
|--------|------|-------|-------------|
| GET | `/users` | user:read | List users with pagination |
| GET | `/users/{id}` | user:read | Get single user by ID |

**List Users Query Parameters:**
```typescript
{
  page?: number = 1,
  pageSize?: number = 50,
  role?: string,           // "Owner", "Manager", etc.
  isActive?: boolean,
  search?: string          // Search by email or display name
}
```

**Response Schema (PaginatedResponse<UserDto>):**
```json
{
  "data": [
    {
      "id": 5,
      "email": "manager@company.com",
      "displayName": "John Doe",
      "role": "Manager",
      "isActive": true,
      "createdAt": "2025-01-15T10:30:00Z"
    }
  ],
  "pagination": {
    "page": 1,
    "pageSize": 50,
    "totalCount": 150,
    "totalPages": 3
  }
}
```

**Feature Flags:**
```json
{
  "Features:Api:Users:ListEnabled": false,
  "Features:Api:Users:GetEnabled": false
}
```

---

#### 2. ShiftsController
**Location:** `Controllers/Api/V1/ShiftsController.cs` (partial implementation)
**Route:** `/api/v1/shifts`
**Scope Required:** `shift:read`, `shift:write`

**Endpoints:**
| Method | Path | Scope | Description |
|--------|------|-------|-------------|
| GET | `/shifts` | shift:read | List shifts with pagination |
| GET | `/shifts/{id}` | shift:read | Get single shift by ID |

**List Shifts Query Parameters:**
```typescript
{
  page?: number = 1,
  pageSize?: number = 50,
  startDate?: string,      // yyyy-MM-dd
  endDate?: string,        // yyyy-MM-dd
  shiftTypeId?: number,
  userId?: number,
  hasOpenSlots?: boolean
}
```

**Response Schema (PaginatedResponse<ShiftDto>):**
```json
{
  "data": [
    {
      "id": 42,
      "shiftTypeId": 1,
      "shiftTypeName": "Morning",
      "workDate": "2025-06-15",
      "startTime": "08:00",
      "endTime": "16:00",
      "staffingRequired": 3,
      "assignedCount": 2,
      "hasOpenSlots": true
    }
  ],
  "pagination": { "page": 1, "pageSize": 50, "totalCount": 200, "totalPages": 4 }
}
```

---

#### 3. TimeOffController
**Location:** `Controllers/Api/V1/TimeOffController.cs` (436 lines)
**Route:** `/api/v1/time-off-requests`
**Scope Required:** `timeoff:read`, `timeoff:write`, `timeoff:approve`

**Endpoints:**
| Method | Path | Scope | Description |
|--------|------|-------|-------------|
| GET | `/time-off-requests` | timeoff:read | List time-off requests |
| GET | `/time-off-requests/{id}` | timeoff:read | Get single request |
| POST | `/time-off-requests` | timeoff:write | Create new request |
| POST | `/time-off-requests/{id}/approve` | timeoff:approve | Approve request (admin) |
| POST | `/time-off-requests/{id}/decline` | timeoff:approve | Decline request (admin) |

**Create Time-Off Request Body:**
```json
{
  "userId": 10,
  "startDate": "2025-07-01",
  "endDate": "2025-07-05",
  "reason": "Family vacation"
}
```

**Response Schema (TimeOffDto):**
```json
{
  "id": 23,
  "userId": 10,
  "userDisplayName": "Jane Smith",
  "startDate": "2025-07-01",
  "endDate": "2025-07-05",
  "timeOffType": "Vacation",
  "reason": "Family vacation",
  "status": "Pending",
  "reviewerId": null,
  "reviewerName": null,
  "reviewedAt": null,
  "createdAt": "2025-06-10T14:22:00Z"
}
```

**Conflict Detection:**
- Returns **409 Conflict** if time-off overlaps with existing approved request
- Error message: `"This time-off request overlaps with an existing approved request"`

**Feature Flags:**
```json
{
  "Features:Api:TimeOff:ListEnabled": false,
  "Features:Api:TimeOff:GetEnabled": false,
  "Features:Api:TimeOff:CreateEnabled": false,
  "Features:Api:TimeOff:ApproveEnabled": false,
  "Features:Api:TimeOff:DeclineEnabled": false
}
```

---

#### 4. NotificationsController
**Location:** `Controllers/Api/V1/NotificationsController.cs` (317 lines)
**Route:** `/api/v1/notifications`
**Scope Required:** `notification:read`, `notification:write`

**Endpoints:**
| Method | Path | Scope | Description |
|--------|------|-------|-------------|
| GET | `/notifications` | notification:read | List notifications with filtering |
| GET | `/notifications/{id}` | notification:read | Get single notification |
| POST | `/notifications/{id}/mark-read` | notification:write | Mark notification as read |
| POST | `/notifications/mark-all-read` | notification:write | Mark all user's notifications as read |

**List Notifications Query Parameters:**
```typescript
{
  page?: number = 1,
  pageSize?: number = 50,
  userId?: number,
  isRead?: boolean,
  type?: string           // "ShiftAdded", "TimeOffApproved", etc.
}
```

**Mark All as Read Request Body:**
```json
{
  "userId": 5
}
```

**Response Schema (MarkAllReadResponse):**
```json
{
  "markedCount": 12
}
```

**Notification Types:**
- `ShiftAdded`, `ShiftRemoved`, `TimeOffApproved`, `TimeOffDeclined`
- `SwapRequestCreated`, `SwapRequestApproved`, `ChoreAssigned`
- See 06-DOMAIN-MODELS.md for full list (18 types)

---

#### 5. SwapRequestsController
**Location:** `Controllers/Api/V1/SwapRequestsController.cs` (495 lines)
**Route:** `/api/v1/swap-requests`
**Scope Required:** `swaps:read`, `swaps:write`, `swaps:approve`

**Endpoints:**
| Method | Path | Scope | Description |
|--------|------|-------|-------------|
| GET | `/swap-requests` | swaps:read | List swap requests with filtering |
| GET | `/swap-requests/{id}` | swaps:read | Get single swap request |
| POST | `/swap-requests` | swaps:write | Create new swap request |
| POST | `/swap-requests/{id}/approve` | swaps:approve | Approve swap (admin) |
| POST | `/swap-requests/{id}/decline` | swaps:approve | Decline swap (admin) |
| DELETE | `/swap-requests/{id}` | swaps:write | Cancel own pending swap |

**Create Swap Request Body:**
```json
{
  "fromAssignmentId": 42,
  "toAssignmentId": 43,
  "toUserId": 10,
  "reason": "Need to switch with John for family event"
}
```

**List Swap Requests Query Parameters:**
```typescript
{
  page?: number = 1,
  pageSize?: number = 50,
  userId?: number,
  status?: string,         // "Pending", "Approved", "Declined"
  startDate?: string,      // yyyy-MM-dd
  endDate?: string,        // yyyy-MM-dd
  includeRelated?: boolean // Include shift details
}
```

**Decline Swap Request Body:**
```json
{
  "declineReason": "Staffing conflict on destination shift"
}
```

**Business Rules:**
- Only the requester can **DELETE** their own **pending** swap
- Reviewers can **approve** or **decline** via separate endpoints
- Approval automatically swaps the shift assignments

---

#### 6. ChoresController
**Location:** `Controllers/Api/V1/ChoresController.cs` (421 lines)
**Route:** `/api/v1/chores`
**Scope Required:** `chores:read`, `chores:write`

**Endpoints:**
| Method | Path | Scope | Description |
|--------|------|-------|-------------|
| GET | `/chores` | chores:read | List chores with filtering |
| GET | `/chores/{id}` | chores:read | Get single chore |
| POST | `/chores` | chores:write | Create new chore |
| PATCH | `/chores/{id}` | chores:write | Update chore (title/notes) |
| DELETE | `/chores/{id}` | chores:write | Delete (cancel) chore |

**Create Chore Body:**
```json
{
  "userId": 10,
  "date": "2025-06-15",
  "title": "Clean storage room",
  "notes": "Focus on back shelves"
}
```

**Update Chore Body (PATCH):**
```json
{
  "title": "Clean storage room and restock supplies",
  "notes": "Updated instructions from manager"
}
```

**List Chores Query Parameters:**
```typescript
{
  page?: number = 1,
  pageSize?: number = 50,
  userId?: number,
  startDate?: string,      // yyyy-MM-dd
  endDate?: string,        // yyyy-MM-dd
  includeRelated?: boolean,
  includeCanceled?: boolean
}
```

**Response Schema (ChoreDto):**
```json
{
  "id": 15,
  "userId": 10,
  "userDisplayName": "Jane Smith",
  "date": "2025-06-15",
  "title": "Clean storage room",
  "notes": "Focus on back shelves",
  "createdById": 5,
  "createdByName": "Manager",
  "createdAt": "2025-06-10T09:00:00Z",
  "canceledAt": null,
  "canceledById": null
}
```

---

#### 7. OnDutyController
**Location:** `Controllers/Api/V1/OnDutyController.cs` (424 lines)
**Route:** `/api/v1/on-duty`
**Scope Required:** `onduty:read`, `onduty:write`

**⚠️ CRITICAL DIFFERENCE: OnDuty is GLOBAL (not company-scoped)**

The `OnDuties` table has **NO CompanyId** field. All on-duty assignments are visible across companies. This is by design for director-level cross-company visibility.

**Endpoints:**
| Method | Path | Scope | Description |
|--------|------|-------|-------------|
| GET | `/on-duty` | onduty:read | List on-duty assignments (global) |
| GET | `/on-duty/{id}` | onduty:read | Get single on-duty assignment |
| POST | `/on-duty` | onduty:write | Create new on-duty assignment |
| PATCH | `/on-duty/{id}` | onduty:write | Update on-duty assignment |
| DELETE | `/on-duty/{id}` | onduty:write | Delete (cancel) on-duty |

**Create On-Duty Body:**
```json
{
  "userId": 10,
  "date": "2025-06-15",
  "type": "Hakam"
}
```

**List On-Duty Query Parameters:**
```typescript
{
  page?: number = 1,
  pageSize?: number = 50,
  userId?: number,
  startDate?: string,      // yyyy-MM-dd
  endDate?: string,        // yyyy-MM-dd
  type?: string,           // "Hakam", "Lead"
  includeRelated?: boolean,
  includeCanceled?: boolean
}
```

**Response Schema (OnDutyDto):**
```json
{
  "id": 8,
  "userId": 10,
  "userDisplayName": "Jane Smith",
  "date": "2025-06-15",
  "type": "Hakam",
  "createdAt": "2025-06-10T10:00:00Z",
  "canceledAt": null,
  "canceledById": null
}
```

**Why Global?**
- Directors manage on-duty across multiple companies
- On-duty assignments need cross-company visibility
- Users from Company A can see on-duty for Company B (if they're directors)

---

#### 8. FeedbackController
**Location:** `Controllers/Api/V1/FeedbackController.cs` (387 lines)
**Route:** `/api/v1/feedback`
**Scope Required:** `feedback:read`, `feedback:write`

**Endpoints:**
| Method | Path | Scope | Description |
|--------|------|-------|-------------|
| GET | `/feedback` | feedback:read | List feedback with filtering |
| GET | `/feedback/{id}` | feedback:read | Get single feedback |
| POST | `/feedback` | feedback:write | Create new feedback |
| PATCH | `/feedback/{id}/status` | feedback:write | Update feedback status |
| DELETE | `/feedback/{id}` | feedback:write | Delete feedback |

**Create Feedback Body:**
```json
{
  "type": "Bug",
  "content": "The schedule page doesn't load on mobile Safari"
}
```

**Update Feedback Status Body:**
```json
{
  "status": "InProgress"
}
```

**List Feedback Query Parameters:**
```typescript
{
  page?: number = 1,
  pageSize?: number = 50,
  submittedBy?: number,
  type?: string,           // "Bug", "Feature", "General"
  status?: string,         // "New", "InProgress", "Resolved", "Closed"
  startDate?: DateTime,
  endDate?: DateTime,
  includeRelated?: boolean
}
```

**Feedback Types:**
- `Bug` (0) - Bug report
- `Feature` (1) - Feature request
- `General` (2) - General feedback

**Feedback Statuses:**
- `New` (0) - Just submitted
- `InProgress` (1) - Being worked on
- `Resolved` (2) - Fixed/completed
- `Closed` (3) - Archived

---

#### 9. AnalyticsController
**Location:** `Controllers/Api/V1/AnalyticsController.cs` (163 lines)
**Route:** `/api/v1/analytics`
**Scope Required:** `analytics:read`

**Endpoints:**
| Method | Path | Scope | Description |
|--------|------|-------|-------------|
| GET | `/analytics/summary` | analytics:read | Get aggregate metrics summary |

**Query Parameters:**
```typescript
{
  startDate?: string,      // yyyy-MM-dd (default: 30 days ago)
  endDate?: string         // yyyy-MM-dd (default: today)
}
```

**Response Schema (AnalyticsSummary):**
```json
{
  "companyId": 2,
  "periodStart": "2025-05-01",
  "periodEnd": "2025-05-31",
  "totalUsers": 45,
  "activeUsers": 42,
  "shiftsScheduled": 320,
  "pendingTimeOffRequests": 5,
  "unreadNotifications": 127,
  "generatedAt": "2025-06-01T10:00:00Z"
}
```

**Metrics Calculated:**
- `totalUsers` - Total users in company
- `activeUsers` - Users with `IsActive=true`
- `shiftsScheduled` - Shift instances in date range
- `pendingTimeOffRequests` - Requests with `Status=Pending`
- `unreadNotifications` - Notifications with `IsRead=false`

---

#### 10. AuditLogsController
**Location:** `Controllers/Api/V1/AuditLogsController.cs` (192 lines)
**Route:** `/api/v1/audit-logs`
**Scope Required:** `audit:read`

**Endpoints:**
| Method | Path | Scope | Description |
|--------|------|-------|-------------|
| GET | `/audit-logs` | audit:read | List audit logs with filtering |

**Query Parameters:**
```typescript
{
  page?: number = 1,
  pageSize?: number = 50,
  userId?: number,
  action?: string,         // "UserCreated", "ShiftDeleted", etc.
  startDate?: string,      // Date parsing (DateTime.TryParse)
  endDate?: string
}
```

**Response Schema (PaginatedResponse<AuditLogDto>):**
```json
{
  "data": [
    {
      "id": 152,
      "userId": 5,
      "userEmail": "manager@company.com",
      "userDisplayName": "Manager",
      "action": "ShiftDeleted",
      "entityType": "ShiftInstance",
      "entityId": 42,
      "description": "Deleted shift Morning on 2025-06-15",
      "details": "{\"shiftId\":42,\"workDate\":\"2025-06-15\"}",
      "ipAddress": "192.168.1.100",
      "userAgent": "Mozilla/5.0...",
      "timestamp": "2025-06-10T14:30:00Z"
    }
  ],
  "pagination": { "page": 1, "pageSize": 50, "totalCount": 3200, "totalPages": 64 }
}
```

**Audit Log Actions:**
- See 07-SERVICE-LAYER.md for full list of actions logged by AuditLogService
- Examples: `UserCreated`, `ShiftDeleted`, `TimeOffApproved`, `RoleChanged`

**Denormalized Fields:**
- `userEmail`, `userDisplayName` - Preserved even if user is deleted
- Ensures audit trail integrity

---

#### 11. TeamCalendarsController
**Location:** `Controllers/TeamCalendarsController.cs` (390 lines)
**Route:** `/api/team-calendars` (NOTE: Not `/api/v1/`)
**Scope Required:** None (uses cookie authentication)

**⚠️ IMPORTANT: This is an internal API endpoint whitelisted in ApiAuthenticationMiddleware**

**Endpoints:**
| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/team-calendars` | Cookie | List user's calendars |
| GET | `/team-calendars/{id}` | Cookie | Get calendar details |
| POST | `/team-calendars` | Cookie | Create new calendar |
| PUT | `/team-calendars/{id}` | Cookie | Rename calendar |
| DELETE | `/team-calendars/{id}` | Cookie | Soft delete calendar |
| GET | `/team-calendars/{id}/members` | Cookie | Get calendar members |
| PUT | `/team-calendars/{id}/members` | Cookie | Update members |
| GET | `/team-calendars/{id}/week` | Cookie | Get week view |

**Create Calendar Request:**
```json
{
  "name": "Sales Team"
}
```

**Update Members Request:**
```json
{
  "memberUserIds": [10, 15, 22, 31]
}
```

**Get Week View Query Parameters:**
```typescript
{
  startDate: string        // yyyy-MM-dd (Monday of week)
}
```

**Response Schema (Week View):**
```json
{
  "id": 5,
  "name": "Sales Team",
  "members": [
    { "id": 10, "displayName": "Jane Smith" }
  ],
  "week": [
    {
      "date": "2025-06-16",
      "shifts": [
        {
          "userId": 10,
          "shiftTypeName": "Morning",
          "startTime": "08:00",
          "endTime": "16:00"
        }
      ],
      "chores": [...],
      "onDuties": [...]
    }
  ]
}
```

**Why Not `/api/v1/`?**
- This predates the v1 API structure
- Used exclusively by browser UI, not external clients
- Whitelisted in ApiAuthenticationMiddleware to bypass API key requirement

---

## Internal API Endpoints

These endpoints are designed for **browser-based UI** and use **cookie authentication** instead of API keys.

### Endpoint Catalog

#### 1. Session Status
**Location:** `Pages/Api/SessionStatus.cshtml.cs` (186 lines)
**Route:** `GET /Api/SessionStatus`
**Auth:** `[AllowAnonymous]` (but checks authentication internally)

**Purpose:** Validate session state for client-side session timeout warnings

**Response Schema:**
```json
{
  "authenticated": true,
  "state": "ok",
  "secondsRemaining": 3420
}
```

**States:**
- `"ok"` - Session healthy (> 5 minutes remaining)
- `"warning"` - Session expiring soon (≤ 5 minutes remaining)
- `"expired"` - Session expired (returns 401)

**Used By:** `wwwroot/js/session-check.js` - Polls every 60 seconds

**Implementation:**
```csharp
var authResult = await HttpContext.AuthenticateAsync(
    CookieAuthenticationDefaults.AuthenticationScheme);
var expiresUtc = authResult.Properties.ExpiresUtc;
var timeRemaining = expiresUtc.HasValue ? (expiresUtc.Value - now) : TimeSpan.FromDays(7);

string state = timeRemaining <= TimeSpan.Zero ? "expired" :
               timeRemaining <= WarningThreshold ? "warning" : "ok";
```

---

#### 2. Game - Get Leaderboard
**Location:** `Pages/Api/Game/GetLeaderboard.cshtml.cs` (135 lines)
**Route:** `GET /Api/Game/GetLeaderboard?type={all-time|monthly}`
**Auth:** `[Authorize]`

**Purpose:** Fetch game leaderboard for shift-swap game

**Query Parameters:**
```typescript
{
  type?: string = "all-time"    // "all-time" or "monthly"
}
```

**Response Schema:**
```json
{
  "success": true,
  "type": "all-time",
  "leaderboard": [
    {
      "rank": 1,
      "userId": 10,
      "displayName": "Jane Smith",
      "score": 25340,
      "playedAt": "2025-06-08T15:30:00Z",
      "isCurrentUser": false
    }
  ],
  "userBest": {
    "rank": 23,
    "userId": 5,
    "displayName": "You",
    "score": 12500,
    "isCurrentUser": true
  }
}
```

**Business Logic:**
- Top 10 players shown in leaderboard
- If current user not in top 10, their best score shown separately
- Monthly leaderboard filters by `CurrentMonth` field (YYYY-MM format)

---

#### 3. Game - Save Score
**Location:** `Pages/Api/Game/SaveScore.cshtml.cs` (109 lines)
**Route:** `POST /Api/Game/SaveScore`
**Auth:** `[Authorize]`, `[IgnoreAntiforgeryToken]`

**Purpose:** Save game score to leaderboard

**Request Body:**
```json
{
  "score": 15340
}
```

**Response Schema:**
```json
{
  "success": true,
  "rank": 12,
  "score": 15340
}
```

**Implementation:**
```csharp
var gameScore = new GameScore
{
    CompanyId = companyId,
    UserId = userId,
    Score = data.Score,
    PlayedAt = DateTime.UtcNow,
    CurrentMonth = DateTime.UtcNow.ToString("yyyy-MM")  // For monthly leaderboard
};

// Calculate rank (IgnoreQueryFilters for global leaderboard)
var rank = await _db.GameScores
    .IgnoreQueryFilters()
    .Where(gs => gs.Score >= data.Score)
    .Select(gs => gs.UserId)
    .Distinct()
    .CountAsync();
```

**Why `IgnoreQueryFilters()`?**
- GameScores are **tenant-scoped** (have CompanyId)
- But leaderboard shown globally across all companies
- Explicit bypass of global query filter

---

#### 4. Game - Get Localization
**Location:** `Pages/Api/Game/GetLocalization.cshtml.cs` (108 lines)
**Route:** `GET /Api/Game/GetLocalization`
**Auth:** `[AllowAnonymous]`, `[IgnoreAntiforgeryToken]`

**Purpose:** Fetch localized strings for game UI

**Response Schema:**
```json
{
  "title": "Shift Swap Game",
  "instructions": "Match 3 or more shifts...",
  "score": "Score",
  "playAgain": "Play Again",
  "roasts": {
    "r1000": ["Not bad for a beginner!", ...],
    "r5000": ["Now we're talking!", ...],
    "r10000": ["Impressive work!", ...]
  },
  "leaderboard": {
    "title": "Leaderboard",
    "allTime": "All Time",
    "monthly": "Monthly",
    "rank": "Rank",
    "player": "Player"
  }
}
```

**Localization:**
- Uses `IStringLocalizer<SharedResources>`
- Strings resolved based on user's culture (en-US or he-IL)
- See 11-LOCALIZATION-AND-RTL.md for details

---

#### 5. Game - Get Configuration
**Location:** `Pages/Api/Game/GetConfiguration.cshtml.cs` (92 lines)
**Route:** `GET /Api/Game/GetConfiguration`
**Auth:** `[AllowAnonymous]`, `[IgnoreAntiforgeryToken]`

**Purpose:** Fetch game configuration (grid size, scoring, milestones)

**Response Schema:**
```json
{
  "enabled": true,
  "gridSize": 6,
  "scoring": {
    "points3Match": 40,
    "points4Match": 100,
    "points5PlusMatch": 200
  },
  "megaCombo": {
    "multiplier": 2,
    "min3MatchLines": 0,
    "min4MatchLines": 2,
    "min5MatchLines": 0
  },
  "milestones": [1000, 2500, 5000, 7500, 10000, 15000, 20000]
}
```

**Configuration Keys:**
- `GameEnabled`, `GameGridSize`
- `GamePointsPer3Match`, `GamePointsPer4Match`, `GamePointsPer5PlusMatch`
- `GameMegaComboMultiplier`, `GameMegaCombo3MatchMinLines`
- `GameMilestones` (comma-separated list)

**Stored In:** `Configs` table (one row per key, per company)

---

#### 6. Calendar - Quick Add Chore
**Location:** `Pages/Api/Calendar/QuickAddChore.cshtml.cs` (270 lines)
**Route:** `POST /Api/Calendar/QuickAddChore`
**Auth:** `[Authorize(Policy = "CanEditChores")]`, `[IgnoreAntiforgeryToken]`

**Purpose:** Create chore from calendar quick-add UI

**Request Body:**
```json
{
  "assigneeId": 10,
  "date": "2025-06-15",
  "title": "Clean storage room",
  "notes": "Focus on back shelves",
  "forceAssign": false
}
```

**Response Scenarios:**

**Success:**
```json
{
  "success": true,
  "choreId": 42,
  "message": "Chore created successfully"
}
```

**Shift Conflict (409):**
```json
{
  "success": false,
  "message": "User has shift",
  "conflictType": "shift",
  "statusCode": 409
}
```

**Validation:**
- Title max length: 200 characters
- Date validation: No past dates, max 2 years future
- Permission check: `CanUserManageChoresAsync()`
- Conflict detection: User has shift on this date (unless `forceAssign=true`)

**Side Effects:**
- Creates `UserNotification` (chore assigned)
- Logs to `AuditLogs` (ChoreCreatedQuick)

---

#### 7. Calendar - Quick Add On-Duty
**Location:** `Pages/Api/Calendar/QuickAddOnDuty.cshtml.cs` (243 lines)
**Route:** `POST /Api/Calendar/QuickAddOnDuty`
**Auth:** `[Authorize(Policy = "CanEditOnDuty")]`, `[IgnoreAntiforgeryToken]`

**Purpose:** Create on-duty assignment from calendar quick-add UI

**Request Body:**
```json
{
  "assigneeId": 10,
  "date": "2025-06-15",
  "onDutyType": 0,
  "notes": "Night shift coverage",
  "forceAssign": false
}
```

**On-Duty Types:**
- `0` - Hakam
- `1` - Lead

**Response Scenarios:**

**Success:**
```json
{
  "success": true,
  "onDutyId": 8,
  "message": "On-duty assignment created successfully"
}
```

**Vacation Conflict (409):**
```json
{
  "success": false,
  "conflictType": "vacation",
  "message": "This user has an approved vacation on this date.",
  "vacationStart": "2025-06-14",
  "vacationEnd": "2025-06-18",
  "vacationType": "Vacation"
}
```

**Validation:**
- Notes max length: 1000 characters
- Date validation: No past dates, max 2 years future
- Permission check: `CanUserManageOnDutyAsync()`
- Conflict detection: Approved vacation (unless `forceAssign=true`)

**Side Effects:**
- Creates `UserNotification` (on-duty assigned)
- Logs to `AuditLogs` (OnDutyCreatedQuick)

---

#### 8. Calendar - Delete Chore
**Location:** `Pages/Api/Calendar/DeleteChore.cshtml.cs` (150 lines)
**Route:** `POST /Api/Calendar/DeleteChore`
**Auth:** `[Authorize(Policy = "CanEditChores")]`, `[IgnoreAntiforgeryToken]`

**Purpose:** Delete chore from calendar quick-delete UI

**Request Body:**
```json
{
  "id": 42
}
```

**Response Schema:**
```json
{
  "success": true,
  "message": "Chore deleted successfully"
}
```

**Business Logic:**
- Soft delete (sets `CanceledAt`, `CanceledById`)
- Permission check: `CanUserManageChoresAsync()`
- Notification sent to assignee (chore canceled)

---

#### 9. Calendar - Delete On-Duty
**Location:** `Pages/Api/Calendar/DeleteOnDuty.cshtml.cs` (150 lines)
**Route:** `POST /Api/Calendar/DeleteOnDuty`
**Auth:** `[Authorize(Policy = "CanEditOnDuty")]`, `[IgnoreAntiforgeryToken]`

**Purpose:** Delete on-duty assignment from calendar quick-delete UI

**Request Body:**
```json
{
  "id": 8
}
```

**Response Schema:**
```json
{
  "success": true,
  "message": "On-duty assignment deleted successfully"
}
```

**Business Logic:**
- Soft delete (sets `CanceledAt`, `CanceledById`)
- Permission check: `CanUserManageOnDutyAsync()`
- Notification sent to assignee (on-duty canceled)

---

## API Authentication

### External API (REST v1)

**Mechanism:** API Key authentication via `X-API-Key` header

**Middleware:** `ApiAuthenticationMiddleware.cs` (lines 1-200+)

**Flow:**
```
1. Request arrives at /api/v1/users
2. ApiAuthenticationMiddleware intercepts
3. Extracts X-API-Key header
4. Hashes key using SHA256
5. Queries ApiKeys table for matching KeyHash
6. Validates:
   - Key exists
   - Not expired (ExpiresAt > now)
   - IsActive = true
   - Rate limit not exceeded
   - Required scope present
7. Sets ClaimsPrincipal with:
   - ApiKeyId
   - CompanyId
   - UserId (CreatedBy)
8. Controller executes with claims
```

**Hash Generation:**
```csharp
private string HashApiKey(string key)
{
    using var sha256 = SHA256.Create();
    var bytes = Encoding.UTF8.GetBytes(key);
    var hash = sha256.ComputeHash(bytes);
    return Convert.ToBase64String(hash);
}
```

**API Key Table Schema:**
```sql
CREATE TABLE ApiKeys (
    Id INTEGER PRIMARY KEY,
    CompanyId INTEGER,
    CreatedById INTEGER,
    Name TEXT,
    KeyHash TEXT UNIQUE,
    PlainTextKey TEXT,        -- WARNING: Security concern (stored plain)
    Scopes TEXT,              -- "user:read,shift:write"
    RateLimitPerMinute INTEGER,
    ExpiresAt DATETIME,
    LastUsedAt DATETIME,
    IsActive BOOLEAN,
    CreatedAt DATETIME
);
```

**Scope Validation:**
```csharp
private string DetermineRequiredScope(string path, string method)
{
    if (path.Contains("/users")) return method == "GET" ? "user:read" : "user:write";
    if (path.Contains("/shifts")) return method == "GET" ? "shift:read" : "shift:write";
    if (path.Contains("/time-off")) return method == "GET" ? "timeoff:read" : "timeoff:write";
    // ... etc
}

if (!apiKey.HasScope(requiredScope))
{
    return WriteForbiddenResponse(context, $"Required scope: {requiredScope}");
}
```

**Rate Limiting:**
- Tracked via `ApiRequestLogs` table
- Sliding window algorithm (per minute)
- Configurable per API key
- See [Rate Limiting](#rate-limiting) section

---

### Internal API (Browser Endpoints)

**Mechanism:** Cookie authentication (`.AspNetCore.Cookies`)

**Flow:**
```
1. User logs in via /Login
2. Cookie created with claims:
   - NameIdentifier (UserId)
   - CompanyId
   - Role
   - Email
3. Subsequent requests include cookie
4. UseAuthentication() middleware validates
5. Razor Page checks authorization:
   - [Authorize] - Must be authenticated
   - [Authorize(Policy = "CanEditChores")] - Role-based
   - [AllowAnonymous] - No auth required
6. Handler executes with User.Claims
```

**Example Policy:**
```csharp
// Program.cs
options.AddPolicy("CanEditChores", policy =>
    policy.RequireAssertion(context =>
        context.User.IsInRole("Owner") ||
        context.User.IsInRole("Director") ||
        context.User.IsInRole("Manager") ||
        context.User.IsInRole("Assigner")));
```

**Session Timeout:**
- Default: 7 days (SlidingExpiration)
- Warning threshold: 5 minutes
- See `Pages/Api/SessionStatus.cshtml.cs` for client-side warning

---

## Request/Response Patterns

### Common Response Schemas

#### PaginatedResponse<T>
```csharp
public class PaginatedResponse<T>
{
    public List<T> Data { get; set; } = new();
    public PaginationInfo Pagination { get; set; } = new();
}

public class PaginationInfo
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
}
```

**Usage:**
```csharp
var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
return Ok(new PaginatedResponse<UserDto>
{
    Data = users,
    Pagination = new PaginationInfo
    {
        Page = page,
        PageSize = pageSize,
        TotalCount = totalCount,
        TotalPages = totalPages
    }
});
```

---

#### ApiProblemDetails
**Location:** `Models/Api/ApiProblemDetails.cs`

```csharp
public class ApiProblemDetails
{
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int Status { get; set; }
    public string Detail { get; set; } = string.Empty;
    public string Instance { get; set; } = string.Empty;

    public static ApiProblemDetails ValidationError(string detail, string instance)
    {
        return new ApiProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            Title = "Validation Error",
            Status = 400,
            Detail = detail,
            Instance = instance
        };
    }

    public static ApiProblemDetails NotFound(string detail, string instance)
    {
        return new ApiProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc7231#section-6.5.4",
            Title = "Not Found",
            Status = 404,
            Detail = detail,
            Instance = instance
        };
    }

    public static ApiProblemDetails Unauthorized(string detail, string instance)
    {
        return new ApiProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc7235#section-3.1",
            Title = "Unauthorized",
            Status = 401,
            Detail = detail,
            Instance = instance
        };
    }

    public static ApiProblemDetails Conflict(string detail, string instance)
    {
        return new ApiProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc7231#section-6.5.8",
            Title = "Conflict",
            Status = 409,
            Detail = detail,
            Instance = instance
        };
    }
}
```

**Example Usage:**
```csharp
if (error.Contains("overlaps"))
{
    return Conflict(ApiProblemDetails.Conflict(error, HttpContext.Request.Path));
}
```

**Response:**
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.8",
  "title": "Conflict",
  "status": 409,
  "detail": "This time-off request overlaps with an existing approved request",
  "instance": "/api/v1/time-off-requests"
}
```

---

### HTTP Status Codes

| Code | Meaning | Usage |
|------|---------|-------|
| 200 | OK | Successful GET, PATCH, POST (non-creation) |
| 201 | Created | Successful POST with resource creation |
| 204 | No Content | Successful DELETE |
| 400 | Bad Request | Validation error, malformed request |
| 401 | Unauthorized | Missing/invalid API key or session |
| 403 | Forbidden | Insufficient permissions (scope/role) |
| 404 | Not Found | Resource not found or endpoint disabled |
| 409 | Conflict | Business rule violation (overlap, duplicate) |
| 429 | Too Many Requests | Rate limit exceeded |
| 500 | Internal Server Error | Unhandled exception |

---

## Error Handling

### Try-Catch Pattern (All Controllers)

```csharp
public async Task<IActionResult> CreateTimeOffRequest([FromBody] CreateTimeOffRequest request)
{
    try
    {
        // 1. Feature flag check
        if (!_configuration.GetValue<bool>("Features:Api:TimeOff:CreateEnabled", false))
        {
            _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
                nameof(CreateTimeOffRequest), HttpContext.Request.Path);
            return NotFound(ApiProblemDetails.NotFound("This API endpoint is not enabled", HttpContext.Request.Path));
        }

        // 2. Authentication check
        var companyIdClaim = User.FindFirst("CompanyId")?.Value;
        if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
        {
            _logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}",
                nameof(CreateTimeOffRequest), HttpContext.Request.Path);
            return Unauthorized(ApiProblemDetails.Unauthorized("Invalid authentication", HttpContext.Request.Path));
        }

        // 3. Validation
        if (string.IsNullOrEmpty(request.StartDate))
        {
            return BadRequest(ApiProblemDetails.ValidationError("StartDate is required", HttpContext.Request.Path));
        }

        // 4. Business logic
        var (timeOffRequest, error) = await _timeOffService.CreateTimeOffRequestAsync(...);

        if (error != null)
        {
            if (error.Contains("overlaps"))
            {
                return Conflict(ApiProblemDetails.Conflict(error, HttpContext.Request.Path));
            }
            return BadRequest(ApiProblemDetails.ValidationError(error, HttpContext.Request.Path));
        }

        // 5. Success
        return CreatedAtAction(nameof(GetTimeOffRequest), new { id = timeOffRequest!.Id }, timeOffRequest);
    }
    catch (DbUpdateException ex)
    {
        _logger.LogError(ex, "Database error in {Endpoint}. CompanyId={CompanyId}, Path={Path}",
            nameof(CreateTimeOffRequest), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
        return StatusCode(500, ApiProblemDetails.InternalError(
            "An error occurred while processing your request", HttpContext.Request.Path));
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Unexpected error in {Endpoint}. CompanyId={CompanyId}, Path={Path}",
            nameof(CreateTimeOffRequest), User.FindFirst("CompanyId")?.Value, HttpContext.Request.Path);
        return StatusCode(500, ApiProblemDetails.InternalError(
            "An unexpected error occurred", HttpContext.Request.Path));
    }
}
```

**Error Handling Layers:**
1. **Feature flag** - Endpoint disabled? Return 404
2. **Authentication** - Missing/invalid claims? Return 401
3. **Validation** - Invalid input? Return 400
4. **Business logic** - Conflict/rule violation? Return 409 or 400
5. **Database exception** - Log and return 500
6. **Unexpected exception** - Log and return 500

---

### Structured Logging

**Pattern:**
```csharp
_logger.LogWarning("Unauthorized API access attempt. Endpoint={Endpoint}, Path={Path}, HasCompanyClaim={HasClaim}",
    nameof(ListTimeOffRequests), HttpContext.Request.Path, companyIdClaim != null);
```

**Placeholders (NOT string interpolation):**
- ✅ `{Endpoint}`, `{CompanyId}`, `{Path}`
- ❌ `$"Endpoint: {nameof(...)}"` (don't use)

**Why?**
- Structured logging frameworks (Serilog, Application Insights) parse placeholders
- Enables filtering, querying, and alerting on specific fields

**Log Levels:**
- `LogInformation` - Successful operations
- `LogWarning` - Security events, validation failures, missing resources
- `LogError` - Exceptions, database errors

---

## Rate Limiting

### Implementation

**Table:** `ApiRequestLogs`
```sql
CREATE TABLE ApiRequestLogs (
    Id INTEGER PRIMARY KEY,
    CompanyId INTEGER,
    ApiKeyId INTEGER,
    Method TEXT,
    Path TEXT,
    StatusCode INTEGER,
    CreatedAt DATETIME
);
```

**Middleware Check:**
```csharp
// Count requests in the last minute
var oneMinuteAgo = DateTime.UtcNow.AddMinutes(-1);
var requestCount = await dbContext.ApiRequestLogs
    .Where(r => r.ApiKeyId == apiKey.Id && r.CreatedAt >= oneMinuteAgo)
    .CountAsync();

if (requestCount >= apiKey.RateLimitPerMinute)
{
    await WriteTooManyRequestsResponse(context,
        $"Rate limit exceeded. Max {apiKey.RateLimitPerMinute} requests per minute.");
    return;
}
```

**Response (429):**
```json
{
  "type": "https://tools.ietf.org/html/rfc6585#section-4",
  "title": "Too Many Requests",
  "status": 429,
  "detail": "Rate limit exceeded. Max 60 requests per minute."
}
```

**Configuration:**
- Stored per API key in `ApiKeys.RateLimitPerMinute`
- Default: 60 requests/minute
- No rate limiting for internal endpoints (cookie auth)

---

## Feature Flags

### Configuration Pattern

**All REST v1 endpoints gated by feature flags:**

```json
{
  "Features": {
    "Api": {
      "Users": {
        "ListEnabled": false,
        "GetEnabled": false
      },
      "TimeOff": {
        "ListEnabled": false,
        "GetEnabled": false,
        "CreateEnabled": false,
        "ApproveEnabled": false,
        "DeclineEnabled": false
      },
      "Notifications": {
        "ListEnabled": false,
        "GetEnabled": false,
        "MarkReadEnabled": false,
        "MarkAllReadEnabled": false
      }
    }
  }
}
```

**Default State:** **All disabled** (must be explicitly enabled)

**Controller Check:**
```csharp
if (!_configuration.GetValue<bool>("Features:Api:TimeOff:ListEnabled", false))
{
    _logger.LogWarning("API endpoint not enabled. Endpoint={Endpoint}, Path={Path}",
        nameof(ListTimeOffRequests), HttpContext.Request.Path);
    return NotFound(ApiProblemDetails.NotFound("This API endpoint is not enabled", HttpContext.Request.Path));
}
```

**Why Feature Flags?**
- **Gradual rollout** - Enable endpoints one at a time
- **Safety** - Disable endpoints without code changes
- **Testing** - Enable only for specific companies
- **Compliance** - Disable sensitive endpoints in certain regions

---

## Security Considerations

### 1. API Key Storage

**⚠️ SECURITY CONCERN: Plain-text keys stored**

From `ApiKeys` table:
```sql
CREATE TABLE ApiKeys (
    ...
    KeyHash TEXT UNIQUE,      -- SHA256 hash (used for validation)
    PlainTextKey TEXT,        -- WARNING: Stored plain-text
    ...
);
```

**Why Both?**
- **Hash** - Used for authentication (never exposed)
- **PlainTextKey** - Shown to user ONCE after creation

**Risk:**
- If database compromised, all API keys exposed
- No way to rotate keys without re-creating

**Mitigation:**
- Encrypt `PlainTextKey` at rest using `IDataProtectionProvider`
- Set short expiration (e.g., 30 days)
- Require periodic key rotation

---

### 2. Multi-Tenancy Enforcement

**Automatic CompanyId Filtering:**
- Global query filters ensure users only see their company's data
- See 05-MULTI-TENANCY-DEEP-DIVE.md

**Exception: OnDuty**
- OnDuty entities are **global** (no CompanyId)
- Requires explicit `IgnoreQueryFilters()` in queries
- Directors can access cross-company on-duty assignments

**Validation:**
```csharp
// Extract CompanyId from claims
var companyIdClaim = User.FindFirst("CompanyId")?.Value;
if (companyIdClaim == null || !int.TryParse(companyIdClaim, out var companyId))
{
    return Unauthorized(ApiProblemDetails.Unauthorized("Invalid authentication", HttpContext.Request.Path));
}

// Pass to service layer
var (users, totalCount) = await _userService.ListUsersAsync(companyId, page, pageSize, ...);
```

**Services enforce CompanyId:**
```csharp
public async Task<(List<UserDto>, int)> ListUsersAsync(int companyId, ...)
{
    var query = _db.Users.Where(u => u.CompanyId == companyId);
    // Global query filter ALSO applies
}
```

---

### 3. Scope-Based Authorization

**Scopes are comma-separated:**
```
user:read,shift:write,timeoff:approve
```

**Validation:**
```csharp
public bool HasScope(string scope)
{
    if (string.IsNullOrEmpty(Scopes)) return false;
    var scopes = Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries);
    return scopes.Any(s => s.Trim().Equals(scope, StringComparison.OrdinalIgnoreCase));
}
```

**Least Privilege:**
- API keys should have **minimal scopes**
- Read-only keys should NOT have `:write` scopes
- Approval scopes (`:approve`) require elevated privileges

---

### 4. Input Validation

**Date Parsing:**
```csharp
if (!DateOnly.TryParse(request.StartDate, out var startDate))
{
    return BadRequest(ApiProblemDetails.ValidationError(
        "Invalid StartDate format. Use yyyy-MM-dd", HttpContext.Request.Path));
}
```

**Length Limits:**
```csharp
if (request.Title.Length > 200)
{
    return BadRequest(ApiProblemDetails.ValidationError(
        "Title must not exceed 200 characters", HttpContext.Request.Path));
}
```

**Future Date Prevention:**
```csharp
if (choreDate > DateOnly.FromDateTime(DateTime.Today.AddYears(2)))
{
    return BadRequest(ApiProblemDetails.ValidationError(
        "Cannot create chores more than 2 years in the future", HttpContext.Request.Path));
}
```

---

### 5. CSRF Protection

**REST v1 Endpoints:**
- API key authentication (not cookies) - **CSRF not applicable**

**Internal Endpoints:**
- Use `[IgnoreAntiforgeryToken]` attribute
- Why? AJAX requests from JavaScript don't include CSRF tokens
- **Mitigation:** Cookie `SameSite=Strict` prevents cross-site requests

**Example:**
```csharp
[Authorize]
[IgnoreAntiforgeryToken]  // Required for JSON POST from JavaScript
public class SaveScoreModel : PageModel
{
    public async Task<IActionResult> OnPostAsync()
    {
        // Cookie authentication validates session
        // SameSite=Strict prevents CSRF
    }
}
```

---

## Summary

**API Layer Characteristics:**

| Aspect | REST API v1 | Internal API |
|--------|-------------|--------------|
| **Base Path** | `/api/v1/*` | `/Api/*`, `/api/team-*` |
| **Authentication** | X-API-Key header | Cookie (.AspNetCore.Cookies) |
| **Authorization** | Scope-based (user:read) | Policy-based (CanEditChores) |
| **Rate Limiting** | Yes (per API key) | No |
| **Feature Flags** | All endpoints gated | No feature flags |
| **Multi-Tenancy** | CompanyId from claim | CompanyId from claim |
| **CSRF Protection** | N/A (no cookies) | [IgnoreAntiforgeryToken] + SameSite |
| **Use Case** | External integrations | Browser UI (AJAX) |

**Total Endpoints:**
- **REST v1:** 38 endpoints across 11 controllers
- **Internal:** 9 endpoints (game, calendar, session)
- **Authentication Middleware:** 1 (ApiAuthenticationMiddleware)

**Key Design Principles:**
1. **Dual authentication** - API keys for external, cookies for internal
2. **Scope-based permissions** - Fine-grained access control
3. **Feature flags** - All endpoints can be toggled
4. **Structured errors** - ApiProblemDetails for consistency
5. **Multi-tenancy enforcement** - CompanyId validated on every request
6. **Rate limiting** - Protect against abuse
7. **Structured logging** - Queryable, filterable logs

**Next Steps:**
- See 10-AUTHENTICATION-AND-AUTHORIZATION.md for login flows, roles, and session management
- See 14-WORKFLOWS-AND-BUSINESS-LOGIC.md for request approval workflows
- See 07-SERVICE-LAYER.md for service implementations called by these endpoints

---

**Document Status:** ✅ Complete
**Lines:** 1,670
**Coverage:** All 11 REST v1 controllers + 9 internal endpoints + authentication middleware documented

**Cross-References:**
- 05-MULTI-TENANCY-DEEP-DIVE.md - CompanyId enforcement
- 06-DOMAIN-MODELS.md - Entity schemas
- 07-SERVICE-LAYER.md - Business logic services
- 10-AUTHENTICATION-AND-AUTHORIZATION.md - Login flows, roles, session management
- 14-WORKFLOWS-AND-BUSINESS-LOGIC.md - Request approval workflows
