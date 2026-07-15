using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// Owner-scoped CRUD for <see cref="DeskTeamView"/> — saved dynamic (TargetCompany × JobType)
/// team-table views for the /Calendar/Team page. See <see cref="IDeskTeamViewService"/> for the
/// rationale for keeping this separate from TeamCalendar/TeamCalendarService.
/// </summary>
public class DeskTeamViewService : IDeskTeamViewService
{
    private readonly AppDbContext _db;
    private readonly ITenantResolver _tenantResolver;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DeskTeamViewService(AppDbContext db, ITenantResolver tenantResolver, IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _tenantResolver = tenantResolver;
        _httpContextAccessor = httpContextAccessor;
    }

    private int GetCurrentUserId()
    {
        var userIdClaim = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out var userId) ? userId : 0;
    }

    public async Task<List<DeskTeamView>> ListForOwnerAsync()
    {
        var ownerId = GetCurrentUserId();
        return await _db.DeskTeamViews
            .Where(v => v.OwnerId == ownerId && !v.IsDeleted)
            .OrderBy(v => v.SortOrder)
            .ThenBy(v => v.CreatedAt)
            .ToListAsync();
    }

    public async Task<DeskTeamView> CreateAsync(int targetCompanyId, int jobTypeId, string name)
    {
        var now = DateTime.UtcNow;
        var view = new DeskTeamView
        {
            CompanyId = _tenantResolver.GetCurrentTenantId(),
            OwnerId = GetCurrentUserId(),
            TargetCompanyId = targetCompanyId,
            JobTypeId = jobTypeId,
            Name = name,
            IsDeleted = false,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.DeskTeamViews.Add(view);
        await _db.SaveChangesAsync();
        return view;
    }

    public async Task RenameAsync(int id, string name)
    {
        var view = await FindOwnedAsync(id);
        view.Name = name;
        view.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var view = await FindOwnedAsync(id);
        view.IsDeleted = true;
        view.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    /// <summary>Looks up a view by Id, scoped to the current user as owner. Throws if not found/not owned.</summary>
    private async Task<DeskTeamView> FindOwnedAsync(int id)
    {
        var ownerId = GetCurrentUserId();
        var view = await _db.DeskTeamViews
            .FirstOrDefaultAsync(v => v.Id == id && v.OwnerId == ownerId && !v.IsDeleted);
        if (view == null)
            throw new InvalidOperationException($"DeskTeamView {id} not found.");
        return view;
    }
}
