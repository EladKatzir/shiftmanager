# Feedback Modal Consolidation Plan

**Goal:** Replace dual toast+banner feedback with a single feedback modal reusing confirm-modal infrastructure.

---

## Architecture Decision

**Extend confirm-modal.js with a feedback mode** rather than creating a separate file. The existing modal already has DOM management, accessibility, keyboard handling, and backdrop logic. A feedback modal is a confirm modal with one button and no cancel.

Expose:  alongside existing .

---

## Phase 1: Create Feedback Mode in confirm-modal.js + CSS

### File: wwwroot/js/confirm-modal.js

Add showFeedbackModal(opts) reusing getOrCreateModal():

1. Hide Cancel button (cancelBtn.style.display = none)
2. Hide X close button (closeBtn.style.display = none)
3. Relabel Confirm button to OK (English) or Hebrew equivalent based on document.documentElement.lang
4. Add base class modal--feedback plus severity class modal--feedback-{type} to the modal element
5. Prepend icon SVG to modal__body (reuse icons from toast-notifications.js: checkmark, X, triangle, info)
6. Auto-generate title by type+lang if not provided (Success, Error, Warning, Info / Hebrew equivalents)
7. onConfirm = no-op (just closes). Backdrop click and Escape also close.
8. On close, remove the modal--feedback* classes and restore Cancel/Close button display for next confirm-modal use

Expose: window.FeedbackModal = { show: function(type, message, title) { showFeedbackModal({type, message, title}); } };

### File: wwwroot/css/components.css (after ~line 835)

Add:
- .modal--feedback .modal__body: text-align center
- .modal--feedback-success .modal__header: border-bottom-color var(--success)
- .modal--feedback-success .feedback-icon: color var(--success)
- Same for error (--danger), warning (--warning), info (--primary)
- .feedback-icon: flex centered, margin-bottom, svg 48x48
- OK button color matches severity

---

## Phase 2: Rewire _Layout.cshtml Bridge

### File: Pages/Shared/_Layout.cshtml (lines 1094-1108)

Replace Toast.success/error/warning/info with FeedbackModal.show calls.
Use else-if chain (only one modal). Keep data-toast-* body attributes as transport.

---

## Phase 3: Remove Inline Alert Banners (~38+ pages)

Delete from each .cshtml: the if-block checking Model.Success/Model.Error rendering div.alert.
Delete from each .cshtml.cs: TempData-to-property reads in OnGetAsync.

Audit: if any page sets Error directly (not from TempData) for validation, convert to ModelState first.

Pages: Admin(7), Assignments(1), Auth(5), Chores(1), My(5), Owner(15), Public(3), Other(2), Organization(12) = ~48 files.

---

## Phase 4: Clean Up LocalizedPageModel

### File: Pages/LocalizedPageModel.cs

Remove Success and Error properties once grep confirms zero .cshtml references.

---

## Phase 5: Keep Toast for JS-Only Use

Toast system stays for AJAX operations. Only the _Layout TempData bridge changes consumer.

---

## Edge Cases

1. Cross-page redirects: Work fine -- TempData survives redirect, _Layout reads it on destination
2. Error without redirect: Convert to ModelState + asp-validation-summary
3. Multiple messages: else-if chain prioritizes error > warning > info > success

---

## Verification

1. Phase 1: Console test FeedbackModal.show in both languages
2. Phase 2: POST action triggers modal instead of toast
3. Phase 3: grep Model.Success in .cshtml = 0 results
4. Phase 4: dotnet build succeeds
5. Regression: one page per category, English + Hebrew

---

## Order

Phase 1+2 = single commit. Phase 3 = incremental or batch. Phase 4 = final cleanup.

**Estimated effort: ~4 hours total**
