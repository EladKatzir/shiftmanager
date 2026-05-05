using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Api.Dto;
using ShiftManager.Models.Support;

namespace ShiftManager.Services.Api;

/// <summary>
/// API service for Chore operations.
/// Handles all chore CRUD operations for the API layer.
/// </summary>
public class ChoreApiService
{
    private readonly AppDbContext _context;
    private readonly ILogger<ChoreApiService> _logger;
    private readonly IBusyService _busyService;

    public ChoreApiService(AppDbContext context, ILogger<ChoreApiService> logger, IBusyService busyService)
    {
        _context = context;
        _logger = logger;
        _busyService = busyService;
    }

    // V1 callers (external integrators) have historically received English-language conflict
    // strings. The unified BusyService returns stable Keys instead. Map known keys back to the
    // V1 strings to keep backward-compat for existing API consumers; fall through to the key
    // for any new severity-policy outcomes V1 hadn't seen before.
    private static string MapBusyKeyToV1Message(string key) => key switch
    {
        "USER_NOT_FOUND"        => "User not found",
        "USER_INACTIVE"         => "User is inactive",
        "USER_NOT_IN_MOLECULE"  => "User is not in this molecule",
        "CHORE_CONFLICT"        => "User already has an active chore on this date",
        "DUPLICATE_CHORE"       => "User already has an active chore on this date",
        "SHIFT_EXISTS_CONFLICT" => "User already has a shift assignment on this date. Chores and shifts are mutually exclusive.",
        "ONDUTY_CONFLICT"       => "User already has an on-duty assignment on this date",
        "VACATION_CONFLICT"     => "User is on approved vacation on this date",
        "PAST_DATE"             => "Cannot create chore for a past date",
        _ => key
    };

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

        // Resolve the user's molecule so BusyService can run its cross-tenant predicate.
        // Pre-refactor, V1's inline checks were tenant-scoped — silently missing conflicts
        // in sibling companies of the same molecule. BusyService closes that gap.
        var company = await _context.Companies
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == companyId);
        if (company == null)
        {
            return (null, "Company not found");
        }
        if (!company.MoleculeId.HasValue)
        {
            // Misconfigured company — busy detection requires a molecule for cross-tenant scope.
            return (null, "Company is not assigned to a molecule");
        }

        var validation = await _busyService.ValidateAsync(
            new BusyTarget.Chore(date, company.MoleculeId.Value),
            userId: dto.UserId,
            actorUserId: creatorId);

        if (!validation.CanProceed)
        {
            var firstErr = validation.Errors.FirstOrDefault();
            return (null, firstErr != null ? MapBusyKeyToV1Message(firstErr.Key) : "Validation failed");
        }

        // V1 has no override mechanism (no UI), so any warning blocks the create.
        // External integrators that need to bypass should resolve the conflict before retry.
        if (validation.Warnings.Count > 0)
        {
            var firstWarn = validation.Warnings.First();
            return (null, MapBusyKeyToV1Message(firstWarn.Key));
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
