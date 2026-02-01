namespace ShiftManager.Services;

/// <summary>
/// Service interface for handling optimistic concurrency conflicts (B-018).
/// </summary>
public interface IConcurrencyService
{
    /// <summary>
    /// Attempts to save changes, handling concurrency conflicts gracefully.
    /// </summary>
    /// <param name="saveAction">The async action that performs the save (typically _db.SaveChangesAsync)</param>
    /// <param name="entityType">The type of entity being saved (for logging/error messages)</param>
    /// <param name="entityId">The ID of the entity being saved</param>
    /// <returns>A result indicating success or the type of failure</returns>
    Task<ConcurrencySaveResult> SaveWithConcurrencyHandlingAsync(
        Func<Task<int>> saveAction,
        string entityType,
        int? entityId = null);
}

/// <summary>
/// Result of a save operation with concurrency handling.
/// </summary>
public record ConcurrencySaveResult(
    bool Success,
    ConcurrencyFailureType? FailureType = null,
    string? ErrorMessage = null);

/// <summary>
/// Types of concurrency failures.
/// </summary>
public enum ConcurrencyFailureType
{
    /// <summary>
    /// Another user modified the same entity.
    /// </summary>
    ConcurrentModification,

    /// <summary>
    /// The entity was deleted by another user.
    /// </summary>
    EntityDeleted,

    /// <summary>
    /// A general database error occurred.
    /// </summary>
    DatabaseError
}
