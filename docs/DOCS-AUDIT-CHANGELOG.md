# Documentation Audit & Reorganization — Change Log

**Date:** 2026-03-03
**Audited by:** Claude Opus 4.6
**Method:** Cross-referenced every doc against the codebase (services, models, pages, middleware, migrations)

---

## New Folder Structure

```
docs/
├── README.md                  ← Updated with new folder map and fixed links
├── CLAUDE.md                  ← Project instructions for Claude Code (unchanged)
├── DOCS-AUDIT-CHANGELOG.md   ← This file
│
├── inventory/  (9 files)      ← Factual snapshots of current system state
├── guides/     (15 files)     ← How-to documentation matching current code
├── reference/  (23 files)     ← Stable reference material
│   └── genesis/ (27 docs + 8 diagrams) ← Architecture deep-dive
├── testing/    (5 files)      ← QA plans, test data, policies
└── archive/    (82 files)     ← Historical docs kept for traceability
    ├── plans/                 ← 22 completed implementation plans
    ├── planner/               ← Old UI spec documents (Word + extracted XML)
    ├── audits/                ← Historical audit reports
    ├── ui-overhaul/           ← UI overhaul completion records
    └── misc/                  ← Superseded/informal documents
```

**Total files: 171 (all accounted for)**

---

## Files Moved (by destination)

### → `docs/inventory/` (9 files)
| File | Rationale |
|------|-----------|
| FEATURE-INVENTORY.md | Feature checklist — factual snapshot |
| CURRENT_STATE_REFERENCE.md | System state reference (Feb 2026) |
| DESIGNED_NOT_IMPLEMENTED.md | Feature implementation matrix |
| LOCALIZATION-CATALOG.md | Localization key reference |
| COMPONENT-LIBRARY.md | ViewComponent and design token catalog |
| BUNDLE-ANALYSIS.md | CSS/JS bundle size analysis |
| BROWSER-COMPATIBILITY.md | Detailed browser support matrix |
| BROWSER-SUPPORT.md | Summary browser support (different audience) |
| PERMISSION-AUDIT.md | Permission/grant audit snapshot |

### → `docs/guides/` (15 files)
| File | Rationale |
|------|-----------|
| USAGE_GUIDE.md | End-user guide |
| GRIFFIN_OWNER_GUIDE.md | Griffin ADFS setup and admin guide |
| CUSTOMIZATION_GUIDE.md | UI/UX customization how-to |
| DEPLOYMENT-CHECKLIST.md | Deployment verification procedures |
| DEPLOYMENT-SOP.md | Standard operating procedure for IIS deployment |
| DEPLOYMENT-CHECKLIST-OPS-CONSOLE.md | Ops Console deployment guide |
| TRANSLATION-WORKFLOW-GUIDE.md | Localization workflow |
| LANGUAGE_MANAGEMENT_TESTING_GUIDE.md | RTL/i18n testing guide |
| SCREEN-READER-TESTING-GUIDE.md | Accessibility testing guide |
| VISUAL-REGRESSION.md | Visual regression testing with Playwright |
| MIGRATIONS.md | EF Core migration guide |
| SEEDING_CONFIGURATION.md | Production seeding configuration |
| ICON-GENERATION.md | Brand icon generation guide |
| PRINT-STYLES.md | Print stylesheet guide |
| PERFORMANCE-PROFILING-CHECKLIST.md | Performance profiling procedures |

### → `docs/reference/` (23 files + genesis/)
| File | Rationale |
|------|-----------|
| project.md | Main technical reference (2100+ lines) |
| context.md | Project overview and tech stack |
| TERMINOLOGY.md | UI ↔ code terminology mapping |
| DESIGN_GUIDE.md | "Soft Air, Tactical Truth" design philosophy |
| KNOWN_LIMITATIONS.md | Known system limitations |
| API_DOCUMENTATION.md | REST API endpoint reference |
| API-CONTRACTS.md | API error/response format contracts |
| API-VERSIONING.md | API versioning policy |
| APIs_FOR_THE_NON_TECH.md | Non-technical API explainer |
| API_USE_CASES_AND_EXAMPLES.md | API usage examples (webhook section marked NOT IMPLEMENTED) |
| GRANTS-AND-PERMISSIONS-DEEP-DIVE.md | Grant system deep-dive (124 grant types) |
| GRANTS_FAILURE_MODES.md | Grant failure mode analysis |
| MAIL_SERVICE_DOCUMENTATION.md | Email service architecture |
| TRAINEE_ROLE_DESIGN.md | Trainee role technical design |
| BATCH_APPROVAL_FEATURE_DOCUMENTATION.md | Batch approval feature reference |
| AUDIT_AND_ANALYTICS_DESIGN.md | Audit/analytics architecture |
| OPS-CONSOLE-SCHEDULER.md | Shift program scheduler reference |
| RATE_LIMITING.md | Rate limiting implementation |
| DATA-PRIVACY.md | Data privacy guidelines |
| ESCALATION.md | Escalation procedures |
| TELEMETRY.md | Client telemetry documentation |
| CODE-SPLITTING.md | LazyLoader/ModalLoader infrastructure |
| RELEASE-NOTES-UI-OVERHAUL.md | v3.0.0 UI overhaul release notes |
| genesis/ (27 + 8) | Architecture deep-dive (verified accurate) |

### → `docs/testing/` (5 files)
| File | Rationale |
|------|-----------|
| COMPREHENSIVE-TEST-PLAN.md | Master test plan (950 test cases) |
| TEST-PLAN-REVIEW.md | Critical review with 26 blocking findings |
| TEST-DATA.md | Test user credentials and data strategy |
| FLAKY-TEST-POLICY.md | Flaky test handling policy |
| QA-IMPLEMENTATION-PLAN.md | QA strategy (24% complete) |

### → `docs/archive/` (82 files total)

#### archive/plans/ (22 files) — All completed implementation plans
All 22 plans were verified as **fully implemented** (100% implementation rate):
- Organizational hierarchy design (v1, v3)
- Announcements feed, CSS token migration, integration tests expansion
- Schedule export/print, military rank system
- Excel calendars (design + implementation)
- Scope-aware grants refactoring, dynamic roles
- Feature completion, noop services (design + implementation)
- Unified feature flags, audit remediation (design + implementation)
- Navigation completeness (design + implementation)
- Role-grant matrix (design + implementation)
- Feature completion progress tracker

#### archive/planner/ — Old specification documents
- Organizational hierarchy redesign plan (markdown)
- Planner_UI_Blind_Complete_Spec.docx + extracted XML artifacts
- Planner_UI_Blind_Spec_With_Images.docx + extracted image artifacts

#### archive/audits/
- 2026-01-26-v3-ui-access-audit.md — Historical UI access audit

#### archive/ui-overhaul/
- UI-OVERHAUL-IMPLEMENTATION-COMPLETE.md — Implementation checklist (Jan 2026)
- UI-OVERHAUL-PRODUCTION-COMPLETE.md — Production gate document (Jan 2026)

#### archive/misc/ (12 files)
| File | Rationale for archiving |
|------|------------------------|
| REDESIGN_MIGRATION_NOTES.md | Prior redesign cycle; superseded |
| REDESIGN_TESTING_CHECKLIST.md | Superseded by COMPREHENSIVE-TEST-PLAN.md |
| QA_REMEDIATION_PROGRESS.md | Stale (Feb 7, 2026 snapshot; 25 days behind) |
| TEST-REPORT.md | Historical test results snapshot |
| TESTING_RESULTS.md | Historical test results snapshot |
| EMPLOYEE_PROFILE_ENHANCEMENTS_DESIGN.md | Design partially implemented; status unclear |
| next week plan.md | Ad-hoc plan from Oct 2025; long superseded |
| tasks.md | Issue tracker from Oct 2025; all 6 issues resolved |
| adfs semi-description.md | Informal ADFS notes; superseded by GRIFFIN_OWNER_GUIDE.md |
| understandme.md | Informal onboarding doc; superseded by README + genesis |
| my_team_calendars_product_ux_spec_draft.md | UX spec draft; feature implemented |
| my_team_hebrew_ui_copy_all_user_facing_labels.md | Hebrew UI strings; covered by .resx files |

---

## Files Updated (content changes)

| File | Change | Rationale |
|------|--------|-----------|
| **README.md** | Added folder structure diagram; updated all internal links to new paths; fixed connection string key from `"DefaultConnection"` to `"Default"` | Reflect new structure; fix factual error |
| **reference/API_USE_CASES_AND_EXAMPLES.md** | Added NOT IMPLEMENTED warning to Webhook APIs section (line 1022+) and TOC entry | Webhooks are documented but do not exist in codebase — no controller, no model |

---

## Files Deleted

None. All files preserved (either in active location or archive).

---

## Key Audit Findings

### Genesis Docs: Excellent (25/27 accurate)
The 23-chapter genesis architecture docs plus supplements were **verified against the codebase** and found to be highly accurate. Only two docs have minor staleness:
- `08-UI-UX-ARCHITECTURE.md` — Page count outdated (says 66, actually 110+)
- `17-TESTING-STRATEGY.md` — Coverage stats likely unchanged from original low baseline

### Plans: 100% Implemented
All 22 implementation plans in `docs/plans/` describe features that are **fully implemented** in the codebase. Evidence verified: models, services, pages, migrations, seed data, and tests all present.

### Critical Content Issues Found
1. **Webhook API documented but not implemented** — `API_USE_CASES_AND_EXAMPLES.md` lines 1022-1471 describe webhook endpoints (`POST /api/v1/webhooks`) that don't exist. **Fixed: added warning header.**
2. **Connection string key wrong in README** — Said `"DefaultConnection"`, actual key is `"Default"`. **Fixed.**
3. **Test plan has blocking issues** — `TEST-PLAN-REVIEW.md` identifies 26 findings (credential mismatches, config conflicts) that must be resolved before test implementation.
4. **QA progress stalled** — `QA-IMPLEMENTATION-PLAN.md` shows only 15/66 items done; Phase 2 security items are not yet complete.
