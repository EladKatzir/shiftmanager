using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Api.Dto;
using ShiftManager.Models.Support;

namespace ShiftManager.Services.Api;

/// <summary>
/// API service for OnDuty operations.
/// Handles all on-duty CRUD operations for the API layer.
/// NOTE: OnDuty assignments are global (not company-scoped).
/// </summary>
public class OnDutyApiService
{
    private readonly AppDbContext _context;
    private readonly ILogger<OnDutyApiService> _logger;

    public OnDutyApiService(AppDbContext context, ILogger<OnDutyApiService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Lists on-duty assignments with pagination and filtering.
    /// NOTE: On-duty is global, so results are not filtered by company.
    /// </summary>
    public async Task<(List<OnDutyDto> OnDuties, int TotalCount)> ListOnDutiesAsync(
        int page = 1,
        int pageSize = 50,
        int? userId = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        string? type = null,
        bool includeRelated = false,
        bool includeCanceled = false)
    {
        // Validate pagination
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 100) pageSize = 100;

        // On-duty is global, use IgnoreQueryFilters to bypass company filtering
        var query = _context.OnDuties.IgnoreQueryFilters().AsQueryable();

        // Filter by canceled status
        if (!includeCanceled)
        {
            query = query.Where(od => od.CanceledAt == null);
        }

        // Filter by user
        if (userId.HasValue)
        {
            query = query.Where(od => od.UserId == userId.Value);
        }

        // Filter by date range
        if (startDate.HasValue)
        {
            query = query.Where(od => od.Date >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(od => od.Date <= endDate.Value);
        }

        // Filter by type
        if (!string.IsNullOrEmpty(type) && Enum.TryParse<OnDutyType>(type, true, out var typeEnum))
        {
            query = query.Where(od => od.Type == typeEnum);
        }

        // Get total count
        var totalCount = await query.CountAsync();

        // Apply pagination
        var onDuties = await query
            .OrderBy(od => od.Date)
            .ThenBy(od => od.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Map to DTOs
        var dtos = new List<OnDutyDto>();
        foreach (var onDuty in onDuties)
        {
            var dto = await MapToDto(onDuty, includeRelated);
            dtos.Add(dto);
        }

        return (dtos, totalCount);
    }

    /// <summary>
    /// Gets a single on-duty assignment by ID.
    /// NOTE: On-duty is global, so no company filtering.
    /// </summary>
    public async Task<OnDutyDto?> GetOnDutyAsync(int onDutyId, bool includeRelated = true)
    {
        var onDuty = await _context.OnDuties
            .IgnoreQueryFilters()
            .Where(od => od.Id == onDutyId)
            .FirstOrDefaultAsync();

        if (onDuty == null)
        {
            return null;
        }

        return await MapToDto(onDuty, includeRelated);
    }

    /// <summary>
    /// Creates a new on-duty assignment.
    /// </summary>
    public async Task<(OnDutyDto? OnDuty, string? Error)> CreateOnDutyAsync(
        int creatorId,
        CreateOnDutyDto dto)
    {
        // Validate user exists (use IgnoreQueryFilters since on-duty is cross-company)
        var user = await _context.Users
            .IgnoreQueryFilters()
            .Where(u => u.Id == dto.UserId)
            .FirstOrDefaultAsync();

        if (user == null)
        {
            return (null, "User not found");
        }

        // Parse date
        if (!DateOnly.TryParse(dto.Date, out var date))
        {
            return (null, "Invalid date format. Use yyyy-MM-dd");
        }

        // Parse type
        if (!Enum.TryParse<OnDutyType>(dto.Type, true, out var type))
        {
            return (null, "Invalid type. Must be 'Hakam' or 'Lead'");
        }

        // Check for existing active on-duty assignment of the same type on the same day
        var existingOnDuty = await _context.OnDuties
            .IgnoreQueryFilters()
            .Where(od => od.Date == date &&
                        od.Type == type &&
                        od.CanceledAt == null)
            .FirstOrDefaultAsync();

        if (existingOnDuty != null)
        {
            return (null, $"An active {type} on-duty assignment already exists for this date");
        }

        // Create on-duty assignment
        var onDuty = new OnDuty
        {
            UserId = dto.UserId,
            Date = date,
            Type = type,
            Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim(),
            CreatedBy = creatorId,
            CreatedAt = DateTime.UtcNow
        };

        _context.OnDuties.Add(onDuty);
        await _context.SaveChangesAsync();

        _logger.LogInformation("On-duty assignment created: Id={Id}, UserId={UserId}, Date={Date}, Type={Type}",
            onDuty.Id, dto.UserId, date, type);

        var result = await GetOnDutyAsync(onDuty.Id);
        return (result, null);
    }

    /// <summary>
    /// Updates an existing on-duty assignment.
    /// </summary>
    public async Task<(OnDutyDto? OnDuty, string? Error)> UpdateOnDutyAsync(
        int onDutyId,
        UpdateOnDutyDto dto)
    {
        var onDuty = await _context.OnDuties
            .IgnoreQueryFilters()
            .Where(od => od.Id == onDutyId)
            .FirstOrDefaultAsync();

        if (onDuty == null)
        {
            return (null, "On-duty assignment not found");
        }

        if (onDuty.CanceledAt != null)
        {
            return (null, "Cannot update a canceled on-duty assignment");
        }

        // Update notes
        if (dto.Notes != null)
        {
            onDuty.Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim();
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("On-duty assignment updated: Id={Id}", onDutyId);

        var result = await GetOnDutyAsync(onDutyId);
        return (result, null);
    }

    /// <summary>
    /// Deletes (cancels) an on-duty assignment.
    /// </summary>
    public async Task<bool> DeleteOnDutyAsync(int onDutyId, int canceledBy)
    {
        var onDuty = await _context.OnDuties
            .IgnoreQueryFilters()
            .Where(od => od.Id == onDutyId)
            .FirstOrDefaultAsync();

        if (onDuty == null)
        {
            return false;
        }

        if (onDuty.CanceledAt != null)
        {
            return false; // Already canceled
        }

        // Soft delete
        onDuty.CanceledAt = DateTime.UtcNow;
        onDuty.CanceledBy = canceledBy;

        await _context.SaveChangesAsync();

        _logger.LogInformation("On-duty assignment canceled: Id={Id}, CanceledBy={CanceledBy}", onDutyId, canceledBy);

        return true;
    }

    /// <summary>
    /// Maps OnDuty entity to DTO with optional related data.
    /// </summary>
    private async Task<OnDutyDto> MapToDto(OnDuty onDuty, bool includeRelated)
    {
        var dto = new OnDutyDto
        {
            Id = onDuty.Id,
            UserId = onDuty.UserId,
            Date = onDuty.Date.ToString("yyyy-MM-dd"),
            Type = onDuty.Type.ToString(),
            Notes = onDuty.Notes,
            CreatedBy = onDuty.CreatedBy,
            CreatedAt = onDuty.CreatedAt,
            CanceledAt = onDuty.CanceledAt,
            CanceledBy = onDuty.CanceledBy,
            IsActive = onDuty.IsActive
        };

        if (includeRelated)
        {
            // Load related entities
            await _context.Entry(onDuty).Reference(od => od.User).LoadAsync();
            await _context.Entry(onDuty).Reference(od => od.Creator).LoadAsync();

            if (onDuty.CanceledBy.HasValue)
            {
                await _context.Entry(onDuty).Reference(od => od.Canceler).LoadAsync();
            }

            // Map users
            dto.User = onDuty.User != null ? MapUserDto(onDuty.User) : null;
            dto.Creator = onDuty.Creator != null ? MapUserDto(onDuty.Creator) : null;
            dto.Canceler = onDuty.Canceler != null ? MapUserDto(onDuty.Canceler) : null;
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
