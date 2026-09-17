using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ShiftManager.Models;
using ShiftManager.Services;
using ShiftManager.Services.Api;
using ShiftManager.Tests.Helpers;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Models;

/// <summary>
/// A new user must participate in shifts and chores out of the box. Previously every creation path
/// left <c>DoesShifts</c>/<c>DoesChores</c> at the CLR default of <c>false</c>, so each new account
/// had to be switched on by hand from /Admin/Users before it appeared on any roster.
///
/// The default lives as a C# property initializer on <see cref="AppUser"/>, NOT as a database
/// <c>defaultValue</c>: EF sends the CLR value explicitly for a non-nullable bool, so a column
/// default would never actually apply to an EF insert.
///
/// Safe because a participant with no category falls under the company header rather than being
/// dropped — pinned by ShiftsCategoryFallbackTests and ChoresCategoryGroupingTests.
/// </summary>
public class UserParticipationDefaultsTests
{
    [Fact]
    public void NewUser_ParticipatesInShiftsByDefault()
    {
        Assert.True(new AppUser().DoesShifts);
    }

    [Fact]
    public void NewUser_ParticipatesInChoresByDefault()
    {
        Assert.True(new AppUser().DoesChores);
    }

    [Fact]
    public void NewCompanyMembership_ParticipatesInShiftsByDefault()
    {
        // The per-company mirror of the AppUser flag must agree, or a user is a shift participant
        // globally while being excluded in the very company they were added to.
        Assert.True(new CompanyMembership().DoesShifts);
    }

    [Fact]
    public async Task UserCreatedThroughTheRestApi_ParticipatesInShiftsAndChores()
    {
        await using var f = await SqliteDbContextFixture.CreateAsync();
        f.Db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        f.Db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        f.Db.Molecules.Add(new Molecule { Id = 1, AreaId = 1, Name = "M", DisplayName = "M" });
        f.Db.Companies.Add(new Company { Id = 1, MoleculeId = 1, Name = "Co", Slug = "co", DisplayName = "Co" });
        await f.Db.SaveChangesAsync();

        var svc = new UserApiService(f.Db, NullLogger<UserApiService>.Instance, Mock.Of<IGrantService>());

        var (user, error) = await svc.CreateUserAsync(
            companyId: 1, email: "new@test.com", displayName: "New User", role: "Employee", password: "Test1234!");

        Assert.Null(error);
        Assert.NotNull(user);

        var saved = f.Db.Users.Single(u => u.Email == "new@test.com");
        Assert.True(saved.DoesShifts);
        Assert.True(saved.DoesChores);
    }
}
