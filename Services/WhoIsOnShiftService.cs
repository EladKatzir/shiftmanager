using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

public class WhoIsOnShiftService : IWhoIsOnShiftService
{
    private readonly AppDbContext _db;
    private readonly IGrantService _grantService;

    public WhoIsOnShiftService(AppDbContext db, IGrantService grantService)
    {
        _db = db;
        _grantService = grantService;
    }

    /// <inheritdoc />
    public async Task<List<MonitorableShiftType>> GetMonitorableShiftTypesAsync(int userId)
    {
        // Resolve the molecule ids the user has ViewShifts access to — same scope helper the shift calendar uses.
        var accessibleMoleculeIds = await _grantService.GetAccessibleMoleculeIdsForGrantAsync(userId, "ViewShifts");

        // Also resolve the area ids reachable from those molecules (for area-scoped shift types).
        // SECURITY-AUDITED: scoped by accessibleMoleculeIds which are themselves grant-gated;
        // IgnoreQueryFilters required because ShiftType is not IBelongsToCompany.
        List<int> accessibleAreaIds = new();
        if (accessibleMoleculeIds.Count > 0)
        {
            accessibleAreaIds = await _db.Molecules
                .IgnoreQueryFilters()
                .Where(m => accessibleMoleculeIds.Contains(m.Id))
                .Select(m => m.AreaId)
                .Distinct()
                .ToListAsync();
        }

        // SECURITY-AUDITED: filtered to shift types whose MoleculeId is in the grant-resolved
        // accessible molecule set OR whose AreaId is in the derived accessible area set.
        // IgnoreQueryFilters required because ShiftType has no global tenant query filter.
        var shiftTypes = await _db.ShiftTypes
            .IgnoreQueryFilters()
            .Where(st =>
                (st.MoleculeId != null && accessibleMoleculeIds.Contains(st.MoleculeId.Value)) ||
                (st.AreaId != null && accessibleAreaIds.Contains(st.AreaId.Value)))
            .Select(st => new { st.Id, st.NameEn, st.Key })
            .ToListAsync();

        return shiftTypes
            .Select(st => new MonitorableShiftType(
                st.Id,
                // Mirror ShiftType.Name computed property: NameEn first, then key-based fallback
                !string.IsNullOrWhiteSpace(st.NameEn) ? st.NameEn : FormatShiftName(st.Key),
                st.Key))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<List<int>> GetSelectedShiftIdsAsync(int userId)
    {
        return await _db.UserMonitoredShifts
            .Where(m => m.UserId == userId)
            .Select(m => m.ShiftTypeId)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task SaveSelectedShiftsAsync(int userId, IReadOnlyList<int> shiftTypeIds)
    {
        // Whole-set replace — same pattern as CalendarRowOrderService.SaveOrderAsync.
        await _db.UserMonitoredShifts
            .Where(m => m.UserId == userId)
            .ExecuteDeleteAsync();

        foreach (var shiftTypeId in shiftTypeIds)
        {
            _db.UserMonitoredShifts.Add(new UserMonitoredShift
            {
                UserId = userId,
                ShiftTypeId = shiftTypeId
            });
        }

        await _db.SaveChangesAsync();
    }

    /// <inheritdoc />
    public async Task<List<ShiftCube>> BuildWhoIsOnShiftAsync(int userId)
    {
        var selectedIds = await GetSelectedShiftIdsAsync(userId);
        if (selectedIds.Count == 0)
            return new List<ShiftCube>();

        var today = DateOnly.FromDateTime(DateTime.Today);
        var now = TimeOnly.FromDateTime(DateTime.Now);

        // SECURITY-AUDITED: scoped to shift types explicitly selected by the authenticated user;
        // instances and assignments are bounded to those shift type ids + today's date only.
        // IgnoreQueryFilters required because ShiftInstance/ShiftAssignment are tenant-scoped
        // and the dashboard owner may be an area admin viewing across companies in their area.

        // Load today's instances for the selected shift types.
        var instances = await _db.ShiftInstances
            .IgnoreQueryFilters()
            .Where(si => selectedIds.Contains(si.ShiftTypeId) && si.WorkDate == today)
            .Select(si => new { si.Id, si.ShiftTypeId, si.StaffingRequired })
            .ToListAsync();

        var instanceIds = instances.Select(i => i.Id).ToList();

        // Load assignments for those instances (with assigned user and trainee names).
        var assignments = await _db.ShiftAssignments
            .IgnoreQueryFilters()
            .Where(a => instanceIds.Contains(a.ShiftInstanceId))
            .Select(a => new
            {
                a.ShiftInstanceId,
                AssignedUserId = a.UserId,
                AssignedUserName = a.User != null ? a.User.DisplayName : null,
                TraineeUserId = a.TraineeUserId,
                a.IsTraineeShift
            })
            .ToListAsync();

        // Load trainee display names in a second pass (join via TraineeUserId).
        var traineeUserIds = assignments
            .Where(a => a.TraineeUserId.HasValue)
            .Select(a => a.TraineeUserId!.Value)
            .Distinct()
            .ToList();

        Dictionary<int, string> traineeNames = new();
        if (traineeUserIds.Count > 0)
        {
            traineeNames = await _db.Users
                .IgnoreQueryFilters()
                .Where(u => traineeUserIds.Contains(u.Id))
                .Select(u => new { u.Id, u.DisplayName })
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName);
        }

        // Load shift type metadata (name + times) for all selected ids.
        var shiftTypeMeta = await _db.ShiftTypes
            .IgnoreQueryFilters()
            .Where(st => selectedIds.Contains(st.Id))
            .Select(st => new { st.Id, st.NameEn, st.Key, st.Start, st.End })
            .ToListAsync();

        var shiftTypeDict = shiftTypeMeta.ToDictionary(st => st.Id);

        // Build one cube per selected shift type.
        var cubes = new List<ShiftCube>();
        foreach (var shiftTypeId in selectedIds)
        {
            if (!shiftTypeDict.TryGetValue(shiftTypeId, out var meta))
                continue; // shift type no longer exists — skip gracefully

            var shiftName = !string.IsNullOrWhiteSpace(meta.NameEn)
                ? meta.NameEn
                : FormatShiftName(meta.Key);

            var instance = instances.FirstOrDefault(i => i.ShiftTypeId == shiftTypeId);

            // IsLiveNow: handle overnight shifts where End < Start.
            // Normal (e.g. 06:00–14:00): live if Start <= now <= End.
            // Overnight (e.g. 22:00–06:00): live if now >= Start OR now <= End.
            bool isLiveNow;
            if (meta.End > meta.Start)
            {
                // Normal shift: now must be within [Start, End]
                isLiveNow = now >= meta.Start && now <= meta.End;
            }
            else
            {
                // Overnight shift: live if now is in [Start, midnight) OR [midnight, End]
                isLiveNow = now >= meta.Start || now <= meta.End;
            }

            if (instance == null)
            {
                // No instance today — show empty cube so admin still sees the monitored shift.
                cubes.Add(new ShiftCube(
                    shiftTypeId,
                    shiftName,
                    AssignedCount: 0,
                    StaffingRequired: 0,
                    isLiveNow,
                    Users: new List<ShiftCubeUser>()));
                continue;
            }

            // Build the user list for this instance.
            var instanceAssignments = assignments.Where(a => a.ShiftInstanceId == instance.Id).ToList();
            var users = new List<ShiftCubeUser>();

            foreach (var a in instanceAssignments)
            {
                // Primary assigned user (skip if null/unassigned slot).
                if (a.AssignedUserId.HasValue && !string.IsNullOrEmpty(a.AssignedUserName))
                {
                    users.Add(new ShiftCubeUser(a.AssignedUserName, a.IsTraineeShift));
                }

                // Trainee shadowing this assignment.
                if (a.TraineeUserId.HasValue && traineeNames.TryGetValue(a.TraineeUserId.Value, out var traineeName))
                {
                    users.Add(new ShiftCubeUser(traineeName, IsTrainee: true));
                }
            }

            cubes.Add(new ShiftCube(
                shiftTypeId,
                shiftName,
                AssignedCount: users.Count,
                StaffingRequired: instance.StaffingRequired,
                isLiveNow,
                users));
        }

        return cubes;
    }

    /// <summary>
    /// Mirrors ShiftType.Name computed property fallback for key-based display names.
    /// Used when NameEn is null/empty (molecule/area shifts without a custom English name).
    /// </summary>
    private static string FormatShiftName(string key) => key switch
    {
        ShiftType.KEY_MORNING => "Morning Shift",
        ShiftType.KEY_NOON => "Afternoon Shift",
        ShiftType.KEY_AFTERNOON => "Afternoon Shift",
        ShiftType.KEY_NIGHT => "Night Shift",
        ShiftType.KEY_MIDDLE => "Mid Shift",
        ShiftType.KEY_EVENING => "Evening Shift",
        ShiftType.KEY_OFFLINE => "Offline",
        ShiftType.KEY_HOME => "Home",
        _ => System.Globalization.CultureInfo.CurrentCulture.TextInfo
                .ToTitleCase((key.StartsWith("CUSTOM_", StringComparison.OrdinalIgnoreCase)
                    ? key[7..] : key).Replace('_', ' ').ToLower())
    };
}
