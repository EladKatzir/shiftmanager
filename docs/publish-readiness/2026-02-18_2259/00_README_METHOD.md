# Audit Methodology

## Execution Context

| Item | Value |
|------|-------|
| **Date** | 2026-02-18 22:59 UTC |
| **Branch** | `merged-canonical` |
| **Commit** | `cd7af8a` (HEAD) |
| **Runtime** | ASP.NET Core 8.0, SQLite, Razor Pages |
| **Platform** | Windows 10 (MSYS2/bash), air-gapped target: IIS on Windows Server |
| **Auditor** | Claude Opus 4.6 (automated release readiness audit) |

## Acceptance Bar

**ZERO findings above LOW severity => READY**

Any finding at MEDIUM, HIGH, or PUBLISH BLOCKER triggers a NOT READY verdict.

## Severity Definitions

| Severity | Definition |
|----------|------------|
| **PUBLISH BLOCKER** | Data loss, security breach, or total feature failure in production |
| **HIGH** | Significant functional defect affecting core workflows |
| **MEDIUM** | Incorrect behavior in non-happy-path scenarios, security weakness, or missing validation that could cause data issues |
| **LOW** | Minor inconsistency, cosmetic issue, or edge case with trivial impact |

## Phases Executed

### Phase A: Repository Cartography
- Full project structure scan (2,410 files)
- TODO/FIXME/stub/placeholder hunt
- Dead code, orphaned routes, unused services scan
- Feature flag status audit
- **Result:** No blockers. 4 benign TODOs, 0 NotImplementedException, 0 dead services.

### Phase B: Runtime & Boot
- `dotnet build` — 0 warnings, 0 errors
- `dotnet test` — 236/236 passing
- Application startup on `http://localhost:5000`
- Health checks: `/health` (200 OK), `/ready` (503 — correctly detecting 6.2% disk free < 10% threshold)
- Background services verified: EmailBackgroundProcessor, DailyNotificationJob, DatabaseBackupService, GracefulShutdownService
- 55 feature flags loaded from DB
- SQLite WAL mode confirmed, busy_timeout=5000ms

### Phase C: End-to-End Critical Journeys
Three journeys tested via code review (3 parallel agents) + Playwright browser testing:

1. **Shift Assignment + View** — Code review of Calendar/Table.cshtml.cs, ShiftAssignmentService.cs, IShiftAssignmentService.cs
2. **Vacation Request + Approve** — Code review of Create.cshtml.cs, Requests/Index.cshtml.cs, TimeOffApiService.cs, VacationApprovalService.cs + Playwright form submission
3. **User Management** — Code review of Admin/Users.cshtml.cs, UserApiService.cs, PasswordHasher.cs, Signup.cshtml.cs

### Phase D: Services & Integration (Code Review Only)
- Griffin authentication: disabled in appsettings, code paths reviewed
- Mail service: disabled in appsettings, email queue + dead-letter DB persistence reviewed
- SignalR hub group validation reviewed (CalendarHub.cs)

### Phase E: Data Layer
- AppDbContext: 68 DbSets, query filters for all IBelongsToCompany entities
- Unique indexes verified on ShiftAssignment, ShiftInstance, AppUser
- CompanyIdInterceptor auto-sets CompanyId

### Phase F: Build & Install
- Clean build: 0 warnings, 0 errors
- Test suite: 236/236 passing
- Lock file: `packages.lock.json` present (RestorePackagesWithLockFile=true)
- All NuGet packages current (EF Core 9.0.9, Swashbuckle 6.5.0, SixLabors.ImageSharp 3.1.11, QuestPDF 2024.10.0, ClosedXML 0.102.2)
- No deprecated packages detected

### Phase G: Packaging & Release
- `Build-Release.ps1` (1,628 lines) — full pipeline: pre-build checks → backup → dotnet publish → air-gapped assets → verify → package
- Output: self-contained win-x64 with ReadyToRun compilation
- IIS `web.config` present: AspNetCoreModuleV2, in-process hosting
- Publish exclusions correctly configured (.csproj lines 19-30)
- `.gitignore` correctly excludes secrets, databases, build artifacts
- `appsettings.Production.template.json` — comprehensive template with env var substitution examples
- Swagger/OpenAPI correctly disabled in production (only enabled in Development)

### Phase H: Observability & Reliability
- Health checks: DiskSpaceHealthCheck (warn 20%, fail 10%), MemoryHealthCheck (warn 600MB, fail 800MB)
- Liveness (`/health`) vs Readiness (`/ready`) separation for IIS/orchestration
- Structured JSON logging in production (ISO 8601 UTC timestamps)
- Windows Event Log for warnings/errors in IIS environments
- 9 startup diagnostic checks: Data Protection keys, network share detection, timezone, default credentials, public signup, feature flag state
- Rate limiting on API endpoints
- Correlation ID middleware for request tracing (F-07)
- Graceful shutdown service: WAL checkpoint on IIS app pool recycle (C-08)

### Phase I: Docs-as-Truth
- `appsettings.Production.template.json` — comprehensive with per-environment instructions
- Build-Release.ps1 — documented pipeline with help
- Swagger/OpenAPI at `/swagger` (development only)
- Security headers documented in code comments (CSP `unsafe-inline` rationale)

## Constraints & Limitations

- **Air-gapped environment**: Griffin and Mail services cannot be tested at runtime — validated by code review only
- **sqlite3 CLI unavailable**: Database queries performed via application UI, not direct SQL
- **Disk space**: Test machine at 6.2% free — `/ready` correctly reports Unhealthy (not a code bug)
- **Test data**: Calendar shifts require molecule+JobType alignment; some UI paths tested with limited seed data

## Commands Run

```bash
# Build
dotnet build --no-restore 2>&1

# Tests
dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj --no-restore --verbosity normal 2>&1

# Runtime
dotnet run --project . --urls "http://localhost:5000" --environment Development

# Health
curl -s http://localhost:5000/health
curl -s http://localhost:5000/ready
curl -s http://localhost:5000/api/v1/version

# Playwright browser testing
# - Homepage load, Calendar/Shifts navigation, scope switcher
# - Vacation request form submission + persistence verification
```
