using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Hubs;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;
using System.Text.Json;

namespace ShiftManager.Pages.Api.Calendar;

// Deletes ONE day note, named by id. Sibling of QuickAddDayNote, following the same convention as
// QuickAddChore/DeleteChore, QuickAddOnDuty/DeleteOnDuty and QuickAddTextEntry/DeleteTextEntry —
// deletion is always its own page here, never a second handler on the add page.
//
// This replaces "post empty text to QuickAddDayNote", which let any holder of the broad
// WriteOverviewNotes grant (seeded to the Employee template) silently clear a colleague's note with
// no ownership check, and recorded the deletion with entityId: 0 so it was unattributable. See
// docs/superpowers/audit/cluster-1-calendars.md:644-647.
//
// Covered by ApiAuthenticationMiddleware's "/Api/Calendar" prefix (IsInternalWebUiEndpoint), so it
// needs no middleware entry; and no Program.cs AllowAnonymousToPage entry, because it is [Authorize].
[Authorize]
[IgnoreAntiforgeryToken]
public class DeleteDayNoteModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ICalendarDayNoteService _dayNoteService;
    private readonly IAuditLogService _auditLogService;
    private readonly IGrantService _grantService;
    private readonly ICalendarNotificationService _notificationService;
    private readonly ITenantResolver _tenantResolver;
    private readonly ILogger<DeleteDayNoteModel> _logger;
    private readonly IStringLocalizer<SharedResources> _localizer;

    public DeleteDayNoteModel(
        AppDbContext db,
        ICalendarDayNoteService dayNoteService,
        IAuditLogService auditLogService,
        IGrantService grantService,
        ICalendarNotificationService notificationService,
        ITenantResolver tenantResolver,
        ILogger<DeleteDayNoteModel> logger,
        IStringLocalizer<SharedResources> localizer)
    {
        _db = db;
        _dayNoteService = dayNoteService;
        _auditLogService = auditLogService;
        _grantService = grantService;
        _notificationService = notificationService;
        _tenantResolver = tenantResolver;
        _logger = logger;
        _localizer = localizer;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            var data = JsonSerializer.Deserialize<DeleteDayNoteRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (data == null || data.NoteId <= 0)
            {
                return Json(400, "A note id is required");
            }

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var currentUserId))
            {
                return Json(401, "User not authenticated");
            }

            // SECURITY: the same baseline note gate the write side uses.
            if (!await _grantService.HasCalendarNotePermissionAsync(currentUserId))
            {
                _logger.LogWarning("SECURITY: User {UserId} attempted to delete day note {NoteId} without note permission",
                    currentUserId, data.NoteId);
                return Json(403, "You do not have permission to delete notes");
            }

            var note = await _dayNoteService.GetByIdAsync(data.NoteId);
            if (note == null)
            {
                // Also the double-click path: the client treats 404 as success so a second click does
                // not raise an error toast at the user.
                return Json(404, "Note not found");
            }

            // SECURITY — THIS GUARD IS LOAD-BEARING, NOT TIDINESS.
            // MoleculeId and JobTypeId are both nullable, and legacy un-keyed rows (MoleculeId NULL)
            // are deliberately preserved. Passing those nulls into HasGrantWithScopeAsync below would
            // produce an ALL-NULL scope, and GrantService's project, area and molecule branches each
            // return true for any holder in their own hierarchy — degenerating the ownership rule into
            // "any assigner anywhere may delete it". Reject before the grant call, never after.
            // Pinned by DeleteDayNoteTests.LegacyNoteWithNullMolecule_IsNotFound_AndNoGrantIsConsulted.
            if (note.MoleculeId == null)
            {
                _logger.LogWarning("SECURITY: User {UserId} attempted to delete day note {NoteId}, which belongs to no calendar",
                    currentUserId, data.NoteId);
                return Json(404, "Note not found");
            }

            // Resolve the caller's own molecule from the SWITCHER-AWARE active desk, exactly as
            // QuickAddDayNote does — not from the raw CompanyId claim.
            var companyId = _tenantResolver.GetCurrentTenantId();
            if (companyId <= 0)
            {
                return Json(400, "No active company context");
            }

            // SECURITY-AUDITED: SAFE — reads only the caller's own active desk's MoleculeId.
            var ownMoleculeId = await _db.Companies
                .IgnoreQueryFilters()
                .Where(c => c.Id == companyId)
                .Select(c => c.MoleculeId)
                .FirstOrDefaultAsync();

            // SECURITY: prove the caller can VIEW the calendar this note lives on — the exact rule the
            // Shifts page uses to decide what it shows. Checked before ownership, so nobody can reach
            // into a calendar they cannot see even for a note they wrote.
            //
            // `?? 0` is deliberate and safe (0 is never a molecule id). Writing this as
            // `note.MoleculeId.HasValue && !viewable.Contains(note.MoleculeId.Value)` would silently
            // skip the check for a null molecule and reopen the hole the guard above closes.
            var viewable = await ShiftCalendarAccess.GetViewableMoleculeIdsAsync(
                _db, _grantService, currentUserId, ownMoleculeId);
            if (!viewable.Contains(note.MoleculeId ?? 0))
            {
                _logger.LogWarning("SECURITY: User {UserId} attempted to delete day note {NoteId} on molecule {MoleculeId} they cannot view",
                    currentUserId, data.NoteId, note.MoleculeId);
                return Json(403, "You do not have access to this calendar");
            }

            // You may delete a note you WROTE, or any note on a calendar you can assign shifts for.
            // This is what closes the audit finding: the broad note grant alone is no longer enough to
            // remove someone else's note.
            //
            // KNOWN, ACCEPTED WIDENING: for a Tech-molecule note JobTypeId is legitimately null, and
            // GrantService computes its job-type mismatch only when BOTH sides have a value (see the
            // SECURITY-AUDITED comment at GrantService.cs:213-223, which documents that as deliberate
            // legacy behaviour with enforcement delegated to callers passing jobTypeId explicitly). So
            // an assigner scoped to one job type can delete Tech-molecule notes. Tech molecules have no
            // job types, so this is defensible — but it is a choice, not an accident.
            var isAuthor = note.CreatedByUserId == currentUserId;
            var canAssign = await _grantService.HasGrantWithScopeAsync(
                currentUserId, "AssignShifts", moleculeId: note.MoleculeId, jobTypeId: note.JobTypeId);

            if (!isAuthor && !canAssign)
            {
                _logger.LogWarning("SECURITY: User {UserId} is neither the author of day note {NoteId} nor an assigner for its calendar",
                    currentUserId, data.NoteId);
                return Json(403, "Only the note's author or a shift assigner can delete it");
            }

            var deleted = await _dayNoteService.DeleteDayNoteAsync(note.Id);
            if (!deleted)
            {
                return Json(404, "Note not found");
            }

            // The real note id, unlike the old empty-text path which logged entityId: 0 and left every
            // day-note deletion unattributable.
            await _auditLogService.LogAsync(
                action: "DayNoteDeleted",
                entityType: "CalendarDayNote",
                entityId: note.Id,
                description: $"Deleted day note '{note.Text}' on {note.Date:yyyy-MM-dd} "
                           + $"(molecule {note.MoleculeId}, job type {note.JobTypeId?.ToString() ?? "none"}, "
                           + $"tab {note.TabId?.ToString() ?? "all"}), author {note.CreatedByUserId?.ToString() ?? "deleted user"}");


            // See QuickAddDayNote: best-effort, and the delete has already happened.
            try
            {
                await _notificationService.NotifyDayNoteChangedAsync(
                    CalendarGroups.Shifts(note.MoleculeId.Value, note.JobTypeId),
                    new CalendarDayNoteChangedEvent(note.Id, note.MoleculeId.Value, note.JobTypeId, note.TabId, note.Date, "deleted"));
            }
            catch (Exception notifyEx)
            {
                _logger.LogWarning(notifyEx, "Day note {NoteId} deleted but the realtime notification failed", note.Id);
            }
            return new JsonResult(new { success = true, deleted = true, message = "Day note deleted" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting day note");
            return new JsonResult(
                ShiftManager.Models.ApiErrorResponse.Create(
                    "ERROR_CALENDAR_DELETE_DAY_NOTE_FAILED",
                    _localizer["Error_CalendarApi_CreateTextEntryFailed"].Value)
                .WithCorrelationId(HttpContext.TraceIdentifier))
                { StatusCode = 500 };
        }
    }

    private static JsonResult Json(int status, string message) =>
        new JsonResult(new { success = false, message }) { StatusCode = status };

    private class DeleteDayNoteRequest
    {
        public int NoteId { get; set; }
    }
}
