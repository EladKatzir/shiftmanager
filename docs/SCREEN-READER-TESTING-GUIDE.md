# Screen Reader Flow Testing Guide (B-038)

## Overview

This document provides testing procedures for verifying screen reader compatibility in ShiftManager. Testing should be performed with NVDA (Windows), VoiceOver (macOS/iOS), and TalkBack (Android).

## Test Environment Setup

### Recommended Screen Readers
| Platform | Screen Reader | Download |
|----------|--------------|----------|
| Windows | NVDA | https://www.nvaccess.org/download/ |
| macOS | VoiceOver | Built-in (Cmd+F5) |
| iOS | VoiceOver | Settings > Accessibility > VoiceOver |
| Android | TalkBack | Settings > Accessibility > TalkBack |

### Browser Pairings
- **NVDA**: Firefox or Chrome
- **VoiceOver macOS**: Safari
- **VoiceOver iOS**: Safari
- **TalkBack**: Chrome

---

## Critical User Flows to Test

### 1. Login Flow
**Path:** `/Auth/Login`

| Step | Expected Behavior | ARIA Support |
|------|-------------------|--------------|
| Page load | "Login page" announced | `<main>` landmark |
| Email field | "Email, edit text, required" | `type="email"`, `required` |
| Password field | "Password, edit text, required" | `type="password"`, `required` |
| Login button | "Login, button" | `<button type="submit">` |
| Error message | Error announced with role="alert" | `role="alert"` on validation |

**Test Steps:**
1. Navigate to login page with screen reader
2. Tab through form fields - verify labels announced
3. Submit with invalid data - verify error announced
4. Submit successfully - verify navigation announced

### 2. Calendar Navigation
**Path:** `/Calendar/Month`, `/Calendar/Week`, `/Calendar/Day`

| Step | Expected Behavior | ARIA Support |
|------|-------------------|--------------|
| Page load | Calendar view type announced | Heading structure |
| Date navigation | Current date context announced | `aria-label` on nav buttons |
| Calendar cells | Date and shift count announced | `aria-label` on cells |
| Shift items | Shift details announced on focus | Item has accessible name |
| Quick actions | Action buttons announced | Button roles |

**Test Steps:**
1. Navigate to calendar with month view
2. Use arrow keys to navigate calendar grid
3. Verify date announcement on each cell
4. Activate a cell - verify shift details
5. Navigate between Month/Week/Day views

### 3. Scope Switcher
**Path:** All pages (header component)

| Step | Expected Behavior | ARIA Support |
|------|-------------------|--------------|
| Dropdown trigger | "Scope selector, button, expanded/collapsed" | `aria-expanded`, `aria-haspopup` |
| Option list | "listbox" role announced | `role="listbox"` |
| Options | Option name + selected state | `role="option"`, `aria-selected` |
| Selection | Selection change announced | Live region update |

**Test Steps:**
1. Tab to scope switcher
2. Press Enter/Space to open
3. Arrow through options - verify announcements
4. Select an option - verify confirmation

### 4. Form Submission (Shift Creation/Edit)
**Path:** Various modal forms

| Step | Expected Behavior | ARIA Support |
|------|-------------------|--------------|
| Modal open | Modal title announced, focus trapped | `role="dialog"`, `aria-modal` |
| Form fields | Field labels and types announced | `<label>` associations |
| Required fields | "Required" announced | `required` attribute |
| Validation errors | Errors announced immediately | `aria-describedby`, `role="alert"` |
| Success | Toast notification announced | `role="status"`, `aria-live` |

**Test Steps:**
1. Open shift creation modal
2. Tab through all fields
3. Submit with missing required fields
4. Verify error announcements
5. Fix errors and submit successfully

### 5. Data Tables (Admin/Users, Audit Log)
**Path:** `/Admin/Users`, `/Admin/AuditLog`

| Step | Expected Behavior | ARIA Support |
|------|-------------------|--------------|
| Table structure | "Table with X rows, Y columns" | Semantic `<table>` |
| Headers | Column headers announced | `<th scope="col">` |
| Row navigation | Row context maintained | Semantic `<tr>` |
| Action buttons | Action + row context | Button in cell |
| Pagination | Page info and controls announced | Pagination component ARIA |

**Test Steps:**
1. Navigate to users table
2. Enter table with screen reader table mode
3. Navigate rows/columns - verify announcements
4. Activate row actions
5. Use pagination controls

### 6. Error States and Notifications
**Path:** All pages

| Step | Expected Behavior | ARIA Support |
|------|-------------------|--------------|
| Toast notification | Message announced immediately | `role="status"`, `aria-live="polite"` |
| Error banner | Error announced assertively | `role="alert"`, `aria-live="assertive"` |
| Offline banner | Status change announced | `role="status"` |
| Loading states | Loading announced | `aria-busy`, `aria-live` |

**Test Steps:**
1. Trigger a success action - verify toast announcement
2. Trigger an error - verify alert announcement
3. Go offline (DevTools) - verify offline banner announcement
4. Navigate during loading - verify loading state

---

## Landmark Navigation

ShiftManager uses semantic HTML landmarks:

| Landmark | Element | Purpose |
|----------|---------|---------|
| Banner | `<header>` | Site header with navigation |
| Navigation | `<nav>` | Primary navigation, scope switcher |
| Main | `<main>` | Primary page content |
| Complementary | `<aside>` | Sidebar, widgets |
| Contentinfo | `<footer>` | Site footer |

**Test:** Use screen reader landmark navigation (NVDA: D key, VoiceOver: rotor) to jump between landmarks.

---

## Heading Structure

Each page should have a logical heading hierarchy:

```
h1: Page title (one per page)
  h2: Section headings
    h3: Subsection headings
      h4: Further subdivisions
```

**Test:** Use screen reader heading navigation (NVDA: H key, VoiceOver: rotor) to verify structure.

---

## Keyboard Navigation Checklist

| Element | Key | Expected Behavior |
|---------|-----|-------------------|
| All interactive | Tab | Focus moves to next element |
| All interactive | Shift+Tab | Focus moves to previous element |
| Buttons | Enter/Space | Activate button |
| Links | Enter | Follow link |
| Dropdowns | Arrow keys | Navigate options |
| Modals | Escape | Close modal |
| Calendar cells | Arrow keys | Navigate grid |
| Tabs | Arrow keys | Switch between tabs |

---

## ARIA Patterns Implemented

### Live Regions
- **Polite** (`aria-live="polite"`): Toast notifications, status updates
- **Assertive** (`aria-live="assertive"`): Error alerts, critical notifications

### Expanded/Collapsed
- Scope switcher: `aria-expanded`
- Dropdowns: `aria-expanded`
- Collapsible sections: `aria-expanded`

### Selected State
- Calendar items: `aria-selected`
- List options: `aria-selected`
- Tab panels: `aria-selected`

### Busy State
- Loading content: `aria-busy="true"`
- Skeleton loaders: `aria-busy="true"`

---

## Common Issues to Check

### 1. Missing Labels
- [ ] All form inputs have associated labels
- [ ] Icon-only buttons have `aria-label`
- [ ] Images have alt text or `aria-hidden="true"`

### 2. Focus Management
- [ ] Focus visible on all interactive elements
- [ ] Focus trapped in modals
- [ ] Focus returned after modal close
- [ ] No focus loss on dynamic content

### 3. Dynamic Content
- [ ] AJAX updates announced via live regions
- [ ] Loading states announced
- [ ] Error messages announced immediately

### 4. Color Contrast
- [ ] Text meets 4.5:1 ratio (normal text)
- [ ] Large text meets 3:1 ratio
- [ ] Focus indicators meet 3:1 ratio

---

## Files with ARIA Implementation

### View Components
- `Pages/Shared/Components/ErrorBanner/Default.cshtml` - Error alerts
- `Pages/Shared/Components/ErrorToast/Default.cshtml` - Toast notifications
- `Pages/Shared/Components/LoadingSpinner/Default.cshtml` - Loading states
- `Pages/Shared/Components/LoadingSkeleton/Default.cshtml` - Skeleton loaders
- `Pages/Shared/Components/ScopeSwitcher/Default.cshtml` - Dropdown pattern
- `Pages/Shared/Components/Pagination/Default.cshtml` - Page navigation

### JavaScript Modules
- `wwwroot/js/keyboard-nav.js` - Keyboard navigation
- `wwwroot/js/modal-focus.js` - Focus trapping
- `wwwroot/js/error-boundary.js` - Error handling with ARIA
- `wwwroot/js/toast-notifications.js` - Live region announcements
- `wwwroot/js/offline-handler.js` - Status announcements
- `wwwroot/js/site.js` - General ARIA management

---

## Testing Checklist

### Pre-Release Testing
- [ ] All critical flows tested with NVDA
- [ ] All critical flows tested with VoiceOver
- [ ] Landmark navigation verified
- [ ] Heading structure verified
- [ ] Form labels verified
- [ ] Error announcements verified
- [ ] Dynamic content announcements verified
- [ ] Focus management verified

### Known Limitations
1. Calendar grid navigation optimized for sighted keyboard users; screen reader users may prefer list view
2. Some complex interactions (drag-drop for shift swapping) require alternative keyboard methods
3. Chart/graph content in reports may need supplementary data tables

---

## Resources

- [WCAG 2.1 Guidelines](https://www.w3.org/WAI/WCAG21/quickref/)
- [ARIA Authoring Practices](https://www.w3.org/WAI/ARIA/apg/)
- [NVDA User Guide](https://www.nvaccess.org/files/nvda/documentation/userGuide.html)
- [VoiceOver User Guide](https://support.apple.com/guide/voiceover/welcome/mac)

---

## Revision History

| Date | Version | Author | Changes |
|------|---------|--------|---------|
| 2026-02-01 | 1.0 | Claude (B-038) | Initial guide creation |
