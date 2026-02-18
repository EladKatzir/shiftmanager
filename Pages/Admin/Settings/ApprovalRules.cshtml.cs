using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Pages.Admin.Settings;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:SystemConfiguration policy;
// rule CRUD operations are scoped by CompanyId from user claims; orphaned rule detection is company-scoped
[Authorize(Policy = "Grant:SystemConfiguration")]
public class ApprovalRulesModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly IVacationApprovalService _vacationApprovalService;
    private readonly ILogger<ApprovalRulesModel> _logger;
    private readonly IJobTypeService _jobTypeService;

    public ApprovalRulesModel(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext db,
        IVacationApprovalService vacationApprovalService,
        ILogger<ApprovalRulesModel> logger,
        IJobTypeService jobTypeService) : base(localizer)
    {
        _db = db;
        _vacationApprovalService = vacationApprovalService;
        _logger = logger;
        _jobTypeService = jobTypeService;
    }

    public List<VacationApprovalRule> Rules { get; set; } = new();
    public List<OrphanedApprovalRuleInfo> OrphanedRules { get; set; } = new();
    public List<JobTypeOption> AvailableJobTypes { get; set; } = new();
    public List<UserOption> AvailableApprovers { get; set; } = new();

    [BindProperty]
    public RuleForm Form { get; set; } = new();

    public record JobTypeOption(int Id, string Name);
    public record UserOption(int Id, string DisplayName);

    public async Task OnGetAsync()
    {
        if (TempData["SuccessMessage"] is string successMsg) Success = successMsg;
        if (TempData["ErrorMessage"] is string errorMsg) Error = errorMsg;

        var companyId = GetCompanyId();
        if (companyId == null) return;

        await LoadDataAsync(companyId.Value);
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        var companyId = GetCompanyId();
        if (companyId == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_NotAuthenticated"].Value;
            return RedirectToPage();
        }

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            TempData["ErrorMessage"] = _localizer["Error_NotAuthenticated"].Value;
            return RedirectToPage();
        }

        var rule = new VacationApprovalRule
        {
            CompanyId = companyId.Value,
            JobTypeId = Form.JobTypeId > 0 ? Form.JobTypeId : null,
            ApproverUserId = Form.ApproverUserId > 0 ? Form.ApproverUserId : null,
            ApproverGrantKey = string.IsNullOrWhiteSpace(Form.ApproverGrantKey) ? "ApproveVacations" : Form.ApproverGrantKey,
            MaxAutoApproveDays = Form.MaxAutoApproveDays,
            RequiresSecondApproval = Form.RequiresSecondApproval,
            ExtendedLeaveDaysThreshold = Form.ExtendedLeaveDaysThreshold,
            SecondApproverGrantKey = string.IsNullOrWhiteSpace(Form.SecondApproverGrantKey) ? null : Form.SecondApproverGrantKey,
            Priority = Form.Priority,
            IsActive = true,
            CreatedBy = userId
        };

        await _vacationApprovalService.CreateRuleAsync(rule);
        _logger.LogInformation("Approval rule {RuleId} created by user {UserId} for company {CompanyId}", rule.Id, userId, companyId.Value);
        TempData["SuccessMessage"] = _localizer["Success_RuleCreated"].Value;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateAsync()
    {
        var companyId = GetCompanyId();
        if (companyId == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_NotAuthenticated"].Value;
            return RedirectToPage();
        }

        if (Form.Id <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidRule"].Value;
            return RedirectToPage();
        }

        var rule = new VacationApprovalRule
        {
            Id = Form.Id,
            CompanyId = companyId.Value,
            JobTypeId = Form.JobTypeId > 0 ? Form.JobTypeId : null,
            ApproverUserId = Form.ApproverUserId > 0 ? Form.ApproverUserId : null,
            ApproverGrantKey = string.IsNullOrWhiteSpace(Form.ApproverGrantKey) ? "ApproveVacations" : Form.ApproverGrantKey,
            MaxAutoApproveDays = Form.MaxAutoApproveDays,
            RequiresSecondApproval = Form.RequiresSecondApproval,
            ExtendedLeaveDaysThreshold = Form.ExtendedLeaveDaysThreshold,
            SecondApproverGrantKey = string.IsNullOrWhiteSpace(Form.SecondApproverGrantKey) ? null : Form.SecondApproverGrantKey,
            Priority = Form.Priority,
            IsActive = Form.IsActive
        };

        var success = await _vacationApprovalService.UpdateRuleAsync(rule);
        if (success)
        {
            TempData["SuccessMessage"] = _localizer["Success_RuleUpdated"].Value;
        }
        else
        {
            TempData["ErrorMessage"] = _localizer["Error_RuleNotFound"].Value;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int ruleId)
    {
        var companyId = GetCompanyId();
        if (companyId == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_NotAuthenticated"].Value;
            return RedirectToPage();
        }

        var success = await _vacationApprovalService.DeleteRuleAsync(ruleId);
        if (success)
        {
            TempData["SuccessMessage"] = _localizer["Success_RuleDeleted"].Value;
        }
        else
        {
            TempData["ErrorMessage"] = _localizer["Error_RuleNotFound"].Value;
        }

        return RedirectToPage();
    }

    private int? GetCompanyId()
    {
        var companyIdClaim = User.FindFirst("CompanyId")?.Value;
        if (int.TryParse(companyIdClaim, out var companyId))
            return companyId;

        Error = _localizer["Error_NotAuthenticated"];
        return null;
    }

    private async Task LoadDataAsync(int companyId)
    {
        Rules = await _vacationApprovalService.GetRulesForCompanyAsync(companyId);
        OrphanedRules = await _vacationApprovalService.DetectOrphanedRulesAsync(companyId);

        // SECURITY-AUDITED: SAFE — service uses IgnoreQueryFilters internally;
        // only used for dropdown display, scoped to active job types only
        var allJobTypes = await _jobTypeService.GetAllJobTypesAsync();
        AvailableJobTypes = allJobTypes
            .Select(jt => new JobTypeOption(jt.Id, jt.DisplayName))
            .ToList();

        // Load potential approvers (active managers, directors, owners in the company)
        AvailableApprovers = await _db.Users
            .Where(u => u.CompanyId == companyId && u.IsActive &&
                       (u.Role == Models.Support.UserRole.Manager ||
                        u.Role == Models.Support.UserRole.Director ||
                        u.Role == Models.Support.UserRole.Owner))
            .OrderBy(u => u.DisplayName)
            .Select(u => new UserOption(u.Id, u.DisplayName))
            .ToListAsync();
    }

    public class RuleForm
    {
        public int Id { get; set; }
        public int? JobTypeId { get; set; }
        public int? ApproverUserId { get; set; }
        public string ApproverGrantKey { get; set; } = "ApproveVacations";
        public int MaxAutoApproveDays { get; set; }
        public bool RequiresSecondApproval { get; set; }
        public int ExtendedLeaveDaysThreshold { get; set; } = 5;
        public string? SecondApproverGrantKey { get; set; }
        public int Priority { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
