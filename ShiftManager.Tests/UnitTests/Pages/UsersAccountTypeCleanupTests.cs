using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Pages.Admin;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Pages;

/// <summary>
/// Verifies that OnPostAccountTypeAsync removes the FUTURE assignments a user can no longer
/// legally hold after an account-type change, while leaving past assignments untouched.
///
/// Capability rules:
///   Mil       → no chores (does shifts, does on-call)
///   GroupUser → no shifts, no chores, no on-call
///   Standard  → no cleanup (de-restriction — nothing to remove)
///
/// Uses real SQLite (:memory:) so EF query filters and date comparisons work exactly as
/// they do in production. (NOT UseInMemoryDatabase.)
/// </summary>
public class UsersAccountTypeCleanupTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly Mock<IGrantService> _grants = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly Mock<IConcurrencyService> _concurrency = new();

    public UsersAccountTypeCleanupTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        // Concurrency service always succeeds — just runs the save delegate and returns Success.
        _concurrency
            .Setup(c => c.SaveWithConcurrencyHandlingAsync(
                It.IsAny<Func<Task<int>>>(), It.IsAny<string>(), It.IsAny<int>()))
            .Returns<Func<Task<int>>, string, int>(async (save, _, __) =>
            {
                await save();
                return new ConcurrencySaveResult(Success: true);
            });
    }

    // ─── helpers ──────────────────────────────────────────────────────────────

    private Mock<IStringLocalizer<SharedResources>> BuildLocalizer()
    {
        var loc = new Mock<IStringLocalizer<SharedResources>>();
        loc.Setup(l => l[It.IsAny<string>()]).Returns<string>(k => new LocalizedString(k, k));
        loc.Setup(l => l[It.IsAny<string>(), It.IsAny<object[]>()]).Returns<string, object[]>((k, _) => new LocalizedString(k, k));
        return loc;
    }

    private UsersModel BuildModel(int callerId, Mock<IStringLocalizer<SharedResources>> loc)
    {
        var model = new UsersModel(
            loc.Object, _db, NullLogger<UsersModel>.Instance,
            Mock.Of<ICompanyContext>(), Mock.Of<IDirectorService>(), Mock.Of<ITraineeService>(),
            _audit.Object, Mock.Of<IMailService>(), Mock.Of<INotificationService>(),
            _grants.Object, Mock.Of<IRoleService>(), Mock.Of<IJobTypeService>(),
            _concurrency.Object, Mock.Of<ITenantResolver>(), Mock.Of<IHierarchyService>(),
            Mock.Of<IUserCompanyTransferService>(), Mock.Of<IShiftCategoryService>(),
            Mock.Of<ICompanyMembershipService>());

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, callerId.ToString()) }, "test"))
        };
        model.PageContext = new Microsoft.AspNetCore.Mvc.RazorPages.PageContext { HttpContext = httpContext };
        model.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        return model;
    }

    /// <summary>
    /// Seeds a complete hierarchy + target user + all assignment types (past and future).
    /// Returns the IDs of every seeded entity so each test can independently verify state.
    /// </summary>
    private async Task<(int companyId, int userId,
        int pastShiftId, int futureShiftId,
        int pastChoreId, int futureChoreId,
        int pastOnDutyId, int futureOnDutyId)> SeedAsync()
    {
        var project = new Project { Name = "P", DisplayName = "P" };
        _db.Projects.Add(project); await _db.SaveChangesAsync();
        var area = new Area { ProjectId = project.Id, Name = "A", DisplayName = "A" };
        _db.Areas.Add(area); await _db.SaveChangesAsync();
        var mol = new Molecule { AreaId = area.Id, Name = "M", DisplayName = "M", Type = MoleculeType.Workforce };
        _db.Molecules.Add(mol); await _db.SaveChangesAsync();
        var company = new Company { MoleculeId = mol.Id, Name = "C", DisplayName = "C" };
        _db.Companies.Add(company); await _db.SaveChangesAsync();

        // ShiftInstance requires a ShiftType (FK)
        var shiftType = new ShiftType
        {
            Scope = ShiftScope.Molecule,
            MoleculeId = mol.Id,
            Key = ShiftType.KEY_MORNING,
            NameEn = "Morning"
        };
        _db.ShiftTypes.Add(shiftType); await _db.SaveChangesAsync();

        // Target user (starts as Standard)
        var user = new AppUser
        {
            CompanyId = company.Id,
            Email = "target@test.com",
            DisplayName = "Target",
            Role = UserRole.Employee,
            AccountType = AccountType.Standard,
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>()
        };
        _db.Users.Add(user); await _db.SaveChangesAsync();

        var today = DateOnly.FromDateTime(DateTime.Today);
        var past = today.AddDays(-10);
        var future = today.AddDays(10);

        // Shift instances (past and future)
        var pastInstance = new ShiftInstance
        {
            CompanyId = company.Id,
            ShiftTypeId = shiftType.Id,
            WorkDate = past,
            UpdatedAt = DateTime.UtcNow
        };
        var futureInstance = new ShiftInstance
        {
            CompanyId = company.Id,
            ShiftTypeId = shiftType.Id,
            WorkDate = future,
            UpdatedAt = DateTime.UtcNow
        };
        _db.ShiftInstances.AddRange(pastInstance, futureInstance);
        await _db.SaveChangesAsync();

        // Shift assignments
        var pastShift = new ShiftAssignment
        {
            CompanyId = company.Id,
            ShiftInstanceId = pastInstance.Id,
            UserId = user.Id
        };
        var futureShift = new ShiftAssignment
        {
            CompanyId = company.Id,
            ShiftInstanceId = futureInstance.Id,
            UserId = user.Id
        };
        _db.ShiftAssignments.AddRange(pastShift, futureShift);
        await _db.SaveChangesAsync();

        // Chores (active — CanceledAt = null)
        var pastChore = new Chore
        {
            CompanyId = company.Id,
            UserId = user.Id,
            Date = past,
            Title = "Past Chore",
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow
        };
        var futureChore = new Chore
        {
            CompanyId = company.Id,
            UserId = user.Id,
            Date = future,
            Title = "Future Chore",
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow
        };
        _db.Chores.AddRange(pastChore, futureChore);
        await _db.SaveChangesAsync();

        // OnDuties (active — CanceledAt = null)
        var pastOnDuty = new OnDuty
        {
            UserId = user.Id,
            Date = past,
            Type = OnDutyType.Hakam,
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow
        };
        var futureOnDuty = new OnDuty
        {
            UserId = user.Id,
            Date = future,
            Type = OnDutyType.Hakam,
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow
        };
        _db.OnDuties.AddRange(pastOnDuty, futureOnDuty);
        await _db.SaveChangesAsync();

        return (company.Id, user.Id,
            pastShift.Id, futureShift.Id,
            pastChore.Id, futureChore.Id,
            pastOnDuty.Id, futureOnDuty.Id);
    }

    // ─── tests ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Changing to Mil: future chore must be soft-canceled; future shift + future on-call + ALL past assignments untouched.
    /// </summary>
    [Fact]
    public async Task OnPostAccountTypeAsync_ToMil_RemovesFutureChoresOnly()
    {
        var (companyId, userId, pastShiftId, futureShiftId, pastChoreId, futureChoreId, pastOnDutyId, futureOnDutyId)
            = await SeedAsync();
        const int callerId = 999;

        _grants.Setup(g => g.HasGrantAsync(callerId, "AdminAccess")).ReturnsAsync(true);

        var model = BuildModel(callerId, BuildLocalizer());
        var result = await model.OnPostAccountTypeAsync(userId, (int)AccountType.Mil);

        result.Should().BeOfType<RedirectToPageResult>();
        model.TempData.ContainsKey("ErrorMessage").Should().BeFalse("no error on success");

        // Reload from DB (bypass tenant filter so we can see all rows)
        var futureShift = await _db.ShiftAssignments.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == futureShiftId);
        var pastShift   = await _db.ShiftAssignments.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == pastShiftId);
        var futureChore = await _db.Chores.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == futureChoreId);
        var pastChore   = await _db.Chores.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == pastChoreId);
        var futureOnDuty = await _db.OnDuties.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.Id == futureOnDutyId);
        var pastOnDuty  = await _db.OnDuties.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.Id == pastOnDutyId);

        // Future chore must be soft-canceled (Mil cannot have chores)
        futureChore.Should().NotBeNull("row is kept; soft-canceled");
        futureChore!.CanceledAt.Should().NotBeNull("future chore must be soft-canceled on Mil change");

        // Future shift must remain intact (Mil CAN do shifts)
        futureShift.Should().NotBeNull();
        futureShift!.UserId.Should().Be(userId, "future shift must NOT be vacated — Mil can do shifts");

        // Future on-call must remain intact (Mil has no on-call restriction per spec)
        futureOnDuty.Should().NotBeNull();
        futureOnDuty!.CanceledAt.Should().BeNull("future on-call must NOT be canceled on Mil change");

        // All past assignments must remain completely untouched
        pastShift.Should().NotBeNull();
        pastShift!.UserId.Should().Be(userId, "past shift is history — must not be touched");

        pastChore.Should().NotBeNull();
        pastChore!.CanceledAt.Should().BeNull("past chore must NOT be canceled");

        pastOnDuty.Should().NotBeNull();
        pastOnDuty!.CanceledAt.Should().BeNull("past on-call must NOT be canceled");
    }

    /// <summary>
    /// Changing to GroupUser: future shift must be vacated, future chore and future on-call
    /// must be soft-canceled. Past assignments remain completely untouched.
    /// </summary>
    [Fact]
    public async Task OnPostAccountTypeAsync_ToGroupUser_RemovesAllFutureAssignments()
    {
        var (companyId, userId, pastShiftId, futureShiftId, pastChoreId, futureChoreId, pastOnDutyId, futureOnDutyId)
            = await SeedAsync();
        const int callerId = 999;

        _grants.Setup(g => g.HasGrantAsync(callerId, "AdminAccess")).ReturnsAsync(true);

        var model = BuildModel(callerId, BuildLocalizer());
        var result = await model.OnPostAccountTypeAsync(userId, (int)AccountType.GroupUser);

        result.Should().BeOfType<RedirectToPageResult>();
        model.TempData.ContainsKey("ErrorMessage").Should().BeFalse("no error on success");

        // Reload from DB
        var futureShift  = await _db.ShiftAssignments.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == futureShiftId);
        var pastShift    = await _db.ShiftAssignments.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == pastShiftId);
        var futureChore  = await _db.Chores.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == futureChoreId);
        var pastChore    = await _db.Chores.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == pastChoreId);
        var futureOnDuty = await _db.OnDuties.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.Id == futureOnDutyId);
        var pastOnDuty   = await _db.OnDuties.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.Id == pastOnDutyId);

        // Future shift must be vacated (UserId = null; row stays for the slot)
        futureShift.Should().NotBeNull("shift slot row stays; only UserId is nulled");
        futureShift!.UserId.Should().BeNull("future shift must be vacated — GroupUser cannot do shifts");

        // Future chore must be soft-canceled
        futureChore.Should().NotBeNull();
        futureChore!.CanceledAt.Should().NotBeNull("future chore must be soft-canceled on GroupUser change");

        // Future on-call must be soft-canceled
        futureOnDuty.Should().NotBeNull();
        futureOnDuty!.CanceledAt.Should().NotBeNull("future on-call must be soft-canceled on GroupUser change");

        // All past assignments must remain completely untouched
        pastShift.Should().NotBeNull();
        pastShift!.UserId.Should().Be(userId, "past shift is history — must not be touched");

        pastChore.Should().NotBeNull();
        pastChore!.CanceledAt.Should().BeNull("past chore must NOT be canceled");

        pastOnDuty.Should().NotBeNull();
        pastOnDuty!.CanceledAt.Should().BeNull("past on-call must NOT be canceled");
    }

    /// <summary>
    /// Changing to Standard (de-restriction) does NOT remove any assignments.
    /// </summary>
    [Fact]
    public async Task OnPostAccountTypeAsync_ToStandard_DoesNotRemoveAnyAssignments()
    {
        var (companyId, userId, pastShiftId, futureShiftId, pastChoreId, futureChoreId, pastOnDutyId, futureOnDutyId)
            = await SeedAsync();
        const int callerId = 999;

        // Pre-set to GroupUser so the change to Standard is a de-restriction
        var user = await _db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == userId);
        user.AccountType = AccountType.GroupUser;
        await _db.SaveChangesAsync();

        _grants.Setup(g => g.HasGrantAsync(callerId, "AdminAccess")).ReturnsAsync(true);

        var model = BuildModel(callerId, BuildLocalizer());
        var result = await model.OnPostAccountTypeAsync(userId, (int)AccountType.Standard);

        result.Should().BeOfType<RedirectToPageResult>();
        model.TempData.ContainsKey("ErrorMessage").Should().BeFalse();

        var futureShift  = await _db.ShiftAssignments.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == futureShiftId);
        var futureChore  = await _db.Chores.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == futureChoreId);
        var futureOnDuty = await _db.OnDuties.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.Id == futureOnDutyId);

        futureShift!.UserId.Should().Be(userId, "Standard change must NOT clean up any assignments");
        futureChore!.CanceledAt.Should().BeNull("Standard change must NOT cancel any chores");
        futureOnDuty!.CanceledAt.Should().BeNull("Standard change must NOT cancel any on-calls");
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }
}
