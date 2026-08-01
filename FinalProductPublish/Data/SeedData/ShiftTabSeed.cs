using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Services;

namespace ShiftManager.Data.SeedData;

/// <summary>
/// Dev/Test-only example calendar tabs (לשונית) for (Oren, Alhut): "Geo" {Tzafona, City, Camps, Hir} and
/// "Tacti" {Radio}. Idempotent — skips a tab whose (molecule, jobtype, NameEn) already exists. Shift-type
/// membership is left empty (= all Alhut shift types) so the example reads cleanly.
/// </summary>
// SECURITY-AUDITED: IgnoreQueryFilters() is SAFE — dev-only test data, guarded by the environment check.
public static class ShiftTabSeed
{
    private static readonly (string NameEn, string NameHe, string[] Companies)[] OrenAlhutTabs =
    {
        ("Geo", "גאו", new[] { "Tzafona", "City", "Camps", "Hir" }),
        ("Tacti", "טקטי", new[] { "Radio" })
    };

    public static async Task SeedAsync(AppDbContext db, IShiftTabService tabService, ILogger logger)
    {
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (env != "Development" && env != "Test") return;

        var oren = await db.Molecules.FirstOrDefaultAsync(m => m.Name == "Oren");
        var alhut = await db.JobTypes.FirstOrDefaultAsync(j => j.Name == "Alhut" && j.MoleculeId == null);
        if (oren == null || alhut == null)
        {
            logger.LogInformation("[ShiftTabSeed] Skipping — Oren molecule or Alhut job type not found");
            return;
        }

        var companyByName = await db.Companies.IgnoreQueryFilters()
            .Where(c => c.MoleculeId == oren.Id)
            .ToDictionaryAsync(c => c.Name, c => c.Id);

        int created = 0;
        foreach (var (nameEn, nameHe, companyNames) in OrenAlhutTabs)
        {
            var exists = await db.ShiftTabs.AnyAsync(t => t.MoleculeId == oren.Id && t.JobTypeId == alhut.Id && t.NameEn == nameEn);
            if (exists) continue;

            var tab = await tabService.CreateAsync(oren.Id, alhut.Id, nameEn, nameHe);
            if (tab == null) continue;
            var ids = companyNames.Where(companyByName.ContainsKey).Select(n => companyByName[n]).ToList();
            await tabService.SetCompaniesForTabAsync(tab.Id, ids);
            created++;
        }

        if (created > 0)
            logger.LogInformation("[ShiftTabSeed] Created {Count} example (Oren, Alhut) tabs", created);
    }
}
