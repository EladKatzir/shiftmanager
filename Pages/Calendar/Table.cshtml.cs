using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using System.Text.Json;

namespace ShiftManager.Pages.Calendar;

[Authorize(Policy = "IsManagerOrAdmin")]
public class TableModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ICompanyContext _companyContext;
    private readonly ILogger<TableModel> _logger;
    private readonly IBusyUserService _busyUserService;
    private readonly IShiftTypeCacheService _shiftTypeCache;
    private readonly IShiftProgramService _programService;

    public TableModel(
        AppDbContext db,
        ICompanyContext companyContext,
        ILogger<TableModel> logger,
        IBusyUserService busyUserService,
        IShiftTypeCacheService shiftTypeCache,
        IShiftProgramService programService)
    {
        _db = db;
        _companyContext = companyContext;
        _logger = logger;
        _busyUserService = busyUserService;
        _shiftTypeCache = shiftTypeCache;
        _programService = programService;
    }

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateOnly PreviousWeekStart { get; set; }
    public DateOnly NextWeekStart { get; set; }
    public List<DateOnly> Dates { get; set; } = new();
    public List<ShiftType> ShiftTypes { get; set; } = new();
    public List<AppUser> Employees { get; set; } = new();
    public string ViewMode { get; set; } = "week"; // week, 2weeks, month

    // Map: [ShiftTypeId][Date] => List of assignments
    public Dictionary<int, Dictionary<DateOnly, List<AssignmentInfo>>> AssignmentGrid { get; set; } = new();

    // Busy user status per date: [Date][UserId] => BusyStatus
    public Dictionary<DateOnly, Dictionary<int, BusyStatus>> BusyUsersByDate { get; set; } = new();

    public class AssignmentInfo
    {
        public int AssignmentId { get; set; }
        public int ShiftInstanceId { get; set; }
        public int? UserId { get; set; }
        public string? EmployeeName { get; set; }
        public int? TraineeUserId { get; set; }
        public string? TraineeName { get; set; }
        public bool IsDetached { get; set; }
        public int? OriginalProgramId { get; set; }
        public string? OverriddenFields { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(string? start, string? view)
    {
        var companyId = _companyContext.GetCompanyIdOrThrow();

        // Set view mode (week, 2weeks, month)
        ViewMode = view?.ToLower() ?? "week";
        if (ViewMode != "week" && ViewMode != "2weeks" && ViewMode != "month")
        {
            ViewMode = "week"; // Default fallback
        }

        // Default to current week if no start date provided
        // ✅ A-019: Safe date parsing with fallback to today on invalid input
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (string.IsNullOrEmpty(start))
        {
            StartDate = today.AddDays(-(int)today.DayOfWeek); // Start of week (Sunday)
        }
        else if (DateOnly.TryParse(start, out var parsedDate))
        {
            // Validate parsed date is within reasonable bounds
            if (parsedDate.Year >= 1900 && parsedDate.Year <= 2100)
            {
                StartDate = parsedDate;
            }
            else
            {
                _logger.LogWarning("Invalid start date year: {Start}, defaulting to current week", start);
                StartDate = today.AddDays(-(int)today.DayOfWeek);
            }
        }
        else
        {
            _logger.LogWarning("Invalid start date format: {Start}, defaulting to current week", start);
            StartDate = today.AddDays(-(int)today.DayOfWeek);
        }

        // Calculate end date based on view mode
        EndDate = ViewMode switch
        {
            "2weeks" => StartDate.AddDays(13), // 14 days
            "month" => StartDate.AddDays(DateTime.DaysInMonth(StartDate.Year, StartDate.Month) - 1),
            _ => StartDate.AddDays(6) // Default: 7 days
        };

        // Calculate previous and next navigation dates
        var daysToMove = ViewMode switch
        {
            "2weeks" => 14,
            "month" => DateTime.DaysInMonth(StartDate.Year, StartDate.Month),
            _ => 7
        };
        PreviousWeekStart = StartDate.AddDays(-daysToMove);
        NextWeekStart = StartDate.AddDays(daysToMove);

        // Generate date range
        for (var date = StartDate; date <= EndDate; date = date.AddDays(1))
        {
            Dates.Add(date);
        }

        // Load shift instances for date range FIRST to determine which shift types to show
        var instances = await _db.ShiftInstances
            .Where(si => si.WorkDate >= StartDate && si.WorkDate <= EndDate)
            .ToListAsync();

        // Get shift type IDs that have instances in this date range
        var shiftTypeIdsWithInstances = instances.Select(si => si.ShiftTypeId).Distinct().ToHashSet();

        // Get shift type IDs from active programs
        var activePrograms = await _programService.GetCompanyProgramsAsync(companyId, includeInactive: false);
        var shiftTypeIdsFromPrograms = activePrograms.Select(p => p.ShiftTypeId).Distinct().ToHashSet();

        // Combine: show shift types that have instances OR are in active programs
        var relevantShiftTypeIds = shiftTypeIdsWithInstances.Union(shiftTypeIdsFromPrograms).ToHashSet();

        // Load only relevant shift types
        var allShiftTypes = await _shiftTypeCache.GetShiftTypesAsync(companyId);
        ShiftTypes = allShiftTypes
            .Where(st => relevantShiftTypeIds.Contains(st.Id))
            .OrderBy(st => st.IsOffline ? 1 : 0) // Offline last
            .ThenBy(st => st.Start) // Then by start time (chronological)
            .ThenBy(st => st.CustomName ?? st.Name) // Then by name for same start time
            .ToList();

        _logger.LogInformation("Loaded {Count} shift types for company", ShiftTypes.Count);

        if (!ShiftTypes.Any())
        {
            _logger.LogWarning("No shift types found - calendar will be empty");
        }

        // Load active employees for this company
        Employees = await _db.Users
            .Where(u => u.IsActive)
            .OrderBy(u => u.DisplayName)
            .ToListAsync();

        // Load busy user status per date (not aggregated)
        // This will show if users are busy on each specific date
        foreach (var date in Dates)
        {
            var busyForDate = await _busyUserService.GetBusyUsersAsync(date, TimeOnly.MinValue, TimeOnly.MaxValue);
            BusyUsersByDate[date] = busyForDate;
        }

        // instances already loaded above - Load all assignments for these instances (including trainee information)
        var instanceIds = instances.Select(i => i.Id).ToList();
        var assignments = await _db.ShiftAssignments
            .Include(a => a.User)
            .Include(a => a.Trainee)
            .Where(a => instanceIds.Contains(a.ShiftInstanceId))
            .ToListAsync();

        // Build grid structure
        foreach (var shiftType in ShiftTypes)
        {
            AssignmentGrid[shiftType.Id] = new Dictionary<DateOnly, List<AssignmentInfo>>();

            foreach (var date in Dates)
            {
                var instance = instances.FirstOrDefault(i => i.ShiftTypeId == shiftType.Id && i.WorkDate == date);

                if (instance != null)
                {
                    var instanceAssignments = assignments
                        .Where(a => a.ShiftInstanceId == instance.Id)
                        .Select(a => new AssignmentInfo
                        {
                            AssignmentId = a.Id,
                            ShiftInstanceId = instance.Id,
                            UserId = a.UserId,
                            EmployeeName = a.User?.DisplayName,
                            TraineeUserId = a.TraineeUserId,
                            TraineeName = a.Trainee?.DisplayName,
                            IsDetached = instance.IsDetached,
                            OriginalProgramId = instance.OriginalProgramId,
                            OverriddenFields = instance.OverriddenFields
                        })
                        .ToList();

                    AssignmentGrid[shiftType.Id][date] = instanceAssignments;
                }
                else
                {
                    AssignmentGrid[shiftType.Id][date] = new List<AssignmentInfo>();
                }
            }
        }

        return Page();
    }

    public async Task<IActionResult> OnPostEnsureShiftInstanceAsync([FromBody] EnsureShiftInstanceRequest request)
    {
        try
        {
            var companyId = _companyContext.GetCompanyIdOrThrow();

            // Get or create instance (idempotent)
            var instance = await _db.ShiftInstances
                .FirstOrDefaultAsync(si => si.CompanyId == companyId &&
                                          si.ShiftTypeId == request.ShiftTypeId &&
                                          si.WorkDate == request.Date);

            bool isNew = instance == null;

            if (instance == null)
            {
                var shiftType = await _db.ShiftTypes.FindAsync(request.ShiftTypeId);
                if (shiftType == null)
                {
                    return new JsonResult(new { success = false, error = "Shift type not found" });
                }

                // Create new instance
                instance = new ShiftInstance
                {
                    CompanyId = companyId,
                    ShiftTypeId = request.ShiftTypeId,
                    WorkDate = request.Date,
                    StaffingRequired = request.StaffingRequired,
                    Concurrency = 0
                };
                _db.ShiftInstances.Add(instance);
                await _db.SaveChangesAsync();

                // Create empty assignment slots
                for (int i = 0; i < request.StaffingRequired; i++)
                {
                    var assignment = new ShiftAssignment
                    {
                        CompanyId = companyId,
                        ShiftInstanceId = instance.Id,
                        UserId = null // Unassigned slot
                    };
                    _db.ShiftAssignments.Add(assignment);
                }
                await _db.SaveChangesAsync();
            }
            else
            {
                // Instance exists - check if we need to adjust staffing
                var currentSlotCount = await _db.ShiftAssignments
                    .CountAsync(a => a.ShiftInstanceId == instance.Id);

                if (request.StaffingRequired > currentSlotCount)
                {
                    // Add more slots
                    for (int i = currentSlotCount; i < request.StaffingRequired; i++)
                    {
                        var assignment = new ShiftAssignment
                        {
                            CompanyId = companyId,
                            ShiftInstanceId = instance.Id,
                            UserId = null
                        };
                        _db.ShiftAssignments.Add(assignment);
                    }
                    instance.StaffingRequired = request.StaffingRequired;
                    await _db.SaveChangesAsync();
                }
            }

            return new JsonResult(new
            {
                success = true,
                instanceId = instance.Id,
                staffingRequired = instance.StaffingRequired,
                isNew = isNew
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ensuring shift instance");
            return new JsonResult(new { success = false, error = "Failed to create/retrieve shift instance" });
        }
    }

    public async Task<IActionResult> OnPostCreateShiftInstanceAsync([FromBody] CreateShiftInstanceRequest request)
    {
        try
        {
            var companyId = _companyContext.GetCompanyIdOrThrow();

            // Check if instance already exists
            var existingInstance = await _db.ShiftInstances
                .FirstOrDefaultAsync(si => si.ShiftTypeId == request.ShiftTypeId && si.WorkDate == request.Date);

            if (existingInstance != null)
            {
                return new JsonResult(new { success = false, error = "Shift instance already exists for this date" });
            }

            var shiftType = await _db.ShiftTypes.FindAsync(request.ShiftTypeId);
            if (shiftType == null)
            {
                return new JsonResult(new { success = false, error = "Shift type not found" });
            }

            // Create shift instance with staffing requirement
            var instance = new ShiftInstance
            {
                CompanyId = companyId,
                ShiftTypeId = request.ShiftTypeId,
                WorkDate = request.Date,
                StaffingRequired = request.StaffingRequired,
                Concurrency = 0
            };
            _db.ShiftInstances.Add(instance);
            await _db.SaveChangesAsync();

            // Create empty assignment slots
            for (int i = 0; i < request.StaffingRequired; i++)
            {
                var assignment = new ShiftAssignment
                {
                    CompanyId = companyId,
                    ShiftInstanceId = instance.Id,
                    UserId = null // Unassigned slot
                };
                _db.ShiftAssignments.Add(assignment);
            }
            await _db.SaveChangesAsync();

            return new JsonResult(new
            {
                success = true,
                instanceId = instance.Id,
                staffingRequired = instance.StaffingRequired
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating shift instance");
            return new JsonResult(new { success = false, error = "Failed to create shift instance" });
        }
    }

    public async Task<IActionResult> OnPostAssignUserToSlotAsync([FromBody] AssignUserToSlotRequest request)
    {
        try
        {
            var assignment = await _db.ShiftAssignments
                .Include(a => a.ShiftInstance)
                .ThenInclude(si => si.ShiftType)
                .FirstOrDefaultAsync(a => a.Id == request.AssignmentId);

            if (assignment == null)
            {
                return new JsonResult(new { success = false, error = "Assignment not found" });
            }

            // Check if user is already assigned to this shift instance
            var existingAssignment = await _db.ShiftAssignments
                .FirstOrDefaultAsync(a => a.ShiftInstanceId == assignment.ShiftInstanceId && a.UserId == request.UserId);

            if (existingAssignment != null && existingAssignment.Id != request.AssignmentId)
            {
                return new JsonResult(new { success = false, error = "Employee already assigned to this shift" });
            }

            // Overlap detection: Check if user has conflicting shifts on the same date
            var shiftDate = assignment.ShiftInstance.WorkDate;
            var shiftStart = assignment.ShiftInstance.ShiftType.Start;
            var shiftEnd = assignment.ShiftInstance.ShiftType.End;

            var overlappingShifts = await _db.ShiftAssignments
                .Include(a => a.ShiftInstance)
                .ThenInclude(si => si.ShiftType)
                .Where(a => a.UserId == request.UserId &&
                           a.ShiftInstance.WorkDate == shiftDate &&
                           a.Id != request.AssignmentId)
                .ToListAsync();

            foreach (var existing in overlappingShifts)
            {
                var existingStart = existing.ShiftInstance.ShiftType.Start;
                var existingEnd = existing.ShiftInstance.ShiftType.End;

                // Check for time overlap
                bool overlaps = false;

                // Handle overnight shifts
                if (shiftEnd < shiftStart) // Current shift is overnight
                {
                    if (existingEnd < existingStart) // Existing shift is also overnight
                    {
                        overlaps = true; // Both overnight shifts on same date = overlap
                    }
                    else // Existing shift is same-day
                    {
                        // Overnight shift overlaps if existing shift starts before midnight
                        overlaps = existingStart >= shiftStart || existingEnd <= shiftEnd;
                    }
                }
                else if (existingEnd < existingStart) // Existing shift is overnight
                {
                    overlaps = shiftStart >= existingStart || shiftEnd <= existingEnd;
                }
                else // Both shifts are same-day
                {
                    overlaps = (shiftStart < existingEnd && shiftEnd > existingStart);
                }

                if (overlaps)
                {
                    return new JsonResult(new
                    {
                        success = false,
                        error = $"Employee has an overlapping shift: {existing.ShiftInstance.ShiftType.Name} ({existingStart:HH:mm} - {existingEnd:HH:mm})"
                    });
                }
            }

            // Assign user to slot
            assignment.UserId = request.UserId;
            await _db.SaveChangesAsync();

            var user = await _db.Users.FindAsync(request.UserId);

            return new JsonResult(new
            {
                success = true,
                employeeName = user?.DisplayName
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning user to slot");
            return new JsonResult(new { success = false, error = "Failed to assign user to slot" });
        }
    }

    public async Task<IActionResult> OnPostAssignEmployeeAsync([FromBody] AssignEmployeeRequest request)
    {
        try
        {
            var companyId = _companyContext.GetCompanyIdOrThrow();

            // Get or create shift instance
            var instance = await _db.ShiftInstances
                .FirstOrDefaultAsync(si => si.ShiftTypeId == request.ShiftTypeId && si.WorkDate == request.Date);

            if (instance == null)
            {
                var shiftType = await _db.ShiftTypes.FindAsync(request.ShiftTypeId);
                if (shiftType == null)
                {
                    return new JsonResult(new { success = false, error = "Shift type not found" });
                }

                instance = new ShiftInstance
                {
                    CompanyId = companyId,
                    ShiftTypeId = request.ShiftTypeId,
                    WorkDate = request.Date,
                    StaffingRequired = 1, // Default to 1 employee required
                    Concurrency = 0
                };
                _db.ShiftInstances.Add(instance);
                await _db.SaveChangesAsync();
            }

            // Check if assignment already exists for this user
            var existingAssignment = await _db.ShiftAssignments
                .FirstOrDefaultAsync(a => a.ShiftInstanceId == instance.Id && a.UserId == request.UserId);

            if (existingAssignment != null)
            {
                return new JsonResult(new { success = false, error = "Employee already assigned to this shift" });
            }

            // Check if we've reached the staffing limit
            var assignmentCount = await _db.ShiftAssignments
                .CountAsync(a => a.ShiftInstanceId == instance.Id);

            if (assignmentCount >= instance.StaffingRequired)
            {
                return new JsonResult(new { success = false, error = "Shift is fully staffed" });
            }

            // Create new assignment
            var assignment = new ShiftAssignment
            {
                CompanyId = companyId,
                ShiftInstanceId = instance.Id,
                UserId = request.UserId
            };

            _db.ShiftAssignments.Add(assignment);
            await _db.SaveChangesAsync();

            var user = await _db.Users.FindAsync(request.UserId);

            return new JsonResult(new
            {
                success = true,
                assignmentId = assignment.Id,
                employeeName = user?.DisplayName
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning employee");
            return new JsonResult(new { success = false, error = "Failed to assign employee" });
        }
    }

    public async Task<IActionResult> OnPostUnassignEmployeeAsync([FromBody] UnassignEmployeeRequest request)
    {
        try
        {
            var assignment = await _db.ShiftAssignments.FindAsync(request.AssignmentId);
            if (assignment == null)
            {
                return new JsonResult(new { success = false, error = "Assignment not found" });
            }

            _db.ShiftAssignments.Remove(assignment);
            await _db.SaveChangesAsync();

            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unassigning employee");
            return new JsonResult(new { success = false, error = "Failed to unassign employee" });
        }
    }

    public async Task<IActionResult> OnPostClearAssignmentAsync([FromBody] ClearAssignmentRequest request)
    {
        try
        {
            var assignment = await _db.ShiftAssignments.FindAsync(request.AssignmentId);
            if (assignment == null)
            {
                return new JsonResult(new { success = false, error = "Assignment not found" });
            }

            // Clear user and trainee without deleting the assignment slot
            assignment.UserId = null;
            assignment.TraineeUserId = null;
            await _db.SaveChangesAsync();

            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing assignment");
            return new JsonResult(new { success = false, error = "Failed to clear assignment" });
        }
    }

    public async Task<IActionResult> OnPostUpdateShiftStaffingAsync([FromBody] UpdateShiftStaffingRequest request)
    {
        try
        {
            var companyId = _companyContext.GetCompanyIdOrThrow();

            var instance = await _db.ShiftInstances.FindAsync(request.ShiftInstanceId);
            if (instance == null)
            {
                return new JsonResult(new { success = false, error = "Shift instance not found" });
            }

            // If this instance is from a Program and not yet detached, detach it now
            if (instance.OriginalProgramId.HasValue && !instance.IsDetached)
            {
                await _programService.DetachInstanceAsync(instance.Id, "Manual staffing adjustment");
                _logger.LogInformation(
                    "Auto-detached ShiftInstance {InstanceId} from Program {ProgramId} due to manual staffing change",
                    instance.Id, instance.OriginalProgramId);
            }

            var currentStaffing = await _db.ShiftAssignments
                .CountAsync(a => a.ShiftInstanceId == instance.Id);

            var difference = request.StaffingRequired - currentStaffing;

            if (difference > 0)
            {
                // Add empty assignment slots
                for (int i = 0; i < difference; i++)
                {
                    var assignment = new ShiftAssignment
                    {
                        CompanyId = companyId,
                        ShiftInstanceId = instance.Id,
                        UserId = null // Unassigned slot
                    };
                    _db.ShiftAssignments.Add(assignment);
                }
            }
            else if (difference < 0)
            {
                // Check how many assignments have users assigned
                var assignedCount = await _db.ShiftAssignments
                    .CountAsync(a => a.ShiftInstanceId == instance.Id && a.UserId != null);

                var unassignedCount = currentStaffing - assignedCount;

                // If decreasing would require removing assigned users, require confirmation
                if (Math.Abs(difference) > unassignedCount && !request.ForceRemoval)
                {
                    var affectedCount = Math.Abs(difference) - unassignedCount;
                    return new JsonResult(new
                    {
                        success = false,
                        requiresConfirmation = true,
                        affectedAssignments = affectedCount,
                        message = $"This will remove {affectedCount} assigned employee(s). Do you want to continue?"
                    });
                }

                // Remove unassigned slots first
                var unassignedToRemove = await _db.ShiftAssignments
                    .Where(a => a.ShiftInstanceId == instance.Id && a.UserId == null)
                    .Take(Math.Abs(difference))
                    .ToListAsync();

                _db.ShiftAssignments.RemoveRange(unassignedToRemove);

                // If still need to remove more and force removal is true, remove assigned slots
                var remaining = Math.Abs(difference) - unassignedToRemove.Count;
                if (remaining > 0 && request.ForceRemoval)
                {
                    var assignedToRemove = await _db.ShiftAssignments
                        .Where(a => a.ShiftInstanceId == instance.Id && a.UserId != null)
                        .Take(remaining)
                        .ToListAsync();

                    _db.ShiftAssignments.RemoveRange(assignedToRemove);
                }
            }

            // Update staffing requirement
            instance.StaffingRequired = request.StaffingRequired;
            await _db.SaveChangesAsync();

            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating shift staffing");
            return new JsonResult(new { success = false, error = "Failed to update shift staffing" });
        }
    }

    public async Task<IActionResult> OnPostDeleteShiftInstanceAsync([FromBody] DeleteShiftInstanceRequest request)
    {
        try
        {
            var companyId = _companyContext.GetCompanyIdOrThrow();

            var instance = await _db.ShiftInstances
                .FirstOrDefaultAsync(si => si.Id == request.ShiftInstanceId && si.CompanyId == companyId);

            if (instance == null)
            {
                return new JsonResult(new { success = false, error = "Shift instance not found" });
            }

            // Delete all assignments first
            var assignments = await _db.ShiftAssignments
                .Where(a => a.ShiftInstanceId == instance.Id)
                .ToListAsync();

            _db.ShiftAssignments.RemoveRange(assignments);

            // Delete the instance
            _db.ShiftInstances.Remove(instance);

            await _db.SaveChangesAsync();

            _logger.LogInformation("Deleted shift instance {InstanceId} with {AssignmentCount} assignments",
                instance.Id, assignments.Count);

            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting shift instance {InstanceId}", request.ShiftInstanceId);
            return new JsonResult(new { success = false, error = "Failed to delete shift instance" });
        }
    }

    public async Task<IActionResult> OnPostAddTraineeAsync([FromBody] AddTraineeRequest request)
    {
        try
        {
            var assignment = await _db.ShiftAssignments.FindAsync(request.AssignmentId);
            if (assignment == null)
            {
                return new JsonResult(new { success = false, error = "Assignment not found" });
            }

            // Update trainee
            assignment.TraineeUserId = request.TraineeUserId;
            await _db.SaveChangesAsync();

            var trainee = await _db.Users.FindAsync(request.TraineeUserId);

            return new JsonResult(new
            {
                success = true,
                traineeName = trainee?.DisplayName
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding trainee");
            return new JsonResult(new { success = false, error = "Failed to add trainee" });
        }
    }

    public async Task<IActionResult> OnPostRemoveTraineeAsync([FromBody] RemoveTraineeRequest request)
    {
        try
        {
            var assignment = await _db.ShiftAssignments.FindAsync(request.AssignmentId);
            if (assignment == null)
            {
                return new JsonResult(new { success = false, error = "Assignment not found" });
            }

            // Remove trainee
            assignment.TraineeUserId = null;
            await _db.SaveChangesAsync();

            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing trainee");
            return new JsonResult(new { success = false, error = "Failed to remove trainee" });
        }
    }

    public async Task<IActionResult> OnPostChangeUserAsync([FromBody] ChangeUserRequest request)
    {
        try
        {
            var assignment = await _db.ShiftAssignments.FindAsync(request.AssignmentId);
            if (assignment == null)
            {
                return new JsonResult(new { success = false, error = "Assignment not found" });
            }

            // Change primary user
            assignment.UserId = request.NewUserId;
            await _db.SaveChangesAsync();

            var user = await _db.Users.FindAsync(request.NewUserId);

            return new JsonResult(new
            {
                success = true,
                employeeName = user?.DisplayName
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing user");
            return new JsonResult(new { success = false, error = "Failed to change user" });
        }
    }

    public async Task<IActionResult> OnPostUpdateShiftMetadataAsync([FromBody] UpdateShiftMetadataRequest request)
    {
        try
        {
            var companyId = _companyContext.GetCompanyIdOrThrow();
            var shiftType = await _db.ShiftTypes.FindAsync(request.ShiftTypeId);
            if (shiftType == null || shiftType.CompanyId != companyId)
            {
                return new JsonResult(new { success = false, error = "Shift type not found" });
            }

            // Validate name
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return new JsonResult(new { success = false, error = "Shift name cannot be empty" });
            }

            // Parse time strings
            if (!TimeOnly.TryParse(request.StartTime, out var startTime) ||
                !TimeOnly.TryParse(request.EndTime, out var endTime))
            {
                return new JsonResult(new { success = false, error = "Invalid time format" });
            }

            // Update metadata (company-scoped rename via CustomName)
            shiftType.CustomName = request.Name.Trim();
            shiftType.Start = startTime;
            shiftType.End = endTime;

            await _db.SaveChangesAsync();

            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating shift metadata");
            return new JsonResult(new { success = false, error = "Failed to update shift metadata" });
        }
    }

    public async Task<IActionResult> OnPostCreateCustomShiftTypeAsync([FromBody] CreateCustomShiftTypeRequest request)
    {
        try
        {
            var companyId = _companyContext.GetCompanyIdOrThrow();

            // Validate name
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return new JsonResult(new { success = false, error = "Shift name cannot be empty" });
            }

            // Parse time strings
            if (!TimeOnly.TryParse(request.StartTime, out var startTime) ||
                !TimeOnly.TryParse(request.EndTime, out var endTime))
            {
                return new JsonResult(new { success = false, error = "Invalid time format" });
            }

            // Create custom shift type with a unique internal key but user-visible name
            var customKey = $"CUSTOM_{Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper()}";

            var shiftType = new ShiftType
            {
                CompanyId = companyId,
                Key = customKey,
                CustomName = request.Name.Trim(), // User-provided name (NO KEY LEAKAGE)
                Start = startTime,
                End = endTime
            };

            _db.ShiftTypes.Add(shiftType);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Created custom shift type {ShiftTypeId} with name '{Name}' for company {CompanyId}",
                shiftType.Id, shiftType.CustomName, companyId);

            return new JsonResult(new
            {
                success = true,
                shiftTypeId = shiftType.Id,
                shiftName = shiftType.Name // Returns the user-friendly name
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating custom shift type");
            return new JsonResult(new { success = false, error = "Failed to create custom shift type" });
        }
    }

    public class UpdateShiftMetadataRequest
    {
        public int ShiftTypeId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string StartTime { get; set; } = string.Empty;
        public string EndTime { get; set; } = string.Empty;
    }

    public class CreateCustomShiftTypeRequest
    {
        public string Name { get; set; } = string.Empty;
        public string StartTime { get; set; } = string.Empty;
        public string EndTime { get; set; } = string.Empty;
    }

    public class CreateShiftInstanceRequest
    {
        public int ShiftTypeId { get; set; }
        public DateOnly Date { get; set; }
        public int StaffingRequired { get; set; }
    }

    public class AssignUserToSlotRequest
    {
        public int AssignmentId { get; set; }
        public int UserId { get; set; }
    }

    public class AssignEmployeeRequest
    {
        public int ShiftTypeId { get; set; }
        public DateOnly Date { get; set; }
        public int UserId { get; set; }
    }

    public class UnassignEmployeeRequest
    {
        public int AssignmentId { get; set; }
    }

    public class AddTraineeRequest
    {
        public int AssignmentId { get; set; }
        public int TraineeUserId { get; set; }
    }

    public class RemoveTraineeRequest
    {
        public int AssignmentId { get; set; }
    }

    public class ChangeUserRequest
    {
        public int AssignmentId { get; set; }
        public int NewUserId { get; set; }
    }

    public class ClearAssignmentRequest
    {
        public int AssignmentId { get; set; }
    }

    public class UpdateShiftStaffingRequest
    {
        public int ShiftInstanceId { get; set; }
        public int StaffingRequired { get; set; }
        public bool ForceRemoval { get; set; } = false;
    }

    public class EnsureShiftInstanceRequest
    {
        public int ShiftTypeId { get; set; }
        public DateOnly Date { get; set; }
        public int StaffingRequired { get; set; }
    }

    public class DeleteShiftInstanceRequest
    {
        public int ShiftInstanceId { get; set; }
    }

    /// <summary>
    /// Detaches a ShiftInstance from its Program (if any), marking it as manually modified.
    /// </summary>
    public async Task<IActionResult> OnPostDetachInstanceAsync([FromBody] DetachInstanceRequest request)
    {
        try
        {
            var companyId = _companyContext.GetCompanyIdOrThrow();

            var instance = await _db.ShiftInstances
                .FirstOrDefaultAsync(si => si.Id == request.ShiftInstanceId && si.CompanyId == companyId);

            if (instance == null)
            {
                return new JsonResult(new { success = false, error = "Shift instance not found" });
            }

            // Call service to detach instance
            await _programService.DetachInstanceAsync(request.ShiftInstanceId, request.Reason);

            _logger.LogInformation(
                "Detached ShiftInstance {InstanceId} from Program. Reason: {Reason}",
                request.ShiftInstanceId, request.Reason);

            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detaching shift instance {InstanceId}", request.ShiftInstanceId);
            return new JsonResult(new { success = false, error = "Failed to detach shift instance" });
        }
    }

    /// <summary>
    /// Resets a detached ShiftInstance back to its Program defaults.
    /// </summary>
    public async Task<IActionResult> OnPostResetInstanceToProgramAsync([FromBody] ResetInstanceRequest request)
    {
        try
        {
            var companyId = _companyContext.GetCompanyIdOrThrow();

            var instance = await _db.ShiftInstances
                .FirstOrDefaultAsync(si => si.Id == request.ShiftInstanceId && si.CompanyId == companyId);

            if (instance == null)
            {
                return new JsonResult(new { success = false, error = "Shift instance not found" });
            }

            if (!instance.IsDetached || !instance.OriginalProgramId.HasValue)
            {
                return new JsonResult(new { success = false, error = "Shift instance is not detached from a Program" });
            }

            // Call service to reset instance to Program defaults
            await _programService.ResetInstanceToProgramAsync(request.ShiftInstanceId);

            _logger.LogInformation(
                "Reset ShiftInstance {InstanceId} to Program {ProgramId} defaults",
                request.ShiftInstanceId, instance.OriginalProgramId);

            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting shift instance {InstanceId}", request.ShiftInstanceId);
            return new JsonResult(new { success = false, error = "Failed to reset shift instance" });
        }
    }

    /// <summary>
    /// Get 7-day availability for all employees (for availability cubes display).
    /// Returns availability status (free/busy/partial) for each of the next 7 days.
    /// </summary>
    public async Task<IActionResult> OnGetEmployeeAvailabilityAsync()
    {
        var startDate = DateOnly.FromDateTime(DateTime.Today);
        var endDate = startDate.AddDays(6);

        var employees = await _db.Users
            .Where(u => u.IsActive && (u.Role == UserRole.Employee || u.Role == UserRole.Trainee))
            .OrderBy(u => u.DisplayName)
            .Select(u => new { u.Id, u.DisplayName })
            .ToListAsync();

        var result = new List<object>();

        foreach (var emp in employees)
        {
            var availability = new List<object>();

            for (var date = startDate; date <= endDate; date = date.AddDays(1))
            {
                // Check for shifts
                var hasShift = await _db.ShiftAssignments
                    .AnyAsync(sa => sa.UserId == emp.Id &&
                                   sa.ShiftInstance!.WorkDate == date);

                // Check for time-off (DateOnly comparison)
                var hasTimeOff = await _db.TimeOffRequests
                    .AnyAsync(tor => tor.UserId == emp.Id &&
                                     tor.StartDate <= date &&
                                     tor.EndDate >= date &&
                                     tor.Status == RequestStatus.Approved);

                var status = "free";
                var tooltip = "Available";

                if (hasTimeOff)
                {
                    status = "busy";
                    tooltip = "Time Off";
                }
                else if (hasShift)
                {
                    status = "partial";
                    tooltip = "Has Shift";
                }

                availability.Add(new {
                    date = date.ToString("d/M"),
                    dayName = date.DayOfWeek.ToString().Substring(0, 3),
                    status,
                    tooltip
                });
            }

            result.Add(new {
                id = emp.Id,
                name = emp.DisplayName,
                availability
            });
        }

        return new JsonResult(result);
    }

    /// <summary>
    /// Get roster of employees with their availability status for the Roster Dock.
    /// Checks availability across the entire date range being displayed.
    /// </summary>
    public async Task<IActionResult> OnGetGetRosterEmployeesAsync(DateOnly? startDate = null, DateOnly? endDate = null)
    {
        try
        {
            var companyId = _companyContext.GetCompanyIdOrThrow();

            // Use provided date range or default to current week
            var start = startDate ?? StartDate;
            var end = endDate ?? EndDate;

            // Get all employees for the company
            var employees = await _db.Users
                .Where(u => u.CompanyId == companyId && u.IsActive)
                .OrderBy(u => u.DisplayName)
                .Select(u => new
                {
                    u.Id,
                    u.DisplayName
                })
                .ToListAsync();

            // Check busy status across the entire date range
            var employeeStatusMap = new Dictionary<int, (bool hasVacation, bool hasShift, bool hasChore)>();

            for (var date = start; date <= end; date = date.AddDays(1))
            {
                var busyInfo = await _busyUserService.GetBusyUsersAsync(date, TimeOnly.MinValue, TimeOnly.MaxValue);

                foreach (var emp in employees)
                {
                    if (busyInfo.ContainsKey(emp.Id))
                    {
                        var status = employeeStatusMap.GetValueOrDefault(emp.Id);
                        employeeStatusMap[emp.Id] = (
                            status.hasVacation || busyInfo[emp.Id].HasVacation,
                            status.hasShift || busyInfo[emp.Id].HasShift,
                            status.hasChore || busyInfo[emp.Id].HasChore
                        );
                    }
                }
            }

            var employeeList = employees.Select(emp =>
            {
                var status = employeeStatusMap.GetValueOrDefault(emp.Id);
                return new
                {
                    id = emp.Id,
                    name = emp.DisplayName,
                    onVacation = status.hasVacation,
                    hasShift = status.hasShift,
                    hasChore = status.hasChore
                };
            }).ToList();

            return new JsonResult(new { employees = employeeList });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading roster employees");
            return new JsonResult(new { success = false, error = "Failed to load employees" });
        }
    }

    /// <summary>
    /// Fill a range of dates with shifts copied from a source shift.
    /// Supports three modes: exact copy, staffing only, or apply Program defaults.
    /// </summary>
    public async Task<IActionResult> OnPostFillRangeAsync([FromBody] FillRangeRequest request)
    {
        try
        {
            var companyId = _companyContext.GetCompanyIdOrThrow();

            // Validate source instance
            var sourceInstance = await _db.ShiftInstances
                .Include(si => si.ShiftType)
                .FirstOrDefaultAsync(si => si.Id == request.SourceInstanceId && si.CompanyId == companyId);

            if (sourceInstance == null)
            {
                return new JsonResult(new { success = false, error = "Source shift not found" });
            }

            // Load source assignments separately
            var sourceAssignments = await _db.ShiftAssignments
                .Include(sa => sa.User)
                .Include(sa => sa.Trainee)
                .Where(sa => sa.ShiftInstanceId == sourceInstance.Id)
                .ToListAsync();

            var createdCount = 0;
            var updatedCount = 0;

            foreach (var targetDate in request.TargetDates)
            {
                DateOnly parsedDate;
                if (!DateOnly.TryParse(targetDate, out parsedDate))
                {
                    _logger.LogWarning("Invalid target date: {Date}", targetDate);
                    continue;
                }

                // Check if instance already exists for this date and shift type
                var existingInstance = await _db.ShiftInstances
                    .FirstOrDefaultAsync(si =>
                        si.CompanyId == companyId &&
                        si.ShiftTypeId == sourceInstance.ShiftTypeId &&
                        si.WorkDate == parsedDate);

                // Load existing assignments if instance exists
                List<ShiftAssignment> existingAssignments = new();
                if (existingInstance != null)
                {
                    existingAssignments = await _db.ShiftAssignments
                        .Where(sa => sa.ShiftInstanceId == existingInstance.Id)
                        .ToListAsync();
                }

                switch (request.Mode)
                {
                    case "exact":
                        // Copy exact: duplicate all assignments including trainees
                        if (existingInstance != null)
                        {
                            // Update existing instance
                            existingInstance.StaffingRequired = sourceInstance.StaffingRequired;
                            existingInstance.IsDetached = true;
                            existingInstance.OriginalProgramId = sourceInstance.OriginalProgramId;

                            // Remove old assignments
                            _db.ShiftAssignments.RemoveRange(existingAssignments);

                            // Add new assignments (copy from source)
                            foreach (var sourceAssignment in sourceAssignments)
                            {
                                _db.ShiftAssignments.Add(new ShiftAssignment
                                {
                                    ShiftInstanceId = existingInstance.Id,
                                    UserId = sourceAssignment.UserId,
                                    TraineeUserId = sourceAssignment.TraineeUserId,
                                    CompanyId = companyId
                                });
                            }

                            updatedCount++;
                        }
                        else
                        {
                            // Create new instance
                            var newInstance = new ShiftInstance
                            {
                                CompanyId = companyId,
                                ShiftTypeId = sourceInstance.ShiftTypeId,
                                WorkDate = parsedDate,
                                StaffingRequired = sourceInstance.StaffingRequired,
                                IsDetached = true,
                                OriginalProgramId = sourceInstance.OriginalProgramId
                            };

                            _db.ShiftInstances.Add(newInstance);
                            await _db.SaveChangesAsync(); // Save to get ID

                            // Add assignments
                            foreach (var sourceAssignment in sourceAssignments)
                            {
                                _db.ShiftAssignments.Add(new ShiftAssignment
                                {
                                    ShiftInstanceId = newInstance.Id,
                                    UserId = sourceAssignment.UserId,
                                    TraineeUserId = sourceAssignment.TraineeUserId,
                                    CompanyId = companyId
                                });
                            }

                            createdCount++;
                        }
                        break;

                    case "staffing":
                        // Copy staffing only: create empty slots with same count
                        if (existingInstance != null)
                        {
                            existingInstance.StaffingRequired = sourceInstance.StaffingRequired;
                            existingInstance.IsDetached = true;

                            // Remove old assignments
                            _db.ShiftAssignments.RemoveRange(existingAssignments);

                            // Add empty slots
                            for (int i = 0; i < sourceInstance.StaffingRequired; i++)
                            {
                                _db.ShiftAssignments.Add(new ShiftAssignment
                                {
                                    ShiftInstanceId = existingInstance.Id,
                                    UserId = null,
                                    CompanyId = companyId
                                });
                            }

                            updatedCount++;
                        }
                        else
                        {
                            var newInstance = new ShiftInstance
                            {
                                CompanyId = companyId,
                                ShiftTypeId = sourceInstance.ShiftTypeId,
                                WorkDate = parsedDate,
                                StaffingRequired = sourceInstance.StaffingRequired,
                                IsDetached = true
                            };

                            _db.ShiftInstances.Add(newInstance);
                            await _db.SaveChangesAsync();

                            // Add empty slots
                            for (int i = 0; i < sourceInstance.StaffingRequired; i++)
                            {
                                _db.ShiftAssignments.Add(new ShiftAssignment
                                {
                                    ShiftInstanceId = newInstance.Id,
                                    UserId = null,
                                    CompanyId = companyId
                                });
                            }

                            createdCount++;
                        }
                        break;

                    case "program":
                        // Apply Program defaults: use Program template if available
                        if (sourceInstance.OriginalProgramId.HasValue)
                        {
                            var program = await _db.ShiftPrograms
                                .Include(p => p.ProgramDays)
                                .FirstOrDefaultAsync(p => p.Id == sourceInstance.OriginalProgramId.Value);

                            if (program != null)
                            {
                                var dayOfWeek = parsedDate.DayOfWeek;
                                var programDay = program.ProgramDays.FirstOrDefault(pd => pd.DayOfWeek == dayOfWeek);

                                if (programDay != null)
                                {
                                    var staffingRequired = programDay.StaffingRequired ?? program.DefaultStaffingRequired;

                                    if (existingInstance != null)
                                    {
                                        existingInstance.StaffingRequired = staffingRequired;
                                        existingInstance.IsDetached = false;
                                        existingInstance.OriginalProgramId = program.Id;

                                        // Remove old assignments
                                        _db.ShiftAssignments.RemoveRange(existingAssignments);

                                        // Add empty slots
                                        for (int i = 0; i < staffingRequired; i++)
                                        {
                                            _db.ShiftAssignments.Add(new ShiftAssignment
                                            {
                                                ShiftInstanceId = existingInstance.Id,
                                                UserId = null,
                                                CompanyId = companyId
                                            });
                                        }

                                        updatedCount++;
                                    }
                                    else
                                    {
                                        var newInstance = new ShiftInstance
                                        {
                                            CompanyId = companyId,
                                            ShiftTypeId = sourceInstance.ShiftTypeId,
                                            WorkDate = parsedDate,
                                            StaffingRequired = staffingRequired,
                                            IsDetached = false,
                                            OriginalProgramId = program.Id
                                        };

                                        _db.ShiftInstances.Add(newInstance);
                                        await _db.SaveChangesAsync();

                                        // Add empty slots
                                        for (int i = 0; i < staffingRequired; i++)
                                        {
                                            _db.ShiftAssignments.Add(new ShiftAssignment
                                            {
                                                ShiftInstanceId = newInstance.Id,
                                                UserId = null,
                                                CompanyId = companyId
                                            });
                                        }

                                        createdCount++;
                                    }
                                }
                            }
                        }
                        else
                        {
                            return new JsonResult(new { success = false, error = "Source shift is not linked to a Program" });
                        }
                        break;

                    default:
                        return new JsonResult(new { success = false, error = "Invalid fill mode" });
                }
            }

            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "Fill range completed: {Created} created, {Updated} updated using mode {Mode}",
                createdCount, updatedCount, request.Mode);

            return new JsonResult(new
            {
                success = true,
                created = createdCount,
                updated = updatedCount,
                message = $"Successfully filled {createdCount + updatedCount} shifts"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error filling range");
            return new JsonResult(new { success = false, error = "Failed to fill range" });
        }
    }

    public class DetachInstanceRequest
    {
        public int ShiftInstanceId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public class ResetInstanceRequest
    {
        public int ShiftInstanceId { get; set; }
    }

    /// <summary>
    /// Get conflicts and warnings for Radar Mode.
    /// Returns cells that are underfilled or have overlapping assignments.
    /// </summary>
    public async Task<IActionResult> OnGetGetConflictsAsync(string? start, string? view)
    {
        try
        {
            var companyId = _companyContext.GetCompanyIdOrThrow();

            // Calculate date range from query parameters (same logic as OnGetAsync)
            string viewMode = view?.ToLower() ?? "week";
            if (viewMode != "week" && viewMode != "2weeks" && viewMode != "month")
            {
                viewMode = "week";
            }

            DateOnly startDate;
            if (string.IsNullOrEmpty(start))
            {
                var today = DateOnly.FromDateTime(DateTime.Today);
                startDate = today.AddDays(-(int)today.DayOfWeek); // Start of week (Sunday)
            }
            else if (!DateOnly.TryParse(start, out startDate))
            {
                var today = DateOnly.FromDateTime(DateTime.Today);
                startDate = today.AddDays(-(int)today.DayOfWeek);
            }

            DateOnly endDate = viewMode switch
            {
                "2weeks" => startDate.AddDays(13),
                "month" => startDate.AddDays(DateTime.DaysInMonth(startDate.Year, startDate.Month) - 1),
                _ => startDate.AddDays(6)
            };

            // Get all instances in the current view
            var instances = await _db.ShiftInstances
                .Where(si => si.WorkDate >= startDate && si.WorkDate <= endDate)
                .Include(si => si.ShiftType)
                .ToListAsync();

            // Get all assignments for these instances
            var instanceIds = instances.Select(i => i.Id).ToList();
            var assignments = await _db.ShiftAssignments
                .Where(a => instanceIds.Contains(a.ShiftInstanceId))
                .ToListAsync();

            var conflicts = new List<object>();

            foreach (var instance in instances)
            {
                var instanceAssignments = assignments.Where(a => a.ShiftInstanceId == instance.Id).ToList();
                var filledCount = instanceAssignments.Count(a => a.UserId.HasValue);

                // Check if underfilled
                if (filledCount < instance.StaffingRequired)
                {
                    conflicts.Add(new
                    {
                        instanceId = instance.Id,
                        shiftTypeId = instance.ShiftTypeId,
                        shiftTypeName = instance.ShiftType.Name,
                        date = instance.WorkDate.ToString("yyyy-MM-dd"),
                        type = "underfilled",
                        severity = "warning",
                        message = $"Understaffed: {filledCount}/{instance.StaffingRequired} filled",
                        filled = filledCount,
                        required = instance.StaffingRequired
                    });
                }

                // Check for overfilling
                if (filledCount > instance.StaffingRequired)
                {
                    conflicts.Add(new
                    {
                        instanceId = instance.Id,
                        shiftTypeId = instance.ShiftTypeId,
                        shiftTypeName = instance.ShiftType.Name,
                        date = instance.WorkDate.ToString("yyyy-MM-dd"),
                        type = "overfilled",
                        severity = "error",
                        message = $"Overstaffed: {filledCount}/{instance.StaffingRequired} filled",
                        filled = filledCount,
                        required = instance.StaffingRequired
                    });
                }
            }

            return new JsonResult(new { conflicts });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting conflicts");
            return new JsonResult(new { success = false, error = "Failed to load conflicts" });
        }
    }

    public class FillRangeRequest
    {
        public int SourceInstanceId { get; set; }
        public List<string> TargetDates { get; set; } = new();
        public List<int> TargetInstanceIds { get; set; } = new();
        public string Mode { get; set; } = "exact"; // exact, staffing, program
    }
}
