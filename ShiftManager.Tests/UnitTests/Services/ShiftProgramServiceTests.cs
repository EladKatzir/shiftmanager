using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services;

public class ShiftProgramServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ShiftProgramService _service;

    private const int CompanyId = 1;
    private const int MoleculeId = 1;
    private const int AreaId = 1;
    private const int ShiftTypeId = 1;
    private const int UserId = 100;

    public ShiftProgramServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new AppDbContext(options);

        _service = new ShiftProgramService(
            _db,
            Mock.Of<ILogger<ShiftProgramService>>(),
            Mock.Of<ITenantResolver>());

        SeedBaseData();
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private void SeedBaseData()
    {
        _db.Set<Area>().Add(new Area { Id = AreaId, Name = "TestArea", DisplayName = "Test Area", ProjectId = 1 });
        _db.Set<Molecule>().Add(new Molecule { Id = MoleculeId, AreaId = AreaId, Name = "TestMol", DisplayName = "Test Molecule" });
        _db.Companies.Add(new Company { Id = CompanyId, MoleculeId = MoleculeId, Name = "TestCo", DisplayName = "Test Company" });
        _db.ShiftTypes.Add(new ShiftType
        {
            Id = ShiftTypeId, Key = ShiftType.KEY_MORNING, MoleculeId = MoleculeId,
            Start = new TimeOnly(7, 0), End = new TimeOnly(15, 0)
        });
        _db.SaveChanges();
    }

    // --- CreateProgramAsync ---

    [Fact]
    public async Task CreateProgramAsync_HappyPath_CreatesProgramWithDays()
    {
        var days = new List<DayOfWeek> { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday };

        var result = await _service.CreateProgramAsync(
            CompanyId, ShiftTypeId, "Morning Mon-Wed", days, defaultStaffing: 3, perDayStaffing: null, UserId);

        result.Should().NotBeNull();
        result.Name.Should().Be("Morning Mon-Wed");
        result.CompanyId.Should().Be(CompanyId);
        result.ShiftTypeId.Should().Be(ShiftTypeId);
        result.DefaultStaffingRequired.Should().Be(3);
        result.IsActive.Should().BeTrue();
        result.CreatedBy.Should().Be(UserId);
        result.ProgramDays.Should().HaveCount(3);
    }

    [Fact]
    public async Task CreateProgramAsync_WithPerDayStaffing_SetsOverrides()
    {
        var days = new List<DayOfWeek> { DayOfWeek.Sunday, DayOfWeek.Monday };
        var perDayStaffing = new Dictionary<DayOfWeek, int>
        {
            { DayOfWeek.Sunday, 5 }
        };

        var result = await _service.CreateProgramAsync(
            CompanyId, ShiftTypeId, "Test", days, defaultStaffing: 2, perDayStaffing, UserId);

        var sundayDay = result.ProgramDays.First(pd => pd.DayOfWeek == DayOfWeek.Sunday);
        var mondayDay = result.ProgramDays.First(pd => pd.DayOfWeek == DayOfWeek.Monday);

        sundayDay.StaffingRequired.Should().Be(5);
        mondayDay.StaffingRequired.Should().BeNull(); // Uses default
    }

    [Fact]
    public async Task CreateProgramAsync_InvalidShiftType_Throws()
    {
        var days = new List<DayOfWeek> { DayOfWeek.Sunday };

        var act = () => _service.CreateProgramAsync(CompanyId, 999, "Bad", days, 1, null, UserId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task CreateProgramAsync_EmptyDays_Throws()
    {
        var act = () => _service.CreateProgramAsync(CompanyId, ShiftTypeId, "Bad", new List<DayOfWeek>(), 1, null, UserId);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*At least one day*");
    }

    // --- GetProgramAsync ---

    [Fact]
    public async Task GetProgramAsync_ExistingProgram_ReturnsWithIncludes()
    {
        var program = await CreateTestProgram();

        var result = await _service.GetProgramAsync(program.Id);

        result.Should().NotBeNull();
        result!.ShiftType.Should().NotBeNull();
        result.ProgramDays.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetProgramAsync_NonExistent_ReturnsNull()
    {
        var result = await _service.GetProgramAsync(999);

        result.Should().BeNull();
    }

    // --- GetCompanyProgramsAsync ---

    [Fact]
    public async Task GetCompanyProgramsAsync_ReturnsActivePrograms()
    {
        await CreateTestProgram("Active Program");
        var inactive = await CreateTestProgram("Inactive Program");
        await _service.DeleteProgramAsync(inactive.Id, UserId);

        var result = await _service.GetCompanyProgramsAsync(CompanyId);

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Active Program");
    }

    [Fact]
    public async Task GetCompanyProgramsAsync_IncludeInactive_ReturnsAll()
    {
        await CreateTestProgram("Active Program");
        var inactive = await CreateTestProgram("Inactive Program");
        await _service.DeleteProgramAsync(inactive.Id, UserId);

        var result = await _service.GetCompanyProgramsAsync(CompanyId, includeInactive: true);

        result.Should().HaveCount(2);
    }

    // --- UpdateProgramAsync ---

    [Fact]
    public async Task UpdateProgramAsync_HappyPath_UpdatesFieldsAndDays()
    {
        var program = await CreateTestProgram();

        var newDays = new List<DayOfWeek> { DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
        await _service.UpdateProgramAsync(program.Id, "Updated Name", newDays, 5, null, UserId);

        var updated = await _service.GetProgramAsync(program.Id);
        updated!.Name.Should().Be("Updated Name");
        updated.DefaultStaffingRequired.Should().Be(5);
        updated.ProgramDays.Should().HaveCount(3);
        updated.ProgramDays.Select(pd => pd.DayOfWeek).Should()
            .BeEquivalentTo(new[] { DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday });
        updated.UpdatedBy.Should().Be(UserId);
    }

    [Fact]
    public async Task UpdateProgramAsync_NonExistent_Throws()
    {
        var act = () => _service.UpdateProgramAsync(999, "Bad", new List<DayOfWeek> { DayOfWeek.Sunday }, 1, null, UserId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task UpdateProgramAsync_EmptyDays_Throws()
    {
        var program = await CreateTestProgram();

        var act = () => _service.UpdateProgramAsync(program.Id, "Bad", new List<DayOfWeek>(), 1, null, UserId);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*At least one day*");
    }

    // --- DeleteProgramAsync ---

    [Fact]
    public async Task DeleteProgramAsync_HappyPath_SetsInactive()
    {
        var program = await CreateTestProgram();

        await _service.DeleteProgramAsync(program.Id, UserId);

        var deleted = await _service.GetProgramAsync(program.Id);
        deleted!.IsActive.Should().BeFalse();
        deleted.UpdatedBy.Should().Be(UserId);
    }

    [Fact]
    public async Task DeleteProgramAsync_NonExistent_Throws()
    {
        var act = () => _service.DeleteProgramAsync(999, UserId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    // --- GenerateInstancesAsync ---

    [Fact]
    public async Task GenerateInstancesAsync_CreatesInstancesForMatchingDays()
    {
        var program = await CreateTestProgram(); // Sun, Mon, Tue

        // 2026-03-01 is Sunday, 2026-03-07 is Saturday — 7 days covering Sun/Mon/Tue
        var startDate = new DateOnly(2026, 3, 1);
        var endDate = new DateOnly(2026, 3, 7);

        var result = await _service.GenerateInstancesAsync(program.Id, startDate, endDate);

        // Sunday 3/1, Monday 3/2, Tuesday 3/3 match
        result.Should().HaveCount(3);
        result.Should().OnlyContain(i => i.CompanyId == CompanyId && i.ShiftTypeId == ShiftTypeId);
    }

    [Fact]
    public async Task GenerateInstancesAsync_CreatesAssignmentSlots()
    {
        var program = await CreateTestProgram(defaultStaffing: 2); // Sun, Mon, Tue with 2 slots

        var startDate = new DateOnly(2026, 3, 1); // Sunday
        var endDate = new DateOnly(2026, 3, 1);   // Just Sunday

        var result = await _service.GenerateInstancesAsync(program.Id, startDate, endDate);

        result.Should().HaveCount(1);
        var assignments = await _db.ShiftAssignments.Where(a => a.ShiftInstanceId == result[0].Id).ToListAsync();
        assignments.Should().HaveCount(2);
        assignments.Should().OnlyContain(a => a.UserId == null); // Empty slots
    }

    [Fact]
    public async Task GenerateInstancesAsync_SkipsExistingInstances_WhenOverwriteFalse()
    {
        var program = await CreateTestProgram(defaultStaffing: 1);

        var date = new DateOnly(2026, 3, 1); // Sunday

        // Generate first time
        await _service.GenerateInstancesAsync(program.Id, date, date);

        // Generate again — should skip
        var result = await _service.GenerateInstancesAsync(program.Id, date, date, overwriteExisting: false);

        result.Should().BeEmpty();
        var instanceCount = await _db.ShiftInstances.CountAsync(i => i.WorkDate == date);
        instanceCount.Should().Be(1); // Only one instance exists
    }

    [Fact]
    public async Task GenerateInstancesAsync_OverwritesExistingInstances_WhenOverwriteTrue()
    {
        var program = await CreateTestProgram(defaultStaffing: 1);

        var date = new DateOnly(2026, 3, 1); // Sunday

        // Generate first time
        await _service.GenerateInstancesAsync(program.Id, date, date);

        // Update program staffing, then regenerate with overwrite
        await _service.UpdateProgramAsync(program.Id, program.Name,
            new List<DayOfWeek> { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday },
            3, null, UserId);

        var result = await _service.GenerateInstancesAsync(program.Id, date, date, overwriteExisting: true);

        result.Should().HaveCount(1);
        result[0].StaffingRequired.Should().Be(3);
    }

    [Fact]
    public async Task GenerateInstancesAsync_NoProgramDays_ReturnsEmpty()
    {
        // Create a program then remove all days
        var program = await CreateTestProgram();
        var programDays = await _db.ProgramDays.Where(pd => pd.ProgramId == program.Id).ToListAsync();
        _db.ProgramDays.RemoveRange(programDays);
        await _db.SaveChangesAsync();

        var result = await _service.GenerateInstancesAsync(program.Id, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 7));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateInstancesAsync_NonExistentProgram_Throws()
    {
        var act = () => _service.GenerateInstancesAsync(999, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 7));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    // --- ApplyProgramToDateRangeAsync ---

    [Fact]
    public async Task ApplyProgramToDateRangeAsync_ReturnsInstanceCount()
    {
        var program = await CreateTestProgram();

        var count = await _service.ApplyProgramToDateRangeAsync(program.Id,
            new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 7));

        // Sun, Mon, Tue = 3 instances
        count.Should().Be(3);
    }

    // --- DetachInstanceAsync ---

    [Fact]
    public async Task DetachInstanceAsync_SetsDetachedAndOverride()
    {
        var program = await CreateTestProgram();
        var instances = await _service.GenerateInstancesAsync(program.Id, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 1));
        var instanceId = instances[0].Id;

        await _service.DetachInstanceAsync(instanceId, "staffing");

        var instance = await _db.ShiftInstances.FindAsync(instanceId);
        instance!.IsDetached.Should().BeTrue();
        instance.OverriddenFields.Should().Contain("Staffing");
    }

    [Fact]
    public async Task DetachInstanceAsync_MultipleOverrides_AccumulatesFlags()
    {
        var program = await CreateTestProgram();
        var instances = await _service.GenerateInstancesAsync(program.Id, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 1));
        var instanceId = instances[0].Id;

        await _service.DetachInstanceAsync(instanceId, "staffing");
        await _service.DetachInstanceAsync(instanceId, "name");

        var instance = await _db.ShiftInstances.FindAsync(instanceId);
        instance!.OverriddenFields.Should().Contain("Staffing");
        instance.OverriddenFields.Should().Contain("Name");
    }

    [Fact]
    public async Task DetachInstanceAsync_InvalidOverrideType_Throws()
    {
        var program = await CreateTestProgram();
        var instances = await _service.GenerateInstancesAsync(program.Id, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 1));

        var act = () => _service.DetachInstanceAsync(instances[0].Id, "invalid");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Invalid override type*");
    }

    [Fact]
    public async Task DetachInstanceAsync_NonExistentInstance_Throws()
    {
        var act = () => _service.DetachInstanceAsync(999, "staffing");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    // --- ResetInstanceToProgramAsync ---

    [Fact]
    public async Task ResetInstanceToProgramAsync_RestoresDefaults()
    {
        var program = await CreateTestProgram(defaultStaffing: 2);
        var instances = await _service.GenerateInstancesAsync(program.Id, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 1));
        var instanceId = instances[0].Id;

        // Detach and modify
        await _service.DetachInstanceAsync(instanceId, "staffing");
        var instance = await _db.ShiftInstances.FindAsync(instanceId);
        instance!.StaffingRequired = 10;
        instance.Name = "Custom Name";
        await _db.SaveChangesAsync();

        // Reset
        await _service.ResetInstanceToProgramAsync(instanceId);

        var reset = await _db.ShiftInstances.FindAsync(instanceId);
        reset!.StaffingRequired.Should().Be(2);
        reset.Name.Should().BeEmpty();
        reset.IsDetached.Should().BeFalse();
        reset.OverriddenFields.Should().BeNull();
    }

    [Fact]
    public async Task ResetInstanceToProgramAsync_NonExistentInstance_Throws()
    {
        var act = () => _service.ResetInstanceToProgramAsync(999);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task ResetInstanceToProgramAsync_NoProgramLink_Throws()
    {
        // Create an instance manually without a program
        var instance = new ShiftInstance
        {
            CompanyId = CompanyId, ShiftTypeId = ShiftTypeId,
            WorkDate = new DateOnly(2026, 3, 1), Name = "Manual", StaffingRequired = 1
        };
        _db.ShiftInstances.Add(instance);
        await _db.SaveChangesAsync();

        var act = () => _service.ResetInstanceToProgramAsync(instance.Id);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not generated from a Program*");
    }

    // --- GetInstancesFromProgramAsync ---

    [Fact]
    public async Task GetInstancesFromProgramAsync_ReturnsAllInstances()
    {
        var program = await CreateTestProgram();
        await _service.GenerateInstancesAsync(program.Id, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 7));

        var result = await _service.GetInstancesFromProgramAsync(program.Id);

        result.Should().HaveCount(3); // Sun, Mon, Tue
        result.Should().BeInAscendingOrder(i => i.WorkDate);
    }

    [Fact]
    public async Task GetInstancesFromProgramAsync_WithDateFilter_FiltersCorrectly()
    {
        var program = await CreateTestProgram();
        await _service.GenerateInstancesAsync(program.Id, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 7));

        var result = await _service.GetInstancesFromProgramAsync(program.Id,
            startDate: new DateOnly(2026, 3, 2), endDate: new DateOnly(2026, 3, 3));

        // Monday 3/2 and Tuesday 3/3
        result.Should().HaveCount(2);
    }

    // --- Helpers ---

    private async Task<ShiftProgram> CreateTestProgram(string name = "Test Program", int defaultStaffing = 1)
    {
        var days = new List<DayOfWeek> { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday };
        return await _service.CreateProgramAsync(CompanyId, ShiftTypeId, name, days, defaultStaffing, null, UserId);
    }
}
