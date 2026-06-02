using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services;

public class WidgetServiceTests : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly AppDbContext _db;
    private readonly WidgetService _service;
    private readonly Mock<IGrantService> _grantServiceMock;
    private readonly Mock<IStoreService> _storeServiceMock;
    private readonly Mock<IQuickInfoConfigService> _configServiceMock;

    public WidgetServiceTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _sqliteConnection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _grantServiceMock = new Mock<IGrantService>();
        _storeServiceMock = new Mock<IStoreService>();
        _configServiceMock = new Mock<IQuickInfoConfigService>();

        _service = new WidgetService(
            _db,
            _grantServiceMock.Object,
            _storeServiceMock.Object,
            _configServiceMock.Object);
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
    }

    // --- GetUserWidgetPreferencesAsync ---

    [Fact]
    public async Task GetUserWidgetPreferencesAsync_ReturnsDefaults()
    {
        var prefs = await _service.GetUserWidgetPreferencesAsync(1);

        prefs.Should().NotBeNull();
        prefs.OnCallWidgetCollapsed.Should().BeFalse();
        prefs.OfficeNumbersCollapsed.Should().BeTrue();
        prefs.FriendsOnCallCollapsed.Should().BeTrue();
        prefs.ShowOfficeNumbers.Should().BeTrue();
        prefs.ShowFriendsOnCall.Should().BeTrue();
    }

    // --- SaveUserWidgetPreferencesAsync ---

    [Fact]
    public async Task SaveUserWidgetPreferencesAsync_DoesNotThrow()
    {
        var prefs = new WidgetPreferences { OnCallWidgetCollapsed = true };

        var act = async () => await _service.SaveUserWidgetPreferencesAsync(1, prefs);

        await act.Should().NotThrowAsync();
    }

    // --- GetOfficeNumbersAsync ---

    [Fact]
    public async Task GetOfficeNumbersAsync_ReturnsEmptyList()
    {
        var result = await _service.GetOfficeNumbersAsync(1);

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    // --- GetCurrentHakamAsync ---

    [Fact]
    public async Task GetCurrentHakamAsync_NoHakamShiftTypes_ReturnsNull()
    {
        var result = await _service.GetCurrentHakamAsync(DateTime.Today);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentHakamAsync_HakamShiftTypeExists_NoAssignment_ReturnsNull()
    {
        _db.ShiftTypes.Add(new ShiftType
        {
            Id = 1, Key = "Hakam", NameEn = "Hakam",
            MoleculeId = 1, Scope = ShiftScope.Molecule,
            Start = new TimeOnly(8, 0), End = new TimeOnly(20, 0)
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetCurrentHakamAsync(DateTime.Today);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentHakamAsync_HakamAssignedToday_ReturnsContact()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        _db.Users.Add(new AppUser
        {
            Id = 10, Email = "hakam@test.com", DisplayName = "Hakam User",
            CompanyId = 1, IsActive = true, Role = UserRole.Employee, Phone = "050-1234567"
        });

        var shiftType = new ShiftType
        {
            Id = 1, Key = "Hakam", NameEn = "Hakam",
            MoleculeId = 1, Scope = ShiftScope.Molecule,
            Start = new TimeOnly(8, 0), End = new TimeOnly(20, 0)
        };
        _db.ShiftTypes.Add(shiftType);

        var instance = new ShiftInstance
        {
            Id = 1, ShiftTypeId = 1, CompanyId = 1, WorkDate = today, StaffingRequired = 1
        };
        _db.ShiftInstances.Add(instance);

        _db.ShiftAssignments.Add(new ShiftAssignment
        {
            Id = 1, ShiftInstanceId = 1, UserId = 10, CompanyId = 1
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetCurrentHakamAsync(DateTime.Today);

        result.Should().NotBeNull();
        result!.UserId.Should().Be(10);
        result.Name.Should().Be("Hakam User");
        result.Role.Should().Be("Hakam");
        result.PhoneNumber.Should().Be("050-1234567");
        result.ContactType.Should().Be(ContactType.Hakam);
    }

    // --- GetCompanyOnCallAsync ---

    [Fact]
    public async Task GetCompanyOnCallAsync_CompanyNotFound_ReturnsEmpty()
    {
        var result = await _service.GetCompanyOnCallAsync(999, DateTime.Today);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCompanyOnCallAsync_NoBRShiftTypes_ReturnsEmpty()
    {
        _db.Companies.Add(new Company { Id = 1, MoleculeId = 1, Name = "TestCo", DisplayName = "Test Co" });
        await _db.SaveChangesAsync();

        var result = await _service.GetCompanyOnCallAsync(1, DateTime.Today);

        result.Should().BeEmpty();
    }

    // --- GetFriendsOnCallAsync ---

    [Fact]
    public async Task GetFriendsOnCallAsync_NoFriends_ReturnsEmpty()
    {
        var result = await _service.GetFriendsOnCallAsync(1, DateTime.Today);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetFriendsOnCallAsync_WithFriend_ReturnsFriendInfo()
    {
        _db.Users.Add(new AppUser
        {
            Id = 1, Email = "user@test.com", DisplayName = "User 1",
            CompanyId = 1, IsActive = true, Role = UserRole.Employee
        });
        _db.Users.Add(new AppUser
        {
            Id = 2, Email = "friend@test.com", DisplayName = "Friend User",
            CompanyId = 1, IsActive = true, Role = UserRole.Employee, Phone = "050-9999999"
        });
        _db.UserFriendships.Add(new UserFriendship
        {
            Id = 1, UserId = 1, FriendId = 2, Status = FriendshipStatus.Accepted
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetFriendsOnCallAsync(1, DateTime.Today);

        result.Should().HaveCount(1);
        result[0].UserId.Should().Be(2);
        result[0].Name.Should().Be("Friend User");
        result[0].IsOnCall.Should().BeFalse();
    }

    [Fact]
    public async Task GetFriendsOnCallAsync_PendingFriendship_Excluded()
    {
        _db.Users.Add(new AppUser
        {
            Id = 1, Email = "user@test.com", DisplayName = "User 1",
            CompanyId = 1, IsActive = true, Role = UserRole.Employee
        });
        _db.Users.Add(new AppUser
        {
            Id = 2, Email = "pending@test.com", DisplayName = "Pending Friend",
            CompanyId = 1, IsActive = true, Role = UserRole.Employee
        });
        _db.UserFriendships.Add(new UserFriendship
        {
            Id = 1, UserId = 1, FriendId = 2, Status = FriendshipStatus.Pending
        });
        await _db.SaveChangesAsync();

        var result = await _service.GetFriendsOnCallAsync(1, DateTime.Today);

        result.Should().BeEmpty();
    }

    // --- BuildOnCallWidgetAsync ---

    [Fact]
    public async Task BuildOnCallWidgetAsync_GrantBased_NoGrants_ReturnsEmptyContacts()
    {
        _grantServiceMock.Setup(g => g.HasGrantAsync(1, "ViewHakamOnCall")).ReturnsAsync(false);
        _grantServiceMock.Setup(g => g.GetUserGrantsAsync(1)).ReturnsAsync(new List<Grant>());

        var result = await _service.BuildOnCallWidgetAsync(1, currentCompanyId: 0, moleculeId: 0);

        result.Should().NotBeNull();
        result.Contacts.Should().BeEmpty();
        result.IsCollapsed.Should().BeFalse();
    }

    [Fact]
    public async Task BuildOnCallWidgetAsync_MoleculeBased_WithConfig_BuildsContactsFromOnDuty()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        // Setup config service
        _configServiceMock.Setup(c => c.HasConfigAsync(1)).ReturnsAsync(true);
        _configServiceMock.Setup(c => c.GetConfigForMoleculeAsync(1)).ReturnsAsync(new List<QuickInfoConfig>
        {
            new QuickInfoConfig
            {
                Id = 1, MoleculeId = 1, SectionType = QuickInfoSectionType.OnCallRole,
                EntityId = 0, DisplayOrder = 0, IsEnabled = true
            }
        });

        // Setup on-duty type config
        _db.OnDutyTypeConfigs.Add(new OnDutyTypeConfig
        {
            Id = 1, TypeValue = 0, NameEn = "Hakam", NameHe = "חקם", IsActive = true
        });

        // Setup on-duty entry for today
        _db.Users.Add(new AppUser
        {
            Id = 10, Email = "hakam@test.com", DisplayName = "Hakam Today",
            CompanyId = 1, IsActive = true, Role = UserRole.Employee, Phone = "050-1111111"
        });
        _db.OnDuties.Add(new OnDuty
        {
            Id = 1, UserId = 10, Date = today, Type = OnDutyType.Hakam
        });
        await _db.SaveChangesAsync();

        var result = await _service.BuildOnCallWidgetAsync(1, currentCompanyId: 1, moleculeId: 1);

        result.Should().NotBeNull();
        result.Contacts.Should().HaveCount(1);
        result.Contacts[0].UserId.Should().Be(10);
        result.Contacts[0].Name.Should().Be("Hakam Today");
    }

    [Fact]
    public async Task BuildOnCallWidgetAsync_MoleculeBased_NoOnDutyToday_ShowsPlaceholder()
    {
        _configServiceMock.Setup(c => c.HasConfigAsync(1)).ReturnsAsync(true);
        _configServiceMock.Setup(c => c.GetConfigForMoleculeAsync(1)).ReturnsAsync(new List<QuickInfoConfig>
        {
            new QuickInfoConfig
            {
                Id = 1, MoleculeId = 1, SectionType = QuickInfoSectionType.OnCallRole,
                EntityId = 0, DisplayOrder = 0, IsEnabled = true
            }
        });

        _db.OnDutyTypeConfigs.Add(new OnDutyTypeConfig
        {
            Id = 1, TypeValue = 0, NameEn = "Hakam", NameHe = "חקם", IsActive = true
        });
        await _db.SaveChangesAsync();

        var result = await _service.BuildOnCallWidgetAsync(1, currentCompanyId: 1, moleculeId: 1);

        result.Contacts.Should().HaveCount(1);
        result.Contacts[0].UserId.Should().Be(0); // placeholder
        result.Contacts[0].AvatarInitial.Should().Be("?");
    }

    [Fact]
    public async Task BuildOnCallWidgetAsync_MoleculeBased_StoreConfig_CallsStoreService()
    {
        _configServiceMock.Setup(c => c.HasConfigAsync(1)).ReturnsAsync(true);
        _configServiceMock.Setup(c => c.GetConfigForMoleculeAsync(1)).ReturnsAsync(new List<QuickInfoConfig>
        {
            new QuickInfoConfig
            {
                Id = 1, MoleculeId = 1, SectionType = QuickInfoSectionType.Store,
                EntityId = 5, DisplayOrder = 0, IsEnabled = true
            }
        });

        _storeServiceMock
            .Setup(s => s.ComputeStoreStatusAsync(5, It.IsAny<DateTime>()))
            .ReturnsAsync(new StoreStatus
            {
                StoreId = 5, StoreName = "Main Store", Status = StoreStatusType.Open, Message = "Open until 14:00"
            });

        var result = await _service.BuildOnCallWidgetAsync(1, currentCompanyId: 1, moleculeId: 1);

        result.StoreStatuses.Should().HaveCount(1);
        result.StoreStatuses[0].StoreName.Should().Be("Main Store");
        _storeServiceMock.Verify(s => s.ComputeStoreStatusAsync(5, It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task BuildOnCallWidgetAsync_MoleculeBased_ShowBackup_AddsBackupContactUnderHakam()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        // Primary-Hakam section with ShowBackup ON.
        _configServiceMock.Setup(c => c.HasConfigAsync(1)).ReturnsAsync(true);
        _configServiceMock.Setup(c => c.GetConfigForMoleculeAsync(1)).ReturnsAsync(new List<QuickInfoConfig>
        {
            new QuickInfoConfig
            {
                Id = 1, MoleculeId = 1, SectionType = QuickInfoSectionType.OnCallRole,
                EntityId = 0, DisplayOrder = 0, IsEnabled = true, ShowBackup = true
            }
        });
        // The configured backup type is TypeValue 2.
        _configServiceMock.Setup(c => c.GetBackupHakamTypeValueAsync()).ReturnsAsync(2);

        _db.OnDutyTypeConfigs.Add(new OnDutyTypeConfig
        {
            Id = 1, TypeValue = 2, NameEn = "Backup-hakam", NameHe = "חקם רזרבה", IsActive = true
        });

        _db.Users.AddRange(
            new AppUser { Id = 10, Email = "primary@test.com", DisplayName = "Primary Hakam", CompanyId = 1, IsActive = true, Role = UserRole.Employee, Phone = "050-1111111" },
            new AppUser { Id = 11, Email = "backup@test.com", DisplayName = "Backup Hakam", CompanyId = 1, IsActive = true, Role = UserRole.Employee, Phone = "050-2222222" }
        );
        _db.OnDuties.AddRange(
            new OnDuty { Id = 1, UserId = 10, Date = today, Type = OnDutyType.Hakam },
            new OnDuty { Id = 2, UserId = 11, Date = today, Type = (OnDutyType)2 } // backup type value
        );
        await _db.SaveChangesAsync();

        var result = await _service.BuildOnCallWidgetAsync(1, currentCompanyId: 1, moleculeId: 1);

        // Both primary and backup render under the Hakam slot.
        result.Contacts.Should().HaveCount(2);
        result.Contacts[0].UserId.Should().Be(10);
        result.Contacts[0].Name.Should().Be("Primary Hakam");
        result.Contacts[0].Role.Should().Be("Hakam");
        result.Contacts[1].UserId.Should().Be(11);
        result.Contacts[1].Name.Should().Be("Backup Hakam");
        result.Contacts[1].Role.Should().Be("Backup-hakam");
    }

    [Fact]
    public async Task BuildOnCallWidgetAsync_MoleculeBased_PrimaryHakam_ShowBackupOff_PrimaryOnly()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        _configServiceMock.Setup(c => c.HasConfigAsync(1)).ReturnsAsync(true);
        _configServiceMock.Setup(c => c.GetConfigForMoleculeAsync(1)).ReturnsAsync(new List<QuickInfoConfig>
        {
            new QuickInfoConfig
            {
                Id = 1, MoleculeId = 1, SectionType = QuickInfoSectionType.OnCallRole,
                EntityId = 0, DisplayOrder = 0, IsEnabled = true, ShowBackup = false
            }
        });

        _db.Users.Add(new AppUser { Id = 10, Email = "primary@test.com", DisplayName = "Primary Hakam", CompanyId = 1, IsActive = true, Role = UserRole.Employee });
        _db.OnDuties.Add(new OnDuty { Id = 1, UserId = 10, Date = today, Type = OnDutyType.Hakam });
        await _db.SaveChangesAsync();

        var result = await _service.BuildOnCallWidgetAsync(1, currentCompanyId: 1, moleculeId: 1);

        // ShowBackup off → only the primary, and the backup resolver is never consulted.
        result.Contacts.Should().HaveCount(1);
        result.Contacts[0].UserId.Should().Be(10);
        result.Contacts[0].Role.Should().Be("Hakam");
        _configServiceMock.Verify(c => c.GetBackupHakamTypeValueAsync(), Times.Never);
    }

    [Fact]
    public async Task BuildOnCallWidgetAsync_MoleculeBased_NoConfig_UsesDefaults()
    {
        _configServiceMock.Setup(c => c.HasConfigAsync(1)).ReturnsAsync(false);
        _configServiceMock.Setup(c => c.GetDefaultConfigAsync(1)).ReturnsAsync(new List<QuickInfoConfigItem>());

        var result = await _service.BuildOnCallWidgetAsync(1, currentCompanyId: 1, moleculeId: 1);

        result.Should().NotBeNull();
        result.Contacts.Should().BeEmpty();
        _configServiceMock.Verify(c => c.GetDefaultConfigAsync(1), Times.Once);
    }
}
