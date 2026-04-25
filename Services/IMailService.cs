using System.Threading.Tasks;
using ShiftManager.Models.Results;

namespace ShiftManager.Services;

/// <summary>
/// Service interface for sending email notifications via company mail API.
/// Used to notify users about shift assignments, changes, and deletions.
///
/// <para>
/// Migrated to <see cref="OperationResult"/> as part of the project-wide error-handling overhaul.
/// Previously these methods returned <c>bool</c> with no diagnostic context — admins setting up email
/// config or troubleshooting failed sends had no structured error information. Now each method returns
/// an <see cref="OperationResult"/> with a stable <c>Error_MailService_*</c> key plus a localized message
/// (see <c>SharedResources.resx</c>) so admin/diagnostic callers can surface specific failure reasons.
/// Fire-and-forget callers (most notification handlers) continue to work unchanged because awaiting a
/// <c>Task&lt;OperationResult&gt;</c> without capturing the result is valid.
/// </para>
/// </summary>
public interface IMailService
{
    /// <summary>
    /// Enqueue an email for background delivery. Returns immediately without blocking the HTTP request.
    /// </summary>
    /// <param name="recipient">Email address of the recipient</param>
    /// <param name="subject">Email subject line</param>
    /// <param name="htmlBody">HTML-formatted email body</param>
    /// <returns>
    /// <see cref="OperationResult.Ok"/> if the email was successfully enqueued; otherwise a failure
    /// result keyed under <c>Error_MailService_InvalidRecipient</c> (validation) or
    /// <c>Error_MailService_SendFailed</c> (queue full / unavailable).
    /// </returns>
    Task<OperationResult> SendMailAsync(string recipient, string subject, string htmlBody);

    /// <summary>
    /// Send an email directly (synchronous HTTP call). Used by the background processor.
    /// Do not call from HTTP request handlers — use SendMailAsync instead.
    /// Optional companyId (from QueuedEmail) enables EmailApiLog persistence from background services.
    /// </summary>
    /// <returns>
    /// <see cref="OperationResult.Ok"/> on a successful HTTP 2xx response, otherwise a failure result
    /// keyed under one of <c>Error_MailService_DisabledByFlag</c>, <c>Error_MailService_NotConfigured</c>,
    /// <c>Error_MailService_MissingApiUrl</c>, <c>Error_MailService_MissingApiKey</c>,
    /// <c>Error_MailService_InvalidRecipient</c>, or <c>Error_MailService_SendFailed</c>.
    /// </returns>
    Task<OperationResult> SendMailDirectAsync(string recipient, string subject, string htmlBody, int companyId = 0);

    /// <summary>
    /// Send shift assignment notification email.
    /// </summary>
    /// <param name="recipientEmail">Employee email address</param>
    /// <param name="employeeName">Employee display name</param>
    /// <param name="shiftTypeName">Type of shift (Morning, Afternoon, etc.)</param>
    /// <param name="shiftDate">Date of the shift</param>
    /// <param name="startTime">Shift start time</param>
    /// <param name="endTime">Shift end time</param>
    Task<OperationResult> SendShiftAssignedEmailAsync(string recipientEmail, string employeeName,
        string shiftTypeName, DateOnly shiftDate, TimeOnly startTime, TimeOnly endTime);

    /// <summary>
    /// Send shift change notification email.
    /// </summary>
    Task<OperationResult> SendShiftChangedEmailAsync(string recipientEmail, string employeeName,
        string shiftTypeName, DateOnly shiftDate, TimeOnly startTime, TimeOnly endTime, string changeDescription);

    /// <summary>
    /// Send shift deletion notification email.
    /// </summary>
    Task<OperationResult> SendShiftDeletedEmailAsync(string recipientEmail, string employeeName,
        string shiftTypeName, DateOnly shiftDate, TimeOnly startTime, TimeOnly endTime);

    /// <summary>
    /// Send chore assignment notification email.
    /// </summary>
    /// <param name="recipientEmail">Employee email address</param>
    /// <param name="employeeName">Employee display name</param>
    /// <param name="choreTitle">Title of the chore</param>
    /// <param name="choreDate">Date of the chore</param>
    Task<OperationResult> SendChoreAssignedEmailAsync(string recipientEmail, string employeeName,
        string choreTitle, DateOnly choreDate);

    /// <summary>
    /// Send chore cancellation notification email.
    /// </summary>
    Task<OperationResult> SendChoreCanceledEmailAsync(string recipientEmail, string employeeName,
        string choreTitle, DateOnly choreDate);

    /// <summary>
    /// Send account approval notification email.
    /// </summary>
    /// <param name="recipientEmail">New user's email address</param>
    /// <param name="userName">New user's display name</param>
    /// <param name="assignedRole">Role assigned to the user (Employee, Manager, etc.)</param>
    /// <param name="companyName">Name of the company</param>
    Task<OperationResult> SendAccountApprovedEmailAsync(string recipientEmail, string userName,
        string assignedRole, string companyName);

    /// <summary>
    /// Send time-off request approved notification email.
    /// </summary>
    Task<OperationResult> SendTimeOffApprovedEmailAsync(string recipientEmail, string employeeName,
        DateOnly startDate, DateOnly endDate);

    /// <summary>
    /// Send time-off request declined notification email.
    /// </summary>
    Task<OperationResult> SendTimeOffDeclinedEmailAsync(string recipientEmail, string employeeName,
        DateOnly startDate, DateOnly endDate);

    /// <summary>
    /// Send time-off request deleted notification email.
    /// </summary>
    Task<OperationResult> SendTimeOffDeletedEmailAsync(string recipientEmail, string employeeName,
        DateOnly startDate, DateOnly endDate);

    /// <summary>
    /// Send swap request approved notification email.
    /// </summary>
    Task<OperationResult> SendSwapRequestApprovedEmailAsync(string recipientEmail, string employeeName,
        string shiftInfo);

    /// <summary>
    /// Send swap request declined notification email.
    /// </summary>
    Task<OperationResult> SendSwapRequestDeclinedEmailAsync(string recipientEmail, string employeeName,
        string shiftInfo);

    /// <summary>
    /// Send on-duty assignment notification email.
    /// </summary>
    Task<OperationResult> SendOnDutyAssignedEmailAsync(string recipientEmail, string employeeName,
        string onDutyTypeName, DateOnly onDutyDate);

    /// <summary>
    /// Send on-duty cancellation notification email.
    /// </summary>
    Task<OperationResult> SendOnDutyCanceledEmailAsync(string recipientEmail, string employeeName,
        string onDutyTypeName, DateOnly onDutyDate);

    /// <summary>
    /// Send access request submitted notification email to owners.
    /// </summary>
    Task<OperationResult> SendAccessRequestSubmittedEmailAsync(string recipientEmail, string ownerName,
        string requesterName, string requesterEmail, string companyName);

    // ============= Ops Console Scheduler: New Email Templates =============

    /// <summary>
    /// Send trainee added notification email.
    /// </summary>
    Task<OperationResult> SendTraineeAddedEmailAsync(string recipientEmail, string employeeName,
        string traineeName, string shiftTypeName, DateOnly shiftDate, TimeOnly startTime, TimeOnly endTime);

    /// <summary>
    /// Send slot removed notification email (staffing decreased).
    /// </summary>
    Task<OperationResult> SendSlotRemovedEmailAsync(string recipientEmail, string employeeName,
        string shiftTypeName, DateOnly shiftDate, TimeOnly startTime, TimeOnly endTime, string reason);

    /// <summary>
    /// Send shift modified notification email (time or name changes).
    /// </summary>
    Task<OperationResult> SendShiftModifiedEmailAsync(string recipientEmail, string employeeName,
        string shiftTypeName, DateOnly shiftDate, string changeDescription);
}
