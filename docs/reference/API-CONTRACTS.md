# ShiftManager API Contracts

## Overview

This document describes the API contracts for ShiftManager, a Military Shift Scheduling System. The API follows RESTful conventions and uses JSON for request and response bodies.

**Documentation Task:** B-026 - API Contract Documentation

## Base URL

All API endpoints are relative to the application root:
- Development: `http://localhost:5000` or `https://localhost:5001`
- Production: Configured per deployment

## Interactive API Documentation

When running in development mode, Swagger UI is available at:
- `/swagger` - Interactive API documentation and testing

## Authentication

### Session Cookie Authentication
Most web-based endpoints use session cookie authentication (`shiftmgr.auth` cookie) set after successful login via `/Auth/Login`.

### API Key Authentication
REST API endpoints under `/api/v1/` accept API key authentication:

```
X-API-Key: your-api-key-here
```

API keys are managed through the Admin interface and have associated scopes that control access to specific endpoints.

### Required Scopes

Each API endpoint requires specific scopes. Common scopes include:
- `shift:read` - Read shift data
- `shift:write` - Create/update shifts
- `user:read` - Read user data
- `user:write` - Create/update users
- `timeoff:read` - Read time-off requests
- `timeoff:write` - Create time-off requests
- `timeoff:approve` - Approve/decline time-off requests
- `notification:read` - Read notifications
- `notification:write` - Mark notifications as read
- `swaps:read` - Read swap requests
- `swaps:write` - Create swap requests
- `swaps:approve` - Approve/decline swap requests
- `chores:read` - Read chores
- `chores:write` - Create/update chores
- `onduty:read` - Read on-duty assignments
- `onduty:write` - Create/update on-duty assignments
- `feedback:read` - Read feedback
- `feedback:write` - Create feedback
- `analytics:read` - Read analytics data

## Common Query Parameters

| Parameter | Type | Description | Example |
|-----------|------|-------------|---------|
| `page` | integer | Page number for pagination (default: 1) | `?page=2` |
| `pageSize` | integer | Items per page (default: 50, max: 100) | `?pageSize=25` |
| `startDate` | string | Filter by start date (ISO 8601: yyyy-MM-dd) | `?startDate=2026-01-01` |
| `endDate` | string | Filter by end date (ISO 8601: yyyy-MM-dd) | `?endDate=2026-01-31` |
| `includeRelated` | boolean | Include related entities | `?includeRelated=true` |

## Error Response Formats

ShiftManager uses two error response formats depending on the endpoint type.

### RFC-7807 Problem Details (Controllers)

Used by `/api/v1/*` controller endpoints:

```json
{
  "type": "about:blank",
  "title": "Validation Error",
  "status": 400,
  "detail": "Invalid startDate format. Use yyyy-MM-dd",
  "instance": "/api/v1/shifts"
}
```

### Standardized API Error Response (Middleware)

Used by the global exception handler:

```json
{
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "One or more validation errors occurred",
    "details": {
      "email": ["Email is required"],
      "displayName": ["DisplayName is required"]
    }
  }
}
```

### Error Codes

| Code | HTTP Status | Description |
|------|-------------|-------------|
| `BAD_REQUEST` | 400 | Invalid request parameters |
| `VALIDATION_ERROR` | 400 | Field validation failed |
| `UNAUTHORIZED` | 401 | Authentication required or invalid |
| `FORBIDDEN` | 403 | Insufficient permissions |
| `GRANT_REQUIRED` | 403 | Specific grant/permission needed |
| `*_NOT_FOUND` | 404 | Resource not found (e.g., `SHIFT_NOT_FOUND`, `USER_NOT_FOUND`) |
| `RESOURCE_NOT_FOUND` | 404 | Generic resource not found |
| `CONFLICT` | 409 | Resource conflict (e.g., duplicate email) |
| `SCHEDULE_CONFLICT` | 409 | Scheduling conflict detected |
| `REQUEST_CANCELLED` | 499 | Client cancelled the request |
| `RATE_LIMIT_EXCEEDED` | 429 | Too many requests |
| `SERVER_ERROR` | 500 | Internal server error |
| `SERVICE_UNAVAILABLE` | 503 | Service temporarily unavailable |
| `TIMEOUT` | 504 | Operation timed out |

## Pagination Response Format

All list endpoints return paginated responses:

```json
{
  "data": [...],
  "pagination": {
    "page": 1,
    "pageSize": 50,
    "totalCount": 142,
    "totalPages": 3
  }
}
```

---

## API Endpoints

### Shifts API

Base path: `/api/v1/shifts`

#### GET /api/v1/shifts

Lists shift instances with pagination and filtering.

**Required scope:** `shift:read`

**Query Parameters:**
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `page` | integer | No | Page number (default: 1) |
| `pageSize` | integer | No | Page size (default: 50, max: 100) |
| `startDate` | string | No | Filter by start date (yyyy-MM-dd) |
| `endDate` | string | No | Filter by end date (yyyy-MM-dd) |
| `shiftTypeId` | integer | No | Filter by shift type ID |
| `userId` | integer | No | Filter by assigned user ID |
| `hasOpenSlots` | boolean | No | Filter shifts with open slots |

**Response:** `200 OK`
```json
{
  "data": [
    {
      "id": 1,
      "workDate": "2026-01-31",
      "shiftTypeId": 1,
      "shiftTypeName": "MORNING",
      "assignedUsers": [
        {
          "id": 5,
          "displayName": "John Doe"
        }
      ]
    }
  ],
  "pagination": {
    "page": 1,
    "pageSize": 50,
    "totalCount": 42,
    "totalPages": 1
  }
}
```

#### GET /api/v1/shifts/{id}

Gets a single shift instance by ID with full details.

**Required scope:** `shift:read`

**Response:** `200 OK`
```json
{
  "id": 1,
  "workDate": "2026-01-31",
  "shiftTypeId": 1,
  "shiftTypeName": "MORNING",
  "startTime": "08:00",
  "endTime": "16:00",
  "assignedUsers": [...]
}
```

---

### Users API

Base path: `/api/v1/users`

#### GET /api/v1/users

Lists users with pagination and filtering.

**Required scope:** `user:read`

**Query Parameters:**
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `page` | integer | No | Page number (default: 1) |
| `pageSize` | integer | No | Page size (default: 50, max: 100) |
| `role` | string | No | Filter by role (Owner, Manager, Employee, Director, Trainee) |
| `isActive` | boolean | No | Filter by active status |
| `search` | string | No | Search by email or display name |

**Response:** `200 OK`
```json
{
  "data": [
    {
      "id": 1,
      "email": "user@example.com",
      "displayName": "John Doe",
      "role": "Employee",
      "isActive": true,
      "department": "Operations",
      "jobTitle": "Shift Worker"
    }
  ],
  "pagination": {...}
}
```

#### GET /api/v1/users/{id}

Gets a single user by ID.

**Required scope:** `user:read`

#### POST /api/v1/users

Creates a new user.

**Required scope:** `user:write`

**Request Body:**
```json
{
  "email": "newuser@example.com",
  "displayName": "Jane Smith",
  "role": "Employee",
  "password": "optional-initial-password",
  "department": "Operations",
  "jobTitle": "Shift Worker"
}
```

**Response:** `201 Created`

#### PATCH /api/v1/users/{id}

Updates an existing user (partial update).

**Required scope:** `user:write`

**Request Body:**
```json
{
  "displayName": "Updated Name",
  "role": "Manager",
  "isActive": true,
  "department": "Management",
  "jobTitle": "Team Lead",
  "phone": "+1-555-0123"
}
```

---

### Time-Off Requests API

Base path: `/api/v1/time-off-requests`

#### GET /api/v1/time-off-requests

Lists time-off requests with pagination and filtering.

**Required scope:** `timeoff:read`

**Query Parameters:**
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `page` | integer | No | Page number |
| `pageSize` | integer | No | Page size |
| `userId` | integer | No | Filter by user ID |
| `status` | string | No | Filter by status (Pending, Approved, Declined) |
| `startDate` | string | No | Filter by start date |
| `endDate` | string | No | Filter by end date |

#### GET /api/v1/time-off-requests/{id}

Gets a single time-off request by ID.

**Required scope:** `timeoff:read`

#### POST /api/v1/time-off-requests

Creates a new time-off request.

**Required scope:** `timeoff:write`

**Request Body:**
```json
{
  "userId": 5,
  "startDate": "2026-02-01",
  "endDate": "2026-02-05",
  "reason": "Family vacation"
}
```

#### POST /api/v1/time-off-requests/{id}/approve

Approves a time-off request.

**Required scope:** `timeoff:approve`

#### POST /api/v1/time-off-requests/{id}/decline

Declines a time-off request.

**Required scope:** `timeoff:approve`

---

### Notifications API

Base path: `/api/v1/notifications`

#### GET /api/v1/notifications

Lists notifications with pagination and filtering.

**Required scope:** `notification:read`

**Query Parameters:**
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `userId` | integer | No | Filter by user ID |
| `isRead` | boolean | No | Filter by read status |
| `type` | string | No | Filter by notification type |

#### GET /api/v1/notifications/{id}

Gets a single notification by ID.

**Required scope:** `notification:read`

#### POST /api/v1/notifications/{id}/mark-read

Marks a notification as read.

**Required scope:** `notification:write`

**Request Body (optional):**
```json
{
  "userId": 5
}
```

#### POST /api/v1/notifications/mark-all-read

Marks all notifications for a user as read.

**Required scope:** `notification:write`

**Request Body:**
```json
{
  "userId": 5
}
```

**Response:**
```json
{
  "markedCount": 15
}
```

---

### Swap Requests API

Base path: `/api/v1/swap-requests`

#### GET /api/v1/swap-requests

Lists swap requests with pagination and filtering.

**Required scope:** `swaps:read`

**Query Parameters:**
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `userId` | integer | No | Filter by user ID |
| `status` | string | No | Filter by status |
| `startDate` | string | No | Filter by start date |
| `endDate` | string | No | Filter by end date |
| `includeRelated` | boolean | No | Include related entities |

#### GET /api/v1/swap-requests/{id}

Gets a single swap request by ID.

**Required scope:** `swaps:read`

#### POST /api/v1/swap-requests

Creates a new swap request.

**Required scope:** `swaps:write`

**Request Body:**
```json
{
  "fromAssignmentId": 123
}
```

#### POST /api/v1/swap-requests/{id}/approve

Approves a swap request (admin operation).

**Required scope:** `swaps:approve`

#### POST /api/v1/swap-requests/{id}/decline

Declines a swap request (admin operation).

**Required scope:** `swaps:approve`

**Request Body:**
```json
{
  "declineReason": "Insufficient coverage on that day"
}
```

#### DELETE /api/v1/swap-requests/{id}

Deletes (cancels) a swap request. Only the requester can cancel their own pending request.

**Required scope:** `swaps:write`

**Response:** `204 No Content`

---

### Chores API

Base path: `/api/v1/chores`

#### GET /api/v1/chores

Lists chores with pagination and filtering.

**Required scope:** `chores:read`

**Query Parameters:**
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `userId` | integer | No | Filter by user ID |
| `startDate` | string | No | Filter by start date |
| `endDate` | string | No | Filter by end date |
| `includeRelated` | boolean | No | Include related entities |
| `includeCanceled` | boolean | No | Include canceled chores |

#### GET /api/v1/chores/{id}

Gets a single chore by ID.

**Required scope:** `chores:read`

#### POST /api/v1/chores

Creates a new chore.

**Required scope:** `chores:write`

**Request Body:**
```json
{
  "userId": 5,
  "date": "2026-01-31",
  "title": "Clean break room"
}
```

#### PATCH /api/v1/chores/{id}

Updates an existing chore.

**Required scope:** `chores:write`

#### DELETE /api/v1/chores/{id}

Deletes (cancels) a chore.

**Required scope:** `chores:write`

**Response:** `204 No Content`

---

### On-Duty API

Base path: `/api/v1/on-duty`

**Note:** On-duty assignments are global (not company-scoped).

#### GET /api/v1/on-duty

Lists on-duty assignments with pagination and filtering.

**Required scope:** `onduty:read`

**Query Parameters:**
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `userId` | integer | No | Filter by user ID |
| `startDate` | string | No | Filter by start date |
| `endDate` | string | No | Filter by end date |
| `type` | string | No | Filter by duty type |
| `includeRelated` | boolean | No | Include related entities |
| `includeCanceled` | boolean | No | Include canceled assignments |

#### GET /api/v1/on-duty/{id}

Gets a single on-duty assignment by ID.

**Required scope:** `onduty:read`

#### POST /api/v1/on-duty

Creates a new on-duty assignment.

**Required scope:** `onduty:write`

**Request Body:**
```json
{
  "userId": 5,
  "date": "2026-01-31",
  "type": "PRIMARY"
}
```

#### PATCH /api/v1/on-duty/{id}

Updates an existing on-duty assignment.

**Required scope:** `onduty:write`

#### DELETE /api/v1/on-duty/{id}

Deletes (cancels) an on-duty assignment.

**Required scope:** `onduty:write`

**Response:** `204 No Content`

---

### Feedback API

Base path: `/api/v1/feedback`

#### GET /api/v1/feedback

Lists feedback with pagination and filtering.

**Required scope:** `feedback:read`

**Query Parameters:**
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `submittedBy` | integer | No | Filter by submitter user ID |
| `type` | string | No | Filter by feedback type |
| `status` | string | No | Filter by status |
| `startDate` | datetime | No | Filter by submission date start |
| `endDate` | datetime | No | Filter by submission date end |
| `includeRelated` | boolean | No | Include related entities |

#### GET /api/v1/feedback/{id}

Gets a single feedback by ID.

**Required scope:** `feedback:read`

#### POST /api/v1/feedback

Creates new feedback.

**Required scope:** `feedback:write`

**Request Body:**
```json
{
  "type": "BUG",
  "content": "The calendar doesn't load on mobile devices"
}
```

#### PATCH /api/v1/feedback/{id}/status

Updates feedback status.

**Required scope:** `feedback:write`

**Request Body:**
```json
{
  "status": "RESOLVED"
}
```

#### DELETE /api/v1/feedback/{id}

Deletes feedback.

**Required scope:** `feedback:write`

**Response:** `204 No Content`

---

### Analytics API

Base path: `/api/v1/analytics`

#### GET /api/v1/analytics/summary

Gets basic analytics and metrics summary.

**Required scope:** `analytics:read`

**Query Parameters:**
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `startDate` | string | No | Period start date (default: 30 days ago) |
| `endDate` | string | No | Period end date (default: today) |

**Response:** `200 OK`
```json
{
  "companyId": 1,
  "periodStart": "2026-01-01",
  "periodEnd": "2026-01-31",
  "totalUsers": 50,
  "activeUsers": 45,
  "shiftsScheduled": 320,
  "pendingTimeOffRequests": 5,
  "unreadNotifications": 12,
  "generatedAt": "2026-01-31T12:00:00.000Z"
}
```

---

### Telemetry API (Razor Pages)

Base path: `/Api/Telemetry`

These endpoints use Razor Pages handler pattern and are used for client-side observability.

#### POST /Api/Telemetry?handler=Event

Logs a single analytics event.

**Authentication:** Optional (supports anonymous for login page)

**Request Body:**
```json
{
  "eventType": "calendar_view_changed",
  "eventData": "{\"from\": \"week\", \"to\": \"month\"}",
  "sessionId": "abc123",
  "pageUrl": "/Calendar/Month",
  "timestamp": "2026-01-31T12:00:00Z"
}
```

**Response:**
```json
{
  "success": true,
  "id": 123
}
```

#### POST /Api/Telemetry?handler=EventBatch

Logs a batch of analytics events (max 50).

#### POST /Api/Telemetry?handler=Error

Logs a single client-side error.

**Request Body:**
```json
{
  "message": "Cannot read property 'x' of undefined",
  "stackTrace": "at handleClick (main.js:42)",
  "source": "main.js",
  "lineNumber": 42,
  "columnNumber": 15,
  "errorType": "TypeError",
  "pageUrl": "/Calendar/Month",
  "timestamp": "2026-01-31T12:00:00Z"
}
```

#### POST /Api/Telemetry?handler=ErrorBatch

Logs a batch of client-side errors (max 20).

#### POST /Api/Telemetry?handler=Performance

Logs a single performance metric.

**Request Body:**
```json
{
  "metricName": "LCP",
  "value": 2500,
  "rating": "needs-improvement",
  "pageUrl": "/Calendar/Month",
  "connectionType": "wifi",
  "effectiveType": "4g",
  "deviceMemory": 8,
  "hardwareConcurrency": 4,
  "timestamp": "2026-01-31T12:00:00Z"
}
```

#### POST /Api/Telemetry?handler=PerformanceBatch

Logs a batch of performance metrics (max 50).

---

### Scope Switcher API (Razor Pages)

Base path: `/Api/ScopeSwitcher`

These endpoints provide organizational scope information for the Scope Switcher UI component. They use session cookie authentication and return scopes based on user grants.

#### GET /Api/ScopeSwitcher

Gets available organizational scopes for the current user based on their grants.

**Authentication:** Session Cookie (required)

**Query Parameters:**
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `calendarType` | string | No | Calendar type to check grants for (e.g., "shifts", "chores"). Affects which view grants are checked. |

**Response:** `200 OK`
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
      "name": "Alpha Company",
      "parentId": 1,
      "parentType": "molecule",
      "isDefault": true
    },
    {
      "type": "molecule",
      "id": 1,
      "name": "Operations Molecule",
      "parentId": 1,
      "parentType": "area",
      "isDefault": false
    },
    {
      "type": "area",
      "id": 1,
      "name": "Central Area",
      "parentId": 1,
      "parentType": "project",
      "isDefault": false
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

**Scope Types:**
| Type | Description | Visibility |
|------|-------------|------------|
| `mine` | User's own shifts only | Always visible |
| `company` | User's company | Visible if user belongs to a company |
| `department` | User's department | Visible if user belongs to a department (tech users) |
| `molecule` | Molecule/team grouping | Requires `View{CalendarType}Molecule` grant or Director/Owner role |
| `area` | Area grouping | Requires `View{CalendarType}Area` grant or Owner role |

**Error Response:** `401 Unauthorized`
```json
{
  "error": "Invalid user session"
}
```

#### GET /Api/ScopeSwitcher?handler=Hierarchy

Gets the full organizational hierarchy tree with user access information.

**Authentication:** Session Cookie (required)

**Response:** `200 OK`
```json
{
  "projects": [
    {
      "id": 1,
      "name": "Main Project",
      "areas": [
        {
          "id": 1,
          "name": "Central Area",
          "molecules": [
            {
              "id": 1,
              "name": "Operations",
              "type": "Workforce",
              "companies": [
                {
                  "id": 1,
                  "name": "Alpha Company"
                }
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
    "isWorkforce": true,
    "isTech": false,
    "isOwner": false,
    "isDirector": false
  }
}
```

**Notes:**
- Owners and Directors see the full organizational hierarchy
- Regular users see only their branch of the hierarchy
- Used for advanced scope selection UI that needs to show the full tree structure

**JavaScript Usage Example:**
```javascript
// Fetch available scopes
fetch('/Api/ScopeSwitcher?calendarType=shifts', {
    credentials: 'same-origin'
})
.then(response => response.json())
.then(data => {
    console.log('Available scopes:', data.scopes);
    console.log('Current scope:', data.currentScope);
});

// Fetch full hierarchy (for advanced UI)
fetch('/Api/ScopeSwitcher?handler=Hierarchy', {
    credentials: 'same-origin'
})
.then(response => response.json())
.then(data => {
    console.log('Organization hierarchy:', data.projects);
});
```

---

## Rate Limiting

API endpoints are subject to rate limiting:
- Default: Configurable per API key
- Telemetry endpoints: 30 requests per minute per IP

When rate limited, the API returns:
- **HTTP Status:** `429 Too Many Requests`
- **Response:**
```json
{
  "error": {
    "code": "RATE_LIMIT_EXCEEDED",
    "message": "Rate limit exceeded. Retry after 60 seconds",
    "details": {
      "retryAfterSeconds": 60
    }
  }
}
```

---

## Feature Flags

API endpoints can be enabled/disabled via feature flags in `appsettings.json`:

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
      }
    }
  }
}
```

When an endpoint is disabled, it returns `404 Not Found` with message "This API endpoint is not enabled".

---

## Changelog

### v1.2.0 (2026-03-23)
- Added Admin Grants Controller
  - `GET /api/v1/admin/grants` - List grant assignments (requires admin scope)
  - `POST /api/v1/admin/grants` - Create/modify grant assignments (requires admin scope)
- **Note:** The internal API whitelist in `ApiAuthenticationMiddleware.cs` has grown significantly beyond Telemetry and ScopeSwitcher. Current whitelisted internal endpoints include: team-calendars, SessionStatus, Calendar, Game, GriffinCallback, Localization, ScopeSwitcher, SelectMolecule, Signup, Telemetry, ScheduleExport, Hierarchy, OnDuty, Friends, TechShift, admin/verify-grants. See `ApiAuthenticationMiddleware.IsInternalWebUiEndpoint()` for the authoritative list.

### v1.1.0 (2026-01-31)
- Added Scope Switcher API (A-003-EXT)
  - `GET /Api/ScopeSwitcher` - Fetch user's available scopes
  - `GET /Api/ScopeSwitcher?handler=Hierarchy` - Fetch full org hierarchy
- Added Telemetry API to middleware whitelist

### v1.0.0 (2026-01-31)
- Initial API documentation (B-026)
- Documented all v1 API endpoints
- Added Swagger/OpenAPI support for development
