using Microsoft.EntityFrameworkCore;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Data.SeedData;

/// <summary>
/// Seeds the Shifty organization hierarchy:
/// Project: Shifty → Area: 190 → Molecules (Oren, Ella, Harava, Shaked, Shikma, NOC, Shiklut, Gefen)
/// with their companies, departments, job types, and shift groupings.
/// </summary>
public static class ShiftyOrganizationSeed
{
    public static async Task SeedAsync(AppDbContext db)
    {
        // Check if already seeded
        if (await db.Projects.AnyAsync(p => p.Name == "Shifty"))
        {
            return;
        }

        // ============================================================
        // PROJECT
        // ============================================================
        var project = new Project
        {
            Name = "Shifty",
            DisplayName = "שיפטי",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        // ============================================================
        // AREA
        // ============================================================
        var area = new Area
        {
            ProjectId = project.Id,
            Name = "190",
            DisplayName = "190",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Areas.Add(area);
        await db.SaveChangesAsync();

        // ============================================================
        // JOB TYPES (Area-scoped - shared across all workforce molecules)
        // ============================================================
        var jobTypes = new List<JobType>
        {
            new() { AreaId = area.Id, Name = "Alhut", DisplayName = "אלחוט", Color = "#3b82f6", SortOrder = 1 },
            new() { AreaId = area.Id, Name = "BR", DisplayName = "ב\"ר", Color = "#10b981", SortOrder = 2 },
            new() { AreaId = area.Id, Name = "Text", DisplayName = "טקסט", Color = "#8b5cf6", SortOrder = 3 },
            new() { AreaId = area.Id, Name = "Hakam", DisplayName = "חק\"ם", Color = "#f59e0b", SortOrder = 4 }
        };
        db.JobTypes.AddRange(jobTypes);
        await db.SaveChangesAsync();

        var alhut = jobTypes.First(j => j.Name == "Alhut");
        var br = jobTypes.First(j => j.Name == "BR");
        var text = jobTypes.First(j => j.Name == "Text");
        var hakam = jobTypes.First(j => j.Name == "Hakam");

        // ============================================================
        // MOLECULES
        // ============================================================

        // --- Workforce Molecules ---
        var oren = new Molecule { AreaId = area.Id, Name = "Oren", DisplayName = "אורן", Type = MoleculeType.Workforce };
        var ella = new Molecule { AreaId = area.Id, Name = "Ella", DisplayName = "אלה", Type = MoleculeType.Workforce };
        var harava = new Molecule { AreaId = area.Id, Name = "Harava", DisplayName = "ערבה", Type = MoleculeType.Workforce };
        var shaked = new Molecule { AreaId = area.Id, Name = "Shaked", DisplayName = "שקד", Type = MoleculeType.Workforce };
        var gefen = new Molecule { AreaId = area.Id, Name = "Gefen", DisplayName = "גפן", Type = MoleculeType.Workforce };

        // --- Tech Molecule ---
        var shikma = new Molecule { AreaId = area.Id, Name = "Shikma", DisplayName = "שקמה", Type = MoleculeType.Tech };

        // --- Helper Molecules ---
        var noc = new Molecule { AreaId = area.Id, Name = "NOC", DisplayName = "נגדים", Type = MoleculeType.Helper };
        var shiklut = new Molecule { AreaId = area.Id, Name = "Shiklut", DisplayName = "שקלוט", Type = MoleculeType.Helper };

        db.Molecules.AddRange(oren, ella, harava, shaked, gefen, shikma, noc, shiklut);
        await db.SaveChangesAsync();

        // ============================================================
        // COMPANIES (for Workforce Molecules)
        // ============================================================

        // --- Oren Companies ---
        var orenCompanies = new List<Company>
        {
            new() { Name = "Tzafona", DisplayName = "צפונה", MoleculeId = oren.Id },
            new() { Name = "Hir", DisplayName = "חיר", MoleculeId = oren.Id },
            new() { Name = "Camps", DisplayName = "מחנות", MoleculeId = oren.Id },
            new() { Name = "City", DisplayName = "העיר", MoleculeId = oren.Id },
            new() { Name = "Radio", DisplayName = "טקטי", MoleculeId = oren.Id }
        };
        db.Companies.AddRange(orenCompanies);

        // --- Ella Companies ---
        var ellaCompanies = new List<Company>
        {
            new() { Name = "Hitazmut", DisplayName = "התעצמות", MoleculeId = ella.Id },
            new() { Name = "GAP", DisplayName = "גא\"פ", MoleculeId = ella.Id },
            new() { Name = "Yeadim", DisplayName = "יעדים", MoleculeId = ella.Id }
        };
        db.Companies.AddRange(ellaCompanies);

        // --- Harava Companies ---
        var haravaCompanies = new List<Company>
        {
            new() { Name = "Element", DisplayName = "אלמנט", MoleculeId = harava.Id }
        };
        db.Companies.AddRange(haravaCompanies);

        // --- Shaked Companies ---
        var shakedCompanies = new List<Company>
        {
            new() { Name = "Inside", DisplayName = "פנים", MoleculeId = shaked.Id },
            new() { Name = "Out", DisplayName = "חוץ", MoleculeId = shaked.Id }
        };
        db.Companies.AddRange(shakedCompanies);

        // --- Gefen Companies ---
        var gefenCompanies = new List<Company>
        {
            new() { Name = "Hamasa", DisplayName = "חמסה", MoleculeId = gefen.Id },
            new() { Name = "Kabah", DisplayName = "קבה\"ח", MoleculeId = gefen.Id },
            new() { Name = "Matot", DisplayName = "מטות", MoleculeId = gefen.Id }
        };
        db.Companies.AddRange(gefenCompanies);

        await db.SaveChangesAsync();

        // ============================================================
        // DEPARTMENTS (for Tech Molecule - Shikma)
        // ============================================================
        var shikmaDepartments = new List<Department>
        {
            new() { MoleculeId = shikma.Id, Name = "Pie", DisplayName = "פאי" },
            new() { MoleculeId = shikma.Id, Name = "Tao", DisplayName = "טאו" },
            new() { MoleculeId = shikma.Id, Name = "Yekeb", DisplayName = "יקב" },
            new() { MoleculeId = shikma.Id, Name = "Snir", DisplayName = "שניר" },
            new() { MoleculeId = shikma.Id, Name = "Arbel", DisplayName = "ארבל" },
            new() { MoleculeId = shikma.Id, Name = "Samapkam", DisplayName = "סמפקמה" }
        };
        db.Departments.AddRange(shikmaDepartments);
        await db.SaveChangesAsync();

        // ============================================================
        // SHIFT GROUPINGS (for Oren - Tzafon/Darom/Tacti structure)
        // ============================================================

        // Oren Shift Groupings:
        // - Tzafon: Tzafona + City
        // - Darom: Camps + Hir
        // - Tacti: Radio
        var tzafona = orenCompanies.First(c => c.Name == "Tzafona");
        var hir = orenCompanies.First(c => c.Name == "Hir");
        var camps = orenCompanies.First(c => c.Name == "Camps");
        var city = orenCompanies.First(c => c.Name == "City");
        var radio = orenCompanies.First(c => c.Name == "Radio");

        var tzafonGrouping = new ShiftGrouping
        {
            MoleculeId = oren.Id,
            Name = "Tzafon",
            DisplayName = "צפון"
        };
        var daromGrouping = new ShiftGrouping
        {
            MoleculeId = oren.Id,
            Name = "Darom",
            DisplayName = "דרום"
        };
        var tactiGrouping = new ShiftGrouping
        {
            MoleculeId = oren.Id,
            Name = "Tacti",
            DisplayName = "טקטי"
        };

        db.ShiftGroupings.AddRange(tzafonGrouping, daromGrouping, tactiGrouping);
        await db.SaveChangesAsync();

        // Link companies to shift groupings
        db.Set<ShiftGroupingCompany>().AddRange(
            new ShiftGroupingCompany { ShiftGroupingId = tzafonGrouping.Id, CompanyId = tzafona.Id },
            new ShiftGroupingCompany { ShiftGroupingId = tzafonGrouping.Id, CompanyId = city.Id },
            new ShiftGroupingCompany { ShiftGroupingId = daromGrouping.Id, CompanyId = camps.Id },
            new ShiftGroupingCompany { ShiftGroupingId = daromGrouping.Id, CompanyId = hir.Id },
            new ShiftGroupingCompany { ShiftGroupingId = tactiGrouping.Id, CompanyId = radio.Id }
        );

        // Link job types to shift groupings (Alhut and Text use these groupings)
        db.Set<ShiftGroupingJobType>().AddRange(
            // Tzafon grouping - Alhut and Text
            new ShiftGroupingJobType { ShiftGroupingId = tzafonGrouping.Id, JobTypeId = alhut.Id },
            new ShiftGroupingJobType { ShiftGroupingId = tzafonGrouping.Id, JobTypeId = text.Id },
            // Darom grouping - Alhut and Text
            new ShiftGroupingJobType { ShiftGroupingId = daromGrouping.Id, JobTypeId = alhut.Id },
            new ShiftGroupingJobType { ShiftGroupingId = daromGrouping.Id, JobTypeId = text.Id },
            // Tacti grouping - Alhut and Text
            new ShiftGroupingJobType { ShiftGroupingId = tactiGrouping.Id, JobTypeId = alhut.Id },
            new ShiftGroupingJobType { ShiftGroupingId = tactiGrouping.Id, JobTypeId = text.Id }
        );

        await db.SaveChangesAsync();

        // ============================================================
        // GEFEN SHIFT GROUPINGS (Hamasa + Kabah combined structure)
        // ============================================================
        var hamasa = gefenCompanies.First(c => c.Name == "Hamasa");
        var kabah = gefenCompanies.First(c => c.Name == "Kabah");
        var matot = gefenCompanies.First(c => c.Name == "Matot");

        var gefenHamasaKabahGrouping = new ShiftGrouping
        {
            MoleculeId = gefen.Id,
            Name = "HamasaKabah",
            DisplayName = "חמסה+קבה\"ח"
        };
        var gefenMatotGrouping = new ShiftGrouping
        {
            MoleculeId = gefen.Id,
            Name = "Matot",
            DisplayName = "מטות"
        };

        db.ShiftGroupings.AddRange(gefenHamasaKabahGrouping, gefenMatotGrouping);
        await db.SaveChangesAsync();

        db.Set<ShiftGroupingCompany>().AddRange(
            new ShiftGroupingCompany { ShiftGroupingId = gefenHamasaKabahGrouping.Id, CompanyId = hamasa.Id },
            new ShiftGroupingCompany { ShiftGroupingId = gefenHamasaKabahGrouping.Id, CompanyId = kabah.Id },
            new ShiftGroupingCompany { ShiftGroupingId = gefenMatotGrouping.Id, CompanyId = matot.Id }
        );

        // Link all jobtypes (except Hakam which is area-wide)
        db.Set<ShiftGroupingJobType>().AddRange(
            new ShiftGroupingJobType { ShiftGroupingId = gefenHamasaKabahGrouping.Id, JobTypeId = alhut.Id },
            new ShiftGroupingJobType { ShiftGroupingId = gefenHamasaKabahGrouping.Id, JobTypeId = text.Id },
            new ShiftGroupingJobType { ShiftGroupingId = gefenHamasaKabahGrouping.Id, JobTypeId = br.Id },
            new ShiftGroupingJobType { ShiftGroupingId = gefenMatotGrouping.Id, JobTypeId = alhut.Id },
            new ShiftGroupingJobType { ShiftGroupingId = gefenMatotGrouping.Id, JobTypeId = text.Id },
            new ShiftGroupingJobType { ShiftGroupingId = gefenMatotGrouping.Id, JobTypeId = br.Id }
        );

        await db.SaveChangesAsync();

        // ============================================================
        // ELLA SHIFT GROUPINGS (Hakam joint: Hitazmut + Yeadim)
        // ============================================================
        var hitazmut = ellaCompanies.First(c => c.Name == "Hitazmut");
        var yeadim = ellaCompanies.First(c => c.Name == "Yeadim");

        var ellaHakamGrouping = new ShiftGrouping
        {
            MoleculeId = ella.Id,
            Name = "HitazmutYeadim",
            DisplayName = "התעצמות+יעדים"
        };
        db.ShiftGroupings.Add(ellaHakamGrouping);
        await db.SaveChangesAsync();

        db.Set<ShiftGroupingCompany>().AddRange(
            new ShiftGroupingCompany { ShiftGroupingId = ellaHakamGrouping.Id, CompanyId = hitazmut.Id },
            new ShiftGroupingCompany { ShiftGroupingId = ellaHakamGrouping.Id, CompanyId = yeadim.Id }
        );
        db.Set<ShiftGroupingJobType>().Add(
            new ShiftGroupingJobType { ShiftGroupingId = ellaHakamGrouping.Id, JobTypeId = hakam.Id }
        );

        await db.SaveChangesAsync();

        // ============================================================
        // AREA SETTINGS (default settings for the area)
        // ============================================================
        var areaSettings = new AreaSettings
        {
            AreaId = area.Id,
            DefaultRestHours = 8,
            DefaultWeeklyCap = 56,
            UpdatedAt = DateTime.UtcNow
        };
        db.AreaSettings.Add(areaSettings);
        await db.SaveChangesAsync();
    }
}
