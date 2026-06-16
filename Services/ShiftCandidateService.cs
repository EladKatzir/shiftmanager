using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Data.SeedData;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

// SECURITY-AUDITED: IgnoreQueryFilters is SAFE here — molecule-scoped reads; the calling endpoint
// gates molecule access (ValidateScopeAccessAsync + shiftType-belongs-to-molecule) before this runs.
public class ShiftCandidateService : IShiftCandidateService
{
    private readonly AppDbContext _db;
    private readonly IShiftAssignmentService _workforce;
    private readonly IShiftCalendarService _tech;
    private readonly IFeatureFlagService _flags;

    public ShiftCandidateService(
        AppDbContext db,
        IShiftAssignmentService workforce,
        IShiftCalendarService tech,
        IFeatureFlagService flags)
    {
        _db = db;
        _workforce = workforce;
        _tech = tech;
        _flags = flags;
    }

    public async Task<EligibleCandidatesResult> GetEligibleCandidatesAsync(
        int moleculeId, int shiftTypeId, int currentCompanyId, bool allowFallback = false)
    {
        var shiftType = await _db.ShiftTypes.IgnoreQueryFilters()
            .FirstOrDefaultAsync(st => st.Id == shiftTypeId);
        var molecule = await _db.Molecules.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == moleculeId);
        if (shiftType == null || molecule == null)
            return new EligibleCandidatesResult("category", Array.Empty<EligibleCandidateDto>());

        var useCategoryRule = await _flags.IsEnabledAsync(
            FeatureFlagSeed.Flags.CategoryBasedShiftEligibility, userId: null, companyId: currentCompanyId);

        var isTech = molecule.Type == MoleculeType.Tech;

        // Flag OFF -> behavior-preserving legacy set.
        if (!useCategoryRule)
            return new EligibleCandidatesResult("category", await DispatchAsync(isTech, moleculeId, shiftType, categoryFilter: false));

        // Flag ON -> category rule + null-category fork.
        if (shiftType.CategoryId.HasValue)
            return new EligibleCandidatesResult("category", await DispatchAsync(isTech, moleculeId, shiftType, categoryFilter: true));

        var isShared = shiftType.Key is "HOME" or "OFFLINE" || shiftType.JobTypeId == null;
        if (isShared || allowFallback)
            return new EligibleCandidatesResult("sharedFallback", await DispatchAsync(isTech, moleculeId, shiftType, categoryFilter: true));

        // Assignable type missing its category, no fallback requested -> structural zero + admin nudge.
        return new EligibleCandidatesResult("noCategory", Array.Empty<EligibleCandidateDto>());
    }

    private async Task<IReadOnlyList<EligibleCandidateDto>> DispatchAsync(
        bool isTech, int moleculeId, ShiftType shiftType, bool categoryFilter)
    {
        if (isTech)
        {
            var users = await _tech.GetEligibleUsersForShiftTypeAsync(moleculeId, shiftType.Id, categoryFilter);
            var companyIds = users.Select(u => u.CompanyId).Distinct().ToList();
            var names = await _db.Companies.IgnoreQueryFilters()
                .Where(c => companyIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name);
            return users.Select(u => new EligibleCandidateDto(
                u.Id, u.DisplayName, names.GetValueOrDefault(u.CompanyId))).ToList();
        }

        var dtos = await _workforce.GetEligibleUsersForShiftTypeAsync(
            shiftType.Id, shiftType.JobTypeId, shiftType.ShiftGroupingId, categoryFilter);
        return dtos.Select(d => new EligibleCandidateDto(d.UserId, d.DisplayName, d.CompanyName)).ToList();
    }
}
