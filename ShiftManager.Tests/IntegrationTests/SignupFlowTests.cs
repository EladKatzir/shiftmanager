using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using Moq;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using ShiftManager.Data;
using ShiftManager.Data.SeedData;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using Microsoft.Extensions.DependencyInjection;
using ShiftManager.Pages.Auth;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Net;

namespace ShiftManager.Tests.IntegrationTests;

/// <summary>
/// Comprehensive signup flow tests covering every visible role template
/// across all molecule types (Workforce, Tech, Helper).
/// Verifies that the signup backend correctly:
/// - Creates UserJoinRequest with proper CompanyId, JobTypeId, Role
/// - Auto-resolves HQ for Director/MoleculeAdmin/AreaAdmin
/// - Clears JobTypeId for MoleculeAdmin/AreaAdmin (molecule/area scope)
/// - Accepts JobTypeId for Director (molecule+jobtype scope)
/// - Handles all molecule types consistently
/// </summary>
public class SignupFlowTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Mock<IFeatureFlagService> _featureFlags;
    private readonly Mock<IValidationService> _validation;
    private readonly Mock<INotificationService> _notifications;
    private readonly Mock<IRateLimitingService> _rateLimiting;
    private readonly Mock<IAuditLogService> _auditLogService;
    private readonly Mock<ICompanyCacheService> _companyCache;
    private readonly Mock<IRoleService> _roleService;
    private readonly Mock<IStringLocalizer<SharedResources>> _localizer;
    private readonly Mock<ILogger<SignupModel>> _logger;

    // Seed data references
    private Area _area = null!;
    private Molecule _workforceMol = null!;
    private Molecule _techMol = null!;
    private Molecule _helperMol = null!;
    private Company _workforceCompany = null!;
    private Company _workforceHq = null!;
    private Company _techHq = null!;
    private Company _helperHq = null!;
    private JobType _jobType = null!;
    private List<RoleTemplate> _roleTemplates = null!;

    public SignupFlowTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("SignupFlowTests_" + Guid.NewGuid())
            .Options;
        _db = new AppDbContext(options);

        // Mock services
        _featureFlags = new Mock<IFeatureFlagService>();
        _featureFlags.Setup(f => f.IsEnabledAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>())).ReturnsAsync(true);

        _validation = new Mock<IValidationService>();
        _validation.Setup(v => v.IsValidEmail(It.IsAny<string>())).Returns(true);

        _notifications = new Mock<INotificationService>();
        _rateLimiting = new Mock<IRateLimitingService>();
        _auditLogService = new Mock<IAuditLogService>();
        _rateLimiting.Setup(r => r.IsAllowed(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>())).Returns(true);

        _companyCache = new Mock<ICompanyCacheService>();
        _roleService = new Mock<IRoleService>();
        _logger = new Mock<ILogger<SignupModel>>();

        // Localizer returns key as value (sufficient for tests)
        _localizer = new Mock<IStringLocalizer<SharedResources>>();
        _localizer.Setup(l => l[It.IsAny<string>()])
            .Returns<string>(key => new LocalizedString(key, key));
        _localizer.Setup(l => l[It.IsAny<string>(), It.IsAny<object[]>()])
            .Returns<string, object[]>((key, args) => new LocalizedString(key, string.Format(key, args)));

        SeedOrganization().Wait();
    }

    private async Task SeedOrganization()
    {
        // Project
        var project = new Project { Name = "TestProject", DisplayName = "Test", IsActive = true };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();

        // Area
        _area = new Area { ProjectId = project.Id, Name = "TestArea", DisplayName = "Test Area", IsActive = true };
        _db.Areas.Add(_area);
        await _db.SaveChangesAsync();

        // Job Type
        _jobType = new JobType { AreaId = _area.Id, Name = "BR", DisplayName = "BR", SortOrder = 1 };
        _db.JobTypes.Add(_jobType);
        await _db.SaveChangesAsync();

        // Molecules (one of each type)
        _workforceMol = new Molecule { AreaId = _area.Id, Name = "WorkforceMol", DisplayName = "Workforce", Type = MoleculeType.Workforce, IsActive = true };
        _techMol = new Molecule { AreaId = _area.Id, Name = "TechMol", DisplayName = "Tech", Type = MoleculeType.Tech, IsActive = true };
        _helperMol = new Molecule { AreaId = _area.Id, Name = "HelperMol", DisplayName = "Helper", Type = MoleculeType.Helper, IsActive = true };
        _db.Molecules.AddRange(_workforceMol, _techMol, _helperMol);
        await _db.SaveChangesAsync();

        // Regular company (workforce)
        _workforceCompany = new Company { Name = "RegularCo", DisplayName = "Regular", MoleculeId = _workforceMol.Id };
        _db.Companies.Add(_workforceCompany);

        // HQ companies (one per molecule)
        _workforceHq = new Company { Name = "WorkforceHQ", DisplayName = "HQ", MoleculeId = _workforceMol.Id, IsHeadquarters = true };
        _techHq = new Company { Name = "TechHQ", DisplayName = "HQ", MoleculeId = _techMol.Id, IsHeadquarters = true };
        _helperHq = new Company { Name = "HelperHQ", DisplayName = "HQ", MoleculeId = _helperMol.Id, IsHeadquarters = true };
        _db.Companies.AddRange(_workforceHq, _techHq, _helperHq);
        await _db.SaveChangesAsync();

        // Role Templates (from seed)
        _roleTemplates = RoleTemplateSeed.GetRoleTemplates();
        _db.RoleTemplates.AddRange(_roleTemplates);
        await _db.SaveChangesAsync();

        // Setup role service to return templates
        foreach (var rt in _roleTemplates)
        {
            _roleService.Setup(r => r.GetRoleTemplateAsync(rt.Id)).ReturnsAsync(rt);
        }

        // Setup company cache
        _companyCache.Setup(c => c.GetCompanyAsync(_workforceCompany.Id)).ReturnsAsync(_workforceCompany);
        _companyCache.Setup(c => c.GetCompanyAsync(_workforceHq.Id)).ReturnsAsync(_workforceHq);
        _companyCache.Setup(c => c.GetCompanyAsync(_techHq.Id)).ReturnsAsync(_techHq);
        _companyCache.Setup(c => c.GetCompanyAsync(_helperHq.Id)).ReturnsAsync(_helperHq);
    }

    private SignupModel CreateSignupModel()
    {
        var backgroundTaskQueue = new Mock<IBackgroundTaskQueue>();
        var model = new SignupModel(
            _db, _logger.Object, _localizer.Object,
            _featureFlags.Object, _validation.Object,
            _notifications.Object, _rateLimiting.Object,
            _auditLogService.Object,
            _companyCache.Object, _roleService.Object,
            backgroundTaskQueue.Object);

        // Setup HttpContext so rate limiting and ModelState work
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Loopback;
        var actionContext = new ActionContext(httpContext, new RouteData(), new PageActionDescriptor());
        model.PageContext = new PageContext(actionContext);

        return model;
    }

    // ─────────────────────────────────────────────
    // Employee (ScopeLevel=Implicit) — needs Molecule + JobType + Company
    // ─────────────────────────────────────────────

    [Fact]
    public async Task Signup_Employee_Workforce_CreatesCorrectJoinRequest()
    {
        var model = CreateSignupModel();
        model.Email = "emp.wf@test.com";
        model.DisplayName = "Employee WF";
        model.Password = "Test1234!";
        model.MoleculeId = _workforceMol.Id;
        model.JobTypeId = _jobType.Id;
        model.CompanyId = _workforceCompany.Id;
        model.RequestedRole = UserRole.Employee;
        model.RequestedRoleTemplateId = 1; // Employee template

        await model.OnPostAsync();

        var jr = await _db.UserJoinRequests.FirstOrDefaultAsync(j => j.Email == "emp.wf@test.com");
        jr.Should().NotBeNull("Employee signup should create a join request");
        jr!.CompanyId.Should().Be(_workforceCompany.Id, "Employee should be in the selected company");
        jr.JobTypeId.Should().Be(_jobType.Id, "Employee should have the selected job type");
        jr.RequestedRole.Should().Be(UserRole.Employee);
        jr.RequestedRoleTemplateId.Should().Be(1);
        jr.Status.Should().Be(JoinRequestStatus.Pending);
    }

    [Fact]
    public async Task Signup_Employee_Tech_CreatesCorrectJoinRequest()
    {
        var model = CreateSignupModel();
        model.Email = "emp.tech@test.com";
        model.DisplayName = "Employee Tech";
        model.Password = "Test1234!";
        model.MoleculeId = _techMol.Id;
        model.JobTypeId = _jobType.Id;
        model.CompanyId = _techHq.Id; // Tech molecules might only have HQ
        model.RequestedRole = UserRole.Employee;
        model.RequestedRoleTemplateId = 1;

        await model.OnPostAsync();

        var jr = await _db.UserJoinRequests.FirstOrDefaultAsync(j => j.Email == "emp.tech@test.com");
        jr.Should().NotBeNull("Employee signup in Tech molecule should work");
        jr!.CompanyId.Should().Be(_techHq.Id);
        jr.JobTypeId.Should().Be(_jobType.Id);
    }

    // ─────────────────────────────────────────────
    // BRDirector (ScopeLevel=Company) — needs Molecule + JobType + Company
    // ─────────────────────────────────────────────

    [Fact]
    public async Task Signup_BRDirector_Workforce_CreatesCorrectJoinRequest()
    {
        var model = CreateSignupModel();
        model.Email = "brd@test.com";
        model.DisplayName = "BR Director";
        model.Password = "Test1234!";
        model.MoleculeId = _workforceMol.Id;
        model.JobTypeId = _jobType.Id;
        model.CompanyId = _workforceCompany.Id;
        model.RequestedRole = UserRole.Manager; // BRDirector derives to Manager
        model.RequestedRoleTemplateId = 2;

        await model.OnPostAsync();

        var jr = await _db.UserJoinRequests.FirstOrDefaultAsync(j => j.Email == "brd@test.com");
        jr.Should().NotBeNull("BRDirector signup should create a join request");
        jr!.CompanyId.Should().Be(_workforceCompany.Id, "BRDirector should be in the selected company");
        jr.JobTypeId.Should().Be(_jobType.Id);
        jr.RequestedRole.Should().Be(UserRole.Manager);
        jr.RequestedRoleTemplateId.Should().Be(2);
    }

    // ─────────────────────────────────────────────
    // Lead (ScopeLevel=CompanyJobType) — needs Molecule + JobType + Company
    // ─────────────────────────────────────────────

    [Fact]
    public async Task Signup_Lead_Workforce_CreatesCorrectJoinRequest()
    {
        var model = CreateSignupModel();
        model.Email = "lead@test.com";
        model.DisplayName = "Lead";
        model.Password = "Test1234!";
        model.MoleculeId = _workforceMol.Id;
        model.JobTypeId = _jobType.Id;
        model.CompanyId = _workforceCompany.Id;
        model.RequestedRole = UserRole.Manager; // Lead derives to Manager
        model.RequestedRoleTemplateId = 3;

        await model.OnPostAsync();

        var jr = await _db.UserJoinRequests.FirstOrDefaultAsync(j => j.Email == "lead@test.com");
        jr.Should().NotBeNull("Lead signup should create a join request");
        jr!.CompanyId.Should().Be(_workforceCompany.Id);
        jr.JobTypeId.Should().Be(_jobType.Id);
        jr.RequestedRoleTemplateId.Should().Be(3);
    }

    // ─────────────────────────────────────────────
    // Director (ScopeLevel=MoleculeJobType) — needs Molecule + JobType, auto-HQ
    // ─────────────────────────────────────────────

    [Theory]
    [InlineData("dir.wf@test.com", "Director WF")]
    public async Task Signup_Director_Workforce_AutoResolvesToHQ(string email, string name)
    {
        var model = CreateSignupModel();
        model.Email = email;
        model.DisplayName = name;
        model.Password = "Test1234!";
        model.MoleculeId = _workforceMol.Id;
        model.JobTypeId = _jobType.Id;
        model.CompanyId = 0; // Not selected (auto-HQ)
        model.RequestedRole = UserRole.Director;
        model.RequestedRoleTemplateId = 5;

        await model.OnPostAsync();

        var jr = await _db.UserJoinRequests.FirstOrDefaultAsync(j => j.Email == email);
        jr.Should().NotBeNull("Director signup should create a join request");
        jr!.CompanyId.Should().Be(_workforceHq.Id, "Director should be auto-assigned to HQ company");
        jr.JobTypeId.Should().Be(_jobType.Id, "Director still needs a job type (molecule+jobtype scope)");
        jr.RequestedRole.Should().Be(UserRole.Director);
    }

    [Fact]
    public async Task Signup_Director_Tech_AutoResolvesToHQ()
    {
        var model = CreateSignupModel();
        model.Email = "dir.tech@test.com";
        model.DisplayName = "Director Tech";
        model.Password = "Test1234!";
        model.MoleculeId = _techMol.Id;
        model.JobTypeId = _jobType.Id;
        model.CompanyId = 0;
        model.RequestedRole = UserRole.Director;
        model.RequestedRoleTemplateId = 5;

        await model.OnPostAsync();

        var jr = await _db.UserJoinRequests.FirstOrDefaultAsync(j => j.Email == "dir.tech@test.com");
        jr.Should().NotBeNull();
        jr!.CompanyId.Should().Be(_techHq.Id, "Director in Tech molecule should auto-resolve to Tech HQ");
        jr.JobTypeId.Should().Be(_jobType.Id, "Director still needs job type regardless of molecule type");
    }

    // ─────────────────────────────────────────────
    // MoleculeAdmin (ScopeLevel=Molecule) — needs Molecule only, auto-HQ, NO JobType
    // ─────────────────────────────────────────────

    [Fact]
    public async Task Signup_MoleculeAdmin_Workforce_AutoHQ_NoJobType()
    {
        var model = CreateSignupModel();
        model.Email = "moladmin.wf@test.com";
        model.DisplayName = "MolAdmin WF";
        model.Password = "Test1234!";
        model.MoleculeId = _workforceMol.Id;
        model.JobTypeId = 0; // Not selected (molecule scope)
        model.CompanyId = 0; // Not selected (auto-HQ)
        model.RequestedRole = UserRole.Manager; // MoleculeAdmin derives to Manager
        model.RequestedRoleTemplateId = 7;

        await model.OnPostAsync();

        var jr = await _db.UserJoinRequests.FirstOrDefaultAsync(j => j.Email == "moladmin.wf@test.com");
        jr.Should().NotBeNull("MoleculeAdmin signup should create a join request");
        jr!.CompanyId.Should().Be(_workforceHq.Id, "MoleculeAdmin should be auto-assigned to HQ");
        jr.JobTypeId.Should().BeNull("MoleculeAdmin should have null JobTypeId (molecule-wide scope)");
        jr.RequestedRole.Should().Be(UserRole.Manager);
        jr.RequestedRoleTemplateId.Should().Be(7);
    }

    [Fact]
    public async Task Signup_MoleculeAdmin_Tech_AutoHQ_NoJobType()
    {
        var model = CreateSignupModel();
        model.Email = "moladmin.tech@test.com";
        model.DisplayName = "MolAdmin Tech";
        model.Password = "Test1234!";
        model.MoleculeId = _techMol.Id;
        model.JobTypeId = 0;
        model.CompanyId = 0;
        model.RequestedRole = UserRole.Manager;
        model.RequestedRoleTemplateId = 7;

        await model.OnPostAsync();

        var jr = await _db.UserJoinRequests.FirstOrDefaultAsync(j => j.Email == "moladmin.tech@test.com");
        jr.Should().NotBeNull("MoleculeAdmin signup in Tech molecule should work");
        jr!.CompanyId.Should().Be(_techHq.Id);
        jr.JobTypeId.Should().BeNull("MoleculeAdmin in Tech molecule should also have null JobTypeId");
    }

    [Fact]
    public async Task Signup_MoleculeAdmin_Helper_AutoHQ_NoJobType()
    {
        var model = CreateSignupModel();
        model.Email = "moladmin.helper@test.com";
        model.DisplayName = "MolAdmin Helper";
        model.Password = "Test1234!";
        model.MoleculeId = _helperMol.Id;
        model.JobTypeId = 0;
        model.CompanyId = 0;
        model.RequestedRole = UserRole.Manager;
        model.RequestedRoleTemplateId = 7;

        await model.OnPostAsync();

        var jr = await _db.UserJoinRequests.FirstOrDefaultAsync(j => j.Email == "moladmin.helper@test.com");
        jr.Should().NotBeNull("MoleculeAdmin signup in Helper molecule should work");
        jr!.CompanyId.Should().Be(_helperHq.Id);
        jr.JobTypeId.Should().BeNull();
    }

    // ─────────────────────────────────────────────
    // AreaAdmin (ScopeLevel=Area) — needs Molecule (for HQ lookup), auto-HQ, NO JobType
    // ─────────────────────────────────────────────

    [Fact]
    public async Task Signup_AreaAdmin_Workforce_AutoHQ_NoJobType()
    {
        var model = CreateSignupModel();
        model.Email = "areaadmin.wf@test.com";
        model.DisplayName = "AreaAdmin WF";
        model.Password = "Test1234!";
        model.MoleculeId = _workforceMol.Id; // Area derived from molecule
        model.JobTypeId = 0; // Not selected (area scope)
        model.CompanyId = 0; // Not selected (auto-HQ)
        model.RequestedRole = UserRole.AreaAdmin;
        model.RequestedRoleTemplateId = 10;

        await model.OnPostAsync();

        var jr = await _db.UserJoinRequests.FirstOrDefaultAsync(j => j.Email == "areaadmin.wf@test.com");
        jr.Should().NotBeNull("AreaAdmin signup should create a join request");
        jr!.CompanyId.Should().Be(_workforceHq.Id, "AreaAdmin should be auto-assigned to molecule's HQ");
        jr.JobTypeId.Should().BeNull("AreaAdmin should have null JobTypeId (area-wide scope)");
        jr.RequestedRole.Should().Be(UserRole.AreaAdmin);
        jr.RequestedRoleTemplateId.Should().Be(10);
    }

    [Fact]
    public async Task Signup_AreaAdmin_Tech_AutoHQ_NoJobType()
    {
        var model = CreateSignupModel();
        model.Email = "areaadmin.tech@test.com";
        model.DisplayName = "AreaAdmin Tech";
        model.Password = "Test1234!";
        model.MoleculeId = _techMol.Id;
        model.JobTypeId = 0;
        model.CompanyId = 0;
        model.RequestedRole = UserRole.AreaAdmin;
        model.RequestedRoleTemplateId = 10;

        await model.OnPostAsync();

        var jr = await _db.UserJoinRequests.FirstOrDefaultAsync(j => j.Email == "areaadmin.tech@test.com");
        jr.Should().NotBeNull();
        jr!.CompanyId.Should().Be(_techHq.Id, "AreaAdmin from Tech molecule should be placed in Tech HQ");
        jr.JobTypeId.Should().BeNull("AreaAdmin should always have null JobTypeId");
    }

    [Fact]
    public async Task Signup_AreaAdmin_Helper_AutoHQ_NoJobType()
    {
        var model = CreateSignupModel();
        model.Email = "areaadmin.helper@test.com";
        model.DisplayName = "AreaAdmin Helper";
        model.Password = "Test1234!";
        model.MoleculeId = _helperMol.Id;
        model.JobTypeId = 0;
        model.CompanyId = 0;
        model.RequestedRole = UserRole.AreaAdmin;
        model.RequestedRoleTemplateId = 10;

        await model.OnPostAsync();

        var jr = await _db.UserJoinRequests.FirstOrDefaultAsync(j => j.Email == "areaadmin.helper@test.com");
        jr.Should().NotBeNull();
        jr!.CompanyId.Should().Be(_helperHq.Id);
        jr.JobTypeId.Should().BeNull();
    }

    // ─────────────────────────────────────────────
    // Edge cases
    // ─────────────────────────────────────────────

    [Fact]
    public async Task Signup_MoleculeAdmin_WithJobTypeProvided_ClearsJobType()
    {
        // Even if frontend accidentally sends a JobTypeId, backend should clear it
        var model = CreateSignupModel();
        model.Email = "moladmin.clear@test.com";
        model.DisplayName = "MolAdmin ClearJT";
        model.Password = "Test1234!";
        model.MoleculeId = _workforceMol.Id;
        model.JobTypeId = _jobType.Id; // Accidentally provided
        model.CompanyId = 0;
        model.RequestedRole = UserRole.Manager;
        model.RequestedRoleTemplateId = 7; // MoleculeAdmin

        await model.OnPostAsync();

        var jr = await _db.UserJoinRequests.FirstOrDefaultAsync(j => j.Email == "moladmin.clear@test.com");
        jr.Should().NotBeNull();
        jr!.JobTypeId.Should().BeNull("MoleculeAdmin should clear JobTypeId even if provided");
    }

    [Fact]
    public async Task Signup_AreaAdmin_WithJobTypeProvided_ClearsJobType()
    {
        var model = CreateSignupModel();
        model.Email = "areaadmin.clear@test.com";
        model.DisplayName = "AreaAdmin ClearJT";
        model.Password = "Test1234!";
        model.MoleculeId = _workforceMol.Id;
        model.JobTypeId = _jobType.Id; // Accidentally provided
        model.CompanyId = 0;
        model.RequestedRole = UserRole.AreaAdmin;
        model.RequestedRoleTemplateId = 10;

        await model.OnPostAsync();

        var jr = await _db.UserJoinRequests.FirstOrDefaultAsync(j => j.Email == "areaadmin.clear@test.com");
        jr.Should().NotBeNull();
        jr!.JobTypeId.Should().BeNull("AreaAdmin should clear JobTypeId even if provided");
    }

    [Fact]
    public async Task Signup_Director_WithoutMolecule_Fails()
    {
        var model = CreateSignupModel();
        model.Email = "dir.nomol@test.com";
        model.DisplayName = "Director NoMol";
        model.Password = "Test1234!";
        model.MoleculeId = null; // Missing!
        model.JobTypeId = _jobType.Id;
        model.CompanyId = 0;
        model.RequestedRole = UserRole.Director;
        model.RequestedRoleTemplateId = 5;

        await model.OnPostAsync();

        var jr = await _db.UserJoinRequests.FirstOrDefaultAsync(j => j.Email == "dir.nomol@test.com");
        jr.Should().BeNull("Director signup without molecule should fail");
    }

    [Fact]
    public async Task Signup_DuplicateEmail_Fails()
    {
        // First signup
        _db.Users.Add(new AppUser
        {
            Email = "existing@test.com",
            DisplayName = "Existing",
            CompanyId = _workforceCompany.Id,
            Role = UserRole.Employee,
            IsActive = true,
            PasswordHash = Array.Empty<byte>(),
            PasswordSalt = Array.Empty<byte>()
        });
        await _db.SaveChangesAsync();

        var model = CreateSignupModel();
        model.Email = "existing@test.com";
        model.DisplayName = "Duplicate";
        model.Password = "Test1234!";
        model.MoleculeId = _workforceMol.Id;
        model.JobTypeId = _jobType.Id;
        model.CompanyId = _workforceCompany.Id;
        model.RequestedRole = UserRole.Employee;
        model.RequestedRoleTemplateId = 1;

        await model.OnPostAsync();

        var jr = await _db.UserJoinRequests.FirstOrDefaultAsync(j => j.Email == "existing@test.com");
        jr.Should().BeNull("Duplicate email signup should fail");
    }

    // ─────────────────────────────────────────────
    // Cross-molecule consistency: same role across all molecule types
    // ─────────────────────────────────────────────

    [Theory]
    [InlineData("emp.cross.wf@test.com", "WorkforceMol")]
    [InlineData("emp.cross.tech@test.com", "TechMol")]
    [InlineData("emp.cross.helper@test.com", "HelperMol")]
    public async Task Signup_Employee_AllMoleculeTypes_Consistent(string email, string molName)
    {
        var mol = await _db.Molecules.FirstAsync(m => m.Name == molName);
        var hqCompany = await _db.Companies.FirstAsync(c => c.MoleculeId == mol.Id && c.IsHeadquarters);

        var model = CreateSignupModel();
        model.Email = email;
        model.DisplayName = "Cross Employee";
        model.Password = "Test1234!";
        model.MoleculeId = mol.Id;
        model.JobTypeId = _jobType.Id;
        model.CompanyId = hqCompany.Id;
        model.RequestedRole = UserRole.Employee;
        model.RequestedRoleTemplateId = 1;

        _companyCache.Setup(c => c.GetCompanyAsync(hqCompany.Id)).ReturnsAsync(hqCompany);

        await model.OnPostAsync();

        var jr = await _db.UserJoinRequests.FirstOrDefaultAsync(j => j.Email == email);
        jr.Should().NotBeNull($"Employee signup in {molName} should succeed");
        jr!.JobTypeId.Should().Be(_jobType.Id);
    }

    [Theory]
    [InlineData("moladmin.cross.wf@test.com", "WorkforceMol")]
    [InlineData("moladmin.cross.tech@test.com", "TechMol")]
    [InlineData("moladmin.cross.helper@test.com", "HelperMol")]
    public async Task Signup_MoleculeAdmin_AllMoleculeTypes_NoJobType(string email, string molName)
    {
        var mol = await _db.Molecules.FirstAsync(m => m.Name == molName);
        var hqCompany = await _db.Companies.FirstAsync(c => c.MoleculeId == mol.Id && c.IsHeadquarters);

        var model = CreateSignupModel();
        model.Email = email;
        model.DisplayName = "Cross MolAdmin";
        model.Password = "Test1234!";
        model.MoleculeId = mol.Id;
        model.JobTypeId = 0;
        model.CompanyId = 0;
        model.RequestedRole = UserRole.Manager;
        model.RequestedRoleTemplateId = 7;

        await model.OnPostAsync();

        var jr = await _db.UserJoinRequests.FirstOrDefaultAsync(j => j.Email == email);
        jr.Should().NotBeNull($"MoleculeAdmin signup in {molName} should succeed");
        jr!.CompanyId.Should().Be(hqCompany.Id, "Should auto-resolve to HQ");
        jr.JobTypeId.Should().BeNull("MoleculeAdmin should never have JobTypeId");
    }

    [Theory]
    [InlineData("areaadmin.cross.wf@test.com", "WorkforceMol")]
    [InlineData("areaadmin.cross.tech@test.com", "TechMol")]
    [InlineData("areaadmin.cross.helper@test.com", "HelperMol")]
    public async Task Signup_AreaAdmin_AllMoleculeTypes_NoJobType(string email, string molName)
    {
        var mol = await _db.Molecules.FirstAsync(m => m.Name == molName);
        var hqCompany = await _db.Companies.FirstAsync(c => c.MoleculeId == mol.Id && c.IsHeadquarters);

        var model = CreateSignupModel();
        model.Email = email;
        model.DisplayName = "Cross AreaAdmin";
        model.Password = "Test1234!";
        model.MoleculeId = mol.Id;
        model.JobTypeId = 0;
        model.CompanyId = 0;
        model.RequestedRole = UserRole.AreaAdmin;
        model.RequestedRoleTemplateId = 10;

        await model.OnPostAsync();

        var jr = await _db.UserJoinRequests.FirstOrDefaultAsync(j => j.Email == email);
        jr.Should().NotBeNull($"AreaAdmin signup via {molName} should succeed");
        jr!.CompanyId.Should().Be(hqCompany.Id, "Should auto-resolve to molecule's HQ");
        jr.JobTypeId.Should().BeNull("AreaAdmin should never have JobTypeId");
    }

    public void Dispose() => _db?.Dispose();
}
