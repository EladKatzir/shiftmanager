# ShiftManager Open Questions (Blocking Fluency)

**Generated:** 2026-01-12
**Agent:** Antigravity Workspace Intake
**Status:** 20 Questions Requiring Verification

---

## Critical Questions (Security & Data Integrity)

### Q1: Multi-Tenant Isolation Verification
**Question:** Are all tenant-scoped entities properly isolated with query filters?
**Status:** ❓ Partially Verified
**Evidence:** `Data/AppDbContext.cs:436-511` shows query filters for ~20 entities

**Verification Action:**
```powershell
# List all entities without query filters
# Compare DbSets to query filter applications
```

**IDE Action:** Open `Data/AppDbContext.cs`, search for "HasQueryFilter", count occurrences vs DbSet count (34)

**Known Exceptions (Intentional):**
- `OnDuty` — Global table (no CompanyId)
- `OnDutyTypeConfig` — Global configuration
- `ApiKey`, `ApiKeyRequest`, `ApiRequestLog` — Sidecar tables
- `DirectorCompany` — Cross-tenant mapping
- `ProgramDay`, `MasterProgramItem` — Accessed via parent entities

---

### Q2: IgnoreQueryFilters Usage Audit
**Question:** Where is `IgnoreQueryFilters()` used and is it properly guarded?
**Status:** ❓ Unverified

**Verification Action:**
```powershell
Select-String -Path "c:\Users\katzi\Downloads\ShiftManager\**\*.cs" -Pattern "IgnoreQueryFilters" -Recurse
```

**Expected Findings:**
- Director multi-company access
- Database seeding
- Diagnostic page (Owner-only)

**Risk:** Each usage must have proper authorization validation

---

### Q3: API Key Scope Enforcement
**Question:** How are API key scopes validated in controllers?
**Status:** ❓ Unverified
**Evidence:** `docs/context.md:389` mentions "Supported Scopes"

**Verification Action:**
```powershell
Select-String -Path "c:\Users\katzi\Downloads\ShiftManager\Controllers\**\*.cs" -Pattern "HasScope|Scope" -Recurse
```

**IDE Action:** Open any controller in `Controllers/Api/V1/`, search for scope validation logic

---

### Q4: CSRF Protection Status
**Question:** Are all POST forms protected against CSRF attacks?
**Status:** ❓ Unverified

**Verification Action:**
```powershell
# Check for AntiForgeryToken in Razor pages
Select-String -Path "c:\Users\katzi\Downloads\ShiftManager\Pages\**\*.cshtml" -Pattern "AntiForgeryToken|ValidateAntiForgeryToken" -Recurse
```

**Note:** ASP.NET Core Razor Pages have CSRF protection by default for POST handlers, but must verify explicit configuration

---

### Q5: Password Policy Enforcement
**Question:** What password requirements are enforced during signup/password change?
**Status:** ❓ Unverified

**Verification Action:**
- Open `Pages/Auth/Signup.cshtml.cs` — Check password validation
- Open `Pages/My/Profile.cshtml.cs` — Check password change validation
- Open `Models/PasswordHasher.cs` — Check algorithm parameters

**Evidence:** `docs/context.md:523` mentions "PBKDF2 Password Hashing - 100k iterations with SHA256"

---

## High Priority Questions (Core Functionality)

### Q6: TenantResolver Null Handling
**Question:** What happens when TenantResolver returns 0 or null (unauthenticated user)?
**Status:** ❓ Unverified

**Verification Action:**
1. Open `Services/TenantResolver.cs`
2. Trace what value is returned for unauthenticated requests
3. Check how query filters handle CompanyId = 0

**Risk:** If 0 is treated as valid, could leak data with CompanyId = 0

---

### Q7: Signup Company Selection Logic
**Question:** How does the signup page handle company selection without authentication?
**Status:** ❓ Unverified

**Verification Action:**
1. Open `Pages/Auth/Signup.cshtml.cs`
2. Check how companies are queried (must bypass tenant filter)
3. Verify join request creates with correct CompanyId

---

### Q8: Notification Types Complete List
**Question:** What are all notification types and their triggers?
**Status:** ❓ Partially Verified
**Evidence:** `docs/context.md:158` mentions "14 notification types"

**Verification Action:**
1. Open `Models/Support/Enums.cs`
2. Find `NotificationType` enum
3. Map each type to its trigger in code

---

### Q9: Background Job Scheduling Configuration
**Question:** When does DailyNotificationJob run and is it configurable?
**Status:** ❓ Unverified
**Evidence:** `Program.cs:164` registers the job

**Verification Action:**
1. Open `Services/DailyNotificationJob.cs`
2. Find schedule/timer configuration
3. Check if configurable via appsettings

---

### Q10: Vacation Extension Day Logic
**Question:** How does the "vacation until 1 PM" extension work?
**Status:** ❓ Partially Verified
**Evidence:** `docs/context.md:429-434` describes semantics

**Verification Action:**
1. Open `Services/TeamCalendarEventAggregator.cs`
2. Find vacation extension calculation
3. Understand how it affects "busy" status

---

## Medium Priority Questions (Features)

### Q11: Game Scoring Algorithm
**Question:** How does the shift swap game calculate scores?
**Status:** ❓ Unverified
**Evidence:** `appsettings.json` mentions game configuration

**Verification Action:**
1. Open `Pages/Game/Index.cshtml.cs`
2. Find scoring logic or reference to game service
3. Check `GameScore` entity usage

**From appsettings.json (lines 249-258):**
- GamePointsPer3Match: 40
- GamePointsPer4Match: 100
- GamePointsPer5PlusMatch: 200
- GameMegaComboMultiplier: 2

---

### Q12: Ops Console Scheduler Programs
**Question:** How do ShiftPrograms and MasterPrograms work together?
**Status:** ❓ Unverified
**Evidence:** 
- `Data/AppDbContext.cs:50-54` — ShiftProgram, MasterProgram DbSets
- `Services/ShiftProgramService.cs:17140 bytes`
- `Services/MasterProgramService.cs:10590 bytes`

**Verification Action:**
1. Open `Pages/Owner/Programs.cshtml.cs`
2. Open `Pages/Owner/MasterPrograms.cshtml.cs`
3. Understand program definition and execution flow

---

### Q13: Language Override System
**Question:** How do company-specific localization overrides work?
**Status:** ❓ Unverified
**Evidence:**
- `Models/CompanyLocalizationOverride.cs`
- `Services/CompanyLocalizationService.cs:13948 bytes`

**Verification Action:**
1. Open `Pages/Owner/LanguageManagement.cshtml.cs`
2. Understand how overrides are stored and applied
3. Check priority: default → company override

---

### Q14: Email Template Customization
**Question:** Can companies customize email templates? How?
**Status:** ❓ Unverified
**Evidence:**
- `Models/EmailTemplateCustomization.cs`
- `Services/EmailTemplateService.cs:6794 bytes`
- `Pages/Owner/EmailTemplates.cshtml.cs`

**Verification Action:**
1. Open `Services/EmailTemplateBuilder.cs:8249 bytes`
2. Understand template system
3. Check what placeholders are supported

---

### Q15: Data Lifecycle Operations
**Question:** How do Archive and Purge operations work?
**Status:** ❓ Unverified
**Evidence:**
- `Services/ArchiveService.cs:28068 bytes`
- `Services/PurgeService.cs:17192 bytes`
- `Pages/Owner/DataLifecycle.cshtml.cs:18566 bytes`

**Verification Action:**
1. Open services and page model
2. Understand what data can be archived/purged
3. Check if archive is reversible

---

## Lower Priority Questions (Enhancement/Documentation)

### Q16: API Feature Flags Granularity
**Question:** What is the full list of API feature flags?
**Status:** ✅ Verified
**Evidence:** `appsettings.json:20-79` shows all API feature flags

**Finding:**
```json
"Features": {
    "Api": {
        "Users": { "ListEnabled", "GetEnabled", "CreateEnabled", "UpdateEnabled" },
        "Shifts": { "ListEnabled", "GetEnabled" },
        "TimeOff": { "ListEnabled", "GetEnabled", "CreateEnabled", "ApproveEnabled", "DeclineEnabled" },
        "Notifications": { "ListEnabled", "GetEnabled", "MarkReadEnabled", "MarkAllReadEnabled" },
        "Analytics": { "SummaryEnabled" },
        "AuditLogs": { "ListEnabled" },
        "SwapRequests": { "ListEnabled", "GetEnabled", "CreateEnabled", "ApproveEnabled", "DeclineEnabled", "DeleteEnabled" },
        "Chores": { "ListEnabled", "GetEnabled", "CreateEnabled", "UpdateEnabled", "DeleteEnabled" },
        "OnDuty": { "ListEnabled", "GetEnabled", "CreateEnabled", "UpdateEnabled", "DeleteEnabled" },
        "Feedback": { "ListEnabled", "GetEnabled", "CreateEnabled", "UpdateStatusEnabled", "DeleteEnabled" }
    }
}
```

---

### Q17: Import Service Capabilities
**Question:** What data formats/types can ImportService handle?
**Status:** ❓ Unverified
**Evidence:** `Services/ImportService.cs:35064 bytes` (large file)

**Verification Action:**
1. Open `Services/IImportService.cs:2384 bytes`
2. Review public methods
3. Check supported file formats

---

### Q18: Concurrency Token Usage
**Question:** How is optimistic concurrency used for ShiftInstance?
**Status:** ✅ Partially Verified
**Evidence:** `Data/AppDbContext.cs:101-102` — `IsConcurrencyToken()`

**Verification Action:**
1. Find where ShiftInstance updates handle concurrency conflicts
2. Review exception handling for `DbUpdateConcurrencyException`

---

### Q19: Assigner Role Permissions
**Question:** What exactly can the Assigner role do?
**Status:** ❓ Unverified
**Evidence:** `Program.cs:107` — Included in `CanEditChores` policy

**Verification Action:**
1. Search for all policies that include Assigner
2. Review what pages/actions are accessible
3. Document permission matrix

---

### Q20: Database Backup Mechanism
**Question:** How does the Owner backup feature work?
**Status:** ❓ Unverified
**Evidence:** `Pages/Owner/Backup.cshtml.cs:10317 bytes`

**Verification Action:**
1. Open backup page model
2. Understand backup creation (file copy? SQL dump?)
3. Check backup storage location
4. Review restore capability

---

## Verification Priority Matrix

| Question | Priority | Effort | Risk if Unresolved |
|----------|----------|--------|-------------------|
| Q1: Query Filters | 🔴 Critical | Medium | Data leak |
| Q2: IgnoreQueryFilters | 🔴 Critical | Low | Data leak |
| Q3: API Scopes | 🔴 Critical | Medium | Unauthorized access |
| Q4: CSRF | 🟠 High | Low | Security vulnerability |
| Q5: Password Policy | 🟠 High | Low | Weak passwords |
| Q6: Tenant Null | 🔴 Critical | Medium | Data leak |
| Q7: Signup | 🟠 High | Medium | User onboarding issues |
| Q8: Notifications | 🟡 Medium | Low | Documentation gap |
| Q9: Background Job | 🟡 Medium | Low | Operations clarity |
| Q10: Vacation Logic | 🟡 Medium | Low | Feature understanding |
| Q11: Game | 🟢 Low | Medium | Feature understanding |
| Q12: Ops Console | 🟡 Medium | High | Feature understanding |
| Q13: Language Override | 🟡 Medium | Medium | Configuration |
| Q14: Email Templates | 🟢 Low | Medium | Configuration |
| Q15: Data Lifecycle | 🟡 Medium | Medium | Operations |
| Q16: API Flags | ✅ Done | — | — |
| Q17: Import | 🟢 Low | Low | Operations |
| Q18: Concurrency | ✅ Done | — | — |
| Q19: Assigner Role | 🟡 Medium | Low | Role clarity |
| Q20: Backup | 🟡 Medium | Low | Operations |

---

## Quick Verification Commands

### Security Audit Commands
```powershell
cd c:\Users\katzi\Downloads\ShiftManager

# 1. Find IgnoreQueryFilters
Select-String -Path ".\**\*.cs" -Pattern "IgnoreQueryFilters" -Recurse | Select-Object Path, LineNumber

# 2. Find authorization attributes
Select-String -Path ".\**\*.cs" -Pattern '\[Authorize' -Recurse | Select-Object Path, LineNumber

# 3. Find all Razor page handlers
Select-String -Path ".\Pages\**\*.cs" -Pattern "public async Task<IActionResult> On" -Recurse | Select-Object Path, LineNumber

# 4. Count entities with query filters
Select-String -Path ".\Data\AppDbContext.cs" -Pattern "HasQueryFilter" | Measure-Object
```

### Feature Discovery Commands
```powershell
# Find all services
Get-ChildItem -Path ".\Services" -Filter "I*.cs" | Select-Object Name

# Find all controllers
Get-ChildItem -Path ".\Controllers" -Recurse -Filter "*.cs" | Select-Object FullName

# Find all page models
Get-ChildItem -Path ".\Pages" -Recurse -Filter "*.cshtml.cs" | Select-Object FullName
```

---

## Next Steps to Achieve Full Fluency

### Immediate (Today)
1. Run security audit commands (Q1-Q6)
2. Review `IgnoreQueryFilters` usage
3. Verify CSRF protection

### Short-term (This Week)
1. Create test cases for critical paths
2. Complete verification of questions Q7-Q10
3. Document permission matrix for all roles

### Medium-term (This Month)
1. Add unit tests for untested services
2. Complete all open question verification
3. Update risk register based on findings

---

## Files to Review for Full Fluency

**Critical (Review First):**
1. `Data/AppDbContext.cs` — Query filters, entity config
2. `Services/TenantResolver.cs` — Tenant resolution
3. `Middleware/ApiAuthenticationMiddleware.cs` — API security
4. `Pages/Auth/Login.cshtml.cs` — Authentication

**High Priority:**
5. `Services/DirectorService.cs` — Multi-company access
6. `Services/TraineeService.cs` — Shadowing logic
7. `Services/ChoreService.cs` — Conflict detection
8. `Services/NotificationService.cs` — All notification types

**Medium Priority:**
9. `Services/TeamCalendarEventAggregator.cs` — Event priority
10. `Services/MailService.cs` — Email integration
11. `Services/AnalyticsService.cs` — Reporting
12. `Pages/Owner/` (all) — Owner features
