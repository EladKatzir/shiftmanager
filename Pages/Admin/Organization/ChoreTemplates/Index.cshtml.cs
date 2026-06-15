using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Pages.Admin.Organization.ChoreTemplates;

// SECURITY-AUDITED: All IgnoreQueryFilters() are SAFE — gated by Grant:EditChoreTypes; templates are
// molecule-scoped config; every handler re-verifies the molecule via IsUserAuthorizedForMoleculeAsync.
[Authorize(Policy = "Grant:EditChoreTypes")]
public class IndexModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly IChoreTemplateService _templateService;
    private readonly IChoreService _choreService;
    private readonly IChoreTypeService _choreTypeService;
    private readonly IGrantService _grantService;
    private readonly ICompanyContext _companyContext;
    private readonly IAuditLogService _auditLogService;

    public IndexModel(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext db,
        IChoreTemplateService templateService,
        IChoreService choreService,
        IChoreTypeService choreTypeService,
        IGrantService grantService,
        ICompanyContext companyContext,
        IAuditLogService auditLogService) : base(localizer)
    {
        _db = db;
        _templateService = templateService;
        _choreService = choreService;
        _choreTypeService = choreTypeService;
        _grantService = grantService;
        _companyContext = companyContext;
        _auditLogService = auditLogService;
    }

    public record TemplateVM(int Id, string Name, int? ChoreTypeId, string? ChoreTypeName, string DefaultTitle,
        TimeOnly? StartTime, TimeOnly? EndTime, int? WeightMinutesOverride, string? Notes, bool IsActive);
    public record MoleculeOption(int Id, string Name);
    public record ChoreTypeOption(int Id, string Name);
    public record AssigneeOption(int Id, string Name);

    public List<TemplateVM> Templates { get; set; } = new();
    public List<MoleculeOption> AvailableMolecules { get; set; } = new();
    public List<ChoreTypeOption> ChoreTypeOptions { get; set; } = new();
    public List<AssigneeOption> AssigneeOptions { get; set; } = new();

    [BindProperty(SupportsGet = true)] public int? MoleculeId { get; set; }

    // Create form
    [BindProperty] public string TemplateName { get; set; } = string.Empty;
    [BindProperty] public int? TemplateChoreTypeId { get; set; }
    [BindProperty] public string TemplateDefaultTitle { get; set; } = string.Empty;
    [BindProperty] public TimeOnly? TemplateStartTime { get; set; }
    [BindProperty] public TimeOnly? TemplateEndTime { get; set; }
    [BindProperty] public int TemplateWeightHours { get; set; }
    [BindProperty] public int TemplateWeightMinutes { get; set; }
    [BindProperty] public string? TemplateNotes { get; set; }

    // Edit form
    [BindProperty] public int EditId { get; set; }
    [BindProperty] public string EditName { get; set; } = string.Empty;
    [BindProperty] public int? EditChoreTypeId { get; set; }
    [BindProperty] public string EditDefaultTitle { get; set; } = string.Empty;
    [BindProperty] public TimeOnly? EditStartTime { get; set; }
    [BindProperty] public TimeOnly? EditEndTime { get; set; }
    [BindProperty] public int EditWeightHours { get; set; }
    [BindProperty] public int EditWeightMinutes { get; set; }
    [BindProperty] public string? EditNotes { get; set; }

    public async Task OnGetAsync() => await LoadDataAsync();

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

        if (!MoleculeId.HasValue && userMoleculeId.HasValue)
            MoleculeId = userMoleculeId.Value;

        if (MoleculeId.HasValue && !AvailableMolecules.Any(m => m.Id == MoleculeId.Value))
            MoleculeId = userMoleculeId;

        if (MoleculeId.HasValue)
        {
            var templates = await _templateService.GetTemplatesForMoleculeAsync(MoleculeId.Value, includeInactive: true);
            var types = await _choreTypeService.GetChoreTypesForMoleculeAsync(MoleculeId.Value);
            var typeNames = types.ToDictionary(t => t.Id, t => t.DisplayName);
            ChoreTypeOptions = types.Select(t => new ChoreTypeOption(t.Id, t.DisplayName)).ToList();
            Templates = templates.Select(t => new TemplateVM(t.Id, t.Name, t.ChoreTypeId,
                t.ChoreTypeId.HasValue ? typeNames.GetValueOrDefault(t.ChoreTypeId.Value) : null,
                t.DefaultTitle, t.StartTime, t.EndTime, t.WeightMinutesOverride, t.Notes, t.IsActive)).ToList();

            // Stamp assignee roster: active Standard users in this molecule (display roster; Busy gate enforces real authz).
            var companyIds = await _db.Companies.IgnoreQueryFilters()
                .Where(c => c.MoleculeId == MoleculeId.Value).Select(c => c.Id).ToListAsync();
            AssigneeOptions = await _db.Users.IgnoreQueryFilters()
                .Where(u => companyIds.Contains(u.CompanyId) && u.IsActive
                    && u.AccountType == AccountType.Standard)
                .OrderBy(u => u.DisplayName)
                .Select(u => new AssigneeOption(u.Id, u.DisplayName)).ToListAsync();
        }
    }

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

    private static int? ToWeightMinutes(int h, int m) { var t = h * 60 + m; return t > 0 ? t : (int?)null; }

    // ─────────────────────────── Template CRUD ───────────────────────────

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (!MoleculeId.HasValue || MoleculeId.Value <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_MoleculeRequired"].Value;
            return RedirectToPage();
        }
        if (string.IsNullOrWhiteSpace(TemplateName) || string.IsNullOrWhiteSpace(TemplateDefaultTitle))
        {
            TempData["ErrorMessage"] = _localizer["Error_ChoreTemplateNameRequired"].Value;
            return RedirectToPage(new { MoleculeId });
        }
        if (!await IsUserAuthorizedForMoleculeAsync(MoleculeId.Value))
        {
            TempData["ErrorMessage"] = _localizer["Error_MoleculeNotFound"].Value;
            return RedirectToPage();
        }
        if (!int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var currentUserId))
        {
            TempData["ErrorMessage"] = _localizer["Error_UserNotFound"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        // Validate the optional chore type belongs to this molecule.
        var choreTypeId = await ResolveAccessibleChoreTypeIdAsync(TemplateChoreTypeId, MoleculeId.Value);

        var created = await _templateService.CreateAsync(
            MoleculeId.Value, TemplateName.Trim(), choreTypeId, TemplateDefaultTitle.Trim(),
            TemplateStartTime, TemplateEndTime, ToWeightMinutes(TemplateWeightHours, TemplateWeightMinutes),
            TemplateNotes, currentUserId);
        if (created == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_ChoreTemplateNameRequired"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        await _auditLogService.LogAsync("ChoreTemplateCreated", "ChoreTemplate", created.Id,
            $"Created chore template '{created.Name}' in molecule (MoleculeId={MoleculeId.Value})");
        TempData["SuccessMessage"] = string.Format(CultureInfo.CurrentCulture, _localizer["Success_ChoreTemplateCreated"], created.Name);
        return RedirectToPage(new { MoleculeId });
    }

    public async Task<IActionResult> OnPostUpdateAsync()
    {
        var existing = await _templateService.GetByIdAsync(EditId);
        if (existing == null || !await IsUserAuthorizedForMoleculeAsync(existing.MoleculeId))
        {
            TempData["ErrorMessage"] = _localizer["Error_ChoreTemplateNotFound"].Value;
            return RedirectToPage(new { MoleculeId });
        }
        if (string.IsNullOrWhiteSpace(EditName) || string.IsNullOrWhiteSpace(EditDefaultTitle))
        {
            TempData["ErrorMessage"] = _localizer["Error_ChoreTemplateNameRequired"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        var choreTypeId = await ResolveAccessibleChoreTypeIdAsync(EditChoreTypeId, existing.MoleculeId);

        var ok = await _templateService.UpdateAsync(EditId, EditName.Trim(), choreTypeId, EditDefaultTitle.Trim(),
            EditStartTime, EditEndTime, ToWeightMinutes(EditWeightHours, EditWeightMinutes), EditNotes);
        if (!ok)
        {
            TempData["ErrorMessage"] = _localizer["Error_ChoreTemplateNotFound"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        await _auditLogService.LogAsync("ChoreTemplateUpdated", "ChoreTemplate", EditId, $"Updated chore template '{EditName}'");
        TempData["SuccessMessage"] = _localizer["Success_ChoreTemplateUpdated"].Value;
        return RedirectToPage(new { MoleculeId });
    }

    public async Task<IActionResult> OnPostDeactivateAsync(int id) => await SetActiveAsync(id, false);
    public async Task<IActionResult> OnPostActivateAsync(int id) => await SetActiveAsync(id, true);

    private async Task<IActionResult> SetActiveAsync(int id, bool active)
    {
        var t = await _templateService.GetByIdAsync(id);
        if (t == null || !await IsUserAuthorizedForMoleculeAsync(t.MoleculeId))
        {
            TempData["ErrorMessage"] = _localizer["Error_ChoreTemplateNotFound"].Value;
            return RedirectToPage(new { MoleculeId });
        }
        if (active) await _templateService.ActivateAsync(id); else await _templateService.DeactivateAsync(id);
        await _auditLogService.LogAsync("ChoreTemplateUpdated", "ChoreTemplate", id, $"{(active ? "Activated" : "Deactivated")} chore template '{t.Name}'");
        TempData["SuccessMessage"] = _localizer["Success_ChoreTemplateUpdated"].Value;
        return RedirectToPage(new { MoleculeId });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var t = await _templateService.GetByIdAsync(id);
        if (t == null || !await IsUserAuthorizedForMoleculeAsync(t.MoleculeId))
        {
            TempData["ErrorMessage"] = _localizer["Error_ChoreTemplateNotFound"].Value;
            return RedirectToPage(new { MoleculeId });
        }
        await _templateService.DeleteAsync(id);
        await _auditLogService.LogAsync("ChoreTemplateDeleted", "ChoreTemplate", id, $"Deleted chore template '{t.Name}'");
        TempData["SuccessMessage"] = string.Format(CultureInfo.CurrentCulture, _localizer["Success_ChoreTemplateDeleted"], t.Name);
        return RedirectToPage(new { MoleculeId });
    }

    /// <summary>Returns the chore type id only if it exists in the given molecule; otherwise null (clears the link).</summary>
    private async Task<int?> ResolveAccessibleChoreTypeIdAsync(int? choreTypeId, int moleculeId)
    {
        if (!choreTypeId.HasValue) return null;
        var ct = await _choreTypeService.GetByIdAsync(choreTypeId.Value);
        return (ct != null && ct.MoleculeId == moleculeId) ? choreTypeId : null;
    }

    // ─────────────────────────── Stamp (AJAX) ───────────────────────────

    public class StampRequest
    {
        public int TemplateId { get; set; }
        public DateOnly From { get; set; }
        public DateOnly To { get; set; }
        public List<int>? Weekdays { get; set; }   // 0=Sunday..6=Saturday (DayOfWeek ints)
        public List<int>? AssigneeIds { get; set; }
        public bool Rotate { get; set; }
    }

    public async Task<IActionResult> OnPostStampAsync([FromBody] StampRequest req)
    {
        var template = await _templateService.GetByIdAsync(req.TemplateId);
        if (template == null || !await IsUserAuthorizedForMoleculeAsync(template.MoleculeId))
            return new JsonResult(new { success = false }) { StatusCode = 403 };
        if (req.AssigneeIds == null || req.AssigneeIds.Count == 0 || req.From > req.To)
            return new JsonResult(new { success = false, error = _localizer["Error_StampInvalidInput"].Value });

        var weekdays = (req.Weekdays ?? new List<int>()).Select(d => (DayOfWeek)d).ToList();

        // StampTemplateAsync never throws: it caps spans > 92 days / totals > 500 by returning a single
        // skipped row with ReasonKey == "STAMP_TOO_LARGE" (localized to Stamp_TooLarge in the result view).
        var result = await _choreService.StampTemplateAsync(req.TemplateId, req.From, req.To, weekdays, req.AssigneeIds, req.Rotate);

        await _auditLogService.LogAsync("ChoreTemplateStamped", "ChoreTemplate", req.TemplateId,
            $"Stamped template {req.TemplateId}: created {result.CreatedCount}, skipped {result.SkippedCount}");

        var ids = result.Created.Select(c => c.UserId).Concat(result.Skipped.Select(s => s.UserId)).Distinct().ToList();
        // SECURITY-AUDITED: SAFE — name lookup scoped to the (date,user) rows this stamp touched.
        var names = await _db.Users.IgnoreQueryFilters().Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName);

        return new JsonResult(new
        {
            success = true,
            created = result.CreatedCount,
            skipped = result.SkippedCount,
            createdRows = result.Created.Select(c => new { date = c.Date.ToString("yyyy-MM-dd"), user = names.GetValueOrDefault(c.UserId, "?") }),
            skippedRows = result.Skipped.Select(s => new { date = s.Date.ToString("yyyy-MM-dd"), user = names.GetValueOrDefault(s.UserId, "?"), reasonKey = s.ReasonKey })
        });
    }
}
