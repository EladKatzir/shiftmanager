namespace ShiftManager.Models.Support;

public enum SimFailureKind
{
    GrantKeyNotFound,    // key doesn't exist in GrantTypes OR IsActive=false
    NoGrantsForUser,     // user has zero CanOwn=true rows for this GrantType
    ScopeNotCovered,     // grants exist but none cover the requested scope
    JobTypeMismatch,     // scope matched but JobTypeId differs
    NullJobTypeTrap,     // caller passed null jobTypeId, grant has specific JobTypeId
    SelfScopeNotSelf,    // self-scoped grant, targetUserId != userId
    SelfScopeNoTarget    // self-scoped grant but no targetUserId was provided at all
}

public class GrantEvalTrace
{
    public int GrantId         { get; set; }
    public string ScopeLabel   { get; set; } = "";  // e.g. "MoleculeId=5, JobTypeId=null"
    public bool Matched        { get; set; }
    public string? FailReason  { get; set; }
    public string GrantSource  { get; set; } = "";  // "AutoGrant (Lead)" | "Manual"
    public bool CanGive        { get; set; }
}

public class UserContextSummary
{
    public string  ProjectName  { get; set; } = "";
    public string  AreaName     { get; set; } = "";
    public string  MoleculeName { get; set; } = "";
    public string? CompanyName  { get; set; }
    public string? DeptName     { get; set; }
    public string? JobTypeName  { get; set; }
}

public class PermissionSimulationResult
{
    public bool IsGranted                        { get; set; }
    public GrantEvalTrace? MatchedTrace          { get; set; }
    public SimFailureKind? FailureKind           { get; set; }
    public string? FailureReason                 { get; set; }
    public string? MissingGrantKey               { get; set; }
    public string? SuggestionToFix               { get; set; }
    public List<GrantEvalTrace> EvaluatedGrants  { get; set; } = new();
    public UserContextSummary? UserContext        { get; set; }
    public bool IsRoleSimulation                 { get; set; }
    public string? SimulatedRoleName             { get; set; }
}
