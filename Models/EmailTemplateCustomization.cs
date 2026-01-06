using ShiftManager.Models.Support;
using System.ComponentModel.DataAnnotations;

namespace ShiftManager.Models;

/// <summary>
/// Stores customized email message templates per company.
/// Allows owners to personalize automated email content using variable placeholders.
/// </summary>
public class EmailTemplateCustomization : IBelongsToCompany
{
    public int Id { get; set; }

    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>
    /// The type of email template this customization applies to.
    /// </summary>
    public EmailTemplateType TemplateType { get; set; }

    /// <summary>
    /// Custom message body with variable placeholders like {EmployeeName}, {Date}, etc.
    /// Maximum 2000 characters.
    /// </summary>
    [MaxLength(2000)]
    public string CustomMessage { get; set; } = string.Empty;

    /// <summary>
    /// Whether this custom template is enabled and should be used.
    /// If false, the default system template is used.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// When this template was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this template was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// User ID who created this template.
    /// </summary>
    public int CreatedBy { get; set; }

    /// <summary>
    /// User ID who last updated this template.
    /// </summary>
    public int? UpdatedBy { get; set; }
}
