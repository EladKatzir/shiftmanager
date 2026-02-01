namespace ShiftManager.Models;

public class UserRoleAssignment
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int RoleTemplateId { get; set; }

    // Scope of this role assignment
    public int? CompanyId { get; set; }
    public int? DepartmentId { get; set; }
    public int? MoleculeId { get; set; }
    public int? AreaId { get; set; }
    public int? JobTypeId { get; set; }

    // Audit
    public int AssignedByUserId { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    // Navigation
    public AppUser User { get; set; } = null!;
    public RoleTemplate RoleTemplate { get; set; } = null!;
    public AppUser AssignedByUser { get; set; } = null!;
    public Company? Company { get; set; }
    public Department? Department { get; set; }
    public Molecule? Molecule { get; set; }
    public Area? Area { get; set; }
    public JobType? JobType { get; set; }
}
