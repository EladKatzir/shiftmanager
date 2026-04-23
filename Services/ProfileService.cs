using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Dto;
using ShiftManager.Models.Support;
using ShiftManager.Resources;

namespace ShiftManager.Services;

/// <summary>
/// Service interface for employee profile management with field-level permissions
/// </summary>
public interface IProfileService
{
    Task<bool> CanEditFieldAsync(int editorUserId, int targetUserId, string fieldName);
    Task<(bool Success, string? Error)> UpdateProfileAsync(int editorUserId, int targetUserId, ProfileUpdateDto dto);
    Task<List<ProfileChangeAudit>> GetProfileHistoryAsync(int userId, int days = 90);
    Task<List<AppUser>> SearchProfilesAsync(string searchTerm, int maxResults = 20);
}

/// <summary>
/// Service for managing employee profiles with field-level permissions and audit logging
/// </summary>
public class ProfileService : IProfileService
{
    private readonly AppDbContext _db;
    private readonly ITenantResolver _tenantResolver;
    private readonly IGrantService _grantService;
    private readonly ILogger<ProfileService> _logger;
    private readonly IStringLocalizer<SharedResources> _localizer;

    // Fields that employees can edit themselves
    private static readonly HashSet<string> EmployeeEditableFields = new()
    {
        nameof(AppUser.DisplayName),
        nameof(AppUser.PreferredName),
        nameof(AppUser.Phone),
        nameof(AppUser.City),
        nameof(AppUser.DateOfBirth),
        nameof(AppUser.Skills),
        nameof(AppUser.Certifications),
        nameof(AppUser.EmergencyContactName),
        nameof(AppUser.EmergencyContactPhone),
        nameof(AppUser.EmergencyContactRelation)
    };

    // Fields that only managers can edit
    private static readonly HashSet<string> ManagerOnlyFields = new()
    {
        nameof(AppUser.Email),
        nameof(AppUser.Department),
        nameof(AppUser.JobTitle),
        nameof(AppUser.HireDate),
        nameof(AppUser.Role),
        nameof(AppUser.IsActive)
    };

    public ProfileService(
        AppDbContext db,
        ITenantResolver tenantResolver,
        IGrantService grantService,
        ILogger<ProfileService> logger,
        IStringLocalizer<SharedResources> localizer)
    {
        _db = db;
        _tenantResolver = tenantResolver;
        _grantService = grantService;
        _logger = logger;
        _localizer = localizer;
    }

    public async Task<bool> CanEditFieldAsync(int editorUserId, int targetUserId, string fieldName)
    {
        // SECURITY-AUDITED: SAFE — bypass tenant filter so cross-tenant editors with
        // EditCompanyUsers grant for the target's company can be authorized; the grant check
        // below is the access boundary.
        var target = await _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == targetUserId);
        if (target == null) return false;

        // ✅ Grant-based: Check if editor has EditCompanyUsers grant for target's company
        var hasEditGrant = await _grantService.HasGrantForCompanyAsync(editorUserId, "EditCompanyUsers", target.CompanyId);
        if (hasEditGrant)
        {
            return true;
        }

        // Employees can only edit their own profile
        if (editorUserId != targetUserId)
        {
            return false;
        }

        // Check if field is in employee-editable list
        return EmployeeEditableFields.Contains(fieldName);
    }

    public async Task<(bool Success, string? Error)> UpdateProfileAsync(
        int editorUserId,
        int targetUserId,
        ProfileUpdateDto dto)
    {
        try
        {
            // SECURITY-AUDITED: SAFE — bypassing the tenant filter ensures the editor (who is
            // the caller) and the target are findable even when the editor acts cross-tenant
            // via the EditCompanyUsers grant. The grant check below is the access boundary.
            var editor = await _db.Users.IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Id == editorUserId);
            if (editor == null)
            {
                return (false, _localizer["EditorUserNotFound"]);
            }

            var targetUser = await _db.Users.IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Id == targetUserId);
            if (targetUser == null)
            {
                return (false, _localizer["TargetUserNotFound"]);
            }

            // ✅ Grant-based: Check if editor has EditCompanyUsers grant for target's company
            var companyId = _tenantResolver.GetCurrentTenantId();
            var hasEditGrant = await _grantService.HasGrantForCompanyAsync(editorUserId, "EditCompanyUsers", targetUser.CompanyId);

            // Verify editor has permission to edit target's company
            if (!hasEditGrant && editorUserId != targetUserId)
            {
                return (false, _localizer["UnauthorizedAccess"]);
            }

            // Editor has manager permissions if they have the EditCompanyUsers grant
            var hasManagerPermissions = hasEditGrant;
            var isEditingSelf = editorUserId == targetUserId;

            // Track changes for audit log
            var changes = new List<ProfileChangeAudit>();
            var timestamp = DateTime.UtcNow;

            // Update Email (Manager/Director/Owner only)
            if (dto.Email != null && dto.Email != targetUser.Email)
            {
                if (!hasManagerPermissions)
                    return (false, _localizer["OnlyManagersCanChangeEmail"]);

                changes.Add(CreateAuditEntry(companyId, targetUserId, editorUserId,
                    nameof(AppUser.Email), targetUser.Email, dto.Email, timestamp));
                targetUser.Email = dto.Email;
            }

            // Update DisplayName (Self or Manager)
            if (dto.DisplayName != null && dto.DisplayName != targetUser.DisplayName)
            {
                if (!isEditingSelf && !hasManagerPermissions)
                    return (false, _localizer["UnauthorizedToChangeDisplayName"]);

                changes.Add(CreateAuditEntry(companyId, targetUserId, editorUserId,
                    nameof(AppUser.DisplayName), targetUser.DisplayName, dto.DisplayName, timestamp));
                targetUser.DisplayName = dto.DisplayName;
            }

            // Update PreferredName (Self or Manager)
            if (dto.PreferredName != targetUser.PreferredName)
            {
                if (!isEditingSelf && !hasManagerPermissions)
                    return (false, _localizer["UnauthorizedToChangePreferredName"]);

                changes.Add(CreateAuditEntry(companyId, targetUserId, editorUserId,
                    nameof(AppUser.PreferredName), targetUser.PreferredName, dto.PreferredName, timestamp));
                targetUser.PreferredName = dto.PreferredName;
            }

            // Update Phone (Self or Manager)
            if (dto.Phone != targetUser.Phone)
            {
                if (!isEditingSelf && !hasManagerPermissions)
                    return (false, _localizer["UnauthorizedToChangePhone"]);

                changes.Add(CreateAuditEntry(companyId, targetUserId, editorUserId,
                    nameof(AppUser.Phone), targetUser.Phone, dto.Phone, timestamp));
                targetUser.Phone = dto.Phone;
            }

            // Update City (Self or Manager)
            if (dto.City != targetUser.City)
            {
                if (!isEditingSelf && !hasManagerPermissions)
                    return (false, _localizer["UnauthorizedToChangeCity"]);

                changes.Add(CreateAuditEntry(companyId, targetUserId, editorUserId,
                    nameof(AppUser.City), targetUser.City, dto.City, timestamp));
                targetUser.City = dto.City;
            }

            // Update DateOfBirth (Self or Manager)
            if (dto.DateOfBirth != targetUser.DateOfBirth)
            {
                if (!isEditingSelf && !hasManagerPermissions)
                    return (false, _localizer["UnauthorizedToChangeDateOfBirth"]);

                changes.Add(CreateAuditEntry(companyId, targetUserId, editorUserId,
                    nameof(AppUser.DateOfBirth),
                    targetUser.DateOfBirth?.ToString("yyyy-MM-dd"),
                    dto.DateOfBirth?.ToString("yyyy-MM-dd"),
                    timestamp));
                targetUser.DateOfBirth = dto.DateOfBirth;
            }

            // Update Department (Manager/Director/Owner only)
            if (dto.Department != targetUser.LegacyDepartment)
            {
                if (!hasManagerPermissions)
                    return (false, _localizer["OnlyManagersCanChangeDepartment"]);

                changes.Add(CreateAuditEntry(companyId, targetUserId, editorUserId,
                    nameof(AppUser.LegacyDepartment), targetUser.LegacyDepartment, dto.Department, timestamp));
                targetUser.LegacyDepartment = dto.Department;
            }

            // Update JobTitle (Manager/Director/Owner only)
            if (dto.JobTitle != targetUser.JobTitle)
            {
                if (!hasManagerPermissions)
                    return (false, _localizer["OnlyManagersCanChangeJobTitle"]);

                changes.Add(CreateAuditEntry(companyId, targetUserId, editorUserId,
                    nameof(AppUser.JobTitle), targetUser.JobTitle, dto.JobTitle, timestamp));
                targetUser.JobTitle = dto.JobTitle;
            }

            // Update HireDate (Manager/Director/Owner only)
            if (dto.HireDate != targetUser.HireDate)
            {
                if (!hasManagerPermissions)
                    return (false, _localizer["OnlyManagersCanChangeHireDate"]);

                changes.Add(CreateAuditEntry(companyId, targetUserId, editorUserId,
                    nameof(AppUser.HireDate),
                    targetUser.HireDate?.ToString("yyyy-MM-dd"),
                    dto.HireDate?.ToString("yyyy-MM-dd"),
                    timestamp));
                targetUser.HireDate = dto.HireDate;
            }

            // Update Skills (Self or Manager)
            if (dto.Skills != targetUser.Skills)
            {
                if (!isEditingSelf && !hasManagerPermissions)
                    return (false, _localizer["UnauthorizedToChangeSkills"]);

                changes.Add(CreateAuditEntry(companyId, targetUserId, editorUserId,
                    nameof(AppUser.Skills), targetUser.Skills, dto.Skills, timestamp));
                targetUser.Skills = dto.Skills;
            }

            // Update Certifications (Self or Manager)
            if (dto.Certifications != targetUser.Certifications)
            {
                if (!isEditingSelf && !hasManagerPermissions)
                    return (false, _localizer["UnauthorizedToChangeCertifications"]);

                changes.Add(CreateAuditEntry(companyId, targetUserId, editorUserId,
                    nameof(AppUser.Certifications), targetUser.Certifications, dto.Certifications, timestamp));
                targetUser.Certifications = dto.Certifications;
            }

            // Update Emergency Contact Name (Self or Manager)
            if (dto.EmergencyContactName != targetUser.EmergencyContactName)
            {
                if (!isEditingSelf && !hasManagerPermissions)
                    return (false, _localizer["UnauthorizedToChangeEmergencyContact"]);

                changes.Add(CreateAuditEntry(companyId, targetUserId, editorUserId,
                    nameof(AppUser.EmergencyContactName),
                    targetUser.EmergencyContactName,
                    dto.EmergencyContactName,
                    timestamp));
                targetUser.EmergencyContactName = dto.EmergencyContactName;
            }

            // Update Emergency Contact Phone (Self or Manager)
            if (dto.EmergencyContactPhone != targetUser.EmergencyContactPhone)
            {
                if (!isEditingSelf && !hasManagerPermissions)
                    return (false, _localizer["UnauthorizedToChangeEmergencyContact"]);

                changes.Add(CreateAuditEntry(companyId, targetUserId, editorUserId,
                    nameof(AppUser.EmergencyContactPhone),
                    targetUser.EmergencyContactPhone,
                    dto.EmergencyContactPhone,
                    timestamp));
                targetUser.EmergencyContactPhone = dto.EmergencyContactPhone;
            }

            // Update Emergency Contact Relation (Self or Manager)
            if (dto.EmergencyContactRelation != targetUser.EmergencyContactRelation)
            {
                if (!isEditingSelf && !hasManagerPermissions)
                    return (false, _localizer["UnauthorizedToChangeEmergencyContact"]);

                changes.Add(CreateAuditEntry(companyId, targetUserId, editorUserId,
                    nameof(AppUser.EmergencyContactRelation),
                    targetUser.EmergencyContactRelation,
                    dto.EmergencyContactRelation,
                    timestamp));
                targetUser.EmergencyContactRelation = dto.EmergencyContactRelation;
            }

            // Update Role (Manager/Director/Owner only)
            if (dto.Role.HasValue && dto.Role.Value != targetUser.Role)
            {
                if (!hasManagerPermissions)
                    return (false, _localizer["OnlyManagersCanChangeUserRoles"]);

                changes.Add(CreateAuditEntry(companyId, targetUserId, editorUserId,
                    nameof(AppUser.Role), targetUser.Role.ToString(), dto.Role.Value.ToString(), timestamp));
                targetUser.Role = dto.Role.Value;
            }

            // Update IsActive (Manager/Director/Owner only)
            if (dto.IsActive.HasValue && dto.IsActive.Value != targetUser.IsActive)
            {
                if (!hasManagerPermissions)
                    return (false, _localizer["OnlyManagersCanChangeActiveStatus"]);

                changes.Add(CreateAuditEntry(companyId, targetUserId, editorUserId,
                    nameof(AppUser.IsActive),
                    targetUser.IsActive.ToString(),
                    dto.IsActive.Value.ToString(),
                    timestamp));
                targetUser.IsActive = dto.IsActive.Value;
            }

            // Update metadata
            if (changes.Any())
            {
                targetUser.ProfileLastUpdated = timestamp;
                targetUser.ProfileLastUpdatedBy = editorUserId;

                // Add all audit entries
                await _db.ProfileChangeAudits.AddRangeAsync(changes);

                await _db.SaveChangesAsync();

                _logger.LogInformation(
                    "Profile updated for user {TargetUserId} by {EditorUserId}. {ChangeCount} changes made",
                    targetUserId, editorUserId, changes.Count);
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating profile for user {TargetUserId} by {EditorUserId}",
                targetUserId, editorUserId);
            return (false, _localizer["ErrorUpdatingProfile"]);
        }
    }

    public async Task<List<ProfileChangeAudit>> GetProfileHistoryAsync(int userId, int days = 90)
    {
        var startDate = DateTime.UtcNow.AddDays(-days);

        return await _db.ProfileChangeAudits
            .Where(p => p.TargetUserId == userId && p.Timestamp >= startDate)
            .OrderByDescending(p => p.Timestamp)
            .Include(p => p.ChangedByUser)
            .ToListAsync();
    }

    public async Task<List<AppUser>> SearchProfilesAsync(string searchTerm, int maxResults = 20)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return new List<AppUser>();
        }

        var term = searchTerm.ToLower();

        return await _db.Users
            .Where(u => u.IsActive &&
                (u.DisplayName.ToLower().Contains(term) ||
                 (u.PreferredName != null && u.PreferredName.ToLower().Contains(term)) ||
                 u.Email.ToLower().Contains(term) ||
                 (u.LegacyDepartment != null && u.LegacyDepartment.ToLower().Contains(term)) ||
                 (u.JobTitle != null && u.JobTitle.ToLower().Contains(term))))
            .OrderBy(u => u.DisplayName)
            .Take(maxResults)
            .ToListAsync();
    }

    private ProfileChangeAudit CreateAuditEntry(
        int companyId,
        int targetUserId,
        int changedBy,
        string fieldName,
        string? oldValue,
        string? newValue,
        DateTime timestamp)
    {
        return new ProfileChangeAudit
        {
            CompanyId = companyId,
            TargetUserId = targetUserId,
            ChangedBy = changedBy,
            FieldName = fieldName,
            OldValue = oldValue,
            NewValue = newValue,
            Timestamp = timestamp
        };
    }
}
