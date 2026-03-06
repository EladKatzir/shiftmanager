# Browser Support Matrix

## Minimum Supported Browsers

| Browser | Minimum Version | Notes |
|---------|----------------|-------|
| Microsoft Edge | 88+ | Chromium-based Edge. Legacy Edge (EdgeHTML) is NOT supported. |
| Google Chrome | 88+ | If available in the environment. |
| Mozilla Firefox | 78 ESR+ | Firefox ESR is common in managed environments. |
| Safari | Not supported | Not expected in target deployment environment. |

## Required Browser Features

ShiftManager relies on the following web platform features. All are available in the minimum browser versions listed above:

- **ES6+ JavaScript**: `let`/`const`, arrow functions, `async`/`await`, `Promise`, template literals, destructuring, `fetch()`, `URLSearchParams`
- **CSS Custom Properties (variables)**: `var(--property)` for theming
- **CSS Grid / Flexbox**: Layout system
- **SignalR (WebSocket)**: Real-time calendar updates (falls back to SSE/long-polling)
- **`<input type="date">`**: Native date picker in calendar navigation
- **`Intl.DateTimeFormat`**: Date/time formatting
- **`FormData` API**: Form submissions
- **`ResizeObserver`**: Responsive layout adjustments

## Known Compatibility Issues

### Internet Explorer 11
**NOT SUPPORTED.** IE11 lacks ES6+ support, CSS variables, and modern APIs. ShiftManager will not function in IE11.

### Older Edge (EdgeHTML, versions < 79)
**NOT SUPPORTED.** Legacy Edge lacks full ES6+ module support and modern CSS features.

### Network-Restricted Environments
- If the network strips JavaScript: a `<noscript>` banner is displayed informing the user that JavaScript is required.
- If WebSocket connections are blocked by proxy/firewall: SignalR will automatically fall back to Server-Sent Events, then Long Polling. Real-time updates will still function but with higher latency.

## Testing Checklist

Before deployment, verify the following on actual target workstation browsers:

- [ ] Login page loads and authenticates successfully
- [ ] Calendar renders with correct Hebrew text
- [ ] Calendar inline editing (click-to-assign) works
- [ ] Real-time updates via SignalR connect (check connection indicator)
- [ ] PDF export generates readable Hebrew content
- [ ] Print view renders correctly (Ctrl+P)
- [ ] Date picker navigation works
- [ ] All dropdown selectors function
- [ ] Form submissions (vacation requests, swap requests) complete
- [ ] Notification center displays and links work

## Deployment Note

Military managed environments may use specific browser versions locked by IT policy. Before deployment:

1. Identify the exact browser and version installed on target workstations
2. Run the testing checklist above on that specific browser
3. If the browser is below minimum supported versions, coordinate with IT for browser update
4. Document the verified browser version in the deployment record
