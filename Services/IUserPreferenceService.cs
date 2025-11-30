namespace ShiftManager.Services;

/// <summary>
/// ✅ PHASE 20: Service for managing user preferences (cookie-based)
/// </summary>
public interface IUserPreferenceService
{
    /// <summary>
    /// Get the "Show My Items Only" preference for calendar views.
    /// Default: true for employees/trainees, false for managers/directors/owners
    /// </summary>
    bool GetShowMyItemsOnly();

    /// <summary>
    /// Set the "Show My Items Only" preference
    /// </summary>
    void SetShowMyItemsOnly(bool showMyItemsOnly);
}
