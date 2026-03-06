# Grants Authorization System - Failure Modes Analysis

> **Last Updated:** 2026-02-06
> **Document Status:** Active
> **Related Documentation:** [GRANTS_AND_ROLES.md](./GRANTS_AND_ROLES.md)

## Overview

This document identifies and analyzes potential failure modes in the ShiftManager grant-based authorization system. It serves as a risk register and mitigation guide for the development team during the migration from role-based to grant-based authorization.

### Risk Assessment Matrix

| Risk Level | Impact | Likelihood | Action Required |
|------------|--------|------------|-----------------|
| **CRITICAL** | HIGH | HIGH | Immediate mitigation before deployment |
| **HIGH** | HIGH | MEDIUM | Mitigation required, testing essential |
| **MEDIUM** | MEDIUM | MEDIUM | Mitigation recommended |
| **LOW** | LOW | LOW | Monitor and document |

### Failure Mode Summary

| ID | Failure Mode | Risk Level | Impact | Likelihood |
|----|--------------|------------|--------|------------|
| FM-1 | Migration Data Loss | HIGH | HIGH | MEDIUM |
| FM-2 | Grant Scope Mismatch | HIGH | HIGH | MEDIUM |
| FM-3 | Missing Grant Assignments | MEDIUM | MEDIUM | LOW |
| FM-4 | UI/Support Confusion | MEDIUM | LOW | HIGH |
| FM-5 | Performance Degradation | MEDIUM | MEDIUM | LOW |
| FM-6 | Circular Grant Delegation | LOW | LOW | LOW |

---

## FM-1: Migration Data Loss

### Description
During the migration from role-based to grant-based authorization, users may lose access to features they previously had. This can occur when the mapping between legacy roles and new grants is incomplete or incorrect.

### Risk Assessment

| Aspect | Details |
|--------|---------|
| **Impact** | HIGH - Users lose access to features they need for daily work |
| **Likelihood** | MEDIUM - Complex role-to-grant mapping increases error probability |
| **Risk Level** | HIGH |

### Detection Strategies

1. **Pre-Migration Audit**
   - Compare user role counts against expected grant counts
   - Generate report of all unique role combinations in production
   - Validate that every role maps to at least one grant

2. **Post-Migration Verification**
   - Run access verification for sample users from each role
   - Compare "before" and "after" permission snapshots
   - Monitor 401/403 error rates in first 24 hours

### Mitigation Strategies

1. **Dry-Run Migration Script**
   - Execute migration in read-only mode first
   - Generate detailed report of proposed changes
   - Flag any users who would lose permissions

2. **Rollback Capability**
   - Maintain role-based authorization in parallel for 2 weeks
   - Feature flag to switch between systems: `UseGrantAuthorization`
   - Database backup before migration

3. **Admin Override Grant**
   - Emergency grant `AdminAccess` (ID 57) bypasses all checks
   - Designated admins can restore access while issues are resolved

### Required Tests

| Test | File | Purpose |
|------|------|---------|
| `MigrationTests.AllUsersHaveExpectedGrants()` | `ShiftManager.Tests/IntegrationTests/MigrationTests.cs` | Verify no user loses permissions |
| `MigrationTests.NoOrphanedPermissions()` | `ShiftManager.Tests/IntegrationTests/MigrationTests.cs` | Detect grants not linked to users |
| `MigrationTests.RoleToGrantMappingComplete()` | `ShiftManager.Tests/IntegrationTests/MigrationTests.cs` | All roles map to grants |

### Recovery Procedure

1. Enable feature flag `UseRoleAuthorization` to fall back to legacy system
2. Identify affected users from error logs
3. Manually assign missing grants via Admin panel
4. Re-run migration with corrected mapping
5. Disable fallback feature flag

---

## FM-2: Grant Scope Mismatch

### Description
Grants are assigned at incorrect scope levels, resulting in either:
- **Over-permission**: User can access data from other companies/molecules/areas
- **Under-permission**: User cannot access data they should be able to see

### Risk Assessment

| Aspect | Details |
|--------|---------|
| **Impact** | HIGH - Cross-company data exposure is a security incident; denied access blocks work |
| **Likelihood** | MEDIUM - Scope resolution involves complex hierarchy traversal |
| **Risk Level** | HIGH |

### Scope Level Reference

```
Project (broadest)
  └── Area
       └── Molecule
            └── Company
                 └── Department
                      └── Self (narrowest)
```

### Detection Strategies

1. **Integration Tests for Each Scope Level**
   - Test grant checks at every scope boundary
   - Verify scope inheritance works correctly (molecule grant covers all companies)
   - Test negative cases (company grant does NOT cover other companies)

2. **Cross-Company Access Monitoring**
   - Log all cross-company data access attempts
   - Alert on patterns suggesting misconfiguration
   - Audit trail for scope-sensitive operations

### Mitigation Strategies

1. **Default Deny Policy**
   - All authorization checks default to `false`
   - Explicit grant required for any access
   - No implicit permissions from hierarchy

2. **Explicit Scope Checks**
   - Every authorization call must specify scope context
   - `HasGrantWithScopeAsync()` validates against hierarchy
   - No shortcut methods that skip scope validation

3. **Audit Logging**
   - Log all grant assignments with scope details
   - Log all scope-sensitive authorization decisions
   - Include hierarchy context in logs for debugging

### Required Tests

| Test | File | Purpose |
|------|------|---------|
| `GrantScopeResolutionTests.GetAccessibleCompanyIdsForGrantAsync_WithCompanyScope_ReturnsOnlyThatCompany` | `ShiftManager.Tests/UnitTests/Services/V3Hierarchy/GrantServiceTests.cs` | Company scope isolation |
| `GrantScopeResolutionTests.GetAccessibleCompanyIdsForGrantAsync_WithMoleculeScope_ReturnsAllCompaniesInMolecule` | `ShiftManager.Tests/UnitTests/Services/V3Hierarchy/GrantServiceTests.cs` | Molecule scope expansion |
| `GrantScopeResolutionTests.HasGrantForCompanyAsync_WithNonMatchingScope_ReturnsFalse` | `ShiftManager.Tests/UnitTests/Services/V3Hierarchy/GrantServiceTests.cs` | Cross-company denial |
| `GrantServiceTests.HasGrantWithScope_CompanyScope_MatchesUserCompany` | `ShiftManager.Tests/UnitTests/Services/V3Hierarchy/GrantServiceTests.cs` | User company matching |
| `GrantServiceTests.HasGrantWithScope_MoleculeScope_CoversAllCompanies` | `ShiftManager.Tests/UnitTests/Services/V3Hierarchy/GrantServiceTests.cs` | Molecule coverage |
| `GrantServiceTests.HasGrantWithScope_AreaScope_CoversMolecules` | `ShiftManager.Tests/UnitTests/Services/V3Hierarchy/GrantServiceTests.cs` | Area coverage |
| `CrossCompanyAccessTests.*` | `ShiftManager.Tests/IntegrationTests/CrossCompanyAccessTests.cs` | End-to-end isolation |

### Recovery Procedure

1. Identify affected grant assignments from audit logs
2. Correct scope level in grant record
3. If data was exposed, follow security incident procedure
4. Add regression test for the specific scenario

---

## FM-3: Missing Grant Assignments

### Description
New users are created without the required grants for their role, leaving them unable to perform expected functions.

### Risk Assessment

| Aspect | Details |
|--------|---------|
| **Impact** | MEDIUM - New users cannot work until manually fixed |
| **Likelihood** | LOW - Role templates auto-assign grants on user creation |
| **Risk Level** | MEDIUM |

### Detection Strategies

1. **Onboarding Flow Verification**
   - Automated test creates user with each role template
   - Verify all expected grants are assigned
   - Run after any role template changes

2. **User Self-Service Check**
   - Dashboard shows "permission health" indicator
   - Users can request missing permissions
   - Admin notified of requests

### Mitigation Strategies

1. **Role Template Validation**
   - CI/CD validates role templates have all required grants
   - Seed data tests verify grant assignments
   - Schema validation prevents orphaned templates

2. **Admin Notification for Missing Grants**
   - Background job detects users with no grants
   - Email alert to company admin
   - Dashboard widget showing "users needing attention"

3. **Auto-Repair Mechanism**
   - Scheduled job reapplies role template grants
   - Logs any users that received missing grants
   - Non-destructive (doesn't remove extra grants)

### Required Tests

| Test | File | Purpose |
|------|------|---------|
| `RoleAssignmentTests.ApplyAutoGrants_AddsRoleGrants` | `ShiftManager.Tests/UnitTests/Services/V3Hierarchy/GrantServiceTests.cs` | Auto-grants applied |
| `RoleAssignmentTests.ApplyAutoGrants_RespectsRoleScope` | `ShiftManager.Tests/UnitTests/Services/V3Hierarchy/GrantServiceTests.cs` | Correct scope on auto-grants |
| `RoleAssignmentTests.ApplyAutoGrants_DoesNotDuplicateGrants` | `ShiftManager.Tests/UnitTests/Services/V3Hierarchy/GrantServiceTests.cs` | Idempotent application |
| `RoleTemplateTests.AllGrantsAssignedOnUserCreation()` | `ShiftManager.Tests/IntegrationTests/RoleTemplateTests.cs` | Full onboarding verification |

### Recovery Procedure

1. Identify user's intended role template
2. Run `GrantService.ApplyAutoGrantsAsync(userId, roleTemplateId, scope)`
3. Verify user can now access expected features
4. Investigate why auto-grant failed initially

---

## FM-4: UI/Support Confusion

### Description
Users and support staff are confused by grant terminology, unclear permission names, or difficulty understanding why access is denied.

### Risk Assessment

| Aspect | Details |
|--------|---------|
| **Impact** | LOW - Operational friction, no data loss or security issues |
| **Likelihood** | HIGH - Terminology change from "roles" to "grants" affects all users |
| **Risk Level** | MEDIUM |

### Detection Strategies

1. **User Feedback Channels**
   - In-app feedback button on permission denied pages
   - Track support ticket categories related to access issues
   - User surveys after role template changes

2. **Support Ticket Analysis**
   - Tag tickets with "grant-confusion" category
   - Track time-to-resolution for permission issues
   - Identify common misunderstandings

### Mitigation Strategies

1. **Clear Grant Names**
   - Use action-oriented names: "View Shifts", "Assign Chores"
   - Avoid technical jargon in user-facing UI
   - Consistent naming pattern across all grants

2. **Tooltips and Help Documentation**
   - Hover tooltips on grant names explain what they enable
   - "Why don't I have access?" link on 403 pages
   - Help article explaining grant system for power users

3. **Permission Denied Page Improvements**
   - Show which specific grant is missing
   - Provide "Request Access" button
   - Link to relevant help documentation

4. **Admin Dashboard Enhancements**
   - Visual grant assignment interface
   - Preview "what can this user do?" before saving
   - Bulk grant assignment tools

### Required Tests

| Test | Type | Purpose |
|------|------|---------|
| Grant Management UI | Manual QA | Verify tooltips appear and are accurate |
| Permission Denied Pages | Manual QA | Verify helpful information is shown |
| Admin Grant Assignment | Manual QA | Verify preview shows correct permissions |

### Recovery Procedure

1. Gather specific feedback about confusion point
2. Update grant name, description, or tooltip
3. Update help documentation
4. Communicate changes to support team

---

## FM-5: Performance Degradation

### Description
Grant authorization checks slow down page loads, especially on pages that check multiple grants or render many items with different permission requirements.

### Risk Assessment

| Aspect | Details |
|--------|---------|
| **Impact** | MEDIUM - Poor user experience, potential timeouts |
| **Likelihood** | LOW - Most checks are simple database lookups with indexing |
| **Risk Level** | MEDIUM |

### Detection Strategies

1. **Performance Monitoring**
   - Track P50/P95/P99 latency for pages with grant checks
   - Alert if grant check methods exceed 10ms average
   - Dashboard showing grant check performance over time

2. **Load Tests**
   - Simulate 100 concurrent users checking grants
   - Measure response time under load
   - Identify N+1 query patterns

### Mitigation Strategies

1. **Grant Caching Per Request**
   - Cache user grants at start of HTTP request
   - Single database query loads all user grants
   - Subsequent checks use in-memory cache

2. **Denormalized Claims**
   - Store commonly-checked grants in JWT claims
   - Avoid database lookup for basic checks
   - Refresh claims on grant changes

3. **Efficient Queries**
   - Index on `Grants.UserId` and `Grants.GrantTypeId`
   - Use `AsNoTracking()` for read-only grant queries
   - Batch load grants when checking multiple users

4. **Lazy Grant Loading**
   - Only load grants when first checked
   - Avoid loading all grants on every request
   - Profile to identify hot paths

### Required Tests

| Test | File | Purpose |
|------|------|---------|
| `PerformanceTests.GrantCheckUnder10ms()` | `ShiftManager.Tests/PerformanceTests/GrantPerformanceTests.cs` | Single check performance |
| `PerformanceTests.BulkGrantCheckUnder100ms()` | `ShiftManager.Tests/PerformanceTests/GrantPerformanceTests.cs` | Bulk check performance |
| `PerformanceTests.GrantCacheHitRate()` | `ShiftManager.Tests/PerformanceTests/GrantPerformanceTests.cs` | Caching effectiveness |

### Recovery Procedure

1. Identify slow endpoint from monitoring
2. Profile to find specific bottleneck
3. Apply targeted optimization (caching, query improvement, etc.)
4. Deploy and verify improvement

---

## FM-6: Circular Grant Delegation

### Description
Users with `CanGive` permission create circular chains of grant delegation, leading to confusing permission audit trails or potential for privilege escalation.

### Risk Assessment

| Aspect | Details |
|--------|---------|
| **Impact** | LOW - Confusing audit trails, no security breach if scope is enforced |
| **Likelihood** | LOW - `CanGive` is rare and typically only for directors |
| **Risk Level** | LOW |

### Example Scenario

```
User A grants ViewShifts to User B (via CanGive)
User B grants ViewShifts to User C (via CanGive)
User C grants ViewShifts to User A (circular!)
```

### Detection Strategies

1. **Admin Audit Log Review**
   - Report showing grant delegation chains
   - Flag chains longer than 3 levels
   - Identify circular references

2. **Automated Chain Analysis**
   - Nightly job traces all delegation chains
   - Alert on circular or deep chains
   - Dashboard visualization of delegation graph

### Mitigation Strategies

1. **Depth Limit on Delegation**
   - Maximum 3 levels of delegation depth
   - Grant check fails if chain is too deep
   - Error message explains the limit

2. **Circular Reference Prevention**
   - Before granting, check if recipient already granted to granter
   - Reject grants that would create a cycle
   - Log attempted circular grants for review

3. **Audit Trail**
   - Every grant records `GrantedByUserId`
   - Query can trace delegation chain
   - Admin can revoke entire chain if needed

### Required Tests

| Test | File | Purpose |
|------|------|---------|
| `CanGiveDelegationTests.CanUserGrantAsync_WithCanGiveTrue_ReturnsTrue` | `ShiftManager.Tests/UnitTests/Services/V3Hierarchy/GrantServiceTests.cs` | Basic delegation works |
| `CanGiveDelegationTests.GrantAsync_WithBroaderScope_ThrowsUnauthorized` | `ShiftManager.Tests/UnitTests/Services/V3Hierarchy/GrantServiceTests.cs` | Scope enforcement |
| `GrantDelegationTests.PreventCircularDelegation()` | `ShiftManager.Tests/UnitTests/Services/V3Hierarchy/GrantDelegationTests.cs` | Circular reference prevention |
| `GrantDelegationTests.EnforceDepthLimit()` | `ShiftManager.Tests/UnitTests/Services/V3Hierarchy/GrantDelegationTests.cs` | Depth limit enforcement |

### Recovery Procedure

1. Identify circular chain from audit report
2. Determine which grant to revoke to break cycle
3. Revoke grant and notify affected users
4. Review delegation policies with involved users

---

## Monitoring and Alerting Recommendations

### Key Metrics to Monitor

| Metric | Threshold | Alert Level | Response |
|--------|-----------|-------------|----------|
| 403 Error Rate | > 1% of requests | WARNING | Investigate permission issues |
| 403 Error Rate | > 5% of requests | CRITICAL | Possible system misconfiguration |
| Grant Check Latency P95 | > 50ms | WARNING | Performance tuning needed |
| Grant Check Latency P99 | > 100ms | CRITICAL | Immediate performance fix |
| Users with Zero Grants | Any | INFO | Review during business hours |
| Failed Grant Assignments | Any | WARNING | Investigate auto-grant failures |
| Cross-Company Access Attempts | Any | INFO | Log for audit, review weekly |

### Recommended Dashboards

1. **Grant Health Dashboard**
   - Grant check success/failure rates
   - Grant assignment trends
   - Users by grant count histogram

2. **Security Dashboard**
   - Cross-company access attempts
   - Delegation chain visualizations
   - Unusual permission patterns

3. **Performance Dashboard**
   - Grant check latency percentiles
   - Cache hit rates
   - Database query performance

### Alert Configuration

```yaml
# Example alert configuration (adapt to your monitoring system)

alerts:
  - name: High403ErrorRate
    condition: rate(http_responses_total{status="403"}) > 0.05
    severity: critical
    notification: pagerduty

  - name: SlowGrantChecks
    condition: histogram_quantile(0.95, grant_check_duration_seconds) > 0.05
    severity: warning
    notification: slack

  - name: UserWithNoGrants
    condition: count(users_with_grant_count{count="0"}) > 0
    severity: info
    notification: email
```

---

## Related Files

### Core Implementation
- `Services/GrantService.cs` - Grant checking implementation
- `Services/IGrantService.cs` - Grant service interface
- `Data/SeedData/GrantTypeSeed.cs` - Grant type definitions (114 grants)
- `Data/SeedData/RoleTemplateSeed.cs` - Role template grant mappings

### Test Files
- `ShiftManager.Tests/UnitTests/Services/V3Hierarchy/GrantServiceTests.cs` - Core grant tests
- `ShiftManager.Tests/IntegrationTests/DirectorCrossTenantTests.cs` - Cross-company isolation tests
- `ShiftManager.Tests/IntegrationTests/ProgramManagementAuthorizationTests.cs` - Authorization integration tests

### Documentation
- `docs/GRANTS_AND_ROLES.md` - Complete grant and role template reference

---

## Revision History

| Date | Author | Changes |
|------|--------|---------|
| 2026-02-06 | Grants Hardening Team | Initial document creation |
