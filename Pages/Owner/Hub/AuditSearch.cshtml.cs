using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Pages.Owner.Hub;

/// <summary>
/// OwnerHub Audit Log Search - Cross-tenant audit log search with filtering and pagination.
/// Provides entity type, user, date range, and action type filtering with paginated results.
/// </summary>
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — OwnerHub requires Grant:AdminAccess (all ~132 grants)
[Authorize(Policy = "Grant:AdminAccess")]
public class AuditSearchModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<AuditSearchModel> _logger;

    public AuditSearchModel(AppDbContext db, ILogger<AuditSearchModel> logger)
    {
        _db = db;
        _logger = logger;
    }

    // Results
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

    // Dropdown data
    public List<UserOption> Users { get; set; } = new();
    public List<string> Actions { get; set; } = new();
    public List<string> EntityTypes { get; set; } = new();

    public async Task OnGetAsync()
    {
        try
        {
            CurrentPage = PageNumber;

            // Load dropdown data across all tenants
            Users = await _db.Users
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(u => u.IsActive)
                .OrderBy(u => u.DisplayName)
                .Select(u => new UserOption { Id = u.Id, DisplayName = u.DisplayName })
                .ToListAsync();

            Actions = await _db.AuditLogs
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Select(a => a.Action)
                .Distinct()
                .OrderBy(a => a)
                .ToListAsync();

            EntityTypes = await _db.AuditLogs
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Select(a => a.EntityType)
                .Distinct()
                .OrderBy(e => e)
                .ToListAsync();

            // Build query with filters (cross-tenant)
            var query = _db.AuditLogs
                .IgnoreQueryFilters()
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
                                        a.UserEmail.Contains(SearchTerm) ||
                                        (a.Details != null && a.Details.Contains(SearchTerm)));
            }

            // Get total count
            TotalRecords = await query.CountAsync();
            TotalPages = (int)Math.Ceiling((double)TotalRecords / PageSize);

            // Clamp current page
            if (CurrentPage < 1) CurrentPage = 1;
            if (CurrentPage > TotalPages && TotalPages > 0) CurrentPage = TotalPages;

            // Get paginated results
            AuditLogs = await query
                .OrderByDescending(a => a.Timestamp)
                .Skip((CurrentPage - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading audit search results");
        }
    }

    public async Task<IActionResult> OnGetExportCsvAsync()
    {
        try
        {
            var query = _db.AuditLogs
                .IgnoreQueryFilters()
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

            var logs = await query
                .OrderByDescending(a => a.Timestamp)
                .Take(10000)
                .ToListAsync();

            var csv = new System.Text.StringBuilder();
            csv.AppendLine("Timestamp,User,Email,Action,Entity Type,Entity ID,Description,IP Address,Company ID");

            foreach (var log in logs)
            {
                csv.AppendLine($"\"{SanitizeCsvField(log.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"))}\",\"{SanitizeCsvField(log.UserDisplayName)}\",\"{SanitizeCsvField(log.UserEmail)}\",\"{SanitizeCsvField(log.Action)}\",\"{SanitizeCsvField(log.EntityType)}\",\"{log.EntityId}\",\"{SanitizeCsvField(log.Description)}\",\"{SanitizeCsvField(log.IpAddress)}\",\"{log.CompanyId}\"");
            }

            var fileName = $"AuditSearch_Export_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";

            // Add UTF-8 BOM for Hebrew Excel compatibility
            var preamble = System.Text.Encoding.UTF8.GetPreamble();
            var csvBytes = System.Text.Encoding.UTF8.GetBytes(csv.ToString());
            var bomResult = new byte[preamble.Length + csvBytes.Length];
            preamble.CopyTo(bomResult, 0);
            csvBytes.CopyTo(bomResult, preamble.Length);
            return File(bomResult, "text/csv", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting audit search to CSV");
            return RedirectToPage();
        }
    }

    /// <summary>
    /// Sanitizes a field value for CSV export to prevent formula injection.
    /// Values starting with =, +, -, @, tab, or CR are prefixed with a single quote.
    /// </summary>
    private static string SanitizeCsvField(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        // Escape double quotes for CSV
        value = value.Replace("\"", "\"\"");

        // Prevent CSV formula injection: prefix with single quote if value starts with dangerous chars
        if (value.Length > 0 && (value[0] == '=' || value[0] == '+' || value[0] == '-' || value[0] == '@'
            || value[0] == '\t' || value[0] == '\r'))
        {
            value = "'" + value;
        }

        return value;
    }

    // Lightweight view model for user dropdown
    public class UserOption
    {
        public int Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
    }
}
