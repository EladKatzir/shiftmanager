using ShiftManager.Models.Support;

namespace ShiftManager.Services;

/// <summary>
/// Reusable facility for propagating new template AutoGrants to users who
/// already have their RoleTemplate assigned. Added 2026-04-15 as a permanent
/// admin tool — see <c>/Owner/Hub/Grants</c> "Grant Actions" tab.
///
/// Idempotent: skips users whose grants already match the template.
/// Safe for manual admin grants (IsAutoGrant=false) — they are never touched.
/// </summary>
public interface IGrantBackfillService
{
    /// <summary>
    /// Dry-run. Returns the users who would receive additional AutoGrants
    /// if <see cref="ExecuteAsync"/> were called now, with no DB writes.
    /// </summary>
    /// <param name="roleTemplateId">Optional filter: limit to a single template.</param>
    Task<BackfillReport> PreviewAsync(int? roleTemplateId = null);

    /// <summary>
    /// Apply missing AutoGrants. Delegates to
    /// <c>IGrantService.ApplyAutoGrantsAsync</c> per user (idempotent inside).
    /// </summary>
    /// <param name="roleTemplateId">Optional filter: limit to a single template.</param>
    /// <param name="actingUserId">Id of the admin running the back-fill — used for audit logging.</param>
    Task<BackfillResult> ExecuteAsync(int? roleTemplateId, int actingUserId);

    /// <summary>
    /// Read-only surplus audit. Finds users whose Grants table contains rows
    /// where <c>IsAutoGrant=true</c> but the grant is no longer in their
    /// template's <c>AutoGrants</c> list (template was edited, stale auto-grant
    /// remained). Manual admin grants (<c>IsAutoGrant=false</c>) are excluded —
    /// those are intentional and not flagged.
    ///
    /// WARN-ONLY: this method never modifies data. Removing surplus is a
    /// deliberate admin decision made manually.
    /// </summary>
    Task<SurplusReport> PreviewSurplusAsync(int? roleTemplateId = null);
}

/// <summary>Summary of a dry-run.</summary>
public sealed class BackfillReport
{
    public int UsersScanned { get; set; }
    public int UsersWithMissingGrants { get; set; }
    public int TotalMissingGrantRows { get; set; }
    public List<BackfillReportEntry> Entries { get; set; } = new();
}

/// <summary>Per-user drift entry.</summary>
public sealed class BackfillReportEntry
{
    public int UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int RoleTemplateId { get; set; }
    public string RoleTemplateKey { get; set; } = string.Empty;
    public int MissingGrantCount { get; set; }

    /// <summary>The actual grant keys that would be inserted on Execute. Populated by PreviewAsync (2026-04-16).</summary>
    public List<string> MissingGrantKeys { get; set; } = new();
}

/// <summary>Per-user surplus entry — auto-grants no longer in their template.</summary>
public sealed class SurplusReportEntry
{
    public int UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int RoleTemplateId { get; set; }
    public string RoleTemplateKey { get; set; } = string.Empty;
    public int SurplusGrantCount { get; set; }

    /// <summary>Grant keys present as IsAutoGrant=true on the user but absent from the template AutoGrants.</summary>
    public List<string> SurplusGrantKeys { get; set; } = new();
}

/// <summary>Summary of surplus audit (warn-only).</summary>
public sealed class SurplusReport
{
    public int UsersScanned { get; set; }
    public int UsersWithSurplus { get; set; }
    public int TotalSurplusRows { get; set; }
    public List<SurplusReportEntry> Entries { get; set; } = new();
}

/// <summary>Outcome of an execute run.</summary>
public sealed class BackfillResult
{
    public int UsersProcessed { get; set; }
    public int UsersUpdated { get; set; }
    public int TotalGrantsInserted { get; set; }
    public int UsersFailed { get; set; }
    public List<string> Errors { get; set; } = new();
}
