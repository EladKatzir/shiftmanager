using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ShiftManager.Services;

/// <summary>
/// F-02: Checks available disk space where app.db resides.
/// Warn at less than 20% free (Degraded), fail at less than 10% free (Unhealthy).
/// Also checks WAL file size — warn if WAL exceeds 100MB (indicates checkpoint issues).
/// </summary>
public class DiskSpaceHealthCheck : IHealthCheck
{
    private const double WarnFreePercent = 20.0;   // Warn (Degraded) below 20% free
    private const double FailFreePercent = 10.0;   // Fail (Unhealthy) below 10% free
    private const long MinFreeSpaceMB = 100;       // Absolute minimum 100MB free
    private const long MaxWalSizeMB = 100;         // Warn if WAL exceeds 100MB

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var dbPath = Path.Combine(AppContext.BaseDirectory, "app.db");
            var driveInfo = new DriveInfo(Path.GetPathRoot(dbPath) ?? "C");

            var freeSpaceMB = driveInfo.AvailableFreeSpace / 1024 / 1024;
            var totalSpaceMB = driveInfo.TotalSize / 1024 / 1024;
            var freePercent = totalSpaceMB > 0 ? (double)freeSpaceMB / totalSpaceMB * 100 : 0;

            var data = new Dictionary<string, object>
            {
                ["freeSpaceMB"] = freeSpaceMB,
                ["totalSpaceMB"] = totalSpaceMB,
                ["freePercent"] = Math.Round(freePercent, 1)
            };

            // Check WAL file size
            var walPath = dbPath + "-wal";
            if (File.Exists(walPath))
            {
                var walSizeMB = new FileInfo(walPath).Length / 1024 / 1024;
                data["walSizeMB"] = walSizeMB;
                if (walSizeMB > MaxWalSizeMB)
                {
                    return Task.FromResult(HealthCheckResult.Degraded(
                        $"WAL file is {walSizeMB}MB (threshold: {MaxWalSizeMB}MB). Checkpoint may be stalled.",
                        data: new System.Collections.ObjectModel.ReadOnlyDictionary<string, object>(data)));
                }
            }

            if (freePercent < FailFreePercent || freeSpaceMB < MinFreeSpaceMB)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    $"Critical: {freeSpaceMB}MB free ({freePercent:F1}%). Below {FailFreePercent}% threshold.",
                    data: new System.Collections.ObjectModel.ReadOnlyDictionary<string, object>(data)));
            }

            if (freePercent < WarnFreePercent)
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    $"Low disk space: {freeSpaceMB}MB free ({freePercent:F1}%). Below {WarnFreePercent}% threshold.",
                    data: new System.Collections.ObjectModel.ReadOnlyDictionary<string, object>(data)));
            }

            return Task.FromResult(HealthCheckResult.Healthy(
                $"Disk space OK: {freeSpaceMB}MB free ({freePercent:F1}%)"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "Could not check disk space", ex));
        }
    }
}

/// <summary>
/// Checks application memory usage.
/// Reports Degraded if working set exceeds 600MB (early warning).
/// Reports Unhealthy if working set exceeds 800MB (hard limit).
/// </summary>
public class MemoryHealthCheck : IHealthCheck
{
    private const long WarnMemoryMB = 600;
    private const long MaxMemoryMB = 800;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var process = System.Diagnostics.Process.GetCurrentProcess();
            var memoryMB = process.WorkingSet64 / 1024 / 1024;

            if (memoryMB > MaxMemoryMB)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    $"High memory usage: {memoryMB}MB (threshold: {MaxMemoryMB}MB)"));
            }

            if (memoryMB > WarnMemoryMB)
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    $"Elevated memory usage: {memoryMB}MB (warn: {WarnMemoryMB}MB, max: {MaxMemoryMB}MB)"));
            }

            return Task.FromResult(HealthCheckResult.Healthy(
                $"Memory OK: {memoryMB}MB"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "Could not check memory", ex));
        }
    }
}
