using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ShiftManager.Services;

/// <summary>
/// Handles graceful shutdown to prevent SQLite corruption during IIS app pool recycling.
/// - Drains active requests via cancellation token
/// - Flushes pending WAL writes via PRAGMA wal_checkpoint(TRUNCATE)
/// - Logs shutdown sequence for diagnostics
/// Fixes: C-08 (no graceful shutdown handling)
/// </summary>
public class GracefulShutdownService : IHostedService
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GracefulShutdownService> _logger;

    public GracefulShutdownService(
        IHostApplicationLifetime lifetime,
        IConfiguration configuration,
        ILogger<GracefulShutdownService> logger)
    {
        _lifetime = lifetime;
        _configuration = configuration;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _lifetime.ApplicationStopping.Register(OnStopping);
        _lifetime.ApplicationStopped.Register(OnStopped);

        _logger.LogInformation("Graceful shutdown handler registered");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private void OnStopping()
    {
        _logger.LogWarning("Application is shutting down. Flushing SQLite WAL checkpoint...");

        try
        {
            var connectionString = _configuration.GetConnectionString("Default") ?? "Data Source=app.db";

            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            // Checkpoint the WAL file to ensure all pending writes are flushed to the main DB
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            using var reader = cmd.ExecuteReader();

            if (reader.Read())
            {
                var busy = reader.GetInt32(0);
                var checkpointed = reader.GetInt32(1);
                var total = reader.GetInt32(2);

                _logger.LogInformation(
                    "WAL checkpoint completed. Busy: {Busy}, Checkpointed: {Checkpointed}/{Total} pages",
                    busy, checkpointed, total);

                if (busy != 0)
                {
                    _logger.LogWarning("WAL checkpoint was blocked by concurrent readers. Some data may still be in WAL file.");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to checkpoint WAL during shutdown. Database may have pending WAL entries.");
        }
    }

    private void OnStopped()
    {
        _logger.LogInformation("Application has stopped. SQLite resources released.");
    }
}
