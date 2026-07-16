namespace ShiftManager.Models;

/// <summary>
/// A single touched on-call cell within a <see cref="DraftSession"/> (surface = OnCall) — identified by
/// (DutyTypeValue, WorkDate). <see cref="DutyTypeValue"/> is the int the calendar already keys on and covers
/// the built-in <c>OnDutyType</c> enum plus custom <c>OnDutyTypeConfig</c> (TypeValue &gt; 1) uniformly.
/// The user set reuses the same sorted-CSV convention as <see cref="DraftCell"/> (Encode/Decode verbatim).
///
/// SCOPE LANDMINE (sub-project D): baseline / staged-target / live-now are computed over the GLOBAL active
/// set for (DutyTypeValue, WorkDate) — on-call has no company/molecule/area data boundary. The session's
/// AreaId is a UI hint only. Created in the Foundation (Spec F) so sub-project D ships without a migration.
/// </summary>
public class DraftDutyCell
{
    public int Id { get; set; }
    public int DraftSessionId { get; set; }

    /// <summary>Row coordinate — the duty type (built-in enum value or custom config TypeValue).</summary>
    public int DutyTypeValue { get; set; }

    /// <summary>Column coordinate.</summary>
    public DateOnly WorkDate { get; set; }

    /// <summary>Sorted CSV of the live (global) on-duty user ids captured at first touch.</summary>
    public string BaselineUserIds { get; set; } = string.Empty;

    /// <summary>Sorted CSV of the draft's desired (global) on-duty user ids for this cell.</summary>
    public string StagedUserIds { get; set; } = string.Empty;

    public DraftSession DraftSession { get; set; } = null!;
}
