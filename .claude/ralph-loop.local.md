---
active: true
iteration: 1
max_iterations: 0
completion_promise: null
started_at: "2026-02-08T14:07:28Z"
---

task: You are responsible for completing the entire testing phase end-to-end after hardening. Do not ask me questions or request confirmation. Make reasonable assumptions, execute, and document everything.

Execution rules (must follow)

Sequential gating: Never start a later phase until the current phase is fully complete.

Active monitoring: When running tests, stay in a monitoring mode until completion; capture relevant output and failures.

Verbose reporting: Provide exhaustive detail in logs and final results. Prefer too much detail over too little.

No follow-ups: If ambiguity exists, choose the safest default, state the assumption, and proceed.

Phase 1 — Automated test suite (run + watch)

Run the complete suite.

Record:

command(s) executed

environment details (versions/config relevant to reproducibility)

start/end timestamps

full outcome summary

For any failure:

identify the failing test precisely

extract the assertion/error

analyze likely root cause

propose next actions (fix product vs fix test)

Phase 2 — False positive handling (strict logging)

If you identify a failure that is likely a false positive:

Add an entry to False Positive Register with:

ID (FP-001, FP-002…)

test identifier + location

observed behavior

expected behavior

why it’s a false positive

how to reproduce

recommended correction and risk

Phase 3 — Manual verification (simulate test intent)

After all automated tests complete, run a manual verification pass that mimics the test cases:

Convert the suite into a manual checklist grouped by feature/area.

Execute each check manually (do not invoke automated tests here).

For each check, report:

steps performed

expected result

actual result

pass/fail

notes/screenshots/log excerpts if available

Final deliverable (single, comprehensive report)

Output a final report with this exact structure:

Hardening Summary

Automated Test Run

Failures & Analysis

False Positive Register

Manual Verification Checklist + Results

Open Issues (prioritized)

Final Readiness Verdict

Goal: when I return later, I should find the system fully tested both via automated suites and manual mimic verification, with complete logs and no missing steps.
Do not conclude until you have produced the final report and explicitly confirmed that both automated and manual verification are complete.. do not stop the ralph loop until you can provide evidance of completion of this task
