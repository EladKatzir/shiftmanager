# Code Splitting Strategy (B-030)

## Overview

ShiftManager implements code splitting and lazy loading to reduce the initial JavaScript payload and improve page load performance. The target is to keep the initial JS payload under 200KB gzipped.

## Core Utilities

### 1. LazyLoader (`/js/lazy-loader.js`)

A utility for dynamically loading scripts on demand.

**Features:**
- Script deduplication (won't load the same script twice)
- Promise-based API for chaining
- Support for IntersectionObserver-based loading
- Interaction-triggered loading
- Idle-time loading
- Script preloading and prefetching

**Basic Usage:**

```javascript
// Load a single script
await LazyLoader.load('/js/charts.js');

// Load multiple scripts in order
await LazyLoader.loadAll(['/js/lib.js', '/js/app.js']);

// Load multiple scripts in parallel
await LazyLoader.loadParallel(['/js/chart1.js', '/js/chart2.js']);
```

### 2. ModalLoader (`/js/modal-loader.js`)

Loads modal content on demand via AJAX.

**Features:**
- Content caching for subsequent opens
- Loading state management
- Integration with ModalFocus for accessibility
- CSRF token support
- Custom events for modal lifecycle

**Usage:**

```html
<button data-modal-lazy="editModal" data-modal-url="/modals/edit">Edit</button>
```

## Loading Strategies

### 1. Page-Specific Scripts

Scripts are loaded only on pages that need them using Razor sections:

```razor
@section Scripts {
    <script src="~/js/calendar-specific.js" asp-append-version="true"></script>
}
```

The `_Layout.cshtml` renders this section at the end of the body:
```razor
@await RenderSectionAsync("Scripts", required: false)
```

### 2. Lazy Loading on Visibility

Scripts load when their target element becomes visible (great for below-the-fold content):

```javascript
// Load charts.js when .chart-container becomes visible
LazyLoader.loadOnVisible('.chart-container', '/js/charts.js', function() {
    // Initialize chart after script loads
    Chart.init();
});
```

### 3. Lazy Loading on Interaction

Scripts load on first user interaction (great for optional features):

```javascript
// Load rich editor on first focus of textarea
LazyLoader.loadOnInteraction('[data-editor]', 'focus', '/js/editor.js', function(element) {
    // Transform textarea into rich editor
    initEditor(element);
});

// Load on multiple events
LazyLoader.loadOnInteraction('.dropdown', ['mouseenter', 'focus'], '/js/dropdown.js');
```

### 4. Idle-Time Loading

Scripts load during browser idle time (low priority, non-critical):

```javascript
// Load analytics after page becomes idle
LazyLoader.loadWhenIdle('/js/analytics.js', { timeout: 3000 });
```

### 5. Modal Content Lazy Loading

Modal content is fetched on demand:

```html
<!-- Trigger button -->
<button data-modal-lazy="editModal"
        data-modal-url="/Admin/Modals/EditUser?id=123"
        data-loading-text="Loading...">
    Edit User
</button>

<!-- Modal will be created dynamically -->
```

### 6. Preloading and Prefetching

For anticipated navigation:

```javascript
// Preload script (high priority, same page)
LazyLoader.preload('/js/heavy-feature.js');

// Prefetch script (low priority, future navigation)
LazyLoader.prefetch('/js/next-page-feature.js');
```

## Page Script Mapping

| Page Pattern | Scripts Loaded |
|--------------|----------------|
| All Pages | site.js, error-states.js, telemetry.js, localization-*.js |
| Calendar/* | calendar-inline-edit.js |
| Calendar/Table | roster-dock.js, calendar-radar.js, calendar-fill-handle.js |
| Admin/* | (admin-specific scripts as needed) |
| Owner/* | (owner-specific scripts as needed) |
| Authenticated | session-check.js |
| Hebrew UI | hebrew-audit.js |
| Edit Mode | language-edit-mode.js |

## Implementation Guidelines

### When to Use Lazy Loading

**Good candidates for lazy loading:**
- Features behind user interaction (modals, dropdowns, editors)
- Below-the-fold content (charts, maps, heavy visualizations)
- Admin-only features on pages accessible by regular users
- Optional enhancements (keyboard shortcuts, animations)

**Not good candidates:**
- Critical above-the-fold functionality
- Security-related code
- Core navigation
- Small scripts (<5KB)

### Best Practices

1. **Group related scripts** - Load dependent scripts together:
   ```javascript
   await LazyLoader.loadAll(['/js/chart-core.js', '/js/chart-plugins.js']);
   ```

2. **Show loading states** - Provide feedback during loading:
   ```javascript
   button.classList.add('is-loading');
   await LazyLoader.load('/js/feature.js');
   button.classList.remove('is-loading');
   ```

3. **Handle errors gracefully**:
   ```javascript
   try {
       await LazyLoader.load('/js/feature.js');
       initFeature();
   } catch (error) {
       ErrorStates.showError('Failed to load feature');
   }
   ```

4. **Preload anticipated needs**:
   ```javascript
   // On calendar page, preload the edit modal script
   LazyLoader.preload('/js/calendar-edit-modal.js');
   ```

5. **Use version query strings** for cache busting:
   ```javascript
   LazyLoader.load('/js/feature.js?v=' + window.appVersion);
   ```

## CSS for Loading States

Add to your stylesheet:

```css
/* Loading spinner for lazy modal triggers */
.modal-loading-spinner {
    display: inline-block;
    width: 1em;
    height: 1em;
    border: 2px solid currentColor;
    border-right-color: transparent;
    border-radius: 50%;
    animation: spinner 0.75s linear infinite;
}

@keyframes spinner {
    to { transform: rotate(360deg); }
}

/* Loading state for buttons */
.is-loading {
    opacity: 0.7;
    pointer-events: none;
    cursor: wait;
}

.is-loading::after {
    content: '';
    display: inline-block;
    width: 1em;
    height: 1em;
    margin-left: 0.5em;
    border: 2px solid currentColor;
    border-right-color: transparent;
    border-radius: 50%;
    animation: spinner 0.75s linear infinite;
    vertical-align: middle;
}
```

## Measuring Performance

### Using Browser DevTools

1. Open DevTools > Network tab
2. Filter by JS
3. Check "Disable cache" for accurate measurements
4. Note the initial payload size vs. lazy-loaded scripts

### Using Telemetry

```javascript
// The telemetry system tracks script load times automatically
// Check /Admin/Analytics for performance metrics
```

### Performance Budget

| Metric | Target |
|--------|--------|
| Initial JS (gzipped) | < 200KB |
| First Contentful Paint | < 1.5s |
| Time to Interactive | < 3s |
| Lazy script load time | < 500ms |

## Troubleshooting

### Script not loading

1. Check browser console for errors
2. Verify the script URL is correct
3. Check if script is already loaded: `LazyLoader.isLoaded('/js/feature.js')`
4. Check network tab for 404 or CORS errors

### Script loads but feature doesn't work

1. Ensure dependent scripts are loaded first
2. Check if script relies on DOMContentLoaded (already fired)
3. Add initialization after load:
   ```javascript
   await LazyLoader.load('/js/feature.js');
   window.Feature.init(); // Explicit initialization
   ```

### Modal content not loading

1. Check the modal URL returns valid HTML
2. Verify CSRF token is being sent (check Network tab)
3. Check `ModalLoader.isCached('modalId')` for caching issues
4. Clear cache: `ModalLoader.clearCache('modalId')`

## Related Documentation

- [Performance Monitoring](./TELEMETRY.md)
- [Modal Focus Management](./ACCESSIBILITY.md)
- [Error States](./ERROR-HANDLING.md)
