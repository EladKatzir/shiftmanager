using Microsoft.Extensions.Logging;
using ShiftManager.Models.Support;

namespace ShiftManager.Pages.Admin;

// Source-generated LoggerMessage delegates for UsersModel (closes 60 sites of F-C-013).
// EventId range 12000-12099 reserved (see docs/reference/LOGGING-CONVENTIONS.md).
//
// 43 unique methods cover 60 call sites — three reuse pockets (~17 sites of savings):
//   1. "Invalid or missing NameIdentifier claim" — 10 bare-form sites collapse to 1 method;
//      3 suffixed variants stay distinct (user-creation audit / password-reset auth / password-reset audit).
//   2. "User X attempted {Action} on user Y without EditCompanyUsers grant for company Z" — 6 handlers
//      (toggle, role change, job-type change, password reset, unlock, delete) parameterized via {Action}.
//   3. "RoleTemplate K (Id=I) missing DerivedUserRole — defaulting to Employee" — 3 identical sites
//      (single role change, batch role assignment, bulk import).
//
// Sub-range layout:
//   12000-12009: Cross-cutting / common errors (claim, user-not-found, role-template-missing, unauthorized-action)
//   12010-12019: Create flow
//   12020-12029: Toggle (activate / deactivate)
//   12030-12039: Role change
//   12040-12049: PrimaryShiftType
//   12050-12059: Unlock account
//   12060-12079: Delete user
//   12080-12089: Join request approve / reject (single + batch)
//   12090-12099: CSV export + bulk import
public partial class UsersModel
{
    // ── 12000-12009: Cross-cutting / common errors ─────────────────────────

    [LoggerMessage(EventId = 12000, Level = LogLevel.Error,
        Message = "Invalid or missing NameIdentifier claim")]
    private static partial void LogInvalidNameIdClaim(ILogger logger);

    [LoggerMessage(EventId = 12001, Level = LogLevel.Error,
        Message = "Invalid or missing NameIdentifier claim during user creation audit")]
    private static partial void LogInvalidNameIdClaimUserCreationAudit(ILogger logger);

    [LoggerMessage(EventId = 12002, Level = LogLevel.Error,
        Message = "Invalid or missing NameIdentifier claim during password reset authorization")]
    private static partial void LogInvalidNameIdClaimPasswordResetAuth(ILogger logger);

    [LoggerMessage(EventId = 12003, Level = LogLevel.Error,
        Message = "Invalid or missing NameIdentifier claim during password reset audit")]
    private static partial void LogInvalidNameIdClaimPasswordResetAudit(ILogger logger);

    [LoggerMessage(EventId = 12004, Level = LogLevel.Error,
        Message = "User {UserId} not found in database")]
    private static partial void LogUserNotFoundInDatabase(ILogger logger, int userId);

    [LoggerMessage(EventId = 12005, Level = LogLevel.Warning,
        Message = "RoleTemplate {Key} (Id={Id}) missing DerivedUserRole — defaulting to Employee")]
    private static partial void LogRoleTemplateMissingDerivedRole(ILogger logger, string key, int id);

    // Pattern C — Action verb parameterized across 6 handlers.
    // Action values: "to toggle", "role change on", "job type change on",
    //                "password reset on", "to unlock", "to delete".
    [LoggerMessage(EventId = 12006, Level = LogLevel.Warning,
        Message = "User {CurrentUserId} attempted {Action} user {TargetUserId} without EditCompanyUsers grant for company {CompanyId}")]
    private static partial void LogUnauthorizedUserActionWithGrant(
        ILogger logger, int currentUserId, string action, int targetUserId, int companyId);

    // ── 12010-12019: Create flow (OnPostCreateAsync) ───────────────────────

    [LoggerMessage(EventId = 12010, Level = LogLevel.Information,
        Message = "Assigned {GrantsCount} grants from role template {RoleTemplate} to new user {UserId}")]
    private static partial void LogGrantsAssignedToNewUser(
        ILogger logger, int grantsCount, string roleTemplate, int userId);

    [LoggerMessage(EventId = 12011, Level = LogLevel.Information,
        Message = "Created DirectorCompany mapping for new Director {DirectorId} to Company {CompanyId}")]
    private static partial void LogDirectorCompanyMappingForNewDirector(
        ILogger logger, int directorId, int companyId);

    // ── 12020-12029: Toggle (OnPostToggleAsync) ────────────────────────────

    [LoggerMessage(EventId = 12020, Level = LogLevel.Information,
        Message = "Reactivated user {UserId} has 0 grants but RoleTemplateId={TemplateId} — re-provisioning")]
    private static partial void LogReactivatedUserHasNoGrants(
        ILogger logger, int userId, int templateId);

    [LoggerMessage(EventId = 12021, Level = LogLevel.Information,
        Message = "Grants restored for reactivated user {UserId}")]
    private static partial void LogGrantsRestoredForReactivatedUser(ILogger logger, int userId);

    [LoggerMessage(EventId = 12022, Level = LogLevel.Error,
        Message = "Failed to restore grants for reactivated user {UserId}")]
    private static partial void LogFailedToRestoreGrants(
        ILogger logger, System.Exception ex, int userId);

    // ── 12030-12039: Role change (OnPostRoleAsync) ─────────────────────────

    [LoggerMessage(EventId = 12030, Level = LogLevel.Information,
        Message = "Canceled {Count} shadowing assignments for user {UserId} due to role change from {OldRole} to {NewRole}")]
    private static partial void LogShadowingAssignmentsCanceled(
        ILogger logger, int count, int userId, UserRole oldRole, UserRole newRole);

    [LoggerMessage(EventId = 12031, Level = LogLevel.Information,
        Message = "Removed auto-grants from old role template {OldTemplateId} for user {UserId} during role change")]
    private static partial void LogAutoGrantsRemovedDuringRoleChange(
        ILogger logger, int oldTemplateId, int userId);

    [LoggerMessage(EventId = 12032, Level = LogLevel.Information,
        Message = "Assigned {GrantsCount} grants from role template {RoleTemplate} to user {UserId} during role change")]
    private static partial void LogGrantsAssignedDuringRoleChange(
        ILogger logger, int grantsCount, string roleTemplate, int userId);

    [LoggerMessage(EventId = 12033, Level = LogLevel.Information,
        Message = "Created DirectorCompany mapping for user {UserId} promoted to Director for Company {CompanyId}")]
    private static partial void LogDirectorCompanyMappingPromoted(
        ILogger logger, int userId, int companyId);

    // ── 12040-12049: PrimaryShiftType (OnPostPrimaryShiftTypeAsync) ────────

    [LoggerMessage(EventId = 12040, Level = LogLevel.Warning,
        Message = "User {CurrentUserId} attempted PrimaryShiftType change on user {TargetUserId} without grant")]
    private static partial void LogPrimaryShiftTypeAttemptedWithoutGrant(
        ILogger logger, int currentUserId, int targetUserId);

    [LoggerMessage(EventId = 12041, Level = LogLevel.Warning,
        Message = "Rejected cross-molecule PrimaryShiftType assignment: User {UserId} (Molecule {UserMolecule}) → ShiftType {StId} (Molecule {StMolecule})")]
    private static partial void LogRejectedCrossMoleculePrimaryShiftType(
        ILogger logger, int userId, int? userMolecule, int stId, int? stMolecule);

    [LoggerMessage(EventId = 12042, Level = LogLevel.Warning,
        Message = "Rejected PrimaryShiftType assignment: User {UserId} (Company {CompanyId}) not in EligibleCompanyIds for ShiftType {StId}")]
    private static partial void LogRejectedPrimaryShiftTypeNotInEligibleCompanies(
        ILogger logger, int userId, int companyId, int stId);

    // ── 12050-12059: Unlock account (OnPostUnlockAccountAsync) ─────────────

    [LoggerMessage(EventId = 12050, Level = LogLevel.Information,
        Message = "User {CurrentUserId} unlocked account for user {TargetUserId} ({Email})")]
    private static partial void LogAccountUnlocked(
        ILogger logger, int currentUserId, int targetUserId, string email);

    // ── 12060-12079: Delete user (OnPostDeleteUserAsync) ───────────────────

    [LoggerMessage(EventId = 12060, Level = LogLevel.Warning,
        Message = "User {CurrentUserId} attempted to delete themselves")]
    private static partial void LogUserAttemptedSelfDelete(ILogger logger, int currentUserId);

    [LoggerMessage(EventId = 12061, Level = LogLevel.Warning,
        Message = "User {UserId} not found for deletion")]
    private static partial void LogUserNotFoundForDeletion(ILogger logger, int userId);

    [LoggerMessage(EventId = 12062, Level = LogLevel.Information,
        Message = "Starting deletion of user {UserId} ({UserName}) by admin {CurrentUserId}")]
    private static partial void LogStartingUserDeletion(
        ILogger logger, int userId, string userName, int currentUserId);

    [LoggerMessage(EventId = 12063, Level = LogLevel.Information,
        Message = "Deleting {Count} swap requests related to user {UserId}")]
    private static partial void LogDeletingSwapRequests(ILogger logger, int count, int userId);

    [LoggerMessage(EventId = 12064, Level = LogLevel.Information,
        Message = "Removing {Count} shift assignments for user {UserId}")]
    private static partial void LogRemovingShiftAssignments(ILogger logger, int count, int userId);

    [LoggerMessage(EventId = 12065, Level = LogLevel.Information,
        Message = "Deleting {Count} time-off requests for user {UserId}")]
    private static partial void LogDeletingTimeOffRequests(ILogger logger, int count, int userId);

    [LoggerMessage(EventId = 12066, Level = LogLevel.Information,
        Message = "Removing {Count} grants for deactivated user {UserId}")]
    private static partial void LogRemovingGrants(ILogger logger, int count, int userId);

    [LoggerMessage(EventId = 12067, Level = LogLevel.Information,
        Message = "Completed cleanup of all related records for user {UserId}")]
    private static partial void LogCompletedRelatedRecordsCleanup(ILogger logger, int userId);

    [LoggerMessage(EventId = 12068, Level = LogLevel.Information,
        Message = "Permanently deleting user {UserId} ({UserName})")]
    private static partial void LogPermanentlyDeletingUser(
        ILogger logger, int userId, string userName);

    [LoggerMessage(EventId = 12069, Level = LogLevel.Information,
        Message = "Successfully deleted user {UserId} ({UserName}) and all related data")]
    private static partial void LogSuccessfullyDeletedUser(
        ILogger logger, int userId, string userName);

    [LoggerMessage(EventId = 12070, Level = LogLevel.Error,
        Message = "FK constraint prevented deleting user {UserId}")]
    private static partial void LogFkConstraintPreventedDeletion(
        ILogger logger, System.Exception ex, int userId);

    [LoggerMessage(EventId = 12071, Level = LogLevel.Error,
        Message = "Error deleting user {UserId}")]
    private static partial void LogErrorDeletingUser(
        ILogger logger, System.Exception ex, int userId);

    // ── 12080-12089: Join request approve / reject (single + batch) ────────

    [LoggerMessage(EventId = 12080, Level = LogLevel.Information,
        Message = "Assigned {GrantsCount} grants from role template {RoleTemplate} to user {UserId} via join request approval")]
    private static partial void LogGrantsAssignedViaJoinRequestApproval(
        ILogger logger, int grantsCount, string roleTemplate, int userId);

    [LoggerMessage(EventId = 12081, Level = LogLevel.Information,
        Message = "Join request {RequestId} approved by {ApproverId}. Created user {UserId} ({Email}) for company {CompanyId}")]
    private static partial void LogJoinRequestApproved(
        ILogger logger, int requestId, int approverId, int userId, string email, int companyId);

    [LoggerMessage(EventId = 12082, Level = LogLevel.Error,
        Message = "Error approving join request {RequestId}")]
    private static partial void LogErrorApprovingJoinRequest(
        ILogger logger, System.Exception ex, int requestId);

    [LoggerMessage(EventId = 12083, Level = LogLevel.Information,
        Message = "Join request {RequestId} rejected by {ReviewerId}. Email: {Email}, Company: {CompanyId}")]
    private static partial void LogJoinRequestRejected(
        ILogger logger, int requestId, int reviewerId, string email, int companyId);

    [LoggerMessage(EventId = 12084, Level = LogLevel.Error,
        Message = "Error rejecting join request {RequestId}")]
    private static partial void LogErrorRejectingJoinRequest(
        ILogger logger, System.Exception ex, int requestId);

    [LoggerMessage(EventId = 12085, Level = LogLevel.Warning,
        Message = "SECURITY: User {UserId} submitted invalid join request IDs: {InvalidIds}")]
    private static partial void LogSecurityInvalidJoinRequestIds(
        ILogger logger, int userId, string invalidIds);

    [LoggerMessage(EventId = 12086, Level = LogLevel.Warning,
        Message = "SECURITY: User {UserId} ({Role}) attempted to approve join request {RequestId} for unauthorized company/jobtype {CompanyId}/{JobTypeId}")]
    private static partial void LogSecurityUnauthorizedJoinRequestApproval(
        ILogger logger, int userId, UserRole role, int requestId, int companyId, int? jobTypeId);

    [LoggerMessage(EventId = 12087, Level = LogLevel.Information,
        Message = "Batch approval: Join request {RequestId} approved by {ApproverId}. Created user {UserId} ({Email}) with role {Role} template {TemplateKey} for company {CompanyId}. Assigned {GrantsCount} grants.")]
    private static partial void LogBatchApprovalSuccess(
        ILogger logger, int requestId, int approverId, int userId, string email,
        UserRole role, string templateKey, int companyId, int grantsCount);

    [LoggerMessage(EventId = 12088, Level = LogLevel.Error,
        Message = "Error during batch approval of join requests")]
    private static partial void LogErrorBatchApproval(ILogger logger, System.Exception ex);

    // ── 12090-12099: CSV export + bulk import ──────────────────────────────

    [LoggerMessage(EventId = 12090, Level = LogLevel.Error,
        Message = "Error exporting users to CSV")]
    private static partial void LogErrorExportingUsersToCsv(ILogger logger, System.Exception ex);

    [LoggerMessage(EventId = 12091, Level = LogLevel.Information,
        Message = "Bulk import completed: {Created} created, {Skipped} skipped, {Errors} errors for company {CompanyId}")]
    private static partial void LogBulkImportCompleted(
        ILogger logger, int created, int skipped, int errors, int companyId);
}
