using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;

namespace ShiftManager.Services;

/// <summary>
/// The single rule for which molecules' Shifts calendars a user may view: every active molecule the
/// <c>ViewShifts</c> grant reaches, plus the caller's own molecule.
///
/// Shared by the Shifts page (which molecules to offer) and the day-note write endpoint (whether a
/// note may be attached to the molecule a request names). Keeping one implementation means the set of
/// calendars a user can SEE and the set they can ANNOTATE cannot drift apart.
/// </summary>
public static class ShiftCalendarAccess
{
    public static async Task<HashSet<int>> GetViewableMoleculeIdsAsync(
        AppDbContext db, IGrantService grantService, int userId, int? ownMoleculeId)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(grantService);

        var candidateIds = new HashSet<int>(
            await grantService.GetAccessibleMoleculeIdsForGrantAsync(userId, "ViewShifts"));
        if (ownMoleculeId.HasValue)
            candidateIds.Add(ownMoleculeId.Value);

        if (candidateIds.Count == 0)
            return candidateIds;

        // SECURITY-AUDITED: SAFE — Molecule has no tenant query filter; the id set is already the
        // caller's grant-derived reach plus their own molecule, so this only removes inactive ones.
        var activeIds = await db.Molecules
            .Where(m => candidateIds.Contains(m.Id) && m.IsActive)
            .Select(m => m.Id)
            .ToListAsync();

        return activeIds.ToHashSet();
    }
}
