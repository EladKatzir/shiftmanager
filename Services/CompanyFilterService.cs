using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

public class CompanyFilterService : ICompanyFilterService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly AppDbContext _db;
    private readonly IDirectorService _directorService;
    private const string FilterCookieName = "director_company_filter";

    public CompanyFilterService(
        IHttpContextAccessor httpContextAccessor,
        AppDbContext db,
        IDirectorService directorService)
    {
        _httpContextAccessor = httpContextAccessor;
        _db = db;
        _directorService = directorService;
    }

    private ClaimsPrincipal? CurrentUser => _httpContextAccessor.HttpContext?.User;

    private int? CurrentUserId
    {
        get
        {
            var userIdClaim = CurrentUser?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdClaim, out var uid) ? uid : null;
        }
    }

    public async Task<List<int>> GetSelectedCompanyIdsAsync()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
            return new List<int>();

        // Read from cookie
        if (httpContext.Request.Cookies.TryGetValue(FilterCookieName, out var filterValue)
            && !string.IsNullOrEmpty(filterValue))
        {
            var companyIds = filterValue.Split(',')
                .Where(s => int.TryParse(s, out _))
                .Select(int.Parse)
                .ToList();

            // Validate that user has access to these companies
            var accessible = await GetAccessibleCompanyIdsAsync();
            return companyIds.Where(id => accessible.Contains(id)).ToList();
        }

        // No filter set - return empty (meaning show all)
        return new List<int>();
    }

    public async Task SetSelectedCompanyIdsAsync(List<int> companyIds)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
            return;

        // Validate that user has access to these companies
        var accessible = await GetAccessibleCompanyIdsAsync();
        var validIds = companyIds.Where(id => accessible.Contains(id)).ToList();

        if (validIds.Any())
        {
            var filterValue = string.Join(",", validIds);
            httpContext.Response.Cookies.Append(FilterCookieName, filterValue, new CookieOptions
            {
                MaxAge = TimeSpan.FromDays(30),
                HttpOnly = true,
                SameSite = SameSiteMode.Strict
            });
        }
        else
        {
            await ClearFilterAsync();
        }
    }

    public Task ClearFilterAsync()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext != null)
        {
            httpContext.Response.Cookies.Delete(FilterCookieName);
        }
        return Task.CompletedTask;
    }

    public async Task<List<int>> GetAccessibleCompanyIdsAsync()
    {
        if (CurrentUserId == null)
            return new List<int>();

        // Owner can access all companies
        if (CurrentUser?.IsInRole(nameof(UserRole.Owner)) ?? false)
        {
            return await _db.Companies.Select(c => c.Id).ToListAsync();
        }

        // Director can access their assigned companies
        if (_directorService.IsDirector())
        {
            return await _directorService.GetDirectorCompanyIdsAsync();
        }

        // Manager can access their company
        if (CurrentUser?.IsInRole(nameof(UserRole.Manager)) ?? false)
        {
            var user = await _db.Users.FindAsync(CurrentUserId.Value);
            return user != null ? new List<int> { user.CompanyId } : new List<int>();
        }

        // Employee can access their company
        var employeeUser = await _db.Users.FindAsync(CurrentUserId.Value);
        return employeeUser != null ? new List<int> { employeeUser.CompanyId } : new List<int>();
    }
}
