# ShiftManager API Documentation

## Table of Contents
- [Authentication](#authentication)
- [Rate Limiting](#rate-limiting)
- [Error Handling](#error-handling)
- [Swap Requests API](#swap-requests-api)
- [Chores API](#chores-api)
- [On-Duty API](#on-duty-api)
- [Feedback API](#feedback-api)

---

## Authentication

All API endpoints require authentication via API key.

### API Key Header
```
X-API-Key: your-api-key-here
```

### Scopes
API keys have specific scopes that determine which endpoints they can access:
- `swaps:read` - List and view swap requests
- `swaps:write` - Create and cancel swap requests
- `swaps:approve` - Approve/decline swap requests (admin)
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
- **Headers**: Rate limit info is included in response headers

When rate limit is exceeded, you'll receive a `429 Too Many Requests` response.

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

## Swap Requests API

Manage shift swap requests between users.

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

**Example Request**:
```bash
curl -H "X-API-Key: your-key" \
  "https://api.example.com/api/v1/swap-requests?page=1&pageSize=10&status=Pending&includeRelated=true"
```

**Example Response**:
```json
{
  "data": [
    {
      "id": 1,
      "fromAssignmentId": 123,
      "toAssignmentId": 456,
      "fromUserId": 10,
      "toUserId": 20,
      "status": 0,
      "reason": "Need to attend family event",
      "declineReason": null,
      "createdAt": "2025-11-13T10:00:00Z",
      "reviewedAt": null,
      "reviewedBy": null,
      "fromUser": {
        "id": 10,
        "email": "user1@example.com",
        "displayName": "John Doe",
        "role": "Employee",
        "isActive": true
      },
      "toUser": {
        "id": 20,
        "email": "user2@example.com",
        "displayName": "Jane Smith",
        "role": "Employee",
        "isActive": true
      },
      "fromAssignment": {
        "id": 123,
        "shiftInstanceId": 789,
        "userId": 10,
        "shiftInstance": {
          "id": 789,
          "shiftTypeId": 1,
          "workDate": "2025-11-20",
          "staffingRequired": 5,
          "shiftType": {
            "id": 1,
            "name": "Morning Shift",
            "key": "MORNING",
            "start": "08:00",
            "end": "16:00"
          }
        }
      }
    }
  ],
  "pagination": {
    "page": 1,
    "pageSize": 10,
    "totalCount": 25,
    "totalPages": 3
  }
}
```

### Get Swap Request
`GET /api/v1/swap-requests/{id}`

**Required Scope**: `swaps:read`

**Query Parameters**:
- `includeRelated` (bool, default: true) - Include related entities

**Example Request**:
```bash
curl -H "X-API-Key: your-key" \
  "https://api.example.com/api/v1/swap-requests/1?includeRelated=true"
```

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

**Example Request**:
```bash
curl -X POST -H "X-API-Key: your-key" \
  -H "Content-Type: application/json" \
  -d '{"fromAssignmentId":123,"toAssignmentId":456,"reason":"Family event"}' \
  "https://api.example.com/api/v1/swap-requests"
```

**Example Response**: (201 Created)
```json
{
  "id": 1,
  "fromAssignmentId": 123,
  "toAssignmentId": 456,
  "fromUserId": 10,
  "toUserId": 20,
  "status": 0,
  "reason": "Need to attend family event",
  "createdAt": "2025-11-13T10:00:00Z"
}
```

### Approve Swap Request
`POST /api/v1/swap-requests/{id}/approve`

**Required Scope**: `swaps:approve`

**Example Request**:
```bash
curl -X POST -H "X-API-Key: your-admin-key" \
  "https://api.example.com/api/v1/swap-requests/1/approve"
```

### Decline Swap Request
`POST /api/v1/swap-requests/{id}/decline`

**Required Scope**: `swaps:approve`

**Request Body**:
```json
{
  "declineReason": "Scheduling conflict"
}
```

**Example Request**:
```bash
curl -X POST -H "X-API-Key: your-admin-key" \
  -H "Content-Type: application/json" \
  -d '{"declineReason":"Scheduling conflict"}' \
  "https://api.example.com/api/v1/swap-requests/1/decline"
```

### Delete Swap Request
`DELETE /api/v1/swap-requests/{id}`

**Required Scope**: `swaps:write`

**Note**: Only the requester can cancel their own pending request.

**Example Request**:
```bash
curl -X DELETE -H "X-API-Key: your-key" \
  "https://api.example.com/api/v1/swap-requests/1"
```

**Example Response**: 204 No Content

---

## Chores API

Manage chore assignments to users.

### List Chores
`GET /api/v1/chores`

**Required Scope**: `chores:read`

**Query Parameters**:
- `page` (int, default: 1) - Page number
- `pageSize` (int, default: 50, max: 100) - Items per page
- `userId` (int, optional) - Filter by assigned user
- `startDate` (string, optional) - Filter by date >= (yyyy-MM-dd)
- `endDate` (string, optional) - Filter by date <= (yyyy-MM-dd)
- `includeRelated` (bool, default: false) - Include related entities
- `includeCanceled` (bool, default: false) - Include canceled chores

**Example Request**:
```bash
curl -H "X-API-Key: your-key" \
  "https://api.example.com/api/v1/chores?userId=10&startDate=2025-11-01&endDate=2025-11-30"
```

**Example Response**:
```json
{
  "data": [
    {
      "id": 1,
      "userId": 10,
      "date": "2025-11-15",
      "title": "Office cleaning",
      "notes": "Focus on break room",
      "createdBy": 5,
      "createdAt": "2025-11-01T09:00:00Z",
      "canceledAt": null,
      "canceledBy": null,
      "isActive": true,
      "user": {
        "id": 10,
        "email": "user@example.com",
        "displayName": "John Doe",
        "role": "Employee",
        "isActive": true
      }
    }
  ],
  "pagination": {
    "page": 1,
    "pageSize": 50,
    "totalCount": 1,
    "totalPages": 1
  }
}
```

### Get Chore
`GET /api/v1/chores/{id}`

**Required Scope**: `chores:read`

**Query Parameters**:
- `includeRelated` (bool, default: true) - Include related entities

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

**Validation**:
- User must exist
- Date must be valid (yyyy-MM-dd)
- Title is required
- Chores cannot overlap with shifts on the same day

**Example Request**:
```bash
curl -X POST -H "X-API-Key: your-key" \
  -H "Content-Type: application/json" \
  -d '{"userId":10,"date":"2025-11-15","title":"Office cleaning","notes":"Focus on break room"}' \
  "https://api.example.com/api/v1/chores"
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

**Example Request**:
```bash
curl -X PATCH -H "X-API-Key: your-key" \
  -H "Content-Type: application/json" \
  -d '{"title":"Deep office cleaning"}' \
  "https://api.example.com/api/v1/chores/1"
```

### Delete Chore
`DELETE /api/v1/chores/{id}`

**Required Scope**: `chores:write`

**Note**: This performs a soft delete (sets canceledAt/canceledBy).

**Example Request**:
```bash
curl -X DELETE -H "X-API-Key: your-key" \
  "https://api.example.com/api/v1/chores/1"
```

---

## On-Duty API

Manage global on-duty assignments (cross-company).

### List On-Duty Assignments
`GET /api/v1/on-duty`

**Required Scope**: `onduty:read`

**Query Parameters**:
- `page` (int, default: 1) - Page number
- `pageSize` (int, default: 50, max: 100) - Items per page
- `userId` (int, optional) - Filter by assigned user
- `startDate` (string, optional) - Filter by date >= (yyyy-MM-dd)
- `endDate` (string, optional) - Filter by date <= (yyyy-MM-dd)
- `type` (string, optional) - Filter by type: `Hakam`, `Lead`
- `includeRelated` (bool, default: false) - Include related entities
- `includeCanceled` (bool, default: false) - Include canceled assignments

**Example Request**:
```bash
curl -H "X-API-Key: your-key" \
  "https://api.example.com/api/v1/on-duty?type=Hakam&startDate=2025-11-01"
```

**Example Response**:
```json
{
  "data": [
    {
      "id": 1,
      "userId": 10,
      "date": "2025-11-15",
      "type": "Hakam",
      "notes": "Night shift coverage",
      "createdBy": 5,
      "createdAt": "2025-11-01T09:00:00Z",
      "canceledAt": null,
      "canceledBy": null,
      "isActive": true,
      "user": {
        "id": 10,
        "email": "user@example.com",
        "displayName": "John Doe",
        "role": "Employee",
        "isActive": true
      }
    }
  ],
  "pagination": {
    "page": 1,
    "pageSize": 50,
    "totalCount": 1,
    "totalPages": 1
  }
}
```

### Get On-Duty Assignment
`GET /api/v1/on-duty/{id}`

**Required Scope**: `onduty:read`

**Query Parameters**:
- `includeRelated` (bool, default: true) - Include related entities

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

**Validation**:
- User must exist (cross-company allowed)
- Date must be valid (yyyy-MM-dd)
- Type must be either `Hakam` or `Lead`
- Only one active assignment of each type per date

**Example Request**:
```bash
curl -X POST -H "X-API-Key: your-key" \
  -H "Content-Type: application/json" \
  -d '{"userId":10,"date":"2025-11-15","type":"Hakam","notes":"Night shift"}' \
  "https://api.example.com/api/v1/on-duty"
```

### Update On-Duty Assignment
`PATCH /api/v1/on-duty/{id}`

**Required Scope**: `onduty:write`

**Request Body**:
```json
{
  "notes": "Updated notes"
}
```

### Delete On-Duty Assignment
`DELETE /api/v1/on-duty/{id}`

**Required Scope**: `onduty:write`

**Note**: This performs a soft delete (sets canceledAt/canceledBy).

---

## Feedback API

Manage user feedback submissions.

### List Feedback
`GET /api/v1/feedback`

**Required Scope**: `feedback:read`

**Query Parameters**:
- `page` (int, default: 1) - Page number
- `pageSize` (int, default: 50, max: 100) - Items per page
- `submittedBy` (int, optional) - Filter by submitter
- `type` (string, optional) - Filter by type: `Error`, `Suggestion`
- `status` (string, optional) - Filter by status: `New`, `ToWorkOn`
- `startDate` (DateTime, optional) - Filter by creation date >=
- `endDate` (DateTime, optional) - Filter by creation date <=
- `includeRelated` (bool, default: false) - Include related entities

**Example Request**:
```bash
curl -H "X-API-Key: your-key" \
  "https://api.example.com/api/v1/feedback?type=Error&status=New"
```

**Example Response**:
```json
{
  "data": [
    {
      "id": 1,
      "submittedBy": 10,
      "type": "Error",
      "status": "New",
      "content": "The shift swap button is not working",
      "imageFileName": "abc123.png",
      "createdAt": "2025-11-13T10:00:00Z",
      "statusUpdatedAt": null,
      "statusUpdatedBy": null,
      "submitter": {
        "id": 10,
        "email": "user@example.com",
        "displayName": "John Doe",
        "role": "Employee",
        "isActive": true
      }
    }
  ],
  "pagination": {
    "page": 1,
    "pageSize": 50,
    "totalCount": 1,
    "totalPages": 1
  }
}
```

### Get Feedback
`GET /api/v1/feedback/{id}`

**Required Scope**: `feedback:read`

**Query Parameters**:
- `includeRelated` (bool, default: true) - Include related entities

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

**Validation**:
- Type must be either `Error` or `Suggestion`
- Content is required

**Example Request**:
```bash
curl -X POST -H "X-API-Key: your-key" \
  -H "Content-Type: application/json" \
  -d '{"type":"Error","content":"Button not working"}' \
  "https://api.example.com/api/v1/feedback"
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

**Example Request**:
```bash
curl -X PATCH -H "X-API-Key: your-key" \
  -H "Content-Type: application/json" \
  -d '{"status":"ToWorkOn"}' \
  "https://api.example.com/api/v1/feedback/1/status"
```

### Delete Feedback
`DELETE /api/v1/feedback/{id}`

**Required Scope**: `feedback:write`

**Example Request**:
```bash
curl -X DELETE -H "X-API-Key: your-key" \
  "https://api.example.com/api/v1/feedback/1"
```

---

## Feature Flags

All API endpoints can be individually enabled/disabled via configuration in `appsettings.json`:

```json
{
  "Features": {
    "Api": {
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
