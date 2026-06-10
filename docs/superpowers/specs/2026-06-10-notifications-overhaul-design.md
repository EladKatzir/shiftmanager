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

### 5.8 Calendar / "summon" module (Felix-native + self-healing feed)

**Felix mail API (confirmed from swagger).** `ApiUrl` is the full `/mail/send` endpoint; the
calendar endpoints are sub-paths derived as `{ApiUrl.TrimEnd('/')}/calendar` and `/allDayEvent`.
The mail object is the same `{from,to,cc,bcc,subject,text,html}` we send today; the calendar
endpoints **wrap** it:
- `POST {ApiUrl}/calendar` — `{ "mail": {...}, "calendarEvent": { "startTime": ISO-8601-UTC,
  "endTime": ISO-8601-UTC, "location": "..." } }` — timed events (shifts, chores, on-duty, after).
- `POST {ApiUrl}/allDayEvent` — `{ "mail": {...}, "calendarAllDayEvent": { "startTime": "yyyy-MM-dd",
  "endTime": "yyyy-MM-dd", "location": "..." } }` — vacation, Day-At-X (sent to requester **and**
  approver(s)).
- `POST {ApiUrl}/v2` — multipart FormData with file attachments + `cid:` inline images. Not used
  by this design (see "rejected option" below).

**Felix limitation (confirmed):** `calendarEvent` carries **only** start/end/location — no UID,
method, or sequence. So a Felix invite is **fire-and-forget**: Felix builds a real native invite,
but we can never revise or cancel the event it pushed. This single fact drives the architecture.

Times must be **converted local (`Asia/Jerusalem`) → UTC** before formatting as `...Z` (shift
`TimeOnly`s are local; sending them verbatim with a `Z` suffix would be wrong by the TZ offset).

**Two cooperating mechanisms, each used for what it is best at:**

1. **Felix per-event invite = the instant "summon" (primary).** On a *new* assignment we POST to
   `/calendar` (timed) or `/allDayEvent`. Felix pushes a real calendar event immediately — the
   headline UX.
2. **Self-hosted subscription feed = self-healing source of truth (safety net).** Per-user
   `/calendar/feed/{token}.ics` (revocable token on `AppUser`, tenant-safe), regenerated from the
   DB on every poll via `IcsBuilder` with **stable per-assignment UIDs**. Always correct, so it
   silently fixes what Felix cannot: reschedules, cancellations, and any failed Felix send. This is
   the "support for when the per-event is lacking or suddenly fails" the product owner asked to keep.

**Duplicate avoidance — subscription-aware routing.** `CalendarFeedToken.LastPolledAt` is stamped
whenever Outlook fetches the feed. At calendar-send time: if the user is **actively subscribed**
(polled within the freshness window, e.g. 14 days) the feed already owns their calendar → send a
**plain** `/mail/send` notification and **skip** the Felix calendar push (no second event). If
**not** subscribed → send the Felix calendar invite. Either path yields exactly **one** calendar
entry.

**Updates & cancels — honest about the Felix limit.** Subscribed users: the feed auto-corrects;
we also send a plain heads-up email. Non-subscribed users who received a one-off Felix invite:
we cannot revise it, so we send a clear plain email ("your shift on X moved to Y / was cancelled —
please update your calendar") and surface the feed as the hands-off fix. We never claim an update
that did not happen.

`IcsBuilder` (used by the feed) emits standards-compliant `VCALENDAR`/`VEVENT`: stable `UID` per
assignment entity, `DTSTART/DTEND` in UTC (or `VALUE=DATE` for all-day), localized `SUMMARY`,
`LOCATION` = molecule/area, `DESCRIPTION` + deep link to Shifty.

**Rejected option (kept on record):** hand-rolling our own `.ics` with our UID and pushing it via
`/mail/send/v2` would give update/cancel + feed-dedup for *non-subscribers too*, but trades Felix's
clean native-invite UX for a file attachment and significant MIME complexity. The feed already
solves correctness, so this is held as a future enhancement, not the default (YAGNI).

### 5.9 Language learning

- `AppUser.PreferredLanguage` (nullable `he`/`en`).
- Lightweight hook on authenticated requests updates it to current UI culture **only on change**.
- Background email composition renders under the recipient's `PreferredLanguage` (fallback:
  company default → `he`).

## 6. Database changes (migrations)

| Change | Why |
|---|---|
| `AppUser.PreferredLanguage` | learned email language (**DONE, Phase 1**) |
| `CalendarFeedToken` (per-user token + `LastPolledAt`) | subscription feed auth + subscription-aware routing |
| `NotificationPreference.EngagementMode` + `LastCatchUpEmailAt` + pending guard | Quiet mode + throttle |
| `NotificationCategoryMute` (child table) | per-category granular mutes |
| New `NotificationType` enum values | new events (**append-only — never insert mid-enum**) |
| New `EmailTemplateType` values + `SharedResources(.he-IL).resx` keys | new emails, both languages |

## 7. Build sequence (each phase shippable + tested)

- **Phase 0 — Dispatcher foundation (Approach C):** event base, catalogue, `NotificationDispatcher`
  wrapping existing services. No behavior change. **DONE (b8b0309).**
- **Phase 1 — Language learning:** `PreferredLanguage` + request hook + background email culture.
  **DONE (260a34c).**
- **Phase 2 — Engagement + throttle + preferences UI:** `EngagementMode`, per-category mutes,
  signed opt-out link, 20-unread catch-up, NotificationCenter page; dispatcher *consumes* the
  metadata (owns the in-app-persist + email-decision instead of delegating to the dual-channel
  methods).
- **Phase 3 — Coverage migration (Approach A end state):** migrate the 14 existing pairs onto the
  dispatcher; wire every gap event (trainee email, feedback email, account actions, approver
  notifications, calendar text entries); add bulk batching.
- **Phase 4 — Subscription feed:** `IcsBuilder` + `/calendar/feed/{token}.ics` + `CalendarFeedToken`
  (with `LastPolledAt`). Self-hosted, no Felix dependency.
- **Phase 5 — Felix per-event "summon" + routing:** `IMailService` calendar-aware sends
  (`/calendar`, `/allDayEvent`), `QueuedEmail` carries the event payload, processor routes to the
  derived sub-path; subscription-aware routing (skip Felix push for active feed subscribers);
  plain-email fallback on update/cancel. Local→UTC time conversion.

## 8. Constraints & risks

- **Coordinate with concurrent Claude Code sessions.** Per the project's executable-lock policy,
  do not rebuild or kill processes while another session may hold the binary. Watch for merge
  conflicts in `NotificationService.cs`, `MailService.cs`, `IMailService.cs`, and the resx files.
- **Felix swagger received (2026-06-10).** Endpoints: `/mail/send` (basic), `/mail/send/calendar`
  (timed event), `/mail/send/allDayEvent` (all-day), `/mail/send/v2` (FormData files + `cid:`
  images). `calendarEvent` exposes only start/end/location (no UID/method) → Felix invites are
  fire-and-forget; correctness is handled by the self-hosted feed (§5.8).
- **Grant/seed discipline** unaffected (no new grants expected); follow append-only rules for the
  `NotificationType` / `EmailTemplateType` enums (same hazard as the GrantType seed).
- **Bilingual resx** — every new email needs both `SharedResources.resx` and
  `SharedResources.he-IL.resx` keys.
- **Tests** — each phase adds unit + integration tests; use the real-SQLite fixtures
  (`SqliteDbContextFixture`), not `UseInMemoryDatabase`, for any EF-touching tests.

## 9. Open items (resolve during the relevant phase)

- ~~Felix swagger~~ — RESOLVED 2026-06-10 (see §5.8 / §8).
- Exact "approver(s)" resolution for time-off/swap creation (who is the approver for a given
  requester — confirm against `VacationApprovalService` hierarchy logic). **Phase 3.**
- Whether `CalendarNoteChanged` should email anyone (currently: in-app to managers only). **Phase 3.**
- Feed freshness window for subscription-aware routing (default 14 days). **Phase 5.**
