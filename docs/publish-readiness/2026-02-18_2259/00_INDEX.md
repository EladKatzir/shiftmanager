# Release Readiness Audit — Table of Contents

**Project:** ShiftManager
**Branch:** `merged-canonical` @ `cd7af8a`
**Date:** 2026-02-18 22:59 UTC
**Verdict:** **NOT READY** (10 MEDIUM, 1 LOW)

---

## Documents

| Document | Description |
|----------|-------------|
| [00_EXEC_SUMMARY.md](00_EXEC_SUMMARY.md) | Executive summary, verdict, and remediation roadmap |
| [00_README_METHOD.md](00_README_METHOD.md) | Audit methodology, phases, constraints, commands run |
| [00_INDEX.md](00_INDEX.md) | This file |

## Findings

| ID | Title | Severity | Fix Est. |
|----|-------|----------|----------|
| [FINDING-001](findings/FINDING-001-version-endpoint-blocked.md) | Version endpoint blocked by API middleware | LOW | 5 min |
| [FINDING-002](findings/FINDING-002-missing-permission-check-shift-assign.md) | Missing permission/grant check on shift assignment | MEDIUM | 15 min |
| [FINDING-003](findings/FINDING-003-no-past-date-validation-shift.md) | No past-date validation in shift assignment | MEDIUM | 10 min |
| [FINDING-004](findings/FINDING-004-api-timeoff-approval-incomplete.md) | API time-off approval missing shift cleanup & notifications | MEDIUM | 30 min |
| [FINDING-005](findings/FINDING-005-vacation-creation-missing-overlap-check.md) | Vacation creation page missing overlap validation | MEDIUM | 10 min |
| [FINDING-006](findings/FINDING-006-auto-approval-missing-notification.md) | Auto-approval path missing notification | MEDIUM | 10 min |
| [FINDING-007](findings/FINDING-007-email-uniqueness-tenant-scoped.md) | Email uniqueness check tenant-scoped (not global) | MEDIUM | 5 min |
| [FINDING-008](findings/FINDING-008-missing-audit-on-user-disable.md) | Missing audit log on user disable/enable toggle | MEDIUM | 10 min |
| [FINDING-009](findings/FINDING-009-grant-recalculation-on-role-change.md) | Grant recalculation not triggered on role change | MEDIUM | 30 min |
| [FINDING-010](findings/FINDING-010-role-audit-missing-join-approval.md) | RoleAssignmentAudit missing on join request approval | MEDIUM | 10 min |
| [FINDING-011](findings/FINDING-011-api-password-hashing-inconsistency.md) | API password hashing uses HMACSHA512 instead of PBKDF2 | MEDIUM | 15 min |

## Evidence

Runtime evidence was collected via:
- `dotnet build` + `dotnet test` output
- `curl` responses from `/health`, `/ready`, `/api/v1/version`
- Playwright browser automation (homepage, calendar, vacation request submission)
- Code review by 3 parallel analysis agents (shift assignment, vacation request, user management)

## Category Breakdown

| Category | Findings |
|----------|----------|
| Authorization / Grants | FINDING-002, FINDING-009 |
| Business Logic | FINDING-003, FINDING-005, FINDING-006 |
| API Consistency | FINDING-001, FINDING-004 |
| Data Integrity | FINDING-007 |
| Audit / Compliance | FINDING-008, FINDING-010 |
| Security | FINDING-011 |
