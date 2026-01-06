# ShiftManager Usage Guide

Complete guide for using ShiftManager effectively across all roles and use cases.

**Version:** 2.0.0
**Last Updated:** 2025-12-04

---

## Table of Contents

1. [Introduction](#introduction)
2. [Understanding Roles & Permissions](#understanding-roles--permissions)
3. [Employee Guide](#employee-guide)
4. [Manager Guide](#manager-guide)
5. [Admin Guide](#admin-guide)
6. [Owner Guide](#owner-guide)
7. [Common Workflows](#common-workflows)
8. [Configuration & Customization](#configuration--customization)
9. [Advanced Features](#advanced-features)
10. [Mobile & Responsive Usage](#mobile--responsive-usage)
11. [Troubleshooting & FAQ](#troubleshooting--faq)
12. [Glossary](#glossary)

---

## Introduction

### About This Guide

This guide provides detailed instructions for using ShiftManager after installation. Whether you're an employee checking your schedule or an administrator managing the entire system, this guide covers your needs.

### Who Should Read This

- **Employees** - View schedules, request time off, swap shifts (see [Employee Guide](#employee-guide))
- **Managers** - Manage teams, create shifts, approve requests (see [Manager Guide](#manager-guide))
- **Admins** - Manage users, configure system, view analytics (see [Admin Guide](#admin-guide))
- **Owners** - Full system access, multi-company management (see [Owner Guide](#owner-guide))

### Prerequisites

- ShiftManager is already installed and running
- You have login credentials (email and password)
- You know your assigned role in the system

**For installation instructions, see [README.md](README.md)**

---

## Understanding Roles & Permissions

### Role Hierarchy

ShiftManager uses a role-based permission system with six levels:

```
Owner (全) → Director (➕) → Admin (⚙️) → Manager (📋) → Employee (👤) → Trainee (🎓)
```

Each role inherits permissions from roles below it.

### Permission Matrix

| Feature | Owner | Director | Admin | Manager | Employee | Trainee |
|---------|-------|----------|-------|---------|----------|---------|
| **View Own Schedule** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Request Time Off** | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ |
| **Request Shift Swaps** | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ |
| **Submit Feedback** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Create Shifts (Own Company)** | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ |
| **Assign Employees to Shifts** | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ |
| **Approve/Reject Requests** | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ |
| **Manage Team Members** | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ |
| **Create/Edit Users** | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| **Manage Companies** | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| **View Analytics** | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| **Configure Shift Types** | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| **Access Multiple Companies** | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| **System Configuration** | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **Feature Flags** | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **Database Console** | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |

### Role-Specific UI Differences

**Employees and Trainees see:**
- My Schedule (Calendar view)
- My Requests (Time Off, Shift Swaps)
- Notifications
- Profile Settings

**Managers additionally see:**
- Team Management
- Shift Creation Tools
- Request Approval Queue
- Team Calendar Views

**Admins additionally see:**
- User Management
- Company Settings
- System Configuration
- Shift Type Management
- Analytics Dashboard
- Audit Logs

**Owners additionally see:**
- Multi-Company Management
- Feature Flags
- Database Console
- Email Configuration
- System Health Monitoring
- Backup & Restore Tools
- Game Configuration

---

## Employee Guide

### Viewing Your Schedule

**Access your calendar:**
1. Login to ShiftManager
2. Click **Calendar** in the navigation menu
3. Select view: **Month**, **Week**, or **Day**

**Calendar Views:**

**Month View:**
- Shows entire month at a glance
- Your shifts highlighted in color
- Click any day to see shift details
- Navigate months with ◀ ▶ buttons

**Week View:**
- Shows one week in detail
- Time slots for each day
- Scheduled shifts and day shifts
- Easy to spot conflicts or gaps

**Day View:**
- Single day detailed view
- All shifts with start/end times
- Who's working each shift
- Day shift assignments (On-Duty)

**Understanding Shift Types:**

See [Glossary](#glossary) for the difference between:
- **Scheduled Shifts** (specific time-bounded work)
- **Day Shifts** (full-day assignments without specific times)

### Requesting Time Off

**Step-by-step:**

1. Navigate to **Requests → Time Off**
2. Click **Request Time Off** button
3. Fill in the form:
   - **Start Date**: First day off
   - **End Date**: Last day off
   - **Reason** (optional): Brief explanation
4. Click **Submit Request**
5. Wait for manager approval

**What happens next:**
- Manager receives notification
- Request shows as "Pending" in your requests list
- You'll receive notification when approved/rejected
- If approved, day shifts with "OFFLINE" type are automatically created

**Tips:**
- Request time off in advance (company policy varies)
- Check your schedule first to avoid conflicts
- Provide reason for better approval chances
- Cancel pending requests if plans change

### Requesting Shift Swaps

**When to use:** You need to swap your assigned shift with another employee.

**Step-by-step:**

1. Navigate to **Requests → Shift Swaps**
2. Click **Request Shift Swap**
3. Select **Your Shift** (the shift you want to give away)
4. Select **Employee** to swap with
5. (Optional) Select **Their Shift** if swapping (not just giving away)
6. Add **Reason** (optional but recommended)
7. Click **Submit Swap Request**

**Approval Process:**
1. Both employees must agree (if bilateral swap)
2. Manager must approve
3. Shifts are automatically reassigned upon approval

**Important Notes:**
- Both shifts must be conflict-free for each employee
- Rest hours are checked automatically
- Weekly hour caps are enforced
- Manager can reject if swap creates issues

### Managing Notifications

**View notifications:**
- Bell icon (🔔) in top navigation
- Number badge shows unread count
- Click to open notification center

**Notification Types:**
- **Shift Assigned** - You've been assigned to a shift
- **Shift Changed** - Your shift details modified
- **Shift Canceled** - You've been removed from a shift
- **Request Approved** - Your time-off or swap request approved
- **Request Rejected** - Your request was denied (with reason)
- **Swap Request** - Someone wants to swap shifts with you

**Mark as read:**
- Click notification to mark as read
- Or click **Mark All as Read**

**Email Notifications (if enabled):**
- You'll receive emails for important events
- Check spam folder if not receiving
- Email preferences set by administrator

### Using the Shift Swap Game

**What is it:** A gamified mini-game where you match colored shift blocks to earn points.

**Access the game:**
1. Click **Game** in navigation menu
2. Game loads with colorful grid

**How to play:**
1. Click and drag to swap adjacent blocks
2. Match 3 or more of the same color to score
3. Longer matches = more points
4. Trigger Mega Combos for bonus multipliers
5. Reach milestones to level up

**Scoring:**
- 3-match: 10 points (default, admin configurable)
- 4-match: 25 points
- 5+ match: 50 points
- Mega Combo: Points × multiplier

**View leaderboard:**
- See top scorers company-wide
- Your best score tracked
- Compete with colleagues

**Note:** Game configuration (points, combos, milestones) set by Owner via Admin Panel.

### Updating Your Profile

**Access profile:**
1. Click your name (top right corner)
2. Select **Profile**

**Update information:**
- **Name**: Full name displayed in system
- **Email**: Login email (must be unique)
- **Password**: Change your password here
- **Language**: Select English or Hebrew (עברית)
- **Theme**: Light or Dark mode

**Change Password:**
1. Enter **Current Password**
2. Enter **New Password** (12+ characters recommended)
3. **Confirm New Password**
4. Click **Update Password**

**Security Tips:**
- Use strong, unique passwords
- Don't share your login credentials
- Log out on shared computers
- Change password if you suspect compromise

---

## Manager Guide

### Managing Your Team

**View team members:**
1. Navigate to **My Team**
2. See all employees in your company
3. Filter by role, status, or search by name

**Assign shift types to employees:**
(Note: Only Admins can create/edit users, but Managers can coordinate assignments)

### Creating Shifts

**Create a Scheduled Shift:**

1. Navigate to **Calendar → Month View**
2. Click on a day to create shift
3. Fill in shift details:
   - **Shift Type**: MORNING, NOON, NIGHT, MIDDLE, or custom
   - **Date**: Shift date
   - **Start Time**: Shift start (from shift type default)
   - **End Time**: Shift end (from shift type default)
   - **Required Staff**: Number of employees needed
4. Click **Create Shift**

**Assign employees to shift:**
1. View shift in calendar
2. Click **Assign Employees**
3. Select employees from dropdown
4. System checks:
   - Conflict with other shifts
   - Minimum rest hours (8 hours default, configurable)
   - Weekly hour cap (40 hours default, configurable)
5. If conflicts detected, red warning shown
6. Click **Assign** to confirm

**Create Day Shifts (On-Duty):**

1. Navigate to **Calendar → Day Shifts** or **Public → On Duty**
2. Click **Create Day Shift Assignment**
3. Select:
   - **Type**: Hakam (🛡️), Lead (⭐), or other on-duty types
   - **Date**: Assignment date
   - **Employee**: Who's on duty
4. Click **Create Assignment**

**Day Shift Types:**
- **Hakam (🛡️)**: Hakam on-duty role
- **Lead (⭐)**: Team lead on-duty role
- **Backup Hakam**: Backup hakam assignments

**Quick Staffing Adjustments:**
- Use **+** and **-** buttons to adjust required staff count
- System prevents reducing below current assignments
- Concurrency control prevents conflicts

### Approving Requests

**Time-Off Requests:**

1. Navigate to **Requests → Index**
2. See all pending time-off requests
3. Review request:
   - Employee name
   - Date range
   - Reason (if provided)
   - Conflicts (if any, highlighted in red)
4. Click **Approve** or **Reject**
5. (Optional) Add rejection reason
6. Employee receives notification

**Shift Swap Requests:**

1. Navigate to **Requests → Swaps**
2. See all pending swap requests
3. Review:
   - Requesting employee
   - Target employee
   - Shifts being swapped
   - Both employees' consent status
4. Verify no conflicts created
5. Click **Approve** or **Reject**
6. Shifts automatically reassigned on approval

**Batch Approvals:**

1. Select multiple requests (checkboxes)
2. Click **Approve Selected** or **Reject Selected**
3. All selected requests processed at once
4. Notifications sent to all affected employees

**Approval Best Practices:**
- Review requests daily to avoid delays
- Check for conflicts and coverage gaps
- Consider staffing needs before approving time off
- Communicate with team about decisions
- Use rejection reasons to help employees understand

### Managing Team Calendar

**View team schedules:**
1. **Calendar → Month View**: See all shifts for month
2. Filter by employee to see individual schedules
3. Color-coded by shift type

**Identify conflicts:**
- Red highlights indicate conflicts
- Click on conflicted shift for details
- System automatically detects:
  - Double-booking (same employee, overlapping times)
  - Insufficient rest (< 8 hours between shifts)
  - Over weekly cap (> 40 hours per week)

**Handle understaffing:**
- Shifts with unfilled slots highlighted
- Use **My Team → Available Employees** filter
- Check who's available for specific dates
- Assign employees to fill gaps

### Team Analytics (Admin+ Required)

**If you have Admin role, access:**
1. **Admin → Analytics**
2. View:
   - Shifts per employee (last 30 days)
   - Average hours per week
   - Request approval rates
   - Coverage statistics
   - Overtime hours
3. Export reports (CSV or PDF)

---

## Admin Guide

### Managing Users

**Create new user:**

1. Navigate to **Admin → Users**
2. Click **Create User**
3. Fill in form:
   - **Name**: Full name
   - **Email**: Unique email (used for login)
   - **Password**: Initial password (user should change after first login)
   - **Role**: Select role (Employee, Manager, Admin, etc.)
   - **Company**: Assign to company
4. Click **Create User**
5. User can now login with email/password

**Edit existing user:**

1. **Admin → Users** → Find user
2. Click **Edit**
3. Update:
   - Name, email, role, or company
   - Reset password (if user forgot)
   - Deactivate user (soft delete)
4. Click **Update User**

**Manage join requests:**

When employees submit self-service signup requests:
1. **Admin → Users** → **Join Requests** tab
2. Review pending requests
3. Verify employee legitimacy
4. Click **Approve** to create user account
5. Or **Reject** with reason
6. Approved users receive login credentials

### Managing Companies

**View all companies:**
1. Navigate to **Admin → Companies**
2. See list of all companies in system

**Create new company:**

1. Click **Create Company**
2. Fill in:
   - **Name**: Company name
   - **Description** (optional)
3. Click **Create**

**Edit company:**

1. Find company in list
2. Click **Edit**
3. Update name or description
4. Click **Update**

**Assign users to company:**
- Done via **Admin → Users → Edit User**
- Select company from dropdown
- User's shifts/data scoped to that company

### Configuring Shift Types

**View shift types:**
1. Navigate to **Admin → Shift Types**
2. See list: MORNING, NOON, NIGHT, MIDDLE, OFFLINE, etc.

**Create custom shift type:**

1. Click **Create Shift Type**
2. Fill in:
   - **Code**: Unique identifier (e.g., "EVENING")
   - **Name (English)**: Display name (e.g., "Evening Shift")
   - **Name (Hebrew)**: עברית translation
   - **Description**: Brief description
   - **Default Start Time**: e.g., 16:00
   - **Default End Time**: e.g., 00:00
   - **Color**: Hex color for calendar (e.g., #FF5733)
3. Click **Create**

**Edit shift type:**

1. Find shift type in list
2. Click **Edit**
3. Update times, names, or color
4. Click **Update**

**Note:** Changing shift type times doesn't affect existing shift instances (only new shifts).

### System Configuration

**Access system config:**
1. Navigate to **Admin → Config**
2. Configure global settings:

**Rest Hours Enforcement:**
- Minimum hours between shifts
- Default: 8 hours
- Prevents employee burnout
- System blocks assignments violating rest hours

**Weekly Hours Cap:**
- Maximum hours per employee per week
- Default: 40 hours
- Prevents overtime violations
- System warns when approaching cap

**Email Configuration:**
- Enable/disable email notifications
- Set API key (encrypted at rest)
- Set API URL
- Set from address
- Test email delivery

**Company Scope Enforcement:**
- Strict multi-tenant isolation (experimental)
- Default: false (admins can see all companies)
- When true: strict company data separation

**Save configuration:**
- Changes saved to database
- Audit log entry created automatically
- Settings apply immediately

### Viewing Analytics

**Access analytics dashboard:**
1. Navigate to **Admin → Analytics**
2. View dashboards:

**Shift Statistics:**
- Total shifts created (last 30 days)
- Shifts by type breakdown
- Average shifts per employee
- Coverage percentages

**Employee Metrics:**
- Total hours worked per employee
- Overtime hours
- Time-off days taken
- Swap request acceptance rates

**Request Analytics:**
- Pending requests count
- Approval/rejection rates
- Average response time
- Most common rejection reasons

**Export Reports:**
- Click **Export** button
- Select format (CSV, PDF)
- Download for offline analysis

### Reviewing Audit Logs

**Access audit log:**
1. Navigate to **Admin → Audit Log**
2. View all system actions:
   - Role changes
   - User creation/modification
   - Configuration changes
   - Shift assignments
   - Request approvals/rejections

**Filter logs:**
- By date range
- By user
- By action type
- By company

**Audit log entries include:**
- Timestamp (when action occurred)
- User (who performed action)
- Action type (what was done)
- Details (specific changes)
- Old/new values (for updates)

**Use cases:**
- Security investigation
- Compliance auditing
- Troubleshooting user issues
- Understanding system history

---

## Owner Guide

### Multi-Company Management

**As Owner, you have:**
- Access to all companies
- Ability to create/edit/delete companies
- Cross-company user assignment
- Global analytics across all companies

**Switch between companies:**
1. Use company selector (if "View As" mode enabled)
2. See data for specific company
3. Or view aggregated data across all companies

**Best practices:**
- Keep company data logically separated
- Assign Directors to manage multiple companies
- Use consistent shift types across companies
- Regularly review cross-company analytics

### Feature Flags Management

**Access feature flags:**
1. Navigate to **Owner → Feature Flags**
2. Toggle features on/off system-wide

**Available flags:**
- **Enable Game**: Shift Swap Game feature
- **Enable Multi-Company Director Role**: Cross-company access
- **Enforce Company Scope**: Strict isolation (experimental)
- **Enable Advanced Analytics**: Additional metrics
- **Enable Email Notifications**: System-wide email toggle

**Impact of changes:**
- Flags apply immediately after save
- Audit log entry created
- Users may need to refresh browser

**Caution:** Disabling features may hide UI elements users rely on.

### Database Console

**Access database console:**
1. Navigate to **Owner → Database Console**
2. **Use with extreme caution** (direct database access)

**Capabilities:**
- Execute SQL queries directly
- View raw table data
- Export database contents
- Troubleshoot data issues

**Safety:**
- Read-only queries recommended
- Backup database before modifications
- Understand multi-tenant data isolation
- Test queries in development first

**Example queries:**
```sql
-- View all users
SELECT * FROM Users;

-- Count shifts per company
SELECT CompanyId, COUNT(*)
FROM ShiftInstances
GROUP BY CompanyId;

-- Find overlapping shifts for employee
SELECT * FROM ShiftAssignments
WHERE UserId = 'user-id-here'
AND EndTime > 'start-time' AND StartTime < 'end-time';
```

### Email Configuration

**Configure email system-wide:**
1. Navigate to **Owner → Email Config**
2. Enter SMTP or API details:
   - **Enable Email**: Toggle on/off
   - **API Key**: Your email service API key (encrypted at rest)
   - **API URL**: Email service endpoint
   - **From Address**: Sender email (e.g., noreply@yourcompany.com)
3. Click **Test Configuration** to send test email
4. Click **Save** to apply

**Email templates (automatic):**
- Shift assigned
- Shift changed
- Shift canceled
- Request approved/rejected
- Swap request notifications

**Fallback configuration:**
- Database config has priority
- Fallback to `appsettings.json` if not set
- Environment variables override appsettings

### System Health Monitoring

**Access system health:**
1. Navigate to **Owner → System Health**
2. View metrics:

**Application Metrics:**
- Uptime
- Total requests (last 24 hours)
- Active users
- Database size

**Database Health:**
- Connection status
- Total records
- Tables health check
- Last backup timestamp

**Performance:**
- Average response time
- Memory usage
- CPU usage (if available)
- Slow queries (if tracked)

**Alerts:**
- Red alerts indicate critical issues
- Yellow warnings suggest attention needed
- Green status means all healthy

### Backup & Restore

**Create manual backup:**
1. Navigate to **Owner → Backup**
2. Click **Create Backup**
3. Backup saved to `Backups/` folder
4. File named with timestamp: `app.db.backup.2025-01-15`

**Automated backups:**
- Configure via PowerShell script (see Deployment Guide)
- Schedule with Windows Task Scheduler
- Keep 30 days of backups recommended
- Store off-server for disaster recovery

**Restore from backup:**
1. Stop ShiftManager application
2. Rename current `app.db` to `app.db.old`
3. Copy backup file to `app.db`
4. Delete `app.db-shm` and `app.db-wal` if present
5. Start ShiftManager
6. Verify data integrity

**See [FinalProductPublish/DEPLOYMENT_GUIDE.txt](FinalProductPublish/DEPLOYMENT_GUIDE.txt) Section 10 for complete backup procedures.**

### Game Configuration

**Configure Shift Swap Game:**
1. Navigate to **Owner → Game Config**
2. Settings:

**Game Status:**
- Enable/Disable game system-wide

**Scoring Configuration:**
- Points for 3-match (default: 10)
- Points for 4-match (default: 25)
- Points for 5+ match (default: 50)

**Mega Combo Settings:**
- Mega Combo multiplier (e.g., 2x, 3x)
- Minimum lines required for Mega Combo
- Separate thresholds for 3/4/5-match combos

**Grid Settings:**
- Grid size (4x4 to 10x10)

**Milestones:**
- Comma-separated point thresholds
- Example: `1000,2500,5000,10000`
- Employees earn achievements at these levels

**Save changes:**
- Click **Save Configuration**
- Changes apply to all users immediately

---

## Common Workflows

### Workflow 1: Creating and Filling a Shift

**Scenario:** Manager needs to create a morning shift for next Monday and assign 3 employees.

**Steps:**

1. Navigate to **Calendar → Month View**
2. Click on next Monday's date
3. Select **Create Shift**
4. Fill in form:
   - Shift Type: MORNING
   - Date: [Next Monday]
   - Required Staff: 3
   - Start Time: 08:00 (from MORNING defaults)
   - End Time: 16:00
5. Click **Create Shift**
6. Shift appears in calendar with "0/3" staffing
7. Click on the shift → **Assign Employees**
8. Select 3 available employees from dropdown
9. System checks conflicts for each:
   - ✅ No conflicts detected for Employee A
   - ⚠️ Conflict for Employee B (only 6 hours rest from previous shift)
   - ✅ No conflicts for Employee C
10. Deselect Employee B, select Employee D instead
11. Click **Assign**
12. Shift now shows "3/3" staffing
13. Employees receive notifications

**Result:** Fully staffed shift with no conflicts.

### Workflow 2: Approving Time-Off Requests

**Scenario:** Manager has 5 pending time-off requests to review.

**Steps:**

1. Navigate to **Requests → Index**
2. See list of pending requests:
   - Employee A: Dec 20-22 (3 days)
   - Employee B: Dec 24-25 (2 days)
   - Employee C: Jan 2-5 (4 days)
   - Employee D: Dec 20 (1 day)
   - Employee E: Dec 31 (1 day)
3. Review Employee A request:
   - Check calendar for Dec 20-22
   - Coverage adequate for those dates
   - Click **Approve**
   - Employee A receives notification
   - OFFLINE shifts auto-created for Dec 20, 21, 22
4. Review Employee B request:
   - Dec 24-25 are company holidays
   - Already have OFFLINE shifts scheduled
   - Click **Approve** (redundant but harmless)
5. Review Employee C request:
   - Jan 2-5 conflicts with major project deadline
   - Click **Reject**
   - Add reason: "Coverage needed for project deadline"
   - Employee C receives rejection with reason
6. Use batch approval:
   - Select Employee D and E requests (checkboxes)
   - Click **Approve Selected**
   - Both approved at once
7. All requests processed

**Result:** 4 approved, 1 rejected with clear communication.

### Workflow 3: Handling a Shift Swap Request

**Scenario:** Employee A wants to swap their Friday evening shift with Employee B's Saturday morning shift.

**Steps:**

**Employee A (Requestor):**
1. Navigate to **Requests → Shift Swaps**
2. Click **Request Shift Swap**
3. Select "My Shift": Friday 6pm-2am (NIGHT shift)
4. Select "Employee": Employee B
5. Select "Their Shift": Saturday 8am-4pm (MORNING shift)
6. Add reason: "Family event on Friday evening"
7. Click **Submit Swap Request**
8. Request shows as "Pending - Awaiting Employee B Consent"

**Employee B (Target):**
1. Receives notification: "Swap request from Employee A"
2. Clicks notification to view details
3. Reviews both shifts:
   - Give away: Saturday morning (works for them)
   - Receive: Friday evening (they're available)
4. Clicks **Accept Swap**
5. Request status updates to "Pending Manager Approval"

**Manager:**
1. Receives notification: "Swap request ready for approval"
2. Navigate to **Requests → Swaps**
3. Reviews swap request:
   - Both employees consented ✅
   - Employee A: Friday 6pm-2am → Saturday 8am-4pm (14 hours rest) ✅
   - Employee B: Saturday 8am-4pm → Friday 6pm-2am (only 4 hours rest) ❌
   - **CONFLICT DETECTED**: Insufficient rest for Employee B!
4. Clicks **Reject**
5. Adds reason: "Employee B would have only 4 hours rest. Insufficient rest period (8 hours required)."
6. Both employees receive rejection notification with explanation

**Alternative Resolution:**
- Employee A could find different employee to swap with
- Or request just to give away Friday shift without receiving Saturday shift
- Or swap with someone whose shift allows adequate rest

**Result:** Unsafe swap prevented, employees understand why.

### Workflow 4: Setting Up a New Company

**Scenario:** Owner needs to onboard a new company "TechCo" with 15 employees.

**Steps:**

1. **Create Company** (Owner):
   - Navigate to **Admin → Companies**
   - Click **Create Company**
   - Name: "TechCo"
   - Description: "Technical support division"
   - Click **Create**

2. **Create Admin User** for TechCo:
   - Navigate to **Admin → Users → Create User**
   - Name: "Sarah Johnson"
   - Email: "sarah@techco.com"
   - Password: "TempPassword123!" (she'll change on first login)
   - Role: **Admin**
   - Company: **TechCo**
   - Click **Create User**
   - Email Sarah her credentials

3. **Sarah's First Login** (new TechCo Admin):
   - Logs in with sarah@techco.com / TempPassword123!
   - System prompts password change
   - Sets new secure password

4. **Sarah Creates Shift Types**:
   - Navigate to **Admin → Shift Types**
   - Creates custom shift types for tech support:
     - EARLY: 6am-2pm
     - DAY: 10am-6pm
     - LATE: 2pm-10pm
     - NIGHT: 10pm-6am (next day)
     - OFFLINE: for time off
   - Sets appropriate colors for each

5. **Sarah Creates Managers**:
   - Navigate to **Admin → Users → Create User**
   - Creates 2 managers (team leads)
   - Role: Manager
   - Company: TechCo

6. **Managers Create Employees**:
   (Or Sarah creates all 15 employees herself)
   - Navigate to **Admin → Users → Bulk Create** (if available)
   - Or create individually:
     - Name, Email, Password, Role: Employee, Company: TechCo
   - Repeat for all 15 employees
   - Email all employees their credentials

7. **Configure Company Settings**:
   - Navigate to **Admin → Config**
   - Rest Hours: 8 hours (default)
   - Weekly Hours Cap: 40 hours
   - Enable Email: true
   - Email configured by Owner

8. **Create First Week's Schedule**:
   - Managers navigate to Calendar
   - Create shifts for next week
   - Assign employees to shifts
   - Verify coverage for 24/7 tech support

**Result:** TechCo fully operational with 1 admin, 2 managers, 15 employees, custom shift types, and first week scheduled.

---

## Configuration & Customization

### Changing System Settings

**Global Configuration** (Admin+ required):

**Access:** Admin → Config

**Rest Hours Enforcement:**
- Minimum hours between shift end and next shift start
- Default: 8 hours
- Range: 0-24 hours
- Used for conflict detection
- Prevents employee burnout

**Weekly Hours Cap:**
- Maximum hours per employee per 7-day period
- Default: 40 hours
- Range: 1-168 hours (168 = 24×7)
- System warns when employee approaches cap
- Prevents overtime violations

**Company Scope Enforcement:**
- Experimental feature
- When enabled, strict company data isolation
- Admins can only see their own company
- Default: false (Admins and Owners see all companies)

**Changes saved to database and apply immediately.**

### Customizing Shift Types

**Create Custom Shift Type** (Admin+ required):

1. Navigate to **Admin → Shift Types**
2. Click **Create Shift Type**
3. Fill in:
   - **Code**: Uppercase identifier (e.g., "SWING")
   - **Name (English)**: Display name (e.g., "Swing Shift")
   - **Name (Hebrew)**: עברית translation (e.g., "משמרת ערב")
   - **Description**: Optional notes
   - **Default Start**: e.g., 15:00
   - **Default End**: e.g., 23:00
   - **Color**: Hex color code (e.g., #3498db)
4. Click **Create**

**Edit Existing Shift Type:**
- Click **Edit** next to shift type
- Modify fields
- **Note:** Changing times only affects new shifts, not existing ones
- Click **Update**

**Delete Shift Type:**
- Only possible if no shifts use this type
- System prevents deletion of in-use types

**Color Guidelines:**
- Use distinct colors for easy calendar viewing
- Consider colorblind-friendly palettes
- Test in both light and dark themes

### Email Notification Setup

**Method 1: Via Admin UI** (Recommended, Admin+ required):

1. Navigate to **Admin → Config**
2. Scroll to "Email Configuration"
3. Fill in:
   - **Enable Email Notifications**: Toggle on
   - **API Key**: Your email service API key
     - Encrypted at rest with ASP.NET Data Protection
     - Never displayed in UI after saving
   - **API URL**: Email service endpoint (e.g., https://api.yourcompany.com/v1/mail/send)
   - **From Address**: Sender email (e.g., noreply@yourcompany.com)
4. Click **Test Configuration** to send test email
5. Check inbox (and spam folder)
6. If test successful, click **Save Email Configuration**
7. Audit log entry created

**Method 2: Environment Variables** (Deployment):

```cmd
set EMAIL__ENABLED=true
set EMAIL__APIKEY=your-api-key-here
set EMAIL__APIURL=https://api.yourcompany.com/v1/mail/send
set EMAIL__FROMADDRESS=noreply@yourcompany.com
```

**Method 3: appsettings.json** (Fallback):

Edit `appsettings.json`:
```json
{
  "Email": {
    "Enabled": true,
    "ApiKey": "your-api-key-here",
    "ApiUrl": "https://api.yourcompany.com/v1/mail/send",
    "FromAddress": "noreply@yourcompany.com"
  }
}
```

**Configuration Priority:**
1. Database (highest) - set via Admin UI
2. Environment Variables
3. appsettings.json (lowest)

**Email Notification Types:**
- Shift assigned
- Shift changed
- Shift deleted
- Time-off request approved/rejected
- Swap request notifications
- Day shift (on-duty) assignments

### Changing Language

**User-Level Language Selection:**

1. Click language toggle in top navigation
   - Shows current language flag/text
   - English: "EN" or 🇬🇧
   - Hebrew: "עב" or 🇮🇱
2. Click to toggle between English and Hebrew
3. Page refreshes with new language
4. Selection saved to browser (persists across sessions)

**Supported Languages:**
- **English (en-US)**: Default, left-to-right (LTR) layout
- **Hebrew (he-IL)**: Full right-to-left (RTL) support

**RTL Features:**
- Reversed navigation menus
- Right-aligned text
- Mirrored layout elements
- Arabic numerals remain left-to-right (standard)

**System-Wide Language:**
- No system-wide language setting
- Each user selects their own language
- UI elements localized per user
- Dates/times formatted per user culture

### Customizing UI Appearance

**Theme Selection:**

1. Click theme toggle (🌓) in top navigation
2. Toggles between:
   - **Light Mode**: Light backgrounds, dark text
   - **Dark Mode**: Dark backgrounds, light text
3. Selection saved to browser localStorage
4. Persists across sessions

**Advanced Customization** (Requires developer):

See [CUSTOMIZATION_GUIDE.md](CUSTOMIZATION_GUIDE.md) for:
- Changing color scheme
- Modifying typography
- Customizing component styles
- Adding new themes
- Responsive breakpoints
- CSS variable reference

**Quick Color Changes:**

Edit `wwwroot/css/site.css`:
```css
:root {
  --primary: #2563eb;  /* Main brand color */
  --success: #16a34a;  /* Success messages */
  --danger: #dc2626;   /* Errors, destructive actions */
  --warning: #f59e0b;  /* Warnings */
}
```

**No-Code Customization:**
- Logo: Replace `wwwroot/images/logo.png`
- Favicon: Replace `wwwroot/favicon.ico`
- Default language: Change in `appsettings.json`

---

## Advanced Features

### Command Palette

**Access:** Press **Ctrl+K** (Windows/Linux) or **Cmd+K** (Mac)

**What it does:**
- Quick navigation to any page
- Search for pages by name
- Keyboard-driven interface
- Faster than clicking through menus

**How to use:**
1. Press **Ctrl+K** / **Cmd+K**
2. Palette modal appears
3. Start typing page name (e.g., "calendar", "requests", "users")
4. Results filtered in real-time
5. Use ↑↓ arrow keys to navigate results
6. Press **Enter** to navigate to selected page
7. Press **Esc** to close palette

**Example:**
- Type "cal" → Shows "Calendar Month", "Calendar Week", "Calendar Day"
- Type "req" → Shows "My Requests", "Manage Requests", "Shift Swaps"
- Type "user" → Shows "Manage Users", "My Profile"

**Role Filtering:**
- Palette only shows pages you have permission to access
- Admins see more options than Employees

**Customization:**
- See [CUSTOMIZATION_GUIDE.md](CUSTOMIZATION_GUIDE.md) to add custom pages to palette

### Analytics & Reporting (Admin+ Required)

**Access:** Admin → Analytics

**Available Reports:**

**1. Shift Distribution:**
- Shows number of shifts per shift type
- Pie chart or bar graph
- Last 30 days by default
- Filter by date range, company, or employee

**2. Employee Hours:**
- Total hours per employee
- Overtime hours highlighted
- Weekly average
- Export to CSV for payroll

**3. Request Statistics:**
- Time-off requests: Pending, Approved, Rejected
- Shift swap requests: Success rate
- Average approval time
- Most common rejection reasons

**4. Coverage Analysis:**
- Shifts with unfilled slots
- Overstaffed shifts
- Coverage percentage by shift type
- Staffing trends over time

**5. Conflict Report:**
- All scheduling conflicts
- Employees violating rest hours
- Employees over weekly cap
- Double-booked employees

**Export Options:**
- **CSV**: For Excel/Google Sheets
- **PDF**: Printable report
- **JSON**: For API integrations

**Filtering:**
- Date range (last week, month, quarter, year, custom)
- Company (for multi-company setups)
- Shift type
- Employee or team

**Refresh Data:**
- Click **Refresh** to update with latest data
- Auto-refresh every 5 minutes (configurable)

### API Integration

**ShiftManager provides a REST API for external integrations.**

**API Endpoints:** 27 endpoints covering:
- User management
- Shift operations
- Request handling
- Calendar data
- Analytics

**Authentication:**
- API Key authentication
- Configure in Owner → Email Config (shared with email API key)
- Include in request header: `Authorization: Bearer YOUR_API_KEY`

**Example Request:**

```http
GET /api/shifts?date=2025-01-15
Authorization: Bearer YOUR_API_KEY
Accept: application/json
```

**Example Response:**

```json
{
  "shifts": [
    {
      "id": "shift-123",
      "type": "MORNING",
      "date": "2025-01-15",
      "startTime": "08:00",
      "endTime": "16:00",
      "requiredStaff": 3,
      "assignedEmployees": ["user-1", "user-2", "user-3"]
    }
  ]
}
```

**Complete API Documentation:**
See [API_DOCUMENTATION.md](API_DOCUMENTATION.md) for:
- Complete endpoint list
- Request/response examples
- Error codes
- Rate limiting
- Sample client code (JavaScript, Python)

**API Client Libraries:**
- JavaScript client: `clients/javascript/`
- Python client: `clients/python/`

**Use Cases:**
- External dashboards
- Mobile app integration
- Third-party scheduling tools
- Payroll system integration
- HR information systems

### View As Mode (Director+ Only)

**What it is:**
Directors and Owners can switch between company contexts to manage multiple companies.

**Access:**
- Company selector dropdown (top navigation)
- Shows all companies you have access to

**How to use:**
1. Click company selector
2. Select company to view
3. All data now scoped to that company
4. Create shifts, manage users, etc. for that company
5. Switch to different company as needed

**Permissions:**
- **Directors**: Only companies assigned to them
- **Owners**: All companies in the system

**Limitations:**
- Can only be in one company context at a time
- Some global views (analytics) can aggregate across companies

---

## Mobile & Responsive Usage

### Accessing on Mobile Devices

**Supported Devices:**
- iOS (Safari, Chrome)
- Android (Chrome, Firefox, Samsung Internet)
- Tablets (iPad, Android tablets)

**Access:**
1. Open browser on mobile device
2. Navigate to ShiftManager URL (e.g., http://192.168.1.100:5000)
3. Login with your credentials
4. UI automatically adapts to screen size

### Mobile-Optimized Features

**Calendar Views:**
- Month view: Swipe left/right to change months
- Week view: Horizontal scroll for days
- Day view: Full details for single day
- Touch-friendly date picker

**Shift Assignment:**
- Tap shift to view details
- Swipe gestures for quick actions
- Optimized form inputs for mobile keyboards

**Notifications:**
- Touch notification icon to open
- Swipe notification to dismiss
- Pull-to-refresh for new notifications

**Command Palette:**
- On mobile: Menu button (☰) or search icon
- Touch-friendly selection
- Auto-focus on search input

**Navigation:**
- Hamburger menu (☰) collapses full navigation
- Bottom navigation bar (optional theme)
- Breadcrumbs for navigation context

### Mobile Best Practices

**For Users:**
- Bookmark ShiftManager URL for quick access
- Add to home screen (iOS: Share → Add to Home Screen)
- Enable browser notifications (if supported)
- Use landscape mode for calendar views
- Zoom in/out with pinch gesture

**For Administrators:**
- Test mobile access before deploying to staff
- Ensure network firewall allows mobile devices
- Consider mobile data usage (app is lightweight)
- Train employees on mobile features

**Limitations on Mobile:**
- Some complex admin tasks easier on desktop
- File uploads (avatars) may be slower
- Analytics graphs may be cramped on small screens

**Performance:**
- Mobile optimized (minimal JavaScript)
- Fast load times even on 3G
- Responsive images scale appropriately
- Battery-efficient (no constant polling)

---

## Troubleshooting & FAQ

### Common Issues

**Q: I can't login with my credentials**

A: Try these steps:
1. Verify email is correct (case-sensitive)
2. Verify password (case-sensitive)
3. Check Caps Lock is off
4. Try resetting password (if "Forgot Password" available)
5. Clear browser cookies and cache
6. Try incognito/private browsing mode
7. Contact your administrator to verify account status
8. Ensure system clock is correct (affects cookie validation)

**Q: I don't see my shifts in the calendar**

A: Possible causes:
1. No shifts assigned to you yet
2. Viewing wrong date range (check month/year)
3. Wrong company selected (if multi-company)
4. Your account is inactive (contact admin)
5. Shifts were deleted or reassigned
6. Calendar view not loading correctly (try refreshing page: F5)

**Q: I can't request time off**

A: Check:
1. Your role allows time off requests (Trainees cannot request time off)
2. Date range is in the future
3. You don't already have a pending request for same dates
4. System allows overlapping time off (admin setting)
5. Your account is active and approved

**Q: My swap request was rejected**

A: Common reasons:
1. Insufficient rest hours between swapped shifts
2. One or both employees would exceed weekly hour cap
3. Creates staffing conflicts
4. Manager discretion (check rejection reason provided)
5. Other employee declined the swap
6. Request timed out (check company policy on request expiration)

**Q: Notifications not showing**

A: Try:
1. Refresh the page (F5)
2. Check notification icon (bell) for unread count
3. Verify notifications enabled in your profile
4. Clear browser cache
5. Check browser notification permissions
6. If email notifications: Check spam folder

**Q: Language doesn't change**

A: Steps to fix:
1. Click language toggle again (may need to click twice)
2. Hard refresh page (Ctrl+F5 or Cmd+Shift+R)
3. Clear browser cache
4. Try different browser
5. Verify Hebrew localization files are installed (ask admin)

**Q: Theme (dark/light mode) not working**

A: Try:
1. Click theme toggle again
2. Check browser localStorage (developer tools → Application → Local Storage)
3. Clear localStorage: `localStorage.clear()` in console
4. Refresh page
5. Try different browser

**Q: I see "Access Denied" error**

A: You don't have permission for that page:
1. Verify your role with administrator
2. You may have been demoted from higher role
3. Feature may be disabled system-wide (Owner's feature flags)
4. Company scope may have changed
5. Contact administrator to request access

**Q: Calendar not loading or shows errors**

A: Troubleshoot:
1. Check browser console for JavaScript errors (F12 → Console)
2. Refresh page (F5)
3. Clear browser cache
4. Try different browser
5. Check server is running (ask admin)
6. Verify database connection (ask admin)
7. Report exact error message to administrator

**Q: Can't upload avatar/profile picture**

A: Possible issues:
1. File too large (max size set by admin, typically 2-5 MB)
2. File format not supported (use JPG, PNG, GIF)
3. Image processing library (SixLabors.ImageSharp) not loaded correctly
   - Admin needs to run `UNBLOCK_FILES.bat` on deployment folder
4. No write permissions on server (admin needs to fix folder permissions)

**Q: Emails not being received**

A: Check with admin:
1. Email notifications enabled in system (Admin → Config)
2. SMTP/API configured correctly
3. API key valid
4. Check spam/junk folder
5. Email address in your profile is correct
6. Test email configuration (Owner → Email Config → Test)
7. Check server logs for email send errors

### FAQ - Advanced Topics

**Q: What's the difference between Scheduled Shifts and Day Shifts?**

A: See [Glossary](#glossary) section below for detailed explanation.

**Short answer:**
- **Scheduled Shifts**: Specific start/end times (e.g., 8am-4pm)
- **Day Shifts**: Full day assignment without specific times (e.g., Hakam on-duty for entire day)

**Q: Can I have multiple companies?**

A: Yes, ShiftManager supports multi-tenancy:
- Owner creates companies
- Users assigned to one company (or multiple if Director)
- Data completely isolated between companies
- Directors can manage multiple companies

**Q: How do I export my schedule?**

A: Currently:
1. Navigate to Calendar view
2. Use browser Print (Ctrl+P)
3. Save as PDF
4. Or use API to fetch calendar data programmatically

**Future feature:** Direct export to Excel/Google Calendar (see roadmap).

**Q: Can I integrate with our payroll system?**

A: Yes, via API:
1. Use ShiftManager REST API
2. See [API_DOCUMENTATION.md](API_DOCUMENTATION.md)
3. Sample client code in `clients/` folder
4. Export employee hours via **Admin → Analytics → Export**

**Q: Is there a mobile app?**

A: Not currently. However:
- Web interface is fully mobile-responsive
- Add to home screen for app-like experience
- Works offline with browser caching
- Future native app on roadmap

**Q: How do I reset my password?**

A: If "Forgot Password" feature is enabled:
1. Click "Forgot Password" on login page
2. Enter your email
3. Follow reset link sent to email

If not enabled:
- Contact your administrator to reset your password

**Q: Can I see who made changes to shifts?**

A: Yes (Admin+ only):
1. Navigate to **Admin → Audit Log**
2. Filter by shift ID or date range
3. See full history: who created, modified, or deleted shifts
4. Timestamps and old/new values recorded

**Q: How far in advance can I schedule shifts?**

A: No system limit. Best practices:
- Schedule 2-4 weeks in advance
- Adjust based on company policy
- Consider employee preferences for advance notice

**Q: What happens if I exceed the weekly hour cap?**

A: System behavior:
- Warning shown when assigning would exceed cap
- Admin can override (if company policy allows)
- Employee cannot self-assign beyond cap
- Analytics dashboard highlights overtime

---

## Glossary

### Key Terms

**Scheduled Shifts** (formerly "Shifts"):
- Work assignments with specific start and end times
- Example: MORNING shift from 8:00 AM to 4:00 PM
- Managed via Calendar → Month/Week/Day views
- Requires employee assignment
- Subject to conflict detection and rest hour rules

**Day Shifts** (formerly "On-Duty"):
- Full-day work assignments without specific start/end times
- Example: Hakam on-duty for entire day (no specific hours)
- Types: Hakam (🛡️), Lead (⭐), Backup Hakam
- Managed via Public → On Duty or Calendar → Day Shifts
- Not subject to same conflict rules as scheduled shifts (but still logged)

**OFFLINE Shift Type:**
- Special shift type for non-working days
- Auto-created when time-off requests approved
- Blocks employee from being assigned to other shifts
- Not counted toward weekly hour cap
- Used for: PTO, sick days, holidays, personal days

**Multi-Tenancy:**
- Multiple companies using same ShiftManager instance
- Complete data isolation between companies
- Users assigned to one company (except Directors/Owners)
- Each company has own shifts, employees, configurations

**Company:**
- Organizational unit in multi-tenant setup
- Example: "IT Department", "Support Team", "Sales Division"
- Contains users, shifts, and requests
- Admin can configure per-company settings

**Role:**
- Permission level assigned to users
- Six roles: Owner, Director, Admin, Manager, Employee, Trainee
- Determines what features user can access
- Higher roles inherit permissions of lower roles

**Time-Off Request:**
- Employee request for scheduled days off
- Requires manager approval
- Creates OFFLINE shifts when approved
- Subject to conflict detection

**Shift Swap Request:**
- Request to exchange shift assignments between two employees
- Requires consent from both employees
- Requires manager approval
- Subject to conflict and rest hour checks

**Conflict:**
- Scheduling violation detected by system
- Types:
  - Double-booking (employee assigned to overlapping shifts)
  - Insufficient rest (< 8 hours between shifts)
  - Weekly cap exceeded (> 40 hours per week)
- System prevents creating conflicts

**Rest Hours:**
- Minimum time required between shift end and next shift start
- Default: 8 hours
- Configurable by admin (per company or global)
- Enforced by conflict detection
- Prevents employee burnout

**Weekly Hour Cap:**
- Maximum hours an employee can work in a 7-day period
- Default: 40 hours
- Configurable by admin
- System warns when approaching cap
- Prevents overtime violations

**Audit Log:**
- Record of all system changes
- Tracks who did what when
- Includes: user changes, role assignments, shift modifications, config changes
- Immutable (cannot be edited or deleted)
- Admin+ can view

**Command Palette:**
- Quick navigation feature
- Keyboard shortcut: Ctrl+K / Cmd+K
- Search pages by name
- Faster than menu navigation

**API:**
- REST API for external integrations
- 27 endpoints covering all functionality
- API key authentication
- JSON request/response format
- See [API_DOCUMENTATION.md](API_DOCUMENTATION.md)

**Air-Gapped Deployment:**
- Installation on computer with no internet connection
- Complete offline operation
- Deployment via USB transfer
- Common in high-security environments
- See [AIR_GAPPED_DEPLOYMENT_GUIDE.txt](AIR_GAPPED_DEPLOYMENT_GUIDE.txt)

### Terminology Evolution

**Historical Context:**
Originally, the system used hierarchical terminology:
- "Shifts" (official work) vs. "On-Duty" (implied official roles only)
- Created exclusionary perception

**Current Inclusive Terminology (v1.2+):**
- **English**: "Scheduled Shifts" and "Day Shifts"
- **Hebrew**: "משמרות הפקה וב״ר" (Production & BR shifts) and "משמרות רוחב" (Width shifts)

**Why it matters:**
- All workers perform "shifts" regardless of role
- Distinction is scheduling method, not job hierarchy
- People can hold multiple roles and do both types
- Avoids making anyone feel excluded

**Backend Code Names (do not change):**
- `ShiftType`, `ShiftInstance`, `ShiftAssignment` → Scheduled shifts
- `OnDuty`, `OnDutyType` → Day shifts
- Names preserved for stability and backward compatibility

**See [TERMINOLOGY.md](TERMINOLOGY.md) for complete mapping**

---

**End of Usage Guide**

For additional help:
- [README.md](README.md) - Project overview and quick start
- [project.md](project.md) - Complete technical documentation
- [API_DOCUMENTATION.md](API_DOCUMENTATION.md) - API reference
- [CUSTOMIZATION_GUIDE.md](CUSTOMIZATION_GUIDE.md) - UI customization
- [AIR_GAPPED_DEPLOYMENT_GUIDE.txt](AIR_GAPPED_DEPLOYMENT_GUIDE.txt) - Offline deployment
- [FinalProductPublish/DEPLOYMENT_GUIDE.txt](FinalProductPublish/DEPLOYMENT_GUIDE.txt) - Production deployment

**Version:** 2.0.0
**Last Updated:** 2025-12-04
