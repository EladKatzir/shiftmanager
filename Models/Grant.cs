namespace ShiftManager.Models;

public class Grant
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int GrantTypeId { get; set; }

    // Hierarchical Scope (one or more set depending on scope level)
    public int? ProjectId { get; set; }
    public int? AreaId { get; set; }
    public int? MoleculeId { get; set; }
    public int? DepartmentId { get; set; }
    public int? CompanyId { get; set; }
    public int? JobTypeId { get; set; }

    // Capabilities
    public bool CanOwn { get; set; }   // Can perform the action
    public bool CanGive { get; set; }  // Can grant to others

    // Audit
    public int? GrantedByUserId { get; set; }
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
    public bool IsAutoGrant { get; set; }  // true = from role template

    // Navigation
    public AppUser User { get; set; } = null!;
    public GrantType GrantType { get; set; } = null!;
    public AppUser? GrantedByUser { get; set; }
    public Project? Project { get; set; }
    public Area? Area { get; set; }
    public Molecule? Molecule { get; set; }
    public Department? Department { get; set; }
    public Company? Company { get; set; }
    public JobType? JobType { get; set; }
}
