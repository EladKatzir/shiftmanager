# Feature Completion — Implementation Progress

**Created:** 2026-02-18
**Design Doc:** `docs/plans/2026-02-18-feature-completion-design.md`
**Tracking:** Each section updated by its specialized agent upon completion.

---

## Final Status (all sections complete)

| Section | Status | Implementer | Key Changes |
|---------|--------|-------------|-------------|
| S1: IFeatureFlagService | **DONE** | Direct fix | Migrated `Features:EnableDirectorRole` from IConfiguration to `IsEnabledAsync()` in Program.cs |
| S2: IShiftAssignmentService | **DONE** | Pre-existing | ValidateAssignment + OverrideToken fully wired in Calendar/Table |
| S3: IFriendshipService | **DONE** | Pre-existing | Nav, API, calendar highlighting, CSS, JS, toggle buttons |
| S4: IDutyRotationService | **DONE** | Agent S4 | Created Pages/Admin/DutyRotation/Index (CRUD + queue management) |
| S5: IVacationApprovalService | **DONE** | Agent S5 | Wired into My/Requests + Created Admin/Settings/ApprovalRules page |
| S6: IWidgetService | **DONE** | Agent S6 | Refactored OnCallWidgetViewComponent to use IWidgetService |
| S7: ITechShiftService | **DONE** | Agent S7 | Created Api/TechShift/Eligible endpoint + wired into ShiftAssignmentService |
| S8: Hierarchy Reorder | **DONE** | Pre-existing | Models, API, frontend, migration all exist |
| S9: ISetupTaskService | **DONE** | Pre-existing | Hooked into Molecules/Index, Hierarchy/Create, Admin/Companies |
| S10: IClientTelemetryService | **DONE** | Direct fix | Added CleanupOldDataAsync(30) to DailyNotificationJob |
| S11: FeatureFlags UI | **DONE** | Pre-existing | Fully rewritten with IFeatureFlagService, category groups |
| S12: Appsettings Hardening | **DONE** | Direct fix | Startup validation + _Comment documentation keys |

---

## Build & Test Status
- **Last build:** 0 warnings, 0 errors (after all agents completed)
- **Last test run:** 236/236 passing
- **Duplicate .resx warnings:** Fixed (removed ConfirmCancelRequest, Error_RequestNotFound, Error_RequestAlreadyProcessed duplicates)

---

## Implementation Details

### S1: IFeatureFlagService Consumer Migration
- Migrated `Program.cs:~867` — last stale `Features:EnableDirectorRole` IConfiguration read
- Uses `IsEnabledAsync()` (not sync `IsEnabled()`) because cache isn't warmed at seeding time
- **Intentional exception:** `CompanyIdInterceptor.cs:59` stays on IConfiguration (Singleton/Scoped DI conflict)

### S4: DutyRotation Admin Page
- **Files created:** `Pages/Admin/DutyRotation/Index.cshtml` + `.cshtml.cs`
- 6 POST handlers: Create, Update, Delete, AddUser, RemoveUser, ReorderQueue
- Authorization: `[Authorize(Policy = "Grant:ManageOnDuty")]`
- Queue management with drag-reorder UI (moveQueueItem + submitReorder JS)
- 31 localization keys added (EN + HE)
- Nav link added to `_Layout.cshtml` gated by feature flag + grant

### S5: VacationApproval Wiring
- **Files created:** `Pages/Admin/Settings/ApprovalRules.cshtml` + `.cshtml.cs`
- `IVacationApprovalService` injected into `My/Requests.cshtml.cs`
- `SubmitForApprovalAsync` called on time-off submission (gated by feature flag)
- `CancelRequestAsync` wired to `OnPostCancelRequestAsync` handler
- `RequestStatus.Canceled = 3` enum value confirmed present
- Fixed duplicate .resx keys in both EN and HE resource files
- ~30 localization keys added for ApprovalRules page

### S6: WidgetService Wiring
- Refactored `OnCallWidgetViewComponent.cs` — replaced ~100 lines of direct DB queries with `IWidgetService`
- Fixed `WidgetService.cs`: IgnoreQueryFilters for cross-company contacts, NotMapped ShiftType.Name workaround, ManagerHomeAccess fallback

### S7: TechShift API + Calendar Integration
- **Files created:** `Pages/Api/TechShift/Eligible.cshtml` + `.cshtml.cs`
- Authorization: `[Authorize(Policy = "Grant:ManageShifts")]` + `[IgnoreAntiforgeryToken]`
- Added `/Api/TechShift` to `ApiAuthenticationMiddleware.cs` whitelist
- Added `TechShift` to `ValidationCategory` enum in `IShiftAssignmentService.cs`
- Injected `ITechShiftService` into `ShiftAssignmentService` constructor
- Fixed `ShiftAssignmentServiceTests.cs` — added `ITechShiftService` mock
- Localization: `Error_TechShiftIneligible` key (EN + HE)

### S10: Telemetry Cleanup
- Added `CleanupOldDataAsync(30)` call in `DailyNotificationJob.cs` after `ProcessDailyDigestsAsync`
- Wrapped in try/catch — non-critical failure logged as Warning

### S12: Appsettings Hardening
- **12B:** Startup validation for `ConnectionStrings:Default` and `Seeding:Owner:Email` in `Program.cs`
- **12C:** Added `_*Comment` documentation keys to `appsettings.json` (Backup, Features, Email, Griffin)

---

## Evidence Log

### S1 Consumer Migration Evidence
- `Pages/Admin/*` — No IConfiguration matches
- `Pages/Owner/*` — Uses IFeatureFlagService
- `Pages/Calendar/*` — No IConfiguration matches
- `Controllers/*` — No IConfiguration matches
- `Services/DailyNotificationJob.cs` — No IConfiguration
- `Services/DutyRotationService.cs` — No IConfiguration
- `Services/OnDutyService.cs` — No IConfiguration
- `Pages/Auth/Signup.cshtml.cs` — No IConfiguration
- `Middleware/*` — No Features: matches
- **FIXED:** `Program.cs:858` migrated to `IFeatureFlagService.IsEnabledAsync()`
- **INTENTIONAL:** `CompanyIdInterceptor.cs:59` stays on IConfiguration (Singleton/Scoped conflict)

### S2 Calendar/Table Wiring Evidence
- `IShiftAssignmentService` injected at line 23
- `ValidateAssignment` + `OverrideToken` in OnPostAssignEmployee
- `ValidateAssignment` + `OverrideToken` in OnPostChangeUser
- `ValidateAssignment` + `OverrideToken` in OnPostAddTrainee
- `ValidateAssignment` + `OverrideToken` in OnPostFillRange
- Request DTOs have `OverrideToken` property

### S3 Friends Highlighting Evidence
- `Api/Friends/Ids.cshtml.cs` exists
- `_CalendarRow.cshtml:40` has `data-user-id="@assignment.UserId"`
- `friends-highlight.js` implements toggleFriendsHighlight()
- `.is-friend` CSS at site.css:6984
- `friendsToggle` button on Shifts:136, OnCall:117, Chores:113

### S9 SetupTasks Hook Evidence
- `Molecules/Index.cshtml.cs:149` calls `GenerateTasksForMoleculeAsync`
- `Hierarchy/Create.cshtml.cs:139` calls `GenerateTasksForMoleculeAsync`
- `Hierarchy/Create.cshtml.cs:170` calls `GenerateTasksForCompanyAsync`
- `Admin/Companies.cshtml.cs:325` calls `GenerateTasksForCompanyAsync`
