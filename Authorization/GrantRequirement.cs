using Microsoft.AspNetCore.Authorization;

namespace ShiftManager.Authorization;

/// <summary>
/// Requirement for grant-based authorization.
/// </summary>
public class GrantRequirement : IAuthorizationRequirement
{
    public string GrantKey { get; }

    public GrantRequirement(string grantKey)
    {
        GrantKey = grantKey;
    }
}
