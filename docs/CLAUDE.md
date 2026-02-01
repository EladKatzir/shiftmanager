- whenever you fail due to background shell still running, just take a temporary pause and ask the user to terminate the run on his own. after he does, continue working
- 🔧 API 401/Authentication Troubleshooting Checklist

  When encountering 401 errors on internal browser-based API endpoints (used by JavaScript in the UI), check these three things:

  1. ApiAuthenticationMiddleware Whitelist (Most Common Issue)

  File: Middleware/ApiAuthenticationMiddleware.cs
  Method: IsInternalWebUiEndpoint()

  The middleware intercepts ALL /api routes and requires X-API-Key headers by default (for external integrations). Internal browser endpoints must be whitelisted:

  // Current whitelist:
  - /api/team-calendars
  - /Api/SessionStatus
  - /Api/Calendar
  - /Api/Game

  Fix: Add new internal endpoint to the whitelist in IsInternalWebUiEndpoint().

  2. Missing [IgnoreAntiforgeryToken] Attribute

  Issue: Razor Page API endpoints need this attribute to accept JSON requests from JavaScript.

  Pattern from working endpoints:
  [Authorize(Policy = "CanEditChores")]
  [IgnoreAntiforgeryToken]
  public class QuickAddChoreModel : PageModel

  3. Missing credentials: 'same-origin' in JavaScript

  Issue: Fetch requests must include authentication cookies.

  fetch('/Api/Game/GetLocalization', {
      credentials: 'same-origin'  // ← Required!
  })

  Key Distinction

  - Internal APIs (browser/UI): Use cookie auth, need whitelist + [IgnoreAntiforgeryToken]
  - External APIs (/api/v1/*): Use X-API-Key header, managed by ApiAuthenticationMiddleware