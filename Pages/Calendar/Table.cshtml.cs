using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Hubs;
using ShiftManager.Resources;
using ShiftManager.Services;
using ShiftManager.Services.Remediation;
using System.Security.Claims;
using System.Text.Json;

namespace ShiftManager.Pages.Calendar;

[Authorize(Policy = "Grant:ManagerHomeAccess")]
public partial class TableModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ICompanyContext _companyContext;
    private readonly ILogger<TableModel> _logger;
    private readonly IBusyUserService _busyUserService;
    private readonly IShiftTypeCacheService _shiftTypeCache;
    private readonly IShiftProgramService _programService;
    private readonly IShiftAssignmentService _assignmentService;
    private readonly ICalendarNotificationService _calendarNotification;
    private readonly IConcurrencyService _concurrencyService;
    private readonly IGrantService _grantService;
    private readonly IJobTypeService _jobTypeService;
    private readonly ICompanyLocalizationService _companyLocalizationService;
    private readonly ITenantResolver _tenantResolver;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly IAuditLogService _auditLogService;

    public TableModel(
        AppDbContext db,
        ICompanyContext companyContext,
        ILogger<TableModel> logger,
        IBusyUserService busyUserService,
        IShiftTypeCacheService shiftTypeCache,
        IShiftProgramService programService,
        IShiftAssignmentService assignmentService,
        ICalendarNotificationService calendarNotification,
        IConcurrencyService concurrencyService,
        IGrantService grantService,
        IJobTypeService jobTypeService,
        ICompanyLocalizationService companyLocalizationService,
        ITenantResolver tenantResolver,
        IStringLocalizer<SharedResources> localizer,
        IAuditLogService auditLogService,
        IDraftModeService draftService,
        IDraftLifecycle draftLifecycle,
        IShiftTabService shiftTabService,
        IFailureRemediationService remediation)
    {
        _db = db;
        _remediation = remediation;
        _companyContext = companyContext;
        _logger = logger;
        _busyUserService = busyUserService;
        _shiftTypeCache = shiftTypeCache;
        _programService = programService;
        _assignmentService = assignmentService;
        _calendarNotification = calendarNotification;
        _concurrencyService = concurrencyService;
        _grantService = grantService;
        _jobTypeService = jobTypeService;
        _companyLocalizationService = companyLocalizationService;
        _tenantResolver = tenantResolver;
        _localizer = localizer;
        _auditLogService = auditLogService;
        _draftService = draftService;
        _draftLifecycle = draftLifecycle;
        _shiftTabService = shiftTabService;
    }

    private readonly IDraftModeService _draftService;
    private readonly IDraftLifecycle _draftLifecycle;
    private readonly IShiftTabService _shiftTabService;
    private readonly IFailureRemediationService _remediation;

    /// <summary>Builds the "go fix it" remediation payload for a failure key, or null if none.</summary>
    private object? MakeFix(string? key, int? userId)
    {
        var fix = _remediation.For(key, userId);
        return fix is null ? null : new { fix.Label, fix.Url };
    }

    /// <summary>
    /// Resolves the localized display name for a shift type using the full fallback chain.
    /// </summary>
    private Task<string> LocalizeShiftTypeName(ShiftType? shiftType)
    {
        if (shiftType == null) return Task.FromResult("Unknown");
        return _companyLocalizationService.ResolveShiftTypeNameAsync(
            shiftType, _tenantResolver.GetCurrentTenantId(),
            System.Globalization.CultureInfo.CurrentUICulture.Name);
    }

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateOnly PreviousWeekStart { get; set; }
    public DateOnly NextWeekStart { get; set; }
    public List<DateOnly> Dates { get; set; } = new();
    public List<ShiftType> ShiftTypes { get; set; } = new();
    public List<AppUser> Employees { get; set; } = new();
    public string ViewMode { get; set; } = "week"; // week, 2weeks, month

    // Molecule mode properties
    [BindProperty(SupportsGet = true)]
    public int? MoleculeId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? JobTypeId { get; set; }

    public bool IsTechMolecule { get; set; }

    /// <summary>True when Calendar/Table is operating in molecule-scoped mode (MoleculeId + JobTypeId both set, or Tech molecule).</summary>
    public bool IsMoleculeMode => MoleculeId.HasValue && (JobTypeId.HasValue || IsTechMolecule);

    public List<Molecule> AvailableMolecules { get; set; } = new();
    public List<JobType> AvailableJobTypes { get; set; } = new();
    public Molecule? SelectedMolecule { get; set; }
    public JobType? SelectedJobType { get; set; }
    public Dictionary<int, string> CompanyNames { get; set; } = new();

    // Pre-computed localized shift type names for use in Razor view
    public Dictionary<int, string> LocalizedShiftTypeNames { get; set; } = new();

    // Map: [ShiftTypeId][Date] => List of assignments
    public Dictionary<int, Dictionary<DateOnly, List<AssignmentInfo>>> AssignmentGrid { get; set; } = new();

    // Busy user status per date: [Date][UserId] => BusyStatus
    public Dictionary<DateOnly, Dictionary<int, BusyStatus>> BusyUsersByDate { get; set; } = new();

    public class AssignmentInfo
    {
        public int AssignmentId { get; set; }
        public int ShiftInstanceId { get; set; }
        public int? UserId { get; set; }
        public string? EmployeeName { get; set; }
        public int? TraineeUserId { get; set; }
        public string? TraineeName { get; set; }
        public bool IsDetached { get; set; }
        public int? OriginalProgramId { get; set; }
        public string? OverriddenFields { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(string? start, string? view)
    {
        // Switcher-aware active company: the grid's instances/employees come through the tenant query
        // filter (= switched company), so the shift-type rows + molecule-selector seed must use the
        // same source. Using the home-company claim here mismatched the grid for switched Owners.
        var companyId = _tenantResolver.GetCurrentTenantId();
        if (companyId <= 0)
            throw new InvalidOperationException("No active company/tenant context for the calendar.");

        // Set view mode (week, 2weeks, month)
        ViewMode = view?.ToLowerInvariant() ?? "week";
        if (ViewMode != "week" && ViewMode != "2weeks" && ViewMode != "month")
        {
            ViewMode = "week"; // Default fallback
        }

        // Default to current week if no start date provided
        // ✅ A-019: Safe date parsing with fallback to today on invalid input
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (string.IsNullOrEmpty(start))
        {
            StartDate = today.AddDays(-(int)today.DayOfWeek); // Start of week (Sunday)
        }
        else if (DateOnly.TryParse(start, out var parsedDate))
        {
            // Validate parsed date is within reasonable bounds
            if (parsedDate.Year >= 1900 && parsedDate.Year <= 2100)
            {
                StartDate = parsedDate;
            }
            else
            {
                LogInvalidStartDateYear(_logger, start);
                StartDate = today.AddDays(-(int)today.DayOfWeek);
            }
        }
        else
        {
            LogInvalidStartDateFormat(_logger, start);
            StartDate = today.AddDays(-(int)today.DayOfWeek);
        }

        // Calculate end date based on view mode
        EndDate = ViewMode switch
        {
            "2weeks" => StartDate.AddDays(13), // 14 days
            "month" => StartDate.AddDays(DateTime.DaysInMonth(StartDate.Year, StartDate.Month) - 1),
            _ => StartDate.AddDays(6) // Default: 7 days
        };

        // Calculate previous and next navigation dates
        var daysToMove = ViewMode switch
        {
            "2weeks" => 14,
            "month" => DateTime.DaysInMonth(StartDate.Year, StartDate.Month),
            _ => 7
        };
        PreviousWeekStart = StartDate.AddDays(-daysToMove);
        NextWeekStart = StartDate.AddDays(daysToMove);

        // Generate date range
        for (var date = StartDate; date <= EndDate; date = date.AddDays(1))
        {
            Dates.Add(date);
        }

        // --- Molecule mode setup ---
        // Resolve user context for molecule selector (available even in company mode for the dropdown)
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        int.TryParse(userIdClaim, out var currentUserId);

        var userCompany = await _db.Companies
            .Include(c => c.Molecule)
            .FirstOrDefaultAsync(c => c.Id == companyId);

        if (userCompany?.MoleculeId != null)
        {
            await LoadAvailableMoleculesAsync(currentUserId, userCompany.MoleculeId.Value);
            if (MoleculeId.HasValue)
            {
                // Validate selected molecule is accessible
                if (!AvailableMolecules.Any(m => m.Id == MoleculeId))
                    MoleculeId = null; // Fall back to company mode
            }

            SelectedMolecule = AvailableMolecules.FirstOrDefault(m => m.Id == MoleculeId);
            IsTechMolecule = SelectedMolecule?.Type == MoleculeType.Tech;

            // Tech molecules don't use JobType for shift filtering — force null
            if (IsTechMolecule)
                JobTypeId = null;

            // Load job types for selected molecule's area
            if (SelectedMolecule != null)
            {
                await LoadAvailableJobTypesAsync(SelectedMolecule.Id);

                // Validate selected job type (Workforce only — Tech already null)
                if (!IsTechMolecule && (!JobTypeId.HasValue || !AvailableJobTypes.Any(jt => jt.Id == JobTypeId)))
                    JobTypeId = AvailableJobTypes.FirstOrDefault()?.Id;

                SelectedJobType = AvailableJobTypes.FirstOrDefault(jt => jt.Id == JobTypeId);
            }
        }

        // --- Load data based on mode ---
        List<ShiftInstance> instances;
        List<ShiftAssignment> assignments;
        List<int>? moleculeCompanyIds = null; // Available for BusyUsersByDate in molecule mode

        if (IsMoleculeMode)
        {
            var molId = MoleculeId!.Value;

            // MOLECULE MODE: load shifts across all companies in the molecule for this job type
            // SECURITY-AUDITED: SAFE — IgnoreQueryFilters re-scoped by validated MoleculeId + JobTypeId (or Tech molecule null);
            // molecule access validated via AvailableMolecules (grant-derived) above
            // Resolve areaId for area-scoped shift inclusion
            var tableAreaId = await _db.Molecules.Where(m => m.Id == molId).Select(m => m.AreaId).FirstOrDefaultAsync();
            var instanceQuery = _db.ShiftInstances
                .IgnoreQueryFilters()
                .Include(si => si.ShiftType)
                .Where(si => (si.ShiftType.MoleculeId == molId ||
                             (si.ShiftType.Scope == Models.Support.ShiftScope.Area && si.ShiftType.AreaId == tableAreaId))
                    && si.WorkDate >= StartDate && si.WorkDate <= EndDate);

            if (JobTypeId.HasValue)
                instanceQuery = instanceQuery.Where(si => si.ShiftType.JobTypeId == JobTypeId.Value);
            else
                instanceQuery = instanceQuery.Where(si => si.ShiftType.JobTypeId == null);

            instances = await instanceQuery.ToListAsync();

            var shiftTypeIdsWithInstances = instances.Select(si => si.ShiftTypeId).Distinct().ToHashSet();

            // Load molecule-scoped + area overlay shift types
            var allShiftTypes = await _shiftTypeCache.GetMergedShiftTypesAsync(molId, JobTypeId);
            ShiftTypes = allShiftTypes
                // Show all molecule shift types (not just those with instances — leads need to see unfilled shifts too)
                .OrderBy(st => st.IsOffline ? 1 : 0)
                .ThenBy(st => st.Start)
                .ThenBy(st => st.NameEn ?? st.Name)
                .ToList();

            // Load employees from ALL companies in the molecule with matching JobType
            // SECURITY-AUDITED: SAFE — IgnoreQueryFilters re-scoped by validated moleculeId + jobTypeId
            moleculeCompanyIds = await _db.Companies
                .IgnoreQueryFilters()
                .Where(c => c.MoleculeId == molId)
                .Select(c => c.Id)
                .ToListAsync();

            CompanyNames = (await _db.Companies
                .IgnoreQueryFilters()
                .Where(c => moleculeCompanyIds.Contains(c.Id))
                .ToListAsync())
                .ToDictionary(c => c.Id, c => c.LocalizedName);

            var employeeQuery = _db.Users
                .IgnoreQueryFilters()
                .Where(u => u.IsActive && moleculeCompanyIds.Contains(u.CompanyId));

            if (JobTypeId.HasValue)
                employeeQuery = employeeQuery.Where(u => u.JobTypeId == JobTypeId.Value);

            Employees = await employeeQuery
                .OrderBy(u => u.CompanyId)
                .ThenBy(u => u.DisplayName)
                .ToListAsync();

            // Load assignments with IgnoreQueryFilters for cross-company visibility
            var instanceIds = instances.Select(i => i.Id).ToList();
            assignments = await _db.ShiftAssignments
                .IgnoreQueryFilters()
                .Include(a => a.User)
                .Include(a => a.Trainee)
                .Where(a => instanceIds.Contains(a.ShiftInstanceId))
                .ToListAsync();
        }
        else
        {
            // COMPANY MODE (original behavior): load shifts for current company only
            instances = await _db.ShiftInstances
                .Where(si => si.WorkDate >= StartDate && si.WorkDate <= EndDate)
                .ToListAsync();

            var shiftTypeIdsWithInstances = instances.Select(si => si.ShiftTypeId).Distinct().ToHashSet();

            // Get shift type IDs from active programs
            var activePrograms = await _programService.GetCompanyProgramsAsync(companyId, includeInactive: false);
            var shiftTypeIdsFromPrograms = activePrograms.Select(p => p.ShiftTypeId).Distinct().ToHashSet();

            var relevantShiftTypeIds = shiftTypeIdsWithInstances.Union(shiftTypeIdsFromPrograms).ToHashSet();

            // Resolve molecule from company for shift type lookup (shift types are molecule-scoped)
            var companyMoleculeId = await _db.Companies
                .Where(c => c.Id == companyId)
                .Select(c => c.MoleculeId)
                .FirstOrDefaultAsync();
            var allShiftTypes = companyMoleculeId.HasValue
                ? await _shiftTypeCache.GetShiftTypesForMoleculeAsync(companyMoleculeId.Value, null)
                : new List<ShiftType>();
            ShiftTypes = allShiftTypes
                .Where(st => relevantShiftTypeIds.Contains(st.Id))
                .OrderBy(st => st.IsOffline ? 1 : 0)
                .ThenBy(st => st.Start)
                .ThenBy(st => st.NameEn ?? st.Name)
                .ToList();

            Employees = await _db.Users
                .Where(u => u.IsActive)
                .OrderBy(u => u.DisplayName)
                .ToListAsync();

            var instanceIds = instances.Select(i => i.Id).ToList();
            assignments = await _db.ShiftAssignments
                .Include(a => a.User)
                .Include(a => a.Trainee)
                .Where(a => instanceIds.Contains(a.ShiftInstanceId))
                .ToListAsync();
        }

        LogShiftTypesLoaded(_logger, ShiftTypes.Count, IsMoleculeMode);

        // Pre-compute localized names for Razor view
        foreach (var st in ShiftTypes)
        {
            LocalizedShiftTypeNames[st.Id] = await LocalizeShiftTypeName(st);
        }

        if (!ShiftTypes.Any())
        {
            LogNoShiftTypes(_logger);
        }

        // Load busy user status for all dates in a single batch query (avoids N+1)
        if (Dates.Count > 0)
        {
            if (IsMoleculeMode && moleculeCompanyIds != null)
            {
                // Molecule mode: check busy status across all companies in the molecule
                BusyUsersByDate = await _busyUserService.GetBusyUsersByDateRangeForCompaniesAsync(
                    Dates.First(), Dates.Last(), TimeOnly.MinValue, TimeOnly.MaxValue, moleculeCompanyIds);
            }
            else
            {
                BusyUsersByDate = await _busyUserService.GetBusyUsersByDateRangeAsync(
                    Dates.First(), Dates.Last(), TimeOnly.MinValue, TimeOnly.MaxValue);
            }
        }

        // Build grid structure (shared for both modes)
        foreach (var shiftType in ShiftTypes)
        {
            AssignmentGrid[shiftType.Id] = new Dictionary<DateOnly, List<AssignmentInfo>>();

            foreach (var date in Dates)
            {
                var instance = instances.FirstOrDefault(i => i.ShiftTypeId == shiftType.Id && i.WorkDate == date);

                if (instance != null)
                {
                    var instanceAssignments = assignments
                        .Where(a => a.ShiftInstanceId == instance.Id)
                        .Select(a => new AssignmentInfo
                        {
                            AssignmentId = a.Id,
                            ShiftInstanceId = instance.Id,
                            UserId = a.UserId,
                            EmployeeName = a.User?.DisplayName,
                            TraineeUserId = a.TraineeUserId,
                            TraineeName = a.Trainee?.DisplayName,
                            IsDetached = instance.IsDetached,
                            OriginalProgramId = instance.OriginalProgramId,
                            OverriddenFields = instance.OverriddenFields
                        })
                        .ToList();

                    AssignmentGrid[shiftType.Id][date] = instanceAssignments;
                }
                else
                {
                    AssignmentGrid[shiftType.Id][date] = new List<AssignmentInfo>();
                }
            }
        }

        return Page();
    }

    public async Task<IActionResult> OnPostEnsureShiftInstanceAsync([FromBody] EnsureShiftInstanceRequest request)
    {
        try
        {
            var companyId = _companyContext.GetCompanyIdOrThrow();
            if (request.StaffingRequired < 1 || request.StaffingRequired > 30)
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_StaffingRange"].Value });

            // F7 FIX: resolve the target ShiftType (and thus its company + job type) up front so the
            // authorization check applies to BOTH the create-new and adjust-existing branches.
            // SECURITY-AUDITED: SAFE — entity lookup by unique ID
            var ensureShiftType = await _db.ShiftTypes.IgnoreQueryFilters().FirstOrDefaultAsync(st => st.Id == request.ShiftTypeId);
            if (ensureShiftType == null)
            {
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_ShiftTypeNotFound"].Value });
            }

            // F7 FIX: per-handler shift-assign authorization (class gate is not sufficient)
            if (CurrentUserIdOrNull() is not int ensureUserId)
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InvalidUserSession"].Value }) { StatusCode = 401 };
            if (!await CanAssignForShiftTypeAsync(ensureUserId, ensureShiftType))
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InsufficientPermissions"].Value }) { StatusCode = 403 };

            // Get or create instance (idempotent)
            // SECURITY-AUDITED: SAFE — entity lookup by unique ShiftTypeId+WorkDate composite key
            var instance = await _db.ShiftInstances
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(si => si.ShiftTypeId == request.ShiftTypeId &&
                                          si.WorkDate == request.Date);

            bool isNew = instance == null;

            if (instance == null)
            {
                var shiftType = ensureShiftType;

                // Transaction: create instance + assignment slots atomically
                using var transaction = await _db.Database.BeginTransactionAsync();
                try
                {
                    // Create new instance
                    instance = new ShiftInstance
                    {
                        CompanyId = shiftType.GetEffectiveCompanyId(companyId),  // Use ShiftType's company, not caller's tenant
                        ShiftTypeId = request.ShiftTypeId,
                        WorkDate = request.Date,
                        StaffingRequired = request.StaffingRequired,
                        Concurrency = 0
                    };
                    _db.ShiftInstances.Add(instance);
                    var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                        () => _db.SaveChangesAsync(), "ShiftInstance");
                    if (!saveResult.Success)
                        return new JsonResult(new { success = false, error = saveResult.ErrorMessage }) { StatusCode = 409 };

                    // Create empty assignment slots
                    for (int i = 0; i < request.StaffingRequired; i++)
                    {
                        var assignment = new ShiftAssignment
                        {
                            CompanyId = shiftType.GetEffectiveCompanyId(companyId),  // Use ShiftType's company, not caller's tenant
                            ShiftInstanceId = instance.Id,
                            UserId = null // Unassigned slot
                        };
                        _db.ShiftAssignments.Add(assignment);
                    }
                    var saveResult2 = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                        () => _db.SaveChangesAsync(), "ShiftAssignment");
                    if (!saveResult2.Success)
                        return new JsonResult(new { success = false, error = saveResult2.ErrorMessage }) { StatusCode = 409 };

                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            else
            {
                // Instance exists - check if we need to adjust staffing
                // SECURITY-AUDITED: SAFE — entity lookup by unique ID
                var currentSlotCount = await _db.ShiftAssignments
                    .IgnoreQueryFilters()
                    .CountAsync(a => a.ShiftInstanceId == instance.Id);

                if (request.StaffingRequired > currentSlotCount)
                {
                    // Add more slots
                    for (int i = currentSlotCount; i < request.StaffingRequired; i++)
                    {
                        var assignment = new ShiftAssignment
                        {
                            CompanyId = instance.CompanyId,  // Use instance's company
                            ShiftInstanceId = instance.Id,
                            UserId = null
                        };
                        _db.ShiftAssignments.Add(assignment);
                    }
                    instance.StaffingRequired = request.StaffingRequired;
                    var saveResult3 = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                        () => _db.SaveChangesAsync(), "ShiftInstance", instance.Id);
                    if (!saveResult3.Success)
                        return new JsonResult(new { success = false, error = saveResult3.ErrorMessage }) { StatusCode = 409 };
                }
            }

            if (isNew)
            {
                await _auditLogService.LogAsync("ShiftInstanceCreated", "ShiftInstance", instance.Id,
                    FormattableString.Invariant($"Created shift instance for type {request.ShiftTypeId} on {request.Date:yyyy-MM-dd} with {request.StaffingRequired} slots"));
            }

            return new JsonResult(new
            {
                success = true,
                instanceId = instance.Id,
                staffingRequired = instance.StaffingRequired,
                isNew = isNew
            });
        }
        catch (Exception ex)
        {
            LogErrorAction(_logger, ex, "ensuring shift instance");
            return new JsonResult(new { success = false, error = _localizer["Calendar_Error_EnsureShiftInstanceFailed"].Value });
        }
    }

    public async Task<IActionResult> OnPostCreateShiftInstanceAsync([FromBody] CreateShiftInstanceRequest request)
    {
        try
        {
            var companyId = _companyContext.GetCompanyIdOrThrow();
            if (request.StaffingRequired < 1 || request.StaffingRequired > 30)
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_StaffingRange"].Value });

            // Check if instance already exists
            // SECURITY-AUDITED: SAFE — entity lookup by unique ShiftTypeId+WorkDate composite key
            var existingInstance = await _db.ShiftInstances
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(si => si.ShiftTypeId == request.ShiftTypeId && si.WorkDate == request.Date);

            if (existingInstance != null)
            {
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_ShiftInstanceExists"].Value });
            }

            // SECURITY-AUDITED: SAFE — entity lookup by unique ID
            var shiftType = await _db.ShiftTypes.IgnoreQueryFilters().FirstOrDefaultAsync(st => st.Id == request.ShiftTypeId);
            if (shiftType == null)
            {
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_ShiftTypeNotFound"].Value });
            }

            // F7 FIX: per-handler shift-assign authorization (class gate is not sufficient).
            // Target company/jobType come from the resolved ShiftType (NOT the caller's tenant).
            if (CurrentUserIdOrNull() is not int createInstUserId)
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InvalidUserSession"].Value }) { StatusCode = 401 };
            if (!await CanAssignForShiftTypeAsync(createInstUserId, shiftType))
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InsufficientPermissions"].Value }) { StatusCode = 403 };

            // Transaction: create instance + assignment slots atomically
            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                // Create shift instance with staffing requirement
                var instance = new ShiftInstance
                {
                    CompanyId = shiftType.GetEffectiveCompanyId(companyId),  // Use ShiftType's company, not caller's tenant
                    ShiftTypeId = request.ShiftTypeId,
                    WorkDate = request.Date,
                    StaffingRequired = request.StaffingRequired,
                    Concurrency = 0
                };
                _db.ShiftInstances.Add(instance);
                var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                    () => _db.SaveChangesAsync(), "ShiftInstance");
                if (!saveResult.Success)
                    return new JsonResult(new { success = false, error = saveResult.ErrorMessage }) { StatusCode = 409 };

                // Create empty assignment slots
                for (int i = 0; i < request.StaffingRequired; i++)
                {
                    var assignment = new ShiftAssignment
                    {
                        CompanyId = shiftType.GetEffectiveCompanyId(companyId),  // Use ShiftType's company, not caller's tenant
                        ShiftInstanceId = instance.Id,
                        UserId = null // Unassigned slot
                    };
                    _db.ShiftAssignments.Add(assignment);
                }
                var saveResult2 = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                    () => _db.SaveChangesAsync(), "ShiftAssignment");
                if (!saveResult2.Success)
                    return new JsonResult(new { success = false, error = saveResult2.ErrorMessage }) { StatusCode = 409 };

                await transaction.CommitAsync();

                await _auditLogService.LogAsync("ShiftInstanceCreated", "ShiftInstance", instance.Id,
                    FormattableString.Invariant($"Created shift instance for type {request.ShiftTypeId} on {request.Date:yyyy-MM-dd} with {request.StaffingRequired} slots"));

                return new JsonResult(new
                {
                    success = true,
                    instanceId = instance.Id,
                    staffingRequired = instance.StaffingRequired
                });
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            LogErrorAction(_logger, ex, "creating shift instance");
            return new JsonResult(new { success = false, error = _localizer["Calendar_Error_CreateShiftInstanceFailed"].Value });
        }
    }

    public async Task<IActionResult> OnPostAssignUserToSlotAsync([FromBody] AssignUserToSlotRequest request)
    {
        try
        {
            using var transaction = await _db.Database.BeginTransactionAsync();

            // SECURITY-AUDITED: SAFE — entity lookup by unique ID
            var assignment = await _db.ShiftAssignments
                .IgnoreQueryFilters()
                .Include(a => a.ShiftInstance)
                .ThenInclude(si => si.ShiftType)
                .FirstOrDefaultAsync(a => a.Id == request.AssignmentId);

            if (assignment == null)
            {
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_AssignmentNotFound"].Value });
            }

            // F7 FIX: per-handler shift-assign authorization. Target scope = the assignment's own
            // ShiftInstance company + its ShiftType job type (NOT the caller's tenant).
            if (CurrentUserIdOrNull() is not int slotUserId)
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InvalidUserSession"].Value }) { StatusCode = 401 };
            if (!await CanAssignForShiftScopeAsync(slotUserId, assignment.ShiftInstance.CompanyId, assignment.ShiftInstance.ShiftType?.JobTypeId))
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InsufficientPermissions"].Value }) { StatusCode = 403 };

            // Validate via service (job type, grouping, weekly cap, rest hours)
            var validation = await _assignmentService.ValidateShiftAssignmentAsync(request.UserId, assignment.ShiftInstanceId);

            if (!validation.CanAssign)
            {
                return new JsonResult(new
                {
                    success = false,
                    error = validation.Errors.FirstOrDefault()?.Message ?? _localizer["Calendar_Error_ValidationFailed"].Value,
                    errors = validation.Errors.Select(e => new { e.Key, e.Message, e.Category }),
                    fix = MakeFix(validation.Errors.FirstOrDefault()?.Key, request.UserId),
                });
            }

            if (validation.Warnings.Count > 0 && string.IsNullOrEmpty(request.OverrideToken))
            {
                var overrideToken = _assignmentService.GenerateOverrideToken(
                    assignment.ShiftInstanceId, request.UserId, validation.Warnings.Select(w => w.Key).ToList());
                return new JsonResult(new
                {
                    success = false,
                    requiresOverride = true,
                    warnings = validation.Warnings.Select(w => new { w.Key, w.Message, Category = w.Category.ToString(), w.Detail }),
                    overrideToken,
                    shiftInstanceId = assignment.ShiftInstanceId
                });
            }

            // Validate override token if provided
            if (!string.IsNullOrEmpty(request.OverrideToken) &&
                !_assignmentService.ValidateOverrideToken(request.OverrideToken, assignment.ShiftInstanceId, request.UserId))
            {
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InvalidOverrideToken"].Value });
            }

            // Overlap detection (kept as hard check — not part of service validation)
            var shiftDate = assignment.ShiftInstance.WorkDate;
            var shiftStart = assignment.ShiftInstance.ShiftType.Start;
            var shiftEnd = assignment.ShiftInstance.ShiftType.End;

            // SECURITY-AUDITED: SAFE — scoped by validated UserId + date
            var overlappingShifts = await _db.ShiftAssignments
                .IgnoreQueryFilters()
                .Include(a => a.ShiftInstance)
                .ThenInclude(si => si.ShiftType)
                .Where(a => a.UserId == request.UserId &&
                           a.ShiftInstance.WorkDate == shiftDate &&
                           a.Id != request.AssignmentId)
                .ToListAsync();

            foreach (var existing in overlappingShifts)
            {
                var existingStart = existing.ShiftInstance.ShiftType.Start;
                var existingEnd = existing.ShiftInstance.ShiftType.End;

                bool overlaps = false;

                if (shiftEnd < shiftStart)
                {
                    if (existingEnd < existingStart)
                        overlaps = true;
                    else
                        overlaps = existingStart >= shiftStart || existingEnd <= shiftEnd;
                }
                else if (existingEnd < existingStart)
                {
                    overlaps = shiftStart >= existingStart || shiftEnd <= existingEnd;
                }
                else
                {
                    overlaps = (shiftStart < existingEnd && shiftEnd > existingStart);
                }

                if (overlaps)
                {
                    var overlapName = await LocalizeShiftTypeName(existing.ShiftInstance.ShiftType);
                    return new JsonResult(new
                    {
                        success = false,
                        error = string.Format(CultureInfo.CurrentCulture, _localizer["Calendar_Error_OverlappingShift"].Value, overlapName, existingStart.ToString("HH:mm", CultureInfo.InvariantCulture), existingEnd.ToString("HH:mm", CultureInfo.InvariantCulture))
                    });
                }
            }

            // Assign user to slot
            assignment.UserId = request.UserId;
            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "ShiftAssignment", request.AssignmentId);
            if (!saveResult.Success)
                return new JsonResult(new { success = false, error = saveResult.ErrorMessage }) { StatusCode = 409 };

            await transaction.CommitAsync();

            // SECURITY-AUDITED: SAFE — entity lookup by unique ID
            var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == request.UserId);

            await _auditLogService.LogAsync("ShiftSlotAssigned", "ShiftAssignment", request.AssignmentId,
                FormattableString.Invariant($"Assigned user {request.UserId} ({user?.DisplayName}) to slot on {shiftDate:yyyy-MM-dd}"));

            return new JsonResult(new
            {
                success = true,
                employeeName = user?.DisplayName
            });
        }
        catch (Exception ex)
        {
            LogErrorAction(_logger, ex, "assigning user to slot");
            return new JsonResult(new { success = false, error = _localizer["Calendar_Error_AssignUserToSlotFailed"].Value });
        }
    }

    public async Task<IActionResult> OnPostAssignEmployeeAsync([FromBody] AssignEmployeeRequest request)
    {
        // Input validation: an empty/malformed body — or a non-nullable field (e.g. ShiftTypeId)
        // posted as null — makes model binding yield a null request. Return a clean 400 rather than
        // dereferencing null downstream (which surfaced as a misleading "Error assigning employee").
        if (request == null)
            return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InvalidRequest"].Value }) { StatusCode = 400 };

        try
        {
            var companyId = _companyContext.GetCompanyIdOrThrow();

            // FINDING-002 FIX: Authorization check — verify user has shift assignment grant
            if (!int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var currentUserId))
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InvalidUserSession"].Value }) { StatusCode = 401 };

            var isAdmin = await _grantService.HasGrantAsync(currentUserId, "AdminAccess");
            if (!isAdmin)
            {
                // SECURITY-AUDITED 2026-05-07 (Finding #3 fix): pass the shift's JobTypeId
                // to the scope check. Without it, a Lead with JobType=Alhut whose seed gives
                // them BOTH AssignAlhutShifts AND AssignTextShifts (each persisted with
                // JobTypeId=Alhut via useOwnJobType:true) could assign Text-jobtype shifts
                // because the molecule branch of HasGrantWithScopeAsync ignored JobType.
                // Now: lookup the shift's JobTypeId once, pass it, and rely on the new
                // jobTypeMismatch filter to suppress wrong-JobType grants.
                var probeShiftJobTypeId = await _db.ShiftTypes
                    .IgnoreQueryFilters()
                    .Where(st => st.Id == request.ShiftTypeId)
                    .Select(st => (int?)st.JobTypeId)
                    .FirstOrDefaultAsync();

                var hasAnyShiftGrant = await _grantService.HasGrantWithScopeAsync(currentUserId, "AssignShifts", companyId: companyId, jobTypeId: probeShiftJobTypeId);

                if (!hasAnyShiftGrant)
                    return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InsufficientPermissions"].Value }) { StatusCode = 403 };
            }

            // Draft Mode: stage the assignment into the caller's own sandbox instead of writing live (no SignalR).
            if (request.DraftSessionId.HasValue)
            {
                var ownsDraft = await _db.DraftSessions.AnyAsync(d =>
                    d.Id == request.DraftSessionId.Value && d.OwnerUserId == currentUserId && d.Status == DraftSessionStatus.Active);
                if (!ownsDraft)
                    return new JsonResult(new { success = false, error = _localizer["Calendar_Error_DraftInactive"].Value }) { StatusCode = 409 };
                await _draftService.StageAssignAsync(request.DraftSessionId.Value, request.ShiftTypeId, request.Date, request.UserId);
                return new JsonResult(new { success = true, draft = true });
            }

            // Resolve the shift instance to assign into.
            ShiftInstance? instance;
            if (request.ShiftInstanceId is int explicitInstanceId && explicitInstanceId > 0)
            {
                // Instance-aware callers (e.g. Justice "make it real" drilling into a specific hole) name
                // the EXACT instance. Resolve by id so we fill THAT hole instead of FirstOrDefault-ing an
                // arbitrary same-ShiftType+Date instance from another company in the molecule. Require the
                // ShiftType to match the one the grant check above was scoped to (prevents a mismatched
                // ShiftType/instance pair); the assignment itself is still validated below.
                // SECURITY-AUDITED: SAFE — lookup by unique id, re-scoped by request.ShiftTypeId.
                instance = await _db.ShiftInstances
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(si => si.Id == explicitInstanceId && si.ShiftTypeId == request.ShiftTypeId);
                if (instance == null)
                    return new JsonResult(new { success = false, error = _localizer["Calendar_Error_ShiftTypeNotFound"].Value });
            }
            else
            {
                // Get or create shift instance (service expects pre-existing instance)
                // SECURITY-AUDITED: SAFE — entity lookup by unique ShiftTypeId+WorkDate composite key
                instance = await _db.ShiftInstances
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(si => si.ShiftTypeId == request.ShiftTypeId && si.WorkDate == request.Date);

                if (instance == null)
                {
                    // SECURITY-AUDITED: SAFE — entity lookup by unique ID
                    var shiftType = await _db.ShiftTypes.IgnoreQueryFilters().FirstOrDefaultAsync(st => st.Id == request.ShiftTypeId);
                    if (shiftType == null)
                    {
                        return new JsonResult(new { success = false, error = _localizer["Calendar_Error_ShiftTypeNotFound"].Value });
                    }

                    instance = new ShiftInstance
                    {
                        CompanyId = shiftType.GetEffectiveCompanyId(companyId),  // Use ShiftType's company, not caller's tenant
                        ShiftTypeId = request.ShiftTypeId,
                        WorkDate = request.Date,
                        StaffingRequired = 1,
                        Concurrency = 0
                    };
                    _db.ShiftInstances.Add(instance);
                    var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                        () => _db.SaveChangesAsync(), "ShiftInstance");
                    if (!saveResult.Success)
                        return new JsonResult(new { success = false, error = saveResult.ErrorMessage }) { StatusCode = 409 };
                }
            }

            // Validate assignment via service (job type, grouping, weekly cap, rest hours)
            var validation = await _assignmentService.ValidateShiftAssignmentAsync(request.UserId, instance.Id);

            if (!validation.CanAssign)
            {
                return new JsonResult(new
                {
                    success = false,
                    error = validation.Errors.FirstOrDefault()?.Message ?? _localizer["Calendar_Error_ValidationFailed"].Value,
                    errors = validation.Errors.Select(e => new { e.Key, e.Message, e.Category }),
                    fix = MakeFix(validation.Errors.FirstOrDefault()?.Key, request.UserId),
                });
            }

            if (validation.Warnings.Count > 0 && string.IsNullOrEmpty(request.OverrideToken))
            {
                // Return warnings + override token for client to confirm
                var overrideToken = _assignmentService.GenerateOverrideToken(
                    instance.Id, request.UserId, validation.Warnings.Select(w => w.Key).ToList());
                return new JsonResult(new
                {
                    success = false,
                    requiresOverride = true,
                    warnings = validation.Warnings.Select(w => new { w.Key, w.Message, Category = w.Category.ToString(), w.Detail }),
                    overrideToken,
                    shiftInstanceId = instance.Id
                });
            }

            // Proceed with assignment (service handles duplicate/capacity checks + override token validation)
            var result = await _assignmentService.AssignShiftAsync(request.UserId, instance.Id, currentUserId, request.OverrideToken);

            if (!result.Success)
            {
                // If warnings still pending (token invalid/expired), return them again
                if (result.Validation?.Warnings.Count > 0)
                {
                    var newToken = _assignmentService.GenerateOverrideToken(
                        instance.Id, request.UserId, result.Validation.Warnings.Select(w => w.Key).ToList());
                    return new JsonResult(new
                    {
                        success = false,
                        requiresOverride = true,
                        warnings = result.Validation.Warnings.Select(w => new { w.Key, w.Message, Category = w.Category.ToString(), w.Detail }),
                        overrideToken = newToken,
                        shiftInstanceId = instance.Id
                    });
                }
                return new JsonResult(new { success = false, error = result.ErrorMessage, errorKey = result.ErrorKey });
            }

            // SECURITY-AUDITED: SAFE — entity lookup by unique ID
            var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == request.UserId);

            // Send real-time notification (fire-and-forget)
            try
            {
                // SECURITY-AUDITED: SAFE — entity lookup by unique ID
                var shiftType = await _db.ShiftTypes.IgnoreQueryFilters().FirstOrDefaultAsync(st => st.Id == request.ShiftTypeId);
                if (shiftType?.MoleculeId != null)
                {
                    var localizedShiftName = await LocalizeShiftTypeName(shiftType);
                    var groupName = CalendarGroups.Shifts(shiftType.MoleculeId.Value, shiftType.JobTypeId);
                    await _calendarNotification.NotifyAssignmentChangedAsync(groupName,
                        new CalendarAssignmentChangedEvent(
                            ShiftInstanceId: instance.Id,
                            UserId: request.UserId,
                            UserDisplayName: user?.DisplayName,
                            Date: instance.WorkDate,
                            ShiftTypeId: shiftType.Id,
                            ShiftTypeName: localizedShiftName,
                            ChangeType: "Assigned"
                        ));
                }
            }
            catch (Exception notifyEx)
            {
                LogCalendarNotificationFailed(_logger, notifyEx, "AssignEmployee");
            }

            await _auditLogService.LogAsync("ShiftAssigned", "ShiftAssignment", result.AssignmentId,
                FormattableString.Invariant($"Assigned user {request.UserId} ({user?.DisplayName}) to shift type {request.ShiftTypeId} on {instance.WorkDate:yyyy-MM-dd}"));

            return new JsonResult(new
            {
                success = true,
                assignmentId = result.AssignmentId,
                employeeName = user?.DisplayName
            });
        }
        catch (Exception ex)
        {
            LogErrorAction(_logger, ex, "assigning employee");
            return new JsonResult(new { success = false, error = _localizer["Calendar_Error_AssignEmployeeFailed"].Value });
        }
    }

    public async Task<IActionResult> OnPostUnassignEmployeeAsync([FromBody] UnassignEmployeeRequest request)
    {
        try
        {
            // SECURITY-AUDITED: SAFE — entity lookup by unique ID
            var assignment = await _db.ShiftAssignments
                .IgnoreQueryFilters()
                .Include(a => a.ShiftInstance)
                    .ThenInclude(si => si.ShiftType)
                .Include(a => a.User)
                .FirstOrDefaultAsync(a => a.Id == request.AssignmentId);
            if (assignment == null)
            {
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_AssignmentNotFound"].Value });
            }

            // F7 FIX: per-handler shift-assign authorization. Target scope = the assignment's own
            // ShiftInstance company + its ShiftType job type (NOT the caller's tenant).
            if (CurrentUserIdOrNull() is not int unassignUserId)
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InvalidUserSession"].Value }) { StatusCode = 401 };
            if (!await CanAssignForShiftScopeAsync(unassignUserId, assignment.ShiftInstance.CompanyId, assignment.ShiftInstance.ShiftType?.JobTypeId))
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InsufficientPermissions"].Value }) { StatusCode = 403 };

            // Capture data before removal for notification
            var instanceId = assignment.ShiftInstance.Id;
            var workDate = assignment.ShiftInstance.WorkDate;
            var shiftType = assignment.ShiftInstance.ShiftType;
            var removedUserId = assignment.UserId;
            var removedUserName = assignment.User?.DisplayName;

            _db.ShiftAssignments.Remove(assignment);
            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "ShiftAssignment", request.AssignmentId);
            if (!saveResult.Success)
                return new JsonResult(new { success = false, error = saveResult.ErrorMessage }) { StatusCode = 409 };

            // Send real-time notification (fire-and-forget)
            try
            {
                if (shiftType?.MoleculeId != null)
                {
                    var localizedShiftName = await LocalizeShiftTypeName(shiftType);
                    var groupName = CalendarGroups.Shifts(shiftType.MoleculeId.Value, shiftType.JobTypeId);
                    await _calendarNotification.NotifyAssignmentChangedAsync(groupName,
                        new CalendarAssignmentChangedEvent(
                            ShiftInstanceId: instanceId,
                            UserId: removedUserId,
                            UserDisplayName: removedUserName,
                            Date: workDate,
                            ShiftTypeId: shiftType.Id,
                            ShiftTypeName: localizedShiftName,
                            ChangeType: "Unassigned"
                        ));
                }
            }
            catch (Exception notifyEx)
            {
                LogCalendarNotificationFailed(_logger, notifyEx, "UnassignEmployee");
            }

            await _auditLogService.LogAsync("ShiftUnassigned", "ShiftAssignment", request.AssignmentId,
                FormattableString.Invariant($"Unassigned user {removedUserId} ({removedUserName}) from shift on {workDate:yyyy-MM-dd}"));

            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            LogErrorAction(_logger, ex, "unassigning employee");
            return new JsonResult(new { success = false, error = _localizer["Calendar_Error_UnassignEmployeeFailed"].Value });
        }
    }

    public async Task<IActionResult> OnPostClearAssignmentAsync([FromBody] ClearAssignmentRequest request)
    {
        try
        {
            // SECURITY-AUDITED: SAFE — entity lookup by unique ID
            var assignment = await _db.ShiftAssignments
                .IgnoreQueryFilters()
                .Include(a => a.ShiftInstance)
                    .ThenInclude(si => si.ShiftType)
                .FirstOrDefaultAsync(a => a.Id == request.AssignmentId);
            if (assignment == null)
            {
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_AssignmentNotFound"].Value });
            }

            // F7 FIX: per-handler shift-assign authorization. Target scope = the assignment's own
            // ShiftInstance company + its ShiftType job type (NOT the caller's tenant).
            if (CurrentUserIdOrNull() is not int clearUserId)
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InvalidUserSession"].Value }) { StatusCode = 401 };
            if (!await CanAssignForShiftScopeAsync(clearUserId, assignment.ShiftInstance.CompanyId, assignment.ShiftInstance.ShiftType?.JobTypeId))
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InsufficientPermissions"].Value }) { StatusCode = 403 };

            // Clear user and trainee without deleting the assignment slot
            assignment.UserId = null;
            assignment.TraineeUserId = null;
            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "ShiftAssignment", request.AssignmentId);
            if (!saveResult.Success)
                return new JsonResult(new { success = false, error = saveResult.ErrorMessage }) { StatusCode = 409 };

            // Send real-time notification (fire-and-forget)
            try
            {
                var shiftType = assignment.ShiftInstance.ShiftType;
                if (shiftType?.MoleculeId != null)
                {
                    var localizedShiftName = await LocalizeShiftTypeName(shiftType);
                    var groupName = CalendarGroups.Shifts(shiftType.MoleculeId.Value, shiftType.JobTypeId);
                    await _calendarNotification.NotifyAssignmentChangedAsync(groupName,
                        new CalendarAssignmentChangedEvent(
                            ShiftInstanceId: assignment.ShiftInstance.Id,
                            UserId: null,
                            UserDisplayName: null,
                            Date: assignment.ShiftInstance.WorkDate,
                            ShiftTypeId: shiftType.Id,
                            ShiftTypeName: localizedShiftName,
                            ChangeType: "Cleared"
                        ));
                }
            }
            catch (Exception notifyEx)
            {
                LogCalendarNotificationFailed(_logger, notifyEx, "ClearAssignment");
            }

            await _auditLogService.LogAsync("ShiftCleared", "ShiftAssignment", request.AssignmentId,
                FormattableString.Invariant($"Cleared assignment on {assignment.ShiftInstance.WorkDate:yyyy-MM-dd}"));

            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            LogErrorAction(_logger, ex, "clearing assignment");
            return new JsonResult(new { success = false, error = _localizer["Calendar_Error_ClearAssignmentFailed"].Value });
        }
    }

    public async Task<IActionResult> OnPostUpdateShiftStaffingAsync([FromBody] UpdateShiftStaffingRequest request)
    {
        try
        {
            if (request.StaffingRequired < 1 || request.StaffingRequired > 30)
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_StaffingRange"].Value });

            // SECURITY-AUDITED: SAFE — entity lookup by unique ID
            var instance = await _db.ShiftInstances.IgnoreQueryFilters().FirstOrDefaultAsync(si => si.Id == request.ShiftInstanceId);
            if (instance == null)
            {
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_ShiftInstanceNotFound"].Value });
            }

            // F7 FIX: per-handler shift-assign authorization. Target scope = the instance's own company
            // + its ShiftType job type (NOT the caller's tenant). Minimal lookup for the job type.
            if (CurrentUserIdOrNull() is not int staffingUserId)
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InvalidUserSession"].Value }) { StatusCode = 401 };
            var staffingJobTypeId = await _db.ShiftTypes
                .IgnoreQueryFilters()
                .Where(st => st.Id == instance.ShiftTypeId)
                .Select(st => st.JobTypeId)
                .FirstOrDefaultAsync();
            if (!await CanAssignForShiftScopeAsync(staffingUserId, instance.CompanyId, staffingJobTypeId))
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InsufficientPermissions"].Value }) { StatusCode = 403 };

            // If this instance is from a Program and not yet detached, detach it now
            if (instance.OriginalProgramId.HasValue && !instance.IsDetached)
            {
                await _programService.DetachInstanceAsync(instance.Id, "Manual staffing adjustment");
                LogAutoDetachedFromProgram(_logger, instance.Id, instance.OriginalProgramId);
            }

            // SECURITY-AUDITED: SAFE — entity lookup by unique ID
            var currentStaffing = await _db.ShiftAssignments
                .IgnoreQueryFilters()
                .CountAsync(a => a.ShiftInstanceId == instance.Id);

            var difference = request.StaffingRequired - currentStaffing;

            if (difference > 0)
            {
                // Add empty assignment slots
                for (int i = 0; i < difference; i++)
                {
                    var assignment = new ShiftAssignment
                    {
                        CompanyId = instance.CompanyId,  // Use instance's company
                        ShiftInstanceId = instance.Id,
                        UserId = null // Unassigned slot
                    };
                    _db.ShiftAssignments.Add(assignment);
                }
            }
            else if (difference < 0)
            {
                // Check how many assignments have users assigned
                // SECURITY-AUDITED: SAFE — entity lookup by unique ID
                var assignedCount = await _db.ShiftAssignments
                    .IgnoreQueryFilters()
                    .CountAsync(a => a.ShiftInstanceId == instance.Id && a.UserId != null);

                var unassignedCount = currentStaffing - assignedCount;

                // If decreasing would require removing assigned users, require confirmation
                if (Math.Abs(difference) > unassignedCount && !request.ForceRemoval)
                {
                    var affectedCount = Math.Abs(difference) - unassignedCount;
                    return new JsonResult(new
                    {
                        success = false,
                        requiresConfirmation = true,
                        affectedAssignments = affectedCount,
                        message = string.Format(CultureInfo.CurrentCulture, _localizer["Calendar_Confirm_RemoveEmployees"].Value, affectedCount)
                    });
                }

                // Remove unassigned slots first
                // SECURITY-AUDITED: SAFE — scoped by validated instance.Id
                var unassignedToRemove = await _db.ShiftAssignments
                    .IgnoreQueryFilters()
                    .Where(a => a.ShiftInstanceId == instance.Id && a.UserId == null)
                    .Take(Math.Abs(difference))
                    .ToListAsync();

                _db.ShiftAssignments.RemoveRange(unassignedToRemove);

                // If still need to remove more and force removal is true, remove assigned slots
                var remaining = Math.Abs(difference) - unassignedToRemove.Count;
                if (remaining > 0 && request.ForceRemoval)
                {
                    // SECURITY-AUDITED: SAFE — scoped by validated instance.Id
                    var assignedToRemove = await _db.ShiftAssignments
                        .IgnoreQueryFilters()
                        .Where(a => a.ShiftInstanceId == instance.Id && a.UserId != null)
                        .Take(remaining)
                        .ToListAsync();

                    _db.ShiftAssignments.RemoveRange(assignedToRemove);
                }
            }

            // Update staffing requirement
            instance.StaffingRequired = request.StaffingRequired;
            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "ShiftInstance", instance.Id);
            if (!saveResult.Success)
                return new JsonResult(new { success = false, error = saveResult.ErrorMessage }) { StatusCode = 409 };

            // Send real-time notification (fire-and-forget)
            try
            {
                // SECURITY-AUDITED: SAFE — entity lookup by unique ID
                var shiftType = await _db.ShiftTypes.IgnoreQueryFilters().FirstOrDefaultAsync(st => st.Id == instance.ShiftTypeId);
                if (shiftType?.MoleculeId != null)
                {
                    // SECURITY-AUDITED: SAFE — scoped by validated instance.Id
                    var assignedCount = await _db.ShiftAssignments
                        .IgnoreQueryFilters()
                        .CountAsync(a => a.ShiftInstanceId == instance.Id && a.UserId != null);
                    var groupName = CalendarGroups.Shifts(shiftType.MoleculeId.Value, shiftType.JobTypeId);
                    await _calendarNotification.NotifyCapacityChangedAsync(groupName,
                        new CalendarCapacityChangedEvent(
                            ShiftTypeId: shiftType.Id,
                            Date: instance.WorkDate,
                            NewCapacity: instance.StaffingRequired,
                            AssignedCount: assignedCount
                        ));
                }
            }
            catch (Exception notifyEx)
            {
                LogCalendarNotificationFailed(_logger, notifyEx, "UpdateShiftStaffing");
            }

            await _auditLogService.LogAsync("StaffingUpdated", "ShiftInstance", instance.Id,
                FormattableString.Invariant($"Updated staffing to {request.StaffingRequired} on {instance.WorkDate:yyyy-MM-dd}"));

            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            LogErrorAction(_logger, ex, "updating shift staffing");
            return new JsonResult(new { success = false, error = _localizer["Calendar_Error_UpdateStaffingFailed"].Value });
        }
    }

    public async Task<IActionResult> OnPostDeleteShiftInstanceAsync([FromBody] DeleteShiftInstanceRequest request)
    {
        try
        {
            var companyId = _companyContext.GetCompanyIdOrThrow();

            // Support both company mode (companyId check) and molecule mode (IgnoreQueryFilters)
            var moleculeIdParam = request.MoleculeId;
            ShiftInstance? instance;
            if (moleculeIdParam.HasValue)
            {
                // Verify caller has access to this molecule
                if (!await IsCallerInMoleculeAsync(moleculeIdParam.Value))
                    return new JsonResult(new { success = false, error = _localizer["Calendar_Error_UnauthorizedMolecule"].Value }) { StatusCode = 403 };

                // SECURITY-AUDITED: SAFE — molecule access validated above, re-scoped by explicit shiftInstanceId
                instance = await _db.ShiftInstances
                    .IgnoreQueryFilters()
                    .Include(si => si.ShiftType)
                    .FirstOrDefaultAsync(si => si.Id == request.ShiftInstanceId
                        && si.ShiftType.MoleculeId == moleculeIdParam.Value);
            }
            else
            {
                instance = await _db.ShiftInstances
                    .FirstOrDefaultAsync(si => si.Id == request.ShiftInstanceId && si.CompanyId == companyId);
            }

            if (instance == null)
            {
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_ShiftInstanceNotFound"].Value });
            }

            // F7 FIX: per-handler shift-assign authorization (defense in depth with the molecule
            // membership check above). Target scope = the instance's own company + its ShiftType job
            // type (NOT the caller's tenant). Minimal lookup for the job type (company branch above
            // does not Include the ShiftType).
            if (CurrentUserIdOrNull() is not int deleteUserId)
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InvalidUserSession"].Value }) { StatusCode = 401 };
            var deleteJobTypeId = await _db.ShiftTypes
                .IgnoreQueryFilters()
                .Where(st => st.Id == instance.ShiftTypeId)
                .Select(st => st.JobTypeId)
                .FirstOrDefaultAsync();
            if (!await CanAssignForShiftScopeAsync(deleteUserId, instance.CompanyId, deleteJobTypeId))
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InsufficientPermissions"].Value }) { StatusCode = 403 };

            // Delete all assignments first
            // SECURITY-AUDITED: SAFE — scoped by the validated instance.Id
            var assignments = await _db.ShiftAssignments
                .IgnoreQueryFilters()
                .Where(a => a.ShiftInstanceId == instance.Id)
                .ToListAsync();

            _db.ShiftAssignments.RemoveRange(assignments);

            // Delete the instance
            _db.ShiftInstances.Remove(instance);

            // Capture data before deletion for notification
            var deletedInstanceId = instance.Id;
            var deletedWorkDate = instance.WorkDate;
            var deletedShiftTypeId = instance.ShiftTypeId;

            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "ShiftInstance", deletedInstanceId);
            if (!saveResult.Success)
                return new JsonResult(new { success = false, error = saveResult.ErrorMessage }) { StatusCode = 409 };

            LogShiftInstanceDeleted(_logger, deletedInstanceId, assignments.Count);

            await _auditLogService.LogAsync("ShiftDeleted", "ShiftInstance", deletedInstanceId,
                FormattableString.Invariant($"Deleted shift instance on {deletedWorkDate:yyyy-MM-dd} with {assignments.Count} assignments"));

            // Send real-time notification (fire-and-forget)
            try
            {
                // SECURITY-AUDITED: SAFE — entity lookup by unique ID; IgnoreQueryFilters needed for cross-company notification
                var shiftType = await _db.ShiftTypes.IgnoreQueryFilters().FirstOrDefaultAsync(st => st.Id == deletedShiftTypeId);
                if (shiftType?.MoleculeId != null)
                {
                    var groupName = CalendarGroups.Shifts(shiftType.MoleculeId.Value, shiftType.JobTypeId);
                    await _calendarNotification.NotifyCapacityChangedAsync(groupName,
                        new CalendarCapacityChangedEvent(
                            ShiftTypeId: shiftType.Id,
                            Date: deletedWorkDate,
                            NewCapacity: 0,
                            AssignedCount: 0
                        ));
                }
            }
            catch (Exception notifyEx)
            {
                LogCalendarNotificationFailed(_logger, notifyEx, "DeleteShiftInstance");
            }

            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            LogErrorActionInstance(_logger, ex, "deleting shift instance", request.ShiftInstanceId);
            return new JsonResult(new { success = false, error = _localizer["Calendar_Error_DeleteShiftInstanceFailed"].Value });
        }
    }

    public async Task<IActionResult> OnPostAddTraineeAsync([FromBody] AddTraineeRequest request)
    {
        try
        {
            // SECURITY-AUDITED: SAFE — entity lookup by unique ID
            var assignment = await _db.ShiftAssignments
                .IgnoreQueryFilters()
                .Include(a => a.ShiftInstance)
                    .ThenInclude(si => si.ShiftType)
                .FirstOrDefaultAsync(a => a.Id == request.AssignmentId);
            if (assignment == null)
            {
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_AssignmentNotFound"].Value });
            }

            // F7 FIX: per-handler shift-assign authorization. Target scope = the assignment's own
            // ShiftInstance company + its ShiftType job type (NOT the caller's tenant).
            if (CurrentUserIdOrNull() is not int addTraineeUserId)
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InvalidUserSession"].Value }) { StatusCode = 401 };
            if (!await CanAssignForShiftScopeAsync(addTraineeUserId, assignment.ShiftInstance.CompanyId, assignment.ShiftInstance.ShiftType?.JobTypeId))
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InsufficientPermissions"].Value }) { StatusCode = 403 };

            // Validate trainee assignment (self-training, same company, trainee role)
            var validation = await _assignmentService.ValidateTraineeAssignmentAsync(request.TraineeUserId, request.AssignmentId);

            if (!validation.CanAssign)
            {
                return new JsonResult(new
                {
                    success = false,
                    error = validation.Errors.FirstOrDefault()?.Message ?? _localizer["Calendar_Error_ValidationFailed"].Value,
                    errors = validation.Errors.Select(e => new { e.Key, e.Message, e.Category }),
                    fix = MakeFix(validation.Errors.FirstOrDefault()?.Key, request.TraineeUserId),
                });
            }

            if (validation.Warnings.Count > 0 && string.IsNullOrEmpty(request.OverrideToken))
            {
                // For trainee validation, use a synthetic token scoped to assignmentId + traineeUserId
                var overrideToken = _assignmentService.GenerateOverrideToken(
                    request.AssignmentId, request.TraineeUserId, validation.Warnings.Select(w => w.Key).ToList());
                return new JsonResult(new
                {
                    success = false,
                    requiresOverride = true,
                    warnings = validation.Warnings.Select(w => new { w.Key, w.Message, Category = w.Category.ToString(), w.Detail }),
                    overrideToken,
                    assignmentId = request.AssignmentId
                });
            }

            // Validate override token if provided
            if (!string.IsNullOrEmpty(request.OverrideToken) &&
                !_assignmentService.ValidateOverrideToken(request.OverrideToken, request.AssignmentId, request.TraineeUserId))
            {
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InvalidOverrideToken"].Value });
            }

            // Update trainee
            assignment.TraineeUserId = request.TraineeUserId;
            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "ShiftAssignment", request.AssignmentId);
            if (!saveResult.Success)
                return new JsonResult(new { success = false, error = saveResult.ErrorMessage }) { StatusCode = 409 };

            // SECURITY-AUDITED: SAFE — entity lookup by unique ID
            var trainee = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == request.TraineeUserId);

            // Send real-time notification (fire-and-forget)
            try
            {
                var shiftType = assignment.ShiftInstance.ShiftType;
                if (shiftType?.MoleculeId != null)
                {
                    var localizedShiftName = await LocalizeShiftTypeName(shiftType);
                    var groupName = CalendarGroups.Shifts(shiftType.MoleculeId.Value, shiftType.JobTypeId);
                    await _calendarNotification.NotifyAssignmentChangedAsync(groupName,
                        new CalendarAssignmentChangedEvent(
                            ShiftInstanceId: assignment.ShiftInstance.Id,
                            UserId: request.TraineeUserId,
                            UserDisplayName: trainee?.DisplayName,
                            Date: assignment.ShiftInstance.WorkDate,
                            ShiftTypeId: shiftType.Id,
                            ShiftTypeName: localizedShiftName,
                            ChangeType: "TraineeAdded"
                        ));
                }
            }
            catch (Exception notifyEx)
            {
                LogCalendarNotificationFailed(_logger, notifyEx, "AddTrainee");
            }

            await _auditLogService.LogAsync("TraineeAdded", "ShiftAssignment", request.AssignmentId,
                FormattableString.Invariant($"Added trainee {request.TraineeUserId} ({trainee?.DisplayName}) to assignment on {assignment.ShiftInstance.WorkDate:yyyy-MM-dd}"));

            return new JsonResult(new
            {
                success = true,
                traineeName = trainee?.DisplayName
            });
        }
        catch (Exception ex)
        {
            LogErrorAction(_logger, ex, "adding trainee");
            return new JsonResult(new { success = false, error = _localizer["Calendar_Error_AddTraineeFailed"].Value });
        }
    }

    public async Task<IActionResult> OnPostRemoveTraineeAsync([FromBody] RemoveTraineeRequest request)
    {
        try
        {
            // SECURITY-AUDITED: SAFE — entity lookup by unique ID
            var assignment = await _db.ShiftAssignments
                .IgnoreQueryFilters()
                .Include(a => a.ShiftInstance)
                    .ThenInclude(si => si.ShiftType)
                .FirstOrDefaultAsync(a => a.Id == request.AssignmentId);
            if (assignment == null)
            {
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_AssignmentNotFound"].Value });
            }

            // F7 FIX: per-handler shift-assign authorization. Target scope = the assignment's own
            // ShiftInstance company + its ShiftType job type (NOT the caller's tenant).
            if (CurrentUserIdOrNull() is not int removeTraineeUserId)
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InvalidUserSession"].Value }) { StatusCode = 401 };
            if (!await CanAssignForShiftScopeAsync(removeTraineeUserId, assignment.ShiftInstance.CompanyId, assignment.ShiftInstance.ShiftType?.JobTypeId))
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InsufficientPermissions"].Value }) { StatusCode = 403 };

            // Capture trainee info before clearing
            var removedTraineeId = assignment.TraineeUserId;

            // Remove trainee
            assignment.TraineeUserId = null;
            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "ShiftAssignment", request.AssignmentId);
            if (!saveResult.Success)
                return new JsonResult(new { success = false, error = saveResult.ErrorMessage }) { StatusCode = 409 };

            // Send real-time notification (fire-and-forget)
            try
            {
                var shiftType = assignment.ShiftInstance.ShiftType;
                if (shiftType?.MoleculeId != null)
                {
                    var localizedShiftName = await LocalizeShiftTypeName(shiftType);
                    var groupName = CalendarGroups.Shifts(shiftType.MoleculeId.Value, shiftType.JobTypeId);
                    await _calendarNotification.NotifyAssignmentChangedAsync(groupName,
                        new CalendarAssignmentChangedEvent(
                            ShiftInstanceId: assignment.ShiftInstance.Id,
                            UserId: removedTraineeId,
                            UserDisplayName: null,
                            Date: assignment.ShiftInstance.WorkDate,
                            ShiftTypeId: shiftType.Id,
                            ShiftTypeName: localizedShiftName,
                            ChangeType: "TraineeRemoved"
                        ));
                }
            }
            catch (Exception notifyEx)
            {
                LogCalendarNotificationFailed(_logger, notifyEx, "RemoveTrainee");
            }

            await _auditLogService.LogAsync("TraineeRemoved", "ShiftAssignment", request.AssignmentId,
                FormattableString.Invariant($"Removed trainee {removedTraineeId} from assignment on {assignment.ShiftInstance.WorkDate:yyyy-MM-dd}"));

            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            LogErrorAction(_logger, ex, "removing trainee");
            return new JsonResult(new { success = false, error = _localizer["Calendar_Error_RemoveTraineeFailed"].Value });
        }
    }

    public async Task<IActionResult> OnPostChangeUserAsync([FromBody] ChangeUserRequest request)
    {
        try
        {
            // SECURITY-AUDITED: SAFE — entity lookup by unique ID
            var assignment = await _db.ShiftAssignments
                .IgnoreQueryFilters()
                .Include(a => a.ShiftInstance)
                    .ThenInclude(si => si.ShiftType)
                .FirstOrDefaultAsync(a => a.Id == request.AssignmentId);
            if (assignment == null)
            {
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_AssignmentNotFound"].Value });
            }

            // F7 FIX: per-handler shift-assign authorization. Target scope = the assignment's own
            // ShiftInstance company + its ShiftType job type (NOT the caller's tenant).
            if (CurrentUserIdOrNull() is not int changeUserUserId)
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InvalidUserSession"].Value }) { StatusCode = 401 };
            if (!await CanAssignForShiftScopeAsync(changeUserUserId, assignment.ShiftInstance.CompanyId, assignment.ShiftInstance.ShiftType?.JobTypeId))
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InsufficientPermissions"].Value }) { StatusCode = 403 };

            // Validate new user assignment via service
            var validation = await _assignmentService.ValidateShiftAssignmentAsync(request.NewUserId, assignment.ShiftInstanceId);

            if (!validation.CanAssign)
            {
                return new JsonResult(new
                {
                    success = false,
                    error = validation.Errors.FirstOrDefault()?.Message ?? _localizer["Calendar_Error_ValidationFailed"].Value,
                    errors = validation.Errors.Select(e => new { e.Key, e.Message, e.Category }),
                    fix = MakeFix(validation.Errors.FirstOrDefault()?.Key, request.NewUserId),
                });
            }

            if (validation.Warnings.Count > 0 && string.IsNullOrEmpty(request.OverrideToken))
            {
                var overrideToken = _assignmentService.GenerateOverrideToken(
                    assignment.ShiftInstanceId, request.NewUserId, validation.Warnings.Select(w => w.Key).ToList());
                return new JsonResult(new
                {
                    success = false,
                    requiresOverride = true,
                    warnings = validation.Warnings.Select(w => new { w.Key, w.Message, Category = w.Category.ToString(), w.Detail }),
                    overrideToken,
                    shiftInstanceId = assignment.ShiftInstanceId
                });
            }

            // Validate override token if provided
            if (!string.IsNullOrEmpty(request.OverrideToken) &&
                !_assignmentService.ValidateOverrideToken(request.OverrideToken, assignment.ShiftInstanceId, request.NewUserId))
            {
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InvalidOverrideToken"].Value });
            }

            // Change primary user
            assignment.UserId = request.NewUserId;
            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "ShiftAssignment", request.AssignmentId);
            if (!saveResult.Success)
                return new JsonResult(new { success = false, error = saveResult.ErrorMessage }) { StatusCode = 409 };

            // SECURITY-AUDITED: SAFE — entity lookup by unique ID
            var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == request.NewUserId);

            // Send real-time notification (fire-and-forget)
            try
            {
                var shiftType = assignment.ShiftInstance.ShiftType;
                if (shiftType?.MoleculeId != null)
                {
                    var localizedShiftName = await LocalizeShiftTypeName(shiftType);
                    var groupName = CalendarGroups.Shifts(shiftType.MoleculeId.Value, shiftType.JobTypeId);
                    await _calendarNotification.NotifyAssignmentChangedAsync(groupName,
                        new CalendarAssignmentChangedEvent(
                            ShiftInstanceId: assignment.ShiftInstance.Id,
                            UserId: request.NewUserId,
                            UserDisplayName: user?.DisplayName,
                            Date: assignment.ShiftInstance.WorkDate,
                            ShiftTypeId: shiftType.Id,
                            ShiftTypeName: localizedShiftName,
                            ChangeType: "Changed"
                        ));
                }
            }
            catch (Exception notifyEx)
            {
                LogCalendarNotificationFailed(_logger, notifyEx, "ChangeUser");
            }

            await _auditLogService.LogAsync("ShiftUserChanged", "ShiftAssignment", request.AssignmentId,
                FormattableString.Invariant($"Changed user to {request.NewUserId} ({user?.DisplayName}) on {assignment.ShiftInstance.WorkDate:yyyy-MM-dd}"));

            return new JsonResult(new
            {
                success = true,
                employeeName = user?.DisplayName
            });
        }
        catch (Exception ex)
        {
            LogErrorAction(_logger, ex, "changing user");
            return new JsonResult(new { success = false, error = _localizer["Calendar_Error_ChangeUserFailed"].Value });
        }
    }

    public async Task<IActionResult> OnPostUpdateShiftMetadataAsync([FromBody] UpdateShiftMetadataRequest request)
    {
        try
        {
            var companyId = _companyContext.GetCompanyIdOrThrow();

            // SECURITY-AUDITED: SAFE — entity lookup by unique ID with ownership validation below
            var shiftType = await _db.ShiftTypes.IgnoreQueryFilters().FirstOrDefaultAsync(st => st.Id == request.ShiftTypeId);
            if (shiftType == null)
            {
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_ShiftTypeNotFound"].Value });
            }

            // Ownership check: must be company-owned or caller must be in the same molecule
            if (shiftType.CompanyId != companyId)
            {
                if (!shiftType.MoleculeId.HasValue || !await IsCallerInMoleculeAsync(shiftType.MoleculeId.Value))
                    return new JsonResult(new { success = false, error = _localizer["Calendar_Error_ShiftTypeNotFound"].Value }) { StatusCode = 403 };
            }

            // F7 FIX: per-handler shift-assign authorization (defense in depth with the ownership
            // check above). Target scope = the ShiftType's own company + job type (NOT the caller's
            // tenant). Shift-TYPE config uses the same assign-grant family.
            if (CurrentUserIdOrNull() is not int metadataUserId)
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InvalidUserSession"].Value }) { StatusCode = 401 };
            if (!await CanAssignForShiftTypeAsync(metadataUserId, shiftType))
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InsufficientPermissions"].Value }) { StatusCode = 403 };

            // Validate name
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_ShiftNameEmpty"].Value });
            }

            // Parse time strings
            if (!TimeOnly.TryParse(request.StartTime, out var startTime) ||
                !TimeOnly.TryParse(request.EndTime, out var endTime))
            {
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InvalidTimeFormat"].Value });
            }

            // Update metadata (company-scoped rename via NameEn)
            shiftType.NameEn = request.Name.Trim();
            shiftType.Start = startTime;
            shiftType.End = endTime;

            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "ShiftType", request.ShiftTypeId);
            if (!saveResult.Success)
                return new JsonResult(new { success = false, error = saveResult.ErrorMessage }) { StatusCode = 409 };

            // Invalidate caches
            if (shiftType.CompanyId.HasValue)
                _shiftTypeCache.InvalidateCache(shiftType.CompanyId.Value);
            if (shiftType.MoleculeId.HasValue)
                _shiftTypeCache.InvalidateMoleculeCache(shiftType.MoleculeId.Value, shiftType.JobTypeId);

            await _auditLogService.LogAsync("ShiftMetadataUpdated", "ShiftType", request.ShiftTypeId,
                $"Updated shift type name to '{request.Name}', times {request.StartTime}-{request.EndTime}");

            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            LogErrorAction(_logger, ex, "updating shift metadata");
            return new JsonResult(new { success = false, error = _localizer["Calendar_Error_UpdateMetadataFailed"].Value });
        }
    }

    public async Task<IActionResult> OnPostCreateCustomShiftTypeAsync([FromBody] CreateCustomShiftTypeRequest request)
    {
        try
        {
            // Create the custom shift type in the ACTIVE (switcher-aware) company — the one the caller
            // is viewing — not their home-company claim. This makes ownership + the grant-based authz
            // below + cache invalidation all target the company the type will actually appear on.
            var companyId = _tenantResolver.GetCurrentTenantId();
            if (companyId <= 0)
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InvalidUserSession"].Value }) { StatusCode = 401 };

            // Validate name
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_ShiftNameEmpty"].Value });
            }

            // Parse time strings
            if (!TimeOnly.TryParse(request.StartTime, out var startTime) ||
                !TimeOnly.TryParse(request.EndTime, out var endTime))
            {
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InvalidTimeFormat"].Value });
            }

            // Validate molecule scope: the type is created in the active company (above), so the
            // requested molecule must be that company's molecule. IsCallerInMoleculeAsync is now
            // switcher-aware (keys off the active company), so it is the correct shared gate here;
            // the grant-based CanAssignForShiftScopeAsync below is the real authorization.
            if (request.MoleculeId.HasValue && !await IsCallerInMoleculeAsync(request.MoleculeId.Value))
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_UnauthorizedMolecule"].Value }) { StatusCode = 403 };

            // F7 FIX: per-handler shift-assign authorization (defense in depth with the molecule
            // access check above). Target scope = the new ShiftType's company (the caller's tenant,
            // which is where the row is created below) + the requested job type. Shift-TYPE config
            // uses the same assign-grant family.
            if (CurrentUserIdOrNull() is not int createTypeUserId)
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InvalidUserSession"].Value }) { StatusCode = 401 };
            if (!await CanAssignForShiftScopeAsync(createTypeUserId, companyId, request.JobTypeId))
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InsufficientPermissions"].Value }) { StatusCode = 403 };

            // Create custom shift type with a unique internal key but user-visible name
            var customKey = $"CUSTOM_{Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant()}";

            var shiftType = new ShiftType
            {
                CompanyId = companyId,
                Key = customKey,
                NameEn = request.Name.Trim(), // User-provided name (NO KEY LEAKAGE)
                Start = startTime,
                End = endTime,
                MoleculeId = request.MoleculeId,
                JobTypeId = request.JobTypeId
            };

            _db.ShiftTypes.Add(shiftType);
            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "ShiftType");
            if (!saveResult.Success)
                return new JsonResult(new { success = false, error = saveResult.ErrorMessage }) { StatusCode = 409 };

            LogCustomShiftTypeCreated(_logger, shiftType.Id, shiftType.NameEn, companyId);

            await _auditLogService.LogAsync("ShiftTypeCreated", "ShiftType", shiftType.Id,
                $"Created custom shift type '{shiftType.NameEn}' ({customKey})");

            // Invalidate caches so the new shift type appears immediately
            _shiftTypeCache.InvalidateCache(companyId);
            if (shiftType.MoleculeId.HasValue)
                _shiftTypeCache.InvalidateMoleculeCache(shiftType.MoleculeId.Value, shiftType.JobTypeId);

            // Send real-time notification (fire-and-forget)
            try
            {
                if (shiftType.MoleculeId != null)
                {
                    var groupName = CalendarGroups.Shifts(shiftType.MoleculeId.Value, shiftType.JobTypeId);
                    await _calendarNotification.NotifyCapacityChangedAsync(groupName,
                        new CalendarCapacityChangedEvent(
                            ShiftTypeId: shiftType.Id,
                            Date: DateOnly.FromDateTime(DateTime.Today),
                            NewCapacity: 0,
                            AssignedCount: 0
                        ));
                }
            }
            catch (Exception notifyEx)
            {
                LogCalendarNotificationFailed(_logger, notifyEx, "CreateCustomShiftType");
            }

            var localizedShiftName = await LocalizeShiftTypeName(shiftType);
            return new JsonResult(new
            {
                success = true,
                shiftTypeId = shiftType.Id,
                shiftName = localizedShiftName // Returns the user-friendly name
            });
        }
        catch (Exception ex)
        {
            LogErrorAction(_logger, ex, "creating custom shift type");
            return new JsonResult(new { success = false, error = _localizer["Calendar_Error_CreateCustomShiftTypeFailed"].Value });
        }
    }

    public class UpdateShiftMetadataRequest
    {
        public int ShiftTypeId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string StartTime { get; set; } = string.Empty;
        public string EndTime { get; set; } = string.Empty;
    }

    public class CreateCustomShiftTypeRequest
    {
        public string Name { get; set; } = string.Empty;
        public string StartTime { get; set; } = string.Empty;
        public string EndTime { get; set; } = string.Empty;
        public int? MoleculeId { get; set; }
        public int? JobTypeId { get; set; }
    }

    public class CreateShiftInstanceRequest
    {
        public int ShiftTypeId { get; set; }
        public DateOnly Date { get; set; }
        public int StaffingRequired { get; set; }
    }

    public class AssignUserToSlotRequest
    {
        public int AssignmentId { get; set; }
        public int UserId { get; set; }
        public string? OverrideToken { get; set; }
    }

    // ===================== Draft Mode (Epic 4) =====================

    private int? CurrentUserIdOrNull()
        => int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : (int?)null;

    private Task<bool> OwnsActiveDraftAsync(int draftSessionId, int userId)
        => _db.DraftSessions.AnyAsync(d => d.Id == draftSessionId && d.OwnerUserId == userId && d.Status == DraftSessionStatus.Active);

    public async Task<IActionResult> OnPostEnterDraftAsync([FromBody] EnterDraftRequest request)
    {
        if (CurrentUserIdOrNull() is not int userId)
            return new JsonResult(new { success = false }) { StatusCode = 401 };
        var scope = new DraftScope(DraftSurface.Shifts, request.MoleculeId, request.JobTypeId,
            AreaId: null, request.WeekStart, request.WeekEnd);
        var draft = await _draftLifecycle.EnterAsync(userId, scope);
        return new JsonResult(new { success = true, draftSessionId = draft.Id });
    }

    public async Task<IActionResult> OnPostDraftClearAsync([FromBody] DraftClearRequest request)
    {
        if (CurrentUserIdOrNull() is not int userId)
            return new JsonResult(new { success = false }) { StatusCode = 401 };
        if (!await OwnsActiveDraftAsync(request.DraftSessionId, userId))
            return new JsonResult(new { success = false, error = _localizer["Calendar_Error_DraftInactive"].Value }) { StatusCode = 409 };
        await _draftService.StageClearAsync(request.DraftSessionId, request.ShiftTypeId, request.Date, request.UserId);
        return new JsonResult(new { success = true, draft = true });
    }

    public async Task<IActionResult> OnPostDraftAddTraineeAsync([FromBody] DraftAddTraineeRequest request)
    {
        if (CurrentUserIdOrNull() is not int userId)
            return new JsonResult(new { success = false }) { StatusCode = 401 };
        if (!await OwnsActiveDraftAsync(request.DraftSessionId, userId))
            return new JsonResult(new { success = false, error = _localizer["Calendar_Error_DraftInactive"].Value }) { StatusCode = 409 };
        // Coordinate-keyed (never assignmentId): a staged primary has no persisted row.
        await _draftService.StageTraineeAsync(request.DraftSessionId, request.ShiftTypeId, request.Date, request.PrimaryUserId, request.TraineeUserId);
        return new JsonResult(new { success = true, draft = true });
    }

    public async Task<IActionResult> OnPostDraftRemoveTraineeAsync([FromBody] DraftRemoveTraineeRequest request)
    {
        if (CurrentUserIdOrNull() is not int userId)
            return new JsonResult(new { success = false }) { StatusCode = 401 };
        if (!await OwnsActiveDraftAsync(request.DraftSessionId, userId))
            return new JsonResult(new { success = false, error = _localizer["Calendar_Error_DraftInactive"].Value }) { StatusCode = 409 };
        await _draftService.StageTraineeClearAsync(request.DraftSessionId, request.ShiftTypeId, request.Date, request.PrimaryUserId);
        return new JsonResult(new { success = true, draft = true });
    }

    public async Task<IActionResult> OnPostCommitDraftAsync([FromBody] DraftActionRequest request)
    {
        if (CurrentUserIdOrNull() is not int userId)
            return new JsonResult(new { success = false }) { StatusCode = 401 };
        if (!await OwnsActiveDraftAsync(request.DraftSessionId, userId))
            return new JsonResult(new { success = false, error = _localizer["Calendar_Error_DraftInactive"].Value }) { StatusCode = 409 };

        var result = await _draftLifecycle.CommitAsync(request.DraftSessionId, userId);
        // Per-cell policy (Spec F §5): the session commits; some cells may be Skipped (drifted) or Unauthorized
        // (grant revoked). The UI shows this per-cell report instead of a single all-or-nothing "conflict".
        return new JsonResult(new
        {
            success = result.Committed,
            applied = result.Applied.Select(c => new { rowId = c.RowId, date = c.WorkDate.ToString("yyyy-MM-dd"), label = c.Label }),
            appliedCount = result.Applied.Count,
            skipped = result.Skipped.Select(c => new { rowId = c.RowId, date = c.WorkDate.ToString("yyyy-MM-dd"), label = c.Label }),
            unauthorized = result.Unauthorized.Select(c => new { rowId = c.RowId, date = c.WorkDate.ToString("yyyy-MM-dd"), label = c.Label }),
            issues = result.ValidationIssues.Select(i => new { rowId = i.RowId, date = i.WorkDate.ToString("yyyy-MM-dd"), i.UserId, i.Message }),
            notifiedGroups = result.NotifiedGroups
        });
    }

    /// <summary>
    /// PF15 disclosure (read-only): how many of a draft's staged cells sit on shift types NOT shown by the
    /// active tab, so the commit UI can warn before applying off-view changes. Commit is molecule+jobtype-scoped
    /// and always applies ALL staged cells — this only surfaces a count. tabId null/0 (the "All" tab) or a tab
    /// with no shift-type restriction (empty = all shift types) means nothing is off-view.
    /// </summary>
    public async Task<IActionResult> OnPostDraftOffTabCountAsync([FromBody] DraftOffTabCountRequest request)
    {
        if (CurrentUserIdOrNull() is not int userId)
            return new JsonResult(new { success = false }) { StatusCode = 401 };
        if (!await OwnsActiveDraftAsync(request.DraftSessionId, userId))
            return new JsonResult(new { success = false, error = _localizer["Calendar_Error_DraftInactive"].Value }) { StatusCode = 409 };

        var overlay = await _draftService.GetOverlayAsync(request.DraftSessionId);
        var offView = 0;
        if (request.TabId is int tabId && tabId > 0)
        {
            var tabShiftTypeIds = await _shiftTabService.GetShiftTypeIdsForTabAsync(tabId);
            if (tabShiftTypeIds.Count > 0) // empty selection = all shift types (no restriction) → nothing off-view
                offView = overlay.Count(c => !tabShiftTypeIds.Contains(c.ShiftTypeId));
        }
        return new JsonResult(new { success = true, total = overlay.Count, offView });
    }

    public class DraftOffTabCountRequest
    {
        public int DraftSessionId { get; set; }
        public int? TabId { get; set; }
    }

    public async Task<IActionResult> OnPostDiscardDraftAsync([FromBody] DraftActionRequest request)
    {
        if (CurrentUserIdOrNull() is not int userId)
            return new JsonResult(new { success = false }) { StatusCode = 401 };
        // Allow discarding any of your own sessions (active or stale), not others'.
        var owns = await _db.DraftSessions.AnyAsync(d => d.Id == request.DraftSessionId && d.OwnerUserId == userId);
        if (!owns)
            return new JsonResult(new { success = false }) { StatusCode = 403 };
        await _draftLifecycle.DiscardAsync(request.DraftSessionId, userId);
        return new JsonResult(new { success = true });
    }

    public class EnterDraftRequest
    {
        public int MoleculeId { get; set; }
        public int? JobTypeId { get; set; }
        public DateOnly WeekStart { get; set; }
        public DateOnly WeekEnd { get; set; }
    }
    public class DraftActionRequest { public int DraftSessionId { get; set; } }
    public class DraftClearRequest
    {
        public int DraftSessionId { get; set; }
        public int ShiftTypeId { get; set; }
        public DateOnly Date { get; set; }
        public int UserId { get; set; }
    }

    public class DraftAddTraineeRequest
    {
        public int DraftSessionId { get; set; }
        public int ShiftTypeId { get; set; }
        public DateOnly Date { get; set; }
        public int PrimaryUserId { get; set; }
        public int TraineeUserId { get; set; }
    }

    public class DraftRemoveTraineeRequest
    {
        public int DraftSessionId { get; set; }
        public int ShiftTypeId { get; set; }
        public DateOnly Date { get; set; }
        public int PrimaryUserId { get; set; }
    }

    public class AssignEmployeeRequest
    {
        public int ShiftTypeId { get; set; }
        public DateOnly Date { get; set; }
        public int UserId { get; set; }
        public string? OverrideToken { get; set; }
        /// <summary>When set, the assignment is STAGED into this draft sandbox instead of written live.</summary>
        public int? DraftSessionId { get; set; }
        /// <summary>
        /// Optional: the EXACT shift instance to fill. Callers that already know the instance
        /// (e.g. Justice "make it real" drilling into a specific hole) pass it so we don't
        /// re-resolve by ShiftType+Date — which is ambiguous when the same ShiftType exists in
        /// multiple companies of the molecule and would FirstOrDefault the wrong company's instance.
        /// When null/0, the legacy ShiftType+Date resolution (create-if-missing) is used.
        /// </summary>
        public int? ShiftInstanceId { get; set; }
    }

    public class UnassignEmployeeRequest
    {
        public int AssignmentId { get; set; }
    }

    public class AddTraineeRequest
    {
        public int AssignmentId { get; set; }
        public int TraineeUserId { get; set; }
        public string? OverrideToken { get; set; }
    }

    public class RemoveTraineeRequest
    {
        public int AssignmentId { get; set; }
    }

    public class ChangeUserRequest
    {
        public int AssignmentId { get; set; }
        public int NewUserId { get; set; }
        public string? OverrideToken { get; set; }
    }

    public class ClearAssignmentRequest
    {
        public int AssignmentId { get; set; }
    }

    public class UpdateShiftStaffingRequest
    {
        public int ShiftInstanceId { get; set; }
        public int StaffingRequired { get; set; }
        public bool ForceRemoval { get; set; } = false;
    }

    public class EnsureShiftInstanceRequest
    {
        public int ShiftTypeId { get; set; }
        public DateOnly Date { get; set; }
        public int StaffingRequired { get; set; }
    }

    public class DeleteShiftInstanceRequest
    {
        public int ShiftInstanceId { get; set; }
        public int? MoleculeId { get; set; }
    }

    /// <summary>
    /// Detaches a ShiftInstance from its Program (if any), marking it as manually modified.
    /// </summary>
    public async Task<IActionResult> OnPostDetachInstanceAsync([FromBody] DetachInstanceRequest request)
    {
        try
        {
            // SECURITY-AUDITED: SAFE — entity lookup by unique ID
            var instance = await _db.ShiftInstances
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(si => si.Id == request.ShiftInstanceId);

            if (instance == null)
            {
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_ShiftInstanceNotFound"].Value });
            }

            // F7 FIX: per-handler shift-assign authorization. Target scope = the instance's own company
            // + its ShiftType job type (NOT the caller's tenant). Minimal lookup for the job type.
            if (CurrentUserIdOrNull() is not int detachUserId)
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InvalidUserSession"].Value }) { StatusCode = 401 };
            var detachJobTypeId = await _db.ShiftTypes
                .IgnoreQueryFilters()
                .Where(st => st.Id == instance.ShiftTypeId)
                .Select(st => st.JobTypeId)
                .FirstOrDefaultAsync();
            if (!await CanAssignForShiftScopeAsync(detachUserId, instance.CompanyId, detachJobTypeId))
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InsufficientPermissions"].Value }) { StatusCode = 403 };

            // Call service to detach instance
            await _programService.DetachInstanceAsync(request.ShiftInstanceId, request.Reason);

            await _auditLogService.LogAsync("ShiftDetached", "ShiftInstance", request.ShiftInstanceId,
                $"Detached from program. Reason: {request.Reason}");

            LogShiftInstanceDetached(_logger, request.ShiftInstanceId, request.Reason);

            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            LogErrorActionInstance(_logger, ex, "detaching shift instance", request.ShiftInstanceId);
            return new JsonResult(new { success = false, error = _localizer["Calendar_Error_DetachShiftInstanceFailed"].Value });
        }
    }

    /// <summary>
    /// Resets a detached ShiftInstance back to its Program defaults.
    /// </summary>
    public async Task<IActionResult> OnPostResetInstanceToProgramAsync([FromBody] ResetInstanceRequest request)
    {
        try
        {
            // SECURITY-AUDITED: SAFE — entity lookup by unique ID
            var instance = await _db.ShiftInstances
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(si => si.Id == request.ShiftInstanceId);

            if (instance == null)
            {
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_ShiftInstanceNotFound"].Value });
            }

            if (!instance.IsDetached || !instance.OriginalProgramId.HasValue)
            {
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_ShiftNotDetached"].Value });
            }

            // F7 FIX: per-handler shift-assign authorization. Target scope = the instance's own company
            // + its ShiftType job type (NOT the caller's tenant). Minimal lookup for the job type.
            if (CurrentUserIdOrNull() is not int resetUserId)
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InvalidUserSession"].Value }) { StatusCode = 401 };
            var resetJobTypeId = await _db.ShiftTypes
                .IgnoreQueryFilters()
                .Where(st => st.Id == instance.ShiftTypeId)
                .Select(st => st.JobTypeId)
                .FirstOrDefaultAsync();
            if (!await CanAssignForShiftScopeAsync(resetUserId, instance.CompanyId, resetJobTypeId))
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InsufficientPermissions"].Value }) { StatusCode = 403 };

            // Call service to reset instance to Program defaults
            await _programService.ResetInstanceToProgramAsync(request.ShiftInstanceId);

            await _auditLogService.LogAsync("ShiftReset", "ShiftInstance", request.ShiftInstanceId,
                $"Reset to program {instance.OriginalProgramId} defaults");

            LogShiftInstanceReset(_logger, request.ShiftInstanceId, instance.OriginalProgramId);

            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            LogErrorActionInstance(_logger, ex, "resetting shift instance", request.ShiftInstanceId);
            return new JsonResult(new { success = false, error = _localizer["Calendar_Error_ResetShiftInstanceFailed"].Value });
        }
    }

    /// <summary>
    /// Get 7-day availability for all employees (for availability cubes display).
    /// Returns availability status (free/busy/partial) for each of the next 7 days.
    /// Supports molecule mode via moleculeId+jobTypeId query params.
    /// Uses batch queries to avoid N+1 performance issues.
    /// </summary>
    public async Task<IActionResult> OnGetEmployeeAvailabilityAsync(int? moleculeId = null, int? jobTypeId = null)
    {
        var startDate = DateOnly.FromDateTime(DateTime.Today);
        var endDate = startDate.AddDays(6);

        // Load employees — molecule or company scoped
        List<(int Id, string DisplayName)> employees;

        if (moleculeId.HasValue && jobTypeId.HasValue)
        {
            // Verify caller has access to this molecule
            if (!await IsCallerInMoleculeAsync(moleculeId.Value))
                return new JsonResult(new { error = _localizer["Calendar_Error_UnauthorizedMolecule"].Value }) { StatusCode = 403 };

            // SECURITY-AUDITED: SAFE — molecule access validated above, re-scoped by moleculeId + jobTypeId
            var molCompanyIds = await _db.Companies
                .IgnoreQueryFilters()
                .Where(c => c.MoleculeId == moleculeId.Value)
                .Select(c => c.Id)
                .ToListAsync();

            employees = (await _db.Users
                .IgnoreQueryFilters()
                .Where(u => u.IsActive && molCompanyIds.Contains(u.CompanyId) && u.JobTypeId == jobTypeId.Value)
                .OrderBy(u => u.DisplayName)
                .Select(u => new { u.Id, u.DisplayName })
                .ToListAsync())
                .Select(u => (u.Id, u.DisplayName))
                .ToList();
        }
        else
        {
            employees = (await _db.Users
                .Where(u => u.IsActive && (u.Role == UserRole.Employee || u.Role == UserRole.Trainee))
                .OrderBy(u => u.DisplayName)
                .Select(u => new { u.Id, u.DisplayName })
                .ToListAsync())
                .Select(u => (u.Id, u.DisplayName))
                .ToList();
        }

        // Batch load all shifts for these employees in the date range (avoids N+1)
        var empIds = employees.Select(e => e.Id).ToHashSet();

        var shiftsByUserDate = (moleculeId.HasValue
            ? await _db.ShiftAssignments
                .IgnoreQueryFilters()
                .Where(sa => sa.UserId != null && empIds.Contains(sa.UserId!.Value)
                    && sa.ShiftInstance!.WorkDate >= startDate && sa.ShiftInstance.WorkDate <= endDate)
                .Select(sa => new { UserId = sa.UserId!.Value, Date = sa.ShiftInstance!.WorkDate })
                .ToListAsync()
            : await _db.ShiftAssignments
                .Where(sa => sa.UserId != null && empIds.Contains(sa.UserId!.Value)
                    && sa.ShiftInstance!.WorkDate >= startDate && sa.ShiftInstance.WorkDate <= endDate)
                .Select(sa => new { UserId = sa.UserId!.Value, Date = sa.ShiftInstance!.WorkDate })
                .ToListAsync())
            .GroupBy(x => (x.UserId, x.Date))
            .ToDictionary(g => g.Key, g => true);

        var timeOffByUserDate = (moleculeId.HasValue
            ? await _db.TimeOffRequests
                .IgnoreQueryFilters()
                .Where(tor => empIds.Contains(tor.UserId)
                    && tor.StartDate <= endDate && tor.EndDate >= startDate
                    && tor.Status == RequestStatus.Approved)
                .ToListAsync()
            : await _db.TimeOffRequests
                .Where(tor => empIds.Contains(tor.UserId)
                    && tor.StartDate <= endDate && tor.EndDate >= startDate
                    && tor.Status == RequestStatus.Approved)
                .ToListAsync());

        var result = new List<object>();

        foreach (var emp in employees)
        {
            var availability = new List<object>();

            for (var date = startDate; date <= endDate; date = date.AddDays(1))
            {
                var hasShift = shiftsByUserDate.ContainsKey((emp.Id, date));
                var hasTimeOff = timeOffByUserDate.Any(tor => tor.UserId == emp.Id && tor.StartDate <= date && tor.EndDate >= date);

                var status = "free";
                var tooltip = _localizer["Roster_Available"].Value;

                if (hasTimeOff)
                {
                    status = "busy";
                    tooltip = _localizer["Roster_TimeOff"].Value;
                }
                else if (hasShift)
                {
                    status = "partial";
                    tooltip = _localizer["Roster_HasShift"].Value;
                }

                availability.Add(new {
                    date = date.ToString("d/M", CultureInfo.InvariantCulture),
                    dayName = date.DayOfWeek.ToString().Substring(0, 3),
                    status,
                    tooltip
                });
            }

            result.Add(new {
                id = emp.Id,
                name = emp.DisplayName,
                availability
            });
        }

        return new JsonResult(result);
    }

    /// <summary>
    /// Get roster of employees with their availability status for the Roster Dock.
    /// Checks availability across the entire date range being displayed.
    /// </summary>
    public async Task<IActionResult> OnGetGetRosterEmployeesAsync(DateOnly? startDate = null, DateOnly? endDate = null)
    {
        try
        {
            // Switcher-aware active company: the company-mode roster (else branch below) filters
            // AppUser by CompanyId; the AppUser query filter already scopes to the active tenant, so
            // the home-company claim here produced an empty list for switched Owners. Use the tenant.
            var companyId = _tenantResolver.GetCurrentTenantId();
            if (companyId <= 0)
                throw new InvalidOperationException("No active company/tenant context for the roster.");

            // Use provided date range or default to current week
            var start = startDate ?? StartDate;
            var end = endDate ?? EndDate;

            // Get employees — molecule-scoped if MoleculeId+JobTypeId provided, else company-scoped
            var moleculeIdParam = Request.Query.ContainsKey("moleculeId") && int.TryParse(Request.Query["moleculeId"], out var mid) ? mid : (int?)null;
            var jobTypeIdParam = Request.Query.ContainsKey("jobTypeId") && int.TryParse(Request.Query["jobTypeId"], out var jtid) ? jtid : (int?)null;

            List<RosterEmployee> rosterEmployees;
            List<int>? rosterMoleculeCompanyIds = null;
            if (moleculeIdParam.HasValue && jobTypeIdParam.HasValue)
            {
                // Verify caller has access to this molecule
                if (!await IsCallerInMoleculeAsync(moleculeIdParam.Value))
                    return new JsonResult(new { error = _localizer["Calendar_Error_UnauthorizedMolecule"].Value }) { StatusCode = 403 };

                // Molecule mode: load from all companies in the molecule with matching JobType
                // SECURITY-AUDITED: SAFE — molecule access validated above, re-scoped by moleculeId+jobTypeId
                rosterMoleculeCompanyIds = await _db.Companies
                    .IgnoreQueryFilters()
                    .Where(c => c.MoleculeId == moleculeIdParam.Value)
                    .Select(c => c.Id)
                    .ToListAsync();

                var companyNames = (await _db.Companies
                    .IgnoreQueryFilters()
                    .Where(c => rosterMoleculeCompanyIds.Contains(c.Id))
                    .ToListAsync())
                    .ToDictionary(c => c.Id, c => c.LocalizedName);

                rosterEmployees = (await _db.Users
                    .IgnoreQueryFilters()
                    .Where(u => u.IsActive && rosterMoleculeCompanyIds.Contains(u.CompanyId)
                        && u.JobTypeId == jobTypeIdParam.Value)
                    .OrderBy(u => u.CompanyId)
                    .ThenBy(u => u.DisplayName)
                    .Select(u => new { u.Id, u.DisplayName, u.CompanyId, u.AvatarFileName })
                    .ToListAsync())
                    .Select(u => new RosterEmployee
                    {
                        Id = u.Id,
                        DisplayName = u.DisplayName,
                        CompanyName = companyNames.GetValueOrDefault(u.CompanyId, ""),
                        AvatarUrl = !string.IsNullOrWhiteSpace(u.AvatarFileName)
                            ? $"/avatars/{u.CompanyId}/{u.Id}_thumb.jpg" : null
                    })
                    .ToList();
            }
            else
            {
                rosterEmployees = (await _db.Users
                    .Where(u => u.CompanyId == companyId && u.IsActive)
                    .OrderBy(u => u.DisplayName)
                    .Select(u => new { u.Id, u.DisplayName, u.AvatarFileName })
                    .ToListAsync())
                    .Select(u => new RosterEmployee
                    {
                        Id = u.Id,
                        DisplayName = u.DisplayName,
                        CompanyName = "",
                        AvatarUrl = !string.IsNullOrWhiteSpace(u.AvatarFileName)
                            ? $"/avatars/{companyId}/{u.Id}_thumb.jpg" : null
                    })
                    .ToList();
            }

            var employees = rosterEmployees;

            // Check busy status across the entire date range in a single batch query (avoids N+1)
            var employeeStatusMap = new Dictionary<int, (bool hasVacation, bool hasShift, bool hasChore)>();

            Dictionary<DateOnly, Dictionary<int, BusyStatus>> busyByDate;
            if (rosterMoleculeCompanyIds != null)
            {
                busyByDate = await _busyUserService.GetBusyUsersByDateRangeForCompaniesAsync(
                    start, end, TimeOnly.MinValue, TimeOnly.MaxValue, rosterMoleculeCompanyIds);
            }
            else
            {
                busyByDate = await _busyUserService.GetBusyUsersByDateRangeAsync(
                    start, end, TimeOnly.MinValue, TimeOnly.MaxValue);
            }

            foreach (var (date, busyInfo) in busyByDate)
            {
                foreach (var emp in employees)
                {
                    if (busyInfo.TryGetValue(emp.Id, out var busyStatus))
                    {
                        var status = employeeStatusMap.GetValueOrDefault(emp.Id);
                        employeeStatusMap[emp.Id] = (
                            status.hasVacation || busyStatus.HasVacation,
                            status.hasShift || busyStatus.HasShift,
                            status.hasChore || busyStatus.HasChore
                        );
                    }
                }
            }

            var employeeList = employees.Select(emp =>
            {
                var status = employeeStatusMap.GetValueOrDefault(emp.Id);
                return new
                {
                    id = emp.Id,
                    name = emp.DisplayName,
                    avatarUrl = emp.AvatarUrl,
                    onVacation = status.hasVacation,
                    hasShift = status.hasShift,
                    hasChore = status.hasChore
                };
            }).ToList();

            return new JsonResult(new { employees = employeeList });
        }
        catch (Exception ex)
        {
            LogErrorAction(_logger, ex, "loading roster employees");
            return new JsonResult(new { success = false, error = _localizer["Calendar_Error_LoadEmployeesFailed"].Value });
        }
    }

    /// <summary>
    /// Fill a range of dates with shifts copied from a source shift.
    /// Supports three modes: exact copy, staffing only, or apply Program defaults.
    /// </summary>
    public async Task<IActionResult> OnPostFillRangeAsync([FromBody] FillRangeRequest request)
    {
        try
        {
            if (request.TargetDates.Count > 730)
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_FillRangeTooLarge"].Value });

            using var transaction = await _db.Database.BeginTransactionAsync();

            // Validate source instance
            // SECURITY-AUDITED: SAFE — entity lookup by unique ID
            var sourceInstance = await _db.ShiftInstances
                .IgnoreQueryFilters()
                .Include(si => si.ShiftType)
                .FirstOrDefaultAsync(si => si.Id == request.SourceInstanceId);

            if (sourceInstance == null)
            {
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_SourceShiftNotFound"].Value });
            }

            // F7 FIX: per-handler shift-assign authorization. Target scope = the source instance's own
            // company + its ShiftType job type (NOT the caller's tenant). Fill copies into the source's
            // company, so the source scope governs the whole operation.
            if (CurrentUserIdOrNull() is not int fillUserId)
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InvalidUserSession"].Value }) { StatusCode = 401 };
            if (!await CanAssignForShiftScopeAsync(fillUserId, sourceInstance.CompanyId, sourceInstance.ShiftType?.JobTypeId))
                return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InsufficientPermissions"].Value }) { StatusCode = 403 };

            // Load source assignments separately
            // SECURITY-AUDITED: SAFE — scoped by validated sourceInstance.Id
            var sourceAssignments = await _db.ShiftAssignments
                .IgnoreQueryFilters()
                .Include(sa => sa.User)
                .Include(sa => sa.Trainee)
                .Where(sa => sa.ShiftInstanceId == sourceInstance.Id)
                .ToListAsync();

            var createdCount = 0;
            var updatedCount = 0;

            foreach (var targetDate in request.TargetDates)
            {
                DateOnly parsedDate;
                if (!DateOnly.TryParse(targetDate, out parsedDate))
                {
                    LogInvalidTargetDate(_logger, targetDate);
                    continue;
                }

                // Check if instance already exists for this date and shift type
                // SECURITY-AUDITED: SAFE — entity lookup by unique ShiftTypeId+WorkDate composite key
                var existingInstance = await _db.ShiftInstances
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(si =>
                        si.ShiftTypeId == sourceInstance.ShiftTypeId &&
                        si.WorkDate == parsedDate);

                // Load existing assignments if instance exists
                List<ShiftAssignment> existingAssignments = new();
                if (existingInstance != null)
                {
                    // SECURITY-AUDITED: SAFE — scoped by validated existingInstance.Id
                    existingAssignments = await _db.ShiftAssignments
                        .IgnoreQueryFilters()
                        .Where(sa => sa.ShiftInstanceId == existingInstance.Id)
                        .ToListAsync();
                }

                switch (request.Mode)
                {
                    case "exact":
                        // Copy exact: duplicate all assignments including trainees
                        if (existingInstance != null)
                        {
                            // Update existing instance
                            existingInstance.StaffingRequired = sourceInstance.StaffingRequired;
                            existingInstance.IsDetached = true;
                            existingInstance.OriginalProgramId = sourceInstance.OriginalProgramId;

                            // Remove old assignments
                            _db.ShiftAssignments.RemoveRange(existingAssignments);

                            // Add new assignments (copy from source)
                            foreach (var sourceAssignment in sourceAssignments)
                            {
                                _db.ShiftAssignments.Add(new ShiftAssignment
                                {
                                    ShiftInstanceId = existingInstance.Id,
                                    UserId = sourceAssignment.UserId,
                                    TraineeUserId = sourceAssignment.TraineeUserId,
                                    CompanyId = sourceInstance.CompanyId  // Use source instance's company
                                });
                            }

                            updatedCount++;
                        }
                        else
                        {
                            // Create new instance
                            var newInstance = new ShiftInstance
                            {
                                CompanyId = sourceInstance.CompanyId,  // Use source instance's company
                                ShiftTypeId = sourceInstance.ShiftTypeId,
                                WorkDate = parsedDate,
                                StaffingRequired = sourceInstance.StaffingRequired,
                                IsDetached = true,
                                OriginalProgramId = sourceInstance.OriginalProgramId
                            };

                            _db.ShiftInstances.Add(newInstance);
                            {
                                var fillSaveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                                    () => _db.SaveChangesAsync(), "ShiftInstance");
                                if (!fillSaveResult.Success)
                                    return new JsonResult(new { success = false, error = fillSaveResult.ErrorMessage }) { StatusCode = 409 };
                            }

                            // Add assignments
                            foreach (var sourceAssignment in sourceAssignments)
                            {
                                _db.ShiftAssignments.Add(new ShiftAssignment
                                {
                                    ShiftInstanceId = newInstance.Id,
                                    UserId = sourceAssignment.UserId,
                                    TraineeUserId = sourceAssignment.TraineeUserId,
                                    CompanyId = sourceInstance.CompanyId  // Use source instance's company
                                });
                            }

                            createdCount++;
                        }
                        break;

                    case "staffing":
                        // Copy staffing only: create empty slots with same count
                        if (existingInstance != null)
                        {
                            existingInstance.StaffingRequired = sourceInstance.StaffingRequired;
                            existingInstance.IsDetached = true;

                            // Remove old assignments
                            _db.ShiftAssignments.RemoveRange(existingAssignments);

                            // Add empty slots
                            for (int i = 0; i < sourceInstance.StaffingRequired; i++)
                            {
                                _db.ShiftAssignments.Add(new ShiftAssignment
                                {
                                    ShiftInstanceId = existingInstance.Id,
                                    UserId = null,
                                    CompanyId = sourceInstance.CompanyId  // Use source instance's company
                                });
                            }

                            updatedCount++;
                        }
                        else
                        {
                            var newInstance = new ShiftInstance
                            {
                                CompanyId = sourceInstance.CompanyId,  // Use source instance's company
                                ShiftTypeId = sourceInstance.ShiftTypeId,
                                WorkDate = parsedDate,
                                StaffingRequired = sourceInstance.StaffingRequired,
                                IsDetached = true
                            };

                            _db.ShiftInstances.Add(newInstance);
                            {
                                var fillSaveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                                    () => _db.SaveChangesAsync(), "ShiftInstance");
                                if (!fillSaveResult.Success)
                                    return new JsonResult(new { success = false, error = fillSaveResult.ErrorMessage }) { StatusCode = 409 };
                            }

                            // Add empty slots
                            for (int i = 0; i < sourceInstance.StaffingRequired; i++)
                            {
                                _db.ShiftAssignments.Add(new ShiftAssignment
                                {
                                    ShiftInstanceId = newInstance.Id,
                                    UserId = null,
                                    CompanyId = sourceInstance.CompanyId  // Use source instance's company
                                });
                            }

                            createdCount++;
                        }
                        break;

                    case "program":
                        // Apply Program defaults: use Program template if available
                        if (sourceInstance.OriginalProgramId.HasValue)
                        {
                            var program = await _db.ShiftPrograms
                                .Include(p => p.ProgramDays)
                                .FirstOrDefaultAsync(p => p.Id == sourceInstance.OriginalProgramId.Value);

                            if (program != null)
                            {
                                var dayOfWeek = parsedDate.DayOfWeek;
                                var programDay = program.ProgramDays.FirstOrDefault(pd => pd.DayOfWeek == dayOfWeek);

                                if (programDay != null)
                                {
                                    var staffingRequired = programDay.StaffingRequired ?? program.DefaultStaffingRequired;

                                    if (existingInstance != null)
                                    {
                                        existingInstance.StaffingRequired = staffingRequired;
                                        existingInstance.IsDetached = false;
                                        existingInstance.OriginalProgramId = program.Id;

                                        // Remove old assignments
                                        _db.ShiftAssignments.RemoveRange(existingAssignments);

                                        // Add empty slots
                                        for (int i = 0; i < staffingRequired; i++)
                                        {
                                            _db.ShiftAssignments.Add(new ShiftAssignment
                                            {
                                                ShiftInstanceId = existingInstance.Id,
                                                UserId = null,
                                                CompanyId = sourceInstance.CompanyId  // Use source instance's company
                                            });
                                        }

                                        updatedCount++;
                                    }
                                    else
                                    {
                                        var newInstance = new ShiftInstance
                                        {
                                            CompanyId = sourceInstance.CompanyId,  // Use source instance's company
                                            ShiftTypeId = sourceInstance.ShiftTypeId,
                                            WorkDate = parsedDate,
                                            StaffingRequired = staffingRequired,
                                            IsDetached = false,
                                            OriginalProgramId = program.Id
                                        };

                                        _db.ShiftInstances.Add(newInstance);
                                        {
                                            var fillSaveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                                                () => _db.SaveChangesAsync(), "ShiftInstance");
                                            if (!fillSaveResult.Success)
                                                return new JsonResult(new { success = false, error = fillSaveResult.ErrorMessage }) { StatusCode = 409 };
                                        }

                                        // Add empty slots
                                        for (int i = 0; i < staffingRequired; i++)
                                        {
                                            _db.ShiftAssignments.Add(new ShiftAssignment
                                            {
                                                ShiftInstanceId = newInstance.Id,
                                                UserId = null,
                                                CompanyId = sourceInstance.CompanyId  // Use source instance's company
                                            });
                                        }

                                        createdCount++;
                                    }
                                }
                            }
                        }
                        else
                        {
                            return new JsonResult(new { success = false, error = _localizer["Calendar_Error_SourceShiftNotLinkedToProgram"].Value });
                        }
                        break;

                    default:
                        return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InvalidFillMode"].Value });
                }
            }

            var finalSaveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "ShiftInstance", request.SourceInstanceId);
            if (!finalSaveResult.Success)
                return new JsonResult(new { success = false, error = finalSaveResult.ErrorMessage }) { StatusCode = 409 };

            await transaction.CommitAsync();

            LogFillRangeCompleted(_logger, createdCount, updatedCount, request.Mode);

            // Send real-time notification (fire-and-forget) — one aggregated notification for the fill operation
            try
            {
                var shiftType = sourceInstance.ShiftType;
                if (shiftType?.MoleculeId != null)
                {
                    var localizedShiftName = await LocalizeShiftTypeName(shiftType);
                    var groupName = CalendarGroups.Shifts(shiftType.MoleculeId.Value, shiftType.JobTypeId);
                    // Send one notification per filled target date
                    foreach (var targetDate in request.TargetDates)
                    {
                        if (DateOnly.TryParse(targetDate, out var parsedNotifyDate))
                        {
                            // Look up the actual instance to get current state
                            // SECURITY-AUDITED: SAFE — entity lookup by unique ShiftTypeId+WorkDate composite key
                            var filledInstance = await _db.ShiftInstances
                                .IgnoreQueryFilters()
                                .FirstOrDefaultAsync(si => si.ShiftTypeId == sourceInstance.ShiftTypeId && si.WorkDate == parsedNotifyDate);
                            if (filledInstance != null)
                            {
                                await _calendarNotification.NotifyAssignmentChangedAsync(groupName,
                                    new CalendarAssignmentChangedEvent(
                                        ShiftInstanceId: filledInstance.Id,
                                        UserId: null,
                                        UserDisplayName: null,
                                        Date: parsedNotifyDate,
                                        ShiftTypeId: shiftType.Id,
                                        ShiftTypeName: localizedShiftName,
                                        ChangeType: "Filled"
                                    ));
                            }
                        }
                    }
                }
            }
            catch (Exception notifyEx)
            {
                LogCalendarNotificationFailed(_logger, notifyEx, "FillRange");
            }

            await _auditLogService.LogAsync("ShiftRangeFilled", "ShiftInstance", request.SourceInstanceId,
                $"Filled {createdCount + updatedCount} shifts (created: {createdCount}, updated: {updatedCount}) mode: {request.Mode}");

            return new JsonResult(new
            {
                success = true,
                created = createdCount,
                updated = updatedCount,
                message = string.Format(CultureInfo.CurrentCulture, _localizer["FillRange_Success"].Value, createdCount + updatedCount)
            });
        }
        catch (Exception ex)
        {
            LogErrorAction(_logger, ex, "filling range");
            return new JsonResult(new { success = false, error = _localizer["Calendar_Error_FillRangeFailed"].Value });
        }
    }

    public class DetachInstanceRequest
    {
        public int ShiftInstanceId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public class ResetInstanceRequest
    {
        public int ShiftInstanceId { get; set; }
    }

    /// <summary>
    /// Get conflicts and warnings for Radar Mode.
    /// Returns cells that are underfilled or have overlapping assignments.
    /// </summary>
    public async Task<IActionResult> OnGetGetConflictsAsync(string? start, string? view, int? moleculeId = null, int? jobTypeId = null)
    {
        try
        {
            var companyId = _companyContext.GetCompanyIdOrThrow();

            // Calculate date range from query parameters (same logic as OnGetAsync)
            string viewMode = view?.ToLowerInvariant() ?? "week";
            if (viewMode != "week" && viewMode != "2weeks" && viewMode != "month")
            {
                viewMode = "week";
            }

            DateOnly startDate;
            if (string.IsNullOrEmpty(start))
            {
                var today = DateOnly.FromDateTime(DateTime.Today);
                startDate = today.AddDays(-(int)today.DayOfWeek); // Start of week (Sunday)
            }
            else if (!DateOnly.TryParse(start, out startDate))
            {
                var today = DateOnly.FromDateTime(DateTime.Today);
                startDate = today.AddDays(-(int)today.DayOfWeek);
            }

            DateOnly endDate = viewMode switch
            {
                "2weeks" => startDate.AddDays(13),
                "month" => startDate.AddDays(DateTime.DaysInMonth(startDate.Year, startDate.Month) - 1),
                _ => startDate.AddDays(6)
            };

            // Get all instances in the current view — molecule or company scoped
            List<ShiftInstance> instances;
            List<ShiftAssignment> assignments;

            if (moleculeId.HasValue && jobTypeId.HasValue)
            {
                // Verify caller has access to this molecule
                if (!await IsCallerInMoleculeAsync(moleculeId.Value))
                    return new JsonResult(new { error = _localizer["Calendar_Error_UnauthorizedMolecule"].Value }) { StatusCode = 403 };

                // SECURITY-AUDITED: SAFE — molecule access validated above, re-scoped by moleculeId+jobTypeId
                instances = await _db.ShiftInstances
                    .IgnoreQueryFilters()
                    .Include(si => si.ShiftType)
                    .Where(si => si.ShiftType.MoleculeId == moleculeId.Value
                        && si.ShiftType.JobTypeId == jobTypeId.Value
                        && si.WorkDate >= startDate && si.WorkDate <= endDate)
                    .ToListAsync();

                var instanceIds = instances.Select(i => i.Id).ToList();
                assignments = await _db.ShiftAssignments
                    .IgnoreQueryFilters()
                    .Where(a => instanceIds.Contains(a.ShiftInstanceId))
                    .ToListAsync();
            }
            else
            {
                instances = await _db.ShiftInstances
                    .Where(si => si.WorkDate >= startDate && si.WorkDate <= endDate)
                    .Include(si => si.ShiftType)
                    .ToListAsync();

                var instanceIds = instances.Select(i => i.Id).ToList();
                assignments = await _db.ShiftAssignments
                    .Where(a => instanceIds.Contains(a.ShiftInstanceId))
                    .ToListAsync();
            }

            var conflicts = new List<object>();

            foreach (var instance in instances)
            {
                var instanceAssignments = assignments.Where(a => a.ShiftInstanceId == instance.Id).ToList();
                var filledCount = instanceAssignments.Count(a => a.UserId.HasValue);
                var localizedShiftName = await LocalizeShiftTypeName(instance.ShiftType);

                // Check if underfilled
                if (filledCount < instance.StaffingRequired)
                {
                    conflicts.Add(new
                    {
                        instanceId = instance.Id,
                        shiftTypeId = instance.ShiftTypeId,
                        shiftTypeName = localizedShiftName,
                        date = instance.WorkDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        type = "underfilled",
                        severity = "warning",
                        message = string.Format(CultureInfo.CurrentCulture, _localizer["FillRange_Understaffed"].Value, filledCount, instance.StaffingRequired),
                        filled = filledCount,
                        required = instance.StaffingRequired
                    });
                }

                // Check for overfilling
                if (filledCount > instance.StaffingRequired)
                {
                    conflicts.Add(new
                    {
                        instanceId = instance.Id,
                        shiftTypeId = instance.ShiftTypeId,
                        shiftTypeName = localizedShiftName,
                        date = instance.WorkDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        type = "overfilled",
                        severity = "error",
                        message = string.Format(CultureInfo.CurrentCulture, _localizer["FillRange_Overstaffed"].Value, filledCount, instance.StaffingRequired),
                        filled = filledCount,
                        required = instance.StaffingRequired
                    });
                }
            }

            return new JsonResult(new { conflicts });
        }
        catch (Exception ex)
        {
            LogErrorAction(_logger, ex, "getting conflicts");
            return new JsonResult(new { success = false, error = _localizer["Calendar_Error_LoadConflictsFailed"].Value });
        }
    }

    public class FillRangeRequest
    {
        public int SourceInstanceId { get; set; }
        public List<string> TargetDates { get; set; } = new();
        public List<int> TargetInstanceIds { get; set; } = new();
        public string Mode { get; set; } = "exact"; // exact, staffing, program
    }

    public class RosterEmployee
    {
        public int Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
    }

    // --- Molecule mode helpers (matching Calendar/Shifts pattern) ---

    private async Task LoadAvailableMoleculesAsync(int userId, int userMoleculeId)
    {
        // Use grant-based scope resolution that handles all scope levels
        // (Project → Area → Molecule → Company → Self)
        var moleculeIds = new HashSet<int>(
            await _grantService.GetAccessibleMoleculeIdsForGrantAsync(userId, "ViewShifts"));

        // Always include user's own molecule as fallback
        moleculeIds.Add(userMoleculeId);

        AvailableMolecules = await _db.Molecules
            .Where(m => moleculeIds.Contains(m.Id) && m.IsActive)
            .OrderBy(m => m.DisplayName)
            .ToListAsync();
    }

    /// <summary>
    /// Per-handler authorization for shift-assignment mutations. Checks the unified, job-type-agnostic
    /// AssignShifts grant (2026-06-16 collapse of the 8 Assign*Shifts grants) plus the AdminAccess
    /// short-circuit. The class-level [Authorize(Policy="Grant:ManagerHomeAccess")] gate is NOT
    /// sufficient: ~59% of users hold it but must not be able to mutate shifts.
    ///
    /// CRITICAL — pass the TARGET shift/instance/assignment's actual scope (NOT the caller's own
    /// company). Manager AssignShifts grants are stored MOLECULE-scoped (MoleculeId set, CompanyId
    /// null); HasGrantWithScopeAsync cascades a molecule-scoped grant to ANY company within that
    /// molecule, so passing the target's company/molecule preserves molecule-wide access while still
    /// rejecting cross-molecule / cross-tenant targets.
    /// </summary>
    private async Task<bool> CanAssignForShiftScopeAsync(int userId, int? companyId, int? moleculeId, int? jobTypeId)
    {
        if (await _grantService.HasGrantAsync(userId, "AdminAccess"))
            return true;

        return await _grantService.HasGrantWithScopeAsync(userId, "AssignShifts",
            companyId: companyId, moleculeId: moleculeId, jobTypeId: jobTypeId);
    }

    // Convenience overload for call sites that already resolved the TARGET's real company (e.g. the
    // owning ShiftInstance.CompanyId). No molecule needed — the company cascades correctly.
    private Task<bool> CanAssignForShiftScopeAsync(int userId, int? companyId, int? jobTypeId)
        => CanAssignForShiftScopeAsync(userId, companyId, moleculeId: null, jobTypeId);

    // Fix #5: resolve a ShiftType's TRUE scope. A molecule/area-scoped type has CompanyId == null;
    // GetEffectiveCompanyId would wrongly resolve that to the CALLER's company, so use the type's own
    // MoleculeId instead — rejecting a foreign-molecule shift type the caller has no scope over.
    private Task<bool> CanAssignForShiftTypeAsync(int userId, ShiftType shiftType)
        => shiftType.CompanyId.HasValue
            ? CanAssignForShiftScopeAsync(userId, companyId: shiftType.CompanyId.Value, moleculeId: null, jobTypeId: shiftType.JobTypeId)
            : CanAssignForShiftScopeAsync(userId, companyId: null, moleculeId: shiftType.MoleculeId, jobTypeId: shiftType.JobTypeId);

    /// <summary>
    /// Validates that the caller's ACTIVE (switcher-aware) company belongs to the specified molecule.
    /// Used to prevent unauthorized molecule access via crafted API calls. internal for testability.
    /// </summary>
    internal async Task<bool> IsCallerInMoleculeAsync(int moleculeId)
    {
        // Switcher-aware: gate on the caller's ACTIVE company (Owner-selection / member-switch), not
        // the raw home-company claim. A switched Owner (home in one molecule, viewing another) was
        // otherwise 403'd from every molecule-mode operation. The active company is validated by the
        // switcher (OwnerCompanySelectorService / member-cookie-vs-claim), so this stays a sound
        // anti-IDOR gate: molecule access requires the active company to belong to that molecule.
        var companyId = _tenantResolver.GetCurrentTenantId();
        return await _db.Companies.AnyAsync(c => c.Id == companyId && c.MoleculeId == moleculeId);
    }

    private async Task LoadAvailableJobTypesAsync(int? moleculeId)
    {
        if (!moleculeId.HasValue)
        {
            AvailableJobTypes = new List<JobType>();
            return;
        }

        AvailableJobTypes = await _jobTypeService.GetJobTypesForMoleculeAsync(moleculeId.Value);
    }
}
