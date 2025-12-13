<#
.SYNOPSIS
    Restore FinalProductPublish from a backup

.DESCRIPTION
    Restores FinalProductPublish from a timestamped backup in Backups/FinalProductPublish/.
    If no timestamp is provided, lists available backups.

.PARAMETER BackupTimestamp
    Timestamp of backup to restore (e.g., "20251213_120024")

.PARAMETER Force
    Skip confirmation prompt

.EXAMPLE
    .\Restore-FinalProductPublish.ps1
    Lists available backups

.EXAMPLE
    .\Restore-FinalProductPublish.ps1 -BackupTimestamp "20251213_120024"
    Restores from specific backup

.EXAMPLE
    .\Restore-FinalProductPublish.ps1 -BackupTimestamp "20251213_120024" -Force
    Restores without confirmation

.NOTES
    Author: Claude Code
    Version: 1.0
#>

[CmdletBinding()]
param(
    [Parameter()]
    [string]$BackupTimestamp,

    [Parameter()]
    [switch]$Force
)

$ErrorActionPreference = "Stop"

# Paths
$ScriptRoot = Split-Path -Parent $PSScriptRoot
$DestPath = Join-Path $ScriptRoot "FinalProductPublish"
$BackupRoot = Join-Path $ScriptRoot "Backups\FinalProductPublish"

# Colors
$ColorGreen = "Green"
$ColorYellow = "Yellow"
$ColorRed = "Red"
$ColorCyan = "Cyan"

function Write-Success { param([string]$Message) Write-Host "  ✓ " -ForegroundColor $ColorGreen -NoNewline; Write-Host $Message }
function Write-Info { param([string]$Message) Write-Host "  ⏳ " -ForegroundColor $ColorCyan -NoNewline; Write-Host $Message }
function Write-WarningMsg { param([string]$Message) Write-Host "  ⚠️  " -ForegroundColor $ColorYellow -NoNewline; Write-Host $Message }
function Write-ErrorMsg { param([string]$Message) Write-Host "  ❌ " -ForegroundColor $ColorRed -NoNewline; Write-Host $Message }

try {
    Write-Host ""
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor $ColorCyan
    Write-Host "  FinalProductPublish Restore Tool" -ForegroundColor White
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor $ColorCyan
    Write-Host ""

    # Check if backup root exists
    if (-not (Test-Path $BackupRoot)) {
        Write-ErrorMsg "Backup directory not found: $BackupRoot"
        throw "No backups available"
    }

    # Get all available backups
    $allBackups = Get-ChildItem -Path $BackupRoot -Directory -Filter "FinalProductPublish_BACKUP_*" |
        Sort-Object Name -Descending

    if ($allBackups.Count -eq 0) {
        Write-ErrorMsg "No backups found in: $BackupRoot"
        throw "No backups available"
    }

    # If no timestamp provided, list backups and exit
    if (-not $BackupTimestamp) {
        Write-Host "Available FinalProductPublish backups:" -ForegroundColor Cyan
        Write-Host ""

        foreach ($backup in $allBackups) {
            $timestamp = $backup.Name -replace "FinalProductPublish_BACKUP_", ""
            $backupDate = [DateTime]::ParseExact($timestamp, "yyyyMMdd_HHmmss", $null)

            $files = Get-ChildItem -Path $backup.FullName -File -Recurse
            $fileCount = $files.Count
            $size = [math]::Round(($files | Measure-Object -Property Length -Sum).Sum / 1MB, 2)

            Write-Host "  $timestamp" -ForegroundColor White -NoNewline
            Write-Host "  ($($backupDate.ToString('MMM d, yyyy HH:mm:ss')))" -ForegroundColor Gray
            Write-Host "    Files: $fileCount, Size: $size MB" -ForegroundColor DarkGray
            Write-Host ""
        }

        Write-Host "Usage:" -ForegroundColor Cyan
        Write-Host "  .\Restore-FinalProductPublish.ps1 -BackupTimestamp `"YYYYMMDD_HHMMSS`"" -ForegroundColor Gray
        Write-Host ""
        Write-Host "Example:" -ForegroundColor Cyan
        Write-Host "  .\Restore-FinalProductPublish.ps1 -BackupTimestamp `"$($allBackups[0].Name -replace 'FinalProductPublish_BACKUP_', '')`"" -ForegroundColor Gray
        Write-Host ""
        exit 0
    }

    # Validate timestamp format
    if ($BackupTimestamp -notmatch '^\d{8}_\d{6}$') {
        Write-ErrorMsg "Invalid timestamp format. Expected: YYYYMMDD_HHMMSS (e.g., 20251213_120024)"
        throw "Invalid timestamp format"
    }

    # Find backup
    $backupName = "FinalProductPublish_BACKUP_$BackupTimestamp"
    $backupPath = Join-Path $BackupRoot $backupName

    if (-not (Test-Path $backupPath)) {
        Write-ErrorMsg "Backup not found: $backupName"
        Write-Host ""
        Write-Host "Available backups:" -ForegroundColor Yellow
        foreach ($backup in $allBackups) {
            $timestamp = $backup.Name -replace "FinalProductPublish_BACKUP_", ""
            Write-Host "  - $timestamp" -ForegroundColor Gray
        }
        Write-Host ""
        throw "Backup not found"
    }

    # Get backup info
    $backupFiles = Get-ChildItem -Path $backupPath -File -Recurse
    $backupFileCount = $backupFiles.Count
    $backupSize = [math]::Round(($backupFiles | Measure-Object -Property Length -Sum).Sum / 1MB, 2)

    $backupDate = [DateTime]::ParseExact($BackupTimestamp, "yyyyMMdd_HHmmss", $null)

    # Show confirmation
    Write-Host "Restore Details:" -ForegroundColor Cyan
    Write-Host "  Source:   $backupPath" -ForegroundColor Gray
    Write-Host "  Created:  $($backupDate.ToString('yyyy-MM-dd HH:mm:ss'))" -ForegroundColor Gray
    Write-Host "  Files:    $backupFileCount" -ForegroundColor Gray
    Write-Host "  Size:     $backupSize MB" -ForegroundColor Gray
    Write-Host "  Target:   $DestPath" -ForegroundColor Gray
    Write-Host ""

    if (-not $Force) {
        Write-WarningMsg "This will DELETE the current FinalProductPublish contents!"
        Write-Host ""
        $confirm = Read-Host "Are you sure you want to continue? (y/N)"
        if ($confirm -ne 'y' -and $confirm -ne 'Y') {
            Write-Host "Restore cancelled by user" -ForegroundColor Yellow
            exit 0
        }
        Write-Host ""
    }

    # Perform restore
    Write-Info "Deleting current FinalProductPublish..."
    if (Test-Path $DestPath) {
        Remove-Item -Path $DestPath -Recurse -Force
    }
    Write-Success "Current contents deleted"

    Write-Info "Copying backup to FinalProductPublish (this may take 1-2 minutes)..."
    Copy-Item -Path $backupPath -Destination $DestPath -Recurse -Force
    Write-Success "Backup copied"

    # Verify restoration
    Write-Info "Verifying restoration..."
    $restoredFiles = Get-ChildItem -Path $DestPath -File -Recurse
    $restoredCount = $restoredFiles.Count
    $restoredSize = [math]::Round(($restoredFiles | Measure-Object -Property Length -Sum).Sum / 1MB, 2)

    if ($restoredCount -ne $backupFileCount) {
        Write-WarningMsg "File count mismatch! Backup: $backupFileCount, Restored: $restoredCount"
        Write-Host "    This may indicate an incomplete restore" -ForegroundColor Yellow
    } else {
        Write-Success "Verification passed: $restoredCount files, $restoredSize MB"
    }

    # Success
    Write-Host ""
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor $ColorGreen
    Write-Host "  RESTORATION SUCCESSFUL!" -ForegroundColor $ColorGreen
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor $ColorGreen
    Write-Host ""
    Write-Host "FinalProductPublish has been restored from backup:" -ForegroundColor White
    Write-Host "  Backup:    $backupName" -ForegroundColor Gray
    Write-Host "  Date:      $($backupDate.ToString('yyyy-MM-dd HH:mm:ss'))" -ForegroundColor Gray
    Write-Host "  Files:     $restoredCount" -ForegroundColor Gray
    Write-Host "  Size:      $restoredSize MB" -ForegroundColor Gray
    Write-Host ""
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor $ColorGreen
    Write-Host ""

} catch {
    Write-Host ""
    Write-ErrorMsg "Restoration failed: $_"
    Write-Host ""
    Write-Host "Error Details:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host ""
    exit 1
}
