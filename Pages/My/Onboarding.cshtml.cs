using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using System.Security.Claims;

namespace ShiftManager.Pages.My;

[Authorize]
public class OnboardingModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<OnboardingModel> _logger;

    public OnboardingModel(AppDbContext db, ILogger<OnboardingModel> logger)
    {
        _db = db;
        _logger = logger;
    }

    public string UserDisplayName { get; set; } = "";
    public string UserRole { get; set; } = "";

    public void OnGet()
    {
        ViewData["Title"] = "Welcome";
        UserDisplayName = User.FindFirst(ClaimTypes.Name)?.Value ?? "";
        UserRole = User.FindFirst(ClaimTypes.Role)?.Value ?? "";
    }

    public async Task<IActionResult> OnPostCompleteAsync()
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
        {
            return RedirectToPage("/Auth/Login");
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user != null)
        {
            user.HasCompletedOnboarding = true;
            await _db.SaveChangesAsync();
            _logger.LogInformation("User {UserId} completed onboarding wizard", userId);
        }

        return Redirect("/");
    }
}
