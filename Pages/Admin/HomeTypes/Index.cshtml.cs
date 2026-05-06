using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Pages.Admin.HomeTypes;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:ManageHomeTypes policy;
// home type management is molecule-scoped configuration data
[Authorize(Policy = "Grant:ManageHomeTypes")]
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IHomeTypeService _homeTypeService;
    private readonly ICompanyContext _companyContext;
    private readonly ILogger<IndexModel> _logger;
    private readonly IStringLocalizer<SharedResources> _localizer;

    public IndexModel(
        AppDbContext db,
        IHomeTypeService homeTypeService,
        ICompanyContext companyContext,
        ILogger<IndexModel> logger,
        IStringLocalizer<SharedResources> localizer)
    {
        _db = db;
        _homeTypeService = homeTypeService;
        _companyContext = companyContext;
        _logger = logger;
        _localizer = localizer;
    }

    // Data
    public List<HomeTypeDto> HomeTypes { get; set; } = new();
    public List<MoleculeOption> AvailableMolecules { get; set; } = new();
    public record MoleculeOption(int Id, string Name);

    [BindProperty(SupportsGet = true)]
    public int? MoleculeId { get; set; }

    // Create form
    [BindProperty] public string Name { get; set; } = string.Empty;
    [BindProperty] public string? NameHe { get; set; }
    [BindProperty] public int CreateMoleculeId { get; set; }

    // Rule-config bind properties (replace painter)
    [BindProperty] public int RuleCycleWeeks { get; set; } = 4;
    [BindProperty] public List<DayOfWeek> RuleHomeDays { get; set; } = new();
    [BindProperty] public List<int> RuleWeekOffsets { get; set; } = new() { 0 };
    [BindProperty] public DateOnly RuleAnchor { get; set; } = NextMonday();
    [BindProperty] public DateOnly? RuleActiveStart { get; set; }
    [BindProperty] public DateOnly? RuleActiveEnd { get; set; }

    // Edit form
    [BindProperty] public int EditId { get; set; }
    [BindProperty] public string EditName { get; set; } = string.Empty;
    [BindProperty] public string? EditNameHe { get; set; }

    // Generate form
    [BindProperty] public int GenerateHomeTypeId { get; set; }
    [BindProperty] public string? GenerateStartDate { get; set; }
    [BindProperty] public string? GenerateEndDate { get; set; }
    [BindProperty] public string? GenerateMode { get; set; }

    // User assignment
    [BindProperty] public int AssignHomeTypeId { get; set; }
    [BindProperty] public string? AssignUserIds { get; set; }
    [BindProperty] public int UnassignHomeTypeId { get; set; }
    [BindProperty] public int UnassignUserId { get; set; }

    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public GenerationResult? LastGenerationResult { get; set; }

    // User assignment data
    public record AssignedUserInfo(int UserId, string DisplayName);
    public record AvailableUserInfo(int UserId, string DisplayName, int? CurrentHomeTypeId, string? CurrentHomeTypeName);
    public Dictionary<int, List<AssignedUserInfo>> AssignedUsersPerHomeType { get; set; } = new();
    public List<AvailableUserInfo> AvailableUsersInMolecule { get; set; } = new();

    // Banner data (Task 26)
    public class HomeTypeBanner
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public bool NeedsRegen { get; set; }
        public DateTime? LastGeneratedAt { get; set; }
        public int UserCount { get; set; }
    }
    public List<HomeTypeBanner> Banners { get; set; } = new();

    private static DateOnly NextMonday()
    {
        var t = DateOnly.FromDateTime(DateTime.Today);
        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)t.DayOfWeek + 7) % 7;
        if (daysUntilMonday == 0) daysUntilMonday = 7;
        return t.AddDays(daysUntilMonday);
    }

    public async Task OnGetAsync()
    {
        SuccessMessage = TempData["SuccessMessage"] as string;
        ErrorMessage = TempData["ErrorMessage"] as string;

        // Resolve user's molecule from their company
        var companyId = _companyContext.CompanyId;
        int? userMoleculeId = null;
        if (companyId.HasValue)
        {
            var userCompany = await _db.Companies
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == companyId.Value);
            userMoleculeId = userCompany?.MoleculeId;
        }

        AvailableMolecules = await _db.Molecules
            .IgnoreQueryFilters()
            .Where(m => m.IsActive)
            .Include(m => m.Area)
            .OrderBy(m => m.Area.Name).ThenBy(m => m.Name)
            .Select(m => new MoleculeOption(m.Id, $"{m.Area.DisplayName} / {m.DisplayName}"))
            .ToListAsync();

        // Default to user's own molecule
        if (!MoleculeId.HasValue && userMoleculeId.HasValue)
            MoleculeId = userMoleculeId.Value;
        else if (!MoleculeId.HasValue && AvailableMolecules.Count > 0)
            MoleculeId = AvailableMolecules[0].Id;

        if (MoleculeId.HasValue)
        {
            HomeTypes = await _homeTypeService.GetHomeTypesAsync(MoleculeId.Value);

            // Load assigned users per home type
            var htIds = HomeTypes.Select(ht => ht.Id).ToList();
            if (htIds.Count > 0)
            {
                // SECURITY-AUDITED: SAFE — scoped to home types already loaded for the selected molecule
                var assignedUsers = await _db.Users
                    .IgnoreQueryFilters()
                    .Where(u => u.HomeTypeId.HasValue && htIds.Contains(u.HomeTypeId.Value) && u.IsActive)
                    .OrderBy(u => u.DisplayName)
                    .Select(u => new { u.Id, u.DisplayName, u.HomeTypeId })
                    .ToListAsync();

                AssignedUsersPerHomeType = assignedUsers
                    .GroupBy(u => u.HomeTypeId!.Value)
                    .ToDictionary(g => g.Key, g => g.Select(u => new AssignedUserInfo(u.Id, u.DisplayName)).ToList());
            }

            // Load available users in the molecule (for the assign dropdown)
            // SECURITY-AUDITED: SAFE — scoped to companies within the selected molecule
            var moleculeCompanyIds = await _db.Companies
                .IgnoreQueryFilters()
                .Where(c => c.MoleculeId == MoleculeId.Value)
                .Select(c => c.Id)
                .ToListAsync();

            // Build a lookup of home type names for users already assigned
            var htNameLookup = HomeTypes.ToDictionary(ht => ht.Id, ht => ht.Name);

            AvailableUsersInMolecule = await _db.Users
                .IgnoreQueryFilters()
                .Where(u => moleculeCompanyIds.Contains(u.CompanyId) && u.IsActive)
                .OrderBy(u => u.DisplayName)
                .Select(u => new AvailableUserInfo(u.Id, u.DisplayName, u.HomeTypeId, null))
                .ToListAsync();

            // Populate the home type name for users already assigned
            AvailableUsersInMolecule = AvailableUsersInMolecule
                .Select(u => u with { CurrentHomeTypeName = u.CurrentHomeTypeId.HasValue && htNameLookup.ContainsKey(u.CurrentHomeTypeId.Value)
                    ? htNameLookup[u.CurrentHomeTypeId.Value] : null })
                .ToList();

            // Compute regen banners (Task 26)
            // SECURITY-AUDITED: SAFE — scoped to MoleculeId we already resolved for the page
            var bannerSource = await _db.HomeTypes
                .IgnoreQueryFilters()
                .Where(h => h.MoleculeId == MoleculeId.Value)
                .Select(h => new
                {
                    h.Id,
                    h.Name,
                    h.LastGeneratedAt,
                    h.UpdatedAt
                })
                .ToListAsync();

            var userCounts = await _db.Users
                .IgnoreQueryFilters()
                .Where(u => u.HomeTypeId.HasValue && bannerSource.Select(b => b.Id).Contains(u.HomeTypeId.Value) && u.IsActive)
                .GroupBy(u => u.HomeTypeId!.Value)
                .Select(g => new { HomeTypeId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.HomeTypeId, x => x.Count);

            Banners = bannerSource
                .Select(h => new HomeTypeBanner
                {
                    Id = h.Id,
                    Name = h.Name,
                    NeedsRegen = h.LastGeneratedAt == null || h.UpdatedAt > h.LastGeneratedAt,
                    LastGeneratedAt = h.LastGeneratedAt,
                    UserCount = userCounts.TryGetValue(h.Id, out var c) ? c : 0
                })
                .ToList();
        }
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId))
            return RedirectToPage();

        // Build rule directly from bind properties (Task 25 — rule-first editor)
        var rule = new DerivedRotationRule(
            CycleWeeks: Math.Clamp(RuleCycleWeeks, 1, 12),
            HomeDays: RuleHomeDays?.Distinct().ToList() ?? new List<DayOfWeek>(),
            WeekOffsets: RuleWeekOffsets?.Distinct().Where(i => i >= 0 && i < Math.Clamp(RuleCycleWeeks, 1, 12)).ToList() ?? new List<int>(),
            Anchor: RuleAnchor,
            StartTime: null,
            EndTime: null);

        // Resolve CompanyId from molecule
        var molecule = await _db.Molecules.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == CreateMoleculeId);
        if (molecule == null)
        {
            TempData["ErrorMessage"] = "Invalid molecule";
            return RedirectToPage(new { MoleculeId = CreateMoleculeId });
        }

        // Use the first company in the molecule for CompanyId (tenant scope)
        var companyId = await _db.Companies.IgnoreQueryFilters()
            .Where(c => c.MoleculeId == CreateMoleculeId)
            .Select(c => c.Id)
            .FirstOrDefaultAsync();

        var homeType = new HomeType
        {
            Name = Name.Trim(),
            NameHe = NameHe?.Trim(),
            MoleculeId = CreateMoleculeId,
            CompanyId = companyId,
            PatternJson = null, // legacy column unused for new rules
            DerivedRule = JsonSerializer.Serialize(rule),
            UpdatedAt = DateTime.UtcNow,
            CreatedBy = currentUserId
        };

        await _homeTypeService.CreateHomeTypeAsync(homeType);
        TempData["SuccessMessage"] = $"Created home type: {homeType.Name}";

        return RedirectToPage(new { MoleculeId = CreateMoleculeId });
    }

    public async Task<IActionResult> OnPostEditAsync()
    {
        var ht = await _homeTypeService.GetHomeTypeAsync(EditId);
        if (ht == null)
        {
            TempData["ErrorMessage"] = "Home type not found";
            return RedirectToPage();
        }

        ht.Name = EditName.Trim();
        ht.NameHe = EditNameHe?.Trim();

        // Build rule from bind properties (Task 25 — rule-first editor)
        var rule = new DerivedRotationRule(
            CycleWeeks: Math.Clamp(RuleCycleWeeks, 1, 12),
            HomeDays: RuleHomeDays?.Distinct().ToList() ?? new List<DayOfWeek>(),
            WeekOffsets: RuleWeekOffsets?.Distinct().Where(i => i >= 0 && i < Math.Clamp(RuleCycleWeeks, 1, 12)).ToList() ?? new List<int>(),
            Anchor: RuleAnchor,
            StartTime: null,
            EndTime: null);

        ht.DerivedRule = JsonSerializer.Serialize(rule);
        ht.PatternJson = null;
        ht.UpdatedAt = DateTime.UtcNow;

        await _homeTypeService.UpdateHomeTypeAsync(ht);
        TempData["SuccessMessage"] = $"Updated: {ht.Name}";

        return RedirectToPage(new { MoleculeId = ht.MoleculeId });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var ht = await _homeTypeService.GetHomeTypeAsync(id);
        var moleculeId = ht?.MoleculeId;
        await _homeTypeService.DeleteHomeTypeAsync(id);
        TempData["SuccessMessage"] = "Deleted";

        return RedirectToPage(new { MoleculeId = moleculeId });
    }

    public async Task<IActionResult> OnPostToggleActiveAsync(int id)
    {
        var ht = await _homeTypeService.GetHomeTypeAsync(id);
        if (ht != null)
        {
            ht.IsActive = !ht.IsActive;
            ht.UpdatedAt = DateTime.UtcNow;
            await _homeTypeService.UpdateHomeTypeAsync(ht);
        }
        return RedirectToPage(new { MoleculeId = ht?.MoleculeId });
    }

    public async Task<IActionResult> OnPostAssignUsersAsync()
    {
        if (string.IsNullOrEmpty(AssignUserIds))
            return RedirectToPage();

        var userIds = AssignUserIds.Split(',')
            .Select(s => int.TryParse(s.Trim(), out var id) ? id : 0)
            .Where(id => id > 0)
            .ToList();

        await _homeTypeService.AssignUsersAsync(AssignHomeTypeId, userIds);
        TempData["SuccessMessage"] = $"Assigned {userIds.Count} user(s)";

        var ht = await _homeTypeService.GetHomeTypeAsync(AssignHomeTypeId);
        // Bump UpdatedAt — new users need shifts generated for them
        if (ht != null)
        {
            ht.UpdatedAt = DateTime.UtcNow;
            await _homeTypeService.UpdateHomeTypeAsync(ht);
        }
        return RedirectToPage(new { MoleculeId = ht?.MoleculeId });
    }

    public async Task<IActionResult> OnPostUnassignUserAsync()
    {
        await _homeTypeService.UnassignUserAsync(UnassignHomeTypeId, UnassignUserId);
        TempData["SuccessMessage"] = "User unassigned";

        var ht = await _homeTypeService.GetHomeTypeAsync(UnassignHomeTypeId);
        return RedirectToPage(new { MoleculeId = ht?.MoleculeId });
    }

    public async Task<IActionResult> OnPostGenerateAsync()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId))
            return RedirectToPage();

        if (!DateOnly.TryParse(GenerateStartDate, out var start))
            start = DateOnly.FromDateTime(DateTime.Today);
        if (!DateOnly.TryParse(GenerateEndDate, out var end))
            end = start.AddYears(1);

        var mode = GenerateMode == "overwrite"
            ? RegenerationMode.OverwriteAll
            : RegenerationMode.KeepManualChanges;

        // Get users assigned to this HomeType
        var users = await _homeTypeService.GetUsersForHomeTypeAsync(GenerateHomeTypeId);
        var userIds = users.Select(u => u.Id).ToList();

        if (userIds.Count == 0)
        {
            TempData["ErrorMessage"] = "No users assigned to this home type";
            var homeType = await _homeTypeService.GetHomeTypeAsync(GenerateHomeTypeId);
            return RedirectToPage(new { MoleculeId = homeType?.MoleculeId });
        }

        var result = await _homeTypeService.GenerateHomeShiftsAsync(
            GenerateHomeTypeId, start, end, userIds, currentUserId, mode);

        // Mark as freshly generated — clears the regen banner (Task 26)
        var ht = await _db.HomeTypes.IgnoreQueryFilters().FirstOrDefaultAsync(h => h.Id == GenerateHomeTypeId);
        if (ht != null)
        {
            ht.LastGeneratedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        TempData["SuccessMessage"] = $"Generated {result.Created} HOME shifts. Skipped: {result.Skipped}. Conflicts: {result.Conflicts.Count}";

        return RedirectToPage(new { MoleculeId = ht?.MoleculeId });
    }

    // --- Per-user override handlers (Task 27) ---

    public async Task<IActionResult> OnGetGetOverrideAsync(int userId, int homeTypeId)
    {
        var dates = await _homeTypeService.GetUserOverrideDatesAsync(homeTypeId, userId);
        return new JsonResult(new { dates = dates.Select(d => d.ToString("yyyy-MM-dd")).ToList() });
    }

    public record OverrideRequest(int UserId, int HomeTypeId, List<string> Dates);

    public async Task<IActionResult> OnPostSaveOverrideAsync([FromBody] OverrideRequest req)
    {
        if (req == null) return BadRequest();

        var dates = (req.Dates ?? new List<string>())
            .Select(s => DateOnly.TryParse(s, out var d) ? (DateOnly?)d : null)
            .Where(d => d.HasValue).Select(d => d!.Value).ToList();

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var actorId)) return Unauthorized();

        await _homeTypeService.SaveUserOverrideAsync(req.HomeTypeId, req.UserId, dates, actorId);

        // Bump UpdatedAt so the regen banner surfaces
        var ht = await _db.HomeTypes.IgnoreQueryFilters().FirstOrDefaultAsync(h => h.Id == req.HomeTypeId);
        if (ht != null)
        {
            ht.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        return new JsonResult(new { success = true });
    }
}
