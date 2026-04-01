# Feedback Mechanism Consolidation

**Date:** 2026-04-01
**Status:** Ready for implementation
**Priority:** Medium — UX consistency, developer ergonomics
**Effort estimate:** Medium (no schema changes, no service layer changes, pure UI/Razor/JS)

---

## 1. Background & Motivation

A full audit of ShiftManager's user feedback surface (April 2026) identified **12 distinct mechanisms** by which the application communicates status, errors, warnings, and confirmations to the user. Three of these overlap with more capable alternatives and add no unique value:

- **Native `confirm()`** — 53 usages across admin pages. Unstyled OS dialog, blocks the JavaScript thread, cannot be localized with RTL layout, and is visually inconsistent with the rest of the application.
- **TempData Inline Alert** — present on ~10 admin pages as dismissible `<div class="alert ...">` banners rendered after Post-Redirect-Get. Duplicates the Toast system visually without any benefit; developers must choose between two systems for the same purpose.
- **Server-Rendered Toast (ErrorToast ViewComponent)** — exists as a fallback toast when the JS `Toast` API is unavailable. In practice, this component is never invoked from any page (only the component definition file itself exists). It exists defensively for a scenario that never occurs in production.

**Goal:** Eliminate these three mechanisms, routing their use cases to the existing Toast and Modal systems. The result is a smaller, more consistent developer surface with clear rules for which mechanism to reach for.

---

## 2. Target Architecture

After consolidation, seven feedback mechanisms remain — each with a single, unambiguous purpose:

| Mechanism | Purpose | Trigger |
|---|---|---|
| **Toast** | Transient action feedback (saved, deleted, failed) | `Toast.success/error/warning/info(msg)` |
| **Modal Dialog** | Blocking decisions and destructive confirmations | `data-modal-confirm` attribute or lazy modal |
| **Error Banner** | Persistent page-level errors (API down, data failed to load) | `ErrorBanner` ViewComponent |
| **Inline Form Validation** | Per-field validation errors tied to a specific input | `asp-validation-for` + ModelState |
| **System Alert** | Site-wide admin maintenance/security announcements | `SystemAlerts` ViewComponent |
| **Bottom Sheet** | Mobile calendar shift assignments (touch-first) | Calendar cell tap on mobile |
| **Notification Center** | Persistent, historical in-app notifications | `/My/NotificationCenter` page |

Decision rule for developers going forward:

```
Did the user just complete an action?      → Toast
Is the user about to do something risky?   → Modal
Is the whole page broken?                  → Error Banner
Is a specific form field wrong?            → Inline Validation
Is the whole site affected?               → System Alert
```

---

## 3. Consolidation Work Items

### 3.1 — Replace `confirm()` with Modal Dialog

#### Current State

53 Razor view files use `onsubmit="return confirm('...')"` or `onclick="return confirm('...')"` to guard destructive form submissions. Affected pages include:

- `Pages/Admin/Announcements.cshtml`
- `Pages/Admin/Companies.cshtml`
- `Pages/Admin/Config.cshtml`
- `Pages/Admin/Directors.cshtml`
- `Pages/Admin/DutyRotation/Index.cshtml`
- `Pages/Admin/HomeTypes/Index.cshtml`
- `Pages/Admin/Organization/Areas/Index.cshtml`
- `Pages/Admin/Organization/Departments/Index.cshtml`
- `Pages/Admin/Organization/Grants/Index.cshtml`
- `Pages/Admin/Organization/Index.cshtml`
- `Pages/Admin/Organization/JobTypes/Index.cshtml`
- `Pages/Admin/Organization/Molecules/Index.cshtml`
- `Pages/Admin/Organization/Projects/Index.cshtml`
- `Pages/Admin/Organization/Roles/Index.cshtml`
- `Pages/Admin/Organization/ShiftGroupings/Index.cshtml`
- `Pages/Admin/Organization/Stores/Index.cshtml`
- `Pages/Admin/Settings/ApprovalRules.cshtml`
- `Pages/Admin/Users.cshtml`
- *(and others — run `grep -rn "confirm(" Pages/ --include="*.cshtml"` for full list)*

#### Problems

1. **No RTL support.** Hebrew users see an OS dialog that doesn't respect `dir="rtl"`.
2. **No localization override.** The message is set inline; there is no way to adjust formatting per locale.
3. **Thread-blocking.** `confirm()` blocks the browser's event loop.
4. **Visual inconsistency.** OS dialog looks nothing like the rest of the ShiftManager UI.
5. **No custom actions.** Cannot add context (e.g., what exactly will be deleted), styling, or secondary options.

#### Solution: `data-confirm-modal` Attribute Pattern

Implement a lightweight JavaScript interceptor that reads a `data-confirm-modal` attribute on any form or button, intercepts the submit/click event, shows a styled Modal, and only proceeds if the user confirms.

**New HTML pattern (replaces `onsubmit="return confirm(...)"`):**

```html
<!-- Before -->
<form method="post" asp-page-handler="Delete"
      onsubmit="return confirm('@Localizer["ConfirmDeleteCompany"].Value');">
  <input type="hidden" name="id" value="@c.Id" />
  <button type="submit" class="btn btn-danger">
    <loc key="Delete" />
  </button>
</form>

<!-- After -->
<form method="post" asp-page-handler="Delete"
      data-confirm-modal
      data-confirm-title="@Localizer["ConfirmDeleteCompany_Title"]"
      data-confirm-message="@Localizer["ConfirmDeleteCompany"]"
      data-confirm-action="@Localizer["Delete"]"
      data-confirm-level="danger">
  <input type="hidden" name="id" value="@c.Id" />
  <button type="submit" class="btn btn-danger">
    <loc key="Delete" />
  </button>
</form>
```

**For inline button clicks (non-form):**

```html
<!-- Before -->
<button onclick="return confirm('@Localizer["ConfirmRevokeGrant"]');"
        type="submit">Revoke</button>

<!-- After -->
<button type="submit"
        data-confirm-modal
        data-confirm-title="@Localizer["ConfirmRevokeGrant_Title"]"
        data-confirm-message="@Localizer["ConfirmRevokeGrant"]"
        data-confirm-action="@Localizer["Revoke"]"
        data-confirm-level="danger">
  <loc key="Revoke" />
</button>
```

#### New JavaScript: `confirm-modal.js`

Create `wwwroot/js/confirm-modal.js`. This script:

1. On `DOMContentLoaded`, attaches a `submit` interceptor to all `form[data-confirm-modal]` and a `click` interceptor to all `button[data-confirm-modal]` and `a[data-confirm-modal]`.
2. When triggered, reads `data-confirm-title`, `data-confirm-message`, `data-confirm-action`, `data-confirm-level` (defaults: title="Confirm", action="Confirm", level="danger").
3. Creates the modal DOM structure dynamically (or reuses a single `#confirm-modal` element injected once into `_Layout.cshtml`).
4. Shows the modal, returns focus to trigger element on cancel, submits/clicks on confirm.
5. Respects `dir` attribute on `<html>` for RTL button ordering.
6. Locks body scroll while modal is open.
7. Keyboard: `Escape` cancels, `Enter` confirms (when modal is focused).

```javascript
// wwwroot/js/confirm-modal.js
// Intercepts data-confirm-modal forms and buttons, shows a styled confirmation modal.

(function () {
  'use strict';

  const MODAL_ID = 'js-confirm-modal';

  function getOrCreateModal() {
    let modal = document.getElementById(MODAL_ID);
    if (modal) return modal;

    modal = document.createElement('div');
    modal.id = MODAL_ID;
    modal.setAttribute('role', 'dialog');
    modal.setAttribute('aria-modal', 'true');
    modal.setAttribute('aria-labelledby', `${MODAL_ID}-title`);
    modal.className = 'modal modal--sm';
    modal.innerHTML = `
      <div class="modal__header">
        <h2 class="modal__title" id="${MODAL_ID}-title"></h2>
        <button class="modal__close" aria-label="Close" data-action="cancel">×</button>
      </div>
      <div class="modal__body" id="${MODAL_ID}-body"></div>
      <div class="modal__footer">
        <button class="btn btn-ghost" data-action="cancel"></button>
        <button class="btn btn-danger" data-action="confirm"></button>
      </div>
    `;

    const backdrop = document.createElement('div');
    backdrop.className = 'modal-backdrop';
    backdrop.id = `${MODAL_ID}-backdrop`;

    document.body.appendChild(backdrop);
    document.body.appendChild(modal);
    return modal;
  }

  function showConfirmModal({ title, message, action, level, onConfirm, triggerEl }) {
    const modal = getOrCreateModal();
    const backdrop = document.getElementById(`${MODAL_ID}-backdrop`);

    modal.querySelector(`#${MODAL_ID}-title`).textContent = title || 'Confirm';
    modal.querySelector(`#${MODAL_ID}-body`).textContent = message || '';

    const cancelBtn = modal.querySelector('[data-action="cancel"]');
    const confirmBtn = modal.querySelector('[data-action="confirm"]');

    // Localize cancel text
    cancelBtn.textContent = document.documentElement.lang === 'he' ? 'ביטול' : 'Cancel';
    confirmBtn.textContent = action || 'Confirm';

    // Apply severity
    confirmBtn.className = `btn btn-${level === 'danger' ? 'danger' : 'primary'}`;

    // Show
    modal.classList.add('is-open');
    backdrop.classList.add('is-open');
    document.body.style.overflow = 'hidden';
    confirmBtn.focus();

    function close(confirmed) {
      modal.classList.remove('is-open');
      backdrop.classList.remove('is-open');
      document.body.style.overflow = '';
      if (triggerEl) triggerEl.focus();
      cleanup();
      if (confirmed) onConfirm();
    }

    function handleKey(e) {
      if (e.key === 'Escape') close(false);
      if (e.key === 'Enter' && document.activeElement === confirmBtn) close(true);
    }

    function cleanup() {
      cancelBtn.removeEventListener('click', cancelHandler);
      confirmBtn.removeEventListener('click', confirmHandler);
      backdrop.removeEventListener('click', cancelHandler);
      document.removeEventListener('keydown', handleKey);
    }

    const cancelHandler = () => close(false);
    const confirmHandler = () => close(true);

    cancelBtn.addEventListener('click', cancelHandler);
    confirmBtn.addEventListener('click', confirmHandler);
    backdrop.addEventListener('click', cancelHandler);
    document.addEventListener('keydown', handleKey);
  }

  function intercept(el) {
    const isForm = el.tagName === 'FORM';
    const eventName = isForm ? 'submit' : 'click';

    el.addEventListener(eventName, function (e) {
      e.preventDefault();
      e.stopImmediatePropagation();

      const dataset = el.dataset;
      const title = dataset.confirmTitle || '';
      const message = dataset.confirmMessage || '';
      const action = dataset.confirmAction || '';
      const level = dataset.confirmLevel || 'danger';

      showConfirmModal({
        title, message, action, level,
        triggerEl: e.target,
        onConfirm: () => {
          if (isForm) {
            el.removeEventListener('submit', arguments.callee);
            el.submit();
          } else {
            el.removeEventListener('click', arguments.callee);
            el.click();
          }
        }
      });
    });
  }

  document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('[data-confirm-modal]').forEach(intercept);
  });

  // Support dynamically added elements
  window.ConfirmModal = { intercept };
})();
```

#### New Localization Keys Required

For each existing `confirm()` message string, add a corresponding title key. Existing message keys stay — only titles are new:

| Existing message key | New title key |
|---|---|
| `ConfirmDeleteCompany` | `ConfirmDeleteCompany_Title` |
| `ConfirmDeleteAnnouncement` | `ConfirmDeleteAnnouncement_Title` |
| `ConfirmDeleteArea` | `ConfirmDeleteArea_Title` |
| `ConfirmDeleteDepartment` | `ConfirmDeleteDepartment_Title` |
| `ConfirmDeleteJobType` | `ConfirmDeleteJobType_Title` |
| `ConfirmDeleteMolecule` | `ConfirmDeleteMolecule_Title` |
| `ConfirmDeleteProject` | `ConfirmDeleteProject_Title` |
| `ConfirmDeleteGrouping` | `ConfirmDeleteGrouping_Title` |
| `ConfirmDeleteRotation` | `ConfirmDeleteRotation_Title` |
| `ConfirmRevokeGrant` | `ConfirmRevokeGrant_Title` |
| `ConfirmRevokeRole` | `ConfirmRevokeRole_Title` |
| `ConfirmBatchApproval` | `ConfirmBatchApproval_Title` |
| *(etc — one title per confirm message)* | |

Default English title values: `"Delete [EntityName]"` or `"Confirm Action"`.
Default Hebrew title values: `"מחיקת [שם]"` or `"אישור פעולה"`.

#### Registration in `_Layout.cshtml`

```html
<!-- Add after existing deferred scripts -->
<script defer src="~/js/confirm-modal.js" asp-append-version="true"></script>
```

---

### 3.2 — Replace TempData Inline Alerts with Toast-on-Page-Load

#### Current State

~10 Razor pages display TempData feedback as inline dismissible Bootstrap-style alert `<div>` elements. These use different CSS classes, different visual styles, and different dismiss patterns than the Toast system. The same result (e.g., "Company deleted successfully") looks different depending on whether it was triggered via AJAX (shows a toast) or a full page POST-redirect (shows an inline alert).

Affected views:
- `Pages/Admin/Analytics.cshtml`
- `Pages/Admin/Organization/Index.cshtml`
- `Pages/Admin/Users.cshtml`
- `Pages/Director/CompanyFilter.cshtml`
- `Pages/Director/ViewAsMode.cshtml`
- *(any page with `@if (TempData["SuccessMessage"] != null)` blocks)*

#### Problems

1. **Visual inconsistency.** An operation that succeeds via AJAX shows a toast; the same operation via form POST shows a banner. Same user action, different feedback.
2. **Developer inconsistency.** Developers must choose between two systems for the same intent.
3. **Layout disruption.** Inline alerts push page content down, causing layout shift.
4. **Code duplication.** Every affected page repeats the same 6-8 lines of Razor `@if` block.

#### Solution: TempData-to-Toast Bridge in `_Layout.cshtml`

The fix is a small script block in `_Layout.cshtml` that reads TempData values serialized as `data-*` attributes and fires the Toast API on page load. No changes to any PageModel — they continue setting `TempData["SuccessMessage"]` exactly as before.

**Step 1 — Serialize TempData into `<body>` in `_Layout.cshtml`**

In `_Layout.cshtml`, where `<body>` is opened, add data attributes:

```html
<body
  @if (TempData["SuccessMessage"] != null) { <text>data-toast-success="@HtmlEncoder.Default.Encode(TempData["SuccessMessage"].ToString())"</text> }
  @if (TempData["ErrorMessage"] != null)   { <text>data-toast-error="@HtmlEncoder.Default.Encode(TempData["ErrorMessage"].ToString())"</text> }
  @if (TempData["WarningMessage"] != null) { <text>data-toast-warning="@HtmlEncoder.Default.Encode(TtmlEncoder.Default.Encode(TempData["WarningMessage"].ToString()))"</text> }
  @if (TempData["InfoMessage"] != null)    { <text>data-toast-info="@HtmlEncoder.Default.Encode(TempData["InfoMessage"].ToString())"</text> }
>
```

**Step 2 — Fire Toast in `_Layout.cshtml` inline script**

Add to the existing inline initialization script (already present at bottom of `_Layout.cshtml`):

```javascript
// TempData → Toast bridge
(function() {
  var b = document.body;
  var s = b.dataset.toastSuccess;
  var e = b.dataset.toastError;
  var w = b.dataset.toastWarning;
  var i = b.dataset.toastInfo;
  if (s) Toast.success(s);
  if (e) Toast.error(e);
  if (w) Toast.warning(w);
  if (i) Toast.info(i);
})();
```

This must run **after** `toast-notifications.js` has loaded. Since `toast-notifications.js` is `defer`-loaded, this bridge script must also be deferred or placed at the bottom of `<body>`.

**Step 3 — Remove inline alert blocks from affected views**

Remove all occurrences of the pattern:

```html
@if (TempData["SuccessMessage"] != null)
{
    <div class="alert alert-success alert-dismissible fade show" ...>
        <span>@TempData["SuccessMessage"]</span>
        <button ...>×</button>
    </div>
}
@if (TempData["ErrorMessage"] != null)
{
    <div class="alert alert-danger alert-dismissible fade show" ...>
        <span>@(TempData["ErrorMessage"] ?? TempData["Error"])</span>
        ...
    </div>
}
```

These blocks can be deleted entirely — the layout bridge handles them.

**Note on `TempData["Error"]` alias:** Some pages use `TempData["Error"]` instead of `TempData["ErrorMessage"]`. During Step 3, update the PageModel side to use `TempData["ErrorMessage"]` uniformly, and update `_Layout.cshtml` to read only `"ErrorMessage"`. Do not keep both keys in the bridge — consolidate to `"ErrorMessage"`.

#### PageModel Convention (unchanged for most, standardized for `"Error"` alias)

PageModels continue to set TempData exactly as before:

```csharp
TempData["SuccessMessage"] = _localizer["Success_CompanyDeleted"].Value;
// or
TempData["ErrorMessage"] = _localizer["Error_Generic"].Value;
```

The only change is pages currently using `TempData["Error"]` (instead of `"ErrorMessage"`) must be updated to use `"ErrorMessage"` so the bridge key is consistent.

---

### 3.3 — Remove the ErrorToast ViewComponent

#### Current State

`Pages/Shared/Components/ErrorToast/Default.cshtml` and a corresponding `ErrorToastModel` ViewComponent class exist as a server-rendered toast fallback. The component is not invoked from any page in the codebase — only the component definition files themselves exist. It was likely created defensively in case the JS Toast API was unavailable.

#### Problems

1. **Dead code.** No page calls `@await Component.InvokeAsync("ErrorToast", ...)`.
2. **Confusing surface.** Its existence implies a use case that doesn't exist in practice.
3. **Maintenance burden.** Any future changes to toast styling must remember to update this component too.

#### Solution: Delete the component

Delete the following files:

```
Pages/Shared/Components/ErrorToast/Default.cshtml
Pages/Shared/Components/ErrorToast/ErrorToastModel.cs   (or wherever the model lives)
```

Verify before deletion:
```bash
grep -rn "ErrorToast" Pages/ Services/ --include="*.cs" --include="*.cshtml"
```

If no references exist beyond the component files themselves, delete. If references are found, migrate them to `Toast.error()` JS calls or TempData bridge before deleting.

---

## 4. Files Changed Summary

### New files
| File | Purpose |
|---|---|
| `wwwroot/js/confirm-modal.js` | `data-confirm-modal` interceptor — replaces all 53 `confirm()` usages |

### Modified files
| File | Change |
|---|---|
| `Pages/Shared/_Layout.cshtml` | Add `data-toast-*` attributes to `<body>` + TempData→Toast bridge script + `<script defer>` for `confirm-modal.js` |
| `Pages/Admin/Announcements.cshtml` | Replace `confirm()` with `data-confirm-modal`; remove TempData alert block |
| `Pages/Admin/Companies.cshtml` | Replace `confirm()` with `data-confirm-modal`; remove TempData alert block |
| `Pages/Admin/Config.cshtml` | Replace `confirm()` with `data-confirm-modal` |
| `Pages/Admin/Directors.cshtml` | Replace `confirm()` with `data-confirm-modal` |
| `Pages/Admin/DutyRotation/Index.cshtml` | Replace `confirm()` with `data-confirm-modal` |
| `Pages/Admin/HomeTypes/Index.cshtml` | Replace `confirm()` with `data-confirm-modal` |
| `Pages/Admin/Organization/Areas/Index.cshtml` | Replace `confirm()` with `data-confirm-modal` |
| `Pages/Admin/Organization/Departments/Index.cshtml` | Replace `confirm()` with `data-confirm-modal` |
| `Pages/Admin/Organization/Grants/Index.cshtml` | Replace `confirm()` with `data-confirm-modal` |
| `Pages/Admin/Organization/Index.cshtml` | Replace `confirm()` + remove TempData alert block |
| `Pages/Admin/Organization/JobTypes/Index.cshtml` | Replace `confirm()` with `data-confirm-modal` |
| `Pages/Admin/Organization/Molecules/Index.cshtml` | Replace `confirm()` with `data-confirm-modal` |
| `Pages/Admin/Organization/Projects/Index.cshtml` | Replace `confirm()` with `data-confirm-modal` |
| `Pages/Admin/Organization/Roles/Index.cshtml` | Replace `confirm()` with `data-confirm-modal` |
| `Pages/Admin/Organization/ShiftGroupings/Index.cshtml` | Replace `confirm()` with `data-confirm-modal` |
| `Pages/Admin/Organization/Stores/Index.cshtml` | Replace `confirm()` with `data-confirm-modal` |
| `Pages/Admin/Settings/ApprovalRules.cshtml` | Replace `confirm()` with `data-confirm-modal` |
| `Pages/Admin/Users.cshtml` | Replace `confirm()` + remove TempData alert block |
| `Pages/Admin/Analytics.cshtml` | Remove TempData alert block |
| `Pages/Director/CompanyFilter.cshtml` | Remove TempData alert block |
| `Pages/Director/ViewAsMode.cshtml` | Remove TempData alert block |
| `Resources/SharedResources.resx` | Add new `*_Title` localization keys for confirm dialogs |
| `Resources/SharedResources.he-IL.resx` | Add Hebrew translations for the same keys |
| *(any page using `TempData["Error"]`)* | Rename to `TempData["ErrorMessage"]` in PageModel |

### Deleted files
| File | Reason |
|---|---|
| `Pages/Shared/Components/ErrorToast/Default.cshtml` | Unused dead code |
| `Pages/Shared/Components/ErrorToast/ErrorToastModel.cs` | Unused dead code |

---

## 5. Rollout Sequence

The three work items are independent and can be done in any order, but this sequence is recommended:

**Phase 1 — Remove dead code (30 min)**
Delete the ErrorToast component (Section 3.3). Zero risk, zero user impact. Confirms the cleanup direction.

**Phase 2 — TempData bridge (2–3 hours)**
Implement the `_Layout.cshtml` bridge (Section 3.2). This is the highest UX value: it makes POST-redirect feedback visually consistent with AJAX feedback site-wide. Test each affected admin page manually after removing the inline alert blocks.

**Phase 3 — confirm() replacement (3–5 hours)**
Create `confirm-modal.js` and migrate all 53 `confirm()` usages (Section 3.1). This is the most file-touches but mechanically straightforward. Add the localization title keys as you go. Test a representative subset: at minimum, one form-submit confirm and one button-click confirm in both LTR and RTL mode.

---

## 6. Testing Plan

### Phase 1
- Grep for `ErrorToast` — confirm zero remaining references.

### Phase 2 (TempData Bridge)
For each affected admin page, perform a representative action that triggers `TempData["SuccessMessage"]` and verify:
- [ ] A toast appears in the top-right corner (LTR) or top-left (RTL/Hebrew)
- [ ] Toast shows the correct message text
- [ ] Toast auto-dismisses after ~5 seconds
- [ ] No inline `<div class="alert ...">` appears on the page
- [ ] Layout shift is eliminated (page height does not change when toast fires)

Pages to verify:
- [ ] `Admin/Analytics` — export action
- [ ] `Admin/Organization/Index` — any entity edit
- [ ] `Admin/Users` — user activate/deactivate
- [ ] `Director/CompanyFilter` — filter change
- [ ] `Director/ViewAsMode` — view-as activation

### Phase 3 (confirm() → Modal)
For each migrated page, verify:
- [ ] Clicking the delete/revoke button opens a styled modal (not OS dialog)
- [ ] Modal title and message text are correct
- [ ] Cancel button dismisses modal without performing action
- [ ] Confirm button performs the action
- [ ] Focus returns to trigger element after cancel
- [ ] Escape key cancels
- [ ] RTL (Hebrew): modal layout is mirrored, buttons in correct order
- [ ] Keyboard-only: Tab navigates between Cancel/Confirm, Enter confirms

Pages to verify (minimum representative set):
- [ ] `Admin/Companies` — Delete company
- [ ] `Admin/Organization/Grants/Index` — Revoke grant
- [ ] `Admin/Users` — Batch approval confirm
- [ ] `Admin/HomeTypes/Index` — Delete home type + unassign user
- [ ] `Admin/Organization/Index` — Any inline confirm

---

## 7. Design Decisions & Rationale

### Why not replace TempData with a SignalR push?
SignalR is available in the app. However, using it for POST-redirect feedback would be architecturally disproportionate. The bridge pattern (serialize to `data-*` on `<body>`, fire on load) is zero-latency, zero-infrastructure, and equally reliable. SignalR is appropriate for real-time multi-user coordination, not single-user action feedback.

### Why keep the TempData keys in PageModels unchanged?
Changing 393 PageModel assignments would be a large blast radius with no user-visible benefit. The bridge isolates the change to a single point: `_Layout.cshtml`. PageModels don't need to know how their messages are displayed.

### Why `data-confirm-modal` on the form, not on the button?
The intercept must prevent form submission, which is a `submit` event on the `<form>` element. Intercepting the `click` on the button works for some cases but can be bypassed (e.g., keyboard Enter on a focused form). Attaching to the form is more robust. For `<button>` elements outside forms, `onclick` interception is used.

### Why not use the existing modal-loader.js lazy-load system?
The lazy-load system (`data-modal-lazy`, `data-modal-url`) fetches modal content from a URL. Confirmation modals are always the same template with different text values — they don't need a server round-trip. The `data-confirm-modal` pattern is purely client-side and fires instantly.

### Why one reusable `#js-confirm-modal` element instead of creating one per trigger?
Creating a new DOM element per confirm call would leak event listeners and accumulate DOM nodes on pages with many delete buttons. A single modal element is shown/hidden with data swapped per call — the pattern used by the existing `modal-loader.js` for lazy modals.

---

## 8. Out of Scope

The following are **not** part of this consolidation:

- **Notification Center** — distinct persistent system, no overlap with Toast
- **Error Banner** — distinct persistent page-level system, no overlap with Toast
- **Bottom Sheet** — mobile-only calendar interaction, no overlap with Modal
- **Inline Form Validation** — field-level, cannot be replaced by Toast
- **System Alert** — site-wide admin mechanism, distinct semantic tier
- **Loading Spinner** — not a feedback mechanism, a loading state indicator
- **Improving Toast features** (e.g., action buttons, stacking behavior) — separate enhancement task
- **Improving Modal features** (e.g., multi-step flows) — separate enhancement task
- **Moving notification delivery to SignalR** — separate architectural task
