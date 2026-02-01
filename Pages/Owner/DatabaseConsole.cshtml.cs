using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
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

    public DatabaseConsoleModel(
        AppDbContext db,
        ILogger<DatabaseConsoleModel> logger)
    {
        _db = db;
        _logger = logger;
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

            // Security: Only allow SELECT statements for safety
            var trimmedQuery = Query.Trim().ToUpper();
            if (!trimmedQuery.StartsWith("SELECT"))
            {
                Error = "For safety, only SELECT queries are allowed in the console.";
                return Page();
            }

            var connection = _db.Database.GetDbConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = Query;
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
                Query.Substring(0, Math.Min(Query.Length, 100)), ResultRows.Count);

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
