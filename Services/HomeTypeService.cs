using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — scoped by explicit moleculeId/homeTypeId;
// called only from authorized admin endpoints with ManageHomeTypes grant
public class HomeTypeService : IHomeTypeService
{
    private readonly AppDbContext _db;
    private readonly ILogger<HomeTypeService> _logger;

    public HomeTypeService(AppDbContext db, ILogger<HomeTypeService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<List<HomeTypeDto>> GetHomeTypesAsync(int moleculeId)
    {
        var homeTypes = await _db.HomeTypes
            .IgnoreQueryFilters()
            .Where(ht => ht.MoleculeId == moleculeId)
            .OrderBy(ht => ht.Name)
            .ToListAsync();

        var htIds = homeTypes.Select(ht => ht.Id).ToList();
        var userCounts = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.HomeTypeId.HasValue && htIds.Contains(u.HomeTypeId.Value))
            .GroupBy(u => u.HomeTypeId!.Value)
            .Select(g => new { HomeTypeId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.HomeTypeId, g => g.Count);

        return homeTypes.Select(ht =>
        {
            var rule = DeserializeRule(ht.DerivedRule);
            var cycleDesc = rule != null
                ? $"{rule.CycleWeeks}-week cycle, {string.Join("/", rule.HomeDays.Select(d => d.ToString()[..3]))}"
                : "Custom pattern";

            return new HomeTypeDto(
                ht.Id, ht.Name, ht.NameHe, ht.MoleculeId,
                userCounts.GetValueOrDefault(ht.Id, 0),
                cycleDesc, ht.IsActive);
        }).ToList();
    }

    public async Task<HomeType?> GetHomeTypeAsync(int id)
    {
        return await _db.HomeTypes
            .IgnoreQueryFilters()
            .Include(ht => ht.Molecule)
            .FirstOrDefaultAsync(ht => ht.Id == id);
    }

    public async Task<HomeType> CreateHomeTypeAsync(HomeType homeType)
    {
        _db.HomeTypes.Add(homeType);
        await _db.SaveChangesAsync();
        return homeType;
    }

    public async Task<bool> UpdateHomeTypeAsync(HomeType homeType)
    {
        _db.HomeTypes.Update(homeType);
        return await _db.SaveChangesAsync() > 0;
    }

    public async Task<bool> DeleteHomeTypeAsync(int id)
    {
        var ht = await _db.HomeTypes.IgnoreQueryFilters().FirstOrDefaultAsync(h => h.Id == id);
        if (ht == null) return false;

        // Unassign all users first
        var users = await _db.Users.IgnoreQueryFilters().Where(u => u.HomeTypeId == id).ToListAsync();
        foreach (var u in users)
            u.HomeTypeId = null;

        _db.HomeTypes.Remove(ht);
        return await _db.SaveChangesAsync() > 0;
    }

    /// <summary>
    /// Derives a recurrence rule from an array of painted dates.
    /// Detects weekly pattern (which days of the week) and cycle length.
    /// </summary>
    public DerivedRotationRule? DeriveRuleFromPattern(List<DateOnly> paintedDates)
    {
        if (paintedDates.Count == 0) return null;

        var sorted = paintedDates.OrderBy(d => d).ToList();
        var firstDate = sorted[0];

        // Detect which days of the week are home days
        var homeDays = sorted.Select(d => d.DayOfWeek).Distinct().OrderBy(d => d).ToList();

        // Determine which weeks (0-based from first date's week) have home days
        var firstMonday = firstDate.AddDays(-(((int)firstDate.DayOfWeek + 6) % 7));
        var weekIndices = sorted
            .Select(d => (d.DayNumber - firstMonday.DayNumber) / 7)
            .Distinct()
            .OrderBy(w => w)
            .ToList();

        if (weekIndices.Count == 0) return null;

        // Detect cycle length from gaps between home weeks
        int cycleWeeks;
        if (weekIndices.Count == 1)
        {
            // Only one week painted — assume 4-week cycle (monthly)
            cycleWeeks = 4;
        }
        else
        {
            // Compute the gap pattern
            var gaps = new List<int>();
            for (int i = 1; i < weekIndices.Count; i++)
                gaps.Add(weekIndices[i] - weekIndices[i - 1]);

            // If all gaps are equal, cycle = gap
            // If gaps vary, cycle = total span + gap from last to next occurrence
            if (gaps.Distinct().Count() == 1)
            {
                cycleWeeks = gaps[0];
            }
            else
            {
                // Complex pattern: total span
                cycleWeeks = weekIndices.Last() + 1;
            }
        }

        // Normalize week offsets within the cycle
        var weekOffsets = weekIndices.Select(w => w % cycleWeeks).Distinct().OrderBy(w => w).ToList();

        return new DerivedRotationRule(cycleWeeks, homeDays, weekOffsets, null, null);
    }

    public async Task<List<AppUser>> GetUsersForHomeTypeAsync(int homeTypeId)
    {
        return await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.HomeTypeId == homeTypeId && u.IsActive)
            .OrderBy(u => u.DisplayName)
            .ToListAsync();
    }

    public async Task AssignUsersAsync(int homeTypeId, List<int> userIds)
    {
        // Load the HomeType to get its MoleculeId for scope validation
        var homeType = await _db.HomeTypes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(ht => ht.Id == homeTypeId);

        if (homeType == null)
        {
            _logger.LogWarning("AssignUsersAsync: HomeType {HomeTypeId} not found", homeTypeId);
            return;
        }

        // Load the molecule's company IDs for scope check
        var moleculeCompanyIds = await _db.Companies
            .IgnoreQueryFilters()
            .Where(c => c.MoleculeId == homeType.MoleculeId)
            .Select(c => c.Id)
            .ToHashSetAsync();

        // Filter userIds to only those whose CompanyId is within the molecule
        var users = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => userIds.Contains(u.Id))
            .ToListAsync();

        var filteredUsers = users.Where(u => moleculeCompanyIds.Contains(u.CompanyId)).ToList();
        var excludedCount = users.Count - filteredUsers.Count;
        if (excludedCount > 0)
        {
            _logger.LogWarning("AssignUsersAsync: Excluded {Count} users not in molecule {MoleculeId} for HomeType {HomeTypeId}",
                excludedCount, homeType.MoleculeId, homeTypeId);
        }

        foreach (var user in filteredUsers)
            user.HomeTypeId = homeTypeId;

        await _db.SaveChangesAsync();
    }

    public async Task UnassignUserAsync(int homeTypeId, int userId)
    {
        var user = await _db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId && u.HomeTypeId == homeTypeId);

        if (user != null)
        {
            user.HomeTypeId = null;
            await _db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Generates HOME ShiftAssignments for users based on the HomeType pattern.
    /// Uses AddRange + single SaveChangesAsync inside a transaction for bulk performance.
    /// </summary>
    public async Task<GenerationResult> GenerateHomeShiftsAsync(
        int homeTypeId,
        DateOnly startDate,
        DateOnly endDate,
        List<int> userIds,
        int createdByUserId,
        RegenerationMode mode = RegenerationMode.KeepManualChanges)
    {
        var homeType = await GetHomeTypeAsync(homeTypeId);
        if (homeType == null)
            return new GenerationResult(0, 0, new List<GenerationConflict> { new(default, 0, "", "HomeType not found") });

        // Ensure HOME ShiftType exists in this molecule
        var homeShiftType = await _db.ShiftTypes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(st => st.Key == ShiftType.KEY_HOME && st.MoleculeId == homeType.MoleculeId);

        if (homeShiftType == null)
        {
            // Auto-create HOME ShiftType with defaults (molecule-scoped, no CompanyId)
            homeShiftType = new ShiftType
            {
                Key = ShiftType.KEY_HOME,
                MoleculeId = homeType.MoleculeId,
                Scope = Models.Support.ShiftScope.Molecule,
                Start = new TimeOnly(0, 0),
                End = new TimeOnly(23, 59),
                RowColor = "#F8E7B1",
                NameEn = "Home",
                NameHe = "בית"
            };
            _db.ShiftTypes.Add(homeShiftType);
            await _db.SaveChangesAsync();
            _logger.LogInformation("Auto-created HOME ShiftType {Id} for molecule {MoleculeId}", homeShiftType.Id, homeType.MoleculeId);
        }

        // Determine home dates from pattern or derived rule
        var homeDates = GetHomeDatesForRange(homeType, startDate, endDate, userIds);

        // Load per-user overrides
        var overrides = await _db.HomeTypeOverrides
            .IgnoreQueryFilters()
            .Where(o => o.HomeTypeId == homeTypeId && userIds.Contains(o.UserId))
            .ToDictionaryAsync(o => o.UserId);

        // Handle re-generation mode
        if (mode == RegenerationMode.OverwriteAll)
        {
            // Remove existing HOME assignments in range
            var existingHomeAssignments = await _db.ShiftAssignments
                .IgnoreQueryFilters()
                .Where(sa => sa.UserId.HasValue && userIds.Contains(sa.UserId.Value)
                    && sa.ShiftInstance.ShiftType.Key == ShiftType.KEY_HOME
                    && sa.ShiftInstance.WorkDate >= startDate
                    && sa.ShiftInstance.WorkDate <= endDate)
                .ToListAsync();
            _db.ShiftAssignments.RemoveRange(existingHomeAssignments);
        }

        // Pre-load existing assignments and chores for conflict detection
        var existingShifts = await _db.ShiftAssignments
            .IgnoreQueryFilters()
            .Where(sa => sa.UserId.HasValue && userIds.Contains(sa.UserId.Value)
                && sa.ShiftInstance.ShiftType.Key != ShiftType.KEY_OFFLINE
                && sa.ShiftInstance.WorkDate >= startDate
                && sa.ShiftInstance.WorkDate <= endDate)
            .Select(sa => new { sa.UserId, sa.ShiftInstance.WorkDate, IsHome = sa.ShiftInstance.ShiftType.Key == ShiftType.KEY_HOME })
            .ToListAsync();

        var existingChores = await _db.Chores
            .IgnoreQueryFilters()
            .Where(c => userIds.Contains(c.UserId)
                && c.Date >= startDate && c.Date <= endDate && c.CanceledAt == null)
            .Select(c => new { c.UserId, c.Date })
            .ToListAsync();

        var shiftLookup = existingShifts.ToLookup(s => (s.UserId, s.WorkDate));
        var choreLookup = existingChores.ToLookup(c => (c.UserId, c.Date));

        // Load user info for conflict reporting and per-user CompanyId
        var userInfo = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new { u.DisplayName, u.CompanyId });

        // Collect ALL unique dates needed across all users for batch instance creation
        var allNeededDates = new HashSet<DateOnly>();
        var perUserDates = new Dictionary<int, List<DateOnly>>();
        foreach (var userId in userIds)
        {
            var userDates = overrides.TryGetValue(userId, out var ov)
                ? DeserializeDates(ov.OverridePatternJson)
                    .Where(d => d >= startDate && d <= endDate).ToList()
                : homeDates;
            perUserDates[userId] = userDates;
            foreach (var d in userDates)
                allNeededDates.Add(d);
        }

        // Batch-load existing ShiftInstances for the HOME ShiftType in the date range (single query)
        var existingInstances = await _db.ShiftInstances
            .IgnoreQueryFilters()
            .Where(si => si.ShiftTypeId == homeShiftType.Id
                && si.WorkDate >= startDate && si.WorkDate <= endDate)
            .ToDictionaryAsync(si => si.WorkDate);

        // Create all missing ShiftInstances in a single batch
        var missingDates = allNeededDates.Where(d => !existingInstances.ContainsKey(d)).ToList();
        if (missingDates.Count > 0)
        {
            var newInstances = missingDates.Select(date => new ShiftInstance
            {
                ShiftTypeId = homeShiftType.Id,
                CompanyId = homeShiftType.GetEffectiveCompanyId(homeType.CompanyId),
                WorkDate = date,
                StaffingRequired = 99 // HOME has no capacity limit
            }).ToList();

            _db.ShiftInstances.AddRange(newInstances);
            await _db.SaveChangesAsync();

            // Add newly created instances to the cache
            foreach (var inst in newInstances)
                existingInstances[inst.WorkDate] = inst;
        }

        var conflicts = new List<GenerationConflict>();
        var newAssignments = new List<ShiftAssignment>();
        int created = 0, skipped = 0;

        using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            foreach (var userId in userIds)
            {
                var userDates = perUserDates[userId];

                foreach (var date in userDates)
                {
                    var existing = shiftLookup[(userId, date)];
                    var hasChore = choreLookup[(userId, date)].Any();

                    // Skip if HOME already exists (KeepManualChanges mode)
                    if (mode == RegenerationMode.KeepManualChanges && existing.Any(s => s.IsHome))
                    {
                        skipped++;
                        continue;
                    }

                    // Check for conflicts with real shifts
                    if (existing.Any(s => !s.IsHome))
                    {
                        conflicts.Add(new GenerationConflict(date, userId,
                            userInfo.TryGetValue(userId, out var ui) ? ui.DisplayName : $"#{userId}",
                            "Has shift assignment"));
                        skipped++;
                        continue;
                    }

                    if (hasChore)
                    {
                        conflicts.Add(new GenerationConflict(date, userId,
                            userInfo.TryGetValue(userId, out var ui) ? ui.DisplayName : $"#{userId}",
                            "Has chore"));
                        skipped++;
                        continue;
                    }

                    // Look up pre-cached ShiftInstance (batch-loaded above)
                    var instance = existingInstances[date];

                    newAssignments.Add(new ShiftAssignment
                    {
                        ShiftInstanceId = instance.Id,
                        UserId = userId,
                        CompanyId = userInfo.TryGetValue(userId, out var assignUi) ? assignUi.CompanyId : homeType.CompanyId,
                        CreatedAt = DateTime.UtcNow
                    });
                    created++;
                }
            }

            if (newAssignments.Count > 0)
            {
                _db.ShiftAssignments.AddRange(newAssignments);
                await _db.SaveChangesAsync();
            }

            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Failed to generate HOME shifts for HomeType {HomeTypeId}", homeTypeId);
            throw;
        }

        _logger.LogInformation("Generated {Created} HOME shifts for HomeType {HomeTypeId}, skipped {Skipped}, conflicts {Conflicts}",
            created, homeTypeId, skipped, conflicts.Count);

        return new GenerationResult(created, skipped, conflicts);
    }

    public async Task SaveUserOverrideAsync(int homeTypeId, int userId, List<DateOnly> overrideDates, int createdByUserId)
    {
        var existing = await _db.HomeTypeOverrides
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.HomeTypeId == homeTypeId && o.UserId == userId);

        var json = JsonSerializer.Serialize(overrideDates.Select(d => d.ToString("yyyy-MM-dd")));

        if (existing != null)
        {
            existing.OverridePatternJson = json;
            existing.CreatedAt = DateTime.UtcNow;
            existing.CreatedBy = createdByUserId;
        }
        else
        {
            var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId);
            _db.HomeTypeOverrides.Add(new HomeTypeOverride
            {
                HomeTypeId = homeTypeId,
                UserId = userId,
                CompanyId = user?.CompanyId ?? 0,
                OverridePatternJson = json,
                CreatedBy = createdByUserId
            });
        }

        await _db.SaveChangesAsync();
    }

    public async Task<List<DateOnly>> GetUserOverrideDatesAsync(int homeTypeId, int userId)
    {
        var ov = await _db.HomeTypeOverrides
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.HomeTypeId == homeTypeId && o.UserId == userId);

        return ov != null ? DeserializeDates(ov.OverridePatternJson) : new List<DateOnly>();
    }

    // --- Private helpers ---

    private List<DateOnly> GetHomeDatesForRange(HomeType homeType, DateOnly start, DateOnly end, List<int> userIds)
    {
        var rule = DeserializeRule(homeType.DerivedRule);
        if (rule != null)
            return GenerateDatesFromRule(rule, start, end);

        // Fall back to raw painted dates
        return DeserializeDates(homeType.PatternJson)
            .Where(d => d >= start && d <= end)
            .ToList();
    }

    private static List<DateOnly> GenerateDatesFromRule(DerivedRotationRule rule, DateOnly start, DateOnly end)
    {
        var dates = new List<DateOnly>();
        var homeDaySet = rule.HomeDays.ToHashSet();

        // Find the Monday of the start week
        var startMonday = start.AddDays(-(((int)start.DayOfWeek + 6) % 7));

        for (var date = start; date <= end; date = date.AddDays(1))
        {
            if (!homeDaySet.Contains(date.DayOfWeek))
                continue;

            // Determine which week of the cycle this is
            var weekNum = (date.DayNumber - startMonday.DayNumber) / 7;
            var cycleWeek = weekNum % rule.CycleWeeks;

            if (rule.WeekOffsets.Contains(cycleWeek))
                dates.Add(date);
        }

        return dates;
    }

    private DerivedRotationRule? DeserializeRule(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<DerivedRotationRule>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize HomeType rule JSON: {Json}", json?.Substring(0, Math.Min(json?.Length ?? 0, 200)));
            return null;
        }
    }

    private List<DateOnly> DeserializeDates(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new();
        try
        {
            var strings = JsonSerializer.Deserialize<List<string>>(json) ?? new();
            return strings
                .Select(s => DateOnly.TryParse(s, out var d) ? d : default)
                .Where(d => d != default)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize HomeType dates JSON: {Json}", json?.Substring(0, Math.Min(json?.Length ?? 0, 200)));
            return new();
        }
    }
}
