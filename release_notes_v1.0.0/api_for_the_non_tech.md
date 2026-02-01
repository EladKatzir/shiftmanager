# ShiftManager API - For Non-Technical Users

## What is the ShiftManager API?

The ShiftManager API allows other software systems to connect to your ShiftManager account and perform actions programmatically. Think of it as a way for computers to talk to each other - your other business tools can automatically read and update shift information without anyone having to manually type it in.

---

## What Can You Do With the API?

### 📅 **Shift Management**
- **View scheduled shifts** - See who's working when, filter by date, employee, or shift type
- **Check shift details** - Get complete information about any shift including times, staff requirements, and assignments

### 👥 **Employee Management**
- **List all employees** - Get a complete directory of your workforce with their roles and details
- **View employee profiles** - See detailed information about any team member
- **Add new employees** - Onboard new team members into the system automatically
- **Update employee information** - Keep profiles current with the latest contact details, roles, and departments

### 🏖️ **Time Off & Vacation Management**
- **See time off requests** - View pending, approved, or declined vacation requests
- **Submit time off requests** - Employees can request time off through integrated systems
- **Approve or decline requests** - Managers can process approvals automatically based on your business rules

### 🔔 **Notifications**
- **Read notifications** - See all system notifications for shift changes, approvals, etc.
- **Check unread alerts** - Know what requires attention
- **Mark notifications as read** - Keep your notification center organized
- **Clear all notifications** - Bulk-manage notification status

### 📊 **Analytics & Reports**
- **Get summary statistics** - See how many users, shifts, and pending requests you have
- **View activity metrics** - Track engagement and system usage over time
- **Monitor operations** - Get real-time insights into your workforce management

### 📝 **Audit Trail**
- **View activity logs** - See who did what and when in your system
- **Track changes** - Maintain compliance by reviewing all modifications
- **Search history** - Find specific actions by user, date, or activity type

### 📆 **Team Calendars** (NEW Feature!)
- **Create team calendars** - Set up custom calendar views for different teams or projects
- **Add team members** - Assign employees to specific calendar groups
- **View team schedules** - See weekly overviews of who's working, on vacation, or on duty
- **Manage calendar permissions** - Control who can view and edit each calendar

---

## Real-World Use Cases

### 🔗 **Integration Scenarios**

1. **HR System Integration**
   - Your HR software automatically creates employee accounts when someone is hired
   - Employee terminations instantly revoke access
   - Department changes sync automatically

2. **Payroll Integration**
   - Export shift data directly to your payroll system
   - Ensure hours worked match payroll records
   - Reduce manual data entry errors

3. **Time Clock Integration**
   - Physical time clocks can check scheduled shifts in real-time
   - Employees get alerted if clocking in when not scheduled
   - Attendance data flows back automatically

4. **Mobile App**
   - Build your own branded mobile app
   - Employees check their schedules on-the-go
   - Push notifications for shift changes

5. **Dashboard & Reporting**
   - Create custom business intelligence dashboards
   - Pull data into Excel, Tableau, or Power BI
   - Generate automated weekly/monthly reports

6. **Multi-Location Coordination**
   - Central office can view all location schedules
   - Transfer shift data between locations
   - Consolidated reporting across all sites

---

## How Does API Access Work?

### 🔐 **Security (Keeping Your Data Safe)**

**API Keys**: Instead of using passwords, you create special "API Keys" that give specific permissions to external systems. Each key:
- Only works for your company's data
- Can be limited to specific actions (read-only vs full access)
- Can be revoked instantly if compromised
- Tracks all usage for security auditing

**Permission Levels**:
- **Read** - View information only
- **Write** - Create and update records
- **Approve** - Special permission for approving requests

### 🚦 **Rate Limits (Fair Usage)**

To ensure the system stays fast for everyone:
- Each API key gets **100 requests per minute**
- Perfect for normal business operations
- Higher limits available for enterprise customers
- You'll get a warning before hitting the limit

### 📊 **Usage Tracking**

Every API request is logged with:
- Who made the request (which API key)
- What they requested
- When it happened
- Whether it succeeded or failed

This helps with:
- Troubleshooting integration issues
- Security auditing
- Understanding which systems use which data

---

## What's Enabled for Release v1.0.0?

✅ **All API features are ENABLED and ready to use:**

| Feature | Status | What It Means |
|---------|--------|---------------|
| User Management | ✅ Active | Create, read, update employee records |
| Shift Viewing | ✅ Active | See all scheduled shifts |
| Time Off Management | ✅ Active | Full request/approval workflow |
| Notifications | ✅ Active | Read and manage alerts |
| Analytics | ✅ Active | Access summary metrics |
| Audit Logs | ✅ Active | View activity history |
| Team Calendars | ✅ Active | Manage custom calendar views |

---

## Getting Started

### Step 1: Request API Access
Contact your system administrator or account owner to request an API key.

### Step 2: Choose Permissions
Decide what your integration needs to do:
- Just view data? Request **read** permissions
- Need to create shifts or employees? Request **write** permissions
- Handling approvals? Request **approve** permissions

### Step 3: Get Your API Key
You'll receive a long string of characters (your API key). **Keep this secret!** Anyone with this key can access your data.

### Step 4: Give to Your Developer
Provide the API key to your developer or IT team who will set up the integration.

---

## Important Notes

### 🔒 **Security Best Practices**
- Never share API keys publicly
- Don't put keys in emails or documents
- Rotate keys periodically (every 90 days recommended)
- Use different keys for different integrations
- Revoke keys immediately if compromised

### 📧 **Email Notifications**
Note: Email notifications are currently disabled. If you need shift assignment or approval emails sent automatically, contact your administrator to configure email settings.

### 🆘 **Support**
If you encounter issues:
1. Check the audit logs to see what happened
2. Verify the API key has proper permissions
3. Ensure you haven't exceeded rate limits
4. Contact your system administrator or support team

---

## Privacy & Compliance

- All API access is **company-scoped** - you only see your own company's data
- Full audit trail for compliance requirements (GDPR, SOC 2, etc.)
- API keys can be restricted by IP address if needed
- Data transmitted over secure HTTPS connections

---

## Future Enhancements (Post v1.0.0)

These features are under consideration for future releases:
- Webhooks (get notified when data changes)
- Bulk operations (update many records at once)
- Advanced filtering and sorting
- Custom field support
- Export to PDF/Excel via API
- Shift templates API
- Conflict detection API

---

## Questions?

**For Business Users**: Contact your account manager or system administrator
**For Developers**: See the complete API documentation in `api-inventory.md`

---

*Document Generated: 2025-01-12 (Release Readiness Phase 4)*
