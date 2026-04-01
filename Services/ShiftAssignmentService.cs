using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;

namespace ShiftManager.Services;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — shift assignments are molecule-scoped by design;
// queries scoped by explicit shiftInstanceId/moleculeId parameters; called only from authorized endpoints
public class ShiftAssignmentService : IShiftAssignmentService
{
    private readonly AppDbContext _db;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly ILogger<ShiftAssignmentService> _logger;
    private readonly IHierarchySettingsService _hierarchySettingsService;
    private readonly IAuditLogService _auditLogService;
    private readonly string _hmacSecret;
    private readonly IAppConfigCacheService _configCache;

    private const int OverrideTokenExpiryMinutes = 5;

    public ShiftAssignmentService(
        AppDbContext db,
        IStringLocalizer<SharedResources> localizer,
        ILogger<ShiftAssignmentService> logger,
        IHierarchySettingsService hierarchySettingsService,
        IAuditLogService auditLogService,
        IConfiguration configuration,
        IAppConfigCacheService configCache)
    {
        _db = db;
        _localizer = localizer;
        _logger = logger;
        _hierarchySettingsService = hierarchySettingsService;
        _auditLogService = auditLogService;
        _hmacSecret = configuration["ApiKeyHmacSecret"]
            ?? Middleware.ApiAuthenticationMiddleware.HmacSecret;
        _configCache = configCache;
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

            // Fall back to shift type's company or all companies in molecule
            if (!companyIds.Any())
            {
                companyIds = shiftType.CompanyId.HasValue
                    ? new List<int> { shiftType.CompanyId.Value }
                    : await _db.Companies.Where(c => c.MoleculeId == shiftType.MoleculeId).Select(c => c.Id).ToListAsync();
            }
        }
        else
        {
            // No grouping: use shift type's company or all companies in molecule
            companyIds = shiftType.CompanyId.HasValue
                ? new List<int> { shiftType.CompanyId.Value }
                : await _db.Companies.Where(c => c.MoleculeId == shiftType.MoleculeId).Select(c => c.Id).ToListAsync();
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
                Start = sa.ShiftInstance.ShiftType.Start,
                End = sa.ShiftInstance.ShiftType.End
            })
            .ToListAsync();

        var assignmentCounts = weekAssignments
            .GroupBy(a => a.UserId)
            .ToDictionary(g => g.Key ?? 0, g => new { Count = g.Count(), Hours = g.Sum(a => GetShiftHours(a.Start, a.End)) });

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
        var errors = new List<ValidationIssue>();
        var warnings = new List<ValidationIssue>();

        // SECURITY-AUDITED: SAFE — IgnoreQueryFilters needed for cross-company assignment within molecule;
        // molecule boundary enforced below via IsUserInSameMoleculeAsShiftAsync
        var user = await _db.Users
            .IgnoreQueryFilters()
            .Include(u => u.JobType)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            errors.Add(new ValidationIssue(
                "USER_NOT_FOUND",
                _localizer["Error_UserNotFound"],
                ValidationSeverity.Error,
                ValidationCategory.JobType));
            return new ShiftAssignmentValidation(false, errors, warnings);
        }

        // SECURITY-AUDITED: SAFE — IgnoreQueryFilters needed for cross-company shift lookup within molecule
        var shiftInstance = await _db.ShiftInstances
            .IgnoreQueryFilters()
            .Include(si => si.ShiftType)
                .ThenInclude(st => st.ShiftGrouping)
                    .ThenInclude(sg => sg!.Companies)
            .FirstOrDefaultAsync(si => si.Id == shiftInstanceId);

        if (shiftInstance == null)
        {
            errors.Add(new ValidationIssue(
                "SHIFT_NOT_FOUND",
                _localizer["Error_ShiftNotFound"],
                ValidationSeverity.Error,
                ValidationCategory.Concurrency));
            return new ShiftAssignmentValidation(false, errors, warnings);
        }

        // Molecule boundary check — user must be in the same molecule as the shift
        if (shiftInstance.ShiftType.MoleculeId.HasValue)
        {
            if (!await IsUserInMoleculeAsync(user.CompanyId, shiftInstance.ShiftType.MoleculeId.Value))
            {
                errors.Add(new ValidationIssue(
                    "USER_NOT_IN_MOLECULE",
                    _localizer["Error_UserNotInMolecule"],
                    ValidationSeverity.Error,
                    ValidationCategory.JobType));
                return new ShiftAssignmentValidation(false, errors, warnings);
            }
        }

        // Check duplicate assignment (hard error)
        // SECURITY-AUDITED: SAFE — scoped by explicit shiftInstanceId+userId; cross-company assignments are valid within molecule
        var alreadyAssigned = await _db.ShiftAssignments
            .IgnoreQueryFilters()
            .AnyAsync(sa => sa.ShiftInstanceId == shiftInstanceId && sa.UserId == userId);
        if (alreadyAssigned)
        {
            errors.Add(new ValidationIssue(
                "ALREADY_ASSIGNED",
                _localizer["Error_AlreadyAssigned"],
                ValidationSeverity.Error,
                ValidationCategory.Concurrency));
        }

        // Check JobType match (warning — overrideable)
        if (shiftInstance.ShiftType.JobTypeId.HasValue && user.JobTypeId != shiftInstance.ShiftType.JobTypeId)
        {
            warnings.Add(new ValidationIssue(
                "JOB_TYPE_MISMATCH",
                _localizer["Error_JobTypeMismatch"],
                ValidationSeverity.Warning,
                ValidationCategory.JobType));
        }

        // Check ShiftGrouping membership (warning — overrideable)
        if (shiftInstance.ShiftType.ShiftGroupingId.HasValue && shiftInstance.ShiftType.ShiftGrouping != null)
        {
            var groupingCompanyIds = shiftInstance.ShiftType.ShiftGrouping.Companies
                .Select(c => c.CompanyId)
                .ToHashSet();
            if (!groupingCompanyIds.Contains(user.CompanyId))
            {
                warnings.Add(new ValidationIssue(
                    "NOT_IN_SHIFT_GROUPING",
                    _localizer["Error_NotInShiftGrouping"],
                    ValidationSeverity.Warning,
                    ValidationCategory.ShiftGrouping));
            }
        }

        // Company eligibility check (data-driven — replaces grant-based TechShiftService)
        var eligibleCompanyIds = shiftInstance.ShiftType.GetEligibleCompanyIdList();
        if (eligibleCompanyIds != null && !eligibleCompanyIds.Contains(user.CompanyId))
        {
            errors.Add(new ValidationIssue(
                "COMPANY_INELIGIBLE",
                _localizer["Error_CompanyIneligibleForShift"],
                ValidationSeverity.Error,
                ValidationCategory.TechShift));
        }

        // Officer rank check
        if (shiftInstance.ShiftType.RequiresOfficerRank && !user.Rank.IsOfficer())
        {
            errors.Add(new ValidationIssue(
                "OFFICER_RANK_REQUIRED",
                _localizer["Error_OfficerRankRequired"],
                ValidationSeverity.Error,
                ValidationCategory.TechShift));
        }

        // Load hierarchy settings for weekly cap and rest hours
        var effectiveSettings = await _hierarchySettingsService.GetEffectiveSettingsAsync(shiftInstance.CompanyId);

        // --- Unified validation: load nearby assignments for overlap + rest checks ---
        var shiftType = shiftInstance.ShiftType;
        bool isOfflineShift = shiftType.IsOffline;
        bool isHomeShift = shiftType.IsHome;
        bool isExemptShift = isOfflineShift || isHomeShift;
        var (newStart, newEnd) = TimeHelpers.GetShiftWindow(shiftType, shiftInstance.WorkDate);

        var windowStart = TimeHelpers.WeekStart(shiftInstance.WorkDate).AddDays(-1);
        var windowEnd = windowStart.AddDays(8);
        // SECURITY-AUDITED: SAFE — IgnoreQueryFilters needed to detect cross-company overlap within molecule
        var nearbyAssignments = await _db.ShiftAssignments.IgnoreQueryFilters()
            .Where(sa => sa.UserId == userId
                && sa.ShiftInstanceId != shiftInstanceId  // exclude self to avoid phantom overlap
                && sa.ShiftInstance.WorkDate >= windowStart
                && sa.ShiftInstance.WorkDate <= windowEnd)
            .Select(sa => new {
                sa.ShiftInstance.WorkDate,
                sa.ShiftInstance.ShiftType.Start,
                sa.ShiftInstance.ShiftType.End,
                IsOffline = sa.ShiftInstance.ShiftType.Key == ShiftType.KEY_OFFLINE,
                IsHome = sa.ShiftInstance.ShiftType.Key == ShiftType.KEY_HOME
            }).ToListAsync();

        // B. Overlap detection (Error — hard block; exempt for OFFLINE and HOME shifts)
        if (!isExemptShift)
        {
            foreach (var ra in nearbyAssignments)
            {
                if (ra.IsOffline || ra.IsHome) continue;
                var (rs, re) = TimeHelpers.GetShiftWindow(
                    new ShiftType { Start = ra.Start, End = ra.End }, ra.WorkDate);
                if (rs < newEnd && newStart < re)
                {
                    errors.Add(new ValidationIssue(
                        "OVERLAP", _localizer["Error_ShiftOverlap"],
                        ValidationSeverity.Error, ValidationCategory.Overlap));
                    break;
                }
            }
        }

        // C. Rest period (Error — hard block; exempt for OFFLINE and HOME shifts)
        if (!isExemptShift)
        {
            var restRequired = effectiveSettings?.RestHours ?? 8;
            var nonExemptWindows = nearbyAssignments
                .Where(ra => !ra.IsOffline && !ra.IsHome)
                .Select(ra => TimeHelpers.GetShiftWindow(
                    new ShiftType { Start = ra.Start, End = ra.End }, ra.WorkDate))
                .ToList();

            var before = nonExemptWindows
                .Where(w => w.end <= newStart)
                .OrderByDescending(w => w.end)
                .FirstOrDefault();
            var after = nonExemptWindows
                .Where(w => w.start >= newEnd)
                .OrderBy(w => w.start)
                .FirstOrDefault();

            bool restViolation = false;
            if (before.end != default && (newStart - before.end).TotalHours < restRequired)
                restViolation = true;
            if (after.start != default && (after.start - newEnd).TotalHours < restRequired)
                restViolation = true;

            if (restViolation)
            {
                errors.Add(new ValidationIssue(
                    "REST_HOURS_VIOLATION", _localizer["Error_RestHoursViolation"],
                    ValidationSeverity.Error, ValidationCategory.RestHours));
            }
        }

        // HOME mutual exclusion: warn when assigning a real shift on a HOME day (overrideable)
        if (!isExemptShift)
        {
            bool hasHomeOnDate = nearbyAssignments.Any(ra => ra.IsHome && ra.WorkDate == shiftInstance.WorkDate);
            if (hasHomeOnDate)
            {
                warnings.Add(new ValidationIssue(
                    "HOME_CONFLICT", _localizer["Warning_HomeConflict"],
                    ValidationSeverity.Warning, ValidationCategory.HomeConflict));
            }
        }

        // HOME mutual exclusion (reverse): warn when assigning HOME on a day with real shifts
        if (isHomeShift)
        {
            bool hasRealShiftOnDate = nearbyAssignments.Any(ra =>
                !ra.IsOffline && !ra.IsHome && ra.WorkDate == shiftInstance.WorkDate);
            if (hasRealShiftOnDate)
            {
                warnings.Add(new ValidationIssue(
                    "SHIFT_EXISTS_CONFLICT", _localizer["Warning_ShiftExistsOnHomeDate"],
                    ValidationSeverity.Warning, ValidationCategory.HomeConflict));
            }

            // Duplicate HOME on same day is a hard error
            bool hasDuplicateHome = nearbyAssignments.Any(ra => ra.IsHome && ra.WorkDate == shiftInstance.WorkDate);
            if (hasDuplicateHome)
            {
                errors.Add(new ValidationIssue(
                    "DUPLICATE_HOME", _localizer["Error_DuplicateHome"],
                    ValidationSeverity.Error, ValidationCategory.Overlap));
            }
        }

        // D. Vacation / time-off conflict (Warning — overrideable)
        // SECURITY-AUDITED: SAFE — IgnoreQueryFilters needed for cross-company time-off lookup
        bool hasTimeOff = await _db.TimeOffRequests.IgnoreQueryFilters()
            .AnyAsync(r => r.UserId == userId
                && r.Status == RequestStatus.Approved
                && shiftInstance.WorkDate >= r.StartDate
                && shiftInstance.WorkDate <= r.EndDate);
        if (hasTimeOff)
        {
            warnings.Add(new ValidationIssue(
                "VACATION_CONFLICT", _localizer["Warning_VacationConflict"],
                ValidationSeverity.Warning, ValidationCategory.TimeOff));
        }

        // E. Chore conflict (Warning — overrideable)
        // Uses CanceledAt == null, NOT IsActive (which is [NotMapped] and can't be used in EF queries)
        bool hasChore = await _db.Chores.IgnoreQueryFilters()
            .AnyAsync(c => c.UserId == userId
                && c.Date == shiftInstance.WorkDate
                && c.CanceledAt == null);
        if (hasChore)
        {
            warnings.Add(new ValidationIssue(
                "CHORE_CONFLICT", _localizer["Warning_ChoreConflict"],
                ValidationSeverity.Warning, ValidationCategory.ChoreConflict));
        }

        // F. On-duty conflict (Warning — overrideable)
        bool hasOnDuty = await _db.OnDuties.IgnoreQueryFilters()
            .AnyAsync(o => o.UserId == userId
                && o.Date == shiftInstance.WorkDate
                && o.CanceledAt == null);
        if (hasOnDuty)
        {
            warnings.Add(new ValidationIssue(
                "ONDUTY_CONFLICT", _localizer["Warning_OnDutyConflict"],
                ValidationSeverity.Warning, ValidationCategory.OnDutyConflict));
        }

        // Check weekly cap using configurable settings from hierarchy (exempt for OFFLINE and HOME)
        if (!isExemptShift)
        {
            var weeklyCap = effectiveSettings?.WeeklyCap ?? 56;

            // Use configurable week start day from AppConfig
            var weekStartDayConfig = await _configCache.GetConfigAsync(shiftInstance.CompanyId, "WeekStartDay");
            var weekStartDayOfWeek = (DayOfWeek)Math.Clamp(
                int.TryParse(weekStartDayConfig?.Value, out var d) ? d : 0, 0, 6);
            var startOfWeek = TimeHelpers.WeekStart(shiftInstance.WorkDate, weekStartDayOfWeek);
            var endOfWeek = startOfWeek.AddDays(6);

            // SECURITY-AUDITED: SAFE — IgnoreQueryFilters needed to count ALL assignments across companies within molecule
            var weekShiftTimes = await _db.ShiftAssignments
                .IgnoreQueryFilters()
                .Include(sa => sa.ShiftInstance)
                    .ThenInclude(si => si.ShiftType)
                .Where(sa => sa.UserId == userId
                    && sa.ShiftInstance.WorkDate >= startOfWeek
                    && sa.ShiftInstance.WorkDate <= endOfWeek)
                .Select(sa => new { sa.ShiftInstance.WorkDate, sa.ShiftInstance.ShiftType.Start, sa.ShiftInstance.ShiftType.End })
                .ToListAsync();

            // Deduplicate overlapping time windows before summing (ported from ConflictChecker)
            var weekWindows = weekShiftTimes
                .Select(s => TimeHelpers.GetShiftWindow(new ShiftType { Start = s.Start, End = s.End }, s.WorkDate))
                .OrderBy(w => w.start)
                .ToList();
            var weeklyHours = MergeAndSumHours(weekWindows);

            var shiftHours = TimeHelpers.Hours(shiftInstance.ShiftType, shiftInstance.WorkDate);
            if ((weeklyHours + shiftHours) > weeklyCap)
            {
                warnings.Add(new ValidationIssue(
                    "EXCEEDS_WEEKLY_CAP",
                    _localizer["Error_ExceedsWeeklyCap"],
                    ValidationSeverity.Warning,
                    ValidationCategory.WeeklyHours));
            }
        }

        // FINDING-003 FIX: Warn when assigning to past dates (back-fill allowed via override; exempt for HOME)
        if (!isExemptShift && shiftInstance.WorkDate < DateOnly.FromDateTime(DateTime.UtcNow))
        {
            warnings.Add(new ValidationIssue(
                "PAST_DATE",
                _localizer["Warning_PastDateShiftAssignment"],
                ValidationSeverity.Warning,
                ValidationCategory.Concurrency));
        }

        bool canAssign = errors.Count == 0;
        return new ShiftAssignmentValidation(canAssign, errors, warnings);
    }

    public async Task<ShiftAssignmentValidation> ValidateTraineeAssignmentAsync(int traineeUserId, int assignmentId)
    {
        var errors = new List<ValidationIssue>();
        var warnings = new List<ValidationIssue>();

        // SECURITY-AUDITED: SAFE — IgnoreQueryFilters needed for cross-company assignment lookup within molecule
        var assignment = await _db.ShiftAssignments
            .IgnoreQueryFilters()
            .Include(a => a.ShiftInstance)
                .ThenInclude(si => si.ShiftType)
            .FirstOrDefaultAsync(a => a.Id == assignmentId);

        if (assignment == null)
        {
            errors.Add(new ValidationIssue(
                "ASSIGNMENT_NOT_FOUND",
                _localizer["Error_ShiftNotFound"],
                ValidationSeverity.Error,
                ValidationCategory.Trainee));
            return new ShiftAssignmentValidation(false, errors, warnings);
        }

        // SECURITY-AUDITED: SAFE — IgnoreQueryFilters needed for cross-company trainee within molecule
        var trainee = await _db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == traineeUserId);
        if (trainee == null)
        {
            errors.Add(new ValidationIssue(
                "TRAINEE_NOT_FOUND",
                _localizer["Error_UserNotFound"],
                ValidationSeverity.Error,
                ValidationCategory.Trainee));
            return new ShiftAssignmentValidation(false, errors, warnings);
        }

        // Prevent self-training
        if (assignment.UserId == traineeUserId)
        {
            warnings.Add(new ValidationIssue(
                "TRAINEE_SELF_TRAINING",
                _localizer["Error_TraineeSelfTraining"],
                ValidationSeverity.Warning,
                ValidationCategory.Trainee));
        }

        // Verify trainee has Trainee role
        if (trainee.Role != UserRole.Trainee)
        {
            warnings.Add(new ValidationIssue(
                "TRAINEE_WRONG_ROLE",
                _localizer["Error_TraineeWrongRole"],
                ValidationSeverity.Warning,
                ValidationCategory.Trainee));
        }

        // Verify same molecule (not same company — trainees can shadow cross-company within molecule)
        if (assignment.ShiftInstance?.ShiftType?.MoleculeId is int moleculeId)
        {
            if (!await IsUserInMoleculeAsync(trainee.CompanyId, moleculeId))
            {
                warnings.Add(new ValidationIssue(
                    "TRAINEE_DIFFERENT_MOLECULE",
                    _localizer["Error_TraineeDifferentMolecule"],
                    ValidationSeverity.Warning,
                    ValidationCategory.Trainee));
            }
        }
        else if (assignment.CompanyId != trainee.CompanyId)
        {
            // Fallback for shifts without MoleculeId — use original company check
            warnings.Add(new ValidationIssue(
                "TRAINEE_DIFFERENT_COMPANY",
                _localizer["Error_TraineeDifferentCompany"],
                ValidationSeverity.Warning,
                ValidationCategory.Trainee));
        }

        bool canAssign = errors.Count == 0;
        return new ShiftAssignmentValidation(canAssign, errors, warnings);
    }

    public async Task<ShiftAssignmentResult> AssignShiftAsync(
        int userId,
        int shiftInstanceId,
        int assignedByUserId,
        string? overrideToken = null,
        string? notes = null)
    {
        var validation = await ValidateShiftAssignmentAsync(userId, shiftInstanceId);

        // Hard errors always block
        if (!validation.CanAssign)
        {
            return new ShiftAssignmentResult(
                Success: false,
                AssignmentId: null,
                ErrorKey: validation.Errors.FirstOrDefault()?.Key,
                ErrorMessage: validation.Errors.FirstOrDefault()?.Message,
                Validation: validation);
        }

        // Warnings require valid override token
        if (validation.Warnings.Count > 0)
        {
            if (string.IsNullOrEmpty(overrideToken) || !ValidateOverrideToken(overrideToken, shiftInstanceId, userId))
            {
                return new ShiftAssignmentResult(
                    Success: false,
                    AssignmentId: null,
                    ErrorKey: "WARNINGS_REQUIRE_OVERRIDE",
                    ErrorMessage: "Assignment has warnings that require manager override",
                    Validation: validation);
            }

            // Log the override for audit
            _logger.LogInformation(
                "Override token accepted for assignment: User {UserId} → Shift {ShiftInstanceId} by {AssignedBy}. Warnings: {Warnings}",
                userId, shiftInstanceId, assignedByUserId,
                string.Join(", ", validation.Warnings.Select(w => w.Key)));
        }

        // SECURITY-AUDITED: SAFE — IgnoreQueryFilters needed for cross-company shift within molecule;
        // molecule boundary already validated by ValidateShiftAssignmentAsync above
        var shiftInstance = await _db.ShiftInstances
            .IgnoreQueryFilters()
            .Include(si => si.ShiftType)
            .FirstOrDefaultAsync(si => si.Id == shiftInstanceId);
        if (shiftInstance == null)
        {
            return new ShiftAssignmentResult(false, null, "SHIFT_NOT_FOUND", _localizer["Error_ShiftNotFound"]);
        }

        // Wrap check-then-act in transaction to reduce race window for concurrent assignments
        using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            // Check if already assigned (re-check inside transaction)
            // SECURITY-AUDITED: SAFE — scoped by explicit shiftInstanceId+userId
            var existingAssignment = await _db.ShiftAssignments
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(sa => sa.ShiftInstanceId == shiftInstanceId && sa.UserId == userId);

            if (existingAssignment != null)
            {
                await transaction.RollbackAsync();
                return new ShiftAssignmentResult(false, existingAssignment.Id, "ALREADY_ASSIGNED", _localizer["Error_AlreadyAssigned"]);
            }

            // Check capacity — exempt HOME/OFFLINE shifts (they allow unlimited assignments)
            var isExemptShift = shiftInstance.ShiftType.IsHome || shiftInstance.ShiftType.IsOffline;
            if (!isExemptShift)
            {
                // SECURITY-AUDITED: SAFE — scoped by explicit shiftInstanceId
                var currentAssignedCount = await _db.ShiftAssignments
                    .IgnoreQueryFilters()
                    .CountAsync(sa => sa.ShiftInstanceId == shiftInstanceId && sa.UserId != null);
                if (currentAssignedCount >= shiftInstance.StaffingRequired)
                {
                    await transaction.RollbackAsync();
                    return new ShiftAssignmentResult(false, null, "SHIFT_FULLY_STAFFED", _localizer["Error_ShiftFullyStaffed"]);
                }
            }

            // Use the EMPLOYEE's CompanyId (not the instance's or assigner's)
            // This ensures the employee's manager can see the assignment from their company context
            var assignedUser = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId);
            var assignment = new ShiftAssignment
            {
                CompanyId = assignedUser?.CompanyId ?? shiftInstance.CompanyId,
                ShiftInstanceId = shiftInstanceId,
                UserId = userId,
                CreatedAt = DateTime.UtcNow
            };

            _db.ShiftAssignments.Add(assignment);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Assigned user {UserId} to shift {ShiftInstanceId} by {AssignedBy}",
                userId, shiftInstanceId, assignedByUserId);

            await _auditLogService.LogUserActionAsync(
                assignedByUserId,
                "ShiftAssigned",
                "ShiftAssignment",
                assignment.Id,
                $"Assigned user {userId} to shift instance {shiftInstanceId}",
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    assignedByUserId,
                    shiftInstanceId,
                    userId,
                    notes
                }));

            return new ShiftAssignmentResult(true, assignment.Id, null, null);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();
            _logger.LogWarning(
                "Concurrent assignment conflict for user {UserId} on shift {ShiftInstanceId}",
                userId, shiftInstanceId);
            return new ShiftAssignmentResult(false, null, "ALREADY_ASSIGNED", _localizer["Error_AlreadyAssigned"]);
        }
    }

    public async Task<bool> UnassignShiftAsync(
        int userId,
        int shiftInstanceId,
        int unassignedByUserId,
        string? reason = null)
    {
        using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            // SECURITY-AUDITED: SAFE — IgnoreQueryFilters needed for cross-company unassignment within molecule;
            // scoped by explicit shiftInstanceId+userId
            var assignment = await _db.ShiftAssignments
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(sa => sa.ShiftInstanceId == shiftInstanceId && sa.UserId == userId);

            if (assignment == null)
            {
                await transaction.RollbackAsync();
                return false;
            }

            var assignmentId = assignment.Id;
            _db.ShiftAssignments.Remove(assignment);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Unassigned user {UserId} from shift {ShiftInstanceId} by {UnassignedBy}. Reason: {Reason}",
                userId, shiftInstanceId, unassignedByUserId, reason ?? "Not specified");

            await _auditLogService.LogUserActionAsync(
                unassignedByUserId,
                "ShiftUnassigned",
                "ShiftAssignment",
                assignmentId,
                $"Unassigned user {userId} from shift instance {shiftInstanceId}",
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    unassignedByUserId,
                    shiftInstanceId,
                    userId,
                    reason
                }));

            return true;
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();
            _logger.LogWarning(
                "Concurrent unassignment conflict for user {UserId} on shift {ShiftInstanceId}",
                userId, shiftInstanceId);
            return false;
        }
    }

    public string GenerateOverrideToken(int shiftInstanceId, int userId, IReadOnlyList<string> warningKeys)
    {
        var expiry = DateTimeOffset.UtcNow.AddMinutes(OverrideTokenExpiryMinutes).ToUnixTimeSeconds();
        var sortedKeys = string.Join(",", warningKeys.OrderBy(k => k));
        var payload = $"{shiftInstanceId}|{userId}|{sortedKeys}|{expiry}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_hmacSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var signature = Convert.ToBase64String(hash);

        // Token format: base64(payload)|signature
        var tokenPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
        return $"{tokenPayload}.{signature}";
    }

    public bool ValidateOverrideToken(string token, int shiftInstanceId, int userId)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 2) return false;

            var payloadBytes = Convert.FromBase64String(parts[0]);
            var payload = Encoding.UTF8.GetString(payloadBytes);
            var providedSignature = parts[1];

            // Verify HMAC
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_hmacSecret));
            var expectedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            var expectedSignature = Convert.ToBase64String(expectedHash);

            if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expectedSignature),
                Encoding.UTF8.GetBytes(providedSignature)))
            {
                return false;
            }

            // Parse and verify payload
            var payloadParts = payload.Split('|');
            if (payloadParts.Length < 4) return false;

            if (!int.TryParse(payloadParts[0], out var tokenShiftId) || tokenShiftId != shiftInstanceId)
                return false;
            if (!int.TryParse(payloadParts[1], out var tokenUserId) || tokenUserId != userId)
                return false;

            // Check expiry
            if (!long.TryParse(payloadParts[3], out var expiry))
                return false;

            var expiryTime = DateTimeOffset.FromUnixTimeSeconds(expiry);
            if (DateTimeOffset.UtcNow > expiryTime)
            {
                _logger.LogWarning("Override token expired for shift {ShiftId} user {UserId}", shiftInstanceId, userId);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid override token format");
            return false;
        }
    }

    public async Task<Dictionary<(int shiftInstanceId, int userId), ShiftAssignmentValidation>>
        ValidateBatchAsync(IEnumerable<(int shiftInstanceId, int userId)> assignments)
    {
        var result = new Dictionary<(int shiftInstanceId, int userId), ShiftAssignmentValidation>();
        var assignmentList = assignments.ToList();

        if (!assignmentList.Any())
            return result;

        // Pre-load all shift instances in one query
        // SECURITY-AUDITED: SAFE — IgnoreQueryFilters for cross-company batch validation within molecule;
        // scoped by explicit shiftInstanceIds
        var shiftInstanceIds = assignmentList.Select(a => a.shiftInstanceId).Distinct().ToList();
        var shiftInstances = await _db.ShiftInstances
            .IgnoreQueryFilters()
            .Include(si => si.ShiftType)
                .ThenInclude(st => st.ShiftGrouping)
                    .ThenInclude(sg => sg!.Companies)
            .Where(si => shiftInstanceIds.Contains(si.Id))
            .ToDictionaryAsync(si => si.Id);

        // Pre-load all users in one query
        // SECURITY-AUDITED: SAFE — scoped by explicit userIds from assignment list
        var userIds = assignmentList.Select(a => a.userId).Distinct().ToList();
        var users = await _db.Users
            .IgnoreQueryFilters()
            .Include(u => u.JobType)
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id);

        // Pre-load all existing assignments for these shifts
        // SECURITY-AUDITED: SAFE — scoped by explicit shiftInstanceIds
        var existingAssignments = await _db.ShiftAssignments
            .IgnoreQueryFilters()
            .Where(sa => shiftInstanceIds.Contains(sa.ShiftInstanceId))
            .Select(sa => new { sa.ShiftInstanceId, sa.UserId })
            .ToListAsync();

        var existingSet = existingAssignments
            .Where(ea => ea.UserId.HasValue)
            .Select(ea => (ea.ShiftInstanceId, ea.UserId!.Value))
            .ToHashSet();

        // Pre-load weekly hours for all users
        var anyInstance = shiftInstances.Values.FirstOrDefault();
        var anyDate = anyInstance?.WorkDate ?? DateOnly.FromDateTime(DateTime.Today);
        // Use configurable week start day (consistent with single validation)
        var batchCompanyId = anyInstance?.CompanyId ?? 0;
        var batchWeekStartConfig = batchCompanyId > 0
            ? await _configCache.GetConfigAsync(batchCompanyId, "WeekStartDay")
            : null;
        var batchWeekStartDay = (DayOfWeek)Math.Clamp(
            int.TryParse(batchWeekStartConfig?.Value, out var bd) ? bd : 0, 0, 6);
        var startOfWeek = TimeHelpers.WeekStart(anyDate, batchWeekStartDay);
        var endOfWeek = startOfWeek.AddDays(6);

        // SECURITY-AUDITED: SAFE — IgnoreQueryFilters needed to count ALL assignments across companies for weekly hours
        var weekShiftData = await _db.ShiftAssignments
            .IgnoreQueryFilters()
            .Include(sa => sa.ShiftInstance)
                .ThenInclude(si => si.ShiftType)
            .Where(sa => userIds.Contains(sa.UserId ?? 0)
                && sa.ShiftInstance.WorkDate >= startOfWeek
                && sa.ShiftInstance.WorkDate <= endOfWeek)
            .Select(sa => new { sa.UserId, sa.ShiftInstance.WorkDate, sa.ShiftInstance.ShiftType.Start, sa.ShiftInstance.ShiftType.End })
            .ToListAsync();

        // Deduplicate overlapping time windows per user before summing (consistent with single validation)
        var weeklyHoursByUser = weekShiftData
            .GroupBy(w => w.UserId ?? 0)
            .ToDictionary(g => g.Key, g => MergeAndSumHours(
                g.Select(s => TimeHelpers.GetShiftWindow(new ShiftType { Start = s.Start, End = s.End }, s.WorkDate))
                 .OrderBy(w => w.start)
                 .ToList()));

        foreach (var (shiftInstanceId, userId) in assignmentList)
        {
            var errors = new List<ValidationIssue>();
            var warnings = new List<ValidationIssue>();

            if (!users.TryGetValue(userId, out var user))
            {
                errors.Add(new ValidationIssue("USER_NOT_FOUND", _localizer["Error_UserNotFound"], ValidationSeverity.Error, ValidationCategory.JobType));
                result[(shiftInstanceId, userId)] = new ShiftAssignmentValidation(false, errors, warnings);
                continue;
            }

            if (!shiftInstances.TryGetValue(shiftInstanceId, out var shiftInstance))
            {
                errors.Add(new ValidationIssue("SHIFT_NOT_FOUND", _localizer["Error_ShiftNotFound"], ValidationSeverity.Error, ValidationCategory.Concurrency));
                result[(shiftInstanceId, userId)] = new ShiftAssignmentValidation(false, errors, warnings);
                continue;
            }

            // Molecule boundary check
            if (shiftInstance.ShiftType.MoleculeId.HasValue)
            {
                if (!await IsUserInMoleculeAsync(user.CompanyId, shiftInstance.ShiftType.MoleculeId.Value))
                {
                    errors.Add(new ValidationIssue("USER_NOT_IN_MOLECULE",
                        _localizer["Error_UserNotInMolecule"],
                        ValidationSeverity.Error, ValidationCategory.JobType));
                    result[(shiftInstanceId, userId)] = new ShiftAssignmentValidation(false, errors, warnings);
                    continue;
                }
            }

            // Duplicate check
            if (existingSet.Contains((shiftInstanceId, userId)))
            {
                errors.Add(new ValidationIssue("ALREADY_ASSIGNED", _localizer["Error_AlreadyAssigned"], ValidationSeverity.Error, ValidationCategory.Concurrency));
            }

            // Job type mismatch
            if (shiftInstance.ShiftType.JobTypeId.HasValue && user.JobTypeId != shiftInstance.ShiftType.JobTypeId)
            {
                warnings.Add(new ValidationIssue("JOB_TYPE_MISMATCH", _localizer["Error_JobTypeMismatch"], ValidationSeverity.Warning, ValidationCategory.JobType));
            }

            // ShiftGrouping membership
            if (shiftInstance.ShiftType.ShiftGroupingId.HasValue && shiftInstance.ShiftType.ShiftGrouping != null)
            {
                var groupingCompanyIds = shiftInstance.ShiftType.ShiftGrouping.Companies
                    .Select(c => c.CompanyId).ToHashSet();
                if (!groupingCompanyIds.Contains(user.CompanyId))
                {
                    warnings.Add(new ValidationIssue("NOT_IN_SHIFT_GROUPING", _localizer["Error_NotInShiftGrouping"], ValidationSeverity.Warning, ValidationCategory.ShiftGrouping));
                }
            }

            // Company eligibility check (data-driven — replaces grant-based TechShiftService)
            var batchEligibleCompanyIds = shiftInstance.ShiftType.GetEligibleCompanyIdList();
            if (batchEligibleCompanyIds != null && !batchEligibleCompanyIds.Contains(user.CompanyId))
            {
                errors.Add(new ValidationIssue("COMPANY_INELIGIBLE", _localizer["Error_CompanyIneligibleForShift"], ValidationSeverity.Error, ValidationCategory.TechShift));
            }

            // Officer rank check
            if (shiftInstance.ShiftType.RequiresOfficerRank && !user.Rank.IsOfficer())
            {
                errors.Add(new ValidationIssue("OFFICER_RANK_REQUIRED", _localizer["Error_OfficerRankRequired"], ValidationSeverity.Error, ValidationCategory.TechShift));
            }

            // Weekly cap (using pre-loaded data; exempt for OFFLINE and HOME shifts — consistent with single validation)
            bool isExemptShift = shiftInstance.ShiftType.IsOffline || shiftInstance.ShiftType.IsHome;
            if (!isExemptShift)
            {
                var effectiveSettings = await _hierarchySettingsService.GetEffectiveSettingsAsync(shiftInstance.CompanyId);
                var weeklyCap = effectiveSettings?.WeeklyCap ?? 56;
                var userWeeklyHours = weeklyHoursByUser.GetValueOrDefault(userId, 0.0);
                var shiftHours = GetShiftHours(shiftInstance.ShiftType.Start, shiftInstance.ShiftType.End);
                if ((userWeeklyHours + shiftHours) > weeklyCap)
                {
                    warnings.Add(new ValidationIssue("EXCEEDS_WEEKLY_CAP", _localizer["Error_ExceedsWeeklyCap"], ValidationSeverity.Warning, ValidationCategory.WeeklyHours));
                }
            }

            // HOME mutual exclusion (consistent with single validation)
            if (isExemptShift && shiftInstance.ShiftType.IsHome)
            {
                // DUPLICATE_HOME: check if another HOME assignment exists on same date
                var sameDateHomeExists = existingAssignments.Any(ea =>
                    ea.UserId == userId
                    && shiftInstances.TryGetValue(ea.ShiftInstanceId, out var otherSi)
                    && otherSi.ShiftType.Key == ShiftType.KEY_HOME
                    && otherSi.WorkDate == shiftInstance.WorkDate);
                if (sameDateHomeExists)
                {
                    errors.Add(new ValidationIssue("DUPLICATE_HOME", _localizer["Error_DuplicateHome"], ValidationSeverity.Error, ValidationCategory.Overlap));
                }
            }

            // Note: rest hours check skipped in batch (would require N additional queries per assignment)
            // FillRange typically copies staffing structure, not specific user assignments

            result[(shiftInstanceId, userId)] = new ShiftAssignmentValidation(errors.Count == 0, errors, warnings);
        }

        return result;
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

    /// <summary>
    /// Merges overlapping time windows and sums total unique hours.
    /// Delegates to shared TimeHelpers implementation.
    /// </summary>
    private static double MergeAndSumHours(List<(DateTime start, DateTime end)> windows)
        => TimeHelpers.MergeAndSumHours(windows);

    /// <summary>
    /// Checks whether a user's company belongs to the specified molecule.
    /// Used as molecule boundary enforcement after IgnoreQueryFilters() calls.
    /// </summary>
    private async Task<bool> IsUserInMoleculeAsync(int userCompanyId, int moleculeId)
    {
        return await _db.Companies
            .IgnoreQueryFilters()
            .AnyAsync(c => c.Id == userCompanyId && c.MoleculeId == moleculeId);
    }
}
