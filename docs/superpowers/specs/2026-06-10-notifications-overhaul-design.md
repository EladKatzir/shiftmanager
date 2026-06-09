# Notifications & Mail Overhaul — Design Spec

**Date:** 2026-06-10
**Status:** Approved (brainstorming) → ready for implementation planning
**Author:** Elad Katzir + Claude

## 1. Goal

Make notifications comprehensive and channel-complete: **every action that should logically
alert a user produces both an in-app notification and (per the user's engagement mode) an
email**, and the email system can "summon" users to their shifts and chores via Outlook
calendar integration.

The root problem today is **coverage gaps**, not missing plumbing. The system already pairs 14
actions across both channels, but the pairing is done by hand at each call site (call
`CreateXNotificationAsync` *and* `SendXEmailAsync`). Whenever a developer forgot one or both,
a gap appeared (trainees, feedback, all account/admin actions, approver-side request alerts).
There is also **no calendar (.ics) capability at all** and **no per-user language**, so
background emails render in the wrong language.

## 2. Chosen approach

**Approach A (centralized event-dispatch layer) delivered via the C migration path
(incremental).** Build a single `NotificationDispatcher` that fans every notification event
out to in-app + email + calendar, driven by declarative per-event metadata. Initially the
dispatcher wraps the existing `NotificationService`/`MailService` methods (Approach C), then
all call sites migrate onto it and the old dual-call pattern is retired (Approach A end state).

Rationale: the dual-call pattern is the **root cause** of every coverage gap. A single
`RaiseAsync` call cannot "forget" a channel. Preferences, language, ICS, and batching then
live in exactly one place.

## 3. Current state (audit findings)

### Mail
- `Services/MailService.cs` POSTs JSON `{ from, to, subject, html }` with header `Apikey` to a
  configurable `ApiUrl` (DB per-company → global → `appsettings.json`). Async via
  `EmailBackgroundQueue` + `EmailBackgroundProcessor`. Logged to `EmailApiLog`.
- **No attachment field** in the payload — blocks `.ics` per-event invites until the mail
  service ("Felix") swagger confirms attachment support.
- 15+ typed templates already exist (shift assigned/changed/deleted, chore assigned/canceled,
  on-duty assigned/canceled, time-off approved/declined/deleted, swap approved/declined,
  account approved, access request, trainee added, slot removed, shift modified).

### In-app
- `Services/NotificationService.cs` (+ `.Logging.cs`) persists `UserNotification` rows
  (`NotificationType` enum, Title/Message, IsRead, CreatedAt/ReadAt, RelatedEntityId/Type).
- `Hubs/CalendarHub.cs` / `ICalendarNotificationService` push **ephemeral** SignalR realtime
  calendar updates — these do **not** persist in-app notifications.

### Coverage gaps
| Tier | Actions | State |
|---|---|---|
| In-app only (no email) | Trainee added/removed/changed, Feedback submitted | missing email |
| SignalR only (no persisted in-app, no email) | Calendar text entries, calendar notes | vanish if offline |
| Neither channel | Role change, grant change, job-type change, does-shifts toggle, category assignment, password reset, account unlock, deactivation, deletion, join-request rejection, bulk import; **time-off/swap creation → approver never alerted** | silent |

### Preferences (today)
- `DailyNotificationPreference`: daily digest (time + which sections) and opt-in day-before
  reminders for shifts/chores/on-duty. **No per-event opt-out, no engagement mode.**

### Other
- `AppUser` has `Email` + `DisplayName` but **no preferred language** → background emails use
  the job thread's culture (wrong).
- **No iCalendar / .ics / text/calendar generation anywhere.**

## 4. Decisions (from brainstorming)

1. **Email aggression:** both channels for every notifiable action by default, with a one-click
   **opt-out link at the bottom of every email**. Opt-out = switch to **Quiet** mode (email only
   for personally-actionable events), plus a **20-unread catch-up email**.
2. **Calendar:** target Outlook (all users). Want **both** per-event invites **and** a
   subscribable feed, if Felix supports attachments. Feed ships regardless; invites gated on
   swagger.
3. **Language:** add `AppUser.PreferredLanguage`, **passively learned** from the web request
   (update on change), fallback to company default.
4. **Admin/account actions:** notify the affected user on **impactful** changes (role, grant,
   job-type, deactivation, approval/rejection); **security-critical** ones (password reset,
   account unlock) **always email even in Quiet mode**; skip minor toggles.

## 5. Architecture

### 5.1 Dispatch flow

```
Domain action ──raises──▶ NotificationEvent ──▶ NotificationDispatcher
                                                      │
   persist in-app ─ resolve audience ─ eval engagement+prefs ─ render email ─ attach .ics ─ enqueue (batched)
```

In-app notifications are **always** persisted. Email is conditional (precedence chain below).

### 5.2 Event metadata (declarative, drives the dispatcher)

| Flag | Meaning |
|---|---|
| `Audience` | resolver → recipient user(s) (affected employee / approver / owners / counterparty) |
| `PersonallyActionable` | does it email in Quiet mode? |
| `SecurityCritical` | always emails; ignores Quiet **and** category mutes |
| `CalendarEligible` + `IcsMethod` | attaches `.ics` (REQUEST create / UPDATE change / CANCEL remove) |
| `Batchable` | can be coalesced into one summary email during bulk ops |

### 5.3 Email decision precedence (in the dispatcher)

```
if SecurityCritical            → EMAIL  (ignores everything)
else if category muted         → no email
else if Quiet && !Actionable   → no email   (relies on the 20-unread throttle)
else                           → EMAIL
in-app notification            → ALWAYS persisted
```

### 5.4 Event catalogue

**Shifts** — ShiftAssigned `(actionable, ICS:REQUEST, batch)` · ShiftUnassigned `(actionable,
ICS:CANCEL, batch)` · ShiftModified `(actionable, ICS:UPDATE)` · SlotRemoved `(actionable,
ICS:CANCEL)` · ShiftMovedAcrossMolecule `(actionable, ICS:UPDATE)` · DraftPublished
`(actionable, ICS:REQUEST, batch → one "your N shifts are official" mail)`

**Trainees** — TraineeAddedToShift `(trainee: actionable+ICS:REQUEST; primary: info)` ·
TraineeRemovedFromShift `(trainee: actionable+ICS:CANCEL; primary: info)` · TraineeChanged
`(info to primary/old/new)` — *adds email (was in-app only)*

**Chores** — ChoreAssigned `(actionable, ICS:REQUEST)` · ChoreCanceled `(actionable,
ICS:CANCEL)` · ChoreModified `(actionable, ICS:UPDATE)`

**On-duty** — OnDutyAssigned `(actionable, ICS:REQUEST)` · OnDutyCanceled `(actionable,
ICS:CANCEL)`

**Swaps** — SwapRequestCreated → approver + counterparty `(actionable to approver)` *[new]* ·
SwapApproved → both `(actionable, ICS updates moved shifts)` · SwapDeclined → requester
`(actionable)` · SwapCanceled → counterparty/approver `(info)`

**Time-off** — TimeOffRequestCreated → approver(s) `(actionable to approver)` *[new]* ·
TimeOffApproved → requester `(actionable, ICS:CANCEL for auto-removed shifts)` · TimeOffDeclined
`(actionable)` · TimeOffDeleted `(actionable)` · TimeOffDatesUpdated → approver `(info)`

**Accounts** — AccessRequestSubmitted → owners `(actionable)` · AccountApproved `(actionable)` ·
JoinRequestRejected `(actionable)` *[new]* · RoleChanged / GrantChanged / JobTypeChanged → user
`(impactful)` *[new]* · Deactivated/Reactivated `(impactful)` *[new]* · PasswordReset /
AccountUnlocked `(SECURITY-CRITICAL — always email)` *[new]*

**Calendar entries** (currently SignalR-only) — CalendarTextEntryForUser add/remove → affected
user `(actionable — "sick day"/"training" on your calendar)` now persisted · CalendarNoteChanged
→ relevant managers `(in-app only, low signal)`

**Social / misc** — FeedbackSubmitted → owners `(in-app + email)` · FriendRequestReceived/Accepted
`(in-app default; not personally-actionable → no email in Quiet)`

**System** — UnreadCatchUp `(20-unread throttle, Quiet-mode only)` · DailyDigest /
DayBeforeReminder `(existing, kept)`

### 5.5 Engagement mode + preferences

- `EngagementMode` (`Engaged` default / `Quiet`) — toggled by the email footer opt-out link and
  the NotificationCenter page.
- Per-category mutes — small child table; NotificationCenter UI lets users mute categories.
- **Opt-out link**: signed `IDataProtector` token (userId + purpose), endpoint `/n/quiet?token=…`
  — stateless, no login, tamper-proof (suits air-gapped deployment).

### 5.6 20-unread catch-up throttle

Evaluated in the dispatcher after persisting an in-app notification:

```
if EngagementMode == Quiet
   && unreadCount crosses 20 (was <20, now ≥20)
   && no catch-up pending since last read
→ enqueue ONE "a lot happened — come back to Shifty to catch up" email
```

State: `LastCatchUpEmailAt` + a pending guard so it fires **once** per accumulation cycle. When
the user reads notifications and unread drops below 20, the guard resets.

### 5.7 Bulk batching

Request-scoped `NotificationBatchScope` buffers events during one unit of work (draft publish,
bulk import, mass-assign). In-app posts individually; emails are **grouped by (recipient,
category)** and flushed once at end-of-scope, with a **single combined `.ics`** (multiple
VEVENTs).

### 5.8 Calendar / ICS module

`IcsBuilder` emits standards-compliant `VCALENDAR`/`VEVENT`:
- Stable `UID` per assignment entity + incrementing `SEQUENCE` → Outlook treats create/update/
  cancel as the same event (no duplicates).
- `METHOD=REQUEST` (assign/update) / `CANCEL` (remove); `ORGANIZER` = company from-address;
  `ATTENDEE` = user; `DTSTART/DTEND` with `TZID=Asia/Jerusalem`; localized `SUMMARY`,
  `LOCATION` = molecule/area, `DESCRIPTION` + deep link to Shifty.

Two independent deliverables:
- **Subscription feed** — `/calendar/feed/{token}.ics` (per-user revocable token on `AppUser`,
  tenant-safe). User adds the URL once in Outlook; all their shifts/chores/on-call auto-sync.
  **No Felix dependency.**
- **Per-event calendar delivery** — **always feasible**; the same `IcsBuilder` always serves the
  `.ics` from *our* server. The Felix swagger only decides *quality*, not feasibility:
  - **5a (Felix supports attachments)** — attach the `.ics` (`method=REQUEST`) directly to the
    notification email → **native Outlook invite** with auto-appear + Accept/Decline RSVP.
    `IMailService.SendMailAsync` + the Felix payload gain an optional `attachments` parameter.
  - **5b (Felix is HTML-only, the wrapper-as-dumb-pipe path)** — embed an **"Add to Calendar"
    button** in the HTML body linking to a per-event server endpoint
    (`/calendar/event/{token}.ics`, `Content-Type: text/calendar`). One extra click, then Outlook
    opens and adds it. Works through Felix exactly as it sends mail today.

  Technical note: a native invite (auto-appear + RSVP) requires the `.ics` as an **attachment**
  or the message `Content-Type` to be `text/calendar; method=REQUEST`. A `from/to/subject/html`-only
  body cannot produce that — HTML body alone has no MIME control. Hence 5b is a button/link, not a
  silent auto-add. Both 5a and 5b reuse `IcsBuilder`; only the *delivery vehicle* differs.

### 5.9 Language learning

- `AppUser.PreferredLanguage` (nullable `he`/`en`).
- Lightweight hook on authenticated requests updates it to current UI culture **only on change**.
- Background email composition renders under the recipient's `PreferredLanguage` (fallback:
  company default → `he`).

## 6. Database changes (migrations)

| Change | Why |
|---|---|
| `AppUser.PreferredLanguage` | learned email language |
| `AppUser.CalendarFeedToken` | subscription feed auth |
| `NotificationPreference.EngagementMode` + `LastCatchUpEmailAt` + pending guard | Quiet mode + throttle |
| `NotificationCategoryMute` (child table) | per-category granular mutes |
| New `NotificationType` enum values | new events (**append-only — never insert mid-enum**) |
| New `EmailTemplateType` values + `SharedResources(.he-IL).resx` keys | new emails, both languages |

## 7. Build sequence (each phase shippable + tested)

- **Phase 0 — Dispatcher foundation (Approach C):** event base, catalogue, `NotificationDispatcher`
  wrapping existing services. No behavior change.
- **Phase 1 — Language learning:** `PreferredLanguage` + request hook + background email culture.
- **Phase 2 — Engagement + throttle + preferences UI:** `EngagementMode`, per-category mutes,
  signed opt-out link, 20-unread catch-up, NotificationCenter page.
- **Phase 3 — Coverage migration (Approach A end state):** migrate the 14 existing pairs onto the
  dispatcher; wire every gap event (trainee email, feedback email, account actions, approver
  notifications, calendar text entries); add bulk batching.
- **Phase 4 — ICS subscription feed:** `IcsBuilder` + feed endpoint. (No Felix dependency.)
- **Phase 5 — ICS per-event delivery:** **always feasible.** Implement 5b (HTML "Add to Calendar"
  button → `/calendar/event/{token}.ics`) first since it needs nothing from Felix; upgrade to 5a
  (native attachment invite) if/when the swagger confirms attachment support. Swagger picks
  quality, not feasibility — nothing is blocked.

## 8. Constraints & risks

- **Coordinate with concurrent Claude Code sessions.** Per the project's executable-lock policy,
  do not rebuild or kill processes while another session may hold the binary. Watch for merge
  conflicts in `NotificationService.cs`, `MailService.cs`, `IMailService.cs`, and the resx files.
- **Felix swagger outstanding** — does **not block** any phase. Phase 5b (HTML "Add to Calendar"
  button → our own `.ics` endpoint) works through Felix as-is; the swagger only enables the 5a
  upgrade to native attachment invites. All phases proceed independently.
- **Grant/seed discipline** unaffected (no new grants expected); follow append-only rules for the
  `NotificationType` / `EmailTemplateType` enums (same hazard as the GrantType seed).
- **Bilingual resx** — every new email needs both `SharedResources.resx` and
  `SharedResources.he-IL.resx` keys.
- **Tests** — each phase adds unit + integration tests; use the real-SQLite fixtures
  (`SqliteDbContextFixture`), not `UseInMemoryDatabase`, for any EF-touching tests.

## 9. Open items (resolve during planning / before Phase 5)

- Felix attachment capability (swagger) — only gates the 5a *upgrade* (native invites) over the
  5b button fallback; not a blocker.
- Exact "approver(s)" resolution for time-off/swap creation (who is the approver for a given
  requester — confirm against `VacationApprovalService` hierarchy logic).
- Whether `CalendarNoteChanged` should email anyone (currently: in-app to managers only).
