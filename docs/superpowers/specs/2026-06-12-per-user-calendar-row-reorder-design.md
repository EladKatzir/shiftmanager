# Per-User Calendar Reordering (Categories + Rows) — Design Spec

**Date:** 2026-06-12
**Phase 1 items:** 1.4 (drag-reorder doesn't work on Shifts by-people / by-shift) + 1.5 (reorder rows/people inside categories across all calendars, including Overview)
**Branch:** `dev`

## 1. Problem

Neither calendar **rows** (`<tr data-row-id="user-14" data-group-id="category-5">`) nor the **categories/groups** that contain them can be reordered by dragging. A group grip handle (`⋮⋮`) is rendered on group **headers** but is inert, and no row-level drag exists anywhere. Users want to (a) arrange the **order of categories** and (b) arrange the **order of people/shifts inside each category** — in a personally meaningful order that sticks, across all calendar surfaces.

This feature delivers **two** kinds of reordering, both per-user:
- **Category reordering** — drag a group header to move that whole category (header + all its rows) up/down among the other categories.
- **Row reordering** — drag a row to reposition it within its own category.

## 2. Decisions (locked with stakeholder)

1. **Scope of order = PER-USER (personal view).** Each user arranges their own ordering; nobody else is affected. ⇒ no permission gating (every user may reorder their own view); no cross-user conflicts.
2. **Two reorder axes, both supported:** (a) **categories** reorder among themselves; (b) **rows** reorder WITHIN their current category. A row drag never moves a row to *another* category (membership stays owned by the existing molecule-scoped "move" feature) — to change a row's category, that's a separate operation.
3. **Apply mechanism = HYBRID.** Server renders rows already in the user's saved order (no flash, correct first paint); a client module owns the live drag, optimistic DOM update, and re-application after the in-place grid refresh.

## 3. Surfaces covered

All calendars that render through the shared `ExcelCalendarTable` view component (verified: `Pages/Calendar/{Shifts,Chores,OnCall,Overview}.cshtml` all call `Component.InvokeAsync("ExcelCalendarTable", Model.CalendarData)`):

- **Shifts — user mode** (rows = users, grouped by category or distribution list)
- **Shifts — shift mode** (rows = shift types, grouped by category)
- **Chores** (rows = users)
- **On-Call** (rows = duty types / users)
- **Overview** (rows = users, ungrouped → single flat list, `GroupId = ""`)

## 4. Data model

New entity `Models/UserCalendarRowOrder.cs`. **One table stores both axes**, distinguished by the `GroupId` column:

| Column | Type | Purpose |
|---|---|---|
| `Id` | int PK | |
| `UserId` | int FK→AppUser | whose personal order this is |
| `ContextKey` | string (≤128) | identifies the view (see §5) |
| `GroupId` | string (≤64) | **row order:** the category/list the row lives in (`category-5`, `list-3`). **category order:** the empty string `""` (the context-level namespace that orders the groups themselves). Overview's single flat list also uses `""` for its rows. |
| `RowId` | string (≤64) | **row order:** `user-14` / `shift-7` / `dutytype-3`. **category order:** the group id being positioned, e.g. `category-5` / `list-3`. (Row ids and group ids never collide — different prefixes.) |
| `SortOrder` | int | position within (ContextKey, GroupId) |

So entries with `GroupId = ""` are the **category ordering** for the context (their `RowId`s are group ids); entries with a non-empty `GroupId` are the **row ordering** inside that group.

Note: in Overview (ungrouped) the rows live under `GroupId=""`, which is the *same* namespace category-ordering would use elsewhere — harmless because Overview has no groups, so there are no `category-*` RowIds to collide with its `user-*` rows.

- **Unique index** `(UserId, ContextKey, GroupId, RowId)`.
- **Query index** `(UserId, ContextKey)` for the render-time load (loads both axes in one query).
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
2. `var order = await _rowOrderService.GetOrderMapAsync(userId, contextKey);` → `Dictionary<(string GroupId, string RowId), int>` (covers both axes in one load).
3. **Sort categories** (`model.Groups`): using the `GroupId=""` entries whose `RowId` matches an actual group id, stable-sort `model.Groups` so positioned groups come first in ascending `SortOrder`, then un-positioned groups in their existing `ExcelCalendarGroup.SortOrder`. (Default.cshtml iterates `model.Groups` in list order, so reordering the list reorders the rendered categories.)
4. **Sort rows within each group** (`model.Rows`): for each group's rows (matched by `GroupId`), stable-sort so positioned rows come first in ascending saved `SortOrder`, then un-positioned rows (e.g. a newly-added person) in original default order.
   - Implementation: project each item to a sort key `(positioned ? 0 : 1, positioned ? savedSortOrder : originalIndex)` partitioned by group/context, using a stable ordering (`OrderBy` is stable in LINQ-to-Objects).
   - Overview rows live under `GroupId=""`; they sort by the `GroupId=""` entries whose `RowId` matches a real `user-*` row (no collision with category-order entries, which carry `category-*`/`list-*` RowIds).

`Default.cshtml` already filters `Rows.Where(r => r.GroupId == group.Id)` (order-preserving) and iterates `Groups` in order, so once `model.Groups` and `model.Rows` are sorted, rendering reflects both with no view change.

`ICalendarRowOrderService` (`Services/CalendarRowOrderService.cs`):
- `Task<Dictionary<(string,string),int>> GetOrderMapAsync(int userId, string contextKey)`
- `Task SaveOrderAsync(int userId, string contextKey, string groupId, IReadOnlyList<string> itemIds)` — upsert: delete existing rows for `(userId, contextKey, groupId)` then insert `itemIds` with `SortOrder = index`. Used for **both** axes: row order (`groupId` = the category, `itemIds` = row ids) and category order (`groupId` = `""`, `itemIds` = group ids). Whole-namespace replace = simplest correct semantics.

## 7. Client module `wwwroot/js/calendar-row-reorder.js`

Self-invoking module, mirrors the codebase's vanilla-JS pattern. Handles **both** axes.

- **Init + rebind:** on `DOMContentLoaded` and on the existing `calendar:grid-refreshed` event (the hook added in the lockup fix), `(re)enable()` runs against `.excel-calendar[data-reorder-context]`:
  - **Row grip:** inject `⋮⋮` (`.excel-calendar__row-grip`) into each data row's **name cell** (the sticky first column — never a date cell, so no conflict with Quick Entry's cell clicks).
  - **Category grip:** wire the **already-rendered** group-header grip (`.excel-calendar__group-grip`, currently inert) as the category drag handle.
  - Grips are `draggable="true"`; the `<tr>` elements are not.
- **Row drag:** `dragstart` on a row grip captures `{ kind:'row', rowId, groupId }`. `dragover` computes an insert position **only within the same `groupId`** (cross-group dragover → `dropEffect='none'`, no drop line). Drop reorders the `<tr>` nodes within the group (optimistic), collects the group's new row-id order, and POSTs `{ groupId, itemIds }`.
- **Category drag:** `dragstart` on a group-header grip captures `{ kind:'category', groupId }`. `dragover` over other group headers shows the insert slot among categories. Drop moves the **entire category block** — the group-header `<tr>` plus all its member rows (contiguous in the DOM) — to the new position, collects the new group-id order, and POSTs `{ groupId:"", itemIds:[groupIds] }`. A category drag can only drop between other categories, never inside one.
- **Optimistic + safety net:** DOM is reordered immediately on drop; on POST failure → toast error and re-apply server order on the next refresh.
- **Keyboard a11y:** `Alt+ArrowUp/Down` on a focused **row** moves it within its group; on a focused **group header** moves the whole category — both POST via the same path. Grips have localized `aria-label` and `role="button"`.
- **Touch:** pointer/touch drag via the explicit grips (small targets avoid scroll-vs-drag ambiguity). If touch proves unreliable, keyboard reorder is the fallback; ship desktop drag first.
- **Collapsed categories:** a collapsed category can still be dragged by its header (its hidden rows move with it); the row grips inside it are simply not visible. Category drag and the existing collapse/expand toggle must not interfere — the grip is a distinct sub-element of the header, and `dragstart` suppresses the header's collapse click.

## 8. API

`POST /Api/Calendar/SaveRowOrder` — new Razor page handler `Pages/Api/Calendar/SaveRowOrder.cshtml.cs`, decorated `[Authorize]` + `[IgnoreAntiforgeryToken]` consistent with the sibling `QuickAdd*` endpoints. The `/Api/Calendar` prefix is **already** whitelisted in `ApiAuthenticationMiddleware.IsInternalWebUiEndpoint()` (per `docs/CLAUDE.md`), so no middleware change is needed; the JS fetch must send `credentials: 'same-origin'`. It is an authenticated endpoint, so it must **not** be added to any anonymous bypass.

Body: `{ contextKey: string, groupId: string, itemIds: string[] }`. The same endpoint serves both axes: `groupId=""` + group ids = category order; `groupId="category-5"` + row ids = row order within that category.
Server: resolve `userId` from claims; basic validation (`itemIds` non-empty, lengths within column limits, count ≤ 500); `await _rowOrderService.SaveOrderAsync(userId, contextKey, groupId, itemIds)`; audit-log `CalendarOrderChanged`; return `{ success: true }`.

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
- **Unit (category order):** `SaveOrderAsync(groupId="")` persists group order; component sorts `model.Groups` by saved order with un-positioned groups falling back to `ExcelCalendarGroup.SortOrder`; Overview `user-*` rows under `GroupId=""` are not disturbed by category-order entries.
- **Browser (Playwright):** (a) drag row within a group → order changes → reload → persisted; cross-group row drag rejected. (b) drag a **category header** → whole category block moves → reload → persisted; category drag can't drop inside a category. (c) `Alt+↑/↓` reorders both a focused row and a focused category header. (d) second user sees their own (unaffected) order.

## 11. Out of scope (YAGNI)

- Moving rows **between** groups (changing membership — owned by the existing molecule-scoped "move" feature). Row drag is within-category; category drag moves whole categories, not individual rows across them.
- A shared/global order or admin-curated order (explicitly chose per-user).
- A GET endpoint / cross-device live sync beyond what server-render already provides.

## 12. Risks / edge cases

- **New/removed rows:** un-positioned rows fall back to default order (handled in §6); removed rows leave stale `UserCalendarRowOrder` rows — harmless (ignored at render; optionally pruned lazily on save).
- **Grouping switch (list ↔ category) in Shifts user-mode:** different `GroupId` namespaces; orders coexist, each grouping remembers its own.
- **Draft Mode:** reordering is orthogonal to draft assignments; ContextKey does not include draft session, so order is shared between live and draft views (intended).
- **Quick Entry coexistence:** row grip lives in the name cell only; cell clicks (date cells) remain Quick Entry's. Verified no overlap.
- **Category block-move assumes contiguity:** `Default.cshtml` renders each group as a header `<tr>` immediately followed by that group's rows, so a category's nodes are contiguous in the DOM and can be moved as one block. The client must move the header + exactly its member rows (selected by `data-group-id`), recomputing after any collapse state change.
- **Category grip vs collapse toggle:** the group header is also the collapse/expand click target (`excel-calendar-groups.js`). The drag grip is a distinct child; `dragstart` must suppress the ensuing collapse click so a drag doesn't also toggle the category.
- **Ungrouped calendars (Overview):** no categories → category drag is simply absent; only row drag applies.
