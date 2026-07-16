using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

// SECURITY-AUDITED: IgnoreQueryFilters() here is SAFE — ShiftTab/ShiftTabCompany/ShiftType are molecule-scoped
// (no tenant filter); writes are scoped by explicit ids and every assign enforces a same-molecule invariant
// (a company/shift can only join a tab in its own molecule). Callers (Blueprints) re-verify the
// ManageShiftCategories grant against the molecule.
public class ShiftTabService : IShiftTabService
{
    private readonly AppDbContext _db;

    public ShiftTabService(AppDbContext db) => _db = db;

    // ---- Tab queries ----

    public async Task<List<ShiftTab>> GetTabsForMoleculeAsync(int moleculeId, bool includeInactive = false)
    {
        var q = _db.ShiftTabs.Where(t => t.MoleculeId == moleculeId);
        if (!includeInactive)
            q = q.Where(t => t.IsActive);
        return await q.OrderBy(t => t.SortOrder).ThenBy(t => t.DisplayName).ToListAsync();
    }

    public Task<ShiftTab?> GetTabAsync(int tabId)
        => _db.ShiftTabs.FirstOrDefaultAsync(t => t.Id == tabId);

    // ---- Tab CRUD ----

    public async Task<ShiftTab?> CreateAsync(int moleculeId, string name, string displayName, string? color = null)
    {
        name = name.Trim();
        displayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return null;

        // Enforce (molecule, name) uniqueness before hitting the DB index so the caller gets a clean null.
        var exists = await _db.ShiftTabs.AnyAsync(t => t.MoleculeId == moleculeId && t.Name == name);
        if (exists)
            return null;

        var nextSort = await _db.ShiftTabs.Where(t => t.MoleculeId == moleculeId)
            .Select(t => (int?)t.SortOrder).MaxAsync() ?? -1;

        var tab = new ShiftTab
        {
            MoleculeId = moleculeId,
            Name = name,
            DisplayName = displayName,
            Color = string.IsNullOrWhiteSpace(color) ? null : color.Trim(),
            SortOrder = nextSort + 1,
            IsActive = true
        };
        _db.ShiftTabs.Add(tab);
        await _db.SaveChangesAsync();
        return tab;
    }

    public async Task<bool> RenameAsync(int tabId, string name, string displayName, string? color)
    {
        var tab = await _db.ShiftTabs.FirstOrDefaultAsync(t => t.Id == tabId);
        if (tab == null)
            return false;

        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (!string.Equals(tab.Name, name, StringComparison.Ordinal))
        {
            var clash = await _db.ShiftTabs
                .AnyAsync(t => t.MoleculeId == tab.MoleculeId && t.Name == name && t.Id != tabId);
            if (clash)
                return false;
        }

        tab.Name = name;
        tab.DisplayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName.Trim();
        tab.Color = string.IsNullOrWhiteSpace(color) ? null : color.Trim();
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int tabId)
    {
        var tab = await _db.ShiftTabs.FirstOrDefaultAsync(t => t.Id == tabId);
        if (tab == null)
            return false;

        // FK behavior handles the rest: ShiftType.TabId → SetNull (back to Main), ShiftTabCompany → Cascade,
        // UserShiftTabPreference.TabId → SetNull. Shift instances/assignments are untouched.
        _db.ShiftTabs.Remove(tab);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ReorderTabsAsync(int moleculeId, IReadOnlyList<int> orderedTabIds)
    {
        var tabs = await _db.ShiftTabs.Where(t => t.MoleculeId == moleculeId).ToListAsync();
        var byId = tabs.ToDictionary(t => t.Id);

        // Reject if any provided id isn't a tab in this molecule (defensive against forged/cross-molecule ids).
        if (orderedTabIds.Any(id => !byId.ContainsKey(id)))
            return false;

        var order = 0;
        foreach (var id in orderedTabIds)
            byId[id].SortOrder = order++;

        // Any tabs not listed keep a stable position after the listed ones.
        var listed = orderedTabIds.ToHashSet();
        foreach (var t in tabs.Where(t => !listed.Contains(t.Id)).OrderBy(t => t.SortOrder))
            t.SortOrder = order++;

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<(int ShiftTypeCount, int CompanyCount)> GetUsageAsync(int tabId)
    {
        var shiftTypeCount = await _db.ShiftTypes.IgnoreQueryFilters().CountAsync(st => st.TabId == tabId);
        var companyCount = await _db.ShiftTabCompanies.CountAsync(tc => tc.ShiftTabId == tabId);
        return (shiftTypeCount, companyCount);
    }

    // ---- Membership assignment ----

    public async Task<bool> AssignShiftTypeToTabAsync(int shiftTypeId, int? tabId)
    {
        var shiftType = await _db.ShiftTypes.IgnoreQueryFilters()
            .FirstOrDefaultAsync(st => st.Id == shiftTypeId);
        if (shiftType == null)
            return false;

        // Area-scoped shifts span multiple molecules; a tab is molecule-scoped. They always stay on Main.
        if (shiftType.Scope == ShiftScope.Area)
            return false;

        if (tabId == null)
        {
            shiftType.TabId = null;
            await _db.SaveChangesAsync();
            return true;
        }

        var tab = await _db.ShiftTabs.FirstOrDefaultAsync(t => t.Id == tabId.Value);
        if (tab == null)
            return false;

        // The tab must belong to the molecule the shift type resolves to (direct, or via its company).
        var shiftMoleculeId = shiftType.MoleculeId
            ?? await _db.Companies.IgnoreQueryFilters()
                .Where(c => c.Id == shiftType.CompanyId)
                .Select(c => c.MoleculeId)
                .FirstOrDefaultAsync();
        if (shiftMoleculeId != tab.MoleculeId)
            return false;

        shiftType.TabId = tabId;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> AssignCompanyToTabAsync(int companyId, int? tabId)
    {
        var existing = await _db.ShiftTabCompanies.FirstOrDefaultAsync(tc => tc.CompanyId == companyId);

        if (tabId == null)
        {
            // Clear → company returns to Main.
            if (existing != null)
            {
                _db.ShiftTabCompanies.Remove(existing);
                await _db.SaveChangesAsync();
            }
            return true;
        }

        var tab = await _db.ShiftTabs.FirstOrDefaultAsync(t => t.Id == tabId.Value);
        if (tab == null)
            return false;

        // C1 guard: the company must belong to the tab's molecule (else molecule-B rosters leak into A).
        var companyMoleculeId = await _db.Companies.IgnoreQueryFilters()
            .Where(c => c.Id == companyId)
            .Select(c => c.MoleculeId)
            .FirstOrDefaultAsync();
        if (companyMoleculeId != tab.MoleculeId)
            return false;

        if (existing != null)
            existing.ShiftTabId = tabId.Value;   // move (upsert — respects UNIQUE(CompanyId))
        else
            _db.ShiftTabCompanies.Add(new ShiftTabCompany { CompanyId = companyId, ShiftTabId = tabId.Value });

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<HashSet<int>> GetCompanyIdsForTabAsync(int moleculeId, int? tabId)
    {
        if (tabId.HasValue)
        {
            // Companies explicitly assigned to this tab (and the tab must be in this molecule — defense).
            var ids = await _db.ShiftTabCompanies
                .Where(tc => tc.ShiftTabId == tabId.Value && tc.ShiftTab.MoleculeId == moleculeId)
                .Select(tc => tc.CompanyId)
                .ToListAsync();
            return ids.ToHashSet();
        }

        // Main = the molecule's companies with no tab assignment (to any tab in this molecule).
        var moleculeCompanyIds = await _db.Companies.IgnoreQueryFilters()
            .Where(c => c.MoleculeId == moleculeId)
            .Select(c => c.Id)
            .ToListAsync();
        var claimed = (await _db.ShiftTabCompanies
            .Where(tc => tc.ShiftTab.MoleculeId == moleculeId)
            .Select(tc => tc.CompanyId)
            .ToListAsync()).ToHashSet();
        return moleculeCompanyIds.Where(id => !claimed.Contains(id)).ToHashSet();
    }

    // ---- Per-user last-tab memory ----

    public async Task<int?> GetLastTabAsync(int userId, int moleculeId)
        => (await _db.UserShiftTabPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId && p.MoleculeId == moleculeId))?.TabId;

    public async Task SetLastTabAsync(int userId, int moleculeId, int? tabId)
    {
        var pref = await _db.UserShiftTabPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId && p.MoleculeId == moleculeId);
        if (pref == null)
        {
            _db.UserShiftTabPreferences.Add(new UserShiftTabPreference
            {
                UserId = userId,
                MoleculeId = moleculeId,
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
