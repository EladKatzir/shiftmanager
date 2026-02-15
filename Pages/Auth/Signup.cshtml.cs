using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.ComponentModel.DataAnnotations;

namespace ShiftManager.Pages.Auth;

[AllowAnonymous]
public class SignupModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<SignupModel> _logger;
    private readonly IConfiguration _configuration;
    private readonly IValidationService _validation;
    private readonly INotificationService _notificationService;

    public SignupModel(
        AppDbContext db,
        ILogger<SignupModel> logger,
        IStringLocalizer<SharedResources> localizer,
        IConfiguration configuration,
        IValidationService validation,
        INotificationService notificationService)
        : base(localizer)
    {
        _db = db;
        _logger = logger;
        _configuration = configuration;
        _validation = validation;
        _notificationService = notificationService;
    }

    [BindProperty, Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [BindProperty, Required]
    public string DisplayName { get; set; } = string.Empty;

    [BindProperty, Required, MinLength(6)]
    public string Password { get; set; } = string.Empty;

    [BindProperty]
    public int? MoleculeId { get; set; }

    [BindProperty, Required]
    public int CompanyId { get; set; }

    [BindProperty, Required]
    public int JobTypeId { get; set; }

    [BindProperty, Required]
    public UserRole RequestedRole { get; set; } = UserRole.Employee;

    public List<Company> AvailableCompanies { get; set; } = new();
    public List<Molecule> AvailableMolecules { get; set; } = new();
    public string? PendingRequestMessage { get; set; }
    public bool IsPublicSignupEnabled { get; set; }

    public async Task OnGetAsync()
    {
        // SECURITY FIX: Only load data if public signup is explicitly enabled
        IsPublicSignupEnabled = _configuration.GetValue<bool>("Features:AllowPublicSignup", false);
        if (IsPublicSignupEnabled)
        {
            _logger.LogWarning("Public signup is enabled - this exposes organizational structure");
            // Load molecules for the cascade - companies will be loaded via API
            AvailableMolecules = await _db.Molecules
                .Where(m => m.IsActive)
                .OrderBy(m => m.DisplayName ?? m.Name)
                .ToListAsync();

            // Also load companies for backward compatibility / server-side fallback
            AvailableCompanies = await _db.Companies
                .OrderBy(c => c.Name)
                .ToListAsync();
        }
        else
        {
            // Production: signup disabled or requires invite code
            AvailableCompanies = new List<Company>();
            AvailableMolecules = new List<Molecule>();
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // SECURITY FIX: Only allow if public signup is explicitly enabled
        IsPublicSignupEnabled = _configuration.GetValue<bool>("Features:AllowPublicSignup", false);
        if (IsPublicSignupEnabled)
        {
            AvailableMolecules = await _db.Molecules
                .Where(m => m.IsActive)
                .OrderBy(m => m.DisplayName ?? m.Name)
                .ToListAsync();

            AvailableCompanies = await _db.Companies
                .OrderBy(c => c.Name)
                .ToListAsync();
        }
        else
        {
            Error = _localizer["Error_Signup_PublicDisabled"];
            return Page();
        }

        if (!ModelState.IsValid)
        {
            Error = _localizer["Error_Signup_RequiredFields"];
            return Page();
        }

        // ✅ SECURITY FIX: Additional input validation beyond data annotations
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(DisplayName) || string.IsNullOrWhiteSpace(Password))
        {
            Error = _localizer["Error_Signup_AllFieldsRequired"];
            return Page();
        }

        if (Email.Length > 255)
        {
            Error = _localizer["Error_Signup_EmailTooLong"];
            return Page();
        }

        if (DisplayName.Length > 200)
        {
            Error = _localizer["Error_Signup_DisplayNameTooLong"];
            return Page();
        }

        if (Password.Length > 128)
        {
            Error = _localizer["Error_Signup_PasswordTooLong"];
            return Page();
        }

        if (CompanyId <= 0)
        {
            Error = _localizer["Error_Signup_SelectValidCompany"];
            return Page();
        }

        // ✅ SECURITY FIX: Proper email format validation with regex
        if (!_validation.IsValidEmail(Email))
        {
            Error = _localizer["Error_InvalidEmailFormat"];
            return Page();
        }

        // Check if user already exists
        // IgnoreQueryFilters: anonymous user has no tenant context (CompanyId=0),
        // so the query filter would skip all real users — check across all companies
        if (await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == Email))
        {
            Error = _localizer["Error_Signup_EmailExists"];
            return Page();
        }

        // Check for existing pending request with same email, company, and role
        // IgnoreQueryFilters: same reason — anonymous user has no tenant context
        var existingPendingRequest = await _db.UserJoinRequests
            .IgnoreQueryFilters()
            .Include(jr => jr.Company)
            .FirstOrDefaultAsync(jr =>
                jr.Email == Email &&
                jr.CompanyId == CompanyId &&
                jr.RequestedRole == RequestedRole &&
                jr.Status == JoinRequestStatus.Pending);

        if (existingPendingRequest != null)
        {
            var company = await _db.Companies.FindAsync(CompanyId);
            PendingRequestMessage = _localizer["SignupPendingMessage", company?.Name ?? "", _localizer[RequestedRole.ToString()].Value];
            return Page();
        }

        // Validate company exists
        var selectedCompany = await _db.Companies.FindAsync(CompanyId);
        if (selectedCompany == null)
        {
            Error = _localizer["Error_Signup_CompanyNotFound"];
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
            JobTypeId = JobTypeId,
            RequestedRole = RequestedRole,
            Status = JoinRequestStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            _db.UserJoinRequests.Add(joinRequest);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save join request for {Email}", Email);
            Error = _localizer["Error_AnErrorOccurred"];
            return Page();
        }

        _logger.LogInformation("New join request created: {Email} requesting {Role} at {Company}",
            Email, RequestedRole, selectedCompany.Name);

        // Notify all owners about the new access request (fire-and-forget with error handling)
        _ = Task.Run(async () =>
        {
            try
            {
                await _notificationService.NotifyOwnersOfAccessRequestAsync(
                    DisplayName,
                    Email,
                    selectedCompany.Name,
                    joinRequest.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to notify owners about join request {RequestId}", joinRequest.Id);
            }
        });

        PendingRequestMessage = _localizer["SignupSubmittedMessage", selectedCompany.Name, _localizer[RequestedRole.ToString()].Value];

        // Clear form fields
        Email = string.Empty;
        DisplayName = string.Empty;
        Password = string.Empty;
        CompanyId = 0;
        RequestedRole = UserRole.Employee;

        return Page();
    }
}
