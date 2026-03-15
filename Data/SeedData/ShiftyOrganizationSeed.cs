using Microsoft.EntityFrameworkCore;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Data.SeedData;

/// <summary>
/// Seeds the Shifty organization hierarchy:
/// Project: Shifty → Area: 190 → Molecules (Oren, Ella, Harava, Shaked, Gefen, Shikma, NOC, Shiklut, System)
/// with their companies, departments, job types, and shift groupings.
///
/// System molecule contains SystemAdmins company for administrative users (Owner, etc.)
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

        using var transaction = await db.Database.BeginTransactionAsync();
        try
        {

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

        // --- System Molecule (for admin users) ---
        var system = new Molecule { AreaId = area.Id, Name = "System", DisplayName = "מערכת", Type = MoleculeType.System };

        db.Molecules.AddRange(oren, ella, harava, shaked, gefen, shikma, noc, shiklut, system);
        await db.SaveChangesAsync();

        // Shikma-specific job type (organizational only — no shift eligibility impact)
        // Note: Hakam is the SAME area-wide job type used by all molecules — not duplicated here
        var shikmaProjectManager = new JobType { AreaId = area.Id, MoleculeId = shikma.Id, Name = "ProjectManager", DisplayName = "מנהל פרוייקט", SortOrder = 10 };
        db.JobTypes.Add(shikmaProjectManager);
        await db.SaveChangesAsync();

        // ============================================================
        // COMPANIES (for Workforce Molecules)
        // ============================================================

        // --- Oren Companies ---
        var orenCompanies = new List<Company>
        {
            new() { Name = "Tzafona", DisplayName = "צפונה", NameHe = "צפונה", MoleculeId = oren.Id },
            new() { Name = "Hir", DisplayName = "חיר", NameHe = "חיר", MoleculeId = oren.Id },
            new() { Name = "Camps", DisplayName = "מחנות", NameHe = "מחנות", MoleculeId = oren.Id },
            new() { Name = "City", DisplayName = "העיר", NameHe = "העיר", MoleculeId = oren.Id },
            new() { Name = "Radio", DisplayName = "טקטי", NameHe = "טקטי", MoleculeId = oren.Id }
        };
        db.Companies.AddRange(orenCompanies);

        // --- Ella Companies ---
        var ellaCompanies = new List<Company>
        {
            new() { Name = "Hitazmut", DisplayName = "התעצמות", NameHe = "התעצמות", MoleculeId = ella.Id },
            new() { Name = "GAP", DisplayName = "גא\"פ", NameHe = "גא\"פ", MoleculeId = ella.Id },
            new() { Name = "Yeadim", DisplayName = "יעדים", NameHe = "יעדים", MoleculeId = ella.Id }
        };
        db.Companies.AddRange(ellaCompanies);

        // --- Harava Companies ---
        var haravaCompanies = new List<Company>
        {
            new() { Name = "Element", DisplayName = "אלמנט", NameHe = "אלמנט", MoleculeId = harava.Id }
        };
        db.Companies.AddRange(haravaCompanies);

        // --- Shaked Companies ---
        var shakedCompanies = new List<Company>
        {
            new() { Name = "Inside", DisplayName = "פנים", NameHe = "פנים", MoleculeId = shaked.Id },
            new() { Name = "Out", DisplayName = "חוץ", NameHe = "חוץ", MoleculeId = shaked.Id }
        };
        db.Companies.AddRange(shakedCompanies);

        // --- Gefen Companies ---
        var gefenCompanies = new List<Company>
        {
            new() { Name = "Hamasa", DisplayName = "חמסה", NameHe = "חמסה", MoleculeId = gefen.Id },
            new() { Name = "Kabah", DisplayName = "קבה\"ח", NameHe = "קבה\"ח", MoleculeId = gefen.Id },
            new() { Name = "Matot", DisplayName = "מטות", NameHe = "מטות", MoleculeId = gefen.Id }
        };
        db.Companies.AddRange(gefenCompanies);

        // --- System Company (for admin users) ---
        var systemAdmins = new Company { Name = "SystemAdmins", DisplayName = "מנהלי מערכת", NameHe = "מנהלי מערכת", MoleculeId = system.Id };
        db.Companies.Add(systemAdmins);

        // --- HQ Companies (for Director auto-assignment per molecule) ---
        var hqMolecules = new[] { oren, ella, harava, shaked, gefen, shikma, noc, shiklut };
        foreach (var mol in hqMolecules)
        {
            db.Companies.Add(new Company
            {
                Name = "HQ",
                DisplayName = "כלל צוותי",
                NameHe = "כלל צוותי",
                Slug = $"hq-{mol.Name.ToLowerInvariant()}",
                MoleculeId = mol.Id,
                IsHeadquarters = true
            });
        }

        await db.SaveChangesAsync();

        // ============================================================
        // COMPANIES (for Tech Molecule - Shikma)
        // ============================================================
        var shikYekev = new Company { MoleculeId = shikma.Id, Name = "Yekev", DisplayName = "יקב", NameHe = "יקב" };
        var shikSnir = new Company { MoleculeId = shikma.Id, Name = "Snir", DisplayName = "שניר", NameHe = "שניר" };
        var shikArbel = new Company { MoleculeId = shikma.Id, Name = "Arbel", DisplayName = "ארבל", NameHe = "ארבל" };
        var shikPie = new Company { MoleculeId = shikma.Id, Name = "Pie", DisplayName = "פאי", NameHe = "פאי" };
        var shikSamapkamia = new Company { MoleculeId = shikma.Id, Name = "Samapkamia", DisplayName = "סמפקמיה", NameHe = "סמפקמיה" };
        var shikTao = new Company { MoleculeId = shikma.Id, Name = "Tao", DisplayName = "טאו", NameHe = "טאו" };
        db.Companies.AddRange(shikYekev, shikSnir, shikArbel, shikPie, shikSamapkamia, shikTao);
        await db.SaveChangesAsync();

        // ============================================================
        // TECH SHIFT TYPES (for Shikma)
        // ============================================================
        var hqShikma = db.Companies.Local.FirstOrDefault(c => c.MoleculeId == shikma.Id && c.IsHeadquarters)
            ?? await db.Companies.FirstOrDefaultAsync(c => c.MoleculeId == shikma.Id && c.IsHeadquarters);
        if (hqShikma != null)
        {
            var techShiftTypes = TechShiftTypeSeed.GetTechShiftTypes(hqShikma.Id, shikma.Id);

            // Set EligibleCompanyIds now that company IDs are known
            foreach (var st in techShiftTypes)
            {
                if (st.TechShiftType == ShiftType.TECH_HANAVA || st.TechShiftType == ShiftType.TECH_DELTA)
                {
                    st.EligibleCompanyIds = System.Text.Json.JsonSerializer.Serialize(
                        new[] { shikTao.Id, shikPie.Id, shikSamapkamia.Id });
                }
                else if (st.TechShiftType == ShiftType.TECH_YEKEV)
                {
                    st.EligibleCompanyIds = System.Text.Json.JsonSerializer.Serialize(
                        new[] { shikYekev.Id });
                }
                // MOVILTECH: EligibleCompanyIds stays null (all companies), RequiresOfficerRank = true (set in TechShiftTypeSeed)
            }

            db.ShiftTypes.AddRange(techShiftTypes);
            await db.SaveChangesAsync();
        }

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

        await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
