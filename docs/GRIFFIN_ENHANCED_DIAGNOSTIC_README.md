# Griffin ADFS Enhanced Diagnostic Tool - README

**Version:** 2.0
**Date:** 2026-01-07
**Status:** ✅ PRODUCTION READY

---

## 🎯 What This Tool Does

The **Griffin ADFS Comprehensive Diagnostic Tool** is a powerful debugging assistant that performs **7 comprehensive validation checks** to diagnose any Griffin ADFS authentication issues.

### Key Features

✅ **Validation Score** - Shows percentage of checks passing
✅ **Real-time Server Connectivity** - Tests if Griffin server is reachable
✅ **URL Scheme Validation** - Detects missing http:// or https://
✅ **Callback Page Verification** - Confirms GriffinCallback.cshtml exists
✅ **Configuration Source Detection** - Shows if using database or appsettings.json
✅ **Common Mistake Detection** - Warns about localhost usage, trailing slashes, etc.
✅ **JSON Export** - Full diagnostic data for support tickets

---

## 🚀 How To Access

### Method 1: From GriffinConfig Page
1. Navigate to: `http://localhost:5000/Owner/GriffinConfig`
2. Click the purple **"🔍 Run Full Diagnostic"** button
3. Opens in new tab

### Method 2: Direct URL
Navigate to: `http://localhost:5000/GriffinDiagnostic`

---

## 📊 What Gets Checked

The diagnostic runs **7 comprehensive checks**:

### ✅ Check 1: Configuration Source
- Verifies if configuration is in database or appsettings.json
- Warns if using fallback configuration
- **Pass Criteria**: Configuration found

### ✅ Check 2: Base URL Validation
- Checks if BaseUrl starts with `http://` or `https://`
- **Pass Criteria**: Valid absolute URL with scheme
- **Common Failure**: `7108dev.d8200.mil` (missing `https://`)

### ✅ Check 3: Callback URL Validation
- Checks if TokenConsumerUrl starts with `http://` or `https://`
- Verifies path is `/Auth/GriffinCallback`
- Warns if using `localhost` (may not work from Griffin server)
- **Pass Criteria**: Valid absolute URL with correct path
- **Common Failure**: `/Auth/GriffinCallback` (missing `http://localhost:5000`)

### ✅ Check 4: Callback Page Exists
- Verifies `Pages/Auth/GriffinCallback.cshtml` file exists
- **Pass Criteria**: File found on disk
- **Failure Indicates**: Missing callback page or incorrect path

### ✅ Check 5: Generated URL Validation
- Builds the authentication URL that Login would create
- Checks if URL is absolute (not relative)
- Validates path is `/authentication`
- **Pass Criteria**: Absolute URL generated correctly
- **Failure Indicates**: Will cause 404 redirect error

### ✅ Check 6: Griffin Server Connectivity
- Makes HTTP request to Griffin server
- Reports response time and HTTP status code
- **Pass Criteria**: Server responds within timeout
- **Failure Indicates**: Network issue, firewall, or wrong BaseUrl

### ✅ Check 7: Additional Validations
- Detects common mistakes (missing `/`, extra spaces, etc.)
- Checks for trailing slashes
- Warns about relative paths
- **Pass Criteria**: No common mistakes detected

---

## 📈 Understanding The Results

### Summary Cards (Top Section)

#### Overall Status
- **✓ (Green)**: All checks passed - Ready to use!
- **⚠ (Yellow)**: Minor warnings - May work but review warnings
- **✗ (Red)**: Critical issues - Will NOT work until fixed

#### Validation Score
- Shows percentage of checks passing
- **100%**: Perfect configuration
- **70-99%**: Minor issues
- **<70%**: Critical problems

#### Server Status
- **✓ Online**: Griffin server is reachable
- **✗ Unreachable**: Cannot connect to Griffin server

#### Config Source
- **Database**: Configuration loaded from database (correct)
- **appsettings.json**: Using fallback configuration (check if intended)
- **None**: No configuration found (need to configure)

---

### Diagnostic Checks Section

Shows step-by-step validation results:
- **✓ (Green)**: Check passed
- **✗ (Red)**: Check failed - needs fixing
- **⚠ (Orange)**: Warning - review recommended

Example output:
```
Starting comprehensive Griffin ADFS diagnostic...
1. Checking configuration source...
   ✓ Configuration loaded from database
2. Validating Base URL...
   ✗ BaseUrl is MISSING scheme: '7108dev.d8200.mil'
3. Validating Callback URL...
   ✓ Callback URL has valid scheme: http://
   ✓ Callback path is correct: /Auth/GriffinCallback
...
```

---

### Root Cause Analysis

If issues are detected, shows:
- **🔴 CRITICAL**: Exact problem description
- **Why this causes the error**: Technical explanation
- **Fix Instructions**: Step-by-step resolution

Example:
```
🔴 CRITICAL: BaseUrl is missing the http:// or https:// scheme

Current value: 7108dev.d8200.mil
Expected value: https://7108dev.d8200.mil

Why this causes the 404 error:
- ASP.NET's Redirect() method sees this URL doesn't start with a scheme
- It treats it as a RELATIVE path instead of an absolute URL
- The browser tries to navigate to: http://localhost:5000/7108dev.d8200.mil/...
- This path doesn't exist, resulting in a 404 error
```

---

## 🔧 Common Issues & Fixes

### Issue 1: "BaseUrl is MISSING scheme"

**Symptoms:**
- Diagnostic shows: `✗ BaseUrl is MISSING scheme`
- Login redirects to 404 error
- Error URL starts with `/authentication/...`

**Fix:**
1. Go to `/Owner/GriffinConfig`
2. Change BaseUrl from: `7108dev.d8200.mil`
3. To: `https://7108dev.d8200.mil`
4. Click "Test Connection" (saves automatically)
5. Verify diagnostic shows: `✓ BaseUrl has valid scheme`

---

### Issue 2: "Callback URL is MISSING scheme"

**Symptoms:**
- Diagnostic shows: `✗ Callback URL is MISSING scheme`
- May cause authentication callback failures

**Fix:**
1. Go to `/Owner/GriffinConfig`
2. Change Callback URL from: `/Auth/GriffinCallback`
3. To: `http://localhost:5000/Auth/GriffinCallback`
4. Click "Test Connection"
5. Verify diagnostic shows: `✓ Callback URL has valid scheme`

---

### Issue 3: "Griffin server is NOT reachable"

**Symptoms:**
- Diagnostic shows: `✗ Griffin server is NOT reachable`
- Connection test fails

**Possible Causes:**
1. **Wrong BaseUrl**: Typo in server address
2. **Network issue**: Cannot reach Griffin server from this machine
3. **Firewall**: Port blocked between machines
4. **Griffin server down**: Server not running

**Fix:**
1. Verify BaseUrl is correct (ask Griffin admin)
2. Test network connectivity: `ping 7108dev.d8200.mil`
3. Check firewall settings
4. Verify Griffin server is running

---

### Issue 4: "Callback path may be incorrect"

**Symptoms:**
- Diagnostic shows: `⚠ Callback path may be incorrect`
- Path is not `/Auth/GriffinCallback`

**Fix:**
1. Go to `/Owner/GriffinConfig`
2. Ensure Callback URL ends with: `/Auth/GriffinCallback`
3. Example: `http://localhost:5000/Auth/GriffinCallback`
4. Click "Test Connection"

---

### Issue 5: "Uses localhost" Warning

**Symptoms:**
- Diagnostic shows: `⚠ Uses localhost` badge
- May work on local machine but not from Griffin server

**Fix (if needed):**
1. Find your server's IP address: `ipconfig` (Windows) or `ifconfig` (Linux)
2. Change Callback URL to use IP instead of localhost
3. Example: `http://192.168.1.100:5000/Auth/GriffinCallback`
4. Verify Griffin server can reach this IP

**Note:** Only fix this if Griffin server is on a different machine!

---

## 📋 Using The JSON Export

The diagnostic provides a JSON export at the bottom of the page.

### When To Use It

- Creating support tickets
- Sharing configuration with team
- Documenting issues
- Comparing before/after configurations

### How To Use It

1. Scroll to bottom: "Diagnostic Data (JSON Export)"
2. Click inside the code block
3. Press `Ctrl+A` to select all
4. Press `Ctrl+C` to copy
5. Paste into support ticket, email, or documentation

---

## 🎯 Best Practices

### Before Changing Configuration

1. **Run diagnostic first** - Understand current state
2. **Note the validation score** - Establishes baseline
3. **Read all warnings** - May indicate issues

### After Changing Configuration

1. **Click "Test Connection"** in GriffinConfig (saves changes)
2. **Re-run diagnostic** - Verify improvement
3. **Check validation score** - Should be higher
4. **Test login** - Try "Login with ADFS"

### Troubleshooting Workflow

```
1. Run Diagnostic
   ↓
2. Check Validation Score
   ↓
3. If < 100%, read "Root Cause Analysis"
   ↓
4. Follow "Fix Instructions"
   ↓
5. Click "Test Connection" to save
   ↓
6. Re-run Diagnostic
   ↓
7. Verify score improved
   ↓
8. Test login
```

---

## 🔍 Technical Details

### What Makes This Diagnostic "Robust"

#### Multiple Validation Layers
1. **Syntax Validation**: Checks URL format
2. **Semantic Validation**: Verifies URLs make sense
3. **File System Check**: Confirms files exist
4. **Network Validation**: Tests actual connectivity
5. **Logic Validation**: Simulates login flow
6. **Pattern Detection**: Identifies common mistakes
7. **Context Awareness**: Warns about environment-specific issues

#### Catches Edge Cases
- Double slashes (`//example.com` vs `http://example.com`)
- Relative paths (`/Auth/...` vs `http://...`)
- Trailing slashes (may cause double-slash in URLs)
- Localhost vs IP address (network reachability)
- Missing callback page (file system check)

#### Auto-Generated Fix Instructions
- Detects EXACT problem
- Provides step-by-step fix
- Shows before/after values
- Explains WHY it's a problem

---

## 📊 Diagnostic Score Guide

| Score | Status | Meaning | Action |
|-------|--------|---------|--------|
| **100%** | ✓ Perfect | All checks passed | Ready to use! |
| **85-99%** | ⚠ Minor Issues | Mostly working, minor warnings | Review warnings, may work |
| **70-84%** | ⚠ Warnings | Some problems detected | Fix recommended before use |
| **50-69%** | ✗ Problems | Multiple failures | Must fix before use |
| **<50%** | ✗ Critical | Severe configuration errors | Cannot work until fixed |

---

## 🤝 Integration With Test Connection

The diagnostic tool works alongside the **Test Connection** button:

### Test Connection
- **Purpose**: Quick check if Griffin server is reachable
- **What it does**: HTTP request to Griffin `/authentication` endpoint
- **When to use**: After saving configuration
- **Result**: Pass/Fail with response time

### Full Diagnostic
- **Purpose**: Deep analysis of entire configuration
- **What it does**: 7 comprehensive checks + network test
- **When to use**: Troubleshooting issues, before deployment
- **Result**: Detailed report with validation score

### Recommended Workflow

1. Configure Griffin settings in `/Owner/GriffinConfig`
2. Click **"Test Connection"** - Quick verify + auto-save
3. If fails OR before deployment, click **"🔍 Run Full Diagnostic"**
4. Review full report and fix any issues
5. Click **"Test Connection"** again to save fixes
6. Re-run diagnostic to verify

---

## 📞 Support & Troubleshooting

### If Diagnostic Shows All Green But Login Still Fails

Check these additional items:

1. **Griffin Server Configuration**
   - Is callback URL whitelisted on Griffin?
   - Is relying party trust configured?
   - Check Griffin server logs

2. **Network Issues**
   - Can Griffin server reach callback URL?
   - Firewall blocking return traffic?
   - DNS resolution working?

3. **Browser Issues**
   - Clear browser cache
   - Try different browser
   - Check for JavaScript errors (F12 console)

4. **Application Logs**
   - Check for `=== GRIFFIN ADFS REDIRECT DEBUG ===` in logs
   - Look for any error messages
   - Verify URLs logged match configuration

---

## 🎓 Understanding The 404 Error

The diagnostic specifically helps diagnose the **"404 after clicking Login with ADFS"** error.

### How The Error Happens

1. **Correct Flow**:
   ```
   User clicks "Login with ADFS"
   → Redirects to: https://7108dev.d8200.mil/authentication?tokenConsumerURL=...
   → User sees Griffin login page
   ```

2. **Broken Flow** (Missing Scheme):
   ```
   User clicks "Login with ADFS"
   → BaseUrl = "7108dev.d8200.mil" (no https://)
   → ASP.NET treats as relative path
   → Browser navigates to: http://localhost:5000/7108dev.d8200.mil/authentication/...
   → 404 Not Found
   ```

### The Diagnostic Catches This

- **Check 2**: Detects missing scheme in BaseUrl
- **Check 5**: Validates generated URL is absolute
- **Root Cause**: Explains exactly why it causes 404
- **Fix Instructions**: Step-by-step resolution

---

## 🚀 Future Enhancements

Potential additions to the diagnostic (not yet implemented):

- [ ] Test callback URL accessibility from external machine
- [ ] Validate SSL certificate (if using HTTPS)
- [ ] Check Griffin server version compatibility
- [ ] Test token exchange flow
- [ ] Validate user provisioning configuration
- [ ] Check audit log for recent errors
- [ ] Performance benchmarking
- [ ] Configuration history comparison

---

## 📝 Version History

### v2.0 (2026-01-07)
- ✅ Added 7 comprehensive validation checks
- ✅ Summary cards with validation score
- ✅ Warnings section for common mistakes
- ✅ Real-time server connectivity test
- ✅ Callback page existence verification
- ✅ Configuration source detection
- ✅ Enhanced UI with color-coded results
- ✅ JSON export for support tickets
- ✅ Print-friendly report generation

### v1.0 (2026-01-06)
- Basic configuration display
- URL scheme validation
- Root cause analysis
- Fix instructions

---

**Tool Location:** `http://localhost:5000/GriffinDiagnostic`
**Access From:** Purple button in `/Owner/GriffinConfig`
**Authorization:** No auth required (diagnostic only)
**Purpose:** Troubleshoot Griffin ADFS authentication issues

🤖 Generated with [Claude Code](https://claude.com/claude-code)
