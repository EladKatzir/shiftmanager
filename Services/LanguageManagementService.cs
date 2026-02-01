using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using System.Text.Json;

namespace ShiftManager.Services;

/// <summary>
/// Service for managing company language settings.
/// </summary>
public class LanguageManagementService : ILanguageManagementService
{
    private readonly AppDbContext _db;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<LanguageManagementService> _logger;

    public LanguageManagementService(
        AppDbContext db,
        IAuditLogService auditLogService,
        ILogger<LanguageManagementService> logger)
    {
        _db = db;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<CompanyLanguageSettings> GetLanguageSettingsAsync(int companyId)
    {
        var settings = await _db.CompanyLanguageSettings
            .FirstOrDefaultAsync(s => s.CompanyId == companyId);

        if (settings == null)
        {
            // Return defaults if not configured
            _logger.LogDebug("No language settings found for CompanyId={CompanyId}, returning defaults", companyId);
            return new CompanyLanguageSettings
            {
                CompanyId = companyId,
                DefaultCulture = "en-US",
                AlternateCulture = "he-IL"
            };
        }

        return settings;
    }

    public async Task<CompanyLanguageSettings> SaveLanguageSettingsAsync(int companyId, string defaultCulture, string alternateCulture, int userId)
    {
        // Validate first
        if (!ValidateLanguageSettings(defaultCulture, alternateCulture, out var error))
        {
            throw new ArgumentException(error);
        }

        var existing = await _db.CompanyLanguageSettings
            .FirstOrDefaultAsync(s => s.CompanyId == companyId);

        if (existing != null)
        {
            // Update existing
            var oldDefault = existing.DefaultCulture;
            var oldAlternate = existing.AlternateCulture;

            existing.DefaultCulture = defaultCulture;
            existing.AlternateCulture = alternateCulture;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.UpdatedBy = userId;

            await _db.SaveChangesAsync();

            // Audit log
            await _auditLogService.LogUserActionAsync(
                userId, "LanguageSettingsUpdated", "CompanyLanguageSettings", existing.Id,
                $"Updated language settings for company {companyId}",
                JsonSerializer.Serialize(new
                {
                    companyId,
                    oldDefault,
                    oldAlternate,
                    newDefault = defaultCulture,
                    newAlternate = alternateCulture
                }));

            _logger.LogInformation("Updated language settings: CompanyId={CompanyId}, Default={Default}, Alternate={Alternate}",
                companyId, defaultCulture, alternateCulture);

            return existing;
        }
        else
        {
            // Create new
            var newSettings = new CompanyLanguageSettings
            {
                CompanyId = companyId,
                DefaultCulture = defaultCulture,
                AlternateCulture = alternateCulture,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = userId,
                UpdatedAt = DateTime.UtcNow,
                UpdatedBy = userId
            };

            _db.CompanyLanguageSettings.Add(newSettings);
            await _db.SaveChangesAsync();

            // Audit log
            await _auditLogService.LogUserActionAsync(
                userId, "LanguageSettingsCreated", "CompanyLanguageSettings", newSettings.Id,
                $"Created language settings for company {companyId}",
                JsonSerializer.Serialize(new
                {
                    companyId,
                    defaultCulture,
                    alternateCulture
                }));

            _logger.LogInformation("Created language settings: CompanyId={CompanyId}, Default={Default}, Alternate={Alternate}",
                companyId, defaultCulture, alternateCulture);

            return newSettings;
        }
    }

    public bool ValidateLanguageSettings(string defaultCulture, string alternateCulture, out string? error)
    {
        var allowedCultures = new[] { "en-US", "he-IL" };

        if (!allowedCultures.Contains(defaultCulture))
        {
            error = $"Invalid default culture '{defaultCulture}'. Must be 'en-US' or 'he-IL'.";
            return false;
        }

        if (!allowedCultures.Contains(alternateCulture))
        {
            error = $"Invalid alternate culture '{alternateCulture}'. Must be 'en-US' or 'he-IL'.";
            return false;
        }

        if (defaultCulture == alternateCulture)
        {
            error = "Default and alternate cultures must be different.";
            return false;
        }

        error = null;
        return true;
    }
}
