using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ShiftManager.Services;

/// <summary>
/// Checks available disk space where app.db resides.
/// Reports unhealthy if less than 100MB free.
/// </summary>
public class DiskSpaceHealthCheck : IHealthCheck
{
    private const long MinFreeSpaceMB = 100;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var dbPath = Path.Combine(AppContext.BaseDirectory, "app.db");
            var driveInfo = new DriveInfo(Path.GetPathRoot(dbPath) ?? "C");

            var freeSpaceMB = driveInfo.AvailableFreeSpace / 1024 / 1024;

            if (freeSpaceMB < MinFreeSpaceMB)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    $"Low disk space: {freeSpaceMB}MB free (minimum: {MinFreeSpaceMB}MB)"));
            }

            return Task.FromResult(HealthCheckResult.Healthy(
                $"Disk space OK: {freeSpaceMB}MB free"));
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
/// Reports unhealthy if working set exceeds 800MB.
/// </summary>
public class MemoryHealthCheck : IHealthCheck
{
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
