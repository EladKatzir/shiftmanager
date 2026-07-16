using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ShiftManager.Data;
using ShiftManager.Hubs;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Models.Validation;
using ShiftManager.Pages.Calendar;
using ShiftManager.Resources;
using ShiftManager.Services;
using ShiftManager.Services.Remediation;
using System.Security.Claims;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Pages;

/// <summary>
/// Locks the fix for: Justice "Make it real" filled the WRONG company's instance.
///
/// TableModel.OnPostAssignEmployeeAsync resolves the shift instance to assign into. When the
/// caller supplies an explicit <c>ShiftInstanceId</c> (e.g. Justice drilling into a specific
/// hole) the handler MUST target that exact instance — not FirstOrDefault by ShiftType+Date,
/// which is ambiguous when the same molecule-scoped ShiftType has instances in multiple
/// companies on the same date. Without the fix, an explicit request for Company B's instance
/// was silently assigned to Company A's (first-inserted) instance, leaving the clicked hole empty.
///
/// Seam: the handler delegates to IShiftAssignmentService.AssignShiftAsync(userId, instance.Id, ...)
/// after resolving the instance, so the test mocks that service and asserts the instance id it is
/// called with — isolating exactly the resolution logic.
/// </summary>
public sealed class TableAssignEmployeeInstanceResolutionTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private AppDbContext _db = null!;

    private const int MoleculeId = 1;
    private const int JobTypeId = 1;
    private const int CompanyA = 1;
    private const int CompanyB = 2;
    private const int ShiftTypeId = 1;
    private const int InstanceCompanyA = 100; // inserted first -> the FirstOrDefault(ShiftType+Date) match
    private const int InstanceCompanyB = 200; // same ShiftType+Date, different company
    private const int AssigneeUserId = 50;
    private const int ManagerUserId = 1;
    private static readonly DateOnly WorkDate = new(2026, 6, 22);

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _db = new AppDbContext(options);
        await _db.Database.EnsureCreatedAsync();
        await SeedAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    /// <summary>Two ShiftInstances sharing the SAME ShiftType + WorkDate, in two companies of one molecule.</summary>
    private async Task SeedAsync()
    {
        _db.Molecules.Add(new Molecule { Id = MoleculeId, Name = "Mol", DisplayName = "Mol", Type = MoleculeType.Workforce });
        _db.Companies.AddRange(
            new Company { Id = CompanyA, MoleculeId = MoleculeId, Name = "CoA", DisplayName = "Company A" },
            new Company { Id = CompanyB, MoleculeId = MoleculeId, Name = "CoB", DisplayName = "Company B" });
        _db.ShiftTypes.Add(new ShiftType
        {
            Id = ShiftTypeId,
            Scope = ShiftScope.Molecule,
            MoleculeId = MoleculeId,
            JobTypeId = JobTypeId,
            Key = ShiftType.KEY_MORNING,
            Start = new TimeOnly(8, 0),
            End = new TimeOnly(16, 0)
        });
        _db.ShiftInstances.AddRange(
            new ShiftInstance { Id = InstanceCompanyA, CompanyId = CompanyA, ShiftTypeId = ShiftTypeId, WorkDate = WorkDate, Name = "Morning Shift", StaffingRequired = 2, Concurrency = 0 },
            new ShiftInstance { Id = InstanceCompanyB, CompanyId = CompanyB, ShiftTypeId = ShiftTypeId, WorkDate = WorkDate, Name = "Morning Shift", StaffingRequired = 2, Concurrency = 0 });
        _db.Users.Add(new AppUser
        {
            Id = AssigneeUserId,
            Email = "assignee@test.com",
            DisplayName = "Assignee",
            CompanyId = CompanyA,
            JobTypeId = JobTypeId,
            IsActive = true,
            DoesShifts = true,
            AccountType = AccountType.Standard,
            Role = UserRole.Employee,
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>()
        });
        await _db.SaveChangesAsync();
    }

    /// <summary>Real SQLite Db; IShiftAssignmentService mocked (validate=clean, assign=success) so the
    /// only thing under test is which instance id the handler resolves and hands to the service.</summary>
    private (TableModel model, Mock<IShiftAssignmentService> assignment) BuildModel()
    {
        var assignment = new Mock<IShiftAssignmentService>();
        assignment
            .Setup(s => s.ValidateShiftAssignmentAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new ShiftAssignmentValidation(true, Array.Empty<ValidationIssue>(), Array.Empty<ValidationIssue>()));
        assignment
            .Setup(s => s.AssignShiftAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(new ShiftAssignmentResult(true, 999, null, null));

        var companyContext = new Mock<ICompanyContext>();
        companyContext.Setup(c => c.GetCompanyIdOrThrow()).Returns(CompanyA);

        var grant = new Mock<IGrantService>();
        grant.Setup(g => g.HasGrantAsync(It.IsAny<int>(), "AdminAccess")).ReturnsAsync(true);

        var localizer = new Mock<IStringLocalizer<SharedResources>>();
        localizer.Setup(l => l[It.IsAny<string>()]).Returns<string>(k => new LocalizedString(k, k));

        var model = new TableModel(
            db: _db,
            companyContext: companyContext.Object,
            logger: NullLogger<TableModel>.Instance,
            busyUserService: Mock.Of<IBusyUserService>(),
            shiftTypeCache: Mock.Of<IShiftTypeCacheService>(),
            programService: Mock.Of<IShiftProgramService>(),
            assignmentService: assignment.Object,
            calendarNotification: Mock.Of<ICalendarNotificationService>(),
            concurrencyService: Mock.Of<IConcurrencyService>(),
            grantService: grant.Object,
            jobTypeService: Mock.Of<IJobTypeService>(),
            companyLocalizationService: Mock.Of<ICompanyLocalizationService>(),
            tenantResolver: Mock.Of<ITenantResolver>(),
            localizer: localizer.Object,
            auditLogService: Mock.Of<IAuditLogService>(),
            draftService: Mock.Of<IDraftModeService>(),
            draftLifecycle: Mock.Of<IDraftLifecycle>(),
            remediation: Mock.Of<IFailureRemediationService>());

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, ManagerUserId.ToString()) }, "test"))
        };
        model.PageContext = new PageContext { HttpContext = httpContext };

        return (model, assignment);
    }

    [Fact]
    public async Task OnPostAssignEmployee_WithExplicitShiftInstanceId_TargetsThatExactInstance_NotFirstByTypeAndDate()
    {
        var (model, assignment) = BuildModel();

        await model.OnPostAssignEmployeeAsync(new TableModel.AssignEmployeeRequest
        {
            ShiftTypeId = ShiftTypeId,
            Date = WorkDate,
            UserId = AssigneeUserId,
            ShiftInstanceId = InstanceCompanyB // explicitly Company B's instance
        });

        assignment.Verify(
            s => s.AssignShiftAsync(AssigneeUserId, InstanceCompanyB, It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Once,
            "the explicit ShiftInstanceId must be honored so the clicked hole (Company B) is filled");
        assignment.Verify(
            s => s.AssignShiftAsync(It.IsAny<int>(), InstanceCompanyA, It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Never,
            "must NOT fall back to FirstOrDefault(ShiftType+Date) = Company A's instance");
    }

    [Fact]
    public async Task OnPostAssignEmployee_WithoutShiftInstanceId_FallsBackToFirstMatchingTypeAndDate()
    {
        var (model, assignment) = BuildModel();

        await model.OnPostAssignEmployeeAsync(new TableModel.AssignEmployeeRequest
        {
            ShiftTypeId = ShiftTypeId,
            Date = WorkDate,
            UserId = AssigneeUserId
            // no ShiftInstanceId -> legacy ShiftType+Date resolution preserved
        });

        assignment.Verify(
            s => s.AssignShiftAsync(AssigneeUserId, InstanceCompanyA, It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Once,
            "with no explicit instance, the legacy first-match-by-ShiftType+Date behavior is preserved (additive change)");
    }
}
