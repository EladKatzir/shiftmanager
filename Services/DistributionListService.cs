// Services/DistributionListService.cs
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — distribution lists are molecule-scoped by
// design (shared across all companies in a molecule), so every query is re-scoped by an explicit moleculeId
// (reads) or by the persisted list's own MoleculeId (mutations). Mutations additionally re-verify the caller's
// ManageDistributionLists grant against that molecule. Same pattern as ShiftCalendarService.
public class DistributionListService : IDistributionListService
{
    private const string GrantKey = "ManageDistributionLists";

    private readonly AppDbContext _db;
    private readonly IGrantService _grantService;
    private readonly ILogger<DistributionListService> _logger;

    public DistributionListService(AppDbContext db, IGrantService grantService, ILogger<DistributionListService> logger)
    {
        _db = db;
        _grantService = grantService;
        _logger = logger;
    }

    public async Task<List<DistributionListSummary>> GetListsForMoleculeAsync(int moleculeId)
    {
        return await _db.DistributionLists
            .IgnoreQueryFilters()
            .Where(l => l.MoleculeId == moleculeId)
            .OrderBy(l => l.Name)
            .Select(l => new DistributionListSummary(l.Id, l.Name, l.Members.Count))
            .ToListAsync();
    }

    public async Task<List<DistributionListWithMembers>> GetListsWithMembersAsync(IEnumerable<int> listIds, int moleculeId)
    {
        var ids = listIds.Distinct().ToList();
        if (ids.Count == 0)
            return new List<DistributionListWithMembers>();

        var lists = await _db.DistributionLists
            .IgnoreQueryFilters()
            .Where(l => l.MoleculeId == moleculeId && ids.Contains(l.Id))
            .OrderBy(l => l.Name)
            .Select(l => new DistributionListWithMembers(
                l.Id,
                l.Name,
                l.Members.Select(m => m.UserId).ToList()))
            .ToListAsync();

        return lists;
    }

    public async Task<DistributionListDetail?> GetListDetailAsync(int listId, int moleculeId)
    {
        return await _db.DistributionLists
            .IgnoreQueryFilters()
            .Where(l => l.Id == listId && l.MoleculeId == moleculeId)
            .Select(l => new DistributionListDetail(
                l.Id,
                l.Name,
                l.MoleculeId,
                l.Members.Select(m => m.UserId).ToList()))
            .FirstOrDefaultAsync();
    }

    public async Task<Dictionary<int, List<string>>> GetMembershipNamesAsync(int moleculeId)
    {
        // One flat query of (UserId, ListName) for every membership in the molecule's lists, grouped client-side.
        var rows = await _db.DistributionListMembers
            .IgnoreQueryFilters()
            .Where(m => m.DistributionList.MoleculeId == moleculeId)
            .Select(m => new { m.UserId, ListName = m.DistributionList.Name })
            .ToListAsync();

        return rows
            .GroupBy(r => r.UserId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => r.ListName).OrderBy(n => n).ToList());
    }

    public async Task<DistributionListResult> CreateAsync(int actingUserId, int moleculeId, string name, IReadOnlyCollection<int> userIds)
    {
        if (!await CanManageAsync(actingUserId, moleculeId))
            return new DistributionListResult(DistributionListOutcome.Forbidden);

        var trimmed = (name ?? string.Empty).Trim();
        var validation = await ValidateAsync(moleculeId, trimmed, userIds, excludeListId: null);
        if (validation.Outcome != DistributionListOutcome.Ok)
            return validation;

        var distinctUserIds = userIds.Distinct().ToList();
        var list = new DistributionList
        {
            MoleculeId = moleculeId,           // interceptor stamps CompanyId; MoleculeId must be set explicitly
            Name = trimmed,
            CreatedBy = actingUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        foreach (var uid in distinctUserIds)
            list.Members.Add(new DistributionListMember { UserId = uid, AddedAt = DateTime.UtcNow });

        _db.DistributionLists.Add(list);
        try
        {
            await _db.SaveChangesAsync(); // single SaveChanges = atomic (list + members)
        }
        catch (DbUpdateException ex)
        {
            // Lost a race against the (MoleculeId, Name) unique index — surface as NameTaken.
            _logger.LogWarning(ex, "DistributionList create hit unique constraint (molecule {MoleculeId}, name {Name})", moleculeId, trimmed);
            return new DistributionListResult(DistributionListOutcome.NameTaken);
        }

        _logger.LogInformation("DistributionList {ListId} created in molecule {MoleculeId} by user {UserId} with {Count} members",
            list.Id, moleculeId, actingUserId, distinctUserIds.Count);
        return new DistributionListResult(DistributionListOutcome.Ok, list.Id);
    }

    public async Task<DistributionListResult> UpdateAsync(int actingUserId, int listId, string name, IReadOnlyCollection<int> userIds)
    {
        var list = await _db.DistributionLists
            .IgnoreQueryFilters()
            .Include(l => l.Members)
            .FirstOrDefaultAsync(l => l.Id == listId);

        if (list == null)
            return new DistributionListResult(DistributionListOutcome.NotFound);

        // IDOR guard: authorize against the LIST's stored molecule, never a caller-supplied value.
        if (!await CanManageAsync(actingUserId, list.MoleculeId))
            return new DistributionListResult(DistributionListOutcome.Forbidden);

        var trimmed = (name ?? string.Empty).Trim();
        var validation = await ValidateAsync(list.MoleculeId, trimmed, userIds, excludeListId: listId);
        if (validation.Outcome != DistributionListOutcome.Ok)
            return validation;

        var newSet = userIds.Distinct().ToHashSet();
        var existingIds = list.Members.Select(m => m.UserId).ToHashSet();

        var toRemove = list.Members.Where(m => !newSet.Contains(m.UserId)).ToList();
        _db.DistributionListMembers.RemoveRange(toRemove);
        foreach (var uid in newSet.Where(id => !existingIds.Contains(id)))
            list.Members.Add(new DistributionListMember { UserId = uid, AddedAt = DateTime.UtcNow });

        list.Name = trimmed;
        list.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "DistributionList update hit unique constraint (list {ListId}, name {Name})", listId, trimmed);
            return new DistributionListResult(DistributionListOutcome.NameTaken);
        }

        _logger.LogInformation("DistributionList {ListId} updated by user {UserId} ({Count} members)", listId, actingUserId, newSet.Count);
        return new DistributionListResult(DistributionListOutcome.Ok, listId);
    }

    public async Task<DistributionListResult> DeleteAsync(int actingUserId, int listId)
    {
        var list = await _db.DistributionLists
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.Id == listId);

        if (list == null)
            return new DistributionListResult(DistributionListOutcome.NotFound);

        // IDOR guard: authorize against the LIST's stored molecule.
        if (!await CanManageAsync(actingUserId, list.MoleculeId))
            return new DistributionListResult(DistributionListOutcome.Forbidden);

        _db.DistributionLists.Remove(list); // cascade removes members
        await _db.SaveChangesAsync();

        _logger.LogInformation("DistributionList {ListId} deleted by user {UserId}", listId, actingUserId);
        return new DistributionListResult(DistributionListOutcome.Ok, listId);
    }

    private Task<bool> CanManageAsync(int userId, int moleculeId) =>
        _grantService.HasGrantWithScopeAsync(userId, GrantKey, moleculeId: moleculeId);

    /// <summary>Validates name presence/uniqueness and that every member belongs to the molecule (cross-company OK).</summary>
    private async Task<DistributionListResult> ValidateAsync(int moleculeId, string trimmedName, IReadOnlyCollection<int> userIds, int? excludeListId)
    {
        if (string.IsNullOrWhiteSpace(trimmedName))
            return new DistributionListResult(DistributionListOutcome.NameRequired);

        var distinctUserIds = userIds.Distinct().ToList();
        if (distinctUserIds.Count == 0)
            return new DistributionListResult(DistributionListOutcome.NoMembers);

        // Case-insensitive uniqueness within the molecule (Name column is NOCASE; the explicit ToLower keeps
        // the check correct even if the collation is ever changed, and is EF-translatable to SQL lower()).
        var nameLower = trimmedName.ToLowerInvariant();
        var nameTaken = await _db.DistributionLists
            .IgnoreQueryFilters()
            .AnyAsync(l => l.MoleculeId == moleculeId
                && l.Id != (excludeListId ?? 0)
                && l.Name.ToLower() == nameLower);
        if (nameTaken)
            return new DistributionListResult(DistributionListOutcome.NameTaken);

        // Every selected user must belong to a company in this molecule (members may span companies).
        var validCount = await _db.Users
            .IgnoreQueryFilters()
            .CountAsync(u => distinctUserIds.Contains(u.Id)
                && _db.Companies.Any(c => c.Id == u.CompanyId && c.MoleculeId == moleculeId));
        if (validCount != distinctUserIds.Count)
            return new DistributionListResult(DistributionListOutcome.InvalidMembers);

        return new DistributionListResult(DistributionListOutcome.Ok);
    }
}
