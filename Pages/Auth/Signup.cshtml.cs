using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Data.SeedData;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.ComponentModel.DataAnnotations;

namespace ShiftManager.Pages.Auth;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — anonymous auth flow before tenant context established;
// scoped by explicit email/companyId parameters; email uniqueness check and company lookup only
[AllowAnonymous]
public class SignupModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<SignupModel> _logger;
    private readonly IFeatureFlagService _featureFlagService;
    private readonly IValidationService _validation;
    private readonly INotificationService _notificationService;
    private readonly IRateLimitingService _rateLimiting;

    private readonly ICompanyCacheService _companyCacheService;
    private readonly IRoleService _roleService;

    public SignupModel(
        AppDbContext db,
        ILogger<SignupModel> logger,
        IStringLocalizer<SharedResources> localizer,
        IFeatureFlagService featureFlagService,
        IValidationService validation,
        INotificationService notificationService,
        IRateLimitingService rateLimiting,
        ICompanyCacheService companyCacheService,
        IRoleService roleService)
        : base(localizer)
    {
        _db = db;
        _logger = logger;
        _featureFlagService = featureFlagService;
        _validation = validation;
        _notificationService = notificationService;
        _rateLimiting = rateLimiting;
        _companyCacheService = companyCacheService;
        _roleService = roleService;
    }

    [BindProperty, Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [BindProperty, Required]
    public string DisplayName { get; set; } = string.Empty;

    [BindProperty, Required, MinLength(6)]
    public string Password { get; set; } = string.Empty;

    [BindProperty]
    public int? MoleculeId { get; set; }

    [BindProperty]
    public int CompanyId { get; set; }

    [BindProperty]
    public int? DepartmentId { get; set; }

    [BindProperty]
    public int JobTypeId { get; set; }

    [BindProperty, Required]
    public UserRole RequestedRole { get; set; } = UserRole.Employee;

    [BindProperty]
    public int? RequestedRoleTemplateId { get; set; }

    public List<Company> AvailableCompanies { get; set; } = new();
    public List<Molecule> AvailableMolecules { get; set; } = new();
    public string? PendingRequestMessage { get; set; }
    public bool IsPublicSignupEnabled { get; set; }

    public async Task OnGetAsync()
    {
        // SECURITY FIX: Only load data if public signup is explicitly enabled
        IsPublicSignupEnabled = await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.AllowPublicSignup);
        if (IsPublicSignupEnabled)
        {
            _logger.LogWarning("Public signup is enabled - this exposes organizational structure");
            // Load molecules for the cascade - companies will be loaded via API
            AvailableMolecules = await _db.Molecules
                .Where(m => m.IsActive)
                .OrderBy(m => m.DisplayName ?? m.Name)
                .ToListAsync();

            // Also load companies for backward compatibility / server-side fallback (exclude HQ)
            AvailableCompanies = await _db.Companies
                .Where(c => !c.IsHeadquarters)
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
        // Load page state BEFORE rate limit check so the form re-renders properly when rate-limited (Bug 4 fix)
        IsPublicSignupEnabled = await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.AllowPublicSignup);
        if (IsPublicSignupEnabled)
        {
            AvailableMolecules = await _db.Molecules
                .Where(m => m.IsActive)
                .OrderBy(m => m.DisplayName ?? m.Name)
                .ToListAsync();

            AvailableCompanies = await _db.Companies
                .Where(c => !c.IsHeadquarters)
                .OrderBy(c => c.Name)
                .ToListAsync();
        }
        else
        {
            Error = _localizer["Error_Signup_PublicDisabled"];
            return Page();
        }

        // Rate limiting — per-IP, 50 attempts per 10 minutes (Bug 3 fix: relaxed from 5/15min)
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var ipRateLimitKey = $"signup:ip:{ipAddress}";

        if (!_rateLimiting.IsAllowed(ipRateLimitKey, 50, 10))
        {
            _logger.LogWarning("Signup rate limit exceeded for IP: {IP}", ipAddress);
            Error = _localizer["Error_Login_RateLimitExceeded"];
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

        // Resolve role template if provided
        RoleTemplate? signupTemplate = null;
        if (RequestedRoleTemplateId.HasValue)
        {
            var candidate = await _roleService.GetRoleTemplateAsync(RequestedRoleTemplateId.Value);
            if (candidate?.IsVisibleInSignup == true)
                signupTemplate = candidate;
            if (signupTemplate?.DerivedUserRole.HasValue == true)
                RequestedRole = signupTemplate.DerivedUserRole.Value;
        }

        // Resolve molecule type for Tech-aware signup
        MoleculeType? moleculeType = null;
        if (MoleculeId.HasValue && MoleculeId.Value > 0)
        {
            var molecule = await _db.Molecules.FirstOrDefaultAsync(m => m.Id == MoleculeId.Value);
            moleculeType = molecule?.Type;
        }

        // Director/AreaAdmin HQ auto-resolve: get assigned to the molecule's HQ company
        if (RequestedRole == UserRole.Director || RequestedRole == UserRole.AreaAdmin)
        {
            if (!MoleculeId.HasValue || MoleculeId.Value <= 0)
            {
                Error = _localizer["Error_Signup_SelectMolecule"];
                return Page();
            }

            var hqCompany = await _db.Companies
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.MoleculeId == MoleculeId.Value && c.IsHeadquarters);

            if (hqCompany == null)
            {
                Error = _localizer["Error_Signup_HQNotFound"];
                return Page();
            }

            CompanyId = hqCompany.Id;
        }
        // Tech molecule: require department selection and auto-assign HQ company
        else if (moleculeType == MoleculeType.Tech && (!DepartmentId.HasValue || DepartmentId.Value <= 0))
        {
            Error = _localizer["Error_Signup_SelectDepartment"];
            return Page();
        }
        else if (moleculeType == MoleculeType.Tech && DepartmentId.HasValue && DepartmentId.Value > 0)
        {
            // Validate department exists and belongs to this molecule
            var dept = await _db.Departments.FirstOrDefaultAsync(d => d.Id == DepartmentId.Value && d.MoleculeId == MoleculeId!.Value);
            if (dept == null)
            {
                Error = _localizer["Error_Signup_InvalidDepartment"];
                return Page();
            }

            var hqCompany = await _db.Companies
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.MoleculeId == MoleculeId!.Value && c.IsHeadquarters);

            if (hqCompany == null)
            {
                Error = _localizer["Error_Signup_HQNotFound"];
                return Page();
            }

            CompanyId = hqCompany.Id;
            // Clear JobTypeId — tech users don't have job types
            JobTypeId = 0;
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

        // Pre-fetch company from cache (used for both pending-request message and existence validation)
        var selectedCompany = await _companyCacheService.GetCompanyAsync(CompanyId);

        if (existingPendingRequest != null)
        {
            PendingRequestMessage = _localizer["SignupPendingMessage", selectedCompany?.Name ?? "", _localizer[RequestedRole.ToString()].Value];
            return Page();
        }

        // Validate company exists
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
            JobTypeId = JobTypeId > 0 ? JobTypeId : null,
            DepartmentId = DepartmentId > 0 ? DepartmentId : null,
            RequestedRole = RequestedRole,
            RequestedRoleTemplateId = signupTemplate?.Id ?? RequestedRoleTemplateId,
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
                    joinRequest.Id,
                    selectedCompany.Id);
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
