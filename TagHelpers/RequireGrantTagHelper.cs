using Microsoft.AspNetCore.Razor.TagHelpers;
using ShiftManager.Services;

namespace ShiftManager.TagHelpers;

/// <summary>
/// Tag helper for conditionally rendering content based on user grants.
/// Usage: &lt;require-grant key="AssignAlhutShifts"&gt;...content...&lt;/require-grant&gt;
/// Usage: &lt;require-grant key="EditUsers,ViewReports" mode="any"&gt;...content...&lt;/require-grant&gt;
/// </summary>
[HtmlTargetElement("require-grant")]
public class RequireGrantTagHelper : TagHelper
{
    private readonly IGrantService _grantService;
    private readonly ICurrentUserService _currentUserService;

    public RequireGrantTagHelper(
        IGrantService grantService,
        ICurrentUserService currentUserService)
    {
        _grantService = grantService;
        _currentUserService = currentUserService;
    }

    /// <summary>
    /// Single grant key or comma-separated list of grant keys.
    /// </summary>
    [HtmlAttributeName("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Matching mode: "all" (default) requires all grants, "any" requires at least one.
    /// </summary>
    [HtmlAttributeName("mode")]
    public string Mode { get; set; } = "all";

    /// <summary>
    /// If true, renders content when user does NOT have the grant(s).
    /// </summary>
    [HtmlAttributeName("negate")]
    public bool Negate { get; set; } = false;

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        // Remove the wrapper tag, only render content if authorized
        output.TagName = null;

        if (!_currentUserService.IsAuthenticated)
        {
            if (!Negate)
            {
                output.SuppressOutput();
            }
            return;
        }

        var userId = _currentUserService.UserId;

        // Build scope from current user context
        var scope = new GrantScope(
            ProjectId: _currentUserService.ProjectId,
            AreaId: _currentUserService.AreaId,
            MoleculeId: _currentUserService.MoleculeId,
            CompanyId: _currentUserService.CompanyId
        );

        // Parse grant keys
        var grantKeys = Key.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (grantKeys.Length == 0)
        {
            // No grants specified, suppress output
            output.SuppressOutput();
            return;
        }

        // Check grants based on mode
        bool hasGrants;
        if (Mode.Equals("any", StringComparison.OrdinalIgnoreCase))
        {
            hasGrants = await HasAnyGrantAsync(userId, grantKeys, scope);
        }
        else
        {
            hasGrants = await HasAllGrantsAsync(userId, grantKeys, scope);
        }

        // Apply negate logic
        var shouldRender = Negate ? !hasGrants : hasGrants;

        if (!shouldRender)
        {
            output.SuppressOutput();
        }
    }

    private async Task<bool> HasAnyGrantAsync(int userId, string[] grantKeys, GrantScope scope)
    {
        foreach (var key in grantKeys)
        {
            if (await _grantService.HasGrantAsync(userId, key, scope))
                return true;
        }
        return false;
    }

    private async Task<bool> HasAllGrantsAsync(int userId, string[] grantKeys, GrantScope scope)
    {
        foreach (var key in grantKeys)
        {
            if (!await _grantService.HasGrantAsync(userId, key, scope))
                return false;
        }
        return true;
    }
}
