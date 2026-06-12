# Per-User Calendar Row Reordering — Design Spec

**Date:** 2026-06-12
**Phase 1 items:** 1.4 (drag-reorder doesn't work on Shifts by-people / by-shift) + 1.5 (reorder rows/people inside categories across all calendars, including Overview)
**Branch:** `dev`

## 1. Problem

Calendar rows (`<tr data-row-id="user-14" data-group-id="...">`) cannot be reordered by dragging. A group grip handle (`⋮⋮`) is rendered on group **headers** but is inert, and no row-level drag exists anywhere. Users want to arrange people/shifts inside their categories in a personally meaningful order and have it stick, across all calendar surfaces.

## 2. Decisions (locked with stakeholder)

1. **Scope of order = PER-USER (personal view).** Each user arranges their own ordering; nobody else is affected. ⇒ no permission gating (every user may reorder their own view); no cross-user conflicts.
2. **Drag boundary = WITHIN category only.** A drag reorders a row inside its current group; it never moves a row to another group (membership stays owned by the existing molecule-scoped "move" feature).
3. **Apply mechanism = HYBRID.** Server renders rows already in the user's saved order (no flash, correct first paint); a client module owns the live drag, optimistic DOM update, and re-application after the in-place grid refresh.

## 3. Surfaces covered

All calendars that render through the shared `ExcelCalendarTable` view component (verified: `Pages/Calendar/{Shifts,Chores,OnCall,Overview}.cshtml` all call `Component.InvokeAsync("ExcelCalendarTable", Model.CalendarData)`):

- **Shifts — user mode** (rows = users, grouped by category or distribution list)
- **Shifts — shift mode** (rows = shift types, grouped by category)
- **Chores** (rows = users)
- **On-Call** (rows = duty types / users)
- **Overview** (rows = users, ungrouped → single flat list, `GroupId = ""`)

## 4. Data model

New entity `Models/UserCalendarRowOrder.cs`:

| Column | Type | Purpose |
|---|---|---|
| `Id` | int PK | |
| `UserId` | int FK→AppUser | whose personal order this is |
| `ContextKey` | string (≤128) | identifies the view (see §5) |
| `GroupId` | string (≤64) | category/list within the view; `""` for ungrouped (Overview) |
| `RowId` | string (≤64) | `user-14` / `shift-7` / `dutytype-3` |
| `SortOrder` | int | position within (ContextKey, GroupId) |

- **Unique index** `(UserId, ContextKey, GroupId, RowId)`.
- **Query index** `(UserId, ContextKey)` for the render-time load.
- **NOT `IBelongsToCompany`** — it is user-scoped personal preference, read only for the current user, so there is no cross-tenant read path. (Confirmed acceptable: the RowId/ContextKey reference a company's calendar, but the row is never read for any user other than its owner.)
- **Cleanup on user deactivation/deletion:** delete the user's `UserCalendarRowOrder` rows in the existing `Pages/Admin/Users.cshtml.cs` deactivation cleanup (alongside the existing TraineeUserId/assignment/grant cleanup).
- **EF migration** `AddUserCalendarRowOrder`. Register `DbSet<UserCalendarRowOrder>` + the indexes in `AppDbContext`. No global query filter (not company-scoped).

## 5. ContextKey

Identifies "a list of groups+rows the user is arranging." **Computed once, server-side**, by each PageModel and passed to the component, then echoed to the client via `data-reorder-context` on `.excel-calendar` so client POSTs use the identical key.

| Calendar | ContextKey format | Example |
|---|---|---|
| Shifts | `shifts:{moleculeId}:{jobTypeId}:{mode}` | `shifts:9:2:user` |
| Chores | `chores:{moleculeId}` | `chores:9` |
| On-Call | `oncall:{areaId}` | `oncall:4` |
| Overview | `overview:{companyId}` | `overview:5` |

Notes:
- `mode` (`user`/`shift`) is part of the Shifts key because the rows differ entirely between modes.
- Distribution-list grouping vs category grouping in Shifts user-mode share the same ContextKey; the `GroupId` differentiates (`list-3` vs `category-5`). If the user switches grouping, un-positioned groups simply fall back to default order.

## 6. Server-side application (the one chokepoint)

Add `string? RowOrderContextKey` to `ExcelCalendarTableViewModel`. Each PageModel sets it when building `CalendarData`.

Convert the view component to **async** (`InvokeAsync`) if not already, inject a new `ICalendarRowOrderService`, and when `RowOrderContextKey != null`:

1. Resolve current `userId` from `ViewComponentContext...HttpContext.User` (`int.TryParse` on `NameIdentifier`).
2. `var order = await _rowOrderService.GetOrderMapAsync(userId, contextKey);` → `Dictionary<(string GroupId, string RowId), int>`.
3. **Stable-sort `model.Rows`** so that within each `GroupId`: positioned rows come first in ascending `SortOrder`, then un-positioned rows (e.g. a newly-added person) in their original default order. Rows in different groups keep their group's relative placement; group order itself is unchanged (groups still sort by `ExcelCalendarGroup.SortOrder`).
   - Implementation: project each row to a sort key `(positioned ? 0 : 1, positioned ? savedSortOrder : originalIndex)` partitioned by group, using a stable ordering (`OrderBy` is stable in LINQ-to-Objects).

`Default.cshtml` already filters `Rows.Where(r => r.GroupId == group.Id)` (order-preserving), so once `model.Rows` is in the right order, rendering reflects it with no view change.

`ICalendarRowOrderService` (`Services/CalendarRowOrderService.cs`):
- `Task<Dictionary<(string,string),int>> GetOrderMapAsync(int userId, string contextKey)`
- `Task SaveOrderAsync(int userId, string contextKey, string groupId, IReadOnlyList<string> rowIds)` — upsert: delete existing rows for `(userId, contextKey, groupId)` then insert `rowIds` with `SortOrder = index`. (Whole-group replace = simplest correct semantics.)

## 7. Client module `wwwroot/js/calendar-row-reorder.js`

Self-invoking module, mirrors the codebase's vanilla-JS pattern.

- **Init + rebind:** on `DOMContentLoaded` and on the existing `calendar:grid-refreshed` event (the hook added in the lockup fix), `(re)enable()` runs: find `.excel-calendar[data-reorder-context]`; for each data row, inject a **drag grip** (`⋮⋮`, `.excel-calendar__row-grip`) into the row's **name cell** (the sticky first column — never a date cell, so no conflict with Quick Entry's cell clicks). The grip is `draggable="true"`; the `<tr>` is not.
- **Drag:** `dragstart` on a grip captures `{ rowId, groupId }` and adds `.is-dragging`. `dragover` on rows computes an insert position **only within the same `groupId`** (cross-group dragover → `dropEffect='none'`, no drop line). A drop-line indicator (`.excel-calendar__drop-line`) shows the target slot.
- **Drop:** reorder the `<tr>` nodes within the group in the DOM (optimistic), collect the group's new `rowId` order, POST it, and remove indicators. On POST failure → toast error + re-apply server order on next refresh.
- **Keyboard a11y:** when a row (or its grip) is focused, `Alt+ArrowUp` / `Alt+ArrowDown` moves the row within its group and POSTs — same persistence path, no mouse required. Grip has `aria-label` (localized) and `role="button"`.
- **Touch:** pointer/touch drag via the grip is supported (the grip is a small explicit target, avoiding scroll-vs-drag ambiguity). If touch proves unreliable, keyboard reorder is the fallback; ship desktop drag first.

## 8. API

`POST /Api/Calendar/SaveRowOrder` — new Razor page handler `Pages/Api/Calendar/SaveRowOrder.cshtml.cs`, decorated `[Authorize]` + `[IgnoreAntiforgeryToken]` consistent with the sibling `QuickAdd*` endpoints. It requires authentication, so it must **not** be added to any anonymous bypass (`Program.cs` AllowAnonymousToPage / `ApiAuthenticationMiddleware` internal-endpoint list).

Body: `{ contextKey: string, groupId: string, rowIds: string[] }`.
Server: resolve `userId` from claims; basic validation (`rowIds` non-empty, lengths within column limits, count ≤ 500); `await _rowOrderService.SaveOrderAsync(...)`; audit-log `RowOrderChanged`; return `{ success: true }`.

Reads happen server-side at render time, so **no GET endpoint** is needed.

## 9. CSS

Add to the calendar stylesheet (follow `[[css_bundling_site_css_import]]` — calendar.css edits need a cache refresh; this module's own selectors are fine):
- `.excel-calendar__row-grip` — hidden by default, revealed on row hover / focus-within; `cursor: grab`; sticky-column aware (RTL via `inset-inline`).
- `.is-dragging` (row ghosting), `.excel-calendar__drop-line` (insert indicator).
- Respect `prefers-reduced-motion` (no transform animations when set).

## 10. Testing

- **Unit (service):** `GetOrderMapAsync` returns correct map; `SaveOrderAsync` replaces a group's order; ordering helper interleaves positioned-then-unpositioned correctly; **user isolation** (user A's order never affects user B). Use the real-SQLite fixture (`SqliteDbContextFixture`), not InMemory.
- **Unit (component):** view component leaves rows in default order when `RowOrderContextKey` is null; applies saved order when present; un-positioned new row lands after positioned ones.
- **API:** save persists; rejects unauthenticated; rejects oversized payload; round-trips through render.
- **Browser (Playwright):** drag row within a group → order changes → reload (full server render) → order persisted; cross-group drag is rejected; `Alt+↑/↓` reorders; second user sees their own (unaffected) order.

## 11. Out of scope (YAGNI)

- Reordering **groups/categories** themselves (only rows within a group). The inert header grip stays out of scope here.
- Moving rows **between** groups (membership — owned by the existing "move" feature).
- A shared/global order or admin-curated order (explicitly chose per-user).
- A GET endpoint / cross-device live sync beyond what server-render already provides.

## 12. Risks / edge cases

- **New/removed rows:** un-positioned rows fall back to default order (handled in §6); removed rows leave stale `UserCalendarRowOrder` rows — harmless (ignored at render; optionally pruned lazily on save).
- **Grouping switch (list ↔ category) in Shifts user-mode:** different `GroupId` namespaces; orders coexist, each grouping remembers its own.
- **Draft Mode:** reordering is orthogonal to draft assignments; ContextKey does not include draft session, so order is shared between live and draft views (intended).
- **Quick Entry coexistence:** grip lives in the name cell only; cell clicks (date cells) remain Quick Entry's. Verified no overlap.
