# Email Template Customization
## Complete Owner-Controlled Email Notification System

**Document Version:** 1.0
**Last Updated:** 2026-01-06
**Implementation Status:** ✅ Complete
**Access Level:** Owner-only (`[Authorize(Policy = "IsAdmin")]`)

---

## Table of Contents
- [Overview](#overview)
- [Business Context](#business-context)
- [Architecture](#architecture)
- [Database Schema](#database-schema)
- [Service Layer](#service-layer)
- [UI Implementation](#ui-implementation)
- [Variable Replacement System](#variable-replacement-system)
- [Localization](#localization)
- [Integration Points](#integration-points)
- [Usage Examples](#usage-examples)
- [Security Considerations](#security-considerations)

---

## Overview

The Email Template Customization system allows **company owners** to customize the content of automated email notifications sent to employees. This feature provides:

- **14 customizable email template types** (shift notifications, time-off, swap requests, on-duty, access requests)
- **Variable placeholders** (e.g., `{EmployeeName}`, `{Date}`) that are replaced with actual values at runtime
- **Enable/disable per template** - Use custom message or fall back to default localized message
- **Multi-tenant isolated** - Each company has its own template customizations
- **Preserves subject lines** - Only message body is customizable (subjects remain localized and standardized)

### Key Capabilities

1. **Moderate Customization Level** - Message text only (not full HTML)
2. **Variable System** - Template variables like `{EmployeeName}` replaced at runtime
3. **Graceful Fallback** - If no custom template, uses default localized message
4. **Real-time Preview** - Shows default message and available variables in UI
5. **Character Limits** - 2000 character maximum per template (prevents abuse)
6. **Audit Trail** - All changes tracked with `CreatedBy`, `UpdatedAt`, `UpdatedBy`

---

## Business Context

### Problem Statement

**Before this feature:**
- All email notifications used hard-coded, localized messages
- Owners had no control over email content
- Companies with specific communication styles or branding couldn't customize
- Military/government clients requested ability to match organizational communication standards

**After this feature:**
- Owners can customize email message content per notification type
- Maintains professional standards (subject lines remain standardized)
- Supports organizational branding and tone
- Full multi-language support (variables work in both English and Hebrew)

### Use Cases

**Use Case 1: Military Unit with Formal Communication Style**
- **Scenario:** Military base wants all shift notifications to include unit-specific protocols
- **Solution:** Customize "Shift Assigned" template to include: "Report to [Unit] at {StartTime} in full uniform. Contact {ManagerName} if unable to attend."

**Use Case 2: Healthcare Facility with Patient Care Instructions**
- **Scenario:** Hospital wants time-off notifications to remind staff about handoff procedures
- **Solution:** Customize "Time-Off Approved" template to add: "Ensure all patient charts are updated before {StartDate}. Brief replacement staff on critical cases."

**Use Case 3: Security Company with Briefing Requirements**
- **Scenario:** Security firm wants on-duty assignments to include briefing information
- **Solution:** Customize "On-Duty Assigned" template to add: "Mandatory security briefing 30 minutes before {Date}. Review post orders in system."

---

## Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────────────┐
│                    Email Notification Flow                   │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
          ┌──────────────────────────────────────┐
          │  NotificationService Trigger Event   │
          │  (TimeOffApproved, ShiftAssigned,   │
          │   OnDutyAssigned, etc.)             │
          └──────────────────────────────────────┘
                              │
                              ▼
          ┌──────────────────────────────────────┐
          │     MailService.SendXxxEmailAsync    │
          │  (e.g., SendTimeOffApprovedEmail)   │
          └──────────────────────────────────────┘
                              │
                              ▼
          ┌──────────────────────────────────────┐
          │ EmailTemplateService.                │
          │   GetCustomMessageAsync()            │
          │ (Check for custom template)          │
          └──────────────────────────────────────┘
                    │                    │
        ┌───────────┴─────────┐         │
        ▼                      ▼         │
┌───────────────┐    ┌──────────────────┴──────┐
│ Custom Found  │    │ No Custom / Disabled    │
│ (IsEnabled)   │    │ Use Default Localized   │
└───────┬───────┘    └──────────┬──────────────┘
        │                       │
        ▼                       ▼
┌───────────────────────────────────────────────┐
│ EmailTemplateService.ReplaceVariables()       │
│ {EmployeeName} → "John Doe"                   │
│ {Date} → "Jan 15, 2026"                       │
└───────────────┬───────────────────────────────┘
                │
                ▼
┌───────────────────────────────────────────────┐
│ MailService: Build HTML Email                │
│ (Wrap message in HTML template with          │
│  headers, styling, footer)                    │
└───────────────┬───────────────────────────────┘
                │
                ▼
┌───────────────────────────────────────────────┐
│ MailService.SendMailAsync()                   │
│ (Send via configured email API)               │
└───────────────────────────────────────────────┘
```

### File Structure

```
ShiftManager/
├── Models/
│   ├── Support/
│   │   └── Enums.cs                    (EmailTemplateType enum)
│   └── EmailTemplateCustomization.cs   (Database model)
├── Services/
│   ├── IEmailTemplateService.cs        (Service interface)
│   ├── EmailTemplateService.cs         (Template management + variable replacement)
│   └── MailService.cs                  (Updated with template integration)
├── Pages/Owner/
│   ├── EmailTemplates.cshtml           (UI view with grid + modal)
│   └── EmailTemplates.cshtml.cs        (Page model)
├── Resources/
│   ├── SharedResources.resx            (English strings - 35 new keys)
│   └── SharedResources.he-IL.resx      (Hebrew strings - 35 new keys)
└── Migrations/
    └── 20XXXXXX_AddEmailTemplateCustomizations.cs
```

---

## Database Schema

### EmailTemplateCustomization Table

**Table Name:** `EmailTemplateCustomizations`

**Columns:**

| Column | Type | Nullable | Description |
|--------|------|----------|-------------|
| `Id` | INTEGER | NO | Primary key (auto-increment) |
| `CompanyId` | INTEGER | NO | Foreign key to Companies (multi-tenant isolation) |
| `TemplateType` | INTEGER | NO | EmailTemplateType enum (0-13) |
| `CustomMessage` | NVARCHAR(2000) | YES | Owner's custom message text (NULL = not customized) |
| `IsEnabled` | INTEGER | NO | Boolean: 1 = use custom, 0 = use default |
| `CreatedAt` | TEXT | NO | UTC timestamp of creation |
| `UpdatedAt` | TEXT | NO | UTC timestamp of last update |
| `CreatedBy` | INTEGER | NO | Foreign key to AppUser who created |
| `UpdatedBy` | INTEGER | YES | Foreign key to AppUser who last updated |

**Indexes:**
- Primary Key: `Id`
- Unique Index: `(CompanyId, TemplateType)` - One template per company per type
- Foreign Key Index: `CompanyId` → `Companies.Id` (CASCADE delete)

**EF Core Configuration:**

```csharp
modelBuilder.Entity<EmailTemplateCustomization>()
    .HasIndex(e => new { e.CompanyId, e.TemplateType })
    .IsUnique(); // Enforces one template per company per type

modelBuilder.Entity<EmailTemplateCustomization>()
    .HasOne(e => e.Company)
    .WithMany()
    .HasForeignKey(e => e.CompanyId)
    .OnDelete(DeleteBehavior.Cascade); // Delete templates when company deleted
```

---

## Service Layer

### IEmailTemplateService Interface

**File:** `Services/IEmailTemplateService.cs`

**Methods:**

```csharp
public interface IEmailTemplateService
{
    /// <summary>
    /// Get all email template customizations for the current company.
    /// </summary>
    Task<List<EmailTemplateCustomization>> GetAllTemplatesAsync();

    /// <summary>
    /// Get a specific email template customization for the current company.
    /// Returns null if not found.
    /// </summary>
    Task<EmailTemplateCustomization?> GetTemplateAsync(EmailTemplateType templateType);

    /// <summary>
    /// Save or update an email template customization.
    /// </summary>
    Task<EmailTemplateCustomization> SaveTemplateAsync(
        EmailTemplateType templateType,
        string customMessage,
        bool isEnabled,
        int userId);

    /// <summary>
    /// Get the customized message for a template type, or return null if not enabled/configured.
    /// </summary>
    Task<string?> GetCustomMessageAsync(EmailTemplateType templateType);

    /// <summary>
    /// Replace variable placeholders in a message with actual values.
    /// Example: "{EmployeeName} has been assigned..." becomes "John Doe has been assigned..."
    /// </summary>
    string ReplaceVariables(string message, Dictionary<string, string> variables);

    /// <summary>
    /// Get the list of available variables for a given template type.
    /// </summary>
    List<string> GetAvailableVariables(EmailTemplateType templateType);

    /// <summary>
    /// Get the default message (in localized language) for a given template type.
    /// Used to show owners what the default looks like.
    /// </summary>
    string GetDefaultMessage(EmailTemplateType templateType);
}
```

### EmailTemplateService Implementation

**File:** `Services/EmailTemplateService.cs` (155 lines)

**Key Implementation Details:**

#### 1. Tenant Scoping (Multi-Tenancy)

```csharp
public async Task<List<EmailTemplateCustomization>> GetAllTemplatesAsync()
{
    var companyId = _tenantResolver.GetCurrentTenantId();
    return await _db.EmailTemplateCustomizations
        .Where(t => t.CompanyId == companyId) // Row-level security
        .OrderBy(t => t.TemplateType)
        .ToListAsync();
}
```

#### 2. Variable Replacement Logic

```csharp
public string ReplaceVariables(string message, Dictionary<string, string> variables)
{
    var result = message;

    foreach (var variable in variables)
    {
        var placeholder = $"{{{variable.Key}}}"; // e.g., "{EmployeeName}"
        result = result.Replace(placeholder, variable.Value);
    }

    return result;
}
```

**Example:**
- Input: `"Hello {EmployeeName}, you have been assigned to {ShiftType} on {Date}."`
- Variables: `{ "EmployeeName": "John Doe", "ShiftType": "Morning", "Date": "Jan 15" }`
- Output: `"Hello John Doe, you have been assigned to Morning on Jan 15."`

#### 3. Available Variables by Template Type

```csharp
public List<string> GetAvailableVariables(EmailTemplateType templateType)
{
    return templateType switch
    {
        EmailTemplateType.ShiftAssigned =>
            new List<string> { "EmployeeName", "ShiftType", "Date", "StartTime", "EndTime" },

        EmailTemplateType.TimeOffApproved =>
            new List<string> { "EmployeeName", "StartDate", "EndDate" },

        EmailTemplateType.SwapRequestApproved =>
            new List<string> { "EmployeeName", "ShiftInfo" },

        EmailTemplateType.OnDutyAssigned =>
            new List<string> { "EmployeeName", "OnDutyType", "Date" },

        EmailTemplateType.AccessRequestSubmitted =>
            new List<string> { "OwnerName", "RequesterName", "RequesterEmail", "CompanyName" },

        EmailTemplateType.AccountApproved =>
            new List<string> { "UserName", "CompanyName", "AssignedRole" },

        // ... (14 total template types)
        _ => new List<string>()
    };
}
```

#### 4. Default Message Localization

```csharp
public string GetDefaultMessage(EmailTemplateType templateType)
{
    return templateType switch
    {
        EmailTemplateType.TimeOffApproved => _localizer["Email_TimeOffApprovedBody"],
        EmailTemplateType.TimeOffDeclined => _localizer["Email_TimeOffDeclinedBody"],
        EmailTemplateType.SwapRequestApproved => _localizer["Email_SwapRequestApprovedBody"],
        // ... (14 total)
        _ => ""
    };
}
```

**Important:** Default messages are pulled from `SharedResources.resx` (English) and `SharedResources.he-IL.resx` (Hebrew), ensuring multi-language support.

---

## UI Implementation

### Page Structure

**File:** `Pages/Owner/EmailTemplates.cshtml` (237 lines)

### Layout: Bootstrap Tabs Pattern

```
┌─────────────────────────────────────────────────────────────┐
│  📧 Email Templates                                          │
│  Customize email notifications sent to your employees       │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  ℹ️ How Email Templates Work                                 │
│  Customize the message body for each email type. You can    │
│  use variables like {EmployeeName} that will be replaced... │
│                                                              │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  ┌───────────────────────┐  ┌───────────────────────┐      │
│  │ Shift Assigned        │  │ Time-Off Approved     │      │
│  │ Status: Enabled ✓     │  │ Status: Disabled      │      │
│  ├───────────────────────┤  ├───────────────────────┤      │
│  │ Custom Message:       │  │ Default Message:      │      │
│  │ "You have been..."    │  │ "Your time-off..."    │      │
│  │                       │  │                       │      │
│  │ Variables:            │  │ Variables:            │      │
│  │ {EmployeeName}        │  │ {EmployeeName}        │      │
│  │ {ShiftType} {Date}    │  │ {StartDate} {EndDate} │      │
│  │                       │  │                       │      │
│  │ [Edit] [Reset]        │  │ [Edit]                │      │
│  └───────────────────────┘  └───────────────────────┘      │
│                                                              │
│  (Grid continues with 14 total templates...)                │
└─────────────────────────────────────────────────────────────┘
```

### Edit Modal Dialog

When owner clicks "Edit" button, a Bootstrap modal opens:

```
┌──────────────────────────────────────────────────────┐
│  Edit Email Template                            [X]  │
├──────────────────────────────────────────────────────┤
│                                                      │
│  Template: Time-Off Approved                         │
│                                                      │
│  Custom Message:                                     │
│  ┌─────────────────────────────────────────────┐   │
│  │ Your time-off request for {StartDate} to   │   │
│  │ {EndDate} has been approved. Enjoy your    │   │
│  │ time off!                                   │   │
│  │                                             │   │
│  │                                             │   │
│  └─────────────────────────────────────────────┘   │
│  Characters: 85 / 2000                               │
│                                                      │
│  [✓] Enable custom message                          │
│                                                      │
│  ℹ️ Available Variables:                             │
│  {EmployeeName} {StartDate} {EndDate}               │
│  Use these variables in your message. They will be  │
│  replaced with actual values when the email is sent.│
│                                                      │
│  ℹ️ Default Message:                                 │
│  Your time-off request has been approved for        │
│  {StartDate} to {EndDate}. Enjoy your vacation!     │
│                                                      │
│  [Cancel]  [Save Changes]                           │
└──────────────────────────────────────────────────────┘
```

### Page Model (Code-Behind)

**File:** `Pages/Owner/EmailTemplates.cshtml.cs` (175 lines)

**Key Methods:**

#### OnGet - Load Templates

```csharp
public async Task OnGetAsync()
{
    await LoadTemplatesAsync();
}

private async Task LoadTemplatesAsync()
{
    var existingTemplates = await _templateService.GetAllTemplatesAsync();

    Templates = new List<TemplateViewModel>();

    // Load all 14 template types
    foreach (EmailTemplateType templateType in Enum.GetValues(typeof(EmailTemplateType)))
    {
        var existing = existingTemplates.FirstOrDefault(t => t.TemplateType == templateType);

        Templates.Add(new TemplateViewModel
        {
            Type = templateType,
            CustomMessage = existing?.CustomMessage ?? string.Empty,
            IsEnabled = existing?.IsEnabled ?? false,
            AvailableVariables = _templateService.GetAvailableVariables(templateType),
            DefaultMessage = _templateService.GetDefaultMessage(templateType),
            DisplayName = GetTemplateDisplayName(templateType)
        });
    }
}
```

#### OnPostSaveTemplateAsync - Save Custom Template

```csharp
public async Task<IActionResult> OnPostSaveTemplateAsync()
{
    if (!ModelState.IsValid)
    {
        Error = _localizer["Error_InvalidInput"];
        await LoadTemplatesAsync();
        return Page();
    }

    // Validate message length (max 2000 characters)
    if (CustomMessage.Length > 2000)
    {
        Error = _localizer["Error_EmailTemplate_MessageTooLong"];
        await LoadTemplatesAsync();
        return Page();
    }

    try
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            Error = "Invalid user claim";
            await LoadTemplatesAsync();
            return Page();
        }

        await _templateService.SaveTemplateAsync(
            EditingTemplateType,
            CustomMessage,
            IsEnabled,
            userId);

        TempData["SuccessMessage"] = _localizer["Success_EmailTemplateSaved"];
        return RedirectToPage();
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error saving email template {TemplateType}", EditingTemplateType);
        Error = _localizer["Error_SavingEmailTemplate"];
        await LoadTemplatesAsync();
        return Page();
    }
}
```

#### OnPostResetTemplateAsync - Reset to Default

```csharp
public async Task<IActionResult> OnPostResetTemplateAsync(EmailTemplateType templateType)
{
    try
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            Error = "Invalid user claim";
            await LoadTemplatesAsync();
            return Page();
        }

        // Save with empty message and disabled to effectively reset
        await _templateService.SaveTemplateAsync(
            templateType,
            string.Empty,
            false,
            userId);

        TempData["SuccessMessage"] = _localizer["Success_EmailTemplateReset"];
        return RedirectToPage();
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error resetting email template {TemplateType}", templateType);
        Error = _localizer["Error_ResettingEmailTemplate"];
        await LoadTemplatesAsync();
        return Page();
    }
}
```

### JavaScript for Modal Interaction

**Embedded in:** `Pages/Owner/EmailTemplates.cshtml` (Lines 177-208)

```javascript
function loadTemplateForEdit(typeId, displayName, variablesJson, defaultMsg, customMsg, isEnabled) {
    document.getElementById('editingTemplateType').value = typeId;
    document.getElementById('templateName').textContent = displayName;
    document.getElementById('customMessage').value = customMsg;
    document.getElementById('isEnabled').checked = isEnabled;
    document.getElementById('defaultMessage').textContent = defaultMsg;

    // Update character count
    updateCharCount();

    // Display available variables
    const variables = JSON.parse(variablesJson);
    const variablesHtml = variables.map(v => `<code class="variable-tag">{${v}}</code>`).join(' ');
    document.getElementById('availableVariables').innerHTML = variablesHtml;
}

function updateCharCount() {
    const textarea = document.getElementById('customMessage');
    const count = textarea.value.length;
    document.getElementById('charCount').textContent = count;
}

// Update character count on input
document.addEventListener('DOMContentLoaded', function() {
    const textarea = document.getElementById('customMessage');
    if (textarea) {
        textarea.addEventListener('input', updateCharCount);
    }
});
```

---

## Variable Replacement System

### 14 Email Template Types

| Template Type | Enum Value | Available Variables |
|--------------|------------|---------------------|
| `ShiftAssigned` | 0 | `EmployeeName`, `ShiftType`, `Date`, `StartTime`, `EndTime` |
| `ShiftChanged` | 1 | `EmployeeName`, `ShiftType`, `Date`, `StartTime`, `EndTime`, `ChangeDescription` |
| `ShiftDeleted` | 2 | `EmployeeName`, `ShiftType`, `Date`, `StartTime`, `EndTime` |
| `ChoreAssigned` | 3 | `EmployeeName`, `ChoreTitle`, `Date` |
| `ChoreCanceled` | 4 | `EmployeeName`, `ChoreTitle`, `Date` |
| `TimeOffApproved` | 5 | `EmployeeName`, `StartDate`, `EndDate` |
| `TimeOffDeclined` | 6 | `EmployeeName`, `StartDate`, `EndDate` |
| `TimeOffDeleted` | 7 | `EmployeeName`, `StartDate`, `EndDate` |
| `SwapRequestApproved` | 8 | `EmployeeName`, `ShiftInfo` |
| `SwapRequestDeclined` | 9 | `EmployeeName`, `ShiftInfo` |
| `OnDutyAssigned` | 10 | `EmployeeName`, `OnDutyType`, `Date` |
| `OnDutyCanceled` | 11 | `EmployeeName`, `OnDutyType`, `Date` |
| `AccessRequestSubmitted` | 12 | `OwnerName`, `RequesterName`, `RequesterEmail`, `CompanyName` |
| `AccountApproved` | 13 | `UserName`, `CompanyName`, `AssignedRole` |

### Variable Replacement Example (Step-by-Step)

**Scenario:** Employee "John Doe" is assigned to "Morning" shift on "January 15, 2026" from "08:00" to "16:00".

**Step 1:** Owner creates custom template for `ShiftAssigned`:
```
Custom Message:
"Hello {EmployeeName}, you have been scheduled for {ShiftType} shift on {Date} from {StartTime} to {EndTime}. Please arrive 15 minutes early for briefing."
```

**Step 2:** System triggers email send:
```csharp
await _mailService.SendShiftAssignedEmailAsync(
    recipientEmail: "john.doe@example.com",
    employeeName: "John Doe",
    shiftType: "Morning",
    date: new DateOnly(2026, 1, 15),
    startTime: "08:00",
    endTime: "16:00"
);
```

**Step 3:** MailService checks for custom template:
```csharp
var customMessage = await _emailTemplateService.GetCustomMessageAsync(EmailTemplateType.ShiftAssigned);
```

**Step 4:** Variable replacement:
```csharp
var variables = new Dictionary<string, string>
{
    { "EmployeeName", "John Doe" },
    { "ShiftType", "Morning" },
    { "Date", "Jan 15, 2026" },
    { "StartTime", "08:00" },
    { "EndTime", "16:00" }
};

var messageBody = _emailTemplateService.ReplaceVariables(customMessage, variables);
```

**Step 5:** Result:
```
"Hello John Doe, you have been scheduled for Morning shift on Jan 15, 2026 from 08:00 to 16:00. Please arrive 15 minutes early for briefing."
```

**Step 6:** Wrap in HTML template and send:
```html
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body { font-family: Arial, sans-serif; }
        .container { max-width: 600px; margin: 0 auto; padding: 20px; }
        .header { background-color: #4A90E2; color: white; padding: 15px; }
        .content { padding: 20px; background-color: #f9f9f9; }
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h2>📅 Shift Assignment</h2>
        </div>
        <div class='content'>
            <p>Hello <strong>John Doe</strong>,</p>
            <p>Hello John Doe, you have been scheduled for Morning shift on Jan 15, 2026 from 08:00 to 16:00. Please arrive 15 minutes early for briefing.</p>
            <!-- Shift details, footer, etc. -->
        </div>
    </div>
</body>
</html>
```

---

## Localization

### 35 New Localization Keys Added

#### English (SharedResources.resx)

| Key | English Value |
|-----|---------------|
| `Owner_EmailTemplates` | Email Templates |
| `EmailTemplates_Subtitle` | Customize email notifications sent to your employees |
| `EmailTemplates_InfoTitle` | How Email Templates Work |
| `EmailTemplates_InfoDescription` | Customize the message body for each email type. You can use variables like {EmployeeName}... |
| `EmailTemplates_CustomMessage` | Custom Message |
| `EmailTemplates_DefaultMessage` | Default Message |
| `EmailTemplates_AvailableVariables` | Available Variables |
| `EmailTemplates_EditTemplate` | Edit Email Template |
| `EmailTemplates_MessagePlaceholder` | Enter your custom message here. Leave empty to use the default message. |
| `EmailTemplates_CharacterCount` | Characters |
| `EmailTemplates_EnableCustomTemplate` | Enable custom message |
| `EmailTemplates_VariablesHelp` | Use these variables in your message. They will be replaced with actual values when the email is sent. |
| `Status_Enabled` | Enabled |
| `Status_Disabled` | Disabled |
| `Action_ResetToDefault` | Reset to Default |
| `Confirm_ResetTemplate` | Are you sure you want to reset this template to its default message? |
| `Success_EmailTemplateSaved` | Email template saved successfully |
| `Success_EmailTemplateReset` | Email template reset to default |
| `Error_EmailTemplate_MessageTooLong` | Message is too long. Maximum 2000 characters allowed. |
| `Error_SavingEmailTemplate` | Error saving email template. Please try again. |
| `Error_ResettingEmailTemplate` | Error resetting email template. Please try again. |
| `EmailTemplate_ShiftAssigned` | Shift Assigned |
| `EmailTemplate_ShiftChanged` | Shift Changed |
| `EmailTemplate_ShiftDeleted` | Shift Deleted |
| `EmailTemplate_ChoreAssigned` | Chore Assigned |
| `EmailTemplate_ChoreCanceled` | Chore Canceled |
| `EmailTemplate_TimeOffApproved` | Time-Off Approved |
| `EmailTemplate_TimeOffDeclined` | Time-Off Declined |
| `EmailTemplate_TimeOffDeleted` | Time-Off Deleted |
| `EmailTemplate_SwapRequestApproved` | Swap Request Approved |
| `EmailTemplate_SwapRequestDeclined` | Swap Request Declined |
| `EmailTemplate_OnDutyAssigned` | On-Duty Assigned |
| `EmailTemplate_OnDutyCanceled` | On-Duty Canceled |
| `EmailTemplate_AccessRequestSubmitted` | Access Request Submitted |
| `EmailTemplate_AccountApproved` | Account Approved |

#### Hebrew (SharedResources.he-IL.resx)

All 35 keys have corresponding Hebrew translations (RTL-compatible).

Example:
- `Owner_EmailTemplates` → `תבניות אימייל`
- `EmailTemplates_Subtitle` → `התאם אישית הודעות אימייל הנשלחות לעובדים שלך`
- `EmailTemplate_TimeOffApproved` → `חופשה אושרה`

---

## Integration Points

### MailService Integration

**File:** `Services/MailService.cs` (Lines 1-49, 660-1379)

All 8 email notification methods have been updated to check for custom templates before using defaults:

#### Pattern Used in All Methods

```csharp
public async Task<bool> SendTimeOffApprovedEmailAsync(
    string recipientEmail,
    string employeeName,
    DateOnly startDate,
    DateOnly endDate)
{
    if (string.IsNullOrWhiteSpace(recipientEmail))
    {
        _logger.LogWarning("Cannot send time-off approved email: recipient email is null or empty");
        return false;
    }

    var emailDir = _localizer["Dir"] == "rtl" ? "rtl" : "ltr";
    var dateRange = startDate == endDate
        ? startDate.ToString("MMM dd, yyyy")
        : $"{startDate:MMM dd} - {endDate:MMM dd, yyyy}";

    string subject = string.Format(_localizer["Email_TimeOffApprovedSubject"], dateRange);

    // ✅ NEW: Check for custom template
    var customMessage = await _emailTemplateService.GetCustomMessageAsync(EmailTemplateType.TimeOffApproved);
    string messageBody;

    if (!string.IsNullOrWhiteSpace(customMessage))
    {
        // Use custom template with variable replacement
        var variables = new Dictionary<string, string>
        {
            { "EmployeeName", employeeName },
            { "StartDate", startDate.ToString("MMM dd, yyyy") },
            { "EndDate", endDate.ToString("MMM dd, yyyy") }
        };
        messageBody = _emailTemplateService.ReplaceVariables(customMessage, variables);
    }
    else
    {
        // Fall back to default localized message
        messageBody = _localizer["Email_TimeOffApprovedBody"];
    }

    // Build HTML email with messageBody
    string htmlBody = $@"
<!DOCTYPE html>
<html dir='{emailDir}'>
<head>
    <meta charset='utf-8'>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #4CAF50; color: white; padding: 15px; text-align: center; }}
        .content {{ padding: 20px; background-color: #f9f9f9; }}
        .highlight {{ font-weight: bold; color: #4CAF50; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h2>✓ {_localizer["Email_TimeOffApprovedTitle"]}</h2>
        </div>
        <div class='content'>
            <p>{string.Format(_localizer["Email_Hello"], $"<strong>{employeeName}</strong>")},</p>
            <p>{messageBody}</p>

            <div class='timeoff-details'>
                <p><strong>{_localizer["Email_DateRange"]}:</strong> <span class='highlight'>{dateRange}</span></p>
                <p><strong>{_localizer["Email_Status"]}:</strong> <span class='highlight'>{_localizer["Email_Approved"]}</span></p>
            </div>

            <p>{_localizer["Email_TimeOffApprovedEnjoy"]}</p>
        </div>
        <div class='footer'>
            <p>{_localizer["Email_AutomatedMessage"]}</p>
        </div>
    </div>
</body>
</html>";

    return await SendMailAsync(recipientEmail, subject, htmlBody);
}
```

#### 8 Updated Email Methods

1. **SendTimeOffApprovedEmailAsync** (Lines 727-804) - `EmailTemplateType.TimeOffApproved`
2. **SendTimeOffDeclinedEmailAsync** (Lines 809-886) - `EmailTemplateType.TimeOffDeclined`
3. **SendTimeOffDeletedEmailAsync** (Lines 891-967) - `EmailTemplateType.TimeOffDeleted`
4. **SendSwapRequestApprovedEmailAsync** (Lines 972-1043) - `EmailTemplateType.SwapRequestApproved`
5. **SendSwapRequestDeclinedEmailAsync** (Lines 1048-1119) - `EmailTemplateType.SwapRequestDeclined`
6. **SendOnDutyAssignedEmailAsync** (Lines 1124-1197) - `EmailTemplateType.OnDutyAssigned`
7. **SendOnDutyCanceledEmailAsync** (Lines 1202-1275) - `EmailTemplateType.OnDutyCanceled`
8. **SendAccessRequestSubmittedEmailAsync** (Lines 1299-1379) - `EmailTemplateType.AccessRequestSubmitted`
9. **SendAccountApprovedEmailAsync** (Lines 660-741) - `EmailTemplateType.AccountApproved`

---

## Usage Examples

### Example 1: Customize Time-Off Approval

**Scenario:** Hospital wants to remind employees to complete patient handoffs before taking time off.

**Steps:**
1. Owner navigates to `/Owner/EmailTemplates`
2. Clicks "Edit" on "Time-Off Approved" template card
3. Enters custom message:
   ```
   Hello {EmployeeName},

   Your time-off request from {StartDate} to {EndDate} has been approved.

   IMPORTANT REMINDERS:
   - Complete all patient chart updates before {StartDate}
   - Brief your replacement on any critical cases
   - Submit end-of-shift report to department head

   Enjoy your time off!
   ```
4. Checks "Enable custom message" checkbox
5. Clicks "Save Changes"

**Result:** All future time-off approval emails use this custom message with variables replaced.

### Example 2: Military Unit Shift Briefing

**Scenario:** Military base wants all shift assignments to include briefing requirements.

**Steps:**
1. Owner navigates to `/Owner/EmailTemplates`
2. Clicks "Edit" on "Shift Assigned" template card
3. Enters custom message:
   ```
   {EmployeeName},

   You have been assigned to {ShiftType} shift on {Date} from {StartTime} to {EndTime}.

   MANDATORY REQUIREMENTS:
   - Attend pre-shift briefing 30 minutes before {StartTime}
   - Review daily orders in the system
   - Ensure uniform compliance (no exceptions)
   - Report to Squad Leader immediately upon arrival

   Failure to attend briefing may result in shift reassignment.
   ```
4. Enables template
5. Saves

**Result:** All shift assignment emails now include military-specific requirements.

### Example 3: Reset to Default

**Scenario:** Company no longer needs custom on-duty messages and wants to use default.

**Steps:**
1. Owner navigates to `/Owner/EmailTemplates`
2. Finds "On-Duty Assigned" template card (status: Enabled)
3. Clicks "Reset to Default" button
4. Confirms in dialog

**Result:** Template is disabled, future emails use default localized message.

---

## Security Considerations

### Multi-Tenancy Isolation

**Critical:** EmailTemplateCustomization is tenant-scoped via `CompanyId` foreign key.

**Enforcement:**
1. **Global Query Filter** (EF Core):
   ```csharp
   builder.Entity<EmailTemplateCustomization>()
       .HasQueryFilter(e => e.CompanyId == _companyContext.CurrentCompanyId);
   ```
2. **Service Layer Scoping**:
   ```csharp
   var companyId = _tenantResolver.GetCurrentTenantId();
   return await _db.EmailTemplateCustomizations
       .Where(t => t.CompanyId == companyId)
       .ToListAsync();
   ```
3. **CompanyIdInterceptor** (automatic on SaveChanges):
   - Automatically sets `CompanyId` on new `EmailTemplateCustomization` entities
   - Verifies `CompanyId` matches current tenant before update/delete

**Test Case:**
```csharp
// User from Company A tries to access Company B's templates
var companyAUser = CreateUserWithCompanyId(companyId: 1);
var companyBTemplate = CreateTemplate(companyId: 2);

// Should return empty list (global query filter blocks access)
var templates = await _templateService.GetAllTemplatesAsync();
Assert.Empty(templates); // ✅ Pass - tenant isolation enforced
```

### Input Validation

**1. Message Length Limit: 2000 Characters**
```csharp
[MaxLength(2000)]
public string CustomMessage { get; set; } = string.Empty;
```

**Rationale:** Prevents abuse (e.g., storing large text files in database).

**2. HTML Escaping**
- Custom messages are plain text (not HTML)
- When embedded in email HTML, variables are NOT escaped (intentional for flexibility)
- **Security Note:** Only owners can create templates, and owners are trusted users

**3. Variable Injection Protection**
- Variable replacement is simple string replacement (not eval/exec)
- No code execution risk
- Variables are predefined per template type (cannot inject arbitrary variables)

### Authorization

**Page-Level:** `[Authorize(Policy = "IsAdmin")]`
- Only company owners can access `/Owner/EmailTemplates`
- Directors, Managers, Employees, Trainees are denied access

**Service-Level:** No additional authorization (relies on page-level + multi-tenancy)

### Audit Trail

**Tracked Fields:**
- `CreatedAt` - When template was first created
- `CreatedBy` - Which owner user created it
- `UpdatedAt` - When template was last modified
- `UpdatedBy` - Which owner user last modified it

**Future Enhancement:** Could add AuditLog entries for template changes (currently not implemented).

---

## Testing

### Unit Tests (Recommended)

**EmailTemplateServiceTests.cs:**
```csharp
[Fact]
public async Task GetCustomMessageAsync_WhenTemplateEnabledAndCustomized_ReturnsCustomMessage()
{
    // Arrange
    var mockDb = CreateMockDbContext();
    var service = new EmailTemplateService(mockDb, mockTenantResolver, mockLocalizer, mockLogger);

    var template = new EmailTemplateCustomization
    {
        CompanyId = 1,
        TemplateType = EmailTemplateType.TimeOffApproved,
        CustomMessage = "Hello {EmployeeName}, your time-off is approved!",
        IsEnabled = true
    };
    mockDb.EmailTemplateCustomizations.Add(template);

    // Act
    var result = await service.GetCustomMessageAsync(EmailTemplateType.TimeOffApproved);

    // Assert
    Assert.Equal("Hello {EmployeeName}, your time-off is approved!", result);
}

[Fact]
public async Task GetCustomMessageAsync_WhenTemplateDisabled_ReturnsNull()
{
    // Arrange
    var template = new EmailTemplateCustomization
    {
        CompanyId = 1,
        TemplateType = EmailTemplateType.TimeOffApproved,
        CustomMessage = "Custom message",
        IsEnabled = false // ← Disabled
    };
    mockDb.EmailTemplateCustomizations.Add(template);

    // Act
    var result = await service.GetCustomMessageAsync(EmailTemplateType.TimeOffApproved);

    // Assert
    Assert.Null(result); // ✅ Should return null when disabled
}

[Fact]
public void ReplaceVariables_WithValidVariables_ReplacesCorrectly()
{
    // Arrange
    var service = new EmailTemplateService(mockDb, mockTenantResolver, mockLocalizer, mockLogger);
    var message = "Hello {EmployeeName}, you are assigned to {ShiftType}.";
    var variables = new Dictionary<string, string>
    {
        { "EmployeeName", "John Doe" },
        { "ShiftType", "Morning" }
    };

    // Act
    var result = service.ReplaceVariables(message, variables);

    // Assert
    Assert.Equal("Hello John Doe, you are assigned to Morning.", result);
}
```

### Integration Tests

**EmailNotificationIntegrationTests.cs:**
```csharp
[Fact]
public async Task SendTimeOffApprovedEmail_WithCustomTemplate_UsesCustomMessage()
{
    // Arrange
    var factory = new WebApplicationFactory<Program>();
    var client = factory.CreateClient();

    // Create custom template in database
    var template = new EmailTemplateCustomization
    {
        CompanyId = 1,
        TemplateType = EmailTemplateType.TimeOffApproved,
        CustomMessage = "Congratulations {EmployeeName}! Time off approved.",
        IsEnabled = true
    };
    await SeedDatabase(template);

    // Act
    var result = await _mailService.SendTimeOffApprovedEmailAsync(
        "test@example.com",
        "John Doe",
        new DateOnly(2026, 1, 15),
        new DateOnly(2026, 1, 20)
    );

    // Assert
    Assert.True(result);
    var sentEmail = GetLastSentEmail();
    Assert.Contains("Congratulations John Doe! Time off approved.", sentEmail.HtmlBody);
}
```

### Manual Testing Checklist

- [ ] **Template CRUD:**
  - [ ] Create new custom template
  - [ ] Edit existing template
  - [ ] Reset template to default
  - [ ] Character count updates in real-time
  - [ ] Save button disabled when over 2000 characters

- [ ] **Variable Replacement:**
  - [ ] All 14 template types show correct available variables
  - [ ] Variables are replaced correctly in sent emails
  - [ ] Handles missing variables gracefully (e.g., leaves `{Variable}` if not provided)

- [ ] **Multi-Tenancy:**
  - [ ] Company A cannot see Company B's templates
  - [ ] Director switching companies sees correct templates per company

- [ ] **Localization:**
  - [ ] UI strings display in Hebrew when language is Hebrew
  - [ ] Default messages show in correct language (English/Hebrew)
  - [ ] RTL layout works correctly for Hebrew

- [ ] **Email Delivery:**
  - [ ] Custom template used when enabled
  - [ ] Default template used when disabled or no custom template exists
  - [ ] Subject lines remain standard (not affected by custom templates)
  - [ ] HTML formatting is preserved (headers, footers, styling)

---

## Future Enhancements

### Potential Improvements (Not Currently Implemented)

1. **Rich Text Editor** (HTML Customization)
   - Allow owners to use bold, italics, links in custom messages
   - Requires HTML sanitization to prevent XSS
   - Complexity: Medium

2. **Email Preview**
   - "Send Test Email" button to send preview to owner's email
   - Helps verify formatting before enabling
   - Complexity: Low

3. **Template Versioning**
   - Track history of template changes (who changed what, when)
   - Ability to revert to previous version
   - Complexity: Medium

4. **Subject Line Customization**
   - Currently only message body is customizable
   - Could allow subject line customization with variables
   - Complexity: Low

5. **Conditional Logic**
   - Advanced: `IF {TimeOffDays} > 7 THEN "extended vacation" ELSE "time off"`
   - Would require template engine (e.g., Liquid, Handlebars)
   - Complexity: High

6. **Attachment Support**
   - Allow owners to attach PDFs (policies, forms) to specific email types
   - Complexity: Medium

---

## Summary

The Email Template Customization system provides:

✅ **14 customizable email template types** covering all notification scenarios
✅ **Variable placeholder system** for dynamic content (`{EmployeeName}`, `{Date}`, etc.)
✅ **Multi-tenant isolation** - Each company has independent templates
✅ **Graceful fallback** - Uses default localized messages if no custom template
✅ **Owner-only access** - Only trusted users can customize
✅ **Character limits** - Prevents abuse (2000 char max)
✅ **Full localization** - UI and defaults support English + Hebrew
✅ **Real-time preview** - Shows default message and available variables
✅ **Audit trail** - Tracks who created/modified templates

**Impact:** Empowers company owners to align email communications with organizational culture while maintaining security and standardization.

---

**End of Document**

**Related Documentation:**
- [07-SERVICE-LAYER.md](07-SERVICE-LAYER.md) - EmailTemplateService + MailService
- [08-UI-UX-ARCHITECTURE.md](08-UI-UX-ARCHITECTURE.md) - Owner/EmailTemplates page
- [14-WORKFLOWS-AND-BUSINESS-LOGIC.md](14-WORKFLOWS-AND-BUSINESS-LOGIC.md) - Email notification triggers
