using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;
using System.Text.Json;

namespace ShiftManager.Pages.Api.Calendar;

/// <summary>
/// ✅ PHASE 20: API endpoint to quickly create on-duty assignments from calendar views
/// </summary>
// 2026-04-15: widened from [Authorize(Policy = "Grant:ManageOnDuty")] to plain [Authorize]
// so EditOnCallCalendar-grant holders (any hakam-eligible user) can reach the handler.
// The in-handler CanUserManageOnDutyAsync check is the real auth gate (OR chain of
// AssignHakamDuties | AssignKatzinDuties | ManageOnDuty | EditOnCallCalendar).
[Authorize]
[IgnoreAntiforgeryToken]
public class QuickAddOnDutyModel : PageModel
{
    private readonly IOnDutyService _onDutyService;
    private readonly INotificationService _notificationService;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<QuickAddOnDutyModel> _logger;
    private readonly IStringLocalizer<SharedResources> _localizer;

    public QuickAddOnDutyModel(
        IOnDutyService onDutyService,
        INotificationService notificationService,
        IAuditLogService auditLogService,
        ILogger<QuickAddOnDutyModel> logger,
        IStringLocalizer<SharedResources> localizer)
    {
        _onDutyService = onDutyService;
        _notificationService = notificationService;
        _auditLogService = auditLogService;
        _logger = logger;
        _localizer = localizer;
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
                return new JsonResult(new { success = false, message = _localizer["QuickAddOnDuty_InvalidRequestData"].Value })
                {
                    StatusCode = 400
                };
            }

            if (data.AssigneeId <= 0)
            {
                return new JsonResult(new { success = false, message = _localizer["QuickAddOnDuty_InvalidAssignee"].Value })
                {
                    StatusCode = 400
                };
            }

            // Validate notes length
            if (!string.IsNullOrWhiteSpace(data.Notes) && data.Notes.Length > 1000)
            {
                return new JsonResult(new { success = false, message = _localizer["QuickAddOnDuty_NotesTooLong"].Value })
                {
                    StatusCode = 400
                };
            }

            // Parse and validate date
            if (!DateOnly.TryParse(data.Date, out var onDutyDate))
            {
                return new JsonResult(new { success = false, message = _localizer["QuickAddOnDuty_InvalidDateFormat"].Value })
                {
                    StatusCode = 400
                };
            }

            // Validate date (prevent far future dates)
            if (onDutyDate > DateOnly.FromDateTime(DateTime.Today.AddYears(2)))
            {
                return new JsonResult(new { success = false, message = _localizer["QuickAddOnDuty_DateTooFarFuture"].Value })
                {
                    StatusCode = 400
                };
            }

            // Validate date (prevent past dates)
            if (onDutyDate < DateOnly.FromDateTime(DateTime.Today))
            {
                return new JsonResult(new { success = false, message = _localizer["QuickAddOnDuty_DateInPast"].Value })
                {
                    StatusCode = 400
                };
            }

            // Validate on-duty type: accept built-in enum values AND custom types from OnDutyTypeConfig
            if (!Enum.IsDefined(typeof(OnDutyType), data.OnDutyType))
            {
                // Check if it's a valid custom duty type
                var isCustomType = await _onDutyService.IsValidDutyTypeAsync(data.OnDutyType);
                if (!isCustomType)
                {
                    return new JsonResult(new { success = false, message = _localizer["QuickAddOnDuty_InvalidType"].Value })
                    {
                        StatusCode = 400
                    };
                }
            }

            var onDutyType = (OnDutyType)data.OnDutyType;

            // Get current user
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var currentUserId))
            {
                _logger.LogWarning("QuickAddOnDuty: User not authenticated - IsAuth={IsAuth}, NameId={NameId}",
                    User.Identity?.IsAuthenticated ?? false,
                    userIdClaim ?? "null");
                return new JsonResult(new { success = false, message = _localizer["QuickAddOnDuty_UserNotAuthenticated"].Value })
                {
                    StatusCode = 401
                };
            }

            // Check if user has permission
            if (!await _onDutyService.CanUserManageOnDutyAsync(currentUserId))
            {
                _logger.LogWarning("SECURITY: User {UserId} attempted to create on-duty without permission", currentUserId);
                return new JsonResult(new { success = false, message = _localizer["QuickAddOnDuty_NoPermissionCreate"].Value })
                {
                    StatusCode = 403
                };
            }

            // SECURITY-AUDITED: MoleculeId for token scoping. Authority order:
            //   1) Explicit POST body field — Justice "Make it real" supplies this so the HMAC
            //      override-token canonical (BusyService.TargetCanonical for OnDuty) matches what
            //      Justice's eligibility handler signed. Without this, override flow silently fails
            //      because the token's canonical includes moleculeId.
            //   2) Assignee's company → molecule (fallback for callers without molecule context, e.g.
            //      bottom sheet, quick entry).
            // The moleculeId value does NOT control any data access — it is used ONLY as a
            // token-scoping parameter for HMAC validation. The OnDuty entity has no MoleculeId column.
            int? moleculeId = data.MoleculeId;
            if (moleculeId == null)
            {
                try
                {
                    using var scope = HttpContext.RequestServices.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ShiftManager.Data.AppDbContext>();
                    var assignee = await db.Users.AsNoTracking()
                        .Where(u => u.Id == data.AssigneeId)
                        .Select(u => new { u.CompanyId })
                        .FirstOrDefaultAsync();
                    if (assignee != null)
                    {
                        var company = await db.Companies.AsNoTracking()
                            .Where(c => c.Id == assignee.CompanyId)
                            .Select(c => new { c.MoleculeId })
                            .FirstOrDefaultAsync();
                        moleculeId = company?.MoleculeId;
                    }
                }
                catch { /* moleculeId stays null; OnDuty target accepts 0 */ }
            }

            // Attempt to create the on-duty assignment via unified envelope
            var result = await _onDutyService.CreateOnDutyAsync(
                assigneeId: data.AssigneeId,
                date: onDutyDate,
                type: onDutyType,
                notes: data.Notes,
                forceAssign: false,
                moleculeId: moleculeId,
                overrideToken: data.OverrideToken);

            if (!result.Success)
            {
                if (result.Message == "BUSY_OVERRIDE_REQUIRED" && result.Validation != null)
                {
                    return new JsonResult(new
                    {
                        success = false,
                        requiresOverride = true,
                        warnings = result.Validation.Warnings.Select(w => new
                        {
                            w.Key,
                            w.Message,
                            Category = w.Category.ToString(),
                            w.Detail
                        }),
                        overrideToken = result.OverrideToken
                    });
                }

                if (result.Message == "OFFICER_RANK_REQUIRED")
                {
                    return new JsonResult(new
                    {
                        success = false,
                        error = "OFFICER_RANK_REQUIRED",
                        message = _localizer["QuickAddOnDuty_OfficerRankRequired"].Value
                    })
                    {
                        StatusCode = 403
                    };
                }

                return new JsonResult(new { success = false, message = result.Message })
                {
                    StatusCode = 400
                };
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
                message = _localizer["QuickAddOnDuty_Success"].Value
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating on-duty via quick-add");
            return new JsonResult(new { success = false, message = _localizer["QuickAddOnDuty_Error"].Value })
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
        public string? OverrideToken { get; set; }

        // Phase 2d: Justice "Make it real" supplies this so the override token canonical matches.
        // Optional for non-Justice callers — they fall back to assignee-company resolution.
        public int? MoleculeId { get; set; }
    }
}
