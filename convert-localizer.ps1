# PowerShell script to convert @Localizer[...] to <loc key="..." /> in .cshtml files
# Run from ShiftManager root directory

param(
    [switch]$DryRun = $false
)

$filesProcessed = 0
$totalReplacements = 0

# Get all .cshtml files in Pages directory
$files = Get-ChildItem -Path "Pages" -Filter "*.cshtml" -Recurse | Where-Object { $_.Name -notlike "*_*" -or $_.Name -like "_TimelineItem.cshtml" }

Write-Host "Found $($files.Count) .cshtml files to process" -ForegroundColor Cyan
Write-Host "Dry run mode: $DryRun" -ForegroundColor Yellow
Write-Host ""

foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw
    $originalContent = $content
    $fileReplacements = 0

    # Pattern 1: Simple HTML content - @Localizer["Key"]
    # Match: >@Localizer["SomeKey"]< or >@Localizer["SomeKey"]</tag>
    # Replace with: ><loc key="SomeKey" /></ or ><loc key="SomeKey" /></tag>
    $pattern1 = '(>)\s*@Localizer\["([^"]+)"\]\s*(<)'
    if ($content -match $pattern1) {
        $content = $content -replace $pattern1, '$1<loc key="$2" />$3'
        $matches = [regex]::Matches($originalContent, $pattern1)
        $fileReplacements += $matches.Count
    }

    # Pattern 2: Start of element - <tag>@Localizer["Key"]
    $pattern2 = '(<[^>]+>)\s*@Localizer\["([^"]+)"\]'
    if ($content -match $pattern2) {
        $content = $content -replace $pattern2, '$1<loc key="$2" />'
        $matches = [regex]::Matches($originalContent, $pattern2)
        $fileReplacements += $matches.Count
    }

    # Pattern 3: End with closing tag - @Localizer["Key"]</tag>
    $pattern3 = '@Localizer\["([^"]+)"\]\s*(</[^>]+>)'
    if ($content -match $pattern3) {
        $content = $content -replace $pattern3, '<loc key="$1" />$2'
        $matches = [regex]::Matches($originalContent, $pattern3)
        $fileReplacements += $matches.Count
    }

    # Remove @using and @inject for Localizer if all @Localizer removed
    if ($content -notmatch '@Localizer\[' -and $content -match '@inject IStringLocalizer') {
        $content = $content -replace '@using Microsoft\.Extensions\.Localization\r?\n', ''
        $content = $content -replace '@using ShiftManager\.Resources\r?\n', ''
        $content = $content -replace '@inject IStringLocalizer<SharedResources> Localizer\r?\n', ''
    }

    if ($content -ne $originalContent) {
        $filesProcessed++
        $totalReplacements += $fileReplacements

        Write-Host "[MODIFIED] $($file.Name) - $fileReplacements replacements" -ForegroundColor Green

        if (-not $DryRun) {
            Set-Content -Path $file.FullName -Value $content -NoNewline
        }
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Conversion Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Files processed: $filesProcessed" -ForegroundColor Yellow
Write-Host "Total replacements: $totalReplacements" -ForegroundColor Yellow
Write-Host ""

if ($DryRun) {
    Write-Host "DRY RUN - No files were modified" -ForegroundColor Magenta
    Write-Host "Run without -DryRun to apply changes" -ForegroundColor Magenta
} else {
    Write-Host "Files have been updated!" -ForegroundColor Green
    Write-Host "Run 'dotnet build' to verify" -ForegroundColor Yellow
}
