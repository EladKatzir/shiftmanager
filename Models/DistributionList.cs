using System.ComponentModel.DataAnnotations;

namespace ShiftManager.Models;

/// <summary>
/// A named, shared group of users scoped to a Molecule (e.g. "מוסמכי כחול" / blue-qualified,
/// "מחזיקי משמרת הבנה"). Used to organize the by-user shift calendar into collapsible sections —
/// one section per selected list. Created/edited/deleted by managers (ManageDistributionLists grant,
/// floor = Lead/מפ"צ and above).
///
/// Scope note: although this entity implements <see cref="IBelongsToCompany"/> (so the
/// CompanyIdInterceptor stamps the creator's CompanyId for provenance and a standard tenant query
/// filter applies), the REAL visibility scope is the <see cref="MoleculeId"/>. Lists are shared across
/// all companies in a molecule, so reads go through DistributionListService which uses
/// IgnoreQueryFilters() + MoleculeId scoping — mirroring ShiftCalendarService.GetUsersForCalendarAsync,
/// the established cross-company-within-molecule pattern. Hard-delete (no soft-delete flag): deleting a
/// list removes the row and cascades its members.
/// </summary>
public class DistributionList : IBelongsToCompany
{
    public int Id { get; set; }

    /// <summary>
    /// Provenance / tenant stamp — auto-set by CompanyIdInterceptor on insert (the creator's company).
    /// NOT the visibility scope; see <see cref="MoleculeId"/>.
    /// </summary>
    public int CompanyId { get; set; }

    /// <summary>
    /// The molecule this list belongs to and is visible within. Set explicitly by the service on create
    /// (the interceptor only stamps CompanyId, never MoleculeId).
    /// </summary>
    public int MoleculeId { get; set; }

    /// <summary>
    /// List name (1-80 chars). Case-insensitive unique within a molecule.
    /// </summary>
    [Required]
    [MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// User who created the list.
    /// </summary>
    public int CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Company? Company { get; set; }
    public Molecule? Molecule { get; set; }
    public AppUser? Creator { get; set; }
    public ICollection<DistributionListMember> Members { get; set; } = new List<DistributionListMember>();
}
