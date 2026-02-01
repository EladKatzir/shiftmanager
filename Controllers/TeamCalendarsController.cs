using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShiftManager.Services;
using ShiftManager.Models.Support;
using System.Security.Claims;

namespace ShiftManager.Controllers;

/// <summary>
/// API controller for My Team Calendars feature.
/// Provides REST endpoints for calendar management, member management, and week view data.
/// </summary>
[Authorize]
[ApiController]
[Route("api/team-calendars")]
public class TeamCalendarsController : ControllerBase
{
    private readonly TeamCalendarService _calendarService;
    private readonly TeamCalendarEventAggregator _eventAggregator;

    public TeamCalendarsController(
        TeamCalendarService calendarService,
        TeamCalendarEventAggregator eventAggregator)
    {
        _calendarService = calendarService;
        _eventAggregator = eventAggregator;
    }

    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.Parse(userIdClaim ?? "0");
    }

    private UserRole GetCurrentUserRole()
    {
        var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value;
        return Enum.TryParse<UserRole>(roleClaim, out var role) ? role : UserRole.Employee;
    }

    #region Calendar Management

    /// <summary>
    /// GET /api/team-calendars
    /// Gets all calendars owned by the current user.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetMyCalendars()
    {
        var userId = GetCurrentUserId();
        var calendars = await _calendarService.GetCalendarsForOwnerAsync(userId);

        var result = calendars.Select(c => new
        {
            c.Id,
            c.Name,
            c.CreatedAt,
            c.UpdatedAt,
            MemberCount = c.Members.Count
        });

        return Ok(result);
    }

    /// <summary>
    /// GET /api/team-calendars/{id}
    /// Gets a specific calendar with its members.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetCalendar(int id)
    {
        var userId = GetCurrentUserId();
        var calendar = await _calendarService.GetCalendarByIdAsync(id, userId);

        if (calendar == null)
        {
            return NotFound(new { error = "Calendar not found" });
        }

        var result = new
        {
            calendar.Id,
            calendar.Name,
            calendar.CreatedAt,
            calendar.UpdatedAt,
            Members = calendar.Members.Select(m => new
            {
                m.MemberUserId,
                m.Member.DisplayName,
                m.AddedAt
            }).OrderBy(m => m.DisplayName)
        };

        return Ok(result);
    }

    /// <summary>
    /// POST /api/team-calendars
    /// Creates a new calendar.
    /// Body: { "name": "My Team" }
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateCalendar([FromBody] CreateCalendarRequest request)
    {
        var userId = GetCurrentUserId();
        var (success, calendar, errorMessage) = await _calendarService.CreateCalendarAsync(userId, request.Name);

        if (!success || calendar == null)
        {
            return BadRequest(new { error = errorMessage });
        }

        return CreatedAtAction(
            nameof(GetCalendar),
            new { id = calendar.Id },
            new
            {
                calendar.Id,
                calendar.Name,
                calendar.CreatedAt,
                calendar.UpdatedAt,
                MemberCount = 0
            });
    }

    /// <summary>
    /// PUT /api/team-calendars/{id}
    /// Renames a calendar.
    /// Body: { "name": "New Name" }
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> RenameCalendar(int id, [FromBody] RenameCalendarRequest request)
    {
        var userId = GetCurrentUserId();
        var (success, errorMessage) = await _calendarService.RenameCalendarAsync(id, userId, request.Name);

        if (!success)
        {
            return BadRequest(new { error = errorMessage });
        }

        return Ok(new { message = "Calendar renamed successfully" });
    }

    /// <summary>
    /// DELETE /api/team-calendars/{id}
    /// Soft deletes a calendar.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCalendar(int id)
    {
        var userId = GetCurrentUserId();
        var (success, errorMessage) = await _calendarService.DeleteCalendarAsync(id, userId);

        if (!success)
        {
            return BadRequest(new { error = errorMessage });
        }

        return Ok(new { message = "Calendar deleted successfully" });
    }

    #endregion

    #region Member Management

    /// <summary>
    /// GET /api/team-calendars/{id}/members
    /// Gets current members and available users for a calendar.
    /// </summary>
    [HttpGet("{id}/members")]
    public async Task<IActionResult> GetMembers(int id)
    {
        var userId = GetCurrentUserId();

        var currentMembers = await _calendarService.GetCalendarMembersAsync(id, userId);
        var availableUsers = await _calendarService.GetAvailableUsersAsync(id, userId);

        return Ok(new
        {
            CurrentMembers = currentMembers.Select(u => new
            {
                u.Id,
                u.DisplayName,
                u.Email
            }),
            AvailableUsers = availableUsers.Select(u => new
            {
                u.Id,
                u.DisplayName,
                u.Email
            })
        });
    }

    /// <summary>
    /// PUT /api/team-calendars/{id}/members
    /// Replaces all members with a new set.
    /// Body: { "memberUserIds": [1, 2, 3] }
    /// </summary>
    [HttpPut("{id}/members")]
    public async Task<IActionResult> SetMembers(int id, [FromBody] SetMembersRequest request)
    {
        var userId = GetCurrentUserId();
        var (success, errorMessage) = await _calendarService.SetMembersAsync(id, userId, request.MemberUserIds);

        if (!success)
        {
            return BadRequest(new { error = errorMessage });
        }

        return Ok(new { message = "Members updated successfully" });
    }

    #endregion

    #region Week View

    /// <summary>
    /// GET /api/team-calendars/{id}/week?date=2025-11-10
    /// Gets the week view for a calendar starting at the given date (should be a Sunday).
    /// Returns member statuses for the entire week.
    /// </summary>
    [HttpGet("{id}/week")]
    public async Task<IActionResult> GetWeekView(int id, [FromQuery] string? date)
    {
        var userId = GetCurrentUserId();
        var userRole = GetCurrentUserRole();

        // Verify ownership
        var calendar = await _calendarService.GetCalendarByIdAsync(id, userId);
        if (calendar == null)
        {
            return NotFound(new { error = "Calendar not found" });
        }

        // Parse date or default to current week's Sunday
        DateOnly weekStart;
        if (!string.IsNullOrEmpty(date) && DateOnly.TryParse(date, out var parsedDate))
        {
            weekStart = parsedDate;
        }
        else
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var daysSinceSunday = ((int)today.DayOfWeek);
            weekStart = today.AddDays(-daysSinceSunday);
        }

        // Get member IDs
        var memberUserIds = calendar.Members.Select(m => m.MemberUserId).ToList();

        if (memberUserIds.Count == 0)
        {
            return Ok(new
            {
                WeekStart = weekStart.ToString("yyyy-MM-dd"),
                Members = new object[] { }
            });
        }

        // Get week data
        var weekData = await _eventAggregator.GetWeekViewAsync(memberUserIds, weekStart);

        // Build response with role-based targetUrl
        var members = calendar.Members
            .OrderBy(m => m.Member.DisplayName)
            .Select(m =>
            {
                var userWeek = weekData.GetValueOrDefault(m.MemberUserId);

                var days = Enumerable.Range(0, 7).Select(i =>
                {
                    var dayOfWeek = (DayOfWeek)i;

                    if (userWeek == null)
                    {
                        return new
                        {
                            DayOfWeek = dayOfWeek.ToString(),
                            Type = "Free",
                            Label = "Free",
                            TimeRange = (string?)null,
                            TargetUrl = (string?)null
                        };
                    }

                    var dayStatus = userWeek.GetValueOrDefault(dayOfWeek);

                    if (dayStatus == null)
                    {
                        return new
                        {
                            DayOfWeek = dayOfWeek.ToString(),
                            Type = "Free",
                            Label = "Free",
                            TimeRange = (string?)null,
                            TargetUrl = (string?)null
                        };
                    }

                    // Apply role-based targetUrl
                    var targetUrl = GetTargetUrlForRole(dayStatus.TargetUrl, userRole);

                    return new
                    {
                        DayOfWeek = dayOfWeek.ToString(),
                        Type = dayStatus.Type.ToString(),
                        Label = dayStatus.Label,
                        TimeRange = dayStatus.TimeRange,
                        TargetUrl = targetUrl
                    };
                }).ToList();

                return new
                {
                    UserId = m.MemberUserId,
                    DisplayName = m.Member.DisplayName,
                    Days = days
                };
            }).ToList();

        return Ok(new
        {
            WeekStart = weekStart.ToString("yyyy-MM-dd"),
            Members = members
        });
    }

    /// <summary>
    /// Determines the actual target URL based on user role and permissions.
    /// Regular employees can only navigate to their own pages.
    /// </summary>
    /// <summary>
    /// Applies role-based access control for My Team calendar navigation.
    /// Based on the spec role matrix:
    /// - Regular users: no clicks work
    /// - Assigners: can only click chores
    /// - Manager/Director/Owner: can click everything
    /// </summary>
    private string? GetTargetUrlForRole(string? baseUrl, UserRole role)
    {
        if (string.IsNullOrEmpty(baseUrl))
        {
            return null;
        }

        // Manager, Director, Owner: full access to all navigation
        if (role == UserRole.Manager || role == UserRole.Director || role == UserRole.Owner)
        {
            return baseUrl;
        }

        // Assigner: can only navigate to chores
        if (role == UserRole.Assigner)
        {
            // Only allow navigation to chores page
            if (baseUrl.StartsWith("/Public/Chores", StringComparison.OrdinalIgnoreCase))
            {
                return baseUrl;
            }
            return null; // Block all other navigation for assigners
        }

        // Regular Employee and Trainee: no navigation allowed in My Team view
        return null;
    }

    #endregion

    #region Request Models

    public class CreateCalendarRequest
    {
        public string Name { get; set; } = string.Empty;
    }

    public class RenameCalendarRequest
    {
        public string Name { get; set; } = string.Empty;
    }

    public class SetMembersRequest
    {
        public List<int> MemberUserIds { get; set; } = new();
    }

    #endregion
}
