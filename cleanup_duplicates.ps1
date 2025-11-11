# PowerShell script to clean up duplicate ShiftTypes
# Run this with: powershell -ExecutionPolicy Bypass -File cleanup_duplicates.ps1

Write-Host "Cleaning up duplicate ShiftTypes..." -ForegroundColor Yellow

# Check if database exists
if (-not (Test-Path "ShiftManager.db")) {
    Write-Host "Error: ShiftManager.db not found!" -ForegroundColor Red
    exit 1
}

# Create a backup first
Copy-Item "ShiftManager.db" "ShiftManager.db.backup_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
Write-Host "Created database backup" -ForegroundColor Green

# Python script to execute SQL (works on most systems)
$pythonScript = @'
import sqlite3
import sys

conn = sqlite3.connect('ShiftManager.db')
cursor = conn.cursor()

# Find duplicates
print("Finding duplicates...")
cursor.execute("""
    SELECT CompanyId, Key, COUNT(*) as Count
    FROM ShiftTypes
    GROUP BY CompanyId, Key
    HAVING COUNT(*) > 1
""")
duplicates = cursor.fetchall()
print(f"Found {len(duplicates)} duplicate groups")
for dup in duplicates:
    print(f"  Company {dup[0]}, Key {dup[1]}: {dup[2]} records")

# Delete duplicates (keep only the oldest record for each CompanyId+Key)
cursor.execute("""
    DELETE FROM ShiftTypes
    WHERE Id NOT IN (
        SELECT MIN(Id)
        FROM ShiftTypes
        GROUP BY CompanyId, Key
    )
""")
deleted = cursor.rowcount
conn.commit()

print(f"\nDeleted {deleted} duplicate records")

# Show final state
cursor.execute("""
    SELECT Id, CompanyId, Key, Start, End
    FROM ShiftTypes
    ORDER BY CompanyId, Key
""")
remaining = cursor.fetchall()
print(f"\nRemaining ShiftTypes ({len(remaining)} total):")
for st in remaining:
    print(f"  Id={st[0]}, Company={st[1]}, Key={st[2]}, Time={st[3]}-{st[4]}")

conn.close()
'@

# Try to run with Python
try {
    $pythonScript | python -
    Write-Host "`nCleanup completed successfully!" -ForegroundColor Green
} catch {
    Write-Host "`nPython not available. Please run the SQL manually:" -ForegroundColor Yellow
    Write-Host "1. Install DB Browser for SQLite or use dotnet-ef"
    Write-Host "2. Open ShiftManager.db"
    Write-Host "3. Run the SQL from cleanup_duplicates.sql"
}
