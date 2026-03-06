# Print Styles Guide (B-006)

## Overview

ShiftManager includes print-optimized styles for calendars and reports. The print stylesheet ensures clean, professional output that is B&W-friendly and avoids awkward page breaks.

## Features

- **Clean output**: All navigation, buttons, and interactive elements are hidden
- **B&W friendly**: Colors convert to grayscale-friendly shades with distinct patterns
- **No page breaks**: Shifts and calendar cells avoid splitting across pages
- **Full width**: Content expands to use the full page width
- **Readable text**: All text converts to black on white for maximum contrast

## Usage

### Print Button

Add a print button to any page (it will hide itself when printing):

```html
<button class="btn btn--secondary no-print" onclick="window.print()">
    <icon name="download" size="sm" /> Print
</button>
```

### Hide Elements from Print

Use `no-print` class or `data-print-hide` attribute:

```html
<div class="no-print">This won't print</div>
<nav data-print-hide>Navigation</nav>
```

### Show Elements Only in Print

```html
<div class="print-only" style="display:none">Print-only content</div>
```

The `print-only` class is hidden on screen but visible when printing.

### Control Page Breaks

```html
<!-- Start on new page -->
<div class="page-break-before">Starts on new page</div>

<!-- Force page break after -->
<div class="page-break-after">Followed by page break</div>

<!-- Never split across pages -->
<div class="page-break-avoid">Won't split across pages</div>
<div class="avoid-break">Alternative class name</div>
```

### Print Header

Add a print header that shows company/context information:

```html
<div class="print-header print-only" style="display:none">
    <div class="print-header__logo">ShiftManager</div>
    <div class="print-header__meta">
        <div class="print-header__context">Company Name</div>
        <div class="print-header__date">Printed: January 31, 2026</div>
    </div>
</div>
```

## Calendar Printing

The calendar views (Month, Week, Day) are optimized for printing:

### Month View
- 7-column grid preserved
- Each day cell avoids page breaks
- Shift items won't split across pages
- Colors convert to B&W-friendly shades
- Interactive elements (quick-add, delete) hidden

### Week View
- 7-column grid maintained
- Column headers clearly visible
- Today highlighted with bold border
- Empty states hidden

### Day View
- Vertical list format
- Full item details shown
- Personal items have solid border
- Other items have dashed border for distinction

### Shift Type Indicators (B&W)

Since colors don't print reliably, shift types use border patterns:

| Shift Type | Border Style |
|------------|-------------|
| Morning    | 4px solid #666 (medium gray) |
| Afternoon  | 4px solid #999 (light gray) |
| Night      | 4px solid #333 (dark gray) |
| Hakam      | 4px solid #444 (dark) |
| BR         | 4px solid #777 (medium) |

## Testing Print Styles

1. Open any calendar page (Month, Week, or Day view)
2. Use browser Print Preview:
   - Windows: `Ctrl+P`
   - Mac: `Cmd+P`
3. Verify:
   - [ ] No navigation or buttons visible
   - [ ] Calendar is full width
   - [ ] Colors are readable in B&W
   - [ ] Shifts don't split across pages
   - [ ] Personal vs other items are distinguishable
   - [ ] Headers repeat on each page (browser-dependent)

## Page Setup

Default print settings:
- **Paper size**: A4
- **Margins**: 1.5cm (first page: 1cm top)
- **Orientation**: Portrait (use `.calendar-print-landscape` for landscape)

For landscape printing on large calendars:

```html
<div class="calendar-print-landscape">
    <!-- Calendar content -->
</div>
```

## Browser Compatibility

Print styles are tested with:
- Chrome/Edge (Chromium)
- Firefox
- Safari

Note: Some features like header repetition on each page depend on browser support.

## Customization

To add custom print styles, use the `@media print` query:

```css
@media print {
    .my-custom-element {
        /* Print-specific styles */
    }
}
```

## Files

- Main print stylesheet: `/wwwroot/css/print.css`
- Calendar styles: `/wwwroot/css/calendar.css` (has print overrides)

## Related Documentation

- [UI Overhaul Implementation](./UI-OVERHAUL-IMPLEMENTATION-COMPLETE.md)
- [Design System Tokens](../wwwroot/css/tokens.css)
