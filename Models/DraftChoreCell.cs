namespace ShiftManager.Models;

/// <summary>
/// A single touched chore-cell within a <see cref="DraftSession"/> (surface = Chores) — identified by
/// (UserId row, WorkDate column). A chore cell holds 0..N chores, so the payload is a canonical
/// serialization of a SET of chore descriptors (not an int-CSV): sub-project C serializes each descriptor
/// (ChoreTypeId, Title, StartTime, EndTime, Notes) to a stable string, sorts the set, and joins — keeping
/// the Foundation's conflict primitive (staged==baseline no-op; live==baseline safe; else drift → skip).
///
/// Created in the Foundation (Spec F) so sub-project C ships without its own migration.
/// </summary>
public class DraftChoreCell
{
    public int Id { get; set; }
    public int DraftSessionId { get; set; }

    /// <summary>Row coordinate — the user whose chore column this cell belongs to.</summary>
    public int UserId { get; set; }

    /// <summary>Column coordinate.</summary>
    public DateOnly WorkDate { get; set; }

    /// <summary>Canonical serialization of the live chore-descriptor set captured at first touch.</summary>
    public string BaselineChores { get; set; } = string.Empty;

    /// <summary>Canonical serialization of the draft's desired chore-descriptor set.</summary>
    public string StagedChores { get; set; } = string.Empty;

    public DraftSession DraftSession { get; set; } = null!;
}
