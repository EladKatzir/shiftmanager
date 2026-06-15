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
using ShiftManager.Pages.Admin.Organization.ChoreTypes;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Pages;

/// <summary>
/// Covers the ChoreTypes/Index exemption AJAX handlers: add by an authorized admin persists a waiver;
/// the audit description NEVER contains the reason text (sensitive); remove deletes it; an unauthorized
/// caller (no molecule access) is rejected 403.
/// </summary>
public class ChoreTypesExemptionHandlerTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly Mock<IGrantService> _grants = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly Mock<ICompanyContext> _companyContext = new();

    private const string SecretReason = "knee-surgery-2026";

    public ChoreTypesExemptionHandlerTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
    }

    private Mock<IStringLocalizer<SharedResources>> BuildLocalizer()
    {
        var loc = new Mock<IStringLocalizer<SharedResources>>();
        loc.Setup(l => l[It.IsAny<string>()]).Returns<string>(k => new LocalizedString(k, k));
        loc.Setup(l => l[It.IsAny<string>(), It.IsAny<object[]>()]).Returns<string, object[]>((k, _) => new LocalizedString(k, k));
        return loc;
    }

    private IndexModel BuildModel(int callerId)
    {
        var model = new IndexModel(
            BuildLocalizer().Object, _db, NullLogger<IndexModel>.Instance,
            new ChoreTypeService(_db), new ChoreCategoryService(_db), new ChoreEligibilityAdminService(_db),
            _grants.Object, _companyContext.Object, _audit.Object);

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, callerId.ToString()) }, "test"))
        };
        model.PageContext = new Microsoft.AspNetCore.Mvc.RazorPages.PageContext { HttpContext = httpContext };
        model.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        return model;
    }

    private async Task<(int typeId, int userId)> SeedAsync()
    {
        _db.Projects.Add(new Project { Id = 1, Name = "P", DisplayName = "P" });
        _db.Areas.Add(new Area { Id = 1, ProjectId = 1, Name = "A", DisplayName = "A" });
        _db.Molecules.Add(new Molecule { Id = 1, AreaId = 1, Name = "M1", DisplayName = "M1" });
        _db.Companies.Add(new Company { Id = 1, Name = "Co", Slug = "co", MoleculeId = 1 });
        _db.Users.Add(new AppUser { Id = 10, CompanyId = 1, Email = "a@x.mil", DisplayName = "A", AccountType = AccountType.Standard, IsActive = true });
        await _db.SaveChangesAsync();
        var ct = await new ChoreTypeService(_db).CreateAsync(1, "kitchen", "Kitchen", null, 999);
        return (ct.Id, 10);
    }

    [Fact]
    public async Task OnPostAddExemption_Authorized_Persists_And_Audit_Has_No_Reason()
    {
        var (typeId, userId) = await SeedAsync();
        const int caller = 999;
        _companyContext.Setup(c => c.CompanyId).Returns((int?)null);
        _grants.Setup(g => g.GetAccessibleMoleculeIdsForGrantAsync(caller, "EditChoreTypes")).ReturnsAsync(new List<int> { 1 });

        string? capturedDescription = null;
        _audit.Setup(a => a.LogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Callback<string, string, int?, string, string?>((_, _, _, desc, _) => capturedDescription = desc)
            .Returns(Task.CompletedTask);

        var model = BuildModel(caller);
        var result = await model.OnPostAddExemptionAsync(new IndexModel.ExemptionRequest
        {
            ChoreTypeId = typeId, UserId = userId, Reason = SecretReason
        });

        var json = result.Should().BeOfType<JsonResult>().Subject;
        json.Value!.GetType().GetProperty("success")!.GetValue(json.Value).Should().Be(true);

        var ex = await _db.UserChoreExemptions.SingleAsync();
        ex.UserId.Should().Be(userId);
        ex.Reason.Should().Be(SecretReason, "the reason IS persisted on the row (just never audited)");

        capturedDescription.Should().NotBeNull();
        capturedDescription!.Should().NotContain(SecretReason, "audit log must never include the sensitive reason text");
    }

    [Fact]
    public async Task OnPostAddExemption_Unauthorized_Is_Rejected_403()
    {
        var (typeId, userId) = await SeedAsync();
        const int caller = 777;
        _companyContext.Setup(c => c.CompanyId).Returns((int?)null);
        _grants.Setup(g => g.GetAccessibleMoleculeIdsForGrantAsync(caller, "EditChoreTypes")).ReturnsAsync(new List<int>());

        var model = BuildModel(caller);
        var result = await model.OnPostAddExemptionAsync(new IndexModel.ExemptionRequest
        {
            ChoreTypeId = typeId, UserId = userId, Reason = "x"
        });

        var json = result.Should().BeOfType<JsonResult>().Subject;
        json.StatusCode.Should().Be(403);
        (await _db.UserChoreExemptions.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task OnPostRemoveExemption_Authorized_Deletes()
    {
        var (typeId, userId) = await SeedAsync();
        const int caller = 999;
        _companyContext.Setup(c => c.CompanyId).Returns((int?)null);
        _grants.Setup(g => g.GetAccessibleMoleculeIdsForGrantAsync(caller, "EditChoreTypes")).ReturnsAsync(new List<int> { 1 });
        await new ChoreEligibilityAdminService(_db).AddExemptionAsync(userId, typeId, "x", caller);

        var model = BuildModel(caller);
        var result = await model.OnPostRemoveExemptionAsync(new IndexModel.ExemptionRequest { ChoreTypeId = typeId, UserId = userId });

        var json = result.Should().BeOfType<JsonResult>().Subject;
        json.Value!.GetType().GetProperty("success")!.GetValue(json.Value).Should().Be(true);
        (await _db.UserChoreExemptions.AnyAsync()).Should().BeFalse();
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }
}
