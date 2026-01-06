<#
.SYNOPSIS
    Restore FinalProductPublish from a backup

.DESCRIPTION
    Restores FinalProductPublish from:
      <repo_root>\Backups\FinalProductPublish\FinalProductPublish_BACKUP_<timestamp>

    If no timestamp is provided, lists available backups.

.PARAMETER BackupTimestamp
    Timestamp of backup to restore (e.g., "20251213_120024")

.PARAMETER Force
    Skip interactive confirmation prompt.

.NOTES
    ASCII-only output to avoid encoding/parser issues.
#>

[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [Parameter()]
    [string]$BackupTimestamp,

    [Parameter()]
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Paths
$RepoRoot   = Split-Path -Parent $PSScriptRoot
$DestPath   = Join-Path $RepoRoot 'FinalProductPublish'
$BackupRoot = Join-Path $RepoRoot 'Backups\FinalProductPublish'

function Write-Info    { param([string]$Message) Write-Host ("[INFO] {0}" -f $Message) -ForegroundColor Cyan }
function Write-Success { param([string]$Message) Write-Host ("[ OK ] {0}" -f $Message) -ForegroundColor Green }
function Write-Warn    { param([string]$Message) Write-Host ("[WARN] {0}" -f $Message) -ForegroundColor Yellow }
function Write-Err     { param([string]$Message) Write-Host ("[FAIL] {0}" -f $Message) -ForegroundColor Red }

function Ensure-Directory {
    param([Parameter(Mandatory=$true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        $null = New-Item -ItemType Directory -Path $Path -Force
    }
}

function Get-FolderStats {
    param([Parameter(Mandatory=$true)][string]$Path)
    $files = Get-ChildItem -LiteralPath $Path -File -Recurse -Force -ErrorAction Stop
    $count = $files.Count
    $sum = 0
    if ($count -gt 0) { $sum = ($files | Measure-Object -Property Length -Sum).Sum }
    $mb = [math]::Round(($sum / 1MB), 2)
    return [pscustomobject]@{ Files = $files; Count = $count; SizeMB = $mb }
}

function Copy-Contents {
    param(
        [Parameter(Mandatory=$true)][string]$FromDir,
        [Parameter(Mandatory=$true)][string]$ToDir
    )

    Ensure-Directory -Path $ToDir

    if (Get-Command robocopy -ErrorAction SilentlyContinue) {
        # Robocopy codes: 0-7 success; 8+ failure
        $args = @(
            $FromDir, $ToDir,
            '/E',
            '/COPY:DAT',
            '/DCOPY:DAT',
            '/R:2','/W:1',
            '/NFL','/NDL','/NP',
            '/NJH','/NJS'
        )

        Write-Info ("Copying with robocopy: {0} -> {1}" -f $FromDir, $ToDir)
        & robocopy @args | Out-Null
        $rc = $LASTEXITCODE
        if ($rc -ge 8) {
            throw ("robocopy failed with code {0}" -f $rc)
        }
    }
    else {
        Write-Warn "robocopy not found; using Copy-Item fallback (slower)."
        Copy-Item -LiteralPath (Join-Path $FromDir '*') -Destination $ToDir -Recurse -Force -ErrorAction Stop
    }
}

function Remove-DirectoryContents {
    param([Parameter(Mandatory=$true)][string]$Path)
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath (Join-Path $Path '*') -Recurse -Force -ErrorAction Stop
    } else {
        Ensure-Directory -Path $Path
    }
}

try {
    Write-Host ""
    Write-Info "FinalProductPublish Restore Tool"
    Write-Host ""

    if (-not (Test-Path -LiteralPath $BackupRoot)) {
        Write-Err ("Backup directory not found: {0}" -f $BackupRoot)
        throw "No backups available"
    }

    $allBackups = Get-ChildItem -LiteralPath $BackupRoot -Directory -Filter 'FinalProductPublish_BACKUP_*' -ErrorAction SilentlyContinue |
                  Sort-Object Name -Descending

    if (-not $allBackups -or $allBackups.Count -eq 0) {
        Write-Err ("No backups found in: {0}" -f $BackupRoot)
        throw "No backups available"
    }

    # List mode
    if (-not $BackupTimestamp) {
        Write-Host "Available FinalProductPublish backups:" -ForegroundColor Cyan
        Write-Host ""

        foreach ($b in $allBackups) {
            $ts = $b.Name -replace '^FinalProductPublish_BACKUP_', ''
            $dt = $null
            try { $dt = [DateTime]::ParseExact($ts, 'yyyyMMdd_HHmmss', $null) } catch { }

            $stats = $null
            try { $stats = Get-FolderStats -Path $b.FullName } catch { $stats = $null }

            Write-Host ("  {0}" -f $ts) -ForegroundColor White -NoNewline
            if ($dt) {
                Write-Host ("  ({0})" -f $dt.ToString('MMM d, yyyy HH:mm:ss')) -ForegroundColor Gray
            } else {
                Write-Host ""
            }

            if ($stats) {
                Write-Host ("    Files: {0}, Size: {1} MB" -f $stats.Count, $stats.SizeMB) -ForegroundColor DarkGray
            } else {
                Write-Host "    Files: (unable to read), Size: (unable to read)" -ForegroundColor DarkGray
            }
            Write-Host ""
        }

        $exampleTs = ($allBackups[0].Name -replace '^FinalProductPublish_BACKUP_', '')
        Write-Host "Usage:" -ForegroundColor Cyan
        Write-Host '  .\scripts\Restore-FinalProductPublish.ps1 -BackupTimestamp "YYYYMMDD_HHMMSS"' -ForegroundColor Gray
        Write-Host ""
        Write-Host "Example:" -ForegroundColor Cyan
        Write-Host ("  .\scripts\Restore-FinalProductPublish.ps1 -BackupTimestamp ""{0}""" -f $exampleTs) -ForegroundColor Gray
        Write-Host ""
        exit 0
    }

    # Validate timestamp format
    if ($BackupTimestamp -notmatch '^\d{8}_\d{6}$') {
        Write-Err "Invalid timestamp format. Expected: YYYYMMDD_HHMMSS (e.g., 20251213_120024)"
        throw "Invalid timestamp format"
    }

    $backupName = "FinalProductPublish_BACKUP_{0}" -f $BackupTimestamp
    $backupPath = Join-Path $BackupRoot $backupName

    if (-not (Test-Path -LiteralPath $backupPath)) {
        Write-Err ("Backup not found: {0}" -f $backupName)
        Write-Host ""
        Write-Host "Available backups:" -ForegroundColor Yellow
        foreach ($b in $allBackups) {
            $ts = $b.Name -replace '^FinalProductPublish_BACKUP_', ''
            Write-Host ("  - {0}" -f $ts) -ForegroundColor Gray
        }
        Write-Host ""
        throw "Backup not found"
    }

    $backupDate = $null
    try { $backupDate = [DateTime]::ParseExact($BackupTimestamp, 'yyyyMMdd_HHmmss', $null) } catch { }

    $bakStats = Get-FolderStats -Path $backupPath

    Write-Host "Restore Details:" -ForegroundColor Cyan
    Write-Host ("  Source:  {0}" -f $backupPath) -ForegroundColor Gray
    if ($backupDate) {
        Write-Host ("  Created: {0}" -f $backupDate.ToString('yyyy-MM-dd HH:mm:ss')) -ForegroundColor Gray
    }
    Write-Host ("  Files:   {0}" -f $bakStats.Count) -ForegroundColor Gray
    Write-Host ("  Size:    {0} MB" -f $bakStats.SizeMB) -ForegroundColor Gray
    Write-Host ("  Target:  {0}" -f $DestPath) -ForegroundColor Gray
    Write-Host ""

    if (-not $Force) {
        Write-Warn "This will DELETE the current FinalProductPublish contents."
        $confirm = Read-Host "Are you sure you want to continue? (y/N)"
        if ($confirm -notin @('y','Y')) {
            Write-Warn "Restore cancelled by user."
            exit 0
        }
        Write-Host ""
    }

    if ($PSCmdlet.ShouldProcess($DestPath, ("Restore from {0}" -f $backupName))) {
        Write-Info "Clearing current FinalProductPublish contents..."
        Remove-DirectoryContents -Path $DestPath
        Write-Success "Destination cleared."

        Write-Info "Copying backup contents to FinalProductPublish..."
        Copy-Contents -FromDir $backupPath -ToDir $DestPath
        Write-Success "Backup copied."
    } else {
        Write-Warn "Restore skipped due to WhatIf/Confirm."
        exit 0
    }

    # Verify restoration by file count
    Write-Info "Verifying restoration..."
    $dstStats = Get-FolderStats -Path $DestPath

    if ($dstStats.Count -ne $bakStats.Count) {
        Write-Warn ("File count mismatch! Backup: {0}, Restored: {1}" -f $bakStats.Count, $dstStats.Count)
        Write-Warn "This may indicate an incomplete restore."
    } else {
        Write-Success ("Verification passed: {0} files, {1} MB" -f $dstStats.Count, $dstStats.SizeMB)
    }

    Write-Host ""
    Write-Success "RESTORATION SUCCESSFUL"
    Write-Host ("  Backup: {0}" -f $backupName) -ForegroundColor Gray
    if ($backupDate) {
        Write-Host ("  Date:   {0}" -f $backupDate.ToString('yyyy-MM-dd HH:mm:ss')) -ForegroundColor Gray
    }
    Write-Host ("  Files:  {0}" -f $dstStats.Count) -ForegroundColor Gray
    Write-Host ("  Size:   {0} MB" -f $dstStats.SizeMB) -ForegroundColor Gray
    Write-Host ""

    exit 0
}
catch {
    Write-Host ""
    Write-Err ("Restoration failed: {0}" -f $_.Exception.Message)
    exit 1
}
