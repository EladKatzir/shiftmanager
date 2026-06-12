# Phase 4 — Design Proposals (items 4.1, 4.3, 4.5)

**Date:** 2026-06-13
**Status:** PROPOSALS — awaiting stakeholder approval before implementation.
Items 4.2 (Techno job type) and 4.4 (Test Summon) were buildable mechanically and are **already done + committed**. The three below need product decisions first.

---

## 4.1 — Vacation requests for admins

### What the investigation found
- Admin roles (Kabar/Manager/Lead) **already CAN submit time-off requests** — the form lives at `/My/Requests` (`Pages/My/Requests.cshtml`), gated only by `[Authorize]` (no role/grant block). The handler `OnPostTimeOffAsync` sets `UserId = current user`; nothing prevents a manager being the requester.
- Self-approval is already blocked (`VacationApprovalService.ApproveAsync` → `CannotApproveSelf`), and the approval router (`GetApprovalRouteAsync`) routes a request to a *different* approver based on `VacationApprovalRule`.
- `/Requests/Index` is **approval/review only** (3 tabs: Pending Time-Off, Pending Swaps, Approved). It has **no request form**. Admins see the approval view there; the request form is on a separate page they may not discover.

### So the real gap is UX surfacing, not capability
The user wants admins to have a **request tab at `/Requests/Index#timeoff`** matching regular users, while keeping their approval capabilities.

### Proposed approach (recommended)
Add a **"Request Time Off" card** (or sub-tab) to `/Requests/Index` that renders the same time-off request form used on `/My/Requests` — shown to admin/approver roles (who currently only see the approval list there). Regular users already have the form on `/My/Requests`; this brings parity by surfacing it where admins live.

- **Reuse**, don't duplicate: extract the request form into a partial (`_TimeOffRequestForm.cshtml`) shared by `/My/Requests` and `/Requests/Index`, posting to the existing `OnPostTimeOffAsync` (move/duplicate the handler onto the Requests page model, or post to `/My/Requests` handler).
- Keep the approval list exactly as-is; the request card is additive.

### Decisions needed from you
1. **Placement:** a distinct "Request my time off" **card at the top of the #timeoff tab**, or a separate **4th tab**? (Recommend: a collapsible card at the top of #timeoff — keeps approval list primary, request secondary.)
2. **Which roles** see the request card on `/Requests/Index`? All approver roles (anyone with `ManagerHomeAccess`/`AdminAccess`), or a specific set (Kabar, מפק"מ, Lead)? (Recommend: all approver roles — parity is the goal.)
3. **Who approves an admin's own request?** Today it routes via `VacationApprovalRule` by the requester's JobType → a different approver, with self-approval blocked. Is that acceptable, or do Kabar/מפק"מ requests need an explicit higher-tier approver / auto-approve threshold? (Recommend: keep existing routing; add a rule only if a gap surfaces.)
4. **Visibility:** an admin's own pending request will appear in the approval list of their peers (same company). Acceptable, or should self-requests be hidden from the requester's own approval list? (Recommend: hide the requester's own request from *their* approval list to avoid the confusing "approve" button on themselves.)

**Effort:** Small–medium (mostly a shared partial + conditional render + the decisions above).

---

## 4.3 — Owner Email Config dashboard (all-companies overrides)

### What the investigation found
- `/Owner/EmailConfig` **already exists** (`Pages/Owner/EmailConfig.cshtml`), gated by `[Authorize(Policy = "Grant:ConfigureEmailSettings")]`. It is **per-company**: shows the current company's effective config, whether it has an override (`HasCompanyOverride`), and a **"Revert to Global"** action (`OnPostRemoveOverrideAsync` → `DeleteCompanyOverrideAsync`, a hard delete of the override row).
- Model `EmailConfig` (`CompanyId` null = global, non-null = per-company override). Service `IEmailConfigService`: `GetEmailConfigAsync` (company → global → null fallback), `HasCompanyOverrideAsync`, `DeleteCompanyOverrideAsync`.

### The ask
A **dashboard showing which companies have mail overrides**, with a **toggle to disable** those overrides.

### Proposed approach (recommended)
Add an **"All company overrides" section/table** to the existing `/Owner/EmailConfig` page (Owner-only): one row per `EmailConfig` where `CompanyId != null`, columns = Company, Enabled, From address, Last updated / by, and a per-row **disable** action. This reuses the page's existing auth + layout.

### Decisions needed from you
1. **"Disable" semantics** — three options:
   - (a) **Hard delete** the override (reverts that company to global) — matches the existing "Revert to Global" behavior. Simplest; no schema change.
   - (b) **Soft toggle** — flip the existing `EmailConfig.Enabled` flag to false (keeps the override row but inactive). Reversible without re-entering the API key. **Requires** confirming `Enabled` already exists on the model (it does) and that the resolver treats `Enabled=false` correctly.
   - (c) **Both** — a toggle to disable + a separate delete.
   *(Recommend (b) soft toggle — reversible, least destructive; "disable" literally means disable.)*
2. **New page vs. section** on existing `/Owner/EmailConfig`? (Recommend: a section on the existing page — it's already the email-config home.)
3. **Columns / actions** to show per company (company name, override enabled?, from-address, last-updated-by, disable/enable button, link to that company's detail).
4. **Audit:** log each enable/disable with actor + company? (Recommend: yes — mirror existing audit calls.)

**Effort:** Small–medium (one query for all overrides + a table + a toggle handler; schema-free if soft-toggle uses existing `Enabled`).

---

## 4.5 — "Who is on Shift" home dashboard (Area Admins & מפק"מ)

### What the investigation found
- Home page: `Pages/Home/Index.cshtml(.cs)` with role-segmented widgets (employee/manager/director blocks).
- Assignments: `ShiftAssignment` → `ShiftInstance` (`WorkDate`, `ShiftTypeId`, `StaffingRequired`) → `ShiftType` (`Key`, `Name`, `Start`/`End` TimeOnly, `AreaId`, `Scope`, `MoleculeId`).
- Existing widget pattern: `OnCallWidgetViewComponent` + `IWidgetService` (good template for a card widget).
- Per-user persistence pattern available: `UserCalendarRowOrder` / `CalendarRowOrderService` (contextKey + items) — or a small new entity `UserMonitoredShift`.
- Roles: "AreaAdmin" role + area-scoped grants (`EditArea`, etc.); "מפק"מ" is a position/title, likely identified by an area-scoped grant — **needs confirmation of the exact grant/role key**.

### Proposed approach (recommended)
A new **home dashboard widget** ("Who's on shift") for Area Admins / מפק"מ:
- A **shift picker** (multiselect of shift types within their area/molecule scope) → persisted per-user (new `UserMonitoredShift` entity, mirroring the reorder-persistence pattern, or a `UserCalendarRowOrder` contextKey like `monitored-shifts:{area}`).
- The widget renders **one cube per selected shift**: shift name on top; inside, the names of users currently on that shift. Color by staffing (green=full, amber=under, red=critical).
- Data via a new `IWidgetService.BuildWhoIsOnShiftAsync(userId, selectedShiftTypeIds)` querying today's `ShiftInstance` + `ShiftAssignment` for those shift types.

### Decisions needed from you
1. **"Currently on shift" definition:** (a) shift whose `Start..End` window contains *now* (true "currently on"), or (b) anyone assigned *today* regardless of time? (Recommend (a) for a live "who's on now" board; fall back to today's assignment if the shift has no time window.)
2. **Selection scope:** which shifts can they pick — only their area/molecule's shift types, or any? Include trainees in the cube? (Recommend: their scope only; show trainees with a small "trainee" marker.)
3. **Cube granularity:** one cube per **shift type** (today's instance), or per **shift instance**? (Recommend: per shift type, showing today's instance.)
4. **Refresh:** static-on-load with a manual refresh, or live (SignalR/poll every ~30s)? (Recommend: start static + manual refresh; the app already has SignalR for calendars if you later want live.)
5. **Persistence scope:** per-user personal selection (recommended), and does it reset daily or persist?
6. **Exact role/grant** that unlocks the widget — confirm the "AreaAdmin" + "מפק"מ" identifiers (grant key) so the gate is correct.

**Effort:** Medium–large (new entity + migration, a save-selection API, a widget view component + cube CSS, the "who's on now" query, role gate). This is the biggest Phase 4 item and benefits most from a focused brainstorm + spec + plan (like the reorder feature).

---

## Recommended order for next session
1. **4.3** (smallest — table + toggle on an existing page).
2. **4.1** (shared partial + conditional surface).
3. **4.5** (full brainstorm → spec → plan → build, like the reorder feature).

Each should go through the normal brainstorm → approve → (spec/plan for 4.5) → build → verify cycle.
