namespace ShiftManager.Services;

public interface ICalendarColumnWidthService
{
    /// <summary>All saved widths for a user+context as ColumnKey → width in px.</summary>
    Task<Dictionary<string, int>> GetWidthMapAsync(int userId, string contextKey);

    /// <summary>Replace every saved width for this user+context. Columns absent from
    /// <paramref name="widths"/> are cleared (i.e. reset to their CSS default); an empty map
    /// clears the whole context.</summary>
    Task SaveWidthsAsync(int userId, string contextKey, IReadOnlyDictionary<string, int> widths);
}
