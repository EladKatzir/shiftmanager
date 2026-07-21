using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

// SECURITY-AUDITED: IgnoreQueryFilters() here is SAFE — ShiftTab/ShiftTabCompany/ShiftTabShiftType/ShiftType
// are molecule-scoped (no tenant filter); writes are scoped by explicit ids and every membership set enforces
// a same-molecule (and, for shift types, same-jobtype) invariant. The admin page re-verifies the
// ManageCalendarTabs grant against the molecule before every call.
public class ShiftTabService : IShiftTabService
{
    private readonly AppDbContext _db;

    public ShiftTabService(AppDbContext db) => _db = db;

    // ---- Tab queries ----

    public async Task<List<ShiftTab>> GetTabsForMoleculeAsync(int moleculeId, int? jobTypeId, bool includeInactive = false)
    {
        var q = _db.ShiftTabs.Where(t => t.MoleculeId == moleculeId && t.JobTypeId == jobTypeId);
        if (!includeInactive)
            q = q.Where(t => t.IsActive);
        return await q.OrderBy(t => t.SortOrder).ThenBy(t => t.NameEn).ToListAsync();
    }

    public Task<ShiftTab?> GetTabAsync(int tabId)
        => _db.ShiftTabs.FirstOrDefaultAsync(t => t.Id == tabId);

    // ---- Tab CRUD ----

    public async Task<ShiftTab?> CreateAsync(int moleculeId, int? jobTypeId, string nameEn, string nameHe,
        string? color = null, int? createdByUserId = null)
    {
        nameEn = (nameEn ?? string.Empty).Trim();
        nameHe = (nameHe ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(nameEn) || string.IsNullOrWhiteSpace(nameHe))
            return null;

        // Dual-name uniqueness within (molecule, jobtype), before hitting the DB index → clean null.
        var clash = await _db.ShiftTabs.AnyAsync(t => t.MoleculeId == moleculeId && t.JobTypeId == jobTypeId
            && (t.NameEn == nameEn || t.NameHe == nameHe));
        if (clash)
            return null;

        var nextSort = await _db.ShiftTabs.Where(t => t.MoleculeId == moleculeId && t.JobTypeId == jobTypeId)
            .Select(t => (int?)t.SortOrder).MaxAsync() ?? -1;

        var tab = new ShiftTab
        {
            MoleculeId = moleculeId,
            JobTypeId = jobTypeId,
            NameEn = nameEn,
            NameHe = nameHe,
            Color = string.IsNullOrWhiteSpace(color) ? null : color.Trim(),
            PrioritizeCompanyUsers = true,   // UD3
            SortOrder = nextSort + 1,
            IsActive = true,
            CreatedByUserId = createdByUserId
        };
        _db.ShiftTabs.Add(tab);
        await _db.SaveChangesAsync();
        return tab;
    }

    public async Task<bool> RenameAsync(int tabId, string nameEn, string nameHe, string? color, bool prioritizeCompanyUsers)
    {
        var tab = await _db.ShiftTabs.FirstOrDefaultAsync(t => t.Id == tabId);
        if (tab == null)
            return false;

        nameEn = (nameEn ?? string.Empty).Trim();
        nameHe = (nameHe ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(nameEn) || string.IsNullOrWhiteSpace(nameHe))
            return false;

        var clash = await _db.ShiftTabs.AnyAsync(t => t.MoleculeId == tab.MoleculeId && t.JobTypeId == tab.JobTypeId
            && t.Id != tabId && (t.NameEn == nameEn || t.NameHe == nameHe));
        if (clash)
            return false;

        tab.NameEn = nameEn;
        tab.NameHe = nameHe;
        tab.Color = string.IsNullOrWhiteSpace(color) ? null : color.Trim();
        tab.PrioritizeCompanyUsers = prioritizeCompanyUsers;
        tab.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int tabId)
    {
        var tab = await _db.ShiftTabs.FirstOrDefaultAsync(t => t.Id == tabId);
        if (tab == null)
            return false;

        // FK behavior handles the rest: ShiftTabCompany + ShiftTabShiftType → Cascade,
        // UserShiftTabPreference.TabId → SetNull. Shift instances/assignments are untouched.
        _db.ShiftTabs.Remove(tab);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<(int ShiftTypeCount, int CompanyCount)> GetUsageAsync(int tabId)
    {
        var shiftTypeCount = await _db.ShiftTabShiftTypes.CountAsync(x => x.ShiftTabId == tabId);
        var companyCount = await _db.ShiftTabCompanies.CountAsync(tc => tc.ShiftTabId == tabId);
        return (shiftTypeCount, companyCount);
    }

    // ---- Membership (replace-set) ----

    public async Task<bool> SetCompaniesForTabAsync(int tabId, IReadOnlyCollection<int> companyIds)
    {
        var tab = await _db.ShiftTabs.FirstOrDefaultAsync(t => t.Id == tabId);
        if (tab == null)
            return false;

        var ids = companyIds.Distinct().ToList();
        if (ids.Count > 0)
        {
            // C1 guard: every company must belong to the tab's molecule.
            var validCount = await _db.Companies.IgnoreQueryFilters()
                .CountAsync(c => ids.Contains(c.Id) && c.MoleculeId == tab.MoleculeId);
            if (validCount != ids.Count)
                return false;
        }

        var existing = await _db.ShiftTabCompanies.Where(tc => tc.ShiftTabId == tabId).ToListAsync();
        _db.ShiftTabCompanies.RemoveRange(existing.Where(e => !ids.Contains(e.CompanyId)));
        var have = existing.Select(e => e.CompanyId).ToHashSet();
        foreach (var cid in ids.Where(cid => !have.Contains(cid)))
            _db.ShiftTabCompanies.Add(new ShiftTabCompany { ShiftTabId = tabId, CompanyId = cid });

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetShiftTypesForTabAsync(int tabId, IReadOnlyCollection<int> shiftTypeIds)
    {
        var tab = await _db.ShiftTabs.FirstOrDefaultAsync(t => t.Id == tabId);
        if (tab == null)
            return false;

        var ids = shiftTypeIds.Distinct().ToList();
        if (ids.Count > 0)
        {
            // Every shift type must be in the tab's molecule AND its jobtype scope (jobtype match or null).
            var validCount = await _db.ShiftTypes.IgnoreQueryFilters()
                .CountAsync(st => ids.Contains(st.Id) && st.MoleculeId == tab.MoleculeId
                    && (st.JobTypeId == tab.JobTypeId || st.JobTypeId == null));
            if (validCount != ids.Count)
                return false;
        }

        var existing = await _db.ShiftTabShiftTypes.Where(x => x.ShiftTabId == tabId).ToListAsync();
        _db.ShiftTabShiftTypes.RemoveRange(existing.Where(e => !ids.Contains(e.ShiftTypeId)));
        var have = existing.Select(e => e.ShiftTypeId).ToHashSet();
        foreach (var sid in ids.Where(sid => !have.Contains(sid)))
            _db.ShiftTabShiftTypes.Add(new ShiftTabShiftType { ShiftTabId = tabId, ShiftTypeId = sid });

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<HashSet<int>> GetCompanyIdsForTabAsync(int tabId)
        => (await _db.ShiftTabCompanies.Where(tc => tc.ShiftTabId == tabId)
            .Select(tc => tc.CompanyId).ToListAsync()).ToHashSet();

    public async Task<HashSet<int>> GetShiftTypeIdsForTabAsync(int tabId)
        => (await _db.ShiftTabShiftTypes.Where(x => x.ShiftTabId == tabId)
            .Select(x => x.ShiftTypeId).ToListAsync()).ToHashSet();

    // ---- Per-user last-tab memory ----

    public async Task<int?> GetLastTabAsync(int userId, int moleculeId, int? jobTypeId)
        => (await _db.UserShiftTabPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId && p.MoleculeId == moleculeId && p.JobTypeId == jobTypeId))?.TabId;

    public async Task SetLastTabAsync(int userId, int moleculeId, int? jobTypeId, int? tabId)
    {
        var pref = await _db.UserShiftTabPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId && p.MoleculeId == moleculeId && p.JobTypeId == jobTypeId);
        if (pref == null)
        {
            _db.UserShiftTabPreferences.Add(new UserShiftTabPreference
            {
                UserId = userId,
                MoleculeId = moleculeId,
                JobTypeId = jobTypeId,
                TabId = tabId,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            pref.TabId = tabId;
            pref.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
    }
}
