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
/// Excel-style Chores Calendar page - Shows chore assignments by user across dates.
/// Chores are molecule-scoped (cross-company within molecule).
/// </summary>
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires [Authorize];
// chores are molecule-scoped (cross-company within molecule); users filtered by companyIds in molecule
[Authorize]
public class ChoresModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IChoreService _choreService;
    private readonly IChoreTypeService _choreTypeService;
    private readonly IGrantService _grantService;
    private readonly ICompanyContext _companyContext;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly ILogger<ChoresModel> _logger;

    public ChoresModel(
        AppDbContext db,
        IChoreService choreService,
        IChoreTypeService choreTypeService,
        IGrantService grantService,
        ICompanyContext companyContext,
        IStringLocalizer<SharedResources> localizer,
        ILogger<ChoresModel> logger)
    {
        _db = db;
        _choreService = choreService;
        _choreTypeService = choreTypeService;
        _grantService = grantService;
        _companyContext = companyContext;
        _localizer = localizer;
        _logger = logger;
    }

    // Query parameters
    [BindProperty(SupportsGet = true)]
    public int? MoleculeId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Start { get; set; }

    [BindProperty(SupportsGet = true)]
    public string ViewMode { get; set; } = "week"; // week, 2weeks, month

    [BindProperty(SupportsGet = true)]
    public int? ChoreTypeFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool JustMine { get; set; }

    // Page properties
    public ExcelCalendarTableViewModel CalendarData { get; set; } = new();
    public bool CanEdit { get; set; }
    public List<Molecule> AvailableMolecules { get; set; } = new();
    public Molecule? SelectedMolecule { get; set; }
    public List<ChoreType> ChoreTypes { get; set; } = new();
    public int CurrentUserId { get; set; }
    public List<AppUser> Users { get; set; } = new();

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

        // Set default MoleculeId if not provided
        if (!MoleculeId.HasValue)
        {
            MoleculeId = userCompany.MoleculeId;
        }

        // Load available molecules (user has access to via grants or their own)
        await LoadAvailableMoleculesAsync(currentUserId, userCompany.MoleculeId.Value);

        // Validate selected molecule is accessible
        if (!AvailableMolecules.Any(m => m.Id == MoleculeId))
        {
            MoleculeId = userCompany.MoleculeId;
        }

        SelectedMolecule = AvailableMolecules.FirstOrDefault(m => m.Id == MoleculeId);

        // Load chore types for the selected molecule
        if (MoleculeId.HasValue)
        {
            ChoreTypes = await _choreTypeService.GetChoreTypesForMoleculeAsync(MoleculeId.Value);
        }

        // Calculate date range
        CalculateDateRange();

        // Check edit permission
        CanEdit = await _grantService.HasGrantAsync(currentUserId, "AssignChores");

        // Build calendar data
        if (MoleculeId.HasValue)
        {
            await BuildUserBasedCalendarAsync(MoleculeId.Value);
        }

        _logger.LogInformation(
            "Chores calendar loaded for User {UserId}, Molecule {MoleculeId}, ViewMode {ViewMode}",
            currentUserId, MoleculeId, ViewMode);

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
        // Use grant-based scope resolution that handles all scope levels
        // (Project → Area → Molecule → Company → Self)
        var moleculeIds = new HashSet<int>(
            await _grantService.GetAccessibleMoleculeIdsForGrantAsync(userId, "ViewChores"));

        // Also include molecules from AssignChores grant
        foreach (var id in await _grantService.GetAccessibleMoleculeIdsForGrantAsync(userId, "AssignChores"))
            moleculeIds.Add(id);

        // Always include user's own molecule as fallback
        moleculeIds.Add(userMoleculeId);

        AvailableMolecules = await _db.Molecules
            .Where(m => moleculeIds.Contains(m.Id) && m.IsActive)
            .OrderBy(m => m.DisplayName)
            .ToListAsync();
    }

    private async Task BuildUserBasedCalendarAsync(int moleculeId)
    {
        // Get all users in companies belonging to this molecule
        var users = await GetUsersForMoleculeAsync(moleculeId);

        // Expose full user list for bottom-sheet dropdown (before JustMine filter)
        Users = users;

        // Filter to just current user if "Just Mine" is enabled
        if (JustMine)
        {
            users = users.Where(u => u.Id == CurrentUserId).ToList();
        }

        // Get chores for the date range and molecule
        var chores = await _choreService.GetChoresAsync(
            startDate: StartDate,
            endDate: EndDate,
            moleculeId: moleculeId,
            includeCancel: false);

        // Apply chore type filter if set
        if (ChoreTypeFilter.HasValue)
        {
            chores = chores.Where(c => c.ChoreTypeId == ChoreTypeFilter.Value).ToList();
        }

        // Build groups by chore type
        var groups = ChoreTypes.Select(ct => new ExcelCalendarGroup
        {
            Id = $"choretype-{ct.Id}",
            Name = ct.DisplayName,
            SortOrder = ct.SortOrder
        }).ToList();

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
            row.Cells = BuildCellsForUser(user.Id, chores);
            rows.Add(row);
        }

        CalendarData = new ExcelCalendarTableViewModel
        {
            StartDate = StartDate,
            EndDate = EndDate,
            ViewMode = ViewMode,
            IsReadOnly = !CanEdit,
            CalendarType = "chores",
            Rows = rows,
            Groups = groups.Any() ? groups : null
        };
    }

    private async Task<List<AppUser>> GetUsersForMoleculeAsync(int moleculeId)
    {
        // Get all companies in this molecule
        var companyIds = await _db.Companies
            .Where(c => c.MoleculeId == moleculeId)
            .Select(c => c.Id)
            .ToListAsync();

        // Get active users in those companies
        return await _db.Users
            .IgnoreQueryFilters()
            .Where(u => companyIds.Contains(u.CompanyId) && u.IsActive)
            .OrderBy(u => u.DisplayName)
            .Select(u => new AppUser
            {
                Id = u.Id,
                DisplayName = u.DisplayName,
                CompanyId = u.CompanyId
            })
            .ToListAsync();
    }

    private Dictionary<DateOnly, ExcelCalendarCell> BuildCellsForUser(
        int userId,
        List<Chore> chores)
    {
        var cells = new Dictionary<DateOnly, ExcelCalendarCell>();

        for (var date = StartDate; date <= EndDate; date = date.AddDays(1))
        {
            var cell = new ExcelCalendarCell();

            // Get user's chores for this date
            var userChores = chores
                .Where(c => c.UserId == userId && c.Date == date)
                .ToList();

            cell.Assignments = userChores.Select(c => new ExcelCalendarAssignment
            {
                Id = c.Id,
                Name = c.ChoreType?.DisplayName ?? c.Title,
                Role = c.ChoreType?.Color, // Use color as role for styling
                UserId = c.UserId
            }).ToList();

            cells[date] = cell;
        }

        return cells;
    }
}
