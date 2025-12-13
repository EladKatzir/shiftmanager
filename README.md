# ShiftManager

**Multi-tenant shift scheduling and workforce management system for air-gapped environments**

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

ShiftManager is an enterprise-grade shift scheduling system designed specifically for secure, offline Windows environments. Built with ASP.NET Core 8.0, it provides comprehensive workforce management with complete multi-tenant isolation, role-based access control, and multi-language support.

---

## Table of Contents

- [Key Features](#key-features)
- [Quick Start](#quick-start)
- [Documentation](#documentation)
- [Prerequisites](#prerequisites)
- [Installation](#installation)
- [First Steps](#first-steps)
- [Tech Stack](#tech-stack)
- [Configuration](#configuration)
- [Common Issues](#common-issues)
- [Support](#support)
- [Security](#security)
- [Contributing](#contributing)
- [License](#license)

---

## Key Features

### Shift Management
- **Scheduled Shifts** - Time-bounded work assignments with specific start/end times
- **Day Shifts** - Full-day assignments (On-Duty: Hakam, Lead, and more)
- **Calendar Views** - Month, week, and day views with intuitive navigation
- **Conflict Detection** - Automatic detection and prevention of scheduling conflicts
- **Rest Hours Enforcement** - Configurable minimum rest periods between shifts
- **Weekly Hour Caps** - Automatic tracking and enforcement of maximum weekly hours
- **OFFLINE Shift Type** - Special shift type for non-working days and scheduled time off

### User Management
- **Multi-Tenant Architecture** - Complete company data isolation with automatic scoping
- **Role-Based Access Control** - Six roles with granular permissions (Owner, Director, Admin, Manager, Employee, Trainee)
- **Self-Service Signup** - Employee join requests with admin approval workflow
- **Cross-Company Directors** - Director role with access to multiple companies
- **Trainee Shadowing** - Assign trainees to shadow experienced employees

### Requests & Workflows
- **Time-Off Requests** - PTO request workflow with conflict detection
- **Shift Swap Requests** - Employee-initiated shift swaps with manager approval
- **Batch Approvals** - Approve multiple requests simultaneously
- **Request History** - Complete audit trail of all requests and approvals

### Localization & Accessibility
- **Multi-Language Support** - English (en-US) and Hebrew (he-IL) with RTL support
- **Inclusive Terminology** - Updated shift terminology to avoid hierarchical language
- **Culture-Aware Formatting** - Dates, times, and numbers formatted per user locale
- **Theme Support** - Light and dark modes with automatic theme persistence

### Notifications & Communication
- **In-App Notifications** - Real-time notifications for shift changes and approvals
- **Email Integration** - Optional encrypted email notifications for assignments
- **Notification Center** - Centralized notification management with read/unread tracking

### Security & Compliance
- **Air-Gapped Deployment** - Complete offline operation with USB deployment
- **Encrypted Configuration** - API keys encrypted at rest using ASP.NET Data Protection
- **Audit Logging** - Comprehensive audit trail for role assignments and configuration changes
- **PBKDF2 Password Hashing** - Industry-standard password protection (100k iterations, SHA256)
- **Multi-Tenant Isolation** - Query filters ensure complete data separation

### Advanced Features
- **Shift Swap Game** - Gamified shift swapping with scoring and milestones
- **Command Palette** - Quick navigation with keyboard shortcuts (Ctrl+K / Cmd+K)
- **Analytics Dashboard** - Workforce analytics and reporting for admins
- **API Layer** - Complete REST API with 27 endpoints for integrations
- **Mobile-Responsive** - Fully responsive design for phones, tablets, and desktops

---

## Quick Start

### For End Users

**Accessing the application:**
1. Navigate to the URL provided by your administrator
2. Login with your credentials
3. View your schedule in the Calendar section
4. Submit time-off requests via Requests → Time Off
5. Check notifications regularly for shift updates

**See [USAGE_GUIDE.md](USAGE_GUIDE.md) for detailed instructions**

### For IT Administrators

**Quick deployment (3 steps):**

```bash
# Step 1: Configure admin password
# Edit appsettings.json, set SEED_ADMIN_PASSWORD to a strong password

# Step 2: Start application
START_HERE.bat

# Step 3: Access application
# Open browser: http://localhost:5000
# Login: admin@local / [your password]
```

**For production deployment, see:**
- [Air-Gapped Deployment Guide](AIR_GAPPED_DEPLOYMENT_GUIDE.txt) - Complete offline deployment instructions
- [Deployment Guide](FinalProductPublish/DEPLOYMENT_GUIDE.txt) - Windows Service setup, IIS hosting, network configuration
- [Pre-Demo Checklist](PRE_DEMO_CHECKLIST.txt) - Deployment verification procedures

### For Developers

```bash
# Clone and setup
git clone <repository-url>
cd ShiftManager
dotnet restore
dotnet run

# Navigate to: http://localhost:5000
# Default login: admin@local / admin123
```

**See [project.md](project.md) for complete technical documentation (2100+ lines)**

---

## Documentation

### By Audience

**For End Users:**
- [USAGE_GUIDE.md](USAGE_GUIDE.md) - Complete user guide with role-based instructions

**For IT Administrators & Deployment:**
- [AIR_GAPPED_DEPLOYMENT_GUIDE.txt](AIR_GAPPED_DEPLOYMENT_GUIDE.txt) - Offline Windows deployment guide
- [FinalProductPublish/DEPLOYMENT_GUIDE.txt](FinalProductPublish/DEPLOYMENT_GUIDE.txt) - Production deployment manual
- [FinalProductPublish/README.txt](FinalProductPublish/README.txt) - Deployment package overview
- [PRE_DEMO_CHECKLIST.txt](PRE_DEMO_CHECKLIST.txt) - Pre-deployment verification procedures
- [GRIFFIN_OWNER_GUIDE.md](GRIFFIN_OWNER_GUIDE.md) - Complete guide for Griffin ADFS authentication in air-gapped environments

**For Developers:**
- [project.md](project.md) - Complete technical documentation (architecture, APIs, database schema)
- [API_DOCUMENTATION.md](API_DOCUMENTATION.md) - REST API reference with 27 endpoints
- [CUSTOMIZATION_GUIDE.md](CUSTOMIZATION_GUIDE.md) - UI/UX customization instructions
- [TERMINOLOGY.md](TERMINOLOGY.md) - UI vs. code terminology mapping

**Reference:**
- [Build-Release.ps1](Build-Release.ps1) - Automated build pipeline documentation

---

## Prerequisites

### System Requirements

**Operating System:**
- Windows Server 2022 (recommended for production)
- Windows Server 2019
- Windows 11 64-bit
- Windows 10 64-bit

**Hardware (Minimum):**
- CPU: x64 processor, 1 GHz or faster
- RAM: 512 MB minimum (1 GB recommended)
- Disk: 150 MB free space

**Network:**
- None required for localhost-only deployment
- Port 5000 available (default, configurable)
- Static IP recommended for network access

**Software:**
- **Self-contained deployment**: No additional software required (includes .NET runtime)
- **Development**: .NET SDK 8.0 or later

### Development Prerequisites

For local development:
1. [.NET SDK 8.0](https://dotnet.microsoft.com/download/dotnet/8.0) or later
2. Git (for version control)
3. Code editor (Visual Studio, VS Code, or Rider)

---

## Installation

### Production Installation (Air-Gapped)

**For offline Windows Server deployment:**

1. **Prepare deployment package** (on internet-connected machine):
   ```powershell
   .\Build-Release.ps1 -Version "2.0.0"
   # Copy packages\ShiftManager-v2.0.0-win-x64.zip to USB
   ```

2. **Transfer to air-gapped server**:
   - Copy ZIP file from USB to local folder (e.g., `C:\ShiftManager\`)
   - Right-click ZIP file → Properties → Unblock → OK
   - Extract to installation folder

3. **Configure and start**:
   - Run `VERIFY_FILES.bat` to check all files present
   - Run `UNBLOCK_FILES.bat` to prevent DLL loading errors
   - Edit `appsettings.json`, set `SEED_ADMIN_PASSWORD`
   - Run `START_HERE.bat`

4. **Verify deployment**:
   - Open http://localhost:5000
   - Login: admin@local / [your password]

**Complete instructions**: See [AIR_GAPPED_DEPLOYMENT_GUIDE.txt](AIR_GAPPED_DEPLOYMENT_GUIDE.txt)

**Windows Service setup**: See [FinalProductPublish/DEPLOYMENT_GUIDE.txt](FinalProductPublish/DEPLOYMENT_GUIDE.txt) Section 7

### Development Installation

**Automated setup** (recommended):

**Windows (PowerShell):**
```powershell
git clone <repository-url>
cd ShiftManager
.\setup.ps1
dotnet run
```

**Linux/macOS (Bash):**
```bash
git clone <repository-url>
cd ShiftManager
chmod +x setup.sh
./setup.sh
dotnet run
```

**Manual setup:**

```bash
# 1. Clone repository
git clone <repository-url>
cd ShiftManager

# 2. Copy seed database
# Windows: Copy-Item seed.db app.db
# Linux/macOS: cp seed.db app.db

# 3. Restore dependencies
dotnet restore

# 4. Run application
dotnet run
```

Navigate to **http://localhost:5000** and login with:
- Email: `admin@local`
- Password: `admin123` (development default)

---

## First Steps

### After Installation

**1. Configure Admin Password** (Production):
   - Edit `appsettings.json`
   - Set `SEED_ADMIN_PASSWORD` to a strong password (12+ characters, mixed case, symbols)
   - Save and restart application

**2. Initial Login**:
   - Navigate to http://localhost:5000
   - Email: `admin@local`
   - Password: [from appsettings.json or `admin123` for development]

**3. Change Admin Password** (First login):
   - Click your name (top right) → Profile
   - Change Password
   - Set a unique, strong password

**4. Create Your Organization**:
   - Navigate to Admin → Companies
   - Edit "Demo Co" or create new company
   - Configure company-specific settings

**5. Add Users**:
   - Navigate to Admin → Users
   - Create managers, employees, and other roles
   - Assign users to appropriate companies

**6. Configure Shift Types**:
   - Default shift types: MORNING, NOON, NIGHT, MIDDLE, OFFLINE
   - Navigate to Admin → Shift Types to customize
   - Set start/end times, descriptions, colors

**7. Select Language**:
   - Click language toggle (top right)
   - Choose English or עברית (Hebrew)
   - Selection persists across sessions

### Network Access Setup

**To allow access from other computers:**

1. Edit `appsettings.json`:
   ```json
   "Kestrel": {
     "Endpoints": {
       "Http": {
         "Url": "http://*:5000"  // Change from localhost to *
       }
     }
   }
   ```

2. Configure Windows Firewall:
   ```cmd
   netsh advfirewall firewall add rule name="ShiftManager HTTP" dir=in action=allow protocol=TCP localport=5000
   ```

3. Restart application
4. Access from network: `http://[server-ip]:5000`

**See [FinalProductPublish/DEPLOYMENT_GUIDE.txt](FinalProductPublish/DEPLOYMENT_GUIDE.txt) Section 6 for complete instructions**

---

## Tech Stack

| Component | Technology | Version |
|-----------|------------|---------|
| **Framework** | ASP.NET Core (Razor Pages) | 8.0 |
| **Runtime** | .NET | 8.0 |
| **Database** | SQLite | 3.x |
| **ORM** | Entity Framework Core | 9.0 |
| **Authentication** | Cookie-based (ASP.NET Core Identity) | 8.0 |
| **Frontend** | Razor Pages + Vanilla JavaScript | - |
| **Localization** | ASP.NET Core Resource Files (.resx) | - |
| **Testing** | xUnit + FluentAssertions + Moq | 2.5.3 + 8.7.1 + 4.20.72 |
| **Image Processing** | SixLabors.ImageSharp | 3.0 |

### Architecture Highlights

- **Multi-tenant**: Automatic company-scoped queries via EF Core interceptors
- **Self-contained deployment**: Complete .NET runtime included (111 MB, 378 files)
- **Air-gapped ready**: No internet connection required for operation
- **Service-oriented**: Clean separation of concerns with injectable services
- **API-first**: 27 REST endpoints for external integrations

**For detailed architecture documentation, see [project.md](project.md)**

---

## Configuration

### Critical Settings (appsettings.json)

**SEED_ADMIN_PASSWORD** (REQUIRED for production):
```json
{
  "SEED_ADMIN_PASSWORD": "YourStrongPassword123!"
}
```
- Sets initial admin password on first run
- Must be strong: 12+ characters, mixed case, numbers, symbols
- CRITICAL: Set before first deployment

**Database Connection**:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=app.db"
  }
}
```
- Default: SQLite database in application folder
- Change path for custom database location

**Network Binding**:
```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://localhost:5000"  // localhost only (most secure)
        // "Url": "http://*:5000"  // Network access (requires firewall config)
      }
    }
  }
}
```

**Multi-Tenancy**:
```json
{
  "Features": {
    "EnforceCompanyScope": false,  // false = admin can see all companies
    "EnableDirectorRole": true     // true = enables Director role
  }
}
```

**Email Notifications** (Optional):
- Configure via Admin → Config UI (recommended)
- Or set environment variables:
  ```cmd
  set EMAIL__ENABLED=true
  set EMAIL__APIKEY=your-api-key
  set EMAIL__APIURL=https://api.yourcompany.com/v1/mail/send
  set EMAIL__FROMADDRESS=noreply@yourcompany.com
  ```

**Griffin ADFS Authentication** (Optional - Air-Gapped Environments):

ShiftManager supports authentication via Griffin ADFS for air-gapped military/government environments.

**Quick Configuration:**
- **Configure via Owner Menu**: Navigate to **Owner → Griffin ADFS**
- **Enable Griffin**: Toggle Griffin ADFS authentication
- **Base URL**: Griffin service endpoint (e.g., `http://7108dev-auth.d8200.mil`)
- **Callback URL**: Your app's callback URL (e.g., `https://your-app.local/Auth/GriffinCallback`)
- **Auto-Provision**: Automatically create user accounts on first Griffin login
- **Default Role**: Role assigned to auto-provisioned users (Employee, Manager, etc.)
- **Timeout**: Timeout for Griffin API calls (1-60 seconds)

**Key Features:**
- Dual authentication: ADFS + email/password fallback
- Login page shows "Login with ADFS" / "הזדהות במערכת היחידה" button (English/Hebrew)
- If Griffin is unavailable, users can always use local authentication
- Login page shows warning when Griffin is temporarily unavailable

**Documentation:**
- **[GRIFFIN_OWNER_GUIDE.md](GRIFFIN_OWNER_GUIDE.md)** - Complete owner/admin guide with:
  - Step-by-step setup for local development and air-gapped environments
  - Authentication flows and user lifecycle management
  - Configuration options and troubleshooting procedures
  - FAQ covering every aspect of Griffin ADFS integration
- [project.md](project.md) Configuration section - Technical reference

---

## Common Issues

### Application Won't Start

**Error: "SEED_ADMIN_PASSWORD must be set"**
- Solution: Edit `appsettings.json`, set `SEED_ADMIN_PASSWORD` to a strong password
- Restart application

**Error: "Port 5000 already in use"**
- Solution 1: Stop other application using port 5000
  ```cmd
  netstat -ano | findstr ":5000"
  taskkill /F /PID [PID]
  ```
- Solution 2: Change port in `appsettings.json` (e.g., 5001, 8080)

### Air-Gapped Deployment Errors

**Error: "Could not load file or assembly 'SixLabors.ImageSharp'"**
- Cause: DLL files blocked by Windows after USB transfer
- Solution: Run `UNBLOCK_FILES.bat` in deployment folder
- Alternative: See [AIR_GAPPED_DEPLOYMENT_GUIDE.txt](AIR_GAPPED_DEPLOYMENT_GUIDE.txt) Manual Unblock Procedure

**Error: "Database error" or "Migration failed"**
- Solution: Delete `app.db`, `app.db-shm`, `app.db-wal` and restart (database recreated automatically)

### Login Issues

**Can't login with admin@local**
- Verify `SEED_ADMIN_PASSWORD` is set correctly (case-sensitive)
- Try deleting `app.db` and restarting (creates fresh database with seed data)
- Check browser console for errors (F12)

**Authentication failed errors**
- Clear browser cookies
- Try incognito/private browsing mode
- Verify system clock is correct

### Network Access Issues

**Can't access from other computers**
- Verify `Kestrel:Endpoints:Http:Url` is set to `http://*:5000` (not localhost)
- Check Windows Firewall allows port 5000
- Verify application is running (check console or Windows Services)

**For complete troubleshooting, see:**
- [FinalProductPublish/DEPLOYMENT_GUIDE.txt](FinalProductPublish/DEPLOYMENT_GUIDE.txt) Section 8
- [AIR_GAPPED_DEPLOYMENT_GUIDE.txt](AIR_GAPPED_DEPLOYMENT_GUIDE.txt) Troubleshooting section
- [USAGE_GUIDE.md](USAGE_GUIDE.md) FAQ section

---

## Support

### Getting Help

**Documentation:**
- [USAGE_GUIDE.md](USAGE_GUIDE.md) - User guide for all roles
- [project.md](project.md) - Complete technical documentation
- [API_DOCUMENTATION.md](API_DOCUMENTATION.md) - API reference
- [CUSTOMIZATION_GUIDE.md](CUSTOMIZATION_GUIDE.md) - UI customization

**Deployment Issues:**
- [AIR_GAPPED_DEPLOYMENT_GUIDE.txt](AIR_GAPPED_DEPLOYMENT_GUIDE.txt) - Offline deployment troubleshooting
- [FinalProductPublish/DEPLOYMENT_GUIDE.txt](FinalProductPublish/DEPLOYMENT_GUIDE.txt) - Production deployment manual

**Reporting Issues:**
- Check Event Viewer (Windows Logs → Application) for error details
- Review [TERMINOLOGY.md](TERMINOLOGY.md) for understanding UI vs. code terminology
- Provide exact error messages and steps to reproduce

---

## Security

### Security Features

- **PBKDF2 Password Hashing** - 100k iterations with SHA256
- **CSRF Protection** - Anti-forgery tokens on all forms
- **SQL Injection Protection** - Parameterized queries via EF Core
- **XSS Protection** - Razor auto-escaping of user input
- **Multi-Tenant Isolation** - Query filters + service layer validation
- **Role-Based Authorization** - Policies + attribute-based access control
- **Encrypted Configuration** - API keys encrypted at rest (ASP.NET Data Protection)
- **Audit Logging** - Complete trail of role assignments and config changes

### Known Security Considerations

- **HTTP by default**: Configure HTTPS for production network access
- **Database encryption**: SQLite database stored unencrypted (consider SQLCipher for production)
- **No rate limiting**: Consider adding rate limiting for production deployments
- **Security headers**: Add CSP, HSTS, X-Frame-Options for production

**Reporting Vulnerabilities:**
Please report security vulnerabilities responsibly via your organization's security contact.

**Production Security Checklist:**
1. Set strong `SEED_ADMIN_PASSWORD` (12+ characters)
2. Change admin password after first login
3. Protect `appsettings.json` with NTFS permissions (Admin only)
4. Configure HTTPS for network access
5. Enable Windows Firewall rules
6. Set up regular database backups
7. Run as Windows Service with dedicated service account
8. Monitor Event Viewer logs regularly

**See [FinalProductPublish/DEPLOYMENT_GUIDE.txt](FinalProductPublish/DEPLOYMENT_GUIDE.txt) Section 9 for complete security hardening**

---

## Contributing

We welcome contributions to ShiftManager!

### How to Contribute

1. Fork the repository
2. Create a feature branch: `git checkout -b feature/my-feature`
3. Make your changes following coding standards
4. Write unit tests for new functionality (80% coverage target)
5. Commit changes: `git commit -m 'Add my feature'`
6. Push to branch: `git push origin feature/my-feature`
7. Submit a pull request

### Coding Standards

- Follow ASP.NET Core conventions and best practices
- Write unit tests for services (80% coverage goal)
- Update documentation for new features
- Test database migrations (up/down/up cycle)
- Maintain multi-tenant isolation in new features
- Support both English and Hebrew localization

### Development Commands

```bash
# Run with hot-reload
dotnet watch

# Run tests
dotnet test

# Create migration
dotnet ef migrations add <MigrationName>

# Update database
dotnet ef database update

# Reset to clean database
rm app.db
cp seed.db app.db
```

---

## License

[MIT License](LICENSE)

Copyright (c) 2025 ShiftManager

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

---

**Built with ASP.NET Core 8.0 for secure, offline enterprise environments**
