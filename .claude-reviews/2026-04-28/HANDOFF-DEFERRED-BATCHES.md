# Handoff Prompt — Deferred Batches from 2026-04-28 Diagnostic Review

**Use this when continuing the multi-batch fix campaign in a fresh session.**

This is a self-contained continuation prompt. Don't read the prior conversation
history — everything you need is here. The audit-week sprint already shipped
6 batches and verified 5 as invalid; this prompt covers the 9 batches that
remain genuinely deferred (real findings, oversized for inline batch execution).

---

## 0. Context Pre-Brief

### What this is

ShiftManager is an air-gapped ASP.NET Core 8.0 + SQLite + Razor Pages app
deployed on Windows/IIS for a security workforce management product. In late
April 2026 a 5-lens diagnostic review surfaced 88 findings catalogued in
`.claude-reviews/2026-04-28/00-index.md`. During the week of 2026-05-05 a
batch-execution sprint shipped 6 batches and confirmed 5 as factually invalid.

The repo is at `C:\Users\katzi\Downloads\ShiftManager`. Branch `update`. Build
verified clean throughout (0 errors). Production warnings dropped 3,398 → 2,750
(-19%) across the sprint.

### What shipped (do NOT re-do)

| Batch | Closes | Verification |
|---|---|---|
| A | F-C-001 + F-C-007 + F-C-009 + F-C-010 + F-C-018 | Analyzer infra durable; `Directory.Build.props` + `.editorconfig` |
| B | F-C-011 | IconTagHelper printer dict overwrite |
| D | F-H-003 + F-A-002 + F-R-001 | DirectorService full async conversion (5 deadlock sites) |
| F (partial) | F-H-013 (auth-scope) + F-H-004 already-done | Deactivation nulls TraineeUserId + removes DirectorCompany. **Preservative for grants/shifts per user's design call.** Don't change this. |
| H | F-C-002 + F-C-006 + F-D-001 + F-D-015 | All 10 V1 controllers migrated, `ApiProblemDetails` class deleted |
| J (partial) | F-H-009 | `FeatureFlagService` `GetOrCreateAsync` race fix |
| L (substantial) | ~half of F-C-014 culture sites | 30+ files; `string.Format(_localizer[…])` → `string.Format(CultureInfo.CurrentCulture, _localizer[…])` |
| M | F-C-012 | MD5 → SHA256 in CacheHelper |
| S (partial) | F-H-014 | ImportService NDJSON parse-error visibility (Path + BytePosition + inner) |
| T (partial) | F-R-007 + F-A-019 | DailyNotificationJob lock helpers + Program.cs config-precedence comment |

### What's verified-invalid (do NOT investigate again)

The original audit had a notable false-positive rate in 3 of 5 lenses. These
findings are NOT bugs — recorded so you don't re-audit:

- **F-A-001 / F-A-014 / F-A-015** (Batch C) — SignalR Hubs are transient
  per-method-call, not Singleton. EF interceptor pattern is docs-recommended.
  `IHubContext<T>` is Singleton.
- **F-H-001 / F-H-002 / F-H-006 / F-H-016** (Batch E) — Null guards already
  present at cited lines. `c.Molecule!.Area!.ProjectId` inside LINQ-to-SQL
  expressions doesn't NRE at runtime — EF translates to LEFT JOIN. Pattern
  was likely fixed pre-audit by commit `195aacc`.
- **F-H-010** (Batch G CSRF) — `services.AddRazorPages()` auto-validates
  antiforgery on every POST by default. Codebase has 60+ `[IgnoreAntiforgeryToken]`
  attrs to opt OUT — proves the default is on.
- **F-R-002 / F-R-008** (Batch P readability) — `Console.WriteLine` in
  `DeploymentExportService` runs pre-`builder.Build()`, no DI yet. Comment
  in `Pages/Owner/Backup.cshtml.cs:42` is deliberate breadcrumbing.

### Source-of-truth artifacts

- `.claude-reviews/2026-04-28/00-index.md` — main triage index, top-20, batches A-T, ship order, **§K Execution Log** (live status), **§M Calibration Notes**
- `.claude-reviews/2026-04-28/01-compiler.md` through `05-hidden-bugs.md` — original 88-finding reports
- `.claude-reviews/2026-04-28/_raw/dotnet-build-analyzers.log` — 1.4 MB analyzer baseline
- `~/.claude/projects/C--Users-katzi-Downloads-ShiftManager/memory/reference_diagnostic_review_2026_04_28.md` — memory pointer summarising shipped/deferred

---

## 1. Critical Conventions (Reuse — Don't Redesign)

### Dedup chain (was applied during triage; some findings still need it)

When a finding shows up in multiple lenses, the canonical owner is the
highest-priority lens:

```
HiddenBugs > Compiler > Architecture > Duplication > Readability
```

**BUT** — the sprint discovered the chain can promote a *less-accurate* analysis
to canonical when the higher-priority lens has factual errors. Always verify
the technical claim before treating any finding as authoritative. Cross-check
lifetime/lifecycle claims against Microsoft Docs. Cross-check null-safety
claims against EF Core's LINQ-to-SQL semantics.

### Finding ID convention

`F-[CADRH]-NNN` — C=Compiler, A=Architecture, D=Duplication, R=Readability,
H=HiddenBugs. Cite IDs verbatim in commit messages: `Closes F-X-NNN`.

### Severity / effort ladders (from the original schema)

| Severity | Meaning |
|---|---|
| Critical | Auth bypass, data loss, tenant leakage, RCE, build-broken-in-Release. Ship-blocker. |
| High | Latent bug that will fire under realistic load/data; or large architecture violation. |
| Medium | Likely-but-not-certain bug, or maintainability cliff. |
| Low | Style, minor naming, single-site dead code, micro-perf. |
| Info | Worth recording but not actionable on its own. |

| Effort | Meaning |
|---|---|
| Trivial | <30 min, single file. |
| Small | <half-day, 1-3 files. |
| Medium | 1-3 days, multi-file or schema change. |
| Large | >3 days, design change, migration, coordinated rollout. |

### Build-verification protocol

After each batch, run:
```bash
dotnet build ShiftManager.csproj -c Debug -v minimal -nologo
```
Expected: **0 errors**, warnings should *decrease or stay flat* (never grow
unexpectedly — that signals a regression). If running `--no-incremental`,
warning count is in the ~3,000 range. Without it, may show "0 warnings"
because the cached projects are reused — that's fine for verifying the file
you changed compiles.

### CLAUDE.md constraints (still in effect)

Per `~/.claude/CLAUDE.md`:

- **§1**: verify function references before any edit
- **§2**: declare model — Opus 4.7 for cross-cutting work, Sonnet 4.5 for scoped subtasks
- **§3**: before any rebuild, check that the executable isn't locked (`tasklist | grep -E "ShiftManager|dotnet|iisexpress|w3wp"`)
- **§4**: NO workarounds — root cause only. If a finding turns out to be invalid (like Batches C, E, G, parts of P/F), record the verification and DON'T add defensive code.
- **§5**: Surface deferments explicitly with "Deferred Items" sections.
- **NEVER commit** unless the user explicitly asks.

---

## 2. Deferred Batches — Per-Batch Specs

Ordered roughly by impact-per-effort. Each batch is a candidate for its own
focused PR session.

---

### Batch H-leftover — *Already complete, but verify on next build*

**Status**: Should be 100% complete. Listed here as a sanity check.

If you see any `ApiProblemDetails` references in production code (not docs,
not the AuditLogsController docstring), something regressed. Run:

```bash
grep -rn "ApiProblemDetails" /c/Users/katzi/Downloads/ShiftManager --include="*.cs" \
  | grep -v FinalProductPublish | grep -v ProductionReady
```

Expected output: ONE match — the docstring in `AuditLogsController.cs`. Any
other match means a controller was added or a merge re-introduced the obsolete
class. The class file `Models/Api/ProblemDetails.cs` was deleted in commit
`8aa8de8`.

---

### Batch I — Generic `IBackgroundTaskQueue` Infrastructure

**Findings**: F-A-003 (Task.Run fire-and-forget) + F-D-009 (Mail/Notification dispatcher divergence)
**Severity**: High (F-A-003) / Medium (F-D-009)
**Effort**: Medium (2-3 days)

**Problem**: 4 fire-and-forget `_ = Task.Run(...)` sites in middleware/auth/signup
([`Middleware/ApiAuthenticationMiddleware.cs:207`](Middleware/ApiAuthenticationMiddleware.cs),
`Middleware/ApiRequestLoggingMiddleware.cs:82`, `Pages/Auth/Signup.cshtml.cs:325`,
`Pages/Auth/GriffinSignup.cshtml.cs:323`). Each already wraps `try/catch`
inside the lambda but lacks back-pressure and observability. `EmailBackgroundQueue`
exists but is email-specific — not a generic infrastructure piece.

**Recommended approach**:

1. Create `Services/IBackgroundTaskQueue.cs` with shape:
   ```csharp
   public interface IBackgroundTaskQueue
   {
       void Enqueue(Func<IServiceProvider, CancellationToken, Task> work);
       ValueTask<Func<IServiceProvider, CancellationToken, Task>?> DequeueAsync(CancellationToken ct);
       int QueueLength { get; }
   }
   ```
   Implementation backed by `System.Threading.Channels.Channel<>` like the existing
   `Services/EmailBackgroundQueue.cs` (channel size 500, SingleReader=true,
   SingleWriter=false).

2. Create `Services/BackgroundTaskHostedService.cs` (`IHostedService`) that
   dequeues and invokes work with a fresh DI scope per item. Pattern matches
   `Services/EmailBackgroundProcessor.cs`.

3. Register in `Program.cs` near the existing `EmailBackgroundQueue`/`EmailBackgroundProcessor`
   registrations (search for those to find the right block).

4. Migrate the 4 fire-and-forget sites:
   ```csharp
   // Before:
   _ = Task.Run(async () => { /* try/catch wrapper */ ... });
   // After:
   _backgroundTaskQueue.Enqueue(async (sp, ct) => { ... });
   ```

5. Test with a load-test scenario (e.g. 100 simultaneous API key auth requests)
   and verify: queue doesn't overflow, exceptions are logged, processing
   completes within 30s.

**Verification**: zero `_ = Task.Run` matches in `Middleware/` and `Pages/Auth/`
after migration. Existing `Services/EmailBackgroundProcessor.cs:73` `_ = Task.Run`
for retry scheduling can stay (it's domain-specific, not generic).

**Don't merge with `EmailBackgroundQueue`** — that one's strongly-typed for
`QueuedEmail` and adding a generic abstraction layer to it would add ceremony
without benefit. Two queues is fine.

---

### Batch K — LoggerMessage Source-Gen Sweep

**Findings**: F-C-013 (1,720 CA1848 sites)
**Severity**: High
**Effort**: Large (multi-PR — split by file group)

**Problem**: Every `_logger.LogXxx("user {Id} did {Action}", id, action)` boxes
value-types, allocates `params object[]`, runs format-string parsing even when
the log level is filtered. `LoggerMessage.Define<T>(...)` produces a static
delegate that allocates nothing.

**The hot files** (from F-C-013 distribution table):

| File | Sites |
|---|---|
| `Program.cs` | 74 |
| `Pages/Admin/Users.cshtml.cs` | 60 |
| `Pages/Auth/Login.cshtml.cs` | 43 |
| `Controllers/Api/V1/SwapRequestsController.cs` | 42 |
| `Pages/Calendar/Table.cshtml.cs` | 39 |
| `Services/NotificationService.cs` | 39 |
| `Services/GriffinConfigService.cs` | 38 |
| `Pages/Requests/Index.cshtml.cs` | 38 |
| (rest of distribution: ~480 sites in the top 10, ~1,240 in the long tail) |

**Recommended approach** (per PR):

1. Pick ONE file. Convert its class to `partial`.
2. Add `using Microsoft.Extensions.Logging;` (already there in most cases).
3. Add a private static partial method per log site:
   ```csharp
   [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
       Message = "User {UserId} performed {Action} at {Timestamp}")]
   private static partial void LogUserAction(ILogger logger, int userId, string action, DateTime timestamp);
   ```
4. Replace call site:
   ```csharp
   // Before:
   _logger.LogInformation("User {UserId} performed {Action} at {Timestamp}", id, action, DateTime.UtcNow);
   // After:
   LogUserAction(_logger, id, action, DateTime.UtcNow);
   ```
5. Verify build still succeeds; CA1848 count in build log should drop by exactly
   the number of sites you migrated.

**Recommended order**:
- Phase 1 (highest-value, hot-path): `Pages/Auth/Login.cshtml.cs` (43 sites — login is on every authentication round-trip)
- Phase 2: `Services/NotificationService.cs` (39 sites — every push event fans out)
- Phase 3: `Controllers/Api/V1/SwapRequestsController.cs` (42 sites — request endpoint)
- Phase 4: `Pages/Admin/Users.cshtml.cs` (60 sites — large but lower frequency)
- Phase 5: `Program.cs` (74 sites — only fires at startup, low-impact perf)

**Don't try to do all 1,720 in one PR**. Each file is a self-contained PR
with its own build verification.

**Watch out**: `EventId` values must be unique across the codebase to avoid
collision in log aggregators. Easiest convention — use file-prefix range:
`Login.cshtml.cs` uses 1000-1099, `NotificationService.cs` uses 2000-2099, etc.
Document this in a `docs/logging-conventions.md` or in the `Directory.Build.props`
comment.

---

### Batch L — Remaining Culture Sweep

**Findings**: F-C-014 (596 baseline sites; ~half closed in the sprint)
**Severity**: High (Hebrew-RTL bilingual app)
**Effort**: Medium-Large (file-by-file)

**Status after sprint**: ~150 sites closed. Remaining ~400 sites distributed
across services and pages. The pattern fixed so far:
```
string.Format(_localizer[…])  →  string.Format(CultureInfo.CurrentCulture, _localizer[…])
```

**Other CA1305/CA1304/CA1311/CA1310/CA1862 patterns still open**:

1. **`.ToString()` on numbers/dates** (CA1305) — needs `.ToString(CultureInfo.X)`
2. **`.ToUpper()` / `.ToLower()`** (CA1311) — needs `.ToUpperInvariant()` /
   `.ToLowerInvariant()` for code paths, or explicit culture for user-facing
3. **`string.IndexOf(other)`** (CA1310) — needs `StringComparison.Ordinal`
4. **`name.ToLower() == "value"`** (CA1862) — replace with
   `string.Equals(name, "value", StringComparison.OrdinalIgnoreCase)`

**Recommended approach**: Same as the shipped Batch L slice — pick a service,
add `using System.Globalization;`, convert patterns by category. Use
`replace_all` Edit operations for unambiguous patterns; manual for ambiguous
cases (especially `.ToLower()` — must distinguish "comparison" from "display").

**Files with remaining concentration** (run `dotnet build --no-incremental`
and `grep "warning CA1305" build.log | grep -oE "[A-Z][A-Za-z]+\.cs" | sort | uniq -c | sort -rn`
for current top files):

- `Pages/Admin/Analytics.cshtml.cs` (~17)
- `Services/ProfileService.cs` (~21)
- `Services/ArchiveService.cs` (~14)
- `Pages/Admin/EmailConfig.cshtml.cs` (~16)
- The long tail: many files with 1-3 sites each

**Verification**: warning count in `dotnet build --no-incremental` should drop
by the migration count per PR. Total Batch L closure target: -596 from F-C-014
baseline.

---

### Batch N — Middleware as `AuthenticationScheme`

**Findings**: F-A-004 (middleware ordering risk) + F-A-022 (exception wrapper) + F-R-003 (path-check duplication)
**Severity**: Medium / Low / Medium
**Effort**: Medium (2-3 days; touches auth path)

**Problem**: `Middleware/ApiAuthenticationMiddleware.cs` is registered as
position-dependent middleware before `UseAuthorization()`. The file itself
contains a 70-line "FUTURE REFACTOR" comment recommending re-implementation
as a proper `AuthenticationHandler<TOptions>`. The current pattern is fragile —
if anyone reorders middleware, API key auth silently breaks.

**Recommended approach**:

1. Read the existing FUTURE REFACTOR comment in `ApiAuthenticationMiddleware.cs`
   (search for "FUTURE REFACTOR"). It already documents the migration.

2. Create `Authentication/ApiKeyAuthenticationHandler.cs` extending
   `AuthenticationHandler<ApiKeyAuthenticationOptions>`. Move the validation
   logic from `ApiAuthenticationMiddleware.InvokeAsync` into `HandleAuthenticateAsync`.

3. Register in `Program.cs` via:
   ```csharp
   services.AddAuthentication(...)
       .AddCookie(...)
       .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
           "ApiKey", options => { /* config */ });
   ```

4. Apply `[Authorize(AuthenticationSchemes = "ApiKey")]` to the V1 controllers
   (or set up policy-based selection: cookie scheme for `/Pages`, ApiKey for
   `/api`).

5. Delete `Middleware/ApiAuthenticationMiddleware.cs` and its registration.
   F-A-022 (the exception wrapper) closes for free.

6. F-R-003 (10+ `StartsWithSegments` checks) — these handle anonymous-bypass
   for specific paths. After the scheme migration, most can be replaced by
   `[AllowAnonymous]` attributes on the corresponding pages. The remaining
   path-checks (e.g. `/api/v1/version`) can use a single `IPathMatcher` helper.

**Critical**: This touches the auth path. Run integration tests for at least
one V1 endpoint before merging. Test scenarios:
- Valid API key → 200
- Missing X-API-Key → 401
- Invalid X-API-Key → 401
- Valid key, insufficient scope → 403

**Verification**: zero matches for `class ApiAuthenticationMiddleware` after
migration. Path-check `if (context.Request.Path.StartsWithSegments(...))`
count drops from 10+ to ~2 (only the truly-anonymous endpoints).

---

### Batch O — God-Class Decomposition

**Findings**: F-A-007 (MailService 1,654 lines), F-A-008 (NotificationService
1,335), F-A-009 (ShiftAssignmentService 1,043), F-A-010 (ImportService 1,033)
+ F-D-009 (Mail/Notification divergence) + F-H-005 (email queue durability) + F-H-012 (email idempotency) + F-D-004 (API PageModel boilerplate) + F-R-004 (MailService 11-param ctor)
**Severity**: All Medium — architectural debt
**Effort**: Large (genuinely multi-week per service)

**Don't try to do this in one session**. Each service is a multi-week program.
The right framing is "one service per sprint", with each service split into
multiple PRs (one per extracted responsibility).

**Recommended decomposition order** (by independence — least entangled first):

1. **NotificationService** (1,335 lines) → `NotificationFactory` (creation),
   `NotificationDispatcher` (delivery), `NotificationTemplateService` (text gen).
   Naturally closes F-D-009 alongside MailService when the dispatcher abstraction
   lands.

2. **ShiftAssignmentService** (1,043 lines) → `ShiftEligibilityPolicy` (eligibility
   rules), `ShiftCapacityValidator` (capacity), `AssignmentTokenService` (HMAC
   override tokens). Note: `BusyService` is already extracted (per CLAUDE.md
   memory) — don't re-do that.

3. **MailService** (1,654 lines) → `EmailProvider` (SMTP/Gmail abstraction),
   `EmailTemplateRenderer`, `EmailLogger`, `IBackgroundEmailDispatcher`. F-R-004
   (11-param ctor) closes naturally when the responsibilities split. F-H-005
   (in-memory queue durability) and F-H-012 (idempotency) become focused
   sub-PRs against `EmailProvider`.

4. **ImportService** (1,033 lines) → `ImportParser` (CSV/NDJSON parsing),
   `ImportValidator` (business rules), `ImportReporter` (results). Largely
   independent from the email pipeline.

**Per-service approach**:

- Read the service end-to-end first. Identify natural seams (methods that
  cluster around one concern).
- Extract one responsibility at a time. Keep the original service as a thin
  facade during migration.
- After all responsibilities extract cleanly, retire the facade.

**Verification per service**: build clean, all existing tests pass, line
count of the original drops by 30-50% per PR.

**F-D-004 (API PageModel boilerplate, 36 files)** — adjacent but separate.
Don't try to bundle. Extract `ApiPageModelBase`, `JsonRequestParser`,
`RequestAuthExtractor` in its own session.

---

### Batch Q — Layering / Coupling Refactors

**Findings**: F-A-012 (PageModel direct EF), F-A-017 (`IHttpContextAccessor`
in 10+ services), F-A-013 (1,297 `IgnoreQueryFilters` audit surface), F-A-019
(already shipped)
**Severity**: All Medium-Low
**Effort**: Mixed — Small per refactor, Medium for the audit-CI rule

**F-A-012** — `Pages/Admin/Companies.cshtml.cs:85-123` calls EF directly.
Extract `ICompanyManagementService` with `GetCompaniesAsync()`,
`GetAvailableMoleculesAsync()`, `GetAvailableDirectorsAsync()`. Migrate just
this one PageModel; if it works well, repeat for other PageModels with
direct EF (search for `_db.` usage in `cshtml.cs` files).

**F-A-017** — 10+ services inject `IHttpContextAccessor` directly. Create
`ICurrentUserService` facade that wraps it and exposes only what services
need (`UserId`, `CompanyId`, `Roles`). Migrate services one at a time.

**F-A-013** — 1,297 `IgnoreQueryFilters()` calls. Most are intentional
(security-audited per CLAUDE.md memory). The fix here isn't to remove them —
it's to **enforce a CI rule** that every new `IgnoreQueryFilters()` call must
have a `// SECURITY-AUDITED:` comment within 3 lines. Implement as a Roslyn
analyzer in `Tools/IgnoreQueryFiltersAnalyzer/` or as a script in
`scripts/check-ignore-query-filters.ps1` run in CI. The Roslyn analyzer is
the right answer; the script is the pragmatic answer.

**Verification**: Companies page renders correctly after F-A-012 refactor.
Audit script catches a deliberately-introduced unaudited `IgnoreQueryFilters()`
in a test.

---

### Batch R — Migrations / Single-Impl Audit / Misc

**Findings**: F-A-005 (161 unsquashed migrations), F-D-007 (40+ single-impl
interfaces), F-D-013 (resx duplication scripts), F-D-018 (DeleteChore +
DeleteOnDuty 95% identical), F-D-019 (RequestStatus.Declined vs
JoinRequestStatus.Rejected)
**Severity**: Mostly Low; F-A-005 and F-D-018 are Medium
**Effort**: Mixed

**F-A-005 — Squash 161 migrations into a baseline initial-create**:
- Effort: Large (>3 days), needs deployment-coordination plan
- Risk: existing prod databases need to be at the squash point or migrated
  through an intermediate state
- Approach: `dotnet ef migrations remove` repeatedly is wrong (loses history).
  Right approach is `dotnet ef migrations script --idempotent` to capture all
  current schema, then create new "InitialCreate" against an empty database,
  then handle the migration history table for existing deployments.
- **Don't tackle this until air-gapped deployment cadence is well-documented**

**F-D-007 — 40+ single-implementation interfaces**:
- Effort: Large
- Risk: removing interfaces is fine for internal services; risky if any are
  re-implemented for testing
- Approach: grep for `class.*: I\w+` (implementations) and cross-check against
  test mocks. Interfaces with 1 impl AND 0 test fakes can be deleted.
- Verification: build still succeeds, all tests still pass.

**F-D-013 — resx duplication**:
- Approach: run the existing `analyze_duplicates.py` and `deduplicate_resources.py`
  scripts (in repo root). DON'T run blindly — review the output first.
- Effort: Small (script-driven)

**F-D-018 — Generic `DeleteEntityModel<TService, TEntity>` base**:
- Files: `Pages/Api/Calendar/DeleteChore.cshtml.cs` and `DeleteOnDuty.cshtml.cs`
- Effort: Small (these are 95% identical 50-line files)
- Approach: extract a generic base class with abstract `GetServiceAsync()` and
  `EntityName` property. Two derived classes pass the type parameters.
- Verification: both endpoints still return same response shape, audit log entries unchanged.

**F-D-019 — `RequestStatus.Declined` vs `JoinRequestStatus.Rejected`**:
- Effort: Trivial-Small
- Risk: enum value renames affect serialised state in DB rows. Migration
  needed.
- Approach: pick one term ("Rejected" is more standard). Add a DB migration
  that updates existing rows. Update enum, all callers, all .resx keys.
- **Defer this one** — design clarity but no functional bug.

---

### Batch S-leftover — Auth Edge Cases (real but not crucial)

**Findings**: F-H-011 (VacationApprovalService re-auth gap), F-H-015 (overlap
boundary docs)
**Severity**: Medium / Low
**Effort**: Medium (F-H-011) / Trivial (F-H-015)

**F-H-011 — `VacationApprovalService` re-authorize at processing time**:
- Approach: in the approval handler method, after the `[Authorize]` filter
  has gated entry, re-check the grant before applying the approval
  (`await _grantService.HasGrantWithScopeAsync(currentUserId, "ApproveTimeOff", ...)`).
  This closes the time-of-check-time-of-use window if the grant is revoked
  mid-request.
- Verification: write a test that revokes the grant after request entry but
  before processing. Assert approval fails.

**F-H-015 — Overlap boundary inclusive/exclusive docs**:
- Approach: add XML doc to `Services/BusyService.ValidateAsync()` (or wherever
  overlap detection lives) explicitly stating: "shifts ending at 08:00 and
  shifts starting at 08:00 are NOT considered overlapping (boundaries are
  exclusive)" — or whichever is the actual semantics. Verify by reading the
  comparison operators (`<` vs `<=`).
- Verification: docs match the code's actual behavior for a boundary-touching
  pair of shifts.

---

### Batch T-leftover — Drop-in PRs (when convenient)

**Findings**: F-A-018 (DEPLOYMENT_GUIDE rollback flow), F-D-018 (already
covered in Batch R), F-D-019 (already covered in Batch R), F-D-003 (163
catch-log-throw sites), F-D-005/F-D-008/F-D-011/F-D-014/F-D-020 (info-only),
F-C-017 (CA1716 + CA1707 production naming review — 14 sites)
**Severity**: All Low or Info
**Effort**: Trivial each

**F-A-018 — Document migration rollback flow**:
- File: `DEPLOYMENT_GUIDE.txt`
- Add section: "On startup migration failure, restore from
  `Backups/app.db.pre-migration-{timestamp}` manually using SQLite tools.
  Do NOT delete the failed migration — it may have committed schema changes.
  Contact ops before retry."
- Verification: paragraph exists, references the correct backup path
  (verify by reading `Program.cs:486` for the actual backup location).

**F-D-003 — `ExceptionHelper.LogAndThrow()`**:
- 163 sites of `catch(Exception ex) { _logger.LogError(ex, ...); throw; }`
- Extract a helper, migrate sites with caution — many catch blocks have
  different log messages, so the helper takes a `string contextMessage`
  parameter.
- Effort: Trivial per site, Medium aggregate. Consider deferring unless
  there's appetite for stylistic cleanup.

**F-C-017 — CA1716 + CA1707 production naming review**:
- 14 production sites use underscore identifiers (CA1707) or names colliding
  with .NET-language reserved keywords (CA1716, 65 sites).
- Most can be renamed without breaking external callers (private/internal).
  Public API surface (V1 controllers, `Models/` referenced by Swagger) needs
  more care.
- Effort: Trivial per site.

---

## 3. Build State

### Latest known-clean state

- **Branch**: `update`
- **Last commit**: `b918a54` "refactor: Batch T drop-ins — Interlocked helper (F-R-007) + config precedence comment (F-A-019)"
- **Production warnings (`ShiftManager.csproj`)**: 2,750
- **Test project warnings**: untouched in the sprint; CA2007 + CA1707 are suppressed via `Directory.Build.props`
- **Errors**: 0

### Sanity check at session start

```bash
git log --oneline -5
dotnet build ShiftManager.csproj -c Debug -v minimal -nologo 2>&1 | tail -5
```

Expected: 0 errors, 2,750ish warnings (will drift as commits accumulate).

If errors appear: revert the last commit and investigate. Don't mask errors
with try/catches per CLAUDE.md §4.

### Pre-rebuild check (CLAUDE.md §3)

```bash
tasklist 2>/dev/null | grep -E "ShiftManager|dotnet|iisexpress|w3wp" || echo "Safe to rebuild"
```

If anything matches: stop the running app or test instance before rebuilding.

---

## 4. Templates / Reference Files

These shipped during the sprint and serve as patterns for similar work:

| Pattern | Reference File | Use For |
|---|---|---|
| Async interface conversion | `Services/IDirectorService.cs` + `Services/DirectorService.cs` | Batch O when extracting async services from god classes |
| `ApiErrorResponse` migration call-site mapping | `Controllers/Api/V1/AuditLogsController.cs` (docstring) | Any new V1 endpoint |
| `string.Format` culture fix | Most modified files in commit `713f17b` | Batch L remaining |
| Lock helper wrap pattern | `Services/DailyNotificationJob.cs` `TryAcquireRunningLock` | Future Interlocked sites |
| Auth-scope cleanup on deactivate | `Pages/Admin/Users.cshtml.cs` `OnPostToggleAsync` | Future user-state changes |
| `GetOrCreateAsync` race fix | `Services/FeatureFlagService.cs` | Future `IMemoryCache` users |
| Analyzer infra | `Directory.Build.props` + `.editorconfig` at repo root | Don't change without checking F-C-001 notes |

---

## 5. First Actions in Fresh Session

1. **Greet briefly, declare model** (Opus 4.7 for this work).
2. **Sanity check**:
   ```bash
   git log --oneline -10
   git status --short
   ls .claude-reviews/2026-04-28/
   ```
3. **Read this handoff in full**, plus `00-index.md` §K (Execution Log).
4. **Confirm branch + build state** (see §3 above).
5. **Pick ONE batch from §2** based on what's most valuable to ship next.
   Recommended order:
   - **Batch L remaining** (highest leverage — closes ~400 warnings with
     mechanical fixes, low risk)
   - **Batch K Phase 1** (`Pages/Auth/Login.cshtml.cs` 43 LoggerMessage sites —
     hot path, measurable perf)
   - **Batch I** (`IBackgroundTaskQueue` infra — unblocks proper fix for F-A-003)
   - **Batch O NotificationService split** (least entangled god class)
   - **Batch N** (middleware as scheme — needs careful auth-path testing)
6. **Execute the batch**. After each PR-sized chunk, build-verify and commit.
7. **Update `00-index.md` §K** with the batch's status before stopping.

## 6. Anti-Patterns to Avoid

These were catalogued during the sprint and are worth flagging:

1. **Don't trust dedup-chain-canonical findings without verification.** The
   sprint found ~30% of HiddenBugs lens findings to be factually invalid.
   Verify with current code, not the report's claim.

2. **Don't add defensive code "just in case."** If a finding turns out to be
   invalid, record the verification — don't add a null check that compensates
   for a non-bug. CLAUDE.md §4.

3. **Don't bulk-migrate API call sites without per-site review.** F-C-006
   warned about `ApiProblemDetails.ValidationError` shape drift. Each call
   site has different intent (field-level errors vs. simple message);
   pick the right `ApiErrorResponse` factory per site.

4. **Don't try to fix CA1860 (`.Any()` → `.Count > 0`) with a global sed.**
   `.Any()` on `IQueryable<T>` translates to SQL EXISTS; `.Count > 0`
   translates to SELECT COUNT. The analyzer only fires on `ICollection<T>`,
   but a global sed will catch IQueryable too and break translation.

5. **Don't squash migrations without a deployment-coordination plan.**
   Air-gapped deployments are at unknown migration points; squash needs
   a per-deployment migration path.

6. **Don't try to do all of Batch K (1,720 sites) in one PR.** Source-gen
   partial classes have build implications; per-file PRs are reviewable,
   one big PR isn't.

7. **Don't add `TreatWarningsAsErrors` until Batches K and L close.** With
   2,750 production warnings still open, flipping that flag turns every PR
   into a build failure. Only enable after warnings are below ~50.

---

## 7. Out-of-Scope Reminders

Things NOT covered by this handoff (separate concerns):

- **HOME unification** (in-flight, see commits before `7d37a25`)
- **Justice analytics** (Phase 1 shipped 2026-05-03; Phase 2/3 deferred per CLAUDE.md memory)
- **Tech molecule convergence** (design spec ready, separate workstream)
- **Test-project analyzer profile** — recorded in F-C-018, suppressed via
  Directory.Build.props. Not part of these batches.
- **Performance tuning** beyond the analyzer-flagged categories — separate concern.
- **Air-gapped deployment changes** — out of scope for code-quality batches.

---

## 8. Stop Criteria

Stop the session when ANY of these are true:

- Current batch reaches a natural seam (one PR's worth of changes)
- Build breaks and root-cause diagnosis exceeds 30 minutes
- A finding turns out to be factually invalid (record verification, don't fix)
- User says "wrap up" or "stop" or "we're done"
- You're about to make a destructive change (delete >100 lines, drop a table,
  rename a public API) — pause and confirm with user

Always commit before stopping. Never leave uncommitted in-flight work.

---

**End of handoff. Pick a batch and ship.**
