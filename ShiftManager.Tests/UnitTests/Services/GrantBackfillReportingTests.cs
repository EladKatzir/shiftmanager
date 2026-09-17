using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using ShiftManager.Tests.Helpers;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// What the back-fill TELLS you. This is the defect that made a real configuration attempt look like
/// a no-op: the dry run compared only WHICH grant types a user had, never at WHAT SCOPE, so changing
/// a template's scope mode reported "0 users missing grants". Execute then reported 0 as well,
/// because it counted rows after minus rows before — and a rescope removes one row and adds one.
///
/// These tests pin the honest report. They do not change what Execute WRITES.
/// </summary>
public class GrantBackfillReportingTests
{
    private const int LeadTemplate = 3;
    private const int GrantType = 17;   // arbitrary existing type id; the seed is built here
    private const int UserId = 50;

    private static GrantBackfillService BuildService(AppDbContext db)
    {
        var hierarchy = new HierarchyService(db);
        var grants = new GrantService(db, hierarchy, Mock.Of<IAuditLogService>(), NullLogger<GrantService>.Instance);
        return new GrantBackfillService(db, grants, hierarchy, NullLogger<GrantBackfillService>.Instance);
    }

    /// <summary>Molecule 1 / company 1, one Lead user, and a template holding ONE molecule-scoped grant.</summary>
    private static async Task<SqliteDbContextFixture> SeededAsync()
    {
        var f = await SqliteDbContextFixture.CreateAsync();
        var db = f.Db;
        db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        db.Molecules.Add(new Molecule { Id = 1, AreaId = 1, Name = "M", DisplayName = "M" });
        db.Companies.Add(new Company { Id = 1, MoleculeId = 1, Name = "C", Slug = "c", DisplayName = "C" });
        db.GrantTypes.Add(new GrantType { Id = GrantType, Key = "AssignChores", NameKey = "n", DescriptionKey = "d" });
        db.RoleTemplates.Add(new RoleTemplate { Id = LeadTemplate, Key = "Lead", SortOrder = 20, DerivedUserRole = UserRole.Manager });
        await db.SaveChangesAsync();

        db.RoleTemplateGrants.Add(new RoleTemplateGrant
        {
            RoleTemplateId = LeadTemplate, GrantTypeId = GrantType,
            ScopeMode = GrantScopeMode.ExpandToMolecule, CanOwn = true
        });
        db.Users.Add(new AppUser
        {
            Id = UserId, Email = "lead@t", DisplayName = "Lead", CompanyId = 1,
            IsActive = true, RoleTemplateId = LeadTemplate, Role = UserRole.Manager
        });
        await db.SaveChangesAsync();
        return f;
    }

    /// <summary>The shape the old create-user path wrote: right grant, narrower (company) scope.</summary>
    private static async Task GiveCompanyScopedGrantAsync(AppDbContext db)
    {
        db.Grants.Add(new Grant
        {
            UserId = UserId, GrantTypeId = GrantType, CompanyId = 1, CanOwn = true,
            IsAutoGrant = true, Notes = "Auto-granted from role: Lead"
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Preview_ReportsAGrantThatIsMissingEntirely()
    {
        await using var f = await SeededAsync();

        var report = await BuildService(f.Db).PreviewAsync(LeadTemplate);

        report.UsersScanned.Should().Be(1);
        report.UsersWithMissingGrants.Should().Be(1);
        report.TotalMissingGrantRows.Should().Be(1);
    }

    [Fact]
    public async Task Preview_ReportsAGrantHeldAtTheWrongScope_InsteadOfCallingItNothingToDo()
    {
        // THE defect: the user holds the grant, but company-scoped while the template says molecule.
        // Comparing grant-type ids alone reports "nothing missing" and the operator concludes the
        // configuration did not apply.
        await using var f = await SeededAsync();
        await GiveCompanyScopedGrantAsync(f.Db);

        var report = await BuildService(f.Db).PreviewAsync(LeadTemplate);

        report.UsersWithMissingGrants.Should().Be(0, "the grant type IS present");
        report.UsersWithScopeMismatch.Should().Be(1);
        report.TotalScopeMismatchRows.Should().Be(1);
        report.Entries.Should().ContainSingle()
            .Which.ScopeMismatchGrantKeys.Should().Contain("AssignChores");
    }

    [Fact]
    public async Task Preview_SaysNothingWhenTheGrantIsAlreadyAtTheExpectedScope()
    {
        await using var f = await SeededAsync();
        f.Db.Grants.Add(new Grant
        {
            UserId = UserId, GrantTypeId = GrantType, MoleculeId = 1, CanOwn = true,
            IsAutoGrant = true, Notes = "Auto-granted from role: Lead"
        });
        await f.Db.SaveChangesAsync();

        var report = await BuildService(f.Db).PreviewAsync(LeadTemplate);

        report.UsersWithMissingGrants.Should().Be(0);
        report.UsersWithScopeMismatch.Should().Be(0);
        report.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_ReportsInsertedAndRemovedSeparately_SoARescopeIsNotReportedAsZero()
    {
        await using var f = await SeededAsync();
        await GiveCompanyScopedGrantAsync(f.Db);

        var result = await BuildService(f.Db).ExecuteAsync(LeadTemplate, actingUserId: 1);

        result.TotalGrantsInserted.Should().Be(1, "the molecule-scoped row is added");
        result.TotalGrantsRemoved.Should().Be(1, "the superseded company-scoped row is removed");
        result.UsersUpdated.Should().Be(1, "a rescope IS an update, even though the row count nets to zero");

        var grants = await f.Db.Grants.IgnoreQueryFilters().Where(g => g.UserId == UserId).ToListAsync();
        grants.Should().ContainSingle().Which.MoleculeId.Should().Be(1);
    }

    [Fact]
    public async Task Execute_ReportsNothingWhenThereIsNothingToDo()
    {
        await using var f = await SeededAsync();
        f.Db.Grants.Add(new Grant
        {
            UserId = UserId, GrantTypeId = GrantType, MoleculeId = 1, CanOwn = true,
            IsAutoGrant = true, Notes = "Auto-granted from role: Lead"
        });
        await f.Db.SaveChangesAsync();

        var result = await BuildService(f.Db).ExecuteAsync(LeadTemplate, actingUserId: 1);

        result.TotalGrantsInserted.Should().Be(0);
        result.TotalGrantsRemoved.Should().Be(0);
        result.UsersUpdated.Should().Be(0);
    }
}
