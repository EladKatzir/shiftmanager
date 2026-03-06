# Browser Compatibility Matrix

**Version:** 1.0
**Last Updated:** February 2026
**Task:** B-039

## Supported Browsers

| Browser | Minimum Version | Status | Notes |
|---------|-----------------|--------|-------|
| Chrome | 90+ | **Fully Supported** | Primary development target |
| Firefox | 90+ | **Fully Supported** | |
| Safari | 14+ | **Fully Supported** | macOS and iOS |
| Edge | 90+ | **Fully Supported** | Chromium-based |
| IE11 | - | **Not Supported** | No polyfills provided |

### Why These Versions?

- **Chrome/Edge 90+** (April 2021): Stable support for all CSS and JS features used
- **Firefox 90+** (July 2021): Full support for CSS `gap` in flexbox and all modern JS
- **Safari 14+** (September 2020): Native support for `aspect-ratio`, optional chaining, and nullish coalescing

---

## CSS Features Used

### Modern CSS Properties

| Feature | Chrome | Firefox | Safari | Edge | Notes |
|---------|--------|---------|--------|------|-------|
| CSS Custom Properties (Variables) | 49+ | 31+ | 9.1+ | 15+ | Extensively used throughout tokens.css |
| Flexbox | 29+ | 28+ | 9+ | 12+ | Primary layout method |
| CSS Grid | 57+ | 52+ | 10.1+ | 16+ | Used in calendar layouts |
| `gap` property (flexbox) | 84+ | 63+ | 14.1+ | 84+ | Used for spacing in flex containers |
| `aspect-ratio` | 88+ | 89+ | 15+ | 88+ | Used for image components |
| `backdrop-filter` | 76+ | 103+ | 9+ | 17+ | Used for modal overlays, blur effects |

### CSS Features with Vendor Prefixes

The codebase includes appropriate vendor prefixes for:

```css
/* backdrop-filter (Safari) */
-webkit-backdrop-filter: blur(2px);

/* background-clip (text gradient effects) */
-webkit-background-clip: text;
-webkit-text-fill-color: transparent;

/* print color adjustments */
-webkit-print-color-adjust: exact;

/* Smooth scrolling (iOS) */
-webkit-overflow-scrolling: touch;
```

### Media Queries Used

| Media Query | Purpose | Browser Support |
|-------------|---------|-----------------|
| `prefers-color-scheme` | Dark mode detection | Chrome 76+, Firefox 67+, Safari 12.1+ |
| `prefers-reduced-motion` | Accessibility animations | Chrome 74+, Firefox 63+, Safari 10.1+ |
| `hover: none` | Touch device detection | Chrome 38+, Firefox 64+, Safari 9+ |
| `pointer: coarse` | Touch input detection | Chrome 41+, Firefox 64+, Safari 9+ |
| `forced-colors: active` | Windows High Contrast | Chrome 89+, Firefox 89+, Edge 79+ |

### Responsive Breakpoints

```css
--breakpoint-sm: 640px;
--breakpoint-md: 768px;
--breakpoint-lg: 1024px;
--breakpoint-xl: 1280px;
--breakpoint-2xl: 1536px;
```

---

## JavaScript Features Used

### ES2020+ Features

| Feature | Chrome | Firefox | Safari | Edge | Status |
|---------|--------|---------|--------|------|--------|
| `async/await` | 55+ | 52+ | 10.1+ | 15+ | Used extensively |
| Optional chaining (`?.`) | 80+ | 74+ | 13.1+ | 80+ | Used in 15+ files |
| Nullish coalescing (`??`) | 80+ | 72+ | 13.1+ | 80+ | Used in multiple files |
| Arrow functions | 45+ | 22+ | 10+ | 12+ | Used throughout |
| Template literals | 41+ | 34+ | 9+ | 12+ | Used for string interpolation |
| Spread operator | 46+ | 16+ | 8+ | 12+ | Used in object/array operations |
| `const`/`let` | 49+ | 44+ | 10+ | 14+ | ES6 block scoping |
| Classes | 49+ | 45+ | 10+ | 13+ | Used for error classes (ApiError, etc.) |
| Destructuring | 49+ | 41+ | 8+ | 14+ | Parameter and assignment destructuring |
| `Promise.allSettled` | 76+ | 71+ | 13+ | 76+ | Used in api-client.js |

### Web APIs Used

| API | Chrome | Firefox | Safari | Edge | Purpose |
|-----|--------|---------|--------|------|---------|
| `fetch` | 42+ | 39+ | 10.1+ | 14+ | All HTTP requests |
| `localStorage` | 4+ | 3.5+ | 4+ | 8+ | Theme, widget state persistence |
| `sessionStorage` | 5+ | 2+ | 4+ | 8+ | Session data |
| `IntersectionObserver` | 51+ | 55+ | 12.1+ | 15+ | Lazy loading (with fallback) |
| `MutationObserver` | 26+ | 14+ | 6+ | 12+ | DOM change detection |
| `ResizeObserver` | 64+ | 69+ | 13.1+ | 79+ | Element resize detection |
| `AbortController` | 66+ | 57+ | 11.1+ | 16+ | Request cancellation |
| `CustomEvent` | 11+ | 6+ | 5.1+ | 12+ | Custom event dispatching |
| `matchMedia` | 9+ | 6+ | 5.1+ | 10+ | Media query detection |
| `requestIdleCallback` | 47+ | 55+ | N/A | 79+ | Idle loading (with fallback) |

### Fallback Patterns

The codebase includes graceful fallbacks for:

1. **IntersectionObserver** (lazy-loader.js):
   ```javascript
   if ('IntersectionObserver' in window) {
       // Use IntersectionObserver
   } else {
       // Fallback: load immediately
   }
   ```

2. **requestIdleCallback** (lazy-loader.js):
   ```javascript
   if ('requestIdleCallback' in window) {
       requestIdleCallback(load, options);
   } else {
       setTimeout(load, 200);
   }
   ```

3. **navigator.onLine** (offline-handler.js):
   - Browser online/offline events
   - Active health check fallback

---

## Known Limitations

### Safari-Specific

1. **`backdrop-filter`** may have performance implications on older Safari versions
2. **`requestIdleCallback`** not supported - uses setTimeout fallback
3. **Date input type** renders differently; app uses custom formatting

### Firefox-Specific

1. **`backdrop-filter`** requires Firefox 103+ for full support
2. Earlier Firefox versions will see solid backgrounds instead of blur effects

### Touch Device Considerations

- Touch targets are sized appropriately (44x44px minimum for interactive elements)
- `pointer: coarse` media query adjusts sizing for touch devices
- Swipe gestures not used; tap-based interaction only

---

## Testing Recommendations

### Automated Testing

1. **BrowserStack/Sauce Labs** for cross-browser testing
2. **Lighthouse CI** for performance monitoring across browsers
3. **axe-core** for accessibility testing

### Manual Testing Checklist

#### Core Functionality
- [ ] User authentication (login/logout)
- [ ] Calendar navigation (month/week/day views)
- [ ] Shift assignments and modifications
- [ ] Form submissions and validation
- [ ] Modal dialogs (open/close, focus trap)
- [ ] Toast notifications

#### Visual Features
- [ ] Dark mode toggle and persistence
- [ ] RTL language support (Hebrew)
- [ ] Responsive layouts at all breakpoints
- [ ] Print stylesheets

#### Progressive Enhancement
- [ ] Offline banner appears when disconnected
- [ ] Form data queues when offline
- [ ] Reconnection triggers sync

### Recommended Test Matrix

| Test Scenario | Chrome | Firefox | Safari | Edge |
|---------------|--------|---------|--------|------|
| Windows 10/11 | Yes | Yes | N/A | Yes |
| macOS | Yes | Yes | Yes | Yes |
| iOS (Safari) | N/A | N/A | Yes | N/A |
| Android (Chrome) | Yes | N/A | N/A | N/A |

### Performance Budgets

| Metric | Target | Notes |
|--------|--------|-------|
| Initial JS | < 200KB gzipped | Code splitting enabled |
| Initial CSS | < 50KB gzipped | Critical CSS inlined |
| LCP | < 2.5s | Largest Contentful Paint |
| FID | < 100ms | First Input Delay |
| CLS | < 0.1 | Cumulative Layout Shift |

---

## Polyfill Strategy

**Current approach: No polyfills**

The supported browser matrix (Chrome/Firefox/Edge 90+, Safari 14+) provides native support for all features used. No polyfills are included or required.

### If IE11 Support Were Needed (Not Currently Planned)

Would require polyfills for:
- CSS Custom Properties (css-vars-ponyfill)
- fetch API (whatwg-fetch)
- Promise (es6-promise)
- Optional chaining/nullish coalescing (Babel transforms)
- IntersectionObserver (@nicbell/intersection-observer)

**Note:** IE11 support is explicitly NOT provided and NOT planned.

---

## Feature Detection vs Browser Detection

The codebase uses **feature detection** rather than browser detection:

```javascript
// Good: Feature detection
if ('IntersectionObserver' in window) { ... }

// Avoided: Browser detection
if (navigator.userAgent.includes('Chrome')) { ... }
```

This approach ensures:
- Forward compatibility with new browsers
- Graceful degradation for missing features
- No false positives from user agent spoofing

---

## Accessibility Compliance

WCAG 2.1 AA compliance is maintained across all supported browsers:

- **Color contrast**: 4.5:1 minimum for text (verified in tokens.css)
- **Focus indicators**: Visible focus rings on all interactive elements
- **Reduced motion**: Respects `prefers-reduced-motion` preference
- **Screen readers**: Proper ARIA attributes and live regions
- **Keyboard navigation**: Full keyboard accessibility

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | Feb 2026 | Initial document (B-039) |
