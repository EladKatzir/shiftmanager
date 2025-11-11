using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Services;
using Microsoft.Extensions.Logging;

namespace ShiftManager.Pages.Calendar;

[Authorize]
[IgnoreAntiforgeryToken]
public class WeekModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ICompanyContext _companyContext;
    private readonly ILogger<WeekModel> _logger;
    private readonly IDirectorService _directorService;

    public WeekModel(AppDbContext db, ICompanyContext companyContext, ILogger<WeekModel> logger, IDirectorService directorService)
    {
        _db = db;
        _companyContext = companyContext;
        _logger = logger;
        _directorService = directorService;
    }

    public DateOnly CurrentWeekStart { get; set; }
    public (DateOnly WeekStart, string Label) Previous { get; set; }
    public (DateOnly WeekStart, string Label) Next { get; set; }
    public List<DayVM> Days { get; set; } = new();

    public class DayVM
    {
        public DateOnly Date { get; set; }
        public string DayName { get; set; } = "";
        public List<LineVM> Lines { get; set; } = new();
    }

    public class LineVM
    {
        public int ShiftTypeId { get; set; }
        public int InstanceId { get; set; }
        public int Concurrency { get; set; }
        public string ShortName { get; set; } = "";
        public string ShiftTypeKey { get; set; } = "";
        public string ShiftTypeName { get; set; } = "";
        public string ShiftName { get; set; } = ""; // Custom shift instance name
        public int Assigned { get; set; }
        public int Required { get; set; }
        public int TraineeCount { get; set; }
        public List<string> AssignedNames { get; set; } = new();
        public List<string> EmptySlots { get; set; } = new();
        public string StartTime { get; set; } = "";
        public string EndTime { get; set; } = "";
    }

    public async Task OnGetAsync(int? year, int? month, int? day)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var targetDate = year.HasValue && month.HasValue && day.HasValue
            ? new DateOnly(year.Value, month.Value, day.Value)
            : today;

        // Get Monday of the week containing the target date
        int daysFromMonday = ((int)targetDate.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        CurrentWeekStart = targetDate.AddDays(-daysFromMonday);

        Previous = (CurrentWeekStart.AddDays(-7), CurrentWeekStart.AddDays(-7).ToString("MMM dd"));
        Next = (CurrentWeekStart.AddDays(7), CurrentWeekStart.AddDays(7).ToString("MMM dd"));

        var companyId = _companyContext.GetCompanyIdOrThrow();

        // Load accessible companies and shift types based on role
        // SECURITY FIX: Use TryParse to prevent crashes
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId))
        {
            return;
        }
        var currentUser = await _db.Users.FindAsync(currentUserId);

        List<int> accessibleCompanyIds;
        List<Company> accessibleCompanies;
        List<ShiftType> types;

        if (currentUser!.Role == Models.Support.UserRole.Owner)
        {
            // Owner: all companies
            accessibleCompanies = await _db.Companies.OrderBy(c => c.Name).ToListAsync();
            accessibleCompanyIds = accessibleCompanies.Select(c => c.Id).ToList();
            types = await _db.ShiftTypes.IgnoreQueryFilters()
                .Where(st => accessibleCompanyIds.Contains(st.CompanyId))
                .OrderBy(s => s.Key)
                .ToListAsync();
        }
        else if (currentUser.Role == Models.Support.UserRole.Director)
        {
            // Director: companies they manage
            accessibleCompanyIds = await _directorService.GetDirectorCompanyIdsAsync(currentUserId);
            accessibleCompanies = await _db.Companies
                .Where(c => accessibleCompanyIds.Contains(c.Id))
                .OrderBy(c => c.Name)
                .ToListAsync();
            types = await _db.ShiftTypes.IgnoreQueryFilters()
                .Where(st => accessibleCompanyIds.Contains(st.CompanyId))
                .OrderBy(s => s.Key)
                .ToListAsync();
        }
        else
        {
            // Manager/Employee/Trainee: only their company
            accessibleCompanyIds = new List<int> { currentUser.CompanyId };
            accessibleCompanies = await _db.Companies
                .Where(c => c.Id == currentUser.CompanyId)
                .ToListAsync();
            types = await _db.ShiftTypes.OrderBy(s => s.Key).ToListAsync();
        }

        // ✅ SECURITY FIX (DEFECT-002): Post-query validation for defense-in-depth
        // Validate every shift type after IgnoreQueryFilters to prevent data leaks
        var validatedTypes = new List<ShiftType>();
        foreach (var type in types)
        {
            bool hasAccess = false;
            if (currentUser.Role == Models.Support.UserRole.Owner)
            {
                hasAccess = true;
            }
            else if (currentUser.Role == Models.Support.UserRole.Director)
            {
                hasAccess = await _directorService.IsDirectorOfAsync(type.CompanyId);
            }
            else if (currentUser.Role == Models.Support.UserRole.Manager || currentUser.Role == Models.Support.UserRole.Employee || currentUser.Role == Models.Support.UserRole.Trainee)
            {
                hasAccess = currentUser.CompanyId == type.CompanyId;
            }

            if (hasAccess)
            {
                validatedTypes.Add(type);
            }
            else
            {
                _logger.LogWarning("SECURITY: Filtered unauthorized shift type {ShiftTypeId} from company {CompanyId} for user {UserId}",
                    type.Id, type.CompanyId, currentUserId);
            }
        }
        types = validatedTypes;

        // Prepare companies for JavaScript
        ViewData["Companies"] = accessibleCompanies.Select(c => new
        {
            id = c.Id,
            name = c.Name
        }).ToList();

        // Prepare shift types with company info for JavaScript
        var companyDict = accessibleCompanies.ToDictionary(c => c.Id, c => c.Name);
        ViewData["ShiftTypes"] = types.Select(t => new
        {
            id = t.Id,
            key = t.Key,
            name = t.Name,
            start = t.Start.ToString("HH:mm"),
            end = t.End.ToString("HH:mm"),
            companyId = t.CompanyId,
            companyName = companyDict.ContainsKey(t.CompanyId) ? companyDict[t.CompanyId] : "Unknown"
        }).ToList();

        // Generate 7 days for the week
        var weekDates = Enumerable.Range(0, 7).Select(i => CurrentWeekStart.AddDays(i)).ToList();

        // Load instances and assignments for the week
        var instances = await _db.ShiftInstances
            .Where(si => si.CompanyId == companyId && si.WorkDate >= weekDates.First() && si.WorkDate <= weekDates.Last())
            .ToListAsync();

        var instanceIds = instances.Select(i => i.Id).ToList();
        var assignmentCounts = await _db.ShiftAssignments
            .Where(a => instanceIds.Contains(a.ShiftInstanceId))
            .GroupBy(a => a.ShiftInstanceId)
            .Select(g => new { ShiftInstanceId = g.Key, Count = g.Count() })
            .ToListAsync();

        // Fetch assignments with user names
        var assignmentsWithNames = await (from a in _db.ShiftAssignments
                                         join u in _db.Users on a.UserId equals u.Id
                                         where instanceIds.Contains(a.ShiftInstanceId)
                                         select new { a.ShiftInstanceId, UserName = u.DisplayName })
                                         .ToListAsync();

        // Count trainees per shift instance
        var traineeCounts = await _db.ShiftAssignments
            .Where(a => instanceIds.Contains(a.ShiftInstanceId) && a.TraineeUserId != null)
            .GroupBy(a => a.ShiftInstanceId)
            .Select(g => new { ShiftInstanceId = g.Key, TraineeCount = g.Count() })
            .ToListAsync();

        var dictAssigned = assignmentCounts.ToDictionary(x => x.ShiftInstanceId, x => x.Count);
        var dictAssignedNames = assignmentsWithNames
            .GroupBy(x => x.ShiftInstanceId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.UserName).ToList());
        var dictTraineeCount = traineeCounts.ToDictionary(x => x.ShiftInstanceId, x => x.TraineeCount);

        foreach (var date in weekDates)
        {
            var dayVm = new DayVM
            {
                Date = date,
                DayName = date.DayOfWeek.ToString()
            };

            foreach (var t in types)
            {
                var inst = instances.FirstOrDefault(i => i.WorkDate == date && i.ShiftTypeId == t.Id && i.CompanyId == companyId);
                var assignedCount = inst != null && dictAssigned.ContainsKey(inst.Id) ? dictAssigned[inst.Id] : 0;
                var requiredCount = inst?.StaffingRequired ?? 0;
                var traineeCount = inst != null && dictTraineeCount.ContainsKey(inst.Id) ? dictTraineeCount[inst.Id] : 0;
                var assignedNames = inst != null && dictAssignedNames.ContainsKey(inst.Id) ? dictAssignedNames[inst.Id] : new List<string>();
                var emptySlots = Enumerable.Repeat("Empty", Math.Max(0, requiredCount - assignedCount)).ToList();

                dayVm.Lines.Add(new LineVM
                {
                    ShiftTypeId = t.Id,
                    InstanceId = inst?.Id ?? 0,
                    Concurrency = inst?.Concurrency ?? 0,
                    ShortName = t.Name[..Math.Min(3, t.Name.Length)],
                    ShiftTypeKey = t.Key.ToLower(),
                    ShiftTypeName = t.Name,
                    ShiftName = inst?.Name ?? "",
                    Assigned = assignedCount,
                    Required = requiredCount,
                    TraineeCount = traineeCount,
                    AssignedNames = assignedNames,
                    EmptySlots = emptySlots,
                    StartTime = t.Start.ToString("HH:mm"),
                    EndTime = t.End.ToString("HH:mm")
                });
            }
            Days.Add(dayVm);
        }
    }

    public class AdjustPayload
    {
        public string date { get; set; } = "";
        public int shiftTypeId { get; set; }
        public int delta { get; set; }
        public int concurrency { get; set; }
        public int? companyId { get; set; }
    }


    public async Task<IActionResult> OnPostAdjustAsync([FromBody] AdjustPayload payload)
    {
        _logger.LogInformation("Adjust staffing: date={Date} shiftTypeId={ShiftTypeId} delta={Delta} companyId={CompanyId}", payload.date, payload.shiftTypeId, payload.delta, payload.companyId);

        // Get current user for authorization
        // SECURITY FIX: Use TryParse to prevent crashes
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId))
        {
            return BadRequest("Invalid user claim");
        }
        var currentUser = await _db.Users.FindAsync(currentUserId);

        // ✅ SECURITY FIX (DEFECT-003): Always validate company access
        // Determine target company ID
        int targetCompanyId = payload.companyId ?? _companyContext.GetCompanyIdOrThrow();

        // ALWAYS validate user has access to target company (even for fallback case)
        bool hasAccess = false;
        if (currentUser!.Role == Models.Support.UserRole.Owner)
        {
            hasAccess = true;
        }
        else if (currentUser.Role == Models.Support.UserRole.Director)
        {
            hasAccess = await _directorService.IsDirectorOfAsync(targetCompanyId);
        }
        else if (currentUser.Role == Models.Support.UserRole.Manager)
        {
            hasAccess = currentUser.CompanyId == targetCompanyId;
        }

        if (!hasAccess)
        {
            _logger.LogWarning("SECURITY: User {UserId} ({Role}) attempted unauthorized access to company {CompanyId}",
                currentUserId, currentUser.Role, targetCompanyId);
            return StatusCode(403, new { message = $"Access denied to company {targetCompanyId}" });
        }

        // Additional validation: Verify shift type belongs to target company
        var shiftType = await _db.ShiftTypes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(st => st.Id == payload.shiftTypeId);

        if (shiftType == null)
        {
            return NotFound(new { message = "Shift type not found" });
        }

        if (shiftType.CompanyId != targetCompanyId)
        {
            _logger.LogWarning("SECURITY: User {UserId} attempted to use shift type {ShiftTypeId} from company {ShiftTypeCompanyId} in company {TargetCompanyId}",
                currentUserId, payload.shiftTypeId, shiftType.CompanyId, targetCompanyId);
            return BadRequest(new { message = "Shift type does not belong to the target company" });
        }

        var date = DateOnly.Parse(payload.date);

        var inst = await _db.ShiftInstances.IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.CompanyId == targetCompanyId && i.WorkDate == date && i.ShiftTypeId == payload.shiftTypeId);

        bool isNewInstance = inst == null;

        if (inst == null)
        {
            if (payload.delta < 0)
                return BadRequest(new { message = "Cannot go below zero." });
            inst = new ShiftInstance
            {
                CompanyId = targetCompanyId,
                ShiftTypeId = payload.shiftTypeId,
                WorkDate = date,
                StaffingRequired = 0,
                Concurrency = 0,
                UpdatedAt = DateTime.UtcNow
            };
            _db.ShiftInstances.Add(inst);
        }
        else
        {
            // ✅ SECURITY FIX (DEFECT-001): Always validate concurrency for existing instances
            // Removed "payload.concurrency != 0 &&" to prevent bypass attacks
            if (inst.Concurrency != payload.concurrency)
            {
                _logger.LogWarning("Concurrency mismatch for ShiftInstanceId={Id}: sent={Sent}, current={Current}", inst.Id, payload.concurrency, inst.Concurrency);
                return BadRequest(new { message = "Concurrent update detected. Reload the page." });
            }
        }

        int newRequired = inst.StaffingRequired + payload.delta;
        if (newRequired < 0) return BadRequest(new { message = "Cannot go below zero." });

        // prevent dropping below assigned count
        int assigned = await _db.ShiftAssignments.CountAsync(a => a.ShiftInstanceId == inst.Id);
        if (newRequired < assigned)
            return BadRequest(new { message = $"Cannot set required below assigned ({assigned})." });

        inst.StaffingRequired = newRequired;
        inst.Concurrency++;
        inst.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return new JsonResult(new { required = inst.StaffingRequired, assigned, concurrency = inst.Concurrency });
    }

    public class DeleteShiftInstanceRequest
    {
        public int ShiftInstanceId { get; set; }
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

}