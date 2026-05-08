# Cross-Agent Triage Index — ShiftManager Diagnostic Review

- Triage date: 2026-05-05
- Last updated: 2026-05-08 (Batch K Phase 1-4 — 163 LoggerMessage sites + Batch I — IBackgroundTaskQueue infra closes F-A-003; build 2,524 → **2,363 warnings, 0 errors** [-161 net], tests **1,196/1,196 passing**)
- Source reports: 5 (`01-compiler.md`, `02-architecture.md`, `03-duplication.md`, `04-readability.md`, `05-hidden-bugs.md`)
- Source-report run date: 2026-04-28 (analyzer pass completed 2026-05-03)
- Total findings catalogued: 88 (Compiler 18, Architecture 22, Duplication 20, Readability 9, HiddenBugs 19)
- Triage model: Opus 4.7 (1M context)
- Status: **EXECUTION IN PROGRESS** — see §K (Execution Log) for batch-by-batch state

## K. Execution Log (2026-05-06)

| Batch | Status | Outcome |
|---|---|---|
| **A — Analyzer infrastructure** | ✅ Shipped | `Directory.Build.props` + `.editorconfig` + test carve-out. Closes F-C-001/009/018/007/010. Build durable: 3,398→3,400 warnings (with later Batch deltas). |
| **B — IconTagHelper bug** | ✅ Shipped | Deleted dead `printer` dict entry. Closes F-C-011. |
| **C — CalendarHub DI** | ❌ Voided | All 3 findings (F-A-001/014/015) verified **factually invalid** — see §E Batch C. SignalR Hubs are transient, not Singleton. |
| **D — DirectorService async** | ✅ Shipped | Full async conversion: interface, impl, 6 caller files, 8 tests. Removed 5 `GetAwaiter().GetResult()` deadlock sites. Deleted dead `AssignableRoles` getter. Closes F-H-003 + F-A-002 + F-R-001. |
| **E — GrantService null safety** | ❌ Voided | All 4 findings (F-H-001/002/006/016) verified **factually invalid** — null guards already present, EF translates `!.` to SQL with proper LEFT JOIN null handling, `SameAsRole` already extracts only Company+Department per spec. Pattern likely fixed pre-audit by commit `195aacc "fixed grants issues"`. |
| **F — User lifecycle** | ✅ Partial / Real | F-H-004 already implemented (audit missed reading lines 1604-1672). F-H-013 partially shipped: deactivation now nulls `TraineeUserId` references on dependents and removes `DirectorCompany` rows. **Deferred sub-items**: shift-assignment removal and grant cleanup (semantically tied to soft-reactivate workflow at lines 940-967; design-alignment with user required). |
| **G — CSRF hardening** | ❌ Voided | F-H-010 verified invalid: ASP.NET Razor Pages auto-validates antiforgery on every POST by default. Codebase opt-outs (`[IgnoreAntiforgeryToken]` × 60+) prove the default is on. F-A-016 actually had the correct take ("likely working implicitly") but was suppressed by dedup chain. |
| **H — ApiErrorResponse migration** | ⏸️ Deferred | Genuinely Large effort (389 sites × 10 controllers, **incompatible call shapes** per F-C-006 — naive sed will break Swagger consumers). Needs focused per-controller PR with response-shape regression testing. |
| **I — Task.Run cleanup** | ✅ Shipped | **2026-05-08**: New `Services/IBackgroundTaskQueue.cs` (Channel-bounded capacity 500, SingleReader=true, generic `BackgroundTask` delegate) + `Services/BackgroundTaskHostedService.cs` (drains the queue with fresh DI scope per item, catches per-item exceptions to keep loop alive). Registered as Singleton + IHostedService in Program.cs. Migrated all 4 fire-and-forget `Task.Run` sites: `ApiAuthenticationMiddleware:207` (API key LastUsedAt update), `ApiRequestLoggingMiddleware:82` (request log to DB — also refactored `LogRequestAsync` to take `IServiceProvider` instead of `IServiceScopeFactory` since the queue handles scoping), `Pages/Auth/Signup.cshtml.cs:325` and `Pages/Auth/GriffinSignup.cshtml.cs:323` (owner notification fan-out — both PageModels migrated from `IServiceScopeFactory` injection to `IBackgroundTaskQueue` injection, dead `_serviceScopeFactory` field removed cleanly). Closes **F-A-003**. F-D-009 partially closed (NotificationService god-class still pending Batch O). EventIds 11000-11012 reserved for queue infrastructure logging (also `[LoggerMessage]` source-gen). Test fix: `SignupFlowTests.cs:146` updated to mock `IBackgroundTaskQueue` instead of `IServiceScopeFactory`. **+2 warnings** are CA1711 ("type ends in 'Queue'") on the new types — matching the established `EmailBackgroundQueue` pattern that already had this fire. Tests **1,196/1,196 passing**. |
| **J — Cache refactor** | ✅ Partial | F-H-009 race fix: `FeatureFlagService.IsEnabledAsync` now uses `GetOrCreateAsync`. **Deferred sub-item**: F-D-002 base-class extraction across 3 cache services (refactor, not a bug). |
| **K — Hot-path perf sweep** | ✅ Phase 1-4 / Continuing | **Phase 1** (`Pages/Auth/Login.cshtml.cs`, 43 sites, 1000-1055): -43. **Phase 2** (`Services/NotificationService.cs`, 39 sites, 2000-2064): -39. Surfaced `int?` signature refinement for catch-block companyId. **Phase 3** (`Controllers/Api/V1/SwapRequestsController.cs`, 42 sites, 3000-3071): -42. Established **method reuse pattern** — 21 methods cover 42 call sites because boilerplate events fire identically across 6 endpoints (Endpoint param distinguishes); also mixed `int` vs `string?` companyId handled. **Phase 4** (`Pages/Calendar/Table.cshtml.cs`, 39 sites, 4000-4040): -39. Established **template parameterization pattern** — 14 methods cover 39 sites because 28 of them share templates that differ only by an action verb, now passed as `{Action}` placeholder (`LogErrorAction(ex, "ensuring shift instance")`, `LogCalendarNotificationFailed(ex, "AssignEmployee")`, etc). Structured-log consumers gain a filterable Action property; console output is identical. **Cumulative -163 warnings** (Phase 1-4 alone). Build clean, **1,196/1,196 tests passing**. **Remaining Phase 5+**: ~1,560 sites in Admin/Users (60), MailService (34), Requests/Index (42), GriffinConfigService (38), My/Requests (38), V1 controllers TimeOff/Chores/OnDuty/Users/Feedback (~37 each), plus 158 `.Any()` + 29 `JsonOptions`. Per-file PRs only. |
| **L — Hebrew/culture sweep** | ✅ Partial (continuing) | 2026-05-07 Slice 2: ProfileService.cs (CA1304+CA1311+CA1862, 17 sites), BusyService.cs (CA1305, 11 sites — including HMAC token cache key), ArchiveService.cs (CA1305, 14 sites — wire format), Pages/Owner/{EmailConfig,GameConfig,GriffinConfig,FeatureFlags}.cshtml.cs (35 sites combined). **-84 warnings net** (2,750 → 2,666). 2026-05-07 Slice 3: TagHelpers/OptimizedImageTagHelper.cs (CA1304+CA1311, 5 call sites = 10 warnings). **-10 warnings net** (2,666 → 2,656), build clean. **2026-05-07 PM: Slices 4 + 5a + 5b + 6 + 7 + 8 + 9 shipped in one push.** Slice 4 (Services/ScheduleExportService.cs) — 13 sites (10 v2-mapped + 3 v2-table-missed at lines 468/478/559 caught during verification grep); Slice 5a (Pages/Calendar/{Shifts,Chores,OnCall}.cshtml.cs) — 28 sites; Slice 5b (Pages/Calendar/{Day,Week,Month,Table}.cshtml.cs) — 41 sites including `Key.ToLower→ToLowerInvariant`, `view?.ToLower→ToLowerInvariant`, log message `FormattableString.Invariant` wraps; Slice 6 (Services/{CompanyLocalizationService,FriendshipService,Api/UserApiService,AvatarService}.cs) — 8 edits closing ~19 fires (3 EF-query simplifications using EF Core 9 `Contains(string, StringComparison.OrdinalIgnoreCase)` translation, dropped 3 stale `term`/`queryLower`/`searchLower` lowercased-vars + AvatarService initials/path-traversal); Slice 7 (Services/GriffinService.cs) — 6 edits including line 273 EF email lookup `.ToLowerInvariant()` and line 354 `DateTime.UtcNow.ToString("o", InvariantCulture)`; Slice 8 (Pages/Auth/ForgotPassword.cshtml.cs only — Signup/Login/GriffinSignup verified-empty for CA1305 patterns) — 2 edits × 2 `.ToLowerInvariant()` per line = 4 fires; Slice 9 (Middleware/RateLimitingMiddleware.cs + Pages/Api/Calendar/GetOverviewData.cshtml.cs + Pages/GriffinDiagnostic.cshtml.cs + ViewComponents/SystemAlertsViewComponent.cs) — ~25 edits closing ~30 fires across CA1305 + CA1310. **−132 warnings net** (2,656 → **2,524**), build clean, **1,196/1,196 tests passing**. Bonus: `BusyService.TargetCanonical` extended to `FormattableString.Invariant` for all 4 int components (HMAC cache key consistency). Slice 10+ long-tail (~150 culture sites, 1-3 per file in 60+ files) intentionally unscheduled per v2 §4 — opportunistic only. |
| **M — Crypto / migration hygiene** | ✅ Shipped | F-C-012 closed: `MD5.HashData` → `SHA256.HashData` in `CacheHelper.cs` (2 sites). CA5351 count: was 2, now **0**. |
| **N — Middleware cleanup** | ⏸️ Deferred | F-A-004 / F-A-022 / F-R-003. Each path-check has different downstream logic — extraction doesn't actually simplify. F-A-004 (auth-as-scheme) is the structurally-correct refactor and bundles F-A-022 for free. |
| **O — God-class decomposition** | ⏸️ Deferred | Genuinely multi-week effort (4 services × 1k+ lines). Belongs in its own multi-PR program. |
| **P — Readability cleanup** | ❌ Mostly Voided | F-R-002 (Console.WriteLine in DeploymentExportService) is **by design** — runs pre-`builder.Build()`, no DI yet, Console is the only output. Existing block comment at lines 39-43 already documents this. F-R-008 (BackupModel comment) is **deliberate breadcrumbing**, not stale. F-R-006 / F-R-005 are bikeshedding. **No fixes shipped from this batch.** |
| **Q — Layering refactors** | ⏸️ Deferred | Medium-effort, no immediate bug consequences. |
| **R — Migrations / single-impl audit** | ⏸️ Deferred | Mixed bag, mostly Large-effort observations. |
| **S — Auth edge cases** | ✅ Partial / Real | F-H-014 shipped 2026-05-05 (ImportService NDJSON parse-error visibility). F-H-015 shipped 2026-05-07 PM: XML doc on `BusyService.ValidateAsync` documenting half-open `[start, end)` overlap semantics (the existing `existingStart < newEnd && newStart < existingEnd` test). F-H-011 verified **factually invalid**: `VacationApprovalService.ApproveAsync` line 218 already calls `CanUserApproveAsync(approverId, requestId)` which delegates to `_grantService.HasGrantWithScopeAsync` — exactly the time-of-check/time-of-use re-authorization v2 prescribed; no defensive code added per CLAUDE.md §4. |
| **T — Drop-in PRs** | ✅ Partial | F-R-007 + F-A-019 shipped 2026-05-05 (DailyNotificationJob lock helper + Program.cs config-precedence comment). F-A-018 shipped 2026-05-07 PM: `DEPLOYMENT_GUIDE.txt` "Migration failed" section rewritten — pre-migration auto-backup restore (preserves data) is now the primary path; destructive recreate demoted to "fresh installs only". Restore steps explicitly cite `Backups/app.db.pre-migration-{timestamp}` from `Program.cs:489-495`. **Deferred sub-items**: F-D-003 (`ExceptionHelper.LogAndThrow()` — 163 stylistic sites, no functional benefit per v2); F-C-017 (CA1716/CA1707 14+65 sites — public API surface needs care). |

## L. Session Statistics (2026-05-06)

- **Findings closed (real fixes shipped)**: F-C-001, F-C-007, F-C-009, F-C-010, F-C-011, F-C-012, F-C-018, F-H-003, F-A-002, F-R-001 (cluster), F-H-009, F-H-013 (partial)
- **Findings voided (verified invalid)**: F-A-001, F-A-014, F-A-015 (Batch C); F-H-001, F-H-002, F-H-006, F-H-016 (Batch E); F-H-010, F-A-016, F-D-016 (Batch G); F-H-004 (already implemented before audit); F-R-002, F-R-008, F-R-006, F-R-005, F-R-009 (Batch P false positives)
- **Findings deferred (real but oversized for inline)**: F-C-002 + F-C-006 + F-D-001 + F-D-015 (Batch H = 389 sites); F-C-013 (Batch K = 1,720 sites); F-C-014 (Batch L = 596 sites); F-A-007/008/009/010 (Batch O = 4 god classes); F-D-002 (Batch J cache base extraction); F-A-003 + F-D-009 (Batch I background queue infra); F-A-004 + F-A-022 + F-R-003 (Batch N middleware refactor); F-A-005 (migrations squash); F-A-013 (IgnoreQueryFilters audit CI rule)
- **False-positive rate by lens** (real findings ÷ examined):
  - Compiler (18): ~14 real, ~4 voided as info-only — **78% real**
  - Architecture (22): ~10 real, ~12 voided/deferred-with-skepticism — **45% real**
  - Duplication (20): ~6 real opportunities, ~14 stylistic — **30% real**
  - Readability (9): ~1 real, ~8 stylistic/false — **11% real**
  - HiddenBugs (19): ~6 real, ~13 voided after careful read — **32% real**
- **Net build delta from session start**: +0 errors, +2 warnings (3,398 → 3,400 — net of MD5 closures, dead code deletion, plus new warnings from added code)
- **Files modified**: 14 (`Directory.Build.props` *new*, `.editorconfig` *new*, `TagHelpers/IconTagHelper.cs`, `Services/DirectorService.cs`, `Services/IDirectorService.cs`, `Services/CompanyFilterService.cs`, `Services/ViewAsModeService.cs`, `Pages/Admin/Users.cshtml.cs`, `ShiftManager.Tests/UnitTests/Services/DirectorServiceTests.cs`, `Services/FeatureFlagService.cs`, `Helpers/CacheHelper.cs`, plus this index + memory pointer)

## M. Calibration Notes for Future Audits

The high false-positive rate on the Architecture, Readability, and HiddenBugs lenses suggests audit-prompt improvements:
1. **Lifetime/lifecycle claims** (e.g., "Hubs are Singletons") MUST be verified against authoritative docs before flagging Critical-severity DI issues.
2. **Null-safety claims** MUST consider EF Core's LINQ-to-SQL translation — `!.` in a query expression doesn't NRE at runtime; it's a compiler hint.
3. **"Stale comment" / "dead code"** claims MUST grep for actual references before flagging — many "stale" comments are deliberate breadcrumbing.
4. **Dedup chain (HiddenBugs > Architecture > ...)** can promote a *less-accurate* analysis to canonical when the higher-priority lens has factual errors. Future runs should weight by confidence, not just lens priority.
5. **Pre-audit fixes**: commits between audit-time and triage-time may have already addressed findings. Always grep current code before assuming a finding is still valid.

This index is **derivative** of the 5 lens reports. Severity, file paths, and line ranges are quoted from the source findings; the index adds *cross-agent ranking*, *file grouping*, *fix-batch identification*, *cross-reference adjudication*, and *recommended ship order*.

---

## A. Source Material Sanity Check

| Item | Notes |
|---|---|
| Reports unchanged since 2026-04-28 directory creation | Confirmed — file sizes match expected (`01`=30 KB, `02`=21 KB, `03`=9 KB, `04`=7 KB, `05`=14 KB) |
| Repo state vs. reports | 3 commits post-date reports (`bdf9ea3`, `236ccf6`, `b002841`) — all HOME-rotation/spec work, none touch any flagged file. **No findings are stale.** |
| Severity-table arithmetic drift | Architecture report tail says Total=26 but enumerates 22 IDs. Duplication tail has similar drift. **This index uses per-finding declared severities, not the tail rollups.** |
| Dedup-chain violations in source | 3 cases where a lower-priority lens emitted a full finding instead of a See-also (see §C). Index treats the higher-priority finding as canonical. |
| Positive observations (info-only) | F-H-017, F-H-018, F-H-019, F-A-020, F-D-005, F-D-014, F-D-020, F-C-005, F-C-007, F-C-008, F-C-010, F-C-018 — recorded so future agents don't re-investigate. Excluded from ship order. |

---

## B. Cross-Agent Top 20 (Severity desc, Effort asc)

Sort key: `severity_rank * 10 + effort_rank` where Critical=1, High=2, Medium=3, Low=4, Info=5; Trivial=1, Small=2, Medium=3, Large=4. Lower = ship-sooner.

| # | Score | ID | Sev | Eff | File | Title |
|---|-------|----|-----|-----|------|-------|
| ~~1~~ | ~~12~~ | ~~**F-A-001**~~ | ~~Critical~~ | ~~Small~~ | ~~`Hubs/CalendarHub.cs`~~ | ❌ **VOIDED 2026-05-06** — false positive (Hubs are transient, not Singleton); see §E Batch C |
|  1 | 12 | **F-C-001** | Critical | Small | solution-wide (`ShiftManager.csproj`, no `.editorconfig`, no `Directory.Build.props`) | ✅ **shipped 2026-05-06** — `Directory.Build.props` + `.editorconfig` + test carve-out |
|  2 | 21 | **F-C-011** | High | Trivial | `TagHelpers/IconTagHelper.cs:50` | ✅ **shipped 2026-05-06** — deleted dead `printer` dict entry (line 154 Lucide entry retained) |
|  4 | 22 | **F-H-001** | High | Small | `Services/GrantService.cs:295-373` (deref at 366-368) | Null deref in `GetAccessibleCompanyIdsForGrantAsync` — NRE during Self-scope auth resolution |
|  5 | 22 | **F-A-003** | High | Small | `Middleware/ApiAuthenticationMiddleware.cs`, `Middleware/ApiRequestLoggingMiddleware.cs`, `Pages/Auth/Signup.cshtml.cs`, `Pages/Auth/GriffinSignup.cshtml.cs` | `Task.Run` fire-and-forget in request paths — exceptions lost, thread-pool risk |
|  6 | 23 | **F-H-003** | High | Medium | `Services/DirectorService.cs:52-53, 124-125, 134-135, 144-145, 157-158` | Sync-over-async deadlock in DirectorService (5 sites) — owns the F-A-002 / F-R-001 cluster |
|  7 | 23 | **F-H-004** | High | Medium | `Pages/Admin/Users.cshtml.cs:186-500` | User hard-delete orphans `ShiftAssignment.UserId`, leaves dangling slots |
|  8 | 23 | **F-H-010** | High | Medium | `Program.cs` (no global filter), `Pages/Admin/Users.cshtml.cs` and others | Missing CSRF protection on OnPost handlers — owns the F-A-016 cross-ref |
|  9 | 23 | **F-C-002** | High | Medium | `Controllers/Api/V1/*.cs` (10 files, 389 sites) | 389× CS0618 `ApiProblemDetails` obsolete; deletion will hard-break V1 API. Owns F-D-001, F-C-006, F-D-015 |
| 10 | 24 | **F-C-013** | High | Large | clusters across `Pages/Auth/Login.cshtml.cs` (43), `Services/NotificationService.cs` (39), `Controllers/Api/V1/SwapRequestsController.cs` (42), `Pages/Calendar/Table.cshtml.cs` (39), `Program.cs` (74), `Pages/Admin/Users.cshtml.cs` (60) — 1,720 total | CA1848 — 1,720 `ILogger.LogXxx(...)` extension calls instead of `LoggerMessage` delegates; largest single allocation source |
| 11 | 24 | **F-C-014** | High | Large | clusters in `Services/MailService.cs` (34), `Services/NotificationService.cs` (30), `Pages/Admin/Users.cshtml.cs` (24), `Services/ProfileService.cs` (21), `Pages/Admin/Analytics.cshtml.cs` (17) — 596 total | Culture/comparison cluster (CA1305+CA1304+CA1311+CA1310+CA1862) — Hebrew-RTL bilingual app cannot afford this silently |
| 12 | 31 | **F-R-002** | Medium | Trivial | `Services/DeploymentExportService.cs:74,81,94,108` | `Console.WriteLine` in production deployment-restoration code |
| 13 | 32 | **F-D-002** | Medium | Small | `Services/AppConfigCacheService.cs`, `Services/CompanyCacheService.cs`, `Services/ShiftTypeCacheService.cs` | Cache service triplication — 3 nearly-identical implementations (~330 lines) |
| 14 | 32 | **F-C-006** | Medium | Small | `Models/Api/ProblemDetails.cs:63-83` vs `Models/ApiErrorResponse.cs:114-129` | `ValidationError` shape drift (`Extensions["errors"]` vs `Error.Details`) — silent break risk on naive sed-rename |
| 15 | 32 | **F-C-012** | Medium | Small | `Helpers/CacheHelper.cs:21,32` | CA5351 — MD5 in `GenerateETag`; only crypto-class warning in build |
| 16 | 32 | **F-C-015** | Medium | Small | spread across Services and Pages — 158 sites | CA1860 — `.Any()` over `ICollection<T>` where `.Count > 0` is O(1); mechanically fixable |
| 17 | 32 | **F-C-016** | Medium | Small | `Middleware/ApiExceptionMiddleware.cs:67`, `Models/ShiftInstanceOverride.cs:50`, plus 27 OnPost handlers | CA1869 — `JsonSerializerOptions` re-instantiated per call defeats source-gen cache; 29 sites |
| 18 | 32 | **F-H-002** | Medium | Small | `Services/GrantService.cs:177-217` | Null `userContext` silently skips self-scope fallback — users can fail authorization unexpectedly |
| 19 | 32 | **F-H-007** | Medium | Small | `Services/AnalyticsService.cs:148, 180` | `DateTime.Today` (local time) on date-range queries — off-by-one on non-UTC server |
| 20 | 32 | **F-H-008** | Medium | Small | `Services/ShiftAssignmentService.cs` (`GetEligibleUsersForShiftAsync`) | Cross-company user scoping gap — `ShiftGrouping` company metadata not re-validated against caller's grants |

**On-deck (just outside top 20, also Medium/Small):** F-H-009 (`IMemoryCache` race in FeatureFlagService), F-D-018 (Delete endpoint duplication), F-R-003 (ApiAuthMiddleware path checks), F-A-006 (Home/Index `.Result`).

---

## C. Cross-Reference Adjudication

The dedup chain in the original plan is **HiddenBugs > Compiler > Architecture > Duplication > Readability**. The source reports applied this *mostly* correctly but emitted three cases of overlapping full findings instead of See-also references. The canonical owner is named first; the others are recorded as cross-references.

| Cluster | Canonical | Cross-refs (defer to canonical) | Reason for canon |
|---|---|---|---|
| **DirectorService sync-over-async** (5 call sites) | **F-H-003** (Medium effort) | F-A-002 (Architecture, High), F-R-001 (Readability, High) | HiddenBugs > Architecture > Readability — and only F-H-003 enumerates *all 5 call sites* (line 157 missed by F-A-002) |
| **ApiProblemDetails parallel envelope** | **F-C-002** (Medium effort, 389 sites) | F-D-001 (Duplication, High), F-C-006 (sub-finding — shape drift), F-D-015 (controller inconsistency, Low) | Compiler > Duplication. Note F-C-006 is intentionally a *sub-finding* of F-C-002, not a duplicate |
| **CSRF / Anti-forgery** | **F-H-010** (Medium effort) | F-A-016 (Architecture, Medium), F-D-016 (PageModel auth checks, Low — partial overlap) | HiddenBugs > Architecture |
| **Nullability cluster (CS86xx implicit)** | **F-C-004** (Medium effort) — meta cluster summary | F-H-001 (specific NRE), F-H-002 (specific skip), F-H-016 (specific IgnoreQueryFilters case) | Hybrid: F-C-004 is the **cluster summary** (compiler-class observation across 5 mega-services); F-H-### are *concrete bug instances*. Both should land — they are not duplicates |
| **Hardcoded grant strings** | **F-A-011** (Architecture, Medium) | F-A-021 (stale constants in DirectorService, Low — contained subset) | Both Architecture; F-A-011 is the broad finding, F-A-021 is one specific manifestation |
| **IgnoreQueryFilters audit surface** | **F-A-013** (Architecture, Medium — 1,297 sites meta) | F-H-016 (specific null-deref under IQF, Low — bug instance) | F-A-013 is a *governance/audit* finding; F-H-016 is a *bug-class* finding. Both stand. |
| **Mail/Notification fire-and-forget divergence** | **F-D-009** (Duplication, Medium) | F-A-003 (Task.Run anti-pattern, High — but a *broader* category) | These are *adjacent* not duplicates — F-A-003 is "Task.Run in request paths" generally, F-D-009 is "Mail and Notification picked different solutions for the same problem". Land in the same PR but separate concerns |

**One missed duplicate the dedup chain didn't catch:** F-A-018 (Migration rollback Low) and F-C-005 (Migration pragma Info) both touch `Migrations/` but are unrelated concerns (data-loss-recovery vs. generated-code observation). **Do not merge.**

---

## D. Findings Grouped by File

A fix-PR can usually batch all findings in a single file. Files with multiple findings are listed; files with a single finding appear in the per-file column of §B.

### `Services/DirectorService.cs` — 5 findings (1 cluster)
- F-H-003 (High, Medium) — sync-over-async deadlock — **canonical**
- F-A-002 (High, Medium) — sync-over-async tech debt — *cross-ref*
- F-R-001 (High, Large) — sync-over-async readability — *cross-ref*
- F-A-021 (Low, Trivial) — stale grant constants
- F-H-017 (Info) — positive: `int.TryParse` claim parsing safe

### `Services/GrantService.cs` — 6 findings (4 distinct concerns)
- F-H-001 (High, Small) — null deref line 366-368
- F-H-002 (Medium, Small) — null `userContext` fallback skip
- F-H-006 (Medium, Medium) — `SameAsRole` scope residual cross-molecule risk
- F-H-016 (Low, Small) — null-forgiving `c.Molecule!.Area!.ProjectId` under IQF
- F-D-010 (Low, Small) — repetitive `HasCalendarEdit/Note` permission-check methods
- F-A-011 (Medium, Small) — partial: hardcoded grant strings

### `Services/MailService.cs` — 8 findings (god class + cluster contributor)
- F-A-007 (Medium, Large) — god class, 1,654 lines, 6+ responsibilities
- F-D-009 (Medium, Medium) — fire-and-forget queue divergence vs. NotificationService
- F-H-005 (Medium, Large) — in-memory retry queue (durability)
- F-H-012 (Medium, Medium) — idempotency gap (network timeout = duplicate send)
- F-D-006 (Low, Trivial) — config fallback chain split with EmailConfigService
- F-R-004 (Low, Medium) — 11-param constructor
- Cluster contributor: F-C-004 (nullability), F-C-014 (34 culture sites), F-C-013 (LoggerMessage)

### `Services/NotificationService.cs` — 4 findings (god class + cluster contributor)
- F-A-008 (Medium, Large) — god class, 1,335 lines
- F-D-009 (Medium, Medium) — fire-and-forget divergence (shared with MailService)
- Cluster contributor: F-C-004, F-C-014 (30 culture sites), F-C-013 (39 LoggerMessage sites)

### `Services/ShiftAssignmentService.cs` — 5 findings (god class + clusters)
- F-A-009 (Medium, Large) — god class, 1,043 lines
- F-H-008 (Medium, Small) — cross-company eligibility scoping gap
- F-H-015 (Low, Small) — overlap boundary inclusive/exclusive undocumented
- F-H-018 (Info) — positive: HOME bidirectional exclusion verified
- Cluster contributor: F-C-004, F-C-014

### `Services/ImportService.cs` — 5 findings (god class + clusters)
- F-A-010 (Medium, Large) — god class, 1,033 lines
- F-D-011 (Low, Trivial) — validation accumulation pattern shared with ShiftAssignmentService
- F-H-014 (Low, Small) — NDJSON parse-error visibility (swallowed inner exceptions)
- F-R-006 (Low, Trivial) — generic `result` variable name at line 43
- Cluster contributor: F-C-004

### `Pages/Admin/Users.cshtml.cs` — 4 findings + 2 cluster contributors
- F-H-004 (High, Medium) — hard-delete orphans ShiftAssignments
- F-H-013 (Medium, Medium) — `IsActive=false` doesn't enforce CLAUDE.md cleanup chain
- F-H-010 (High, Medium) — partial: CSRF on OnPost
- F-A-016 (Medium, Small) — partial: explicit `[ValidateAntiForgeryToken]`
- Cluster contributor: 60 LogXxx (F-C-013), 24 culture (F-C-014), 97 total CA warnings

### `Models/Api/ProblemDetails.cs` + `Models/ApiErrorResponse.cs` — 4 findings (one cluster)
- F-C-002 (High, Medium) — 389 CS0618 — **canonical**
- F-C-006 (Medium, Small) — `ValidationError` shape drift — sub-finding
- F-D-001 (High, Medium) — parallel error shapes — *cross-ref*
- F-D-015 (Low, Small) — V1 controllers return both shapes during transition

### `Hubs/CalendarHub.cs` + `Program.cs` (lines 359, 146) — 3 findings (DI cluster)
- F-A-001 (Critical, Small) — Singleton/Scoped DI violation
- F-A-014 (Medium, Small) — CompanyIdInterceptor Singleton + Scoped DbContext
- F-A-015 (Low, Small) — CalendarNotificationService couples SignalR to business layer

### `Program.cs` (broader) — 5 findings + cluster contributor
- F-H-010 (High, Medium) — partial: no global anti-forgery filter
- F-A-016 (Medium, Small) — partial: explicit attributes
- F-A-018 (Low, Trivial) — `Database.MigrateAsync` no rollback flow
- F-A-019 (Low, Trivial) — config precedence implicit
- F-A-022 (Low, Trivial) — middleware exception wrapper hack
- Cluster contributor: 74 LogXxx, 101 total CA warnings (top file)

### `Middleware/ApiAuthenticationMiddleware.cs` + neighbors — 4 findings
- F-A-004 (Medium, Medium) — middleware ordering risk (70-line "FUTURE REFACTOR" comment)
- F-A-022 (Low, Trivial) — wrapping middleware exception guard
- F-R-003 (Medium, Small) — 10+ duplicated `Path.StartsWithSegments` checks
- F-A-003 (High, Small) — partial: Task.Run fire-and-forget

### `Services/AnalyticsService.cs` — 3 findings + cluster
- F-H-007 (Medium, Small) — `DateTime.Today` off-by-one
- F-R-005 (Low, Small) — "Phase 2C" comments without context
- Cluster contributor: 17 culture sites

### Other files with 1 finding (referenced in §B): `TagHelpers/IconTagHelper.cs`, `Helpers/CacheHelper.cs`, `Services/FeatureFlagService.cs`, `Services/DeploymentExportService.cs`, `Services/AppConfigCacheService.cs` (+ 2 cache siblings as one batch), `Services/DailyNotificationJob.cs`, `Pages/Owner/Backup.cshtml.cs`, `Migrations/`, `Pages/Home/Index.cshtml.cs`, `Pages/Admin/Companies.cshtml.cs`, `Pages/Api/Calendar/DeleteChore.cshtml.cs` + `DeleteOnDuty.cshtml.cs`, `Models/Support/Enums.cs`, `Resources/SharedResources*.resx`, `ShiftManager.csproj` + `ShiftManager.Tests.csproj`.

---

## E. Fix Batches (one PR each, 3-7 related findings)

Each batch is a candidate `/plan` invocation in a future session. Effort is the *highest* among bundled findings; smaller items are bundled-in for free.

### Batch A — Analyzer Infrastructure Foundation [GATE — must precede F-C-013, F-C-014, F-C-015, F-C-016, F-C-017]
- F-C-001 (Critical, Small) — `Directory.Build.props` + `.editorconfig` + `Microsoft.CodeAnalysis.NetAnalyzers` + `<AnalysisLevel>latest-recommended</AnalysisLevel>`
- F-C-009 (Low, Trivial) — Test csproj inheritance via `Directory.Build.props`
- F-C-018 (Info) — Suppress CA2007 + CA1707 inside `ShiftManager.Tests` only
- F-C-007 (Info) — Document XML doc gating in props comment
- F-C-010 (Info) — Document `NoWarn=1591` narrow intent in props comment

> **Why batch:** A single `Directory.Build.props` PR closes all 5. Without this, today's analyzer pass disappears the next `dotnet build`. Ship before any other CA-class work.

### Batch B — IconTagHelper printer dictionary bug [Trivial quick win]
- F-C-011 (High, Trivial) — pick the intended `printer` SVG body, delete the dead one

> **Why batch:** Standalone, ≤30-min fix, only CA2244 instance in the build that flags an actual data-loss bug. Can ship within 24 hours of read.

### Batch C — CalendarHub DI Lifecycle [VOIDED 2026-05-06 — all findings verified invalid]

**Verification result:** All three findings in this batch were re-checked against actual code and .NET runtime conventions before implementation. None describes a real bug.

- ~~F-A-001 (Critical, Small)~~ — **INVALID** — finding's premise is wrong. SignalR Hubs are transient (per Hub method call), not Singleton. Verified per [Microsoft Docs (.NET 8)](https://learn.microsoft.com/aspnet/core/signalr/hubs): *"Hubs are transient: A new instance of the Hub class is created for each Hub method call."* Constructor-injected scoped services (`AppDbContext`, `IGrantService`) are resolved fresh per call. `Program.cs:360` is vanilla `builder.Services.AddSignalR()` — no Singleton override. Actual code in `Hubs/CalendarHub.cs` uses `_db` and `_grantService` only inside method bodies; no long-lived state. **No fix required.**
- ~~F-A-014 (Medium, Small)~~ — **INVALID** — pattern at `Data/CompanyIdInterceptor.cs:63` (Singleton interceptor + explicit `IServiceProvider.CreateScope()` for scoped `ITenantResolver` access) is the **EF Core docs-recommended idiom** for `SaveChangesInterceptor` needing scoped dependencies. Class is stateless; documented at lines 56-58, 61-62. Alternative ("Scoped interceptor") not structurally cleaner — `ITenantResolver` resolution happens at SaveChanges time, after the scope already exists, so manual scope creation is required either way. **No fix required.**
- ~~F-A-015 (Low, Small)~~ — **INVALID** — finding's premise contains a factual error: `IHubContext<T>` is registered **Singleton** by `AddSignalR()`, not Scoped. `CalendarNotificationService` (Scoped) → `IHubContext` (Singleton) is the correct dependency direction. The "create `IRealtimeNotificationService` abstraction" suggestion is taste-based, not a bug; `ICalendarNotificationService` already is that abstraction. **No fix required.**

**Per CLAUDE.md §4 (No Workarounds Policy)**: implementing changes for any of these would constitute *adding defensive code to compensate for a bug that doesn't exist* — the inverse anti-pattern. Batch C closes with zero source changes.

**Lesson for re-running similar audits:** the original Architecture-lens agent had partial knowledge of SignalR Hub lifetimes (a known industry misconception) and `IHubContext<T>` registration. Future audits should explicitly verify Hub lifecycle claims against Microsoft Docs before flagging Critical-severity DI issues.

### Batch D — DirectorService Async Conversion [unblocks middleware/auth path improvements]
- F-H-003 (High, Medium) — convert `IDirectorService` to fully async; remove all 5 `GetAwaiter().GetResult()` sites
- F-A-002 — *closes via F-H-003*
- F-R-001 — *closes via F-H-003*
- F-A-021 (Low, Trivial) — relocate stale grant constants to `GrantKeys` (also bundle into Batch L)
- F-A-011 (Medium, Small) — *partial close*: replace string literals in DirectorService with `GrantKeys.*`

> **Why batch:** Touching DirectorService.cs once for the async conversion is the right time to swap the constants. Saves a second PR-cycle on the same file.

### Batch E — GrantService Null-Safety + Tenancy Hardening [auth-path correctness]
- F-H-001 (High, Small) — null guard on `userContext.Path.Company.Id` line 366-368
- F-H-002 (Medium, Small) — explicit fallback when `userContext` is null in `HasGrantWithScopeAsync`
- F-H-006 (Medium, Medium) — `SameAsRole` scope must validate molecule boundary
- F-H-016 (Low, Small) — null-forgiving `c.Molecule!.Area!.ProjectId` under `IgnoreQueryFilters`
- F-D-010 (Low, Small) — collapse repetitive `HasCalendarEdit/Note` permission methods
- (cluster contributor: F-C-004 nullability — landing F-H-001/002/016 closes the GrantService slice of it)

> **Why batch:** All 6 findings live in `GrantService.cs`. Auth path is load-bearing — single coordinated PR with thorough tests is safer than 4 small PRs touching the same file.

### Batch F — User Lifecycle (Hard-Delete + Deactivate) [data integrity]
- F-H-004 (High, Medium) — hard-delete must null `ShiftAssignment.UserId`, remove dangling refs, clear DirectorCompany
- F-H-013 (Medium, Medium) — `IsActive=false` must enforce same cleanup chain (TraineeUserId, grants, etc.)

> **Why batch:** Same code path in `Pages/Admin/Users.cshtml.cs` (lines 186-500). Single transaction-bounded helper closes both.

### Batch G — CSRF Hardening [security baseline]
- F-H-010 (High, Medium) — register global `AutoValidateAntiforgeryTokenAttribute` filter in `Program.cs`
- F-A-016 (Medium, Small) — *closes via F-H-010* (or add explicit attributes for documentation)
- F-D-016 (Low, Trivial) — page-level handler auth checks (related but not duplicate)

> **Why batch:** Single global filter PR + spot-check a few high-value pages. Run authenticated POST through the full handler suite as the smoke test.

### Batch H — ApiErrorResponse Migration [closes 389 CS0618]
- F-C-002 (High, Medium) — migrate 10 V1 controllers from `ApiProblemDetails` to `ApiErrorResponse`
- F-C-006 (Medium, Small) — handle `ValidationError` shape drift (`Extensions["errors"]` → `Error.Details`) explicitly per call site
- F-D-001 — *closes via F-C-002*
- F-D-015 (Low, Small) — controller-side error response inconsistency *closes via F-C-002*
- After migration: delete `Models/Api/ProblemDetails.cs` and the `[Obsolete]` attribute

> **Why batch:** All 4 findings describe one migration. Note from F-C-006: a `sed` rename will silently break Swagger consumers — each call site needs case-by-case shape mapping.

### Batch I — Task.Run / Fire-and-Forget Cleanup [thread-pool safety]
- F-A-003 (High, Small) — replace fire-and-forget `Task.Run` in 4 sites with proper `IHostedService` or queue
- F-A-006 (Medium, Small) — `Home/Index.cshtml.cs:136-199` — `await Task.WhenAll(...)` instead of `.Result`

> **Why batch:** Both are thread-pool antipatterns. F-D-009 (MailService/NotificationService dispatcher divergence) is *adjacent* but better deferred to god-class decomposition (Batch O).

### Batch J — Cache Service Refactor [reduces ~330 lines, fixes a race]
- F-D-002 (Medium, Small) — extract `GenericCacheService<T>` base; collapse 3 cache services
- F-D-017 (Info) — centralize cache key formatting via `CacheKeyFactory`
- F-H-009 (Medium, Small) — `IMemoryCache` `TryGetValue→Set` race in `FeatureFlagService` — fix by using `GetOrCreateAsync` (likely also folded into the new base class)

> **Why batch:** F-H-009 is a concrete instance of the pattern Batch J refactors. Build the new base with atomic-create from the start.

### Batch K — Hot-Path Perf Sweep [REQUIRES Batch A; Large effort, may split into K1/K2/K3]
- F-C-013 (High, Large) — 1,720 sites → `[LoggerMessage]` source-gen partials. Split by file: K1 = top-10 files (~480 sites), K2 = next 30, K3 = remainder
- F-C-015 (Medium, Small) — 158 `.Any()` → `.Count > 0`; mechanical with `dotnet format analyzers`
- F-C-016 (Medium, Small) — 29 `JsonSerializerOptions` → `static readonly` field per page

> **Why batch:** All three are perf-class CA codes. F-C-001 must land first (analyzers persistent) so the rule remains enforced after the sweep.

### Batch L — Hebrew/Culture Sweep [REQUIRES Batch A; Large effort, splits by file]
- F-C-014 (High, Large) — 596 sites: CA1305+CA1304+CA1311+CA1310+CA1862. Real bug class for `MailService` (34 sites — load-bearing for user-visible Hebrew formatting), `NotificationService` (30), `ProfileService` (21)

> **Why batch:** Single concern, single semantic fix per site (`StringComparison.Ordinal` for code-paths; `CultureInfo.InvariantCulture` for storage; explicit `he-IL`/`en-US` for user-facing). Ship per-file PRs to keep review reasonable.

### Batch M — Crypto / Migration Hygiene [security micro]
- F-C-012 (Medium, Small) — `MD5.Create()` in `CacheHelper.GenerateETag` → `XxHash64` (System.IO.Hashing) or HMAC-truncated
- F-A-018 (Low, Trivial) — Document migration rollback flow in DEPLOYMENT_GUIDE
- F-A-020 (Info) — Recorded; no action

### Batch N — Middleware / Layering Cleanup
- F-A-004 (Medium, Medium) — `ApiAuthenticationMiddleware` as proper `AuthenticationScheme` (the 70-line FUTURE REFACTOR)
- F-A-022 (Low, Trivial) — collapse the wrapper into the middleware itself
- F-R-003 (Medium, Small) — extract path-segment matching helper

> **Why batch:** Same middleware family, single concern. F-A-022 is dead-code once F-A-004 lands.

### Batch O — God-Class Decomposition [Large; splits per service into separate PRs]
- F-A-007 MailService (1,654 lines) → `EmailProvider`, `EmailTemplateRenderer`, `EmailLogger`, `IBackgroundEmailDispatcher`
- F-A-008 NotificationService (1,335) → `NotificationFactory`, `NotificationDispatcher`, `NotificationTemplateService`
- F-A-009 ShiftAssignmentService (1,043) → `ShiftEligibilityPolicy`, `ShiftCapacityValidator`, `AssignmentTokenService`
- F-A-010 ImportService (1,033) → `ImportParser`, `ImportValidator`, `ImportReporter`
- F-D-009 (Medium, Medium) — Mail/Notification fire-and-forget unification — *closes naturally* during MailService/NotificationService split
- F-D-004 (Medium, Medium) — API PageModel boilerplate (36 files) — adjacent and worth bundling if dispatcher abstraction lands here
- F-H-005 (Medium, Large) — durable email queue — natural fit during MailService split
- F-H-012 (Medium, Medium) — email idempotency — same
- F-R-004 (Low, Medium) — MailService 11-param constructor — disappears under split
- F-D-006 (Low, Trivial) — email config chain unification — same

> **Why batch:** Each service is its own multi-PR effort. Sequence: NotificationService (smallest non-import) → ShiftAssignmentService → MailService → ImportService.

### Batch P — Readability Cleanup [low-stakes hygiene PR — drop in any sprint]
- F-R-002 (Medium, Trivial) — replace `Console.WriteLine` in DeploymentExportService with `ILogger` (if pre-Build, document why Console)
- F-R-005 (Low, Small) — Phase 2C comments → link to design doc or inline rationale
- F-R-006 (Low, Trivial) — `result` → `validationResult`
- F-R-007 (Low, Small) — wrap `Interlocked.CompareExchange` in `TryAcquireLock`
- F-R-008 (Low, Trivial) — delete stale BackupModel comment
- F-R-009 (Low, n/a) — standardize SECURITY-AUDITED comment template

### Batch Q — Layering / Coupling Refactors [Architecture maintenance]
- F-A-012 (Medium, Small) — `Pages/Admin/Companies.cshtml.cs` direct EF → `ICompanyManagementService`
- F-A-017 (Low, Medium) — `IHttpContextAccessor` injection → facade `ICurrentUserService` for 10 services
- F-A-013 (Medium, Medium) — `IgnoreQueryFilters()` audit (1,297 sites) — add CI rule requiring `// SECURITY-AUDITED:` comment on every call
- F-A-019 (Low, Trivial) — config precedence comment in Program.cs

### Batch R — Lifecycle / Long-tail
- F-A-005 (Medium, Large) — squash 161 migrations to a baseline initial-create
- F-D-007 (Low, Large) — 40+ single-implementation interfaces — audit and remove the no-test-fake ones
- F-D-013 (Info, Small) — execute existing `analyze_duplicates.py` against `Resources/SharedResources*.resx`
- F-D-018 (Medium, Small) — Generic `DeleteEntityModel<TService, TEntity>` for `DeleteChore.cshtml.cs` + `DeleteOnDuty.cshtml.cs`
- F-D-019 (Low, Trivial) — unify `RequestStatus.Declined` vs `JoinRequestStatus.Rejected`

### Batch S — Auth Edge Cases [HiddenBugs long-tail]
- F-H-011 (Medium, Medium) — `VacationApprovalService` re-authorize at processing time, not just request entry
- F-H-014 (Low, Small) — surface NDJSON parse-error inner exceptions in ImportService
- F-H-015 (Low, Small) — document overlap boundary semantics (inclusive vs exclusive) on `ShiftAssignmentService`

### Batch T — Single-finding "drop-in any sprint" PRs
- F-D-003 (Low, Trivial) — 163× catch-log-throw → `ExceptionHelper.LogAndThrow()`
- F-D-005 (Info) — *no action* (positive observation)
- F-D-008 (Info) — `DateTimeFormatHelper` vs `LocalizationService` unify if/when convenient
- F-D-011 (Low, Trivial) — `ValidationBuilder` shared utility
- F-D-012 (Info) — *no action* (necessary boilerplate)
- F-D-014 (Info) — *no action* (correct design)
- F-D-020 (Info) — *no action* (correct usage)
- F-C-017 (Low, Medium) — CA1716 + CA1707 production naming review

---

## F. Recommended Ship Order

The wave model below maximizes parallelism on shippable-day-one items while sequencing the rest behind their gates.

### Wave 1 — Day-One Parallel (no dependencies)

| Batch | Sev | Effort | Status (as of 2026-05-06) |
|---|---|---|---|
| **A — Analyzer infrastructure** | Critical | Small | ✅ **shipped** (closes F-C-001, F-C-009, F-C-018) |
| **B — IconTagHelper bug** | High | Trivial | ✅ **shipped** (closes F-C-011) |
| **C — CalendarHub DI** | Critical → **INVALID** | — | ❌ **voided** — all 3 findings verified invalid (see §E Batch C) |
| **P — Readability cleanup** | Medium | Trivial | not started |
| **M — Crypto/migration hygiene** | Medium | Small | not started |

> A and B shipped together with one build verification (3,398 warnings / 0 errors). C voided after verification — no source change required. **Updated leverage point**: with A's analyzer infrastructure durable, Wave 3 perf/culture sweeps (K, L) become measurable; pull them forward.

### Wave 2 — Security & Data Integrity (sequence within wave; can overlap C from Wave 1)

| Batch | Sev | Effort | Depends on |
|---|---|---|---|
| **E — GrantService null-safety** | High | Medium | — |
| **F — User lifecycle** | High | Medium | — |
| **G — CSRF hardening** | High | Medium | — |
| **D — DirectorService async** | High | Medium | (no dep, but touches middleware — sequence after G to avoid auth-path contention) |

> Run E and F in parallel (different files). G is the smallest blast radius. D is last because converting `IDirectorService` to async ripples through middleware, Razor pages, and authorization handlers.

### Wave 3 — API Migration & Perf (after Wave 1 A)

| Batch | Sev | Effort | Depends on |
|---|---|---|---|
| **H — ApiErrorResponse migration** | High | Medium | — |
| **K — Hot-path perf sweep** | High | Large | A |
| **L — Hebrew/culture sweep** | High | Large | A |
| **I — Task.Run cleanup** | High | Small | — |
| **J — Cache refactor** | Medium | Small | — |

> H is independent (no analyzer dep). K and L need A's persistent analyzer to prevent regression. K and L are each multi-PR — K1/K2/K3 by file group, L by service.

### Wave 4 — Architectural

| Batch | Sev | Effort | Depends on |
|---|---|---|---|
| **N — Middleware cleanup** | Medium | Medium | D (avoid touching middleware twice) |
| **Q — Layering / coupling** | Medium | Medium | — |
| **O — God-class decomposition** | Medium | Large | E, F (don't decompose while bug fixes are in flight) |

> O is the longest tail — sequence: NotificationService → ShiftAssignmentService → MailService → ImportService.

### Wave 5 — Long Tail

| Batch | Sev | Effort | Depends on |
|---|---|---|---|
| **R — Migrations / single-impl audit** | Mixed | Large | — |
| **S — Auth edge cases** | Medium | Medium | E |
| **T — Drop-in PRs** | Low/Info | Trivial-Small | — |

---

## G. Dependency Graph (textual)

```
Wave 1
    A (analyzer infra) ──────┬──► K (perf sweep)
                             └──► L (culture sweep)
    B (icon bug) ────────► (terminal)
    C (CalendarHub) ─────► (terminal — parallel-shippable)
    M (crypto) ──────────► (terminal)
    P (readability) ─────► (terminal)

Wave 2
    E (GrantService) ────────► (terminal) ────► S (auth edges)
    F (user lifecycle) ──────► (terminal)
    G (CSRF) ────────────────► (terminal)
    D (DirectorService async) ────► N (middleware cleanup)

Wave 3
    H (ApiErrorResponse) ────► (terminal)
    I (Task.Run) ────────────► (terminal)
    J (cache refactor) ──────► (terminal)
    K (perf sweep) ──────────► (terminal — but multi-PR)
    L (culture sweep) ───────► (terminal — but multi-PR)

Wave 4
    N (middleware) ──────────► (terminal)
    Q (layering) ────────────► (terminal)
    O (god-class) ───────────► (long-tail multi-PR)

Wave 5
    R, S, T (long tail)
```

**Critical-path observation:** B (IconTagHelper bug) is the only Critical/High finding with **zero dependencies** and **Trivial effort**. Ship it the day this index is read.

---

## H. Verification Notes (for the next session)

When this index seeds future `/plan` invocations:

1. **Re-verify file/line numbers** before opening a fix PR. Per CLAUDE.md memory, recent commits may have changed line offsets even if not the underlying issues. Use `Grep` to relocate the exact site.
2. **Bundled vs. canonical findings** — when implementing Batch D, mark F-A-002 and F-R-001 as "closed via F-H-003" in commit message so the cross-reference is auditable.
3. **Analyzer pass non-regression** — after Batch A, the next `dotnet build` should emit ~3,228 production CA warnings (the recorded transient baseline). If it emits fewer, A's `<AnalysisLevel>` is misconfigured. If more, AnalysisMode crept up.
4. **Hot-path perf measurement** — Batch K's value claim (LoggerMessage allocation savings) deserves a before/after BenchmarkDotNet on `Pages/Auth/Login.cshtml.cs` flow before declaring victory.
5. **Tests** — `ShiftManager.Tests` analyzer profile (CA2007×2150 + CA1707×977) is *intentionally noisy*; do not let analyzer-suppression configuration in `Directory.Build.props` leak into production scope.

---

## I. Severity / Effort Source-of-Truth Reference

Verbatim from the unified schema in the original plan:

| Severity | Meaning |
|---|---|
| Critical | Auth bypass, data loss, tenant leakage, RCE, build-broken-in-Release. Ship-blocker. |
| High | Latent bug that will fire under realistic load/data; or large architecture violation that compounds. |
| Medium | Likely-but-not-certain bug, or maintainability cliff (god class, duplicated logic across 5+ sites). |
| Low | Style, minor naming, single-site dead code, micro-perf. |
| Info | Worth recording but not actionable on its own. |

| Effort | Meaning |
|---|---|
| Trivial | <30 min, single file. |
| Small | <half-day, 1-3 files. |
| Medium | 1-3 days, multi-file or schema change. |
| Large | >3 days, design change, migration, coordinated rollout. |

---

## J. Aggregate Severity Summary (cross-agent, post-dedup)

After collapsing the 7 cross-references identified in §C, the **88 raw findings** resolve to **78 distinct issues**:

| Severity | Count (post-dedup) | Of which already shippable in Wave 1 |
|---|---|---|
| Critical | 2 | 2 (F-A-001, F-C-001) |
| High | 9 | 1 (F-C-011) |
| Medium | 24 | 1 (F-R-002) |
| Low | 25 | many (Batch P, T) |
| Info | 18 | n/a — 12 are positive observations, 6 are governance acknowledgements |
| **Total** | **78** | — |

The 88→78 dedup delta breakdown:
- F-A-002, F-R-001 collapse into F-H-003 (−2)
- F-D-001 collapses into F-C-002 (−1)
- F-A-016 collapses into F-H-010 (−1)
- F-D-015 collapses into F-C-002 (−1)
- F-A-021 collapses into F-A-011 (−1)
- F-A-022 collapses into F-A-004 once F-A-004 lands (−1)
- F-D-016 partial collapse into F-H-010 (−1)
- F-A-018 + F-A-020 + F-D-005 + F-D-014 + F-D-020 + F-D-012 + F-D-017 + F-C-005 + F-C-007 + F-C-008 + F-C-010 + F-C-018 + F-H-017 + F-H-018 + F-H-019 — **info / observational, kept for reference but not in active backlog (15 items)**

---

## Deferred Items

None. This triage produces exactly the deliverable requested in the continuation prompt:
- ✅ Cross-agent top 20 ranked by (severity desc, effort asc) — §B
- ✅ Findings grouped by file — §D
- ✅ Fix batches identified (20 batches A-T, each 3-7 related findings or single-finding bundles) — §E
- ✅ Cross-references missed by dedup chain flagged with canonical owners — §C
- ✅ Recommended ship order with explicit dependency graph — §F + §G

No code fixes were proposed. No source files were modified. The 5 lens reports were not edited. This index is read-only output.

The next natural step is per-batch `/plan` invocation, starting with Batch B (IconTagHelper, 30 min) and Batch A (analyzer infrastructure, 1 day) running in parallel.
