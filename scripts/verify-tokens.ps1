# Token lint script for tokens.css (ShiftManager).
# Fails if any calendar-palette token in :root has no dark-mode counterpart.
# Tokens checked: --shift-*, --chore*, --onduty*, --vacation*
# Usage: powershell -File scripts/verify-tokens.ps1

param(
    [string]$TokensFile = "$PSScriptRoot/../wwwroot/css/tokens.css"
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $TokensFile)) {
    Write-Host "tokens.css not found at $TokensFile" -ForegroundColor Red
    exit 2
}

$content = Get-Content -Raw -Path $TokensFile

function Extract-Block {
    param([string]$Pattern, [string]$Label)
    $m = [regex]::Match($content, $Pattern, [System.Text.RegularExpressions.RegexOptions]::Singleline)
    if (-not $m.Success) {
        Write-Host ("Could not locate block: {0}" -f $Label) -ForegroundColor Red
        exit 2
    }
    return $m.Groups[1].Value
}

$lightBlock     = Extract-Block ':root\s*\{(.+?)\n\}' 'light-mode root'
$darkThemeBlock = Extract-Block ':root\[data-theme=[^\]]*dark[^\]]*\]\s*\{(.+?)\n\}' 'data-theme dark'
$mediaDarkBlock = Extract-Block '@media\s*\(prefers-color-scheme:\s*dark\).+?\{(.+?)\n\s*\}\s*\n\s*\}' 'prefers-color-scheme dark'

$tokenPattern = '--(?:shift|chore|onduty|vacation)[A-Za-z0-9-]*'

function Get-Tokens {
    param([string]$block)
    $set = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($m in [regex]::Matches($block, $tokenPattern)) {
        [void]$set.Add($m.Value)
    }
    return ,$set
}

$lightTokens     = Get-Tokens $lightBlock
$darkTokens      = Get-Tokens $darkThemeBlock
$mediaDarkTokens = Get-Tokens $mediaDarkBlock

Write-Host ("Light-mode tokens:                 {0}" -f $lightTokens.Count)
Write-Host ("data-theme dark tokens:            {0}" -f $darkTokens.Count)
Write-Host ("prefers-color-scheme dark tokens:  {0}" -f $mediaDarkTokens.Count)

$missingInDataTheme = [System.Collections.Generic.List[string]]::new()
$missingInMedia     = [System.Collections.Generic.List[string]]::new()

foreach ($t in $lightTokens) {
    if (-not $darkTokens.Contains($t))      { $missingInDataTheme.Add($t) }
    if (-not $mediaDarkTokens.Contains($t)) { $missingInMedia.Add($t) }
}

$hasDrift = $false

if ($missingInDataTheme.Count -gt 0) {
    Write-Host ""
    Write-Host "DRIFT in data-theme dark block:" -ForegroundColor Red
    foreach ($t in ($missingInDataTheme | Sort-Object)) {
        Write-Host ("  - {0}" -f $t) -ForegroundColor Red
    }
    $hasDrift = $true
}

if ($missingInMedia.Count -gt 0) {
    Write-Host ""
    Write-Host "DRIFT in prefers-color-scheme dark block:" -ForegroundColor Red
    foreach ($t in ($missingInMedia | Sort-Object)) {
        Write-Host ("  - {0}" -f $t) -ForegroundColor Red
    }
    $hasDrift = $true
}

if ($hasDrift) {
    Write-Host ""
    Write-Host "Token drift detected." -ForegroundColor Red
    Write-Host "Every calendar-palette token in :root must also appear in both dark blocks." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "OK - calendar-palette tokens are consistent across all three blocks." -ForegroundColor Green
exit 0
