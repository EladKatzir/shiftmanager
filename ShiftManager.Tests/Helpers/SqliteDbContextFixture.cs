using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;

namespace ShiftManager.Tests.Helpers;

/// <summary>
/// Test fixture that creates an <see cref="AppDbContext"/> backed by a real SQLite in-memory
/// database — NOT the EF Core `UseInMemoryDatabase` provider.
///
/// **Why this exists**: the EF Core In-Memory provider executes LINQ in C# without invoking
/// the SQL translator. That makes it blind to LINQ-to-SQL translation failures, which is
/// exactly the bug class that produced GRIFFIN-USERLOOKUP-510 in production (a
/// `.ToLowerInvariant()` inside an EF expression that the SQLite provider cannot translate).
/// All 1256 existing tests passed against the In-Memory provider while the real SQLite engine
/// would have thrown `InvalidOperationException` on the first invocation.
///
/// **When to use**: any new test that exercises an EF query expression — `.Where(...)`,
/// `.FirstOrDefaultAsync(...)`, `.AnyAsync(...)`, `.CountAsync(...)`, `.OrderBy(...)`,
/// `.Select(...)`, etc. — should use this fixture instead of `UseInMemoryDatabase`. The
/// SQLite provider is the same translator that runs in production, so anything that translates
/// here will work in production and anything that throws here is a real bug.
///
/// **When NOT to use**: tests that only exercise service business logic on already-materialized
/// objects (e.g. validation rules, mapping functions) and never touch the EF query layer can
/// keep using In-Memory — those tests don't gain anything from the SQLite engine.
///
/// **Usage**:
/// <code>
/// using var fixture = await SqliteDbContextFixture.CreateAsync();
/// fixture.Db.Users.Add(new AppUser { ... });
/// await fixture.Db.SaveChangesAsync();
/// // ... run tests against fixture.Db ...
/// </code>
///
/// The fixture implements <see cref="IAsyncDisposable"/> — the connection is closed and the
/// in-memory database is destroyed when disposed. Each fixture gets its own isolated DB.
/// </summary>
public sealed class SqliteDbContextFixture : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    public AppDbContext Db { get; }

    private SqliteDbContextFixture(SqliteConnection connection, AppDbContext db)
    {
        _connection = connection;
        Db = db;
    }

    /// <summary>
    /// Create a fresh SQLite-backed AppDbContext with the production schema applied.
    /// EnsureCreated() materialises every table the model defines — no migrations needed
    /// because we're starting from an empty in-memory file.
    /// </summary>
    public static async Task<SqliteDbContextFixture> CreateAsync()
    {
        // ":memory:" databases are scoped to the connection. We keep the connection alive
        // for the lifetime of the fixture so the in-memory DB persists across queries; when
        // the connection closes, the DB is gone.
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        return new SqliteDbContextFixture(connection, db);
    }

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
