# Deferred Tasks

Tasks identified during development that are deferred for future work.

---

## 1. ChoreType Bilingual Localization

**Priority:** Medium
**Identified:** 2026-03-13
**Context:** ShiftType localization fix (Issue 2) revealed ChoreType has zero i18n infrastructure

**Problem:** `ChoreType` model only has `Name` (internal key) and `DisplayName` (single-language string). Unlike `ShiftType` (which has `Key`+`NameKey`+`CustomName` feeding `ResolveShiftTypeNameAsync`) or `OnDutyTypeConfig` (which has `NameEn`/`NameHe`), ChoreType names are stored in whatever language the admin typed.

**Affected rendering sites:**
- `Pages/Calendar/Chores.cshtml` lines 44, 114 — dropdown and legend use `choreType.DisplayName`
- `Pages/Api/Calendar/GetChoresData.cshtml.cs` line 86 — API returns `ChoreType.DisplayName`

**Required work:**
1. Add `NameEn`/`NameHe` fields to `ChoreType` model (following `OnDutyTypeConfig` pattern)
2. EF migration to add columns
3. Update `ChoreTypeService.cs` and Admin/Organization/ChoreTypes/Index to accept bilingual names
4. Update all rendering sites to select name by `CultureInfo.CurrentUICulture`
5. Migrate existing `DisplayName` values to the appropriate language column

---

## 2. Service-Level Shift Type Name Localization

**Priority:** Medium
**Identified:** 2026-03-13
**Context:** Audit during ShiftType localization fix found 15+ service-level `shiftType.Name` usages

**Problem:** Multiple backend services use `shiftType.Name` (English-only computed property) when constructing user-facing strings (notifications, exports, analytics). The page-level fixes are done, but services still emit English names.

**Affected services (high impact):**
- `Services/NotificationService.cs` line 667 — HTML email notifications
- `Services/TraineeService.cs` lines 62, 117, 268, 358 — Trainee notification messages (4 occurrences)
- `Services/WidgetService.cs` line 230 — On-call contacts widget
- `Services/ScheduleExportService.cs` line 187 — Excel/PDF schedule export
- `Services/ShiftCalendarService.cs` lines 303, 374 — Calendar service DTOs
- `Services/TeamCalendarEventAggregator.cs` line 415 — Team calendar events
- `Services/Api/SwapRequestApiService.cs` line 492 — Swap request API
- `Services/AnalyticsService.cs` lines 233, 236, 335, 381 — Analytics reports

**Affected services (medium impact — admin pages):**
- `Pages/Owner/Blueprints.cshtml.cs` lines 307, 348, 361, 416 — Blueprint admin
- `Services/MasterProgramService.cs` line 308 — Master program names
- `Pages/Assignments/Manage.cshtml.cs` line 303 — Assignment notification

**Required work:**
Each service needs `ICompanyLocalizationService` + `ITenantResolver` injected, then `shiftType.Name` replaced with `await ResolveShiftTypeNameAsync(...)`. For services without tenant context (background jobs, etc.), may need to pass companyId explicitly.

---

## 3. Admin Page Shift Type Name Localization (Razor `else` branches)

**Priority:** Low
**Identified:** 2026-03-13
**Context:** Owner/Programs.cshtml and Owner/MasterPrograms.cshtml use `<loc>` tag helper with `@st.Name` fallback

**Problem:** The `<loc key="@st.NameKey" />` path works correctly for predefined shift types. The `else` branch (`@st.Name`) only triggers when `NameKey` is null — custom shift types. The gap is narrow (custom types without NameKey that could still resolve via `ShiftType_{Key}` pattern) and only affects admin-level Owner pages.

**Affected files:**
- `Pages/Owner/Programs.cshtml` lines 63, 155
- `Pages/Owner/MasterPrograms.cshtml` lines 85, 150

**Required work:**
Add `LocalizedShiftTypeNames` dictionary to the code-behind models and use it in the Razor views instead of the inline `<loc>` / `@st.Name` conditional.
