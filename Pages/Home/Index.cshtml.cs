using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace ShiftManager.Pages.Home
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly AppDbContext _context;
        private readonly ICompanyContext _companyContext;
        private readonly IGrantService _grantService;
        private readonly ILogger<IndexModel> _logger;
        private readonly IWhoIsOnShiftService _whoIsOnShiftService;
        private readonly IAnnouncementService _announcementService;

        public IndexModel(AppDbContext context, ICompanyContext companyContext, IGrantService grantService, ILogger<IndexModel> logger, IWhoIsOnShiftService whoIsOnShiftService, IAnnouncementService announcementService)
        {
            _context = context;
            _companyContext = companyContext;
            _grantService = grantService;
            _logger = logger;
            _whoIsOnShiftService = whoIsOnShiftService;
            _announcementService = announcementService;
        }

        // Common properties (all users)
        public ShiftAssignment? NextShift { get; set; }
        public int UnreadNotificationsCount { get; set; }
        public List<UserNotification> RecentNotifications { get; set; } = new();
        public int ShiftsThisWeek { get; set; }
        public int DaysWithShiftsThisWeek { get; set; }
        public int OfflineDaysThisWeek { get; set; }
        public int VacationDaysThisWeek { get; set; }

        // Employee properties
        public int PendingRequestsCount { get; set; }
        public int ApprovedRequestsCount { get; set; }
        public int DeclinedRequestsCount { get; set; }

        // Manager properties
        public int UnassignedShiftsCount { get; set; }
        public int UnderstaffedDaysCount { get; set; }
        public int PendingTimeOffCount { get; set; }
        public int PendingSwapsCount { get; set; }

        // Director/Owner properties
        public int TotalCompaniesCount { get; set; }
        public int ActiveCompaniesCount { get; set; }

        // ✅ PHASE 18: Owner analytics summary properties
        public int TotalUsersCount { get; set; }
        public int TotalShiftsThisMonth { get; set; }
        public double AverageStaffingRate { get; set; }

        // User info
        public bool IsAdmin { get; set; }
        public bool IsDirector { get; set; }
        public bool IsOwner { get; set; }
        public string UserName { get; set; } = "";
        public string FirstName { get; set; } = "";

        // Company announcements — carried over from the legacy "/" landing so the consolidated Home
        // (now the only landing under the domain-hub nav) doesn't lose them.
        public List<Announcement> RecentAnnouncements { get; set; } = new();

        public async Task OnGetAsync()
        {
            UserName = User.FindFirst(ClaimTypes.Name)?.Value ?? "";
            // GivenName claim is populated from user.PreferredName ?? first-token-of-DisplayName
            // by GriffinService.BuildPrincipalFromClaimsAsync. Splitting UserName is the fallback
            // for non-Griffin auth paths that don't emit GivenName.
            var given = User.FindFirst(ClaimTypes.GivenName)?.Value;
            FirstName = !string.IsNullOrWhiteSpace(given)
                ? given
                : (UserName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? UserName);

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
            {
                _logger.LogWarning("Invalid or missing NameIdentifier claim on {Page}", "Home/Index");
                Response.Redirect("/Auth/Login");
                return;
            }

            // Grant-based access checks (never use User.IsInRole)
            IsAdmin = await _grantService.HasGrantAsync(userId, "AccessAdminNavigation");
            IsDirector = await _grantService.HasGrantAsync(userId, "DirectorHubAccess");
            IsOwner = await _grantService.HasGrantAsync(userId, "AdminAccess");

            var companyId = _companyContext.GetCompanyIdOrThrow();

            var now = DateTime.UtcNow;
            var today = DateOnly.FromDateTime(DateTime.Today);
            var startOfWeek = today.AddDays(-(int)DateTime.Today.DayOfWeek);
            var endOfWeek = startOfWeek.AddDays(7);

            // Common data for all users
            await LoadCommonDataAsync(userId, companyId, today, startOfWeek, endOfWeek);

            // Role-specific data
            if (!IsAdmin)
            {
                await LoadEmployeeDataAsync(userId, companyId);
            }
            else if (IsAdmin && !IsDirector)
            {
                await LoadManagerDataAsync(companyId, today);
            }
            else if (IsDirector)
            {
                await LoadDirectorDataAsync(companyId, today, userId);
            }

            // Active company announcements (everyone) — preserved from the legacy landing.
            RecentAnnouncements = await _announcementService.GetActiveAnnouncementsAsync(userId);
        }

        internal async Task LoadCommonDataAsync(int userId, int companyId, DateOnly today, DateOnly startOfWeek, DateOnly endOfWeek)
        {
            // Phase 2C: Parallelize independent queries using Task.WhenAll
            var nextShiftTask = _context.ShiftAssignments
                .Include(sa => sa.ShiftInstance)
                    .ThenInclude(si => si.ShiftType)
                .Include(sa => sa.User)
                .Where(sa => sa.UserId == userId && sa.CompanyId == companyId && sa.ShiftInstance.WorkDate >= today)
                .OrderBy(sa => sa.ShiftInstance.WorkDate)
                .FirstOrDefaultAsync();

            // A user owns all their notifications regardless of which company generated them.
            // Filtering by CompanyId would silently drop notifications from secondary companies
            // for multi-company members — consistent with bell widget and NotificationCenter.
            var notificationsTask = _context.UserNotifications
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .Take(5)
                .ToListAsync();

            var weekShiftsTask = _context.ShiftAssignments
                .Include(sa => sa.ShiftInstance)
                    .ThenInclude(si => si.ShiftType)
                .Where(sa => sa.UserId == userId && sa.CompanyId == companyId
                    && sa.ShiftInstance.WorkDate >= startOfWeek && sa.ShiftInstance.WorkDate < endOfWeek)
                .ToListAsync();

            // Wait for all queries to complete in parallel
            await Task.WhenAll(nextShiftTask, notificationsTask, weekShiftsTask);

            // Extract results
            NextShift = nextShiftTask.Result;
            var notifications = notificationsTask.Result;
            var weekShifts = weekShiftsTask.Result;

            RecentNotifications = notifications;
            UnreadNotificationsCount = notifications.Count(n => !n.IsRead);

            // Set-based classification: each of the 7 week days lands in exactly ONE disjoint
            // bucket (real shift / vacation / offline) by precedence. A day can carry BOTH an
            // approved vacation AND a HOME shift (HOME shifts are materialized from approved
            // TimeOffRequests — see ShiftAssignment.SourceTimeOffRequestId), so computing offline
            // via subtraction (7 - shifts - vacation) would double-count that day and could go
            // negative. HOME/OFFLINE are presence statuses, not real shifts, so they never claim
            // the shiftDay bucket.
            var weekDays = Enumerable.Range(0, 7).Select(i => startOfWeek.AddDays(i)).ToList();
            // Pre-computed as plain locals (not indexed inline) because EF Core compiles the
            // .Where() below into an expression tree, and the '^'/indexer Range syntax is not
            // legal inside an expression tree (CS8790/CS8791).
            var weekFirstDay = weekDays[0];
            var weekLastDay = weekDays[^1];

            var shiftDays = weekShifts
                .Where(sa => sa.ShiftInstance.ShiftType != null
                          && !sa.ShiftInstance.ShiftType.IsHome && !sa.ShiftInstance.ShiftType.IsOffline)
                .Select(sa => sa.ShiftInstance.WorkDate)
                .ToHashSet();

            // Approved vacation requests overlapping this week — fresh query scoped to the common
            // path. The TimeOffRequests reference in LoadEmployeeDataAsync is employee-only and has
            // no date filter, so it isn't reusable here.
            var approvedVac = await _context.TimeOffRequests
                .Where(r => r.UserId == userId && r.CompanyId == companyId && r.Status == RequestStatus.Approved
                         && r.StartDate <= weekLastDay && r.EndDate >= weekFirstDay)
                .Select(r => new { r.StartDate, r.EndDate })
                .ToListAsync();

            // Precedence: a real shift beats an overlapping vacation on the same day.
            var vacationDays = weekDays
                .Where(d => !shiftDays.Contains(d) && approvedVac.Any(v => v.StartDate <= d && v.EndDate >= d))
                .ToHashSet();

            ShiftsThisWeek = weekShifts.Count(sa => sa.ShiftInstance.ShiftType != null
                && !sa.ShiftInstance.ShiftType.IsHome && !sa.ShiftInstance.ShiftType.IsOffline);
            DaysWithShiftsThisWeek = shiftDays.Count;
            VacationDaysThisWeek = vacationDays.Count;
            OfflineDaysThisWeek = weekDays.Count(d => !shiftDays.Contains(d) && !vacationDays.Contains(d));
        }

        private async Task LoadEmployeeDataAsync(int userId, int companyId)
        {
            // My requests stats
            var requests = await _context.TimeOffRequests
                .Where(r => r.UserId == userId && r.CompanyId == companyId)
                .ToListAsync();

            PendingRequestsCount = requests.Count(r => r.Status == RequestStatus.Pending);
            ApprovedRequestsCount = requests.Count(r => r.Status == RequestStatus.Approved);
            DeclinedRequestsCount = requests.Count(r => r.Status == RequestStatus.Declined);
        }

        private async Task LoadManagerDataAsync(int companyId, DateOnly today)
        {
            var next30Days = today.AddDays(30);

            // Phase 2C: Parallelize independent queries using Task.WhenAll
            var shiftsInPeriodTask = _context.ShiftInstances
                .Where(si => si.CompanyId == companyId && si.WorkDate >= today && si.WorkDate < next30Days)
                .Include(si => si.ShiftType)
                .ToListAsync();

            var assignmentsTask = _context.ShiftAssignments
                .Where(sa => sa.CompanyId == companyId && sa.UserId != null && sa.ShiftInstance.WorkDate >= today && sa.ShiftInstance.WorkDate < next30Days)
                .ToListAsync();

            var pendingTimeOffTask = _context.TimeOffRequests
                .Where(r => r.CompanyId == companyId && r.Status == RequestStatus.Pending)
                .CountAsync();

            var pendingSwapsTask = _context.SwapRequests
                .Where(r => r.CompanyId == companyId && r.Status == RequestStatus.Pending)
                .CountAsync();

            // Wait for all queries to complete in parallel
            await Task.WhenAll(shiftsInPeriodTask, assignmentsTask, pendingTimeOffTask, pendingSwapsTask);

            // Extract results
            var shiftsInPeriod = shiftsInPeriodTask.Result;
            var assignments = assignmentsTask.Result;
            PendingTimeOffCount = pendingTimeOffTask.Result;
            PendingSwapsCount = pendingSwapsTask.Result;

            var assignmentsByShift = assignments.GroupBy(a => a.ShiftInstanceId).ToDictionary(g => g.Key, g => g.Count());

            UnassignedShiftsCount = shiftsInPeriod.Count(si =>
            {
                var assigned = assignmentsByShift.GetValueOrDefault(si.Id, 0);
                return assigned < si.StaffingRequired;
            });

            UnderstaffedDaysCount = shiftsInPeriod
                .Where(si =>
                {
                    var assigned = assignmentsByShift.GetValueOrDefault(si.Id, 0);
                    return assigned < si.StaffingRequired;
                })
                .Select(si => si.WorkDate)
                .Distinct()
                .Count();
        }

        /// <summary>
        /// PRG handler: saves the user's monitored shift selection for the Who-Is-On-Shift widget.
        /// Gated to EditArea or AdminAccess grant holders; silently redirects others.
        /// </summary>
        public async Task<IActionResult> OnPostSaveMonitoredShiftsAsync(List<int>? shiftTypeIds)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
                return RedirectToPage();

            var hasEditArea = await _grantService.HasGrantAsync(userId, "EditArea");
            var hasEditMolecule = await _grantService.HasGrantAsync(userId, "EditMolecule");
            var hasAdminAccess = await _grantService.HasGrantAsync(userId, "AdminAccess");
            if (!hasEditArea && !hasEditMolecule && !hasAdminAccess)
                return RedirectToPage();

            await _whoIsOnShiftService.SaveSelectedShiftsAsync(userId, shiftTypeIds ?? new List<int>());
            return RedirectToPage();
        }

        private async Task LoadDirectorDataAsync(int companyId, DateOnly today, int userId)
        {
            // Load manager data first
            await LoadManagerDataAsync(companyId, today);

            // Companies overview (for Directors/Owners)
            if (IsOwner)
            {
                var startOfMonth = new DateOnly(today.Year, today.Month, 1);
                var endOfMonth = startOfMonth.AddMonths(1);

                // SECURITY-AUDITED: IgnoreQueryFilters() below are SAFE — Owner-only analytics (gated by IsOwner check); aggregate counts only, no sensitive data
                // Phase 2C: Parallelize Owner analytics queries
                var totalCompaniesTask = _context.Companies.CountAsync();
                var totalUsersTask = _context.Users
                    .IgnoreQueryFilters()
                    .Where(u => u.IsActive)
                    .CountAsync();
                var totalShiftsTask = _context.ShiftInstances
                    .IgnoreQueryFilters()
                    .Where(si => si.WorkDate >= startOfMonth && si.WorkDate < endOfMonth)
                    .CountAsync();
                var shiftsThisMonthTask = _context.ShiftInstances
                    .IgnoreQueryFilters()
                    .Where(si => si.WorkDate >= startOfMonth && si.WorkDate < endOfMonth)
                    .ToListAsync();
                var assignmentsThisMonthTask = _context.ShiftAssignments
                    .IgnoreQueryFilters()
                    .Where(sa => sa.ShiftInstance.WorkDate >= startOfMonth && sa.ShiftInstance.WorkDate < endOfMonth)
                    .GroupBy(sa => sa.ShiftInstanceId)
                    .Select(g => new { ShiftInstanceId = g.Key, Count = g.Count() })
                    .ToListAsync();

                // Wait for all queries to complete in parallel
                await Task.WhenAll(totalCompaniesTask, totalUsersTask, totalShiftsTask, shiftsThisMonthTask, assignmentsThisMonthTask);

                // Extract results
                TotalCompaniesCount = totalCompaniesTask.Result;
                ActiveCompaniesCount = TotalCompaniesCount;
                TotalUsersCount = totalUsersTask.Result;
                TotalShiftsThisMonth = totalShiftsTask.Result;
                var shiftsThisMonth = shiftsThisMonthTask.Result;
                var assignmentsThisMonth = assignmentsThisMonthTask.Result;

                var assignmentDict = assignmentsThisMonth.ToDictionary(a => a.ShiftInstanceId, a => a.Count);

                if (shiftsThisMonth.Any())
                {
                    var totalRequired = shiftsThisMonth.Sum(s => s.StaffingRequired);
                    var totalAssigned = shiftsThisMonth.Sum(s => assignmentDict.GetValueOrDefault(s.Id, 0));
                    AverageStaffingRate = totalRequired > 0 ? (totalAssigned / (double)totalRequired) * 100 : 100;
                }
                else
                {
                    AverageStaffingRate = 100;
                }
            }
            else
            {
                // Directors see companies they manage
                var directorCompanyIds = await _context.DirectorCompanies
                    .Where(dc => dc.UserId == userId && !dc.IsDeleted)
                    .Select(dc => dc.CompanyId)
                    .ToListAsync();

                TotalCompaniesCount = directorCompanyIds.Count;
                ActiveCompaniesCount = TotalCompaniesCount; // All assigned companies are considered active
            }
        }
    }
}
