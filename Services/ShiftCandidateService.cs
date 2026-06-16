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
        // HOME/OFFLINE are presence STATUSES, not real shifts: assignable to ANYONE active in the
        // molecule (no DoesShifts, no category, no rank). A user with DoesShifts=false can still be
        // marked home/offline. Checked first so it holds even if such a type ever carries a category.
        if (shiftType.IsHome || shiftType.IsOffline)
            return new EligibleCandidatesResult("sharedFallback", await AnyoneInMoleculeAsync(moleculeId));

        if (shiftType.CategoryId.HasValue)
            return new EligibleCandidatesResult("category", await DispatchAsync(isTech, moleculeId, shiftType, categoryFilter: true));

        // A genuinely-shared null-jobType shift that ISN'T HOME/OFFLINE still uses the DoesShifts set
        // (it's a real shift everyone-who-does-shifts can take), as does the escape-hatch fallback.
        var isShared = shiftType.JobTypeId == null;
        if (isShared || allowFallback)
            return new EligibleCandidatesResult("sharedFallback", await DispatchAsync(isTech, moleculeId, shiftType, categoryFilter: true));

        // Assignable type missing its category, no fallback requested -> structural zero + admin nudge.
        return new EligibleCandidatesResult("noCategory", Array.Empty<EligibleCandidateDto>());
    }

    // All active, non-GroupUser users in the molecule — regardless of DoesShifts/category/rank.
    // Used for HOME/OFFLINE presence statuses (everyone can be marked home/offline).
    private async Task<IReadOnlyList<EligibleCandidateDto>> AnyoneInMoleculeAsync(int moleculeId)
    {
        // SECURITY-AUDITED: SAFE — molecule-scoped; the endpoint gates molecule access before this runs.
        var companyIds = await _db.Companies.IgnoreQueryFilters()
            .Where(c => c.MoleculeId == moleculeId).Select(c => c.Id).ToListAsync();
        var names = await _db.Companies.IgnoreQueryFilters()
            .Where(c => companyIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name);
        var users = await _db.Users.IgnoreQueryFilters()
            .Where(u => u.IsActive && u.AccountType != AccountType.GroupUser && companyIds.Contains(u.CompanyId))
            .OrderBy(u => u.DisplayName)
            .Select(u => new { u.Id, u.DisplayName, u.CompanyId })
            .ToListAsync();
        return users.Select(u => new EligibleCandidateDto(
            u.Id, u.DisplayName, names.GetValueOrDefault(u.CompanyId))).ToList();
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
