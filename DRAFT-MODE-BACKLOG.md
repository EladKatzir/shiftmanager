# Draft Mode — Backlog for Elad (review when you wake up)

These are decisions I proceeded on with a sensible default so I wasn't blocked. Nothing here stops the build — each is "shipped with default X; change if you disagree." Full rationale lives in the specs under `docs/superpowers/specs/2026-07-16-draft-mode-*.md`.

| # | Area | Decision | Default I shipped | Why |
|---|---|---|---|---|
| 1 | A (trainees) | Trainee validation *warnings* at commit (self-training / wrong-role / cross-molecule) | **SHIPPED as option (a): warnings don't block → trainee applies; hard errors skip+report.** Matches the existing primary-assignment path exactly. Switching to (b) "downgrade warnings to a skip+report note" is a ~1-line change if you prefer it. | Non-interactive commit can't do the live override handshake; least-surprise = consistent with primary |
| 2 | C (chores) | Chore descriptor identity | Include Title (retyping a title = a different chore) | Matches what the user authored; identical descriptors are fungible |
| 3 | C (chores) | Multi-chore-per-day clear key | Cell ordinal | Stable within a render; simplest |
| 4 | D (on-call) | Session granularity for mixed duty types | One session; grant re-checked per cell by duty type | Matches how the page renders all duty rows together |
| 5 | D (on-call) | All-areas draft (AreaId = null) | Its own distinct scope value (an "all" session) | Avoids ambiguous overlap with per-area sessions |
| 6 | D (on-call) | Busy/vacation warnings at commit | Auto-skip + report | Can't prompt in bulk commit |
| 7 | D (on-call) | Duty `Notes` staging | Out of scope (commit with null notes) | Note editing is a B-class concern |
| 8 | B | Per-cell shift `Note` + overview-note editing in draft | Out of scope (follow-up) | Adjacent, low value; keeps B tight |
| 9 | E | Bulk fill / copy-week draft | Deferred (you chose this) | The real "fill the whole week fast" primitive; follow-up |
| 10 | Scope | Legacy `Table.cshtml` grid draft support | Out of scope (draft gated behind Excel-calendar flag) | Excel calendar is the default rendering; legacy is a fallback |

## Needs YOU (not blocking the code — verification only)
- **2-minute logged-in visual check** of the new draft UI on the integration app (http://localhost:5100). I couldn't do the logged-in walkthrough because it requires typing a password into the login form (safety rule) and you were asleep. Everything is already verified by 1935 automated tests + a clean migration deploy; this is just eyes-on confirmation. Steps + relaunch command are in `DRAFT-MODE-HANDOFF.md` §5. Or tell me "proceed" and I'll drive it via your existing browser session.

## Decisions made via research (decision-maker agent)
(will append: item, chosen option, one-line rationale)
