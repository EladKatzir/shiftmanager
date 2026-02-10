using System.Threading.Tasks;

namespace ShiftManager.Services;

/// <summary>
/// Service interface for sending email notifications via company mail API.
/// Used to notify users about shift assignments, changes, and deletions.
/// </summary>
public interface IMailService
{
    /// <summary>
    /// Enqueue an email for background delivery.
    /// Returns true if successfully queued, false if the queue is full.
    /// </summary>
    /// <param name="recipient">Email address of the recipient</param>
    /// <param name="subject">Email subject line</param>
    /// <param name="htmlBody">HTML-formatted email body</param>
    /// <returns>True if email queued successfully, false otherwise</returns>
    Task<bool> SendMailAsync(string recipient, string subject, string htmlBody);

    /// <summary>
    /// Send an email directly (synchronous HTTP call). Used by the background processor.
    /// Do not call from HTTP request handlers — use SendMailAsync instead.
    /// </summary>
    Task<bool> SendMailDirectAsync(string recipient, string subject, string htmlBody);

    /// <summary>
    /// Send shift assignment notification email.
    /// </summary>
    /// <param name="recipientEmail">Employee email address</param>
    /// <param name="employeeName">Employee display name</param>
    /// <param name="shiftTypeName">Type of shift (Morning, Afternoon, etc.)</param>
    /// <param name="shiftDate">Date of the shift</param>
    /// <param name="startTime">Shift start time</param>
    /// <param name="endTime">Shift end time</param>
    Task<bool> SendShiftAssignedEmailAsync(string recipientEmail, string employeeName,
        string shiftTypeName, DateOnly shiftDate, TimeOnly startTime, TimeOnly endTime);

    /// <summary>
    /// Send shift change notification email.
    /// </summary>
    Task<bool> SendShiftChangedEmailAsync(string recipientEmail, string employeeName,
        string shiftTypeName, DateOnly shiftDate, TimeOnly startTime, TimeOnly endTime, string changeDescription);

    /// <summary>
    /// Send shift deletion notification email.
    /// </summary>
    Task<bool> SendShiftDeletedEmailAsync(string recipientEmail, string employeeName,
        string shiftTypeName, DateOnly shiftDate, TimeOnly startTime, TimeOnly endTime);

    /// <summary>
    /// Send chore assignment notification email.
    /// </summary>
    /// <param name="recipientEmail">Employee email address</param>
    /// <param name="employeeName">Employee display name</param>
    /// <param name="choreTitle">Title of the chore</param>
    /// <param name="choreDate">Date of the chore</param>
    Task<bool> SendChoreAssignedEmailAsync(string recipientEmail, string employeeName,
        string choreTitle, DateOnly choreDate);

    /// <summary>
    /// Send chore cancellation notification email.
    /// </summary>
    Task<bool> SendChoreCanceledEmailAsync(string recipientEmail, string employeeName,
        string choreTitle, DateOnly choreDate);

    /// <summary>
    /// Send account approval notification email.
    /// </summary>
    /// <param name="recipientEmail">New user's email address</param>
    /// <param name="userName">New user's display name</param>
    /// <param name="assignedRole">Role assigned to the user (Employee, Manager, etc.)</param>
    /// <param name="companyName">Name of the company</param>
    Task<bool> SendAccountApprovedEmailAsync(string recipientEmail, string userName,
        string assignedRole, string companyName);

    /// <summary>
    /// Send time-off request approved notification email.
    /// </summary>
    Task<bool> SendTimeOffApprovedEmailAsync(string recipientEmail, string employeeName,
        DateOnly startDate, DateOnly endDate);

    /// <summary>
    /// Send time-off request declined notification email.
    /// </summary>
    Task<bool> SendTimeOffDeclinedEmailAsync(string recipientEmail, string employeeName,
        DateOnly startDate, DateOnly endDate);

    /// <summary>
    /// Send time-off request deleted notification email.
    /// </summary>
    Task<bool> SendTimeOffDeletedEmailAsync(string recipientEmail, string employeeName,
        DateOnly startDate, DateOnly endDate);

    /// <summary>
    /// Send swap request approved notification email.
    /// </summary>
    Task<bool> SendSwapRequestApprovedEmailAsync(string recipientEmail, string employeeName,
        string shiftInfo);

    /// <summary>
    /// Send swap request declined notification email.
    /// </summary>
    Task<bool> SendSwapRequestDeclinedEmailAsync(string recipientEmail, string employeeName,
        string shiftInfo);

    /// <summary>
    /// Send on-duty assignment notification email.
    /// </summary>
    Task<bool> SendOnDutyAssignedEmailAsync(string recipientEmail, string employeeName,
        string onDutyTypeName, DateOnly onDutyDate);

    /// <summary>
    /// Send on-duty cancellation notification email.
    /// </summary>
    Task<bool> SendOnDutyCanceledEmailAsync(string recipientEmail, string employeeName,
        string onDutyTypeName, DateOnly onDutyDate);

    /// <summary>
    /// Send access request submitted notification email to owners.
    /// </summary>
    Task<bool> SendAccessRequestSubmittedEmailAsync(string recipientEmail, string ownerName,
        string requesterName, string requesterEmail, string companyName);

    // ============= Ops Console Scheduler: New Email Templates =============

    /// <summary>
    /// Send trainee added notification email.
    /// </summary>
    Task<bool> SendTraineeAddedEmailAsync(string recipientEmail, string employeeName,
        string traineeName, string shiftTypeName, DateOnly shiftDate, TimeOnly startTime, TimeOnly endTime);

    /// <summary>
    /// Send slot removed notification email (staffing decreased).
    /// </summary>
    Task<bool> SendSlotRemovedEmailAsync(string recipientEmail, string employeeName,
        string shiftTypeName, DateOnly shiftDate, TimeOnly startTime, TimeOnly endTime, string reason);

    /// <summary>
    /// Send shift modified notification email (time or name changes).
    /// </summary>
    Task<bool> SendShiftModifiedEmailAsync(string recipientEmail, string employeeName,
        string shiftTypeName, DateOnly shiftDate, string changeDescription);
}
