# Justice Page Redesign — Design Spec

**Date:** 2026-05-24
**Status:** Approved (design); implementation in progress
**Page:** `/Admin/Analytics` (the "Justice — Workload Distribution" page)
**Grants:** reuses `ViewJusticeTable` (#133) and `EditJusticeTargets` (#134) — **no new grants**

---

## 1. Goal

Make it easy to see how fairly **shifts / chores / on-duty** are distributed across the org hierarchy
(Project → Area → Molecule → Company → Users), with beautiful, scannable visuals (share donut + fairness
gauge), a guided **drill-down**, **role-capped** visibility, switchable **fairness basis**, and **month
comparison**. This is a presentation + navigation overhaul on top of the existing `JusticeService` math,
plus targeted service extensions and three latent-bug fixes.

The four reference mockups (standalone, offline HTML) live in
`.superpowers/brainstorm/design-drafts/`:
`converged-d.html` (companies-in-molecule, the canonical screen), `level-area.html`,
`level-users-in-company.html`, `level-users-in-molecule.html`.

---

## 2. Locked product decisions

| Topic | Decision |
|---|---|
| **Fairness basis** | Switchable, viewer-controlled: **"by size / capacity"** (today's behavior) ↔ **"equal share"** (total ÷ N units). Flipping it recomputes Expected, Expected-% and re-colors/re-sorts live. |
| **Comparison** | (a) **Period A vs B** side-by-side with per-row deltas ("vs other month"); (b) **per-row sparklines** (6-month). The standalone **spread-index trend chart was cut** (user: confusing, low value). |
| **Navigation** | Adaptive, **role-capped drill-down**: Area → Molecules → Companies → Users, with a branch at molecule → **Users-in-Molecule** (cross-company pool). Breadcrumb-based, plus a **"Drill Into ▾"** control so you can focus without scrolling. |
| **Sibling visibility** | At every level, **all siblings show as a full named ranking** so a unit can see where it stands. Capped roles can **only drill their own subtree**; non-drillable siblings render **dimmed + 🔒 "comparison only"** (visible data, no drill). |
| **Entry level** | Adaptive to the viewer's grant scope. **v1 ceiling = Area** (molecules-in-area), matching today's top level — no regression. Project-level ("Areas in Project") is **deferred** (needs new grant helper; see §8). |
| **Per-row columns** | Name · Actual · **% of total** · Expected (+basis tag) · **Expected % of total** · Deviation (shape+color) · vs other month · Trend (sparkline). |
| **Hero** | Two charts side-by-side: **Fairness gauge** (spread index + 0–4 severity + verdict → "how unjust") and **Share donut** (each slice = a unit's share of the total, colored by fairness band → "how many / what %"), plus Most-Over / Most-Under callouts. |
| **Work types** | Prominent pill rail: Shifts / Chores / On-Duty / All (per-type colors: shift navy/sky, chore gold `#E6C447`, on-duty terracotta `#C97064`). |
| **Time control** | Month-centric: presets (This / Last / Last-3 months) + A/B period pickers. |

---

## 3. The four levels (one parameterized component)

All four levels share **one chassis**: the same control bar, hero (two charts), 8-column ranked table,
distribution ribbon, and reminder card. Only four things vary per level: **row entity**, **breadcrumb
depth**, **drill affordances / lock placement**, and (at user levels) **avatars + company tag**.

1. **Area view** — rows = molecules in the area. Capped role: only own molecule drillable, rest locked.
   (Area-admin / higher: all drillable, no locks.) Info banner explains the capped constraint.
2. **Companies-in-Molecule** — rows = companies. Drill a company → users-in-company; or branch → users-in-molecule.
3. **Users-in-Company** — rows = members (avatars). Leaf (terminal, no drill). Drill-Into offers sibling companies + all-users-in-molecule.
4. **Users-in-Molecule** — rows = all members across companies (avatars + company tag). Leaf. Has a
   "Flat ranking / Group by company" toggle. Drill-Into offers "focus a company".

**Implementation:** this maps to extending `JusticeLevel` usage and rendering one Razor partial driven by
a `JusticeRow` list + level metadata — **not** four duplicated views (the 100 KB mockups proved why: heavy
CSS must live once in `justice.css`, loaded per-page).

---

## 4. Authorization & tenant isolation (the highest-risk area)

**Comparison tier = read scope; drill tier = subset.** The existing grant cascade already bounds reads
correctly: `GetAccessibleMoleculeIdsForGrantAsync(userId, "ViewJusticeTable")` returns *all* molecules the
user may read (area-scoped grant → whole area; molecule-scoped → just theirs).

- **Render** rows for every readable sibling (already authorized).
- **Mark** rows outside the user's own subtree as `IsDrillable = false` → dimmed + 🔒 in the UI.
- The page model computes `drillableIds` (the user's own subtree) and passes it to the service/view.
- **No new grants. No new `IgnoreQueryFilters` for the comparison tier** — existing bypasses (already
  documented with SECURITY comments) cover it; scope-gating stays at the page-model boundary.
- Drill requests (`?scopeId=`) are still validated by `UserCanAccessScopeAsync`, extended to distinguish
  **read** vs **drill**: a capped user may *read* an area (to rank molecules) but may only *drill* a
  molecule in their own subtree. Locked-row scopeIds must be rejected at the handler, not just hidden in UI.

---

## 5. Service extensions (`IJusticeService` / `JusticeService`)

Add to `JusticeRow`: `bool IsDrillable`, `decimal? ActualShare`, `decimal? ExpectedShare`,
`decimal? ExpectedEqualShare`/basis-aware expected, `decimal? DeltaVsCompare` (vs other period),
`IReadOnlyList<decimal>? Sparkline`. (Record → additive init props; update all call sites.)

Add to `JusticeQuery`: `FairnessBasis Basis` (`BySize` default | `EqualShare`).

New methods:
- `ComputeShares(rows)` — in-memory O(N): `ActualShare = Actual / Σ Actual`, `ExpectedShare = Expected / Σ Expected`.
- Fairness-basis branch in `ResolveSingleWorkTypeExpected` / per-user resolver: `EqualShare` → `total / rowCount`.
- `GetSparklineSeriesAsync(baseQuery, buckets=6)` — **single GROUP BY month** per work type (avoid N round-trips).
- `GetComparisonViewAsync(periodA, periodB)` — runs both views (`Task.WhenAll`), returns per-row deltas. Consider a short response cache for Area scope.

---

## 6. Latent bugs to fix (root-cause, part of this work)

1. **HIGH — JusticeTargets not read with `IgnoreQueryFilters`** (`JusticeService.cs:62`). Company-scoped
   target overrides are invisible to a cross-company admin (their tenant filter hides other companies'
   overrides → wrong Expected). Fix: `.IgnoreQueryFilters()` on the targets load + SECURITY comment
   (scope already gated at page boundary). Add a regression test using real SQLite.
2. **MEDIUM — exempt-shift exclusion misses `HOME_PM` / `HOME_AM`** (`CountActualPerUserAsync`). Uses raw
   `Key != KEY_HOME && Key != KEY_OFFLINE`; should use the `IsHome` semantics so all home variants are
   excluded when `ExcludeExemptShifts`. Fix + test.
3. **MEDIUM — N+1 in per-company chore/user building** (`BuildChoreHolesAsync` / per-company loops). Batch
   into a single `GROUP BY CompanyId/UserId` query. Verify with a query-count assertion where practical.

---

## 7. UI / front-end

- **`wwwroot/css/justice.css`** — rebuilt to the mockup: machined double-bezel cards, control bar, the
  SVG fairness gauge + share donut, deviation pills (shape **and** color — never hue alone), locked-row
  treatment, sparklines, distribution ribbon. **Token-only colors** (palette inverts in dark mode).
  Carry over the mockup's verified fixes: keyframe ends on `transform: none`; control bar gets an explicit
  stacking layer (dropdown over charts); dark-mode soft-over pill uses the light warning hue (no dark-on-dark).
- **`wwwroot/js/justice.js`** — work-type select, fairness-basis toggle (live recompute via server round-trip
  or precomputed both-basis values), time presets + A/B, "Drill Into" dropdown (a11y: Esc/outside-click/ARIA),
  group-by toggle. Respect `prefers-reduced-motion`. No external libs (air-gapped); SVG charts server-rendered
  or built from injected data.
- **Page** — `Pages/Admin/Analytics.cshtml(.cs)` rewritten around the new layout + level navigation; CSV
  export retained and extended with the new columns.
- **RTL + dark**: logical CSS properties; numeric/bar cells `dir="ltr"`; verified in both themes and Hebrew.

---

## 8. Localization

Every visible string via `<loc key="Justice_*">` / `IStringLocalizer<SharedResources>`, added to **both**
`Resources/SharedResources.resx` and `SharedResources.he-IL.resx`. JS-visible strings also added to
`_LocalizationScript.cshtml`. Mockups are English; production must be fully bilingual and RTL-correct.
A `localization-qa-inspector` pass is part of acceptance.

---

## 9. Out of scope / deferred (v1)

- **Project-level entry ("Areas in Project")** — needs new `GetAccessibleProjectIdsForGrantAsync` /
  `GetAccessibleAreaIdsForGrantAsync` and a new level builder. v1 ceiling stays at Area (no regression vs
  today). **Flagged for follow-up.**
- **Standalone spread-index trend chart** — cut by user decision (sparklines + A/B remain).
- **In-context calendar drawer** (`_JusticePanel`) — unchanged in v1; it reuses the same service, so service
  changes must remain backward-compatible with `GetInContextViewAsync`.
- **Second donut ring (users-within-company sunburst)** — possible later enhancement; v1 uses single-ring donut.

---

## 10. Testing strategy

- **Unit (xUnit, real SQLite fixtures):** Actual/Expected per work type; fairness-basis math (by-size vs
  equal-share); shares sum to ~100%; spread index/severity/bands; sparkline GROUP BY correctness; A/B
  deltas; the 3 bug-fix regressions (targets cross-company, HOME_PM exclusion, N+1 query count).
- **Authorization:** capped role reads all siblings but cannot drill a locked scopeId (handler rejects);
  area read vs molecule drill distinction; tenant isolation preserved.
- **Front-end (Playwright):** renders in light+dark and Hebrew RTL; fairness toggle updates table+donut;
  drill-into works and overlays charts; deviation encoding present without color; no console errors.
- **Localization:** `localization-qa-inspector` — no hardcoded strings, both resx complete, RTL correct.
- **Build discipline:** ensure the app is **not running** before `dotnet build`/test (locked-exe rule);
  full test suite green; `feature-dev:code-reviewer` pass; final strongest-model validation.

---

## 11. Acceptance (production-ready) checklist

- [ ] All four levels implemented as one parameterized component, matching the mockups.
- [ ] Switchable fairness basis; % of total + expected % of total; sparklines; A/B "vs other month".
- [ ] Role-capped drill with locked siblings; handler rejects locked drill targets.
- [ ] 3 latent bugs fixed with regression tests.
- [ ] Full bilingual loc (en + he-IL) + RTL verified; loc-QA pass clean.
- [ ] Light + dark verified; deviation encoded by shape+color; WCAG AA contrast.
- [ ] Unit + auth + Playwright suites green; existing suite still green.
- [ ] Code review pass; final validation; honest readiness report with any Deferred Items.
