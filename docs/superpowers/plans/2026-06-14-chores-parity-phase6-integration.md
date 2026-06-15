# Chores↔ShiftType Parity — Phase 6 (Integration, QA & Docs) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to run this plan. Steps use checkbox (`- [ ]`) syntax.

**Goal:** After Phases 1–5 land, prove the whole feature works end-to-end — full sequential regression, a populated-data browser sweep in light/dark/RTL, a bilingual localization QA pass, the MEMORY/docs update, and the final correctness validation.

**Architecture:** This phase writes almost no production code; it is the integration gate. The one code-ish change is the MEMORY.md entry. Everything else is verification with explicit pass criteria.

**Tech Stack:** xUnit + FluentAssertions (`dotnet test`), Playwright/browser for the visual sweep, the project's `localization-qa-inspector` agent.

**Depends on:** Phases 1–5 fully merged. **Spec:** `docs/superpowers/specs/2026-06-14-chores-shifttype-parity-design.md`.

---

## Pre-flight
- The dev app on `:5000` must be **stopped** before `dotnet test`/`dotnet build` (executable-lock rule). Restart it (fresh `bin/Debug`) before the browser sweep so it serves the new JS/CSS (the project serves stale static assets until restart).
- Tests run **sequentially**: `dotnet test -- xUnit.ParallelizeTestCollections=false` (parallel `:memory:` runs are flaky).
- This feature added **zero grants** — the grant count stays 136; `RoleTemplateAutoGrantTests` is unchanged.

---

## Task 1: Full-suite sequential regression

**Files:** none (verification).

- [ ] **Step 1: Confirm the app is stopped, then run the entire suite.**

Run: `dotnet test ShiftManager.Tests/ShiftManager.Tests.csproj -- xUnit.ParallelizeTestCollections=false`
Expected: **all green.** Baseline before this feature was ~1595/1595 (project memory); this feature adds the new suites: `EligibilityEvaluatorTests`, `BusyServiceChoreEligibilityTests`, `ChoreCategoryServiceTests`, `ChoreEligibilityAdminServiceTests`, `ChoreWeightTests`/stamp tests, `ChoreTemplateServiceTests`, `ChoreFoundationBackfillTests`, `ChoreFoundationSchemaTests`, `ChoreRosterTests` (+ the fixed `ChoresRosterAccountTypeTests` seed), the chore-category grouping test, and `JusticeChoreWeightingTests`.

- [ ] **Step 2: Confirm NO shift/justice regression.** Spot-check these existing suites are still green (the feature must be inert for shifts): `ShiftCategoryBackfillTests`, `ShiftCalendarServiceAccountTypeTests`, every `JusticeService*Tests`, `RoleTemplateAutoGrantTests` (count unchanged), `ShiftValidationTests`/`MilitaryRank*Tests`.

- [ ] **Step 3:** If anything is red, STOP and fix at root cause (no masking) before proceeding — a red shift/justice test means a parity change leaked into shared behavior.

---

## Task 2: Populated-data browser sweep (light / dark / Hebrew RTL)

**Files:** none (verification). Per the spec's hard rule: **test against POPULATED data** — an empty "0" hid a 1.14:1 donut contrast bug historically. Seed at least: 2 chore categories (Physical, Computer), 3 chore types (one male-only, one officer-only, one with a `DefaultWeightMinutes`), a few `DoesChores` users mapped to categories with varied `Gender`, one `UserChoreExemption`, a `ChoreTemplate`, and a handful of assigned chores of varied weight.

- [ ] **Step 1: ChoreTypes admin** (`/Admin/Organization/ChoreTypes`) — categories accordion, type editor (category dropdown, weight h+m, eligibility fieldset), eligibility reason chips in the table, exemptions list. Verify in **light, dark, RTL**. Category color renders as a **dot/accent**, never a text background.
- [ ] **Step 2: Admin/Users** (`/Admin/Users`) — DoesChores toggle + chore-category multiselect + the keep/cancel prompt; the Gender badge + edit dialog (visible to a plain user-editor; audited). `.gender-badge` legible in all 3 modes.
- [ ] **Step 3: ChoreTemplates + Stamp** (`/Admin/Organization/ChoreTemplates`) — create a template, open the Stamp modal, toggle **rotate vs everyone-every-day**, stamp across a range with a deliberately ineligible day, confirm the **created/skipped result view** shows the skip with a localized reason. Verify the one-chore-per-day skip is reported, not thrown.
- [ ] **Step 4: Chores calendar** (`/Calendar/Chores`) — rows grouped by **ChoreCategory** accordion, a multi-category user appears **mirrored** under each, uncategorized under the company header. Open the assignment picker on a male-only chore for a female/Unspecified user → **greyed + reason chip** (`.elig-chip`), and confirm gender is overrideable on assign while officer/exempt are hard. Verify `.elig-chip` legibility in **dark mode** specifically (the `--warning-text` trap Phase 4 flagged).
- [ ] **Step 5: Fairness** — the Justice drawer on the chores calendar + `/Admin/Analytics` (WorkType=Chore): the **ChoreCategory selector**, weighted load shown as **hours** (`DurationFormat.FormatHours`), and that two users with equal chore *counts* but different durations show **unequal** load. Confirm a null-category query reproduces pre-feature numbers.
- [ ] **Step 6:** Capture screenshots to `docs/superpowers/plans/artifacts/` for the record (light/dark/RTL of each surface).

---

## Task 3: Bilingual localization QA

**Files:** `Resources/SharedResources.resx`, `Resources/SharedResources.he-IL.resx` (verification + any fixes).

- [ ] **Step 1: Dispatch the `localization-qa-inspector` agent** over every new surface (ChoreTypes admin, Users gender/chores cells, ChoreTemplates + stamp, chores calendar eligibility chips, Justice/Analytics chore filters). It must verify: no hardcoded strings, every new key present in BOTH resx files, no mixed-language text, correct RTL, and **military-term correctness** (קצינים for officers, גברים/נקבות for gender, the gender-warning phrasing, "כללי" for the General category).
- [ ] **Step 2:** Fix any flagged key (add the missing translation; correct wording). Re-run the localization test suite: `dotnet test --filter "FullyQualifiedName~Localization" -- xUnit.ParallelizeTestCollections=false` → green. (Reminder: a duplicate resx key breaks the loc tests — grep before adding.)

---

## Task 4: MEMORY + docs update

**Files:** `C:\Users\katzi\.claude\projects\C--Users-katzi-Downloads-ShiftManager\memory\` (new memory file + MEMORY.md pointer).

- [ ] **Step 1: Write a memory file** `chores_shifttype_parity.md` capturing the non-obvious facts: chores now mirror the shift spine (`ChoreCategory`→promoted `ChoreType`→`Chore`); `DoesChores` + `UserChoreCategory`; `EligibilityRule` (**chore-scoped per `ChoreType` via a direct `ChoreTypeId` FK** — shifts do NOT use it); gender = **overrideable warning** + editable by any user-editor (**no new grant — count stays 136**); `Chore.WeightMinutes` frozen at create, fallback `DEFAULT_CHORE_WEIGHT_MINUTES = 480`; Justice chore actual is now `Sum(WeightMinutes)`; `ChoreType.ChoreCategoryId` is **nullable + SetNull** (uncategorized allowed); stamping is additive (per-stamp rotate toggle). Link the spec + the 6 phase plans.
- [ ] **Step 2: Add the one-line pointer** to `MEMORY.md` under Active Work.
- [ ] **Step 3:** Confirm the grant-count note in MEMORY.md is **unchanged** (still 136 — this feature added none). Do NOT edit `FinalProductPublish/` (generated).

---

## Task 5: Final correctness validation (per global policy)

- [ ] **Step 1:** Answer the required question explicitly, with evidence: **"Did we do everything correctly?"** — confirm: (a) full suite green sequentially; (b) shift/justice behavior byte-identical (no leak); (c) the one-chore-per-day unique index intact; (d) the gender warning is overrideable while officer/exempt are hard; (e) weighted fairness reproduces pre-feature numbers for a null-category query; (f) zero grant-seed changes; (g) all new UI legible in light/dark/RTL on populated data.
- [ ] **Step 2:** Decide branch integration (the `superpowers:finishing-a-development-branch` skill) — merge/PR per the project's `dev` workflow.

---

## Deferred Items (carried from earlier phases — confirm with the user)
- **Template times don't drive frozen weight** (Phase 2/3): a stamped chore's `WeightMinutes` comes from the chore type default / 480, not the template's `StartTime/EndTime/WeightMinutesOverride`. Honoring template windows needs a `CreateChoreAsync` signature change (small follow-up).
- **What-if preview "+1" semantics** (Phase 5): the in-drawer candidate preview still adds a count-flavored +1 for chores rather than the prospective weight; fairness columns are correct.
- **Analytics basis-toggle re-formatting** (Phase 5): after a client-side basis toggle, the chore Expected cell shows raw minutes until reload (server-rendered initial value is correct hours).
- **Eligibility fieldset on edit-row only** (Phase 3): not on the type create form (set rules after creating the type).
- **`EligibilityRule` is chore-scoped by design** (spec): shifts keep `ShiftType.RequiresOfficerRank` and do NOT use this table (no future-shift shaping).
