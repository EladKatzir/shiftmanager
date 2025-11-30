using Microsoft.AspNetCore.Authorization;
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

        public IndexModel(AppDbContext context, ICompanyContext companyContext)
        {
            _context = context;
            _companyContext = companyContext;
        }

        // Common properties (all users)
        public ShiftAssignment? NextShift { get; set; }
        public int UnreadNotificationsCount { get; set; }
        public List<UserNotification> RecentNotifications { get; set; } = new();
        public double HoursThisWeek { get; set; }
        public int DaysWithShiftsThisWeek { get; set; }
        public int DaysOffThisWeek { get; set; }

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

        public async Task OnGetAsync()
        {
            UserName = User.FindFirst(ClaimTypes.Name)?.Value ?? "";
            IsAdmin = User.IsInRole("Owner") || User.IsInRole("Manager") || User.IsInRole("Director");
            IsDirector = User.IsInRole("Owner") || User.IsInRole("Director");
            IsOwner = User.IsInRole("Owner");

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
            {
                return;
            }

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
        }

        private async Task LoadCommonDataAsync(int userId, int companyId, DateOnly today, DateOnly startOfWeek, DateOnly endOfWeek)
        {
            // Next shift
            NextShift = await _context.ShiftAssignments
                .Include(sa => sa.ShiftInstance)
                    .ThenInclude(si => si.ShiftType)
                .Include(sa => sa.User)
                .Where(sa => sa.UserId == userId && sa.CompanyId == companyId && sa.ShiftInstance.WorkDate >= today)
                .OrderBy(sa => sa.ShiftInstance.WorkDate)
                .FirstOrDefaultAsync();

            // Notifications
            var notifications = await _context.UserNotifications
                .Where(n => n.UserId == userId && n.CompanyId == companyId)
                .OrderByDescending(n => n.CreatedAt)
                .Take(5)
                .ToListAsync();

            RecentNotifications = notifications;
            UnreadNotificationsCount = notifications.Count(n => !n.IsRead);

            // Weekly stats
            var weekShifts = await _context.ShiftAssignments
                .Include(sa => sa.ShiftInstance)
                    .ThenInclude(si => si.ShiftType)
                .Where(sa => sa.UserId == userId && sa.CompanyId == companyId
                    && sa.ShiftInstance.WorkDate >= startOfWeek && sa.ShiftInstance.WorkDate < endOfWeek)
                .ToListAsync();

            HoursThisWeek = weekShifts.Sum(sa =>
            {
                var shiftType = sa.ShiftInstance.ShiftType;
                if (shiftType != null)
                {
                    var duration = shiftType.End - shiftType.Start;
                    return duration.TotalHours;
                }
                return 0;
            });

            DaysWithShiftsThisWeek = weekShifts.Select(sa => sa.ShiftInstance.WorkDate).Distinct().Count();
            DaysOffThisWeek = 7 - DaysWithShiftsThisWeek;
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

            // Staffing overview - shifts with slots needed but not filled
            var shiftsInPeriod = await _context.ShiftInstances
                .Where(si => si.CompanyId == companyId && si.WorkDate >= today && si.WorkDate < next30Days)
                .Include(si => si.ShiftType)
                .ToListAsync();

            var assignments = await _context.ShiftAssignments
                .Where(sa => sa.CompanyId == companyId && sa.ShiftInstance.WorkDate >= today && sa.ShiftInstance.WorkDate < next30Days)
                .ToListAsync();

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

            // Approvals
            PendingTimeOffCount = await _context.TimeOffRequests
                .Where(r => r.CompanyId == companyId && r.Status == RequestStatus.Pending)
                .CountAsync();

            PendingSwapsCount = await _context.SwapRequests
                .Where(r => r.CompanyId == companyId && r.Status == RequestStatus.Pending)
                .CountAsync();
        }

        private async Task LoadDirectorDataAsync(int companyId, DateOnly today, int userId)
        {
            // Load manager data first
            await LoadManagerDataAsync(companyId, today);

            // Companies overview (for Directors/Owners)
            if (IsOwner)
            {
                TotalCompaniesCount = await _context.Companies.CountAsync();
                ActiveCompaniesCount = TotalCompaniesCount; // All companies are considered active

                // ✅ PHASE 18: Load analytics summary for Owner
                // Total active users across all companies
                TotalUsersCount = await _context.Users
                    .IgnoreQueryFilters()
                    .Where(u => u.IsActive)
                    .CountAsync();

                // Total shifts this month across all companies
                var startOfMonth = new DateOnly(today.Year, today.Month, 1);
                var endOfMonth = startOfMonth.AddMonths(1);

                TotalShiftsThisMonth = await _context.ShiftInstances
                    .IgnoreQueryFilters()
                    .Where(si => si.WorkDate >= startOfMonth && si.WorkDate < endOfMonth)
                    .CountAsync();

                // Calculate average staffing rate across all companies this month
                var shiftsThisMonth = await _context.ShiftInstances
                    .IgnoreQueryFilters()
                    .Where(si => si.WorkDate >= startOfMonth && si.WorkDate < endOfMonth)
                    .ToListAsync();

                var assignmentsThisMonth = await _context.ShiftAssignments
                    .IgnoreQueryFilters()
                    .Where(sa => sa.ShiftInstance.WorkDate >= startOfMonth && sa.ShiftInstance.WorkDate < endOfMonth)
                    .GroupBy(sa => sa.ShiftInstanceId)
                    .Select(g => new { ShiftInstanceId = g.Key, Count = g.Count() })
                    .ToListAsync();

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
