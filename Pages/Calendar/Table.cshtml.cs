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

    public TableModel(AppDbContext db, ICompanyContext companyContext, ILogger<TableModel> logger, IBusyUserService busyUserService)
    {
        _db = db;
        _companyContext = companyContext;
        _logger = logger;
        _busyUserService = busyUserService;
    }

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public List<DateOnly> Dates { get; set; } = new();
    public List<ShiftType> ShiftTypes { get; set; } = new();
    public List<AppUser> Employees { get; set; } = new();

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
    }

    public async Task<IActionResult> OnGetAsync(string? start)
    {
        var companyId = _companyContext.GetCompanyIdOrThrow();

        // Default to current week if no start date provided
        if (string.IsNullOrEmpty(start))
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            StartDate = today.AddDays(-(int)today.DayOfWeek); // Start of week (Sunday)
        }
        else
        {
            StartDate = DateOnly.Parse(start);
        }

        EndDate = StartDate.AddDays(13); // 2 weeks view

        // Generate date range
        for (var date = StartDate; date <= EndDate; date = date.AddDays(1))
        {
            Dates.Add(date);
        }

        // Load shift types for this company
        // Sort by start time (chronological order), with Offline always last
        ShiftTypes = (await _db.ShiftTypes.ToListAsync())
            .OrderBy(st => st.IsOffline ? 1 : 0) // Offline last
            .ThenBy(st => st.Start) // Then by start time (chronological)
            .ThenBy(st => st.CustomName ?? st.Name) // Then by name for same start time
            .ToList();

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

        // Load shift instances for date range
        var instances = await _db.ShiftInstances
            .Where(si => si.WorkDate >= StartDate && si.WorkDate <= EndDate)
            .ToListAsync();

        // Load all assignments for these instances (including trainee information)
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
                            TraineeName = a.Trainee?.DisplayName
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
}
