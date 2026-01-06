# MailService Usage Examples

## Quick Start

### 1. Enable Email Notifications

Edit `appsettings.json`:
```json
{
  "Email": {
    "Enabled": true,
    "ApiKey": "your-company-api-key-here",
    "ApiUrl": "https://api.yourcompany.com/v1/mail/send",
    "FromAddress": "noreply@yourcompany.com"
  }
}
```

### 2. Restart Application

```bash
dotnet run
```

### 3. Assign a Shift

Navigate to `/Assignments/Manage` and assign an employee to a shift. The employee will automatically receive an email notification.

---

## Email Examples

### Example 1: Shift Assigned Email

**Trigger**: Employee assigned to shift via Assignments/Manage page

**Email Sent**:
```
From: noreply@shiftmanager.local
To: employee@example.com
Subject: New Shift Assignment - Oct 20, 2025

[HTML Email with green theme]
Hello John Doe,

You have been assigned to work a new shift:

Shift Type: Morning Shift
Date: Saturday, October 20, 2025
Time: 08:00 - 16:00

Please log in to the ShiftManager system to view full details.
```

### Example 2: Shift Changed Email

**Trigger**: Shift time or type modified by manager

**Email Sent**:
```
From: noreply@shiftmanager.local
To: employee@example.com
Subject: Shift Change Notification - Oct 20, 2025

[HTML Email with orange theme]
Hello John Doe,

Your shift has been modified:

Shift Type: Afternoon Shift
Date: Saturday, October 20, 2025
Time: 16:00 - 00:00

Change Details: Shift time changed from Morning to Afternoon

Please log in to the ShiftManager system to review the updated shift details.
```

### Example 3: Shift Deleted Email

**Trigger**: Employee removed from shift via Assignments/Manage page

**Email Sent**:
```
From: noreply@shiftmanager.local
To: employee@example.com
Subject: Shift Removed - Oct 20, 2025

[HTML Email with red theme]
Hello John Doe,

Your assigned shift has been removed from the schedule:

Shift Type: Morning Shift
Date: Saturday, October 20, 2025
Time: 08:00 - 16:00

This shift is no longer on your schedule. Please log in to the ShiftManager system to view your updated schedule.
```

---

## Programmatic Usage

### Direct API Call (Advanced)

```csharp
// Inject IMailService into your service/controller
public class MyCustomService
{
    private readonly IMailService _mailService;

    public MyCustomService(IMailService mailService)
    {
        _mailService = mailService;
    }

    public async Task SendCustomNotification()
    {
        // Send generic email
        bool success = await _mailService.SendMailAsync(
            recipient: "manager@example.com",
            subject: "Weekly Report",
            htmlBody: "<h1>Weekly Statistics</h1><p>Total shifts: 120</p>"
        );

        if (success)
        {
            Console.WriteLine("Email sent successfully");
        }
    }

    public async Task NotifyShiftAssignment()
    {
        // Send shift assigned email (pre-formatted template)
        bool success = await _mailService.SendShiftAssignedEmailAsync(
            recipientEmail: "employee@example.com",
            employeeName: "Jane Smith",
            shiftTypeName: "Night Shift",
            shiftDate: new DateOnly(2025, 10, 25),
            startTime: new TimeOnly(0, 0),
            endTime: new TimeOnly(8, 0)
        );

        if (success)
        {
            Console.WriteLine("Shift notification sent");
        }
    }
}
```

### Integration with Existing NotificationService

**Automatic Integration** (already implemented):

When you call:
```csharp
await _notificationService.CreateShiftAddedNotificationAsync(
    userId: 123,
    shiftTypeName: "Morning Shift",
    shiftDate: new DateOnly(2025, 10, 20),
    startTime: new TimeOnly(8, 0),
    endTime: new TimeOnly(16, 0)
);
```

**Two actions occur**:
1. ✅ In-app notification created in UserNotifications table
2. ✅ Email sent to employee's email address (if Email:Enabled = true)

---

## Configuration Examples

### Development Environment

```json
{
  "Email": {
    "Enabled": false,
    "ApiKey": "not-configured",
    "ApiUrl": "https://test.example.com",
    "FromAddress": "dev@localhost"
  }
}
```

**Behavior**: No emails sent, logs show "Email service disabled"

### Staging Environment

```json
{
  "Email": {
    "Enabled": true,
    "ApiKey": "staging-api-key-12345",
    "ApiUrl": "https://staging-mail-api.yourcompany.com/send",
    "FromAddress": "staging@yourcompany.com"
  }
}
```

**Behavior**: Emails sent to staging mail API (test recipients)

### Production Environment

**appsettings.json** (checked into source control):
```json
{
  "Email": {
    "Enabled": true,
    "ApiKey": "",
    "ApiUrl": "",
    "FromAddress": "noreply@yourcompany.com"
  }
}
```

**Environment Variables** (set on production server):
```bash
export Email__ApiKey="prod-api-key-from-vault"
export Email__ApiUrl="https://mail-api.yourcompany.com/v1/send"
```

**Behavior**: Emails sent to production mail API with real recipients

---

## Testing with Mock Mail API

### Using httpbin.org (for testing)

```json
{
  "Email": {
    "Enabled": true,
    "ApiKey": "test-key",
    "ApiUrl": "https://httpbin.org/post",
    "FromAddress": "test@shiftmanager.local"
  }
}
```

**Result**: httpbin.org echoes the request back. Check logs for:
```
[Information] Email sent successfully to employee@example.com. Response: {"json":{"from":"test@shiftmanager.local",...}}
```

### Using Mailtrap.io (for staging)

```json
{
  "Email": {
    "Enabled": true,
    "ApiKey": "your-mailtrap-api-key",
    "ApiUrl": "https://send.api.mailtrap.io/api/send",
    "FromAddress": "noreply@yourcompany.com"
  }
}
```

**Result**: Emails captured in Mailtrap inbox (doesn't send to real recipients)

---

## Monitoring Email Delivery

### Check Application Logs

```bash
# View all email-related logs
dotnet run | grep "Email"

# View only successful sends
dotnet run | grep "Email sent successfully"

# View only failures
dotnet run | grep "Failed to send email"
```

### Sample Log Output

**Success**:
```
[Information] Sending email to john.doe@example.com with subject: New Shift Assignment - Oct 20, 2025
[Information] Email sent successfully to john.doe@example.com. Response: {"status":"sent","messageId":"abc123"}
```

**Failure (API error)**:
```
[Information] Sending email to invalid-email with subject: New Shift Assignment - Oct 20, 2025
[Error] Failed to send email to invalid-email. Status: 400, Response: {"error":"Invalid email address"}
```

**Failure (network error)**:
```
[Information] Sending email to john.doe@example.com with subject: New Shift Assignment - Oct 20, 2025
[Error] HTTP error while sending email to john.doe@example.com: No such host is known
```

**Disabled**:
```
[Information] Email service disabled. Skipping email to john.doe@example.com with subject: New Shift Assignment - Oct 20, 2025
```

---

## Error Scenarios

### Scenario 1: Employee has no email address

**Database**:
```sql
SELECT Email FROM Users WHERE Id = 123;
-- Result: NULL or empty string
```

**Behavior**:
- In-app notification: ✅ Created
- Email: ❌ Skipped (logs: "Cannot send email: recipient is null or empty")
- Shift operation: ✅ Succeeds

### Scenario 2: Mail API returns 500 Internal Server Error

**Behavior**:
- In-app notification: ✅ Created
- Email: ❌ Failed (logs: "Failed to send email. Status: 500")
- Shift operation: ✅ Succeeds (email failure doesn't block)

### Scenario 3: Mail API times out (>30 seconds)

**Behavior**:
- In-app notification: ✅ Created
- Email: ❌ Timed out (logs: "Email request timed out")
- Shift operation: ✅ Succeeds

### Scenario 4: Invalid API key

**Behavior**:
- In-app notification: ✅ Created
- Email: ❌ Failed (logs: "Failed to send email. Status: 401, Response: Unauthorized")
- Shift operation: ✅ Succeeds

**Key Principle**: Email failures NEVER block shift operations. Shift assignments always succeed even if email fails.

---

## Production Checklist

Before deploying to production:

- [ ] Set `Email:Enabled` to `true` in production appsettings.json
- [ ] Configure production API key via environment variable (not hardcoded)
- [ ] Configure production API URL (replace placeholder)
- [ ] Set `Email:FromAddress` to company email (e.g., noreply@yourcompany.com)
- [ ] Test email sending in staging environment
- [ ] Verify API key has correct permissions in mail API
- [ ] Monitor logs for first 24 hours after deployment
- [ ] Set up alerts for email send failures (>10% failure rate)

---

## FAQ

### Q: Can I disable emails temporarily?
**A**: Yes, set `"Enabled": false` in appsettings.json and restart.

### Q: Can I customize email templates?
**A**: Yes, edit HTML in `Services/MailService.cs` methods (SendShiftAssignedEmailAsync, etc.)

### Q: Can I send emails in different languages?
**A**: Not currently. Future enhancement: detect user's preferred language and send localized templates.

### Q: What happens if email API is down?
**A**: Email fails gracefully, logged as error. Shift operation succeeds. In-app notification still created.

### Q: Can I test email sending locally?
**A**: Yes, use httpbin.org or Mailtrap.io as test API endpoint (see Testing section above).

### Q: How do I change email templates?
**A**: Edit HTML strings in MailService.cs:190-220 (SendShiftAssignedEmailAsync), 230-270 (SendShiftChangedEmailAsync), 280-320 (SendShiftDeletedEmailAsync).

---

## Support

**Email not sending?** Check:
1. `Email:Enabled` = true
2. Valid `ApiKey` and `ApiUrl` configured
3. Application logs for errors
4. Mail API is reachable (ping, curl test)

**Need help?** Review MAIL_SERVICE_DOCUMENTATION.md for detailed troubleshooting.

---

**Last Updated**: 2025-10-19
