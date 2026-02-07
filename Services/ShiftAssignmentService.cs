using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Resources;

namespace ShiftManager.Services;

public class ShiftAssignmentService : IShiftAssignmentService
{
    private readonly AppDbContext _db;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly ILogger<ShiftAssignmentService> _logger;
    private readonly IHierarchySettingsService _hierarchySettingsService;

    public ShiftAssignmentService(
        AppDbContext db,
        IStringLocalizer<SharedResources> localizer,
        ILogger<ShiftAssignmentService> logger,
        IHierarchySettingsService hierarchySettingsService)
    {
        _db = db;
        _localizer = localizer;
        _logger = logger;
        _hierarchySettingsService = hierarchySettingsService;
    }

    public async Task<List<EligibleUserDto>> GetEligibleUsersForShiftAsync(int shiftInstanceId)
    {
        var shiftInstance = await _db.ShiftInstances
            .Include(si => si.ShiftType)
            .FirstOrDefaultAsync(si => si.Id == shiftInstanceId);

        if (shiftInstance == null)
            return new List<EligibleUserDto>();

        return await GetEligibleUsersForShiftTypeAsync(
            shiftInstance.ShiftTypeId,
            shiftInstance.ShiftType.JobTypeId,
            shiftInstance.ShiftType.ShiftGroupingId);
    }

    public async Task<List<EligibleUserDto>> GetEligibleUsersForShiftTypeAsync(
        int shiftTypeId,
        int? jobTypeId = null,
        int? shiftGroupingId = null)
    {
        var shiftType = await _db.ShiftTypes
            .Include(st => st.Molecule)
            .Include(st => st.ShiftGrouping)
                .ThenInclude(sg => sg!.Companies)
            .FirstOrDefaultAsync(st => st.Id == shiftTypeId);

        if (shiftType == null)
            return new List<EligibleUserDto>();

        // Determine effective grouping and job type
        var effectiveGroupingId = shiftGroupingId ?? shiftType.ShiftGroupingId;
        var effectiveJobTypeId = jobTypeId ?? shiftType.JobTypeId;

        // Determine which companies to include
        List<int> companyIds;

        if (effectiveGroupingId.HasValue)
        {
            // ShiftGrouping specified: get all companies in the grouping (cross-company query)
            companyIds = await _db.ShiftGroupingCompanies
                .Where(sgc => sgc.ShiftGroupingId == effectiveGroupingId.Value)
                .Select(sgc => sgc.CompanyId)
                .ToListAsync();

            // Fall back to shift type's company if grouping has no companies
            if (!companyIds.Any())
            {
                companyIds = new List<int> { shiftType.CompanyId };
            }
        }
        else
        {
            // No grouping: use only the shift type's company
            companyIds = new List<int> { shiftType.CompanyId };
        }

        // Start with active users from the determined companies
        // SECURITY-AUDITED: SAFE — re-scoped by ShiftGrouping-derived companyIds or shift type's own companyId
        var usersQuery = _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.IsActive && companyIds.Contains(u.CompanyId));

        // Filter by JobType if specified
        if (effectiveJobTypeId.HasValue)
        {
            usersQuery = usersQuery.Where(u => u.JobTypeId == effectiveJobTypeId.Value);
        }

        var users = await usersQuery
            .Include(u => u.JobType)
            .Select(u => new
            {
                u.Id,
                u.DisplayName,
                JobTypeName = u.JobType != null ? u.JobType.DisplayName : null,
                u.CompanyId
            })
            .ToListAsync();

        // Get company names
        var userCompanyIds = users.Select(u => u.CompanyId).Distinct().ToList();
        // SECURITY-AUDITED: SAFE — scoped by companyIds of eligible users; returns names only
        var companyNames = await _db.Companies
            .IgnoreQueryFilters()
            .Where(c => userCompanyIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name);

        // Get assignment counts for this week
        var startOfWeek = GetStartOfWeek(DateOnly.FromDateTime(DateTime.Today));
        var endOfWeek = startOfWeek.AddDays(7);

        var userIds = users.Select(u => u.Id).ToList();
        var weekAssignments = await _db.ShiftAssignments
            .Include(sa => sa.ShiftInstance)
                .ThenInclude(si => si.ShiftType)
            .Where(sa => userIds.Contains(sa.UserId ?? 0)
                && sa.ShiftInstance.WorkDate >= startOfWeek
                && sa.ShiftInstance.WorkDate < endOfWeek)
            .Select(sa => new
            {
                sa.UserId,
                Hours = GetShiftHours(sa.ShiftInstance.ShiftType.Start, sa.ShiftInstance.ShiftType.End)
            })
            .ToListAsync();

        var assignmentCounts = weekAssignments
            .GroupBy(a => a.UserId)
            .ToDictionary(g => g.Key ?? 0, g => new { Count = g.Count(), Hours = g.Sum(a => a.Hours) });

        // Check if user is in shift grouping
        var groupingCompanyIdsSet = effectiveGroupingId.HasValue
            ? (await _db.ShiftGroupingCompanies
                .Where(sgc => sgc.ShiftGroupingId == effectiveGroupingId.Value)
                .Select(sgc => sgc.CompanyId)
                .ToListAsync())
                .ToHashSet()
            : new HashSet<int>();

        return users.Select(u => new EligibleUserDto(
            u.Id,
            u.DisplayName,
            u.JobTypeName,
            companyNames.TryGetValue(u.CompanyId, out var companyName) ? companyName : null,
            IsInShiftGrouping: !effectiveGroupingId.HasValue || groupingCompanyIdsSet.Contains(u.CompanyId),
            AssignedShiftsThisWeek: assignmentCounts.TryGetValue(u.Id, out var stats) ? stats.Count : 0,
            HoursThisWeek: assignmentCounts.TryGetValue(u.Id, out var hrs) ? hrs.Hours : 0
        )).ToList();
    }

    public async Task<ShiftAssignmentValidation> ValidateShiftAssignmentAsync(int userId, int shiftInstanceId)
    {
        var user = await _db.Users
            .Include(u => u.JobType)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            return new ShiftAssignmentValidation(
                IsValid: false,
                ErrorKey: "Error_UserNotFound",
                ErrorMessage: _localizer["Error_UserNotFound"],
                HasJobTypeMismatch: false,
                NotInShiftGrouping: false,
                ExceedsWeeklyCap: false,
                RestHoursViolation: false);
        }

        var shiftInstance = await _db.ShiftInstances
            .Include(si => si.ShiftType)
                .ThenInclude(st => st.ShiftGrouping)
                    .ThenInclude(sg => sg!.Companies)
            .FirstOrDefaultAsync(si => si.Id == shiftInstanceId);

        if (shiftInstance == null)
        {
            return new ShiftAssignmentValidation(
                IsValid: false,
                ErrorKey: "Error_ShiftNotFound",
                ErrorMessage: _localizer["Error_ShiftNotFound"],
                HasJobTypeMismatch: false,
                NotInShiftGrouping: false,
                ExceedsWeeklyCap: false,
                RestHoursViolation: false);
        }

        // Check JobType match
        bool hasJobTypeMismatch = false;
        if (shiftInstance.ShiftType.JobTypeId.HasValue)
        {
            hasJobTypeMismatch = user.JobTypeId != shiftInstance.ShiftType.JobTypeId;
        }

        // Check ShiftGrouping membership
        bool notInShiftGrouping = false;
        if (shiftInstance.ShiftType.ShiftGroupingId.HasValue && shiftInstance.ShiftType.ShiftGrouping != null)
        {
            var groupingCompanyIds = shiftInstance.ShiftType.ShiftGrouping.Companies
                .Select(c => c.CompanyId)
                .ToHashSet();
            notInShiftGrouping = !groupingCompanyIds.Contains(user.CompanyId);
        }

        // Check weekly cap using effective settings from hierarchy (Area -> Molecule -> Company)
        var effectiveSettings = await _hierarchySettingsService.GetEffectiveSettingsAsync(shiftInstance.CompanyId);
        var weeklyCap = effectiveSettings?.WeeklyCap ?? 48; // Default 48h weekly cap if settings not configured

        var startOfWeek = GetStartOfWeek(shiftInstance.WorkDate);
        var endOfWeek = startOfWeek.AddDays(7);

        var weeklyHours = await _db.ShiftAssignments
            .Include(sa => sa.ShiftInstance)
                .ThenInclude(si => si.ShiftType)
            .Where(sa => sa.UserId == userId
                && sa.ShiftInstance.WorkDate >= startOfWeek
                && sa.ShiftInstance.WorkDate < endOfWeek)
            .SumAsync(sa => GetShiftHours(sa.ShiftInstance.ShiftType.Start, sa.ShiftInstance.ShiftType.End));

        var shiftHours = GetShiftHours(shiftInstance.ShiftType.Start, shiftInstance.ShiftType.End);
        bool exceedsWeeklyCap = (weeklyHours + shiftHours) > weeklyCap;

        // Check rest hours (simplified - uses default 11 hours)
        bool restHoursViolation = await CheckRestHoursViolationAsync(userId, shiftInstance);

        var isValid = !hasJobTypeMismatch && !notInShiftGrouping && !exceedsWeeklyCap && !restHoursViolation;

        string? errorKey = null;
        string? errorMessage = null;

        if (!isValid)
        {
            if (hasJobTypeMismatch)
            {
                errorKey = "Error_JobTypeMismatch";
                errorMessage = _localizer["Error_JobTypeMismatch"];
            }
            else if (notInShiftGrouping)
            {
                errorKey = "Error_NotInShiftGrouping";
                errorMessage = _localizer["Error_NotInShiftGrouping"];
            }
            else if (exceedsWeeklyCap)
            {
                errorKey = "Error_ExceedsWeeklyCap";
                errorMessage = _localizer["Error_ExceedsWeeklyCap"];
            }
            else if (restHoursViolation)
            {
                errorKey = "Error_RestHoursViolation";
                errorMessage = _localizer["Error_RestHoursViolation"];
            }
        }

        return new ShiftAssignmentValidation(
            IsValid: isValid,
            ErrorKey: errorKey,
            ErrorMessage: errorMessage,
            HasJobTypeMismatch: hasJobTypeMismatch,
            NotInShiftGrouping: notInShiftGrouping,
            ExceedsWeeklyCap: exceedsWeeklyCap,
            RestHoursViolation: restHoursViolation);
    }

    public async Task<ShiftAssignmentResult> AssignShiftAsync(
        int userId,
        int shiftInstanceId,
        int assignedByUserId,
        string? notes = null)
    {
        var validation = await ValidateShiftAssignmentAsync(userId, shiftInstanceId);

        if (!validation.IsValid)
        {
            return new ShiftAssignmentResult(
                Success: false,
                AssignmentId: null,
                ErrorKey: validation.ErrorKey,
                ErrorMessage: validation.ErrorMessage);
        }

        var shiftInstance = await _db.ShiftInstances.FindAsync(shiftInstanceId);
        if (shiftInstance == null)
        {
            return new ShiftAssignmentResult(
                Success: false,
                AssignmentId: null,
                ErrorKey: "Error_ShiftNotFound",
                ErrorMessage: _localizer["Error_ShiftNotFound"]);
        }

        // Check if already assigned
        var existingAssignment = await _db.ShiftAssignments
            .FirstOrDefaultAsync(sa => sa.ShiftInstanceId == shiftInstanceId && sa.UserId == userId);

        if (existingAssignment != null)
        {
            return new ShiftAssignmentResult(
                Success: false,
                AssignmentId: existingAssignment.Id,
                ErrorKey: "Error_AlreadyAssigned",
                ErrorMessage: _localizer["Error_AlreadyAssigned"]);
        }

        var assignment = new ShiftAssignment
        {
            CompanyId = shiftInstance.CompanyId,
            ShiftInstanceId = shiftInstanceId,
            UserId = userId,
            CreatedAt = DateTime.UtcNow
        };

        _db.ShiftAssignments.Add(assignment);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Assigned user {UserId} to shift {ShiftInstanceId} by {AssignedBy}",
            userId, shiftInstanceId, assignedByUserId);

        return new ShiftAssignmentResult(
            Success: true,
            AssignmentId: assignment.Id,
            ErrorKey: null,
            ErrorMessage: null);
    }

    public async Task<bool> UnassignShiftAsync(
        int userId,
        int shiftInstanceId,
        int unassignedByUserId,
        string? reason = null)
    {
        var assignment = await _db.ShiftAssignments
            .FirstOrDefaultAsync(sa => sa.ShiftInstanceId == shiftInstanceId && sa.UserId == userId);

        if (assignment == null)
        {
            return false;
        }

        _db.ShiftAssignments.Remove(assignment);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Unassigned user {UserId} from shift {ShiftInstanceId} by {UnassignedBy}. Reason: {Reason}",
            userId, shiftInstanceId, unassignedByUserId, reason ?? "Not specified");

        return true;
    }

    private async Task<bool> CheckRestHoursViolationAsync(int userId, ShiftInstance shiftInstance)
    {
        // Get shifts in the surrounding 24-hour window
        var shiftDate = shiftInstance.WorkDate;
        var prevDate = shiftDate.AddDays(-1);
        var nextDate = shiftDate.AddDays(1);

        var nearbyAssignments = await _db.ShiftAssignments
            .Include(sa => sa.ShiftInstance)
                .ThenInclude(si => si.ShiftType)
            .Where(sa => sa.UserId == userId
                && (sa.ShiftInstance.WorkDate == prevDate
                    || sa.ShiftInstance.WorkDate == shiftDate
                    || sa.ShiftInstance.WorkDate == nextDate))
            .ToListAsync();

        if (!nearbyAssignments.Any())
            return false;

        // Simplified check: look for shifts that end within 11 hours of this shift's start
        var thisShiftStart = shiftInstance.WorkDate.ToDateTime(shiftInstance.ShiftType.Start);

        foreach (var assignment in nearbyAssignments)
        {
            var otherShiftEnd = GetShiftEndDateTime(
                assignment.ShiftInstance.WorkDate,
                assignment.ShiftInstance.ShiftType.Start,
                assignment.ShiftInstance.ShiftType.End);

            var restHours = (thisShiftStart - otherShiftEnd).TotalHours;

            if (restHours > 0 && restHours < 11) // 11 hour rest requirement
            {
                return true;
            }
        }

        return false;
    }

    private static DateOnly GetStartOfWeek(DateOnly date)
    {
        int diff = (7 + (date.DayOfWeek - DayOfWeek.Sunday)) % 7;
        return date.AddDays(-diff);
    }

    private static double GetShiftHours(TimeOnly start, TimeOnly end)
    {
        if (end <= start)
        {
            // Overnight shift
            return (24 - start.Hour + end.Hour) + (end.Minute - start.Minute) / 60.0;
        }
        return (end - start).TotalHours;
    }

    private static DateTime GetShiftEndDateTime(DateOnly workDate, TimeOnly start, TimeOnly end)
    {
        var endDate = end <= start ? workDate.AddDays(1) : workDate;
        return endDate.ToDateTime(end);
    }
}
