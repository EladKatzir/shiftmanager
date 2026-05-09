# Logging Conventions

ShiftManager is migrating from `LoggerExtensions.LogXxx(...)` calls to compile-time
source-generated `[LoggerMessage]` partial methods (closes F-C-013 from the 2026-04-28
diagnostic review). This document is the **single source of truth** for `EventId`
allocation across the codebase, so each new partial method gets a globally-unique ID.

## Why source-generated logging?

`_logger.LogInformation("template {Foo}", foo)` allocates a `params object?[]` on
every call, boxes value-type arguments, and re-parses the template string. The
source-generated equivalent emits a strongly-typed delegate at compile time —
zero allocation when the log level is filtered out, and minimal allocation when
it isn't. On hot paths (auth, request middleware, notification fan-out) the win
is measurable.

## EventId allocation table

`EventId` is a 32-bit integer that uniquely identifies a log event across the
process. Reserve a contiguous range per file/component so future partial methods
in the same file don't collide with sibling files.

| Range          | Component                                | Status |
|----------------|------------------------------------------|--------|
| 1000–1099      | `Pages/Auth/Login.cshtml.cs`             | ✅ In use (43 of 100 IDs allocated) |
| 1100–1199      | *(reserved — Auth/Signup, Auth/GriffinSignup, Auth/ForgotPassword)* | Pending |
| 1200–1299      | *(reserved — Auth callbacks, e.g. GriffinCallback)* | Pending |
| 2000–2099      | `Services/NotificationService.cs`        | ✅ In use (39 of 100 IDs allocated) |
| 3000–3099      | `Controllers/Api/V1/SwapRequestsController.cs` | ✅ In use (21 methods cover 42 call sites) |
| 4000–4099      | `Pages/Calendar/Table.cshtml.cs`         | ✅ In use (14 methods cover 39 call sites) |
| 5000–5099      | `Pages/Requests/Index.cshtml.cs`         | ✅ In use (28 methods cover 42 call sites) |
| 5500–5599      | `Services/MailService.cs`                | Pending (Batch O scope — defer until god-class decomp begins) |
| 6000–6099      | `Controllers/Api/V1/TimeOffController.cs`| ✅ In use (25 methods cover 37 call sites) |
| 12000–12099    | `Pages/Admin/Users.cshtml.cs`            | ✅ In use (43 methods cover 60 call sites) |
| 7000–7099      | `Services/GriffinService.cs` + `GriffinConfigService.cs` | Pending |
| 8000–8099      | `Pages/My/Requests.cshtml.cs`            | ✅ In use (35 methods cover 38 call sites) |
| 9000–9099      | Middleware (`ApiAuthenticationMiddleware`, `ApiRequestLoggingMiddleware`, `RateLimitingMiddleware`) | Pending |
| 90000–99999    | `Program.cs` startup banners             | Pending (last — startup-only events, low value) |

When adding a new file to the migration: pick the next free 100-ID block,
update this table in the same PR, and use IDs from that block exclusively.

## Detailed sub-ranges (allocated)

### `Pages/Auth/Login.cshtml.cs` — 1000–1099

| Sub-range | Handler             | Used | Description                          |
|-----------|---------------------|------|--------------------------------------|
| 1000–1009 | `OnGetAsync`        | 4    | Griffin probe lifecycle              |
| 1010–1029 | `OnPostAsync`       | 13   | Local-credential auth flow           |
| 1030–1099 | `OnPostGriffinAsync`| 26   | ADFS redirect path (verbose debug)   |

The full method-by-method mapping lives in `Pages/Auth/Login.cshtml.Logging.cs`.

### `Services/NotificationService.cs` — 2000–2099

| Sub-range | Section                                   | Used | Description                          |
|-----------|-------------------------------------------|------|--------------------------------------|
| 2000–2009 | `CreateNotificationAsync` core            | 4    | Recipient lookup, success, DB/unexpected catches |
| 2010–2019 | Email fan-out errors per notification type| 9    | Shift/chore/on-duty/time-off/swap/access email failures |
| 2020–2029 | Access request notifications              | 4    | Owner lookup, fan-out, success summary |
| 2030–2039 | Daily digest                              | 8    | Per-user dispatch lifecycle, success/fail/error |
| 2040–2049 | Day-before reminders                      | 6    | Same lifecycle as digest, distinct event family |
| 2050–2059 | Ops Console scheduler email failures      | 3    | Trainee added / slot removed / shift modified |
| 2060–2069 | TryCreate diagnostic helpers              | 5    | Admin-only structured-result wrappers |

The full method-by-method mapping lives in `Services/NotificationService.Logging.cs`.

### `Controllers/Api/V1/SwapRequestsController.cs` — 3000–3099

This controller demonstrates **method reuse across endpoints** — 21 unique partial
methods cover 42 call sites because the same boilerplate event (e.g. "API endpoint
not enabled") fires from each of 6 endpoints, distinguished only by the `Endpoint`
parameter. Reuse is correct here: events with identical message templates *are*
the same event, just from different actions.

| Sub-range | Section                                   | Methods | Sites | Description                          |
|-----------|-------------------------------------------|---------|-------|--------------------------------------|
| 3000–3009 | Common API auth/access boilerplate        | 3       | 16    | Endpoint disabled / unauthorized / userid missing |
| 3010–3019 | Date-parsing validation                   | 2       | 2     | List endpoint only                   |
| 3020–3029 | Catch-block diagnostics                   | 4       | 12    | DB/unexpected, with/without RequestId — string? CompanyId from claim |
| 3030–3039 | Get endpoint                              | 1       | 1     | Not-found warning                    |
| 3040–3049 | Create endpoint                           | 3       | 3     | Validation, success                  |
| 3050–3059 | Approve endpoint                          | 3       | 3     | Not-found, validation, success       |
| 3060–3069 | Decline endpoint                          | 3       | 3     | Not-found, validation, success       |
| 3070–3079 | Delete endpoint                           | 2       | 2     | Failed, success                      |

Note the `string? CompanyId` in 3020-3029: catch blocks log the **raw claim string**
(`User.FindFirst("CompanyId")?.Value`) because the parsed `int` may not be in scope.
Success paths use the parsed `int CompanyId`. Same template literal text, distinct
method overloads — source-gen requires concrete signatures.

The full method-by-method mapping lives in `Controllers/Api/V1/SwapRequestsController.Logging.cs`.

### `Pages/Calendar/Table.cshtml.cs` — 4000–4099

A second example of **template parameterization** for repeated patterns: 28 of
the 39 call sites collapse into 3 partial methods because they share template
shape, with the action name extracted as an `{Action}` placeholder:

| Sub-range | Section                                   | Methods | Sites | Description                          |
|-----------|-------------------------------------------|---------|-------|--------------------------------------|
| 4000–4009 | Page lifecycle                            | 4       | 4     | OnGet date parsing + initial load    |
| 4010–4019 | Action error catches                      | 2       | 18    | `LogErrorAction(ex, "<verb>")` + `LogErrorActionInstance(ex, "<verb>", id)` |
| 4020–4029 | Calendar notification dispatch failures   | 1       | 10    | `LogCalendarNotificationFailed(ex, "<Action>")` |
| 4030–4039 | Information logs                          | 6       | 6     | Each unique (Auto-detached, Deleted, Created custom shift type, Detached, Reset, Fill range completed) |
| 4040–4049 | Misc validation                           | 1       | 1     | Invalid target date                  |

**Behavioral note**: parameterizing the action name into `{Action}` produces
*structured-log property* `Action` rather than embedding the verb in the
literal template. Console output is identical; structured-log consumers
(if/when added) gain a filterable property.

The full method-by-method mapping lives in `Pages/Calendar/Table.cshtml.Logging.cs`.

### `Pages/Requests/Index.cshtml.cs` — 5000–5099

A third example of **template parameterization**, now applied to a Cartesian
product. Approve/Decline × TimeOff/Swap produced 4 distinct SECURITY literals
and 4 distinct CONCURRENCY literals (8 unique templates). Parameterizing
both the action verb (`{Action}`) and entity type (`{EntityType}`) collapses
those 8 templates into 2 partial methods covering all 8 call sites:

```csharp
LogSecurityUnauthorizedAction(_logger, userId, role, "approve", "swap", id, companyId);
LogConcurrencyAlreadyProcessed(_logger, userId, "decline", "time off", id, status);
```

Combined with reused validation literals ("Invalid X request ID", "X request
not found", "Invalid or missing NameIdentifier claim"), 42 call sites
collapse to 28 methods.

| Sub-range | Section                              | Methods | Sites | Description |
|-----------|--------------------------------------|---------|-------|-------------|
| 5000–5019 | Page lifecycle                       | 11      | 11    | OnGet flow with separate paths for employee vs admin |
| 5020–5029 | Common validation/auth errors        | 5       | 14    | TimeOff invalid+not-found, Swap invalid+not-found, claim missing (×5) |
| 5030–5039 | Authz / concurrency / manager grant  | 4       | 9     | Parameterized SECURITY+CONCURRENCY (×4 each), side-effects, manager grant |
| 5040–5049 | Delete approved time-off flow        | 8       | 8     | Each unique (Admin attempt, not-found, security, non-approved, started, deleting, success, error) |

The full method-by-method mapping lives in `Pages/Requests/Index.cshtml.Logging.cs`.

### `Pages/Admin/Users.cshtml.cs` — 12000–12099

Largest single-file PR in Batch K. 60 call sites collapse to 43 partial methods (~17
sites of reuse savings) using a mix of Pattern A (one-method-per-site) for distinct
events and Pattern C (template parameterization) for repeated boilerplate:

- **Pattern C — bare-vs-suffixed claim error**: 10 sites of `"Invalid or missing
  NameIdentifier claim"` collapse to 1 method; 3 suffixed variants
  (user-creation audit, password-reset auth, password-reset audit) stay distinct.
- **Pattern C — `{Action}` parameterization**: 6 unauthorized-action warnings
  across handlers (toggle, role change, job-type change, password reset, unlock,
  delete) collapse to 1 method; the verb is a structured-log property.
- **Pattern A — identical literal, different vars**: 3 `RoleTemplate ... missing
  DerivedUserRole` sites all share one method despite differing argument names
  (`selectedTemplate`, `batchTemplate`, `importTemplate`).

| Sub-range   | Section                                       | Methods | Sites | Description |
|-------------|-----------------------------------------------|---------|-------|-------------|
| 12000–12009 | Cross-cutting / common errors                 | 7       | 24    | Claim/User-not-found/Role-template-missing/Unauthorized-action |
| 12010–12019 | Create flow (`OnPostCreateAsync`)             | 2       | 2     | Grants assigned, DirectorCompany mapping |
| 12020–12029 | Toggle (`OnPostToggleAsync`)                  | 3       | 3     | Reactivation re-provisioning lifecycle |
| 12030–12039 | Role change (`OnPostRoleAsync`)               | 4       | 4     | Shadowing cancel, grants swap, promote-to-Director |
| 12040–12049 | PrimaryShiftType (`OnPostPrimaryShiftTypeAsync`) | 3    | 3     | Grant check, cross-molecule, eligible-companies |
| 12050–12059 | Unlock account (`OnPostUnlockAccountAsync`)   | 1       | 1     | Successful unlock |
| 12060–12079 | Delete user (`OnPostDeleteUserAsync`)         | 12      | 12    | Self-delete attempt, not-found, lifecycle, FK constraint, errors |
| 12080–12089 | Join request approve / reject (single+batch)  | 9       | 9     | Per-request + security audits + batch summary |
| 12090–12099 | CSV export + bulk import                      | 2       | 2     | Export error, bulk-import summary |

**Behavioral note on Pattern C**: parameterizing `{Action}` produces a structured-log
property `Action` instead of embedding the verb in the literal template. Console
output is identical; consumers (if/when added) gain a filterable property without
breaking existing queries.

The full method-by-method mapping lives in `Pages/Admin/Users.cshtml.Logging.cs`.

## Pattern — adding a new partial method

```csharp
// Pages/Auth/SomePage.cshtml.Logging.cs
using Microsoft.Extensions.Logging;

namespace ShiftManager.Pages.Auth;

public partial class SomePageModel
{
    [LoggerMessage(
        EventId = 1100,                // Pick next free ID from your file's range
        Level = LogLevel.Information,
        Message = "User {UserId} did {Action} at {Timestamp}")]
    private static partial void LogUserAction(
        ILogger logger,
        int userId,
        string action,
        DateTime timestamp);
}
```

Then at the call site:

```csharp
// Before
_logger.LogInformation("User {UserId} did {Action} at {Timestamp}", id, action, ts);

// After
LogUserAction(_logger, id, action, ts);
```

## Method-naming conventions

- **`Log` prefix**: makes call sites grep-able as a group; mirrors the standard
  `_logger.LogXxx(...)` shape.
- **Verb-then-subject**: `LogSignedInSuccessfully`, not `LogUserSignIn`. Match
  the message's intent ("X happened to Y") rather than the method's parameter list.
- **Disambiguate sibling messages**: when two log sites have similar templates,
  add a distinguishing suffix (`LogAccountAlreadyLocked` vs
  `LogAccountLockedAfterAttempts`). Don't reuse method names across files —
  partial methods are scoped to their containing class but the EventId is
  process-global, so name collisions are confusing in tooling.

## Exception parameter placement

For `LogError(ex, "template", args)` migrations, place `Exception` immediately
after `ILogger` in the partial-method signature. The source generator detects
it positionally and routes it to the underlying `ILogger.Log(...)` exception slot:

```csharp
[LoggerMessage(EventId = 1022, Level = LogLevel.Error,
    Message = "Unhandled exception during login for {Email}")]
private static partial void LogUnhandledLoginException(
    ILogger logger,
    System.Exception ex,            // ← positional, comes right after logger
    string email);
```

## Verifying the migration

After converting a file, run:

```powershell
dotnet build ShiftManager.csproj -c Debug -v minimal -nologo --no-incremental 2>&1 |
  Select-String "warning CA1848" |
  Select-String "YourFile.cs"
```

The expected count is **0** when the migration is complete. The total project-wide
CA1848 count should also drop by the number of sites converted (note: each site
emits twice in the build log — once from the live analyzer, once from the compile
analyzer — but counts as one in the `Warning(s)` summary).

## See also

- `Directory.Build.props` — analyzer infrastructure that turns CA1848 on
- `.claude-reviews/2026-04-28/00-index.md` §K — Batch K execution log (per-file PRs)
- F-C-013 in `01-compiler.md` — original finding (1,720 sites estimated)
