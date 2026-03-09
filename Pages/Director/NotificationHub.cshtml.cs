using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Services;

namespace ShiftManager.Pages.Director;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:DirectorHubAccess policy;
// cross-company queries by design — Directors view notifications/requests across their hierarchy
[Authorize(Policy = "Grant:DirectorHubAccess")]
public class NotificationHubModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IDirectorService _directorService;

    public NotificationHubModel(AppDbContext db, IDirectorService directorService)
    {
        _db = db;
        _directorService = directorService;
    }

    public record NotificationVM(
        int Id,
        string Type,
        string Message,
        DateTime CreatedAt,
        bool IsRead,
        string CompanyName,
        string CompanySlug);

    public record PendingRequestVM(
        int Id,
        string Type,
        string UserName,
        string Details,
        DateTime CreatedAt,
        string CompanyName,
        string CompanySlug);

    public List<NotificationVM> RecentNotifications { get; set; } = new();
    public List<PendingRequestVM> PendingTimeOffRequests { get; set; } = new();
    public List<PendingRequestVM> PendingSwapRequests { get; set; } = new();

    public async Task OnGetAsync()
    {
        var companyIds = await _directorService.GetDirectorCompanyIdsAsync();

        if (!companyIds.Any())
            return;

        // Get recent notifications across all assigned companies
        // IgnoreQueryFilters: Director manages multiple companies — tenant filter restricts to home company
        var notificationsQuery = await (from n in _db.UserNotifications.IgnoreQueryFilters()
                                        join c in _db.Companies.IgnoreQueryFilters() on n.CompanyId equals c.Id
                                        where companyIds.Contains(n.CompanyId)
                                        orderby n.CreatedAt descending
                                        select new NotificationVM(
                                            n.Id,
                                            n.Type.ToString(),
                                            n.Message,
                                            n.CreatedAt,
                                            n.IsRead,
                                            c.DisplayName ?? c.Name,
                                            c.Slug ?? ""
                                        ))
                                        .Take(50)
                                        .ToListAsync();
        RecentNotifications = notificationsQuery;

        // Get pending time-off requests
        // IgnoreQueryFilters: Director manages multiple companies — tenant filter restricts to home company
        var timeOffQuery = await (from t in _db.TimeOffRequests.IgnoreQueryFilters()
                                  join u in _db.Users.IgnoreQueryFilters() on t.UserId equals u.Id
                                  join c in _db.Companies.IgnoreQueryFilters() on t.CompanyId equals c.Id
                                  where companyIds.Contains(t.CompanyId) && t.Status == Models.Support.RequestStatus.Pending
                                  orderby t.StartDate
                                  select new PendingRequestVM(
                                      t.Id,
                                      "TimeOff",
                                      u.DisplayName,
                                      $"{t.StartDate:yyyy-MM-dd} to {t.EndDate:yyyy-MM-dd}: {t.Reason}",
                                      t.CreatedAt,
                                      c.DisplayName ?? c.Name,
                                      c.Slug ?? ""
                                  ))
                                  .ToListAsync();
        PendingTimeOffRequests = timeOffQuery;

        // Get pending swap requests
        // IgnoreQueryFilters: Director manages multiple companies — tenant filter restricts to home company
        var swapQuery = await (from s in _db.SwapRequests.IgnoreQueryFilters()
                               join sa in _db.ShiftAssignments.IgnoreQueryFilters() on s.FromAssignmentId equals sa.Id
                               join fromUser in _db.Users.IgnoreQueryFilters() on sa.UserId equals fromUser.Id
                               join toUser in _db.Users.IgnoreQueryFilters() on s.ToUserId equals toUser.Id
                               join c in _db.Companies.IgnoreQueryFilters() on s.CompanyId equals c.Id
                               where companyIds.Contains(s.CompanyId) && s.Status == Models.Support.RequestStatus.Pending
                               orderby s.CreatedAt descending
                               select new PendingRequestVM(
                                   s.Id,
                                   "Swap",
                                   fromUser.DisplayName,
                                   $"Swap with {toUser.DisplayName}",
                                   s.CreatedAt,
                                   c.DisplayName ?? c.Name,
                                   c.Slug ?? ""
                               ))
                               .ToListAsync();
        PendingSwapRequests = swapQuery;
    }
}
