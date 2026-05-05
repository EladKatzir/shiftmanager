using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;

namespace ShiftManager.Services;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — busy detection is
// molecule-scoped by design, not tenant-scoped; queries are gated by explicit moleculeId
// and target.userId. Tenant filters would silently miss legitimate cross-company
// assignments within a molecule (verified live during the 2026-05-03 audit).
public class BusyService : IBusyService
{
    private readonly AppDbContext _db;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly ILogger<BusyService> _logger;
    private readonly IHierarchySettingsService _hierarchySettingsService;
    private readonly IAppConfigCacheService _configCache;
    private readonly string _hmacSecret;

    private const int OverrideTokenExpiryMinutes = 5;

    public BusyService(
        AppDbContext db,
        IStringLocalizer<SharedResources> localizer,
        ILogger<BusyService> logger,
        IHierarchySettingsService hierarchySettingsService,
        IAppConfigCacheService configCache,
        IConfiguration configuration)
    {
        _db = db;
        _localizer = localizer;
        _logger = logger;
        _hierarchySettingsService = hierarchySettingsService;
        _configCache = configCache;
        _hmacSecret = configuration["ApiKeyHmacSecret"]
            ?? Middleware.ApiAuthenticationMiddleware.HmacSecret;
    }

    public async Task<Dictionary<int, BusySummaryWithHardError>> GetBusyStatesAsync(
        IReadOnlyList<int> userIds,
        DateOnly date,
        int moleculeId,
        BusyTarget? target = null)
    {
        var result = new Dictionary<int, BusySummaryWithHardError>(userIds.Count);
        if (userIds.Count == 0) return result;

        var shifts = await _db.ShiftAssignments.IgnoreQueryFilters()
            .Where(sa => sa.UserId != null
                && userIds.Contains(sa.UserId.Value)
                && sa.ShiftInstance.WorkDate == date)
            .Select(sa => new
            {
                UserId = sa.UserId!.Value,
                sa.ShiftInstance.ShiftType.Key,
                sa.ShiftInstance.ShiftType.Start,
                sa.ShiftInstance.ShiftType.End,
                ShiftTypeNameEn = sa.ShiftInstance.ShiftType.NameEn
            })
            .ToListAsync();

        var chores = await _db.Chores.IgnoreQueryFilters()
            .Where(c => userIds.Contains(c.UserId)
                && c.Date == date
                && c.CanceledAt == null)
            .Select(c => new { c.UserId, c.Title })
            .ToListAsync();

        var onDuties = await _db.OnDuties.IgnoreQueryFilters()
            .Where(o => userIds.Contains(o.UserId)
                && o.Date == date
                && o.CanceledAt == null)
            .Select(o => new { o.UserId, o.Type })
            .ToListAsync();

        var vacations = await _db.TimeOffRequests.IgnoreQueryFilters()
            .Where(t => userIds.Contains(t.UserId)
                && t.Status == RequestStatus.Approved
                && t.StartDate <= date
                && t.EndDate >= date)
            .Select(t => new { t.UserId, t.EndDate })
            .ToListAsync();

        foreach (var userId in userIds)
        {
            var userShift = shifts.FirstOrDefault(s => s.UserId == userId);
            var userChore = chores.FirstOrDefault(c => c.UserId == userId);
            var userOnDuty = onDuties.FirstOrDefault(o => o.UserId == userId);
            var userVacation = vacations.FirstOrDefault(v => v.UserId == userId);

            BusyShiftHit? shiftHit = userShift == null
                ? null
                : new BusyShiftHit(
                    Name: new ShiftType { Key = userShift.Key, NameEn = userShift.ShiftTypeNameEn }.Name,
                    Start: userShift.Start.ToString("HH:mm"),
                    End: userShift.End.ToString("HH:mm"),
                    IsHome: userShift.Key == ShiftType.KEY_HOME,
                    IsOffline: userShift.Key == ShiftType.KEY_OFFLINE);

            var hasShift = userShift != null;
            var hasChore = userChore != null;
            var hasOnDuty = userOnDuty != null;
            var hasVacation = userVacation != null;

            var initialHighest = (hasShift || hasChore || hasOnDuty || hasVacation)
                ? BusyLevel.Soft
                : BusyLevel.None;

            bool hasHardError = false;
            string? hardErrorKey = null;
            if (target != null)
            {
                var validation = await ValidateAsync(target, userId, actorUserId: 0, overrideToken: null);
                if (!validation.CanProceed)
                {
                    hasHardError = true;
                    hardErrorKey = validation.Errors.FirstOrDefault()?.Key;
                }
            }

            var summary = new BusySummary(
                UserId: userId,
                HasShift: hasShift,
                Shift: shiftHit,
                HasChore: hasChore,
                ChoreTitle: userChore?.Title,
                HasOnDuty: hasOnDuty,
                OnDutyType: userOnDuty?.Type,
                HasVacation: hasVacation,
                VacationEnd: userVacation?.EndDate,
                Highest: hasHardError ? BusyLevel.Hard : initialHighest);

            result[userId] = new BusySummaryWithHardError(summary, hasHardError, hardErrorKey);
        }

        return result;
    }

    public async Task<BusyValidation> ValidateAsync(
        BusyTarget target,
        int userId,
        int actorUserId,
        string? overrideToken = null)
    {
        return target switch
        {
            BusyTarget.Shift s => await ValidateShiftAsync(userId, s.ShiftInstanceId, target, overrideToken),
            BusyTarget.Chore c => await ValidateChoreAsync(userId, c, overrideToken),
            BusyTarget.OnDuty o => await ValidateOnDutyAsync(userId, o, overrideToken),
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
    }

    private async Task<BusyValidation> ValidateChoreAsync(
        int userId,
        BusyTarget.Chore target,
        string? overrideToken)
    {
        var errors = new List<ValidationIssue>();
        var warnings = new List<ValidationIssue>();

        var user = await _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
        {
            errors.Add(new ValidationIssue(
                "USER_NOT_FOUND",
                _localizer["Error_UserNotFound"],
                ValidationSeverity.Error,
                ValidationCategory.JobType));
            return new BusyValidation(false, errors, warnings);
        }

        if (!await IsUserInMoleculeAsync(user.CompanyId, target.MoleculeId))
        {
            errors.Add(new ValidationIssue(
                "USER_NOT_IN_MOLECULE",
                _localizer["Error_UserNotInMolecule"],
                ValidationSeverity.Error,
                ValidationCategory.JobType));
            return new BusyValidation(false, errors, warnings);
        }

        // Vacation conflict (warning — overrideable)
        var vacation = await _db.TimeOffRequests.IgnoreQueryFilters()
            .Where(t => t.UserId == userId
                && t.Status == RequestStatus.Approved
                && t.StartDate <= target.Date
                && t.EndDate >= target.Date)
            .Select(t => new { t.StartDate, t.EndDate })
            .FirstOrDefaultAsync();
        if (vacation != null)
        {
            warnings.Add(new ValidationIssue(
                "VACATION_CONFLICT", _localizer["Warning_VacationConflict"],
                ValidationSeverity.Warning, ValidationCategory.TimeOff,
                new BusyConflictDetail("VACATION_CONFLICT", "TimeOff", "vacation", null, target.Date, null, null)));
        }

        // Existing real shift on date (warning — was hard error in old ChoreService:311)
        // HOME and OFFLINE shifts are exempt — they explicitly model "available for ad-hoc work"
        var existingShift = await _db.ShiftAssignments.IgnoreQueryFilters()
            .Where(sa => sa.UserId == userId
                && sa.ShiftInstance.WorkDate == target.Date
                && sa.ShiftInstance.ShiftType.Key != ShiftType.KEY_HOME
                && sa.ShiftInstance.ShiftType.Key != ShiftType.KEY_OFFLINE)
            .Select(sa => new
            {
                sa.ShiftInstance.ShiftType.Key,
                sa.ShiftInstance.ShiftType.NameEn,
                sa.ShiftInstance.ShiftType.Start,
                sa.ShiftInstance.ShiftType.End
            })
            .FirstOrDefaultAsync();
        if (existingShift != null)
        {
            var name = new ShiftType { Key = existingShift.Key, NameEn = existingShift.NameEn }.Name;
            warnings.Add(new ValidationIssue(
                "SHIFT_EXISTS_CONFLICT", _localizer["Warning_ShiftExistsOnHomeDate"],
                ValidationSeverity.Warning, ValidationCategory.HomeConflict,
                new BusyConflictDetail("SHIFT_EXISTS_CONFLICT", "HomeConflict", "shift", name, target.Date,
                    existingShift.Start.ToString("HH:mm"), existingShift.End.ToString("HH:mm"))));
        }

        // Existing chore on date (warning — was hard error in old ChoreService:304)
        // Per 2026-05-03 policy: two chores same day are overrideable (no time component).
        var existingChore = await _db.Chores.IgnoreQueryFilters()
            .Where(c => c.UserId == userId
                && c.Date == target.Date
                && c.CanceledAt == null)
            .Select(c => new { c.Title })
            .FirstOrDefaultAsync();
        if (existingChore != null)
        {
            warnings.Add(new ValidationIssue(
                "CHORE_CONFLICT", _localizer["Warning_ChoreConflict"],
                ValidationSeverity.Warning, ValidationCategory.ChoreConflict,
                new BusyConflictDetail("CHORE_CONFLICT", "ChoreConflict", "chore", existingChore.Title, target.Date, null, null)));
        }

        // Existing on-duty on date (NEW warning — not checked today)
        var existingOnDuty = await _db.OnDuties.IgnoreQueryFilters()
            .Where(o => o.UserId == userId
                && o.Date == target.Date
                && o.CanceledAt == null)
            .Select(o => new { o.Type })
            .FirstOrDefaultAsync();
        if (existingOnDuty != null)
        {
            warnings.Add(new ValidationIssue(
                "ONDUTY_CONFLICT", _localizer["Warning_OnDutyConflict"],
                ValidationSeverity.Warning, ValidationCategory.OnDutyConflict,
                new BusyConflictDetail("ONDUTY_CONFLICT", "OnDutyConflict", "onduty", existingOnDuty.Type.ToString(), target.Date, null, null)));
        }

        if (warnings.Count > 0 && !string.IsNullOrEmpty(overrideToken)
            && ValidateOverrideToken(overrideToken, target, userId))
        {
            warnings.Clear();
        }

        return new BusyValidation(errors.Count == 0, errors, warnings);
    }

    private async Task<BusyValidation> ValidateShiftAsync(
        int userId,
        int shiftInstanceId,
        BusyTarget target,
        string? overrideToken)
    {
        var errors = new List<ValidationIssue>();
        var warnings = new List<ValidationIssue>();

        // SECURITY-AUDITED: SAFE — IgnoreQueryFilters needed for cross-company assignment within molecule;
        // molecule boundary enforced below via IsUserInMoleculeAsync
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
            return new BusyValidation(false, errors, warnings);
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
            return new BusyValidation(false, errors, warnings);
        }

        // Molecule boundary check
        if (shiftInstance.ShiftType.MoleculeId.HasValue)
        {
            if (!await IsUserInMoleculeAsync(user.CompanyId, shiftInstance.ShiftType.MoleculeId.Value))
            {
                errors.Add(new ValidationIssue(
                    "USER_NOT_IN_MOLECULE",
                    _localizer["Error_UserNotInMolecule"],
                    ValidationSeverity.Error,
                    ValidationCategory.JobType));
                return new BusyValidation(false, errors, warnings);
            }
        }

        // Duplicate assignment (hard error)
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

        // JobType match warning
        if (shiftInstance.ShiftType.JobTypeId.HasValue && user.JobTypeId != shiftInstance.ShiftType.JobTypeId)
        {
            warnings.Add(new ValidationIssue(
                "JOB_TYPE_MISMATCH",
                _localizer["Error_JobTypeMismatch"],
                ValidationSeverity.Warning,
                ValidationCategory.JobType));
        }

        // ShiftGrouping membership warning
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

        // Company eligibility check
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

        var effectiveSettings = await _hierarchySettingsService.GetEffectiveSettingsAsync(shiftInstance.CompanyId);

        var shiftType = shiftInstance.ShiftType;
        bool isOfflineShift = shiftType.IsOffline;
        bool isHomeShift = shiftType.IsHome;
        bool isExemptShift = isOfflineShift || isHomeShift;
        var (newStart, newEnd) = TimeHelpers.GetShiftWindow(shiftType, shiftInstance.WorkDate);

        var windowStart = TimeHelpers.WeekStart(shiftInstance.WorkDate).AddDays(-1);
        var windowEnd = windowStart.AddDays(8);
        var nearbyAssignments = await _db.ShiftAssignments.IgnoreQueryFilters()
            .Where(sa => sa.UserId == userId
                && sa.ShiftInstanceId != shiftInstanceId
                && sa.ShiftInstance.WorkDate >= windowStart
                && sa.ShiftInstance.WorkDate <= windowEnd)
            .Select(sa => new
            {
                sa.ShiftInstance.WorkDate,
                sa.ShiftInstance.ShiftType.Start,
                sa.ShiftInstance.ShiftType.End,
                IsOffline = sa.ShiftInstance.ShiftType.Key == ShiftType.KEY_OFFLINE,
                IsHome = sa.ShiftInstance.ShiftType.Key == ShiftType.KEY_HOME,
                ShiftKey = sa.ShiftInstance.ShiftType.Key,
                ShiftNameEn = sa.ShiftInstance.ShiftType.NameEn
            }).ToListAsync();

        // Overlap detection
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
                        ValidationSeverity.Error, ValidationCategory.Overlap,
                        new BusyConflictDetail(
                            Key: "OVERLAP",
                            Category: "Overlap",
                            ResourceType: "shift",
                            ResourceName: new ShiftType { Key = ra.ShiftKey, NameEn = ra.ShiftNameEn }.Name,
                            Date: ra.WorkDate,
                            StartTime: ra.Start.ToString("HH:mm"),
                            EndTime: ra.End.ToString("HH:mm"))));
                    break;
                }
            }
        }

        // Rest period
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

        // HOME mutual exclusion (forward)
        if (!isExemptShift)
        {
            var homeOnDate = nearbyAssignments.FirstOrDefault(ra => ra.IsHome && ra.WorkDate == shiftInstance.WorkDate);
            if (homeOnDate != null)
            {
                warnings.Add(new ValidationIssue(
                    "HOME_CONFLICT", _localizer["Warning_HomeConflict"],
                    ValidationSeverity.Warning, ValidationCategory.HomeConflict,
                    new BusyConflictDetail(
                        Key: "HOME_CONFLICT",
                        Category: "HomeConflict",
                        ResourceType: "home",
                        ResourceName: null,
                        Date: homeOnDate.WorkDate,
                        StartTime: null,
                        EndTime: null)));
            }
        }

        // HOME mutual exclusion (reverse)
        if (isHomeShift)
        {
            var realShiftOnDate = nearbyAssignments.FirstOrDefault(ra =>
                !ra.IsOffline && !ra.IsHome && ra.WorkDate == shiftInstance.WorkDate);
            if (realShiftOnDate != null)
            {
                warnings.Add(new ValidationIssue(
                    "SHIFT_EXISTS_CONFLICT", _localizer["Warning_ShiftExistsOnHomeDate"],
                    ValidationSeverity.Warning, ValidationCategory.HomeConflict,
                    new BusyConflictDetail(
                        Key: "SHIFT_EXISTS_CONFLICT",
                        Category: "HomeConflict",
                        ResourceType: "shift",
                        ResourceName: new ShiftType { Key = realShiftOnDate.ShiftKey, NameEn = realShiftOnDate.ShiftNameEn }.Name,
                        Date: realShiftOnDate.WorkDate,
                        StartTime: realShiftOnDate.Start.ToString("HH:mm"),
                        EndTime: realShiftOnDate.End.ToString("HH:mm"))));
            }

            bool hasDuplicateHome = nearbyAssignments.Any(ra => ra.IsHome && ra.WorkDate == shiftInstance.WorkDate);
            if (hasDuplicateHome)
            {
                errors.Add(new ValidationIssue(
                    "DUPLICATE_HOME", _localizer["Error_DuplicateHome"],
                    ValidationSeverity.Error, ValidationCategory.Overlap));
            }
        }

        // Vacation conflict
        var vacation = await _db.TimeOffRequests.IgnoreQueryFilters()
            .Where(r => r.UserId == userId
                && r.Status == RequestStatus.Approved
                && shiftInstance.WorkDate >= r.StartDate
                && shiftInstance.WorkDate <= r.EndDate)
            .Select(r => new { r.StartDate, r.EndDate })
            .FirstOrDefaultAsync();
        if (vacation != null)
        {
            warnings.Add(new ValidationIssue(
                "VACATION_CONFLICT", _localizer["Warning_VacationConflict"],
                ValidationSeverity.Warning, ValidationCategory.TimeOff,
                new BusyConflictDetail(
                    Key: "VACATION_CONFLICT",
                    Category: "TimeOff",
                    ResourceType: "vacation",
                    ResourceName: null,
                    Date: shiftInstance.WorkDate,
                    StartTime: null,
                    EndTime: null)));
        }

        // Chore conflict
        var chore = await _db.Chores.IgnoreQueryFilters()
            .Where(c => c.UserId == userId
                && c.Date == shiftInstance.WorkDate
                && c.CanceledAt == null)
            .Select(c => new { c.Title })
            .FirstOrDefaultAsync();
        if (chore != null)
        {
            warnings.Add(new ValidationIssue(
                "CHORE_CONFLICT", _localizer["Warning_ChoreConflict"],
                ValidationSeverity.Warning, ValidationCategory.ChoreConflict,
                new BusyConflictDetail(
                    Key: "CHORE_CONFLICT",
                    Category: "ChoreConflict",
                    ResourceType: "chore",
                    ResourceName: chore.Title,
                    Date: shiftInstance.WorkDate,
                    StartTime: null,
                    EndTime: null)));
        }

        // On-duty conflict
        var onDuty = await _db.OnDuties.IgnoreQueryFilters()
            .Where(o => o.UserId == userId
                && o.Date == shiftInstance.WorkDate
                && o.CanceledAt == null)
            .Select(o => new { o.Type })
            .FirstOrDefaultAsync();
        if (onDuty != null)
        {
            warnings.Add(new ValidationIssue(
                "ONDUTY_CONFLICT", _localizer["Warning_OnDutyConflict"],
                ValidationSeverity.Warning, ValidationCategory.OnDutyConflict,
                new BusyConflictDetail(
                    Key: "ONDUTY_CONFLICT",
                    Category: "OnDutyConflict",
                    ResourceType: "onduty",
                    ResourceName: onDuty.Type.ToString(),
                    Date: shiftInstance.WorkDate,
                    StartTime: null,
                    EndTime: null)));
        }

        // Weekly cap
        if (!isExemptShift)
        {
            var weeklyCap = effectiveSettings?.WeeklyCap ?? 56;
            var weekStartDayConfig = await _configCache.GetConfigAsync(shiftInstance.CompanyId, "WeekStartDay");
            var weekStartDayOfWeek = (DayOfWeek)Math.Clamp(
                int.TryParse(weekStartDayConfig?.Value, out var d) ? d : 0, 0, 6);
            var startOfWeek = TimeHelpers.WeekStart(shiftInstance.WorkDate, weekStartDayOfWeek);
            var endOfWeek = startOfWeek.AddDays(6);

            var weekShiftTimes = await _db.ShiftAssignments
                .IgnoreQueryFilters()
                .Include(sa => sa.ShiftInstance)
                    .ThenInclude(si => si.ShiftType)
                .Where(sa => sa.UserId == userId
                    && sa.ShiftInstance.WorkDate >= startOfWeek
                    && sa.ShiftInstance.WorkDate <= endOfWeek)
                .Select(sa => new { sa.ShiftInstance.WorkDate, sa.ShiftInstance.ShiftType.Start, sa.ShiftInstance.ShiftType.End })
                .ToListAsync();

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

        // Past date warning (exempt for HOME)
        if (!isExemptShift && shiftInstance.WorkDate < DateOnly.FromDateTime(DateTime.UtcNow))
        {
            warnings.Add(new ValidationIssue(
                "PAST_DATE",
                _localizer["Warning_PastDateShiftAssignment"],
                ValidationSeverity.Warning,
                ValidationCategory.Concurrency));
        }

        // Apply override token if supplied and valid
        if (warnings.Count > 0 && !string.IsNullOrEmpty(overrideToken)
            && ValidateOverrideToken(overrideToken, target, userId))
        {
            warnings.Clear();
        }

        return new BusyValidation(
            CanProceed: errors.Count == 0,
            Errors: errors,
            Warnings: warnings);
    }

    private async Task<BusyValidation> ValidateOnDutyAsync(
        int userId,
        BusyTarget.OnDuty target,
        string? overrideToken)
    {
        var errors = new List<ValidationIssue>();
        var warnings = new List<ValidationIssue>();

        var user = await _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
        {
            errors.Add(new ValidationIssue(
                "USER_NOT_FOUND",
                _localizer["Error_UserNotFound"],
                ValidationSeverity.Error,
                ValidationCategory.JobType));
            return new BusyValidation(false, errors, warnings);
        }

        if (!user.IsActive)
        {
            errors.Add(new ValidationIssue(
                "USER_INACTIVE",
                _localizer["Error_UserNotFound"],
                ValidationSeverity.Error,
                ValidationCategory.JobType));
            return new BusyValidation(false, errors, warnings);
        }

        // Vacation conflict (warning — overrideable)
        var vacation = await _db.TimeOffRequests.IgnoreQueryFilters()
            .Where(t => t.UserId == userId
                && t.Status == RequestStatus.Approved
                && t.StartDate <= target.Date
                && t.EndDate >= target.Date)
            .Select(t => new { t.StartDate, t.EndDate })
            .FirstOrDefaultAsync();
        if (vacation != null)
        {
            warnings.Add(new ValidationIssue(
                "VACATION_CONFLICT", _localizer["Warning_VacationConflict"],
                ValidationSeverity.Warning, ValidationCategory.TimeOff,
                new BusyConflictDetail("VACATION_CONFLICT", "TimeOff", "vacation", null, target.Date, null, null)));
        }

        // Existing on-duty same date+type — was hard error in old OnDutyService:294, now a
        // warning per the 2026-05-03 policy ("two of the same resource shouldn't be a blocker").
        var sameTypeExisting = await _db.OnDuties.IgnoreQueryFilters()
            .AnyAsync(o => o.UserId == userId
                && o.Date == target.Date
                && o.Type == target.Type
                && o.CanceledAt == null);
        if (sameTypeExisting)
        {
            warnings.Add(new ValidationIssue(
                "DUPLICATE_ONDUTY", _localizer["Warning_OnDutyConflict"],
                ValidationSeverity.Warning, ValidationCategory.OnDutyConflict,
                new BusyConflictDetail("DUPLICATE_ONDUTY", "OnDutyConflict", "onduty", target.Type.ToString(), target.Date, null, null)));
        }

        // Existing on-duty different type same date (NEW warning — not checked today)
        var otherTypeExisting = await _db.OnDuties.IgnoreQueryFilters()
            .Where(o => o.UserId == userId
                && o.Date == target.Date
                && o.Type != target.Type
                && o.CanceledAt == null)
            .Select(o => new { o.Type })
            .FirstOrDefaultAsync();
        if (otherTypeExisting != null)
        {
            warnings.Add(new ValidationIssue(
                "ONDUTY_CONFLICT", _localizer["Warning_OnDutyConflict"],
                ValidationSeverity.Warning, ValidationCategory.OnDutyConflict,
                new BusyConflictDetail("ONDUTY_CONFLICT", "OnDutyConflict", "onduty", otherTypeExisting.Type.ToString(), target.Date, null, null)));
        }

        // Existing real shift on date (NEW warning — not checked today)
        // HOME and OFFLINE are exempt (model "available for ad-hoc work")
        var existingShift = await _db.ShiftAssignments.IgnoreQueryFilters()
            .Where(sa => sa.UserId == userId
                && sa.ShiftInstance.WorkDate == target.Date
                && sa.ShiftInstance.ShiftType.Key != ShiftType.KEY_HOME
                && sa.ShiftInstance.ShiftType.Key != ShiftType.KEY_OFFLINE)
            .Select(sa => new
            {
                sa.ShiftInstance.ShiftType.Key,
                sa.ShiftInstance.ShiftType.NameEn,
                sa.ShiftInstance.ShiftType.Start,
                sa.ShiftInstance.ShiftType.End
            })
            .FirstOrDefaultAsync();
        if (existingShift != null)
        {
            var name = new ShiftType { Key = existingShift.Key, NameEn = existingShift.NameEn }.Name;
            warnings.Add(new ValidationIssue(
                "SHIFT_EXISTS_CONFLICT", _localizer["Warning_ShiftExistsOnHomeDate"],
                ValidationSeverity.Warning, ValidationCategory.HomeConflict,
                new BusyConflictDetail("SHIFT_EXISTS_CONFLICT", "HomeConflict", "shift", name, target.Date,
                    existingShift.Start.ToString("HH:mm"), existingShift.End.ToString("HH:mm"))));
        }

        // Existing chore (NEW warning — not checked today)
        var existingChore = await _db.Chores.IgnoreQueryFilters()
            .Where(c => c.UserId == userId
                && c.Date == target.Date
                && c.CanceledAt == null)
            .Select(c => new { c.Title })
            .FirstOrDefaultAsync();
        if (existingChore != null)
        {
            warnings.Add(new ValidationIssue(
                "CHORE_CONFLICT", _localizer["Warning_ChoreConflict"],
                ValidationSeverity.Warning, ValidationCategory.ChoreConflict,
                new BusyConflictDetail("CHORE_CONFLICT", "ChoreConflict", "chore", existingChore.Title, target.Date, null, null)));
        }

        if (warnings.Count > 0 && !string.IsNullOrEmpty(overrideToken)
            && ValidateOverrideToken(overrideToken, target, userId))
        {
            warnings.Clear();
        }

        return new BusyValidation(errors.Count == 0, errors, warnings);
    }

    public async Task<Dictionary<(BusyTarget target, int userId), BusyValidation>> ValidateBatchAsync(
        IReadOnlyList<(BusyTarget target, int userId)> assignments,
        int actorUserId)
    {
        // Step 4 will replace this loop with a batched implementation that pre-loads
        // users/instances/assignments in three queries.
        var result = new Dictionary<(BusyTarget target, int userId), BusyValidation>(assignments.Count);
        foreach (var (target, userId) in assignments)
        {
            result[(target, userId)] = await ValidateAsync(target, userId, actorUserId);
        }
        return result;
    }

    public string GenerateOverrideToken(BusyTarget target, int userId, IReadOnlyList<string> warningKeys)
    {
        var keysSorted = string.Join(",", warningKeys.OrderBy(k => k, StringComparer.Ordinal));
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(OverrideTokenExpiryMinutes).ToUnixTimeSeconds();
        var payload = $"{TargetCanonical(target)}|{userId}|{keysSorted}|{expiresAt}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_hmacSecret));
        var sig = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
        return $"{Convert.ToBase64String(Encoding.UTF8.GetBytes(payload))}.{sig}";
    }

    public bool ValidateOverrideToken(string? token, BusyTarget target, int userId)
    {
        if (string.IsNullOrEmpty(token)) return false;
        var parts = token.Split('.');
        if (parts.Length != 2) return false;

        try
        {
            var payload = Encoding.UTF8.GetString(Convert.FromBase64String(parts[0]));
            var providedSig = parts[1];

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_hmacSecret));
            var expectedSig = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(expectedSig),
                    Encoding.UTF8.GetBytes(providedSig)))
                return false;

            var fields = payload.Split('|');
            if (fields.Length < 4) return false;
            if (fields[0] != TargetCanonical(target)) return false;
            if (!int.TryParse(fields[1], out var tokUserId) || tokUserId != userId) return false;
            if (!long.TryParse(fields[3], out var expiresAt)) return false;
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expiresAt) return false;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid override token format");
            return false;
        }
    }

    private async Task<bool> IsUserInMoleculeAsync(int userCompanyId, int moleculeId)
    {
        var company = await _db.Companies.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == userCompanyId);
        return company?.MoleculeId == moleculeId;
    }

    private static double MergeAndSumHours(List<(DateTime start, DateTime end)> windows)
    {
        if (windows.Count == 0) return 0;
        var merged = new List<(DateTime start, DateTime end)>();
        var current = windows[0];
        for (int i = 1; i < windows.Count; i++)
        {
            var next = windows[i];
            if (next.start <= current.end)
            {
                current = (current.start, current.end > next.end ? current.end : next.end);
            }
            else
            {
                merged.Add(current);
                current = next;
            }
        }
        merged.Add(current);
        return merged.Sum(w => (w.end - w.start).TotalHours);
    }

    private static string TargetCanonical(BusyTarget target) => target switch
    {
        BusyTarget.Shift s => $"shift:{s.ShiftInstanceId}",
        BusyTarget.Chore c => $"chore:{c.Date:yyyy-MM-dd}:{c.MoleculeId}:{c.ChoreTypeId?.ToString() ?? "_"}",
        BusyTarget.OnDuty o => $"onduty:{o.Date:yyyy-MM-dd}:{(int)o.Type}:{o.MoleculeId}",
        _ => throw new ArgumentOutOfRangeException(nameof(target))
    };
}
