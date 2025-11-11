using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Services;

namespace ShiftManager.Pages.Admin;

[Authorize(Policy = "IsManagerOrAdmin")]
public class ShiftTypesModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ICompanyContext _companyContext;

    public ShiftTypesModel(AppDbContext db, ICompanyContext companyContext)
    {
        _db = db;
        _companyContext = companyContext;
    }

    public record Item(int Id, string Key, string Name, string Start, string End);
    public List<Item> Items { get; set; } = new();

    public async Task OnGetAsync()
    {
        var companyId = _companyContext.GetCompanyIdOrThrow();
        // Query filter automatically scopes ShiftTypes by CompanyId
        var t = await _db.ShiftTypes.OrderBy(s => s.Key).ToListAsync();
        Items = t.Select(x => new Item(x.Id, x.Key, x.Name, x.Start.ToString("HH:mm"), x.End.ToString("HH:mm"))).ToList();
    }

    public async Task<IActionResult> OnPostAsync(List<Item> items)
    {
        // ✅ SECURITY FIX: Input validation
        if (items == null || !items.Any())
        {
            ModelState.AddModelError("", "No items provided");
            await OnGetAsync();
            return Page();
        }

        var companyId = _companyContext.GetCompanyIdOrThrow();
        var ids = items.Select(i => i.Id).ToList();

        // Query filter ensures we only get ShiftTypes for current company
        var types = await _db.ShiftTypes.Where(s => ids.Contains(s.Id)).ToListAsync();

        // Validate all items before making any changes
        foreach (var it in items)
        {
            // Check if shift type exists and belongs to user's company
            if (!types.Any(t => t.Id == it.Id))
            {
                ModelState.AddModelError("", $"Shift type ID {it.Id} not found or unauthorized");
                await OnGetAsync();
                return Page();
            }

            // Validate Key (must not be empty, max 50 chars)
            if (string.IsNullOrWhiteSpace(it.Key) || it.Key.Length > 50)
            {
                ModelState.AddModelError("", $"Shift key must be 1-50 characters");
                await OnGetAsync();
                return Page();
            }

            // Validate Name (must not be empty, max 100 chars)
            if (string.IsNullOrWhiteSpace(it.Name) || it.Name.Length > 100)
            {
                ModelState.AddModelError("", $"Shift name must be 1-100 characters");
                await OnGetAsync();
                return Page();
            }

            // Validate time formats
            if (!TimeOnly.TryParse(it.Start, out _) || !TimeOnly.TryParse(it.End, out _))
            {
                ModelState.AddModelError("", $"Invalid time format for shift {it.Name}");
                await OnGetAsync();
                return Page();
            }
        }

        // All validation passed - apply changes
        foreach (var it in items)
        {
            var t = types.First(x => x.Id == it.Id);
            t.Key = it.Key.Trim();
            t.Name = it.Name.Trim();
            t.Start = TimeOnly.Parse(it.Start);
            t.End = TimeOnly.Parse(it.End);
        }

        await _db.SaveChangesAsync();
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var companyId = _companyContext.GetCompanyIdOrThrow();

        // Query filter ensures we only get ShiftType for current company
        var shiftType = await _db.ShiftTypes.FirstOrDefaultAsync(s => s.Id == id);

        if (shiftType == null)
        {
            ModelState.AddModelError("", "Shift type not found or unauthorized");
            await OnGetAsync();
            return Page();
        }

        // Check if any shift instances are using this shift type
        var hasInstances = await _db.ShiftInstances.AnyAsync(si => si.ShiftTypeId == id);

        if (hasInstances)
        {
            ModelState.AddModelError("", $"Cannot delete shift type '{shiftType.Name}' because it is being used in shift instances");
            await OnGetAsync();
            return Page();
        }

        _db.ShiftTypes.Remove(shiftType);
        await _db.SaveChangesAsync();

        return RedirectToPage();
    }
}
