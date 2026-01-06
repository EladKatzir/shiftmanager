using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

public interface IEmailTemplateService
{
    /// <summary>
    /// Get all email template customizations for the current company.
    /// </summary>
    Task<List<EmailTemplateCustomization>> GetAllTemplatesAsync();

    /// <summary>
    /// Get a specific email template customization for the current company.
    /// Returns null if not found.
    /// </summary>
    Task<EmailTemplateCustomization?> GetTemplateAsync(EmailTemplateType templateType);

    /// <summary>
    /// Save or update an email template customization.
    /// </summary>
    Task<EmailTemplateCustomization> SaveTemplateAsync(EmailTemplateType templateType, string customMessage, bool isEnabled, int userId);

    /// <summary>
    /// Get the customized message for a template type, or return null if not enabled/configured.
    /// </summary>
    Task<string?> GetCustomMessageAsync(EmailTemplateType templateType);

    /// <summary>
    /// Replace variable placeholders in a message with actual values.
    /// Example: "{EmployeeName} has been assigned..." becomes "John Doe has been assigned..."
    /// </summary>
    string ReplaceVariables(string message, Dictionary<string, string> variables);

    /// <summary>
    /// Get the list of available variables for a given template type.
    /// </summary>
    List<string> GetAvailableVariables(EmailTemplateType templateType);

    /// <summary>
    /// Get the default message (in localized language) for a given template type.
    /// Used to show owners what the default looks like.
    /// </summary>
    string GetDefaultMessage(EmailTemplateType templateType);
}
