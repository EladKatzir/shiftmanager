using System.Collections.Concurrent;

namespace ShiftManager.Services;

/// <summary>
/// In-memory rate limiting service to prevent brute force attacks.
/// Uses periodic timer-based cleanup to prevent memory leaks.
/// Note: For multi-server deployments, use Redis or similar distributed cache.
/// </summary>
public class RateLimitingService : IRateLimitingService, IDisposable
{
    private readonly ConcurrentDictionary<string, RateLimitEntry> _attempts = new();
    private readonly ILogger<RateLimitingService> _logger;
    private readonly Timer _cleanupTimer;
    private const int CleanupIntervalMinutes = 5;
    private const int EntryExpiryMinutes = 60;
    private const int MaxEntries = 10000;

    public RateLimitingService(ILogger<RateLimitingService> logger)
    {
        _logger = logger;
        // Run cleanup every 5 minutes instead of on every request
        _cleanupTimer = new Timer(
            _ => CleanupExpiredEntries(),
            null,
            TimeSpan.FromMinutes(CleanupIntervalMinutes),
            TimeSpan.FromMinutes(CleanupIntervalMinutes));
    }

    public bool IsAllowed(string key, int maxAttempts, int windowMinutes)
    {
        var now = DateTime.UtcNow;
        var windowStart = now.AddMinutes(-windowMinutes);

        // SECURITY: Memory cap to prevent DoS via dictionary exhaustion
        if (_attempts.Count > MaxEntries && !_attempts.ContainsKey(key))
        {
            // Emergency cleanup of expired entries
            CleanupExpiredEntries();

            // If still over limit after cleanup, reject as rate-limited
            if (_attempts.Count > MaxEntries)
            {
                _logger.LogWarning("Rate limiter memory cap reached ({Count} entries). Rejecting new key: {Key}",
                    _attempts.Count, key);
                return false;
            }
        }

        var entry = _attempts.GetOrAdd(key, _ => new RateLimitEntry());

        lock (entry)
        {
            // Remove attempts outside the time window
            entry.Attempts.RemoveAll(timestamp => timestamp < windowStart);

            // Check if limit exceeded
            if (entry.Attempts.Count >= maxAttempts)
            {
                _logger.LogWarning("Rate limit exceeded for key: {Key}. Attempts: {Count}/{Max}",
                    key, entry.Attempts.Count, maxAttempts);
                return false;
            }

            // Record this attempt
            entry.Attempts.Add(now);
            return true;
        }
    }

    public void Reset(string key)
    {
        _attempts.TryRemove(key, out _);
    }

    private void CleanupExpiredEntries()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-EntryExpiryMinutes);
        var removedCount = 0;

        foreach (var kvp in _attempts)
        {
            lock (kvp.Value)
            {
                if (kvp.Value.Attempts.All(t => t < cutoff))
                {
                    if (_attempts.TryRemove(kvp.Key, out _))
                        removedCount++;
                }
            }
        }

        if (removedCount > 0)
        {
            _logger.LogDebug("Rate limiter cleanup: removed {Count} expired entries, {Remaining} remaining",
                removedCount, _attempts.Count);
        }
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();
    }

    private class RateLimitEntry
    {
        public List<DateTime> Attempts { get; } = new();
    }
}
