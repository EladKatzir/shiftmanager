using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using System.Security.Claims;
using System.Text.Json;

namespace ShiftManager.Pages.Owner.Hub;

/// <summary>
/// Grant Management UI - Unified page for managing grant types, role templates, and user grants.
/// Supports CanGive delegation with depth limiting.
/// </summary>
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — Owner grant management requires Grant:AdminAccess (all ~134 grants; exact count: _db.GrantTypes.CountAsync(gt => gt.IsActive))
[Authorize(Policy = "Grant:AdminAccess")]
public class GrantsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IGrantService _grantService;
    private readonly ILogger<GrantsModel> _logger;
    private readonly IRoleService _roleService;
    private readonly IConcurrencyService _concurrencyService;
    private readonly IGrantBackfillService _backfillService;

    public GrantsModel(AppDbContext db, IGrantService grantService, ILogger<GrantsModel> logger, IRoleService roleService, IConcurrencyService concurrencyService, IGrantBackfillService backfillService)
    {
        _db = db;
        _grantService = grantService;
        _logger = logger;
        _roleService = roleService;
        _concurrencyService = concurrencyService;
        _backfillService = backfillService;
    }

    // Back-fill Grant Actions handlers (2026-04-15).
    // Preview = dry-run (no writes). Execute = apply missing template AutoGrants.
    public BackfillReport? BackfillPreview { get; set; }
    public SurplusReport? SurplusPreview { get; set; }

    public async Task<IActionResult> OnPostBackfillPreviewAsync(int? roleTemplateId)
    {
        BackfillPreview = await _backfillService.PreviewAsync(roleTemplateId);
        SuccessMessage = $"Dry-run complete. {BackfillPreview.UsersWithMissingGrants} of {BackfillPreview.UsersScanned} users would receive {BackfillPreview.TotalMissingGrantRows} new grant rows.";
        await LoadPageDataAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostSurplusPreviewAsync(int? roleTemplateId)
    {
        SurplusPreview = await _backfillService.PreviewSurplusAsync(roleTemplateId);
        if (SurplusPreview.UsersWithSurplus == 0)
        {
            SuccessMessage = $"No surplus auto-grants found across {SurplusPreview.UsersScanned} users. Templates and user grants are aligned.";
        }
        else
        {
            SuccessMessage = $"Surplus audit: {SurplusPreview.UsersWithSurplus} of {SurplusPreview.UsersScanned} users have {SurplusPreview.TotalSurplusRows} stale auto-grants (template was edited; rows remain). Manual admin grants are excluded — only flagged when no longer in the template.";
        }
        await LoadPageDataAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostBackfillExecuteAsync(int? roleTemplateId)
    {
        var actorId = GetCurrentUserId();
        var result = await _backfillService.ExecuteAsync(roleTemplateId, actorId);

        if (result.UsersFailed > 0)
        {
            ErrorMessage = $"Back-fill completed with {result.UsersFailed} failure(s). Inserted {result.TotalGrantsInserted} grants across {result.UsersUpdated} users (of {result.UsersProcessed} scanned). See logs for details.";
        }
        else
        {
            SuccessMessage = $"Back-fill complete. Inserted {result.TotalGrantsInserted} grants across {result.UsersUpdated} users (of {result.UsersProcessed} scanned).";
        }
        return RedirectToPage();
    }

    private int GetCurrentUserId()
    {
        var claim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        return claim != null && int.TryParse(claim.Value, out var id) ? id : 0;
    }

    private async Task LoadPageDataAsync() => await OnGetAsync();

    // Stats
    public int TotalGrantTypes { get; set; }
    public int TotalRoleTemplates { get; set; }
    public int TotalGrants { get; set; }
    public int UsersWithGrants { get; set; }

    // Messages for Grant Actions tab
    [TempData]
    public string? SuccessMessage { get; set; }
    [TempData]
    public string? ErrorMessage { get; set; }

    // Grant Types by Category
    public Dictionary<GrantCategory, List<GrantTypeViewModel>> GrantTypesByCategory { get; set; } = new();

    // Role Templates
    public List<RoleTemplateViewModel> RoleTemplates { get; set; } = new();

    // All Grant Types (for role template editor)
    public List<GrantTypeViewModel> AllGrantTypes { get; set; } = new();

    // Hierarchy data for scope selection
    public List<ProjectViewModel> Projects { get; set; } = new();

    public async Task OnGetAsync()
    {
        try
        {
            // Load stats
            TotalGrantTypes = await _db.GrantTypes.CountAsync(gt => gt.IsActive);
            TotalRoleTemplates = await _roleService.GetActiveRoleTemplateCountAsync();
            TotalGrants = await _db.Grants.IgnoreQueryFilters().CountAsync();
            UsersWithGrants = await _db.Grants.IgnoreQueryFilters().Select(g => g.UserId).Distinct().CountAsync();

            // Load grant types by category
            var grantTypes = await _db.GrantTypes
                .Where(gt => gt.IsActive)
                .OrderBy(gt => gt.Category)
                .ThenBy(gt => gt.Key)
                .Select(gt => new GrantTypeViewModel
                {
                    Id = gt.Id,
                    Key = gt.Key,
                    NameKey = gt.NameKey,
                    DescriptionKey = gt.DescriptionKey,
                    Category = gt.Category,
                    DefaultScope = gt.DefaultScope,
                    IsSystem = gt.IsSystem,
                    UsageCount = gt.Grants.Count
                })
                .ToListAsync();

            // Group by category
            GrantTypesByCategory = grantTypes
                .GroupBy(gt => gt.Category)
                .ToDictionary(g => g.Key, g => g.ToList());

            AllGrantTypes = grantTypes;

            // Load active role templates with auto-grants via service
            var activeTemplates = await _roleService.GetActiveRoleTemplatesWithAutoGrantsAsync();
            RoleTemplates = activeTemplates.Select(rt => new RoleTemplateViewModel
            {
                Id = rt.Id,
                Key = rt.Key,
                NameKey = rt.NameKey,
                DescriptionKey = rt.DescriptionKey,
                ScopeLevel = rt.ScopeLevel,
                IsSystem = rt.IsSystem,
                GrantCount = rt.AutoGrants.Count,
                Grants = rt.AutoGrants.Select(ag => new RoleTemplateGrantViewModel
                {
                    Id = ag.Id,
                    GrantTypeId = ag.GrantTypeId,
                    GrantTypeKey = ag.GrantType.Key,
                    GrantTypeNameKey = ag.GrantType.NameKey,
                    CanOwn = ag.CanOwn,
                    CanGive = ag.CanGive,
                    ScopeMode = ag.ScopeMode
                }).ToList()
            }).ToList();

            // Load hierarchy for scope selection (projects -> areas -> molecules -> companies)
            Projects = await _db.Projects
                .IgnoreQueryFilters()
                .OrderBy(p => p.Name)
                .Select(p => new ProjectViewModel
                {
                    Id = p.Id,
                    Name = p.Name,
                    Areas = p.Areas.OrderBy(a => a.SortOrder).ThenBy(a => a.Name).Select(a => new AreaViewModel
                    {
                        Id = a.Id,
                        Name = a.Name,
                        Molecules = a.Molecules.OrderBy(m => m.SortOrder).ThenBy(m => m.Name).Select(m => new MoleculeViewModel
                        {
                            Id = m.Id,
                            Name = m.Name,
                            Companies = m.Companies.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).Select(c => new CompanyViewModel
                            {
                                Id = c.Id,
                                Name = c.Name
                            }).ToList()
                        }).ToList()
                    }).ToList()
                })
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading grant management data");
        }
    }

    // === Grant Actions POST Handlers ===

    public async Task<IActionResult> OnPostApplyOwnerGrantsAsync()
    {
        try
        {
            var ownerTemplate = await _roleService.GetRoleTemplateByKeyAsync("Owner");
            if (ownerTemplate == null)
            {
                ErrorMessage = "Owner role template not found. Seed role templates first.";
                return RedirectToPage();
            }

            var project = await _db.Projects.IgnoreQueryFilters().FirstOrDefaultAsync();
            if (project == null)
            {
                ErrorMessage = "No project found. Seed the organization first.";
                return RedirectToPage();
            }

            await _grantService.ApplyAutoGrantsAsync(
                userId: 1,
                roleTemplateId: ownerTemplate.Id,
                roleScope: GrantScope.Project(project.Id));

            SuccessMessage = "Owner grants applied to user #1 successfully.";
            _logger.LogInformation("Applied Owner grants to user #1 with project scope {ProjectId}", project.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying owner grants");
            ErrorMessage = "An unexpected error occurred. Please try again.";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostApplyUserManagementGrantsAsync()
    {
        try
        {
            var userMgmtKeys = new[]
            {
                "ViewUsers", "EditUsers", "CreateUsers", "DeactivateUsers",
                "ResetPasswords", "AssignJobTypes", "ViewAllUsers"
            };

            var grantTypes = await _db.GrantTypes
                .Where(gt => userMgmtKeys.Contains(gt.Key))
                .ToListAsync();

            if (grantTypes.Count == 0)
            {
                ErrorMessage = "User management grant types not found. Seed grant types first.";
                return RedirectToPage();
            }

            var existingGrants = await _db.Grants
                .IgnoreQueryFilters()
                .Where(g => g.UserId == 1)
                .Select(g => g.GrantTypeId)
                .ToListAsync();

            var project = await _db.Projects.IgnoreQueryFilters().FirstOrDefaultAsync();
            int added = 0;

            foreach (var gt in grantTypes)
            {
                if (!existingGrants.Contains(gt.Id))
                {
                    _db.Grants.Add(new Grant
                    {
                        UserId = 1,
                        GrantTypeId = gt.Id,
                        ProjectId = project?.Id,
                        CanOwn = true,
                        CanGive = true,
                        IsAutoGrant = false,
                        GrantedAt = DateTime.UtcNow,
                        Notes = "Added via OwnerHub Grant Actions"
                    });
                    added++;
                }
            }

            if (added > 0)
            {
                var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                    () => _db.SaveChangesAsync(), "Grant");
                if (!saveResult.Success)
                {
                    ErrorMessage = "A concurrency conflict occurred. Please try again.";
                    return RedirectToPage();
                }
                SuccessMessage = $"Added {added} user management grants to user #1.";
                _logger.LogInformation("Added {Count} user management grants to user #1", added);
            }
            else
            {
                SuccessMessage = "All user management grants already assigned.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying user management grants");
            ErrorMessage = "An unexpected error occurred. Please try again.";
        }

        return RedirectToPage();
    }

    // === AJAX Handlers ===

    /// <summary>
    /// Search users for grant assignment
    /// </summary>
    public async Task<IActionResult> OnGetSearchUsersAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
        {
            return new JsonResult(new List<object>());
        }

        var users = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.IsActive &&
                (u.DisplayName.Contains(query) || u.Email.Contains(query)))
            .OrderBy(u => u.DisplayName)
            .Take(20)
            .Select(u => new
            {
                u.Id,
                u.DisplayName,
                u.Email,
                u.CompanyId
            })
            .ToListAsync();

        // Get company names for the users
        var companyIds = users.Select(u => u.CompanyId).Distinct().ToList();
        var companies = await _db.Companies
            .IgnoreQueryFilters()
            .Where(c => companyIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name);

        var result = users.Select(u => new
        {
            u.Id,
            u.DisplayName,
            u.Email,
            CompanyName = companies.GetValueOrDefault(u.CompanyId)
        }).ToList();

        return new JsonResult(result);
    }

    /// <summary>
    /// Get user's current grants
    /// </summary>
    public async Task<IActionResult> OnGetUserGrantsAsync(int userId)
    {
        var grants = await _db.Grants
            .IgnoreQueryFilters()
            .Include(g => g.GrantType)
            .Include(g => g.GrantedByUser)
            .Include(g => g.Project)
            .Include(g => g.Area)
            .Include(g => g.Molecule)
            .Include(g => g.Company)
            .Include(g => g.Department)
            .Where(g => g.UserId == userId)
            .OrderBy(g => g.GrantType.Category)
            .ThenBy(g => g.GrantType.Key)
            .Select(g => new
            {
                g.Id,
                g.GrantTypeId,
                GrantTypeKey = g.GrantType.Key,
                GrantTypeNameKey = g.GrantType.NameKey,
                Category = g.GrantType.Category.ToString(),
                g.CanOwn,
                g.CanGive,
                g.IsAutoGrant,
                g.GrantedAt,
                GrantedByName = g.GrantedByUser != null ? g.GrantedByUser.DisplayName : null,
                g.Notes,
                Scope = new
                {
                    ProjectId = g.ProjectId,
                    ProjectName = g.Project != null ? g.Project.Name : null,
                    AreaId = g.AreaId,
                    AreaName = g.Area != null ? g.Area.Name : null,
                    MoleculeId = g.MoleculeId,
                    MoleculeName = g.Molecule != null ? g.Molecule.Name : null,
                    CompanyId = g.CompanyId,
                    CompanyName = g.Company != null ? g.Company.Name : null,
                    DepartmentId = g.DepartmentId,
                    DepartmentName = g.Department != null ? g.Department.Name : null
                }
            })
            .ToListAsync();

        return new JsonResult(grants);
    }

    /// <summary>
    /// Assign a grant to a user
    /// </summary>
    public async Task<IActionResult> OnPostAssignGrantAsync([FromBody] AssignGrantRequest request)
    {
        try
        {
            // Get current user ID
            var currentUserIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int? grantedByUserId = null;
            if (currentUserIdClaim != null && int.TryParse(currentUserIdClaim, out var parsedUserId))
            {
                grantedByUserId = parsedUserId;
            }

            // Create scope
            var scope = new GrantScope(
                ProjectId: request.ProjectId,
                AreaId: request.AreaId,
                MoleculeId: request.MoleculeId,
                DepartmentId: request.DepartmentId,
                CompanyId: request.CompanyId
            );

            // Check if user can grant (has CanGive for this grant type)
            if (grantedByUserId.HasValue)
            {
                var canGrant = await _grantService.CanUserGrantAsync(grantedByUserId.Value, request.GrantTypeId, scope);
                // For now, allow Owner-level users to bypass this check
                var hasAdminAccess = await _grantService.HasGrantAsync(grantedByUserId.Value, "AdminAccess");
                if (!canGrant && !hasAdminAccess)
                {
                    return new JsonResult(new { success = false, error = "You don't have permission to grant this type at this scope" });
                }
            }

            // Create the grant
            var grant = await _grantService.GrantAsync(
                request.UserId,
                request.GrantTypeId,
                scope,
                grantedByUserId,
                request.Notes);

            if (grant == null)
            {
                return new JsonResult(new { success = false, error = "Failed to create grant" });
            }

            // Update CanGive if specified
            if (request.CanGive)
            {
                grant.CanGive = true;
                var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                    () => _db.SaveChangesAsync(), "Grant", grant.Id);
                if (!saveResult.Success)
                    return new JsonResult(new { success = false, error = saveResult.ErrorMessage }) { StatusCode = 409 };
            }

            _logger.LogInformation("Grant assigned: UserId={UserId}, GrantTypeId={GrantTypeId}, GrantedBy={GrantedBy}",
                request.UserId, request.GrantTypeId, grantedByUserId);

            return new JsonResult(new { success = true, grantId = grant.Id });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized access while assigning grant");
            return new JsonResult(new { success = false, error = "You do not have permission to perform this action." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning grant");
            return new JsonResult(new { success = false, error = "An error occurred" });
        }
    }

    /// <summary>
    /// Revoke a grant from a user
    /// </summary>
    public async Task<IActionResult> OnPostRevokeGrantAsync([FromBody] RevokeGrantRequest request)
    {
        try
        {
            var grant = await _db.Grants.FindAsync(request.GrantId);
            if (grant == null)
            {
                return new JsonResult(new { success = false, error = "Grant not found" });
            }

            var result = await _grantService.RevokeAsync(request.GrantId);

            if (!result)
            {
                return new JsonResult(new { success = false, error = "Failed to revoke grant" });
            }

            _logger.LogInformation("Grant revoked: GrantId={GrantId}, UserId={UserId}", request.GrantId, grant.UserId);

            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revoking grant");
            return new JsonResult(new { success = false, error = "An error occurred" });
        }
    }

    /// <summary>
    /// Update CanGive on an existing grant
    /// </summary>
    public async Task<IActionResult> OnPostUpdateGrantDelegationAsync([FromBody] UpdateDelegationRequest request)
    {
        try
        {
            var grant = await _db.Grants.FindAsync(request.GrantId);
            if (grant == null)
            {
                return new JsonResult(new { success = false, error = "Grant not found" });
            }

            grant.CanGive = request.CanGive;
            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "Grant", request.GrantId);
            if (!saveResult.Success)
                return new JsonResult(new { success = false, error = saveResult.ErrorMessage }) { StatusCode = 409 };

            _logger.LogInformation("Grant delegation updated: GrantId={GrantId}, CanGive={CanGive}", request.GrantId, request.CanGive);

            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating grant delegation");
            return new JsonResult(new { success = false, error = "An error occurred" });
        }
    }

    /// <summary>
    /// Get role template details with grants
    /// </summary>
    public async Task<IActionResult> OnGetRoleTemplateAsync(int templateId)
    {
        var template = await _roleService.GetRoleTemplateWithAutoGrantsAsync(templateId);

        if (template == null)
        {
            return new JsonResult(new { success = false, error = "Template not found" });
        }

        var result = new
        {
            template.Id,
            template.Key,
            template.NameKey,
            template.DescriptionKey,
            ScopeLevel = template.ScopeLevel.ToString(),
            template.IsSystem,
            Grants = template.AutoGrants.Select(ag => new
            {
                ag.Id,
                ag.GrantTypeId,
                ag.GrantType.Key,
                ag.GrantType.NameKey,
                Category = ag.GrantType.Category.ToString(),
                ag.CanOwn,
                ag.CanGive,
                ScopeMode = ag.ScopeMode.ToString()
            }).ToList()
        };

        return new JsonResult(result);
    }

    /// <summary>
    /// Add a grant to a role template
    /// </summary>
    public async Task<IActionResult> OnPostAddRoleTemplateGrantAsync([FromBody] AddRoleTemplateGrantRequest request)
    {
        try
        {
            // Check if already exists
            var exists = await _db.RoleTemplateGrants
                .AnyAsync(rtg => rtg.RoleTemplateId == request.RoleTemplateId && rtg.GrantTypeId == request.GrantTypeId);

            if (exists)
            {
                return new JsonResult(new { success = false, error = "Grant already exists on this role template" });
            }

            var grant = new RoleTemplateGrant
            {
                RoleTemplateId = request.RoleTemplateId,
                GrantTypeId = request.GrantTypeId,
                CanOwn = request.CanOwn,
                CanGive = request.CanGive,
                ScopeMode = request.ScopeMode,
                IsOverride = true // Manual addition
            };

            _db.RoleTemplateGrants.Add(grant);
            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "Grant");
            if (!saveResult.Success)
                return new JsonResult(new { success = false, error = saveResult.ErrorMessage }) { StatusCode = 409 };

            _logger.LogInformation("Role template grant added: TemplateId={TemplateId}, GrantTypeId={GrantTypeId}",
                request.RoleTemplateId, request.GrantTypeId);

            return new JsonResult(new { success = true, grantId = grant.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding role template grant");
            return new JsonResult(new { success = false, error = "An error occurred" });
        }
    }

    /// <summary>
    /// Update a role template grant
    /// </summary>
    public async Task<IActionResult> OnPostUpdateRoleTemplateGrantAsync([FromBody] UpdateRoleTemplateGrantRequest request)
    {
        try
        {
            var grant = await _db.RoleTemplateGrants.FindAsync(request.GrantId);
            if (grant == null)
            {
                return new JsonResult(new { success = false, error = "Grant not found" });
            }

            grant.CanOwn = request.CanOwn;
            grant.CanGive = request.CanGive;
            grant.ScopeMode = request.ScopeMode;
            grant.IsOverride = true;

            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "Grant", request.GrantId);
            if (!saveResult.Success)
                return new JsonResult(new { success = false, error = saveResult.ErrorMessage }) { StatusCode = 409 };

            _logger.LogInformation("Role template grant updated: GrantId={GrantId}", request.GrantId);

            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating role template grant");
            return new JsonResult(new { success = false, error = "An error occurred" });
        }
    }

    /// <summary>
    /// Remove a grant from a role template
    /// </summary>
    public async Task<IActionResult> OnPostRemoveRoleTemplateGrantAsync([FromBody] RemoveRoleTemplateGrantRequest request)
    {
        try
        {
            var grant = await _db.RoleTemplateGrants.FindAsync(request.GrantId);
            if (grant == null)
            {
                return new JsonResult(new { success = false, error = "Grant not found" });
            }

            _db.RoleTemplateGrants.Remove(grant);
            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "Grant", request.GrantId);
            if (!saveResult.Success)
                return new JsonResult(new { success = false, error = saveResult.ErrorMessage }) { StatusCode = 409 };

            _logger.LogInformation("Role template grant removed: GrantId={GrantId}", request.GrantId);

            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing role template grant");
            return new JsonResult(new { success = false, error = "An error occurred" });
        }
    }

    /// <summary>
    /// Get which role templates include a specific grant type
    /// </summary>
    public async Task<IActionResult> OnGetGrantTypeUsageAsync(int grantTypeId)
    {
        var roleTemplates = await _db.RoleTemplateGrants
            .Include(rtg => rtg.RoleTemplate)
            .Where(rtg => rtg.GrantTypeId == grantTypeId)
            .Select(rtg => new
            {
                rtg.RoleTemplate.Id,
                rtg.RoleTemplate.Key,
                rtg.RoleTemplate.NameKey,
                rtg.CanOwn,
                rtg.CanGive,
                ScopeMode = rtg.ScopeMode.ToString()
            })
            .ToListAsync();

        var userCount = await _db.Grants
            .IgnoreQueryFilters()
            .Where(g => g.GrantTypeId == grantTypeId)
            .Select(g => g.UserId)
            .Distinct()
            .CountAsync();

        return new JsonResult(new { roleTemplates, userCount });
    }

    // === View Models ===

    public class GrantTypeViewModel
    {
        public int Id { get; set; }
        public string Key { get; set; } = string.Empty;
        public string NameKey { get; set; } = string.Empty;
        public string DescriptionKey { get; set; } = string.Empty;
        public GrantCategory Category { get; set; }
        public GrantScopeLevel DefaultScope { get; set; }
        public bool IsSystem { get; set; }
        public int UsageCount { get; set; }
    }

    public class RoleTemplateViewModel
    {
        public int Id { get; set; }
        public string Key { get; set; } = string.Empty;
        public string NameKey { get; set; } = string.Empty;
        public string DescriptionKey { get; set; } = string.Empty;
        public RoleScopeLevel ScopeLevel { get; set; }
        public bool IsSystem { get; set; }
        public int GrantCount { get; set; }
        public List<RoleTemplateGrantViewModel> Grants { get; set; } = new();
    }

    public class RoleTemplateGrantViewModel
    {
        public int Id { get; set; }
        public int GrantTypeId { get; set; }
        public string GrantTypeKey { get; set; } = string.Empty;
        public string GrantTypeNameKey { get; set; } = string.Empty;
        public bool CanOwn { get; set; }
        public bool CanGive { get; set; }
        public GrantScopeMode ScopeMode { get; set; }
    }

    public class ProjectViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<AreaViewModel> Areas { get; set; } = new();
    }

    public class AreaViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<MoleculeViewModel> Molecules { get; set; } = new();
    }

    public class MoleculeViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<CompanyViewModel> Companies { get; set; } = new();
    }

    public class CompanyViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    // === Request Models ===

    public class AssignGrantRequest
    {
        public int UserId { get; set; }
        public int GrantTypeId { get; set; }
        public int? ProjectId { get; set; }
        public int? AreaId { get; set; }
        public int? MoleculeId { get; set; }
        public int? DepartmentId { get; set; }
        public int? CompanyId { get; set; }
        public bool CanGive { get; set; }
        public string? Notes { get; set; }
    }

    public class RevokeGrantRequest
    {
        public int GrantId { get; set; }
    }

    public class UpdateDelegationRequest
    {
        public int GrantId { get; set; }
        public bool CanGive { get; set; }
    }

    public class AddRoleTemplateGrantRequest
    {
        public int RoleTemplateId { get; set; }
        public int GrantTypeId { get; set; }
        public bool CanOwn { get; set; }
        public bool CanGive { get; set; }
        public GrantScopeMode ScopeMode { get; set; }
    }

    public class UpdateRoleTemplateGrantRequest
    {
        public int GrantId { get; set; }
        public bool CanOwn { get; set; }
        public bool CanGive { get; set; }
        public GrantScopeMode ScopeMode { get; set; }
    }

    public class RemoveRoleTemplateGrantRequest
    {
        public int GrantId { get; set; }
    }
}
