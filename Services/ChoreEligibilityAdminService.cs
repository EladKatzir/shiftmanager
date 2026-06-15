using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

// SECURITY-AUDITED: IgnoreQueryFilters() is SAFE — EligibilityRule/UserChoreExemption/ChoreType are
// global/molecule-scoped config (no tenant filter). Authorization (EditChoreTypes) is enforced by the
// calling page against the chore type's molecule before invoking these methods.
public class ChoreEligibilityAdminService : IChoreEligibilityAdminService
{
    private readonly AppDbContext _db;

    public ChoreEligibilityAdminService(AppDbContext db) => _db = db;

    public async Task<List<EligibilityRule>> GetRulesForChoreTypeAsync(int choreTypeId)
        => await _db.EligibilityRules.IgnoreQueryFilters()
            .Where(r => r.ChoreTypeId == choreTypeId)
            .ToListAsync();

    public async Task<bool> SetRulesForChoreTypeAsync(int choreTypeId, Gender? requiredGender, bool requiresOfficerRank, int createdBy)
    {
        var typeExists = await _db.ChoreTypes.IgnoreQueryFilters().AnyAsync(ct => ct.Id == choreTypeId);
        if (!typeExists)
            return false;

        // Replace semantics: drop the existing rule set for this chore type, then insert the desired one.
        var existing = await _db.EligibilityRules.IgnoreQueryFilters()
            .Where(r => r.ChoreTypeId == choreTypeId)
            .ToListAsync();
        if (existing.Count > 0)
            _db.EligibilityRules.RemoveRange(existing);

        if (requiredGender.HasValue)
        {
            _db.EligibilityRules.Add(new EligibilityRule
            {
                ChoreTypeId = choreTypeId,
                RuleKind = EligibilityRuleKind.RequiresGender,
                GenderValue = requiredGender.Value,
                CreatedBy = createdBy
            });
        }

        if (requiresOfficerRank)
        {
            _db.EligibilityRules.Add(new EligibilityRule
            {
                ChoreTypeId = choreTypeId,
                RuleKind = EligibilityRuleKind.RequiresOfficerRank,
                GenderValue = null,
                CreatedBy = createdBy
            });
        }

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<UserChoreExemption>> GetExemptionsForChoreTypeAsync(int choreTypeId)
        => await _db.UserChoreExemptions.IgnoreQueryFilters()
            .Where(e => e.ChoreTypeId == choreTypeId)
            .ToListAsync();

    public async Task<UserChoreExemption?> AddExemptionAsync(int userId, int choreTypeId, string? reason, int createdBy)
    {
        var typeExists = await _db.ChoreTypes.IgnoreQueryFilters().AnyAsync(ct => ct.Id == choreTypeId);
        var userExists = await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == userId);
        if (!typeExists || !userExists)
            return null;

        var existing = await _db.UserChoreExemptions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.UserId == userId && e.ChoreTypeId == choreTypeId);
        if (existing != null)
            return existing; // idempotent — the (UserId, ChoreTypeId) unique index would otherwise throw.

        var trimmed = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmed is { Length: > 200 })
            trimmed = trimmed[..200];

        var exemption = new UserChoreExemption
        {
            UserId = userId,
            ChoreTypeId = choreTypeId,
            Reason = trimmed,
            CreatedBy = createdBy
        };
        _db.UserChoreExemptions.Add(exemption);
        await _db.SaveChangesAsync();
        return exemption;
    }

    public async Task<bool> RemoveExemptionAsync(int userId, int choreTypeId)
    {
        var exemption = await _db.UserChoreExemptions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.UserId == userId && e.ChoreTypeId == choreTypeId);
        if (exemption == null)
            return false;

        _db.UserChoreExemptions.Remove(exemption);
        await _db.SaveChangesAsync();
        return true;
    }
}
