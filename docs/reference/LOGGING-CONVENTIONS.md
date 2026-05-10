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
| 7000–7099      | `Services/GriffinConfigService.cs`       | ✅ In use (24 methods cover 38 call sites) |
| 8000–8099      | `Pages/My/Requests.cshtml.cs`            | ✅ In use (35 methods cover 38 call sites) |
| 9000–9099      | Middleware (`ApiAuthenticationMiddleware`, `ApiRequestLoggingMiddleware`, `RateLimitingMiddleware`) | Pending |
| 12000–12099    | `Pages/Admin/Users.cshtml.cs`            | ✅ In use (43 methods cover 60 call sites) |
| 13000–13099    | `Controllers/Api/V1/ChoresController.cs` | ✅ In use (19 methods cover 35 call sites) |
| 14000–14099    | `Controllers/Api/V1/OnDutyController.cs` | ✅ In use (20 methods cover 35 call sites) |
| 15000–15099    | `Controllers/Api/V1/UsersController.cs`  | ✅ In use (16 methods cover 26 call sites) |
| 16000–16099    | `Controllers/Api/V1/FeedbackController.cs` | ✅ In use (17 methods cover 32 call sites) |
| 17000–17099    | `Services/GriffinService.cs`             | ✅ In use (18 methods cover 26 call sites) |
| 18000–18099    | `Services/DatabaseBackupService.cs`      | ✅ In use (26 methods cover 26 call sites) |
| 90000–99999    | `Program.cs` startup banners             | ✅ In use (75 methods cover 80 call sites — 90000-91210 used, 88% headroom remaining) |

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

### `Services/GriffinConfigService.cs` — 7000–7099

Service-class migration (Pattern A primary, with one Pattern C reuse for save header).
24 methods cover 38 call sites; the Created/Updated branch of `SaveGriffinConfigAsync`
shares 6 per-field `[LoggerMessage]` declarations across both branches.

| Sub-range | Section                                  | Methods | Sites | Description |
|-----------|------------------------------------------|---------|-------|-------------|
| 7000–7004 | `Get*GriffinConfigAsync`                 | 5       | 5     | Lookup hit/miss + fallback |
| 7010–7016 | DB-config dump (Debug)                   | 7       | 7     | One method per field, debug-level |
| 7020–7025 | Appsettings fallback dump (Debug)        | 6       | 6     | One method per field, debug-level |
| 7030–7037 | `SaveGriffinConfigAsync` (Information)   | 8       | 15    | Header `{Action}` reuse (Created/Updated × 6 fields), success info |
| 7040–7044 | `TestConnectionAsync`                    | 5       | 5     | Probe lifecycle: started, response, success, failure shapes |

**Note**: Debug-level db-config dump (7011–7016) and Information-level SaveAsync
dump (7031–7036) share *identical message templates* but differing log levels —
source-gen requires separate `[LoggerMessage]` declarations per level.

The full method-by-method mapping lives in `Services/GriffinConfigService.Logging.cs`.

### `Controllers/Api/V1/ChoresController.cs` — 13000–13099

V1 controller — Pattern B reuse via `Endpoint` parameter. 19 methods cover 35 call sites.

| Sub-range   | Section                              | Methods | Sites | Description |
|-------------|--------------------------------------|---------|-------|-------------|
| 13000–13002 | Common API auth/access boilerplate   | 3       | 12    | Endpoint disabled / unauthorized / claim missing |
| 13005–13008 | Catch-block diagnostics              | 4       | 10    | DB+unexpected, with/without ChoreId — `string? CompanyId` from claim |
| 13010–13011 | List endpoint                        | 2       | 2     | Date validation |
| 13012       | Get endpoint                         | 1       | 1     | Not-found warning |
| 13013–13017 | Create endpoint                      | 5       | 5     | Validation, success |
| 13018–13020 | Update endpoint                      | 3       | 3     | Validation, success |
| 13021–13022 | Delete endpoint                      | 2       | 2     | Failed, success |

The full method-by-method mapping lives in `Controllers/Api/V1/ChoresController.Logging.cs`.

### `Controllers/Api/V1/OnDutyController.cs` — 14000–14099

V1 controller — Pattern B reuse. 20 methods cover 35 call sites. Unlike TimeOff/Chores,
on-duty is global (no `CompanyId` logged), so signatures are simpler.

| Sub-range   | Section                              | Methods | Sites | Description |
|-------------|--------------------------------------|---------|-------|-------------|
| 14000–14002 | Common API auth/access boilerplate   | 3       | 12    | Endpoint disabled / unauthorized / claim missing |
| 14005–14008 | Catch-block diagnostics              | 4       | 10    | Path-only (List/Create) + with-OnDutyId (Get/Update/Delete) |
| 14010–14019 | Endpoint-specific events             | 13      | 13    | Validations, not-found per endpoint, success info, list date warnings |

The full method-by-method mapping lives in `Controllers/Api/V1/OnDutyController.Logging.cs`.

### `Controllers/Api/V1/UsersController.cs` — 15000–15099

V1 controller — Pattern B reuse. 16 methods cover 26 call sites.

| Sub-range   | Section                              | Methods | Sites | Description |
|-------------|--------------------------------------|---------|-------|-------------|
| 15000–15002 | Common API auth/access boilerplate   | 3       | 8     | Endpoint disabled / unauthorized / unauthorized-with-userid |
| 15005–15008 | Catch-block diagnostics              | 4       | 8     | DB+unexpected, with/without UserId |
| 15010–15012 | Validation errors                    | 3       | 3     | Email/DisplayName/Role required (one method, 3 sites) — actually a single `LogValidationError` method covering 3 distinct call sites |
| 15015–15017 | Endpoint-specific not-found / success | 6      | 7     | UserNotFound (Get) + UserNotFoundForUpdate + success |

**Note**: 15003 and 15013–15014 left intentionally unused as gaps between sub-range
boundaries; sequential method declaration was prioritized over filling every slot.

The full method-by-method mapping lives in `Controllers/Api/V1/UsersController.Logging.cs`.

### `Controllers/Api/V1/FeedbackController.cs` — 16000–16099

V1 controller — Pattern B reuse. 17 methods cover 32 call sites (47% reuse).

| Sub-range   | Section                              | Methods | Sites | Description |
|-------------|--------------------------------------|---------|-------|-------------|
| 16000–16002 | Common API auth/access               | 3       | 12    | Endpoint disabled (×5) / unauthorized (×5) / claim missing (×2) |
| 16005–16008 | Catch-block diagnostics              | 4       | 10    | DB+unexpected, with/without FeedbackId |
| 16010–16012 | Get + Delete distinct sites          | 3       | 3     | Not-found, ownership warnings |
| 16015–16018 | Create distinct sites                | 4       | 4     | Validations, success |
| 16020–16022 | UpdateStatus distinct sites          | 3       | 3     | Validation, success, status-change |

The full method-by-method mapping lives in `Controllers/Api/V1/FeedbackController.Logging.cs`.

### `Services/GriffinService.cs` — 17000–17099

Service class — Pattern A primary, with one Pattern C reuse (`{Stage}` placeholder
consolidates 3 sites of "Griffin {Stage} call failed [{ErrorToken}]: {Detail}"
across token-exchange / token-validation / getClaims). 18 methods cover 26 call sites.

| Sub-range   | Section                              | Methods | Sites | Description |
|-------------|--------------------------------------|---------|-------|-------------|
| 17000–17003 | `BuildAuthenticationUrl`             | 4       | 4     | Probe lifecycle |
| 17010–17012 | `ExchangeTokenAsync`                 | 3       | 3     | Lifecycle |
| 17020–17021 | `ValidateTokenAsync`                 | 2       | 2     | Lifecycle |
| 17030–17034 | `GetClaimsAsync`                     | 5       | 5     | Lifecycle |
| 17040–17041 | `ValidateAndGetClaimsAsync` cache    | 2       | 2     | Cache hit / miss |
| 17050–17052 | `AuthenticateUserAsync`              | 3       | 3     | Login lifecycle |
| 17060–17062 | `AutoProvisionUserAsync`             | 3       | 3     | Provision lifecycle (`UserRole` enum logged here — needs `using ShiftManager.Models.Support`) |
| 17070       | `CallGriffinGetAsync` unhandled      | 1       | 1     | Unexpected exception |
| 17080       | Pattern C reuse                      | 1       | 3     | `LogStageCallFailed(_logger, "<stage>", ...)` |

**Critical signature note**: The `UserRole` parameter on `LogAutoProvisioned` must
be the enum from `ShiftManager.Models.Support` (NOT `ShiftManager.Models`). This
caused an integration-build error that was caught and fixed.

The full method-by-method mapping lives in `Services/GriffinService.Logging.cs`.

### `Services/DatabaseBackupService.cs` — 18000–18099

Service class — pure Pattern A. 26 methods cover 26 call sites (1.00× ratio — every
event is semantically distinct, no reuse opportunity).

| Sub-range   | Section                              | Methods | Sites | Description |
|-------------|--------------------------------------|---------|-------|-------------|
| 18000–18004 | Service lifecycle                    | 5       | 5     | Started/disabled/skipped |
| 18010–18018 | Backup creation                      | 9       | 9     | Per-step lifecycle + DB/file/disk-full errors |
| 18020–18024 | Cleanup of old backups               | 5       | 5     | Retention policy lifecycle + cleanup-on-failure |
| 18030–18031 | Restore validation                   | 2       | 2     | Pre-restore checks |
| 18040–18043 | Statistics                           | 4       | 4     | Disk usage / count / size summary |
| 18050       | Misc                                 | 1       | 1     | Unscheduled diagnostic |

**Note on format specifiers**: `{SizeKB:F1}` and `{Hours:F1}` are valid in source-gen
templates — preserved verbatim from the original `_logger.Log*` calls.

The full method-by-method mapping lives in `Services/DatabaseBackupService.Logging.cs`.

### `Program.cs` — 90000–99999

Startup-banner migration. 75 methods cover 80 call sites with 1 Pattern C reuse method.
Used 90000–91210 (12% of the reserved 10,000-ID range; 88% headroom for future startup
events). Three local `ILogger<Program>` variables (`logger`, `startupLogger`,
`featureFlagLogger`) all share the same partial methods — call sites pick whichever
local is in scope.

| Sub-range   | Section                                              | Methods | Sites | Description |
|-------------|------------------------------------------------------|---------|-------|-------------|
| 90000–90099 | DB migration / pre-migration backup / DB directory   | 7       | 8     | Phase 2 restore, backup creation, directory setup, migration failure |
| 90100–90199 | SQLite WAL / busy_timeout / version PRAGMAs          | 4       | 4     | journal_mode, busy_timeout, version, WAL config failure |
| 90200–90299 | Grant type seeding                                   | 1       | 1     | New grants seeded |
| 90300–90399 | Role template seed/rename/sync/orphan cleanup        | 11      | 13    | Includes 1 Pattern C reuse (`LogOrphanTemplateRemapped` with `{Entity}` placeholder, 5 sites) |
| 90400–90499 | Grant mappings + sentinel resolution                 | 4       | 4     | New mappings, reconciliation |
| 90500–90599 | Catch-up (System molecule, SystemAdmins, HQ)         | 4       | 4     | One-shot org backfills |
| 90600–90699 | Additional molecules / companies / departments seed  | 10      | 10    | Demo / dev seeds |
| 90700–90799 | Owner / config seed                                  | 2       | 2     | Owner password / global config |
| 90800–90899 | Feature flags + test data + grant repair             | 9       | 9     | Demo populations |
| 90900–90999 | Role-scope migration / Home unification backfills    | 8       | 8     | One-time data migrations |
| 91000–91099 | Startup lifecycle / safety checks / banner           | 10      | 10    | Application started, safety guards, environment banner |
| 91100–91199 | Feature flag startup logging                         | 2       | 2     | Banner-time feature-flag dump |
| 91200–91299 | Owner password warning + API auth exception          | 3       | 3     | Default-password warning, ApiAuth catch |

**Critical structural note**: `public partial class Program { }` is declared at the
**bottom of `Program.cs`** (load-bearing for `WebApplicationFactory<Program>` in
integration tests) — DO NOT remove. The new `Program.Logging.cs` mirrors the same
partial declaration, allowing source-gen to merge the methods.

**Special signature**:
- `MoleculeType` enum logged at line 1112 → requires `using ShiftManager.Models.Support;`
- `PathString` for `context.Request.Path` at line 1916 → requires `using Microsoft.AspNetCore.Http;`
- `int? templateId` for `user.RoleTemplateId` at line 1471 — preserved as nullable
- Two sites use `object?` for `ExecuteScalarAsync()` results (lines 568 + 581) — preserved as-is

**Open verification item (2026-05-10)**: Phase 16 source-level work is complete and verified
by grep (0 remaining `logger.Log*` calls in Program.cs, 75 partial methods declared). The
integration build's link/copy step was blocked by a running `ShiftManager.exe` (PID 138844),
so the final no-incremental clean build is pending. Compile-phase numbers from the partial
build: 0 CS errors, 1942 unique warnings (−26 net from 1968), CA1848 unique 1104 (−80
exactly matching site count). The 54-warning gap may indicate ~28 new warnings from the
new partial methods (likely CA1822 / CA2007 — investigate next session).

The full method-by-method mapping lives in `Program.Logging.cs`.

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
