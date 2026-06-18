using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Services;

namespace ShiftManager.Tests.UnitTests.Services;

public class CalendarDayNoteServiceTests : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly AppDbContext _db;
    private readonly CalendarDayNoteService _service;

    private const int CompanyA = 1;
    private const int CompanyB = 2;

    public CalendarDayNoteServiceTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _sqliteConnection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;

        _db = new AppDbContext(options); // _tenantResolver is null in tests — service must IgnoreQueryFilters
        _db.Database.EnsureCreated();
        _service = new CalendarDayNoteService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
    }

    // --- SetDayNoteAsync (upsert) ---

    [Fact]
    public async Task SetDayNoteAsync_CreatesNote_WhenNoneExists()
    {
        var date = new DateOnly(2026, 6, 20);

        var note = await _service.SetDayNoteAsync(date, CompanyA, "Short-staffed today", createdByUserId: 7);

        note.Should().NotBeNull();
        note.Id.Should().BeGreaterThan(0);
        note.Date.Should().Be(date);
        note.CompanyId.Should().Be(CompanyA);
        note.Text.Should().Be("Short-staffed today");
        note.CreatedByUserId.Should().Be(7);

        (await _db.CalendarDayNotes.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task SetDayNoteAsync_UpdatesExisting_WhenNoteExistsForSameDateAndCompany()
    {
        var date = new DateOnly(2026, 6, 20);

        await _service.SetDayNoteAsync(date, CompanyA, "First text", createdByUserId: 7);
        var updated = await _service.SetDayNoteAsync(date, CompanyA, "Revised text", createdByUserId: 8);

        updated.Text.Should().Be("Revised text");
        updated.UpdatedAt.Should().NotBeNull();

        // Upsert — exactly one note for this (date, company), no duplicate row
        var rows = await _db.CalendarDayNotes.IgnoreQueryFilters()
            .Where(n => n.Date == date && n.CompanyId == CompanyA).ToListAsync();
        rows.Should().HaveCount(1);
        rows[0].Text.Should().Be("Revised text");
    }

    [Fact]
    public async Task SetDayNoteAsync_SameDateDifferentCompanies_CreatesSeparateIsolatedNotes()
    {
        var date = new DateOnly(2026, 6, 20);

        await _service.SetDayNoteAsync(date, CompanyA, "Company A note", createdByUserId: 1);
        await _service.SetDayNoteAsync(date, CompanyB, "Company B note", createdByUserId: 2);

        var all = await _db.CalendarDayNotes.IgnoreQueryFilters().ToListAsync();
        all.Should().HaveCount(2);
        all.Single(n => n.CompanyId == CompanyA).Text.Should().Be("Company A note");
        all.Single(n => n.CompanyId == CompanyB).Text.Should().Be("Company B note");
    }

    // --- DeleteDayNoteAsync ---

    [Fact]
    public async Task DeleteDayNoteAsync_ExistingNote_RemovesAndReturnsTrue()
    {
        var date = new DateOnly(2026, 6, 20);
        await _service.SetDayNoteAsync(date, CompanyA, "Delete me", createdByUserId: 1);

        var result = await _service.DeleteDayNoteAsync(date, CompanyA);

        result.Should().BeTrue();
        (await _db.CalendarDayNotes.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteDayNoteAsync_NotFound_ReturnsFalse()
    {
        var result = await _service.DeleteDayNoteAsync(new DateOnly(2026, 6, 20), CompanyA);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteDayNoteAsync_DoesNotTouchOtherCompanysNote()
    {
        var date = new DateOnly(2026, 6, 20);
        await _service.SetDayNoteAsync(date, CompanyA, "A note", createdByUserId: 1);
        await _service.SetDayNoteAsync(date, CompanyB, "B note", createdByUserId: 1);

        var result = await _service.DeleteDayNoteAsync(date, CompanyA);

        result.Should().BeTrue();
        var remaining = await _db.CalendarDayNotes.IgnoreQueryFilters().ToListAsync();
        remaining.Should().HaveCount(1);
        remaining[0].CompanyId.Should().Be(CompanyB);
    }

    // --- GetDayNotesForCompanyAsync ---

    [Fact]
    public async Task GetDayNotesForCompanyAsync_ReturnsNotesInRange_ForCompanyOnly()
    {
        await _service.SetDayNoteAsync(new DateOnly(2026, 6, 1), CompanyA, "In range A1", createdByUserId: 1);
        await _service.SetDayNoteAsync(new DateOnly(2026, 6, 15), CompanyA, "In range A2", createdByUserId: 1);
        await _service.SetDayNoteAsync(new DateOnly(2026, 7, 1), CompanyA, "Out of range", createdByUserId: 1);
        await _service.SetDayNoteAsync(new DateOnly(2026, 6, 10), CompanyB, "Other company", createdByUserId: 1);

        var result = await _service.GetDayNotesForCompanyAsync(CompanyA, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        result.Should().HaveCount(2);
        result[new DateOnly(2026, 6, 1)].Should().Be("In range A1");
        result[new DateOnly(2026, 6, 15)].Should().Be("In range A2");
        result.Should().NotContainKey(new DateOnly(2026, 7, 1));
        result.Should().NotContainKey(new DateOnly(2026, 6, 10));
    }

    [Fact]
    public async Task GetDayNotesForCompanyAsync_EmptyRange_ReturnsEmpty()
    {
        var result = await _service.GetDayNotesForCompanyAsync(CompanyA, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        result.Should().BeEmpty();
    }
}
