# ShiftManager - Air-Gapped Deployment Guide

**Version:** 1.0.3-final
**Build Date:** 2025-11-15
**Environment:** Windows (offline/air-gapped)

## Table of Contents

1. [Quick Start](#quick-start)
2. [Pre-Deployment Requirements](#pre-deployment-requirements)
3. [Step-by-Step Deployment](#step-by-step-deployment)
4. [Verification](#verification)
5. [Troubleshooting](#troubleshooting)
6. [API Integration](#api-integration)

---

## Quick Start

**For immediate deployment on an air-gapped machine:**

1. Copy `FinalProductPublish/` folder to USB drive
2. Transfer to target Windows machine
3. Open Command Prompt in the `FinalProductPublish` folder
4. Run: `UNBLOCK_FILES.bat` (CRITICAL - prevents DLL loading errors)
5. Run: `START_HERE.bat`
6. Open browser: `http://localhost:5000`
7. Login: `admin@local` / `ShareholderDemo2025!`

---

## Pre-Deployment Requirements

### Target Machine Requirements

- **Operating System:** Windows 10/11 or Windows Server 2019/2022
- **Architecture:** x64 (64-bit)
- **RAM:** Minimum 2 GB, Recommended 4 GB
- **Disk Space:** 500 MB free space
- **Port:** TCP port 5000 must be available
- **.NET Runtime:** NOT REQUIRED (self-contained deployment)

### No Internet Required

This is a fully self-contained deployment:
- All .NET 8.0 runtime libraries included (336 DLLs)
- All dependencies bundled
- SQLite database (embedded)
- No external API calls
- Email notifications disabled by default

---

## Step-by-Step Deployment

### Step 1: Transfer Files

1. **Copy to USB Drive:**
   ```
   Copy the entire FinalProductPublish folder to your USB drive
   Size: ~110 MB, 448 files
   ```

2. **Transfer to Target Machine:**
   ```
   Recommended location: C:\ShiftManager\
   Copy FinalProductPublish folder from USB to target location
   ```

### Step 2: Unblock DLLs (CRITICAL)

Windows marks files from external sources as "blocked" which prevents DLL loading.

1. **Open Command Prompt** in the `FinalProductPublish` folder:
   ```
   cd C:\ShiftManager\FinalProductPublish
   ```

2. **Run UNBLOCK_FILES.bat:**
   ```
   UNBLOCK_FILES.bat
   ```

3. **Expected Output:**
   ```
   [1/3] Using PowerShell Unblock-File...
   Successfully unblocked 336 DLLs

   [CRITICAL] Testing SixLabors.ImageSharp.dll...
   ✓ SixLabors.ImageSharp.dll is UNBLOCKED

   ALL FILES UNBLOCKED SUCCESSFULLY
   ```

**If unblocking fails:**
- Right-click each .dll file → Properties → Check "Unblock" → OK
- Or use the manual unblock PowerShell command (see TROUBLESHOOT_DEMO.txt)

### Step 3: Verify Files (Optional but Recommended)

```
VERIFY_FILES.bat
```

**Expected Output:**
- ✓ All 5 critical DLLs present
- ✓ DLL count: 336 DLLs
- ✓ No files blocked
- ✓ Admin password configured

### Step 4: Start Application

```
START_HERE.bat
```

**Wait for startup message:**
```
Now listening on: http://localhost:5000
Application started. Press Ctrl+C to shut down.
```

**Startup time:** 10-15 seconds (first run includes database initialization)

### Step 5: Access Application

1. **Open Web Browser** (Chrome, Edge, Firefox)
2. **Navigate to:** `http://localhost:5000`
3. **Login:**
   - **Email:** `admin@local`
   - **Password:** `ShareholderDemo2025!`

### Step 6: Verify Functionality

After logging in, verify core features:

- [ ] Dashboard loads with statistics
- [ ] Navigate to Users → Create New User
- [ ] Navigate to Calendar → View shifts
- [ ] Navigate to My Team → See team calendar
- [ ] Test language switcher (Hebrew ↔ English)
- [ ] **Easter Egg:** Press Ctrl+Click on "ShiftManager" logo (Shift Swap game should appear)

---

## Verification

### Database Verification

After first startup, verify database was created:

```
dir app.db
```

**Expected:** File size ~20-40 KB with current timestamp

### Shift Types Verification

The application should seed 5 shift types:
1. MORNING (06:00-14:00)
2. NOON (14:00-22:00)
3. NIGHT (22:00-06:00)
4. MIDDLE (10:00-18:00)
5. OFFLINE (Special shift type for off-duty personnel)

Verify in UI: Calendar → Create Shift → Shift Type dropdown should show all 5 types

### API Verification

Test API health endpoint:

```
curl http://localhost:5000/health
```

**Expected Response:**
```json
{
  "status": "Healthy",
  "timestamp": "2025-11-15T12:00:00Z"
}
```

### All Seeded Data

On first startup, the application automatically seeds:
- 1 Default company ("Default Company")
- 1 Admin user (admin@local)
- 5 Shift types (MORNING, NOON, NIGHT, MIDDLE, OFFLINE)
- 2 Configuration entries

---

## Troubleshooting

### Error: "Could not load file or assembly 'SixLabors.ImageSharp'"

**Cause:** DLLs are blocked by Windows

**Solution:**
```
1. Run UNBLOCK_FILES.bat again
2. If that fails, run: QUICK_FIX.bat
3. Restart application
```

### Error: "Address already in use" or "Port 5000 conflict"

**Cause:** Another application is using port 5000

**Solution:**
```
1. Check what's using port 5000:
   netstat -ano | findstr ":5000"

2. Kill the process (use PID from above):
   taskkill /F /PID <PID>

3. Restart START_HERE.bat
```

### Error: "Login failed" or "Incorrect password"

**Cause:** Admin password not set or incorrect

**Solution:**
```
1. Stop application (Ctrl+C)
2. Open appsettings.json in Notepad
3. Find "SEED_ADMIN_PASSWORD" (line 84)
4. Ensure it's set to: "ShareholderDemo2025!"
5. Save and restart
```

### Application Won't Start

**Run emergency repair:**
```
QUICK_FIX.bat
```

This will:
1. Stop any running instances
2. Verify all files present
3. Unblock DLLs
4. Validate configuration
5. Start application

### Database Corruption

If database becomes corrupt delete it and restart (will re-seed):
```
del app.db
START_HERE.bat
```

**Note:** This deletes all data. Only use for testing/demo purposes.

---

## API Integration

### API Documentation

Complete API documentation: `API_DOCUMENTATION.md`

### API Client Libraries

**JavaScript Client:**
```
clients/javascript/shiftmanager-client.js
```

**Python Client:**
```
clients/python/shiftmanager_client.py
clients/python/README.md
```

### API Endpoints

**Authentication:**
- POST `/api/auth/login` - Login and get session

**Users:**
- GET `/api/users` - List all users
- GET `/api/users/{id}` - Get user by ID
- POST `/api/users` - Create new user
- PUT `/api/users/{id}` - Update user

**Shifts:**
- GET `/api/shifts` - List shifts (with filters)
- GET `/api/shifts/{id}` - Get shift by ID

**Time Off:**
- GET `/api/timeoff` - List time-off requests
- POST `/api/timeoff` - Create time-off request
- POST `/api/timeoff/{id}/approve` - Approve request
- POST `/api/timeoff/{id}/decline` - Decline request

**Analytics:**
- GET `/api/analytics/summary` - Get dashboard summary

**Full list:** See `API_DOCUMENTATION.md`

### API Authentication

All API endpoints (except `/health`) require authentication:

1. **Login to get session cookie:**
   ```bash
   curl -X POST http://localhost:5000/api/auth/login \
     -H "Content-Type: application/json" \
     -d '{"email":"admin@local","password":"ShareholderDemo2025!"}' \
     -c cookies.txt
   ```

2. **Use session cookie in subsequent requests:**
   ```bash
   curl http://localhost:5000/api/users \
     -b cookies.txt
   ```

---

## Production Deployment Checklist

Before deploying to production (non-demo environment):

- [ ] Change admin password from demo password
- [ ] Review and customize `appsettings.json` settings
- [ ] Set appropriate `AllowedHosts` (currently set to "localhost")
- [ ] Configure `EnforceCompanyScope` as needed
- [ ] Set up backup strategy for `app.db`
- [ ] Configure reverse proxy (IIS/Nginx) if needed
- [ ] Set up SSL/TLS certificate for HTTPS
- [ ] Review and enable/disable API endpoints as needed
- [ ] Test all workflows in production environment
- [ ] Document custom configuration changes

---

## File Structure

```
FinalProductPublish/
│
├── ShiftManager.exe              # Main application executable
├── ShiftManager.dll              # Main application library
├── appsettings.json              # Configuration file
├── app.db                        # SQLite database (created on first run)
│
├── Helper Scripts:
│   ├── START_HERE.bat            # Application launcher
│   ├── UNBLOCK_FILES.bat         # DLL unblocking utility
│   ├── VERIFY_FILES.bat          # File verification utility
│   ├── QUICK_FIX.bat             # Emergency repair utility
│
├── Documentation:
│   ├── DEPLOYMENT.md             # This file
│   ├── API_DOCUMENTATION.md      # Complete API reference
│   ├── CRITICAL_BEFORE_DEMO.txt  # Demo preparation guide
│   ├── AIR_GAPPED_DEPLOYMENT_GUIDE.txt
│   ├── PRE_DEMO_CHECKLIST.txt
│   ├── TROUBLESHOOT_DEMO.txt
│
├── API Clients:
│   └── clients/
│       ├── javascript/           # JavaScript client library
│       └── python/               # Python client library
│
├── Runtime Dependencies:
│   ├── Microsoft.*.dll           # 300+ Microsoft DLLs
│   ├── e_sqlite3.dll             # SQLite native library
│   ├── SixLabors.ImageSharp.dll  # Image processing library
│   └── ... (333 more DLLs)
│
├── Localization:
│   └── he-IL/                    # Hebrew translations
│       └── ShiftManager.resources.dll
│
└── Static Assets:
    └── wwwroot/
        ├── css/
        │   ├── site.css
        │   ├── rtl.css           # Right-to-left layout
        │   └── shift-swap-game.css  # Easter egg styles
        └── js/
            ├── site.js
            ├── myteam.js
            ├── hebrew-audit.js
            └── shift-swap-game.js   # Easter egg game
```

---

## Features Overview

### Core Features
- User management with role-based access (Admin, Director, User)
- Shift scheduling with 5 shift types
- Team calendar views
- Time-off request management
- Shift swap requests
- Chores assignment
- On-duty assignments (Hakam 🛡️, Lead ⭐)
- Notifications system
- Audit logging
- Feedback submission with file uploads

### Localization
- Full Hebrew/English support
- RTL (Right-to-Left) layout for Hebrew
- Language switcher in UI

### API Layer
- 17+ RESTful API endpoints
- JSON request/response format
- Session-based authentication
- Comprehensive error handling

### Easter Egg
- "Shift Swap" match-3 game
- Trigger: Ctrl+Click on "ShiftManager" logo
- Icons: ⏰ 📅 🧹 ☕ 📦 🔔

---

## Support

For issues encountered during deployment:

1. Check `TROUBLESHOOT_DEMO.txt` for common errors
2. Run `VERIFY_FILES.bat` to check file integrity
3. Run `QUICK_FIX.bat` for automatic repairs
4. Review application logs in Command Prompt window

---

## Security Notes

### Demo Environment
- Default password is set for demo purposes
- **Change password before production use**
- Email notifications are disabled (air-gapped)

### Production Environment
- Use strong passwords
- Enable HTTPS/SSL
- Configure firewall rules
- Set up regular database backups
- Review API endpoint permissions
- Enable audit logging

---

## Version History

**v1.0.3-final (2025-11-15)**
- Fresh production build from latest source code
- Added API client libraries (JavaScript + Python)
- Complete API documentation
- Enhanced helper scripts for air-gapped deployment
- OFFLINE shift type implemented
- Hebrew localization complete
- Easter egg feature included

---

**End of Deployment Guide**

For detailed API documentation, see: `API_DOCUMENTATION.md`
For demo preparation, see: `PRE_DEMO_CHECKLIST.txt`
For troubleshooting, see: `TROUBLESHOOT_DEMO.txt`
