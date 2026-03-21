using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using System.Security.Claims;

namespace ShiftManager.ViewComponents;

public class UnreadNotificationCountViewComponent : ViewComponent
{
    private readonly AppDbContext _db;

    public UnreadNotificationCountViewComponent(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var claimsPrincipal = User as ClaimsPrincipal;
        if (claimsPrincipal?.Identity?.IsAuthenticated != true)
        {
            return Content("");
        }

        if (!int.TryParse(claimsPrincipal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
        {
            return Content("");  // Graceful fallback — no notification count shown
        }

        try
        {
            var unreadCount = await _db.UserNotifications
                .CountAsync(n => n.UserId == userId && !n.IsRead);

            return View(unreadCount);
        }
        catch
        {
            return Content("");
        }
    }
}