# Summary
Allow any user to create one or more private, named “My Team” calendars. Each calendar is a week-style grid showing a row per selected teammate and a single, concise status for each day (e.g., *vacation*, *shift X*, *chore A*, *on-duty*, *free*). Users can switch calendars, manage the member list, and—based on role—click statuses to jump to existing pages.

---

# Goals
- Quick, at-a-glance view of what selected teammates are doing each day.
- Support multiple saved views (e.g., **Project X Team**, **QA Crew**, **Ops Rotation**).
- Respect role-based actions when clicking on items.
- Use the new vacation semantics (vacations extend until **1:00 PM the day *after*** the last day; “After/אפטֵר” runs **4:00 PM → next day 1:00 PM**).

# Non‑Goals
- Sharing calendars between users (v1). Calendars are private to the calendar's owner and **unshareable**; each user creates their own.
- Editing events inline on this view (v1). This view is read-only except for role-based navigation links.

---

# Key Concepts & Definitions
- **My Team calendar**: A named, private saved view owned by a user containing a member list (any user in the company).
- **Day Status** (what we render in each cell for a member/date): one concise label reflecting the *highest‑priority* applicable item for that member on that date (see Priority below).
- **Event Types shown**: Vacation, “After/אפטֵר”, On‑Duty, Shift, Chore, Free.

## Priority (what to show if multiple apply)
1. **Vacation** (incl. the “until 1 PM next day” rule)
2. **After/אפטֵר** (4 PM → next day 1 PM)
3. **On‑Duty**
4. **Shift**
5. **Chore**
6. **Free**

> If multiple of the same class exist (e.g., 2 chores), show the first one by start time and suffix with “+N” (e.g., *chore A +1*). For shifts, if multiple, show the first by start time (no suffix in v1).

---

# Roles & Permissions

| Role | View Grid | Click Chore → `/Public/Chores` | Click Shift → `/Calendar/Table` | Click On‑Duty → `/Public/OnDuty` | Click Vacation → `/Admin/TimeOff` |
|---|---|---|---|---|---|
| **Regular User** | ✅ | ❌ | ❌ | ❌ | ❌ |
| **Assigners** | ✅ | ✅ | ❌ | ❌ | ❌ |
| **Manager** | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Director** | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Owner** | ✅ | ✅ | ✅ | ✅ | ✅ |

> Everyone can create and manage their own **My Team** calendars (name, members). Editing event data is **not** done here; this view only links out according to the matrix above.

---

# User Stories
1. As a user, I can open my last‑used **My Team** calendar and see a weekly grid of statuses for its members.
2. As a user, I can press **+** to create a new **My Team** calendar, name it, and start with an empty member list.
3. As a user, I can press the **cycle** button to open a dropdown (DDL) listing my calendars to **switch**, **rename**, or **delete**.
4. As a user, I can press the **settings (gear)** button to manage members: add anyone from the company, remove existing members, and search by name.
5. As an Assigners/Manager/Director/Owner, clicking on a cell navigates to the respective page for that item type.

---

# UX Spec

## Main Week View (based on provided mock)
- **Header Controls (right-aligned):**
  - **Gear**: “Configure team members.” Opens Member Manager (see below).
  - **Cycle**: Opens a dropdown listing all of *my* calendars (simple list: *name*, *Switch*, *Rename*, *Delete*). Selecting a row switches the view. Rename is inline (Enter=save, Esc=cancel). Delete asks for confirmation.
  - **+**: Creates a new calendar. Prompt: *Name your team calendar*. Default suggestion: *My Team* or *My Team (2)* if name is taken.
- **Grid:**
  - Columns: **Sunday → Saturday** (fixed week start and end).
  - Rows: members (display name + optional avatar/initials).
  - Cell content: a single status string (see Formatting) with hover tooltip for more detail.

### Status Formatting
- **Vacation**: *vacation* (for full covered days). On the terminal day apply label *vacation until 1 PM* (per rule). The day *after* the last day shows *vacation until 1 PM* if it extends past midnight into that day.
- **After/אפטֵר**:
  - Start day: *after from 4 PM*
  - Next day: *after until 1 PM*
- **On‑Duty**: *on‑duty*
- **Shift**: *shift X* (or *shift* if unnamed)
- **Chore**: *chore A*; multiple → *chore A +N*
- **Free**: *free*

> Localization: labels use i18n keys (e.g., `status.vacation_until_1pm`). Hebrew for *After* is **אפטֵר**.

### Cell Click Behavior
- Follows the Role matrix above.
- If the day has multiple qualifying items of the same priority (e.g., two chores), navigate to the earliest.
- If multiple different types exist, we only show/route for the highest‑priority one.

### Empty State
- If a calendar has no members, show an instructional empty state with a CTA to **Configure members**.

---

## Member Manager (Gear)
Two-pane selector (based on second mock):
- **Left column:** Current team members (sorted **A→Z by display name**)
  - Search box filters the left list by name.
  - Each member is a pill/card. Clicking removes the member from the team and moves them to the right list.
- **Right column:** All company users not in the team (sorted **A→Z by display name**)
  - Search box filters the right list by name.
  - Clicking a user adds them to the team (moves to left list).
- **Footer actions:** *Save* (persist), *Cancel* (discard), *Select all* (optional, v2).
- **Constraints:** Any employee in the company may be added (no level restriction in v1).

---

> Calendars are private to the calendar's owner. Deleting a calendar cascades to remove its member rows.

---

# API / Routes (REST-ish; adjust to MVC as needed)
add relevent apis. the following are just an example
- `GET /api/team-calendars` → list my calendars (id, name)
- `POST /api/team-calendars` { name }
- `PATCH /api/team-calendars/:id` { name }
- `DELETE /api/team-calendars/:id`
- `GET /api/team-calendars/:id/members` → list members
- `POST /api/team-calendars/:id/members` { memberUserId }
- `DELETE /api/team-calendars/:id/members/:memberUserId`
- `GET /api/team-calendars/:id/events?start=YYYY-MM-DD&end=YYYY-MM-DD`
  - Returns, per member per day, the highest‑priority status + metadata for routing (e.g., `type`, `id`, `targetUrl`).

**User search**
- `GET /api/users?query=…` → returns users (id, displayName, role) to populate the right list.

---

# Event Aggregation Logic (for `/events`)
1. **Input:** `calendarId`, `start`, `end` (7‑day window). For each `member_user_id` in the calendar:
2. Query the member’s items by date range across event stores: vacations, After/אפטֵר, on‑duty, shifts, chores.
3. Apply vacation semantics:
   - Normal vacation covers full days from `startDate` through `endDate`, **plus busy until 13:00 the day after** `endDate`.
   - Mark the “day after” status as *vacation until 1 PM* if the window includes that day.
4. Apply **After/אפטֵר** semantics:
   - Start day: block from 16:00 → 23:59 (status: *after from 4 PM*).
   - Next day: block 00:00 → 13:00 (status: *after until 1 PM*).
5. For all items found on a given day, compute the single **highest‑priority** status (see table). Return:
   ```json
   {
     "memberId": 123,
     "date": "2025-01-11",
     "status": "vacation until 1 PM",
     "type": "vacation",
     "targetUrl": "/Admin/TimeOff?user=123&date=2025-01-11"
   }
   ```

---

# Validation & Edge Cases
- **Time zones/DST:** Use user/company timezone; calculate 1:00 PM boundaries in that TZ. Store UTC, render local.
- **Week:** Always Sunday start and Saturday end.
- **Name rules:** 1–60 chars, unique per calendar's owner, trimmed; reject empty or dup. different users can have the same calendar's name.
- **Delete confirmation:** “Delete *{name}*? This only removes your saved view.”
- **Large teams:** Virtualize member list; cap at, say, 200 per calendar in v1 (tunable).
- **Deprovisioned users:** Show as disabled row with tooltip; allow owner to remove.
- **No events on a day:** show *free*.

---

# Accessibility & i18n
- All controls keyboard‑navigable and labeled. DDL and dual‑list have ARIA roles.
- All labels translatable; include Hebrew for *After* (**אפטֵר**). Date/number formats localized.

---

# Telemetry
- Create/rename/delete calendar; add/remove member; calendar switch; cell clicks by type.

---

# Acceptance Criteria (high level)
1. Create, view, **rename**, switch, and delete **My Team** calendars as the owner.
2. Manage members via the two‑pane selector with search; changes persist.
3. Week grid renders one status per member/day with correct priority and labels.
4. Vacation and After/אפטֵר semantics follow the 1:00 PM rule exactly.
5. Role-based navigation works per the matrix.
6. Empty, loading, and error states are implemented.

---

# Open Questions
- Should chores also open in `/Calendar/Table` for managers if there is a time block overlap? (Still open)
- Future: do we ever want sharing (read-only or shared ownership)? **Deferred**; current version is explicitly unshareable.

---

# Implementation Notes (Engineering)
- **Calendars count:** No enforced cap; typical usage ~3 per user. Ensure the DDL and list APIs scale (virtualize long lists, typeahead search).
- **Caching**: For a 7‑day window × N members, batch-load per source and memoize day summaries.
- **Security**: Authorize membership/visibility by company; calendars are private to owner.
- **Tests**: Unit tests for day‑status computation (esp. vacations and After), API tests for member add/remove, and UI tests for role redirects.


---

# Wireframes (text-only, based on your sketches)
> These describe the UI so readers don’t need the images. Dimensions are indicative; spacing uses your design system tokens.

## 1) Main “My Team” Week View (Desktop)
```
┌──────────────────────────────────────────────────────────────────────────────┐
│ My Team — {Calendar Name}                                   [⚙] [⟳] [＋]    │
│ Sun Jan 12 – Sat Jan 18, 2025              ⟨ Prev week ⟩   •   This week    │
└──────────────────────────────────────────────────────────────────────────────┘
  Sticky left column      Sun      Mon      Tue      Wed      Thu      Fri      Sat
 ────────────────────────────────────────────────────────────────────────────────
  [◼︎][Alice Cohen]      status   status   status   status   status   status   status
  [◻︎][Ben David]        status   status   status   status   status   status   status
  [◼︎][Chen Levi]        status   status   status   status   status   status   status
  …
```
**Top-right controls**
- **[⚙] Gear (Configure members)** → opens Member Manager (two‑pane selector).
- **[⟳] Cycle (Switch calendar)** → opens DDL listing all calendars with Switch/Rename/Delete.
- **[＋] New calendar** → opens “Create calendar” modal (name input).

**Week navigation**
- Static **Sunday → Saturday** columns.
- Left of date range: **⟨ Prev week ⟩**; right: **This week** (returns to current).

**Rows**
- Left column is **sticky**: avatar/initials + display name (A→Z sorting). Optional small role badge.

**Cells (status)**
- Show **one compact label** using priority: Vacation → After/אפטֵר → On‑Duty → Shift → Chore → Free.
- Examples:
  - `vacation` / `vacation until 1 PM`
  - `after from 4 PM` → next day `after until 1 PM`
  - `on‑duty` • `shift X` • `chore A` (multiple chores → `chore A +2`)
- **Tooltip on hover/focus**: full details (start–end, source) + where clicking will navigate (based on role).
- **Click**: routes per role matrix; if multiple items, earliest of the highest priority.

**Visual hints (non-blocking improvements)**
- Small **icons** with text for recognition but keep text for a11y: 🧳 vacation, 🌙 after, 🛡 on‑duty, ⏱ shift, 🔧 chore, － free.
- Subtle background tint by type (never rely on color alone).
- Truncate long labels with ellipsis; tooltip shows full text.
- **Row menu** (optional v2): hide row, move to top.
- **Loading**: skeleton rows; **Empty**: CTA to “Configure members”.

---

## 2) Calendar Switcher (DDL opened from [⟳])
```
┌──────── My calendars ────────┐
│ ▸ Project X Team     [✎] [🗑] │  ← row click = Switch
│ ▸ QA Crew            [✎] [🗑] │  ✎ = Rename inline (Enter saves, Esc cancels)
│ ▸ On‑call Helpers    [✎] [🗑] │  🗑 = Delete with confirm
│──────────────────────────────│
│ ＋ Create new calendar        │
└──────────────────────────────┘
```
**Improvements**
- Optional typeahead if list grows long.
- Show last‑viewed calendar at top (recency sort) while preserving A→Z inside groups.

---

## 3) Create / Rename / Delete flows
- **Create modal**: *Name* (required, unique per owner, 1–60 chars). Buttons: **Create**, Cancel. Live validation.
- **Rename**: inline in DDL row; Esc cancels, Enter saves; on error show inline message.
- **Delete confirm**: “Delete *{name}*? This only removes your saved view.” Buttons: Delete (destructive), Cancel.

---

## 4) Member Manager (Gear → Two‑pane)
```
┌──────────────────────────────────────────────────────────────────────────────┐
│ Current team (A→Z)                 |  Company users not in team (A→Z)       │
│ [🔎 Search by name………………]        |  [🔎 Search by name………………]              │
│ ┌─ Alice Cohen ─┐                  |  ┌─ Dana Azulay ─┐                      │
│ ├─ Ben David  ──┤  click → move → |  ├─ Eitan Paz  ──┤  click → move left   │
│ └─ Chen Levi  ──┘                  |  └─ …           ─┘                      │
│ …                                  |  …                                     │
│──────────────────────────────────────────────────────────────────────────────│
│ Cancel                   Save changes                                       │
└──────────────────────────────────────────────────────────────────────────────┘
```
**Behavior**
- Clicking a card **moves** it across lists. Multi‑select via Shift/Ctrl (optional v2) then *Add/Remove selected* buttons.
- Infinite scroll/virtualization on the right list. Avatars + names; optional role pill.
- Keyboard support: Tab through lists, Enter toggles membership.

**Improvements**
- Show **member count** per list.
- **Conflict hint** badges (optional v2): if a member has heavy PTO or frequent on‑duty, show a subtle badge to help selection.

---

## 5) Cell Details Tooltip (all roles)
```
┌ Vacation (until 1 PM)
│ Jan 14, 00:00 → Jan 15, 13:00
│ Opens: /Admin/TimeOff
└────────────────────────────────
```
- Always shows where the click will route given the viewer’s role.

---

## 6) Mobile / Small Screens
- **Header** collapses to: title on left, kebab menu on right with items: Configure members, Switch calendar, Create calendar.
- **Week grid** becomes **horizontal scroll**; sticky left column turns into a **sticky row header** with name above each mini-row.
- Cells show **icons + short labels** (e.g., `vac.` / `aft.` / `shift` / `chore`).
- Bottom sheet for DDL and create/rename flows.

---

## 7) Status Rendering Rules (quick recap)
- Vacation extends through **1:00 PM the day after** the end date; that next day shows `vacation until 1 PM`.
- “After/אפטֵר”: start day `after from 4 PM`; next day `after until 1 PM`.
- Priority: Vacation > After > On‑Duty > Shift > Chore > Free; multiple chores → `+N`.

---

## 8) Quality & a11y notes
- All actions keyboard navigable; ARIA roles for menus, lists, and tooltips.
- Never rely on color only; combine icon + label + tooltip.

---

## 9) when absolutly done with testing, verifying and all other tasks in this document, update context.md to reflect all new changes, and the new apis

