using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models.Api.Dto;

namespace ShiftManager.Services.Api;

/// <summary>
/// Wrapper service for Shift API operations.
/// Isolates API logic from existing shift services to maintain zero regression.
/// </summary>
public class ShiftApiService
{
    private readonly AppDbContext _context;
    private readonly ILogger<ShiftApiService> _logger;

    public ShiftApiService(AppDbContext context, ILogger<ShiftApiService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Lists shift instances with pagination and filtering.
    /// Respects global query filters for multi-tenant isolation.
    /// </summary>
    public async Task<(List<ShiftDto> Shifts, int TotalCount)> ListShiftsAsync(
        int companyId,
        int page = 1,
        int pageSize = 50,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        int? shiftTypeId = null,
        int? userId = null,
        bool? hasOpenSlots = null)
    {
        _logger.LogInformation("Listing shifts. CompanyId={CompanyId}, Page={Page}, PageSize={PageSize}, StartDate={StartDate}, EndDate={EndDate}, ShiftTypeId={ShiftTypeId}",
            companyId, page, pageSize, startDate, endDate, shiftTypeId);

        // Validate pagination parameters
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 100) pageSize = 100; // Max page size

        var query = _context.ShiftInstances
            .Include(s => s.ShiftType)
            .AsQueryable();

        // Manual CompanyId filter (even though global filter applies, explicit is safer for API)
        query = query.Where(s => s.CompanyId == companyId);

        // Apply date range filter
        if (startDate.HasValue)
        {
            query = query.Where(s => s.WorkDate >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(s => s.WorkDate <= endDate.Value);
        }

        // Apply shift type filter
        if (shiftTypeId.HasValue)
        {
            query = query.Where(s => s.ShiftTypeId == shiftTypeId.Value);
        }

        // Note: User assignment and open slots filters would require joining with ShiftAssignments table
        // For now, these filters are not supported in Phase 1

        // Get total count before pagination
        var totalCount = await query.CountAsync();

        _logger.LogDebug("Found {TotalCount} shifts matching criteria. CompanyId={CompanyId}", totalCount, companyId);

        // Apply pagination
        var shifts = await query
            .OrderBy(s => s.WorkDate)
            .ThenBy(s => s.ShiftType!.Start)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Map to DTOs
        var dtos = shifts.Select(s => ShiftDto.FromEntity(s)).ToList();

        _logger.LogInformation("Successfully retrieved {Count} shifts. CompanyId={CompanyId}, TotalCount={TotalCount}",
            dtos.Count, companyId, totalCount);

        return (dtos, totalCount);
    }

    /// <summary>
    /// Gets a single shift instance by ID with full details.
    /// Respects global query filters for multi-tenant isolation.
    /// </summary>
    public async Task<ShiftDto?> GetShiftAsync(int companyId, int shiftId)
    {
        _logger.LogInformation("Getting shift details. CompanyId={CompanyId}, ShiftId={ShiftId}", companyId, shiftId);

        var shift = await _context.ShiftInstances
            .Include(s => s.ShiftType)
            .Where(s => s.CompanyId == companyId && s.Id == shiftId)
            .FirstOrDefaultAsync();

        if (shift == null)
        {
            _logger.LogWarning("Shift not found. CompanyId={CompanyId}, ShiftId={ShiftId}", companyId, shiftId);
            return null;
        }

        _logger.LogDebug("Successfully retrieved shift. CompanyId={CompanyId}, ShiftId={ShiftId}, WorkDate={WorkDate}",
            companyId, shiftId, shift.WorkDate);

        // Return full details
        return ShiftDto.FromEntity(shift);
    }
}
