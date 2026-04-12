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
        var notificationsRaw = await (from n in _db.UserNotifications.IgnoreQueryFilters()
                                      join c in _db.Companies.IgnoreQueryFilters() on n.CompanyId equals c.Id
                                      where companyIds.Contains(n.CompanyId)
                                      orderby n.CreatedAt descending
                                      select new
                                      {
                                          n.Id,
                                          n.Type,
                                          n.Message,
                                          n.CreatedAt,
                                          n.IsRead,
                                          CompanyName = c.Name,
                                          CompanyDisplayName = c.DisplayName,
                                          CompanyNameHe = c.NameHe,
                                          CompanySlug = c.Slug
                                      })
                                      .Take(50)
                                      .ToListAsync();
        RecentNotifications = notificationsRaw.Select(r => new NotificationVM(
            r.Id, r.Type.ToString(), r.Message, r.CreatedAt, r.IsRead,
            Models.Company.ResolveLocalizedName(r.CompanyName, r.CompanyDisplayName, r.CompanyNameHe),
            r.CompanySlug ?? ""
        )).ToList();

        // Get pending time-off requests
        // IgnoreQueryFilters: Director manages multiple companies — tenant filter restricts to home company
        var timeOffRaw = await (from t in _db.TimeOffRequests.IgnoreQueryFilters()
                                join u in _db.Users.IgnoreQueryFilters() on t.UserId equals u.Id
                                join c in _db.Companies.IgnoreQueryFilters() on t.CompanyId equals c.Id
                                where companyIds.Contains(t.CompanyId) && t.Status == Models.Support.RequestStatus.Pending
                                orderby t.StartDate
                                select new
                                {
                                    t.Id,
                                    UserName = u.DisplayName,
                                    t.StartDate,
                                    t.EndDate,
                                    t.Reason,
                                    t.CreatedAt,
                                    CompanyName = c.Name,
                                    CompanyDisplayName = c.DisplayName,
                                    CompanyNameHe = c.NameHe,
                                    CompanySlug = c.Slug
                                })
                                .ToListAsync();
        PendingTimeOffRequests = timeOffRaw.Select(t => new PendingRequestVM(
            t.Id,
            "TimeOff",
            t.UserName,
            $"{t.StartDate:yyyy-MM-dd} to {t.EndDate:yyyy-MM-dd}: {t.Reason}",
            t.CreatedAt,
            Models.Company.ResolveLocalizedName(t.CompanyName, t.CompanyDisplayName, t.CompanyNameHe),
            t.CompanySlug ?? ""
        )).ToList();

        // Get pending swap requests
        // IgnoreQueryFilters: Director manages multiple companies — tenant filter restricts to home company
        var swapRaw = await (from s in _db.SwapRequests.IgnoreQueryFilters()
                             join sa in _db.ShiftAssignments.IgnoreQueryFilters() on s.FromAssignmentId equals sa.Id
                             join fromUser in _db.Users.IgnoreQueryFilters() on sa.UserId equals fromUser.Id
                             join toUser in _db.Users.IgnoreQueryFilters() on s.ToUserId equals toUser.Id
                             join c in _db.Companies.IgnoreQueryFilters() on s.CompanyId equals c.Id
                             where companyIds.Contains(s.CompanyId) && s.Status == Models.Support.RequestStatus.Pending
                             orderby s.CreatedAt descending
                             select new
                             {
                                 s.Id,
                                 FromUserName = fromUser.DisplayName,
                                 ToUserName = toUser.DisplayName,
                                 s.CreatedAt,
                                 CompanyName = c.Name,
                                 CompanyDisplayName = c.DisplayName,
                                 CompanyNameHe = c.NameHe,
                                 CompanySlug = c.Slug
                             })
                             .ToListAsync();
        PendingSwapRequests = swapRaw.Select(s => new PendingRequestVM(
            s.Id,
            "Swap",
            s.FromUserName,
            $"Swap with {s.ToUserName}",
            s.CreatedAt,
            Models.Company.ResolveLocalizedName(s.CompanyName, s.CompanyDisplayName, s.CompanyNameHe),
            s.CompanySlug ?? ""
        )).ToList();
    }
}
