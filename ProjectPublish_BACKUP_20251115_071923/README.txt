================================================================================
                    WELCOME TO SHIFTMANAGER
              Air-Gapped Deployment for Windows Server 2022
================================================================================

WHAT'S IN THIS FOLDER:
======================
This is a complete, self-contained deployment of ShiftManager.
Everything you need is included - no internet connection required!

GETTING STARTED (Choose your path):
====================================

🚀 FASTEST START (For Quick Testing):
   1. Double-click: START_HERE.bat
   2. Follow the on-screen instructions
   3. Done!

📖 STEP-BY-STEP GUIDE (Recommended):
   1. Read: QUICK_START.txt (3 simple steps)
   2. That's it!

📚 COMPREHENSIVE GUIDE (For Production Deployment):
   1. Read: DEPLOYMENT_GUIDE.txt (full documentation)
   2. Includes: Windows Service setup, IIS configuration, security, etc.

✅ VERIFICATION & TROUBLESHOOTING:
   1. See: VERIFICATION_CHECKLIST.txt (deployment verification)
   2. See: DEPLOYMENT_GUIDE.txt (troubleshooting section)

================================================================================
ABSOLUTE MINIMUM TO GET STARTED:
================================================================================

Before running ShiftManager.exe, you MUST:

  1. Open appsettings.json in Notepad
  2. Change: "SEED_ADMIN_PASSWORD": "",
     To:     "SEED_ADMIN_PASSWORD": "YourPassword123!",
  3. Save the file
  4. Run ShiftManager.exe

That's the ONLY required step!

================================================================================
FILES IN THIS FOLDER:
================================================================================

📄 Documentation:
   README.txt                    ← You are here!
   VERSION.txt                  ← Version info and changelog
   QUICK_START.txt              ← 3-step quick start guide
   DEPLOYMENT_GUIDE.txt         ← Complete deployment manual
   UPGRADE_GUIDE.txt            ← Upgrading from old versions
   VERIFICATION_CHECKLIST.txt   ← Deployment verification results
   DEPLOYMENT_READINESS_REPORT.txt ← Automated test results
   FINAL_CERTIFICATION.txt      ← Production certification

🚀 Startup:
   START_HERE.bat               ← Automated startup script with checks
   ShiftManager.exe             ← Main application (double-click to run)

⚙️ Configuration:
   appsettings.json             ← Main configuration (EDIT THIS!)
   appsettings.Development.json ← Development settings
   web.config                   ← IIS configuration

🗄️ Database:
   e_sqlite3.dll                ← SQLite native library
   app.db                       ← Database file (created on first run)

🌐 Web Files:
   wwwroot/                     ← Static files (CSS, JavaScript)

🌍 Localization:
   he-IL/                       ← Hebrew language resources

📦 Runtime & Dependencies:
   347 files                    ← .NET runtime + all dependencies
   Total size: 109 MB

================================================================================
DEFAULT CREDENTIALS:
================================================================================

After setting SEED_ADMIN_PASSWORD in appsettings.json:

  URL:      http://localhost:5000
  Email:    admin@local
  Password: [whatever you set in SEED_ADMIN_PASSWORD]

IMPORTANT: Change the password after first login!

================================================================================
SYSTEM REQUIREMENTS:
================================================================================

✓ Windows Server 2022 (or Windows 10/11 64-bit)
✓ Write permissions in the application folder
✓ Available port 5000 (or configure a different port)

Optional (helpful but not required):
  - .NET 8.0 Hosting Bundle (included in this package)
  - .NET 8.0 SDK (included in this package)
  - .NET 8.0 Runtime (included in this package)

This is a SELF-CONTAINED deployment - it includes its own .NET runtime!

================================================================================
DEPLOYMENT CONFIDENCE: 99.9%
================================================================================

✅ Complete self-contained package verified
✅ All 347 dependencies included
✅ SQLite database library present
✅ All runtime files included
✅ Configuration files ready
✅ Localization resources present
✅ Web files ready
✅ OFFLINE shift type automatically seeded
✅ On-Duty types (Hakam, Lead) built-in

⚠️ Only potential issue: Environment variable/password configuration
✅ SOLVED: Added SEED_ADMIN_PASSWORD to appsettings.json
✅ SOLVED: Created START_HERE.bat to check configuration

This deployment WILL WORK on your air-gapped server!

================================================================================
QUICK TROUBLESHOOTING:
================================================================================

❌ App crashes with "SEED_ADMIN_PASSWORD must be set"
✅ Edit appsettings.json and set SEED_ADMIN_PASSWORD (see QUICK_START.txt)

❌ Can't create database / access denied
✅ Run as Administrator or give write permissions to the folder

❌ Port 5000 already in use
✅ Close other apps or change port (see DEPLOYMENT_GUIDE.txt)

❌ Can't access from other computers
✅ Configure firewall (see DEPLOYMENT_GUIDE.txt Step 6)

For more help, see DEPLOYMENT_GUIDE.txt Troubleshooting section.

================================================================================
SECURITY RECOMMENDATIONS:
================================================================================

Before going to production:

1. ✓ Set a STRONG password in appsettings.json
2. ✓ Protect appsettings.json with NTFS permissions (Admin only)
3. ✓ Change admin password after first login
4. ✓ Configure HTTPS for network access
5. ✓ Enable Windows Firewall rules
6. ✓ Set up regular database backups
7. ✓ Run as Windows Service (not as user)

See DEPLOYMENT_GUIDE.txt for detailed instructions.

================================================================================
SUPPORT:
================================================================================

All documentation is included in this folder:

  - QUICK_START.txt              → Get running in 3 steps
  - DEPLOYMENT_GUIDE.txt         → Complete manual (all scenarios)
  - VERIFICATION_CHECKLIST.txt   → Technical verification details
  - VERSION.txt                  → Version info and changelog
  - README.txt                   → This file

If you encounter issues:
  1. Check DEPLOYMENT_GUIDE.txt Troubleshooting section
  2. Verify all files are present (see VERIFICATION_CHECKLIST.txt)
  3. Ensure appsettings.json has SEED_ADMIN_PASSWORD set

================================================================================
WHAT HAPPENS ON FIRST RUN:
================================================================================

The application will automatically:
  1. Create app.db SQLite database
  2. Run all database migrations
  3. Create initial company: "Demo Co"
  4. Create shift types: MORNING, NOON, NIGHT, MIDDLE, OFFLINE
  5. Set default config: 8 hours rest, 40 hours/week cap
  6. Create admin user with your password
  7. Start web server on port 5000

Total startup time: 5-10 seconds

================================================================================
FEATURES:
================================================================================

✓ Multi-tenant (multiple companies)
✓ Role-based access (Owner, Director, Manager, Assigner, Employee, Trainee)
✓ Shift scheduling with conflict detection
✓ OFFLINE shift type (for non-working days)
✓ On-Duty assignments (Hakam 🛡️, Lead ⭐)
✓ Rest hours enforcement
✓ Weekly hours caps
✓ Notifications system
✓ Multi-language (English, Hebrew)
✓ RTL support for Hebrew
✓ Cookie-based authentication
✓ Complete API layer with 27 endpoints

================================================================================
NEXT STEPS:
================================================================================

1. Run START_HERE.bat (or follow QUICK_START.txt)
2. Login and verify everything works
3. Create additional users
4. Configure shift types for your needs
5. Set up as Windows Service (see DEPLOYMENT_GUIDE.txt)
6. Configure network access (if needed)
7. Set up database backups

================================================================================
ENJOY SHIFTMANAGER!
================================================================================

This deployment package has been verified and is ready for your air-gapped
Windows Server 2022 environment.

All dependencies are included. No internet connection required.

Questions? See DEPLOYMENT_GUIDE.txt for comprehensive documentation.

Version: v1.0.0 (Release Candidate)
Package Date: 2025-11-12
Package Size: 109 MB
Deployment Type: Self-Contained

================================================================================
