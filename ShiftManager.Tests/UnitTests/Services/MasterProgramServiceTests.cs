using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Results;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services;

// Updated 2026-04-25: MasterProgramService migrated to OperationResult<T> — error paths
// no longer throw, they return Fail(errorKey, message). Tests assert Success / ErrorKey
// instead of catching exceptions, and unwrap result.Value when accessing the payload.
// IShiftProgramService mock returns OperationResult<List<ShiftInstance>>.Ok(...) since the
// dependency interface was migrated too.
public class MasterProgramServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly MasterProgramService _service;
    private readonly Mock<IShiftProgramService> _shiftProgramServiceMock;

    private const int TestCompanyId = 1;

    public MasterProgramServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new AppDbContext(options);
        _shiftProgramServiceMock = new Mock<IShiftProgramService>();

        var localizationMock = new Mock<ICompanyLocalizationService>();
        localizationMock.Setup(l => l.ResolveShiftTypeNameAsync(It.IsAny<ShiftType>(), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync((ShiftType st, int _, string _) => st.Name);

        var localizerMock = new Mock<IStringLocalizer<SharedResources>>();
        localizerMock.Setup(l => l[It.IsAny<string>()])
            .Returns((string k) => new LocalizedString(k, k));

        _service = new MasterProgramService(
            _db,
            Mock.Of<ILogger<MasterProgramService>>(),
            Mock.Of<ITenantResolver>(),
            _shiftProgramServiceMock.Object,
            localizationMock.Object,
            localizerMock.Object);

        // Seed company and shift type for programs
        _db.Companies.Add(new Company { Id = TestCompanyId, MoleculeId = 1, Name = "TestCo", DisplayName = "Test Co" });
        _db.ShiftTypes.Add(new ShiftType
        {
            Id = 1, Key = "Morning", NameEn = "Morning", MoleculeId = 1,
            Scope = ShiftScope.Molecule, Start = new TimeOnly(7, 0), End = new TimeOnly(15, 0)
        });
        _db.ShiftTypes.Add(new ShiftType
        {
            Id = 2, Key = "Evening", NameEn = "Evening", MoleculeId = 1,
            Scope = ShiftScope.Molecule, Start = new TimeOnly(15, 0), End = new TimeOnly(23, 0)
        });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private async Task<ShiftProgram> SeedProgramAsync(int id, string name, int shiftTypeId = 1)
    {
        var program = new ShiftProgram
        {
            Id = id,
            CompanyId = TestCompanyId,
            ShiftTypeId = shiftTypeId,
            Name = name,
            IsActive = true,
            CreatedBy = 1,
            UpdatedBy = 1
        };
        _db.ShiftPrograms.Add(program);
        await _db.SaveChangesAsync();
        return program;
    }

    // --- CreateMasterProgramAsync ---

    [Fact]
    public async Task CreateMasterProgramAsync_HappyPath_CreatesMasterProgramWithItems()
    {
        await SeedProgramAsync(1, "Morning Program");
        await SeedProgramAsync(2, "Evening Program", shiftTypeId: 2);

        var result = await _service.CreateMasterProgramAsync(
            TestCompanyId, "Standard Week", "Default schedule", new List<int> { 1, 2 }, userId: 1);

        result.Success.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Name.Should().Be("Standard Week");
        result.Value.Description.Should().Be("Default schedule");
        result.Value.CompanyId.Should().Be(TestCompanyId);
        result.Value.IsActive.Should().BeTrue();

        var items = await _db.MasterProgramItems
            .Where(i => i.MasterProgramId == result.Value.Id)
            .OrderBy(i => i.SortOrder)
            .ToListAsync();
        items.Should().HaveCount(2);
        items[0].ProgramId.Should().Be(1);
        items[0].SortOrder.Should().Be(0);
        items[1].ProgramId.Should().Be(2);
        items[1].SortOrder.Should().Be(1);
    }

    [Fact]
    public async Task CreateMasterProgramAsync_EmptyProgramIds_ReturnsFail()
    {
        var result = await _service.CreateMasterProgramAsync(
            TestCompanyId, "Empty", null, new List<int>(), userId: 1);

        result.Success.Should().BeFalse();
        result.ErrorKey.Should().Be("Error_MasterProgramService_NoPrograms");
    }

    [Fact]
    public async Task CreateMasterProgramAsync_NullProgramIds_ReturnsFail()
    {
        var result = await _service.CreateMasterProgramAsync(
            TestCompanyId, "Null", null, null!, userId: 1);

        result.Success.Should().BeFalse();
        result.ErrorKey.Should().Be("Error_MasterProgramService_NoPrograms");
    }

    [Fact]
    public async Task CreateMasterProgramAsync_ProgramNotInCompany_ReturnsFail()
    {
        await SeedProgramAsync(1, "Program 1");

        var result = await _service.CreateMasterProgramAsync(
            TestCompanyId, "Bad", null, new List<int> { 1, 999 }, userId: 1);

        result.Success.Should().BeFalse();
        result.ErrorKey.Should().Be("Error_MasterProgramService_ProgramsNotInCompany");
    }

    // --- GetMasterProgramAsync ---

    [Fact]
    public async Task GetMasterProgramAsync_ReturnsWithItems()
    {
        await SeedProgramAsync(1, "Morning Program");
        _db.MasterPrograms.Add(new MasterProgram
        {
            Id = 1, CompanyId = TestCompanyId, Name = "Master", IsActive = true,
            CreatedBy = 1, UpdatedBy = 1
        });
        _db.MasterProgramItems.Add(new MasterProgramItem
        {
            Id = 1, MasterProgramId = 1, ProgramId = 1, SortOrder = 0
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetMasterProgramAsync(1);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Master");
        result.Items.Should().HaveCount(1);
        result.Items[0].ProgramId.Should().Be(1);
    }

    [Fact]
    public async Task GetMasterProgramAsync_NotFound_ReturnsNull()
    {
        var result = await _service.GetMasterProgramAsync(999);

        result.Should().BeNull();
    }

    // --- GetCompanyMasterProgramsAsync ---

    [Fact]
    public async Task GetCompanyMasterProgramsAsync_ExcludesInactiveByDefault()
    {
        _db.MasterPrograms.AddRange(
            new MasterProgram { Id = 1, CompanyId = TestCompanyId, Name = "Active", IsActive = true, CreatedBy = 1, UpdatedBy = 1 },
            new MasterProgram { Id = 2, CompanyId = TestCompanyId, Name = "Inactive", IsActive = false, CreatedBy = 1, UpdatedBy = 1 }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetCompanyMasterProgramsAsync(TestCompanyId);

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Active");
    }

    [Fact]
    public async Task GetCompanyMasterProgramsAsync_IncludesInactive_WhenFlagIsTrue()
    {
        _db.MasterPrograms.AddRange(
            new MasterProgram { Id = 1, CompanyId = TestCompanyId, Name = "Active", IsActive = true, CreatedBy = 1, UpdatedBy = 1 },
            new MasterProgram { Id = 2, CompanyId = TestCompanyId, Name = "Inactive", IsActive = false, CreatedBy = 1, UpdatedBy = 1 }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetCompanyMasterProgramsAsync(TestCompanyId, includeInactive: true);

        result.Should().HaveCount(2);
    }

    // --- UpdateMasterProgramAsync ---

    [Fact]
    public async Task UpdateMasterProgramAsync_HappyPath_UpdatesFieldsAndItems()
    {
        await SeedProgramAsync(1, "Morning");
        await SeedProgramAsync(2, "Evening", shiftTypeId: 2);

        _db.MasterPrograms.Add(new MasterProgram
        {
            Id = 1, CompanyId = TestCompanyId, Name = "Old Name", IsActive = true,
            CreatedBy = 1, UpdatedBy = 1
        });
        _db.MasterProgramItems.Add(new MasterProgramItem
        {
            Id = 1, MasterProgramId = 1, ProgramId = 1, SortOrder = 0
        });
        await _db.SaveChangesAsync();

        var result = await _service.UpdateMasterProgramAsync(1, "New Name", "Description", new List<int> { 2 }, userId: 2);
        result.Success.Should().BeTrue();

        var mp = await _db.MasterPrograms.FindAsync(1);
        mp!.Name.Should().Be("New Name");
        mp.Description.Should().Be("Description");
        mp.UpdatedBy.Should().Be(2);

        var items = await _db.MasterProgramItems.Where(i => i.MasterProgramId == 1).ToListAsync();
        items.Should().HaveCount(1);
        items[0].ProgramId.Should().Be(2);
    }

    [Fact]
    public async Task UpdateMasterProgramAsync_NotFound_ReturnsFail()
    {
        var result = await _service.UpdateMasterProgramAsync(999, "X", null, new List<int> { 1 }, 1);

        result.Success.Should().BeFalse();
        result.ErrorKey.Should().Be("Error_MasterProgramService_NotFound");
    }

    [Fact]
    public async Task UpdateMasterProgramAsync_EmptyProgramIds_ReturnsFail()
    {
        _db.MasterPrograms.Add(new MasterProgram
        {
            Id = 1, CompanyId = TestCompanyId, Name = "Master", IsActive = true,
            CreatedBy = 1, UpdatedBy = 1
        });
        await _db.SaveChangesAsync();

        var result = await _service.UpdateMasterProgramAsync(1, "X", null, new List<int>(), 1);

        result.Success.Should().BeFalse();
        result.ErrorKey.Should().Be("Error_MasterProgramService_NoPrograms");
    }

    // --- DeleteMasterProgramAsync ---

    [Fact]
    public async Task DeleteMasterProgramAsync_SoftDeletes()
    {
        _db.MasterPrograms.Add(new MasterProgram
        {
            Id = 1, CompanyId = TestCompanyId, Name = "To Delete", IsActive = true,
            CreatedBy = 1, UpdatedBy = 1
        });
        await _db.SaveChangesAsync();

        var result = await _service.DeleteMasterProgramAsync(1, userId: 2);
        result.Success.Should().BeTrue();

        var mp = await _db.MasterPrograms.FindAsync(1);
        mp.Should().NotBeNull();
        mp!.IsActive.Should().BeFalse();
        mp.UpdatedBy.Should().Be(2);
    }

    [Fact]
    public async Task DeleteMasterProgramAsync_NotFound_ReturnsFail()
    {
        var result = await _service.DeleteMasterProgramAsync(999, 1);

        result.Success.Should().BeFalse();
        result.ErrorKey.Should().Be("Error_MasterProgramService_NotFound");
    }

    // --- GenerateFromMasterProgramAsync ---

    [Fact]
    public async Task GenerateFromMasterProgramAsync_NotFound_ReturnsFail()
    {
        var result = await _service.GenerateFromMasterProgramAsync(
            999, DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today.AddDays(7)));

        result.Success.Should().BeFalse();
        result.ErrorKey.Should().Be("Error_MasterProgramService_NotFound");
    }

    [Fact]
    public async Task GenerateFromMasterProgramAsync_NoItems_ReturnsFail()
    {
        // Service treats an empty MasterProgram as a configuration error rather than silently
        // returning an empty dict — surfaces the misconfiguration to the caller.
        _db.MasterPrograms.Add(new MasterProgram
        {
            Id = 1, CompanyId = TestCompanyId, Name = "Empty Master", IsActive = true,
            CreatedBy = 1, UpdatedBy = 1
        });
        await _db.SaveChangesAsync();

        var today = DateOnly.FromDateTime(DateTime.Today);
        var result = await _service.GenerateFromMasterProgramAsync(1, today, today.AddDays(7));

        result.Success.Should().BeFalse();
        result.ErrorKey.Should().Be("Error_MasterProgramService_NoProgramsInMaster");
    }

    [Fact]
    public async Task GenerateFromMasterProgramAsync_CallsShiftProgramServiceForEachItem()
    {
        await SeedProgramAsync(1, "Morning");
        await SeedProgramAsync(2, "Evening", shiftTypeId: 2);

        _db.MasterPrograms.Add(new MasterProgram
        {
            Id = 1, CompanyId = TestCompanyId, Name = "Full Master", IsActive = true,
            CreatedBy = 1, UpdatedBy = 1
        });
        _db.MasterProgramItems.AddRange(
            new MasterProgramItem { Id = 1, MasterProgramId = 1, ProgramId = 1, SortOrder = 0 },
            new MasterProgramItem { Id = 2, MasterProgramId = 1, ProgramId = 2, SortOrder = 1 }
        );
        await _db.SaveChangesAsync();

        _shiftProgramServiceMock
            .Setup(s => s.GenerateInstancesAsync(It.IsAny<int>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), false))
            .ReturnsAsync(OperationResult<List<ShiftInstance>>.Ok(new List<ShiftInstance>()));

        var today = DateOnly.FromDateTime(DateTime.Today);
        var result = await _service.GenerateFromMasterProgramAsync(1, today, today.AddDays(7));

        result.Success.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        _shiftProgramServiceMock.Verify(
            s => s.GenerateInstancesAsync(1, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), false), Times.Once);
        _shiftProgramServiceMock.Verify(
            s => s.GenerateInstancesAsync(2, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), false), Times.Once);
    }

    [Fact]
    public async Task GenerateFromMasterProgramAsync_PastStartDate_AdjustsToToday()
    {
        await SeedProgramAsync(1, "Morning");

        _db.MasterPrograms.Add(new MasterProgram
        {
            Id = 1, CompanyId = TestCompanyId, Name = "Master", IsActive = true,
            CreatedBy = 1, UpdatedBy = 1
        });
        _db.MasterProgramItems.Add(new MasterProgramItem
        {
            Id = 1, MasterProgramId = 1, ProgramId = 1, SortOrder = 0
        });
        await _db.SaveChangesAsync();

        DateOnly capturedStart = default;
        _shiftProgramServiceMock
            .Setup(s => s.GenerateInstancesAsync(1, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), false))
            .Callback((int _, DateOnly start, DateOnly _, bool _) => capturedStart = start)
            .ReturnsAsync(OperationResult<List<ShiftInstance>>.Ok(new List<ShiftInstance>()));

        var pastDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
        var futureDate = DateOnly.FromDateTime(DateTime.Today.AddDays(7));
        var result = await _service.GenerateFromMasterProgramAsync(1, pastDate, futureDate);
        result.Success.Should().BeTrue();

        capturedStart.Should().Be(DateOnly.FromDateTime(DateTime.Today));
    }

    [Fact]
    public async Task GenerateFromMasterProgramAsync_PastStartAndEnd_ReturnsEmptyWithoutCalling()
    {
        await SeedProgramAsync(1, "Morning");
        _db.MasterPrograms.Add(new MasterProgram
        {
            Id = 1, CompanyId = TestCompanyId, Name = "Master", IsActive = true,
            CreatedBy = 1, UpdatedBy = 1
        });
        _db.MasterProgramItems.Add(new MasterProgramItem { Id = 1, MasterProgramId = 1, ProgramId = 1, SortOrder = 0 });
        await _db.SaveChangesAsync();

        var pastDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
        var pastEnd = DateOnly.FromDateTime(DateTime.Today.AddDays(-1));
        var result = await _service.GenerateFromMasterProgramAsync(1, pastDate, pastEnd);

        result.Success.Should().BeTrue();
        result.Value.Should().BeEmpty();
        _shiftProgramServiceMock.Verify(
            s => s.GenerateInstancesAsync(It.IsAny<int>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<bool>()),
            Times.Never);
    }

    // --- GetMasterProgramSummaryAsync ---

    [Fact]
    public async Task GetMasterProgramSummaryAsync_ReturnsCorrectSummary()
    {
        await SeedProgramAsync(1, "Morning");
        await SeedProgramAsync(2, "Evening", shiftTypeId: 2);

        // Add ProgramDays
        _db.ProgramDays.AddRange(
            new ProgramDay { Id = 1, ProgramId = 1, DayOfWeek = DayOfWeek.Sunday },
            new ProgramDay { Id = 2, ProgramId = 1, DayOfWeek = DayOfWeek.Monday },
            new ProgramDay { Id = 3, ProgramId = 2, DayOfWeek = DayOfWeek.Tuesday }
        );

        _db.MasterPrograms.Add(new MasterProgram
        {
            Id = 1, CompanyId = TestCompanyId, Name = "Full Master", IsActive = true,
            CreatedBy = 1, UpdatedBy = 1
        });
        _db.MasterProgramItems.AddRange(
            new MasterProgramItem { Id = 1, MasterProgramId = 1, ProgramId = 1, SortOrder = 0 },
            new MasterProgramItem { Id = 2, MasterProgramId = 1, ProgramId = 2, SortOrder = 1 }
        );
        await _db.SaveChangesAsync();

        var result = await _service.GetMasterProgramSummaryAsync(1);

        result.Success.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.TotalPrograms.Should().Be(2);
        result.Value.UniqueShiftTypes.Should().Be(2);
        result.Value.TotalProgramDays.Should().Be(3);
        result.Value.ShiftTypeNames.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetMasterProgramSummaryAsync_NotFound_ReturnsFail()
    {
        var result = await _service.GetMasterProgramSummaryAsync(999);

        result.Success.Should().BeFalse();
        result.ErrorKey.Should().Be("Error_MasterProgramService_NotFound");
    }
}
