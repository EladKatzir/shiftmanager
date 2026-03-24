# ShiftManager Pre-Release Adversarial QA Report (Revised)

**Date**: 2026-02-07 | **Branch**: `UiChanging` → `release`
**Stack**: ASP.NET Core 8.0 Razor Pages, EF Core 9 + SQLite, SignalR, QuestPDF, ClosedXML
**Deployment**: Air-gapped IIS on Windows Server, ~300 users max, ~8 concurrent editing managers per molecule
**Severity**: S0=Blocker S1=Critical S2=Major S3=Minor | **Likelihood**: L1=Certain L2=Probable L3=Unlikely | **Detection**: D1=Easy D2=Moderate D3=Hard

---

## Progress Tracker

| Phase | Items | Status |
|-------|-------|--------|
| 1. Data Safety Foundation | 1-5 | COMPLETED |
| 2. Security Hardening | 6-13 | IN PROGRESS (starting item 6) |
| 3. Concurrency & Data Integrity | 14-21 | Not started |
| 4. Operational Readiness | 22-30 | Partially done (28 CorrelationId, some health checks) |
| 5. UX & Accessibility | 31-41 | Not started |
| 6. Performance & Exports | 42-48 | Not started |
| 7. Integration & Final Hardening | 49-66 | Partially done (54 PiiMasker) |

---

## Phase 1: Data Safety Foundation (Est. 12h) - COMPLETED

| # | Item | Status |
|---|------|--------|
| 1 | Automated SQLite backup (`DatabaseBackupService.cs`) | DONE |
| 2 | Pre-migration backup in `Program.cs` | DONE |
| 3 | Graceful shutdown handler (`GracefulShutdownService.cs`) | DONE |
| 4 | Data Protection key persistence | DONE |
| 5 | SQLite WAL mode + busy_timeout | DONE |

## Phase 2: Security Hardening (Est. 24h) - IN PROGRESS

| # | Item | Status |
|---|------|--------|
| 6 | Audit all 70 `IgnoreQueryFilters()` usages - classify, add SECURITY-AUDITED comments, fix unsafe, add tests | TODO |
| 7 | Audit all `@Html.Raw()` for XSS - sanitize user content | TODO |
| 8 | CSRF protection for internal AJAX (`/Api/*`) | TODO |
| 9 | Default credentials warning in OwnerHub/SystemHealth | TODO |
| 10 | Default `AllowPublicSignup` to `false` | TODO |
| 11 | API key salting | TODO |
| 12 | Grant change audit trail | TODO |
| 13 | CSP nonce-based script loading | WONT-FIX -- CSP nonces cause browsers to ignore 'unsafe-inline', which breaks all 80+ inline event handlers (onclick, onchange, onsubmit). Nonces only work on `<script>` elements, not on inline attributes. Reverted in c926fc3. |

## Phase 3: Concurrency & Data Integrity (Est. 20h)

| # | Item | Status |
|---|------|--------|
| 14 | Atomic shift assignment (single transaction) | TODO |
| 15 | Chore/Shift mutual exclusion (single transaction) | TODO |
| 16 | Soft-delete chore exclusion fix (`CanceledAt IS NULL`) | TODO |
| 17 | Vacation approval chain resilience | TODO |
| 18 | Swap request deadlock prevention | TODO |
| 19 | Duty rotation circuit breaker | TODO |
| 20 | Audit log FK integrity (SetNull → Restrict, soft-delete users) | TODO |
| 21 | Director stale company cleanup | TODO |

## Phase 4: Operational Readiness (Est. 28h)

| # | Item | Status |
|---|------|--------|
| 22 | Comprehensive health checks (`/health` vs `/ready`) | PARTIAL |
| 23 | OwnerHub: locked user management | TODO |
| 24 | OwnerHub: audit log search | TODO |
| 25 | Rate limiting memory cleanup | TODO |
| 26 | SystemHealth improvements | TODO |
| 27 | Basic alerting (Windows Event Log) | TODO |
| 28 | Request correlation IDs | DONE |
| 29 | Define "Production" for IIS + deployment validation | TODO |
| 30 | Deployment SOP + runbooks | TODO |

## Phase 5: UX & Accessibility (Est. 40h)

| # | Item | Status |
|---|------|--------|
| 31 | Calendar virtual scrolling (>50 rows) | TODO |
| 32 | Calendar undo toast (5-sec window) | TODO |
| 33 | Mobile calendar assignment UI | TODO |
| 34 | Error messages language fix | TODO |
| 35 | `<noscript>` banner | TODO |
| 36 | Terminology unification ("Day Shifts" vs "On-Duty") | TODO |
| 37 | Swap notification deep links | TODO |
| 38 | Game leaderboard privacy | TODO |
| 39 | Colorblind-accessible calendar | TODO |
| 40 | In-app help/FAQ | TODO |
| 41 | Known issues doc update | TODO |

## Phase 6: Performance & Exports (Est. 22h)

| # | Item | Status |
|---|------|--------|
| 42 | JS/CSS minification + code splitting | TODO |
| 43 | N+1 query optimization | TODO |
| 44 | PDF/Excel export memory safety | TODO |
| 45 | SignalR reconnection backoff | TODO |
| 46 | CSV UTF-8 BOM for Hebrew | TODO |
| 47 | Hebrew font bundling for PDF | TODO |
| 48 | MasterProgram past-date guard | TODO |

## Phase 7: Integration & Final Hardening (Est. 29h)

| # | Item | Status |
|---|------|--------|
| 49 | Griffin ADFS auto-provision placement | TODO |
| 50 | Timezone policy enforcement | TODO |
| 51 | DST-aware shift duration | TODO |
| 52 | ShiftCapacityOverride in auto-generation | TODO |
| 53 | Hebrew niqqud input normalization | TODO |
| 54 | PII redaction in logs | DONE |
| 55 | Document telemetry collection | TODO |
| 56 | Verify EXIF stripping in avatars | TODO |
| 57 | SQLite network share detection | TODO |
| 58 | API versioning documentation | TODO |
| 59 | Feature flag cleanup + production defaults | TODO |
| 60 | Browser compatibility matrix | TODO |
| 61 | Rate limiting per-account | TODO |
| 62 | Escalation path documentation | TODO |
| 63 | Shift history / context-switch messaging | TODO |
| 64 | Stale calendar data indicator | TODO |
| 65 | User data export | TODO |
| 66 | Staged rollout documentation | TODO |
