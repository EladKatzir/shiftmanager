using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Api.Dto;

namespace ShiftManager.Services.Api;

/// <summary>
/// API service for Chore operations.
/// Handles all chore CRUD operations for the API layer.
/// </summary>
public class ChoreApiService
{
    private readonly AppDbContext _context;
    private readonly ILogger<ChoreApiService> _logger;

    public ChoreApiService(AppDbContext context, ILogger<ChoreApiService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Lists chores with pagination and filtering.
    /// </summary>
    public async Task<(List<ChoreDto> Chores, int TotalCount)> ListChoresAsync(
        int companyId,
        int page = 1,
        int pageSize = 50,
        int? userId = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        bool includeRelated = false,
        bool includeCanceled = false)
    {
        // Validate pagination
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 100) pageSize = 100;

        var query = _context.Chores.AsQueryable();

        // Filter by company
        query = query.Where(c => c.CompanyId == companyId);

        // Filter by canceled status
        if (!includeCanceled)
        {
            query = query.Where(c => c.CanceledAt == null);
        }

        // Filter by user
        if (userId.HasValue)
        {
            query = query.Where(c => c.UserId == userId.Value);
        }

        // Filter by date range
        if (startDate.HasValue)
        {
            query = query.Where(c => c.Date >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(c => c.Date <= endDate.Value);
        }

        // Get total count
        var totalCount = await query.CountAsync();

        // Apply pagination
        var chores = await query
            .OrderBy(c => c.Date)
            .ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Map to DTOs
        var dtos = new List<ChoreDto>();
        foreach (var chore in chores)
        {
            var dto = await MapToDto(chore, includeRelated);
            dtos.Add(dto);
        }

        return (dtos, totalCount);
    }

    /// <summary>
    /// Gets a single chore by ID.
    /// </summary>
    public async Task<ChoreDto?> GetChoreAsync(int companyId, int choreId, bool includeRelated = true)
    {
        var chore = await _context.Chores
            .Where(c => c.CompanyId == companyId && c.Id == choreId)
            .FirstOrDefaultAsync();

        if (chore == null)
        {
            return null;
        }

        return await MapToDto(chore, includeRelated);
    }

    /// <summary>
    /// Creates a new chore.
    /// </summary>
    public async Task<(ChoreDto? Chore, string? Error)> CreateChoreAsync(
        int companyId,
        int creatorId,
        CreateChoreDto dto)
    {
        // Validate user exists
        var user = await _context.Users
            .Where(u => u.Id == dto.UserId && u.CompanyId == companyId)
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

        // Validate title
        if (string.IsNullOrWhiteSpace(dto.Title))
        {
            return (null, "Title is required");
        }

        // Check for conflict with existing shift assignment on the same day
        var hasShiftOnDate = await _context.ShiftAssignments
            .Include(a => a.ShiftInstance)
            .AnyAsync(a => a.UserId == dto.UserId &&
                          a.ShiftInstance.WorkDate == date &&
                          a.CompanyId == companyId);

        if (hasShiftOnDate)
        {
            return (null, "User already has a shift assignment on this date. Chores and shifts are mutually exclusive.");
        }

        // Check for existing active chore on the same day
        var existingChore = await _context.Chores
            .Where(c => c.CompanyId == companyId &&
                       c.UserId == dto.UserId &&
                       c.Date == date &&
                       c.CanceledAt == null)
            .FirstOrDefaultAsync();

        if (existingChore != null)
        {
            return (null, "User already has an active chore on this date");
        }

        // Create chore
        var chore = new Chore
        {
            CompanyId = companyId,
            UserId = dto.UserId,
            Date = date,
            Title = dto.Title.Trim(),
            Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim(),
            CreatedBy = creatorId,
            CreatedAt = DateTime.UtcNow
        };

        _context.Chores.Add(chore);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Chore created: Id={Id}, UserId={UserId}, Date={Date}",
            chore.Id, dto.UserId, date);

        var result = await GetChoreAsync(companyId, chore.Id);
        return (result, null);
    }

    /// <summary>
    /// Updates an existing chore.
    /// </summary>
    public async Task<(ChoreDto? Chore, string? Error)> UpdateChoreAsync(
        int companyId,
        int choreId,
        UpdateChoreDto dto)
    {
        var chore = await _context.Chores
            .Where(c => c.CompanyId == companyId && c.Id == choreId)
            .FirstOrDefaultAsync();

        if (chore == null)
        {
            return (null, "Chore not found");
        }

        if (chore.CanceledAt != null)
        {
            return (null, "Cannot update a canceled chore");
        }

        // Update fields if provided
        if (!string.IsNullOrWhiteSpace(dto.Title))
        {
            chore.Title = dto.Title.Trim();
        }

        if (dto.Notes != null)
        {
            chore.Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim();
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("Chore updated: Id={Id}", choreId);

        var result = await GetChoreAsync(companyId, choreId);
        return (result, null);
    }

    /// <summary>
    /// Deletes (cancels) a chore.
    /// </summary>
    public async Task<bool> DeleteChoreAsync(int companyId, int choreId, int canceledBy)
    {
        var chore = await _context.Chores
            .Where(c => c.CompanyId == companyId && c.Id == choreId)
            .FirstOrDefaultAsync();

        if (chore == null)
        {
            return false;
        }

        if (chore.CanceledAt != null)
        {
            return false; // Already canceled
        }

        // Soft delete
        chore.CanceledAt = DateTime.UtcNow;
        chore.CanceledBy = canceledBy;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Chore canceled: Id={Id}, CanceledBy={CanceledBy}", choreId, canceledBy);

        return true;
    }

    /// <summary>
    /// Maps Chore entity to DTO with optional related data.
    /// </summary>
    private async Task<ChoreDto> MapToDto(Chore chore, bool includeRelated)
    {
        var dto = new ChoreDto
        {
            Id = chore.Id,
            UserId = chore.UserId,
            Date = chore.Date.ToString("yyyy-MM-dd"),
            Title = chore.Title,
            Notes = chore.Notes,
            CreatedBy = chore.CreatedBy,
            CreatedAt = chore.CreatedAt,
            CanceledAt = chore.CanceledAt,
            CanceledBy = chore.CanceledBy,
            IsActive = chore.IsActive
        };

        if (includeRelated)
        {
            // Load related entities
            await _context.Entry(chore).Reference(c => c.User).LoadAsync();
            await _context.Entry(chore).Reference(c => c.Creator).LoadAsync();

            if (chore.CanceledBy.HasValue)
            {
                await _context.Entry(chore).Reference(c => c.Canceler).LoadAsync();
            }

            // Map users
            dto.User = chore.User != null ? MapUserDto(chore.User) : null;
            dto.Creator = chore.Creator != null ? MapUserDto(chore.Creator) : null;
            dto.Canceler = chore.Canceler != null ? MapUserDto(chore.Canceler) : null;
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
