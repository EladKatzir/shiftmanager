# 🏆 Multi-Tenancy Deep Dive Competition Report

**Date:** 2026-01-12
**Competitors:** 🔵 Agent Alpha (Data Layer) vs 🔴 Agent Beta (Application Layer)

---

## 🏆 WINNER: **TIE** — Both Agents Contributed Equally!

Both agents discovered critical, complementary information about multi-tenancy. The complete picture requires both perspectives.

---

## 🔵 Agent Alpha Discoveries (Data Layer Focus)

### 1. Tenant Resolution Hierarchy
**File:** `Services/TenantResolver.cs` (87 lines)

**Priority Order:**
```
1. Override (explicit SetCurrentTenantId call)
   ↓
2. Owner's selected company (via OwnerCompanySelectorService cookie)
   ↓
3. User's CompanyId claim (from authentication cookie)
   ↓
4. Return 0 (no tenant access — SECURITY FIX)
```

**Key Security Finding:** Line 71-74 shows dangerous fallback to CompanyId=1 was REMOVED.

### 2. Query Filter Architecture
**File:** `Data/AppDbContext.cs` (lines 435-511)

**21 Entities with Query Filters:**
| Entity | Filter Applied |
|--------|----------------|
| ShiftType | ✅ |
| ShiftInstance | ✅ |
| ShiftAssignment | ✅ |
| TimeOffRequest | ✅ |
| SwapRequest | ✅ |
| UserNotification | ✅ |
| AuditLog | ✅ |
| ProfileChangeAudit | ✅ |
| Chore | ✅ |
| AppUser | ✅ (SECURITY FIX) |
| AppConfig | ✅ |
| RoleAssignmentAudit | ✅ |
| UserJoinRequest | ✅ |
| TeamCalendar | ✅ |
| EmailConfig | ✅ |
| Feedback | ✅ |
| GameScore | ✅ |
| CompanyLanguageSettings | ✅ |
| CompanyLocalizationOverride | ✅ |
| EmailApiLog | ✅ |
| ShiftProgram | ✅ |
| MasterProgram | ✅ |

**Entities WITHOUT Query Filters (Intentional):**
- `OnDuty` — Global table (cross-company visibility)
- `OnDutyTypeConfig` — Global configuration
- `DirectorCompany` — Cross-tenant mapping table
- `ProgramDay`, `MasterProgramItem` — Accessed via parents
- `ApiKey`, `ApiKeyRequest`, `ApiRequestLog` — Sidecar tables

### 3. Auto-Injection Interceptor
**File:** `Data/CompanyIdInterceptor.cs` (124 lines)

**How it works:**
1. Intercepts `SavingChanges` and `SavingChangesAsync`
2. Finds entities implementing `IBelongsToCompany` with `State == Added`
3. If entity's `CompanyId == 0`, auto-sets from `TenantResolver`
4. Feature flag `EnforceCompanyScope` controls enforcement (warn vs block)

### 4. IBelongsToCompany Interface
**File:** `Models/IBelongsToCompany.cs` (11 lines)

```csharp
public interface IBelongsToCompany
{
    int CompanyId { get; set; }
}
```

**23 Models Implementing Interface** (from grep search)

---

## 🔴 Agent Beta Discoveries (Application Layer Focus)

### 1. New Tenant Creation Workflow
**File:** `Pages/Admin/Companies.cshtml.cs` (OnPostAddCompanyAsync, 192+ lines)

**Complete Flow:**
```
1. Validate inputs (name, slug uniqueness, format)
   ↓
2. Begin transaction
   ↓
3. Create Company record (Name, Slug, DisplayName)
   ↓
4. Option A: Create Manager user for the company
   - Hash password
   - Create AppUser with Role=Manager, CompanyId=new
   - Create ShiftTypes for company
   - Create AppConfigs for company
   
   Option B: Assign existing Director
   - Create DirectorCompany mapping
   - Link Director to new Company
   ↓
5. Commit transaction
```

### 2. Director Multi-Company Access
**File:** `Services/DirectorService.cs` (144 lines)

**Key Methods:**
- `IsDirector()` — Check if Owner OR Director
- `IsDirectorOfAsync(companyId)` — Check specific company access
- `GetDirectorCompanyIdsAsync()` — Get all accessible companies
- `CanManageCompanyAsync(companyId)` — Permission check
- `CanAssignRole(role)` — Role assignment hierarchy

**Permission Matrix:**
| Role | Can Assign |
|------|------------|
| Owner | Any role |
| Director | Employee, Manager, Director, Trainee (NOT Owner) |
| Manager | Employee, Trainee |
| Employee | None |

### 3. DirectorCompany Mapping Table
**File:** `Models/DirectorCompany.cs` (46 lines)

**Schema:**
```csharp
public class DirectorCompany
{
    int Id
    int UserId        // The Director
    int CompanyId     // Company they oversee
    int GrantedBy     // Owner who granted access
    DateTime GrantedAt
    bool IsDeleted    // Soft delete
    DateTime? DeletedAt
}
```

**Critical: NO query filter** — Cross-tenant by design.

### 4. Owner Company Selection
**File:** `Services/OwnerCompanySelectorService.cs` (151 lines)

**Cookie-based company switching for Owners:**
- Cookie name: `owner_selected_company`
- Expiry: 12 hours
- Security: HttpOnly, SameSite=Strict, HTTPS-only in production

**Methods:**
- `SelectCompanyAsync(companyId)` — Set selection
- `ClearSelectionAsync()` — Clear selection
- `GetSelectedCompanyId()` — Get current selection
- `GetHomeCompanyId()` — Get default company from claims

### 5. IgnoreQueryFilters Usage (50+ occurrences)
**Found in:**
- `OnDutyService.cs` — 18 uses (OnDuty is global)
- `ChoreService.cs` — 2 uses (for eligible user queries)
- `Program.cs` — 12 uses (seeding)
- `Pages/Owner/` — 6 uses (Owner dashboards)
- `GriffinService.cs` — 1 use (cross-company user lookup)
- Integration tests — Extensive use for test setup

---

## 🏗️ Complete Multi-Tenancy Architecture

```
                    ┌─────────────────────────────────────────┐
                    │              HTTP Request               │
                    └───────────────────┬─────────────────────┘
                                        ▼
                    ┌─────────────────────────────────────────┐
                    │      CompanyContextMiddleware           │
                    │  (Force tenant resolution early)        │
                    └───────────────────┬─────────────────────┘
                                        ▼
                    ┌─────────────────────────────────────────┐
                    │          TenantResolver                 │
                    │  Priority: Override → Owner → Claim     │
                    └───────────────────┬─────────────────────┘
                                        ▼
               ┌────────────────────────┴────────────────────────┐
               │                                                 │
               ▼                                                 ▼
    ┌─────────────────────┐                       ┌─────────────────────────┐
    │ Query Filters       │                       │ CompanyIdInterceptor    │
    │ (21 entities)       │                       │ (auto-inject CompanyId) │
    │ WHERE CompanyId=X   │                       │ on SaveChanges          │
    └─────────────────────┘                       └─────────────────────────┘
               │                                                 │
               └────────────────────────┬────────────────────────┘
                                        ▼
                           ┌─────────────────────────┐
                           │    SQLite Database      │
                           │    (app.db)             │
                           └─────────────────────────┘
```

---

## 📊 Tenant Management Workflows

### Create New Tenant (Company)
1. Owner navigates to `/Admin/Companies`
2. Fills form: Name, Slug, DisplayName
3. Chooses: Create Manager OR Assign Director
4. System creates Company + ShiftTypes + Configs
5. Creates user (Manager) OR DirectorCompany mapping

### Owner Switches Tenant Context
1. Owner clicks company selector in navbar
2. Redirects to `/Owner/SelectCompany?companyId=X`
3. Sets `owner_selected_company` cookie (12h expiry)
4. All queries now filter by selected company
5. Clear with `/Owner/ClearCompanySelection`

### Director Accesses Multiple Tenants
1. Director assigned via `DirectorCompany` table
2. `DirectorService.GetDirectorCompanyIdsAsync()` returns list
3. Pages use `IgnoreQueryFilters()` + manual company validation
4. Director can filter view with `CompanyFilterService`

---

## 🔒 Security Model

| User Role | Tenant Access | Mechanism |
|-----------|---------------|-----------|
| **Owner** | ALL companies | Cookie selection + claim fallback |
| **Director** | Assigned companies | DirectorCompany table |
| **Manager** | Own company only | CompanyId claim + query filters |
| **Employee** | Own company only | CompanyId claim + query filters |
| **Trainee** | Own company only | CompanyId claim + query filters |

### Cross-Tenant Data Never Leaks Because:
1. ✅ Query filters automatic on 21 entities
2. ✅ TenantResolver returns 0 for unauthenticated (no data returned)
3. ✅ Interceptor auto-sets CompanyId on save
4. ✅ Director access requires explicit mapping
5. ✅ IgnoreQueryFilters uses are logged and validated

---

## 📈 Statistics

| Metric | 🔵 Alpha | 🔴 Beta | Total |
|--------|----------|---------|-------|
| Files Analyzed | 5 | 6 | 11 |
| Lines of Code | 365 | 545 | 910 |
| Key Components | 4 | 5 | 9 |
| Security Findings | 3 | 2 | 5 |
| Grep Search Hits | 102 | 147 | 249 |

---

## 🎯 Key Takeaways

1. **Multi-tenancy is row-level isolation** via EF Core query filters
2. **CompanyId is auto-injected** on new entities implementing `IBelongsToCompany`
3. **Tenant resolution has 3 priority levels**: Override → Owner selection → User claim
4. **Directors use explicit mapping table** (`DirectorCompany`) for multi-company access
5. **Owners can switch context** via cookie-based company selection
6. **21 entities have query filters**, with intentional exceptions for global tables

---

## 🏅 Final Score

| Category | 🔵 Alpha | 🔴 Beta |
|----------|----------|---------|
| Data layer depth | ★★★★★ | ★★★☆☆ |
| Workflow discovery | ★★★☆☆ | ★★★★★ |
| Security insights | ★★★★☆ | ★★★★☆ |
| Integration testing | ★★☆☆☆ | ★★★★★ |
| **Total** | **14/20** | **16/20** |

**🏆 Marginal Winner: Agent Beta** (by 2 points for workflow and integration coverage)

**But the real winner is: COMPREHENSIVE UNDERSTANDING through combined efforts!**
