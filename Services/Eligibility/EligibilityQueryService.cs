using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models.Support;

namespace ShiftManager.Services.Eligibility;

/// <inheritdoc />
public class EligibilityQueryService : IEligibilityQueryService
{
    private readonly AppDbContext _db;

    public EligibilityQueryService(AppDbContext db) => _db = db;

    private static string Display(string name, string displayName)
        => string.IsNullOrWhiteSpace(displayName) ? name : displayName;

    public async Task<CategoryCandidatesView?> GetShiftCategoryCandidatesAsync(int shiftCategoryId)
    {
        // SECURITY-AUDITED: admin-gated (ManageShiftCategories), molecule-scoped read for the eligibility editor.
        var cat = await _db.ShiftCategories.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == shiftCategoryId);
        if (cat is null) return null;

        var users = await UsersInMoleculeAsync(cat.MoleculeId);
        var memberIds = (await _db.UserShiftCategories.IgnoreQueryFilters()
            .Where(m => m.ShiftCategoryId == shiftCategoryId).Select(m => m.UserId).ToListAsync()).ToHashSet();

        var candidates = users.Select(u => Classify(u, memberIds.Contains(u.Id), isChore: false)).ToList();
        return new CategoryCandidatesView(cat.Id, Display(cat.Name, cat.DisplayName), false, candidates);
    }

    public async Task<CategoryCandidatesView?> GetChoreCategoryCandidatesAsync(int choreCategoryId)
    {
        // SECURITY-AUDITED: admin-gated, molecule-scoped read for the eligibility editor.
        var cat = await _db.ChoreCategories.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == choreCategoryId);
        if (cat is null) return null;

        var users = await UsersInMoleculeAsync(cat.MoleculeId);
        var memberIds = (await _db.UserChoreCategories.IgnoreQueryFilters()
            .Where(m => m.ChoreCategoryId == choreCategoryId).Select(m => m.UserId).ToListAsync()).ToHashSet();

        var candidates = users.Select(u => Classify(u, memberIds.Contains(u.Id), isChore: true)).ToList();
        return new CategoryCandidatesView(cat.Id, Display(cat.Name, cat.DisplayName), true, candidates);
    }

    public async Task<UserEligibilityView?> GetUserEligibilityAsync(int userId)
    {
        // SECURITY-AUDITED: admin-gated, molecule-scoped read for the eligibility editor.
        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return null;

        var moleculeId = await _db.Companies.IgnoreQueryFilters()
            .Where(c => c.Id == user.CompanyId).Select(c => (int?)c.MoleculeId).FirstOrDefaultAsync();

        var shiftCats = moleculeId is null ? new List<CategoryRow>() : await _db.ShiftCategories.IgnoreQueryFilters()
            .Where(c => c.MoleculeId == moleculeId && c.IsActive).OrderBy(c => c.SortOrder)
            .Select(c => new CategoryRow(c.Id, c.Name, c.DisplayName)).ToListAsync();
        var choreCats = moleculeId is null ? new List<CategoryRow>() : await _db.ChoreCategories.IgnoreQueryFilters()
            .Where(c => c.MoleculeId == moleculeId && c.IsActive).OrderBy(c => c.SortOrder)
            .Select(c => new CategoryRow(c.Id, c.Name, c.DisplayName)).ToListAsync();

        var shiftMemberIds = (await _db.UserShiftCategories.IgnoreQueryFilters()
            .Where(m => m.UserId == userId).Select(m => m.ShiftCategoryId).ToListAsync()).ToHashSet();
        var choreMemberIds = (await _db.UserChoreCategories.IgnoreQueryFilters()
            .Where(m => m.UserId == userId).Select(m => m.ChoreCategoryId).ToListAsync()).ToHashSet();

        var shiftResult = shiftCats.Select(c =>
        {
            var isMember = shiftMemberIds.Contains(c.Id);
            var reasons = EligibilityClassifier.ClassifyMembership(user.AccountType, user.DoesShifts, isMember, isChore: false);
            return new CategoryEligibility(c.Id, Display(c.Name, c.DisplayName), false, EligibilityClassifier.IsEligible(reasons), isMember, reasons);
        }).ToList();

        var choreResult = choreCats.Select(c =>
        {
            var isMember = choreMemberIds.Contains(c.Id);
            var reasons = EligibilityClassifier.ClassifyMembership(user.AccountType, user.DoesChores, isMember, isChore: true);
            return new CategoryEligibility(c.Id, Display(c.Name, c.DisplayName), true, EligibilityClassifier.IsEligible(reasons), isMember, reasons);
        }).ToList();

        return new UserEligibilityView(user.Id, user.DisplayName, shiftResult, choreResult);
    }

    private async Task<List<UserRow>> UsersInMoleculeAsync(int moleculeId)
    {
        var companyIds = await _db.Companies.IgnoreQueryFilters()
            .Where(c => c.MoleculeId == moleculeId).Select(c => c.Id).ToListAsync();
        return await _db.Users.IgnoreQueryFilters()
            .Where(u => u.IsActive && companyIds.Contains(u.CompanyId))
            .OrderBy(u => u.DisplayName)
            .Select(u => new UserRow(u.Id, u.DisplayName, u.AccountType, u.DoesShifts, u.DoesChores))
            .ToListAsync();
    }

    private static CandidateEligibility Classify(UserRow u, bool isMember, bool isChore)
    {
        var participates = isChore ? u.DoesChores : u.DoesShifts;
        var reasons = EligibilityClassifier.ClassifyMembership(u.AccountType, participates, isMember, isChore);
        return new CandidateEligibility(u.Id, u.Name, EligibilityClassifier.IsEligible(reasons), reasons);
    }

    private sealed record UserRow(int Id, string Name, AccountType AccountType, bool DoesShifts, bool DoesChores);
    private sealed record CategoryRow(int Id, string Name, string DisplayName);
}
