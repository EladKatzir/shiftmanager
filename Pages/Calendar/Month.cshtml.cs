using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;               // ensure this using is present
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Localization;
using ShiftManager.Resources;

namespace ShiftManager.Pages.Calendar;

[Authorize]
// SECURITY FIX: Removed [IgnoreAntiforgeryToken] - CSRF protection is REQUIRED
public class MonthModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ICompanyContext _companyContext;
    private readonly ILogger<MonthModel> _logger;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly IDirectorService _directorService;

    public MonthModel(AppDbContext db, ICompanyContext companyContext, ILogger<MonthModel> logger, IStringLocalizer<SharedResources> localizer, IDirectorService directorService)
    {
        _db = db;
        _companyContext = companyContext;
        _logger = logger;
        _localizer = localizer;
        _directorService = directorService;
    }


    public DateOnly CurrentMonth { get; set; }
    public (DateOnly MonthYear, string Label) Previous { get; set; }
    public (DateOnly MonthYear, string Label) Next { get; set; }

    public List<List<DayVM>> Weeks { get; set; } = new();

    public class DayVM
    {
        public DateOnly Date { get; set; }
        public List<LineVM> Lines { get; set; } = new();
    }
    public class LineVM
    {
        public int ShiftTypeId { get; set; }
        public int InstanceId { get; set; } // 0 if none yet
        public int Concurrency { get; set; }
        public string ShortName { get; set; } = "";
        public string ShiftTypeKey { get; set; } = "";
        public string ShiftTypeName { get; set; } = "";
        public string ShiftName { get; set; } = ""; // Custom shift instance name
        public int Assigned { get; set; }
        public int Required { get; set; }
        public int TraineeCount { get; set; }
        public List<string> AssignedNames { get; set; } = new();
        public List<string> TraineeNames { get; set; } = new();
        public List<string> EmptySlots { get; set; } = new();
        public string StartTime { get; set; } = "";
        public string EndTime { get; set; } = "";
    }

    public async Task OnGetAsync(int? year, int? month)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var target = year.HasValue && month.HasValue ? new DateOnly(year.Value, month.Value, 1) : new DateOnly(today.Year, today.Month, 1);
        CurrentMonth = target;

        Previous = (target.AddMonths(-1), target.AddMonths(-1).ToString("MMM yyyy"));
        Next = (target.AddMonths(1), target.AddMonths(1).ToString("MMM yyyy"));

        // Load accessible companies and shift types based on role
        var currentUserId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var companyId = _companyContext.GetCompanyIdOrThrow();
        _logger.LogInformation("Month calendar: User {UserId} loading calendar with CompanyId={CompanyId}", currentUserId, companyId);
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

        // Build 6-week grid starting Monday
        var start = new DateOnly(target.Year, target.Month, 1);
        int delta = ((int)start.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var gridStart = start.AddDays(-delta);
        var dates = Enumerable.Range(0, 42).Select(i => gridStart.AddDays(i)).ToList();

        // Load instances and assignments for the window
        _logger.LogInformation("Month calendar: Querying ShiftInstances for CompanyId={CompanyId}, dates {Start} to {End}",
            companyId, dates.First(), dates.Last());
        var instances = await _db.ShiftInstances
            .Where(si => si.CompanyId == companyId && si.WorkDate >= dates.First() && si.WorkDate <= dates.Last())
            .ToListAsync();
        _logger.LogInformation("Month calendar: Found {Count} shift instances", instances.Count);

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

        // Fetch trainee assignments with names
        var traineeAssignments = await (from a in _db.ShiftAssignments
                                       join t in _db.Users on a.TraineeUserId equals t.Id
                                       where instanceIds.Contains(a.ShiftInstanceId) && a.TraineeUserId != null
                                       select new { a.ShiftInstanceId, TraineeName = t.DisplayName })
                                       .ToListAsync();

        var dictAssigned = assignmentCounts.ToDictionary(x => x.ShiftInstanceId, x => x.Count);
        var dictAssignedNames = assignmentsWithNames
            .GroupBy(x => x.ShiftInstanceId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.UserName).ToList());
        var dictTraineeCount = traineeAssignments
            .GroupBy(x => x.ShiftInstanceId)
            .ToDictionary(g => g.Key, g => g.Count());
        var dictTraineeNames = traineeAssignments
            .GroupBy(x => x.ShiftInstanceId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.TraineeName).ToList());

        for (int w = 0; w < 6; w++)
        {
            var week = new List<DayVM>();
            for (int d = 0; d < 7; d++)
            {
                var date = dates[w * 7 + d];
                var vm = new DayVM { Date = date };
                foreach (var t in types)
                {
                    var inst = instances.FirstOrDefault(i => i.WorkDate == date && i.ShiftTypeId == t.Id && i.CompanyId == companyId);
                    var assignedCount = inst != null && dictAssigned.ContainsKey(inst.Id) ? dictAssigned[inst.Id] : 0;
                    var requiredCount = inst?.StaffingRequired ?? 0;
                    var traineeCount = inst != null && dictTraineeCount.ContainsKey(inst.Id) ? dictTraineeCount[inst.Id] : 0;
                    var assignedNames = inst != null && dictAssignedNames.ContainsKey(inst.Id) ? dictAssignedNames[inst.Id] : new List<string>();
                    var traineeNames = inst != null && dictTraineeNames.ContainsKey(inst.Id) ? dictTraineeNames[inst.Id] : new List<string>();
                    var emptySlots = Enumerable.Repeat(_localizer["Empty"].Value, Math.Max(0, requiredCount - assignedCount)).ToList();

                    vm.Lines.Add(new LineVM
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
                        TraineeNames = traineeNames,
                        EmptySlots = emptySlots,
                        StartTime = t.Start.ToString("HH:mm"),
                        EndTime = t.End.ToString("HH:mm")
                    });
                }
                week.Add(vm);
            }
            Weeks.Add(week);
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
        var currentUserId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
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

}
