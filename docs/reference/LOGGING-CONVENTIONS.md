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
| 2000–2099      | `Services/NotificationService.cs`        | Pending (Phase 2) |
| 3000–3099      | `Controllers/Api/V1/SwapRequestsController.cs` | Pending |
| 4000–4099      | `Pages/Calendar/Table.cshtml.cs`         | Pending |
| 5000–5099      | `Services/MailService.cs`                | Pending |
| 6000–6099      | `Pages/Admin/Users.cshtml.cs`            | Pending |
| 7000–7099      | `Services/GriffinService.cs` + `GriffinConfigService.cs` | Pending |
| 8000–8099      | `Pages/My/Requests.cshtml.cs`            | Pending |
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
That file has the canonical `[LoggerMessage(EventId = ...)]` declarations.

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
