# Session Handoff — Feedback Mechanism Consolidation

**Plan doc (read this first):** `docs/superpowers/plans/2026-04-01-feedback-consolidation.md`
The plan contains the complete JS implementation verbatim and the full file list.

---

## Goal
Eliminate 3 redundant feedback mechanisms from ShiftManager:
1. Delete `ErrorToast` ViewComponent (dead code, never invoked)
2. Replace TempData inline `<div class="alert ...">` blocks on ~6 pages with a TempData→Toast bridge in `_Layout.cshtml`
3. Replace 53 `confirm()` usages with a `data-confirm-modal` attribute pattern backed by `wwwroot/js/confirm-modal.js`

---

## Current Status
Zero code changed. This is a fresh start. Pre-conditions verified 2026-04-01:
- `Pages/Shared/Components/ErrorToast/Default.cshtml` exists — confirmed dead (0 callers)
- `ViewComponents/ErrorToastViewComponent.cs` exists — confirmed dead (0 callers)
- `wwwroot/js/confirm-modal.js` does NOT exist — must be created
- TempData bridge not in `_Layout.cshtml` — must be added

---

## Rollout Sequence

### Phase 1 — Delete ErrorToast dead code (30 min, zero risk)
Delete:
- `Pages/Shared/Components/ErrorToast/Default.cshtml`
- `ViewComponents/ErrorToastViewComponent.cs`

Verify before deleting: `grep -rn "ErrorToast" Pages/ Services/ --include="*.cs" --include="*.cshtml"` should return 0 results after deletion.

---

### Phase 2 — TempData → Toast bridge (2–3 hrs)

**Step 2a:** In `Pages/Shared/_Layout.cshtml`, expand the `<body>` tag to add `data-toast-*` attributes reading TempData (exact code in plan §3.2 Step 1). Use `HtmlEncoder.Default.Encode()` for XSS safety.

**Step 2b:** Add inline bridge script at BOTTOM of `<body>` (after deferred scripts — bridge must run after `toast-notifications.js`). Exact code in plan §3.2 Step 2.

**Step 2c:** Rename `TempData["Error"]` → `TempData["ErrorMessage"]` in these 5 PageModel files:
- `Pages/Api/SelectMolecule.cshtml.cs`
- `Pages/Admin/AuditLog.cshtml.cs`
- `Pages/Admin/Analytics.cshtml.cs`
- `Pages/Owner/SelectCompany.cshtml.cs`
- `Pages/Owner/LanguageEditMode.cshtml.cs`

**Step 2d:** Remove inline `<div class="alert ...">` TempData blocks from:
- `Pages/Admin/Analytics.cshtml`
- `Pages/Admin/Organization/Index.cshtml`
- `Pages/Admin/Users.cshtml`
- `Pages/Owner/EmailTemplates.cshtml` (only TempData alerts — do NOT remove the static `alert-info` blocks at lines 159/165)
- `Pages/Director/CompanyFilter.cshtml`
- `Pages/Director/ViewAsMode.cshtml`

---

### Phase 3 — Replace `confirm()` with Modal (3–5 hrs)

**Step 3a:** Create `wwwroot/js/confirm-modal.js` — full JS is verbatim in plan §3.1. Copy exactly.

**Step 3b:** Register in `_Layout.cshtml` after `toast-notifications.js`:
```html
<script defer src="~/js/confirm-modal.js" asp-append-version="true"></script>
```

**Step 3c:** Add `*_Title` localization keys to `Resources/SharedResources.resx` and `Resources/SharedResources.he-IL.resx` for each `ConfirmDelete*` / `ConfirmRevoke*` key (see plan §3.1 table).

**Step 3d:** Migrate all files with `confirm(`. Actual grep found **37 files** (more than the 18 in the plan). Replace `onsubmit="return confirm('...')"` / `onclick="return confirm('...')"` with `data-confirm-modal` attribute pattern (exact pattern in plan §3.1).

Full file list includes: all `Pages/Admin/Organization/*.cshtml`, `Pages/Admin/Users.cshtml`, `Pages/Admin/Companies.cshtml`, `Pages/Calendar/Table.cshtml`, `Pages/Friends/Index.cshtml`, `Pages/Requests/Index.cshtml`, `Pages/Calendar/Overview.cshtml`, `Pages/My/ApiKeys.cshtml`, `Pages/My/Requests.cshtml`, `Pages/Owner/Hub/RoleTemplates/Edit.cshtml`, `Pages/Owner/Hub/Grants.cshtml`, and more. Run `grep -rn "confirm(" Pages/ --include="*.cshtml"` for the full current list.

---

## Key Constraints

- **NO CSP nonce on script-src** — nonces cause browsers to ignore `'unsafe-inline'`, silently breaking all 80+ inline event handlers. Use `defer` only.
- Bridge script MUST be at BOTTOM of `<body>`, not `<head>` — must run after deferred `toast-notifications.js` executes.
- `arguments.callee` in the plan's JS (strict mode IIFE) — test in browser; if it throws, replace with a named function reference pattern.

---

## Key Files

- `docs/superpowers/plans/2026-04-01-feedback-consolidation.md` — FULL PLAN with all code
- `Pages/Shared/_Layout.cshtml` — `<body>` tag + deferred scripts block
- `Pages/Shared/Components/ErrorToast/Default.cshtml` — DELETE (Phase 1)
- `ViewComponents/ErrorToastViewComponent.cs` — DELETE (Phase 1)
- `wwwroot/js/confirm-modal.js` — CREATE (Phase 3)
- `Resources/SharedResources.resx` + `Resources/SharedResources.he-IL.resx` — add title keys
