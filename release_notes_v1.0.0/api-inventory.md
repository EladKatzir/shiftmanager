# API Inventory - ShiftManager v1.0.0

## Overview
This document provides a complete inventory of all API endpoints in the ShiftManager application, including authentication mechanisms, feature flags, and access controls.

---

## API v1 Endpoints (Authenticated via X-API-Key header)

Base Path: `/api/v1`
Authentication: API Key via `X-API-Key` header
Company Scoping: Automatic via CompanyId claim from API key

### 1. Shifts API

| Method | Route | Purpose | Auth | Scope | Feature Flag | Status |
|--------|-------|---------|------|-------|--------------|--------|
| GET | `/api/v1/shifts` | List shift instances with pagination/filtering | API Key | shift:read | `Features:Api:Shifts:ListEnabled` | ✅ Enabled |
| GET | `/api/v1/shifts/{id}` | Get single shift instance details | API Key | shift:read | `Features:Api:Shifts:GetEnabled` | ✅ Enabled |

**Query Parameters (List)**:
- `page` (int, default: 1)
- `pageSize` (int, default: 50, max: 100)
- `startDate` (string, yyyy-MM-dd)
- `endDate` (string, yyyy-MM-dd)
- `shiftTypeId` (int, optional)
- `userId` (int, optional)
- `hasOpenSlots` (bool, optional)

---

### 2. Users API

| Method | Route | Purpose | Auth | Scope | Feature Flag | Status |
|--------|-------|---------|------|-------|--------------|--------|
| GET | `/api/v1/users` | List users with pagination/filtering | API Key | user:read | `Features:Api:Users:ListEnabled` | ✅ Enabled |
| GET | `/api/v1/users/{id}` | Get single user details | API Key | user:read | `Features:Api:Users:GetEnabled` | ✅ Enabled |
| POST | `/api/v1/users` | Create new user | API Key | user:write | `Features:Api:Users:CreateEnabled` | ✅ Enabled |
| PATCH | `/api/v1/users/{id}` | Update user (partial) | API Key | user:write | `Features:Api:Users:UpdateEnabled` | ✅ Enabled |

**Query Parameters (List)**:
- `page` (int, default: 1)
- `pageSize` (int, default: 50, max: 100)
- `role` (string: Owner/Manager/Employee/Director/Trainee/Assigner)
- `isActive` (bool, optional)
- `search` (string, optional)

**Request Bodies**:
- Create: `{ email*, displayName*, role*, password?, department?, jobTitle? }`
- Update: `{ displayName?, role?, isActive?, department?, jobTitle?, phone? }`

---

### 3. Time Off API

| Method | Route | Purpose | Auth | Scope | Feature Flag | Status |
|--------|-------|---------|------|-------|--------------|--------|
| GET | `/api/v1/time-off-requests` | List time-off requests | API Key | timeoff:read | `Features:Api:TimeOff:ListEnabled` | ✅ Enabled |
| GET | `/api/v1/time-off-requests/{id}` | Get single request details | API Key | timeoff:read | `Features:Api:TimeOff:GetEnabled` | ✅ Enabled |
| POST | `/api/v1/time-off-requests` | Create new request | API Key | timeoff:write | `Features:Api:TimeOff:CreateEnabled` | ✅ Enabled |
| POST | `/api/v1/time-off-requests/{id}/approve` | Approve request | API Key | timeoff:approve | `Features:Api:TimeOff:ApproveEnabled` | ✅ Enabled |
| POST | `/api/v1/time-off-requests/{id}/decline` | Decline request | API Key | timeoff:approve | `Features:Api:TimeOff:DeclineEnabled` | ✅ Enabled |

**Query Parameters (List)**:
- `page` (int, default: 1)
- `pageSize` (int, default: 50, max: 100)
- `userId` (int, optional)
- `status` (string: Pending/Approved/Declined)
- `startDate` (string, yyyy-MM-dd)
- `endDate` (string, yyyy-MM-dd)

**Request Body (Create)**:
- `{ userId*, startDate*, endDate*, reason? }`

---

### 4. Notifications API

| Method | Route | Purpose | Auth | Scope | Feature Flag | Status |
|--------|-------|---------|------|-------|--------------|--------|
| GET | `/api/v1/notifications` | List notifications | API Key | notification:read | `Features:Api:Notifications:ListEnabled` | ✅ Enabled |
| GET | `/api/v1/notifications/{id}` | Get single notification | API Key | notification:read | `Features:Api:Notifications:GetEnabled` | ✅ Enabled |
| POST | `/api/v1/notifications/{id}/mark-read` | Mark as read | API Key | notification:write | `Features:Api:Notifications:MarkReadEnabled` | ✅ Enabled |
| POST | `/api/v1/notifications/mark-all-read` | Mark all as read | API Key | notification:write | `Features:Api:Notifications:MarkAllReadEnabled` | ✅ Enabled |

**Query Parameters (List)**:
- `page` (int, default: 1)
- `pageSize` (int, default: 50, max: 100)
- `userId` (int, optional)
- `isRead` (bool, optional)
- `type` (string, optional)

---

### 5. Analytics API

| Method | Route | Purpose | Auth | Scope | Feature Flag | Status |
|--------|-------|---------|------|-------|--------------|--------|
| GET | `/api/v1/analytics/summary` | Get analytics summary | API Key | analytics:read | `Features:Api:Analytics:SummaryEnabled` | ✅ Enabled |

**Query Parameters**:
- `startDate` (string, yyyy-MM-dd, default: -30 days)
- `endDate` (string, yyyy-MM-dd, default: today)

**Response**: Company metrics including total users, active users, shifts scheduled, pending requests

---

### 6. Audit Logs API

| Method | Route | Purpose | Auth | Scope | Feature Flag | Status |
|--------|-------|---------|------|-------|--------------|--------|
| GET | `/api/v1/audit-logs` | List audit log entries | API Key | audit:read | `Features:Api:AuditLogs:ListEnabled` | ✅ Enabled |

**Query Parameters**:
- `page` (int, default: 1)
- `pageSize` (int, default: 50, max: 100)
- `userId` (int, optional)
- `action` (string, optional)
- `startDate` (DateTime, optional)
- `endDate` (DateTime, optional)

---

## Team Calendars API (Session-Based Authentication)

Base Path: `/api/team-calendars`
Authentication: ASP.NET Core Identity (Cookie/JWT)
Authorization: `[Authorize]` attribute

| Method | Route | Purpose | Auth | Status |
|--------|-------|---------|------|--------|
| GET | `/api/team-calendars` | List user's calendars | Session | ✅ Active |
| GET | `/api/team-calendars/{id}` | Get calendar with members | Session | ✅ Active |
| POST | `/api/team-calendars` | Create new calendar | Session | ✅ Active |
| PUT | `/api/team-calendars/{id}` | Rename calendar | Session | ✅ Active |
| DELETE | `/api/team-calendars/{id}` | Delete calendar | Session | ✅ Active |
| GET | `/api/team-calendars/{id}/members` | Get members and available users | Session | ✅ Active |
| PUT | `/api/team-calendars/{id}/members` | Update calendar members | Session | ✅ Active |
| GET | `/api/team-calendars/{id}/week` | Get week view with statuses | Session | ✅ Active |

**Week View Features**:
- Returns 7-day view starting from specified Sunday
- Member daily status: Free, Shift, TimeOff, OnDuty, Chore
- Role-based navigation URLs (Manager/Director/Owner get links, others view-only)

---

## Response Formats

### Standard Pagination Response
```json
{
  "data": [...],
  "pagination": {
    "page": 1,
    "pageSize": 50,
    "totalCount": 150,
    "totalPages": 3
  }
}
```

### Error Response (ApiProblemDetails)
```json
{
  "type": "about:blank",
  "title": "Error Title",
  "status": 400,
  "detail": "Detailed error message",
  "instance": "/api/v1/resource"
}
```

**HTTP Status Codes**:
- `200` OK - Success
- `201` Created - Resource created successfully
- `400` Bad Request - Validation error
- `401` Unauthorized - Invalid/missing authentication
- `403` Forbidden - Insufficient permissions
- `404` Not Found - Resource not found or endpoint disabled
- `409` Conflict - Duplicate resource or business rule violation
- `429` Too Many Requests - Rate limit exceeded
- `500` Internal Server Error - Unexpected error

---

## Authentication & Security

### API Key Authentication (API v1)
- Header: `X-API-Key: {your-api-key}`
- Managed via: `/My/ApiKeys` page
- Features:
  - Scope-based permissions (read/write/approve)
  - Company-scoped access (automatic isolation)
  - Rate limiting (100 requests per minute default)
  - Request logging (all API calls tracked)
  - Key rotation support

### Session Authentication (Team Calendars)
- Cookie-based or JWT token
- Role-based authorization (Owner, Director, Manager, Employee, Trainee, Assigner)
- Company-scoped data access

---

## Rate Limiting

**API v1 Endpoints**:
- Default: 100 requests per minute per API key
- Configurable per key
- Response headers:
  - `X-RateLimit-Limit`: Total allowed
  - `X-RateLimit-Remaining`: Remaining in window
  - `X-RateLimit-Reset`: Unix timestamp of reset
- 429 response includes `Retry-After` header

---

## Middleware Stack (API v1)

1. **RequestLoggingMiddleware** - Logs all requests with correlation ID
2. **ApiAuthenticationMiddleware** - Validates API key and sets claims
3. **ApiRateLimitingMiddleware** - Enforces rate limits
4. **ApiRequestLoggingMiddleware** - Logs API-specific metrics to database

---

## Summary Statistics

- **Total API Endpoints**: 27
  - API v1 (API Key auth): 19 endpoints
  - Team Calendars (Session auth): 8 endpoints
- **Feature Flags**: 19 (all API v1 endpoints individually controllable)
- **HTTP Methods**: GET (14), POST (8), PUT (2), PATCH (1), DELETE (1)
- **Scopes Defined**: 10 unique scopes
- **Release Status**: All endpoints enabled and ready for production ✅

---

*Document Generated: 2025-01-12 (Release Readiness Phase 4)*
