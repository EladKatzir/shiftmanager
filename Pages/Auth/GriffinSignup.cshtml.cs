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

namespace ShiftManager.Pages.Auth;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — anonymous auth flow before tenant context established;
// scoped by explicit email/companyId parameters; email uniqueness check and company lookup only
[AllowAnonymous]
public class GriffinSignupModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<GriffinSignupModel> _logger;
    private readonly IGriffinService _griffinService;
    private readonly IGriffinConfigService _griffinConfigService;
    private readonly IValidationService _validation;
    private readonly INotificationService _notificationService;
    private readonly IRateLimitingService _rateLimiting;
    private readonly IAuditLogService _auditLogService;
    private readonly ICompanyCacheService _companyCacheService;
    private readonly IRoleService _roleService;
    private readonly IBackgroundTaskQueue _backgroundTaskQueue;

    public GriffinSignupModel(
        AppDbContext db,
        ILogger<GriffinSignupModel> logger,
        IStringLocalizer<SharedResources> localizer,
        IGriffinService griffinService,
        IGriffinConfigService griffinConfigService,
        IValidationService validation,
        INotificationService notificationService,
        IRateLimitingService rateLimiting,
        IAuditLogService auditLogService,
        ICompanyCacheService companyCacheService,
        IRoleService roleService,
        IBackgroundTaskQueue backgroundTaskQueue)
        : base(localizer)
    {
        _db = db;
        _logger = logger;
        _griffinService = griffinService;
        _griffinConfigService = griffinConfigService;
        _validation = validation;
        _notificationService = notificationService;
        _rateLimiting = rateLimiting;
        _auditLogService = auditLogService;
        _companyCacheService = companyCacheService;
        _roleService = roleService;
        _backgroundTaskQueue = backgroundTaskQueue;
    }

    // Email is server-derived from the validated Griffin token on POST — NOT round-tripped
    // through the form, so a tampered hidden field cannot impersonate someone else's identity.
    public string Email { get; set; } = string.Empty;

    // GriffinUniqueID is round-tripped for audit logging only; never participates in identity matching.
    [BindProperty]
    public string GriffinUniqueID { get; set; } = string.Empty;

    // User-editable identity fields (pre-filled from getClaims). DisplayName is synthesized
    // server-side from GivenName + Surname (AppUser has no split first/last name columns).
    [BindProperty]
    public string GivenName { get; set; } = string.Empty;

    [BindProperty]
    public string Surname { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    [BindProperty]
    public int? MoleculeId { get; set; }

    [BindProperty]
    public int CompanyId { get; set; }

    [BindProperty]
    public int? DepartmentId { get; set; }

    [BindProperty]
    public int JobTypeId { get; set; }

    [BindProperty]
    public UserRole RequestedRole { get; set; } = UserRole.Employee;

    [BindProperty]
    public int? RequestedRoleTemplateId { get; set; }

    // Page-level properties (not bound)
    public List<Molecule> AvailableMolecules { get; set; } = new();
    public List<Company> AvailableCompanies { get; set; } = new();
    public string? PendingRequestMessage { get; set; }
    public bool IsGriffinAuthenticated { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        // Read claims from TempData (set by GriffinCallback)
        var email = TempData["GriffinEmail"] as string;
        var displayName = TempData["GriffinDisplayName"] as string;
        var givenName = TempData["GriffinGivenName"] as string;
        var surname = TempData["GriffinSurname"] as string;
        var uniqueId = TempData["GriffinUniqueID"] as string;

        if (string.IsNullOrEmpty(email))
        {
            // No TempData — direct navigation or page refresh
            _logger.LogWarning("GriffinSignup accessed without TempData, redirecting to Login");
            return RedirectToPage("/Auth/Login");
        }

        Email = email;
        GivenName = givenName ?? string.Empty;
        Surname = surname ?? string.Empty;
        DisplayName = displayName ?? string.Empty;
        GriffinUniqueID = uniqueId ?? string.Empty;
        IsGriffinAuthenticated = true;

        // Load signup options server-side (no dependency on GetSignupOptions API gate)
        await LoadPageDataAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // Load page state for form re-render
        await LoadPageDataAsync();
        IsGriffinAuthenticated = true;

        // Rate limiting — per-IP, 50 attempts per 10 minutes
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var ipRateLimitKey = $"griffinsignup:ip:{ipAddress}";
        if (!_rateLimiting.IsAllowed(ipRateLimitKey, 50, 10))
        {
            _logger.LogWarning("Griffin signup rate limit exceeded for IP: {IP}", ipAddress);
            Error = _localizer["Error_Login_RateLimitExceeded"];
            return Page();
        }

        // Re-validate Griffin token (bypass cache to catch expired tokens)
        var griffinToken = Request.Cookies["griffin.token"];
        if (string.IsNullOrEmpty(griffinToken))
        {
            Error = _localizer["Error_GriffinTokenExpired"].Value;
            return Page();
        }

        var griffinConfig = await _griffinConfigService.GetGriffinConfigAsync();
        if (griffinConfig?.Enabled != true || string.IsNullOrEmpty(griffinConfig.BaseUrl))
        {
            Error = _localizer["Error_GriffinNotEnabled"].Value;
            return Page();
        }

        // Validate token AND get claims — needed to cross-check submitted email against token identity.
        // Hidden form fields are client-side and trivially editable; we must verify the email matches the token.
        var claimsResult = await _griffinService.ValidateAndGetClaimsAsync(griffinToken, griffinConfig.BaseUrl, griffinConfig.TimeoutSeconds);
        if (!claimsResult.Success || claimsResult.Value == null)
        {
            _logger.LogWarning("Griffin signup: token validation failed [{ErrorToken}]",
                claimsResult.Error?.ErrorToken ?? "GRIFFIN-UNKNOWN");
            Error = _localizer["Error_GriffinTokenExpired"].Value;
            Response.Cookies.Delete("griffin.token");
            return Page();
        }
        var griffinClaims = claimsResult.Value;

        // Email is identity-bearing — always trust the validated Griffin token claim, NEVER the form.
        // No client-side input renders the email, so a tampered DOM cannot impersonate someone else.
        Email = griffinClaims.EmailAddress;

        // Per-email rate limit — defends against an attacker with a valid Griffin token spinning
        // signup requests across many CompanyId/RequestedRole combinations to flood the owner
        // approval queue. The per-IP limit above doesn't catch this (one valid token can be
        // submitted from many IPs). 3 attempts per hour per email is plenty for any legitimate
        // user fixing a typo'd company selection.
        if (!string.IsNullOrEmpty(Email))
        {
            var emailRateLimitKey = $"griffinsignup:email:{Email.Trim().ToLowerInvariant()}";
            if (!_rateLimiting.IsAllowed(emailRateLimitKey, 3, 60))
            {
                _logger.LogWarning(
                    "Griffin signup per-email rate limit exceeded for {Email} from IP {IP}",
                    Services.PiiMasker.MaskEmail(Email), ipAddress);
                Error = _localizer["Error_Login_RateLimitExceeded"];
                return Page();
            }
        }

        // Synthesize DisplayName from the editable name fields (AppUser has no split first/last columns).
        DisplayName = $"{GivenName} {Surname}".Trim();

        // Validate required fields. GivenName / Surname are user-editable, so they may be empty
        // if the user clears the prefill — block that explicitly with a friendly error.
        if (string.IsNullOrWhiteSpace(Email) ||
            string.IsNullOrWhiteSpace(GivenName) ||
            string.IsNullOrWhiteSpace(Surname))
        {
            Error = _localizer["Error_Signup_AllFieldsRequired"];
            return Page();
        }

        if (Email.Length > 255)
        {
            Error = _localizer["Error_Signup_EmailTooLong"];
            return Page();
        }

        if (DisplayName.Length > 200 || GivenName.Length > 100 || Surname.Length > 100)
        {
            Error = _localizer["Error_Signup_DisplayNameTooLong"];
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

        // Director/AreaAdmin/MoleculeAdmin HQ auto-resolve
        var isJobTypeFreeScope = signupTemplate?.ScopeLevel == RoleScopeLevel.Molecule
                              || signupTemplate?.ScopeLevel == RoleScopeLevel.Area;
        if (RequestedRole == UserRole.Director || RequestedRole == UserRole.AreaAdmin || isJobTypeFreeScope)
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

            if (isJobTypeFreeScope)
            {
                JobTypeId = 0;
            }
        }

        if (CompanyId <= 0)
        {
            Error = _localizer["Error_Signup_SelectValidCompany"];
            return Page();
        }

        if (!_validation.IsValidEmail(Email))
        {
            Error = _localizer["Error_InvalidEmailFormat"];
            return Page();
        }

        // Check if user already exists (case-insensitive, consistent with GriffinCallback).
        // ToLowerInvariant (not ToLower) is load-bearing: a Turkish/Azerbaijani server locale
        // would otherwise lowercase ASCII 'I' to 'ı' (dotless i), breaking matches like
        // "user@ISIK.mil" against "user@isik.mil" stored in the DB.
        if (await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email.ToLowerInvariant() == Email.ToLowerInvariant()))
        {
            Error = _localizer["Error_Signup_EmailExists"];
            return Page();
        }

        // Check for existing pending request
        var selectedCompany = await _companyCacheService.GetCompanyAsync(CompanyId);

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
            PendingRequestMessage = _localizer["SignupPendingMessage", selectedCompany?.LocalizedName ?? "", _localizer[RequestedRole.ToString()].Value];
            return Page();
        }

        if (selectedCompany == null)
        {
            Error = _localizer["Error_Signup_CompanyNotFound"];
            return Page();
        }

        // Create join request — no password for Griffin SSO users
        var joinRequest = new UserJoinRequest
        {
            Email = Email,
            DisplayName = DisplayName,
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>(),
            CompanyId = CompanyId,
            JobTypeId = JobTypeId > 0 ? JobTypeId : null,
            DepartmentId = DepartmentId > 0 ? DepartmentId : null,
            RequestedRole = RequestedRole,
            RequestedRoleTemplateId = signupTemplate?.Id ?? RequestedRoleTemplateId,
            Status = JoinRequestStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            AuthMethod = "Griffin"
        };

        try
        {
            _db.UserJoinRequests.Add(joinRequest);
            await _db.SaveChangesAsync();

            await _auditLogService.LogUserActionAsync(0, "GriffinJoinRequestCreated", "UserJoinRequest", joinRequest.Id,
                $"Griffin join request created by '{joinRequest.Email}' (UniqueID: {GriffinUniqueID}) for role {joinRequest.RequestedRole}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save Griffin join request for {Email}", Email);
            Error = _localizer["Error_AnErrorOccurred"];
            return Page();
        }

        _logger.LogInformation("Griffin join request created: {Email} requesting {Role} at {Company}",
            Email, RequestedRole, selectedCompany.Name);

        // Notify all owners about the new access request (fire-and-forget with scoped service)
        var capturedDisplayName = DisplayName;
        var capturedEmail = Email;
        var capturedCompanyName = selectedCompany.LocalizedName;
        var capturedRequestId = joinRequest.Id;
        var capturedCompanyId = selectedCompany.Id;
        _backgroundTaskQueue.Enqueue(async (sp, _) =>
        {
            try
            {
                var notificationService = sp.GetRequiredService<INotificationService>();
                await notificationService.NotifyOwnersOfAccessRequestAsync(
                    capturedDisplayName,
                    capturedEmail,
                    capturedCompanyName,
                    capturedRequestId,
                    capturedCompanyId);
            }
            catch (Exception ex)
            {
                var logger = sp.GetRequiredService<ILogger<GriffinSignupModel>>();
                logger.LogError(ex, "Failed to notify owners about Griffin join request {RequestId}", capturedRequestId);
            }
        });

        // Clean up Griffin token cookie
        Response.Cookies.Delete("griffin.token");

        PendingRequestMessage = _localizer["SignupSubmittedMessage", selectedCompany.LocalizedName, _localizer[RequestedRole.ToString()].Value];

        return Page();
    }

    private async Task LoadPageDataAsync()
    {
        AvailableMolecules = await _db.Molecules
            .Include(m => m.Area)
            .Where(m => m.IsActive)
            .OrderBy(m => m.DisplayName ?? m.Name)
            .ToListAsync();

        AvailableCompanies = await _db.Companies
            .Where(c => !c.IsHeadquarters)
            .OrderBy(c => c.Name)
            .ToListAsync();
    }
}
