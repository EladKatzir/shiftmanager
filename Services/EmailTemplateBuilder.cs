using Microsoft.Extensions.Localization;
using ShiftManager.Resources;

namespace ShiftManager.Services;

/// <summary>
/// Service for building localized email templates with RTL support for Hebrew.
/// </summary>
public class EmailTemplateBuilder
{
    private readonly IStringLocalizer<SharedResources> _localizer;

    public EmailTemplateBuilder(IStringLocalizer<SharedResources> localizer)
    {
        _localizer = localizer;
    }

    /// <summary>
    /// Builds the HTML wrapper for email templates with proper direction support.
    /// </summary>
    /// <param name="content">The email body content</param>
    /// <param name="isRtl">Whether to use RTL (right-to-left) layout for Hebrew</param>
    /// <returns>Complete HTML email</returns>
    private string BuildEmailHtml(string content, bool isRtl = false)
    {
        var direction = isRtl ? "rtl" : "ltr";
        var textAlign = isRtl ? "right" : "left";

        return $@"
<!DOCTYPE html>
<html dir=""{direction}"" lang=""{(isRtl ? "he" : "en")}"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <style>
        body {{
            font-family: Arial, sans-serif;
            line-height: 1.6;
            color: #333;
            max-width: 600px;
            margin: 0 auto;
            padding: 20px;
            direction: {direction};
            text-align: {textAlign};
        }}
        .header {{
            background-color: #007bff;
            color: white;
            padding: 20px;
            text-align: center;
            border-radius: 5px 5px 0 0;
        }}
        .content {{
            background-color: #f9f9f9;
            padding: 20px;
            border: 1px solid #ddd;
            border-radius: 0 0 5px 5px;
        }}
        .footer {{
            margin-top: 20px;
            padding-top: 20px;
            border-top: 1px solid #ddd;
            color: #666;
            font-size: 0.9em;
        }}
        .button {{
            display: inline-block;
            padding: 10px 20px;
            background-color: #007bff;
            color: white;
            text-decoration: none;
            border-radius: 5px;
            margin: 10px 0;
        }}
    </style>
</head>
<body>
    <div class=""header"">
        <h1>{_localizer["ShiftManager"]}</h1>
    </div>
    <div class=""content"">
        {content}
    </div>
    <div class=""footer"">
        <p>{_localizer["Email_AutomatedMessage"]}</p>
    </div>
</body>
</html>";
    }

    /// <summary>
    /// Builds email for shift assignment notification.
    /// </summary>
    public string BuildShiftAssignedEmail(string employeeName, DateTime shiftDate, string shiftType, string? notes = null)
    {
        var culture = _localizer["Culture"].Value;
        var isRtl = culture == "he-IL";

        var content = $@"
<p>{_localizer["Email_Greeting", employeeName]}</p>
<p>{_localizer["Email_ShiftAssigned_Message"]}</p>
<ul>
    <li><strong>{_localizer["Email_ShiftDate"]}:</strong> {shiftDate:D}</li>
    <li><strong>{_localizer["Email_ShiftType"]}:</strong> {shiftType}</li>
    {(string.IsNullOrEmpty(notes) ? "" : $"<li><strong>{_localizer["Email_Notes"]}:</strong> {notes}</li>")}
</ul>
<p>{_localizer["Email_ThankYou"]}</p>";

        return BuildEmailHtml(content, isRtl);
    }

    /// <summary>
    /// Builds email for request approval notification.
    /// </summary>
    public string BuildRequestApprovedEmail(string employeeName, string requestType, DateTime requestDate)
    {
        var culture = _localizer["Culture"].Value;
        var isRtl = culture == "he-IL";

        var content = $@"
<p>{_localizer["Email_Greeting", employeeName]}</p>
<p>{_localizer["Email_RequestApproved_Message", requestType]}</p>
<p><strong>{_localizer["Email_RequestDate"]}:</strong> {requestDate:D}</p>
<p>{_localizer["Email_ThankYou"]}</p>";

        return BuildEmailHtml(content, isRtl);
    }

    /// <summary>
    /// Builds email for request rejection notification.
    /// </summary>
    public string BuildRequestRejectedEmail(string employeeName, string requestType, DateTime requestDate, string? reason = null)
    {
        var culture = _localizer["Culture"].Value;
        var isRtl = culture == "he-IL";

        var content = $@"
<p>{_localizer["Email_Greeting", employeeName]}</p>
<p>{_localizer["Email_RequestRejected_Message", requestType]}</p>
<p><strong>{_localizer["Email_RequestDate"]}:</strong> {requestDate:D}</p>
{(string.IsNullOrEmpty(reason) ? "" : $"<p><strong>{_localizer["Email_Reason"]}:</strong> {reason}</p>")}
<p>{_localizer["Email_ContactManager"]}</p>";

        return BuildEmailHtml(content, isRtl);
    }

    /// <summary>
    /// Builds email for password reset.
    /// </summary>
    public string BuildPasswordResetEmail(string userName, string resetLink)
    {
        var culture = _localizer["Culture"].Value;
        var isRtl = culture == "he-IL";

        var content = $@"
<p>{_localizer["Email_Greeting", userName]}</p>
<p>{_localizer["Email_PasswordReset_Message"]}</p>
<p><a href=""{resetLink}"" class=""button"">{_localizer["Email_ResetPassword"]}</a></p>
<p>{_localizer["Email_PasswordReset_Expiry"]}</p>
<p>{_localizer["Email_PasswordReset_IgnoreIfNotRequested"]}</p>";

        return BuildEmailHtml(content, isRtl);
    }

    /// <summary>
    /// Builds email for account creation notification.
    /// </summary>
    public string BuildAccountCreatedEmail(string userName, string email, string temporaryPassword)
    {
        var culture = _localizer["Culture"].Value;
        var isRtl = culture == "he-IL";

        var content = $@"
<p>{_localizer["Email_Greeting", userName]}</p>
<p>{_localizer["Email_AccountCreated_Message"]}</p>
<ul>
    <li><strong>{_localizer["Email_Username"]}:</strong> {email}</li>
    <li><strong>{_localizer["Email_TemporaryPassword"]}:</strong> {temporaryPassword}</li>
</ul>
<p>{_localizer["Email_AccountCreated_ChangePassword"]}</p>
<p>{_localizer["Email_ThankYou"]}</p>";

        return BuildEmailHtml(content, isRtl);
    }

    /// <summary>
    /// Builds email for swap request notification to the other employee.
    /// </summary>
    public string BuildSwapRequestEmail(string employeeName, string requesterName, DateTime shiftDate, string shiftType)
    {
        var culture = _localizer["Culture"].Value;
        var isRtl = culture == "he-IL";

        var content = $@"
<p>{_localizer["Email_Greeting", employeeName]}</p>
<p>{_localizer["Email_SwapRequest_Message", requesterName]}</p>
<ul>
    <li><strong>{_localizer["Email_ShiftDate"]}:</strong> {shiftDate:D}</li>
    <li><strong>{_localizer["Email_ShiftType"]}:</strong> {shiftType}</li>
</ul>
<p>{_localizer["Email_SwapRequest_Action"]}</p>";

        return BuildEmailHtml(content, isRtl);
    }

    /// <summary>
    /// Builds email for reminder notification.
    /// </summary>
    public string BuildReminderEmail(string employeeName, string reminderType, DateTime eventDate, string details)
    {
        var culture = _localizer["Culture"].Value;
        var isRtl = culture == "he-IL";

        var content = $@"
<p>{_localizer["Email_Greeting", employeeName]}</p>
<p>{_localizer["Email_Reminder_Message", reminderType]}</p>
<ul>
    <li><strong>{_localizer["Email_EventDate"]}:</strong> {eventDate:D}</li>
    <li><strong>{_localizer["Email_Details"]}:</strong> {details}</li>
</ul>
<p>{_localizer["Email_ThankYou"]}</p>";

        return BuildEmailHtml(content, isRtl);
    }

    /// <summary>
    /// Builds email for general notification.
    /// </summary>
    public string BuildGeneralNotificationEmail(string employeeName, string subject, string message)
    {
        var culture = _localizer["Culture"].Value;
        var isRtl = culture == "he-IL";

        var content = $@"
<p>{_localizer["Email_Greeting", employeeName]}</p>
<h2>{subject}</h2>
<p>{message}</p>
<p>{_localizer["Email_ThankYou"]}</p>";

        return BuildEmailHtml(content, isRtl);
    }
}
