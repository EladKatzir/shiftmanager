using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using System.ComponentModel.DataAnnotations;

namespace ShiftManager.Pages.Auth;

[AllowAnonymous]
public class SignupModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<SignupModel> _logger;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly IConfiguration _configuration;

    public SignupModel(AppDbContext db, ILogger<SignupModel> logger, IStringLocalizer<SharedResources> localizer, IConfiguration configuration)
    {
        _db = db;
        _logger = logger;
        _localizer = localizer;
        _configuration = configuration;
    }

    [BindProperty, Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [BindProperty, Required]
    public string DisplayName { get; set; } = string.Empty;

    [BindProperty, Required, MinLength(12)]
    public string Password { get; set; } = string.Empty;

    [BindProperty, Required]
    public int CompanyId { get; set; }

    [BindProperty, Required]
    public UserRole RequestedRole { get; set; } = UserRole.Employee;

    public List<Company> AvailableCompanies { get; set; } = new();
    public string? Error { get; set; }
    public string? PendingRequestMessage { get; set; }

    public async Task OnGetAsync()
    {
        // SECURITY FIX: Only load companies if public signup is explicitly enabled
        var allowPublicSignup = _configuration.GetValue<bool>("Features:AllowPublicSignup", false);
        if (allowPublicSignup)
        {
            _logger.LogWarning("Public signup with company list is enabled - this exposes all company names");
            AvailableCompanies = await _db.Companies
                .OrderBy(c => c.Name)
                .ToListAsync();
        }
        else
        {
            // Production: signup disabled or requires invite code
            AvailableCompanies = new List<Company>();
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // SECURITY FIX: Only load companies if public signup is explicitly enabled
        var allowPublicSignup = _configuration.GetValue<bool>("Features:AllowPublicSignup", false);
        if (allowPublicSignup)
        {
            AvailableCompanies = await _db.Companies
                .OrderBy(c => c.Name)
                .ToListAsync();
        }
        else
        {
            Error = "Public signup is disabled. Please contact your administrator for an invitation.";
            return Page();
        }

        if (!ModelState.IsValid)
        {
            Error = "Please fill in all required fields.";
            return Page();
        }

        // ✅ SECURITY FIX: Additional input validation beyond data annotations
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(DisplayName) || string.IsNullOrWhiteSpace(Password))
        {
            Error = "All fields are required.";
            return Page();
        }

        if (Email.Length > 255)
        {
            Error = "Email must not exceed 255 characters.";
            return Page();
        }

        if (DisplayName.Length > 200)
        {
            Error = "Display name must not exceed 200 characters.";
            return Page();
        }

        if (Password.Length > 128)
        {
            Error = "Password must not exceed 128 characters.";
            return Page();
        }

        if (CompanyId <= 0)
        {
            Error = "Please select a valid company.";
            return Page();
        }

        // Basic email format validation
        if (!Email.Contains('@') || Email.Length < 3)
        {
            Error = "Invalid email format.";
            return Page();
        }

        // Check if user already exists
        if (await _db.Users.AnyAsync(u => u.Email == Email))
        {
            Error = "An account with this email already exists. Please login instead.";
            return Page();
        }

        // Check for existing pending request with same email, company, and role
        var existingPendingRequest = await _db.UserJoinRequests
            .Include(jr => jr.Company)
            .FirstOrDefaultAsync(jr =>
                jr.Email == Email &&
                jr.CompanyId == CompanyId &&
                jr.RequestedRole == RequestedRole &&
                jr.Status == JoinRequestStatus.Pending);

        if (existingPendingRequest != null)
        {
            var company = await _db.Companies.FindAsync(CompanyId);
            PendingRequestMessage = _localizer["SignupPendingMessage", company?.Name ?? "", RequestedRole.ToString()];
            return Page();
        }

        // Validate company exists
        var selectedCompany = await _db.Companies.FindAsync(CompanyId);
        if (selectedCompany == null)
        {
            Error = "Selected company not found.";
            return Page();
        }

        // Create password hash
        var (hash, salt) = PasswordHasher.CreateHash(Password);

        // Create join request
        var joinRequest = new UserJoinRequest
        {
            Email = Email,
            DisplayName = DisplayName,
            PasswordHash = hash,
            PasswordSalt = salt,
            CompanyId = CompanyId,
            RequestedRole = RequestedRole,
            Status = JoinRequestStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        _db.UserJoinRequests.Add(joinRequest);
        await _db.SaveChangesAsync();

        _logger.LogInformation("New join request created: {Email} requesting {Role} at {Company}",
            Email, RequestedRole, selectedCompany.Name);

        PendingRequestMessage = _localizer["SignupSubmittedMessage", selectedCompany.Name, RequestedRole.ToString()];

        // Clear form fields
        Email = string.Empty;
        DisplayName = string.Empty;
        Password = string.Empty;
        CompanyId = 0;
        RequestedRole = UserRole.Employee;

        return Page();
    }
}
