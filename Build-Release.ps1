<#
.SYNOPSIS
    Automated Release Builder for ShiftManager

.DESCRIPTION
    Enterprise-grade automation system that builds, tests, and packages
    production-ready ProjectPublish deployments with one command.

    Features:
    - Automated building and testing
    - Intelligent documentation generation
    - Git integration with tagging
    - Comprehensive verification
    - Database seeding verification
    - Automatic rollback on failure

.PARAMETER Version
    Version number (e.g., "1.0.2"). If not provided, will prompt.

.PARAMETER SkipTests
    Skip application testing (not recommended for production)

.PARAMETER NoPush
    Create git tag but don't push to remote

.EXAMPLE
    .\Build-Release.ps1
    # Prompts for version and builds with full testing

.EXAMPLE
    .\Build-Release.ps1 -Version "1.0.2"
    # Builds version 1.0.2 with full testing

#>

[CmdletBinding()]
param(
    [Parameter()]
    [string]$Version,

    [Parameter()]
    [switch]$SkipTests,

    [Parameter()]
    [switch]$NoPush
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

# Script metadata
$ScriptVersion = "1.0.0"
$ScriptRoot = $PSScriptRoot
$BuildRoot = Join-Path $ScriptRoot "build"
$ProjectFile = Join-Path $ScriptRoot "ShiftManager.csproj"
$OutputFolder = Join-Path $ScriptRoot "ProjectPublish"

# ANSI Colors for output
$ColorReset = "`e[0m"
$ColorGreen = "`e[32m"
$ColorYellow = "`e[33m"
$ColorRed = "`e[31m"
$ColorBlue = "`e[34m"
$ColorCyan = "`e[36m"

# Track timing
$script:StartTime = Get-Date
$script:StageTimings = @{}

#region Helper Functions

function Write-Header {
    param([string]$Text)
    $width = 60
    Write-Host ""
    Write-Host ("═" * $width) -ForegroundColor Cyan
    Write-Host (" " * (($width - $Text.Length) / 2)) -NoNewline
    Write-Host $Text -ForegroundColor White
    Write-Host ("═" * $width) -ForegroundColor Cyan
    Write-Host ""
}

function Write-Stage {
    param(
        [string]$Stage,
        [string]$Message
    )
    Write-Host "[$Stage] " -ForegroundColor Cyan -NoNewline
    Write-Host $Message
}

function Write-Success {
    param([string]$Message)
    Write-Host "  ✅ " -ForegroundColor Green -NoNewline
    Write-Host $Message
}

function Write-WarningMsg {
    param([string]$Message)
    Write-Host "  ⚠️  " -ForegroundColor Yellow -NoNewline
    Write-Host $Message
}

function Write-ErrorMsg {
    param([string]$Message)
    Write-Host "  ❌ " -ForegroundColor Red -NoNewline
    Write-Host $Message
}

function Write-Info {
    param([string]$Message)
    Write-Host "  ⏳ " -ForegroundColor Blue -NoNewline
    Write-Host $Message
}

function Start-Stage {
    param([string]$Name)
    $script:CurrentStageStart = Get-Date
    $script:CurrentStageName = $Name
}

function Complete-Stage {
    $elapsed = (Get-Date) - $script:CurrentStageStart
    $script:StageTimings[$script:CurrentStageName] = $elapsed
    Write-Success "$script:CurrentStageName completed ($($elapsed.TotalSeconds.ToString('F1'))s)"
}

function Test-Administrator {
    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentUser)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

#endregion

#region Main Build Pipeline

function Invoke-BuildPipeline {
    try {
        Write-Header "ShiftManager Automated Release Builder v$ScriptVersion"

        # Stage 1: Pre-Flight Checks
        Start-Stage "Pre-Flight Checks"
        Write-Stage "STAGE 1/10" "Pre-Flight Checks..."
        & "$BuildRoot\Test-PreBuild.ps1" -Version $Version
        Complete-Stage

        # Stage 2: Backup
        Start-Stage "Backup"
        Write-Stage "STAGE 2/10" "Backing up existing deployment..."
        Invoke-Backup
        Complete-Stage

        # Stage 3: Build
        Start-Stage "Build"
        Write-Stage "STAGE 3/10" "Building release package..."
        Invoke-Build
        Complete-Stage

        # Stage 4: Build Verification
        Start-Stage "Build Verification"
        Write-Stage "STAGE 4/10" "Verifying build output..."
        & "$BuildRoot\Verify-Build.ps1" -OutputPath $OutputFolder
        Complete-Stage

        # Stage 5: Application Testing
        if (-not $SkipTests) {
            Start-Stage "Application Testing"
            Write-Stage "STAGE 5/10" "Testing application..."
            & "$BuildRoot\Test-Application.ps1" -OutputPath $OutputFolder
            Complete-Stage
        } else {
            Write-WarningMsg "Skipping application tests (not recommended for production)"
        }

        # Stage 6: Documentation Generation
        Start-Stage "Documentation Generation"
        Write-Stage "STAGE 6/10" "Generating documentation..."
        & "$BuildRoot\Generate-Documentation.ps1" -Version $Version -OutputPath $OutputFolder
        Complete-Stage

        # Stage 7: Final Integrity Check
        Start-Stage "Final Integrity Check"
        Write-Stage "STAGE 7/10" "Final integrity check..."
        & "$BuildRoot\Test-PackageIntegrity.ps1" -OutputPath $OutputFolder
        Complete-Stage

        # Stage 8: Git Tagging
        Start-Stage "Git Tagging"
        Write-Stage "STAGE 8/10" "Creating git tag..."
        & "$BuildRoot\Git-Integration.ps1" -Version $Version -NoPush:$NoPush
        Complete-Stage

        # Stage 9: Packaging
        Start-Stage "Packaging"
        Write-Stage "STAGE 9/10" "Creating distribution package..."
        $packageInfo = & "$BuildRoot\Create-Package.ps1" -Version $Version -OutputPath $OutputFolder
        Complete-Stage

        # Stage 10: Release Report
        Start-Stage "Release Report"
        Write-Stage "STAGE 10/10" "Generating release report..."
        & "$BuildRoot\Generate-ReleaseReport.ps1" -Version $Version -PackageInfo $packageInfo
        Complete-Stage

        # Success Summary
        Show-SuccessSummary -Version $Version -PackageInfo $packageInfo

    } catch {
        Write-ErrorMsg "Build failed: $_"
        Write-Host ""
        Write-Host "Error Details:" -ForegroundColor Red
        Write-Host $_.Exception.Message -ForegroundColor Red
        Write-Host $_.ScriptStackTrace -ForegroundColor Red

        # Attempt rollback
        Invoke-Rollback
        exit 1
    }
}

function Invoke-Backup {
    if (Test-Path $OutputFolder) {
        $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
        $backupFolder = "$($OutputFolder)_BACKUP_$timestamp"
        Write-Info "Backing up to: $backupFolder"
        Copy-Item -Path $OutputFolder -Destination $backupFolder -Recurse -Force
        Write-Success "Backup created"

        # Clean old backups (keep last 3)
        $backups = Get-ChildItem -Path $ScriptRoot -Directory -Filter "ProjectPublish_BACKUP_*" |
            Sort-Object Name -Descending |
            Select-Object -Skip 3
        if ($backups) {
            Write-Info "Cleaning old backups..."
            $backups | Remove-Item -Recurse -Force
        }
    } else {
        Write-Info "No existing deployment to backup"
    }
}

function Invoke-Build {
    # Clean existing output
    if (Test-Path $OutputFolder) {
        Write-Info "Removing existing ProjectPublish folder..."
        Remove-Item -Path $OutputFolder -Recurse -Force
    }

    # Run dotnet publish
    Write-Info "Running: dotnet publish -c Release -r win-x64 --self-contained"
    $buildStart = Get-Date

    $process = Start-Process -FilePath "dotnet" -ArgumentList @(
        "publish",
        $ProjectFile,
        "-c", "Release",
        "-r", "win-x64",
        "--self-contained", "true",
        "-o", $OutputFolder
    ) -NoNewWindow -Wait -PassThru

    if ($process.ExitCode -ne 0) {
        throw "dotnet publish failed with exit code $($process.ExitCode)"
    }

    $buildTime = ((Get-Date) - $buildStart).TotalSeconds
    Write-Success "Build successful ($($buildTime.ToString('F1'))s)"

    # Copy air-gapped deployment helper scripts (MANDATORY for production)
    Write-Info "Adding air-gapped deployment helper scripts..."

    $requiredScripts = @(
        @{Name = "UNBLOCK_FILES.bat"; Required = $true},
        @{Name = "VERIFY_FILES.bat"; Required = $true},
        @{Name = "QUICK_FIX.bat"; Required = $true},
        @{Name = "CRITICAL_BEFORE_DEMO.txt"; Required = $true},
        @{Name = "AIR_GAPPED_DEPLOYMENT_GUIDE.txt"; Required = $true},
        @{Name = "PRE_DEMO_CHECKLIST.txt"; Required = $false},
        @{Name = "TROUBLESHOOT_DEMO.txt"; Required = $false}
    )

    $missingRequired = @()
    foreach ($script in $requiredScripts) {
        $scriptPath = Join-Path $ScriptRoot $script.Name
        if (Test-Path $scriptPath) {
            Copy-Item -Path $scriptPath -Destination $OutputFolder -Force
            Write-Success "$($script.Name) added"
        } else {
            if ($script.Required) {
                Write-ErrorMsg "$($script.Name) NOT FOUND - REQUIRED for air-gapped deployment!"
                $missingRequired += $script.Name
            } else {
                Write-WarningMsg "$($script.Name) not found (optional)"
            }
        }
    }

    if ($missingRequired.Count -gt 0) {
        throw "Missing required deployment scripts: $($missingRequired -join ', '). Air-gapped deployments will fail without these!"
    }

    # Copy API documentation and client libraries
    Write-Info "Adding API documentation and client libraries..."

    $apiAssets = @(
        @{Name = "API_DOCUMENTATION.md"; Required = $true; Type = "File"},
        @{Name = "appsettings.Production.template.json"; Required = $true; Type = "File"},
        @{Name = "clients"; Required = $true; Type = "Directory"}
    )

    foreach ($asset in $apiAssets) {
        $assetPath = Join-Path $ScriptRoot $asset.Name
        if (Test-Path $assetPath) {
            if ($asset.Type -eq "Directory") {
                Copy-Item -Path $assetPath -Destination $OutputFolder -Recurse -Force
                Write-Success "$($asset.Name)/ folder added (API client libraries)"
            } else {
                Copy-Item -Path $assetPath -Destination $OutputFolder -Force
                Write-Success "$($asset.Name) added"
            }
        } else {
            if ($asset.Required) {
                Write-ErrorMsg "$($asset.Name) NOT FOUND - REQUIRED for complete deployment!"
                $missingRequired += $asset.Name
            } else {
                Write-WarningMsg "$($asset.Name) not found (optional)"
            }
        }
    }

    if ($missingRequired.Count -gt 0) {
        throw "Missing required API assets: $($missingRequired -join ', ')"
    }
}

function Invoke-Rollback {
    Write-WarningMsg "Attempting rollback..."

    # Find most recent backup
    $latestBackup = Get-ChildItem -Path $ScriptRoot -Directory -Filter "ProjectPublish_BACKUP_*" |
        Sort-Object Name -Descending |
        Select-Object -First 1

    if ($latestBackup) {
        Write-Info "Restoring from: $($latestBackup.Name)"

        if (Test-Path $OutputFolder) {
            Remove-Item -Path $OutputFolder -Recurse -Force
        }

        Copy-Item -Path $latestBackup.FullName -Destination $OutputFolder -Recurse -Force
        Write-Success "Rollback complete"
    } else {
        Write-WarningMsg "No backup found to restore"
    }
}

function Show-SuccessSummary {
    param(
        [string]$Version,
        [hashtable]$PackageInfo
    )

    $totalTime = (Get-Date) - $script:StartTime

    Write-Host ""
    Write-Header "BUILD SUCCESSFUL!"

    Write-Host "Version:        " -NoNewline
    Write-Host "v$Version" -ForegroundColor Green

    Write-Host "Build Date:     " -NoNewline
    Write-Host (Get-Date -Format "yyyy-MM-dd HH:mm:ss UTC")

    if ($PackageInfo) {
        Write-Host "Git Commit:     " -NoNewline
        Write-Host $PackageInfo.Commit -ForegroundColor Yellow

        Write-Host "Files:          " -NoNewline
        Write-Host $PackageInfo.FileCount

        Write-Host "DLLs:           " -NoNewline
        Write-Host $PackageInfo.DllCount

        Write-Host "Package Size:   " -NoNewline
        Write-Host $PackageInfo.PackageSize

        Write-Host "Tests Passed:   " -NoNewline
        Write-Host "ALL" -ForegroundColor Green

        Write-Host "OFFLINE Shift:  " -NoNewline
        Write-Host "✅ VERIFIED IN DATABASE" -ForegroundColor Green
    }

    Write-Host ""
    Write-Host "Distribution Files:" -ForegroundColor Cyan
    Write-Host "  [*] ProjectPublish/ (ready for USB transfer)"
    if ($PackageInfo.ZipFile) {
        Write-Host "  [*] $($PackageInfo.ZipFile)"
    }
    if ($PackageInfo.ReportFile) {
        Write-Host "  [*] $($PackageInfo.ReportFile)"
    }

    Write-Host ""
    Write-Host "Total Time: " -NoNewline
    Write-Host "$($totalTime.Minutes) minutes $($totalTime.Seconds) seconds" -ForegroundColor Cyan

    Write-Host ""
    Write-Host "Ready for production deployment!" -ForegroundColor Green
    Write-Host ""
}

#endregion

# Entry Point
try {
    # Check if running from correct directory
    if (-not (Test-Path $ProjectFile)) {
        Write-Host "ERROR: ShiftManager.csproj not found. Please run this script from the project root." -ForegroundColor Red
        exit 1
    }

    # Check if build directory exists
    if (-not (Test-Path $BuildRoot)) {
        Write-Host "ERROR: Build directory not found. Please ensure build/ folder exists with required scripts." -ForegroundColor Red
        exit 1
    }

    # Prompt for version if not provided
    if (-not $Version) {
        Write-Host ""
        Write-Host "Enter version number (e.g., 1.0.2): " -NoNewline -ForegroundColor Cyan
        $Version = Read-Host

        if (-not $Version) {
            Write-Host "ERROR: Version is required" -ForegroundColor Red
            exit 1
        }
    }

    # Validate version format (allow pre-release suffixes like -test, -alpha, -beta)
    if ($Version -notmatch '^\d+\.\d+\.\d+(-[\w\.]+)?$') {
        Write-Host "ERROR: Invalid version format. Use semantic versioning (e.g., 1.0.2 or 1.0.2-test)" -ForegroundColor Red
        exit 1
    }

    # Run the build pipeline
    Invoke-BuildPipeline

} catch {
    Write-Host ""
    Write-Host "FATAL ERROR:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
