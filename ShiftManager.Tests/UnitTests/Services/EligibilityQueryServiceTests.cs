using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services.Eligibility;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// DB-backed eligibility query engine — verifies By-category candidate classification and the
/// By-person overview against a seeded molecule (FK-off SQLite harness).
/// </summary>
public sealed class EligibilityQueryServiceTests : IAsyncLifetime
{
    private const int Molecule = 10;
    private const int Company = 1;
    private const int ShiftCatId = 100;
    private SqliteConnection _connection = null!;
    private AppDbContext _db = null!;
    private EligibilityQueryService _svc = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        await _connection.OpenAsync();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();
        _svc = new EligibilityQueryService(_db);

        _db.Companies.Add(new Company { Id = Company, MoleculeId = Molecule, Name = "C1" });
        _db.ShiftCategories.Add(new ShiftCategory { Id = ShiftCatId, MoleculeId = Molecule, Name = "NightGuard", DisplayName = "Night Guard", IsActive = true, SortOrder = 0 });

        void AddUser(int id, AccountType type, bool doesShifts, bool active = true) =>
            _db.Users.Add(new AppUser { Id = id, CompanyId = Company, Email = $"u{id}@x", DisplayName = $"U{id}", IsActive = active, AccountType = type, DoesShifts = doesShifts });

        AddUser(1, AccountType.Standard, doesShifts: true);   // member + participating -> eligible
        AddUser(2, AccountType.Standard, doesShifts: true);   // not a member
        AddUser(3, AccountType.Standard, doesShifts: false);  // member but not participating
        AddUser(4, AccountType.GroupUser, doesShifts: true);  // member but excluded account
        AddUser(5, AccountType.Standard, doesShifts: true, active: false); // inactive -> excluded

        _db.UserShiftCategories.Add(new UserShiftCategory { UserId = 1, ShiftCategoryId = ShiftCatId });
        _db.UserShiftCategories.Add(new UserShiftCategory { UserId = 3, ShiftCategoryId = ShiftCatId });
        _db.UserShiftCategories.Add(new UserShiftCategory { UserId = 4, ShiftCategoryId = ShiftCatId });
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task ByCategory_ClassifiesEachCandidate_AndExcludesInactive()
    {
        var view = await _svc.GetShiftCategoryCandidatesAsync(ShiftCatId);

        Assert.NotNull(view);
        Assert.False(view!.IsChore);
        Assert.Equal("Night Guard", view.Name);
        Assert.Equal(4, view.Candidates.Count); // U5 (inactive) excluded

        CandidateEligibility C(int id) => view.Candidates.Single(c => c.UserId == id);

        Assert.True(C(1).Eligible);
        Assert.Empty(C(1).Reasons);
        Assert.Contains(EligibilityReason.NotCategoryMember, C(2).Reasons);
        Assert.Contains(EligibilityReason.NotParticipating, C(3).Reasons);
        Assert.Contains(EligibilityReason.AccountTypeIneligible, C(4).Reasons);
        Assert.DoesNotContain(view.Candidates, c => c.UserId == 5);
    }

    [Fact]
    public async Task ByCategory_UnknownCategory_ReturnsNull()
    {
        Assert.Null(await _svc.GetShiftCategoryCandidatesAsync(999));
    }

    [Fact]
    public async Task ByPerson_ListsMoleculeCategories_WithMembershipAndEligibility()
    {
        var view = await _svc.GetUserEligibilityAsync(1);

        Assert.NotNull(view);
        var cat = view!.ShiftCategories.Single(c => c.CategoryId == ShiftCatId);
        Assert.True(cat.IsMember);
        Assert.True(cat.Eligible);

        var view3 = await _svc.GetUserEligibilityAsync(3); // member but DoesShifts off
        var cat3 = view3!.ShiftCategories.Single(c => c.CategoryId == ShiftCatId);
        Assert.True(cat3.IsMember);
        Assert.False(cat3.Eligible);
        Assert.Contains(EligibilityReason.NotParticipating, cat3.Reasons);
    }
}
