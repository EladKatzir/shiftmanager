using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;

namespace ShiftManager.Pages.Api.Hierarchy;

[Authorize]
public class GetCompaniesModel : PageModel
{
    private readonly AppDbContext _db;

    public GetCompaniesModel(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> OnGetAsync(int moleculeId)
    {
        if (moleculeId <= 0)
            return new JsonResult(new { error = "Invalid molecule ID" }) { StatusCode = 400 };

        var companies = await _db.Companies
            .Where(c => c.MoleculeId == moleculeId && !c.IsHeadquarters)
            .OrderBy(c => c.DisplayName ?? c.Name)
            .Select(c => new { id = c.Id, name = c.DisplayName ?? c.Name })
            .ToListAsync();

        return new JsonResult(new { companies });
    }
}
