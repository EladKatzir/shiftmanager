using Microsoft.Extensions.Localization;
using ShiftManager.Models;
using ShiftManager.Resources;

namespace ShiftManager.Helpers;

/// <summary>
/// Maps role information to the correct localized display name.
/// Supports both string-based (legacy) and RoleTemplate-based lookups.
/// </summary>
public static class RoleDisplayHelper
{
    /// <summary>
    /// Template-based overload. Priority: job-type label → DisplayNameEN/HE → NameKey resx → Key fallback.
    /// </summary>
    public static string GetRoleDisplayName(
        IStringLocalizer<SharedResources> localizer,
        RoleTemplate template,
        string? jobTypeKey = null,
        string? cultureName = null)
    {
        var isHebrew = cultureName?.StartsWith("he") == true
            || System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("he");

        // 1. Check job-type-specific label from RoleTemplateJobTypeLabel
        if (!string.IsNullOrEmpty(jobTypeKey) && template.JobTypeLabels?.Count > 0)
        {
            var label = template.JobTypeLabels.FirstOrDefault(l => l.JobType?.Name == jobTypeKey);
            if (label != null)
            {
                var labelName = isHebrew ? label.DisplayNameHE : label.DisplayNameEN;
                if (!string.IsNullOrEmpty(labelName)) return labelName;
            }
        }

        // 2. Check custom role display name (for custom templates)
        if (!string.IsNullOrEmpty(template.DisplayNameEN) || !string.IsNullOrEmpty(template.DisplayNameHE))
        {
            var displayName = isHebrew ? template.DisplayNameHE : template.DisplayNameEN;
            if (!string.IsNullOrEmpty(displayName)) return displayName;
        }

        // 3. Fall back to resx key (system templates have NameKey like "Role_Employee")
        if (!string.IsNullOrEmpty(template.NameKey))
        {
            var localized = localizer[template.NameKey];
            if (!localized.ResourceNotFound) return localized.Value;
        }

        // 4. Ultimate fallback — Key itself
        return template.Key;
    }

    /// <summary>
    /// String-based overload (backward compatible). Used by ~20 business identity sites.
    /// Accepts either a RoleTemplate key (e.g., "BRDirector") or a UserRole enum name (e.g., "Manager").
    /// Resolution order: job-type-specific → Role_{name} (template resx) → direct key (legacy resx).
    /// </summary>
    public static string GetRoleDisplayName(
        IStringLocalizer<SharedResources> localizer,
        string roleName,
        string? jobTypeKey)
    {
        if (!string.IsNullOrEmpty(jobTypeKey) &&
            (roleName == "Manager" || roleName == "Director"))
        {
            var specificKey = $"{roleName}_{jobTypeKey}";
            var specificValue = localizer[specificKey];
            if (!specificValue.ResourceNotFound)
                return specificValue.Value;
        }

        // Try template-style resx key first (e.g., "BRDirector" → "Role_BRDirector" → "קב"ר")
        var templateResxKey = $"Role_{roleName}";
        var templateValue = localizer[templateResxKey];
        if (!templateValue.ResourceNotFound)
            return templateValue.Value;

        // Fall back to direct key (legacy: "Manager" → "מנהל", "Employee" → "חייל")
        return localizer[roleName].Value;
    }
}
