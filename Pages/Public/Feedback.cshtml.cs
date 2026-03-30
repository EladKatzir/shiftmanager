using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Pages;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;
using IO = System.IO;

namespace ShiftManager.Pages.Public;

[Authorize]
public class FeedbackModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly ITenantResolver _tenantResolver;
    private readonly INotificationService _notificationService;
    private readonly IGrantService _grantService;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<FeedbackModel> _logger;
    private readonly IAuditLogService _auditLogService;

    public FeedbackModel(
        AppDbContext db,
        ITenantResolver tenantResolver,
        INotificationService notificationService,
        IGrantService grantService,
        IWebHostEnvironment env,
        ILogger<FeedbackModel> logger,
        IAuditLogService auditLogService,
        IStringLocalizer<SharedResources> localizer) : base(localizer)
    {
        _db = db;
        _tenantResolver = tenantResolver;
        _notificationService = notificationService;
        _grantService = grantService;
        _env = env;
        _logger = logger;
        _auditLogService = auditLogService;
    }

    [BindProperty]
    public FeedbackType SelectedType { get; set; }

    [BindProperty]
    public string FeedbackContent { get; set; } = string.Empty;

    [BindProperty]
    public IFormFile? Picture { get; set; }

    public bool IsOwner { get; set; }
    public List<Feedback> FeedbackList { get; set; } = new();
    public string? Message { get; set; }
    public string? ErrorMessage { get; set; }

    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out var userId) ? userId : 0;
    }

    private async Task<bool> CheckIsOwnerAsync(int userId)
    {
        return await _grantService.HasGrantAsync(userId, "AdminAccess");
    }

    public async Task OnGetAsync()
    {
        var userId = GetCurrentUserId();
        IsOwner = await CheckIsOwnerAsync(userId);

        if (IsOwner)
        {
            // Owner sees the feedback list
            FeedbackList = await _db.Feedbacks
                .Include(f => f.Submitter)
                .OrderByDescending(f => f.Status == FeedbackStatus.New)
                .ThenByDescending(f => f.CreatedAt)
                .ToListAsync();
        }
    }

    public async Task<IActionResult> OnPostSubmitAsync()
    {
        // Validation
        if (string.IsNullOrWhiteSpace(FeedbackContent))
        {
            ErrorMessage = _localizer["Feedback_Error_ContentRequired"];
            await OnGetAsync();
            return Page();
        }

        try
        {
            var feedback = new Feedback
            {
                CompanyId = _tenantResolver.GetCurrentTenantId(),
                SubmittedBy = GetCurrentUserId(),
                Type = SelectedType,
                Content = FeedbackContent.Trim(),
                Status = FeedbackStatus.New,
                CreatedAt = DateTime.UtcNow
            };

            // Handle image upload if provided
            if (Picture != null && Picture.Length > 0)
            {
                var (success, fileName, error) = await SaveImageAsync(Picture);
                if (success)
                {
                    feedback.ImageFileName = fileName;
                }
                else
                {
                    ErrorMessage = error;
                    await OnGetAsync();
                    return Page();
                }
            }

            _db.Feedbacks.Add(feedback);
            await _db.SaveChangesAsync();

            await _auditLogService.LogAsync("FeedbackCreated", "Feedback", feedback.Id,
                $"Submitted {feedback.Type} feedback",
                feedback.Content.Length > 200 ? feedback.Content.Substring(0, 200) + "..." : feedback.Content);

            // Send notification to owner
            await NotifyOwnerAsync(feedback);

            Message = _localizer["Feedback_Success_Submitted"];

            // Clear form
            FeedbackContent = string.Empty;
            Picture = null;

            await OnGetAsync();
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error submitting feedback");
            ErrorMessage = _localizer["Feedback_Error_SubmitFailed"];
            await OnGetAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostMarkToWorkOnAsync(int feedbackId)
    {
        var userId = GetCurrentUserId();
        if (!await CheckIsOwnerAsync(userId))
        {
            return Forbid();
        }

        try
        {
            var feedback = await _db.Feedbacks.FindAsync(feedbackId);
            if (feedback == null)
            {
                ErrorMessage = _localizer["Feedback_Error_NotFound"];
                await OnGetAsync();
                return Page();
            }

            feedback.Status = FeedbackStatus.ToWorkOn;
            feedback.StatusUpdatedAt = DateTime.UtcNow;
            feedback.StatusUpdatedBy = userId;

            await _db.SaveChangesAsync();

            await _auditLogService.LogAsync("FeedbackStatusUpdated", "Feedback", feedbackId,
                $"Marked feedback #{feedbackId} as 'To Work On'");

            Message = _localizer["Feedback_Success_MarkedToWorkOn"];
            await OnGetAsync();
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking feedback as to work on");
            ErrorMessage = _localizer["Feedback_Error_StatusUpdateFailed"];
            await OnGetAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync(int feedbackId)
    {
        var userId = GetCurrentUserId();
        if (!await CheckIsOwnerAsync(userId))
        {
            return Forbid();
        }

        try
        {
            var feedback = await _db.Feedbacks.FindAsync(feedbackId);
            if (feedback == null)
            {
                ErrorMessage = _localizer["Feedback_Error_NotFound"];
                await OnGetAsync();
                return Page();
            }

            // Delete associated image if exists
            if (!string.IsNullOrWhiteSpace(feedback.ImageFileName))
            {
                await DeleteImageAsync(feedback.ImageFileName);
            }

            _db.Feedbacks.Remove(feedback);
            await _db.SaveChangesAsync();

            await _auditLogService.LogAsync("FeedbackDeleted", "Feedback", feedbackId,
                $"Deleted {feedback.Type} feedback #{feedbackId}");

            Message = _localizer["Feedback_Success_Deleted"];
            await OnGetAsync();
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting feedback");
            ErrorMessage = _localizer["Feedback_Error_DeleteFailed"];
            await OnGetAsync();
            return Page();
        }
    }

    private async Task<(bool Success, string? FileName, string? Error)> SaveImageAsync(IFormFile file)
    {
        try
        {
            // Validate file
            const long maxFileSize = 5 * 1024 * 1024; // 5 MB
            if (file.Length > maxFileSize)
            {
                return (false, null, _localizer["Feedback_Error_ImageTooLarge"]);
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (extension != ".jpg" && extension != ".jpeg" && extension != ".png")
            {
                return (false, null, _localizer["Feedback_Error_InvalidImageFormat"]);
            }

            // Create feedback directory
            var companyId = _tenantResolver.GetCurrentTenantId();
            var feedbackDir = Path.Combine(_env.WebRootPath, "feedback", companyId.ToString());
            if (!Directory.Exists(feedbackDir))
            {
                Directory.CreateDirectory(feedbackDir);
            }

            // Generate unique filename
            var fileName = $"{Guid.NewGuid()}{extension}";
            var filePath = Path.Combine(feedbackDir, fileName);

            // Save file
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            return (true, fileName, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving feedback image");
            return (false, null, _localizer["Feedback_Error_ImageSaveFailed"]);
        }
    }

    private async Task DeleteImageAsync(string fileName)
    {
        try
        {
            var companyId = _tenantResolver.GetCurrentTenantId();
            var filePath = Path.Combine(_env.WebRootPath, "feedback", companyId.ToString(), fileName);
            if (IO.File.Exists(filePath))
            {
                IO.File.Delete(filePath);
            }
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error deleting feedback image: {FileName}", fileName);
            // Don't throw - file deletion failure shouldn't break the operation
        }
    }

    private async Task NotifyOwnerAsync(Feedback feedback)
    {
        try
        {
            // Find the owner
            var companyId = _tenantResolver.GetCurrentTenantId();
            var owner = await _db.Users
                .Where(u => u.CompanyId == companyId && u.Role == UserRole.Owner)
                .FirstOrDefaultAsync();

            if (owner == null)
            {
                _logger.LogWarning("No owner found for company {CompanyId}", companyId);
                return;
            }

            var submitter = await _db.Users.FindAsync(feedback.SubmittedBy);
            var typeLabel = feedback.Type == FeedbackType.Error
                ? _localizer["Feedback_TypeLabel_Error"]
                : _localizer["Feedback_TypeLabel_Suggestion"];
            var snippet = feedback.Content.Length > 100
                ? feedback.Content.Substring(0, 100) + "..."
                : feedback.Content;

            var title = _localizer["Feedback_Notification_Title"].Value;
            var message = _localizer["Feedback_Notification_Message", typeLabel, submitter?.DisplayName ?? _localizer["Common_Unknown"], snippet].Value;

            await _notificationService.CreateNotificationAsync(
                owner.Id,
                NotificationType.FeedbackSubmitted,
                title,
                message,
                feedback.Id,
                "Feedback");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending notification to owner");
            // Don't throw - notification failure shouldn't prevent feedback submission
        }
    }

    public string GetImageUrl(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        var companyId = _tenantResolver.GetCurrentTenantId();
        return $"/feedback/{companyId}/{fileName}";
    }
}
