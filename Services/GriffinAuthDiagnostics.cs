using System.Collections.Concurrent;

namespace ShiftManager.Services;

/// <summary>
/// Singleton, thread-safe, fixed-capacity ring buffer of Griffin auth events.
/// Holds the last <see cref="Capacity"/> events in memory only — no DB writes, no disk I/O,
/// no log file dependency. Survives across requests but is reset on app restart.
///
/// Why singleton, not scoped: this is process-wide diagnostic state. Each request should
/// see the same buffer instance so an admin viewing the diagnostic page can read events
/// recorded by a different request's callback handler.
///
/// Why not the database: writing on the auth hot-path adds latency and a failure dependency
/// (a DB hiccup during login would now create a SECOND failure for the diagnostic write).
/// The in-memory pattern matches the "telemetry on, persistence later" trade-off used
/// elsewhere in the project (e.g., performance metrics).
/// </summary>
public sealed class GriffinAuthDiagnostics : IGriffinAuthDiagnostics
{
    public const int Capacity = 50;

    // ConcurrentQueue gives us thread-safe enqueue + dequeue. A lock guards the
    // composite "enqueue + maybe-dequeue" operation so the size never exceeds Capacity
    // even under concurrent Record() calls.
    private readonly ConcurrentQueue<GriffinAuthEvent> _events = new();
    private readonly object _lock = new();

    public void Record(GriffinAuthEvent evt)
    {
        lock (_lock)
        {
            _events.Enqueue(evt);
            while (_events.Count > Capacity && _events.TryDequeue(out _))
            {
                // Drained one over-capacity item; loop in case multiple snuck in.
            }
        }
    }

    public IReadOnlyList<GriffinAuthEvent> GetRecent(int count = Capacity)
    {
        // Snapshot the queue (cheap — at most Capacity items) and return newest-first.
        // ToArray on ConcurrentQueue is documented thread-safe.
        var snapshot = _events.ToArray();
        if (snapshot.Length == 0) return Array.Empty<GriffinAuthEvent>();
        Array.Reverse(snapshot);
        return count >= snapshot.Length ? snapshot : snapshot[..count];
    }

    public void Clear()
    {
        lock (_lock)
        {
            while (_events.TryDequeue(out _)) { }
        }
    }
}
