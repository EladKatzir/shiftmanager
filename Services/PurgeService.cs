using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// Service for purging historical data with safety mechanisms.
///
/// E-03: Deletion Strategy Reference
/// - ShiftInstance / ShiftAssignment: HARD DELETE during purge (cascade). No soft-delete.
/// - Chore: Soft-delete via CanceledAt in normal ops. HARD DELETE during purge.
/// - OnDuty: Soft-delete via CanceledAt in normal ops. HARD DELETE during purge.
/// - TimeOffRequest: HARD DELETE during purge. No soft-delete.
/// - SwapRequest: HARD DELETE during purge. No soft-delete.
/// - DirectorCompany: Soft-delete via IsDeleted/DeletedAt.
/// - TeamCalendar: Soft-delete via IsDeleted.
/// - AuditLog: HARD DELETE via retention policy (E-02). Never soft-deleted.
///
/// All purge operations MUST be preceded by a verified archive (E-04).
/// </summary>
public class PurgeService : IPurgeService
{
    private readonly AppDbContext _db;
    private readonly ITenantResolver _tenantResolver;
    private readonly IArchiveService _archiveService;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<PurgeService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PurgeService(
        AppDbContext db,
        ITenantResolver tenantResolver,
        IArchiveService archiveService,
        IAuditLogService auditLogService,
        ILogger<PurgeService> logger,
        IConfiguration configuration,
        IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _tenantResolver = tenantResolver;
        _archiveService = archiveService;
        _auditLogService = auditLogService;
        _logger = logger;
        _configuration = configuration;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Validate typed confirmation matches expected format
    /// </summary>
    public bool ValidateConfirmation(string confirmation, string companyName, DateOnly cutoffDate)
    {
        if (string.IsNullOrWhiteSpace(confirmation) || string.IsNullOrWhiteSpace(companyName))
            return false;

        var expected = $"DELETE {companyName.ToUpperInvariant()} BEFORE {cutoffDate:yyyy-MM-dd}";
        return confirmation.Trim() == expected;
    }

    /// <summary>
    /// Purge historical data with safety checks
    /// </summary>
    public async Task<PurgeResult> PurgeDataAsync(PurgeRequest request)
    {
        var result = new PurgeResult();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var companyId = _tenantResolver.GetCurrentTenantId();
            var currentUserId = GetCurrentUserId();

            // 1. Validate fresh archive requirement
            if (!request.ArchiveConfirmed)
            {
                result.Success = false;
                result.Error = "You must confirm that you have exported/archived this data.";
                return result;
            }

            var isFreshArchive = await _archiveService.ValidateFreshArchiveAsync(request.CutoffDate, request.Types);
            if (!isFreshArchive)
            {
                result.Success = false;
                result.Error = "No fresh archive found. You must create an archive with the exact same cutoff date and data type selection before purging.";
                return result;
            }

            // 2. Validate typed confirmation
            var company = await _db.Companies.FindAsync(companyId);
            if (company == null)
            {
                result.Success = false;
                result.Error = "Company not found.";
                return result;
            }

            if (!ValidateConfirmation(request.TypedConfirmation, company.Name, request.CutoffDate))
            {
                result.Success = false;
                result.Error = $"Typed confirmation does not match. Expected: DELETE {company.Name.ToUpperInvariant()} BEFORE {request.CutoffDate:yyyy-MM-dd}";
                return result;
            }

            // 3. Create pre-purge backup
            var backupPath = await CreatePrePurgeBackupAsync();
            result.BackupPath = backupPath;

            // 4. Get DB size before
            result.DbSizeBeforeBytes = await GetDatabaseSizeAsync();

            // 5. Begin transaction
            using var transaction = await _db.Database.BeginTransactionAsync();

            try
            {
                // 6. Delete data in FK-safe order
                await PurgeDataInOrderAsync(request, companyId, currentUserId, result);

                // 7. Save changes and commit
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("Purge completed successfully. Deleted counts: {DeletedCounts}",
                    JsonSerializer.Serialize(result.DeletedCounts));

                // 8. Optional VACUUM (outside transaction)
                if (request.RunVacuum)
                {
                    _logger.LogInformation("Running VACUUM to reclaim space...");
                    await _db.Database.ExecuteSqlRawAsync("VACUUM;");
                }

                // 9. Get DB size after
                result.DbSizeAfterBytes = await GetDatabaseSizeAsync();

                result.Success = true;

                // 10. Log audit
                var totalDeleted = result.DeletedCounts.Values.Sum();
                await _auditLogService.LogUserActionAsync(
                    currentUserId,
                    "DataPurged",
                    "DataArchive",
                    null,
                    $"Purged {totalDeleted} records before {request.CutoffDate}",
                    JsonSerializer.Serialize(new
                    {
                        request.CutoffDate,
                        request.Types,
                        result.DeletedCounts,
                        result.DbSizeBeforeBytes,
                        result.DbSizeAfterBytes,
                        SizeSavedBytes = result.DbSizeBeforeBytes - result.DbSizeAfterBytes,
                        request.RunVacuum,
                        request.HardDeleteOnDuty
                    }));
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Purge transaction failed, rolled back");
                result.Success = false;
                result.Error = $"Purge failed and was rolled back: {ex.Message}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during purge operation");
            result.Success = false;
            result.Error = $"Purge error: {ex.Message}";
        }
        finally
        {
            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
        }

        return result;
    }

    #region Delete Order Implementation

    /// <summary>
    /// Delete data in FK-safe order
    /// </summary>
    private async Task PurgeDataInOrderAsync(
        PurgeRequest request,
        int companyId,
        int currentUserId,
        PurgeResult result)
    {
        // FK-Safe Delete Order:
        // 1. SwapRequest (by FromAssignment.ShiftInstance.WorkDate < cutoff)
        // 2. TimeOffRequest (EndDate < cutoff) + OFFLINE shifts auto-cleanup
        // 3. ShiftInstance (WorkDate < cutoff) → Cascades to ShiftAssignment
        // 4. Chore (Date < cutoff)
        // 5. OnDuty (Date < cutoff, UserId IN companyUsers) - soft/hard per checkbox

        // 1. SwapRequest
        if ((request.Types & ArchiveDataTypes.SwapRequests) != 0)
        {
            var deletedSwaps = await DeleteSwapRequestsAsync(companyId, request.CutoffDate);
            result.DeletedCounts["SwapRequests"] = deletedSwaps;
        }

        // 2. TimeOffRequest + OFFLINE shifts
        if ((request.Types & ArchiveDataTypes.TimeOff) != 0)
        {
            var deletedTimeOff = await DeleteTimeOffRequestsAsync(companyId, request.CutoffDate);
            result.DeletedCounts["TimeOffRequests"] = deletedTimeOff;

            var deletedOffline = await DeleteOfflineShiftsAsync(companyId, request.CutoffDate);
            result.DeletedCounts["OfflineShifts"] = deletedOffline;
        }

        // 3. ShiftInstance (cascades to ShiftAssignment)
        if ((request.Types & ArchiveDataTypes.Shifts) != 0)
        {
            var (deletedShifts, deletedAssignments) = await DeleteShiftsAsync(companyId, request.CutoffDate);
            result.DeletedCounts["ShiftInstances"] = deletedShifts;
            result.DeletedCounts["ShiftAssignments"] = deletedAssignments;
        }

        // 4. Chore
        if ((request.Types & ArchiveDataTypes.Chores) != 0)
        {
            var deletedChores = await DeleteChoresAsync(companyId, request.CutoffDate);
            result.DeletedCounts["Chores"] = deletedChores;
        }

        // 5. OnDuty (soft or hard delete)
        if ((request.Types & ArchiveDataTypes.OnDuty) != 0)
        {
            var deletedOnDuty = await DeleteOnDutyAsync(companyId, currentUserId, request.CutoffDate, request.HardDeleteOnDuty);
            result.DeletedCounts["OnDuty"] = deletedOnDuty;
        }
    }

    private async Task<int> DeleteSwapRequestsAsync(int companyId, DateOnly cutoffDate)
    {
        // Filter by FromAssignment.ShiftInstance.WorkDate < cutoff
        var swapRequestIds = await _db.SwapRequests
            .Include(sr => sr.FromAssignment)
            .ThenInclude(fa => fa!.ShiftInstance)
            .Where(sr => sr.FromAssignment != null &&
                         sr.FromAssignment.CompanyId == companyId &&
                         sr.FromAssignment.ShiftInstance != null &&
                         sr.FromAssignment.ShiftInstance.WorkDate < cutoffDate)
            .Select(sr => sr.Id)
            .ToListAsync();

        if (swapRequestIds.Count == 0)
            return 0;

        var swapsToDelete = await _db.SwapRequests
            .Where(sr => swapRequestIds.Contains(sr.Id))
            .ToListAsync();

        _db.SwapRequests.RemoveRange(swapsToDelete);

        _logger.LogInformation("Marked {Count} SwapRequests for deletion", swapsToDelete.Count);
        return swapsToDelete.Count;
    }

    private async Task<int> DeleteTimeOffRequestsAsync(int companyId, DateOnly cutoffDate)
    {
        var timeOffToDelete = await _db.TimeOffRequests
            .Where(tor => tor.CompanyId == companyId && tor.EndDate < cutoffDate)
            .ToListAsync();

        _db.TimeOffRequests.RemoveRange(timeOffToDelete);

        _logger.LogInformation("Marked {Count} TimeOffRequests for deletion", timeOffToDelete.Count);
        return timeOffToDelete.Count;
    }

    private async Task<int> DeleteOfflineShiftsAsync(int companyId, DateOnly cutoffDate)
    {
        // Auto-cleanup OFFLINE shifts when purging TimeOff
        var offlineShiftTypeId = await _db.ShiftTypes
            .Where(st => st.CompanyId == companyId && st.Key == ShiftType.KEY_OFFLINE)
            .Select(st => st.Id)
            .FirstOrDefaultAsync();

        if (offlineShiftTypeId == 0)
            return 0;

        var offlineToDelete = await _db.ShiftInstances
            .Where(si => si.CompanyId == companyId &&
                         si.ShiftTypeId == offlineShiftTypeId &&
                         si.WorkDate < cutoffDate)
            .ToListAsync();

        _db.ShiftInstances.RemoveRange(offlineToDelete);

        _logger.LogInformation("Marked {Count} OFFLINE ShiftInstances for deletion", offlineToDelete.Count);
        return offlineToDelete.Count;
    }

    private async Task<(int shifts, int assignments)> DeleteShiftsAsync(int companyId, DateOnly cutoffDate)
    {
        // Get ShiftInstances to delete (excluding OFFLINE - handled separately)
        var offlineShiftTypeId = await _db.ShiftTypes
            .Where(st => st.CompanyId == companyId && st.Key == ShiftType.KEY_OFFLINE)
            .Select(st => st.Id)
            .FirstOrDefaultAsync();

        var shiftsToDelete = await _db.ShiftInstances
            .Where(si => si.CompanyId == companyId &&
                         si.WorkDate < cutoffDate &&
                         si.ShiftTypeId != offlineShiftTypeId)
            .ToListAsync();

        // Count assignments that will be cascaded
        var shiftIds = shiftsToDelete.Select(si => si.Id).ToList();
        var assignmentCount = await _db.ShiftAssignments
            .Where(sa => shiftIds.Contains(sa.ShiftInstanceId))
            .CountAsync();

        _db.ShiftInstances.RemoveRange(shiftsToDelete);

        _logger.LogInformation("Marked {ShiftCount} ShiftInstances and {AssignmentCount} ShiftAssignments for deletion (cascade)",
            shiftsToDelete.Count, assignmentCount);

        return (shiftsToDelete.Count, assignmentCount);
    }

    private async Task<int> DeleteChoresAsync(int companyId, DateOnly cutoffDate)
    {
        var choresToDelete = await _db.Chores
            .Where(c => c.CompanyId == companyId && c.Date < cutoffDate)
            .ToListAsync();

        _db.Chores.RemoveRange(choresToDelete);

        _logger.LogInformation("Marked {Count} Chores for deletion", choresToDelete.Count);
        return choresToDelete.Count;
    }

    private async Task<int> DeleteOnDutyAsync(int companyId, int currentUserId, DateOnly cutoffDate, bool hardDelete)
    {
        // OnDuty is GLOBAL table - must scope by UserId IN companyUserIds
        var companyUserIds = await _db.Users
            .Where(u => u.CompanyId == companyId)
            .Select(u => u.Id)
            .ToListAsync();

        var onDutyToDelete = await _db.OnDuties
            .Where(od => companyUserIds.Contains(od.UserId) && od.Date < cutoffDate)
            .ToListAsync();

        if (hardDelete)
        {
            _db.OnDuties.RemoveRange(onDutyToDelete);
            _logger.LogInformation("Marked {Count} OnDuty records for HARD deletion", onDutyToDelete.Count);
        }
        else
        {
            // Soft delete
            foreach (var od in onDutyToDelete)
            {
                od.CanceledAt = DateTime.UtcNow;
                od.CanceledBy = currentUserId;
            }
            _logger.LogInformation("Marked {Count} OnDuty records for SOFT deletion", onDutyToDelete.Count);
        }

        return onDutyToDelete.Count;
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Create pre-purge database backup
    /// </summary>
    private async Task<string> CreatePrePurgeBackupAsync()
    {
        try
        {
            var dbPath = _configuration.GetConnectionString("Default")?.Replace("Data Source=", "").Trim();
            if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
            {
                throw new FileNotFoundException("Database file not found", dbPath);
            }

            var backupsDir = Path.Combine(Directory.GetCurrentDirectory(), "Backups");
            Directory.CreateDirectory(backupsDir);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupFileName = $"pre_purge_{timestamp}.db";
            var backupFilePath = Path.Combine(backupsDir, backupFileName);

            _logger.LogInformation("Creating pre-purge backup: {BackupPath}", backupFilePath);

            // Pattern from Backup.cshtml.cs
            using (var sourceStream = new FileStream(dbPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
            using (var destinationStream = new FileStream(backupFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await sourceStream.CopyToAsync(destinationStream);
            }

            _logger.LogInformation("Pre-purge backup created successfully");
            return backupFilePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create pre-purge backup");
            throw new InvalidOperationException("Failed to create pre-purge backup. Purge aborted.", ex);
        }
    }

    /// <summary>
    /// Get current database file size
    /// </summary>
    private async Task<long> GetDatabaseSizeAsync()
    {
        try
        {
            var dbPath = _configuration.GetConnectionString("Default")?.Replace("Data Source=", "").Trim();
            if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
            {
                return 0;
            }

            return await Task.Run(() =>
            {
                var fileInfo = new FileInfo(dbPath);
                return fileInfo.Length;
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get database size");
            return 0;
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
