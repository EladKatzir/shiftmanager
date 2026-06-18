using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Schedule;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Tests for <see cref="PersonalTimelineService"/> — the unified personal-schedule pipeline shared by
/// the /My timeline and the Home spine. FK-off SQLite in-memory harness (no hierarchy seeding needed).
/// This logic was previously inline in the /My page model and untested; these are net-new.
/// </summary>
public sealed class PersonalTimelineServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private AppDbContext _db = null!;
    private PersonalTimelineService _svc = null!;

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _db = new AppDbContext(options);
        await _db.Database.EnsureCreatedAsync();
        _svc = new PersonalTimelineService(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<int> SeedUserAsync(int id = 1, UserRole role = UserRole.Employee)
    {
        _db.Users.Add(new AppUser { Id = id, CompanyId = 1, Email = $"u{id}@x", DisplayName = $"U{id}", Role = role });
        await _db.SaveChangesAsync();
        return id;
    }

    private async Task AddShiftAsync(int userId, DateOnly date)
    {
        var st = new ShiftType
        {
            Scope = ShiftScope.Molecule, MoleculeId = 1,
            Key = ShiftType.KEY_MORNING, Name = "Morning",
            Start = new TimeOnly(8, 0), End = new TimeOnly(16, 0)
        };
        _db.ShiftTypes.Add(st);
        await _db.SaveChangesAsync();
        var si = new ShiftInstance { CompanyId = 1, ShiftTypeId = st.Id, WorkDate = date, Name = "", StaffingRequired = 1 };
        _db.ShiftInstances.Add(si);
        await _db.SaveChangesAsync();
        _db.ShiftAssignments.Add(new ShiftAssignment { CompanyId = 1, ShiftInstanceId = si.Id, UserId = userId });
        await _db.SaveChangesAsync();
    }

    private async Task AddVacationAsync(int userId, DateOnly start, DateOnly end, RequestStatus status = RequestStatus.Approved)
    {
        _db.TimeOffRequests.Add(new TimeOffRequest
        {
            CompanyId = 1, UserId = userId, StartDate = start, EndDate = end,
            Type = TimeOffType.Vacation, Status = status
        });
        await _db.SaveChangesAsync();
    }

    private async Task AddOnDutyAsync(int userId, DateOnly date, DateTime? canceledAt = null)
    {
        _db.OnDuties.Add(new OnDuty { UserId = userId, Date = date, Type = OnDutyType.Hakam, CanceledAt = canceledAt });
        await _db.SaveChangesAsync();
    }

    private async Task AddChoreAsync(int userId, DateOnly date, DateTime? canceledAt = null)
    {
        _db.Chores.Add(new Chore { CompanyId = 1, UserId = userId, Date = date, Title = "Sweep", CanceledAt = canceledAt });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetTimelineAsync_AllFourTypes_AppearWithCorrectStats()
    {
        var uid = await SeedUserAsync();
        await AddShiftAsync(uid, Today.AddDays(1));
        await AddVacationAsync(uid, Today.AddDays(2), Today.AddDays(3));
        await AddOnDutyAsync(uid, Today.AddDays(4));
        await AddChoreAsync(uid, Today.AddDays(5));

        var result = await _svc.GetTimelineAsync(uid, Today, Today.AddDays(30));

        result.Items.Should().HaveCount(4);
        result.Items.Select(i => i.Type).Should().BeEquivalentTo(new[]
        {
            ItemType.Shift, ItemType.Vacation, ItemType.OnDuty, ItemType.Chore
        });
        result.Stats.ShiftCount.Should().Be(1);
        result.Stats.VacationCount.Should().Be(1);
        result.Stats.OnDutyCount.Should().Be(1);
        result.Stats.ChoreCount.Should().Be(1);
        result.Stats.ShiftHours.Should().BeApproximately(8.0, 0.01); // 08:00–16:00
    }

    [Fact]
    public async Task GetTimelineAsync_OrdersByDate()
    {
        var uid = await SeedUserAsync();
        await AddChoreAsync(uid, Today.AddDays(9));
        await AddShiftAsync(uid, Today.AddDays(1));
        await AddOnDutyAsync(uid, Today.AddDays(5));

        var result = await _svc.GetTimelineAsync(uid, Today, Today.AddDays(30));

        result.Items.Select(i => i.Date).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task GetTimelineAsync_ExcludesCanceledChoresAndOnDuty()
    {
        var uid = await SeedUserAsync();
        await AddOnDutyAsync(uid, Today.AddDays(2), canceledAt: DateTime.UtcNow);
        await AddChoreAsync(uid, Today.AddDays(3), canceledAt: DateTime.UtcNow);

        var result = await _svc.GetTimelineAsync(uid, Today, Today.AddDays(30));

        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTimelineAsync_ExcludesNonApprovedVacation()
    {
        var uid = await SeedUserAsync();
        await AddVacationAsync(uid, Today.AddDays(2), Today.AddDays(3), status: RequestStatus.Pending);

        var result = await _svc.GetTimelineAsync(uid, Today, Today.AddDays(30));

        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTimelineAsync_FlagsConflict_WhenTwoItemsShareADate()
    {
        var uid = await SeedUserAsync();
        var clash = Today.AddDays(2);
        await AddShiftAsync(uid, clash);
        await AddChoreAsync(uid, clash);

        var result = await _svc.GetTimelineAsync(uid, Today, Today.AddDays(30));

        result.Items.Should().HaveCount(2);
        result.Items.Should().OnlyContain(i => i.HasConflict);
    }

    [Fact]
    public async Task GetTimelineAsync_UnknownUser_ReturnsEmpty()
    {
        var result = await _svc.GetTimelineAsync(999, Today, Today.AddDays(30));

        result.Items.Should().BeEmpty();
        result.Stats.ShiftCount.Should().Be(0);
    }
}
