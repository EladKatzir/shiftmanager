using System.Threading.Tasks;

namespace ShiftManager.Services;

/// <summary>
/// Service interface for sending email notifications via company mail API.
/// Used to notify users about shift assignments, changes, and deletions.
/// </summary>
public interface IMailService
{
    /// <summary>
    /// Send an email notification asynchronously.
    /// </summary>
    /// <param name="recipient">Email address of the recipient</param>
    /// <param name="subject">Email subject line</param>
    /// <param name="htmlBody">HTML-formatted email body</param>
    /// <returns>True if email sent successfully, false otherwise</returns>
    Task<bool> SendMailAsync(string recipient, string subject, string htmlBody);

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
}
