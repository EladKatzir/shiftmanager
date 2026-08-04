# Cross-cutting hand audit (main loop) — localization + authorization attribute surface

**Date:** 2026-08-03. **Method:** mechanical whole-repo sweeps run directly in the main loop and
verified by reading the implicated code. Scripts are reproducible under `scratchpad/`
(`resx_parity.py`, `authz_sweep.py`, `dead_grants.py`). Nothing here came from a subagent.

## VERIFIED HEALTHY (negative results — these bound the risk)

These classes were swept and found clean. Recording them so nobody re-audits them blind:

1. **Hebrew/English resx parity is EXACT** — `SharedResources.resx` = 5775 entries,
   `SharedResources.he-IL.resx` = 5775 entries, **0 keys missing in either direction**. The
   feared class (key present in EN, absent in HE -> silently renders English to a Hebrew user)
   **does not exist**.
2. **Page authorization coverage is strong** — of 165 PageModels: 146 carry `[Authorize]`, 18 are
   explicitly `[AllowAnonymous]`, and exactly **1** has no attribute
   (`Pages/My/HelpNavigation.cshtml.cs`, read-only, no handlers). There is no population of
   unprotected mutating pages.
3. **The company switcher is sound.** `ActiveCompanySelectorService.SelectCompanyAsync` has a real
   authoritative gate (`IsMemberAsync`) before writing the cookie, and the cookie is `HttpOnly` +
   `SameSite=Strict` + `Secure`-on-HTTPS. Critically the READ path re-validates too:
   `TenantResolver.GetCurrentTenantId()` checks `member_selected_company` against the
   `MemberCompanyIds` claim — a crafted cookie for a non-member company is rejected.
4. **Bare `[Authorize]` on mutating pages is NOT by itself a defect here.** Spot-verified
   `Pages/Requests/Index.cshtml.cs` (`OnPostApproveTimeOffAsync` -> `RequireManagerAccessAsync`,
   id validation, feature flag, then `ValidateAccessToRequestAsync(currentUser, r.CompanyId)` —
   scoped to the TARGET's company) and `Pages/My/ApiKeys.cshtml.cs` (all three admin handlers call
   `CheckIsAdminAsync(reviewerId)` first). **`Requests/Index.cshtml.cs` is the in-repo REFERENCE
   IMPLEMENTATION of the check that `Table.cshtml.cs:811` omits** — the fix for the calendar IDORs
   does not need a new pattern invented, just this one applied.

### Residual risk (stated design tradeoff, not a defect)
`TenantResolver` validates the switcher cookie against the `MemberCompanyIds` claim, which is a
**login-time snapshot**, not a live DB read (the resolver is synchronous). A membership revoked
mid-session therefore stays honored until the user re-authenticates (cookie TTL 12h). This is
documented in the code. Flagging it as accepted risk so it is a decision, not an oversight.

## FINDING X-L01 (HIGH) — 47 localization keys have NO resource; the UI renders the raw key name

**Confirmed mechanism.** `TagHelpers/LocalizationTagHelper.cs:112` ends with
`output.Content.SetContent(localizedValue)`, which **overwrites the element's inner content
unconditionally**. So the English fallback written inside the tag —
`<loc key="LockedUsers_IPAddress">IP Address</loc>` (`Pages/Owner/LockedUsers.cshtml:97`) — is
**discarded and never rendered**. `IStringLocalizer` returns the KEY NAME when a resource is
missing, so that markup renders the literal text `LockedUsers_IPAddress` on screen, in BOTH
languages.

**Why it survived review:**
- The inner fallback text makes every call site *look* safe to a reader; the tag helper throws it away.
- For single-word keys (`Mode`, `None`, `Pattern`, `Reset`, `Generate`, `Offline`) **the key name is
  itself a plausible English word**, so an English tester sees a normal UI while a Hebrew user sees
  untranslated English. Invisible from both vantage points anyone would test from.

**Worst-hit surfaces** (both are security-administration pages):
- `Pages/Owner/LockedUsers.cshtml` — 6 keys: `LockedUsers_IPAddress`, `_Attempts`, `_ClearAll`,
  `_NoRateLimitedIPs`, `_RateLimitedSignups`, `_RateLimitedSubtitle`
- `Pages/Owner/Hub/RoleTemplates/Edit.cshtml` + `Pages/Owner/Index.cshtml` — 5 keys:
  `RoleTemplates_AddNewGrant`, `_GrantType`, `_TargetJobType`, `_UseOwnJobType`, `_Desc`

Error messages are also affected (`Error_InvalidId`, `Error_RequiredFields`, `Error_NameTooLong`,
`Error_InvalidColor`, `Error_CannotDeleteCompanyWithUsers`, `Error_HierarchyApi_DuplicateName`) —
a user hitting these sees a raw key instead of an explanation.

**Proposed fix:** add all 47 to both resx files. **Durable fix:** a guard test that fails when any
`<loc key="X">` / `Localizer["X"]` reference has no matching resx entry — the sweep in
`scratchpad/resx_parity.py` is already that test in script form.

### Full list (47 keys used with no resource entry)

| Key | Example usage |
|---|---|
| `AddCompany` | see `scratchpad/resx_parity.json` |
| `AddDepartment` | see `scratchpad/resx_parity.json` |
| `Calendar_Empty_SelectMolecule` | see `scratchpad/resx_parity.json` |
| `CompanyMode` | see `scratchpad/resx_parity.json` |
| `Confirm_DeleteEntity` | see `scratchpad/resx_parity.json` |
| `EditShiftGrouping` | see `scratchpad/resx_parity.json` |
| `Error_CannotDeleteCompanyWithUsers` | see `scratchpad/resx_parity.json` |
| `Error_HierarchyApi_DuplicateName` | see `scratchpad/resx_parity.json` |
| `Error_InvalidColor` | see `scratchpad/resx_parity.json` |
| `Error_InvalidId` | see `scratchpad/resx_parity.json` |
| `Error_NameTooLong` | see `scratchpad/resx_parity.json` |
| `Error_RequiredFields` | see `scratchpad/resx_parity.json` |
| `Generate` | see `scratchpad/resx_parity.json` |
| `HomeType_Preview_NoMatch` | see `scratchpad/resx_parity.json` |
| `Justice_Widget_EquityRibbon` | see `scratchpad/resx_parity.json` |
| `LockedUsers_Attempts` | see `scratchpad/resx_parity.json` |
| `LockedUsers_ClearAll` | see `scratchpad/resx_parity.json` |
| `LockedUsers_IPAddress` | see `scratchpad/resx_parity.json` |
| `LockedUsers_NoRateLimitedIPs` | see `scratchpad/resx_parity.json` |
| `LockedUsers_RateLimitedSignups` | see `scratchpad/resx_parity.json` |
| `LockedUsers_RateLimitedSubtitle` | see `scratchpad/resx_parity.json` |
| `Mode` | see `scratchpad/resx_parity.json` |
| `None` | see `scratchpad/resx_parity.json` |
| `Notification_Test_Message` | see `scratchpad/resx_parity.json` |
| `Notification_Test_Title` | see `scratchpad/resx_parity.json` |
| `Offline` | see `scratchpad/resx_parity.json` |
| `Owner_Backup_PrepareForUpdate` | see `scratchpad/resx_parity.json` |
| `Owner_EmailConfig_ApiKey_Placeholder` | see `scratchpad/resx_parity.json` |
| `Pattern` | see `scratchpad/resx_parity.json` |
| `RenameArea` | see `scratchpad/resx_parity.json` |
| `RenameDepartment` | see `scratchpad/resx_parity.json` |
| `RenameMolecule` | see `scratchpad/resx_parity.json` |
| `RenameProject` | see `scratchpad/resx_parity.json` |
| `Reset` | see `scratchpad/resx_parity.json` |
| `RoleTemplates_AddNewGrant` | see `scratchpad/resx_parity.json` |
| `RoleTemplates_Desc` | see `scratchpad/resx_parity.json` |
| `RoleTemplates_GrantType` | see `scratchpad/resx_parity.json` |
| `RoleTemplates_TargetJobType` | see `scratchpad/resx_parity.json` |
| `RoleTemplates_UseOwnJobType` | see `scratchpad/resx_parity.json` |
| `Success_AreaRenamed` | see `scratchpad/resx_parity.json` |
| `Success_DepartmentRenamed` | see `scratchpad/resx_parity.json` |
| `Success_GroupingEdited` | see `scratchpad/resx_parity.json` |
| `Success_JobTypeEdited` | see `scratchpad/resx_parity.json` |
| `Success_MoleculeRenamed` | see `scratchpad/resx_parity.json` |
| `Success_ProjectRenamed` | see `scratchpad/resx_parity.json` |
| `VacationApproval_Error` | see `scratchpad/resx_parity.json` |
| `VacationApproval_NotYourRequest` | see `scratchpad/resx_parity.json` |


## FINDING X-S01 (HIGH) — `/Calendar/Week`, `/Month`, `/Day` expose every duty assignment in the deployment

**Method that found it:** a mechanical sweep for methods that ACCEPT a scope parameter and never READ
it (`scratchpad/unread_params.py`) — the same technique that caught `BusyService` and
`ScopeFilterService`.

`Pages/Calendar/Week.cshtml.cs:337`, `Pages/Calendar/Month.cshtml.cs:423`,
`Pages/Calendar/Day.cshtml.cs:313` — all three declare:

```csharp
private async Task<List<CalendarItemViewModel>> LoadOnDutiesAsync(
    List<int> companyIds, List<DateOnly> dates, int currentUserId)
{
    // OnDuty is global - must use IgnoreQueryFilters
    var onDuties = await _db.OnDuties.IgnoreQueryFilters()
        .Include(od => od.User)
        .Where(od => od.Date >= dates.First() && od.Date <= dates.Last() && od.CanceledAt == null)
        .ToListAsync();
```

`IgnoreQueryFilters()` removes tenant isolation and **no company/area/molecule predicate replaces
it**. The `companyIds` parameter — which the caller passes precisely to scope this — is never
referenced in the body. The result is projected with
`AssigneeName = onDuty.User?.DisplayName`, so an authenticated user sees the name and duty date of
**every duty officer in every company, area and project** for the visible range.

The comment *"OnDuty is global - must use IgnoreQueryFilters"* is the meta-pattern again: it
justifies the filter bypass and never adds the compensating WHERE.

**Reachability caveat:** these three legacy pages render this path when `FF_EXCEL_CALENDAR_SHIFTS` is
OFF (the flag's designed fallback state). They lost their nav leaves but remain reachable by URL.
Confirm the flag's production value before rating operational severity.

**Proposed fix:** filter by the `companyIds` already being passed (or the caller's resolved scope),
and treat any `IgnoreQueryFilters()` without a compensating predicate as a build-breaking review gate.

## FINDING X-S02 (LOW) — WidgetService per-user preference API is an unimplemented stub

`Services/WidgetService.cs:420` `GetUserWidgetPreferencesAsync(int userId)` ignores `userId` and
returns hardcoded defaults (*"Try to load from UserPreferences table if it exists / For now, return
defaults"*). `:434` `SaveUserWidgetPreferencesAsync(int userId, WidgetPreferences preferences)` is a
no-op (*"Widget preferences are stored client-side in localStorage. Server-side persistence planned
for future release."*).

**Not a security defect** (nothing leaks; nothing is written). It is a misleading API: callers
believe they are persisting per-user preferences and silently are not. Either implement it or remove
it from the interface so the intent is honest.

## Sweep results — the other unread-parameter hits (NOT yet individually verified)

`scratchpad/unread_params.json` lists 12 methods total. Verified above: the 3 calendar
`LoadOnDutiesAsync` (real), the 2 `WidgetService` preference methods (stub). Known already:
`BusyService.GetBusyStatesAsync(moleculeId)`, `ScopeFilterService.ValidateScopeAccessAsync(scopeId)`.
**False positive:** `Pages/Admin/Organization/Index.cshtml.cs:47 CompanyVM(... MoleculeId ...)` is a
positional record — its parameters are used implicitly by the compiler.

Still to verify individually:
- `Services/BusyService.cs:166 ValidateAsync(BusyTarget target, int userId, int actorUserId, ...)` —
  **`actorUserId` unread in a VALIDATION method**. Second unread security param in this one service;
  worth a careful look.
- `Services/MailService.cs:129 InjectOptOutFooter(string html, int recipientUserId, int companyId)` —
  `companyId` unread.
- `Services/HomeTypeService.cs:496 GetHomeDatesForRange(..., List<int> userIds)` — `userIds` unread.
- `Services/WidgetService.cs:509 GetOfficeNumbersAsync(int companyId)` — `companyId` unread
  (possible cross-tenant contact disclosure — check before trusting).

## FINDING X-L02 (LOW) — Hebrew glossary violation: company rendered as "חברה" not "דסק"

`Error_TimeOff_UserNotInCompany` (he-IL) = `אדם זה אינו שייך לחברה זו.` — per the project glossary
Company must render as **דסק** in Hebrew. Exactly 1 violation across all 5775 Hebrew entries, so
the glossary is otherwise applied consistently.
