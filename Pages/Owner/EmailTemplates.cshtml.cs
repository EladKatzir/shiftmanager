using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace ShiftManager.Pages.Owner;

[Authorize(Policy = "IsAdmin")]
public class EmailTemplatesModel : LocalizedPageModel
{
    private readonly IEmailTemplateService _templateService;
    private readonly ILogger<EmailTemplatesModel> _logger;

    public EmailTemplatesModel(
        IStringLocalizer<SharedResources> localizer,
        IEmailTemplateService templateService,
        ILogger<EmailTemplatesModel> logger)
        : base(localizer)
    {
        _templateService = templateService;
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
            Error = _localizer["Error_InvalidInput"];
            await LoadTemplatesAsync();
            return Page();
        }

        // Validate message length
        if (CustomMessage.Length > 2000)
        {
            Error = _localizer["Error_EmailTemplate_MessageTooLong"];
            await LoadTemplatesAsync();
            return Page();
        }

        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
            {
                Error = "Invalid user claim";
                await LoadTemplatesAsync();
                return Page();
            }

            await _templateService.SaveTemplateAsync(
                EditingTemplateType,
                CustomMessage,
                IsEnabled,
                userId);

            TempData["SuccessMessage"] = _localizer["Success_EmailTemplateSaved"];
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving email template {TemplateType}", EditingTemplateType);
            Error = _localizer["Error_SavingEmailTemplate"];
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
                Error = "Invalid user claim";
                await LoadTemplatesAsync();
                return Page();
            }

            // Save with empty message and disabled to effectively reset
            await _templateService.SaveTemplateAsync(
                templateType,
                string.Empty,
                false,
                userId);

            TempData["SuccessMessage"] = _localizer["Success_EmailTemplateReset"];
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting email template {TemplateType}", templateType);
            Error = _localizer["Error_ResettingEmailTemplate"];
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
