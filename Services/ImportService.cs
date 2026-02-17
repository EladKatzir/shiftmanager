using System.IO.Compression;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

/// <summary>
/// Service for importing archived data
/// </summary>
public class ImportService : IImportService
{
    private readonly AppDbContext _db;
    private readonly ITenantResolver _tenantResolver;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<ImportService> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ImportService(
        AppDbContext db,
        ITenantResolver tenantResolver,
        IAuditLogService auditLogService,
        ILogger<ImportService> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _tenantResolver = tenantResolver;
        _auditLogService = auditLogService;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Validate uploaded ZIP before import
    /// </summary>
    public async Task<ImportValidationResult> ValidateArchiveAsync(string zipPath)
    {
        var result = new ImportValidationResult();

        try
        {
            // Ensure file exists
            if (!File.Exists(zipPath))
            {
                result.Errors.Add("Archive file not found.");
                return result;
            }

            using var zipArchive = ZipFile.OpenRead(zipPath);

            // 1. Validate manifest.json exists
            var manifestEntry = zipArchive.GetEntry("manifest.json");
            if (manifestEntry == null)
            {
                result.Errors.Add("Invalid archive: manifest.json not found.");
                return result;
            }

            // 2. Read and parse manifest
            using var manifestStream = manifestEntry.Open();
            using var manifestReader = new StreamReader(manifestStream);
            var manifestJson = await manifestReader.ReadToEndAsync();

            ArchiveMetadata? metadata;
            try
            {
                metadata = JsonSerializer.Deserialize<ArchiveMetadata>(manifestJson);
                if (metadata == null)
                {
                    result.Errors.Add("Invalid archive: manifest.json is empty or malformed.");
                    return result;
                }
            }
            catch (JsonException ex)
            {
                result.Errors.Add($"Invalid archive: manifest.json parse error - {ex.Message}");
                return result;
            }

            result.Metadata = metadata;

            // 3. Validate company match
            var companyId = _tenantResolver.GetCurrentTenantId();
            var currentCompany = await _db.Companies.FindAsync(companyId);

            if (currentCompany == null)
            {
                result.Errors.Add("Current company not found.");
                return result;
            }

            result.CurrentCompanyName = currentCompany.Name;
            result.ArchiveCompanyName = metadata.CompanyName;
            result.CompanyMatch = currentCompany.Id == metadata.CompanyId;

            if (!result.CompanyMatch)
            {
                result.Errors.Add($"Company mismatch: Archive is for '{metadata.CompanyName}' (ID: {metadata.CompanyId}), but you are logged into '{currentCompany.Name}' (ID: {currentCompany.Id}).");
            }

            // 4. Validate data.ndjson exists
            var dataEntry = zipArchive.GetEntry("data.ndjson");
            if (dataEntry == null)
            {
                result.Errors.Add("Invalid archive: data.ndjson not found.");
                return result;
            }

            // 5. Scan data.ndjson for referenced users and shift types
            using var dataStream = dataEntry.Open();
            using var dataReader = new StreamReader(dataStream);

            var referencedUserEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var referencedShiftTypeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string? line;
            int lineNumber = 0;

            while ((line = await dataReader.ReadLineAsync()) != null)
            {
                lineNumber++;

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    if (!root.TryGetProperty("type", out var typeElement) ||
                        !root.TryGetProperty("data", out var dataElement))
                    {
                        result.Warnings.Add($"Line {lineNumber}: Missing 'type' or 'data' field, skipping.");
                        continue;
                    }

                    var recordType = typeElement.GetString();
                    if (string.IsNullOrEmpty(recordType))
                        continue;

                    // Extract user emails
                    if (dataElement.TryGetProperty("userEmail", out var userEmailProp))
                    {
                        var email = userEmailProp.GetString();
                        if (!string.IsNullOrEmpty(email))
                            referencedUserEmails.Add(email);
                    }

                    if (dataElement.TryGetProperty("traineeEmail", out var traineeEmailProp))
                    {
                        var email = traineeEmailProp.GetString();
                        if (!string.IsNullOrEmpty(email))
                            referencedUserEmails.Add(email);
                    }

                    if (dataElement.TryGetProperty("createdByEmail", out var createdByEmailProp))
                    {
                        var email = createdByEmailProp.GetString();
                        if (!string.IsNullOrEmpty(email))
                            referencedUserEmails.Add(email);
                    }

                    if (dataElement.TryGetProperty("canceledByEmail", out var canceledByEmailProp))
                    {
                        var email = canceledByEmailProp.GetString();
                        if (!string.IsNullOrEmpty(email))
                            referencedUserEmails.Add(email);
                    }

                    // Extract shift type keys
                    if (dataElement.TryGetProperty("shiftTypeKey", out var shiftTypeKeyProp))
                    {
                        var key = shiftTypeKeyProp.GetString();
                        if (!string.IsNullOrEmpty(key))
                            referencedShiftTypeKeys.Add(key);
                    }
                }
                catch (JsonException ex)
                {
                    result.Warnings.Add($"Line {lineNumber}: JSON parse error - {ex.Message}");
                }
            }

            // 6. Check for missing users in current company
            if (referencedUserEmails.Count > 0)
            {
                var existingUserEmails = await _db.Users
                    .Where(u => u.CompanyId == companyId && referencedUserEmails.Contains(u.Email))
                    .Select(u => u.Email)
                    .ToListAsync();

                result.MissingUsers = referencedUserEmails
                    .Except(existingUserEmails, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (result.MissingUsers.Count > 0)
                {
                    result.Warnings.Add($"{result.MissingUsers.Count} user(s) referenced in archive do not exist in current company: {string.Join(", ", result.MissingUsers.Take(10))}{(result.MissingUsers.Count > 10 ? "..." : "")}");
                }
            }

            // 7. Check for missing shift types
            if (referencedShiftTypeKeys.Count > 0)
            {
                var existingShiftTypeKeys = await _db.ShiftTypes
                    .Where(st => st.CompanyId == companyId && referencedShiftTypeKeys.Contains(st.Key))
                    .Select(st => st.Key)
                    .ToListAsync();

                result.MissingShiftTypeKeys = referencedShiftTypeKeys
                    .Except(existingShiftTypeKeys, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (result.MissingShiftTypeKeys.Count > 0)
                {
                    result.Errors.Add($"{result.MissingShiftTypeKeys.Count} shift type(s) referenced in archive do not exist: {string.Join(", ", result.MissingShiftTypeKeys)}. Import cannot proceed.");
                }
            }

            // 8. Final validation result
            result.IsValid = result.Errors.Count == 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating archive at {ZipPath}", zipPath);
            result.Errors.Add($"Validation error: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Import data from validated archive
    /// </summary>
    public async Task<ImportResult> ImportArchiveAsync(ImportRequest request)
    {
        var result = new ImportResult();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // 1. Validate archive first
            var validation = await ValidateArchiveAsync(request.ZipPath);
            if (!validation.IsValid)
            {
                result.Success = false;
                result.Error = $"Archive validation failed: {string.Join("; ", validation.Errors)}";
                return result;
            }

            // 2. Check for missing users
            if (validation.MissingUsers.Count > 0 && !request.SkipMissingUsers)
            {
                result.Success = false;
                result.Error = $"{validation.MissingUsers.Count} user(s) are missing. Please resolve or choose to skip records with missing users.";
                return result;
            }

            var companyId = _tenantResolver.GetCurrentTenantId();
            var currentUserId = GetCurrentUserId();

            // 3. Build lookups for natural keys
            var userLookup = await _db.Users
                .Where(u => u.CompanyId == companyId)
                .ToDictionaryAsync(u => u.Email.ToLowerInvariant(), u => u.Id);

            var shiftTypeLookup = await _db.ShiftTypes
                .Where(st => st.CompanyId == companyId)
                .ToDictionaryAsync(st => st.Key.ToLowerInvariant(), st => st);

            // 4. Begin transaction
            using var transaction = await _db.Database.BeginTransactionAsync();

            try
            {
                using var zipArchive = ZipFile.OpenRead(request.ZipPath);
                var dataEntry = zipArchive.GetEntry("data.ndjson");

                if (dataEntry == null)
                {
                    result.Success = false;
                    result.Error = "data.ndjson not found in archive.";
                    return result;
                }

                using var dataStream = dataEntry.Open();
                using var dataReader = new StreamReader(dataStream);

                // Track imported IDs for FK resolution
                var importedShiftInstanceIds = new Dictionary<int, int>(); // oldId -> newId
                var importedShiftAssignmentIds = new Dictionary<int, int>(); // oldId -> newId

                string? line;
                int lineNumber = 0;

                while ((line = await dataReader.ReadLineAsync()) != null)
                {
                    lineNumber++;

                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;

                        if (!root.TryGetProperty("type", out var typeElement) ||
                            !root.TryGetProperty("data", out var dataElement))
                        {
                            continue;
                        }

                        var recordType = typeElement.GetString();
                        if (string.IsNullOrEmpty(recordType))
                            continue;

                        switch (recordType)
                        {
                            case "ShiftInstance":
                                await ImportShiftInstanceAsync(dataElement, companyId, shiftTypeLookup, importedShiftInstanceIds, request, result);
                                break;

                            case "ShiftAssignment":
                                await ImportShiftAssignmentAsync(dataElement, companyId, userLookup, importedShiftInstanceIds, importedShiftAssignmentIds, request, result);
                                break;

                            case "SwapRequest":
                                await ImportSwapRequestAsync(dataElement, companyId, userLookup, importedShiftAssignmentIds, request, result);
                                break;

                            case "TimeOffRequest":
                                await ImportTimeOffRequestAsync(dataElement, companyId, userLookup, request, result);
                                break;

                            case "Chore":
                                await ImportChoreAsync(dataElement, companyId, userLookup, request, result);
                                break;

                            case "OnDuty":
                                await ImportOnDutyAsync(dataElement, userLookup, request, result);
                                break;

                            default:
                                IncrementStat(result, "Unknown", "Skipped");
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error importing line {LineNumber}", lineNumber);
                        IncrementStat(result, "Unknown", "Failed");
                    }
                }

                // Commit transaction
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                result.Success = true;

                // Log audit
                await _auditLogService.LogUserActionAsync(
                    currentUserId,
                    "DataImported",
                    "DataArchive",
                    null,
                    $"Imported {result.Stats.Values.Sum(s => s.Inserted)} records from archive",
                    JsonSerializer.Serialize(result));
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Import transaction failed, rolled back");
                result.Success = false;
                result.Error = $"Import failed: {ex.Message}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during import from {ZipPath}", request.ZipPath);
            result.Success = false;
            result.Error = $"Import error: {ex.Message}";
        }
        finally
        {
            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
        }

        return result;
    }

    #region Import Entity Methods

    private async Task ImportShiftInstanceAsync(
        JsonElement dataElement,
        int companyId,
        Dictionary<string, ShiftType> shiftTypeLookup,
        Dictionary<int, int> importedShiftInstanceIds,
        ImportRequest request,
        ImportResult result)
    {
        try
        {
            var oldId = dataElement.GetProperty("id").GetInt32();
            var workDate = DateOnly.Parse(dataElement.GetProperty("workDate").GetString()!);
            var shiftTypeKey = dataElement.GetProperty("shiftTypeKey").GetString()!;

            if (!shiftTypeLookup.TryGetValue(shiftTypeKey.ToLowerInvariant(), out var shiftType))
            {
                IncrementStat(result, "ShiftInstance", "Failed");
                return;
            }

            // Check for duplicate
            var exists = await _db.ShiftInstances.AnyAsync(si =>
                si.CompanyId == companyId &&
                si.WorkDate == workDate &&
                si.ShiftTypeId == shiftType.Id);

            if (exists)
            {
                if (request.ConflictPolicy == ImportConflictPolicy.SkipDuplicates)
                {
                    IncrementStat(result, "ShiftInstance", "Skipped");
                    return;
                }
                else if (request.ConflictPolicy == ImportConflictPolicy.FailOnConflict)
                {
                    IncrementStat(result, "ShiftInstance", "Failed");
                    return;
                }
                // OverwriteExisting: Delete and recreate (handled below)
            }

            var shiftInstance = new ShiftInstance
            {
                CompanyId = companyId,
                ShiftTypeId = shiftType.Id,
                WorkDate = workDate,
                Name = dataElement.GetProperty("name").GetString() ?? string.Empty,
                StaffingRequired = dataElement.GetProperty("staffingRequired").GetInt32(),
                UpdatedAt = DateTime.UtcNow
            };

            _db.ShiftInstances.Add(shiftInstance);
            await _db.SaveChangesAsync(); // Get new ID

            importedShiftInstanceIds[oldId] = shiftInstance.Id;
            IncrementStat(result, "ShiftInstance", "Inserted");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to import ShiftInstance");
            IncrementStat(result, "ShiftInstance", "Failed");
        }
    }

    private async Task ImportShiftAssignmentAsync(
        JsonElement dataElement,
        int companyId,
        Dictionary<string, int> userLookup,
        Dictionary<int, int> importedShiftInstanceIds,
        Dictionary<int, int> importedShiftAssignmentIds,
        ImportRequest request,
        ImportResult result)
    {
        try
        {
            var oldId = dataElement.GetProperty("id").GetInt32();
            var oldShiftInstanceId = dataElement.GetProperty("shiftInstanceId").GetInt32();
            var userEmail = dataElement.GetProperty("userEmail").GetString()!;

            // Resolve FK: ShiftInstance
            if (!importedShiftInstanceIds.TryGetValue(oldShiftInstanceId, out var newShiftInstanceId))
            {
                IncrementStat(result, "ShiftAssignment", "Failed");
                return;
            }

            // Resolve FK: User
            if (!userLookup.TryGetValue(userEmail.ToLowerInvariant(), out var userId))
            {
                if (request.SkipMissingUsers)
                {
                    IncrementStat(result, "ShiftAssignment", "Skipped");
                    return;
                }
                else
                {
                    IncrementStat(result, "ShiftAssignment", "Failed");
                    return;
                }
            }

            // Check for duplicate
            var exists = await _db.ShiftAssignments.AnyAsync(sa =>
                sa.ShiftInstanceId == newShiftInstanceId &&
                sa.UserId == userId);

            if (exists)
            {
                if (request.ConflictPolicy == ImportConflictPolicy.SkipDuplicates)
                {
                    IncrementStat(result, "ShiftAssignment", "Skipped");
                    return;
                }
                else if (request.ConflictPolicy == ImportConflictPolicy.FailOnConflict)
                {
                    IncrementStat(result, "ShiftAssignment", "Failed");
                    return;
                }
            }

            int? traineeUserId = null;
            if (dataElement.TryGetProperty("traineeEmail", out var traineeEmailProp))
            {
                var traineeEmail = traineeEmailProp.GetString();
                if (!string.IsNullOrEmpty(traineeEmail))
                {
                    if (userLookup.TryGetValue(traineeEmail.ToLowerInvariant(), out var traineeId))
                    {
                        traineeUserId = traineeId;
                    }
                }
            }

            var shiftAssignment = new ShiftAssignment
            {
                CompanyId = companyId,
                ShiftInstanceId = newShiftInstanceId,
                UserId = userId,
                TraineeUserId = traineeUserId,
                CreatedAt = dataElement.GetProperty("createdAt").GetDateTime()
            };

            _db.ShiftAssignments.Add(shiftAssignment);
            await _db.SaveChangesAsync(); // Get new ID

            importedShiftAssignmentIds[oldId] = shiftAssignment.Id;
            IncrementStat(result, "ShiftAssignment", "Inserted");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to import ShiftAssignment");
            IncrementStat(result, "ShiftAssignment", "Failed");
        }
    }

    private async Task ImportSwapRequestAsync(
        JsonElement dataElement,
        int companyId,
        Dictionary<string, int> userLookup,
        Dictionary<int, int> importedShiftAssignmentIds,
        ImportRequest request,
        ImportResult result)
    {
        try
        {
            var oldFromAssignmentId = dataElement.GetProperty("fromAssignmentId").GetInt32();

            // Resolve FK: FromAssignment
            if (!importedShiftAssignmentIds.TryGetValue(oldFromAssignmentId, out var newFromAssignmentId))
            {
                IncrementStat(result, "SwapRequest", "Failed");
                return;
            }

            int? newToAssignmentId = null;
            if (dataElement.TryGetProperty("toAssignmentId", out var toAssignmentIdProp) &&
                toAssignmentIdProp.ValueKind != JsonValueKind.Null)
            {
                var oldToAssignmentId = toAssignmentIdProp.GetInt32();
                if (importedShiftAssignmentIds.TryGetValue(oldToAssignmentId, out var toAssignId))
                {
                    newToAssignmentId = toAssignId;
                }
            }

            // Check for duplicate (by FromAssignment + Status)
            var statusString = dataElement.GetProperty("status").GetString()!;
            var status = Enum.Parse<RequestStatus>(statusString);

            var exists = await _db.SwapRequests.AnyAsync(sr =>
                sr.FromAssignmentId == newFromAssignmentId &&
                sr.Status == status);

            if (exists)
            {
                if (request.ConflictPolicy == ImportConflictPolicy.SkipDuplicates)
                {
                    IncrementStat(result, "SwapRequest", "Skipped");
                    return;
                }
                else if (request.ConflictPolicy == ImportConflictPolicy.FailOnConflict)
                {
                    IncrementStat(result, "SwapRequest", "Failed");
                    return;
                }
            }

            // Resolve FromUserId from the imported assignment
            var importedAssignment = await _db.ShiftAssignments.FindAsync(newFromAssignmentId);
            var fromUserId = importedAssignment?.UserId ?? 0;

            // Resolve optional ToUserId from archive data
            int? toUserId = null;
            if (dataElement.TryGetProperty("toUserId", out var toUserIdProp) && toUserIdProp.ValueKind != JsonValueKind.Null)
            {
                var oldToUserId = toUserIdProp.GetInt32();
                // Try to find user by looking up their email from the archive's user mapping
                // Fall back to keeping the value if user exists in target company
                var targetUser = await _db.Users.FirstOrDefaultAsync(u => u.Id == oldToUserId && u.CompanyId == companyId);
                if (targetUser != null)
                {
                    toUserId = oldToUserId;
                }
            }

            // Read optional string fields
            string? reason = null;
            if (dataElement.TryGetProperty("reason", out var reasonProp) && reasonProp.ValueKind != JsonValueKind.Null)
            {
                reason = reasonProp.GetString();
            }

            string? declineReason = null;
            if (dataElement.TryGetProperty("declineReason", out var declineReasonProp) && declineReasonProp.ValueKind != JsonValueKind.Null)
            {
                declineReason = declineReasonProp.GetString();
            }

            var swapRequest = new SwapRequest
            {
                CompanyId = companyId,
                FromAssignmentId = newFromAssignmentId,
                ToAssignmentId = newToAssignmentId,
                FromUserId = fromUserId,
                ToUserId = toUserId,
                Status = status,
                Reason = reason,
                DeclineReason = declineReason,
                CreatedAt = dataElement.GetProperty("createdAt").GetDateTime()
            };

            if (dataElement.TryGetProperty("reviewedAt", out var reviewedAtProp) &&
                reviewedAtProp.ValueKind != JsonValueKind.Null)
            {
                swapRequest.ReviewedAt = reviewedAtProp.GetDateTime();
            }

            _db.SwapRequests.Add(swapRequest);
            await _db.SaveChangesAsync();

            IncrementStat(result, "SwapRequest", "Inserted");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to import SwapRequest");
            IncrementStat(result, "SwapRequest", "Failed");
        }
    }

    private async Task ImportTimeOffRequestAsync(
        JsonElement dataElement,
        int companyId,
        Dictionary<string, int> userLookup,
        ImportRequest request,
        ImportResult result)
    {
        try
        {
            var userEmail = dataElement.GetProperty("userEmail").GetString()!;

            if (!userLookup.TryGetValue(userEmail.ToLowerInvariant(), out var userId))
            {
                if (request.SkipMissingUsers)
                {
                    IncrementStat(result, "TimeOffRequest", "Skipped");
                    return;
                }
                else
                {
                    IncrementStat(result, "TimeOffRequest", "Failed");
                    return;
                }
            }

            var startDate = DateOnly.Parse(dataElement.GetProperty("startDate").GetString()!);
            var endDate = DateOnly.Parse(dataElement.GetProperty("endDate").GetString()!);
            var reason = dataElement.GetProperty("reason").GetString()!;

            // Check for duplicate
            var exists = await _db.TimeOffRequests.AnyAsync(tor =>
                tor.UserId == userId &&
                tor.StartDate == startDate &&
                tor.EndDate == endDate);

            if (exists)
            {
                if (request.ConflictPolicy == ImportConflictPolicy.SkipDuplicates)
                {
                    IncrementStat(result, "TimeOffRequest", "Skipped");
                    return;
                }
                else if (request.ConflictPolicy == ImportConflictPolicy.FailOnConflict)
                {
                    IncrementStat(result, "TimeOffRequest", "Failed");
                    return;
                }
            }

            var statusString = dataElement.GetProperty("status").GetString()!;
            var status = Enum.Parse<RequestStatus>(statusString);

            var timeOffRequest = new TimeOffRequest
            {
                CompanyId = companyId,
                UserId = userId,
                StartDate = startDate,
                EndDate = endDate,
                Reason = reason,
                Status = status,
                CreatedAt = dataElement.GetProperty("createdAt").GetDateTime()
            };

            _db.TimeOffRequests.Add(timeOffRequest);
            await _db.SaveChangesAsync();

            IncrementStat(result, "TimeOffRequest", "Inserted");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to import TimeOffRequest");
            IncrementStat(result, "TimeOffRequest", "Failed");
        }
    }

    private async Task ImportChoreAsync(
        JsonElement dataElement,
        int companyId,
        Dictionary<string, int> userLookup,
        ImportRequest request,
        ImportResult result)
    {
        try
        {
            var userEmail = dataElement.GetProperty("userEmail").GetString()!;

            if (!userLookup.TryGetValue(userEmail.ToLowerInvariant(), out var userId))
            {
                if (request.SkipMissingUsers)
                {
                    IncrementStat(result, "Chore", "Skipped");
                    return;
                }
                else
                {
                    IncrementStat(result, "Chore", "Failed");
                    return;
                }
            }

            var date = DateOnly.Parse(dataElement.GetProperty("date").GetString()!);
            var title = dataElement.GetProperty("title").GetString()!;

            // Check for duplicate
            var exists = await _db.Chores.AnyAsync(c =>
                c.UserId == userId &&
                c.Date == date &&
                c.Title == title);

            if (exists)
            {
                if (request.ConflictPolicy == ImportConflictPolicy.SkipDuplicates)
                {
                    IncrementStat(result, "Chore", "Skipped");
                    return;
                }
                else if (request.ConflictPolicy == ImportConflictPolicy.FailOnConflict)
                {
                    IncrementStat(result, "Chore", "Failed");
                    return;
                }
            }

            int? canceledById = null;
            if (dataElement.TryGetProperty("canceledByEmail", out var canceledByEmailProp) &&
                canceledByEmailProp.ValueKind != JsonValueKind.Null)
            {
                var canceledByEmail = canceledByEmailProp.GetString();
                if (!string.IsNullOrEmpty(canceledByEmail))
                {
                    if (userLookup.TryGetValue(canceledByEmail.ToLowerInvariant(), out var canceledBy))
                    {
                        canceledById = canceledBy;
                    }
                }
            }

            // Resolve createdBy via email lookup (same pattern as canceledBy)
            var createdBy = userId; // fallback to the assigned user
            if (dataElement.TryGetProperty("createdByEmail", out var createdByEmailProp2) &&
                createdByEmailProp2.ValueKind != JsonValueKind.Null)
            {
                var createdByEmail = createdByEmailProp2.GetString();
                if (!string.IsNullOrEmpty(createdByEmail) &&
                    userLookup.TryGetValue(createdByEmail.ToLowerInvariant(), out var mappedCreatedBy))
                {
                    createdBy = mappedCreatedBy;
                }
            }

            var chore = new Chore
            {
                CompanyId = companyId,
                UserId = userId,
                Date = date,
                Title = title,
                CreatedBy = createdBy,
                CreatedAt = dataElement.GetProperty("createdAt").GetDateTime(),
                CanceledBy = canceledById
            };

            if (dataElement.TryGetProperty("canceledAt", out var canceledAtProp) &&
                canceledAtProp.ValueKind != JsonValueKind.Null)
            {
                chore.CanceledAt = canceledAtProp.GetDateTime();
            }

            _db.Chores.Add(chore);
            await _db.SaveChangesAsync();

            IncrementStat(result, "Chore", "Inserted");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to import Chore");
            IncrementStat(result, "Chore", "Failed");
        }
    }

    // NOTE: OnDuty is intentionally NOT company-scoped (global, supports cross-company assignments).
    // No CompanyId is set on the entity — this is by design per the OnDuty model.
    private async Task ImportOnDutyAsync(
        JsonElement dataElement,
        Dictionary<string, int> userLookup,
        ImportRequest request,
        ImportResult result)
    {
        try
        {
            var userEmail = dataElement.GetProperty("userEmail").GetString()!;

            if (!userLookup.TryGetValue(userEmail.ToLowerInvariant(), out var userId))
            {
                if (request.SkipMissingUsers)
                {
                    IncrementStat(result, "OnDuty", "Skipped");
                    return;
                }
                else
                {
                    IncrementStat(result, "OnDuty", "Failed");
                    return;
                }
            }

            var date = DateOnly.Parse(dataElement.GetProperty("date").GetString()!);

            // Parse Type before duplicate check so we can include it
            var typeStringForDup = dataElement.TryGetProperty("type", out var typePropForDup) && typePropForDup.ValueKind != JsonValueKind.Null
                ? typePropForDup.GetString()
                : null;
            var onDutyTypeForDup = !string.IsNullOrEmpty(typeStringForDup) && Enum.TryParse<OnDutyType>(typeStringForDup, out var parsedTypeForDup)
                ? parsedTypeForDup
                : OnDutyType.Hakam;

            // Check for duplicate (same user + date + type)
            var exists = await _db.OnDuties.AnyAsync(od =>
                od.UserId == userId &&
                od.Date == date &&
                od.Type == onDutyTypeForDup);

            if (exists)
            {
                if (request.ConflictPolicy == ImportConflictPolicy.SkipDuplicates)
                {
                    IncrementStat(result, "OnDuty", "Skipped");
                    return;
                }
                else if (request.ConflictPolicy == ImportConflictPolicy.FailOnConflict)
                {
                    IncrementStat(result, "OnDuty", "Failed");
                    return;
                }
            }

            int? canceledById = null;
            if (dataElement.TryGetProperty("canceledByEmail", out var canceledByEmailProp) &&
                canceledByEmailProp.ValueKind != JsonValueKind.Null)
            {
                var canceledByEmail = canceledByEmailProp.GetString();
                if (!string.IsNullOrEmpty(canceledByEmail))
                {
                    if (userLookup.TryGetValue(canceledByEmail.ToLowerInvariant(), out var canceledBy))
                    {
                        canceledById = canceledBy;
                    }
                }
            }

            // Resolve createdBy via email lookup (same pattern as canceledBy)
            var createdBy = userId; // fallback to the assigned user
            if (dataElement.TryGetProperty("createdByEmail", out var createdByEmailProp2) &&
                createdByEmailProp2.ValueKind != JsonValueKind.Null)
            {
                var createdByEmail = createdByEmailProp2.GetString();
                if (!string.IsNullOrEmpty(createdByEmail) &&
                    userLookup.TryGetValue(createdByEmail.ToLowerInvariant(), out var mappedCreatedBy))
                {
                    createdBy = mappedCreatedBy;
                }
            }

            // Resolve Type from archive (required field)
            var typeString = dataElement.TryGetProperty("type", out var typeProp) && typeProp.ValueKind != JsonValueKind.Null
                ? typeProp.GetString()
                : null;
            var onDutyType = !string.IsNullOrEmpty(typeString) && Enum.TryParse<OnDutyType>(typeString, out var parsedType)
                ? parsedType
                : OnDutyType.Hakam; // Default fallback

            string? notes = null;
            if (dataElement.TryGetProperty("notes", out var notesProp) && notesProp.ValueKind != JsonValueKind.Null)
            {
                notes = notesProp.GetString();
            }

            var onDuty = new OnDuty
            {
                UserId = userId,
                Date = date,
                Type = onDutyType,
                Notes = notes,
                CreatedBy = createdBy,
                CreatedAt = dataElement.GetProperty("createdAt").GetDateTime(),
                CanceledBy = canceledById
            };

            if (dataElement.TryGetProperty("canceledAt", out var canceledAtProp) &&
                canceledAtProp.ValueKind != JsonValueKind.Null)
            {
                onDuty.CanceledAt = canceledAtProp.GetDateTime();
            }

            _db.OnDuties.Add(onDuty);
            await _db.SaveChangesAsync();

            IncrementStat(result, "OnDuty", "Inserted");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to import OnDuty");
            IncrementStat(result, "OnDuty", "Failed");
        }
    }

    #endregion

    #region Helper Methods

    private void IncrementStat(ImportResult result, string entityType, string category)
    {
        if (!result.Stats.ContainsKey(entityType))
        {
            result.Stats[entityType] = new ImportEntityStats();
        }

        result.Stats[entityType].Total++;

        switch (category)
        {
            case "Inserted":
                result.Stats[entityType].Inserted++;
                break;
            case "Skipped":
                result.Stats[entityType].Skipped++;
                break;
            case "Failed":
                result.Stats[entityType].Failed++;
                break;
        }
    }

    /// <summary>
    /// Get current user ID from HTTP context
    /// </summary>
    private int GetCurrentUserId()
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

    #endregion
}
