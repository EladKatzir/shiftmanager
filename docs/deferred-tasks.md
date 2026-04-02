# Deferred Tasks

Tasks identified during development that are deferred for future work.

---

## ~~1. ChoreType Bilingual Localization~~ ✅ DONE (2026-04-02)

**Implemented in session 2026-04-02.** `NameEn`/`NameHe` added to `ChoreType` model following `OnDutyTypeConfig` pattern. Migration `ChoreTypeBilingualNames` seeds `NameEn = DisplayName` for existing rows. All 9 rendering sites updated with `isHebrew && NameHe ?? NameEn ?? DisplayName` resolution. Admin CRUD UI updated with bilingual inputs. Browser-tested: EN/HE switching verified on Chores calendar (dropdown, legend, quick-entry).

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
