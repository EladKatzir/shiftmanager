using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;
using System.Text.Json;

namespace ShiftManager.Pages.Api.Calendar;

// Day-scoped free-text note from Quick Entry on the Shifts calendar (shift-mode or user-mode).
// Sibling of QuickAddTextEntry, but the note attaches to a DAY (not a user): a shift-type cell has no
// single user.
//
// Keyed to the CALENDAR — (MoleculeId, JobTypeId, TabId), all three named by the client from
// CalendarPageConfig — so every viewer of that calendar sees it whichever desk or molecule they sit
// in. Because the request names all three, each is validated here: the molecule against
// ShiftCalendarAccess (can the caller even view it), the job type against the set that molecule can
// resolve, and the tab against the (molecule, job type) pair that owns it. Holding the broad
// note-writing grant is not sufficient on its own.
//
// This endpoint ALWAYS INSERTS. It used to upsert, and to treat empty text as a delete, which let any
// holder of WriteOverviewNotes silently overwrite or clear a colleague's note. Deletion now lives in
// DeleteDayNote, keyed by note id and gated on authorship or the assignment grant.
//
// Covered by ApiAuthenticationMiddleware's "/Api/Calendar" internal-web-UI prefix
// (IsInternalWebUiEndpoint) — no separate middleware registration needed.
[Authorize]
[IgnoreAntiforgeryToken]
public class QuickAddDayNoteModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ICalendarDayNoteService _dayNoteService;
    private readonly IAuditLogService _auditLogService;
    private readonly IGrantService _grantService;
    private readonly IJobTypeService _jobTypeService;
    private readonly ITenantResolver _tenantResolver;
    private readonly ILogger<QuickAddDayNoteModel> _logger;
    private readonly IStringLocalizer<SharedResources> _localizer;

    public QuickAddDayNoteModel(
        AppDbContext db,
        ICalendarDayNoteService dayNoteService,
        IAuditLogService auditLogService,
        IGrantService grantService,
        IJobTypeService jobTypeService,
        ITenantResolver tenantResolver,
        ILogger<QuickAddDayNoteModel> logger,
        IStringLocalizer<SharedResources> localizer)
    {
        _db = db;
        _dayNoteService = dayNoteService;
        _auditLogService = auditLogService;
        _grantService = grantService;
        _jobTypeService = jobTypeService;
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
            var data = JsonSerializer.Deserialize<DayNoteRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (data == null)
            {
                return new JsonResult(new { success = false, message = "Invalid request data" })
                    { StatusCode = 400 };
            }

            if (!DateOnly.TryParse(data.Date, out var noteDate))
            {
                return new JsonResult(new { success = false, message = "Invalid date format" })
                    { StatusCode = 400 };
            }

            // Notes are informational — unlike assignments, past dates are allowed (annotate yesterday).
            // Only bound the far future to keep input sane.
            if (noteDate > DateOnly.FromDateTime(DateTime.Today.AddYears(2)))
            {
                return new JsonResult(new { success = false, message = "Cannot create notes more than 2 years in the future" })
                    { StatusCode = 400 };
            }

            if (data.MoleculeId is not > 0)
            {
                return new JsonResult(new { success = false, message = "A molecule is required" })
                    { StatusCode = 400 };
            }
            var moleculeId = data.MoleculeId.Value;

            var text = (data.Text ?? string.Empty).Trim();
            if (text.Length > 500)
            {
                return new JsonResult(new { success = false, message = "Text must not exceed 500 characters" })
                    { StatusCode = 400 };
            }

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var currentUserId))
            {
                return new JsonResult(new { success = false, message = "User not authenticated" })
                    { StatusCode = 401 };
            }

            // SECURITY: same note-write gate as user-scoped text entries.
            if (!await _grantService.HasCalendarNotePermissionAsync(currentUserId))
            {
                _logger.LogWarning("SECURITY: User {UserId} attempted to write a day note without note permission", currentUserId);
                return new JsonResult(new { success = false, message = "You do not have permission to create notes" })
                    { StatusCode = 403 };
            }

            var companyId = _tenantResolver.GetCurrentTenantId();
            if (companyId <= 0)
            {
                return new JsonResult(new { success = false, message = "No active company context" })
                    { StatusCode = 400 };
            }

            // SECURITY: the request names the molecule, so prove the caller can view that molecule's
            // calendar — the exact rule the Shifts page uses to decide what it shows.
            // SECURITY-AUDITED: SAFE — reads only the caller's own active desk's MoleculeId.
            var ownMoleculeId = await _db.Companies
                .IgnoreQueryFilters()
                .Where(c => c.Id == companyId)
                .Select(c => c.MoleculeId)
                .FirstOrDefaultAsync();
            var viewable = await ShiftCalendarAccess.GetViewableMoleculeIdsAsync(_db, _grantService, currentUserId, ownMoleculeId);
            if (!viewable.Contains(moleculeId))
            {
                _logger.LogWarning("SECURITY: User {UserId} attempted to write a day note on molecule {MoleculeId} they cannot view",
                    currentUserId, moleculeId);
                return new JsonResult(new { success = false, message = "You do not have access to this calendar" })
                    { StatusCode = 403 };
            }

            // SECURITY-AUDITED: SAFE — Molecule has no tenant filter, and moleculeId was just proven
            // viewable above. Only Type is read, to decide whether a null job type is legitimate.
            var moleculeType = await _db.Molecules
                .Where(m => m.Id == moleculeId)
                .Select(m => (ShiftManager.Models.Support.MoleculeType?)m.Type)
                .FirstOrDefaultAsync();
            if (moleculeType == null)
            {
                return new JsonResult(new { success = false, message = "A molecule is required" })
                    { StatusCode = 400 };
            }

            // SECURITY: the request names a job type, so prove it is one THIS molecule's calendar can
            // actually show. GetJobTypesForMoleculeAsync pins jt.AreaId == molecule.AreaId and
            // (jt.MoleculeId == null || == moleculeId), so a job type from another area is unreachable
            // — which is what stops a note being keyed to a calendar that does not exist.
            if (data.JobTypeId.HasValue)
            {
                var available = await _jobTypeService.GetJobTypesForMoleculeAsync(moleculeId);
                if (available.All(jt => jt.Id != data.JobTypeId.Value))
                {
                    _logger.LogWarning("SECURITY: User {UserId} attempted to write a day note on molecule {MoleculeId} with job type {JobTypeId}, which that molecule cannot resolve",
                        currentUserId, moleculeId, data.JobTypeId.Value);
                    return new JsonResult(new { success = false, message = "You do not have access to this calendar" })
                        { StatusCode = 403 };
                }
            }
            else if (moleculeType != ShiftManager.Models.Support.MoleculeType.Tech)
            {
                // A null job type is legitimate ONLY for a Tech molecule, whose calendar has no job
                // type at all. For any other molecule it means the degenerate "no resolvable job
                // types" state, and a note written there is encoded identically to a Tech note —
                // (molecule, NULL, NULL) — whose meaning then depends on the molecule's Type rather
                // than on the row. Refusing keeps that ambiguous triple out of the data entirely.
                return new JsonResult(new { success = false, message = "A job type is required for this calendar" })
                    { StatusCode = 400 };
            }

            // SECURITY: a tab belongs to exactly one (molecule, job type) pair, so a tab id from
            // another calendar must not be accepted. Closes the cross-molecule tab vector.
            if (data.TabId.HasValue)
            {
                var tabBelongsHere = await _db.ShiftTabs.AnyAsync(t =>
                    t.Id == data.TabId.Value
                    && t.MoleculeId == moleculeId
                    && t.JobTypeId == data.JobTypeId);
                if (!tabBelongsHere)
                {
                    _logger.LogWarning("SECURITY: User {UserId} attempted to write a day note on tab {TabId}, which does not belong to molecule {MoleculeId} / job type {JobTypeId}",
                        currentUserId, data.TabId.Value, moleculeId, data.JobTypeId);
                    return new JsonResult(new { success = false, message = "You do not have access to this calendar" })
                        { StatusCode = 403 };
                }
            }

            // Empty text is REJECTED, not treated as a delete. Treating it as a delete let anyone
            // silently clear a colleague's note with no ownership check and no visible trace (the
            // audit row it wrote carried entityId: 0, so deletions were unattributable). Deleting is
            // now an explicit action against a specific note id — see DeleteDayNote.
            if (text.Length == 0)
            {
                return new JsonResult(new { success = false, message = "Note text is required" })
                    { StatusCode = 400 };
            }

            var note = await _dayNoteService.AddDayNoteAsync(
                noteDate,
                new CalendarScope(moleculeId, data.JobTypeId, data.TabId),
                companyId, text, currentUserId);

            await _auditLogService.LogAsync(
                action: "DayNoteSaved",
                entityType: "CalendarDayNote",
                entityId: note.Id,
                description: $"Saved day note '{text}' on {noteDate:yyyy-MM-dd} "
                           + $"(molecule {moleculeId}, job type {data.JobTypeId?.ToString() ?? "none"}, "
                           + $"tab {data.TabId?.ToString() ?? "all"}) via Quick Entry");

            return new JsonResult(new
            {
                success = true,
                dayNoteId = note.Id,
                message = "Day note saved successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving day note via Quick Entry");
            return new JsonResult(
                ShiftManager.Models.ApiErrorResponse.Create(
                    "ERROR_CALENDAR_SAVE_DAY_NOTE_FAILED",
                    _localizer["Error_CalendarApi_CreateTextEntryFailed"].Value)
                .WithCorrelationId(HttpContext.TraceIdentifier))
                { StatusCode = 500 };
        }
    }

    private class DayNoteRequest
    {
        public string Date { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public int? MoleculeId { get; set; }

        /// <summary>The calendar's job type. Null is legitimate ONLY for a Tech molecule.</summary>
        public int? JobTypeId { get; set; }

        /// <summary>The active tab, or null for the synthetic "All" view.</summary>
        public int? TabId { get; set; }
    }
}
