namespace ShiftManager.Models;

/// <summary>
/// A single touched shift-cell within a <see cref="DraftSession"/> — identified by (ShiftTypeId, WorkDate).
/// <see cref="BaselineUserIds"/> is the snapshot of the live assignees captured the first time the assigner
/// touched the cell; <see cref="StagedUserIds"/> is the draft's desired set. At commit, if the live cell's
/// current assignees still equal the baseline the staged set is applied; otherwise the cell is a conflict.
/// User ids are stored as a sorted, comma-separated string so equality is a plain string compare.
/// </summary>
public class DraftCell
{
    public int Id { get; set; }
    public int DraftSessionId { get; set; }
    public int ShiftTypeId { get; set; }
    public DateOnly WorkDate { get; set; }

    /// <summary>Sorted CSV of live user ids when the cell was first touched (the optimistic baseline).</summary>
    public string BaselineUserIds { get; set; } = string.Empty;

    /// <summary>Sorted CSV of the draft's desired user ids for this cell.</summary>
    public string StagedUserIds { get; set; } = string.Empty;

    public DraftSession DraftSession { get; set; } = null!;

    // --- CSV helpers (sorted + de-duplicated so equality is a stable string compare) ---
    public static string Encode(IEnumerable<int> ids) =>
        string.Join(",", ids.Distinct().OrderBy(x => x));

    public static List<int> Decode(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? new List<int>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Select(s => int.TryParse(s, out var v) ? v : (int?)null)
                 .Where(v => v.HasValue).Select(v => v!.Value)
                 .Distinct().OrderBy(x => x).ToList();
}
