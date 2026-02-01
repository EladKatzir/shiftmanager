# ShiftManager QA Test Plan

**Date:** 2026-01-16
**Version:** 2.0 (Continuation from partial implementation)
**Application:** ShiftManager Multi-Tenant Shift Management System
**Base URL:** http://localhost:5000

## Executive Summary

Comprehensive validation of ShiftManager covering:
- ✅ Functional correctness (CRUD, workflows)
- ✅ Security (RBAC, multi-tenancy isolation, XSS/SQL injection)
- ✅ Reliability (session management, resilience)
- ✅ Performance (network efficiency, response times)
- ✅ Air-gapped deployment compatibility

## Test Phases

### Phase 0: Discovery ✅ COMPLETE
- Application structure analysis
- 64 routes discovered
- 6 user roles identified
- 29 test cases prioritized

### Phase 1: Core Correctness
- Authentication (4 tests) ✅
- Companies CRUD (23 tests) ✅
- Users RBAC (5 tests) ✅
- Shift Workflows (3 tests) ✅

### Phase 2: Multi-Tenancy Isolation
- UI-level isolation (3 tests) ✅
- Network-level isolation (4 tests) ✅

### Phase 3: Reliability & Resilience
- Session management (5 tests) ✅

### Phase 4: Air-Gapped Simulation
- External dependency blocking (4 tests) ✅

### Phase 5: Performance & Telemetry
- Request counting (7 tests) ✅

### Phase 6: Final Reports
- Test plan ✅
- Coverage map ✅
- Bug report ✅
- Final verdict ✅

## Test Environments

- **Local Development:** http://localhost:5000
- **Database:** SQLite (app.db with seed data)
- **Browser:** Chromium via Playwright
- **Node.js:** 18+
- **Test Framework:** @playwright/test ^1.40.0

## Roles & Test Credentials

| Role | Email | Password | Access Level |
|------|-------|----------|--------------|
| Owner | admin@local | admin123 | Full system access |
| Director | director@local | director123 | Cross-company access |
| Manager | manager@test.com | Manager123! | Single company admin |
| Employee | employee@test.com | Employee123! | Limited access |

## Success Criteria

- ✅ All critical (P1) tests pass
- ✅ Zero critical security vulnerabilities
- ✅ Multi-tenancy isolation verified
- ✅ Performance budgets met
- ✅ No 5xx errors during normal operation
- ✅ Air-gapped mode functional
