using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;
using ShiftManager.ViewComponents;
using System.Security.Claims;

namespace ShiftManager.Pages.Calendar;

/// <summary>
/// Excel-style On-Call (Day Shifts) Calendar page - Shows duty types as rows, dates as columns.
/// On-Duty assignments are global (cross-company), optionally filtered by area.
/// </summary>
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires [Authorize];
// OnDuty is global by design; user lookup scoped by area-based company membership
[Authorize]
public class OnCallModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IOnDutyService _onDutyService;
    private readonly IGrantService _grantService;
    private readonly ICompanyContext _companyContext;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly ILogger<OnCallModel> _logger;

    public OnCallModel(
        AppDbContext db,
        IOnDutyService onDutyService,
        IGrantService grantService,
        ICompanyContext companyContext,
        IStringLocalizer<SharedResources> localizer,
        ILogger<OnCallModel> logger)
    {
        _db = db;
        _onDutyService = onDutyService;
        _grantService = grantService;
        _companyContext = companyContext;
        _localizer = localizer;
        _logger = logger;
    }

    // Query parameters
    [BindProperty(SupportsGet = true)]
    public int? AreaId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Start { get; set; }

    [BindProperty(SupportsGet = true)]
    public string ViewMode { get; set; } = "week"; // week, 2weeks, month

    [BindProperty(SupportsGet = true)]
    public int? DutyTypeFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool JustMine { get; set; }

    // Page properties
    public ExcelCalendarTableViewModel CalendarData { get; set; } = new();
    public bool CanEdit { get; set; }
    public List<Area> AvailableAreas { get; set; } = new();
    public Area? SelectedArea { get; set; }
    public List<DutyTypeInfo> DutyTypes { get; set; } = new();
    public int CurrentUserId { get; set; }

    // Navigation
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string PreviousStart { get; set; } = string.Empty;
    public string NextStart { get; set; } = string.Empty;

    /// <summary>
    /// Info about a duty type (built-in or custom)
    /// </summary>
    public class DutyTypeInfo
    {
        public int TypeValue { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
        public bool IsBackupType { get; set; }
        public int? PrimaryTypeValue { get; set; } // Links backup to its primary
    }

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

        // Get user's company
        var companyId = _companyContext.CompanyId;
        if (!companyId.HasValue)
        {
            _logger.LogWarning("User {UserId} has no company context", currentUserId);
            return RedirectToPage("/Error");
        }

        var userCompany = await _db.Companies
            .Include(c => c.Molecule)
            .ThenInclude(m => m != null ? m.Area : null)
            .FirstOrDefaultAsync(c => c.Id == companyId.Value);

        // Get user's area through the hierarchy: Company -> Molecule -> Area
        var userAreaId = userCompany?.Molecule?.AreaId;

        // Load available areas (for filtering, On-Duty is global but we can filter view by area)
        await LoadAvailableAreasAsync(currentUserId);

        // Set default AreaId if not provided and user has one
        if (!AreaId.HasValue && userAreaId != null)
        {
            AreaId = userAreaId;
        }

        // Validate selected area is accessible
        if (AreaId.HasValue && !AvailableAreas.Any(a => a.Id == AreaId))
        {
            AreaId = userAreaId;
        }

        SelectedArea = AvailableAreas.FirstOrDefault(a => a.Id == AreaId);

        // Load duty types (built-in + custom)
        await LoadDutyTypesAsync();

        // Calculate date range
        CalculateDateRange();

        // Check edit permission - using ManageOnDutyTypes grant
        CanEdit = await _grantService.HasGrantAsync(currentUserId, "ManageOnDutyTypes");

        // Build calendar data (duty types as rows, dates as columns)
        await BuildDutyTypeBasedCalendarAsync();

        _logger.LogInformation(
            "On-Call calendar loaded for User {UserId}, Area {AreaId}, ViewMode {ViewMode}",
            currentUserId, AreaId, ViewMode);

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

    private async Task LoadAvailableAreasAsync(int userId)
    {
        // Load all active areas - On-Duty is global but we allow area filtering for the view
        // Users with Director+ role see all areas, others see their company's area
        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
        {
            AvailableAreas = new List<Area>();
            return;
        }

        var canViewAllAreas = await _grantService.HasGrantAsync(userId, "ViewAllAreas");
        if (canViewAllAreas)
        {
            // Users with ViewAllAreas grant see all active areas
            AvailableAreas = await _db.Areas
                .Where(a => a.IsActive)
                .OrderBy(a => a.DisplayName)
                .ToListAsync();
        }
        else
        {
            // Others see areas from their grants or their own company's area (via Molecule)
            var company = await _db.Companies
                .Include(c => c.Molecule)
                .FirstOrDefaultAsync(c => c.Id == user.CompanyId);
            if (company?.Molecule?.AreaId != null)
            {
                var area = await _db.Areas.FirstOrDefaultAsync(a => a.Id == company.Molecule.AreaId && a.IsActive);
                if (area != null)
                {
                    AvailableAreas = new List<Area> { area };
                }
            }

            // Also add areas from grants
            var userGrants = await _grantService.GetUserGrantsAsync(userId);
            var grantAreaIds = userGrants
                .Where(g => g.AreaId.HasValue)
                .Select(g => g.AreaId!.Value)
                .Distinct()
                .ToList();

            if (grantAreaIds.Any())
            {
                var grantAreas = await _db.Areas
                    .Where(a => grantAreaIds.Contains(a.Id) && a.IsActive)
                    .ToListAsync();

                foreach (var area in grantAreas)
                {
                    if (!AvailableAreas.Any(a => a.Id == area.Id))
                    {
                        AvailableAreas.Add(area);
                    }
                }

                AvailableAreas = AvailableAreas.OrderBy(a => a.DisplayName).ToList();
            }
        }
    }

    private async Task LoadDutyTypesAsync()
    {
        DutyTypes = new List<DutyTypeInfo>();

        // Add built-in types
        DutyTypes.Add(new DutyTypeInfo
        {
            TypeValue = (int)OnDutyType.Hakam,
            Name = _localizer["Hakam"],
            Icon = "👮",
            Color = "#8B4513", // Saddle Brown from design tokens
            IsBackupType = false
        });

        DutyTypes.Add(new DutyTypeInfo
        {
            TypeValue = (int)OnDutyType.Lead,
            Name = _localizer["Lead"],
            Icon = "⭐",
            Color = "#1E3A5F", // Deep Navy from design tokens
            IsBackupType = false
        });

        // Load custom types from OnDutyTypeConfig
        var customTypes = await _db.OnDutyTypeConfigs
            .Where(t => t.IsActive)
            .OrderBy(t => t.TypeValue)
            .ToListAsync();

        foreach (var customType in customTypes)
        {
            var isBackup = customType.NameEn.Contains("Backup", StringComparison.OrdinalIgnoreCase) ||
                           customType.NameHe.Contains("רזרבה", StringComparison.OrdinalIgnoreCase);

            DutyTypes.Add(new DutyTypeInfo
            {
                TypeValue = customType.TypeValue,
                Name = Thread.CurrentThread.CurrentUICulture.Name.StartsWith("he")
                    ? customType.NameHe
                    : customType.NameEn,
                Icon = customType.Icon,
                Color = customType.Color,
                IsBackupType = isBackup,
                PrimaryTypeValue = isBackup ? (int)OnDutyType.Hakam : null // Backup-hakam links to Hakam
            });
        }
    }

    private async Task BuildDutyTypeBasedCalendarAsync()
    {
        // Get all on-duty assignments for the date range
        var onDuties = await _onDutyService.GetOnDutiesAsync(
            startDate: StartDate,
            endDate: EndDate,
            includeCanceled: false);

        // If area filter is set, filter by users in that area (via Molecule)
        if (AreaId.HasValue)
        {
            // Get molecules in this area
            var moleculeIdsInArea = await _db.Molecules
                .Where(m => m.AreaId == AreaId.Value)
                .Select(m => m.Id)
                .ToListAsync();

            // Get companies in those molecules
            var companyIdsInArea = await _db.Companies
                .Where(c => c.MoleculeId.HasValue && moleculeIdsInArea.Contains(c.MoleculeId.Value))
                .Select(c => c.Id)
                .ToListAsync();

            onDuties = onDuties
                .Where(o => o.User != null && companyIdsInArea.Contains(o.User.CompanyId))
                .ToList();
        }

        // If "Just Mine" filter is set, filter by current user
        if (JustMine)
        {
            onDuties = onDuties.Where(o => o.UserId == CurrentUserId).ToList();
        }

        // Apply duty type filter if set
        if (DutyTypeFilter.HasValue)
        {
            onDuties = onDuties.Where(o => (int)o.Type == DutyTypeFilter.Value).ToList();
        }

        // Group duty types by primary/backup relationship
        var primaryTypes = DutyTypes.Where(dt => !dt.IsBackupType).ToList();
        var backupTypes = DutyTypes.Where(dt => dt.IsBackupType).ToList();

        // Build rows - one per duty type
        var rows = new List<ExcelCalendarRow>();
        foreach (var dutyType in DutyTypes)
        {
            var row = new ExcelCalendarRow
            {
                Id = $"dutytype-{dutyType.TypeValue}",
                Label = $"{dutyType.Icon} {dutyType.Name}",
                Color = dutyType.Color
            };

            // Build cells for each date
            row.Cells = BuildCellsForDutyType(dutyType.TypeValue, onDuties);
            rows.Add(row);
        }

        CalendarData = new ExcelCalendarTableViewModel
        {
            StartDate = StartDate,
            EndDate = EndDate,
            ViewMode = ViewMode,
            IsReadOnly = !CanEdit,
            CalendarType = "oncall",
            Rows = rows,
            Groups = null // No grouping for duty types
        };
    }

    private Dictionary<DateOnly, ExcelCalendarCell> BuildCellsForDutyType(
        int dutyTypeValue,
        List<OnDuty> onDuties)
    {
        var cells = new Dictionary<DateOnly, ExcelCalendarCell>();

        for (var date = StartDate; date <= EndDate; date = date.AddDays(1))
        {
            var cell = new ExcelCalendarCell();

            // Get on-duty assignments for this type and date
            var assignments = onDuties
                .Where(o => (int)o.Type == dutyTypeValue && o.Date == date)
                .ToList();

            cell.Assignments = assignments.Select(o => new ExcelCalendarAssignment
            {
                Id = o.Id,
                Name = o.User?.DisplayName ?? _localizer["Unknown"],
                Role = o.Notes, // Use notes as additional info
                UserId = o.UserId
            }).ToList();

            cells[date] = cell;
        }

        return cells;
    }
}
