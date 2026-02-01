using Microsoft.EntityFrameworkCore;

namespace ShiftManager.Services;

/// <summary>
/// Service for handling optimistic concurrency conflicts (B-018).
/// Provides utilities for detecting and handling concurrent edit conflicts.
/// </summary>
public class ConcurrencyService : IConcurrencyService
{
    private readonly ILogger<ConcurrencyService> _logger;

    public ConcurrencyService(ILogger<ConcurrencyService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ConcurrencySaveResult> SaveWithConcurrencyHandlingAsync(
        Func<Task<int>> saveAction,
        string entityType,
        int? entityId = null)
    {
        try
        {
            await saveAction();
            return new ConcurrencySaveResult(Success: true);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Log the conflict event for observability
            _logger.LogWarning(
                ex,
                "Concurrency conflict detected for {EntityType} {EntityId}. " +
                "Another user modified this record simultaneously.",
                entityType,
                entityId?.ToString() ?? "unknown");

            // Determine the type of conflict
            var entries = ex.Entries;
            var failureType = ConcurrencyFailureType.ConcurrentModification;

            foreach (var entry in entries)
            {
                // Try to reload the entity to see if it was deleted
                try
                {
                    await entry.ReloadAsync();
                }
                catch (Exception)
                {
                    // If we can't reload, the entity was likely deleted
                    failureType = ConcurrencyFailureType.EntityDeleted;
                    _logger.LogWarning(
                        "Entity {EntityType} {EntityId} appears to have been deleted by another user",
                        entityType,
                        entityId?.ToString() ?? "unknown");
                }
            }

            return new ConcurrencySaveResult(
                Success: false,
                FailureType: failureType,
                ErrorMessage: failureType == ConcurrencyFailureType.EntityDeleted
                    ? "This record was deleted by another user."
                    : "This record was modified by another user. Please reload and try again.");
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(
                ex,
                "Database error while saving {EntityType} {EntityId}",
                entityType,
                entityId?.ToString() ?? "unknown");

            return new ConcurrencySaveResult(
                Success: false,
                FailureType: ConcurrencyFailureType.DatabaseError,
                ErrorMessage: "A database error occurred while saving changes.");
        }
    }
}
