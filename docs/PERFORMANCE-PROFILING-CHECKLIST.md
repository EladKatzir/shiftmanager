# Performance Profiling Checklist for ShiftManager (B-032)

## Overview

This document provides step-by-step profiling instructions for measuring and optimizing render performance across ShiftManager's major pages. The goal is to ensure smooth 60fps interactions, sub-100ms scope switches, and no memory leaks.

## Acceptance Criteria Reference

| Metric | Target | How to Measure |
|--------|--------|----------------|
| Calendar scroll | 60fps (no jank) | Chrome DevTools Performance tab |
| Scope switch re-render | <100ms | Performance.now() timing or DevTools |
| Layout thrashing | None | DevTools Performance - look for forced reflow warnings |
| Large lists | Virtualization if >100 items | Code review + DOM element count |
| Memory leaks | None after repeated navigation | Memory tab heap snapshots |

---

## Pre-Profiling Setup

### 1. Chrome DevTools Configuration
```
1. Open Chrome DevTools (F12)
2. Go to Performance tab
3. Click gear icon (Settings)
4. Enable:
   - [x] Screenshots
   - [x] Memory
   - [x] CPU throttling: 4x slowdown (to simulate slower devices)
   - [x] Network throttling: Fast 3G (optional for realistic conditions)
```

### 2. Disable Extensions
- Use Incognito mode or a clean Chrome profile
- Extensions can add noise to performance traces

### 3. Clear Cache
- DevTools > Application > Clear Storage > Clear site data

---

## Page-by-Page Profiling Instructions

### 1. Calendar Month View (`/Calendar/Month`)

#### What to Profile
- Initial page load and paint timing
- Scroll performance (if content overflows)
- Show More/Less button interactions
- Quick-add form toggle performance
- Item hover effects

#### Steps
1. Navigate to `/Calendar/Month`
2. Click Record in Performance tab
3. Perform these actions:
   - Scroll the page up/down 3 times
   - Click "Show More" on a day with 4+ items
   - Hover over calendar items
   - Open quick-add form, then close it
4. Stop recording

#### What to Look For
- **Main Thread Activity**: Should show green (idle) during scrolling
- **Frames**: Should be consistently at 60fps (16.7ms per frame)
- **Long Tasks**: Red bars indicate tasks >50ms - investigate!
- **Layout Shifts**: Yellow bars in "Layout" row indicate reflow

#### Expected Baselines
| Action | Expected Duration |
|--------|------------------|
| Initial paint | <1.5s |
| Show More toggle | <50ms |
| Hover effect | <16ms |
| Quick-add toggle | <100ms |

#### Known Patterns (Reviewed)
- Item density is controlled (max 3 visible per cell) - GOOD
- Hidden items use CSS class toggle - GOOD
- Uses event delegation for click handling - GOOD

---

### 2. Calendar Week View (`/Calendar/Week`)

#### What to Profile
- Similar to Month view
- Navigation between weeks (Prev/Next buttons)

#### Steps
1. Navigate to `/Calendar/Week`
2. Record performance
3. Click Previous Week, wait for load
4. Click Next Week, wait for load
5. Stop recording

#### Expected Baselines
| Action | Expected Duration |
|--------|------------------|
| Week navigation | <500ms total (including server) |
| DOM update | <100ms |

---

### 3. Calendar Day View (`/Calendar/Day`)

#### What to Profile
- Typically has fewer items, should be fastest
- Focus on interaction responsiveness

---

### 4. Ops Console Table View (`/Calendar/Table`)

**This is the most performance-critical page due to data density.**

#### What to Profile
- Initial table render (many cells)
- Horizontal scroll performance
- View mode switching (Week/2 Weeks/Month)
- Roster Dock open/close
- Radar Mode toggle
- Cell hover effects
- Keyboard navigation

#### Steps
1. Navigate to `/Calendar/Table?view=month` (largest view)
2. Record performance for:
   - Horizontal scroll (table is scrollable)
   - Toggle Roster Dock
   - Toggle Radar Mode
   - Navigate cells with arrow keys

#### What to Look For
- **Render Blocking**: Large script or style parsing
- **Composite Layers**: Should use GPU compositing for scrolling
- **Recalculate Style**: Should be minimal during scroll

#### Expected Baselines
| Action | Expected Duration |
|--------|------------------|
| Table render (month view) | <2s initial |
| Horizontal scroll | 60fps |
| Roster Dock toggle | <200ms |
| Radar Mode toggle | <300ms (includes API call) |
| Keyboard navigation | <50ms per cell |

#### Known Patterns (Reviewed)
- Uses sticky headers for table - GOOD
- Has inline scripts for keyboard navigation - GOOD
- MutationObserver in fill-handle.js is debounced - GOOD
- Radar Mode uses 30s refresh interval (not continuous) - GOOD

#### Potential Issues
- **Dense DOM**: Month view can have 31 days x N shift types x M slots = many elements
- **Inline Styles**: Some style manipulation in click handlers
- **Page Reloads**: Many actions trigger `location.reload()` instead of DOM updates

---

### 5. Scope Switcher Performance

**Critical: Must be <100ms re-render**

#### Current Implementation
The scope switcher currently performs a **full page reload** when changing scope:
```javascript
// From ScopeSwitcher/Default.cshtml
window.location.href = url.toString();
```

#### How to Measure
1. Open DevTools Console
2. Run this before clicking a scope button:
```javascript
const startTime = performance.now();
window.addEventListener('beforeunload', () => {
  console.log('Navigation started at:', performance.now() - startTime, 'ms');
});
```

#### Optimization Opportunity
- Consider implementing AJAX-based scope switching for instant re-render
- Current full reload makes the <100ms target very difficult to meet

---

### 6. Large Lists Check

#### Pages with Potentially Large Lists
1. `/Admin/Users` - User management
2. `/Admin/AuditLog` - Audit logs
3. `/Friends/Index` - Friend lists
4. `/My/NotificationCenter` - Notifications

#### How to Check
1. Navigate to page with test data (100+ items)
2. Open DevTools Console
3. Run: `document.querySelectorAll('table tbody tr').length` or similar
4. If >100, check for virtualization:
   - Look for "virtual" in class names
   - Check if only visible items are in DOM
   - Look for scroll position-based rendering

#### Current Status (Reviewed)
- Pagination controls added (B-009) - helps limit DOM size
- No virtualization implemented - may need addition if lists grow large

---

### 7. Memory Leak Detection

#### How to Test
1. Open DevTools > Memory tab
2. Take initial heap snapshot
3. Navigate: Home > Calendar/Month > Calendar/Week > Admin/Users > Home
4. Repeat navigation 5 times
5. Force garbage collection (click trash can icon)
6. Take another heap snapshot
7. Compare snapshots - look for:
   - Detached DOM nodes
   - Event listeners that keep growing
   - Unreleased closures

#### Expected Behavior
- Heap size should return to baseline after GC
- No detached DOM trees
- Event listener count should stabilize

#### Files to Review for Leaks
- `wwwroot/js/calendar-radar.js` - Has interval, must be cleared on page leave
- `wwwroot/js/calendar-fill-handle.js` - MutationObserver must be disconnected
- Event listeners on `document` - Should use event delegation, not per-element

---

## Common Performance Anti-Patterns to Check

### 1. Layout Thrashing
**Bad pattern - reading then writing in a loop:**
```javascript
// BAD: Forces synchronous layout
elements.forEach(el => {
  const height = el.offsetHeight; // READ - triggers layout
  el.style.height = height + 10 + 'px'; // WRITE - invalidates layout
});
```

**Files to check:**
- `wwwroot/js/site.js` - Line 296-327 (tooltip positioning uses getBoundingClientRect in loop)

### 2. Missing Debounce on Scroll/Resize
**All scroll handlers should be debounced or use requestAnimationFrame**

**Current status:**
- `calendar-fill-handle.js` - Has debounce on MutationObserver - GOOD
- `myteam.js` - No scroll handlers - N/A
- `site.js` - Keydown handlers (not scroll) - N/A

### 3. Heavy DOM Manipulation
**Check for appendChild in loops without DocumentFragment**

**Current patterns:**
- `myteam.js` - Uses innerHTML bulk update - acceptable
- `roster-dock.js` - Uses innerHTML bulk update - acceptable

### 4. Non-Passive Event Listeners
**Scroll and touch handlers should use `{ passive: true }`**

**Current status:**
- `lazy-loader.js` - Uses `{ passive: true }` - GOOD

---

## Performance Optimization Recommendations

### High Priority

1. **Scope Switcher - AJAX Mode**
   - Convert from full page reload to AJAX content swap
   - Use `fetch()` to get filtered content, update DOM in place
   - Target: <100ms re-render

2. **Table View Virtualization**
   - For month view with many shift types, consider virtual scrolling
   - Only render visible rows + buffer

### Medium Priority

3. **Batch DOM Updates**
   - Group multiple classList operations
   - Use DocumentFragment for multiple appendChild

4. **CSS Containment**
   - Add `contain: content` to calendar cells
   - Prevents reflow from propagating

### Low Priority (Future Optimization)

5. **Web Workers for Heavy Calculations**
   - Move conflict detection logic to Web Worker
   - Off-main-thread processing

6. **Service Worker Caching**
   - Cache static assets aggressively
   - Consider app shell pattern

---

## Testing Tools Checklist

| Tool | Purpose | How to Access |
|------|---------|---------------|
| Chrome DevTools Performance | Frame timing, CPU usage | F12 > Performance |
| Chrome DevTools Memory | Heap snapshots, leak detection | F12 > Memory |
| Lighthouse | Overall performance audit | F12 > Lighthouse |
| WebPageTest | Real-world loading metrics | webpagetest.org |
| web.dev Measure | Core Web Vitals | web.dev/measure |

---

## Performance Monitoring Metrics (Production)

Consider adding these metrics to telemetry:

```javascript
// Example: Time to Interactive for calendar pages
window.addEventListener('load', () => {
  const timing = performance.getEntriesByType('navigation')[0];
  console.log('DOM Interactive:', timing.domInteractive);
  console.log('DOM Complete:', timing.domComplete);
  console.log('Load Complete:', timing.loadEventEnd);
});
```

---

## Summary of Current Performance Status

### Strengths
- LazyLoader utility for code splitting
- ReducedMotion utility for accessibility + performance
- Event delegation in main handlers
- Debounced MutationObserver usage
- Item density control in calendar views
- Pagination controls to limit list sizes

### Areas for Improvement
- Scope switcher uses full page reload
- No list virtualization for 100+ items
- Some inline style manipulation in click handlers
- Table view can have very large DOM in month mode
- Radar mode refresh interval (30s) could be optimized

### Risk Areas
- `/Calendar/Table?view=month` with many shift types
- `/Admin/AuditLog` with many log entries
- Memory leaks from interval not cleared on navigation

---

## Revision History

| Date | Version | Author | Changes |
|------|---------|--------|---------|
| 2026-02-01 | 1.0 | Claude (B-032) | Initial checklist creation |
