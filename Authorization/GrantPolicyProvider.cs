using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace ShiftManager.Authorization;

/// <summary>
/// Custom policy provider that creates grant-based policies on demand.
/// Enables [Authorize(Policy = "Grant:SomeGrantKey")] without pre-registration.
/// </summary>
public class GrantPolicyProvider : IAuthorizationPolicyProvider
{
    private const string GrantPrefix = "Grant:";
    private readonly DefaultAuthorizationPolicyProvider _fallbackPolicyProvider;

    public GrantPolicyProvider(IOptions<AuthorizationOptions> options)
    {
        _fallbackPolicyProvider = new DefaultAuthorizationPolicyProvider(options);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync()
    {
        return _fallbackPolicyProvider.GetDefaultPolicyAsync();
    }

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync()
    {
        return _fallbackPolicyProvider.GetFallbackPolicyAsync();
    }

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        // Handle "Grant:SomeKey" policies dynamically
        if (policyName.StartsWith(GrantPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var grantKey = policyName.Substring(GrantPrefix.Length);

            var policy = new AuthorizationPolicyBuilder()
                .AddRequirements(new GrantRequirement(grantKey))
                .Build();

            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        // Fall back to pre-registered policies
        return _fallbackPolicyProvider.GetPolicyAsync(policyName);
    }
}
