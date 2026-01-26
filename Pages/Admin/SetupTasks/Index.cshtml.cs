using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Pages.Admin.SetupTasks;

[Authorize(Policy = "IsAdmin")]
public class IndexModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly ISetupTaskService _setupTaskService;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext db,
        ISetupTaskService setupTaskService,
        ILogger<IndexModel> logger) : base(localizer)
    {
        _db = db;
        _setupTaskService = setupTaskService;
        _logger = logger;
    }

    public List<SetupTaskDto> PendingTasks { get; set; } = new();
    public List<SetupTaskDto> AllTasks { get; set; } = new();
    public List<SetupProgressDto> MoleculeProgress { get; set; } = new();
    public List<MoleculeOption> AvailableMolecules { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int? FilterMoleculeId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string ViewMode { get; set; } = "pending"; // "pending", "all", "progress"

    public int CurrentUserId { get; private set; }

    public record MoleculeOption(int Id, string Name);

    public async Task OnGetAsync()
    {
        if (TempData["SuccessMessage"] is string successMsg) Success = successMsg;
        if (TempData["ErrorMessage"] is string errorMsg) Error = errorMsg;

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            Error = _localizer["Error_NotAuthenticated"];
            return;
        }
        CurrentUserId = userId;

        // Load available molecules for filter
        AvailableMolecules = await _db.Molecules.IgnoreQueryFilters()
            .Where(m => m.IsActive)
            .OrderBy(m => m.Area.Name).ThenBy(m => m.Name)
            .Select(m => new MoleculeOption(m.Id, $"{m.Area.DisplayName} / {m.DisplayName}"))
            .ToListAsync();

        if (ViewMode == "progress")
        {
            // Show progress by molecule
            var moleculeIds = await _db.SetupTasks.IgnoreQueryFilters()
                .Select(t => t.MoleculeId)
                .Where(id => id.HasValue)
                .Distinct()
                .ToListAsync();

            foreach (var moleculeId in moleculeIds.Where(id => id.HasValue))
            {
                var progress = await _setupTaskService.GetProgressAsync(moleculeId!.Value);
                MoleculeProgress.Add(progress);
            }

            MoleculeProgress = MoleculeProgress.OrderByDescending(p => p.PendingTasks).ToList();
        }
        else if (ViewMode == "all" && FilterMoleculeId.HasValue)
        {
            // Show all tasks for a specific molecule
            AllTasks = await _setupTaskService.GetTasksForMoleculeAsync(FilterMoleculeId.Value);
        }
        else
        {
            // Show pending tasks for current user (default)
            PendingTasks = await _setupTaskService.GetPendingTasksAsync(userId);
        }
    }

    public async Task<IActionResult> OnPostCompleteAsync(int id)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            TempData["ErrorMessage"] = _localizer["Error_NotAuthenticated"];
            return RedirectToPage();
        }

        var result = await _setupTaskService.CompleteTaskAsync(id, userId);

        if (result)
        {
            TempData["SuccessMessage"] = _localizer["Success_TaskCompleted"];
        }
        else
        {
            TempData["ErrorMessage"] = _localizer["Error_TaskNotFound"];
        }

        return RedirectToPage(new { ViewMode, FilterMoleculeId });
    }

    public async Task<IActionResult> OnPostSkipAsync(int id)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            TempData["ErrorMessage"] = _localizer["Error_NotAuthenticated"];
            return RedirectToPage();
        }

        var result = await _setupTaskService.SkipTaskAsync(id, userId);

        if (result)
        {
            TempData["SuccessMessage"] = _localizer["Success_TaskSkipped"];
        }
        else
        {
            TempData["ErrorMessage"] = _localizer["Error_TaskNotFound"];
        }

        return RedirectToPage(new { ViewMode, FilterMoleculeId });
    }

    public async Task<IActionResult> OnPostStartAsync(int id)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            TempData["ErrorMessage"] = _localizer["Error_NotAuthenticated"];
            return RedirectToPage();
        }

        var result = await _setupTaskService.UpdateTaskStatusAsync(id, SetupTaskStatus.InProgress, userId);

        if (result)
        {
            TempData["SuccessMessage"] = _localizer["Success_TaskStarted"];
        }
        else
        {
            TempData["ErrorMessage"] = _localizer["Error_TaskNotFound"];
        }

        return RedirectToPage(new { ViewMode, FilterMoleculeId });
    }
}
