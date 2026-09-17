# Full verification pass — plan and honest limits

**Goal (user's words):** every issue found should be adversarially checked *and* runtime-proven, so
that decisions are made on verified information rather than on raw findings.

---

## Where we started

| | Count |
|---|---|
| Raw findings across 4 audit passes | **246** |
| After deduplication (6 genuine duplicates merged) | **240** |
| Already established by hand (source+DB traced, or runtime-proven) | 7 |
| **Remaining to verify** | **233** — 14 critical, 72 high, 117 medium, 30 low |

Deduplication was deliberately conservative (same file + ≥0.62 title similarity), so a few near-
duplicates may survive as separate entries. That is the safer error: merging two distinct bugs would
hide one.

Register: `scratchpad/register.json` — every finding with a stable id `F001`…`F240`.

---

## Phase 2 — adversarial refutation (in progress)

**One independent skeptic per finding.** Each is instructed to *kill* the finding, to open the actual
files rather than trust the write-up, and to prefer REFUTED when genuinely uncertain — because a false
CONFIRMED is more costly here: the user acts on confirmations.

Each skeptic is also given the four **precedents from this audit where a plausible finding was wrong**,
so it knows the shape of error to look for:
- "the page renders the stolen plaintext" — false, a redirect discards it;
- "the duplicate-name check is stricter than the DB" — false, two similarly named columns confused;
- a sweep reporting 442 seed mismatches — true answer 0, wrong argument parsed;
- "`/Admin/Users` has no scope check" — false, scope IS checked; privilege is not.

Each verdict also classifies **runtime testability**, which feeds Phase 3:

| Category | Meaning |
|---|---|
| `http` | a request/response or DB state change demonstrates it |
| `ui` | needs driving the page in a browser |
| `seed-required` | provable, but specific data must be seeded first |
| `fault-injection` | needs a failure induced mid-operation (transactional half-states) |
| `not-observable` | design/UX judgment or dead-code claim — no runtime assertion exists |

---

## Phase 3 — runtime proof for survivors

For every CONFIRMED/PLAUSIBLE finding with a constructible probe, prove it against a **Release build on
:5080 running on a throwaway copy of `app.db`** (never the live database, never the user's `:5000`).

The DB copy was refreshed to a clean baseline for this phase: 616 users, 37 companies, 17 800 grants.

Destructive proofs (hard-deleting a company, cascade-deleting blueprints) are safe to run here
precisely because the database is disposable — that is the point of the copy.

---

## The honest limits — stated up front, not discovered later

**1. This verifies what we FOUND. It cannot establish that nothing else exists.**
Verification and coverage are different axes. Surfaces never audited produce no findings to verify,
and silence there is not evidence of correctness. Still unaudited: RTL layout (never rendered in
Hebrew), `/Calendar/Chores` beyond the assign path, `Pages/Requests/Swaps/*`, and 2 of the 8 C4–C7
dimensions produced nothing. **A clean verification result must not be read as "the app is correct".**

**2. Some findings are not runtime-provable, by their nature.** Expect a meaningful
`not-observable` / `fault-injection` bucket:
- *Design and UX judgments* ("this behaviour is wrong for the real user") have no runtime assertion —
  observing the behaviour does not settle whether it is correct.
- *Dead-control claims* ("this grant is never enforced") are proven by absence of a call site; at
  runtime you can only show the control has no effect, which is weaker and easily confounded.
- *Transactional half-states* need a failure induced mid-operation.
- *Races* need deterministic interleaving.
These will be marked as such rather than quietly counted as verified.

**3. A REFUTED verdict is a judgment, not a proof.** Where a refutation is load-bearing for a decision,
it deserves the same runtime scrutiny as a confirmation — the audit already contains one case
(`.shift-table th`) where I flagged correct code, and one (API-key severity) where I confirmed a defect
whose stated impact was wrong in the user's favour.

---

## Output

A single register with, for every finding: verdict, corrected claim where the original was partly
right, corrected severity, the evidence actually read, and how it was established
(`runtime-proven` / `hand-verified` / `code-read-refuted` / `not-observable`).

Only then are the fix/no-fix decisions made on verified information — which was the point.
