using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using ShiftManager.Services;

namespace ShiftManager.Authorization;

/// <summary>
/// Handles grant-based authorization by checking if the user has the required grant.
/// </summary>
public class GrantAuthorizationHandler : AuthorizationHandler<GrantRequirement>
{
    private readonly IGrantService _grantService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<GrantAuthorizationHandler> _logger;

    public GrantAuthorizationHandler(
        IGrantService grantService,
        ICurrentUserService currentUserService,
        ILogger<GrantAuthorizationHandler> logger)
    {
        _grantService = grantService;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        GrantRequirement requirement)
    {
        if (!_currentUserService.IsAuthenticated)
        {
            _logger.LogDebug("Grant check failed: user not authenticated");
            return;
        }

        var userId = _currentUserService.UserId;
        var grantKey = requirement.GrantKey;

        // Build scope from current user context
        var scope = new GrantScope(
            ProjectId: _currentUserService.ProjectId,
            AreaId: _currentUserService.AreaId,
            MoleculeId: _currentUserService.MoleculeId,
            CompanyId: _currentUserService.CompanyId,
            JobTypeId: _currentUserService.JobTypeId
        );

        var hasGrant = await _grantService.HasGrantAsync(userId, grantKey, scope);

        if (hasGrant)
        {
            _logger.LogDebug("Grant '{GrantKey}' authorized for user {UserId}", grantKey, userId);
            context.Succeed(requirement);
        }
        else
        {
            _logger.LogDebug("Grant '{GrantKey}' denied for user {UserId}", grantKey, userId);
        }
    }
}
