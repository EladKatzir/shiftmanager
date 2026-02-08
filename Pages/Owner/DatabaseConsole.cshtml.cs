using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using System.Data;
using System.Data.Common;

namespace ShiftManager.Pages.Owner;

/// <summary>
/// Database Console - Execute SQL queries and view database schema
/// </summary>
[Authorize(Policy = "Grant:SystemConfiguration")]
public class DatabaseConsoleModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<DatabaseConsoleModel> _logger;
    private readonly IConfiguration _configuration;

    public DatabaseConsoleModel(
        AppDbContext db,
        ILogger<DatabaseConsoleModel> logger,
        IConfiguration configuration)
    {
        _db = db;
        _logger = logger;
        _configuration = configuration;
    }

    [BindProperty]
    public string Query { get; set; } = string.Empty;

    public List<string> Tables { get; set; } = new();
    public string? QueryResult { get; set; }
    public List<Dictionary<string, object?>> ResultRows { get; set; } = new();
    public List<string> ResultColumns { get; set; } = new();
    public string? Error { get; set; }
    public int RowsAffected { get; set; }

    public async Task OnGetAsync()
    {
        await LoadTablesAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            await LoadTablesAsync();

            if (string.IsNullOrWhiteSpace(Query))
            {
                Error = "Please enter a SQL query.";
                return Page();
            }

            // Security: Only allow single SELECT statements
            var trimmedQuery = Query.Trim();
            var upperQuery = trimmedQuery.ToUpper();

            if (!upperQuery.StartsWith("SELECT"))
            {
                Error = "For safety, only SELECT queries are allowed in the console.";
                return Page();
            }

            // Security: Reject multi-statement queries (prevents "SELECT 1; DROP TABLE x" injection)
            if (trimmedQuery.Contains(';'))
            {
                Error = "For safety, queries containing semicolons are not allowed. Please use a single SELECT statement.";
                return Page();
            }

            // Use a dedicated read-only connection to prevent any write operations
            var connString = _configuration.GetConnectionString("Default");
            if (string.IsNullOrEmpty(connString))
            {
                Error = "Database connection string not configured.";
                return Page();
            }

            // Force read-only mode on the SQLite connection
            var builder = new SqliteConnectionStringBuilder(connString)
            {
                Mode = SqliteOpenMode.ReadOnly
            };

            await using var readOnlyConnection = new SqliteConnection(builder.ConnectionString);
            await readOnlyConnection.OpenAsync();

            using var command = readOnlyConnection.CreateCommand();
            command.CommandText = trimmedQuery;
            command.CommandType = CommandType.Text;

            using var reader = await command.ExecuteReaderAsync();

            // Get column names
            ResultColumns = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                ResultColumns.Add(reader.GetName(i));
            }

            // Get rows
            ResultRows = new List<Dictionary<string, object?>>();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                ResultRows.Add(row);
            }

            QueryResult = $"Query executed successfully. {ResultRows.Count} rows returned.";
            _logger.LogInformation("Database query executed: {Query}, Rows: {RowCount}",
                trimmedQuery.Substring(0, Math.Min(trimmedQuery.Length, 100)), ResultRows.Count);

            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing database query");
            Error = $"Error executing query: {ex.Message}";
            return Page();
        }
    }

    private async Task LoadTablesAsync()
    {
        try
        {
            var connection = _db.Database.GetDbConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;";

            using var reader = await command.ExecuteReaderAsync();
            Tables = new List<string>();
            while (await reader.ReadAsync())
            {
                Tables.Add(reader.GetString(0));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading database tables");
        }
    }
}
