using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShiftManager.Services;
using System.Security.Claims;
using System.Text.Json;

namespace ShiftManager.Pages.Api.Calendar;

/// <summary>
/// ✅ PHASE 20: API endpoint to quickly create chores from calendar views
/// </summary>
[Authorize(Policy = "Grant:AssignChores")]
[IgnoreAntiforgeryToken]
public class QuickAddChoreModel : PageModel
{
    private readonly IChoreService _choreService;
    private readonly INotificationService _notificationService;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<QuickAddChoreModel> _logger;

    public QuickAddChoreModel(
        IChoreService choreService,
        INotificationService notificationService,
        IAuditLogService auditLogService,
        ILogger<QuickAddChoreModel> logger)
    {
        _choreService = choreService;
        _notificationService = notificationService;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            // Log request start
            _logger.LogInformation("QuickAddChore: Request received from {UserAgent}, Auth={IsAuth}",
                Request.Headers["User-Agent"].ToString(),
                User.Identity?.IsAuthenticated ?? false);

            // Parse JSON body
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            var data = JsonSerializer.Deserialize<CreateChoreRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            // Validate input
            if (data == null)
            {
                return new JsonResult(new { success = false, message = "Invalid request data" })
                {
                    StatusCode = 400
                };
            }

            if (data.AssigneeId <= 0)
            {
                return new JsonResult(new { success = false, message = "Invalid assignee" })
                {
                    StatusCode = 400
                };
            }

            if (string.IsNullOrWhiteSpace(data.Title))
            {
                return new JsonResult(new { success = false, message = "Chore title is required" })
                {
                    StatusCode = 400
                };
            }

            // Validate title length (prevent DoS and database errors)
            if (data.Title.Length > 200)
            {
                return new JsonResult(new { success = false, message = "Chore title must not exceed 200 characters" })
                {
                    StatusCode = 400
                };
            }

            // Validate notes length
            if (!string.IsNullOrWhiteSpace(data.Notes) && data.Notes.Length > 1000)
            {
                return new JsonResult(new { success = false, message = "Chore notes must not exceed 1000 characters" })
                {
                    StatusCode = 400
                };
            }

            // Parse and validate date
            if (!DateOnly.TryParse(data.Date, out var choreDate))
            {
                return new JsonResult(new { success = false, message = "Invalid date format" })
                {
                    StatusCode = 400
                };
            }

            // Validate date (prevent far future dates)
            if (choreDate > DateOnly.FromDateTime(DateTime.Today.AddYears(2)))
            {
                return new JsonResult(new { success = false, message = "Cannot create chores more than 2 years in the future" })
                {
                    StatusCode = 400
                };
            }

            // Validate date (prevent past dates)
            if (choreDate < DateOnly.FromDateTime(DateTime.Today))
            {
                return new JsonResult(new { success = false, message = "Cannot create chores in the past" })
                {
                    StatusCode = 400
                };
            }

            // Get current user
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var currentUserId))
            {
                _logger.LogWarning("QuickAddChore: User not authenticated - IsAuth={IsAuth}, NameId={NameId}",
                    User.Identity?.IsAuthenticated ?? false,
                    userIdClaim ?? "null");
                return new JsonResult(new { success = false, message = "User not authenticated" })
                {
                    StatusCode = 401
                };
            }

            // Check if user has permission
            if (!await _choreService.CanUserManageChoresAsync(currentUserId))
            {
                _logger.LogWarning("SECURITY: User {UserId} attempted to create chore without permission", currentUserId);
                return new JsonResult(new { success = false, message = "You do not have permission to create chores" })
                {
                    StatusCode = 403
                };
            }

            // Check if assignee is valid
            if (!await _choreService.CanUserManageChoreForAssigneeAsync(currentUserId, data.AssigneeId))
            {
                _logger.LogWarning("SECURITY: User {UserId} attempted to assign chore to unauthorized user {AssigneeId}",
                    currentUserId, data.AssigneeId);
                return new JsonResult(new { success = false, message = "You cannot assign chores to this user" })
                {
                    StatusCode = 403
                };
            }

            // Attempt to create the chore
            var result = await _choreService.CreateChoreAsync(
                assigneeId: data.AssigneeId,
                date: choreDate,
                title: data.Title,
                notes: data.Notes,
                forceAssign: data.ForceAssign,
                moleculeId: data.MoleculeId,
                choreTypeId: data.ChoreTypeId);

            if (!result.Success)
            {
                // Check if it's a shift conflict
                if (result.Message == "SHIFT_CONFLICT")
                {
                    return new JsonResult(new
                    {
                        success = false,
                        message = "This user has a shift on this date. Please use the Chores page to replace the shift."
                    })
                    {
                        StatusCode = 409 // Conflict
                    };
                }
                // Check if it's a vacation conflict
                else if (result.Message.StartsWith("VACATION_CONFLICT"))
                {
                    // Parse vacation details from message: "VACATION_CONFLICT|startDate|endDate|type"
                    var parts = result.Message.Split('|');
                    if (parts.Length == 4)
                    {
                        return new JsonResult(new
                        {
                            success = false,
                            conflictType = "vacation",
                            message = "This user has an approved vacation on this date.",
                            vacationStart = parts[1],
                            vacationEnd = parts[2],
                            vacationType = parts[3]
                        })
                        {
                            StatusCode = 409 // Conflict
                        };
                    }
                    else
                    {
                        return new JsonResult(new
                        {
                            success = false,
                            conflictType = "vacation",
                            message = "This user has an approved vacation on this date."
                        })
                        {
                            StatusCode = 409 // Conflict
                        };
                    }
                }
                else
                {
                    return new JsonResult(new { success = false, message = result.Message })
                    {
                        StatusCode = 400
                    };
                }
            }

            // Success - send notification and audit log
            await _notificationService.CreateChoreAssignedNotificationAsync(
                data.AssigneeId,
                data.Title,
                choreDate,
                result.Chore!.Id);

            await _auditLogService.LogAsync(
                action: "ChoreCreatedQuick",
                entityType: "Chore",
                entityId: result.Chore.Id,
                description: $"Created chore '{data.Title}' for user {data.AssigneeId} on {choreDate:yyyy-MM-dd} via calendar quick-add",
                details: JsonSerializer.Serialize(new
                {
                    ChoreId = result.Chore.Id,
                    AssigneeId = data.AssigneeId,
                    ChoreDate = choreDate,
                    ChoreTitle = data.Title,
                    ChoreNotes = data.Notes,
                    Source = "CalendarQuickAdd"
                }));

            _logger.LogInformation("Chore created via quick-add: ChoreId={ChoreId}, UserId={UserId}, AssigneeId={AssigneeId}, Date={Date}",
                result.Chore.Id, currentUserId, data.AssigneeId, choreDate);

            return new JsonResult(new
            {
                success = true,
                choreId = result.Chore.Id,
                message = "Chore created successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating chore via quick-add");
            return new JsonResult(new { success = false, message = "An error occurred while creating the chore" })
            {
                StatusCode = 500
            };
        }
    }

    private class CreateChoreRequest
    {
        public int AssigneeId { get; set; }
        public string Date { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public bool ForceAssign { get; set; } = false;
        public int? MoleculeId { get; set; }
        public int? ChoreTypeId { get; set; }
    }
}
