using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Pages.Admin.Organization.ChoreTypes;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:EditChoreTypes policy;
// chore type / category / eligibility / exemption management is molecule-scoped configuration data; every
// handler re-verifies the target's molecule via IsUserAuthorizedForMoleculeAsync before mutating (IDOR guard).
[Authorize(Policy = "Grant:EditChoreTypes")]
public class IndexModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<IndexModel> _logger;
    private readonly IChoreTypeService _choreTypeService;
    private readonly IChoreCategoryService _choreCategoryService;
    private readonly IChoreEligibilityAdminService _eligibilityAdmin;
    private readonly IGrantService _grantService;
    private readonly ICompanyContext _companyContext;
    private readonly IAuditLogService _auditLogService;

    public IndexModel(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext db,
        ILogger<IndexModel> logger,
        IChoreTypeService choreTypeService,
        IChoreCategoryService choreCategoryService,
        IChoreEligibilityAdminService eligibilityAdmin,
        IGrantService grantService,
        ICompanyContext companyContext,
        IAuditLogService auditLogService) : base(localizer)
    {
        _db = db;
        _logger = logger;
        _choreTypeService = choreTypeService;
        _choreCategoryService = choreCategoryService;
        _eligibilityAdmin = eligibilityAdmin;
        _grantService = grantService;
        _companyContext = companyContext;
        _auditLogService = auditLogService;
    }

    // View Models
    public record ChoreTypeVM(int Id, string Name, string DisplayName, string? Color, int SortOrder, string MoleculeName,
        bool IsActive, int ChoreCount, string? NameEn, string? NameHe,
        int? ChoreCategoryId, int? DefaultWeightMinutes, IReadOnlyList<string> EligibilityChipKeys);
    public record ChoreCategoryVM(int Id, string Name, string DisplayName, string? NameEn, string? NameHe, string? Color,
        int SortOrder, bool IsActive, int ChoreTypeCount, int MemberCount);
    public record MoleculeOption(int Id, string Name);
    public record CategoryOption(int Id, string Name);

    // Data
    public List<ChoreTypeVM> ChoreTypes { get; set; } = new();
    public List<ChoreCategoryVM> Categories { get; set; } = new();
    public List<CategoryOption> CategoryOptions { get; set; } = new();
    public List<MoleculeOption> AvailableMolecules { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int? MoleculeId { get; set; }

    // Create form (chore type)
    [BindProperty] public string ChoreTypeName { get; set; } = string.Empty;
    [BindProperty] public string ChoreTypeDisplayName { get; set; } = string.Empty;
    [BindProperty] public string? ChoreTypeNameEn { get; set; }
    [BindProperty] public string? ChoreTypeNameHe { get; set; }
    [BindProperty] public string? ChoreTypeColor { get; set; }
    [BindProperty] public int? ChoreTypeCategoryId { get; set; }
    [BindProperty] public int CreateWeightHours { get; set; }
    [BindProperty] public int CreateWeightMinutes { get; set; }

    // Edit form (chore type)
    [BindProperty] public int EditId { get; set; }
    [BindProperty] public string EditDisplayName { get; set; } = string.Empty;
    [BindProperty] public string? EditNameEn { get; set; }
    [BindProperty] public string? EditNameHe { get; set; }
    [BindProperty] public string? EditColor { get; set; }
    [BindProperty] public int EditSortOrder { get; set; }
    [BindProperty] public int? EditCategoryId { get; set; }
    [BindProperty] public int EditWeightHours { get; set; }
    [BindProperty] public int EditWeightMinutes { get; set; }
    [BindProperty] public int? EditRequiredGender { get; set; }   // 1=Male, 2=Female, null/0=none
    [BindProperty] public bool EditRequiresOfficer { get; set; }

    // Category create form
    [BindProperty] public string CategoryName { get; set; } = string.Empty;
    [BindProperty] public string CategoryDisplayName { get; set; } = string.Empty;
    [BindProperty] public string? CategoryNameEn { get; set; }
    [BindProperty] public string? CategoryNameHe { get; set; }
    [BindProperty] public string? CategoryColor { get; set; }

    // Category edit form
    [BindProperty] public int CategoryEditId { get; set; }
    [BindProperty] public string CategoryEditDisplayName { get; set; } = string.Empty;
    [BindProperty] public string? CategoryEditNameEn { get; set; }
    [BindProperty] public string? CategoryEditNameHe { get; set; }
    [BindProperty] public string? CategoryEditColor { get; set; }

    public async Task OnGetAsync()
    {
        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId)) return;

        var companyId = _companyContext.CompanyId;
        if (!companyId.HasValue) return;

        var userCompany = await _db.Companies
            .Include(c => c.Molecule)
            .FirstOrDefaultAsync(c => c.Id == companyId.Value);
        var userMoleculeId = userCompany?.MoleculeId;

        // Load accessible molecules via grants + user's own molecule as fallback
        var moleculeIds = new HashSet<int>(
            await _grantService.GetAccessibleMoleculeIdsForGrantAsync(currentUserId, "EditChoreTypes"));

        if (userMoleculeId.HasValue)
            moleculeIds.Add(userMoleculeId.Value);

        // SECURITY-AUDITED: SAFE — filtered to only molecules the user has grant access to
        AvailableMolecules = await _db.Molecules
            .IgnoreQueryFilters()
            .Where(m => moleculeIds.Contains(m.Id) && m.IsActive)
            .OrderBy(m => m.DisplayName)
            .Select(m => new MoleculeOption(m.Id, m.DisplayName))
            .ToListAsync();

        // Default MoleculeId to user's own molecule if not set
        if (!MoleculeId.HasValue && userMoleculeId.HasValue)
            MoleculeId = userMoleculeId.Value;

        // Validate selected molecule is accessible
        if (MoleculeId.HasValue && !AvailableMolecules.Any(m => m.Id == MoleculeId.Value))
            MoleculeId = userMoleculeId;

        if (MoleculeId.HasValue)
        {
            // SECURITY-AUDITED: SAFE — scoped to specific molecule; includes inactive for admin management
            var choreTypes = await _db.ChoreTypes
                .IgnoreQueryFilters()
                .Where(ct => ct.MoleculeId == MoleculeId.Value)
                .Include(ct => ct.Molecule)
                .OrderBy(ct => ct.SortOrder)
                .ThenBy(ct => ct.DisplayName)
                .ToListAsync();

            // Count active chores per type for delete guard
            var choreCountsByType = await _db.Chores
                .Where(c => c.ChoreTypeId.HasValue && c.CanceledAt == null)
                .GroupBy(c => c.ChoreTypeId!.Value)
                .Select(g => new { ChoreTypeId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ChoreTypeId, x => x.Count);

            var typeVms = new List<ChoreTypeVM>(choreTypes.Count);
            foreach (var ct in choreTypes)
            {
                var rules = await _eligibilityAdmin.GetRulesForChoreTypeAsync(ct.Id);
                var chips = BuildEligibilityChipKeys(rules);
                typeVms.Add(new ChoreTypeVM(
                    ct.Id,
                    ct.Name,
                    ct.DisplayName,
                    ct.Color,
                    ct.SortOrder,
                    ct.Molecule.DisplayName,
                    ct.IsActive,
                    choreCountsByType.GetValueOrDefault(ct.Id, 0),
                    ct.NameEn,
                    ct.NameHe,
                    ct.ChoreCategoryId,
                    ct.DefaultWeightMinutes,
                    chips));
            }
            ChoreTypes = typeVms;

            // Categories (full list incl. inactive for admin) + usage counts.
            var cats = await _choreCategoryService.GetCategoriesForMoleculeAsync(MoleculeId.Value, includeInactive: true);
            var catVms = new List<ChoreCategoryVM>(cats.Count);
            foreach (var c in cats)
            {
                var (typeCount, memberCount) = await _choreCategoryService.GetUsageAsync(c.Id);
                catVms.Add(new ChoreCategoryVM(c.Id, c.Name, c.DisplayName, c.NameEn, c.NameHe, c.Color, c.SortOrder, c.IsActive, typeCount, memberCount));
            }
            Categories = catVms;

            // Active categories for the type editor's dropdown.
            CategoryOptions = (await _choreCategoryService.GetCategoriesForMoleculeAsync(MoleculeId.Value))
                .Select(c => new CategoryOption(c.Id, c.DisplayName)).ToList();
        }
    }

    private static IReadOnlyList<string> BuildEligibilityChipKeys(IEnumerable<EligibilityRule> rules)
    {
        var chips = new List<string>();
        foreach (var r in rules)
        {
            if (r.RuleKind == EligibilityRuleKind.RequiresGender)
            {
                if (r.GenderValue == Gender.Male) chips.Add("Elig_GenderMale");
                else if (r.GenderValue == Gender.Female) chips.Add("Elig_GenderFemale");
            }
            else if (r.RuleKind == EligibilityRuleKind.RequiresOfficerRank)
            {
                chips.Add("Elig_Officer");
            }
        }
        return chips;
    }

    /// <summary>hours+minutes → total minutes; 0/0 → null (type carries no default → chore-create falls to 480).</summary>
    private static int? ToWeightMinutes(int hours, int minutes)
    {
        var total = hours * 60 + minutes;
        return total > 0 ? total : (int?)null;
    }

    // ─────────────────────────── Chore Type CRUD ───────────────────────────

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (string.IsNullOrWhiteSpace(ChoreTypeName))
        {
            TempData["ErrorMessage"] = _localizer["Error_ChoreTypeNameRequired"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        if (string.IsNullOrWhiteSpace(ChoreTypeDisplayName))
        {
            TempData["ErrorMessage"] = _localizer["Error_ChoreTypeDisplayNameRequired"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        if (!MoleculeId.HasValue || MoleculeId.Value <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_MoleculeRequired"].Value;
            return RedirectToPage();
        }

        if (ChoreTypeName.Trim().Length > 100)
        {
            TempData["ErrorMessage"] = _localizer["Error_NameTooLong"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        if (!string.IsNullOrWhiteSpace(ChoreTypeNameEn) && ChoreTypeNameEn.Trim().Length > 100)
        {
            TempData["ErrorMessage"] = _localizer["Error_NameTooLong"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        if (!string.IsNullOrWhiteSpace(ChoreTypeNameHe) && ChoreTypeNameHe.Trim().Length > 100)
        {
            TempData["ErrorMessage"] = _localizer["Error_NameTooLong"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId))
        {
            TempData["ErrorMessage"] = _localizer["Error_UserNotFound"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        // Verify user has grant access to this molecule (prevent IDOR)
        if (!await IsUserAuthorizedForMoleculeAsync(MoleculeId.Value))
        {
            TempData["ErrorMessage"] = _localizer["Error_MoleculeNotFound"].Value;
            return RedirectToPage();
        }

        // Sanitize color to prevent CSS injection
        var safeColor = SanitizeColor(ChoreTypeColor);

        var choreType = await _choreTypeService.CreateAsync(
            MoleculeId.Value,
            ChoreTypeName.Trim(),
            ChoreTypeDisplayName.Trim(),
            safeColor,
            currentUserId,
            string.IsNullOrWhiteSpace(ChoreTypeNameEn) ? null : ChoreTypeNameEn.Trim(),
            string.IsNullOrWhiteSpace(ChoreTypeNameHe) ? null : ChoreTypeNameHe.Trim());

        // Optional category assignment (service rejects cross-molecule).
        if (ChoreTypeCategoryId.HasValue)
            await _choreCategoryService.AssignChoreTypeAsync(choreType.Id, ChoreTypeCategoryId.Value);

        // Optional default weight.
        var createWeight = ToWeightMinutes(CreateWeightHours, CreateWeightMinutes);
        if (createWeight.HasValue)
        {
            var ctEntity = await _db.ChoreTypes.IgnoreQueryFilters().FirstAsync(x => x.Id == choreType.Id);
            ctEntity.DefaultWeightMinutes = createWeight;
            await _db.SaveChangesAsync();
        }

        _logger.LogInformation("Created ChoreType {ChoreTypeId}: {ChoreTypeName} in Molecule {MoleculeId}",
            choreType.Id, choreType.Name, MoleculeId.Value);

        await _auditLogService.LogAsync("ChoreTypeCreated", "ChoreType", choreType.Id,
            $"Created chore type '{choreType.DisplayName}' in molecule (MoleculeId={MoleculeId.Value})");

        TempData["SuccessMessage"] = string.Format(CultureInfo.CurrentCulture, _localizer["Success_ChoreTypeCreated"], choreType.DisplayName);
        return RedirectToPage(new { MoleculeId });
    }

    public async Task<IActionResult> OnPostUpdateAsync()
    {
        if (EditId <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_ChoreTypeNotFound"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        if (string.IsNullOrWhiteSpace(EditDisplayName))
        {
            TempData["ErrorMessage"] = _localizer["Error_ChoreTypeDisplayNameRequired"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        if (!string.IsNullOrWhiteSpace(EditNameEn) && EditNameEn.Trim().Length > 100)
        {
            TempData["ErrorMessage"] = _localizer["Error_NameTooLong"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        if (!string.IsNullOrWhiteSpace(EditNameHe) && EditNameHe.Trim().Length > 100)
        {
            TempData["ErrorMessage"] = _localizer["Error_NameTooLong"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        // Verify user has access to this chore type's molecule (prevent IDOR)
        var existing = await _choreTypeService.GetByIdAsync(EditId);
        if (existing == null || !await IsUserAuthorizedForMoleculeAsync(existing.MoleculeId))
        {
            TempData["ErrorMessage"] = _localizer["Error_ChoreTypeNotFound"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        if (!int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var currentUserId))
        {
            TempData["ErrorMessage"] = _localizer["Error_UserNotFound"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        try
        {
            var choreType = await _choreTypeService.UpdateAsync(
                EditId,
                EditDisplayName.Trim(),
                SanitizeColor(EditColor),
                EditSortOrder,
                string.IsNullOrWhiteSpace(EditNameEn) ? null : EditNameEn.Trim(),
                string.IsNullOrWhiteSpace(EditNameHe) ? null : EditNameHe.Trim());

            // Category (null clears) — service rejects cross-molecule.
            await _choreCategoryService.AssignChoreTypeAsync(EditId, EditCategoryId);

            // Default weight (null clears).
            var editWeight = ToWeightMinutes(EditWeightHours, EditWeightMinutes);
            var ctEntity = await _db.ChoreTypes.IgnoreQueryFilters().FirstAsync(x => x.Id == EditId);
            ctEntity.DefaultWeightMinutes = editWeight;
            await _db.SaveChangesAsync();

            // Replace-semantics eligibility rules. null/0 gender clears the gender rule.
            Gender? reqGender = EditRequiredGender switch { 1 => Gender.Male, 2 => Gender.Female, _ => null };
            await _eligibilityAdmin.SetRulesForChoreTypeAsync(EditId, reqGender, EditRequiresOfficer, currentUserId);
            // Audit: log only WHICH rules (gender/officer present), never any user/reason text.
            await _auditLogService.LogAsync("ChoreTypeEligibilityUpdated", "ChoreType", EditId,
                $"Set eligibility (gender={reqGender?.ToString() ?? "none"}, officer={EditRequiresOfficer})");

            _logger.LogInformation("Updated ChoreType {ChoreTypeId}: {ChoreTypeName}",
                choreType.Id, choreType.DisplayName);

            await _auditLogService.LogAsync("ChoreTypeUpdated", "ChoreType", choreType.Id,
                $"Updated chore type '{choreType.DisplayName}'");

            TempData["SuccessMessage"] = string.Format(CultureInfo.CurrentCulture, _localizer["Success_ChoreTypeUpdated"], choreType.DisplayName);
        }
        catch (ArgumentException)
        {
            TempData["ErrorMessage"] = _localizer["Error_ChoreTypeNotFound"].Value;
        }

        return RedirectToPage(new { MoleculeId });
    }

    public async Task<IActionResult> OnPostDeactivateAsync(int id)
    {
        var choreType = await _choreTypeService.GetByIdAsync(id);
        if (choreType == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_ChoreTypeNotFound"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        // Verify user has access to this chore type's molecule
        if (!await IsUserAuthorizedForMoleculeAsync(choreType.MoleculeId))
        {
            TempData["ErrorMessage"] = _localizer["Error_ChoreTypeNotFound"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        var success = await _choreTypeService.DeactivateAsync(id);
        if (!success)
        {
            TempData["ErrorMessage"] = _localizer["Error_ChoreTypeNotFound"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        _logger.LogInformation("Deactivated ChoreType {ChoreTypeId}: {ChoreTypeName}",
            id, choreType.DisplayName);

        await _auditLogService.LogAsync("ChoreTypeDeleted", "ChoreType", id,
            $"Deactivated chore type '{choreType.DisplayName}'");

        TempData["SuccessMessage"] = string.Format(CultureInfo.CurrentCulture, _localizer["Success_ChoreTypeDeactivated"], choreType.DisplayName);
        return RedirectToPage(new { MoleculeId });
    }

    public async Task<IActionResult> OnPostActivateAsync(int id)
    {
        // SECURITY-AUDITED: SAFE — scoped by specific ChoreType id; molecule access verified below
        var choreType = await _db.ChoreTypes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(ct => ct.Id == id);

        if (choreType == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_ChoreTypeNotFound"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        // Verify user has access to this chore type's molecule
        if (!await IsUserAuthorizedForMoleculeAsync(choreType.MoleculeId))
        {
            TempData["ErrorMessage"] = _localizer["Error_ChoreTypeNotFound"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        choreType.IsActive = true;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Activated ChoreType {ChoreTypeId}: {ChoreTypeName}",
            id, choreType.DisplayName);

        await _auditLogService.LogAsync("ChoreTypeUpdated", "ChoreType", id,
            $"Activated chore type '{choreType.DisplayName}'");

        TempData["SuccessMessage"] = string.Format(CultureInfo.CurrentCulture, _localizer["Success_ChoreTypeActivated"], choreType.DisplayName);
        return RedirectToPage(new { MoleculeId });
    }

    // ─────────────────────────── Chore Category CRUD ───────────────────────────

    public async Task<IActionResult> OnPostCreateCategoryAsync()
    {
        if (!MoleculeId.HasValue || MoleculeId.Value <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_MoleculeRequired"].Value;
            return RedirectToPage();
        }
        if (string.IsNullOrWhiteSpace(CategoryName) || string.IsNullOrWhiteSpace(CategoryDisplayName))
        {
            TempData["ErrorMessage"] = _localizer["Error_ChoreCategoryNameRequired"].Value;
            return RedirectToPage(new { MoleculeId });
        }
        if (!await IsUserAuthorizedForMoleculeAsync(MoleculeId.Value))
        {
            TempData["ErrorMessage"] = _localizer["Error_MoleculeNotFound"].Value;
            return RedirectToPage();
        }

        var created = await _choreCategoryService.CreateAsync(
            MoleculeId.Value, CategoryName.Trim(), CategoryDisplayName.Trim(),
            SanitizeColor(CategoryColor),
            string.IsNullOrWhiteSpace(CategoryNameEn) ? null : CategoryNameEn.Trim(),
            string.IsNullOrWhiteSpace(CategoryNameHe) ? null : CategoryNameHe.Trim());
        if (created == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_ChoreCategoryNameExists"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        await _auditLogService.LogAsync("ChoreCategoryCreated", "ChoreCategory", created.Id,
            $"Created chore category '{created.DisplayName}' in molecule (MoleculeId={MoleculeId.Value})");
        TempData["SuccessMessage"] = string.Format(CultureInfo.CurrentCulture, _localizer["Success_ChoreCategoryCreated"], created.DisplayName);
        return RedirectToPage(new { MoleculeId });
    }

    public async Task<IActionResult> OnPostUpdateCategoryAsync()
    {
        var cat = await _choreCategoryService.GetCategoryAsync(CategoryEditId);
        if (cat == null || !await IsUserAuthorizedForMoleculeAsync(cat.MoleculeId))
        {
            TempData["ErrorMessage"] = _localizer["Error_ChoreCategoryNotFound"].Value;
            return RedirectToPage(new { MoleculeId });
        }
        if (string.IsNullOrWhiteSpace(CategoryEditDisplayName))
        {
            TempData["ErrorMessage"] = _localizer["Error_ChoreCategoryNameRequired"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        var ok = await _choreCategoryService.RenameAsync(CategoryEditId, cat.Name, CategoryEditDisplayName.Trim(),
            SanitizeColor(CategoryEditColor),
            string.IsNullOrWhiteSpace(CategoryEditNameEn) ? null : CategoryEditNameEn.Trim(),
            string.IsNullOrWhiteSpace(CategoryEditNameHe) ? null : CategoryEditNameHe.Trim());
        if (!ok)
        {
            TempData["ErrorMessage"] = _localizer["Error_ChoreCategoryNameExists"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        await _auditLogService.LogAsync("ChoreCategoryUpdated", "ChoreCategory", CategoryEditId, $"Updated chore category '{CategoryEditDisplayName}'");
        TempData["SuccessMessage"] = _localizer["Success_ChoreCategoryUpdated"].Value;
        return RedirectToPage(new { MoleculeId });
    }

    public async Task<IActionResult> OnPostDeleteCategoryAsync(int id)
    {
        var cat = await _choreCategoryService.GetCategoryAsync(id);
        if (cat == null || !await IsUserAuthorizedForMoleculeAsync(cat.MoleculeId))
        {
            TempData["ErrorMessage"] = _localizer["Error_ChoreCategoryNotFound"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        await _choreCategoryService.DeleteAsync(id);   // FK SetNull un-categorizes types; membership cascades.
        await _auditLogService.LogAsync("ChoreCategoryDeleted", "ChoreCategory", id, $"Deleted chore category '{cat.DisplayName}'");
        TempData["SuccessMessage"] = string.Format(CultureInfo.CurrentCulture, _localizer["Success_ChoreCategoryDeleted"], cat.DisplayName);
        return RedirectToPage(new { MoleculeId });
    }

    // ─────────────────────────── Exemptions (AJAX) ───────────────────────────

    public class ExemptionRequest
    {
        public int ChoreTypeId { get; set; }
        public int UserId { get; set; }
        public string? Reason { get; set; }
    }

    public async Task<IActionResult> OnGetExemptionsAsync(int choreTypeId)
    {
        var ct = await _choreTypeService.GetByIdAsync(choreTypeId);
        if (ct == null || !await IsUserAuthorizedForMoleculeAsync(ct.MoleculeId))
            return new JsonResult(new { ok = false }) { StatusCode = 403 };

        var exemptions = await _eligibilityAdmin.GetExemptionsForChoreTypeAsync(choreTypeId);
        var userIds = exemptions.Select(e => e.UserId).ToList();
        // SECURITY-AUDITED: SAFE — user lookup scoped to exemption rows for an authorized chore type.
        var names = await _db.Users.IgnoreQueryFilters()
            .Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName);

        // Candidate users for the add-picker: active users in this type's molecule, not already exempt.
        var moleculeCompanyIds = await _db.Companies.IgnoreQueryFilters()
            .Where(c => c.MoleculeId == ct.MoleculeId).Select(c => c.Id).ToListAsync();
        var candidates = await _db.Users.IgnoreQueryFilters()
            .Where(u => moleculeCompanyIds.Contains(u.CompanyId) && u.IsActive && !userIds.Contains(u.Id))
            .OrderBy(u => u.DisplayName)
            .Select(u => new { id = u.Id, name = u.DisplayName }).ToListAsync();

        return new JsonResult(new
        {
            ok = true,
            exemptions = exemptions.Select(e => new { e.UserId, name = names.GetValueOrDefault(e.UserId, "?"), e.Reason }),
            candidates
        });
    }

    public async Task<IActionResult> OnPostAddExemptionAsync([FromBody] ExemptionRequest req)
    {
        var ct = await _choreTypeService.GetByIdAsync(req.ChoreTypeId);
        if (ct == null || !await IsUserAuthorizedForMoleculeAsync(ct.MoleculeId))
            return new JsonResult(new { success = false }) { StatusCode = 403 };
        if (!int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var actor))
            return new JsonResult(new { success = false }) { StatusCode = 403 };

        var added = await _eligibilityAdmin.AddExemptionAsync(req.UserId, req.ChoreTypeId, req.Reason, actor);
        if (added == null)
            return new JsonResult(new { success = false, error = _localizer["Error_FailedToUpdate"].Value });

        // SECURITY: never log the reason text (sensitive PII). Log only the (user, type) pair.
        await _auditLogService.LogAsync("ChoreExemptionAdded", "ChoreType", req.ChoreTypeId,
            $"Added chore exemption: user {req.UserId} on chore type {req.ChoreTypeId}");
        return new JsonResult(new { success = true });
    }

    public async Task<IActionResult> OnPostRemoveExemptionAsync([FromBody] ExemptionRequest req)
    {
        var ct = await _choreTypeService.GetByIdAsync(req.ChoreTypeId);
        if (ct == null || !await IsUserAuthorizedForMoleculeAsync(ct.MoleculeId))
            return new JsonResult(new { success = false }) { StatusCode = 403 };

        var removed = await _eligibilityAdmin.RemoveExemptionAsync(req.UserId, req.ChoreTypeId);
        if (removed)
            await _auditLogService.LogAsync("ChoreExemptionRemoved", "ChoreType", req.ChoreTypeId,
                $"Removed chore exemption: user {req.UserId} on chore type {req.ChoreTypeId}");
        return new JsonResult(new { success = removed });
    }

    /// <summary>
    /// Verifies the current user has grant access to the specified molecule.
    /// </summary>
    private async Task<bool> IsUserAuthorizedForMoleculeAsync(int moleculeId)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId)) return false;

        var accessibleMoleculeIds = new HashSet<int>(
            await _grantService.GetAccessibleMoleculeIdsForGrantAsync(currentUserId, "EditChoreTypes"));
        var userCompany = await _db.Companies.Include(c => c.Molecule)
            .FirstOrDefaultAsync(c => c.Id == _companyContext.CompanyId);
        if (userCompany?.MoleculeId.HasValue == true)
            accessibleMoleculeIds.Add(userCompany.MoleculeId.Value);

        return accessibleMoleculeIds.Contains(moleculeId);
    }

    /// <summary>
    /// Sanitizes a color value to a strict #RRGGBB hex format to prevent CSS injection.
    /// Returns null if the input is empty or doesn't match the expected format.
    /// </summary>
    // Delegates to shared utility to avoid drift (2026-04-15 extraction).
    private static string? SanitizeColor(string? color) => ShiftManager.Services.ColorUtilities.SanitizeHexColor(color);
}
