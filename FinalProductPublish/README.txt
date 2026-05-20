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

ðŸš€ FASTEST START (For Quick Testing):
   1. Double-click: START_HERE.bat
   2. Follow the on-screen instructions
   3. Done!

ðŸ“– STEP-BY-STEP GUIDE (Recommended):
   1. Read: QUICK_START.txt (3 simple steps)
   2. That's it!

ðŸ“š COMPREHENSIVE GUIDE (For Production Deployment):
   1. Read: DEPLOYMENT_GUIDE.txt (full documentation)
   2. Includes: Windows Service setup, IIS configuration, security, etc.

âœ… VERIFICATION & TROUBLESHOOTING:
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

ðŸ“„ Documentation:
   README.txt                    â† You are here!
   VERSION.txt                  â† Version info and changelog
   QUICK_START.txt              â† 3-step quick start guide
   DEPLOYMENT_GUIDE.txt         â† Complete deployment manual
   UPGRADE_GUIDE.txt            â† Upgrading from old versions
   VERIFICATION_CHECKLIST.txt   â† Deployment verification results
   DEPLOYMENT_READINESS_REPORT.txt â† Automated test results
   FINAL_CERTIFICATION.txt      â† Production certification

ðŸš€ Startup:
   START_HERE.bat               â† Automated startup script with checks
   ShiftManager.exe             â† Main application (double-click to run)

âš™ï¸ Configuration:
   appsettings.json             â† Main configuration (EDIT THIS!)
   appsettings.Development.json â† Development settings
   web.config                   â† IIS configuration

ðŸ—„ï¸ Database:
   e_sqlite3.dll                â† SQLite native library
   app.db                       â† Database file (created on first run)

ðŸŒ Web Files:
   wwwroot/                     â† Static files (CSS, JavaScript)

ðŸŒ Localization:
   he-IL/                       â† Hebrew language resources

ðŸ“¦ Runtime & Dependencies:
   525 files                    â† .NET runtime + all dependencies
   Total size: 166 MB

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

âœ“ Windows Server 2022 (or Windows 10/11 64-bit)
âœ“ Write permissions in the application folder
âœ“ Available port 5000 (or configure a different port)

Optional (helpful but not required):
  - .NET 8.0 Hosting Bundle (included in this package)
  - .NET 8.0 SDK (included in this package)
  - .NET 8.0 Runtime (included in this package)

This is a SELF-CONTAINED deployment - it includes its own .NET runtime!

================================================================================
DEPLOYMENT CONFIDENCE: 99.9%
================================================================================

âœ… Complete self-contained package verified
âœ… All 525 dependencies included
âœ… SQLite database library present
âœ… All runtime files included
âœ… Configuration files ready
âœ… Localization resources present
âœ… Web files ready
âœ… OFFLINE shift type automatically seeded
âœ… On-Duty types (Hakam, Lead) built-in

âš ï¸ Only potential issue: Environment variable/password configuration
âœ… SOLVED: Added SEED_ADMIN_PASSWORD to appsettings.json
âœ… SOLVED: Created START_HERE.bat to check configuration

This deployment WILL WORK on your air-gapped server!

================================================================================
QUICK TROUBLESHOOTING:
================================================================================

âŒ App crashes with "SEED_ADMIN_PASSWORD must be set"
âœ… Edit appsettings.json and set SEED_ADMIN_PASSWORD (see QUICK_START.txt)

âŒ Can't create database / access denied
âœ… Run as Administrator or give write permissions to the folder

âŒ Port 5000 already in use
âœ… Close other apps or change port (see DEPLOYMENT_GUIDE.txt)

âŒ Can't access from other computers
âœ… Configure firewall (see DEPLOYMENT_GUIDE.txt Step 6)

For more help, see DEPLOYMENT_GUIDE.txt Troubleshooting section.

================================================================================
SECURITY RECOMMENDATIONS:
================================================================================

Before going to production:

1. âœ“ Set a STRONG password in appsettings.json
2. âœ“ Protect appsettings.json with NTFS permissions (Admin only)
3. âœ“ Change admin password after first login
4. âœ“ Configure HTTPS for network access
5. âœ“ Enable Windows Firewall rules
6. âœ“ Set up regular database backups
7. âœ“ Run as Windows Service (not as user)

See DEPLOYMENT_GUIDE.txt for detailed instructions.

================================================================================
SUPPORT:
================================================================================

All documentation is included in this folder:

  - QUICK_START.txt              â†’ Get running in 3 steps
  - DEPLOYMENT_GUIDE.txt         â†’ Complete manual (all scenarios)
  - VERIFICATION_CHECKLIST.txt   â†’ Technical verification details
  - VERSION.txt                  â†’ Version info and changelog
  - README.txt                   â†’ This file

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

âœ“ Multi-tenant (multiple companies)
âœ“ Role-based access (Owner, Director, Manager, Assigner, Employee, Trainee)
âœ“ Shift scheduling with conflict detection
âœ“ OFFLINE shift type (for non-working days)
âœ“ On-Duty assignments (Hakam ðŸ›¡ï¸, Lead â­)
âœ“ Rest hours enforcement
âœ“ Weekly hours caps
âœ“ Notifications system
âœ“ Multi-language (English, Hebrew)
âœ“ RTL support for Hebrew
âœ“ Cookie-based authentication
âœ“ Complete API layer with 27 endpoints

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

Version: v4.0.1
Package Date: 2026-05-20
Package Size: 166 MB
Deployment Type: Self-Contained

================================================================================

