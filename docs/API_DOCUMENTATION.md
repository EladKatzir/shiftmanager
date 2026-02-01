# ShiftManager API Documentation

This document describes all internal API endpoints used by the ShiftManager application. The application has two categories of APIs:

1. **Internal Browser APIs** - Used by JavaScript in the UI, authenticated via session cookies
2. **External REST APIs** - For third-party integrations, authenticated via API keys

## Table of Contents

- [Internal Browser APIs](#internal-browser-apis)
  - [Session Management](#session-management)
  - [Localization](#localization)
  - [Telemetry](#telemetry)
  - [Scope Switcher](#scope-switcher)
  - [Calendar Operations](#calendar-operations)
  - [Game APIs](#game-apis)
  - [Team Calendars](#team-calendars)
- [External REST APIs (v1)](#external-rest-apis-v1)
  - [Authentication](#authentication)
  - [Rate Limiting](#rate-limiting)
  - [Error Handling](#error-handling)
  - [Shifts API](#shifts-api)
  - [Users API](#users-api)
  - [Time-Off API](#time-off-api)
  - [Notifications API](#notifications-api)
  - [Analytics API](#analytics-api)
  - [Audit Logs API](#audit-logs-api)
  - [Swap Requests API](#swap-requests-api)
  - [Chores API](#chores-api)
  - [On-Duty API](#on-duty-api)
  - [Feedback API](#feedback-api)
- [Feature Flags](#feature-flags)

---

# Internal Browser APIs

These endpoints are used by client-side JavaScript and require cookie-based authentication. They are whitelisted in `ApiAuthenticationMiddleware` and do NOT require API keys.

## Session Management

### Check Session Status
`GET /Api/SessionStatus`

Checks authentication session status and triggers sliding expiration.

**Authentication**: Cookie-based (AllowAnonymous for status check)

**Headers**:
- `X-Requested-With: XMLHttpRequest` (recommended for AJAX detection)

**Response** (200 - Authenticated):
```json
{
  "authenticated": true,
  "state": "ok",
  "secondsRemaining": 86400,
  "minutesRemaining": 1440,
  "userId": 123,
  "username": "user@example.com",
  "issuedAt": 1699884000,
  "expiresAt": 1699970400,
  "slidingExpirationTriggered": true
}
```

**Response** (401 - Not Authenticated):
```json
{
  "authenticated": false,
  "state": "expired",
  "message": "Session expired"
}
```

**State Values**:
- `ok` - Session healthy (>60 min remaining)
- `warning` - Session expiring soon (<=60 min remaining)
- `expired` - Session expired

**UI Usage**: `wwwroot/js/session-check.js` - Polls every 10 min (ok) or 1 min (warning) to manage session warnings.

---

## Localization

### Get Localized Strings
`GET /Api/Localization`

Fetches localized strings for client-side JavaScript, respecting current culture and company overrides.

**Authentication**: Cookie-based (AllowAnonymous)

**Query Parameters**:
- `keys` (string) - Comma-separated list of resource keys

**Example Request**:
```
GET /Api/Localization?keys=Button_Save,Button_Cancel,Error_Required
```

**Response** (200):
```json
{
  "Button_Save": "Save",
  "Button_Cancel": "Cancel",
  "Error_Required": "This field is required"
}
```

**UI Usage**: `wwwroot/js/localization-api.js` - Provides `window.Localization.get()` and `window.Localization.getMany()` methods with caching.

---

## Telemetry

### Log Analytics Event
`POST /Api/Telemetry?handler=Event`

Logs a single analytics event.

**Authentication**: Cookie-based (AllowAnonymous for pre-auth pages)

**Rate Limit**: 30 requests/minute per IP

**Request Body**:
```json
{
  "eventType": "button_click",
  "eventData": "{\"buttonId\": \"submit\"}",
  "sessionId": "abc123",
  "pageUrl": "/Public/Shifts",
  "timestamp": "2025-11-13T10:00:00Z"
}
```

**Response** (200):
```json
{
  "success": true,
  "id": "evt_12345"
}
```

### Log Analytics Event Batch
`POST /Api/Telemetry?handler=EventBatch`

Logs multiple analytics events in a single request.

**Request Body**: Array of event objects (max 50)

**Response** (200):
```json
{
  "success": true,
  "count": 10
}
```

### Log Client Error
`POST /Api/Telemetry?handler=Error`

Logs a client-side JavaScript error.

**Request Body**:
```json
{
  "message": "Uncaught TypeError: Cannot read property 'x' of undefined",
  "stackTrace": "at someFunction (script.js:10:5)",
  "source": "script.js",
  "lineNumber": 10,
  "columnNumber": 5,
  "errorType": "TypeError",
  "pageUrl": "/Public/Shifts",
  "timestamp": "2025-11-13T10:00:00Z"
}
```

### Log Error Batch
`POST /Api/Telemetry?handler=ErrorBatch`

Logs multiple client-side errors (max 20).

### Log Performance Metric
`POST /Api/Telemetry?handler=Performance`

Logs a performance metric (e.g., Web Vitals).

**Request Body**:
```json
{
  "metricName": "LCP",
  "value": 2500.5,
  "rating": "needs-improvement",
  "pageUrl": "/Public/Shifts",
  "connectionType": "4g",
  "effectiveType": "4g",
  "deviceMemory": 8,
  "hardwareConcurrency": 8,
  "timestamp": "2025-11-13T10:00:00Z"
}
```

### Log Performance Batch
`POST /Api/Telemetry?handler=PerformanceBatch`

Logs multiple performance metrics (max 50).

**UI Usage**: `wwwroot/js/telemetry.js` - Batches and sends telemetry data.

---

## Scope Switcher

### Get Available Scopes
`GET /Api/ScopeSwitcher`

Gets organizational scopes available to the current user.

**Authentication**: Cookie-based (Authorize required)

**Query Parameters**:
- `calendarType` (string, optional) - Filter by calendar type (e.g., "shifts", "chores")

**Response** (200):
```json
{
  "scopes": [
    {
      "type": "mine",
      "id": 123,
      "name": "My Shifts",
      "parentId": null,
      "parentType": null,
      "isDefault": false
    },
    {
      "type": "company",
      "id": 1,
      "name": "Acme Corp",
      "parentId": 5,
      "parentType": "molecule",
      "isDefault": true
    }
  ],
  "currentScope": {
    "type": "company",
    "id": 1
  },
  "userContext": {
    "userId": 123,
    "isWorkforce": true,
    "isTech": false,
    "isOwner": false,
    "isDirector": false
  }
}
```

### Get Full Hierarchy
`GET /Api/ScopeSwitcher?handler=Hierarchy`

Gets the full organizational hierarchy tree.

**Response** (200):
```json
{
  "projects": [
    {
      "id": 1,
      "name": "Project Alpha",
      "areas": [
        {
          "id": 1,
          "name": "Area 1",
          "molecules": [
            {
              "id": 1,
              "name": "Molecule 1",
              "type": "Workforce",
              "companies": [
                { "id": 1, "name": "Acme Corp" }
              ],
              "departments": []
            }
          ]
        }
      ]
    }
  ],
  "userContext": {
    "userId": 123,
    "isOwner": false,
    "isDirector": false
  }
}
```

---

## Calendar Operations

### Quick Add Chore
`POST /Api/Calendar/QuickAddChore`

Creates a chore assignment from calendar views.

**Authentication**: Cookie-based, requires `CanEditChores` policy

**Request Body**:
```json
{
  "assigneeId": 123,
  "date": "2025-11-15",
  "title": "Office cleaning",
  "notes": "Focus on break room",
  "forceAssign": false
}
```

**Validation**:
- Title: max 200 characters
- Notes: max 1000 characters
- Date: cannot be in the past, max 2 years in future
- Checks for shift conflicts and vacation conflicts

**Response** (200 - Success):
```json
{
  "success": true,
  "choreId": 456,
  "message": "Chore created successfully"
}
```

**Response** (409 - Conflict):
```json
{
  "success": false,
  "conflictType": "vacation",
  "message": "This user has an approved vacation on this date.",
  "vacationStart": "2025-11-10",
  "vacationEnd": "2025-11-20",
  "vacationType": "Vacation"
}
```

**UI Usage**: `wwwroot/js/calendar-inline-edit.js`

### Delete Chore
`POST /Api/Calendar/DeleteChore`

Deletes a chore from calendar views.

**Authentication**: Cookie-based, requires `CanEditChores` policy

**Request Body**:
```json
{
  "id": 456
}
```

**Response** (200):
```json
{
  "success": true,
  "message": "Chore deleted successfully"
}
```

### Quick Add On-Duty
`POST /Api/Calendar/QuickAddOnDuty`

Creates an on-duty assignment from calendar views.

**Authentication**: Cookie-based, requires `CanEditOnDuty` policy

**Request Body**:
```json
{
  "assigneeId": 123,
  "date": "2025-11-15",
  "onDutyType": 0,
  "notes": "Night shift coverage",
  "forceAssign": false
}
```

**OnDutyType Values**:
- `0` - Hakam
- `1` - Lead

**Response** (200):
```json
{
  "success": true,
  "onDutyId": 789,
  "message": "On-duty assignment created successfully"
}
```

**UI Usage**: `wwwroot/js/calendar-inline-edit.js`

### Delete On-Duty
`POST /Api/Calendar/DeleteOnDuty`

Deletes an on-duty assignment from calendar views.

**Authentication**: Cookie-based, requires `CanEditOnDuty` policy

**Request Body**:
```json
{
  "id": 789
}
```

---

## Game APIs

### Get Game Localization
`GET /Api/Game/GetLocalization`

Gets localized strings for the shift-swap game.

**Authentication**: Cookie-based (AllowAnonymous)

**Response** (200):
```json
{
  "title": "Shift Swap",
  "instructions": "Match 3 or more shifts...",
  "score": "Score",
  "trophy": "Trophy",
  "playAgain": "Play Again",
  "viewLeaderboard": "View Leaderboard",
  "scoreSaved": "Score Saved!",
  "milestoneReached": "Milestone Reached!",
  "roasts": {
    "r1000": ["Message 1", "Message 2", "Message 3"],
    "r2500": ["Message 1", "Message 2", "Message 3"]
  },
  "leaderboard": {
    "title": "Leaderboard",
    "allTime": "All Time",
    "monthly": "Monthly",
    "rank": "Rank",
    "player": "Player",
    "yourBest": "Your Best",
    "noScoresYet": "No scores yet",
    "backToGame": "Back to Game",
    "you": "You"
  }
}
```

**UI Usage**: `wwwroot/js/shift-swap-game.js`

### Get Game Configuration
`GET /Api/Game/GetConfiguration`

Gets game configuration for the current user's company.

**Authentication**: Cookie-based (AllowAnonymous)

**Response** (200):
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

**UI Usage**: `wwwroot/js/shift-swap-game.js`

### Save Game Score
`POST /Api/Game/SaveScore`

Saves a game score to the leaderboard.

**Authentication**: Cookie-based (Authorize required)

**Request Body**:
```json
{
  "score": 5000
}
```

**Response** (200):
```json
{
  "success": true,
  "rank": 5,
  "score": 5000
}
```

**UI Usage**: `wwwroot/js/shift-swap-game.js`

### Get Leaderboard
`GET /Api/Game/GetLeaderboard`

Gets leaderboard data.

**Authentication**: Cookie-based (Authorize required)

**Query Parameters**:
- `type` (string) - "all-time" (default) or "monthly"

**Response** (200):
```json
{
  "success": true,
  "type": "all-time",
  "leaderboard": [
    {
      "rank": 1,
      "userId": 10,
      "displayName": "John Doe",
      "score": 15000,
      "playedAt": "2025-11-13T10:00:00Z",
      "isCurrentUser": false
    }
  ],
  "userBest": {
    "rank": 15,
    "userId": 123,
    "displayName": "You",
    "score": 3000,
    "isCurrentUser": true
  }
}
```

---

## Team Calendars

### List Calendars
`GET /api/team-calendars`

Gets all calendars owned by the current user.

**Authentication**: Cookie-based (Authorize required)

**Response** (200):
```json
[
  {
    "id": 1,
    "name": "My Team",
    "createdAt": "2025-11-01T10:00:00Z",
    "updatedAt": "2025-11-13T10:00:00Z",
    "memberCount": 5
  }
]
```

**UI Usage**: `wwwroot/js/myteam.js`

### Get Calendar
`GET /api/team-calendars/{id}`

Gets a specific calendar with its members.

**Response** (200):
```json
{
  "id": 1,
  "name": "My Team",
  "createdAt": "2025-11-01T10:00:00Z",
  "updatedAt": "2025-11-13T10:00:00Z",
  "members": [
    {
      "memberUserId": 10,
      "displayName": "John Doe",
      "addedAt": "2025-11-01T10:00:00Z"
    }
  ]
}
```

### Create Calendar
`POST /api/team-calendars`

Creates a new calendar.

**Request Body**:
```json
{
  "name": "My Team"
}
```

**Response** (201):
```json
{
  "id": 1,
  "name": "My Team",
  "createdAt": "2025-11-13T10:00:00Z",
  "updatedAt": "2025-11-13T10:00:00Z",
  "memberCount": 0
}
```

### Rename Calendar
`PUT /api/team-calendars/{id}`

Renames a calendar.

**Request Body**:
```json
{
  "name": "New Name"
}
```

**Response** (200):
```json
{
  "message": "Calendar renamed successfully"
}
```

### Delete Calendar
`DELETE /api/team-calendars/{id}`

Soft deletes a calendar.

**Response** (200):
```json
{
  "message": "Calendar deleted successfully"
}
```

### Get Members
`GET /api/team-calendars/{id}/members`

Gets current members and available users for a calendar.

**Response** (200):
```json
{
  "currentMembers": [
    { "id": 10, "displayName": "John Doe", "email": "john@example.com" }
  ],
  "availableUsers": [
    { "id": 20, "displayName": "Jane Smith", "email": "jane@example.com" }
  ]
}
```

### Set Members
`PUT /api/team-calendars/{id}/members`

Replaces all members with a new set.

**Request Body**:
```json
{
  "memberUserIds": [10, 20, 30]
}
```

**Response** (200):
```json
{
  "message": "Members updated successfully"
}
```

### Get Week View
`GET /api/team-calendars/{id}/week`

Gets the week view for a calendar.

**Query Parameters**:
- `date` (string, optional) - Week start date in yyyy-MM-dd format (should be a Sunday)

**Response** (200):
```json
{
  "weekStart": "2025-11-10",
  "members": [
    {
      "userId": 10,
      "displayName": "John Doe",
      "days": [
        {
          "dayOfWeek": "Sunday",
          "type": "Shift",
          "label": "Morning",
          "timeRange": "08:00-16:00",
          "targetUrl": "/Public/Shifts?date=2025-11-10"
        },
        {
          "dayOfWeek": "Monday",
          "type": "Free",
          "label": "Free",
          "timeRange": null,
          "targetUrl": null
        }
      ]
    }
  ]
}
```

**Role-Based Navigation**:
- Owner/Director/Manager: Full navigation access
- Assigner: Can only navigate to chores
- Employee/Trainee: No navigation in My Team view

---

# External REST APIs (v1)

These endpoints are for third-party integrations and require API key authentication.

## Authentication

All external API endpoints require authentication via API key.

### API Key Header
```
X-API-Key: your-api-key-here
```

### Scopes
API keys have specific scopes that determine which endpoints they can access:
- `shift:read` - List and view shifts
- `user:read` - List and view users
- `user:write` - Create and update users
- `timeoff:read` - List and view time-off requests
- `timeoff:write` - Create time-off requests
- `timeoff:approve` - Approve/decline time-off requests
- `notification:read` - List and view notifications
- `notification:write` - Mark notifications as read
- `analytics:read` - View analytics summary
- `audit:read` - List and view audit logs
- `swaps:read` - List and view swap requests
- `swaps:write` - Create and cancel swap requests
- `swaps:approve` - Approve/decline swap requests
- `chores:read` - List and view chores
- `chores:write` - Create, update, and delete chores
- `onduty:read` - List and view on-duty assignments
- `onduty:write` - Create, update, and delete on-duty assignments
- `feedback:read` - List and view feedback
- `feedback:write` - Submit, update status, and delete feedback

---

## Rate Limiting

- **Limit**: 100 requests per minute per API key
- **Algorithm**: Token bucket
- **Headers**: Rate limit info is included in response headers:
  - `X-RateLimit-Limit` - Maximum requests allowed
  - `X-RateLimit-Remaining` - Requests remaining in window
  - `X-RateLimit-Reset` - Unix timestamp when limit resets

When rate limit is exceeded, you'll receive a `429 Too Many Requests` response with `Retry-After` header.

---

## Error Handling

All errors follow RFC 7807 Problem Details format.

### Error Response Format
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "Bad Request",
  "status": 400,
  "detail": "Validation error message here",
  "instance": "/api/v1/resource/123"
}
```

### Common Status Codes
- `200 OK` - Request succeeded
- `201 Created` - Resource created successfully
- `204 No Content` - Delete succeeded
- `400 Bad Request` - Validation error
- `401 Unauthorized` - Invalid or missing API key
- `403 Forbidden` - Insufficient permissions (scope issue)
- `404 Not Found` - Resource not found or endpoint disabled
- `409 Conflict` - Resource conflict (e.g., duplicate)
- `429 Too Many Requests` - Rate limit exceeded
- `500 Internal Server Error` - Server error

---

## Shifts API

### List Shifts
`GET /api/v1/shifts`

**Required Scope**: `shift:read`

**Query Parameters**:
- `page` (int, default: 1) - Page number
- `pageSize` (int, default: 50, max: 100) - Items per page
- `startDate` (string, optional) - Filter by start date (yyyy-MM-dd)
- `endDate` (string, optional) - Filter by end date (yyyy-MM-dd)
- `shiftTypeId` (int, optional) - Filter by shift type ID
- `userId` (int, optional) - Filter by assigned user ID
- `hasOpenSlots` (bool, optional) - Filter shifts with open slots

**Example Request**:
```bash
curl -H "X-API-Key: your-key" \
  "https://api.example.com/api/v1/shifts?startDate=2025-11-01&endDate=2025-11-30"
```

**Response** (200):
```json
{
  "data": [
    {
      "id": 789,
      "shiftTypeId": 1,
      "shiftTypeName": "Morning Shift",
      "workDate": "2025-11-15",
      "startTime": "08:00",
      "endTime": "16:00",
      "staffingRequired": 5,
      "staffingAssigned": 4,
      "hasOpenSlots": true
    }
  ],
  "pagination": {
    "page": 1,
    "pageSize": 50,
    "totalCount": 100,
    "totalPages": 2
  }
}
```

### Get Shift
`GET /api/v1/shifts/{id}`

**Required Scope**: `shift:read`

---

## Users API

### List Users
`GET /api/v1/users`

**Required Scope**: `user:read`

**Query Parameters**:
- `page` (int, default: 1) - Page number
- `pageSize` (int, default: 50, max: 100) - Items per page
- `role` (string, optional) - Filter by role (Owner, Manager, Employee, Director, Trainee)
- `isActive` (bool, optional) - Filter by active status
- `search` (string, optional) - Search by email or display name

**Response** (200):
```json
{
  "data": [
    {
      "id": 10,
      "email": "user@example.com",
      "displayName": "John Doe",
      "role": "Employee",
      "isActive": true,
      "department": "Engineering",
      "jobTitle": "Developer"
    }
  ],
  "pagination": {
    "page": 1,
    "pageSize": 50,
    "totalCount": 25,
    "totalPages": 1
  }
}
```

### Get User
`GET /api/v1/users/{id}`

**Required Scope**: `user:read`

### Create User
`POST /api/v1/users`

**Required Scope**: `user:write`

**Request Body**:
```json
{
  "email": "newuser@example.com",
  "displayName": "Jane Smith",
  "role": "Employee",
  "password": "SecurePassword123!",
  "department": "Engineering",
  "jobTitle": "Developer"
}
```

**Response** (201): Created user object

### Update User
`PATCH /api/v1/users/{id}`

**Required Scope**: `user:write`

**Request Body** (all fields optional):
```json
{
  "displayName": "Updated Name",
  "role": "Manager",
  "isActive": true,
  "department": "Sales",
  "jobTitle": "Team Lead",
  "phone": "+1234567890"
}
```

---

## Time-Off API

### List Time-Off Requests
`GET /api/v1/time-off-requests`

**Required Scope**: `timeoff:read`

**Query Parameters**:
- `page` (int, default: 1)
- `pageSize` (int, default: 50, max: 100)
- `userId` (int, optional)
- `status` (string, optional) - Pending, Approved, Declined
- `startDate` (string, optional) - yyyy-MM-dd
- `endDate` (string, optional) - yyyy-MM-dd

### Get Time-Off Request
`GET /api/v1/time-off-requests/{id}`

**Required Scope**: `timeoff:read`

### Create Time-Off Request
`POST /api/v1/time-off-requests`

**Required Scope**: `timeoff:write`

**Request Body**:
```json
{
  "userId": 10,
  "startDate": "2025-12-20",
  "endDate": "2025-12-31",
  "reason": "Holiday vacation"
}
```

### Approve Time-Off Request
`POST /api/v1/time-off-requests/{id}/approve`

**Required Scope**: `timeoff:approve`

### Decline Time-Off Request
`POST /api/v1/time-off-requests/{id}/decline`

**Required Scope**: `timeoff:approve`

---

## Notifications API

### List Notifications
`GET /api/v1/notifications`

**Required Scope**: `notification:read`

**Query Parameters**:
- `page` (int, default: 1)
- `pageSize` (int, default: 50, max: 100)
- `userId` (int, optional)
- `isRead` (bool, optional)
- `type` (string, optional)

### Get Notification
`GET /api/v1/notifications/{id}`

**Required Scope**: `notification:read`

### Mark as Read
`POST /api/v1/notifications/{id}/mark-read`

**Required Scope**: `notification:write`

**Request Body** (optional):
```json
{
  "userId": 10
}
```

### Mark All as Read
`POST /api/v1/notifications/mark-all-read`

**Required Scope**: `notification:write`

**Request Body**:
```json
{
  "userId": 10
}
```

**Response** (200):
```json
{
  "markedCount": 15
}
```

---

## Analytics API

### Get Summary
`GET /api/v1/analytics/summary`

**Required Scope**: `analytics:read`

**Query Parameters**:
- `startDate` (string, optional) - yyyy-MM-dd (default: 30 days ago)
- `endDate` (string, optional) - yyyy-MM-dd (default: today)

**Response** (200):
```json
{
  "companyId": 1,
  "periodStart": "2025-10-14",
  "periodEnd": "2025-11-13",
  "totalUsers": 50,
  "activeUsers": 45,
  "shiftsScheduled": 500,
  "pendingTimeOffRequests": 5,
  "unreadNotifications": 25,
  "generatedAt": "2025-11-13T10:00:00.000Z"
}
```

---

## Audit Logs API

### List Audit Logs
`GET /api/v1/audit-logs`

**Required Scope**: `audit:read`

**Query Parameters**:
- `page` (int, default: 1)
- `pageSize` (int, default: 50, max: 100)
- `userId` (int, optional)
- `action` (string, optional) - Filter by action type
- `startDate` (string, optional) - ISO 8601 datetime
- `endDate` (string, optional) - ISO 8601 datetime

**Response** (200):
```json
{
  "data": [
    {
      "id": 1,
      "userId": 10,
      "userEmail": "user@example.com",
      "userDisplayName": "John Doe",
      "action": "ShiftAssigned",
      "entityType": "ShiftInstance",
      "entityId": 789,
      "description": "Assigned shift to user",
      "details": "{\"shiftDate\": \"2025-11-15\"}",
      "ipAddress": "192.168.1.1",
      "userAgent": "Mozilla/5.0...",
      "timestamp": "2025-11-13T10:00:00.000Z"
    }
  ],
  "pagination": {
    "page": 1,
    "pageSize": 50,
    "totalCount": 1000,
    "totalPages": 20
  }
}
```

---

## Swap Requests API

### List Swap Requests
`GET /api/v1/swap-requests`

**Required Scope**: `swaps:read`

**Query Parameters**:
- `page` (int, default: 1) - Page number
- `pageSize` (int, default: 50, max: 100) - Items per page
- `userId` (int, optional) - Filter by user (fromUser or toUser)
- `status` (string, optional) - Filter by status: `Pending`, `Approved`, `Declined`
- `startDate` (string, optional) - Filter by shift date >= (yyyy-MM-dd)
- `endDate` (string, optional) - Filter by shift date <= (yyyy-MM-dd)
- `includeRelated` (bool, default: false) - Include related entities (users, assignments, shifts)

### Get Swap Request
`GET /api/v1/swap-requests/{id}`

**Required Scope**: `swaps:read`

### Create Swap Request
`POST /api/v1/swap-requests`

**Required Scope**: `swaps:write`

**Request Body**:
```json
{
  "fromAssignmentId": 123,
  "toAssignmentId": 456,
  "toUserId": 20,
  "reason": "Need to attend family event"
}
```

### Approve Swap Request
`POST /api/v1/swap-requests/{id}/approve`

**Required Scope**: `swaps:approve`

### Decline Swap Request
`POST /api/v1/swap-requests/{id}/decline`

**Required Scope**: `swaps:approve`

**Request Body**:
```json
{
  "declineReason": "Scheduling conflict"
}
```

### Delete Swap Request
`DELETE /api/v1/swap-requests/{id}`

**Required Scope**: `swaps:write`

**Note**: Only the requester can cancel their own pending request.

---

## Chores API

### List Chores
`GET /api/v1/chores`

**Required Scope**: `chores:read`

**Query Parameters**:
- `page` (int, default: 1)
- `pageSize` (int, default: 50, max: 100)
- `userId` (int, optional) - Filter by assigned user
- `startDate` (string, optional) - yyyy-MM-dd
- `endDate` (string, optional) - yyyy-MM-dd
- `includeRelated` (bool, default: false)
- `includeCanceled` (bool, default: false)

### Get Chore
`GET /api/v1/chores/{id}`

**Required Scope**: `chores:read`

### Create Chore
`POST /api/v1/chores`

**Required Scope**: `chores:write`

**Request Body**:
```json
{
  "userId": 10,
  "date": "2025-11-15",
  "title": "Office cleaning",
  "notes": "Focus on break room"
}
```

### Update Chore
`PATCH /api/v1/chores/{id}`

**Required Scope**: `chores:write`

**Request Body**:
```json
{
  "title": "Updated title",
  "notes": "Updated notes"
}
```

### Delete Chore
`DELETE /api/v1/chores/{id}`

**Required Scope**: `chores:write`

**Note**: Performs a soft delete.

---

## On-Duty API

### List On-Duty Assignments
`GET /api/v1/on-duty`

**Required Scope**: `onduty:read`

**Query Parameters**:
- `page` (int, default: 1)
- `pageSize` (int, default: 50, max: 100)
- `userId` (int, optional)
- `startDate` (string, optional) - yyyy-MM-dd
- `endDate` (string, optional) - yyyy-MM-dd
- `type` (string, optional) - `Hakam`, `Lead`
- `includeRelated` (bool, default: false)
- `includeCanceled` (bool, default: false)

### Get On-Duty Assignment
`GET /api/v1/on-duty/{id}`

**Required Scope**: `onduty:read`

### Create On-Duty Assignment
`POST /api/v1/on-duty`

**Required Scope**: `onduty:write`

**Request Body**:
```json
{
  "userId": 10,
  "date": "2025-11-15",
  "type": "Hakam",
  "notes": "Night shift coverage"
}
```

### Update On-Duty Assignment
`PATCH /api/v1/on-duty/{id}`

**Required Scope**: `onduty:write`

### Delete On-Duty Assignment
`DELETE /api/v1/on-duty/{id}`

**Required Scope**: `onduty:write`

---

## Feedback API

### List Feedback
`GET /api/v1/feedback`

**Required Scope**: `feedback:read`

**Query Parameters**:
- `page` (int, default: 1)
- `pageSize` (int, default: 50, max: 100)
- `submittedBy` (int, optional)
- `type` (string, optional) - `Error`, `Suggestion`
- `status` (string, optional) - `New`, `ToWorkOn`
- `startDate` (DateTime, optional)
- `endDate` (DateTime, optional)
- `includeRelated` (bool, default: false)

### Get Feedback
`GET /api/v1/feedback/{id}`

**Required Scope**: `feedback:read`

### Create Feedback
`POST /api/v1/feedback`

**Required Scope**: `feedback:write`

**Request Body**:
```json
{
  "type": "Error",
  "content": "The shift swap button is not working",
  "imageFileName": "abc123.png"
}
```

### Update Feedback Status
`PATCH /api/v1/feedback/{id}/status`

**Required Scope**: `feedback:write`

**Request Body**:
```json
{
  "status": "ToWorkOn"
}
```

### Delete Feedback
`DELETE /api/v1/feedback/{id}`

**Required Scope**: `feedback:write`

---

# Feature Flags

All API endpoints can be individually enabled/disabled via configuration in `appsettings.json`:

```json
{
  "Features": {
    "Api": {
      "Shifts": {
        "ListEnabled": true,
        "GetEnabled": true
      },
      "Users": {
        "ListEnabled": true,
        "GetEnabled": true,
        "CreateEnabled": true,
        "UpdateEnabled": true
      },
      "TimeOff": {
        "ListEnabled": true,
        "GetEnabled": true,
        "CreateEnabled": true,
        "ApproveEnabled": true,
        "DeclineEnabled": true
      },
      "Notifications": {
        "ListEnabled": true,
        "GetEnabled": true,
        "MarkReadEnabled": true,
        "MarkAllReadEnabled": true
      },
      "Analytics": {
        "SummaryEnabled": true
      },
      "AuditLogs": {
        "ListEnabled": true
      },
      "SwapRequests": {
        "ListEnabled": true,
        "GetEnabled": true,
        "CreateEnabled": true,
        "ApproveEnabled": true,
        "DeclineEnabled": true,
        "DeleteEnabled": true
      },
      "Chores": {
        "ListEnabled": true,
        "GetEnabled": true,
        "CreateEnabled": true,
        "UpdateEnabled": true,
        "DeleteEnabled": true
      },
      "OnDuty": {
        "ListEnabled": true,
        "GetEnabled": true,
        "CreateEnabled": true,
        "UpdateEnabled": true,
        "DeleteEnabled": true
      },
      "Feedback": {
        "ListEnabled": true,
        "GetEnabled": true,
        "CreateEnabled": true,
        "UpdateStatusEnabled": true,
        "DeleteEnabled": true
      }
    }
  }
}
```

When a feature flag is disabled, the endpoint returns `404 Not Found`.
