# Icon Generation Guide

The following icons need to be created from the SHIFTY brand.

## Required Files

| File | Size | Format | Notes |
|------|------|--------|-------|
| favicon.ico | 32x32 | ICO | Multi-resolution (16, 32, 48) |
| favicon.svg | 32x32 | SVG | Vector, scales to any size |
| icons/icon-192.png | 192x192 | PNG | Android/PWA |
| icons/icon-512.png | 512x512 | PNG | Android/PWA large |
| icons/apple-touch-icon.png | 180x180 | PNG | iOS home screen |

## Brand Colors

- **Primary:** #1E3A5F (Deep Navy)
- **Background:** #FFFFFF or transparent
- **Text/Icon:** #FFFFFF (White)

## Design Specification

- Use SHIFTY "S" lettermark
- Rounded corners (border-radius: ~20%)
- White "S" on navy background
- Font: System UI, bold weight (700)

## SVG Source Files

Source SVG files are provided in `/wwwroot/icons/` for each size:

- `icon-192.svg` - 192x192 source
- `icon-512.svg` - 512x512 source
- `apple-touch-icon.svg` - 180x180 source

## Converting SVG to PNG

### Option 1: Browser Dev Tools

1. Open the SVG file in a browser
2. Right-click and inspect the SVG element
3. Take a screenshot at the correct dimensions
4. Or use a browser extension like "SVG Export"

### Option 2: Inkscape (Free)

```bash
inkscape icon-192.svg --export-filename=icon-192.png --export-width=192 --export-height=192
inkscape icon-512.svg --export-filename=icon-512.png --export-width=512 --export-height=512
inkscape apple-touch-icon.svg --export-filename=apple-touch-icon.png --export-width=180 --export-height=180
```

### Option 3: ImageMagick

```bash
convert icon-192.svg -resize 192x192 icon-192.png
convert icon-512.svg -resize 512x512 icon-512.png
convert apple-touch-icon.svg -resize 180x180 apple-touch-icon.png
```

### Option 4: Online Tools

- [CloudConvert](https://cloudconvert.com/svg-to-png)
- [Convertio](https://convertio.co/svg-png/)
- [RealFaviconGenerator](https://realfavicongenerator.net/)

## Creating favicon.ico

The favicon.ico should contain multiple resolutions bundled together:

### Using ImageMagick:

```bash
convert favicon.svg -define icon:auto-resize=48,32,16 favicon.ico
```

### Using Online Tools:

1. Upload the favicon.svg to [favicon.io](https://favicon.io/favicon-converter/)
2. Download the generated favicon.ico

## Verification

After generating icons, verify:

1. **favicon.svg** displays correctly in browser tab (modern browsers)
2. **favicon.ico** displays correctly as fallback
3. **apple-touch-icon.png** appears on iOS when adding to home screen
4. **PWA icons** appear correctly when installing as app (Chrome: ... > Install)

## Manifest Integration

Icons are referenced in `/wwwroot/site.webmanifest`:

```json
{
  "name": "ShiftManager",
  "short_name": "Shifty",
  "theme_color": "#1E3A5F",
  "icons": [
    { "src": "/icons/icon-192.png", "sizes": "192x192", "type": "image/png" },
    { "src": "/icons/icon-512.png", "sizes": "512x512", "type": "image/png" }
  ]
}
```

## Task Reference

This documentation is part of **B-014: Favicon and App Icons** in the UI Overhaul project.
