# Compiler / Analyzer Diagnostic Report

- Agent: Compiler
- Run date: 2026-04-28
- Repository: ShiftManager
- Files inspected: 576 (.cs files in scope after path exclusions; ~321 are non-Migrations source under `Services/`, `Pages/`, `Controllers/`, `Middleware/`)
- Files deeply read: 14 (`ShiftManager.csproj`, `ShiftManager.Tests.csproj`, `GlobalUsings.cs`, `Middleware/PageExceptionMiddleware.cs`, `Models/Api/ProblemDetails.cs`, `Models/ApiErrorResponse.cs`, `Services/MailService.cs`, `Services/NotificationService.cs`, `Services/ShiftAssignmentService.cs`, `Services/ImportService.cs`, `Services/GrantService.cs`, `Pages/Calendar/Table.cshtml.cs`, `Controllers/Api/V1/AnalyticsController.cs`, build logs)
- Tooling used: dotnet build (Debug/Release), NetAnalyzers transient (`AnalysisLevel=latest-recommended`, completed 2026-05-03), Read, Grep
- Status: COMPLETE

> The Debug+Release builds (both clean exit 0) and a clean-rebuild analyzer pass (`-p:EnableNETAnalyzers=true -p:AnalysisLevel=latest-recommended --no-incremental`, completed in 56 s after switching from `AnalysisMode=All` which OOMed at 5.8 GB) are authoritative for the *currently configurable* warning surface. The CS-only baseline holds (390 warnings: CS0618×389 + CS1998×1) and is augmented by 3,228 CA-class production warnings across 25 distinct codes — see F-C-011..F-C-018. F-C-001 (no analyzer infrastructure) remains the upstream cause: today's analyzer pass is *transient* and disappears the next build.

## Findings

### F-C-001: No analyzer infrastructure — CS-only warnings, zero CA/IDE coverage

| Field      | Value |
|------------|-------|
| Severity   | Critical |
| Category   | Compiler/infrastructure |
| File       | solution-wide (`ShiftManager.csproj`, missing `.editorconfig`, missing `Directory.Build.props`) |
| Lines      | n/a |
| Effort     | Small |
| Confidence | High |
| Tags       | analyzers, infrastructure, governance |

**Why it matters.** The repository ships **no** analyzer infrastructure: there is no `.editorconfig`, no `Directory.Build.props`, no transitively-referenced StyleCop / SonarAnalyzer / Microsoft.CodeAnalysis.NetAnalyzers / Roslynator package, and no `<EnableNETAnalyzers>` / `<AnalysisLevel>` / `<AnalysisMode>` properties in either `ShiftManager.csproj` or `ShiftManager.Tests/ShiftManager.Tests.csproj`. The default `dotnet build` for net8.0 enables only the *baseline* CodeAnalysis rules at `AnalysisLevel=latest-default`. As a result the entire build emits exactly two warning codes (CS0618 x389, CS1998 x1) — every CA1xxx (correctness/security), IDE0xxx (style), CA22xx (usage), and CA18xx (performance) rule that ships with .NET is silent. The codebase has 1.6k-line services, 132 grants, multi-tenant query-filter logic, and HMAC-protected override tokens — exactly the surface where CA1062 (null-arg validation), CA2007 (`ConfigureAwait`), CA1822 (`static` candidates), CA1829 (`Count` on `IEnumerable`), and CA2016 (cancellation token forwarding) routinely catch latent bugs. Without any of this turned on, the build cannot even *report* on those classes of issue — the agent baseline is "we don't know what we don't know." This is the upstream cause of every other Compiler-lens finding being collapsed into two codes; it is the infrastructure ship-blocker.

### F-C-002: 389 CS0618 ApiProblemDetails obsolete — controllers stuck on V1 shim

| Field      | Value |
|------------|-------|
| Severity   | High |
| Category   | Compiler/deprecation |
| File       | `Controllers/Api/V1/SwapRequestsController.cs` (64), `TimeOffController.cs` (56), `OnDutyController.cs` (53), `ChoresController.cs` (53), `FeedbackController.cs` (50), `UsersController.cs` (41), `NotificationsController.cs` (36), `ShiftsController.cs` (18), `AnalyticsController.cs` (9), `AuditLogsController.cs` (7) — 389 total sites across 10 files |
| Lines      | n/a (every `[ProducesResponseType(typeof(ApiProblemDetails), …)]` and every `ApiProblemDetails.{NotFound,Unauthorized,Forbidden,ValidationError,Conflict,RateLimitExceeded,InternalError}` factory call) |
| Effort     | Medium |
| Confidence | High |
| Tags       | obsolete, api, error-envelope, technical-debt |

**Why it matters.** Every CS0618 in the entire build comes from the same author-marked obsolescence: `Models/Api/ProblemDetails.cs:21` carries `[Obsolete("Use ShiftManager.Models.ApiErrorResponse. Removal scheduled for next release.")]`. The "next release" deletion is overdue — V1 controllers have not migrated, while the canonical `Models/ApiErrorResponse` envelope is fully implemented (correlation-id support, `FromOperationResult`, factory parity for 400/401/403/404/409/429/500/503). Two consequences: (1) any next-release deletion of `ApiProblemDetails` will turn the entire V1 controller layer into 389 build errors, blocking release; (2) public V1 API responses currently emit *two different* error envelope shapes depending on which controller served the request — the RFC-7807 `{type,title,status,detail,instance}` envelope from `ApiProblemDetails`, vs. the `{error:{code,message,details,correlationId}}` envelope from `ApiExceptionMiddleware` — meaning external API consumers cannot rely on a single error parser. The compiler is correctly flagging this; the migration has stalled.

### F-C-003: CS1998 — `PageExceptionMiddleware.HandleExceptionAsync` is `async` with no `await`

| Field      | Value |
|------------|-------|
| Severity   | Low |
| Category   | Compiler/async |
| File       | `Middleware/PageExceptionMiddleware.cs` |
| Lines      | 55–121 |
| Effort     | Trivial |
| Confidence | High |
| Tags       | async, fake-async, micro-perf |

**Why it matters.** The single CS1998 in the entire codebase. `HandleExceptionAsync(HttpContext, Exception)` is declared `async Task` but its body is purely synchronous — logging, TempData write, and `context.Response.Redirect(...)` (which sets a status code and Location header; it does not flush). The compiler-generated state machine wraps a synchronous body in `Task.FromResult`-equivalent overhead on every unhandled UI exception. The caller at line 51 awaits it, so removing `async` and returning `Task.CompletedTask` (or making it `void` if the caller stops awaiting) is the path. Low severity because this only fires on already-failing requests, but worth noting because it's the lone async smell the compiler can see — every other service-method `async` use is genuinely awaited.

### F-C-004: Nullability is `enable`d project-wide but services routinely chain navigation properties without null guards

| Field      | Value |
|------------|-------|
| Severity   | Medium |
| Category   | Compiler/nullability |
| File       | `Services/ShiftAssignmentService.cs` (cluster), `Services/MailService.cs` (cluster), `Services/NotificationService.cs` (cluster), `Services/ImportService.cs` (cluster), `Services/GrantService.cs` (cluster) |
| Lines      | ShiftAssignmentService: 56–57, 150–151, 220–222, 248, 258–260, 274, 285, 314–317, 464, 649, 893, 924–926; representative — full cluster spans 30+ sites across these five services |
| Effort     | Medium |
| Confidence | Medium |
| Tags       | nullability, ef-core, navigation-property, latent-NRE |

**Why it matters.** `<Nullable>enable</Nullable>` is on, but the compiler reports **zero** CS86xx warnings — meaning every potential null deref the compiler *could* see has been silenced (via null-forgiving `!`, eager `?.`, or `Include(...)` shapes that satisfy the flow analysis). That silence is itself the smell: the suspect services dereference EF navigation properties (`.ShiftType.JobTypeId`, `.ShiftType.Start`, `.ShiftType.MoleculeId.Value`, `.ShiftInstance.ShiftType.End`) without any post-`Include` null check (e.g., `ShiftAssignmentService.cs:56-57, 150-151, 314-317`). The compiler is satisfied because EF marks navigation properties non-nullable at the model level; runtime is not, because (a) FK rows can drift after migrations, (b) some queries do not `Include` the navigation but the projection touches it anyway (line 464 projects `sa.ShiftInstance.ShiftType.Start` from a query that does not visibly `Include(si => si.ShiftType)`), and (c) `IgnoreQueryFilters()` is in heavy use, which can return rows whose related entities were deleted in another tenant. Grep results: `!.` null-forgiving in suspect services — MailService 0, NotificationService 2, ShiftAssignmentService 4, ImportService 0, GrantService 7. The cluster is grouped here as one Medium finding rather than 30+ Low findings; this is exactly the kind of issue CA1062/CS8602 would expose if F-C-001 were closed.

### F-C-005: Migration `.Designer.cs` files universally pragma-disable 612/618 — no production-code suppressions

| Field      | Value |
|------------|-------|
| Severity   | Info |
| Category   | Compiler/observation |
| File       | `Migrations/*.Designer.cs` (78 files) |
| Lines      | line 20 of every Designer.cs |
| Effort     | Trivial (no change needed) |
| Confidence | High |
| Tags       | pragma, generated-code, hygiene |

**Why it matters.** Recorded so the next agent does not re-flag this. Every `#pragma warning disable 612, 618` (78 occurrences) in the repo lives inside `Migrations/*.Designer.cs`, which are EF-generated and per-instructions are info-only. There is **no** `#pragma warning disable` anywhere in hand-written code — meaning the 389 CS0618 in F-C-002 are honest; nobody is silencing them locally. This is the *good* finding: there is no hidden warning surface masked by ad-hoc pragmas. (`<NoWarn>$(NoWarn);1591</NoWarn>` in the .csproj is also confirmed-narrow — only XML-doc misses are suppressed, no broad code-quality codes.)

### F-C-006: `ApiProblemDetails.ValidationError` factory deviates from `ApiErrorResponse.ValidationError` shape

| Field      | Value |
|------------|-------|
| Severity   | Medium |
| Category   | Compiler/deprecation-shape-drift |
| File       | `Models/Api/ProblemDetails.cs` |
| Lines      | 63–83 vs. `Models/ApiErrorResponse.cs:114-129` |
| Effort     | Small |
| Confidence | High |
| Tags       | api, error-shape, swagger, schema-drift |

**Why it matters.** Sub-finding of F-C-002 but worth its own ID because it changes the migration risk. The deprecated `ApiProblemDetails.ValidationError(string detail, string? instance, Dictionary<string, string[]>? errors)` keeps `errors` under `Extensions["errors"]` (RFC-7807 extension), while the canonical `ApiErrorResponse.ValidationError(IDictionary<string, string[]> errors)` puts the dictionary directly into `Error.Details`. A naive global rename will silently break Swagger consumers that destructure either shape — the migration has to be a re-design at each call site, not a sed. This is invisible to the compiler today (CS0618 is symbol-level, not shape-level).

### F-C-007: `<GenerateDocumentationFile>true</GenerateDocumentationFile>` ships with `NoWarn;1591` — XML-doc gating is opt-out, not opt-in

| Field      | Value |
|------------|-------|
| Severity   | Info |
| Category   | Compiler/configuration |
| File       | `ShiftManager.csproj` |
| Lines      | 7–9 |
| Effort     | Trivial (no change needed) |
| Confidence | High |
| Tags       | xml-doc, swagger, configuration |

**Why it matters.** Recorded so other agents understand the choice: the project both generates the XML doc file (for Swashbuckle to consume) AND globally suppresses CS1591 (missing XML comment for publicly visible type/member). This is the right call for a 700-file codebase that doesn't want CS1591 noise on every non-API public symbol — but it means *Swagger documentation completeness* is unenforceable from the build. If the team later wants strict API doc coverage, the path is to *narrow* CS1591 to `<NoWarn Include="...">` excluding the V1 controllers, not to delete the line.

### F-C-008: CS0168/CS0219 absent — no unused-local / declared-but-unread evidence

| Field      | Value |
|------------|-------|
| Severity   | Info |
| Category   | Compiler/observation |
| File       | solution-wide |
| Lines      | n/a |
| Effort     | n/a |
| Confidence | High |
| Tags       | dead-code, copy-paste-residue |

**Why it matters.** Recorded so Readability agent does not double-flag. The compiler reports zero CS0168 (declared-but-unused exception variable) and zero CS0219 (assigned-but-unread local) across the entire build. Either the code is clean of these patterns, or the patterns exist behind discard-elision (`_ = ...`) — the compiler cannot see those. The Readability lens should look for `_ =` and `_ = await` patterns specifically; the *compiler* finds nothing here.

### F-C-009: Test project lacks analyzer wiring — same gap as main project, half the scrutiny

| Field      | Value |
|------------|-------|
| Severity   | Low |
| Category   | Compiler/test-infrastructure |
| File       | `ShiftManager.Tests/ShiftManager.Tests.csproj` |
| Lines      | full file (no analyzer references, no Directory.Build.props inheritance) |
| Effort     | Trivial |
| Confidence | High |
| Tags       | tests, analyzers, governance |

**Why it matters.** Sub-finding of F-C-001 but worth recording: when F-C-001 is fixed, the easiest way is `Directory.Build.props` at repo root. The test csproj currently only references `xunit/Moq/FluentAssertions/coverlet/EFCore.InMemory/Microsoft.NET.Test.Sdk` — no Microsoft.CodeAnalysis.NetAnalyzers, no SonarAnalyzer.CSharp. Whatever analyzer set the main project gains in fixing F-C-001 should propagate via a shared props file rather than per-csproj edits.

### F-C-010: Solution NoWarn / GlobalUsings shape — narrow and intentional

| Field      | Value |
|------------|-------|
| Severity   | Info |
| Category   | Compiler/configuration |
| File       | `ShiftManager.csproj`, `GlobalUsings.cs` |
| Lines      | csproj:9, GlobalUsings.cs:9 |
| Effort     | n/a |
| Confidence | High |
| Tags       | global-using, configuration |

**Why it matters.** Recorded so future agents stop re-investigating. `NoWarn` is exactly `1591` (no overbroad suppression). `GlobalUsings.cs` declares exactly one global using (`ShiftManager.Models.Validation`) with a load-bearing comment about the validation namespace move — it is not a kitchen-sink global-using file. Both files are tight; no Compiler-lens issue here.

### F-C-011: CA2244 — `IconTagHelper.cs:50` overwrites the same `printer` dictionary key twice (real bug)

| Field      | Value |
|------------|-------|
| Severity   | High |
| Category   | Compiler/bug |
| File       | `TagHelpers/IconTagHelper.cs` |
| Lines      | 50 |
| Effort     | Trivial |
| Confidence | High |
| Tags       | dictionary, dead-code, ui |

**Why it matters.** `Redundant element initialization at index 'printer'. Object initializer has another element initializer with the same index that overwrites this value.` This is the only CA-class warning in the entire pass that flags an actual data-loss bug rather than a style/perf concern: one of the two `printer` SVG bodies is silently shadowed at construction, and any consumer asking for the `printer` icon gets only the second one. The first definition is dead. Worth a real triage to determine which definition was *intended* — analyzer cannot tell us.

### F-C-012: CA5351 — `CacheHelper.GenerateETag` uses MD5 (broken-crypto category, but ETag-only use)

| Field      | Value |
|------------|-------|
| Severity   | Medium |
| Category   | Compiler/security-rule |
| File       | `Helpers/CacheHelper.cs` |
| Lines      | 21, 32 |
| Effort     | Small |
| Confidence | High |
| Tags       | crypto, etag, security-rule, false-positive-candidate |

**Why it matters.** Two MD5.Create()/MD5.HashData() calls in `GenerateETag(...)`. CA5351 fires because MD5 is collision-broken; the rule does not differentiate "crypto identity" from "non-security cache key". For pure ETag use (cache validation, not tamper-evidence) MD5 is functionally fine, but the analyzer cannot distinguish — meaning every future build will re-emit these as warnings *and* an attacker who can chosen-prefix-collide an ETag could trick a downstream cache into serving stale content for a hostile resource. Switching to non-crypto `XxHash64` (System.IO.Hashing) or a keyed `HMACSHA256` truncation removes both the warning and the marginal risk. Marked Medium not Critical because the practical attack surface is narrow (ETag, not auth/integrity), but it's the only crypto-class warning in the build.

### F-C-013: CA1848 — 1,720 sites use `ILogger.LogXxx(...)` extension methods instead of `LoggerMessage` delegates

| Field      | Value |
|------------|-------|
| Severity   | High |
| Category   | Compiler/perf |
| File       | clusters: `Pages/**/Index.cshtml.cs` (~100 sites cumulative across feature pages), `Program.cs` (74), `Pages/Admin/Users.cshtml.cs` (60), `Pages/Auth/Login.cshtml.cs` (43), `Controllers/Api/V1/SwapRequestsController.cs` (42), `Pages/Calendar/Table.cshtml.cs` (39), `Services/NotificationService.cs` (39), `Pages/Requests/Index.cshtml.cs` (38), `Services/GriffinConfigService.cs` (38) |
| Lines      | 1,720 sites total — full distribution in `_raw/dotnet-build-analyzers.log` |
| Effort     | Large |
| Confidence | High |
| Tags       | logger, perf, allocation, hot-path |

**Why it matters.** Every `_logger.LogInformation("user {Id} did {Action}", id, action)` boxes the value-type args, allocates a `params object[]`, and runs format-string parsing on every call — even when the log level is filtered out. `LoggerMessage.Define<int,string>(LogLevel.Information, ...)` produces a single static delegate that allocates nothing on the hot path. 1,720 sites is the largest single allocation source in the codebase and concentrates on hot paths: `Pages/Auth/Login.cshtml.cs` (43 — login is on every authentication round-trip), `Services/NotificationService.cs` (39 — every push event fans out), and `Controllers/Api/V1/SwapRequestsController.cs` (42 — request endpoints). This is a Large effort because converting 1,720 sites needs source-generation (`[LoggerMessage(...)]` partial methods, .NET 6+) — but worth it for ASP.NET Core under load.

### F-C-014: Culture/comparison cluster — 596 warnings, real bug class for a Hebrew-RTL bilingual app

| Field      | Value |
|------------|-------|
| Severity   | High |
| Category   | Compiler/correctness-i18n |
| File       | clusters: `Pages/**/Index.cshtml.cs` (68), `Services/MailService.cs` (34), `Services/NotificationService.cs` (30), `Pages/Admin/Users.cshtml.cs` (24), `Services/ProfileService.cs` (21), `Pages/Admin/Analytics.cshtml.cs` (17), `Pages/Calendar/Table.cshtml.cs` (16), `Pages/Admin/EmailConfig.cshtml.cs` (16), `Services/ArchiveService.cs` (14), `Services/SetupTaskService.cs` (13) |
| Lines      | 596 sites: CA1305×408 (specify `IFormatProvider`), CA1304×64 (specify `CultureInfo`), CA1311×61 (specify culture for `ToUpper`/`ToLower`), CA1310×48 (specify `StringComparison`), CA1862×15 (use `String.Equals(StringComparison)` for case-insensitive comparison) |
| Effort     | Large |
| Confidence | High |
| Tags       | culture, i18n, hebrew, rtl, string-comparison, latent-bug |

**Why it matters.** This codebase ships in Hebrew (he-IL) and English (en-US) per `CookieRequestCultureProvider`. Calls like `name.ToUpper()`, `s.IndexOf(other)`, `string.Format("{0:N2}", value)`, `dict.ContainsKey(key)` without an explicit culture or `StringComparison.Ordinal[IgnoreCase]` execute under the request's current culture. Under tr-TR the famous Turkish-i breaks `"FILE".ToLower() == "file"`; under he-IL number formatting flips decimal separators; case-insensitive comparisons under invariant vs. Hebrew can mis-match. **596 sites is genuinely a Large refactor**, but the failure mode is silent — bugs surface only when culture differs from the development culture. `MailService` (34) and `NotificationService` (30) are particularly load-bearing — these format strings are sent to users; if a Hebrew user receives an English-formatted decimal and back, it's hard to debug.

### F-C-015: CA1860 — 158 sites use `.Any()` over collections where `.Count > 0` is O(1)

| Field      | Value |
|------------|-------|
| Severity   | Medium |
| Category   | Compiler/perf |
| File       | spread across Services and Pages — top: `Services/NotificationService.cs`, `Pages/Calendar/Table.cshtml.cs`, `Services/MailService.cs`, `Services/ShiftAssignmentService.cs` |
| Lines      | 158 sites total |
| Effort     | Small |
| Confidence | High |
| Tags       | perf, linq, micro-opt |

**Why it matters.** `.Any()` is O(N) on `IEnumerable` and allocates an enumerator; `.Count > 0` is O(1) on `ICollection`/`List`/`HashSet`/`Dictionary`. The analyzer fires only when the static type implements `ICollection<T>`, so every flag is a real perf delta. 158 sites is enough that the cumulative iterator-allocation cost on the hot Calendar/Shifts/Notifications paths is non-trivial — fixing this is a low-effort sweep, but the analyzer can do the rewrite mechanically with a `dotnet format analyzers` run in a future state.

### F-C-016: CA1869 — 29 sites allocate fresh `JsonSerializerOptions` per request handler

| Field      | Value |
|------------|-------|
| Severity   | Medium |
| Category   | Compiler/perf |
| File       | `Middleware/ApiExceptionMiddleware.cs:67`, `Models/ShiftInstanceOverride.cs:50`, plus 27 OnPost handlers in `Pages/**/*.cshtml.cs` (Delete/Quick/Save/Restore family) |
| Lines      | 29 sites |
| Effort     | Small |
| Confidence | High |
| Tags       | perf, json, allocation, hot-path |

**Why it matters.** `new JsonSerializerOptions { … }` per call defeats the source-generated metadata cache that the System.Text.Json runtime builds on first use. Every request-handler that calls `JsonSerializer.Serialize(obj, new JsonSerializerOptions { … })` re-runs the property-discovery reflection pass on every invocation — measurable on hot calendar/chore handlers. Pattern is concentrated in the `Pages/**/Delete*.cshtml.cs`, `Pages/**/QuickAdd*.cshtml.cs`, and `Pages/**/Save*.cshtml.cs` families — 27 handlers all written with the same copy-paste options block. This pairs naturally with F-D-### (Duplication) findings about copy-paste handler boilerplate; one extracted `static readonly JsonSerializerOptions JsonOpts = new(...)` field per page (or one shared static) closes all 29.

### F-C-017: CA1716 (65) + CA1707 (14 production) — naming clashes and underscore identifiers in production

| Field      | Value |
|------------|-------|
| Severity   | Low |
| Category   | Compiler/naming |
| File       | spread |
| Lines      | 65 sites for CA1716 (identifier matches reserved keyword in another .NET language) + 14 production sites for CA1707 (underscore in identifier) |
| Effort     | Medium |
| Confidence | High |
| Tags       | naming, vb-interop, conventions |

**Why it matters.** CA1716 fires when a public-API identifier collides with a reserved keyword in C#/VB/F# — typically a `Type` named `Property`, `Event`, or `Module`. For an internal-only ASP.NET app that never exposes managed assemblies to other languages, this is mostly noise; but ~65 occurrences signal that public-API hygiene has not been reviewed for the public surface that *does* leak (V1 controllers, Models referenced by Swagger). CA1707 (`_` in identifier) is the rule that produced 977 warnings in the *test* project (deliberate `Method_Underscore_Style`) but only 14 in production — those 14 are worth checking individually because production should not use that style. (Tests log: 977 sites, all in `xunit` `[Fact]`/`[Theory]` method names, conventional and intentional.)

### F-C-018: Production-vs-tests warning shape — analyzers cleanly partitioned

| Field      | Value |
|------------|-------|
| Severity   | Info |
| Category   | Compiler/observation |
| File       | solution-wide |
| Lines      | n/a |
| Effort     | n/a |
| Confidence | High |
| Tags       | tests, analyzers, observation |

**Why it matters.** Recorded so other agents do not re-investigate. The analyzer pass run twice (incremental against the .sln, and clean against `ShiftManager.csproj`) shows the test project hits a *different* analyzer profile than production: tests are dominated by **CA2007 (2150 — `ConfigureAwait`)** and **CA1707 (977 — `_` in test method names)** — both *conventionally noise* in xUnit/ASP.NET-Core test code. Production code emits **0 CA2007** (because ASP.NET Core has no SynchronizationContext, so the rule has no signal there) and only **14 CA1707** (which deserve individual review). The implication for F-C-001's resolution is: the eventual `Directory.Build.props` should suppress CA2007 + CA1707 only inside `ShiftManager.Tests`, not solution-wide.

## Summary

| Severity | Count |
|----------|-------|
| Critical | 1 |
| High     | 4 |
| Medium   | 5 |
| Low      | 3 |
| Info     | 5 |
| **Total**| **18** |

## Top 5 Findings

1. F-C-001 — No analyzer infrastructure — today's pass is transient; without `Directory.Build.props` it disappears next build
2. F-C-011 — CA2244 actual bug: `IconTagHelper.cs:50` overwrites the `printer` dictionary key; one icon definition is dead
3. F-C-002 — 389 CS0618 `ApiProblemDetails` obsolete across 10 V1 controllers; deletion will hard-break the V1 API
4. F-C-014 — 596-warning culture/comparison cluster — real Hebrew-RTL bug class in MailService/NotificationService/ProfileService
5. F-C-013 — 1,720 sites use `ILogger.LogXxx(...)` extensions instead of `LoggerMessage` delegates; largest single allocation source on hot paths

## Frequency Tables

### Top 15 Warning Codes (analyzer pass + CS baseline merged)
| Code | Count | Description |
|------|-------|-------------|
| CA1848 | 1,720 | Use the `LoggerMessage` delegates (perf — see F-C-013) |
| CA1305 | 408 | Specify `IFormatProvider` (culture — see F-C-014) |
| CS0618 | 389 | `ApiProblemDetails` obsolete (see F-C-002) |
| CA1860 | 158 | Avoid using `.Any()` over `ICollection<T>` (see F-C-015) |
| CA1861 | 100 | Avoid constant arrays as arguments — recreated per call |
| CA1716 | 65 | Identifier matches keyword in another .NET language (see F-C-017) |
| CA1304 | 64 | Specify `CultureInfo` (see F-C-014) |
| CA1822 | 63 | Member can be marked `static` (perf micro) |
| CA1311 | 61 | Specify culture for `ToUpper`/`ToLower` (see F-C-014) |
| CA1310 | 48 | Specify `StringComparison` (see F-C-014) |
| CA1869 | 29 | Cache `JsonSerializerOptions` (see F-C-016) |
| CA1805 | 24 | Don't initialize unnecessarily (default-init) |
| CA1862 | 15 | Use `String.Equals(StringComparison)` for case-insensitive (see F-C-014) |
| CA1707 | 14 (prod) / 977 (tests) | Underscore identifiers (see F-C-017, F-C-018) |
| CA1854 | 13 | Prefer `IDictionary.TryGetValue` over double-lookup |
| CA1845 | 11 | Use span-based `string.Concat` |
| CA1806 | 10 | Do not ignore method results |
| CA1826 | 8 | Use `[]` indexer over `.First()` on collection |
| CA1000 | 6 | Don't declare static members on generic types |
| CA5351 | 2 | Broken cryptographic algorithm (see F-C-012) |
| CA2244 | 1 | Redundant element initialization (see F-C-011 — actual bug) |
| CS1998 | 1 | Async method lacks `await` (see F-C-003) |

### Top 10 Files by Warning Count (analyzer pass, production)
| File | Count |
|------|-------|
| `Controllers/Api/V1/SwapRequestsController.cs` | 106 |
| `Program.cs` | 101 |
| `Pages/Admin/Users.cshtml.cs` | 97 |
| `Controllers/Api/V1/TimeOffController.cs` | 93 |
| `Services/NotificationService.cs` | 91 |
| `Controllers/Api/V1/OnDutyController.cs` | 88 |
| `Controllers/Api/V1/ChoresController.cs` | 88 |
| `Controllers/Api/V1/FeedbackController.cs` | 82 |
| `Services/MailService.cs` | 73 |
| `Controllers/Api/V1/UsersController.cs` | 67 |
| `Pages/Calendar/Table.cshtml.cs` | 62 |
| `Middleware/PageExceptionMiddleware.cs` | 1 (CS1998 only) |

## Coverage Notes

- Builds run: Debug (clean, exit 0, 390 CS warnings), Release (clean, exit 0, 390 CS warnings — same set, parity confirmed), Analyzer pass (clean, exit 0, 3,228 production CA warnings + 390 CS — see Frequency Tables)
- Analyzer pass — first attempt (incremental, `.sln` target, `AnalysisMode=All`): **OOM at 5.8 GB** during `csc.dll` invocation; killed after 10+ min. Root cause confirmed: `AnalysisMode=All` enables 400+ rules including all off-by-default ones, and the Roslyn batch-compile model holds analyzer state in memory until the file completes — not a buffering problem (the previous PowerShell `Out-String` theory was incorrect). Logs preserved at `_raw/dotnet-build-analyzers-tests-incremental.log` (228 KB, ASCII). The first attempt did surface the test-project profile (3,410 warnings dominated by CA2007×2150 + CA1707×977, both conventional noise in xUnit/ASP.NET-Core test code) — see F-C-018.
- Analyzer pass — successful (clean rebuild, `ShiftManager.csproj` target, `-p:EnableNETAnalyzers=true -p:AnalysisLevel=latest-recommended -p:RunAnalyzersDuringBuild=true --no-incremental`): completed in 56 s with peak ~4.2 GB. 3,228 warnings across 25 distinct CA codes. Log preserved at `_raw/dotnet-build-analyzers.log` (1.4 MB, ASCII).
- Analyzer set used: `latest-recommended` (= ~110 MS-curated rules). `AnalysisMode=All` (~400 rules) was rejected as not-completable in budget. StyleCop.Analyzers, SonarAnalyzer.CSharp, Roslynator: still not referenced — see F-C-001.
- Paths intentionally skipped: `bin/`, `obj/`, `packages/`, `FinalProductPublish/`, `ProductionReady/`, `wwwroot/lib/`, `qa-automation/`, `screenshots*/`, `*.db`, `*.bat`, `*.ps1` (Build-Release.ps1 inspected separately as configuration), repo-root `*.py` helpers, `Backups/`, `App_Data/`, `logs/`, `clients/`, `ProjectPublish*/`. `Migrations/` was treated info-only (78 generated `.Designer.cs` files, all carrying `#pragma warning disable 612, 618` at line 20 — see F-C-005).
- Known limitations remaining: (a) the analyzer pass was *transient* (`-p:` flags only, no project file edits) — the next `dotnet build` reverts to CS-only output unless F-C-001 is addressed; (b) `AnalysisMode=All` rules (~290 additional off-by-default codes including some `IDE####` style and many `CA##xx` correctness rules — CA1062 null-arg validation, CA2016 cancellation-token forwarding, CA1849 `Async` discipline) remain unmeasured; (c) CS86xx nullability cluster in F-C-004 still derives from manual reading + grep, because `<Nullable>enable</Nullable>` already passes — the compiler finds nothing because EF marks navigation properties non-nullable, so the runtime risk is invisible to both CS and CA passes; (d) test-project analyzer profile is recorded informally from the failed first attempt — re-running cleanly with `--no-incremental` against the .sln would reconfirm but is not in scope.
