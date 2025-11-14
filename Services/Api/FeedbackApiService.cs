using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Api.Dto;

namespace ShiftManager.Services.Api;

/// <summary>
/// API service for Feedback operations.
/// Handles all feedback CRUD operations for the API layer.
/// </summary>
public class FeedbackApiService
{
    private readonly AppDbContext _context;
    private readonly ILogger<FeedbackApiService> _logger;

    public FeedbackApiService(AppDbContext context, ILogger<FeedbackApiService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Lists feedback with pagination and filtering.
    /// </summary>
    public async Task<(List<FeedbackDto> Feedbacks, int TotalCount)> ListFeedbackAsync(
        int companyId,
        int page = 1,
        int pageSize = 50,
        int? submittedBy = null,
        string? type = null,
        string? status = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        bool includeRelated = false)
    {
        // Validate pagination
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 100) pageSize = 100;

        var query = _context.Feedbacks.AsQueryable();

        // Filter by company
        query = query.Where(f => f.CompanyId == companyId);

        // Filter by submitter
        if (submittedBy.HasValue)
        {
            query = query.Where(f => f.SubmittedBy == submittedBy.Value);
        }

        // Filter by type
        if (!string.IsNullOrEmpty(type) && Enum.TryParse<FeedbackType>(type, true, out var typeEnum))
        {
            query = query.Where(f => f.Type == typeEnum);
        }

        // Filter by status
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<FeedbackStatus>(status, true, out var statusEnum))
        {
            query = query.Where(f => f.Status == statusEnum);
        }

        // Filter by date range
        if (startDate.HasValue)
        {
            query = query.Where(f => f.CreatedAt >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(f => f.CreatedAt <= endDate.Value);
        }

        // Get total count
        var totalCount = await query.CountAsync();

        // Apply pagination
        var feedbacks = await query
            .OrderByDescending(f => f.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Map to DTOs
        var dtos = new List<FeedbackDto>();
        foreach (var feedback in feedbacks)
        {
            var dto = await MapToDto(feedback, includeRelated);
            dtos.Add(dto);
        }

        return (dtos, totalCount);
    }

    /// <summary>
    /// Gets a single feedback by ID.
    /// </summary>
    public async Task<FeedbackDto?> GetFeedbackAsync(int companyId, int feedbackId, bool includeRelated = true)
    {
        var feedback = await _context.Feedbacks
            .Where(f => f.CompanyId == companyId && f.Id == feedbackId)
            .FirstOrDefaultAsync();

        if (feedback == null)
        {
            return null;
        }

        return await MapToDto(feedback, includeRelated);
    }

    /// <summary>
    /// Creates new feedback.
    /// </summary>
    public async Task<(FeedbackDto? Feedback, string? Error)> CreateFeedbackAsync(
        int companyId,
        int submitterId,
        CreateFeedbackDto dto)
    {
        // Parse type
        if (!Enum.TryParse<FeedbackType>(dto.Type, true, out var type))
        {
            return (null, "Invalid type. Must be 'Error' or 'Suggestion'");
        }

        // Validate content
        if (string.IsNullOrWhiteSpace(dto.Content))
        {
            return (null, "Content is required");
        }

        // Create feedback
        var feedback = new Feedback
        {
            CompanyId = companyId,
            SubmittedBy = submitterId,
            Type = type,
            Content = dto.Content.Trim(),
            ImageFileName = string.IsNullOrWhiteSpace(dto.ImageFileName) ? null : dto.ImageFileName.Trim(),
            Status = FeedbackStatus.New,
            CreatedAt = DateTime.UtcNow
        };

        _context.Feedbacks.Add(feedback);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Feedback created: Id={Id}, CompanyId={CompanyId}, SubmittedBy={SubmittedBy}, Type={Type}",
            feedback.Id, companyId, submitterId, type);

        var result = await GetFeedbackAsync(companyId, feedback.Id);
        return (result, null);
    }

    /// <summary>
    /// Updates feedback status.
    /// </summary>
    public async Task<(FeedbackDto? Feedback, string? Error)> UpdateFeedbackStatusAsync(
        int companyId,
        int feedbackId,
        int updaterId,
        UpdateFeedbackStatusDto dto)
    {
        var feedback = await _context.Feedbacks
            .Where(f => f.CompanyId == companyId && f.Id == feedbackId)
            .FirstOrDefaultAsync();

        if (feedback == null)
        {
            return (null, "Feedback not found");
        }

        // Parse status
        if (!Enum.TryParse<FeedbackStatus>(dto.Status, true, out var status))
        {
            return (null, "Invalid status. Must be 'New' or 'ToWorkOn'");
        }

        // Update status
        feedback.Status = status;
        feedback.StatusUpdatedAt = DateTime.UtcNow;
        feedback.StatusUpdatedBy = updaterId;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Feedback status updated: Id={Id}, NewStatus={Status}, UpdatedBy={UpdatedBy}",
            feedbackId, status, updaterId);

        var result = await GetFeedbackAsync(companyId, feedbackId);
        return (result, null);
    }

    /// <summary>
    /// Deletes feedback.
    /// </summary>
    public async Task<bool> DeleteFeedbackAsync(int companyId, int feedbackId)
    {
        var feedback = await _context.Feedbacks
            .Where(f => f.CompanyId == companyId && f.Id == feedbackId)
            .FirstOrDefaultAsync();

        if (feedback == null)
        {
            return false;
        }

        _context.Feedbacks.Remove(feedback);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Feedback deleted: Id={Id}, CompanyId={CompanyId}", feedbackId, companyId);

        return true;
    }

    /// <summary>
    /// Maps Feedback entity to DTO with optional related data.
    /// </summary>
    private async Task<FeedbackDto> MapToDto(Feedback feedback, bool includeRelated)
    {
        var dto = new FeedbackDto
        {
            Id = feedback.Id,
            SubmittedBy = feedback.SubmittedBy,
            Type = feedback.Type.ToString(),
            Status = feedback.Status.ToString(),
            Content = feedback.Content,
            ImageFileName = feedback.ImageFileName,
            CreatedAt = feedback.CreatedAt,
            StatusUpdatedAt = feedback.StatusUpdatedAt,
            StatusUpdatedBy = feedback.StatusUpdatedBy
        };

        if (includeRelated)
        {
            // Load related entities
            await _context.Entry(feedback).Reference(f => f.Submitter).LoadAsync();

            if (feedback.StatusUpdatedBy.HasValue)
            {
                await _context.Entry(feedback).Reference(f => f.StatusUpdater).LoadAsync();
            }

            // Map users
            dto.Submitter = feedback.Submitter != null ? MapUserDto(feedback.Submitter) : null;
            dto.StatusUpdater = feedback.StatusUpdater != null ? MapUserDto(feedback.StatusUpdater) : null;
        }

        return dto;
    }

    private UserDto MapUserDto(AppUser user)
    {
        return new UserDto
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Role = user.Role.ToString(),
            IsActive = user.IsActive
        };
    }
}
