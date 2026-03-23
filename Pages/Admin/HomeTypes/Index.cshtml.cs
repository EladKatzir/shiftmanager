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
    private readonly ILogger<IndexModel> _logger;
    private readonly IStringLocalizer<SharedResources> _localizer;

    public IndexModel(
        AppDbContext db,
        IHomeTypeService homeTypeService,
        ILogger<IndexModel> logger,
        IStringLocalizer<SharedResources> localizer)
    {
        _db = db;
        _homeTypeService = homeTypeService;
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
    [BindProperty] public string? PatternJson { get; set; }
    [BindProperty] public string? DefaultStartTime { get; set; }
    [BindProperty] public string? DefaultEndTime { get; set; }

    // Edit form
    [BindProperty] public int EditId { get; set; }
    [BindProperty] public string EditName { get; set; } = string.Empty;
    [BindProperty] public string? EditNameHe { get; set; }
    [BindProperty] public string? EditPatternJson { get; set; }

    // Generate form
    [BindProperty] public int GenerateHomeTypeId { get; set; }
    [BindProperty] public string? GenerateStartDate { get; set; }
    [BindProperty] public string? GenerateEndDate { get; set; }
    [BindProperty] public string? GenerateMode { get; set; }

    // User assignment
    [BindProperty] public int AssignHomeTypeId { get; set; }
    [BindProperty] public string? AssignUserIds { get; set; }

    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public GenerationResult? LastGenerationResult { get; set; }

    public async Task OnGetAsync()
    {
        SuccessMessage = TempData["SuccessMessage"] as string;
        ErrorMessage = TempData["ErrorMessage"] as string;

        AvailableMolecules = await _db.Molecules
            .IgnoreQueryFilters()
            .Where(m => m.IsActive)
            .Include(m => m.Area)
            .OrderBy(m => m.Area.Name).ThenBy(m => m.Name)
            .Select(m => new MoleculeOption(m.Id, $"{m.Area.DisplayName} / {m.DisplayName}"))
            .ToListAsync();

        if (!MoleculeId.HasValue && AvailableMolecules.Count > 0)
            MoleculeId = AvailableMolecules[0].Id;

        if (MoleculeId.HasValue)
            HomeTypes = await _homeTypeService.GetHomeTypesAsync(MoleculeId.Value);
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId))
            return RedirectToPage();

        // Parse painted dates and derive rule
        var paintedDates = ParseDatesFromJson(PatternJson);
        var rule = _homeTypeService.DeriveRuleFromPattern(paintedDates);

        TimeOnly? startTime = TimeOnly.TryParse(DefaultStartTime, out var st) ? st : null;
        TimeOnly? endTime = TimeOnly.TryParse(DefaultEndTime, out var et) ? et : null;

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
            PatternJson = PatternJson,
            DerivedRule = rule != null ? JsonSerializer.Serialize(rule) : null,
            DefaultStartTime = startTime,
            DefaultEndTime = endTime,
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

        if (!string.IsNullOrEmpty(EditPatternJson))
        {
            ht.PatternJson = EditPatternJson;
            var paintedDates = ParseDatesFromJson(EditPatternJson);
            var rule = _homeTypeService.DeriveRuleFromPattern(paintedDates);
            ht.DerivedRule = rule != null ? JsonSerializer.Serialize(rule) : null;
        }

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
        TempData["SuccessMessage"] = $"Assigned {userIds.Count} users";

        var ht = await _homeTypeService.GetHomeTypeAsync(AssignHomeTypeId);
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

        TempData["SuccessMessage"] = $"Generated {result.Created} HOME shifts. Skipped: {result.Skipped}. Conflicts: {result.Conflicts.Count}";

        var ht = await _homeTypeService.GetHomeTypeAsync(GenerateHomeTypeId);
        return RedirectToPage(new { MoleculeId = ht?.MoleculeId });
    }

    private static List<DateOnly> ParseDatesFromJson(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new();
        try
        {
            var strings = JsonSerializer.Deserialize<List<string>>(json) ?? new();
            return strings
                .Select(s => DateOnly.TryParse(s, out var d) ? d : default)
                .Where(d => d != default)
                .ToList();
        }
        catch { return new(); }
    }
}
