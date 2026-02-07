# API Versioning Policy

## Current Version

- **API v1**: `/api/v1/*` - Current and only supported version

## Versioning Strategy

- **URL-based versioning**: Version is part of the URL path (`/api/v1/...`)
- **No sunset policy yet**: v1 is the first and only version
- **Breaking changes**: Will increment to v2 when needed

## Internal vs External APIs

### External API (`/api/v1/*`)
- Authenticated via `X-API-Key` header
- Scoped by API key permissions
- Rate limited per key
- Versioned with `/v1/` prefix
- Documented in `API_DOCUMENTATION.md`

### Internal API (`/Api/*`)
- Authenticated via session cookie
- CSRF protected via `X-Requested-With: XMLHttpRequest` header
- Used by browser UI (Razor Pages JS)
- **Not versioned** — internal endpoints follow the UI release cycle
- Endpoints: Calendar, Game, Localization, ScopeSwitcher, Telemetry, ScheduleExport, SessionStatus

## Breaking Change Policy

A breaking change is defined as:
- Removing or renaming an endpoint
- Removing or renaming a response field
- Changing a field's data type
- Adding a required request parameter
- Changing authentication or authorization requirements

Non-breaking changes (allowed in v1):
- Adding new endpoints
- Adding optional request parameters
- Adding new response fields
- Adding new enum values

## Migration Guide Template

When v2 is introduced:
1. v1 endpoints will continue to work for at least 3 months
2. Deprecation headers will be added to v1 responses
3. Migration guide will document all breaking changes
4. v1 will be removed only after all consumers have migrated
