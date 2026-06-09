namespace ShiftManager.Models;

/// <summary>
/// One row per (user, company) the user belongs to. A user can hold several.
/// Mirrors the DirectorCompany join-table pattern (soft-delete, (UserId, CompanyId) unique,
/// queried by UserId with IgnoreQueryFilters). NOT IBelongsToCompany — it must be readable
/// before an active tenant is chosen, so it carries CompanyId WITHOUT a tenant query filter.
/// The primary membership (IsPrimary=true) mirrors AppUser.CompanyId and the per-company
/// columns on AppUser; exactly one primary per user is an invariant enforced by the service.
/// </summary>
public class CompanyMembership
{
    public int Id { get; set; }

    /// <summary>The member.</summary>
    public int UserId { get; set; }

    /// <summary>The company this membership is in.</summary>
    public int CompanyId { get; set; }

    /// <summary>Role template in THIS company (drives this company's auto-grants — applied in Epic 5).</summary>
    public int? RoleTemplateId { get; set; }

    /// <summary>Org placement in THIS company.</summary>
    public int? JobTypeId { get; set; }
    public int? DepartmentId { get; set; }

    /// <summary>Whether the user participates in shifts in THIS company.</summary>
    public bool DoesShifts { get; set; }

    /// <summary>Rotation type in THIS company.</summary>
    public int? HomeTypeId { get; set; }

    /// <summary>Exactly one per user. The primary mirrors AppUser.CompanyId + AppUser's per-company columns.</summary>
    public bool IsPrimary { get; set; }

    /// <summary>Soft-delete flag (DirectorCompany pattern).</summary>
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Who created this membership (Owner/admin); 0 = system/backfill.</summary>
    public int GrantedBy { get; set; }

    // Navigation properties (optional — cross-tenant, like DirectorCompany)
    public AppUser? User { get; set; }
    public Company? Company { get; set; }
    public RoleTemplate? RoleTemplate { get; set; }
}
