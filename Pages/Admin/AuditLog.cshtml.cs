using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Linq;

namespace ShiftManager.Pages.Admin;

[Authorize(Policy = "IsManagerOrAdmin")]
public class AuditLogModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly ITenantResolver _tenantResolver;
    private readonly ILogger<AuditLogModel> _logger;

    public AuditLogModel(IStringLocalizer<SharedResources> localizer, AppDbContext db, ITenantResolver tenantResolver, ILogger<AuditLogModel> logger)
        : base(localizer)
    {
        _db = db;
        _tenantResolver = tenantResolver;
        _logger = logger;
    }

    public List<AuditLog> AuditLogs { get; set; } = new();
    public int CurrentPage { get; set; } = 1;
    public int TotalPages { get; set; }
    public int TotalRecords { get; set; }
    public int PageSize { get; set; } = 50;

    // Filters
    [BindProperty(SupportsGet = true)]
    public DateTime? StartDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? EndDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? UserId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Action { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? EntityType { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    // Lists for dropdowns
    public List<AppUser> Users { get; set; } = new();
    public List<string> Actions { get; set; } = new();
    public List<string> EntityTypes { get; set; } = new();

    public async Task OnGetAsync()
    {
        try
        {
            CurrentPage = PageNumber;

            // Load dropdown data
            Users = await _db.Users
                .AsNoTracking()
                .Where(u => u.IsActive)
                .OrderBy(u => u.DisplayName)
                .ToListAsync();

            Actions = await _db.AuditLogs
                .AsNoTracking()
                .Select(a => a.Action)
                .Distinct()
                .OrderBy(a => a)
                .ToListAsync();

            EntityTypes = await _db.AuditLogs
                .AsNoTracking()
                .Select(a => a.EntityType)
                .Distinct()
                .OrderBy(e => e)
                .ToListAsync();

            // Build query with filters
            var query = _db.AuditLogs
                .AsNoTracking()
                .Include(a => a.User)
                .AsQueryable();

            if (StartDate.HasValue)
            {
                query = query.Where(a => a.Timestamp >= StartDate.Value);
            }

            if (EndDate.HasValue)
            {
                var endOfDay = EndDate.Value.Date.AddDays(1).AddTicks(-1);
                query = query.Where(a => a.Timestamp <= endOfDay);
            }

            if (UserId.HasValue)
            {
                query = query.Where(a => a.UserId == UserId.Value);
            }

            if (!string.IsNullOrWhiteSpace(Action))
            {
                query = query.Where(a => a.Action == Action);
            }

            if (!string.IsNullOrWhiteSpace(EntityType))
            {
                query = query.Where(a => a.EntityType == EntityType);
            }

            if (!string.IsNullOrWhiteSpace(SearchTerm))
            {
                query = query.Where(a => a.Description.Contains(SearchTerm) ||
                                        a.UserDisplayName.Contains(SearchTerm) ||
                                        a.UserEmail.Contains(SearchTerm));
            }

            // Get total count
            TotalRecords = await query.CountAsync();
            TotalPages = (int)Math.Ceiling((double)TotalRecords / PageSize);

            // Get paginated results
            AuditLogs = await query
                .OrderByDescending(a => a.Timestamp)
                .Skip((CurrentPage - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading audit logs");
            TempData["Error"] = _localizer["Error_LoadingAuditLogs"].Value;
        }
    }

    public async Task<IActionResult> OnGetExportCsvAsync()
    {
        try
        {
            // Build query with filters (same as OnGetAsync)
            var query = _db.AuditLogs
                .AsNoTracking()
                .Include(a => a.User)
                .AsQueryable();

            if (StartDate.HasValue)
            {
                query = query.Where(a => a.Timestamp >= StartDate.Value);
            }

            if (EndDate.HasValue)
            {
                var endOfDay = EndDate.Value.Date.AddDays(1).AddTicks(-1);
                query = query.Where(a => a.Timestamp <= endOfDay);
            }

            if (UserId.HasValue)
            {
                query = query.Where(a => a.UserId == UserId.Value);
            }

            if (!string.IsNullOrWhiteSpace(Action))
            {
                query = query.Where(a => a.Action == Action);
            }

            if (!string.IsNullOrWhiteSpace(EntityType))
            {
                query = query.Where(a => a.EntityType == EntityType);
            }

            if (!string.IsNullOrWhiteSpace(SearchTerm))
            {
                query = query.Where(a => a.Description.Contains(SearchTerm));
            }

            var logs = await query
                .OrderByDescending(a => a.Timestamp)
                .Take(10000) // Limit to 10,000 records for export
                .ToListAsync();

            // Generate CSV
            var csv = new System.Text.StringBuilder();
            csv.AppendLine("Timestamp,User,Action,Entity Type,Entity ID,Description,IP Address");

            foreach (var log in logs)
            {
                csv.AppendLine($"\"{log.Timestamp:yyyy-MM-dd HH:mm:ss}\",\"{log.UserDisplayName}\",\"{log.Action}\",\"{log.EntityType}\",\"{log.EntityId}\",\"{log.Description.Replace("\"", "\"\"")}\",\"{log.IpAddress}\"");
            }

            var company = await _db.Companies.FindAsync(_tenantResolver.GetCurrentTenantId());
            var fileName = $"AuditLog_{company?.Name.Replace(" ", "_")}_{DateTime.UtcNow:yyyyMMdd}.csv";

            return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting audit logs to CSV");
            TempData["Error"] = _localizer["Error_ExportingAuditLogs"].Value;
            return RedirectToPage();
        }
    }
}
