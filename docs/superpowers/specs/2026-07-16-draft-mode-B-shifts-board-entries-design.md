# Draft Mode — Sub-project B: Other shifts-board entries & capacity

**Status:** Design, pending review. **Depends on:** [F — Foundation](2026-07-16-draft-mode-F-foundation-design.md), and coexists with A. **Reshaped by:** ArchReviewer (decision 7) + LeakAuditor (matrix).

The shifts board's quick-entry can create **non-shift** items (chores via `/chore`, on-duty via `/duty`, day-notes, text-entries) and can change **capacity** (`expandCapacityAndRetry`). In draft mode these all write live today (LeakAuditor #9–#15). This sub-project closes those leaks correctly.

## 1. The key decision (from review)

A chore/on-duty created on the *shifts* board is the **same entity** that gets its own draft surface in C/D. Staging it into the *shifts* draft session would put it in the wrong sandbox; routing it into a chore/on-duty draft that may not exist for this molecule+week is incoherent (ArchReviewer decision 7). **Resolution: in draft mode, the shifts board disables non-shift quick-adds** rather than staging them into a foreign sandbox. A user who wants to draft chores/on-duty uses the Chores/On-Call page's own draft (C/D).

## 2. Behavior

When `window.__draftSessionId` is set on the **shifts** board:
- **Disable** the `/chore`, `/duty`, day-note, and text-entry quick-entry actions (LeakAuditor #10–#15): the slash-menu items are hidden/greyed and direct submission is a no-op with a small hint ("Chores and day-notes aren't part of a shift draft — use the Chores/On-Call calendar's draft"). Grounded in `calendar-quick-entry.js` slash-command gating (`SLASH_COMMANDS`, `showSlashPalette`) and the day-note/text-entry branches (`updateDropdown` ~`:663-736`).
- **Capacity (LeakAuditor #9):** `expandCapacityAndRetry` (`calendar-inline-edit.js:466-503`) writes live `StaffingRequired` mid-assign (`EnsureShiftInstance` `Table.cshtml.cs:436` + `UpdateShiftStaffing` `:1123`). In draft mode, staging a primary into a "full" cell must **not** touch live capacity. Options: (a) auto-widen capacity at **commit** when the staged set exceeds `StaffingRequired` [recommended — the reconcile already sizes `StaffingRequired = Math.Max(1, staged.Count)` when creating an instance, `DraftModeService.cs:143`; extend to existing instances at commit], (b) stage an explicit capacity intent. Recommend (a): the staged assignee count *is* the intended capacity; commit sizes the instance to fit. The "shift full — expand & assign?" prompt is **suppressed in draft** (no live capacity write).

## 3. Non-goals / out of scope

- Staging chores/on-duty from the shifts board (belongs to C/D via their own pages).
- Per-cell shift `Note` editing and overview-notes — declare **out of scope** for B (adjacent, low-value; can be a later follow-up). Flag explicitly per the "no silent scope drop" rule.
- Legacy `Table.cshtml` grid capacity/instance handlers (#25–#31) — out of scope (F §9: draft gated behind the Excel-calendar flag).

## 4. Testing

- Unit: assigning a primary into a full staged cell → commit widens `StaffingRequired` to fit (no live capacity write before commit; assert live instance unchanged pre-commit).
- Browser: in shifts draft mode, `/chore` and `/duty` and day-note are disabled with the hint; assigning into a full cell stages without changing live capacity; commit widens capacity and applies.
