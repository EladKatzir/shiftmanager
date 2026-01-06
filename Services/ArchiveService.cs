using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using System.IO.Compression;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ShiftManager.Services;

/// <summary>
/// Service for creating and managing historical data archives
/// </summary>
public class ArchiveService : IArchiveService
{
    private readonly AppDbContext _db;
    private readonly ITenantResolver _tenantResolver;
    private readonly IAuditLogService _auditLogService;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ArchiveService> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly JsonSerializerOptions _jsonOptions;

    public ArchiveService(
        AppDbContext db,
        ITenantResolver tenantResolver,
        IAuditLogService auditLogService,
        IWebHostEnvironment env,
        ILogger<ArchiveService> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _tenantResolver = tenantResolver;
        _auditLogService = auditLogService;
        _env = env;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task<ArchivePreview> PreviewArchiveAsync(DateOnly cutoffDate, ArchiveDataTypes types)
    {
        var preview = new ArchivePreview
        {
            CutoffDate = cutoffDate
        };

        try
        {
            var companyId = _tenantResolver.GetCurrentTenantId();

            if (types.HasFlag(ArchiveDataTypes.Shifts))
            {
                var shiftCount = await _db.ShiftInstances
                    .Where(si => si.WorkDate < cutoffDate && si.ShiftType.Key != ShiftType.KEY_OFFLINE)
                    .CountAsync();

                var assignmentCount = await _db.ShiftAssignments
                    .Include(sa => sa.ShiftInstance)
                    .Where(sa => sa.ShiftInstance.WorkDate < cutoffDate && sa.ShiftInstance.ShiftType.Key != ShiftType.KEY_OFFLINE)
                    .CountAsync();

                preview.Counts["ShiftInstances"] = shiftCount;
                preview.Counts["ShiftAssignments"] = assignmentCount;
            }

            if (types.HasFlag(ArchiveDataTypes.SwapRequests))
            {
                // Count by related shift WorkDate
                var swapIds = await _db.SwapRequests
                    .Include(sr => sr.FromAssignment)
                    .ThenInclude(fa => fa.ShiftInstance)
                    .Where(sr => sr.FromAssignment.ShiftInstance.WorkDate < cutoffDate)
                    .Select(sr => sr.Id)
                    .ToListAsync();

                preview.Counts["SwapRequests"] = swapIds.Count;
            }

            if (types.HasFlag(ArchiveDataTypes.TimeOff))
            {
                var timeOffCount = await _db.TimeOffRequests
                    .Where(tor => tor.EndDate < cutoffDate)
                    .CountAsync();

                preview.Counts["TimeOffRequests"] = timeOffCount;

                // Count OFFLINE shifts that will be auto-cleaned
                var offlineCount = await _db.ShiftInstances
                    .Where(si => si.ShiftType.Key == ShiftType.KEY_OFFLINE && si.WorkDate < cutoffDate)
                    .CountAsync();

                if (offlineCount > 0)
                {
                    preview.Counts["OfflineShifts"] = offlineCount;
                    preview.Warnings.Add($"{offlineCount} OFFLINE shift instances will be auto-cleaned with TimeOff records");
                }
            }

            if (types.HasFlag(ArchiveDataTypes.Chores))
            {
                var choreCount = await _db.Chores
                    .Where(c => c.Date < cutoffDate)
                    .CountAsync();

                preview.Counts["Chores"] = choreCount;
            }

            if (types.HasFlag(ArchiveDataTypes.OnDuty))
            {
                var companyUserIds = await _db.Users
                    .Where(u => u.CompanyId == companyId)
                    .Select(u => u.Id)
                    .ToListAsync();

                var onDutyCount = await _db.OnDuties
                    .Where(od => od.Date < cutoffDate && companyUserIds.Contains(od.UserId))
                    .CountAsync();

                preview.Counts["OnDuty"] = onDutyCount;
                preview.Warnings.Add("OnDuty is a global table - only records for users in this company will be archived");
            }

            // Estimate size (rough approximation: 500 bytes per record)
            var totalRecords = preview.Counts.Values.Sum();
            preview.EstimatedSizeBytes = totalRecords * 500;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error previewing archive");
            throw;
        }

        return preview;
    }

    public async Task<ArchiveResult> CreateArchiveAsync(ArchiveRequest request)
    {
        var result = new ArchiveResult();

        try
        {
            var companyId = _tenantResolver.GetCurrentTenantId();
            var company = await _db.Companies.FindAsync(companyId);

            if (company == null)
            {
                result.Error = "Company not found";
                return result;
            }

            // Create archives folder
            var archivesFolder = Path.Combine(_env.ContentRootPath, "Archives");
            Directory.CreateDirectory(archivesFolder);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            // Collect data
            var data = await CollectArchiveDataAsync(request.CutoffDate, request.Types);

            // Create metadata
            var metadata = new ArchiveMetadata
            {
                CompanyId = companyId,
                CompanyName = company.Name,
                CutoffDate = request.CutoffDate,
                Types = request.Types,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = GetCurrentUserIdFromContext(),
                SchemaVersion = 1
            };

            // Generate CSV ZIP
            var csvZipPath = await GenerateCsvZipAsync(data, metadata, archivesFolder, timestamp);
            result.CsvZipPath = csvZipPath;
            result.CsvZipFileName = Path.GetFileName(csvZipPath);
            result.CsvZipSizeBytes = new FileInfo(csvZipPath).Length;

            // Generate NDJSON ZIP with hash
            var (ndjsonZipPath, hash) = await GenerateNdjsonZipAsync(data, metadata, archivesFolder, timestamp);
            result.NdjsonZipPath = ndjsonZipPath;
            result.NdjsonZipFileName = Path.GetFileName(ndjsonZipPath);
            result.NdjsonZipSizeBytes = new FileInfo(ndjsonZipPath).Length;
            result.Sha256Hash = hash;

            // Set metadata counts
            metadata.RowCounts = data.Counts;
            result.Metadata = metadata;

            // Log to audit (for fresh archive validation)
            await _auditLogService.LogUserActionAsync(
                metadata.CreatedBy,
                "ArchiveCreated",
                "DataArchive",
                null,
                $"Created archive before {request.CutoffDate}",
                JsonSerializer.Serialize(metadata, _jsonOptions));

            result.Success = true;
            _logger.LogInformation("Archive created: CSV={CsvFile}, NDJSON={NdjsonFile}, Hash={Hash}",
                result.CsvZipFileName, result.NdjsonZipFileName, result.Sha256Hash);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating archive");
            result.Error = ex.Message;
        }

        return result;
    }

    public async Task<ArchiveMetadata?> GetLatestArchiveAsync()
    {
        try
        {
            var companyId = _tenantResolver.GetCurrentTenantId();

            var latestLog = await _db.AuditLogs
                .Where(log => log.Action == "ArchiveCreated" && log.CompanyId == companyId)
                .OrderByDescending(log => log.Timestamp)
                .FirstOrDefaultAsync();

            if (latestLog?.Details == null)
                return null;

            return JsonSerializer.Deserialize<ArchiveMetadata>(latestLog.Details, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving latest archive");
            return null;
        }
    }

    public async Task<bool> ValidateFreshArchiveAsync(DateOnly cutoffDate, ArchiveDataTypes types)
    {
        try
        {
            var latest = await GetLatestArchiveAsync();

            if (latest == null)
                return false;

            // Check if created within last 24 hours
            if ((DateTime.UtcNow - latest.CreatedAt).TotalHours > 24)
                return false;

            // Verify exact match on cutoff and types
            return latest.CutoffDate == cutoffDate && latest.Types == types;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating fresh archive");
            return false;
        }
    }

    private async Task<ArchiveData> CollectArchiveDataAsync(DateOnly cutoffDate, ArchiveDataTypes types)
    {
        var companyId = _tenantResolver.GetCurrentTenantId();
        var data = new ArchiveData();

        if (types.HasFlag(ArchiveDataTypes.Shifts))
        {
            data.ShiftInstances = await _db.ShiftInstances
                .AsNoTracking()
                .Include(si => si.ShiftType)
                .Where(si => si.WorkDate < cutoffDate && si.ShiftType.Key != ShiftType.KEY_OFFLINE)
                .OrderBy(si => si.WorkDate)
                .ToListAsync();

            var shiftIds = data.ShiftInstances.Select(si => si.Id).ToList();

            data.ShiftAssignments = await _db.ShiftAssignments
                .AsNoTracking()
                .Include(sa => sa.User)
                .Include(sa => sa.Trainee)
                .Where(sa => shiftIds.Contains(sa.ShiftInstanceId))
                .ToListAsync();

            data.Counts["ShiftInstances"] = data.ShiftInstances.Count;
            data.Counts["ShiftAssignments"] = data.ShiftAssignments.Count;
        }

        if (types.HasFlag(ArchiveDataTypes.SwapRequests))
        {
            var swapIds = await _db.SwapRequests
                .Include(sr => sr.FromAssignment)
                .ThenInclude(fa => fa.ShiftInstance)
                .Where(sr => sr.FromAssignment.ShiftInstance.WorkDate < cutoffDate)
                .Select(sr => sr.Id)
                .ToListAsync();

            data.SwapRequests = await _db.SwapRequests
                .AsNoTracking()
                .Include(sr => sr.FromUser)
                .Include(sr => sr.ToUser)
                .Where(sr => swapIds.Contains(sr.Id))
                .ToListAsync();

            data.Counts["SwapRequests"] = data.SwapRequests.Count;
        }

        if (types.HasFlag(ArchiveDataTypes.TimeOff))
        {
            data.TimeOffRequests = await _db.TimeOffRequests
                .AsNoTracking()
                .Where(tor => tor.CompanyId == companyId && tor.EndDate < cutoffDate)
                .OrderBy(tor => tor.StartDate)
                .ToListAsync();

            // Include OFFLINE shifts for auto-cleanup tracking
            data.OfflineShifts = await _db.ShiftInstances
                .AsNoTracking()
                .Include(si => si.ShiftType)
                .Where(si => si.ShiftType.Key == ShiftType.KEY_OFFLINE && si.WorkDate < cutoffDate)
                .ToListAsync();

            data.Counts["TimeOffRequests"] = data.TimeOffRequests.Count;
            data.Counts["OfflineShifts"] = data.OfflineShifts.Count;
        }

        if (types.HasFlag(ArchiveDataTypes.Chores))
        {
            data.Chores = await _db.Chores
                .AsNoTracking()
                .Include(c => c.User)
                .Where(c => c.Date < cutoffDate)
                .OrderBy(c => c.Date)
                .ToListAsync();

            data.Counts["Chores"] = data.Chores.Count;
        }

        if (types.HasFlag(ArchiveDataTypes.OnDuty))
        {
            var companyUserIds = await _db.Users
                .Where(u => u.CompanyId == companyId)
                .Select(u => u.Id)
                .ToListAsync();

            data.OnDuties = await _db.OnDuties
                .AsNoTracking()
                .Include(od => od.User)
                .Where(od => od.Date < cutoffDate && companyUserIds.Contains(od.UserId))
                .OrderBy(od => od.Date)
                .ToListAsync();

            data.Counts["OnDuty"] = data.OnDuties.Count;
        }

        return data;
    }

    private async Task<string> GenerateCsvZipAsync(ArchiveData data, ArchiveMetadata metadata, string archivesFolder, string timestamp)
    {
        var zipFileName = $"archive_csv_{metadata.CompanyId}_{timestamp}.zip";
        var zipPath = Path.Combine(archivesFolder, zipFileName);

        // Create user email lookup for entities without User navigation property
        var allUserIds = data.TimeOffRequests.Select(t => t.UserId)
            .Concat(data.Chores.Select(c => c.UserId))
            .Concat(data.OnDuties.Select(od => od.UserId))
            .Distinct()
            .ToList();

        var userEmailLookup = await _db.Users
            .Where(u => allUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email);

        using var zipArchive = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        // README.txt
        var readmeEntry = zipArchive.CreateEntry("README.txt");
        using (var writer = new StreamWriter(readmeEntry.Open()))
        {
            await writer.WriteLineAsync("ShiftManager Data Archive (CSV Format)");
            await writer.WriteLineAsync("==========================================");
            await writer.WriteLineAsync();
            await writer.WriteLineAsync($"Company: {metadata.CompanyName}");
            await writer.WriteLineAsync($"Cutoff Date: {metadata.CutoffDate:yyyy-MM-dd}");
            await writer.WriteLineAsync($"Created: {metadata.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC");
            await writer.WriteLineAsync($"Schema Version: {metadata.SchemaVersion}");
            await writer.WriteLineAsync();
            await writer.WriteLineAsync("CSV Files:");
            await writer.WriteLineAsync("- csv/shift_instances.csv");
            await writer.WriteLineAsync("- csv/shift_assignments.csv");
            await writer.WriteLineAsync("- csv/swap_requests.csv");
            await writer.WriteLineAsync("- csv/timeoff_requests.csv");
            await writer.WriteLineAsync("- csv/chores.csv");
            await writer.WriteLineAsync("- csv/onduty.csv");
            await writer.WriteLineAsync();
            await writer.WriteLineAsync("This is a HUMAN-READABLE export. For re-importing, use the NDJSON archive.");
        }

        // CSV files
        if (data.ShiftInstances.Any())
        {
            var csvEntry = zipArchive.CreateEntry("csv/shift_instances.csv");
            using var csvWriter = new StreamWriter(csvEntry.Open());
            await csvWriter.WriteLineAsync("Id,CompanyId,ShiftTypeKey,WorkDate,Name,StaffingRequired,UpdatedAt");

            foreach (var si in data.ShiftInstances)
            {
                var csv = $"{si.Id},{si.CompanyId},{si.ShiftType.Key},{si.WorkDate:yyyy-MM-dd}," +
                          $"\"{EscapeCsv(si.Name)}\",{si.StaffingRequired},{si.UpdatedAt:yyyy-MM-dd HH:mm:ss}";
                await csvWriter.WriteLineAsync(csv);
            }
        }

        if (data.ShiftAssignments.Any())
        {
            var csvEntry = zipArchive.CreateEntry("csv/shift_assignments.csv");
            using var csvWriter = new StreamWriter(csvEntry.Open());
            await csvWriter.WriteLineAsync("Id,CompanyId,ShiftInstanceId,UserEmail,TraineeEmail,CreatedAt");

            foreach (var sa in data.ShiftAssignments)
            {
                var csv = $"{sa.Id},{sa.CompanyId},{sa.ShiftInstanceId}," +
                          $"{sa.User?.Email ?? ""},{sa.Trainee?.Email ?? ""},{sa.CreatedAt:yyyy-MM-dd HH:mm:ss}";
                await csvWriter.WriteLineAsync(csv);
            }
        }

        if (data.SwapRequests.Any())
        {
            var csvEntry = zipArchive.CreateEntry("csv/swap_requests.csv");
            using var csvWriter = new StreamWriter(csvEntry.Open());
            await csvWriter.WriteLineAsync("Id,CompanyId,FromAssignmentId,ToAssignmentId,FromUserEmail,ToUserEmail,Status,Reason,DeclineReason,CreatedAt,ReviewedAt");

            foreach (var sr in data.SwapRequests)
            {
                var csv = $"{sr.Id},{sr.CompanyId},{sr.FromAssignmentId},{sr.ToAssignmentId ?? 0}," +
                          $"{sr.FromUser?.Email ?? ""},{sr.ToUser?.Email ?? ""},{sr.Status}," +
                          $"\"{EscapeCsv(sr.Reason)}\",\"{EscapeCsv(sr.DeclineReason)}\"," +
                          $"{sr.CreatedAt:yyyy-MM-dd HH:mm:ss},{sr.ReviewedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? ""}";
                await csvWriter.WriteLineAsync(csv);
            }
        }

        if (data.TimeOffRequests.Any())
        {
            var csvEntry = zipArchive.CreateEntry("csv/timeoff_requests.csv");
            using var csvWriter = new StreamWriter(csvEntry.Open());
            await csvWriter.WriteLineAsync("Id,CompanyId,UserEmail,StartDate,EndDate,Type,Reason,Status,CreatedAt");

            foreach (var tor in data.TimeOffRequests)
            {
                var userEmail = userEmailLookup.TryGetValue(tor.UserId, out var email) ? email : "";
                var csv = $"{tor.Id},{tor.CompanyId},{userEmail}," +
                          $"{tor.StartDate:yyyy-MM-dd},{tor.EndDate:yyyy-MM-dd},{tor.Type}," +
                          $"\"{EscapeCsv(tor.Reason)}\",{tor.Status},{tor.CreatedAt:yyyy-MM-dd HH:mm:ss}";
                await csvWriter.WriteLineAsync(csv);
            }
        }

        if (data.Chores.Any())
        {
            var csvEntry = zipArchive.CreateEntry("csv/chores.csv");
            using var csvWriter = new StreamWriter(csvEntry.Open());
            await csvWriter.WriteLineAsync("Id,CompanyId,UserEmail,Date,Title,Notes,CreatedBy,CreatedAt,CanceledAt,CanceledBy");

            foreach (var c in data.Chores)
            {
                var csv = $"{c.Id},{c.CompanyId},{c.User?.Email ?? ""},{c.Date:yyyy-MM-dd}," +
                          $"\"{EscapeCsv(c.Title)}\",\"{EscapeCsv(c.Notes)}\",{c.CreatedBy}," +
                          $"{c.CreatedAt:yyyy-MM-dd HH:mm:ss},{c.CanceledAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? ""},{c.CanceledBy?.ToString() ?? ""}";
                await csvWriter.WriteLineAsync(csv);
            }
        }

        if (data.OnDuties.Any())
        {
            var csvEntry = zipArchive.CreateEntry("csv/onduty.csv");
            using var csvWriter = new StreamWriter(csvEntry.Open());
            await csvWriter.WriteLineAsync("Id,UserEmail,Date,Type,Notes,CreatedBy,CreatedAt,CanceledAt,CanceledBy");

            foreach (var od in data.OnDuties)
            {
                var csv = $"{od.Id},{od.User?.Email ?? ""},{od.Date:yyyy-MM-dd},{od.Type}," +
                          $"\"{EscapeCsv(od.Notes)}\",{od.CreatedBy}," +
                          $"{od.CreatedAt:yyyy-MM-dd HH:mm:ss},{od.CanceledAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? ""},{od.CanceledBy?.ToString() ?? ""}";
                await csvWriter.WriteLineAsync(csv);
            }
        }

        return zipPath;
    }

    private async Task<(string zipPath, string hash)> GenerateNdjsonZipAsync(ArchiveData data, ArchiveMetadata metadata, string archivesFolder, string timestamp)
    {
        var zipFileName = $"archive_import_{metadata.CompanyId}_{timestamp}.zip";
        var zipPath = Path.Combine(archivesFolder, zipFileName);

        // Create user email lookup for entities without User navigation property
        var allUserIds = data.TimeOffRequests.Select(t => t.UserId)
            .Concat(data.Chores.Select(c => c.UserId))
            .Concat(data.OnDuties.Select(od => od.UserId))
            .Distinct()
            .ToList();

        var userEmailLookup = await _db.Users
            .Where(u => allUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email);

        using var zipArchive = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        // manifest.json
        var manifestEntry = zipArchive.CreateEntry("manifest.json");
        using (var manifestWriter = new StreamWriter(manifestEntry.Open()))
        {
            var manifestJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            await manifestWriter.WriteAsync(manifestJson);
        }

        // data.ndjson
        var dataEntry = zipArchive.CreateEntry("data.ndjson");
        using var dataWriter = new StreamWriter(dataEntry.Open());

        // Write each entity as a separate line
        foreach (var si in data.ShiftInstances)
        {
            var record = new
            {
                type = "ShiftInstance",
                data = new
                {
                    si.Id,
                    si.CompanyId,
                    ShiftTypeKey = si.ShiftType.Key,
                    WorkDate = si.WorkDate.ToString("yyyy-MM-dd"),
                    si.Name,
                    si.StaffingRequired,
                    UpdatedAt = si.UpdatedAt.ToString("o")
                }
            };
            await dataWriter.WriteLineAsync(JsonSerializer.Serialize(record, _jsonOptions));
        }

        foreach (var sa in data.ShiftAssignments)
        {
            var record = new
            {
                type = "ShiftAssignment",
                data = new
                {
                    sa.Id,
                    sa.CompanyId,
                    sa.ShiftInstanceId,
                    UserEmail = sa.User?.Email ?? "",
                    TraineeEmail = sa.Trainee?.Email ?? "",
                    CreatedAt = sa.CreatedAt.ToString("o")
                }
            };
            await dataWriter.WriteLineAsync(JsonSerializer.Serialize(record, _jsonOptions));
        }

        foreach (var sr in data.SwapRequests)
        {
            var record = new
            {
                type = "SwapRequest",
                data = new
                {
                    sr.Id,
                    sr.CompanyId,
                    sr.FromAssignmentId,
                    sr.ToAssignmentId,
                    FromUserEmail = sr.FromUser?.Email ?? "",
                    ToUserEmail = sr.ToUser?.Email ?? "",
                    Status = sr.Status.ToString(),
                    sr.Reason,
                    sr.DeclineReason,
                    CreatedAt = sr.CreatedAt.ToString("o"),
                    ReviewedAt = sr.ReviewedAt?.ToString("o")
                }
            };
            await dataWriter.WriteLineAsync(JsonSerializer.Serialize(record, _jsonOptions));
        }

        foreach (var tor in data.TimeOffRequests)
        {
            var userEmail = userEmailLookup.TryGetValue(tor.UserId, out var email) ? email : "";
            var record = new
            {
                type = "TimeOffRequest",
                data = new
                {
                    tor.Id,
                    tor.CompanyId,
                    UserEmail = userEmail,
                    StartDate = tor.StartDate.ToString("yyyy-MM-dd"),
                    EndDate = tor.EndDate.ToString("yyyy-MM-dd"),
                    Type = tor.Type.ToString(),
                    tor.Reason,
                    Status = tor.Status.ToString(),
                    CreatedAt = tor.CreatedAt.ToString("o")
                }
            };
            await dataWriter.WriteLineAsync(JsonSerializer.Serialize(record, _jsonOptions));
        }

        foreach (var c in data.Chores)
        {
            var record = new
            {
                type = "Chore",
                data = new
                {
                    c.Id,
                    c.CompanyId,
                    UserEmail = c.User?.Email ?? "",
                    Date = c.Date.ToString("yyyy-MM-dd"),
                    c.Title,
                    c.Notes,
                    c.CreatedBy,
                    CreatedAt = c.CreatedAt.ToString("o"),
                    CanceledAt = c.CanceledAt?.ToString("o"),
                    c.CanceledBy
                }
            };
            await dataWriter.WriteLineAsync(JsonSerializer.Serialize(record, _jsonOptions));
        }

        foreach (var od in data.OnDuties)
        {
            var record = new
            {
                type = "OnDuty",
                data = new
                {
                    od.Id,
                    UserEmail = od.User?.Email ?? "",
                    Date = od.Date.ToString("yyyy-MM-dd"),
                    Type = od.Type.ToString(),
                    od.Notes,
                    od.CreatedBy,
                    CreatedAt = od.CreatedAt.ToString("o"),
                    CanceledAt = od.CanceledAt?.ToString("o"),
                    od.CanceledBy
                }
            };
            await dataWriter.WriteLineAsync(JsonSerializer.Serialize(record, _jsonOptions));
        }

        // Calculate SHA-256 hash
        var hash = await CalculateSha256Async(zipPath);

        return (zipPath, hash);
    }

    private async Task<string> CalculateSha256Async(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hashBytes = await sha256.ComputeHashAsync(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        // Escape double quotes by doubling them
        return value.Replace("\"", "\"\"");
    }

    private int GetCurrentUserIdFromContext()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return 0;
        }

        var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim != null && int.TryParse(userIdClaim.Value, out int userId))
        {
            return userId;
        }

        return 0;
    }

    private class ArchiveData
    {
        public List<ShiftInstance> ShiftInstances { get; set; } = new();
        public List<ShiftAssignment> ShiftAssignments { get; set; } = new();
        public List<SwapRequest> SwapRequests { get; set; } = new();
        public List<TimeOffRequest> TimeOffRequests { get; set; } = new();
        public List<ShiftInstance> OfflineShifts { get; set; } = new();
        public List<Chore> Chores { get; set; } = new();
        public List<OnDuty> OnDuties { get; set; } = new();
        public Dictionary<string, int> Counts { get; set; } = new();
    }
}
