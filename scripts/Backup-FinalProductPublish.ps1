<#
.SYNOPSIS
    Automated backup of FinalProductPublish with timestamp and retention policy

.DESCRIPTION
    Creates a timestamped backup of FinalProductPublish in Backups/FinalProductPublish/ directory.
    Implements retention policy to keep only the last 5 backups.

.EXAMPLE
    .\Backup-FinalProductPublish.ps1

.NOTES
    Author: Claude Code
    Version: 1.0
#>

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

# Paths
$ScriptRoot = Split-Path -Parent $PSScriptRoot
$SourcePath = Join-Path $ScriptRoot "FinalProductPublish"
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
    Write-Host "  FinalProductPublish Backup Tool" -ForegroundColor White
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor $ColorCyan
    Write-Host ""

    # Check if FinalProductPublish exists
    if (-not (Test-Path $SourcePath)) {
        Write-ErrorMsg "FinalProductPublish folder not found at: $SourcePath"
        throw "Source folder missing"
    }

    # Create backup root directory if missing
    if (-not (Test-Path $BackupRoot)) {
        Write-Info "Creating backup directory..."
        New-Item -ItemType Directory -Path $BackupRoot -Force | Out-Null
        Write-Success "Backup directory created"
    }

    # Generate timestamp
    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $backupName = "FinalProductPublish_BACKUP_$timestamp"
    $backupPath = Join-Path $BackupRoot $backupName

    Write-Info "Creating backup..."
    Write-Host "    Source: $SourcePath" -ForegroundColor Gray
    Write-Host "    Destination: $backupPath" -ForegroundColor Gray
    Write-Host "    Timestamp: $timestamp" -ForegroundColor Gray
    Write-Host ""

    # Copy FinalProductPublish to backup
    Write-Info "Copying files (this may take 1-2 minutes)..."
    Copy-Item -Path $SourcePath -Destination $backupPath -Recurse -Force

    # Verify backup
    Write-Info "Verifying backup integrity..."
    $sourceFiles = Get-ChildItem -Path $SourcePath -File -Recurse
    $backupFiles = Get-ChildItem -Path $backupPath -File -Recurse

    $sourceCount = $sourceFiles.Count
    $backupCount = $backupFiles.Count

    if ($sourceCount -ne $backupCount) {
        Write-ErrorMsg "File count mismatch! Source: $sourceCount, Backup: $backupCount"
        throw "Backup verification failed"
    }

    $sourceSize = [math]::Round(($sourceFiles | Measure-Object -Property Length -Sum).Sum / 1MB, 2)
    $backupSize = [math]::Round(($backupFiles | Measure-Object -Property Length -Sum).Sum / 1MB, 2)

    Write-Success "Backup verified: $backupCount files, $backupSize MB"

    # Implement retention policy (keep last 5 backups)
    Write-Info "Checking backup retention policy..."
    $allBackups = Get-ChildItem -Path $BackupRoot -Directory -Filter "FinalProductPublish_BACKUP_*" |
        Sort-Object Name -Descending

    $keepCount = 5
    $backupsToDelete = $allBackups | Select-Object -Skip $keepCount

    if ($backupsToDelete) {
        Write-Info "Cleaning old backups (keeping last $keepCount)..."
        foreach ($oldBackup in $backupsToDelete) {
            Write-Host "    Removing: $($oldBackup.Name)" -ForegroundColor Gray
            Remove-Item -Path $oldBackup.FullName -Recurse -Force
        }
        Write-Success "Removed $($backupsToDelete.Count) old backup(s)"
    } else {
        Write-Info "All backups within retention policy (keeping last $keepCount)"
    }

    # Show remaining backups
    $remainingBackups = Get-ChildItem -Path $BackupRoot -Directory -Filter "FinalProductPublish_BACKUP_*" |
        Sort-Object Name -Descending

    Write-Host ""
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor $ColorCyan
    Write-Host "  BACKUP SUCCESSFUL!" -ForegroundColor $ColorGreen
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor $ColorCyan
    Write-Host ""
    Write-Host "Backup Details:" -ForegroundColor White
    Write-Host "  Location: $backupPath" -ForegroundColor Gray
    Write-Host "  Files:    $backupCount files" -ForegroundColor Gray
    Write-Host "  Size:     $backupSize MB" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Available Backups ($($remainingBackups.Count)):" -ForegroundColor White
    foreach ($backup in $remainingBackups) {
        $backupTimestamp = $backup.Name -replace "FinalProductPublish_BACKUP_", ""
        $backupDate = [DateTime]::ParseExact($backupTimestamp, "yyyyMMdd_HHmmss", $null)
        Write-Host "  - $($backup.Name)" -ForegroundColor Gray
        Write-Host "    Created: $($backupDate.ToString('yyyy-MM-dd HH:mm:ss'))" -ForegroundColor DarkGray
    }
    Write-Host ""
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor $ColorCyan
    Write-Host ""

} catch {
    Write-Host ""
    Write-ErrorMsg "Backup failed: $_"
    Write-Host ""
    Write-Host "Error Details:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
