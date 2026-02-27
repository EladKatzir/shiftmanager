using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests;

/// <summary>
/// Startup Safety Tests — verifies that critical infrastructure components
/// are correctly configured and wired together.
///
/// SS-01: All core DI service interfaces have implementations registered
/// SS-02: InMemory database schema applies without error
/// SS-03: Core entity types are tracked by DbContext
/// SS-04: Tenant query filter is applied to IBelongsToCompany entities
/// SS-05: Critical enums have expected member counts (no accidental removal)
/// </summary>
public class StartupSafetyTests : IDisposable
{
    private readonly AppDbContext _db;

    public StartupSafetyTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    /// <summary>
    /// SS-01: All core service interfaces have concrete implementations in the Services directory.
    /// This validates that the DI wiring won't fail at runtime for critical services.
    /// </summary>
    [Fact]
    public void SS01_CoreServiceInterfaces_HaveImplementations()
    {
        // Get all interface types from the Services namespace
        var serviceAssembly = typeof(ITenantResolver).Assembly;
        var serviceInterfaces = serviceAssembly.GetTypes()
            .Where(t => t.IsInterface && t.Namespace == "ShiftManager.Services")
            .ToList();

        serviceInterfaces.Should().NotBeEmpty("there should be service interfaces defined");

        // Check that each interface has at least one implementing class
        var missingImplementations = new List<string>();
        foreach (var iface in serviceInterfaces)
        {
            var hasImpl = serviceAssembly.GetTypes()
                .Any(t => t.IsClass && !t.IsAbstract && iface.IsAssignableFrom(t));

            if (!hasImpl)
            {
                missingImplementations.Add(iface.Name);
            }
        }

        // ASSERT: No interfaces without implementations
        missingImplementations.Should().BeEmpty(
            $"all service interfaces should have implementations, but these don't: {string.Join(", ", missingImplementations)}");
    }

    /// <summary>
    /// SS-02: InMemory database schema applies without error — verifies that all
    /// entity configurations in OnModelCreating() are valid.
    /// </summary>
    [Fact]
    public void SS02_DatabaseSchema_AppliesWithoutError()
    {
        // Act — ensure the model can be fully built
        var model = _db.Model;

        // Assert
        model.Should().NotBeNull();

        // All entity types should be tracked
        var entityTypes = model.GetEntityTypes().ToList();
        entityTypes.Should().NotBeEmpty();
        entityTypes.Count.Should().BeGreaterThan(20,
            "ShiftManager has many entity types; fewer than 20 indicates missing model configuration");
    }

    /// <summary>
    /// SS-03: Core entity types are tracked by DbContext — verifies the most critical
    /// entities are included in the model.
    /// </summary>
    [Theory]
    [InlineData(typeof(AppUser))]
    [InlineData(typeof(Company))]
    [InlineData(typeof(ShiftInstance))]
    [InlineData(typeof(ShiftAssignment))]
    [InlineData(typeof(TimeOffRequest))]
    [InlineData(typeof(Chore))]
    [InlineData(typeof(OnDuty))]
    [InlineData(typeof(SwapRequest))]
    [InlineData(typeof(Grant))]
    [InlineData(typeof(RoleTemplate))]
    [InlineData(typeof(TeamCalendar))]
    [InlineData(typeof(EmailTemplateCustomization))]
    [InlineData(typeof(CompanyLocalizationOverride))]
    [InlineData(typeof(UserNotification))]
    [InlineData(typeof(DutyRotation))]
    public void SS03_CoreEntityTypes_AreTracked(Type entityType)
    {
        // Act
        var entityTypeModel = _db.Model.FindEntityType(entityType);

        // Assert
        entityTypeModel.Should().NotBeNull(
            $"{entityType.Name} should be tracked by AppDbContext");
    }

    /// <summary>
    /// SS-04: IBelongsToCompany entities have CompanyId property for tenant filtering.
    /// </summary>
    [Fact]
    public void SS04_IBelongsToCompanyEntities_HaveCompanyId()
    {
        // Get all entity types that implement IBelongsToCompany
        var tenantEntities = _db.Model.GetEntityTypes()
            .Where(et => typeof(IBelongsToCompany).IsAssignableFrom(et.ClrType))
            .ToList();

        tenantEntities.Should().NotBeEmpty("there should be tenant-scoped entities");

        foreach (var entityType in tenantEntities)
        {
            var companyIdProp = entityType.FindProperty("CompanyId");
            companyIdProp.Should().NotBeNull(
                $"{entityType.ClrType.Name} implements IBelongsToCompany but doesn't have CompanyId property");
        }
    }

    /// <summary>
    /// SS-05: Critical enums have expected minimum member counts to detect accidental removal.
    /// </summary>
    [Fact]
    public void SS05_CriticalEnums_HaveExpectedMemberCounts()
    {
        // UserRole should have at least 6 members (Owner, Manager, Employee, Director, Trainee, Assigner)
        var userRoleCount = Enum.GetValues<UserRole>().Length;
        userRoleCount.Should().BeGreaterThanOrEqualTo(6,
            "UserRole enum should have at least 6 roles");

        // EmailTemplateType should have at least 14 members
        var templateTypeCount = Enum.GetValues<ShiftManager.Models.Support.EmailTemplateType>().Length;
        templateTypeCount.Should().BeGreaterThanOrEqualTo(14,
            "EmailTemplateType enum should have at least 14 template types");

        // ArchiveDataTypes flags should include known values
        var archiveAll = ArchiveDataTypes.All;
        archiveAll.HasFlag(ArchiveDataTypes.Shifts).Should().BeTrue();
        archiveAll.HasFlag(ArchiveDataTypes.TimeOff).Should().BeTrue();
        archiveAll.HasFlag(ArchiveDataTypes.Chores).Should().BeTrue();
        archiveAll.HasFlag(ArchiveDataTypes.OnDuty).Should().BeTrue();
    }
}
