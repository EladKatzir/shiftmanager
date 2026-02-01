using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using System.Security.Claims;
using IO = System.IO;

namespace ShiftManager.Pages.Public;

[Authorize]
public class FeedbackModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ITenantResolver _tenantResolver;
    private readonly INotificationService _notificationService;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<FeedbackModel> _logger;

    public FeedbackModel(
        AppDbContext db,
        ITenantResolver tenantResolver,
        INotificationService notificationService,
        IWebHostEnvironment env,
        ILogger<FeedbackModel> logger)
    {
        _db = db;
        _tenantResolver = tenantResolver;
        _notificationService = notificationService;
        _env = env;
        _logger = logger;
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

    public async Task OnGetAsync()
    {
        IsOwner = User.IsInRole("Owner");

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
            ErrorMessage = "Feedback content is required.";
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

            // Send notification to owner
            await NotifyOwnerAsync(feedback);

            Message = "Feedback submitted successfully. Thank you!";

            // Clear form
            FeedbackContent = string.Empty;
            Picture = null;

            await OnGetAsync();
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error submitting feedback");
            ErrorMessage = "Error submitting feedback. Please try again.";
            await OnGetAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostMarkToWorkOnAsync(int feedbackId)
    {
        if (!User.IsInRole("Owner"))
        {
            return Forbid();
        }

        try
        {
            var feedback = await _db.Feedbacks.FindAsync(feedbackId);
            if (feedback == null)
            {
                ErrorMessage = "Feedback not found.";
                await OnGetAsync();
                return Page();
            }

            feedback.Status = FeedbackStatus.ToWorkOn;
            feedback.StatusUpdatedAt = DateTime.UtcNow;
            feedback.StatusUpdatedBy = GetCurrentUserId();

            await _db.SaveChangesAsync();

            Message = "Feedback marked as 'To Work On'.";
            await OnGetAsync();
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking feedback as to work on");
            ErrorMessage = "Error updating feedback status.";
            await OnGetAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync(int feedbackId)
    {
        if (!User.IsInRole("Owner"))
        {
            return Forbid();
        }

        try
        {
            var feedback = await _db.Feedbacks.FindAsync(feedbackId);
            if (feedback == null)
            {
                ErrorMessage = "Feedback not found.";
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

            Message = "Feedback deleted successfully.";
            await OnGetAsync();
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting feedback");
            ErrorMessage = "Error deleting feedback.";
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
                return (false, null, "Image size exceeds 5 MB limit.");
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (extension != ".jpg" && extension != ".jpeg" && extension != ".png")
            {
                return (false, null, "Only JPEG and PNG files are allowed.");
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
            return (false, null, "Error saving image. Please try again.");
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
            var typeLabel = feedback.Type == FeedbackType.Error ? "Error" : "Suggestion";
            var snippet = feedback.Content.Length > 100
                ? feedback.Content.Substring(0, 100) + "..."
                : feedback.Content;

            var title = "New Feedback Submitted";
            var message = $"{typeLabel} from {submitter?.DisplayName ?? "Unknown"}: {snippet}";

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
