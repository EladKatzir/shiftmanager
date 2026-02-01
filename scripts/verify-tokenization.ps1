#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Verifies that a file or directory has been properly tokenized (no hardcoded colors).
.DESCRIPTION
    Checks for hardcoded hex colors, rgba() values, and inline styles.
    Part of UI Overhaul task A-009-EXT.
.PARAMETER Path
    Path to file or directory to check
.PARAMETER Recursive
    Check subdirectories recursively
.EXAMPLE
    ./verify-tokenization.ps1 -Path Pages/Calendar/Month.cshtml
    ./verify-tokenization.ps1 -Path Pages/ -Recursive
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$Path,
    [switch]$Recursive
)

$ErrorActionPreference = "Stop"

# Patterns to check
$hexPattern = '#[0-9A-Fa-f]{3,8}\b'
$rgbaPattern = 'rgba?\s*\('
$inlineStylePattern = 'style\s*=\s*"'

# Exclusions (files that are allowed to have colors)
$excludedFiles = @(
    'tokens.css',
    'site.css.bak',
    '*.min.css'
)

function Test-Tokenization {
    param([string]$FilePath)

    $fileName = Split-Path $FilePath -Leaf

    # Skip excluded files
    foreach ($exclude in $excludedFiles) {
        if ($fileName -like $exclude) {
            return @{ Pass = $true; Skipped = $true }
        }
    }

    $content = Get-Content $FilePath -Raw -ErrorAction SilentlyContinue
    if (-not $content) {
        return @{ Pass = $true; Empty = $true }
    }

    $hexMatches = [regex]::Matches($content, $hexPattern)
    $rgbaMatches = [regex]::Matches($content, $rgbaPattern)
    $inlineMatches = [regex]::Matches($content, $inlineStylePattern)

    # Filter out CSS custom property definitions (these are OK)
    $hexMatches = $hexMatches | Where-Object {
        $line = ($content.Substring(0, $_.Index) -split "`n")[-1]
        $line -notmatch '--[a-z-]+\s*:\s*$' -and $line -notmatch 'var\s*\('
    }

    return @{
        Pass = ($hexMatches.Count -eq 0 -and $rgbaMatches.Count -eq 0)
        HexColors = $hexMatches.Count
        RgbaColors = $rgbaMatches.Count
        InlineStyles = $inlineMatches.Count
        HexExamples = ($hexMatches | Select-Object -First 5 | ForEach-Object { $_.Value })
    }
}

# Get files to check
if (Test-Path $Path -PathType Leaf) {
    $files = @(Get-Item $Path)
} else {
    $searchParams = @{
        Path = $Path
        Include = "*.cshtml", "*.css"
    }
    if ($Recursive) {
        $searchParams.Recurse = $true
    }
    $files = Get-ChildItem @searchParams
}

Write-Host "=== Tokenization Verification ===" -ForegroundColor Cyan
Write-Host "Checking $($files.Count) files..." -ForegroundColor Gray

$totalHex = 0
$totalRgba = 0
$failedFiles = @()

foreach ($file in $files) {
    $result = Test-Tokenization -FilePath $file.FullName

    if ($result.Skipped) {
        Write-Host "SKIP: $($file.Name)" -ForegroundColor DarkGray
        continue
    }

    if ($result.Pass) {
        Write-Host "PASS: $($file.Name)" -ForegroundColor Green
    } else {
        Write-Host "FAIL: $($file.Name) - $($result.HexColors) hex, $($result.RgbaColors) rgba" -ForegroundColor Red
        if ($result.HexExamples) {
            Write-Host "      Examples: $($result.HexExamples -join ', ')" -ForegroundColor Yellow
        }
        $failedFiles += $file.Name
        $totalHex += $result.HexColors
        $totalRgba += $result.RgbaColors
    }
}

Write-Host ""
Write-Host "=== Summary ===" -ForegroundColor Cyan
if ($failedFiles.Count -eq 0) {
    Write-Host "All files passed!" -ForegroundColor Green
    exit 0
} else {
    Write-Host "Failed files: $($failedFiles.Count)" -ForegroundColor Red
    Write-Host "Total hardcoded colors: $totalHex hex, $totalRgba rgba" -ForegroundColor Red
    exit 1
}
