# Navigation Completeness Audit & Fix

**Date:** 2026-02-23
**Status:** Approved

## Problem

Multiple management/owner/admin pages are unreachable from their respective hub pages. Users must know direct URLs or rely on the Legacy Owner Hub to access them. The Admin Hub is missing two entire categories of functionality.

## Audit Findings

### Owner Hub (New — `/Owner/Hub/Index`)
Currently has 7 dashboard cards and 8 quick links. **10 pages unreachable:**
- EmailConfig, EmailTemplates, GriffinConfig, GameConfig (infrastructure)
- DataLifecycle, Telemetry, LockedUsers (maintenance)
- MasterPrograms, AreaConfig, Permissions (scheduling/org)
- ExportUserData (data management)

### Admin Hub (`/Admin/Index`)
Currently has 4 sections (12 cards). **Missing 2 entire categories:**
- Organization & Structure: Organization Hub, Companies, JobTypes, Departments
- Configuration: Hierarchy Settings, ApprovalRules, Announcements, DutyRotation (FF), SetupTasks (FF)

### Director Hub (`/Director/Index`)
Mostly complete. No changes needed.

### Sidebar Navigation
Correct grant-based access. No changes needed.

### Context Switcher
Disappearing behavior is by design (feature-flagged + role-gated). No code change.

## Design Decisions

1. **Consolidate into New Owner Hub** — add all missing pages as quick links. Legacy Hub stays as optional fallback.
2. **Add 2 new sections to Admin Hub** — "Organization & Structure" and "Configuration" sections with relevant cards.
3. **Feature-flagged pages shown conditionally** — match existing sidebar behavior (hide card when flag is off).

## Changes

### 1. Owner Hub — Add 12 Quick Links

File: `Pages/Owner/Hub/Index.cshtml`

Add to the quick-links-grid (alongside existing 8):

| Link | Route | Icon |
|------|-------|------|
| Email Config | `/Owner/EmailConfig` | &#x2709; |
| Email Templates | `/Owner/EmailTemplates` | &#x1F4E7; |
| Griffin / ADFS | `/Owner/GriffinConfig` | &#x1F510; |
| Game Config | `/Owner/GameConfig` | &#x1F3AE; |
| Feature Flags | `/Owner/FeatureFlags` | &#x1F6A9; |
| Telemetry | `/Owner/Telemetry` | &#x1F4E1; |
| Data Lifecycle | `/Owner/DataLifecycle` | &#x267B; |
| Locked Users | `/Owner/LockedUsers` | &#x1F512; |
| Master Programs | `/Owner/MasterPrograms` | &#x1F4D0; |
| Area Config | `/Owner/AreaConfig` | &#x1F5FA; |
| Permissions | `/Owner/Permissions` | &#x1F6E1; |
| Export User Data | `/Owner/Hub/ExportUserData` | &#x1F4E4; |

Total quick links: 20. Grid layout handles any count via `auto-fit`.

### 2. Admin Hub — Add 2 New Sections (9 cards)

File: `Pages/Admin/Index.cshtml` + `Pages/Admin/Index.cshtml.cs`

**Section: "Organization & Structure"** (after People Management, 4 cards):
- Organization Hub → `/Admin/Organization`
- Companies → `/Admin/Companies`
- Job Types → `/Admin/Organization/JobTypes`
- Departments → `/Admin/Organization/Departments`

**Section: "Configuration"** (after Scheduling Configuration, 2-5 cards depending on flags):
- Hierarchy Settings → `/Admin/Settings` (always)
- Announcements → `/Admin/Announcements` (always)
- Approval Rules → `/Admin/Settings/ApprovalRules` (if `VacationApprovalEnabled`)
- Duty Rotations → `/Admin/DutyRotation` (if `DutyRotationEnabled`)
- Setup Tasks → `/Admin/SetupTasks` (if `SetupTasksEnabled`)

**PageModel changes:** Inject `IFeatureFlagService`, expose `DutyRotationEnabled`, `SetupTasksEnabled`, `VacationApprovalEnabled` booleans.

### 3. No Sidebar Changes

Sidebar navigation is grant-based and correct.

### 4. No Context Switcher Changes

Behavior is intentional. Single-company users see static display; feature flag controls rendering.

## Localization

All new text must use `<loc>` tags. New keys needed for:
- Owner Hub: 12 new quick link labels
- Admin Hub: 2 section titles + 9 card titles + 9 card descriptions
