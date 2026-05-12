using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Export;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using System.Text;

namespace ShiftManager.Tests.UnitTests.Services;

public class ScheduleExportServiceTests : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly AppDbContext _db;
    private readonly ScheduleExportService _service;
    private readonly Mock<ITenantResolver> _tenantResolverMock;
    private readonly Mock<ICompanyCacheService> _companyCacheMock;
    private readonly Mock<IJobTypeService> _jobTypeServiceMock;

    private const int TestCompanyId = 1;

    public ScheduleExportServiceTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _sqliteConnection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _tenantResolverMock = new Mock<ITenantResolver>();
        _companyCacheMock = new Mock<ICompanyCacheService>();
        _jobTypeServiceMock = new Mock<IJobTypeService>();

        _tenantResolverMock.Setup(t => t.GetCurrentTenantId()).Returns(TestCompanyId);
        _companyCacheMock.Setup(c => c.GetCompanyAsync(TestCompanyId))
            .ReturnsAsync(new Company { Id = TestCompanyId, MoleculeId = 1, Name = "TestCo", DisplayName = "Test Company" });

        var localizationMock = new Mock<ICompanyLocalizationService>();
        localizationMock.Setup(l => l.ResolveShiftTypeNameAsync(It.IsAny<ShiftType>(), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync((ShiftType st, int _, string _) => st.Name);

        _service = new ScheduleExportService(
            _db,
            _tenantResolverMock.Object,
            Mock.Of<ILogger<ScheduleExportService>>(),
            _companyCacheMock.Object,
            _jobTypeServiceMock.Object,
            localizationMock.Object);

        // Seed a company
        _db.Companies.Add(new Company { Id = TestCompanyId, MoleculeId = 1, Name = "TestCo", DisplayName = "Test Company" });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
    }

    // --- CollectExportDataAsync ---

    [Fact]
    public async Task CollectExportDataAsync_EmptyRange_ReturnsDaysWithNoShifts()
    {
        var request = new ScheduleExportRequest
        {
            StartDate = new DateTime(2026, 3, 25),
            EndDate = new DateTime(2026, 3, 27),
            IncludeChores = false,
            IncludeOnDuty = false
        };

        var result = await _service.CollectExportDataAsync(request);

        result.Should().NotBeNull();
        result.CompanyName.Should().Be("Test Company");
        result.Days.Should().HaveCount(3); // 25, 26, 27
        result.Days.Should().OnlyContain(d => d.Shifts.Count == 0);
    }

    [Fact]
    public async Task CollectExportDataAsync_WithShiftsAndAssignments_CollectsData()
    {
        var shiftType = new ShiftType
        {
            Id = 1, Key = "Morning", NameEn = "Morning Shift", MoleculeId = 1,
            Scope = ShiftScope.Molecule, Start = new TimeOnly(7, 0), End = new TimeOnly(15, 0)
        };
        _db.ShiftTypes.Add(shiftType);

        var user = new AppUser
        {
            Id = 1, Email = "emp@test.com", DisplayName = "Employee One",
            CompanyId = TestCompanyId, IsActive = true, Role = UserRole.Employee
        };
        _db.Users.Add(user);

        var instance = new ShiftInstance
        {
            Id = 1, ShiftTypeId = 1, CompanyId = TestCompanyId,
            WorkDate = new DateOnly(2026, 3, 25), StaffingRequired = 2
        };
        _db.ShiftInstances.Add(instance);

        _db.ShiftAssignments.Add(new ShiftAssignment
        {
            Id = 1, ShiftInstanceId = 1, UserId = 1, CompanyId = TestCompanyId
        });
        await _db.SaveChangesAsync();

        var request = new ScheduleExportRequest
        {
            StartDate = new DateTime(2026, 3, 25),
            EndDate = new DateTime(2026, 3, 25),
            IncludeEmployeeNames = true,
            IncludeChores = false,
            IncludeOnDuty = false
        };

        var result = await _service.CollectExportDataAsync(request);

        result.Days.Should().HaveCount(1);
        result.Days[0].Shifts.Should().HaveCount(1);
        result.Days[0].Shifts[0].ShiftName.Should().Be("Morning Shift");
        result.Days[0].Shifts[0].RequiredStaff.Should().Be(2);
        result.Days[0].Shifts[0].AssignedStaff.Should().Be(1);
        result.Days[0].Shifts[0].AssignedEmployees.Should().Contain("Employee One");
    }

    [Fact]
    public async Task CollectExportDataAsync_WithChores_CollectsChoreData()
    {
        _db.Users.Add(new AppUser
        {
            Id = 1, Email = "emp@test.com", DisplayName = "Employee One",
            CompanyId = TestCompanyId, IsActive = true, Role = UserRole.Employee
        });
        _db.Chores.Add(new Chore
        {
            Id = 1, CompanyId = TestCompanyId, UserId = 1,
            Date = new DateOnly(2026, 3, 25), Title = "Guard Duty",
            CreatedBy = 1
        });
        await _db.SaveChangesAsync();

        var request = new ScheduleExportRequest
        {
            StartDate = new DateTime(2026, 3, 25),
            EndDate = new DateTime(2026, 3, 25),
            IncludeChores = true,
            IncludeOnDuty = false,
            IncludeEmployeeNames = true
        };

        var result = await _service.CollectExportDataAsync(request);

        result.Days[0].Chores.Should().HaveCount(1);
        result.Days[0].Chores[0].ChoreName.Should().Be("Guard Duty");
        result.Days[0].Chores[0].AssignedTo.Should().Be("Employee One");
    }

    [Fact]
    public async Task CollectExportDataAsync_ExceedMaxDays_ClampedToMax()
    {
        var request = new ScheduleExportRequest
        {
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 12, 31), // way over 90 days
            IncludeChores = false,
            IncludeOnDuty = false
        };

        var result = await _service.CollectExportDataAsync(request);

        // Should be clamped to 90 days max
        result.Days.Count.Should().BeLessThanOrEqualTo(91); // 90 days inclusive = 91 entries
    }

    [Fact]
    public async Task CollectExportDataAsync_EmployeeNamesExcluded_WhenFlagIsFalse()
    {
        var shiftType = new ShiftType
        {
            Id = 1, Key = "Morning", NameEn = "Morning", MoleculeId = 1,
            Scope = ShiftScope.Molecule, Start = new TimeOnly(7, 0), End = new TimeOnly(15, 0)
        };
        _db.ShiftTypes.Add(shiftType);
        _db.Users.Add(new AppUser
        {
            Id = 1, Email = "emp@test.com", DisplayName = "Employee",
            CompanyId = TestCompanyId, IsActive = true, Role = UserRole.Employee
        });
        _db.ShiftInstances.Add(new ShiftInstance
        {
            Id = 1, ShiftTypeId = 1, CompanyId = TestCompanyId,
            WorkDate = new DateOnly(2026, 3, 25), StaffingRequired = 1
        });
        _db.ShiftAssignments.Add(new ShiftAssignment
        {
            Id = 1, ShiftInstanceId = 1, UserId = 1, CompanyId = TestCompanyId
        });
        await _db.SaveChangesAsync();

        var request = new ScheduleExportRequest
        {
            StartDate = new DateTime(2026, 3, 25),
            EndDate = new DateTime(2026, 3, 25),
            IncludeEmployeeNames = false,
            IncludeChores = false,
            IncludeOnDuty = false
        };

        var result = await _service.CollectExportDataAsync(request);

        result.Days[0].Shifts[0].AssignedEmployees.Should().BeEmpty();
        result.Days[0].Shifts[0].AssignedStaff.Should().Be(1); // count is still there
    }

    // --- GenerateCsvAsync ---

    [Fact]
    public async Task GenerateCsvAsync_ProducesValidCsvWithBom()
    {
        var data = new ScheduleExportData
        {
            CompanyName = "Test Company",
            GeneratedAt = new DateTime(2026, 3, 25, 12, 0, 0),
            StartDate = new DateTime(2026, 3, 25),
            EndDate = new DateTime(2026, 3, 25),
            Days = new List<ExportDayData>
            {
                new()
                {
                    Date = new DateTime(2026, 3, 25),
                    DayName = "Wednesday",
                    Shifts = new List<ExportShiftData>
                    {
                        new()
                        {
                            ShiftName = "Morning",
                            TimeRange = "07:00-15:00",
                            RequiredStaff = 2,
                            AssignedStaff = 1,
                            AssignedEmployees = new List<string> { "John Doe" }
                        }
                    },
                    Chores = new List<ExportChoreData>(),
                    OnDuties = new List<ExportOnDutyData>()
                }
            }
        };

        var bytes = await _service.GenerateCsvAsync(data);

        bytes.Should().NotBeEmpty();

        // Verify BOM
        bytes[0].Should().Be(0xEF);
        bytes[1].Should().Be(0xBB);
        bytes[2].Should().Be(0xBF);

        var csv = Encoding.UTF8.GetString(bytes);
        csv.Should().Contain("Date,Day,Shift,Time,Required,Assigned,Employees,Type");
        csv.Should().Contain("2026-03-25,Wednesday,Morning,07:00-15:00,2,1,John Doe,Shift");
        csv.Should().Contain("# Company: Test Company");
    }

    [Fact]
    public async Task GenerateCsvAsync_EscapesCommasAndQuotes()
    {
        var data = new ScheduleExportData
        {
            CompanyName = "Test",
            GeneratedAt = DateTime.UtcNow,
            StartDate = new DateTime(2026, 3, 25),
            EndDate = new DateTime(2026, 3, 25),
            Days = new List<ExportDayData>
            {
                new()
                {
                    Date = new DateTime(2026, 3, 25),
                    DayName = "Wednesday",
                    Shifts = new List<ExportShiftData>
                    {
                        new()
                        {
                            ShiftName = "Morning, Special",
                            TimeRange = "07:00-15:00",
                            RequiredStaff = 1,
                            AssignedStaff = 1,
                            AssignedEmployees = new List<string> { "Jane \"JD\" Doe" }
                        }
                    },
                    Chores = new List<ExportChoreData>(),
                    OnDuties = new List<ExportOnDutyData>()
                }
            }
        };

        var bytes = await _service.GenerateCsvAsync(data);
        var csv = Encoding.UTF8.GetString(bytes);

        // Commas in field name should be quoted
        csv.Should().Contain("\"Morning, Special\"");
        // Quotes should be doubled
        csv.Should().Contain("\"Jane \"\"JD\"\" Doe\"");
    }

    [Fact]
    public async Task GenerateCsvAsync_EmptyData_ProducesHeaderOnly()
    {
        var data = new ScheduleExportData
        {
            CompanyName = "Test",
            GeneratedAt = DateTime.UtcNow,
            StartDate = new DateTime(2026, 3, 25),
            EndDate = new DateTime(2026, 3, 25),
            Days = new List<ExportDayData>()
        };

        var bytes = await _service.GenerateCsvAsync(data);
        var csv = Encoding.UTF8.GetString(bytes);

        csv.Should().Contain("Date,Day,Shift,Time,Required,Assigned,Employees,Type");
        csv.Should().Contain("# Company: Test");
    }

    // --- GenerateExcelAsync ---

    [Fact]
    public async Task GenerateExcelAsync_ProducesNonEmptyBytes()
    {
        var data = new ScheduleExportData
        {
            CompanyName = "Test Company",
            GeneratedAt = DateTime.UtcNow,
            StartDate = new DateTime(2026, 3, 25),
            EndDate = new DateTime(2026, 3, 25),
            Days = new List<ExportDayData>
            {
                new()
                {
                    Date = new DateTime(2026, 3, 25),
                    DayName = "Wednesday",
                    Shifts = new List<ExportShiftData>
                    {
                        new()
                        {
                            ShiftName = "Morning",
                            TimeRange = "07:00-15:00",
                            RequiredStaff = 1,
                            AssignedStaff = 1,
                            AssignedEmployees = new List<string> { "John Doe" }
                        }
                    },
                    Chores = new List<ExportChoreData>(),
                    OnDuties = new List<ExportOnDutyData>()
                }
            }
        };

        var bytes = await _service.GenerateExcelAsync(data);

        bytes.Should().NotBeEmpty();
        // XLSX files start with PK header (ZIP archive)
        bytes[0].Should().Be(0x50); // 'P'
        bytes[1].Should().Be(0x4B); // 'K'
    }

    // --- GeneratePdfAsync ---

    [Fact]
    public async Task GeneratePdfAsync_ProducesNonEmptyBytes()
    {
        var data = new ScheduleExportData
        {
            CompanyName = "Test Company",
            GeneratedAt = DateTime.UtcNow,
            StartDate = new DateTime(2026, 3, 25),
            EndDate = new DateTime(2026, 3, 25),
            Days = new List<ExportDayData>
            {
                new()
                {
                    Date = new DateTime(2026, 3, 25),
                    DayName = "Wednesday",
                    Shifts = new List<ExportShiftData>(),
                    Chores = new List<ExportChoreData>(),
                    OnDuties = new List<ExportOnDutyData>()
                }
            }
        };

        var bytes = await _service.GeneratePdfAsync(data);

        bytes.Should().NotBeEmpty();
        // PDF files start with %PDF
        var header = Encoding.ASCII.GetString(bytes, 0, 4);
        header.Should().Be("%PDF");
    }
}
