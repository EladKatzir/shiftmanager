using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Api.Dto;
using ShiftManager.Models.Support;

namespace ShiftManager.Services.Api;

/// <summary>
/// API service for Swap Request operations.
/// Handles all swap request CRUD operations for the API layer.
/// </summary>
public class SwapRequestApiService
{
    private readonly AppDbContext _context;
    private readonly ILogger<SwapRequestApiService> _logger;

    public SwapRequestApiService(AppDbContext context, ILogger<SwapRequestApiService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Lists swap requests with pagination and filtering.
    /// </summary>
    public async Task<(List<SwapRequestDto> Requests, int TotalCount)> ListSwapRequestsAsync(
        int companyId,
        int page = 1,
        int pageSize = 50,
        int? userId = null,
        string? status = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        bool includeRelated = false)
    {
        // Validate pagination
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 100) pageSize = 100;

        var query = _context.SwapRequests.AsQueryable();

        // Filter by company
        query = query.Where(sr => sr.CompanyId == companyId);

        // Filter by user (either fromUser or toUser)
        if (userId.HasValue)
        {
            query = query.Where(sr => sr.FromUserId == userId.Value || sr.ToUserId == userId.Value);
        }

        // Filter by status
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<RequestStatus>(status, true, out var statusEnum))
        {
            query = query.Where(sr => sr.Status == statusEnum);
        }

        // Filter by date range (based on shift dates via assignment)
        if (startDate.HasValue || endDate.HasValue)
        {
            query = query.Include(sr => sr.FromAssignment)
                .ThenInclude(a => a!.ShiftInstance);

            if (startDate.HasValue)
            {
                query = query.Where(sr => sr.FromAssignment!.ShiftInstance!.WorkDate >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                query = query.Where(sr => sr.FromAssignment!.ShiftInstance!.WorkDate <= endDate.Value);
            }
        }

        // Get total count
        var totalCount = await query.CountAsync();

        // Apply pagination
        var swapRequests = await query
            .OrderByDescending(sr => sr.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Map to DTOs
        var dtos = new List<SwapRequestDto>();
        foreach (var sr in swapRequests)
        {
            var dto = await MapToDto(sr, includeRelated);
            dtos.Add(dto);
        }

        return (dtos, totalCount);
    }

    /// <summary>
    /// Gets a single swap request by ID.
    /// </summary>
    public async Task<SwapRequestDto?> GetSwapRequestAsync(int companyId, int requestId, bool includeRelated = true)
    {
        var swapRequest = await _context.SwapRequests
            .Where(sr => sr.CompanyId == companyId && sr.Id == requestId)
            .FirstOrDefaultAsync();

        if (swapRequest == null)
        {
            return null;
        }

        return await MapToDto(swapRequest, includeRelated);
    }

    /// <summary>
    /// Creates a new swap request.
    /// </summary>
    public async Task<(SwapRequestDto? SwapRequest, string? Error)> CreateSwapRequestAsync(
        int companyId,
        int fromUserId,
        CreateSwapRequestDto dto)
    {
        // Validate fromAssignment exists and belongs to the user
        var fromAssignment = await _context.ShiftAssignments
            .Include(a => a.ShiftInstance)
            .Where(a => a.Id == dto.FromAssignmentId && a.CompanyId == companyId)
            .FirstOrDefaultAsync();

        if (fromAssignment == null)
        {
            return (null, "From assignment not found");
        }

        if (fromAssignment.UserId != fromUserId)
        {
            return (null, "You can only swap your own shifts");
        }

        // Validate toAssignment if provided
        if (dto.ToAssignmentId.HasValue)
        {
            var toAssignment = await _context.ShiftAssignments
                .Where(a => a.Id == dto.ToAssignmentId.Value && a.CompanyId == companyId)
                .FirstOrDefaultAsync();

            if (toAssignment == null)
            {
                return (null, "To assignment not found");
            }

            // If toAssignment has a user, use that as toUserId
            if (toAssignment.UserId.HasValue && dto.ToUserId == null)
            {
                dto.ToUserId = toAssignment.UserId.Value;
            }
        }

        // Check for conflicting pending swap on the same assignment (prevents circular swaps)
        var hasConflictingSwap = await _context.SwapRequests
            .AnyAsync(sr => sr.Status == RequestStatus.Pending
                && sr.CompanyId == companyId
                && (sr.FromAssignmentId == dto.FromAssignmentId
                    || (dto.ToAssignmentId.HasValue && sr.FromAssignmentId == dto.ToAssignmentId.Value)
                    || sr.ToAssignmentId == dto.FromAssignmentId
                    || (dto.ToAssignmentId.HasValue && sr.ToAssignmentId == dto.ToAssignmentId.Value)));

        if (hasConflictingSwap)
        {
            return (null, "A pending swap request already exists for one of these assignments. Cancel it first.");
        }

        // Create swap request
        var swapRequest = new SwapRequest
        {
            CompanyId = companyId,
            FromAssignmentId = dto.FromAssignmentId,
            ToAssignmentId = dto.ToAssignmentId,
            FromUserId = fromUserId,
            ToUserId = dto.ToUserId,
            Status = RequestStatus.Pending,
            Reason = dto.Reason,
            CreatedAt = DateTime.UtcNow
        };

        _context.SwapRequests.Add(swapRequest);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Swap request created: Id={Id}, FromUser={FromUser}, ToUser={ToUser}",
            swapRequest.Id, fromUserId, dto.ToUserId);

        var result = await GetSwapRequestAsync(companyId, swapRequest.Id);
        return (result, null);
    }

    /// <summary>
    /// Approves a swap request (admin operation).
    /// </summary>
    public async Task<(SwapRequestDto? SwapRequest, string? Error)> ApproveSwapRequestAsync(
        int companyId,
        int requestId,
        int reviewerId)
    {
        var swapRequest = await _context.SwapRequests
            .Include(sr => sr.FromAssignment)
            .Include(sr => sr.ToAssignment)
            .Where(sr => sr.CompanyId == companyId && sr.Id == requestId)
            .FirstOrDefaultAsync();

        if (swapRequest == null)
        {
            return (null, "Swap request not found");
        }

        if (swapRequest.Status != RequestStatus.Pending)
        {
            return (null, $"Swap request is already {swapRequest.Status}");
        }

        // Wrap swap in transaction to prevent conflicting concurrent approvals
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // Re-check no other swap was already approved for the same assignments
            var conflictingApproved = await _context.SwapRequests
                .AnyAsync(sr => sr.Id != requestId
                    && sr.Status == RequestStatus.Approved
                    && (sr.FromAssignmentId == swapRequest.FromAssignmentId
                        || sr.ToAssignmentId == swapRequest.FromAssignmentId
                        || (swapRequest.ToAssignmentId.HasValue &&
                            (sr.FromAssignmentId == swapRequest.ToAssignmentId.Value
                            || sr.ToAssignmentId == swapRequest.ToAssignmentId.Value))));

            if (conflictingApproved)
            {
                await transaction.RollbackAsync();
                return (null, "A conflicting swap was just approved. Please review assignments before retrying.");
            }

            // Perform the swap
            if (swapRequest.FromAssignment == null)
            {
                await transaction.RollbackAsync();
                return (null, "From assignment not found — it may have been deleted.");
            }

            if (swapRequest.ToAssignmentId.HasValue)
            {
                if (swapRequest.ToAssignment == null)
                {
                    await transaction.RollbackAsync();
                    return (null, "To assignment not found — it may have been deleted.");
                }

                var fromUserId = swapRequest.FromAssignment.UserId;
                var toUserId = swapRequest.ToAssignment.UserId;

                swapRequest.FromAssignment.UserId = toUserId;
                swapRequest.ToAssignment.UserId = fromUserId;
            }
            else
            {
                // Just remove from assignment
                swapRequest.FromAssignment.UserId = null;
            }

            // Update swap request status
            swapRequest.Status = RequestStatus.Approved;
            swapRequest.ReviewedAt = DateTime.UtcNow;
            swapRequest.ReviewedBy = reviewerId;

            // Auto-decline other pending swaps for the same assignments
            var conflictingPending = await _context.SwapRequests
                .Where(sr => sr.Id != requestId
                    && sr.Status == RequestStatus.Pending
                    && sr.CompanyId == companyId
                    && (sr.FromAssignmentId == swapRequest.FromAssignmentId
                        || sr.ToAssignmentId == swapRequest.FromAssignmentId
                        || (swapRequest.ToAssignmentId.HasValue &&
                            (sr.FromAssignmentId == swapRequest.ToAssignmentId.Value
                            || sr.ToAssignmentId == swapRequest.ToAssignmentId.Value))))
                .ToListAsync();

            foreach (var conflicting in conflictingPending)
            {
                conflicting.Status = RequestStatus.Declined;
                conflicting.ReviewedAt = DateTime.UtcNow;
                conflicting.ReviewedBy = reviewerId;
                conflicting.DeclineReason = "Auto-declined: a conflicting swap was approved";
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Failed to approve swap request {RequestId}", requestId);
            return (null, "Failed to approve swap - please try again");
        }

        _logger.LogInformation("Swap request approved: Id={Id}, Reviewer={Reviewer}",
            requestId, reviewerId);

        var result = await GetSwapRequestAsync(companyId, requestId);
        return (result, null);
    }

    /// <summary>
    /// Declines a swap request (admin operation).
    /// </summary>
    public async Task<(SwapRequestDto? SwapRequest, string? Error)> DeclineSwapRequestAsync(
        int companyId,
        int requestId,
        int reviewerId,
        string? declineReason)
    {
        var swapRequest = await _context.SwapRequests
            .Where(sr => sr.CompanyId == companyId && sr.Id == requestId)
            .FirstOrDefaultAsync();

        if (swapRequest == null)
        {
            return (null, "Swap request not found");
        }

        if (swapRequest.Status != RequestStatus.Pending)
        {
            return (null, $"Swap request is already {swapRequest.Status}");
        }

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            swapRequest.Status = RequestStatus.Declined;
            swapRequest.ReviewedAt = DateTime.UtcNow;
            swapRequest.ReviewedBy = reviewerId;
            swapRequest.DeclineReason = declineReason;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Failed to decline swap request {RequestId}", requestId);
            return (null, "Failed to decline swap - please try again");
        }

        _logger.LogInformation("Swap request declined: Id={Id}, Reviewer={Reviewer}, Reason={Reason}",
            requestId, reviewerId, declineReason);

        var result = await GetSwapRequestAsync(companyId, requestId);
        return (result, null);
    }

    /// <summary>
    /// Deletes (cancels) a swap request.
    /// </summary>
    public async Task<bool> DeleteSwapRequestAsync(int companyId, int requestId, int userId)
    {
        var swapRequest = await _context.SwapRequests
            .Where(sr => sr.CompanyId == companyId && sr.Id == requestId)
            .FirstOrDefaultAsync();

        if (swapRequest == null)
        {
            return false;
        }

        // Only the requester can cancel their own request
        if (swapRequest.FromUserId != userId)
        {
            return false;
        }

        // Only pending requests can be canceled
        if (swapRequest.Status != RequestStatus.Pending)
        {
            return false;
        }

        _context.SwapRequests.Remove(swapRequest);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Swap request deleted: Id={Id}, User={User}", requestId, userId);

        return true;
    }

    /// <summary>
    /// Maps SwapRequest entity to DTO with optional related data.
    /// </summary>
    private async Task<SwapRequestDto> MapToDto(SwapRequest swapRequest, bool includeRelated)
    {
        var dto = new SwapRequestDto
        {
            Id = swapRequest.Id,
            FromAssignmentId = swapRequest.FromAssignmentId,
            ToAssignmentId = swapRequest.ToAssignmentId,
            FromUserId = swapRequest.FromUserId,
            ToUserId = swapRequest.ToUserId,
            Status = swapRequest.Status,
            Reason = swapRequest.Reason,
            DeclineReason = swapRequest.DeclineReason,
            CreatedAt = swapRequest.CreatedAt,
            ReviewedAt = swapRequest.ReviewedAt,
            ReviewedBy = swapRequest.ReviewedBy
        };

        if (includeRelated)
        {
            // Load related entities
            await _context.Entry(swapRequest).Reference(sr => sr.FromUser).LoadAsync();
            await _context.Entry(swapRequest).Reference(sr => sr.ToUser).LoadAsync();
            await _context.Entry(swapRequest).Reference(sr => sr.FromAssignment).LoadAsync();

            if (swapRequest.FromAssignment != null)
            {
                await _context.Entry(swapRequest.FromAssignment).Reference(a => a.ShiftInstance).LoadAsync();
                if (swapRequest.FromAssignment.ShiftInstance != null)
                {
                    await _context.Entry(swapRequest.FromAssignment.ShiftInstance).Reference(si => si.ShiftType).LoadAsync();
                }
            }

            if (swapRequest.ToAssignmentId.HasValue)
            {
                await _context.Entry(swapRequest).Reference(sr => sr.ToAssignment).LoadAsync();
                if (swapRequest.ToAssignment != null)
                {
                    await _context.Entry(swapRequest.ToAssignment).Reference(a => a.ShiftInstance).LoadAsync();
                    if (swapRequest.ToAssignment.ShiftInstance != null)
                    {
                        await _context.Entry(swapRequest.ToAssignment.ShiftInstance).Reference(si => si.ShiftType).LoadAsync();
                    }
                }
            }

            if (swapRequest.ReviewedBy.HasValue)
            {
                await _context.Entry(swapRequest).Reference(sr => sr.Reviewer).LoadAsync();
                dto.Reviewer = swapRequest.Reviewer != null ? MapUserDto(swapRequest.Reviewer) : null;
            }

            // Map users
            dto.FromUser = swapRequest.FromUser != null ? MapUserDto(swapRequest.FromUser) : null;
            dto.ToUser = swapRequest.ToUser != null ? MapUserDto(swapRequest.ToUser) : null;

            // Map assignments
            dto.FromAssignment = swapRequest.FromAssignment != null ? MapAssignmentDto(swapRequest.FromAssignment) : null;
            dto.ToAssignment = swapRequest.ToAssignment != null ? MapAssignmentDto(swapRequest.ToAssignment) : null;
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

    private SwapShiftAssignmentDto MapAssignmentDto(ShiftAssignment assignment)
    {
        var dto = new SwapShiftAssignmentDto
        {
            Id = assignment.Id,
            ShiftInstanceId = assignment.ShiftInstanceId,
            UserId = assignment.UserId
        };

        if (assignment.ShiftInstance != null)
        {
            dto.ShiftInstance = new SwapShiftInstanceDto
            {
                Id = assignment.ShiftInstance.Id,
                ShiftTypeId = assignment.ShiftInstance.ShiftTypeId,
                WorkDate = assignment.ShiftInstance.WorkDate.ToString("yyyy-MM-dd"),
                StaffingRequired = assignment.ShiftInstance.StaffingRequired
            };

            if (assignment.ShiftInstance.ShiftType != null)
            {
                dto.ShiftInstance.ShiftType = new SwapShiftTypeDto
                {
                    Id = assignment.ShiftInstance.ShiftType.Id,
                    Name = assignment.ShiftInstance.ShiftType.Name,
                    Key = assignment.ShiftInstance.ShiftType.Key,
                    Start = assignment.ShiftInstance.ShiftType.Start.ToString("HH:mm"),
                    End = assignment.ShiftInstance.ShiftType.End.ToString("HH:mm")
                };
            }
        }

        return dto;
    }
}
