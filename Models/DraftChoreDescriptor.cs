using System.Globalization;

namespace ShiftManager.Models;

/// <summary>
/// The user-authored IDENTITY of a chore inside a chores Draft-Mode cell (sub-project C) — deliberately
/// EXCLUDES the volatile/derived fields (<c>Id</c>, <c>CreatedAt</c>, <c>CreatedBy</c>, <c>WeightMinutes</c>).
/// A chore cell holds 0..N of these, so the cell's <see cref="DraftChoreCell.BaselineChores"/> /
/// <see cref="DraftChoreCell.StagedChores"/> payload is a canonical serialization of a SET of descriptors:
/// each descriptor → a reversible, delimiter-safe string; the set is de-duplicated, sorted ordinal, and
/// joined with '\n'. That keeps the Foundation's conflict primitive a plain string compare
/// (<c>staged == baseline</c> no-op; <c>live == baseline</c> safe; else drift → skip), the analog of
/// <see cref="DraftCell.Encode(System.Collections.Generic.IEnumerable{int})"/> for the richer chore cell.
///
/// The per-descriptor <see cref="Encode"/> string is ALSO the stable "descriptorKey" the overlay emits per
/// staged chip so the chore × can clear exactly one descriptor (staged chores have no <c>Chore.Id</c>).
/// </summary>
public sealed record DraftChoreDescriptor(
    int? ChoreTypeId,
    string Title,
    TimeOnly? StartTime,
    TimeOnly? EndTime,
    string? Notes)
{
    /// <summary>Descriptor of an existing live chore (trims exactly like <c>ChoreService.CreateChoreAsync</c> stores).</summary>
    public static DraftChoreDescriptor FromChore(Chore c) =>
        Create(c.ChoreTypeId, c.Title, c.StartTime, c.EndTime, c.Notes);

    /// <summary>Normalizing factory — trims Title/Notes and folds a blank Notes to null (matches create-time storage).</summary>
    public static DraftChoreDescriptor Create(int? choreTypeId, string? title, TimeOnly? startTime, TimeOnly? endTime, string? notes) =>
        new(choreTypeId,
            (title ?? string.Empty).Trim(),
            startTime,
            endTime,
            string.IsNullOrWhiteSpace(notes) ? null : notes!.Trim());

    private static string Fmt(TimeOnly? t) => t?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>
    /// Reversible, delimiter-safe single-descriptor string. Title/Notes are URL-escaped so the field
    /// separator '|', the key/value '=', and the set separator '\n' can never appear inside a value.
    /// </summary>
    public string Encode() =>
        string.Concat(
            "ct=", ChoreTypeId?.ToString(CultureInfo.InvariantCulture) ?? "",
            "|t=", Uri.EscapeDataString(Title ?? string.Empty),
            "|s=", Fmt(StartTime),
            "|e=", Fmt(EndTime),
            "|n=", Uri.EscapeDataString(Notes ?? string.Empty));

    /// <summary>Inverse of <see cref="Encode"/>. Unknown/malformed fields fall back to their defaults.</summary>
    public static DraftChoreDescriptor Decode(string s)
    {
        int? ct = null;
        string title = string.Empty;
        TimeOnly? st = null, et = null;
        string? notes = null;

        foreach (var part in s.Split('|'))
        {
            var idx = part.IndexOf('=');
            if (idx < 0) continue;
            var key = part.Substring(0, idx);
            var val = part.Substring(idx + 1);
            switch (key)
            {
                case "ct":
                    if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var c)) ct = c;
                    break;
                case "t":
                    title = Uri.UnescapeDataString(val);
                    break;
                case "s":
                    if (TimeOnly.TryParseExact(val, "HH:mm", CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var s1)) st = s1;
                    break;
                case "e":
                    if (TimeOnly.TryParseExact(val, "HH:mm", CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var e1)) et = e1;
                    break;
                case "n":
                    var n = Uri.UnescapeDataString(val);
                    notes = string.IsNullOrWhiteSpace(n) ? null : n;
                    break;
            }
        }
        return new DraftChoreDescriptor(ct, title, st, et, notes);
    }

    /// <summary>Canonical SET serialization: de-dup by <see cref="Encode"/>, sort ordinal, join with '\n'.</summary>
    public static string EncodeSet(IEnumerable<DraftChoreDescriptor> descriptors) =>
        string.Join("\n",
            descriptors.Select(d => d.Encode())
                       .Distinct(StringComparer.Ordinal)
                       .OrderBy(k => k, StringComparer.Ordinal));

    /// <summary>Inverse of <see cref="EncodeSet"/>.</summary>
    public static List<DraftChoreDescriptor> DecodeSet(string? canonical) =>
        string.IsNullOrEmpty(canonical)
            ? new List<DraftChoreDescriptor>()
            : canonical.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                       .Select(Decode)
                       .ToList();
}
