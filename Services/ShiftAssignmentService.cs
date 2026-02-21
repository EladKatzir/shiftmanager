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
    private readonly ITechShiftService _techShiftService;
    private readonly IAuditLogService _auditLogService;
    private readonly string _hmacSecret;

    private const int OverrideTokenExpiryMinutes = 5;

    public ShiftAssignmentService(
        AppDbContext db,
        IStringLocalizer<SharedResources> localizer,
        ILogger<ShiftAssignmentService> logger,
        IHierarchySettingsService hierarchySettingsService,
        ITechShiftService techShiftService,
        IAuditLogService auditLogService,
        IConfiguration configuration)
    {
        _db = db;
        _localizer = localizer;
        _logger = logger;
        _hierarchySettingsService = hierarchySettingsService;
        _techShiftService = techShiftService;
        _auditLogService = auditLogService;
        _hmacSecret = configuration["ApiKeyHmacSecret"]
            ?? Middleware.ApiAuthenticationMiddleware.HmacSecret;
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

        var user = await _db.Users
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

        var shiftInstance = await _db.ShiftInstances
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

        // Check duplicate assignment (hard error)
        var alreadyAssigned = await _db.ShiftAssignments
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

        // Check tech shift eligibility (warning — overrideable)
        if (!string.IsNullOrEmpty(shiftInstance.ShiftType.TechShiftType))
        {
            var isEligible = await _techShiftService.IsUserEligibleForTechShiftAsync(userId, shiftInstance.ShiftType.TechShiftType);
            if (!isEligible)
            {
                warnings.Add(new ValidationIssue(
                    "TECH_SHIFT_INELIGIBLE",
                    _localizer["Error_TechShiftIneligible"],
                    ValidationSeverity.Warning,
                    ValidationCategory.TechShift));
            }
        }

        // Check weekly cap using configurable settings from hierarchy
        var effectiveSettings = await _hierarchySettingsService.GetEffectiveSettingsAsync(shiftInstance.CompanyId);
        var weeklyCap = effectiveSettings?.WeeklyCap ?? 48;

        var startOfWeek = GetStartOfWeek(shiftInstance.WorkDate);
        var endOfWeek = startOfWeek.AddDays(7);

        var weekShiftTimes = await _db.ShiftAssignments
            .Include(sa => sa.ShiftInstance)
                .ThenInclude(si => si.ShiftType)
            .Where(sa => sa.UserId == userId
                && sa.ShiftInstance.WorkDate >= startOfWeek
                && sa.ShiftInstance.WorkDate < endOfWeek)
            .Select(sa => new { sa.ShiftInstance.ShiftType.Start, sa.ShiftInstance.ShiftType.End })
            .ToListAsync();
        var weeklyHours = weekShiftTimes.Sum(s => GetShiftHours(s.Start, s.End));

        var shiftHours = GetShiftHours(shiftInstance.ShiftType.Start, shiftInstance.ShiftType.End);
        if ((weeklyHours + shiftHours) > weeklyCap)
        {
            warnings.Add(new ValidationIssue(
                "EXCEEDS_WEEKLY_CAP",
                _localizer["Error_ExceedsWeeklyCap"],
                ValidationSeverity.Warning,
                ValidationCategory.WeeklyHours));
        }

        // Check rest hours using configurable value from hierarchy settings
        var restHoursRequired = effectiveSettings?.RestHours ?? 11;
        if (await CheckRestHoursViolationAsync(userId, shiftInstance, restHoursRequired))
        {
            warnings.Add(new ValidationIssue(
                "REST_HOURS_VIOLATION",
                _localizer["Error_RestHoursViolation"],
                ValidationSeverity.Warning,
                ValidationCategory.RestHours));
        }

        // FINDING-003 FIX: Warn when assigning to past dates (back-fill allowed via override)
        if (shiftInstance.WorkDate < DateOnly.FromDateTime(DateTime.UtcNow))
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

        var assignment = await _db.ShiftAssignments
            .Include(a => a.ShiftInstance)
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

        var trainee = await _db.Users.FindAsync(traineeUserId);
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

        // Verify same company
        if (assignment.CompanyId != trainee.CompanyId)
        {
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

        var shiftInstance = await _db.ShiftInstances.FindAsync(shiftInstanceId);
        if (shiftInstance == null)
        {
            return new ShiftAssignmentResult(false, null, "SHIFT_NOT_FOUND", _localizer["Error_ShiftNotFound"]);
        }

        // Wrap check-then-act in transaction to reduce race window for concurrent assignments
        using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            // Check if already assigned (re-check inside transaction)
            var existingAssignment = await _db.ShiftAssignments
                .FirstOrDefaultAsync(sa => sa.ShiftInstanceId == shiftInstanceId && sa.UserId == userId);

            if (existingAssignment != null)
            {
                await transaction.RollbackAsync();
                return new ShiftAssignmentResult(false, existingAssignment.Id, "ALREADY_ASSIGNED", _localizer["Error_AlreadyAssigned"]);
            }

            // Check capacity
            var currentAssignedCount = await _db.ShiftAssignments
                .CountAsync(sa => sa.ShiftInstanceId == shiftInstanceId && sa.UserId != null);
            if (currentAssignedCount >= shiftInstance.StaffingRequired)
            {
                await transaction.RollbackAsync();
                return new ShiftAssignmentResult(false, null, "SHIFT_FULLY_STAFFED", _localizer["Error_ShiftFullyStaffed"]);
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
            var assignment = await _db.ShiftAssignments
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
        var shiftInstanceIds = assignmentList.Select(a => a.shiftInstanceId).Distinct().ToList();
        var shiftInstances = await _db.ShiftInstances
            .Include(si => si.ShiftType)
                .ThenInclude(st => st.ShiftGrouping)
                    .ThenInclude(sg => sg!.Companies)
            .Where(si => shiftInstanceIds.Contains(si.Id))
            .ToDictionaryAsync(si => si.Id);

        // Pre-load all users in one query
        var userIds = assignmentList.Select(a => a.userId).Distinct().ToList();
        var users = await _db.Users
            .Include(u => u.JobType)
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id);

        // Pre-load all existing assignments for these shifts
        var existingAssignments = await _db.ShiftAssignments
            .Where(sa => shiftInstanceIds.Contains(sa.ShiftInstanceId))
            .Select(sa => new { sa.ShiftInstanceId, sa.UserId })
            .ToListAsync();

        var existingSet = existingAssignments
            .Where(ea => ea.UserId.HasValue)
            .Select(ea => (ea.ShiftInstanceId, ea.UserId!.Value))
            .ToHashSet();

        // Pre-load weekly hours for all users
        var anyDate = shiftInstances.Values.FirstOrDefault()?.WorkDate ?? DateOnly.FromDateTime(DateTime.Today);
        var startOfWeek = GetStartOfWeek(anyDate);
        var endOfWeek = startOfWeek.AddDays(7);

        var weekShiftData = await _db.ShiftAssignments
            .Include(sa => sa.ShiftInstance)
                .ThenInclude(si => si.ShiftType)
            .Where(sa => userIds.Contains(sa.UserId ?? 0)
                && sa.ShiftInstance.WorkDate >= startOfWeek
                && sa.ShiftInstance.WorkDate < endOfWeek)
            .Select(sa => new { sa.UserId, sa.ShiftInstance.ShiftType.Start, sa.ShiftInstance.ShiftType.End })
            .ToListAsync();

        var weeklyHoursByUser = weekShiftData
            .GroupBy(w => w.UserId ?? 0)
            .ToDictionary(g => g.Key, g => g.Sum(s => GetShiftHours(s.Start, s.End)));

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

            // Tech shift eligibility (warning — overrideable)
            if (!string.IsNullOrEmpty(shiftInstance.ShiftType.TechShiftType))
            {
                var isEligible = await _techShiftService.IsUserEligibleForTechShiftAsync(userId, shiftInstance.ShiftType.TechShiftType);
                if (!isEligible)
                {
                    warnings.Add(new ValidationIssue("TECH_SHIFT_INELIGIBLE", _localizer["Error_TechShiftIneligible"], ValidationSeverity.Warning, ValidationCategory.TechShift));
                }
            }

            // Weekly cap (using pre-loaded data)
            var effectiveSettings = await _hierarchySettingsService.GetEffectiveSettingsAsync(shiftInstance.CompanyId);
            var weeklyCap = effectiveSettings?.WeeklyCap ?? 48;
            var userWeeklyHours = weeklyHoursByUser.GetValueOrDefault(userId, 0.0);
            var shiftHours = GetShiftHours(shiftInstance.ShiftType.Start, shiftInstance.ShiftType.End);
            if ((userWeeklyHours + shiftHours) > weeklyCap)
            {
                warnings.Add(new ValidationIssue("EXCEEDS_WEEKLY_CAP", _localizer["Error_ExceedsWeeklyCap"], ValidationSeverity.Warning, ValidationCategory.WeeklyHours));
            }

            // Note: rest hours check skipped in batch (would require N additional queries per assignment)
            // FillRange typically copies staffing structure, not specific user assignments

            result[(shiftInstanceId, userId)] = new ShiftAssignmentValidation(errors.Count == 0, errors, warnings);
        }

        return result;
    }

    private async Task<bool> CheckRestHoursViolationAsync(int userId, ShiftInstance shiftInstance, int restHoursRequired)
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

        var thisShiftStart = shiftInstance.WorkDate.ToDateTime(shiftInstance.ShiftType.Start);

        foreach (var assignment in nearbyAssignments)
        {
            var otherShiftEnd = GetShiftEndDateTime(
                assignment.ShiftInstance.WorkDate,
                assignment.ShiftInstance.ShiftType.Start,
                assignment.ShiftInstance.ShiftType.End);

            var restHours = (thisShiftStart - otherShiftEnd).TotalHours;

            if (restHours > 0 && restHours < restHoursRequired)
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
