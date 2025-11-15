$OutputPath = "C:\Users\katzi\Downloads\ShiftManager\ProjectPublish"

$criticalDlls = @(
    "SixLabors.ImageSharp.dll",
    "ShiftManager.dll",
    "e_sqlite3.dll",
    "Microsoft.EntityFrameworkCore.dll",
    "Microsoft.EntityFrameworkCore.Sqlite.dll",
    "SQLitePCLRaw.provider.e_sqlite3.dll",
    "Microsoft.Data.Sqlite.dll"
)

$checksumContent = @"
================================================================================
                    CRITICAL DLL CHECKSUMS
================================================================================

These checksums can be used to verify files on air-gapped machines.

Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
Version: v1.1.0

To verify on Windows using CertUtil:
    CertUtil -hashfile FILENAME.dll SHA256

"@

foreach ($dll in $criticalDlls) {
    $dllPath = Join-Path $OutputPath $dll
    if (Test-Path $dllPath) {
        $hash = (Get-FileHash -Path $dllPath -Algorithm SHA256).Hash
        $size = (Get-Item $dllPath).Length
        $checksumContent += "`n$dll`:`n"
        $checksumContent += "  SHA256: $hash`n"
        $checksumContent += "  Size:   $size bytes`n"
    }
}

$checksumPath = Join-Path $OutputPath "DLL_CHECKSUMS.txt"
$checksumContent | Set-Content -Path $checksumPath -Encoding UTF8

Write-Host "DLL_CHECKSUMS.txt created successfully at: $checksumPath"
