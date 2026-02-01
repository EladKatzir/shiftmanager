# Image Optimization Audit Script (B-031)
# Checks for image optimization issues in ShiftManager
#
# Usage: .\scripts\audit-images.ps1
#        .\scripts\audit-images.ps1 -MaxSizeKB 50    # Custom size limit
#        .\scripts\audit-images.ps1 -FixMode         # Suggest fixes

param(
    [int]$MaxSizeKB = 100,
    [switch]$FixMode,
    [switch]$Verbose
)

$wwwroot = Join-Path $PSScriptRoot "..\wwwroot"
$pagesPath = Join-Path $PSScriptRoot "..\Pages"

Write-Host "=== Image Optimization Audit (B-031) ===" -ForegroundColor Cyan
Write-Host "Max allowed size: $MaxSizeKB KB" -ForegroundColor Gray
Write-Host ""

# Initialize counters
$totalImages = 0
$oversizedImages = @()
$pngImagesForSvg = @()
$missingWebP = @()

# ========================================
# 1. Check image file sizes
# ========================================
Write-Host "1. Scanning image files..." -ForegroundColor Yellow

# Justified large images (PWA manifest icons must be specific sizes)
$justifiedLargeImages = @(
    "android-chrome-512x512.png",   # Required for PWA manifest
    "icon-512.png",                  # Required for PWA manifest
    "apple-touch-icon.png"           # Required for iOS home screen
)

$images = Get-ChildItem -Path $wwwroot -Recurse -Include "*.png","*.jpg","*.jpeg","*.gif","*.webp" -ErrorAction SilentlyContinue

foreach ($img in $images) {
    $totalImages++
    $sizeKB = [math]::Round($img.Length / 1KB, 2)

    if ($img.Length -gt ($MaxSizeKB * 1KB)) {
        # Check if this is a justified large image
        $isJustified = $justifiedLargeImages -contains $img.Name

        if (-not $isJustified) {
            $oversizedImages += [PSCustomObject]@{
                File = $img.FullName.Replace($PSScriptRoot + "\..\", "")
                SizeKB = $sizeKB
                Limit = $MaxSizeKB
                OverBy = [math]::Round($sizeKB - $MaxSizeKB, 2)
            }
        } elseif ($Verbose) {
            Write-Host "  JUSTIFIED: $($img.Name) - $sizeKB KB (PWA requirement)" -ForegroundColor Gray
        }
    }

    if ($Verbose) {
        Write-Host "  $($img.Name): $sizeKB KB" -ForegroundColor Gray
    }
}

# ========================================
# 2. Check for PNG files that could be SVG
# ========================================
Write-Host "`n2. Checking for PNG icons that could be SVG..." -ForegroundColor Yellow

$pngFiles = Get-ChildItem -Path $wwwroot -Recurse -Include "*.png" -ErrorAction SilentlyContinue

foreach ($png in $pngFiles) {
    # Check if it's likely an icon (small, simple shapes)
    $name = $png.Name.ToLower()
    if ($name -match "icon|logo|badge|button" -and $png.Length -lt 50KB) {
        # Check if SVG version exists
        $svgPath = $png.FullName -replace "\.png$", ".svg"
        if (-not (Test-Path $svgPath)) {
            $pngImagesForSvg += [PSCustomObject]@{
                File = $png.FullName.Replace($PSScriptRoot + "\..\", "")
                SizeKB = [math]::Round($png.Length / 1KB, 2)
                Suggestion = "Consider creating SVG version"
            }
        }
    }
}

# ========================================
# 3. Check for missing WebP alternatives
# ========================================
Write-Host "`n3. Checking for missing WebP alternatives..." -ForegroundColor Yellow

$jpgPngFiles = Get-ChildItem -Path $wwwroot -Recurse -Include "*.png","*.jpg","*.jpeg" -ErrorAction SilentlyContinue

foreach ($img in $jpgPngFiles) {
    # Skip favicons and app icons
    $name = $img.Name.ToLower()
    if ($name -match "favicon|apple-touch|android-chrome") {
        continue
    }

    $webpPath = $img.FullName -replace "\.(png|jpg|jpeg)$", ".webp"
    if (-not (Test-Path $webpPath)) {
        $missingWebP += [PSCustomObject]@{
            File = $img.FullName.Replace($PSScriptRoot + "\..\", "")
            SizeKB = [math]::Round($img.Length / 1KB, 2)
        }
    }
}

# ========================================
# 4. Check Razor files for image issues
# ========================================
Write-Host "`n4. Scanning Razor files for image optimization issues..." -ForegroundColor Yellow

$razorFiles = Get-ChildItem -Path $pagesPath -Recurse -Include "*.cshtml" -ErrorAction SilentlyContinue
$missingDimensions = @()
$missingAlt = @()
$eagerAboveFold = @()

foreach ($razor in $razorFiles) {
    $content = Get-Content $razor.FullName -Raw -ErrorAction SilentlyContinue
    if (-not $content) { continue }

    # Find all img tags
    $imgMatches = [regex]::Matches($content, '<img[^>]*>', 'IgnoreCase')

    foreach ($match in $imgMatches) {
        $imgTag = $match.Value
        $lineNum = ($content.Substring(0, $match.Index) -split "`n").Count

        # Check for missing width/height
        if ($imgTag -notmatch 'width\s*=' -or $imgTag -notmatch 'height\s*=') {
            # Skip if it has style with dimensions
            if ($imgTag -notmatch 'style\s*=.*width.*height') {
                $srcMatch = [regex]::Match($imgTag, 'src\s*=\s*"([^"]*)"')
                $src = if ($srcMatch.Success) { $srcMatch.Groups[1].Value } else { "unknown" }

                $missingDimensions += [PSCustomObject]@{
                    File = $razor.FullName.Replace($PSScriptRoot + "\..\", "")
                    Line = $lineNum
                    Src = $src
                    Issue = "Missing width and/or height attributes"
                }
            }
        }

        # Check for missing alt text
        if ($imgTag -notmatch 'alt\s*=') {
            $srcMatch = [regex]::Match($imgTag, 'src\s*=\s*"([^"]*)"')
            $src = if ($srcMatch.Success) { $srcMatch.Groups[1].Value } else { "unknown" }

            $missingAlt += [PSCustomObject]@{
                File = $razor.FullName.Replace($PSScriptRoot + "\..\", "")
                Line = $lineNum
                Src = $src
                Issue = "Missing alt attribute (accessibility)"
            }
        }
    }
}

# ========================================
# Output Results
# ========================================
Write-Host "`n" -NoNewline
Write-Host "=== AUDIT RESULTS ===" -ForegroundColor Cyan
Write-Host ""

# Summary
Write-Host "Summary:" -ForegroundColor White
Write-Host "  Total images scanned: $totalImages"
Write-Host "  Oversized images (>$MaxSizeKB KB): $($oversizedImages.Count)"
Write-Host "  PNGs that could be SVG: $($pngImagesForSvg.Count)"
Write-Host "  Missing WebP alternatives: $($missingWebP.Count)"
Write-Host "  Missing dimensions in HTML: $($missingDimensions.Count)"
Write-Host "  Missing alt text: $($missingAlt.Count)"
Write-Host ""

# Detailed issues
if ($oversizedImages.Count -gt 0) {
    Write-Host "OVERSIZED IMAGES:" -ForegroundColor Red
    $oversizedImages | Format-Table -AutoSize

    if ($FixMode) {
        Write-Host "  FIX: Use image compression tools like TinyPNG, ImageOptim, or Squoosh" -ForegroundColor Green
        Write-Host "  FIX: Consider using WebP format for better compression" -ForegroundColor Green
    }
}

if ($pngImagesForSvg.Count -gt 0) {
    Write-Host "PNG FILES THAT COULD BE SVG:" -ForegroundColor Yellow
    $pngImagesForSvg | Format-Table -AutoSize

    if ($FixMode) {
        Write-Host "  FIX: Convert simple icons/logos to SVG for better scaling and smaller size" -ForegroundColor Green
    }
}

if ($missingWebP.Count -gt 0) {
    Write-Host "MISSING WEBP ALTERNATIVES:" -ForegroundColor Yellow
    $missingWebP | Format-Table -AutoSize

    if ($FixMode) {
        Write-Host "  FIX: Generate WebP versions using: cwebp image.png -o image.webp" -ForegroundColor Green
    }
}

if ($missingDimensions.Count -gt 0) {
    Write-Host "IMAGES MISSING WIDTH/HEIGHT (CLS RISK):" -ForegroundColor Red
    $missingDimensions | Format-Table -AutoSize

    if ($FixMode) {
        Write-Host "  FIX: Add explicit width and height attributes to prevent layout shift" -ForegroundColor Green
        Write-Host "  FIX: Use the optimize attribute: <img optimize src=... width=... height=...>" -ForegroundColor Green
    }
}

if ($missingAlt.Count -gt 0) {
    Write-Host "IMAGES MISSING ALT TEXT (ACCESSIBILITY):" -ForegroundColor Yellow
    $missingAlt | Format-Table -AutoSize

    if ($FixMode) {
        Write-Host "  FIX: Add descriptive alt text for screen readers" -ForegroundColor Green
        Write-Host "  FIX: Use alt='' for decorative images" -ForegroundColor Green
    }
}

# Final status
Write-Host ""
$hasIssues = $oversizedImages.Count -gt 0 -or $missingDimensions.Count -gt 0 -or $missingAlt.Count -gt 0

if ($hasIssues) {
    Write-Host "STATUS: Issues found - review and fix before production" -ForegroundColor Red
    exit 1
} else {
    Write-Host "STATUS: All images optimized!" -ForegroundColor Green
    exit 0
}
