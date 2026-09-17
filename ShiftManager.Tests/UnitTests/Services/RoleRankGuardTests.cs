using FluentAssertions;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using ShiftManager.Tests.Helpers;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// "You may act at your own rank or below, never above."
///
/// Rank is RoleTemplate.SortOrder, where LOWER means more senior
/// (Owner 1 &lt; AreaAdmin 2 &lt; MoleculeAdmin 3 &lt; Director 5 &lt; BRDirector 10 &lt; Lead 20 &lt; … &lt; Trainee 101).
/// Acting at the SAME rank is allowed — that was an explicit requirement.
///
/// This exists because Lead/Kabar now hold EditCompanyUsers across their whole molecule. Without a
/// rank rule a Lead could reset the password of their MoleculeAdmin in another desk and then log in
/// as them. It also backs the role-assignment rule: no assigning a role more senior than your own.
/// </summary>
public class RoleRankGuardTests
{
    private const int Owner = 11, MoleculeAdmin = 7, Lead = 3, Employee = 1;

    // Users: 100 = MoleculeAdmin, 200 = Lead, 201 = another Lead, 300 = Employee,
    //        400 = Lead-ranked but with AdminAccess, 500 = no RoleTemplateId (rank from Role).
    private static async Task<SqliteDbContextFixture> SeededAsync()
    {
        var f = await SqliteDbContextFixture.CreateAsync();
        var db = f.Db;
        db.RoleTemplates.AddRange(
            new RoleTemplate { Id = Owner, Key = "Owner", SortOrder = 1, DerivedUserRole = UserRole.Owner },
            new RoleTemplate { Id = MoleculeAdmin, Key = "MoleculeAdmin", SortOrder = 3, DerivedUserRole = UserRole.Manager },
            new RoleTemplate { Id = Lead, Key = "Lead", SortOrder = 20, DerivedUserRole = UserRole.Manager },
            new RoleTemplate { Id = Employee, Key = "Employee", SortOrder = 100, DerivedUserRole = UserRole.Employee });
        db.Users.AddRange(
            new AppUser { Id = 100, Email = "mol@t", DisplayName = "Mol", CompanyId = 1, RoleTemplateId = MoleculeAdmin, Role = UserRole.Manager },
            new AppUser { Id = 200, Email = "lead@t", DisplayName = "Lead", CompanyId = 1, RoleTemplateId = Lead, Role = UserRole.Manager },
            new AppUser { Id = 201, Email = "lead2@t", DisplayName = "Lead2", CompanyId = 1, RoleTemplateId = Lead, Role = UserRole.Manager },
            new AppUser { Id = 300, Email = "emp@t", DisplayName = "Emp", CompanyId = 1, RoleTemplateId = Employee, Role = UserRole.Employee },
            new AppUser { Id = 400, Email = "adm@t", DisplayName = "Adm", CompanyId = 1, RoleTemplateId = Lead, Role = UserRole.Manager },
            new AppUser { Id = 500, Email = "none@t", DisplayName = "NoTemplate", CompanyId = 1, RoleTemplateId = null, Role = UserRole.Employee });
        await db.SaveChangesAsync();
        return f;
    }

    /// <param name="adminAccessUserId">user id that holds AdminAccess, if any</param>
    private static IGrantService Grants(int? adminAccessUserId = null)
    {
        var m = new Mock<IGrantService>();
        m.Setup(g => g.HasGrantAsync(It.IsAny<int>(), "AdminAccess"))
         .ReturnsAsync((int uid, string _) => adminAccessUserId.HasValue && uid == adminAccessUserId.Value);
        return m.Object;
    }

    [Fact]
    public async Task SeniorUser_MayActOnAJuniorUser()
    {
        await using var f = await SeededAsync();
        (await RoleRankGuard.CanActOnUserAsync(f.Db, Grants(), actorUserId: 100, targetUserId: 200))
            .Should().BeTrue("MoleculeAdmin (3) outranks Lead (20)");
    }

    [Fact]
    public async Task UserMayActOnSomeoneOfTheSameRank()
    {
        await using var f = await SeededAsync();
        (await RoleRankGuard.CanActOnUserAsync(f.Db, Grants(), actorUserId: 200, targetUserId: 201))
            .Should().BeTrue("acting at your own rank is explicitly allowed");
    }

    [Fact]
    public async Task UserMayActOnThemselves()
    {
        await using var f = await SeededAsync();
        (await RoleRankGuard.CanActOnUserAsync(f.Db, Grants(), actorUserId: 200, targetUserId: 200))
            .Should().BeTrue();
    }

    [Fact]
    public async Task JuniorUser_MayNotActOnASeniorUser()
    {
        await using var f = await SeededAsync();
        (await RoleRankGuard.CanActOnUserAsync(f.Db, Grants(), actorUserId: 200, targetUserId: 100))
            .Should().BeFalse("a Lead must not reset their MoleculeAdmin's password");
    }

    [Fact]
    public async Task AdminAccessHolder_MayActOnAnyone()
    {
        await using var f = await SeededAsync();
        (await RoleRankGuard.CanActOnUserAsync(f.Db, Grants(adminAccessUserId: 400), actorUserId: 400, targetUserId: 100))
            .Should().BeTrue("AdminAccess is the deliberate override, so Owner keeps working");
    }

    [Fact]
    public async Task RankFallsBackToTheUsersRole_WhenNoRoleTemplateIsSet()
    {
        // 6 active users have RoleTemplateId = null. They must still get a rank, or the guard would
        // either block them from everything or let them act on anyone.
        await using var f = await SeededAsync();

        (await RoleRankGuard.CanActOnUserAsync(f.Db, Grants(), actorUserId: 100, targetUserId: 500))
            .Should().BeTrue("MoleculeAdmin outranks an Employee-by-Role user");
        (await RoleRankGuard.CanActOnUserAsync(f.Db, Grants(), actorUserId: 500, targetUserId: 100))
            .Should().BeFalse("an Employee-by-Role user must not act on a MoleculeAdmin");
    }

    [Fact]
    public async Task AssigningARoleMoreSeniorThanYourOwn_IsRefused()
    {
        await using var f = await SeededAsync();
        (await RoleRankGuard.CanAssignTemplateAsync(f.Db, Grants(), actorUserId: 200, roleTemplateId: Owner))
            .Should().BeFalse("a Lead assigning Owner is the escalation this rule exists to stop");
        (await RoleRankGuard.CanAssignTemplateAsync(f.Db, Grants(), actorUserId: 200, roleTemplateId: MoleculeAdmin))
            .Should().BeFalse();
    }

    [Fact]
    public async Task AssigningYourOwnRoleOrLower_IsAllowed()
    {
        await using var f = await SeededAsync();
        (await RoleRankGuard.CanAssignTemplateAsync(f.Db, Grants(), actorUserId: 200, roleTemplateId: Lead))
            .Should().BeTrue("assigning your own role is explicitly allowed");
        (await RoleRankGuard.CanAssignTemplateAsync(f.Db, Grants(), actorUserId: 200, roleTemplateId: Employee))
            .Should().BeTrue();
    }

    [Fact]
    public async Task AdminAccessHolder_MayAssignAnyRole()
    {
        await using var f = await SeededAsync();
        (await RoleRankGuard.CanAssignTemplateAsync(f.Db, Grants(adminAccessUserId: 400), actorUserId: 400, roleTemplateId: Owner))
            .Should().BeTrue();
    }

    [Fact]
    public async Task UnknownActorOrTarget_DoesNotSilentlyAllowEverything()
    {
        // A missing user must not resolve to "most senior". Fail closed for the actor.
        await using var f = await SeededAsync();
        (await RoleRankGuard.CanActOnUserAsync(f.Db, Grants(), actorUserId: 9999, targetUserId: 100))
            .Should().BeFalse();
    }
}
