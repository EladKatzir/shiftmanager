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

namespace ShiftManager.Pages.Admin.DutyRotation;

[Authorize(Policy = "Grant:ManageOnDuty")]
public class IndexModel : LocalizedPageModel
{
    private readonly IDutyRotationService _dutyRotationService;
    private readonly AppDbContext _db;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        IStringLocalizer<SharedResources> localizer,
        IDutyRotationService dutyRotationService,
        AppDbContext db,
        ILogger<IndexModel> logger) : base(localizer)
    {
        _dutyRotationService = dutyRotationService;
        _db = db;
        _logger = logger;
    }

    // View Models
    public record RotationVM(
        int Id,
        string Name,
        OnDutyType DutyType,
        RotationFrequency Frequency,
        bool IsActive,
        bool IncludeWeekends,
        int MaxConsecutiveDays,
        int QueueSize,
        DateOnly? LastAssignedDate,
        string? CreatorName);

    public record QueueEntryVM(int UserId, string DisplayName, int Position);
    public record UserOption(int Id, string DisplayName);

    // Data
    public List<RotationVM> Rotations { get; set; } = new();
    public Models.DutyRotation? SelectedRotation { get; set; }
    public List<QueueEntryVM> QueueEntries { get; set; } = new();
    public List<UserOption> AvailableUsers { get; set; } = new();
    public int? SelectedRotationId { get; set; }

    // Create form bindings
    [BindProperty] public string RotationName { get; set; } = string.Empty;
    [BindProperty] public OnDutyType SelectedDutyType { get; set; }
    [BindProperty] public RotationFrequency SelectedFrequency { get; set; }
    [BindProperty] public bool FormIncludeWeekends { get; set; }
    [BindProperty] public int FormMaxConsecutive { get; set; } = 1;

    // Update form bindings
    [BindProperty] public int EditRotationId { get; set; }
    [BindProperty] public string EditName { get; set; } = string.Empty;
    [BindProperty] public RotationFrequency EditFrequency { get; set; }
    [BindProperty] public bool EditIncludeWeekends { get; set; }
    [BindProperty] public int EditMaxConsecutive { get; set; } = 1;
    [BindProperty] public bool EditIsActive { get; set; }

    // Queue management bindings
    [BindProperty] public int QueueRotationId { get; set; }
    [BindProperty] public int QueueUserId { get; set; }
    [BindProperty] public string? ReorderUserIds { get; set; }

    public async Task OnGetAsync(int? rotationId)
    {
        if (TempData["SuccessMessage"] is string successMsg)
            Success = successMsg;
        if (TempData["ErrorMessage"] is string errorMsg)
            Error = errorMsg;

        await LoadDataAsync(rotationId);
    }

    private async Task LoadDataAsync(int? rotationId)
    {
        var allRotations = await _dutyRotationService.GetAllRotationsAsync(includeInactive: true);

        Rotations = allRotations.Select(r => new RotationVM(
            r.Id,
            r.Name,
            r.DutyType,
            r.Frequency,
            r.IsActive,
            r.IncludeWeekends,
            r.MaxConsecutiveDays,
            r.Entries.Count(e => e.IsActive),
            r.LastAssignedDate,
            r.Creator?.DisplayName
        )).ToList();

        // Load queue for selected rotation
        if (rotationId.HasValue)
        {
            SelectedRotationId = rotationId.Value;
            SelectedRotation = allRotations.FirstOrDefault(r => r.Id == rotationId.Value);

            if (SelectedRotation != null)
            {
                var queue = await _dutyRotationService.GetQueueAsync(rotationId.Value);
                QueueEntries = queue.Select(e => new QueueEntryVM(
                    e.UserId,
                    e.User?.DisplayName ?? $"User #{e.UserId}",
                    e.Position
                )).ToList();
            }
        }

        // Load available users for the company (tenant-filtered via EF query filters)
        AvailableUsers = await _db.Users
            .Where(u => u.IsActive)
            .OrderBy(u => u.DisplayName)
            .Select(u => new UserOption(u.Id, u.DisplayName))
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (string.IsNullOrWhiteSpace(RotationName))
        {
            TempData["ErrorMessage"] = _localizer["Error_RotationNameRequired"].Value;
            return RedirectToPage();
        }

        if (FormMaxConsecutive < 1) FormMaxConsecutive = 1;

        var currentUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

        try
        {
            var rotation = await _dutyRotationService.CreateRotationAsync(
                RotationName.Trim(),
                SelectedDutyType,
                SelectedFrequency,
                FormIncludeWeekends,
                FormMaxConsecutive,
                currentUserId);

            _logger.LogInformation("DutyRotation {RotationId} created by user {UserId}", rotation.Id, currentUserId);

            TempData["SuccessMessage"] = string.Format(_localizer["Success_RotationCreated"].Value, rotation.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating DutyRotation");
            TempData["ErrorMessage"] = _localizer["Error_CreatingRotation"].Value;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateAsync()
    {
        if (EditRotationId <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_RotationNotFound"].Value;
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(EditName))
        {
            TempData["ErrorMessage"] = _localizer["Error_RotationNameRequired"].Value;
            return RedirectToPage(new { rotationId = EditRotationId });
        }

        if (EditMaxConsecutive < 1) EditMaxConsecutive = 1;

        var success = await _dutyRotationService.UpdateRotationAsync(
            EditRotationId,
            EditName.Trim(),
            EditFrequency,
            EditIncludeWeekends,
            EditMaxConsecutive,
            EditIsActive);

        if (success)
        {
            TempData["SuccessMessage"] = _localizer["Success_RotationUpdated"].Value;
        }
        else
        {
            TempData["ErrorMessage"] = _localizer["Error_RotationNotFound"].Value;
        }

        return RedirectToPage(new { rotationId = EditRotationId });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var success = await _dutyRotationService.DeleteRotationAsync(id);

        if (success)
        {
            TempData["SuccessMessage"] = _localizer["Success_RotationDeleted"].Value;
        }
        else
        {
            TempData["ErrorMessage"] = _localizer["Error_RotationNotFound"].Value;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddUserAsync()
    {
        if (QueueRotationId <= 0 || QueueUserId <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidSelection"].Value;
            return RedirectToPage(new { rotationId = QueueRotationId });
        }

        var success = await _dutyRotationService.AddUserToQueueAsync(QueueRotationId, QueueUserId);

        if (success)
        {
            TempData["SuccessMessage"] = _localizer["Success_UserAddedToQueue"].Value;
        }
        else
        {
            TempData["ErrorMessage"] = _localizer["Error_UserAlreadyInQueue"].Value;
        }

        return RedirectToPage(new { rotationId = QueueRotationId });
    }

    public async Task<IActionResult> OnPostRemoveUserAsync()
    {
        if (QueueRotationId <= 0 || QueueUserId <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidSelection"].Value;
            return RedirectToPage(new { rotationId = QueueRotationId });
        }

        var success = await _dutyRotationService.RemoveUserFromQueueAsync(QueueRotationId, QueueUserId);

        if (success)
        {
            TempData["SuccessMessage"] = _localizer["Success_UserRemovedFromQueue"].Value;
        }
        else
        {
            TempData["ErrorMessage"] = _localizer["Error_UserNotInQueue"].Value;
        }

        return RedirectToPage(new { rotationId = QueueRotationId });
    }

    public async Task<IActionResult> OnPostReorderQueueAsync()
    {
        if (QueueRotationId <= 0 || string.IsNullOrWhiteSpace(ReorderUserIds))
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidSelection"].Value;
            return RedirectToPage(new { rotationId = QueueRotationId });
        }

        var userIds = new List<int>();
        foreach (var idStr in ReorderUserIds.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(idStr.Trim(), out var id))
            {
                userIds.Add(id);
            }
        }

        if (!userIds.Any())
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidSelection"].Value;
            return RedirectToPage(new { rotationId = QueueRotationId });
        }

        var success = await _dutyRotationService.ReorderQueueAsync(QueueRotationId, userIds);

        if (success)
        {
            TempData["SuccessMessage"] = _localizer["Success_QueueReordered"].Value;
        }
        else
        {
            TempData["ErrorMessage"] = _localizer["Error_ReorderFailed"].Value;
        }

        return RedirectToPage(new { rotationId = QueueRotationId });
    }

    // Display helpers

    public string GetDutyTypeDisplay(OnDutyType type)
    {
        return type switch
        {
            OnDutyType.Hakam => _localizer["OnDuty_Hakam"],
            OnDutyType.Lead => _localizer["OnDuty_Lead"],
            _ => type.ToString()
        };
    }

    public string GetFrequencyDisplay(RotationFrequency freq)
    {
        return freq switch
        {
            RotationFrequency.Daily => _localizer["Frequency_Daily"],
            RotationFrequency.Weekly => _localizer["Frequency_Weekly"],
            RotationFrequency.Biweekly => _localizer["Frequency_Biweekly"],
            RotationFrequency.Monthly => _localizer["Frequency_Monthly"],
            _ => freq.ToString()
        };
    }
}
