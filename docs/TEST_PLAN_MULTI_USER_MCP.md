# ShiftManager - Multi-User System Testing Plan (MCP Protocol)
**Test Engineer:** AI QA Engineer (Claude Sonnet 4.5)
**Test Date:** 2026-01-06
**Application Version:** v2.2.0+ (Branch: newestafterpl)
**Testing Methodology:** ROSES (Role → Objective → Scenario → Expected Solution → Steps)
**Testing Approach:** Multi-user simulation with systematic coverage

---

## Executive Summary

This document outlines a comprehensive multi-user testing strategy for the ShiftManager application using MCP (Multi-Context Protocol) simulation to test different user roles, permissions, features, and security boundaries.

### System Under Test
- **Application:** ShiftManager - Multi-tenant shift scheduling and workforce management
- **Architecture:** ASP.NET Core 8.0 Razor Pages with SQLite database
- **Deployment:** Air-gapped, self-contained Windows application
- **Authentication:** Cookie-based auth + Griffin ADFS integration
- **Multi-tenancy:** Row-level security with CompanyId scoping
- **Database:** SQLite with 29 tables

### Test Scope
| Category | Count | Status |
|----------|-------|--------|
| **User Roles** | 6 | Owner, Director, Manager, Assigner, Employee, Trainee |
| **Razor Pages** | 65 | Full UI coverage planned |
| **REST API Endpoints** | 11 controllers (~38 endpoints) | API authentication and CRUD tests |
| **Authorization Policies** | 8 | IsAdmin, IsManagerOrAdmin, CanEditChores, etc. |
| **Feature Areas** | 12 | Auth, Calendar, Requests, Chores, OnDuty, Teams, etc. |
| **Security Tests** | 15+ | Multi-tenancy, XSS, SQL injection, CSRF, privilege escalation |

---

## ROSES Framework Application

### Role
**AI QA Test Engineer** - Systematic multi-user functional and security testing

### Objective
- **Primary:** Verify all features work correctly for each user role
- **Secondary:** Ensure multi-tenancy isolation prevents cross-company data leakage
- **Tertiary:** Identify security vulnerabilities and edge cases
- **Quaternary:** Validate business logic (workflows, conflict detection, notifications)

### Scenario
Simulate real-world usage across 6 user roles in a multi-company environment:
- **Company A (Demo Co):** Owner, Manager, Employee, Trainee users
- **Company B (Test Corp):** Director (with cross-company access), Manager, Employee
- **Test Conditions:** Single database, concurrent sessions, permission boundaries

### Expected Solution
- **Pass/Fail Matrix:** Test results categorized by role, feature, and severity
- **Coverage Report:** Percentage of features tested per role
- **Security Assessment:** Vulnerability findings with CVSS scores
- **Recommendations:** Prioritized fixes and improvements

### Steps
1. **Discovery Phase:** Map all pages, endpoints, features (COMPLETE)
2. **Test Planning:** Create test cases per role (IN PROGRESS)
3. **Environment Setup:** Start application, verify database seeding
4. **Role-Based Testing:** Execute tests for each role systematically
5. **Edge Case Testing:** Boundary conditions, null values, large datasets
6. **Security Testing:** Injection, XSS, privilege escalation, multi-tenancy bypass
7. **Reporting:** Generate Markdown + JSON reports with findings

---

## Test Environment

### Prerequisites
- Application running on `http://localhost:5000` or configured URL
- Database seeded with test data (Demo Co + users)
- Browser automation (Playwright MCP integration)
- Test accounts for all 6 roles created

### Test Data Setup
```sql
-- Company A: Demo Co (CompanyId = 1)
-- Owner: admin@local (password from seed)
-- Manager: manager@democompany.local
-- Employee: employee@democompany.local
-- Trainee: trainee@democompany.local
-- Assigner: assigner@democompany.local

-- Company B: Test Corp (CompanyId = 2)
-- Director: director@testcorp.local (has access to both companies)
-- Manager: manager2@testcorp.local
-- Employee: employee2@testcorp.local
```

---

## User Roles & Permission Matrix

| Role | Code | Key Permissions | Test Priority |
|------|------|-----------------|---------------|
| **Owner** | 0 | Full system access, company config, audit logs, database console, director assignment | **CRITICAL** |
| **Director** | 3 | Cross-company access, analytics, view all companies, cannot edit company config | **HIGH** |
| **Manager** | 1 | Approve requests, assign shifts, manage team, edit chores/on-duty, analytics | **CRITICAL** |
| **Assigner** | 5 | Edit chores ONLY (not on-duty), limited role | **MEDIUM** |
| **Employee** | 2 | View schedule, submit time-off/swap requests, view chores/on-duty | **HIGH** |
| **Trainee** | 4 | Limited view, shadowing, cannot request swaps | **MEDIUM** |

### Authorization Policy Matrix
| Policy | Owner | Director | Manager | Assigner | Employee | Trainee |
|--------|-------|----------|---------|----------|----------|---------|
| **IsAdmin** | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **IsManagerOrAdmin** | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| **IsDirector** | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| **IsOwnerOrDirector** | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| **CanViewChores** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| **CanViewOnDuty** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| **CanEditChores** | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ |
| **CanEditOnDuty** | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |

---

## Test Categories

### 1. Authentication & Authorization Tests
- [ ] **AUTH-001:** Login with valid credentials (all roles)
- [ ] **AUTH-002:** Login with invalid credentials (lockout after 5 attempts)
- [ ] **AUTH-003:** Session timeout and auto-redirect
- [ ] **AUTH-004:** Logout functionality
- [ ] **AUTH-005:** Password reset flow
- [ ] **AUTH-006:** Signup and user join request workflow
- [ ] **AUTH-007:** Griffin ADFS login (if configured)
- [ ] **AUTH-008:** Cookie security (HttpOnly, SameSite, Secure)
- [ ] **AUTH-009:** CSRF token validation on forms

### 2. Multi-Tenancy Tests
- [ ] **TENANT-001:** Owner can only see their company's data
- [ ] **TENANT-002:** Employee cannot see other company shifts
- [ ] **TENANT-003:** Director can see multiple companies (DirectorCompanies table)
- [ ] **TENANT-004:** API endpoints enforce CompanyId scoping
- [ ] **TENANT-005:** OnDuty table has NO CompanyId (cross-company visibility)
- [ ] **TENANT-006:** CompanyIdInterceptor auto-sets CompanyId on save
- [ ] **TENANT-007:** Cannot manipulate CompanyId via form tampering

### 3. Owner Role Tests (Priority: CRITICAL)
- [ ] **OWNER-001:** Access `/Owner/Index` dashboard
- [ ] **OWNER-002:** Manage feature flags (`/Owner/FeatureFlags`)
- [ ] **OWNER-003:** Configure email settings (`/Owner/EmailConfig`)
- [ ] **OWNER-004:** Configure Griffin ADFS (`/Owner/GriffinConfig`)
- [ ] **OWNER-005:** View audit logs (`/Admin/AuditLog`)
- [ ] **OWNER-006:** Manage directors (`/Admin/Directors`)
- [ ] **OWNER-007:** Database console access (`/Owner/DatabaseConsole`)
- [ ] **OWNER-008:** Backup/restore functionality (`/Owner/Backup`)
- [ ] **OWNER-009:** Data lifecycle management (`/Owner/DataLifecycle`)
- [ ] **OWNER-010:** Language management (`/Owner/LanguageManagement`)
- [ ] **OWNER-011:** Email template customization (`/Owner/EmailTemplates`)
- [ ] **OWNER-012:** System health monitoring (`/Owner/SystemHealth`)
- [ ] **OWNER-013:** Game configuration (`/Owner/GameConfig`)
- [ ] **OWNER-014:** Company selector (multi-company view)

### 4. Director Role Tests (Priority: HIGH)
- [ ] **DIR-001:** Access `/Director/CompanyFilter` and switch companies
- [ ] **DIR-002:** View-as mode (`/Director/ViewAsMode`) to impersonate users
- [ ] **DIR-003:** Cross-company analytics (`/Admin/Analytics`)
- [ ] **DIR-004:** Notification hub (`/Director/NotificationHub`)
- [ ] **DIR-005:** Cannot access Owner-only pages (should get 403)
- [ ] **DIR-006:** Can view but not edit company configuration
- [ ] **DIR-007:** DirectorCompanies table correctly scopes access

### 5. Manager Role Tests (Priority: CRITICAL)
- [ ] **MGR-001:** Access `/Requests/Index` approval dashboard
- [ ] **MGR-002:** Approve time-off request
- [ ] **MGR-003:** Decline time-off request
- [ ] **MGR-004:** Approve shift swap request
- [ ] **MGR-005:** Decline shift swap request
- [ ] **MGR-006:** Assign shift to employee (`/Calendar/Table`)
- [ ] **MGR-007:** Quick-add chore (`/Api/Calendar/QuickAddChore`)
- [ ] **MGR-008:** Quick-add on-duty (`/Api/Calendar/QuickAddOnDuty`)
- [ ] **MGR-009:** Delete chore (`/Api/Calendar/DeleteChore`)
- [ ] **MGR-010:** Delete on-duty (`/Api/Calendar/DeleteOnDuty`)
- [ ] **MGR-011:** View analytics (`/Admin/Analytics`)
- [ ] **MGR-012:** Manage team members (`/Admin/Users`)
- [ ] **MGR-013:** Shift conflict detection (cannot double-book employee)
- [ ] **MGR-014:** Rest hours enforcement (8-hour minimum between shifts)

### 6. Assigner Role Tests (Priority: MEDIUM)
- [ ] **ASG-001:** Can access `/Public/Chores` and edit chores
- [ ] **ASG-002:** Cannot access `/Public/OnDuty` edit functions (403 expected)
- [ ] **ASG-003:** Quick-add chore works
- [ ] **ASG-004:** Quick-delete chore works
- [ ] **ASG-005:** Cannot access Manager-only features (requests, analytics)
- [ ] **ASG-006:** Cannot access Owner/Director features

### 7. Employee Role Tests (Priority: HIGH)
- [ ] **EMP-001:** View own schedule (`/Schedule/Index`, `/Calendar/Month`)
- [ ] **EMP-002:** Submit time-off request (`/Requests/TimeOff/Create`)
- [ ] **EMP-003:** Submit shift swap request (`/Requests/Swaps/Create`)
- [ ] **EMP-004:** View chores (`/Public/Chores`)
- [ ] **EMP-005:** View on-duty schedule (`/Public/OnDuty`)
- [ ] **EMP-006:** View team calendar (`/MyTeam/Index`)
- [ ] **EMP-007:** View notification center (`/My/NotificationCenter`)
- [ ] **EMP-008:** Update profile (`/My/Profile`)
- [ ] **EMP-009:** Manage API keys (`/My/ApiKeys`)
- [ ] **EMP-010:** Submit feedback (`/Public/Feedback`)
- [ ] **EMP-011:** Play shift swap game (`/Game/Leaderboard`)
- [ ] **EMP-012:** Cannot approve/decline requests (403 expected)
- [ ] **EMP-013:** Cannot assign shifts to others (403 expected)

### 8. Trainee Role Tests (Priority: MEDIUM)
- [ ] **TRN-001:** View schedule (limited)
- [ ] **TRN-002:** Cannot submit shift swap request (business rule)
- [ ] **TRN-003:** Can submit time-off request
- [ ] **TRN-004:** View chores and on-duty
- [ ] **TRN-005:** Cannot access Manager features
- [ ] **TRN-006:** Shadowing relationship with senior employee

### 9. Business Logic Tests
- [ ] **BIZ-001:** Time-off approval creates OFFLINE shift instances
- [ ] **BIZ-002:** Shift conflict detection prevents overlapping shifts
- [ ] **BIZ-003:** Rest hours enforcement (default 8 hours)
- [ ] **BIZ-004:** Weekly hour caps (default 40 hours)
- [ ] **BIZ-005:** Chore mutual exclusion (cannot have chore + shift same day)
- [ ] **BIZ-006:** Swap request cannot swap past shifts
- [ ] **BIZ-007:** Notification triggers on all key events (18 types)
- [ ] **BIZ-008:** Daily notification job sends digests
- [ ] **BIZ-009:** OnDuty cross-company visibility (no CompanyId filter)

### 10. API Tests (REST API v1)
- [ ] **API-001:** `/api/v1/users` - List users (requires user:read scope)
- [ ] **API-002:** `/api/v1/users/{id}` - Get user by ID
- [ ] **API-003:** `/api/v1/shifts` - List shifts (requires shift:read scope)
- [ ] **API-004:** `/api/v1/time-off` - List time-off requests
- [ ] **API-005:** `/api/v1/time-off` - Create time-off request
- [ ] **API-006:** `/api/v1/notifications` - List notifications
- [ ] **API-007:** `/api/v1/notifications/mark-read` - Mark notification as read
- [ ] **API-008:** `/api/v1/swap-requests` - CRUD operations
- [ ] **API-009:** `/api/v1/chores` - CRUD operations
- [ ] **API-010:** `/api/v1/on-duty` - CRUD operations
- [ ] **API-011:** `/api/v1/analytics/summary` - Get analytics
- [ ] **API-012:** `/api/v1/audit-logs` - List audit logs
- [ ] **API-013:** X-API-Key authentication (401 without key)
- [ ] **API-014:** API key scope validation (403 for insufficient scopes)
- [ ] **API-015:** Rate limiting (100 requests/minute default)
- [ ] **API-016:** Multi-tenancy via CompanyId claim in API key

### 11. Security Tests
- [ ] **SEC-001:** XSS prevention (input sanitization in forms)
- [ ] **SEC-002:** SQL injection prevention (parameterized queries)
- [ ] **SEC-003:** CSRF token validation on POST/PUT/DELETE
- [ ] **SEC-004:** Privilege escalation (Employee cannot access Manager pages)
- [ ] **SEC-005:** Horizontal privilege escalation (User A cannot access User B's data)
- [ ] **SEC-006:** Multi-tenancy bypass attempts (CompanyId manipulation)
- [ ] **SEC-007:** API authentication bypass attempts
- [ ] **SEC-008:** Session fixation prevention
- [ ] **SEC-009:** Password hashing (PBKDF2, 100k iterations)
- [ ] **SEC-010:** Account lockout after 5 failed login attempts
- [ ] **SEC-011:** Secure cookie attributes (HttpOnly, SameSite, Secure)
- [ ] **SEC-012:** HTTPS enforcement in production
- [ ] **SEC-013:** Security headers (X-Frame-Options, CSP, X-Content-Type-Options)
- [ ] **SEC-014:** Audit logging for sensitive operations
- [ ] **SEC-015:** Griffin ADFS token validation

### 12. Edge Case & Boundary Tests
- [ ] **EDGE-001:** Null/empty form submissions
- [ ] **EDGE-002:** Very long input strings (max length validation)
- [ ] **EDGE-003:** Special characters in names (Unicode, emojis)
- [ ] **EDGE-004:** Date boundary conditions (Feb 29, leap years, year 2038)
- [ ] **EDGE-005:** Large dataset pagination (1000+ shifts)
- [ ] **EDGE-006:** Concurrent request approval (race conditions)
- [ ] **EDGE-007:** Delete entity with foreign key references (cascade behavior)
- [ ] **EDGE-008:** Timezone handling (DateOnly, TimeOnly conversions)
- [ ] **EDGE-009:** RTL language toggle (Hebrew rendering)
- [ ] **EDGE-010:** Dark mode toggle (theme persistence)
- [ ] **EDGE-011:** Browser back/forward button behavior
- [ ] **EDGE-012:** Session expiry during form submission
- [ ] **EDGE-013:** Database connection failure handling
- [ ] **EDGE-014:** Email service failure (graceful degradation)
- [ ] **EDGE-015:** Griffin ADFS service outage (fallback to local auth)

### 13. Performance Tests
- [ ] **PERF-001:** Page load time < 2 seconds
- [ ] **PERF-002:** API response time < 500ms
- [ ] **PERF-003:** Database query optimization (N+1 prevention)
- [ ] **PERF-004:** Caching effectiveness (ShiftTypeCacheService, CompanyCacheService)
- [ ] **PERF-005:** Concurrent user sessions (10+ simultaneous users)

### 14. Localization Tests
- [ ] **LOC-001:** English (en-US) rendering
- [ ] **LOC-002:** Hebrew (he-IL) rendering with RTL
- [ ] **LOC-003:** Language toggle functionality
- [ ] **LOC-004:** Missing translation keys (fallback to English)
- [ ] **LOC-005:** Date/time formatting per culture

### 15. Workflow Tests
- [ ] **WF-001:** End-to-end time-off request workflow
- [ ] **WF-002:** End-to-end shift swap workflow
- [ ] **WF-003:** End-to-end user signup and approval
- [ ] **WF-004:** Notification delivery (in-app + email)
- [ ] **WF-005:** Daily notification digest job

---

## Test Execution Matrix

| Test ID | Feature | Owner | Director | Manager | Assigner | Employee | Trainee | Status | Severity |
|---------|---------|-------|----------|---------|----------|----------|---------|--------|----------|
| AUTH-001 | Login | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | PENDING | CRITICAL |
| AUTH-002 | Lockout | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | PENDING | HIGH |
| ... | ... | ... | ... | ... | ... | ... | ... | ... | ... |

*(Full matrix to be populated during test execution)*

---

## Test Results Template

### Test Case Format
```markdown
## TEST-ID: Feature Name
**Role:** Owner / Director / Manager / Assigner / Employee / Trainee
**Priority:** CRITICAL / HIGH / MEDIUM / LOW
**Status:** ✅ PASS / ❌ FAIL / ⚠️ BLOCKED / ⏭️ SKIPPED

**Prerequisites:**
- User logged in as [role]
- Company: Demo Co (CompanyId = 1)

**Test Steps:**
1. Navigate to [URL]
2. Perform [action]
3. Verify [expected result]

**Expected Result:**
[Description of expected behavior]

**Actual Result:**
[Description of actual behavior]

**Defect Details:** (if failed)
- **Severity:** Critical / High / Medium / Low
- **Category:** Security / Functionality / UI / Performance
- **Reproducibility:** Always / Sometimes / Once
- **Error Message:** [Exact error if applicable]
- **Screenshot:** [Path to screenshot if captured]

**Notes:**
[Any additional observations]
```

---

## Test Metrics

### Coverage Goals
- **Role Coverage:** 100% (all 6 roles tested)
- **Page Coverage:** 90%+ (58+ of 65 pages)
- **API Coverage:** 100% (all 11 controllers)
- **Policy Coverage:** 100% (all 8 authorization policies)
- **Feature Coverage:** 95%+ (core features fully tested)

### Defect Severity Classification
- **CRITICAL:** System crash, data loss, security breach, multi-tenancy bypass
- **HIGH:** Core feature broken, incorrect business logic, privilege escalation
- **MEDIUM:** UI bug, minor workflow issue, edge case failure
- **LOW:** Cosmetic issue, typo, minor UX inconvenience

### Success Criteria
- **0 CRITICAL defects** in production
- **< 5 HIGH defects** unresolved
- **> 95% test pass rate** for core features
- **Multi-tenancy 100% verified** (no cross-company leakage)
- **Security tests 100% pass rate**

---

## Testing Schedule

### Phase 1: Environment Setup (30 minutes)
- [ ] Start application
- [ ] Verify database seeding
- [ ] Create test user accounts
- [ ] Configure Playwright browser automation

### Phase 2: Role-Based Testing (6-8 hours)
- [ ] Owner tests (2 hours)
- [ ] Director tests (1.5 hours)
- [ ] Manager tests (2 hours)
- [ ] Assigner tests (30 minutes)
- [ ] Employee tests (1.5 hours)
- [ ] Trainee tests (30 minutes)

### Phase 3: Security & Edge Cases (2-3 hours)
- [ ] Multi-tenancy isolation tests
- [ ] Security vulnerability scans
- [ ] Boundary and edge case tests

### Phase 4: Reporting (1 hour)
- [ ] Compile results
- [ ] Generate Markdown report
- [ ] Generate JSON report
- [ ] Calculate confidence score
- [ ] Write recommendations

---

## Appendix

### A. Test Data Requirements
- Minimum 2 companies in database
- Minimum 1 user per role per company
- Minimum 10 shifts across different dates
- Minimum 5 pending time-off requests
- Minimum 5 pending swap requests
- Minimum 3 chores assigned
- Minimum 3 on-duty assignments

### B. Browser Compatibility
- Primary: Chromium (Playwright)
- Secondary: Edge (manual verification)
- Not tested: Firefox, Safari (out of scope for air-gapped deployment)

### C. Test Artifacts
- `TEST_PLAN_MULTI_USER_MCP.md` - This document
- `TEST_RESULTS_YYYY-MM-DD.md` - Detailed test execution results
- `TEST_RESULTS_YYYY-MM-DD.json` - Machine-readable test results
- `TEST_SUMMARY_YYYY-MM-DD.md` - Executive summary with confidence score
- `screenshots/` - Test evidence screenshots
- `logs/` - Application logs during testing

---

**Document Status:** IN PROGRESS
**Next Step:** Begin Phase 1 - Environment Setup
**Estimated Completion:** 2026-01-06 End of Day

---

*Generated by Claude Code - AI QA Engineer*
