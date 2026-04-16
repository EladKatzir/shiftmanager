using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

public sealed class AreaPaletteService : IAreaPaletteService
{
    private const string CacheKeyPrefix = "area-palette:";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly AppDbContext _db;
    private readonly IHierarchyService _hierarchyService;
    private readonly IHttpContextAccessor _httpContext;
    private readonly IMemoryCache _cache;

    public AreaPaletteService(
        AppDbContext db,
        IHierarchyService hierarchyService,
        IHttpContextAccessor httpContext,
        IMemoryCache cache)
    {
        _db = db;
        _hierarchyService = hierarchyService;
        _httpContext = httpContext;
        _cache = cache;
    }

    public async Task<AreaCalendarPalette?> GetPaletteForCurrentUserAsync()
    {
        var userIdClaim = _httpContext.HttpContext?.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId)) return null;

        var hierarchy = await _hierarchyService.GetUserHierarchyContextAsync(userId);
        var areaId = hierarchy?.Path?.Area?.Id;
        if (!areaId.HasValue) return null;

        return await GetPaletteForAreaAsync(areaId.Value);
    }

    public async Task<AreaCalendarPalette?> GetPaletteForAreaAsync(int areaId)
    {
        var key = CacheKeyPrefix + areaId;
        if (_cache.TryGetValue<AreaCalendarPalette?>(key, out var cached))
        {
            return cached;
        }

        var palette = await _db.AreaCalendarPalettes
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.AreaId == areaId);

        _cache.Set(key, palette, CacheTtl);
        return palette;
    }

    public async Task SavePaletteAsync(int areaId, AreaCalendarPalette values, int actingUserId)
    {
        var existing = await _db.AreaCalendarPalettes.FirstOrDefaultAsync(p => p.AreaId == areaId);
        if (existing == null)
        {
            existing = new AreaCalendarPalette
            {
                AreaId = areaId,
                CreatedAt = DateTime.UtcNow
            };
            _db.AreaCalendarPalettes.Add(existing);
        }

        existing.ShiftMorning   = values.ShiftMorning;
        existing.ShiftAfternoon = values.ShiftAfternoon;
        existing.ShiftNight     = values.ShiftNight;
        existing.ShiftHome      = values.ShiftHome;
        existing.OnDuty         = values.OnDuty;
        existing.Chore          = values.Chore;
        existing.Vacation       = values.Vacation;
        existing.UpdatedAt      = DateTime.UtcNow;
        existing.UpdatedByUserId = actingUserId;

        await _db.SaveChangesAsync();
        InvalidateAreaCache(areaId);
    }

    public void InvalidateAreaCache(int areaId) => _cache.Remove(CacheKeyPrefix + areaId);
}
