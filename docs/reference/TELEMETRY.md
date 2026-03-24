# ShiftManager Telemetry Documentation

## Overview

ShiftManager collects client-side telemetry for local observability in air-gapped deployments.
All telemetry data stays on-premise and is never transmitted externally.

## What Is Collected

### 1. Analytics Events
| Event | Data | Purpose |
|-------|------|---------|
| `calendar_view_changed` | Previous/new view names | UI usage patterns |
| `scope_changed` | New scope identifier | Navigation patterns |
| `widget_toggled` | Widget name, visibility state | Feature usage |
| `navigation_category_toggled` | Category name, expanded state | Navigation patterns |
| `context_switched` | Context identifier | Company/molecule switching |

### 2. Client-Side Errors
| Data | Example | Purpose |
|------|---------|---------|
| Error message | "TypeError: Cannot read property..." | Bug detection |
| Source file | "calendar-inline-edit.js" | Error source tracking |
| Line/column number | Line 42, Col 5 | Debug location |
| Stack trace (sanitized) | Abbreviated stack | Root cause analysis |

### 3. Core Web Vitals
| Metric | Description | Purpose |
|--------|-------------|---------|
| LCP (Largest Contentful Paint) | Page load time | Performance monitoring |
| FID/INP (First Input Delay) | Interaction responsiveness | Responsiveness monitoring |
| CLS (Cumulative Layout Shift) | Visual stability | UX quality |
| TTFB (Time to First Byte) | Server response time | Server performance |

## Privacy & Security

- **No PII collected**: Email addresses, phone numbers, and tokens are scrubbed before transmission
- **Session-based**: Random session IDs (`sess_*`), NOT user IDs, are used for grouping
- **Rate limited**: Maximum 30 requests per minute per IP (see RateLimitingService for current limits)
- **Batched**: Events queue locally and send in batches of 10 every 5 seconds
- **Max queue**: 100 events maximum before oldest are dropped
- **Offline support**: Events queue when offline and retry when connection restores

## Retention

Telemetry events are stored in the application's audit/telemetry system.
Retention follows the organization's data lifecycle policy.
Data can be purged via the Owner > Data Lifecycle page.

## Consent

Per organizational policy, telemetry consent is handled at the organizational level.
No individual consent banner is displayed. This is documented for internal governance review.

## Endpoint

- **URL**: `POST /Api/Telemetry`
- **Authentication**: AllowAnonymous (supports pre-auth pages such as the login page; session cookie used when available for user context)
- **Format**: JSON batch array

## Configuration

Telemetry is always enabled for authenticated users. There is no global disable toggle.
Individual users cannot opt out of performance telemetry (Core Web Vitals).
Analytics events can be suppressed by not interacting with tracked UI elements.
