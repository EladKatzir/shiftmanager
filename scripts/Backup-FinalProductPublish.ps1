<#
.SYNOPSIS
    Automated backup of FinalProductPublish with timestamp and retention policy

.DESCRIPTION
    Creates a timestamped backup of FinalProductPublish in:
      <repo_root>\Backups\FinalProductPublish\FinalProductPublish_BACKUP_<timestamp>

    Verifies backup by file count and size (informational), and keeps only the last 5 backups.

.EXAMPLE
    .\scripts\Backup-FinalProductPublish.ps1

.NOTES
    ASCII-only output to avoid encoding/parser issues.
#>

[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Paths
$RepoRoot   = Split-Path -Parent $PSScriptRoot
$SourcePath = Join-Path $RepoRoot 'FinalProductPublish'
$BackupRoot = Join-Path $RepoRoot 'Backups\FinalProductPublish'

# Retention
$KeepCount = 5

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
            '/E',                # include subdirs
            '/COPY:DAT',         # data/attrs/timestamps
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
        # Copy-Item fallback: copy CONTENTS not the directory node
        Write-Warn "robocopy not found; using Copy-Item fallback (slower)."
        Copy-Item -LiteralPath (Join-Path $FromDir '*') -Destination $ToDir -Recurse -Force -ErrorAction Stop
    }
}

try {
    Write-Host ""
    Write-Info "FinalProductPublish Backup Tool"
    Write-Host ""

    if (-not (Test-Path -LiteralPath $SourcePath)) {
        Write-Err ("FinalProductPublish folder not found at: {0}" -f $SourcePath)
        throw "Source folder missing"
    }

    Ensure-Directory -Path $BackupRoot

    $timestamp  = Get-Date -Format 'yyyyMMdd_HHmmss'
    $backupName = "FinalProductPublish_BACKUP_{0}" -f $timestamp
    $backupPath = Join-Path $BackupRoot $backupName

    Write-Info "Creating backup..."
    Write-Host ("  Source:      {0}" -f $SourcePath) -ForegroundColor Gray
    Write-Host ("  Destination: {0}" -f $backupPath) -ForegroundColor Gray
    Write-Host ("  Timestamp:   {0}" -f $timestamp) -ForegroundColor Gray
    Write-Host ""

    if ($PSCmdlet.ShouldProcess($backupPath, "Create backup from FinalProductPublish")) {
        # Ensure destination exists
        Ensure-Directory -Path $backupPath

        # Copy contents so backupPath mirrors FinalProductPublish directly
        Copy-Contents -FromDir $SourcePath -ToDir $backupPath
    }
    else {
        Write-Warn "Backup skipped due to WhatIf/Confirm."
        exit 0
    }

    Write-Info "Verifying backup integrity..."
    $src = Get-FolderStats -Path $SourcePath
    $bak = Get-FolderStats -Path $backupPath

    if ($src.Count -ne $bak.Count) {
        Write-Err ("File count mismatch! Source: {0}, Backup: {1}" -f $src.Count, $bak.Count)
        throw "Backup verification failed"
    }

    Write-Success ("Backup verified: {0} files, {1} MB" -f $bak.Count, $bak.SizeMB)

    # Retention policy
    Write-Info ("Applying retention policy (keep last {0})..." -f $KeepCount)
    $allBackups = Get-ChildItem -LiteralPath $BackupRoot -Directory -Filter 'FinalProductPublish_BACKUP_*' -ErrorAction SilentlyContinue |
                  Sort-Object Name -Descending

    $toDelete = @()
    if ($allBackups.Count -gt $KeepCount) {
        $toDelete = $allBackups | Select-Object -Skip $KeepCount
    }

    if ($toDelete.Count -gt 0) {
        Write-Info ("Removing {0} old backup(s)..." -f $toDelete.Count)
        foreach ($old in $toDelete) {
            Write-Host ("  Deleting: {0}" -f $old.Name) -ForegroundColor DarkGray
            if ($PSCmdlet.ShouldProcess($old.FullName, "Delete old backup")) {
                Remove-Item -LiteralPath $old.FullName -Recurse -Force -ErrorAction Stop
            }
        }
        Write-Success ("Removed {0} old backup(s)" -f $toDelete.Count)
    }
    else {
        Write-Info "No old backups to remove."
    }

    # Show remaining backups
    $remaining = Get-ChildItem -LiteralPath $BackupRoot -Directory -Filter 'FinalProductPublish_BACKUP_*' -ErrorAction SilentlyContinue |
                 Sort-Object Name -Descending

    Write-Host ""
    Write-Success "BACKUP SUCCESSFUL"
    Write-Host ""
    Write-Host "Backup Details:" -ForegroundColor White
    Write-Host ("  Location: {0}" -f $backupPath) -ForegroundColor Gray
    Write-Host ("  Files:    {0}" -f $bak.Count) -ForegroundColor Gray
    Write-Host ("  Size:     {0} MB" -f $bak.SizeMB) -ForegroundColor Gray
    Write-Host ""
    Write-Host ("Available Backups ({0}):" -f $remaining.Count) -ForegroundColor White

    foreach ($b in $remaining) {
        $ts = $b.Name -replace '^FinalProductPublish_BACKUP_', ''
        $dt = $null
        try { $dt = [DateTime]::ParseExact($ts, 'yyyyMMdd_HHmmss', $null) } catch { }
        Write-Host ("  - {0}" -f $b.Name) -ForegroundColor Gray
        if ($dt) {
            Write-Host ("    Created: {0}" -f $dt.ToString('yyyy-MM-dd HH:mm:ss')) -ForegroundColor DarkGray
        }
    }

    exit 0
}
catch {
    Write-Host ""
    Write-Err ("Backup failed: {0}" -f $_.Exception.Message)
    exit 1
}
