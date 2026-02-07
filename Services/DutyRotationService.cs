using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

public class DutyRotationService : IDutyRotationService
{
    private readonly AppDbContext _db;
    private readonly ILogger<DutyRotationService> _logger;
    private readonly IConfiguration _configuration;

    public DutyRotationService(
        AppDbContext db,
        ILogger<DutyRotationService> logger,
        IConfiguration configuration)
    {
        _db = db;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<DutyRotation> CreateRotationAsync(
        string name,
        OnDutyType dutyType,
        RotationFrequency frequency,
        bool includeWeekends,
        int maxConsecutive,
        int createdBy)
    {
        var rotation = new DutyRotation
        {
            Name = name,
            DutyType = dutyType,
            Frequency = frequency,
            IncludeWeekends = includeWeekends,
            MaxConsecutiveDays = maxConsecutive,
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow
        };

        _db.DutyRotations.Add(rotation);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "DutyRotation {RotationId} '{Name}' created by user {CreatedBy}",
            rotation.Id, name, createdBy);

        return rotation;
    }

    public async Task<DutyRotation?> GetRotationAsync(int rotationId)
    {
        return await _db.DutyRotations
            .Include(r => r.Creator)
            .Include(r => r.Entries.Where(e => e.IsActive))
                .ThenInclude(e => e.User)
            .FirstOrDefaultAsync(r => r.Id == rotationId);
    }

    public async Task<List<DutyRotation>> GetAllRotationsAsync(bool includeInactive = false)
    {
        var query = _db.DutyRotations
            .Include(r => r.Creator)
            .Include(r => r.Entries.Where(e => e.IsActive))
            .AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(r => r.IsActive);
        }

        return await query
            .OrderBy(r => r.Name)
            .ToListAsync();
    }

    public async Task<bool> UpdateRotationAsync(
        int rotationId,
        string? name,
        RotationFrequency? frequency,
        bool? includeWeekends,
        int? maxConsecutive,
        bool? isActive)
    {
        var rotation = await _db.DutyRotations.FindAsync(rotationId);
        if (rotation == null) return false;

        if (name != null) rotation.Name = name;
        if (frequency.HasValue) rotation.Frequency = frequency.Value;
        if (includeWeekends.HasValue) rotation.IncludeWeekends = includeWeekends.Value;
        if (maxConsecutive.HasValue) rotation.MaxConsecutiveDays = maxConsecutive.Value;
        if (isActive.HasValue) rotation.IsActive = isActive.Value;

        rotation.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "DutyRotation {RotationId} updated", rotationId);

        return true;
    }

    public async Task<bool> DeleteRotationAsync(int rotationId)
    {
        var rotation = await _db.DutyRotations.FindAsync(rotationId);
        if (rotation == null) return false;

        rotation.IsActive = false;
        rotation.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "DutyRotation {RotationId} soft-deleted", rotationId);

        return true;
    }

    // Queue management

    public async Task<bool> AddUserToQueueAsync(int rotationId, int userId)
    {
        var rotation = await _db.DutyRotations
            .Include(r => r.Entries)
            .FirstOrDefaultAsync(r => r.Id == rotationId);

        if (rotation == null) return false;

        // Check if user already exists in queue
        if (rotation.Entries.Any(e => e.UserId == userId && e.IsActive))
        {
            _logger.LogWarning(
                "User {UserId} already exists in rotation {RotationId} queue", userId, rotationId);
            return false;
        }

        // Determine next position
        var maxPosition = rotation.Entries
            .Where(e => e.IsActive)
            .Select(e => (int?)e.Position)
            .Max() ?? -1;

        var entry = new DutyRotationEntry
        {
            DutyRotationId = rotationId,
            UserId = userId,
            Position = maxPosition + 1,
            CreatedAt = DateTime.UtcNow
        };

        _db.DutyRotationEntries.Add(entry);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "User {UserId} added to rotation {RotationId} at position {Position}",
            userId, rotationId, entry.Position);

        return true;
    }

    public async Task<bool> RemoveUserFromQueueAsync(int rotationId, int userId)
    {
        var entry = await _db.DutyRotationEntries
            .FirstOrDefaultAsync(e => e.DutyRotationId == rotationId && e.UserId == userId && e.IsActive);

        if (entry == null) return false;

        entry.IsActive = false;
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "User {UserId} removed from rotation {RotationId}", userId, rotationId);

        return true;
    }

    public async Task<bool> ReorderQueueAsync(int rotationId, List<int> userIdsInOrder)
    {
        var entries = await _db.DutyRotationEntries
            .Where(e => e.DutyRotationId == rotationId && e.IsActive)
            .ToListAsync();

        if (!entries.Any()) return false;

        for (int i = 0; i < userIdsInOrder.Count; i++)
        {
            var entry = entries.FirstOrDefault(e => e.UserId == userIdsInOrder[i]);
            if (entry != null)
            {
                entry.Position = i;
            }
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Rotation {RotationId} queue reordered with {Count} entries",
            rotationId, userIdsInOrder.Count);

        return true;
    }

    public async Task<List<DutyRotationEntry>> GetQueueAsync(int rotationId)
    {
        return await _db.DutyRotationEntries
            .Include(e => e.User)
            .Where(e => e.DutyRotationId == rotationId && e.IsActive)
            .OrderBy(e => e.Position)
            .ToListAsync();
    }

    // Assignment

    public async Task<(bool Success, string Message, OnDuty? OnDuty)> AssignNextAsync(
        int rotationId,
        DateOnly date,
        int assignedBy)
    {
        var rotation = await _db.DutyRotations
            .Include(r => r.Entries.Where(e => e.IsActive))
                .ThenInclude(e => e.User)
            .FirstOrDefaultAsync(r => r.Id == rotationId);

        if (rotation == null)
            return (false, "Rotation not found.", null);

        if (!rotation.IsActive)
            return (false, "Rotation is not active.", null);

        var activeEntries = rotation.Entries
            .Where(e => e.IsActive)
            .OrderBy(e => e.Position)
            .ToList();

        if (!activeEntries.Any())
            return (false, "No users in rotation queue.", null);

        var enforceRankEligibility = _configuration.GetValue<bool>("Features:EnforceRankEligibility", false);
        var totalEntries = activeEntries.Count;
        var startPosition = rotation.CurrentQueuePosition % totalEntries;
        OnDuty? createdOnDuty = null;

        // Try each user in the queue starting from current position
        for (int i = 0; i < totalEntries; i++)
        {
            var index = (startPosition + i) % totalEntries;
            var entry = activeEntries[index];
            var user = entry.User;

            if (user == null) continue;

            // Check 1: User is active
            var dbUser = await _db.Users.IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Id == entry.UserId);

            if (dbUser == null || !dbUser.IsActive)
            {
                await LogSkipAsync(rotationId, null, entry.UserId, "INACTIVE", date);
                continue;
            }

            // Check 2: Vacation conflict
            var hasVacation = await _db.TimeOffRequests.IgnoreQueryFilters()
                .AnyAsync(t => t.UserId == entry.UserId &&
                              t.Status == RequestStatus.Approved &&
                              t.StartDate <= date &&
                              t.EndDate >= date);

            if (hasVacation)
            {
                await LogSkipAsync(rotationId, null, entry.UserId, "VACATION", date);
                continue;
            }

            // Check 3: Existing OnDuty of same type on same date
            var hasExisting = await _db.Set<OnDuty>().IgnoreQueryFilters()
                .AnyAsync(o => o.UserId == entry.UserId &&
                              o.Date == date &&
                              o.Type == rotation.DutyType &&
                              o.CanceledAt == null);

            if (hasExisting)
            {
                await LogSkipAsync(rotationId, null, entry.UserId, "CONFLICT", date);
                continue;
            }

            // Check 4: Rank eligibility for Lead type
            if (enforceRankEligibility && rotation.DutyType == OnDutyType.Lead)
            {
                if (!dbUser.Rank.IsOfficer())
                {
                    await LogSkipAsync(rotationId, null, entry.UserId, "RANK", date);
                    continue;
                }
            }

            // User is eligible - create OnDuty record
            var onDuty = new OnDuty
            {
                UserId = entry.UserId,
                Date = date,
                Type = rotation.DutyType,
                Notes = $"Auto-assigned from rotation: {rotation.Name}",
                CreatedBy = assignedBy,
                CreatedAt = DateTime.UtcNow
            };

            _db.OnDuties.Add(onDuty);
            await _db.SaveChangesAsync();

            // Log the assignment
            var assignmentLog = new DutyRotationLog
            {
                DutyRotationId = rotationId,
                AssignedUserId = entry.UserId,
                AssignmentDate = date,
                OnDutyId = onDuty.Id,
                WasAutoAssigned = true,
                CreatedAt = DateTime.UtcNow
            };

            _db.DutyRotationLogs.Add(assignmentLog);

            // Advance queue position (wrap around)
            rotation.CurrentQueuePosition = (index + 1) % totalEntries;
            rotation.LastAssignedDate = date;
            rotation.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "DutyRotation {RotationId}: Assigned user {UserId} for {Date}. Queue position advanced to {Position}",
                rotationId, entry.UserId, date, rotation.CurrentQueuePosition);

            createdOnDuty = onDuty;
            return (true, $"Assigned to user {dbUser.DisplayName}.", createdOnDuty);
        }

        // No eligible user found after checking all entries
        return (false, "No eligible user found in rotation queue for this date.", null);
    }

    public async Task<List<(DateOnly Date, int UserId, string? SkipReason)>> PreviewRotationAsync(
        int rotationId,
        DateOnly startDate,
        DateOnly endDate)
    {
        var rotation = await _db.DutyRotations
            .Include(r => r.Entries.Where(e => e.IsActive))
                .ThenInclude(e => e.User)
            .FirstOrDefaultAsync(r => r.Id == rotationId);

        if (rotation == null)
            return new List<(DateOnly, int, string?)>();

        var activeEntries = rotation.Entries
            .Where(e => e.IsActive)
            .OrderBy(e => e.Position)
            .ToList();

        if (!activeEntries.Any())
            return new List<(DateOnly, int, string?)>();

        var enforceRankEligibility = _configuration.GetValue<bool>("Features:EnforceRankEligibility", false);
        var results = new List<(DateOnly Date, int UserId, string? SkipReason)>();
        var currentPosition = rotation.CurrentQueuePosition;
        var totalEntries = activeEntries.Count;

        // Pre-load vacation data for the date range
        var userIds = activeEntries.Select(e => e.UserId).ToList();
        var vacations = await _db.TimeOffRequests.IgnoreQueryFilters()
            .Where(t => userIds.Contains(t.UserId) &&
                       t.Status == RequestStatus.Approved &&
                       t.StartDate <= endDate &&
                       t.EndDate >= startDate)
            .ToListAsync();

        // Pre-load existing OnDuty assignments for the date range
        var existingDuties = await _db.Set<OnDuty>().IgnoreQueryFilters()
            .Where(o => userIds.Contains(o.UserId) &&
                       o.Date >= startDate &&
                       o.Date <= endDate &&
                       o.Type == rotation.DutyType &&
                       o.CanceledAt == null)
            .ToListAsync();

        // Pre-load user data
        var users = await _db.Users.IgnoreQueryFilters()
            .Where(u => userIds.Contains(u.Id))
            .ToListAsync();

        var dates = GetQualifyingDates(startDate, endDate, rotation.Frequency, rotation.IncludeWeekends);

        foreach (var date in dates)
        {
            var assigned = false;

            for (int i = 0; i < totalEntries; i++)
            {
                var index = (currentPosition + i) % totalEntries;
                var entry = activeEntries[index];
                var user = users.FirstOrDefault(u => u.Id == entry.UserId);

                if (user == null || !user.IsActive)
                {
                    results.Add((date, entry.UserId, "INACTIVE"));
                    continue;
                }

                var hasVacation = vacations.Any(t => t.UserId == entry.UserId &&
                                                    t.StartDate <= date &&
                                                    t.EndDate >= date);
                if (hasVacation)
                {
                    results.Add((date, entry.UserId, "VACATION"));
                    continue;
                }

                var hasExisting = existingDuties.Any(o => o.UserId == entry.UserId && o.Date == date);
                if (hasExisting)
                {
                    results.Add((date, entry.UserId, "CONFLICT"));
                    continue;
                }

                if (enforceRankEligibility && rotation.DutyType == OnDutyType.Lead)
                {
                    if (!user.Rank.IsOfficer())
                    {
                        results.Add((date, entry.UserId, "RANK"));
                        continue;
                    }
                }

                // Eligible - record assignment preview
                results.Add((date, entry.UserId, null));
                currentPosition = (index + 1) % totalEntries;
                assigned = true;
                break;
            }

            if (!assigned)
            {
                // No user could be assigned for this date - already logged skips above
            }
        }

        return results;
    }

    public async Task<int> GenerateAssignmentsAsync(
        int rotationId,
        DateOnly startDate,
        DateOnly endDate,
        int generatedBy)
    {
        var rotation = await _db.DutyRotations
            .FirstOrDefaultAsync(r => r.Id == rotationId);

        if (rotation == null) return 0;

        var dates = GetQualifyingDates(startDate, endDate, rotation.Frequency, rotation.IncludeWeekends);
        var successCount = 0;

        foreach (var date in dates)
        {
            var (success, message, onDuty) = await AssignNextAsync(rotationId, date, generatedBy);
            if (success)
            {
                successCount++;
            }
            else
            {
                _logger.LogDebug(
                    "DutyRotation {RotationId}: Could not assign for {Date}: {Message}",
                    rotationId, date, message);
            }
        }

        _logger.LogInformation(
            "DutyRotation {RotationId}: Generated {Count} assignments from {Start} to {End}",
            rotationId, successCount, startDate, endDate);

        return successCount;
    }

    // Logs

    public async Task<List<DutyRotationLog>> GetLogsAsync(
        int rotationId,
        DateOnly? startDate = null,
        DateOnly? endDate = null)
    {
        var query = _db.DutyRotationLogs
            .Include(l => l.AssignedUser)
            .Include(l => l.SkippedUser)
            .Include(l => l.OnDuty)
            .Where(l => l.DutyRotationId == rotationId);

        if (startDate.HasValue)
        {
            query = query.Where(l => l.AssignmentDate >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(l => l.AssignmentDate <= endDate.Value);
        }

        return await query
            .OrderByDescending(l => l.AssignmentDate)
            .ThenByDescending(l => l.CreatedAt)
            .ToListAsync();
    }

    // Private helpers

    private async Task LogSkipAsync(
        int rotationId,
        int? assignedUserId,
        int skippedUserId,
        string skipReason,
        DateOnly date)
    {
        var log = new DutyRotationLog
        {
            DutyRotationId = rotationId,
            AssignedUserId = assignedUserId,
            SkippedUserId = skippedUserId,
            SkipReason = skipReason,
            AssignmentDate = date,
            WasAutoAssigned = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.DutyRotationLogs.Add(log);
        await _db.SaveChangesAsync();
    }

    private List<DateOnly> GetQualifyingDates(
        DateOnly startDate,
        DateOnly endDate,
        RotationFrequency frequency,
        bool includeWeekends)
    {
        var dates = new List<DateOnly>();
        var current = startDate;

        while (current <= endDate)
        {
            var isWeekend = current.DayOfWeek == DayOfWeek.Saturday || current.DayOfWeek == DayOfWeek.Friday;

            if (!includeWeekends && isWeekend)
            {
                current = current.AddDays(1);
                continue;
            }

            switch (frequency)
            {
                case RotationFrequency.Daily:
                    dates.Add(current);
                    current = current.AddDays(1);
                    break;

                case RotationFrequency.Weekly:
                    if (current == startDate || current.DayOfWeek == startDate.DayOfWeek)
                    {
                        dates.Add(current);
                    }
                    current = current.AddDays(1);
                    break;

                case RotationFrequency.Biweekly:
                    if (current == startDate || current.DayOfWeek == startDate.DayOfWeek)
                    {
                        var weeksDiff = (current.DayNumber - startDate.DayNumber) / 7;
                        if (weeksDiff % 2 == 0)
                        {
                            dates.Add(current);
                        }
                    }
                    current = current.AddDays(1);
                    break;

                case RotationFrequency.Monthly:
                    if (current.Day == startDate.Day)
                    {
                        dates.Add(current);
                    }
                    current = current.AddDays(1);
                    break;

                default:
                    current = current.AddDays(1);
                    break;
            }
        }

        return dates;
    }
}
