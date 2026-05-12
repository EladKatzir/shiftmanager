using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services;

public class ShiftGroupingServiceTests : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly AppDbContext _db;
    private readonly ShiftGroupingService _service;

    private const int MoleculeId = 1;
    private const int AreaId = 1;

    public ShiftGroupingServiceTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _sqliteConnection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _service = new ShiftGroupingService(_db);

        SeedBaseData();
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
    }

    private void SeedBaseData()
    {
        _db.Set<Area>().Add(new Area { Id = AreaId, Name = "TestArea", DisplayName = "Test Area", ProjectId = 1 });
        _db.Set<Molecule>().Add(new Molecule { Id = MoleculeId, AreaId = AreaId, Name = "TestMol", DisplayName = "Test Molecule" });
        _db.SaveChanges();
    }

    private Company SeedCompany(int id, string name)
    {
        var company = new Company { Id = id, MoleculeId = MoleculeId, Name = name, DisplayName = name };
        _db.Companies.Add(company);
        _db.SaveChanges();
        return company;
    }

    private JobType SeedJobType(int id, string name)
    {
        var jt = new JobType { Id = id, AreaId = AreaId, Name = name, DisplayName = name, IsActive = true };
        _db.Set<JobType>().Add(jt);
        _db.SaveChanges();
        return jt;
    }

    // --- CreateGroupingAsync ---

    [Fact]
    public async Task CreateGroupingAsync_HappyPath_CreatesGrouping()
    {
        var result = await _service.CreateGroupingAsync(MoleculeId, "north", "North Region");

        result.Should().NotBeNull();
        result!.Name.Should().Be("north");
        result.DisplayName.Should().Be("North Region");
        result.MoleculeId.Should().Be(MoleculeId);
        result.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task CreateGroupingAsync_WithCompaniesAndJobTypes_AddsMembers()
    {
        SeedCompany(1, "CompanyA");
        SeedCompany(2, "CompanyB");
        SeedJobType(1, "Alhut");

        var result = await _service.CreateGroupingAsync(
            MoleculeId, "south", "South Region",
            companyIds: new List<int> { 1, 2 },
            jobTypeIds: new List<int> { 1 });

        result.Should().NotBeNull();
        result!.Companies.Should().HaveCount(2);
        result.JobTypes.Should().HaveCount(1);
    }

    [Fact]
    public async Task CreateGroupingAsync_InvalidMolecule_ReturnsNull()
    {
        var result = await _service.CreateGroupingAsync(999, "invalid", "Invalid");

        result.Should().BeNull();
    }

    // --- GetGroupingAsync ---

    [Fact]
    public async Task GetGroupingAsync_ExistingActive_ReturnsGrouping()
    {
        var created = await _service.CreateGroupingAsync(MoleculeId, "grp1", "Group 1");

        var result = await _service.GetGroupingAsync(created!.Id);

        result.Should().NotBeNull();
        result!.Name.Should().Be("grp1");
    }

    [Fact]
    public async Task GetGroupingAsync_Inactive_ReturnsNull()
    {
        var created = await _service.CreateGroupingAsync(MoleculeId, "grp1", "Group 1");
        await _service.DeactivateGroupingAsync(created!.Id);

        var result = await _service.GetGroupingAsync(created.Id);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetGroupingAsync_NonExistent_ReturnsNull()
    {
        var result = await _service.GetGroupingAsync(999);

        result.Should().BeNull();
    }

    // --- GetGroupingsAsync ---

    [Fact]
    public async Task GetGroupingsAsync_ReturnsByMolecule_OrderedByName()
    {
        await _service.CreateGroupingAsync(MoleculeId, "zulu", "Zulu");
        await _service.CreateGroupingAsync(MoleculeId, "alpha", "Alpha");

        var result = await _service.GetGroupingsAsync(MoleculeId);

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("alpha");
        result[1].Name.Should().Be("zulu");
    }

    [Fact]
    public async Task GetGroupingsAsync_ExcludesInactive()
    {
        var grp = await _service.CreateGroupingAsync(MoleculeId, "active", "Active");
        var grpInactive = await _service.CreateGroupingAsync(MoleculeId, "inactive", "Inactive");
        await _service.DeactivateGroupingAsync(grpInactive!.Id);

        var result = await _service.GetGroupingsAsync(MoleculeId);

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("active");
    }

    // --- GetGroupingsForCompanyAsync ---

    [Fact]
    public async Task GetGroupingsForCompanyAsync_ReturnsMatchingGroupings()
    {
        SeedCompany(1, "CompanyA");
        SeedCompany(2, "CompanyB");

        await _service.CreateGroupingAsync(MoleculeId, "grpA", "Group A", companyIds: new List<int> { 1 });
        await _service.CreateGroupingAsync(MoleculeId, "grpBoth", "Group Both", companyIds: new List<int> { 1, 2 });
        await _service.CreateGroupingAsync(MoleculeId, "grpB", "Group B", companyIds: new List<int> { 2 });

        var result = await _service.GetGroupingsForCompanyAsync(1);

        result.Should().HaveCount(2);
        result.Select(g => g.Name).Should().Contain("grpA").And.Contain("grpBoth");
    }

    // --- GetGroupingsForJobTypeAsync ---

    [Fact]
    public async Task GetGroupingsForJobTypeAsync_ReturnsMatchingGroupings()
    {
        SeedJobType(1, "Alhut");
        SeedJobType(2, "Text");

        await _service.CreateGroupingAsync(MoleculeId, "grpJ1", "JT1", jobTypeIds: new List<int> { 1 });
        await _service.CreateGroupingAsync(MoleculeId, "grpJ2", "JT2", jobTypeIds: new List<int> { 2 });

        var result = await _service.GetGroupingsForJobTypeAsync(1);

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("grpJ1");
    }

    // --- UpdateGroupingAsync ---

    [Fact]
    public async Task UpdateGroupingAsync_UpdatesNameAndDisplayName()
    {
        var grp = await _service.CreateGroupingAsync(MoleculeId, "old", "Old Name");

        var success = await _service.UpdateGroupingAsync(grp!.Id, name: "new", displayName: "New Name");

        success.Should().BeTrue();
        var updated = await _service.GetGroupingAsync(grp.Id);
        updated!.Name.Should().Be("new");
        updated.DisplayName.Should().Be("New Name");
    }

    [Fact]
    public async Task UpdateGroupingAsync_ReplacesCompanies()
    {
        SeedCompany(1, "CompanyA");
        SeedCompany(2, "CompanyB");
        SeedCompany(3, "CompanyC");

        var grp = await _service.CreateGroupingAsync(MoleculeId, "grp", "Group", companyIds: new List<int> { 1, 2 });

        var success = await _service.UpdateGroupingAsync(grp!.Id, companyIds: new List<int> { 2, 3 });

        success.Should().BeTrue();
        var companies = await _service.GetCompaniesInGroupingAsync(grp.Id);
        companies.Select(c => c.Id).Should().BeEquivalentTo(new[] { 2, 3 });
    }

    [Fact]
    public async Task UpdateGroupingAsync_ReplacesJobTypes()
    {
        SeedJobType(1, "JT1");
        SeedJobType(2, "JT2");

        var grp = await _service.CreateGroupingAsync(MoleculeId, "grp", "Group", jobTypeIds: new List<int> { 1 });

        var success = await _service.UpdateGroupingAsync(grp!.Id, jobTypeIds: new List<int> { 2 });

        success.Should().BeTrue();
        var jobTypes = await _service.GetJobTypesInGroupingAsync(grp.Id);
        jobTypes.Should().HaveCount(1);
        jobTypes[0].Id.Should().Be(2);
    }

    [Fact]
    public async Task UpdateGroupingAsync_NonExistent_ReturnsFalse()
    {
        var success = await _service.UpdateGroupingAsync(999, name: "test");

        success.Should().BeFalse();
    }

    // --- DeactivateGroupingAsync ---

    [Fact]
    public async Task DeactivateGroupingAsync_HappyPath_SetsInactive()
    {
        var grp = await _service.CreateGroupingAsync(MoleculeId, "grp", "Group");

        var success = await _service.DeactivateGroupingAsync(grp!.Id);

        success.Should().BeTrue();
        var raw = await _db.ShiftGroupings.FindAsync(grp.Id);
        raw!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DeactivateGroupingAsync_NonExistent_ReturnsFalse()
    {
        var success = await _service.DeactivateGroupingAsync(999);

        success.Should().BeFalse();
    }

    // --- AddCompanyToGroupingAsync / RemoveCompanyFromGroupingAsync ---

    [Fact]
    public async Task AddCompanyToGroupingAsync_AddsCompany()
    {
        SeedCompany(1, "CompanyA");
        var grp = await _service.CreateGroupingAsync(MoleculeId, "grp", "Group");

        var success = await _service.AddCompanyToGroupingAsync(grp!.Id, 1);

        success.Should().BeTrue();
        var companies = await _service.GetCompaniesInGroupingAsync(grp.Id);
        companies.Should().HaveCount(1);
        companies[0].Id.Should().Be(1);
    }

    [Fact]
    public async Task AddCompanyToGroupingAsync_Duplicate_ReturnsTrueWithoutDuplicate()
    {
        SeedCompany(1, "CompanyA");
        var grp = await _service.CreateGroupingAsync(MoleculeId, "grp", "Group", companyIds: new List<int> { 1 });

        var success = await _service.AddCompanyToGroupingAsync(grp!.Id, 1);

        success.Should().BeTrue();
        var companies = await _service.GetCompaniesInGroupingAsync(grp.Id);
        companies.Should().HaveCount(1);
    }

    [Fact]
    public async Task RemoveCompanyFromGroupingAsync_RemovesCompany()
    {
        SeedCompany(1, "CompanyA");
        var grp = await _service.CreateGroupingAsync(MoleculeId, "grp", "Group", companyIds: new List<int> { 1 });

        var success = await _service.RemoveCompanyFromGroupingAsync(grp!.Id, 1);

        success.Should().BeTrue();
        var companies = await _service.GetCompaniesInGroupingAsync(grp.Id);
        companies.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveCompanyFromGroupingAsync_NonExistentMapping_ReturnsFalse()
    {
        var grp = await _service.CreateGroupingAsync(MoleculeId, "grp", "Group");

        var success = await _service.RemoveCompanyFromGroupingAsync(grp!.Id, 999);

        success.Should().BeFalse();
    }

    // --- AddJobTypeToGroupingAsync / RemoveJobTypeFromGroupingAsync ---

    [Fact]
    public async Task AddJobTypeToGroupingAsync_AddsJobType()
    {
        SeedJobType(1, "JT1");
        var grp = await _service.CreateGroupingAsync(MoleculeId, "grp", "Group");

        var success = await _service.AddJobTypeToGroupingAsync(grp!.Id, 1);

        success.Should().BeTrue();
        var jobTypes = await _service.GetJobTypesInGroupingAsync(grp.Id);
        jobTypes.Should().HaveCount(1);
    }

    [Fact]
    public async Task AddJobTypeToGroupingAsync_Duplicate_ReturnsTrueWithoutDuplicate()
    {
        SeedJobType(1, "JT1");
        var grp = await _service.CreateGroupingAsync(MoleculeId, "grp", "Group", jobTypeIds: new List<int> { 1 });

        var success = await _service.AddJobTypeToGroupingAsync(grp!.Id, 1);

        success.Should().BeTrue();
        var jobTypes = await _service.GetJobTypesInGroupingAsync(grp.Id);
        jobTypes.Should().HaveCount(1);
    }

    [Fact]
    public async Task RemoveJobTypeFromGroupingAsync_RemovesJobType()
    {
        SeedJobType(1, "JT1");
        var grp = await _service.CreateGroupingAsync(MoleculeId, "grp", "Group", jobTypeIds: new List<int> { 1 });

        var success = await _service.RemoveJobTypeFromGroupingAsync(grp!.Id, 1);

        success.Should().BeTrue();
        var jobTypes = await _service.GetJobTypesInGroupingAsync(grp.Id);
        jobTypes.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveJobTypeFromGroupingAsync_NonExistentMapping_ReturnsFalse()
    {
        var grp = await _service.CreateGroupingAsync(MoleculeId, "grp", "Group");

        var success = await _service.RemoveJobTypeFromGroupingAsync(grp!.Id, 999);

        success.Should().BeFalse();
    }

    // --- GetUsersInGroupingAsync ---

    [Fact]
    public async Task GetUsersInGroupingAsync_ReturnsUsersMatchingCompanyAndJobType()
    {
        SeedCompany(1, "CompanyA");
        SeedCompany(2, "CompanyB");
        SeedJobType(1, "Alhut");
        SeedJobType(2, "Text");

        _db.Users.AddRange(
            new AppUser { Id = 1, Email = "u1@test.com", DisplayName = "User 1", CompanyId = 1, JobTypeId = 1, IsActive = true },
            new AppUser { Id = 2, Email = "u2@test.com", DisplayName = "User 2", CompanyId = 1, JobTypeId = 2, IsActive = true },
            new AppUser { Id = 3, Email = "u3@test.com", DisplayName = "User 3", CompanyId = 2, JobTypeId = 1, IsActive = true },
            new AppUser { Id = 4, Email = "u4@test.com", DisplayName = "Inactive", CompanyId = 1, JobTypeId = 1, IsActive = false }
        );
        await _db.SaveChangesAsync();

        var grp = await _service.CreateGroupingAsync(MoleculeId, "grp", "Group",
            companyIds: new List<int> { 1 },
            jobTypeIds: new List<int> { 1 });

        var users = await _service.GetUsersInGroupingAsync(grp!.Id);

        // Only user 1 matches: CompanyId=1 AND JobTypeId=1 AND IsActive
        users.Should().HaveCount(1);
        users[0].Id.Should().Be(1);
    }

    [Fact]
    public async Task GetUsersInGroupingAsync_NoCompanyFilter_ReturnsAllActiveWithJobType()
    {
        SeedCompany(1, "CompanyA");
        SeedCompany(2, "CompanyB");
        SeedJobType(1, "Alhut");

        _db.Users.AddRange(
            new AppUser { Id = 1, Email = "u1@test.com", DisplayName = "User 1", CompanyId = 1, JobTypeId = 1, IsActive = true },
            new AppUser { Id = 2, Email = "u2@test.com", DisplayName = "User 2", CompanyId = 2, JobTypeId = 1, IsActive = true }
        );
        await _db.SaveChangesAsync();

        // Grouping with no companies but with job type filter
        var grp = await _service.CreateGroupingAsync(MoleculeId, "grp", "Group",
            jobTypeIds: new List<int> { 1 });

        var users = await _service.GetUsersInGroupingAsync(grp!.Id);

        // Both users match JobTypeId=1 since no company filter
        users.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetUsersInGroupingAsync_NonExistentGrouping_ReturnsEmpty()
    {
        var users = await _service.GetUsersInGroupingAsync(999);

        users.Should().BeEmpty();
    }

    // --- GetEligibleUsersForGroupingAsync ---

    [Fact]
    public async Task GetEligibleUsersForGroupingAsync_DelegatesToGetUsersInGrouping()
    {
        SeedCompany(1, "CompanyA");
        SeedJobType(1, "Alhut");

        _db.Users.Add(new AppUser { Id = 1, Email = "u1@test.com", DisplayName = "User 1", CompanyId = 1, JobTypeId = 1, IsActive = true });
        await _db.SaveChangesAsync();

        var grp = await _service.CreateGroupingAsync(MoleculeId, "grp", "Group",
            companyIds: new List<int> { 1 }, jobTypeIds: new List<int> { 1 });

        var users = await _service.GetEligibleUsersForGroupingAsync(grp!.Id);

        users.Should().HaveCount(1);
    }
}
