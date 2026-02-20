using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Api.Dto;
using ShiftManager.Models.Support;

namespace ShiftManager.Services.Api;

/// <summary>
/// Wrapper service for Time-Off API operations.
/// Isolates API logic from existing time-off services to maintain zero regression.
/// </summary>
public class TimeOffApiService
{
    private readonly AppDbContext _context;
    private readonly ILogger<TimeOffApiService> _logger;
    private readonly IVacationApprovalService _vacationApprovalService;

    public TimeOffApiService(AppDbContext context, ILogger<TimeOffApiService> logger, IVacationApprovalService vacationApprovalService)
    {
        _context = context;
        _logger = logger;
        _vacationApprovalService = vacationApprovalService;
    }

    /// <summary>
    /// Lists time-off requests with pagination and filtering.
    /// Respects global query filters for multi-tenant isolation.
    /// </summary>
    public async Task<(List<TimeOffDto> Requests, int TotalCount)> ListTimeOffRequestsAsync(
        int companyId,
        int page = 1,
        int pageSize = 50,
        int? userId = null,
        string? status = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null)
    {
        // Validate pagination parameters
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 100) pageSize = 100; // Max page size

        var query = _context.TimeOffRequests.AsQueryable();

        // Manual CompanyId filter
        query = query.Where(r => r.CompanyId == companyId);

        // Apply user filter
        if (userId.HasValue)
        {
            query = query.Where(r => r.UserId == userId.Value);
        }

        // Apply status filter
        if (!string.IsNullOrEmpty(status))
        {
            if (Enum.TryParse<RequestStatus>(status, true, out var statusEnum))
            {
                query = query.Where(r => r.Status == statusEnum);
            }
        }

        // Apply date range filters
        if (startDate.HasValue)
        {
            query = query.Where(r => r.EndDate >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(r => r.StartDate <= endDate.Value);
        }

        // Get total count before pagination
        var totalCount = await query.CountAsync();

        // Apply pagination
        var requests = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Load users for requests (optimization: batch load)
        var userIds = requests.Select(r => r.UserId).Distinct().ToList();
        var users = await _context.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id);

        // Map to DTOs
        var dtos = requests.Select(r => TimeOffDto.FromEntity(r, users.GetValueOrDefault(r.UserId))).ToList();

        return (dtos, totalCount);
    }

    /// <summary>
    /// Gets a single time-off request by ID.
    /// </summary>
    public async Task<TimeOffDto?> GetTimeOffRequestAsync(int companyId, int requestId)
    {
        var request = await _context.TimeOffRequests
            .Where(r => r.CompanyId == companyId && r.Id == requestId)
            .FirstOrDefaultAsync();

        if (request == null)
        {
            return null;
        }

        // Load user
        var user = await _context.Users.FindAsync(request.UserId);

        return TimeOffDto.FromEntity(request, user);
    }

    /// <summary>
    /// Creates a new time-off request.
    /// </summary>
    public async Task<(TimeOffDto? Request, string? Error)> CreateTimeOffRequestAsync(
        int companyId,
        int userId,
        DateOnly startDate,
        DateOnly endDate,
        string? reason)
    {
        // Validate dates
        if (startDate > endDate)
        {
            return (null, "Start date must be before or equal to end date");
        }

        if (startDate < DateOnly.FromDateTime(DateTime.UtcNow))
        {
            return (null, "Start date cannot be in the past");
        }

        // Validate user exists and belongs to company
        var user = await _context.Users
            .Where(u => u.Id == userId && u.CompanyId == companyId)
            .FirstOrDefaultAsync();

        if (user == null)
        {
            return (null, $"User {userId} not found in company {companyId}");
        }

        // Check for overlapping requests
        var hasOverlap = await _context.TimeOffRequests
            .Where(r => r.UserId == userId &&
                        r.Status != RequestStatus.Declined &&
                        r.StartDate <= endDate &&
                        r.EndDate >= startDate)
            .AnyAsync();

        if (hasOverlap)
        {
            return (null, "This time-off request overlaps with an existing request");
        }

        // Create request
        var request = new TimeOffRequest
        {
            CompanyId = companyId,
            UserId = userId,
            StartDate = startDate,
            EndDate = endDate,
            Reason = reason,
            Status = RequestStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        _context.TimeOffRequests.Add(request);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Time-off request created via API: UserId={UserId}, StartDate={StartDate}, EndDate={EndDate}",
            userId, startDate, endDate);

        return (TimeOffDto.FromEntity(request, user), null);
    }

    /// <summary>
    /// Approves a time-off request.
    /// </summary>
    public async Task<(TimeOffDto? Request, string? Error)> ApproveTimeOffRequestAsync(
        int companyId,
        int requestId)
    {
        var request = await _context.TimeOffRequests
            .Where(r => r.CompanyId == companyId && r.Id == requestId)
            .FirstOrDefaultAsync();

        if (request == null)
        {
            return (null, $"Time-off request {requestId} not found");
        }

        if (request.Status != RequestStatus.Pending)
        {
            return (null, $"Cannot approve request with status {request.Status}");
        }

        request.Status = RequestStatus.Approved;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Time-off request approved via API: RequestId={RequestId}", requestId);

        // Process approval side effects (shift removal, trainee cancel, notification)
        try
        {
            await _vacationApprovalService.ProcessApprovalSideEffectsAsync(requestId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Side effects failed for approved time-off request {RequestId}. Manual remediation may be needed.", requestId);
        }

        var user = await _context.Users.FindAsync(request.UserId);
        return (TimeOffDto.FromEntity(request, user), null);
    }

    /// <summary>
    /// Declines a time-off request.
    /// </summary>
    public async Task<(TimeOffDto? Request, string? Error)> DeclineTimeOffRequestAsync(
        int companyId,
        int requestId)
    {
        var request = await _context.TimeOffRequests
            .Where(r => r.CompanyId == companyId && r.Id == requestId)
            .FirstOrDefaultAsync();

        if (request == null)
        {
            return (null, $"Time-off request {requestId} not found");
        }

        if (request.Status != RequestStatus.Pending)
        {
            return (null, $"Cannot decline request with status {request.Status}");
        }

        request.Status = RequestStatus.Declined;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Time-off request declined via API: RequestId={RequestId}", requestId);

        var user = await _context.Users.FindAsync(request.UserId);
        return (TimeOffDto.FromEntity(request, user), null);
    }
}
