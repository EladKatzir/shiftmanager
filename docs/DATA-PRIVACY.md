# Data Privacy Guidelines (B-036)

## Overview

ShiftManager follows strict data privacy practices to protect user PII (Personally Identifiable Information). This document outlines the audit findings and guidelines for handling sensitive data.

## Audit Results (2026-01-31)

### Password Fields
| Check | Status | Notes |
|-------|--------|-------|
| Login page | PASS | `type="password"` |
| Signup page | PASS | `type="password"` |
| Forgot password | PASS | `type="password"` |
| Admin user reset | PASS | `type="password"` |
| Email API key | PASS | `type="password"` |
| Manager password | PASS | `type="password"` |

### localStorage Audit
| Key | Contains | Status |
|-----|----------|--------|
| `theme` | "light"/"dark" | PASS - No PII |
| `commandPaletteRecent` | Page URLs only | PASS - No PII |
| `pendingShiftName_*` | Shift name text | PASS - No PII |
| `shifty_nav_prefs` | UI collapsed states | PASS - No PII |
| `SIDEBAR_COLLAPSED_KEY` | Boolean | PASS - No PII |
| `shifty_last_context` | Context ID/name | PASS - No email/phone |
| `shifty_widget_*` | Boolean collapsed states | PASS - No PII |
| `shifty_scope` | Scope name | PASS - No PII |

### sessionStorage Audit
| Key | Contains | Status |
|-----|----------|--------|
| `language-override-drafts:*` | Translation text drafts | PASS - No PII |
| `shifty_cache_version` | Timestamp | PASS - No PII |

### JavaScript API Keys
| Check | Status |
|-------|--------|
| Hardcoded API keys in wwwroot/js/ | PASS - None found |
| Bearer tokens in source | PASS - None found |
| Secret patterns in JS | PASS - None found |

### Telemetry & Analytics
| Check | Status | Notes |
|-------|--------|-------|
| Session ID | PASS | Uses random ID (`sess_*`), not user ID |
| PII scrubbing | PASS | `scrubPii()` replaces emails/phones/tokens |
| Page URLs | PASS | Path only, no query params |
| Error messages | PASS | Scrubbed before sending |

### Phone Number Display
| Context | Display | Justification |
|---------|---------|---------------|
| On-Call Widget | Full number | Emergency contacts require full numbers |
| User's own profile | Full number (editable) | Self-service |
| Admin views | Full number | Administrative access |
| Other contexts | Should use masking | Use `PrivacyHelper.MaskPhone()` |

### Email Display
| Context | Current Behavior | Recommendation |
|---------|------------------|----------------|
| Assignment dropdowns | Full email | Consider masking for large orgs |
| Admin user lists | Full email | Acceptable for admin context |
| localStorage | Not stored | PASS |

## Client-Side Data Rules

### DO NOT store in localStorage/sessionStorage:
- Email addresses
- Full phone numbers (except for display in session)
- Full names
- Unencrypted user IDs
- Session tokens (use httpOnly cookies)
- API keys or secrets
- Medical information
- Financial data

### Allowed in localStorage:
- UI preferences (theme, language)
- Widget collapsed states
- Feature flag states
- Recent page URLs (paths only)
- Context IDs (organization identifiers)

## Using PrivacyHelper

### Phone Number Masking

```csharp
@using ShiftManager.Helpers

// When displaying phone to non-privileged users:
<span>@PrivacyHelper.MaskPhone(user.Phone)</span>
// Output: ***-***-1234

// Exception: On-call contacts show full number by design
```

### Email Masking

```csharp
// For non-admin views:
<span>@PrivacyHelper.MaskEmail(user.Email)</span>
// Output: j***@example.com
```

### Analytics User ID Hashing

```csharp
// In C# (server-side):
var hashedId = PrivacyHelper.HashUserId(user.Id);
// Output: 16-character hex string

// In JavaScript (client-side):
// telemetry.js already uses random session IDs
window.trackEvent('action', { scope: 'monthly' });
```

### PII Scrubbing for Logs

```csharp
// Before logging user-provided content:
var safeMessage = PrivacyHelper.ScrubPii(errorMessage);
_logger.LogError("User error: {Message}", safeMessage);
// Output: "User error: Contact [EMAIL] at [PHONE] for token [TOKEN]"
```

## Exceptions (Full PII Shown)

These cases intentionally display full PII:

1. **On-Call Emergency Contacts**
   - Full phone numbers displayed
   - Required for immediate emergency response
   - Access controlled by grants (`ViewHakamOnCall`, `ViewCompanyOnCall`)

2. **User's Own Profile**
   - User can see/edit their own full information
   - Self-service requirement

3. **Admin/Owner Views**
   - Full access for administrative purposes
   - Protected by role-based access control

4. **Assignment Management**
   - Emails shown in assignment dropdowns
   - Required for shift assignment workflow

## Audit Checklist

Run this checklist periodically:

| Check | How to Verify |
|-------|---------------|
| Password fields | View source, search for `type="text"` on password inputs |
| localStorage | Open DevTools > Application > Local Storage |
| Page source | View source, search for `@` (emails), phone patterns |
| API keys | Search codebase for key patterns |
| Error messages | Check error handling doesn't expose PII |
| Analytics | Verify session IDs, not user IDs |

## Related Documents

- `telemetry.js` - Client-side telemetry with PII scrubbing
- `Helpers/PrivacyHelper.cs` - Server-side masking utilities
- `Pages/Owner/EmailConfig.cshtml` - API key handling example

## Compliance Notes

This implementation supports:
- GDPR Article 25 (Data Protection by Design)
- Data minimization principles
- Purpose limitation (only collect/display what's needed)

---

*Last Audit: 2026-01-31*
*Audit ID: B-036*
