using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Pages.Admin;

[Authorize(Policy = "CanManageAnnouncements")]
public class AnnouncementsModel : LocalizedPageModel
{
    private readonly IAnnouncementService _announcementService;
    private readonly ICurrentUserService _currentUserService;
    private readonly AppDbContext _db;
    private readonly ILogger<AnnouncementsModel> _logger;

    public AnnouncementsModel(
        IStringLocalizer<SharedResources> localizer,
        IAnnouncementService announcementService,
        ICurrentUserService currentUserService,
        AppDbContext db,
        ILogger<AnnouncementsModel> logger)
        : base(localizer)
    {
        _announcementService = announcementService;
        _currentUserService = currentUserService;
        _db = db;
        _logger = logger;
    }

    // View Models
    public record AnnouncementVM(
        int Id,
        string Title,
        string Content,
        string CreatorName,
        DateTime CreatedAt,
        DateTime? ExpiresAt,
        AnnouncementScope Scope,
        string? TargetDepartmentName,
        string? TargetRole,
        bool IsPinned,
        bool IsActive,
        bool IsExpired);

    public record DepartmentOption(int Id, string Name, string MoleculeName);

    // Data
    public List<AnnouncementVM> Announcements { get; set; } = new();
    public List<DepartmentOption> Departments { get; set; } = new();
    public List<SelectListItem> Roles { get; set; } = new();
    public List<SelectListItem> Scopes { get; set; } = new();

    // Form Bindings
    [BindProperty] public string Title { get; set; } = string.Empty;
    [BindProperty] public string AnnouncementContent { get; set; } = string.Empty;
    [BindProperty] public AnnouncementScope Scope { get; set; } = AnnouncementScope.All;
    [BindProperty] public int? TargetDepartmentId { get; set; }
    [BindProperty] public string? TargetRole { get; set; }
    [BindProperty] public DateTime? ExpiresAt { get; set; }
    [BindProperty] public bool IsPinned { get; set; }

    public async Task OnGetAsync()
    {
        if (TempData["SuccessMessage"] is string successMsg)
            Success = successMsg;
        if (TempData["ErrorMessage"] is string errorMsg)
            Error = errorMsg;

        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        var announcements = await _announcementService.GetAllAnnouncementsAsync(includeExpired: true);
        var now = DateTime.UtcNow;

        Announcements = announcements.Select(a => new AnnouncementVM(
            a.Id,
            a.Title,
            a.Content,
            a.Creator?.DisplayName ?? _localizer["Unknown"],
            a.CreatedAt,
            a.ExpiresAt,
            a.Scope,
            a.TargetDepartment?.DisplayName,
            a.TargetRole,
            a.IsPinned,
            a.IsActive,
            a.ExpiresAt.HasValue && a.ExpiresAt.Value < now
        )).ToList();

        // Load departments for dropdown
        Departments = await _db.Departments
            .Include(d => d.Molecule)
            .Where(d => d.IsActive)
            .Select(d => new DepartmentOption(d.Id, d.DisplayName, d.Molecule.DisplayName))
            .OrderBy(d => d.MoleculeName)
            .ThenBy(d => d.Name)
            .ToListAsync();

        // Build role dropdown from UserRole enum
        Roles = Enum.GetValues<UserRole>()
            .Select(r => new SelectListItem(_localizer[r.ToString()], r.ToString()))
            .ToList();

        // Build scope dropdown
        Scopes = Enum.GetValues<AnnouncementScope>()
            .Select(s => new SelectListItem(_localizer[$"AnnouncementScope_{s}"], ((int)s).ToString()))
            .ToList();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            TempData["ErrorMessage"] = _localizer["Error_TitleRequired"];
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(AnnouncementContent))
        {
            TempData["ErrorMessage"] = _localizer["Error_ContentRequired"];
            return RedirectToPage();
        }

        // Validate scope-specific fields
        if (Scope == AnnouncementScope.Department && !TargetDepartmentId.HasValue)
        {
            TempData["ErrorMessage"] = _localizer["Error_DepartmentRequired"];
            return RedirectToPage();
        }

        if (Scope == AnnouncementScope.Role && string.IsNullOrWhiteSpace(TargetRole))
        {
            TempData["ErrorMessage"] = _localizer["Error_RoleRequired"];
            return RedirectToPage();
        }

        var announcement = new Announcement
        {
            Title = Title.Trim(),
            Content = AnnouncementContent.Trim(),
            Scope = Scope,
            TargetDepartmentId = Scope == AnnouncementScope.Department ? TargetDepartmentId : null,
            TargetRole = Scope == AnnouncementScope.Role ? TargetRole : null,
            ExpiresAt = ExpiresAt,
            IsPinned = IsPinned
        };

        await _announcementService.CreateAsync(announcement, _currentUserService.UserId);

        _logger.LogInformation("Announcement {AnnouncementId} created by user {UserId}: {Title}",
            announcement.Id, _currentUserService.UserId, announcement.Title);

        TempData["SuccessMessage"] = _localizer["Success_AnnouncementCreated"];
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleActiveAsync(int id)
    {
        var announcement = await _announcementService.GetByIdAsync(id);
        if (announcement == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_AnnouncementNotFound"];
            return RedirectToPage();
        }

        announcement.IsActive = !announcement.IsActive;
        await _announcementService.UpdateAsync(announcement);

        _logger.LogInformation("Announcement {AnnouncementId} active status changed to {IsActive} by user {UserId}",
            id, announcement.IsActive, _currentUserService.UserId);

        TempData["SuccessMessage"] = announcement.IsActive
            ? _localizer["Success_AnnouncementActivated"]
            : _localizer["Success_AnnouncementDeactivated"];

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var announcement = await _announcementService.GetByIdAsync(id);
        if (announcement == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_AnnouncementNotFound"];
            return RedirectToPage();
        }

        await _announcementService.DeleteAsync(id);

        _logger.LogInformation("Announcement {AnnouncementId} deleted by user {UserId}: {Title}",
            id, _currentUserService.UserId, announcement.Title);

        TempData["SuccessMessage"] = _localizer["Success_AnnouncementDeleted"];
        return RedirectToPage();
    }
}
