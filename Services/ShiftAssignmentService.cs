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
    private readonly IBusyService _busyService;

    private const int OverrideTokenExpiryMinutes = 5;

    public ShiftAssignmentService(
        AppDbContext db,
        IStringLocalizer<SharedResources> localizer,
        ILogger<ShiftAssignmentService> logger,
        IHierarchySettingsService hierarchySettingsService,
        IAuditLogService auditLogService,
        IConfiguration configuration,
        IAppConfigCacheService configCache,
        IBusyService busyService)
    {
        _db = db;
        _localizer = localizer;
        _logger = logger;
        _hierarchySettingsService = hierarchySettingsService;
        _auditLogService = auditLogService;
        _hmacSecret = configuration["ApiKeyHmacSecret"]
            ?? Middleware.ApiAuthenticationMiddleware.HmacSecret;
        _configCache = configCache;
        _busyService = busyService;
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
        int? shiftGroupingId = null,
        bool categoryFilter = false)
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

        List<int> participantUserIds;
        if (categoryFilter)
        {
            // NEW (3b): category-membership eligibility. Drop the jobType filter; gate by DoesShifts
            // (per-company membership, mirror fallback) + the shift's single category.
            // SECURITY-AUDITED: SAFE — re-scoped by companyIds (grouping-derived or shift type's company).
            // Per-company DoesShifts participants: membership row with DoesShifts=true in any candidate
            // company, OR no membership row in candidate companies AND mirrored AppUser.DoesShifts.
            var doersFromMembership = (await _db.CompanyMemberships
                .Where(m => companyIds.Contains(m.CompanyId) && m.DoesShifts)
                .Select(m => m.UserId).ToListAsync()).ToHashSet();
            var anyMembershipUserIds = (await _db.CompanyMemberships
                .Where(m => companyIds.Contains(m.CompanyId))
                .Select(m => m.UserId).ToListAsync()).ToHashSet();

            var candidates = await _db.Users
                .IgnoreQueryFilters()
                .Where(u => u.IsActive
                            && u.AccountType != AccountType.GroupUser
                            && companyIds.Contains(u.CompanyId))
                .Select(u => new { u.Id, u.DoesShifts })
                .ToListAsync();

            participantUserIds = candidates
                .Where(u => doersFromMembership.Contains(u.Id)
                            || (!anyMembershipUserIds.Contains(u.Id) && u.DoesShifts))
                .Select(u => u.Id)
                .ToList();

            var catId = shiftType.CategoryId;
            if (catId.HasValue)
            {
                var membersOfCat = (await _db.UserShiftCategories
                    .Where(m => m.ShiftCategoryId == catId.Value && participantUserIds.Contains(m.UserId))
                    .Select(m => m.UserId).ToListAsync()).ToHashSet();
                participantUserIds = participantUserIds.Where(id => membersOfCat.Contains(id)).ToList();
            }
            // catId == null -> all participants (the runtime D1 shared-shift fallback)
        }
        else
        {
            // LEGACY (behavior-preserving): job-type filter exactly as before.
            // SECURITY-AUDITED: SAFE — re-scoped by ShiftGrouping-derived companyIds or shift type's own companyId
            var legacyQuery = _db.Users
                .IgnoreQueryFilters()
                .Where(u => u.IsActive && companyIds.Contains(u.CompanyId));
            if (effectiveJobTypeId.HasValue)
                legacyQuery = legacyQuery.Where(u => u.JobTypeId == effectiveJobTypeId.Value);
            participantUserIds = await legacyQuery.Select(u => u.Id).ToListAsync();
        }

        var users = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => participantUserIds.Contains(u.Id))
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
        var validation = await _busyService.ValidateAsync(
            new BusyTarget.Shift(shiftInstanceId), userId, actorUserId: 0, overrideToken: null);
        return new ShiftAssignmentValidation(validation.CanProceed, validation.Errors, validation.Warnings);
    }


    public async Task<ShiftAssignmentValidation> ValidateTraineeAssignmentAsync(int traineeUserId, int assignmentId)
    {
        // SECURITY-AUDITED: SAFE — IgnoreQueryFilters needed for cross-company assignment lookup within molecule
        var assignment = await _db.ShiftAssignments
            .IgnoreQueryFilters()
            .Include(a => a.ShiftInstance)
                .ThenInclude(si => si.ShiftType)
            .FirstOrDefaultAsync(a => a.Id == assignmentId);

        if (assignment == null)
        {
            return new ShiftAssignmentValidation(false, new List<ValidationIssue>
            {
                new("ASSIGNMENT_NOT_FOUND", _localizer["Error_ShiftNotFound"],
                    ValidationSeverity.Error, ValidationCategory.Trainee)
            }, Array.Empty<ValidationIssue>());
        }

        return await ValidateTraineeCoreAsync(
            traineeUserId,
            primaryUserId: assignment.UserId,
            cellCompanyId: assignment.CompanyId,
            cellMoleculeId: assignment.ShiftInstance?.ShiftType?.MoleculeId);
    }

    public async Task<ShiftAssignmentValidation> ValidateTraineeAssignmentAsync(int traineeUserId, int primaryUserId, int shiftInstanceId)
    {
        // Overload for Draft Mode commit (sub-project A, G4): a freshly reconciled slot may be UNSAVED, so we
        // key off the ShiftInstance + primary rather than a persisted assignmentId.
        // SECURITY-AUDITED: SAFE — lookup by unique id; IgnoreQueryFilters needed for cross-company within molecule.
        var instance = await _db.ShiftInstances
            .IgnoreQueryFilters()
            .Include(si => si.ShiftType)
            .FirstOrDefaultAsync(si => si.Id == shiftInstanceId);

        if (instance == null)
        {
            return new ShiftAssignmentValidation(false, new List<ValidationIssue>
            {
                new("ASSIGNMENT_NOT_FOUND", _localizer["Error_ShiftNotFound"],
                    ValidationSeverity.Error, ValidationCategory.Trainee)
            }, Array.Empty<ValidationIssue>());
        }

        return await ValidateTraineeCoreAsync(
            traineeUserId,
            primaryUserId: primaryUserId,
            cellCompanyId: instance.CompanyId,
            cellMoleculeId: instance.ShiftType?.MoleculeId);
    }

    /// <summary>
    /// Shared trainee-validation rules (self-training / role / molecule-or-company). Both public overloads
    /// resolve the cell's company + molecule and the primary, then delegate here.
    /// </summary>
    private async Task<ShiftAssignmentValidation> ValidateTraineeCoreAsync(
        int traineeUserId, int? primaryUserId, int cellCompanyId, int? cellMoleculeId)
    {
        var errors = new List<ValidationIssue>();
        var warnings = new List<ValidationIssue>();

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
        if (primaryUserId == traineeUserId)
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
        if (cellMoleculeId is int moleculeId)
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
        else if (cellCompanyId != trainee.CompanyId)
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
        var pairs = assignments.ToList();
        var batch = pairs
            .Select(p => ((BusyTarget)new BusyTarget.Shift(p.shiftInstanceId), p.userId))
            .ToList();

        var batchResult = await _busyService.ValidateBatchAsync(batch, actorUserId: 0);

        var result = new Dictionary<(int shiftInstanceId, int userId), ShiftAssignmentValidation>(pairs.Count);
        foreach (var (shiftInstanceId, userId) in pairs)
        {
            var key = ((BusyTarget)new BusyTarget.Shift(shiftInstanceId), userId);
            var v = batchResult[key];
            result[(shiftInstanceId, userId)] = new ShiftAssignmentValidation(v.CanProceed, v.Errors, v.Warnings);
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
