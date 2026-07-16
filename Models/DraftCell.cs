namespace ShiftManager.Models;

/// <summary>
/// A single touched shift-cell within a <see cref="DraftSession"/> — identified by (ShiftTypeId, WorkDate).
/// <see cref="BaselineUserIds"/> is the snapshot of the live assignees captured the first time the assigner
/// touched the cell; <see cref="StagedUserIds"/> is the draft's desired set. At commit, if the live cell's
/// current assignees still equal the baseline the staged set is applied; otherwise the cell drifted and is
/// skipped + reported. User ids are stored as a sorted, comma-separated string so equality is a plain string
/// compare.
///
/// Primary→trainee shadowing (sub-project A) is staged in the parallel <see cref="BaselineTrainees"/> /
/// <see cref="StagedTrainees"/> columns as a sorted CSV of "primaryUserId:traineeUserId" tokens — created
/// here in the Foundation so A ships without a migration.
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

    /// <summary>Sorted CSV of "primaryUserId:traineeUserId" pairs for the cell's live trainee shadows at first touch.</summary>
    public string BaselineTrainees { get; set; } = string.Empty;

    /// <summary>Sorted CSV of "primaryUserId:traineeUserId" pairs the draft desires for this cell.</summary>
    public string StagedTrainees { get; set; } = string.Empty;

    public DraftSession DraftSession { get; set; } = null!;

    // --- int-CSV helpers (sorted + de-duplicated so equality is a stable string compare) ---
    public static string Encode(IEnumerable<int> ids) =>
        string.Join(",", ids.Distinct().OrderBy(x => x));

    public static List<int> Decode(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? new List<int>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Select(s => int.TryParse(s, out var v) ? v : (int?)null)
                 .Where(v => v.HasValue).Select(v => v!.Value)
                 .Distinct().OrderBy(x => x).ToList();

    // --- pair-CSV helpers ("primary:trainee") — a primary shadows AT MOST ONE trainee (last token wins),
    //     sorted by primary then trainee so equality stays a plain string compare (mirrors the int-CSV pair). ---
    public static string EncodePairs(IEnumerable<KeyValuePair<int, int>> pairs) =>
        string.Join(",", pairs
            .GroupBy(p => p.Key)
            .Select(g => (Primary: g.Key, Trainee: g.Last().Value))
            .OrderBy(p => p.Primary).ThenBy(p => p.Trainee)
            .Select(p => string.Concat(p.Primary.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                       ":",
                                       p.Trainee.ToString(System.Globalization.CultureInfo.InvariantCulture))));

    public static Dictionary<int, int> DecodePairs(string? csv)
    {
        var result = new Dictionary<int, int>();
        if (string.IsNullOrWhiteSpace(csv)) return result;
        foreach (var tok in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = tok.Split(':', 2);
            if (parts.Length == 2
                && int.TryParse(parts[0], out var primary)
                && int.TryParse(parts[1], out var trainee))
                result[primary] = trainee; // last token wins for a repeated primary
        }
        return result;
    }
}
