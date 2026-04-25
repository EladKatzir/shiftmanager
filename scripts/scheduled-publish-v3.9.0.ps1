#requires -Version 5.1
# One-shot wrapper invoked by Windows Task Scheduler at 19:25 on 2026-04-25.
# Mirrors the user's manual publish sequence:
#   1+2. git add . ; git commit -m "Pre-publish WIP for v3.9.0"
#   3.   .\scripts\Update-FinalProductPublish.ps1 -Version "3.9.0" -CommitChanges -SkipTests
#   4+5. git add . ; git commit -m "updated the finalproductpublish"
#   6.   git push

$ErrorActionPreference = 'Continue'

$ProjectRoot = 'C:\Users\katzi\Downloads\ShiftManager'
$LogDir      = Join-Path $ProjectRoot 'logs'
$LogPath     = Join-Path $LogDir 'scheduled-publish-v3.9.0.log'

New-Item -ItemType Directory -Path $LogDir -Force | Out-Null
Set-Location $ProjectRoot
Start-Transcript -Path $LogPath -Force | Out-Null

Write-Host "=== Scheduled v3.9.0 publish started: $(Get-Date) ==="
Write-Host "Working directory: $(Get-Location)"
Write-Host "Branch: $(git rev-parse --abbrev-ref HEAD)"

Write-Host "`n--- Step 1+2: pre-publish commit ---"
git add .
git commit -m "Pre-publish WIP for v3.9.0"
Write-Host "(pre-commit exit code: $LASTEXITCODE  -- non-zero just means nothing to commit)"

Write-Host "`n--- Step 3: Update-FinalProductPublish v3.9.0 ---"
& .\scripts\Update-FinalProductPublish.ps1 -Version "3.9.0" -CommitChanges -SkipTests
$publishExit = $LASTEXITCODE
Write-Host "(publish exit code: $publishExit)"

if ($publishExit -ne 0) {
    Write-Host "ERROR: publish script failed (exit $publishExit). Aborting before push." -ForegroundColor Red
    Stop-Transcript | Out-Null
    exit $publishExit
}

Write-Host "`n--- Step 4+5: post-publish commit ---"
git add .
git commit -m "updated the finalproductpublish"
Write-Host "(post-commit exit code: $LASTEXITCODE  -- non-zero just means nothing to commit)"

Write-Host "`n--- Step 6: push ---"
git push
$pushExit = $LASTEXITCODE
Write-Host "(push exit code: $pushExit)"

Write-Host "`n=== Done: $(Get-Date) ==="
Stop-Transcript | Out-Null
exit $pushExit
