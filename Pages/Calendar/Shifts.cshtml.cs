using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Resources;
using ShiftManager.Services;
using ShiftManager.ViewComponents;
using System.Security.Claims;

namespace ShiftManager.Pages.Calendar;

/// <summary>
/// Excel-style Shifts Calendar page - Primary deliverable for Excel Calendars feature.
/// Supports shift-based and user-based view modes with molecule/job type filtering.
/// </summary>
[Authorize]
public class ShiftsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IShiftCalendarService _calendarService;
    private readonly IGrantService _grantService;
    private readonly ICompanyContext _companyContext;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly ILogger<ShiftsModel> _logger;

    public ShiftsModel(
        AppDbContext db,
        IShiftCalendarService calendarService,
        IGrantService grantService,
        ICompanyContext companyContext,
        IStringLocalizer<SharedResources> localizer,
        ILogger<ShiftsModel> logger)
    {
        _db = db;
        _calendarService = calendarService;
        _grantService = grantService;
        _companyContext = companyContext;
        _localizer = localizer;
        _logger = logger;
    }

    // Query parameters
    [BindProperty(SupportsGet = true)]
    public int? MoleculeId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? JobTypeId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Start { get; set; }

    [BindProperty(SupportsGet = true)]
    public string ViewMode { get; set; } = "week"; // week, 2weeks, month

    [BindProperty(SupportsGet = true)]
    public string Mode { get; set; } = "shift"; // shift, user

    [BindProperty(SupportsGet = true)]
    public bool CapacityMode { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool JustMine { get; set; }

    // Page properties
    public ExcelCalendarTableViewModel CalendarData { get; set; } = new();
    public bool CanEdit { get; set; }
    public List<Molecule> AvailableMolecules { get; set; } = new();
    public List<JobType> AvailableJobTypes { get; set; } = new();
    public Molecule? SelectedMolecule { get; set; }
    public JobType? SelectedJobType { get; set; }
    public int CurrentUserId { get; set; }

    // Navigation
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string PreviousStart { get; set; } = string.Empty;
    public string NextStart { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync()
    {
        // Get current user ID
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId))
        {
            _logger.LogError("Invalid or missing NameIdentifier claim");
            return RedirectToPage("/Error");
        }
        CurrentUserId = currentUserId;

        // Get user's company and molecule
        var companyId = _companyContext.CompanyId;
        if (!companyId.HasValue)
        {
            _logger.LogWarning("User {UserId} has no company context", currentUserId);
            return RedirectToPage("/Error");
        }

        var userCompany = await _db.Companies
            .Include(c => c.Molecule)
            .FirstOrDefaultAsync(c => c.Id == companyId.Value);

        if (userCompany?.MoleculeId == null)
        {
            _logger.LogWarning("User's company {CompanyId} has no molecule", companyId.Value);
            return RedirectToPage("/Error");
        }

        // Get user's job type
        var currentUser = await _db.Users
            .Include(u => u.JobType)
            .FirstOrDefaultAsync(u => u.Id == currentUserId);

        // Set defaults for MoleculeId and JobTypeId
        if (!MoleculeId.HasValue)
        {
            MoleculeId = userCompany.MoleculeId;
        }

        if (!JobTypeId.HasValue && currentUser?.JobTypeId != null)
        {
            JobTypeId = currentUser.JobTypeId;
        }

        // Load available molecules (user has access to via grants or their own)
        await LoadAvailableMoleculesAsync(currentUserId, userCompany.MoleculeId.Value);

        // Validate selected molecule is accessible
        if (!AvailableMolecules.Any(m => m.Id == MoleculeId))
        {
            MoleculeId = userCompany.MoleculeId;
        }

        SelectedMolecule = AvailableMolecules.FirstOrDefault(m => m.Id == MoleculeId);

        // Load available job types for selected molecule's area
        await LoadAvailableJobTypesAsync(SelectedMolecule?.AreaId);

        // Validate selected job type
        if (JobTypeId.HasValue && !AvailableJobTypes.Any(jt => jt.Id == JobTypeId))
        {
            JobTypeId = AvailableJobTypes.FirstOrDefault()?.Id;
        }

        SelectedJobType = AvailableJobTypes.FirstOrDefault(jt => jt.Id == JobTypeId);

        // Calculate date range
        CalculateDateRange();

        // Check edit permission
        CanEdit = await _grantService.HasGrantAsync(currentUserId, "AssignAlhutShifts") ||
                  await _grantService.HasGrantAsync(currentUserId, "AssignTextShifts");

        // Build calendar data based on mode
        if (MoleculeId.HasValue && JobTypeId.HasValue)
        {
            if (Mode == "user")
            {
                await BuildUserBasedCalendarAsync(MoleculeId.Value, JobTypeId.Value);
            }
            else
            {
                await BuildShiftBasedCalendarAsync(MoleculeId.Value, JobTypeId.Value);
            }
        }

        _logger.LogInformation(
            "Shifts calendar loaded for User {UserId}, Molecule {MoleculeId}, JobType {JobTypeId}, Mode {Mode}, ViewMode {ViewMode}",
            currentUserId, MoleculeId, JobTypeId, Mode, ViewMode);

        return Page();
    }

    private void CalculateDateRange()
    {
        // Parse start date or default to start of current week
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (!string.IsNullOrEmpty(Start) && DateOnly.TryParse(Start, out var parsedDate))
        {
            StartDate = parsedDate;
        }
        else
        {
            // Default to Sunday of current week
            StartDate = GetStartOfWeek(today);
        }

        // Calculate end date based on view mode
        EndDate = ViewMode switch
        {
            "2weeks" => StartDate.AddDays(13),
            "month" => StartDate.AddDays(DateTime.DaysInMonth(StartDate.Year, StartDate.Month) - 1),
            _ => StartDate.AddDays(6) // week
        };

        // Calculate navigation dates
        var daysToMove = ViewMode switch
        {
            "2weeks" => 14,
            "month" => DateTime.DaysInMonth(StartDate.Year, StartDate.Month),
            _ => 7
        };

        PreviousStart = StartDate.AddDays(-daysToMove).ToString("yyyy-MM-dd");
        NextStart = StartDate.AddDays(daysToMove).ToString("yyyy-MM-dd");
    }

    private static DateOnly GetStartOfWeek(DateOnly date)
    {
        int daysFromSunday = (int)date.DayOfWeek;
        return date.AddDays(-daysFromSunday);
    }

    private async Task LoadAvailableMoleculesAsync(int userId, int userMoleculeId)
    {
        // Start with user's own molecule
        var moleculeIds = new HashSet<int> { userMoleculeId };

        // Add molecules from grants
        var userGrants = await _grantService.GetUserGrantsAsync(userId);
        foreach (var grant in userGrants)
        {
            if (grant.MoleculeId.HasValue)
            {
                moleculeIds.Add(grant.MoleculeId.Value);
            }
        }

        // Load molecules
        AvailableMolecules = await _db.Molecules
            .Where(m => moleculeIds.Contains(m.Id) && m.IsActive)
            .OrderBy(m => m.DisplayName)
            .ToListAsync();
    }

    private async Task LoadAvailableJobTypesAsync(int? areaId)
    {
        if (!areaId.HasValue)
        {
            AvailableJobTypes = new List<JobType>();
            return;
        }

        AvailableJobTypes = await _db.JobTypes
            .Where(jt => jt.AreaId == areaId.Value && jt.IsActive)
            .OrderBy(jt => jt.SortOrder)
            .ThenBy(jt => jt.DisplayName)
            .ToListAsync();
    }

    private async Task BuildShiftBasedCalendarAsync(int moleculeId, int jobTypeId)
    {
        // Get shift types for this molecule/job type
        var shiftTypes = await _db.ShiftTypes
            .IgnoreQueryFilters()
            .Where(st => st.MoleculeId == moleculeId && st.JobTypeId == jobTypeId)
            .OrderBy(st => st.Start)
            .ThenBy(st => st.Name)
            .ToListAsync();

        // Get shift instances and assignments
        var instances = await _calendarService.GetShiftInstancesAsync(moleculeId, jobTypeId, StartDate, EndDate);
        var assignments = await _calendarService.GetAssignmentsAsync(moleculeId, jobTypeId, StartDate, EndDate);

        // Build rows - one per shift type
        var rows = new List<ExcelCalendarRow>();
        foreach (var shiftType in shiftTypes)
        {
            var row = new ExcelCalendarRow
            {
                Id = $"shift-{shiftType.Id}",
                Label = $"{shiftType.Name} ({shiftType.Start:HH:mm}-{shiftType.End:HH:mm})",
                Color = shiftType.RowColor
            };

            // Build cells for each date
            row.Cells = BuildCellsForShiftType(shiftType.Id, instances, assignments);
            rows.Add(row);
        }

        CalendarData = new ExcelCalendarTableViewModel
        {
            StartDate = StartDate,
            EndDate = EndDate,
            ViewMode = ViewMode,
            IsReadOnly = !CanEdit,
            CalendarType = "shifts",
            Rows = rows
        };
    }

    private async Task BuildUserBasedCalendarAsync(int moleculeId, int jobTypeId)
    {
        // Get users for this molecule/job type
        var users = await _calendarService.GetUsersForCalendarAsync(moleculeId, jobTypeId);

        // Filter to just current user if "Just Mine" is enabled
        if (JustMine)
        {
            users = users.Where(u => u.Id == CurrentUserId).ToList();
        }

        // Get shift instances and assignments
        var instances = await _calendarService.GetShiftInstancesAsync(moleculeId, jobTypeId, StartDate, EndDate);
        var assignments = await _calendarService.GetAssignmentsAsync(moleculeId, jobTypeId, StartDate, EndDate);

        // Get overlays (vacation, chores, on-duty)
        var overlays = await _calendarService.GetOverlaysAsync(moleculeId, StartDate, EndDate);

        // Build rows - one per user
        var rows = new List<ExcelCalendarRow>();
        foreach (var user in users)
        {
            var row = new ExcelCalendarRow
            {
                Id = $"user-{user.Id}",
                Label = user.DisplayName
            };

            // Build cells for each date
            row.Cells = BuildCellsForUser(user.Id, instances, assignments, overlays);
            rows.Add(row);
        }

        CalendarData = new ExcelCalendarTableViewModel
        {
            StartDate = StartDate,
            EndDate = EndDate,
            ViewMode = ViewMode,
            IsReadOnly = !CanEdit,
            CalendarType = "shifts",
            Rows = rows
        };
    }

    private Dictionary<DateOnly, ExcelCalendarCell> BuildCellsForShiftType(
        int shiftTypeId,
        List<ShiftInstance> instances,
        List<ShiftAssignment> assignments)
    {
        var cells = new Dictionary<DateOnly, ExcelCalendarCell>();

        for (var date = StartDate; date <= EndDate; date = date.AddDays(1))
        {
            var instance = instances.FirstOrDefault(i => i.ShiftTypeId == shiftTypeId && i.WorkDate == date);
            var cell = new ExcelCalendarCell();

            if (instance != null)
            {
                var instanceAssignments = assignments
                    .Where(a => a.ShiftInstanceId == instance.Id)
                    .ToList();

                cell.Assignments = instanceAssignments.Select(a => new ExcelCalendarAssignment
                {
                    Id = a.Id,
                    Name = a.User?.DisplayName ?? _localizer["Unassigned"].Value,
                    IsTrainee = a.TraineeUserId.HasValue
                }).ToList();

                if (CapacityMode)
                {
                    cell.Capacity = instanceAssignments.Count;
                    cell.DefaultCapacity = instance.StaffingRequired;
                }
            }

            cells[date] = cell;
        }

        return cells;
    }

    private Dictionary<DateOnly, ExcelCalendarCell> BuildCellsForUser(
        int userId,
        List<ShiftInstance> instances,
        List<ShiftAssignment> assignments,
        Dictionary<(int UserId, DateOnly Date), FyiOverlayData> overlays)
    {
        var cells = new Dictionary<DateOnly, ExcelCalendarCell>();

        for (var date = StartDate; date <= EndDate; date = date.AddDays(1))
        {
            var cell = new ExcelCalendarCell();

            // Get user's assignments for this date
            var userAssignments = assignments
                .Where(a => (a.UserId == userId || a.TraineeUserId == userId) && a.ShiftInstance.WorkDate == date)
                .ToList();

            cell.Assignments = userAssignments.Select(a => new ExcelCalendarAssignment
            {
                Id = a.Id,
                Name = a.ShiftInstance.ShiftType?.Name ?? "Shift",
                IsTrainee = a.TraineeUserId == userId
            }).ToList();

            // Add overlay data
            if (overlays.TryGetValue((userId, date), out var overlay))
            {
                cell.Overlay = new ExcelCalendarOverlay
                {
                    HasVacation = overlay.HasVacation,
                    HasChore = overlay.HasChore,
                    HasOnDuty = overlay.HasOnDuty,
                    OtherItems = overlay.OtherShifts
                };
            }

            cells[date] = cell;
        }

        return cells;
    }
}
