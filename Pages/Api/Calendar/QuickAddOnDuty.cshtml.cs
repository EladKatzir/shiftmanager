using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using System.Security.Claims;
using System.Text.Json;

namespace ShiftManager.Pages.Api.Calendar;

/// <summary>
/// ✅ PHASE 20: API endpoint to quickly create on-duty assignments from calendar views
/// </summary>
[Authorize(Policy = "CanEditOnDuty")]
[IgnoreAntiforgeryToken]
public class QuickAddOnDutyModel : PageModel
{
    private readonly IOnDutyService _onDutyService;
    private readonly INotificationService _notificationService;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<QuickAddOnDutyModel> _logger;

    public QuickAddOnDutyModel(
        IOnDutyService onDutyService,
        INotificationService notificationService,
        IAuditLogService auditLogService,
        ILogger<QuickAddOnDutyModel> logger)
    {
        _onDutyService = onDutyService;
        _notificationService = notificationService;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            // Log request start
            _logger.LogInformation("QuickAddOnDuty: Request received from {UserAgent}, Auth={IsAuth}",
                Request.Headers["User-Agent"].ToString(),
                User.Identity?.IsAuthenticated ?? false);

            // Parse JSON body
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            var data = JsonSerializer.Deserialize<CreateOnDutyRequest>(body, new JsonSerializerOptions
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

            // Validate notes length
            if (!string.IsNullOrWhiteSpace(data.Notes) && data.Notes.Length > 1000)
            {
                return new JsonResult(new { success = false, message = "Notes must not exceed 1000 characters" })
                {
                    StatusCode = 400
                };
            }

            // Parse and validate date
            if (!DateOnly.TryParse(data.Date, out var onDutyDate))
            {
                return new JsonResult(new { success = false, message = "Invalid date format" })
                {
                    StatusCode = 400
                };
            }

            // Validate date (prevent far future dates)
            if (onDutyDate > DateOnly.FromDateTime(DateTime.Today.AddYears(2)))
            {
                return new JsonResult(new { success = false, message = "Cannot create on-duty assignments more than 2 years in the future" })
                {
                    StatusCode = 400
                };
            }

            // Validate date (prevent past dates)
            if (onDutyDate < DateOnly.FromDateTime(DateTime.Today))
            {
                return new JsonResult(new { success = false, message = "Cannot create on-duty assignments in the past" })
                {
                    StatusCode = 400
                };
            }

            // Validate and parse on-duty type
            if (!Enum.IsDefined(typeof(OnDutyType), data.OnDutyType))
            {
                return new JsonResult(new { success = false, message = "Invalid on-duty type" })
                {
                    StatusCode = 400
                };
            }

            var onDutyType = (OnDutyType)data.OnDutyType;

            // Get current user
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var currentUserId))
            {
                _logger.LogWarning("QuickAddOnDuty: User not authenticated - IsAuth={IsAuth}, NameId={NameId}",
                    User.Identity?.IsAuthenticated ?? false,
                    userIdClaim ?? "null");
                return new JsonResult(new { success = false, message = "User not authenticated" })
                {
                    StatusCode = 401
                };
            }

            // Check if user has permission
            if (!await _onDutyService.CanUserManageOnDutyAsync(currentUserId))
            {
                _logger.LogWarning("SECURITY: User {UserId} attempted to create on-duty without permission", currentUserId);
                return new JsonResult(new { success = false, message = "You do not have permission to create on-duty assignments" })
                {
                    StatusCode = 403
                };
            }

            // Attempt to create the on-duty assignment
            var result = await _onDutyService.CreateOnDutyAsync(
                assigneeId: data.AssigneeId,
                date: onDutyDate,
                type: onDutyType,
                notes: data.Notes,
                forceAssign: data.ForceAssign);

            if (!result.Success)
            {
                // Check if it's a vacation conflict
                if (result.Message.StartsWith("VACATION_CONFLICT"))
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
            await _notificationService.CreateOnDutyAssignedNotificationAsync(
                userId: data.AssigneeId,
                onDutyType: onDutyType,
                onDutyDate: onDutyDate,
                onDutyId: result.OnDuty!.Id);

            await _auditLogService.LogAsync(
                action: "OnDutyCreatedQuick",
                entityType: "OnDuty",
                entityId: result.OnDuty.Id,
                description: $"Created on-duty {onDutyType} for user {data.AssigneeId} on {onDutyDate:yyyy-MM-dd} via calendar quick-add",
                details: JsonSerializer.Serialize(new
                {
                    OnDutyId = result.OnDuty.Id,
                    AssigneeId = data.AssigneeId,
                    OnDutyDate = onDutyDate,
                    OnDutyType = onDutyType,
                    OnDutyNotes = data.Notes,
                    Source = "CalendarQuickAdd"
                }));

            _logger.LogInformation("On-duty created via quick-add: OnDutyId={OnDutyId}, UserId={UserId}, AssigneeId={AssigneeId}, Type={Type}, Date={Date}",
                result.OnDuty.Id, currentUserId, data.AssigneeId, onDutyType, onDutyDate);

            return new JsonResult(new
            {
                success = true,
                onDutyId = result.OnDuty.Id,
                message = "On-duty assignment created successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating on-duty via quick-add");
            return new JsonResult(new { success = false, message = "An error occurred while creating the on-duty assignment" })
            {
                StatusCode = 500
            };
        }
    }

    private class CreateOnDutyRequest
    {
        public int AssigneeId { get; set; }
        public string Date { get; set; } = string.Empty;
        public int OnDutyType { get; set; }
        public string? Notes { get; set; }
        public bool ForceAssign { get; set; } = false;
    }
}
