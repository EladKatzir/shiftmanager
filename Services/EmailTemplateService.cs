using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;

namespace ShiftManager.Services;

public class EmailTemplateService : IEmailTemplateService
{
    private readonly AppDbContext _db;
    private readonly ITenantResolver _tenantResolver;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly ILogger<EmailTemplateService> _logger;

    public EmailTemplateService(
        AppDbContext db,
        ITenantResolver tenantResolver,
        IStringLocalizer<SharedResources> localizer,
        ILogger<EmailTemplateService> logger)
    {
        _db = db;
        _tenantResolver = tenantResolver;
        _localizer = localizer;
        _logger = logger;
    }

    public async Task<List<EmailTemplateCustomization>> GetAllTemplatesAsync()
    {
        var companyId = _tenantResolver.GetCurrentTenantId();
        return await _db.EmailTemplateCustomizations
            .Where(t => t.CompanyId == companyId)
            .OrderBy(t => t.TemplateType)
            .ToListAsync();
    }

    public async Task<EmailTemplateCustomization?> GetTemplateAsync(EmailTemplateType templateType)
    {
        var companyId = _tenantResolver.GetCurrentTenantId();
        return await _db.EmailTemplateCustomizations
            .FirstOrDefaultAsync(t => t.CompanyId == companyId && t.TemplateType == templateType);
    }

    public async Task<EmailTemplateCustomization> SaveTemplateAsync(
        EmailTemplateType templateType,
        string customMessage,
        bool isEnabled,
        int userId)
    {
        var companyId = _tenantResolver.GetCurrentTenantId();

        var existing = await GetTemplateAsync(templateType);

        if (existing != null)
        {
            // Update existing
            existing.CustomMessage = customMessage;
            existing.IsEnabled = isEnabled;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.UpdatedBy = userId;

            await _db.SaveChangesAsync();
            return existing;
        }
        else
        {
            // Create new
            var template = new EmailTemplateCustomization
            {
                CompanyId = companyId,
                TemplateType = templateType,
                CustomMessage = customMessage,
                IsEnabled = isEnabled,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CreatedBy = userId
            };

            _db.EmailTemplateCustomizations.Add(template);
            await _db.SaveChangesAsync();

            return template;
        }
    }

    public async Task<string?> GetCustomMessageAsync(EmailTemplateType templateType)
    {
        var template = await GetTemplateAsync(templateType);

        if (template != null && template.IsEnabled && !string.IsNullOrWhiteSpace(template.CustomMessage))
        {
            return template.CustomMessage;
        }

        return null;
    }

    public string ReplaceVariables(string message, Dictionary<string, string> variables)
    {
        var result = message;

        foreach (var variable in variables)
        {
            var placeholder = $"{{{variable.Key}}}";
            result = result.Replace(placeholder, variable.Value);
        }

        return result;
    }

    public List<string> GetAvailableVariables(EmailTemplateType templateType)
    {
        return templateType switch
        {
            EmailTemplateType.ShiftAssigned => new List<string> { "EmployeeName", "ShiftType", "Date", "StartTime", "EndTime" },
            EmailTemplateType.ShiftChanged => new List<string> { "EmployeeName", "ShiftType", "Date", "StartTime", "EndTime", "ChangeDescription" },
            EmailTemplateType.ShiftDeleted => new List<string> { "EmployeeName", "ShiftType", "Date", "StartTime", "EndTime" },
            EmailTemplateType.ChoreAssigned => new List<string> { "EmployeeName", "ChoreTitle", "Date" },
            EmailTemplateType.ChoreCanceled => new List<string> { "EmployeeName", "ChoreTitle", "Date" },
            EmailTemplateType.TimeOffApproved => new List<string> { "EmployeeName", "StartDate", "EndDate" },
            EmailTemplateType.TimeOffDeclined => new List<string> { "EmployeeName", "StartDate", "EndDate" },
            EmailTemplateType.TimeOffDeleted => new List<string> { "EmployeeName", "StartDate", "EndDate" },
            EmailTemplateType.SwapRequestApproved => new List<string> { "EmployeeName", "ShiftInfo" },
            EmailTemplateType.SwapRequestDeclined => new List<string> { "EmployeeName", "ShiftInfo" },
            EmailTemplateType.OnDutyAssigned => new List<string> { "EmployeeName", "OnDutyType", "Date" },
            EmailTemplateType.OnDutyCanceled => new List<string> { "EmployeeName", "OnDutyType", "Date" },
            EmailTemplateType.AccessRequestSubmitted => new List<string> { "OwnerName", "RequesterName", "RequesterEmail", "CompanyName" },
            EmailTemplateType.AccountApproved => new List<string> { "UserName", "CompanyName", "AssignedRole" },
            _ => new List<string>()
        };
    }

    public string GetDefaultMessage(EmailTemplateType templateType)
    {
        return templateType switch
        {
            EmailTemplateType.ShiftAssigned => _localizer["Email_ShiftAssignedBody"],
            EmailTemplateType.ShiftChanged => _localizer["Email_ShiftChangedBody"],
            EmailTemplateType.ShiftDeleted => _localizer["Email_ShiftDeletedBody"],
            EmailTemplateType.ChoreAssigned => _localizer["Email_ChoreAssignedBody"],
            EmailTemplateType.ChoreCanceled => _localizer["Email_ChoreCanceledBody"],
            EmailTemplateType.TimeOffApproved => _localizer["Email_TimeOffApprovedBody"],
            EmailTemplateType.TimeOffDeclined => _localizer["Email_TimeOffDeclinedBody"],
            EmailTemplateType.TimeOffDeleted => _localizer["Email_TimeOffDeletedBody"],
            EmailTemplateType.SwapRequestApproved => _localizer["Email_SwapRequestApprovedBody"],
            EmailTemplateType.SwapRequestDeclined => _localizer["Email_SwapRequestDeclinedBody"],
            EmailTemplateType.OnDutyAssigned => _localizer["Email_OnDutyAssignedBody"],
            EmailTemplateType.OnDutyCanceled => _localizer["Email_OnDutyCanceledBody"],
            EmailTemplateType.AccessRequestSubmitted => _localizer["Email_AccessRequestSubmittedBody"],
            EmailTemplateType.AccountApproved => _localizer["Email_AccountApprovedBody"],
            _ => ""
        };
    }
}
