using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace ShiftManager.Pages.Owner;

[Authorize(Policy = "Grant:AdminAccess")]
public class EmailTemplatesModel : LocalizedPageModel
{
    private readonly IEmailTemplateService _templateService;
    private readonly AppDbContext _db;
    private readonly ILogger<EmailTemplatesModel> _logger;

    public EmailTemplatesModel(
        IStringLocalizer<SharedResources> localizer,
        IEmailTemplateService templateService,
        AppDbContext db,
        ILogger<EmailTemplatesModel> logger)
        : base(localizer)
    {
        _templateService = templateService;
        _db = db;
        _logger = logger;
    }

    public class TemplateViewModel
    {
        public EmailTemplateType Type { get; set; }
        public string CustomMessage { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public List<string> AvailableVariables { get; set; } = new();
        public string DefaultMessage { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }

    public List<TemplateViewModel> Templates { get; set; } = new();

    [BindProperty]
    public EmailTemplateType EditingTemplateType { get; set; }

    [BindProperty, MaxLength(2000)]
    public string CustomMessage { get; set; } = string.Empty;

    [BindProperty]
    public bool IsEnabled { get; set; }

    public async Task OnGetAsync()
    {
        await LoadTemplatesAsync();
    }

    private async Task LoadTemplatesAsync()
    {
        var existingTemplates = await _templateService.GetAllTemplatesAsync();

        Templates = new List<TemplateViewModel>();

        foreach (EmailTemplateType templateType in Enum.GetValues(typeof(EmailTemplateType)))
        {
            var existing = existingTemplates.FirstOrDefault(t => t.TemplateType == templateType);

            Templates.Add(new TemplateViewModel
            {
                Type = templateType,
                CustomMessage = existing?.CustomMessage ?? string.Empty,
                IsEnabled = existing?.IsEnabled ?? false,
                AvailableVariables = _templateService.GetAvailableVariables(templateType),
                DefaultMessage = _templateService.GetDefaultMessage(templateType),
                DisplayName = GetTemplateDisplayName(templateType)
            });
        }
    }

    public async Task<IActionResult> OnPostSaveTemplateAsync()
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidInput"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            await LoadTemplatesAsync();
            return Page();
        }

        // Validate message length
        if (CustomMessage.Length > 2000)
        {
            TempData["ErrorMessage"] = _localizer["Error_EmailTemplate_MessageTooLong"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            await LoadTemplatesAsync();
            return Page();
        }

        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
            {
                TempData["ErrorMessage"] = "Invalid user claim"; TempData["ErrorId"] = HttpContext.TraceIdentifier;
                await LoadTemplatesAsync();
                return Page();
            }

            await _templateService.SaveTemplateAsync(
                EditingTemplateType,
                CustomMessage,
                IsEnabled,
                userId);

            TempData["SuccessMessage"] = _localizer["Success_EmailTemplateSaved"].Value;
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving email template {TemplateType}", EditingTemplateType);
            TempData["ErrorMessage"] = _localizer["Error_SavingEmailTemplate"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            await LoadTemplatesAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostResetTemplateAsync(EmailTemplateType templateType)
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
            {
                TempData["ErrorMessage"] = "Invalid user claim"; TempData["ErrorId"] = HttpContext.TraceIdentifier;
                await LoadTemplatesAsync();
                return Page();
            }

            // Save with empty message and disabled to effectively reset
            await _templateService.SaveTemplateAsync(
                templateType,
                string.Empty,
                false,
                userId);

            TempData["SuccessMessage"] = _localizer["Success_EmailTemplateReset"].Value;
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting email template {TemplateType}", templateType);
            TempData["ErrorMessage"] = _localizer["Error_ResettingEmailTemplate"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            await LoadTemplatesAsync();
            return Page();
        }
    }

    /// <summary>
    /// Apply a template to ALL companies at once (global base template).
    /// Creates or updates the template for each company with the same content.
    /// </summary>
    public async Task<IActionResult> OnPostApplyToAllCompaniesAsync()
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidInput"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            await LoadTemplatesAsync();
            return Page();
        }

        if (CustomMessage.Length > 2000)
        {
            TempData["ErrorMessage"] = _localizer["Error_EmailTemplate_MessageTooLong"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            await LoadTemplatesAsync();
            return Page();
        }

        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
            {
                TempData["ErrorMessage"] = "Invalid user claim"; TempData["ErrorId"] = HttpContext.TraceIdentifier;
                await LoadTemplatesAsync();
                return Page();
            }

            // Get all company IDs
            // SECURITY-AUDITED: IgnoreQueryFilters SAFE — Owner-only page with AdminAccess grant
            var allCompanyIds = await _db.Companies
                .IgnoreQueryFilters()
                .Select(c => c.Id)
                .ToListAsync();

            var now = DateTime.UtcNow;
            var updatedCount = 0;

            foreach (var companyId in allCompanyIds)
            {
                var existing = await _db.EmailTemplateCustomizations
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(t => t.CompanyId == companyId && t.TemplateType == EditingTemplateType);

                if (existing != null)
                {
                    existing.CustomMessage = CustomMessage;
                    existing.IsEnabled = IsEnabled;
                    existing.UpdatedAt = now;
                    existing.UpdatedBy = userId;
                }
                else
                {
                    _db.EmailTemplateCustomizations.Add(new EmailTemplateCustomization
                    {
                        CompanyId = companyId,
                        TemplateType = EditingTemplateType,
                        CustomMessage = CustomMessage,
                        IsEnabled = IsEnabled,
                        CreatedAt = now,
                        UpdatedAt = now,
                        CreatedBy = userId
                    });
                }
                updatedCount++;
            }

            await _db.SaveChangesAsync();

            _logger.LogInformation("Applied email template {TemplateType} to {Count} companies by user {UserId}",
                EditingTemplateType, updatedCount, userId);

            TempData["SuccessMessage"] = string.Format(
                _localizer["Success_EmailTemplateAppliedToAll"].Value,
                updatedCount);
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying email template {TemplateType} to all companies", EditingTemplateType);
            TempData["ErrorMessage"] = _localizer["Error_SavingEmailTemplate"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            await LoadTemplatesAsync();
            return Page();
        }
    }

    private string GetTemplateDisplayName(EmailTemplateType type)
    {
        return type switch
        {
            EmailTemplateType.ShiftAssigned => _localizer["EmailTemplate_ShiftAssigned"],
            EmailTemplateType.ShiftChanged => _localizer["EmailTemplate_ShiftChanged"],
            EmailTemplateType.ShiftDeleted => _localizer["EmailTemplate_ShiftDeleted"],
            EmailTemplateType.ChoreAssigned => _localizer["EmailTemplate_ChoreAssigned"],
            EmailTemplateType.ChoreCanceled => _localizer["EmailTemplate_ChoreCanceled"],
            EmailTemplateType.TimeOffApproved => _localizer["EmailTemplate_TimeOffApproved"],
            EmailTemplateType.TimeOffDeclined => _localizer["EmailTemplate_TimeOffDeclined"],
            EmailTemplateType.TimeOffDeleted => _localizer["EmailTemplate_TimeOffDeleted"],
            EmailTemplateType.SwapRequestApproved => _localizer["EmailTemplate_SwapRequestApproved"],
            EmailTemplateType.SwapRequestDeclined => _localizer["EmailTemplate_SwapRequestDeclined"],
            EmailTemplateType.OnDutyAssigned => _localizer["EmailTemplate_OnDutyAssigned"],
            EmailTemplateType.OnDutyCanceled => _localizer["EmailTemplate_OnDutyCanceled"],
            EmailTemplateType.AccessRequestSubmitted => _localizer["EmailTemplate_AccessRequestSubmitted"],
            EmailTemplateType.AccountApproved => _localizer["EmailTemplate_AccountApproved"],
            _ => type.ToString()
        };
    }
}
