using ShiftManager.Models.Support;

namespace ShiftManager.Models;

public class RoleTemplateGrant
{
    public int Id { get; set; }
    public int RoleTemplateId { get; set; }
    public int GrantTypeId { get; set; }
    public bool CanOwn { get; set; }
    public bool CanGive { get; set; }
    public GrantScopeMode ScopeMode { get; set; }
    public bool IsOverride { get; set; }  // Owner modified default

    public RoleTemplate RoleTemplate { get; set; } = null!;
    public GrantType GrantType { get; set; } = null!;
}
