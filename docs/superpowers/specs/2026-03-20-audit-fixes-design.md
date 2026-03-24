# ShiftManager v3.1.x Audit Fix Spec

**Date:** 2026-03-20
**Scope:** 37 fixes from the pre-release audit, executed by 5 parallel agents
**Branch:** `update` (current working branch)
**Implementation Status (verified 2026-03-23):** All 37 fixes have been implemented. Cross-referenced against codebase: Fix 1-37 all confirmed complete. See `plans/2026-03-23-audit-fixes.md` for remaining non-spec tasks (P3-7 calendar in-place update, M-2 mobile filter layout, P2-2 console cleanup, P3-5/P4-4 meta tag removal — these are tracked in the plan, not this spec).

---

## Execution Model

5 specialized agents run in parallel, each owning a set of files. No agent touches another agent's files. One ordering dependency: Agent 5 completes `Login.cshtml` Figma removal before Agent 4 modifies lockout strings in the same file.

Each agent:
- Gets full context (project memory, audit decisions, file ownership)
- Runs `dotnet build` after changes to verify compilation
- Does NOT commit — all changes are uncommitted for human review
- Reports what was done for each fix with evidence

---

## Excluded Findings (Intentional Triage Decisions)

The following audit findings were reviewed during brainstorming and explicitly excluded:

| Audit ID | Finding | Rationale |
|---|---|---|
| F1-4 | `GetStartOfWeek` hardcodes Sunday | **By design** — user confirmed week is always Sunday–Saturday for this app. The `WeekStartDay` config serves a different purpose (weekly cap boundary). Not a bug. |
| F1-6 | `HomeTypeService.DeleteHomeTypeAsync` orphans rows | **Not a bug** — `HomeTypeOverride → HomeType` FK uses `OnDelete(DeleteBehavior.Cascade)` (AppDbContext.cs:1184). SQLite auto-deletes overrides when HomeType is removed. |
| F1-8 | GetShiftsData API no molecule access validation | **Accepted risk** — low-sensitivity operational data behind authentication. User decided to accept. |
| F1-10 | Admin/Companies has no grant policy at page level | **Accepted** — company names already visible throughout app (dropdowns, profiles, calendars). No additional data exposure. |
| F1-11 | TOCTOU gap in chore cancel/delete | **Skipped** — requires two managers canceling same chore within milliseconds. Practically impossible. Data state remains correct. |
| D2-10 | Legacy `.btn` overrides design-system `.btn` | **Deferred to post-launch** — 227 instances across 74 files. Legacy system IS the app's visual language (95% of buttons). Needs visual regression testing. |
| D2-16 | Hover-only dropdown not keyboard accessible | **Not a bug** — dead CSS rule. No HTML element uses `.dropdown` + `.menu` pattern. Actual dropdowns are JS-controlled. |
| D2-17 | Most modals lack `role="dialog"` | **Deferred** — real accessibility gap but military user base unlikely to use screen readers. `modal-focus.js` handles focus trapping via JS. |
| D2-18 | `console.log` in production layout | **Intentional** — user prefers keeping diagnostic breadcrumbs in console for future developers, as long as they don't expose code internals. |
| C4-1 | HMAC secret hardcoded fallback | **Skipped startup validation** — user prefers improving the config comment/guide instead (done in Fix 33). |
| C4-3 | AuditLog cross-tenant leak | **Not a bug** — verified that `AuditLog` implements `IBelongsToCompany`. EF query filter auto-scopes by tenant. |
| C4-5 | Test data seeders run in all environments | **Not a bug** — verified that seeders have internal environment guards (`IsTestEnvironment()` / `ASPNETCORE_ENVIRONMENT == "Development"`). |
| C4-7 | DatabaseConsole LoadTablesAsync uses shared EF connection | **Skipped** — theoretical risk only. Works fine in practice. SQLite WAL mode handles concurrent reads. |
| L3-7 | language-edit-mode.js hardcoded English | **Skipped** — Owner-only localization management tool. Minimal user impact. |
| L3-8 | localization-attributes.js hardcoded English | **Skipped** — Owner-only tool. Same rationale. |
| L3-10 | DatabaseConsole error strings not localized | **Skipped** — raw SQL console where English is the natural language. |

---

## Agent 1: CSS & Design Tokens

**Owns:** `tokens.css`, `site.css`, `components.css`, `navigation.css`, `calendar.css`, `home-types.css`, `auth.css`, `rtl.css`

### Fix 18 — Dark-mode focus ring contrast
**File:** `wwwroot/css/tokens.css:215` (dark mode `[data-theme="dark"]`) AND the duplicate at `~line 572` (`@media prefers-color-scheme`)
**Problem:** `--focus-ring: 0 0 0 3px var(--primary-soft)` where `--primary-soft` is `#1A2D42` — nearly invisible on `--surface: #1A2332`
**Fix:** Change the dark-mode `--focus-ring` in BOTH dark mode blocks to use a brighter, more visible value. Use `0 0 0 3px rgba(88, 166, 255, 0.5)` or equivalent that achieves at least 3:1 contrast against `--surface` in dark mode.
**Verify:** The focus ring must be visible when tabbing through buttons, inputs, and nav items in dark mode.

### Fix 20 — `--text-subtle` contrast in dark mode
**File:** `wwwroot/css/tokens.css` — both dark mode blocks
**Problem:** `--text-subtle: #64748B` is the same in light and dark mode. On dark `--bg: #0F1419` it achieves only ~3.5:1 contrast (WCAG AA requires 4.5:1).
**Fix:** Change `--text-subtle` in BOTH dark mode blocks to a lighter value. Target ~5:1 contrast ratio against `#0F1419`. Suggested: `#8896A6` (~4.8:1) or `#94A3B8` (~5.6:1). Use a contrast checker to verify.
**CRITICAL:** Update in BOTH `[data-theme="dark"]` AND `@media (prefers-color-scheme: dark)` blocks — they must stay in sync.

### Fix 19 — Remove `--surface-strong` compat alias
**File:** `wwwroot/css/site.css:32`
**Problem:** `--surface-strong: var(--text)` overrides the correct `tokens.css` definition. Causes light background in dark mode for tooltips, badges, user menu.
**Fix:** Delete the line `--surface-strong: var(--text);` from the compat layer. The `tokens.css` definitions (`#1A1F2B` light / `#3D4B5E` dark) will take effect.
**After deletion:** Verify the 3 consumers still make sense:
  - `components.css:1711` — check what element this is, confirm dark background + light text
  - `navigation.css:706` — sidebar collapsed tooltip, confirm dark background + light text
  - `site.css:558` — `.user-menu:hover`, confirm dark hover background

### Fix 21 — Replace ~30 hardcoded hex colors in site.css
**File:** `wwwroot/css/site.css`
**Problem:** Various hardcoded hex values that don't respond to dark mode.
**Discovery:** Find all candidates with: `grep -n '#[0-9a-fA-F]\{3,8\}' wwwroot/css/site.css` — then exclude comments, CSS custom property definitions in `:root`/`[data-theme]` blocks, and `#fff`/`#000` which are often intentional.
**Approach:** Go through each hardcoded color one by one. For each:
1. Read the selector to understand what element it styles
2. Determine the semantic intent (is it a shift color? status color? text? border?)
3. Map to an existing design token from `tokens.css`
4. If no semantic match exists, create a new token in `tokens.css` (both light AND dark mode blocks)

**Known mappings:**
- `#333` (text on colored badges) → `var(--text)` or keep if the badge needs fixed dark text regardless of theme
- `#FFEB3B` (morning shift) → `var(--shift-morning)`
- `#AED581` (noon shift) → `var(--shift-noon)`
- `#FFB74D` (night shift) → `var(--shift-night)`
- `#5C6BC0` (middle shift) → `var(--shift-middle)`
- `#e8f5e8` / `#2e7d32` (success alert) → `var(--success-soft)` / `var(--success)`
- `#ffebee` (error alert) → `var(--danger-soft)`
- `#58a6ff` (game stats) → `var(--accent)` or `var(--primary)` in dark context
- `#9aa8b4` (muted text) → `var(--text-muted)` or `var(--text-subtle)`
- `#ffd700` (gold/trophy) → may need new token `--accent-gold` if no match
- `#4f46e5` (indigo gradient) → `var(--primary)` or `var(--primary-hover)` depending on context
- Alert borders `#2196F3`, etc. → `var(--info)`, `var(--danger)`, `var(--warning)`

**IMPORTANT:** Some hardcoded colors on shift badges may be intentionally fixed (a yellow badge is yellow in all themes). For these, verify the text color has sufficient contrast in both themes. If the badge background is intentionally fixed, ensure the text color is also explicitly set (not inherited).

### Fix 22 — z-index: 999999999
**File:** `wwwroot/css/site.css:1805,1834`
**Problem:** `.assignment-tooltip` and `.tooltip-content` use `z-index: 999999999 !important`
**Fix:** Replace with a token-appropriate value. The tooltip needs to appear above:
  - Calendar cells (`z-index: 1-15`)
  - Dropdowns (`--z-dropdown: 1000`)
  - Fixed elements (`--z-fixed: 1030`)
  - Modals (`--z-modal: 1050`)
  - Toasts (`--z-toast: 1080`)

  Use `z-index: var(--z-max) !important` where `--z-max: 9999`. If the tooltip must appear above modals too, use `--z-toast` or create `--z-tooltip: 1090`.
**REQUIRES VISUAL VERIFICATION:** Note this fix for later browser testing. The tooltip must still appear above all other elements when hovering calendar assignments.

### Fix 3 — Bottom-sheet dark mode tokens
**File:** `wwwroot/css/calendar.css:3208-3517`
**Problem:** ~20 undefined CSS variable names with light-mode hardcoded fallbacks.
**Fix:** Systematically replace every undefined token in this section:

| Find | Replace with |
|------|-------------|
| `var(--border-color, #dee2e6)` | `var(--border)` |
| `var(--bg-subtle, #f8f9fa)` | `var(--surface-soft)` |
| `var(--text-primary, #212529)` | `var(--text)` |
| `var(--text-muted, #6c757d)` | `var(--text-muted)` (this one exists, just remove the fallback) |
| `var(--primary-light, #e3f2fd)` | `var(--primary-soft)` |
| `var(--primary-dark, #1565c0)` | `var(--primary-hover)` |
| `var(--hover-bg, #e9ecef)` | `var(--surface-soft)` |
| `var(--card-bg, #fff)` | `var(--surface)` |
| `var(--text-secondary, #495057)` | `var(--text-muted)` |
| Any other `var(--X, #hardcoded)` where `--X` is not in `tokens.css` | Find closest semantic token |

**After replacement:** Every `var()` reference in this section must resolve to a token defined in `tokens.css`. No hardcoded fallbacks should remain.

### Fix 11 — Sidebar logout button tokens
**File:** `wwwroot/css/navigation.css:856,860`
**Fix:** Replace `var(--error)` with `var(--danger)` and `var(--error-soft)` with `var(--danger-soft)`.

### Fix 12 — Avatar fallback token
**File:** `wwwroot/css/components.css:2975`
**Problem:** `var(--text-secondary)` undefined.
**Fix:** Determine the correct token. The avatar fallback shows initials on a colored circle. The text needs to contrast against the avatar background color. Check what `.avatar` background is — if it's `var(--primary)`, the text should be `var(--primary-contrast)`. If the background varies, use `var(--text-muted)` as a safe default.
**Also:** Search for all places avatar images are rendered (`.avatar img`, `.avatar__image`, etc.) and verify that when a user has `AvatarFileName` set, the image is used everywhere avatars appear (sidebar, profile, user lists, calendar assignments). Flag any inconsistency.

### Fix 13 — `--radius` undefined in home-types.css
**File:** `wwwroot/css/home-types.css:16,44`
**Fix:** Replace `var(--radius)` with the appropriate token. Check the visual context:
  - Line 16: `.htc-nav button` — navigation buttons, likely want `var(--radius-md)` (8px) for pill-like buttons
  - Line 44: `.htc-day` — calendar day cells, likely want `var(--radius-sm)` (4px) for subtle rounding
  Verify the sizing feels right for the element scale.

### Fix 14 — Hardcoded colors in home-types.css
**File:** `wwwroot/css/home-types.css:53-54`
**Problem:** `background: #F8E7B1; border-color: #D4A017;` on `.htc-day--selected`
**Fix:** Check if an existing token matches the "selected day" semantic. Candidates:
  - `--shift-morning-soft` / `--shift-morning` — if the yellow tone is intentional
  - `--primary-soft` / `--primary` — if this should match the app's primary color
  - `--warning-soft` / `--warning` — if this is a "highlighted/attention" state
  If none match the intended design, create a new token pair: `--selected-day-bg` and `--selected-day-border` in `tokens.css` with both light and dark values.

### Fix 15 — Invalid `[dir="rtl"] @keyframes` in auth.css
**File:** `wwwroot/css/auth.css:690-694`
**Fix:** Delete the invalid `[dir="rtl"] @keyframes formEntry { ... }` block. Instead, add an RTL override for the initial state:
```css
[dir="rtl"] .auth-form-container {
    transform: translateX(-20px);
}
```
The existing `formEntry` keyframe animates to `translateX(0)` which works for both directions. Also check that the initial `transform: translateX(20px)` in the base `.auth-form-container` rule (not inside `[dir="rtl"]`) is what creates the LTR starting position. The RTL override just needs to flip the starting direction.

### Fix 36 — Dead opacity: 0.9 in auth.css
**File:** `wwwroot/css/auth.css:97-103`
**Fix:** Delete the first `opacity: 0.9;` line (the dead one). Keep `opacity: 0;` (the animation initial state).

### Fix 24 — Deduplicate RTL CSS
**File:** `wwwroot/css/rtl.css`
**Problem:** 7 pairs of identical rule blocks.
**Fix:** For each pair:
1. Compare the two blocks CHARACTER BY CHARACTER to confirm they're truly identical
2. If identical: delete the second occurrence, keep the first
3. If NOT identical: flag the difference and keep both, adding a comment

Expected pairs to check:
  - `.oncall-contact` (~line 130 vs ~976)
  - `.friend-oncall` (~line 601 vs ~991)
  - `.next-shift` (~line 611 vs ~1015)
  - `.pending-request` (~line 619 vs ~1026)
  - `.announcement__header` (~line 629 vs ~1041)
  - `.metrics-widget` + `.metric` (~line 639 vs ~1063)
  - `.quick-actions` (~line 634 vs ~1052)

---

## Agent 2: Layout & UX Polish

**Owns:** `Pages/Shared/_Layout.cshtml`, `Pages/Home/Index.cshtml`

### Fix 10 — Duplicate class attributes in Home/Index.cshtml
**File:** `Pages/Home/Index.cshtml:169,203,225,251,273,295,335`
**Problem:** `<div class="X" class="Y" style="...">` — duplicate `class` attribute.
**Fix:** For EACH of the 7 occurrences:
1. Read both `class` attribute values — they may differ
2. Check what CSS rules apply to each class name
3. If they're the same value: delete the duplicate, keep one
4. If they differ: merge into a single `class="X Y"` attribute
5. Check if the inline `style` duplicates or conflicts with the class — if so, prefer the class

### Fix 16 — Dark-mode FOUC
**File:** `Pages/Shared/_Layout.cshtml:49` and nearby `<head>` area
**Problem:** `data-theme="light"` is hardcoded. JS flips to dark after render, causing flash.
**Fix:** Add an inline `<script>` in `<head>` BEFORE any `<link>` stylesheets, immediately after the opening `<html>` tag or as the first thing in `<head>`:
```html
<script>
(function() {
    var theme = localStorage.getItem('theme');
    if (theme === 'dark') {
        document.documentElement.setAttribute('data-theme', 'dark');
    }
})();
</script>
```
This pattern already exists in the layout for sidebar collapsed state (the FOUC fix around line ~240). Follow the same pattern.
Keep the server-rendered `data-theme="light"` as-is — it serves as the no-JS fallback. The inline script overrides it for JS-enabled browsers before first paint.

### Fix 17 — Notification bell aria-label
**File:** `Pages/Shared/_Layout.cshtml:793`
**Problem:** `<a href="/My/NotificationCenter" class="action-btn">` has no accessible name.
**Fix:** Add `aria-label="@Localizer["Nav_Notifications"]"` to the `<a>` tag. Check if the key `Nav_Notifications` already exists in `SharedResources.resx`. If not, Agent 4 should be informed to add it (coordinate via fix description).
**NOTE:** If the .resx key doesn't exist, use a `loc-aria-label` attribute instead (which the existing `localization-attributes.js` handles), or hardcode `aria-label="Notifications"` as a fallback and note that a .resx key is needed.

### Fix 23 — Replace emoji nav icons with Lucide
**File:** `Pages/Shared/_Layout.cshtml:428-499` (all `<span class="app-sidebar-nav-icon">` elements)
**Approach:**
1. First, check what Lucide icons are available. Look for Lucide SVG includes or a Lucide CSS class system in the codebase. Search for `lucide`, `feather`, or existing SVG icon usage patterns.
2. For each emoji, find the best Lucide match:

| Current Emoji | Purpose | Candidate Lucide Icon |
|---|---|---|
| `🏠` | Home/Dashboard | `home` |
| `📅` | Calendar | `calendar` |
| `👁️` | Overview | `eye` |
| `📊` | Analytics | `bar-chart-3` |
| `🗓️` | Schedule | `calendar-days` |
| `✋` | Chores | `hand` or `clipboard-list` |
| `🎮` | Game | `gamepad-2` |
| `👥` | Users | `users` |
| `⚙️` | Settings/Config | `settings` |
| `🔧` | Admin tools | `wrench` |
| `📋` | Requests | `clipboard` or `file-text` |
| `👤` | Profile | `user` |
| `🏢` | Organization | `building-2` |

3. If a Lucide icon doesn't exist for a concept, KEEP the emoji for that item
4. Match the icon rendering to existing `.app-sidebar-nav-icon` CSS (size, color, alignment)
5. Icons must respond to CSS `color` property for hover/active states

**How to check if Lucide is available:** Search for `lucide` in `_Layout.cshtml`, `package.json`, `packages.lock.json`, and `wwwroot/lib/`. Also search for `feather` (Lucide's predecessor). Look for any `<svg>` icon system already in use, or a CDN `<link>`/`<script>` for an icon library.

**If Lucide is NOT set up in the project** (no CDN, no npm package, no SVG sprite), then SKIP this fix entirely and report it. Do not add a new dependency.

---

## Agent 3: Backend & Security

**Owns:** All `.cs` files, `appsettings.Production.json`, `Program.cs`

### Fix 2 — CSP Figma domains dev-only
**File:** `Program.cs:1389-1417` (the security headers middleware)
**Fix:** Split the CSP into dev and production:
```csharp
string csp;
if (app.Environment.IsDevelopment())
{
    // Dev-only: allow Figma MCP capture tool
    csp = "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' https://mcp.figma.com; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: https://mcp.figma.com; " +
        "font-src 'self'; " +
        "connect-src 'self' ws: wss: https://mcp.figma.com; " +
        "frame-ancestors 'none'";
}
else
{
    csp = "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "font-src 'self'; " +
        "connect-src 'self' ws: wss:; " +
        "frame-ancestors 'none'";
}
context.Response.Headers["Content-Security-Policy"] = csp;
```
Remove the `// TEMPORARY` comment.

### Fix 4 — Dashboard unassigned shifts metric
**File:** `Pages/Home/Index.cshtml.cs:176-197`
**Fix:** In `LoadManagerDataAsync`, find the ShiftAssignment query and add `.Where(a => a.UserId != null)` to only count real assignments, not empty capacity slots.
**Verify:** The `UnassignedShiftsCount` should now equal `StaffingRequired - actualAssignments` where `actualAssignments` only counts rows with a real user.

### Fix 5 — Auth guard on Public page POST handlers
**Files:** `Pages/Public/Chores.cshtml.cs`, `Pages/Public/OnDuty.cshtml.cs`
**Fix:** At the top of each POST handler (`OnPostCreateChoreAsync`, `OnPostReplaceShiftWithChoreAsync`, `OnPostCancelChoreAsync`, `OnPostCreateOnDutyAsync`, `OnPostCancelOnDutyAsync`), add:
```csharp
if (User.Identity?.IsAuthenticated != true)
    return new JsonResult(new { success = false, error = "Unauthorized" }) { StatusCode = 401 };
```
Or if the handlers return `Page()` not `JsonResult`, use:
```csharp
if (User.Identity?.IsAuthenticated != true)
    return Unauthorized();
```
Check the return type of each handler to use the appropriate pattern.

### Fix 6 — RestoreChore IDOR
**File:** `Pages/Api/Calendar/RestoreChore.cshtml.cs`
**Problem:** `RestoreChoreAsync` uses `IgnoreQueryFilters()`. No tenant check before calling it.
**Fix:** Before calling `_choreService.RestoreChoreAsync(data.Id)`, add a tenant-scoped lookup:
```csharp
// Verify chore belongs to accessible scope before restoring
var chore = await _choreService.GetChoreByIdAsync(data.Id);
if (chore == null)
    return new JsonResult(new { success = false, error = "Chore not found" }) { StatusCode = 404 };
```
`GetChoreByIdAsync` respects query filters (verify this by checking ChoreService). If it does NOT respect filters, then add an explicit company/molecule check against the current user's accessible scope.

### Fix 7 — Silent empty page on invalid claim
**Files:** `Pages/Home/Index.cshtml.cs:68-72`, `Pages/Admin/Index.cshtml.cs:71-74`
**IMPORTANT:** Both methods have return type `Task` (not `Task<IActionResult>`), so `return RedirectToPage(...)` will NOT compile. Use `Response.Redirect` + `return` instead:
```csharp
if (!int.TryParse(userIdClaim, out var userId))
{
    _logger.LogWarning("Invalid or missing NameIdentifier claim on {Page}", "Home/Index");
    Response.Redirect("/Auth/Login");
    return;
}
```
Use `/Auth/Login` since a missing/invalid claim suggests the session is corrupted. Apply the same pattern to both files (change the page name in the log message accordingly).

### Fix 8 — Add logging to HomeTypeService exception swallows
**File:** `Services/HomeTypeService.cs:520,534`
**Fix:** Replace bare `catch { return null; }` and `catch { return new(); }` with:
```csharp
catch (Exception ex)
{
    _logger.LogWarning(ex, "Failed to deserialize HomeType rule JSON: {Json}", json?.Substring(0, Math.Min(json?.Length ?? 0, 200)));
    return null;
}
```
And:
```csharp
catch (Exception ex)
{
    _logger.LogWarning(ex, "Failed to deserialize HomeType dates JSON: {Json}", json?.Substring(0, Math.Min(json?.Length ?? 0, 200)));
    return new();
}
```
Truncate the JSON in the log to avoid log spam from very large corrupt values.

### Fix 9 — Director SignalR group access
**File:** `Hubs/CalendarHub.cs`
**Problem:** `ValidateGroupAccessAsync` only checks the user's own company. Directors (מ"מ) with `DirectorHubAccess` grant at molecule scope are rejected.
**Fix:** Inject `IGrantService` into `CalendarHub`. In `ValidateGroupAccessAsync`, when the direct company check fails for `shifts` and `chores` groups:
```csharp
// Fallback: check if user has DirectorHubAccess for any company in this molecule
var userIdStr = GetUserId();
if (int.TryParse(userIdStr, out var userId))
{
    var accessibleCompanyIds = await _grantService.GetAccessibleCompanyIdsForGrantAsync(userId, "DirectorHubAccess");
    // SECURITY-AUDITED: SAFE — Directors need cross-company molecule lookup to validate SignalR group access
    var moleculeCompanyIds = await _db.Companies.IgnoreQueryFilters()
        .Where(c => c.MoleculeId == moleculeId)
        .Select(c => c.Id)
        .ToListAsync();
    if (accessibleCompanyIds.Intersect(moleculeCompanyIds).Any())
        return true;
}
```
Apply this fallback to `shifts`, `chores`, and `oncall` group types. For `overview`, the existing direct company check is sufficient (overview is company-scoped, not molecule-scoped).

**IMPORTANT:** This adds a DB query on the SignalR connection path. This is acceptable because:
1. It only fires when the direct check fails (most users pass the direct check)
2. `GetAccessibleCompanyIdsForGrantAsync` is per-request cached in GrantService
3. The rate limiter already protects against abuse

### Fix 32 — int.Parse on claims
**Files:** `ViewComponents/UnreadNotificationCountViewComponent.cs:27`, `Pages/Admin/Companies.cshtml.cs:386`, `Pages/Admin/DutyRotation/Index.cshtml.cs:145`, `Pages/Admin/Organization/Molecules/Index.cshtml.cs:148`

**Fix for ViewComponent (most critical):**
```csharp
// Replace: var userId = int.Parse(claimsPrincipal.FindFirst(ClaimTypes.NameIdentifier)!.Value);
if (!int.TryParse(claimsPrincipal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
{
    return Content("");  // Graceful fallback — no notification count shown
}
```

**Fix for the other 3 (all use `?? "0"` pattern):**
```csharp
// Replace: var currentUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var currentUserId))
{
    _logger.LogWarning("Invalid or missing NameIdentifier claim");
    return RedirectToPage("/Auth/Login");
}
```

### Fix 33 — Production config placeholder + HMAC comment
**File:** `appsettings.Production.json`
**Fix:**
1. Replace `"TokenConsumerUrl": "https://7108dev.d8200.mil/Auth/GriffinCallback"` with `"TokenConsumerUrl": "https://your-server.example/Auth/GriffinCallback"`
2. Replace `"Email": "admin@d8200.mil"` with `"Email": "admin@your-domain.example"`
3. Improve the `ApiKeyHmacSecret` comment. Change from `"CHANGE-THIS-TO-A-RANDOM-64-CHAR-STRING"` to include generation instructions:
```json
"ApiKeyHmacSecret": "CHANGE-THIS-TO-A-RANDOM-SECRET",
"_ApiKeyHmacSecretHelp": "Generate with PowerShell: [Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Maximum 256 }) -as [byte[]]). Must be unique per deployment. Used to hash API keys at rest.",
```

### Fix 34 — Fire-and-forget scoped service in Signup
**File:** `Pages/Auth/Signup.cshtml.cs:311-326`
**Fix:** Replace the `Task.Run` that captures scoped `_notificationService` with a pattern using `IServiceScopeFactory`:
```csharp
var serviceScopeFactory = HttpContext.RequestServices.GetRequiredService<IServiceScopeFactory>();
_ = Task.Run(async () =>
{
    using var scope = serviceScopeFactory.CreateScope();
    try
    {
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
        await notificationService.NotifyOwnersOfNewJoinRequestAsync(...);
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<SignupModel>>();
        logger.LogError(ex, "Failed to send owner notification for join request");
    }
});
```
Preserve the original parameters passed to the notification method.
**Also:** After implementation, we must test that join request notifications actually arrive (this has never been verified).

### Fix 35 — Delete dead EnsureHomeInstanceAsync
**File:** `Services/HomeTypeService.cs:488-508`
**Fix:** Delete the entire `EnsureHomeInstanceAsync` method. Verify no callers exist (already confirmed — zero references).

---

## Agent 4: Localization & .resx

**Owns:** `SharedResources.resx`, `SharedResources.he-IL.resx`, `Calendar/Table.cshtml.cs`, `Auth/Login.cshtml` (lockout strings only — AFTER Agent 5 finishes Figma removal), `Admin/HomeTypes/Index.cshtml`, `Admin/AuditLog.cshtml`

**IMPORTANT:** For all Hebrew translations, present the complete list to the user for review BEFORE writing to .resx files. The user will correct any translations that need adjustment.

### Fix 25 — Localize calendar CRUD error messages
**File:** `Pages/Calendar/Table.cshtml.cs` (all hardcoded English in `JsonResult` responses)
**Approach:**
1. Read the entire `Table.cshtml.cs` file
2. Find every instance of `error = "..."` or `message = "..."` in `JsonResult` returns
3. For each unique string:
   a. Create a descriptive .resx key (e.g., `Calendar_Error_ShiftNotFound`, `Calendar_Error_EmployeeNotFound`, `Calendar_Confirm_RemoveEmployees`)
   b. Set the English value to the current hardcoded string
   c. Write a Hebrew translation
   d. Replace the hardcoded string with `_localizer["KeyName"].Value`
4. Verify `IStringLocalizer<SharedResources>` is already injected (it should be via `_localizer`)

**Key naming convention:** `Calendar_Error_*` for errors, `Calendar_Confirm_*` for confirmation prompts, `Calendar_Success_*` for success messages, `Calendar_Info_*` for informational messages.

### Fix 26 — Move lockout countdown strings to .resx
**File:** `Pages/Auth/Login.cshtml:250-262`
**Approach:**
1. Find the hardcoded Hebrew and English strings in the countdown JavaScript
2. For each string, create a .resx key
3. Replace the inline `isHebrew ? 'Hebrew' : 'English'` pattern with `window.AppLocalizer?.KeyName || 'English fallback'`
4. Server-render the AppLocalizer values into the page (check how other pages do this — likely via a `<script>` block that populates `window.AppLocalizer`)

### Fix 28 — Localize HomeTypes confirm dialog
**File:** `Pages/Admin/HomeTypes/Index.cshtml:90`
**Fix:** Replace `return confirm('Delete this home type?')` with the XSS-safe pattern: `return confirm(@Html.Raw(Json.Serialize(Localizer["Confirm_DeleteHomeType"].Value)))`. This is consistent with the pattern Fix 27 establishes (do NOT use `.Value.Trim()` in JS — that's the exact vulnerability we're fixing). Add the key to both .resx files.

### Fix 31 — Localize AuditLog action/entity strings
**File:** `Pages/Admin/AuditLog.cshtml` (display side) + .resx files
**Approach:** Create a display-time mapping rather than changing storage:
1. In the Razor view, wrap the action and entity type display with a localization helper
2. Create .resx keys for each action: `AuditAction_Created`, `AuditAction_Updated`, `AuditAction_Deleted`, `AuditAction_Login`, etc.
3. Create .resx keys for each entity type: `AuditEntity_AppUser`, `AuditEntity_ShiftAssignment`, `AuditEntity_ShiftType`, etc.
4. In the view, replace `@log.Action` with `@Localizer[$"AuditAction_{log.Action}"]` — this dynamically builds the key from the stored English value
5. Add a fallback: if the key doesn't exist, display the raw value (for future actions added without localization)

**Known action values to localize:** Read the AuditLogService to find all action strings used. Common ones: Created, Updated, Deleted, Login, Logout, PasswordChanged, RoleChanged, Approved, Rejected, Locked, Unlocked, BackupCreated, BackupRestored.

**Known entity types to localize:** AppUser, ShiftAssignment, ShiftType, ShiftInstance, Chore, OnDuty, TimeOffRequest, SwapRequest, Company, RoleTemplate, Grant, Config, FeatureFlag, etc.

---

## Agent 5: JavaScript & Auth Views

**Owns:** `Pages/Auth/Login.cshtml` (Figma script removal ONLY), `Pages/Auth/Signup.cshtml`, `Pages/Auth/Logout.cshtml`, `wwwroot/js/home-type-calendar.js`, `wwwroot/js/calendar-realtime.js`

### Fix 1 — Remove Figma scripts
**Files:** `Pages/Auth/Login.cshtml:19-20`, `Pages/Auth/Signup.cshtml:20-21`, `Pages/Auth/Logout.cshtml:17-18`
**Fix:** Delete these 2 lines from each file:
```html
<!-- TEMPORARY: Figma capture script for design export -->
<script src="https://mcp.figma.com/mcp/html-to-design/capture.js" async></script>
```
**IMPORTANT:** Only touch these specific lines. Do NOT modify any other content in these files. Agent 4 will modify Login.cshtml later for lockout strings.

### Fix 27 — Signup XSS fix
**File:** `Pages/Auth/Signup.cshtml`
**Problem:** Lines 356, 446, 456, 468, 479, 489, 499 use `'@Localizer["Key"].Value.Trim()'` in JS strings.
**Fix:** Replace each occurrence with the safe pattern used by Login.cshtml:
```javascript
// Before:
placeholder.textContent = '@Localizer["Auth_SelectRole"].Value.Trim()';

// After:
placeholder.textContent = @Html.Raw(Json.Serialize(Localizer["Auth_SelectRole"].Value));
```
Note: `Json.Serialize` produces a quoted JSON string (e.g., `"Select a role"`), so the outer quotes must be removed. The pattern from Login.cshtml handles this correctly — match it exactly.

**For innerHTML cases** (most occurrences use `<option>` HTML wrapping), concatenate the HTML:
```javascript
// Before:
jobTypeSelect.innerHTML = '<option value="">@Localizer["Auth_SelectJobType"].Value.Trim()</option>';
// After:
jobTypeSelect.innerHTML = '<option value="">' + @Html.Raw(Json.Serialize(Localizer["Auth_SelectJobType"].Value)) + '</option>';
```
Search for ALL instances of `.Value.Trim()` in JS context in this file — there are ~9 total (not just the 7 listed). Replace every one.

### Fix 29 — home-type-calendar.js locale + week order
**File:** `wwwroot/js/home-type-calendar.js`
**Fix:**
1. **Week order:** Change day headers from `['Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa', 'Su']` to start with Sunday: `['Su', 'Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa']`. Also fix the calendar grid generation to start weeks on Sunday.
2. **Locale detection:** Replace hardcoded `'he-IL'` with dynamic locale:
   ```javascript
   const lang = document.documentElement.lang || 'en';
   const locale = lang === 'he' ? 'he-IL' : 'en-US';
   ```
3. **Localize day headers:** Use `Intl.DateTimeFormat` to generate localized day abbreviations:
   ```javascript
   const locale = ...;
   const dayFormatter = new Intl.DateTimeFormat(locale, { weekday: 'short' });
   // Generate Su-Sa headers using actual dates that fall on each day
   ```
4. **Localize selection count:** Replace `"${selectedDates.size} days selected"` with:
   ```javascript
   const daysSelectedText = window.AppLocalizer?.HomeType_DaysSelected || '{0} days selected';
   // Format with count
   ```
   (Agent 4 will need to add the .resx key for this)

### Fix 37 — Clean up interval leak in calendar-realtime.js dispose()
**File:** `wwwroot/js/calendar-realtime.js:536-540`
**Problem:** A `setInterval` runs every 10 seconds to update the elapsed-time display, but is never stored or cleared in `dispose()`.
**Fix:** Store the interval ID in a module-level variable:
```javascript
let elapsedUpdateTimer = null;
// ... where the setInterval is created:
elapsedUpdateTimer = setInterval(() => { ... }, 10000);
```
Then in the `dispose()` function, add:
```javascript
if (elapsedUpdateTimer) { clearInterval(elapsedUpdateTimer); elapsedUpdateTimer = null; }
```
This is trivial to bundle with Fix 30 since both are in the same file.

### Fix 30 — calendar-realtime.js hardcoded RTL/Hebrew
**File:** `wwwroot/js/calendar-realtime.js:469-490`
**Fix:**
1. **Direction:** Replace hardcoded `direction:rtl` with dynamic detection:
   ```javascript
   const dir = document.documentElement.dir || 'ltr';
   indicator.style.direction = dir;
   ```
2. **Status text:** Replace hardcoded Hebrew strings with `window.AppLocalizer` lookups:
   ```javascript
   // Line ~481 (disconnected message):
   const disconnectedMsg = window.AppLocalizer?.Realtime_Disconnected || 'Connection lost. Retrying...';

   // Line ~488 (reconnected message):
   const reconnectedMsg = window.AppLocalizer?.Realtime_Reconnected || 'Connection restored.';
   ```
   (Agent 4 will need to add the .resx keys for these)

---

## Cross-Agent Coordination

### .resx keys needed from other agents
Agent 4 owns the .resx files. Other agents may discover they need new keys. List of known cross-agent key needs:

| Key | English | Hebrew (draft) | Requested by |
|-----|---------|----------------|-------------|
| `Nav_Notifications` | Notifications | התראות | Agent 2 (fix 17) |
| `HomeType_DaysSelected` | {0} days selected | {0} ימים נבחרו | Agent 5 (fix 29) |
| `Realtime_Disconnected` | Connection lost. Retrying... | החיבור נותק. מנסה שוב... | Agent 5 (fix 30) |
| `Realtime_Reconnected` | Connection restored. | החיבור חזר. | Agent 5 (fix 30) |
| `Confirm_DeleteHomeType` | Are you sure you want to delete this home type? | האם למחוק סוג בית זה? | Agent 4 (fix 28) |

Agent 4 should add these keys alongside its own localization work.

### Ordering dependency
Agent 5 MUST complete fix 1 (Figma script removal from Login.cshtml) BEFORE Agent 4 starts fix 26 (lockout string localization in Login.cshtml).

**Mechanism:** Launch Agent 5 first (or in parallel with Agents 1, 2, 3). Agent 5 finishes quickly (~15 min for fix 1). Then launch Agent 4 (or instruct Agent 4 to start with fixes 25, 28, 31 first, leaving fix 26 for last — by which time Agent 5 will be done).

### Build verification
After ALL agents complete, run `dotnet build` to verify the combined changes compile. If build fails, identify which agent's changes caused the issue.

### Rollback plan
If an agent's changes cause build failures: use `git diff -- <file>` to identify the problematic edit, then `git checkout -- <file>` to revert just that file. Each agent's changes are independent at the file level, so reverting one agent's work does not affect others.

---

## Post-Agent Verification Checklist

After all agents complete:
1. `dotnet build` — must pass with 0 errors
2. `dotnet test` — must pass 339/339
3. Visual verification (browser):
   - z-index tooltip (fix 22) — hover calendar assignments, verify tooltip appears above everything
   - Lucide icons (fix 23) — verify icons render correctly in sidebar, collapsed and expanded
   - Dark mode — verify bottom-sheet, sidebar logout, avatar, focus ring, tooltips all look correct
   - RTL — verify auth page form animation, calendar-realtime banner direction
   - Notifications — trigger a join request and verify owner receives notification (fix 34)
4. Hebrew translation review — user reviews all new .resx keys
5. Before/after screenshots for z-index and icon changes
