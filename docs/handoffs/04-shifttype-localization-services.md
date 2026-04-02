# Session Handoff — Service-Level ShiftType Name Localization (Deferred Task #2)

---

## Goal
Replace all `shiftType.Name` / `.ShiftType.Name` usages in backend services with `await _localizationService.ResolveShiftTypeNameAsync(shiftType, companyId, culture)` so notifications, exports, and analytics emit the correct Hebrew or English shift type name.

---

## Current Status (verified 2026-04-01)
Infrastructure is complete — only call sites need updating.

**Confirmed still broken:**

| File | Lines | Context |
|---|---|---|
| `Services/TraineeService.cs` | 62, 117, 268, 358 | Trainee/manager notification strings |
| `Services/AnalyticsService.cs` | 233, 236, 335, 381 | Analytics DTO `ShiftType` string field |
| `Services/ScheduleExportService.cs` | 187 | Excel/PDF export fallback name |
| `Services/NotificationService.cs` | ~672 | Weekly digest email HTML |
| `Services/ShiftCalendarService.cs` | ~375, ~452 | RestViolation warning + calendar DTO |
| `Services/TeamCalendarEventAggregator.cs` | ~415 | Team calendar `ShiftEvent.ShiftTypeName` |
| `Services/Api/SwapRequestApiService.cs` | ~492 | `SwapShiftTypeDto.Name` |
| `Services/MasterProgramService.cs` | ~308 | `ShiftTypeNames` list in summary DTO |
| `Pages/Assignments/Manage.cshtml.cs` | ~308 | `CreateShiftRemovedNotificationAsync` arg |
| `Pages/Owner/Blueprints.cshtml.cs` | ~319, 336, 340, 357, 360 | Admin error/success messages |

**Already confirmed fixed:** `Services/WidgetService.cs` — those lines are comments explaining why `.Name` is avoided. No action needed.

---

## Pattern to Follow

Inject `ICompanyLocalizationService` into each service (add field + ctor param).

```csharp
// HTTP-request-context services:
await _localizationService.ResolveShiftTypeNameAsync(
    shiftType,
    companyId,          // from entity.ShiftInstance.CompanyId or _tenantResolver.GetCurrentTenantId()
    CultureInfo.CurrentUICulture.Name);

// Background job (NotificationService weekly digest):
await _localizationService.ResolveShiftTypeNameAsync(shiftType, companyId, "he-IL");
```

**`shiftType.Name` is `[NotMapped]` — cannot be used in EF/LINQ-to-SQL.** All replacements must happen AFTER `.ToListAsync()` materializes entities into memory.

---

## Per-Service Notes

**TraineeService** — Has `ITenantResolver` already. Use `assignment.ShiftInstance.CompanyId` + `CultureInfo.CurrentUICulture.Name`.

**AnalyticsService** — Has `ITenantResolver` already. The `.Select()` projections execute in-memory after `.ToListAsync()` — `await` is fine.

**ScheduleExportService** — `companyId` already in scope at line 59 via `_tenantResolver.GetCurrentTenantId()`.

**NotificationService** — Background job context — use `"he-IL"` hardcoded (no `PreferredLanguage` field on AppUser, app is Hebrew-first).

**ShiftCalendarService** — No `ITenantResolver` present. Needs injection. Use `sa.ShiftInstance.CompanyId`.

**TeamCalendarEventAggregator** — No `ITenantResolver` or `ICompanyLocalizationService`. Both need injection. Use `a.ShiftInstance.CompanyId`.

**SwapRequestApiService** — Has only `AppDbContext` + `ILogger`. `MapToDto` private method needs to become async, or resolve before calling `MapToDto`. Use `assignment.ShiftInstance.CompanyId`.

**MasterProgramService** — Has `ITenantResolver`. In-memory LINQ — break chain into foreach after `ToListAsync()`.

**Assignments/Manage.cshtml.cs** — No `ITenantResolver`. Use `a.ShiftInstance.CompanyId` directly.

**Owner/Blueprints.cshtml.cs** — Already has both `ICompanyLocalizationService` and `ITenantResolver`. Straightforward replacement.

---

## Also: Deferred Task #3 (Low Priority)
`Pages/Owner/Programs.cshtml` lines 63, 155 and `Pages/Owner/MasterPrograms.cshtml` lines 85, 150 have `@st.Name` fallback in `else` branches for custom shift types. Fix: add `LocalizedShiftTypeNames` dictionary to the code-behind PageModel, populate in `OnGetAsync` via `ResolveShiftTypeNameAsync`, use in Razor instead of inline conditional.

---

## Key Files

- `Services/ICompanyLocalizationService.cs` — `ResolveShiftTypeNameAsync` signature
- `Services/CompanyLocalizationService.cs` — implementation (line 325 `.Name` fallback is intentional — do NOT change)
- `docs/deferred-tasks.md §2 and §3` — original full specification
