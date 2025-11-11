# PowerShell script to add OFFLINE shift type to the database
$dbPath = "C:\Users\katzi\Downloads\ShiftManager\app.db"

# Check if database exists
if (-not (Test-Path $dbPath)) {
    Write-Error "Database not found at: $dbPath"
    exit 1
}

Write-Host "Database found at: $dbPath" -ForegroundColor Green

# Load System.Data.SQLite (built-in with .NET)
Add-Type -Path "C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.Data.SQLite\v4.0_1.0.118.0__db937bc2d44ff139\System.Data.SQLite.dll" -ErrorAction SilentlyContinue

# Create connection string
$connectionString = "Data Source=$dbPath;Version=3;"

try {
    # Create and open connection
    $connection = New-Object System.Data.SQLite.SQLiteConnection($connectionString)
    $connection.Open()
    Write-Host "Connected to database successfully" -ForegroundColor Green

    # Get all companies
    $getCompaniesCmd = $connection.CreateCommand()
    $getCompaniesCmd.CommandText = "SELECT Id, Name FROM Companies"
    $reader = $getCompaniesCmd.ExecuteReader()

    $companiesAdded = 0
    $companiesSkipped = 0

    while ($reader.Read()) {
        $companyId = $reader["Id"]
        $companyName = $reader["Name"]

        # Check if OFFLINE shift type already exists for this company
        $checkCmd = $connection.CreateCommand()
        $checkCmd.CommandText = "SELECT COUNT(*) FROM ShiftTypes WHERE CompanyId = @CompanyId AND Key = 'OFFLINE'"
        $checkCmd.Parameters.AddWithValue("@CompanyId", $companyId) | Out-Null
        $count = [int]$checkCmd.ExecuteScalar()

        if ($count -eq 0) {
            # Insert OFFLINE shift type
            $insertCmd = $connection.CreateCommand()
            $insertCmd.CommandText = @"
INSERT INTO ShiftTypes (CompanyId, Key, Start, End, CustomName)
VALUES (@CompanyId, 'OFFLINE', '00:00:00', '00:00:00', NULL)
"@
            $insertCmd.Parameters.AddWithValue("@CompanyId", $companyId) | Out-Null
            $insertCmd.ExecuteNonQuery() | Out-Null

            Write-Host "✓ Added OFFLINE shift type to company: $companyName (ID=$companyId)" -ForegroundColor Green
            $companiesAdded++
        }
        else {
            Write-Host "- OFFLINE shift type already exists for company: $companyName (ID=$companyId)" -ForegroundColor Yellow
            $companiesSkipped++
        }
    }

    $reader.Close()
    $connection.Close()

    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host "Summary:" -ForegroundColor Cyan
    Write-Host "  Companies with OFFLINE added: $companiesAdded" -ForegroundColor Green
    Write-Host "  Companies skipped (already had OFFLINE): $companiesSkipped" -ForegroundColor Yellow
    Write-Host "========================================" -ForegroundColor Cyan
}
catch {
    Write-Error "Error: $_"
    exit 1
}
