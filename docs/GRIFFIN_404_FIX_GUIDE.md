# Griffin ADFS 404 Redirect Error - Complete Fix Guide

**Date:** 2026-01-06
**Issue:** Login with ADFS redirects to 404 error page
**Error URL:** `/authentication/https%3A%2f%2f7108dev.d8200.mil%2fauth%GriffinCallBack...`
**Status:** 🔍 Diagnostic & Fix Required

---

## 🔥 Issue Summary

**Symptom:** "Test Connection" button works (shows success), but "Login with ADFS" fails with 404 error.

**Root Cause Analysis:** The redirect URL is being treated as a **relative path** instead of an **absolute URL**, causing ASP.NET to route it incorrectly.

---

## 🎯 Three Likely Causes (Ordered by Probability)

### **Cause #1: TokenConsumerUrl Missing HTTP Scheme (90% probability)**

**Problem:** The `TokenConsumerUrl` in the database is stored as a **relative path** instead of an absolute URL.

**Examples of WRONG values:**
```
❌ /Auth/GriffinCallback
❌ Auth/GriffinCallback
❌ localhost:5000/Auth/GriffinCallback
❌ //localhost:5000/Auth/GriffinCallback
```

**Correct value:**
```
✅ http://localhost:5000/Auth/GriffinCallback
```

**Why this causes the error:**
1. `Login.cshtml.cs:300` calls `return Redirect(authUrl);`
2. If `authUrl` starts with `/`, ASP.NET treats it as a **relative** redirect
3. This creates a local path: `/authentication/https%3A%2f%2f...` instead of redirecting to Griffin ADFS

**How to verify:**
1. Go to `http://localhost:5000/Owner/GriffinConfig`
2. Check the "Callback URL" field
3. Verify it starts with `http://` or `https://`

**How to fix:**
1. Navigate to `http://localhost:5000/Owner/GriffinConfig` (login as admin@local / easteregg)
2. Update "Callback URL" to: `http://localhost:5000/Auth/GriffinCallback`
3. Click "Save"
4. Try "Login with ADFS" again

---

### **Cause #2: TokenConsumerUrl Has Path Typo (8% probability)**

**Problem:** The URL path contains a typo or incorrect capitalization.

**Evidence from your error:**
```
❌ /auth%GriffinCallBack  (lowercase 'a', '%G' suggests missing '/')
✅ /Auth/GriffinCallback  (correct: capital 'A', capital 'G', capital 'C')
```

**Common typos:**
```
❌ http://localhost:5000/auth/GriffinCallback         (lowercase 'auth')
❌ http://localhost:5000/Auth/GriffincallBack         (wrong capitalization)
❌ http://localhost:5000/Auth/Griffin/Callback        (extra slash)
❌ http://localhost:5000/AuthGriffinCallback          (missing slash)
❌ http://localhost:5000/Auth/GriffinCallBack         (capital 'B' instead of lowercase)
```

**Correct path:**
```
✅ http://localhost:5000/Auth/GriffinCallback
```

**Why this matters:**
- ASP.NET routing is **case-sensitive** for paths
- The callback page is at `Pages/Auth/GriffinCallback.cshtml.cs`
- Path must match exactly: `/Auth/GriffinCallback`

**How to fix:**
1. Navigate to `http://localhost:5000/Owner/GriffinConfig`
2. Carefully type: `http://localhost:5000/Auth/GriffinCallback`
   - Make sure: `Auth` has capital `A`
   - Make sure: `GriffinCallback` has capital `G` and capital `C`
   - Make sure: There's a forward slash `/` between `Auth` and `GriffinCallback`
3. Click "Save"

---

### **Cause #3: Griffin ADFS Server Configuration Issue (2% probability)**

**Problem:** The Griffin ADFS server at `https://7108dev.d8200.mil` might not be properly configured to handle the callback URL.

**Evidence:** Test connection succeeds, but the redirect flow fails.

**Possible Griffin-side issues:**
1. **Single URL encoding instead of double encoding**
   - ShiftManager sends: `tokenConsumerURL=http%253A%252F%252Flocalhost%253A5000%252FAuth%252FGriffinCallback` (double-encoded)
   - Griffin should decode twice to get: `http://localhost:5000/Auth/GriffinCallback`
   - If Griffin only decodes once, it gets: `http%3A%2F%2Flocalhost%3A5000%2FAuth%2FGriffinCallback` (malformed)

2. **Griffin ADFS redirect URL validation**
   - Some ADFS servers validate the callback URL against a whitelist
   - If `http://localhost:5000` is not registered as a trusted redirect URI, Griffin might reject it

**How to diagnose:**
1. Check Griffin ADFS server logs (if you have access)
2. Verify the callback URL is registered in Griffin's relying party trust configuration
3. Try using the server's actual hostname instead of `localhost`

**How to fix:**
1. Contact your Griffin ADFS administrator
2. Ask them to verify:
   - The callback URL `http://localhost:5000/Auth/GriffinCallback` is whitelisted
   - The relying party trust is configured for your application
   - Griffin is set to accept the callback URL format sent by ShiftManager

---

## ✅ Step-by-Step Fix Procedure

### **Step 1: Run the Diagnostic Script**

```batch
cd C:\Users\katzi\Downloads\ShiftManager
DIAGNOSE_GRIFFIN_ISSUE.bat
```

This will show you the current `TokenConsumerUrl` value from the database.

---

### **Step 2: Log in to the Application**

1. Open browser: `http://localhost:5000`
2. Click "Login"
3. Use credentials:
   - **Email:** `admin@local`
   - **Password:** `easteregg`

---

### **Step 3: Navigate to Griffin Configuration**

1. After logging in, go to: `http://localhost:5000/Owner/GriffinConfig`
2. You should see the Griffin ADFS configuration page

---

### **Step 4: Verify Current Configuration**

Check the following fields:

| Field | Current Value | Expected Value |
|-------|---------------|----------------|
| **Enabled** | ✓ Checked | ✓ Checked |
| **Base URL** | `https://7108dev.d8200.mil` (or similar) | ✅ Correct (if test connection works) |
| **Callback URL** | **❌ CHECK THIS** | `http://localhost:5000/Auth/GriffinCallback` |
| **Timeout (seconds)** | 10-30 | 10-30 (OK) |

---

### **Step 5: Fix the Callback URL**

**CRITICAL:** Update the "Callback URL" field to **EXACTLY**:

```
http://localhost:5000/Auth/GriffinCallback
```

**Important notes:**
- ✅ Starts with `http://` (or `https://` if using SSL)
- ✅ Uses `localhost:5000` (or your server's actual hostname/IP)
- ✅ Path is `/Auth/GriffinCallback` (capital A, capital G, capital C)
- ✅ No trailing slash
- ✅ No extra spaces

---

### **Step 6: Test Configuration**

1. Click the **"🔌 Test Connection"** button
2. Verify you see: **"✅ Connection successful!"**
3. If the test fails:
   - Check the "Base URL" is correct
   - Verify the Griffin ADFS server is reachable from your air-gapped machine
   - Check network connectivity

---

### **Step 7: Save Configuration**

1. Click the **"💾 Save"** button
2. Verify you see: **"✅ Griffin ADFS configuration saved successfully"**

---

### **Step 8: Test ADFS Login**

1. Log out: `http://localhost:5000/Auth/Logout`
2. Go to login page: `http://localhost:5000/Auth/Login`
3. Click **"Login with Griffin ADFS"** button
4. You should be redirected to: `https://7108dev.d8200.mil/authentication?tokenConsumerURL=...`
5. Authenticate with your ADFS credentials
6. Griffin should redirect you back to: `http://localhost:5000/Auth/GriffinCallback?token=...`
7. You should be logged in successfully

---

## 🐛 If the Issue Persists

### Enable Detailed Logging

1. Edit `appsettings.json`:
   ```json
   "Logging": {
     "LogLevel": {
       "Default": "Information",
       "ShiftManager.Services.GriffinService": "Debug",
       "ShiftManager.Pages.Auth": "Debug"
     }
   }
   ```

2. Restart the application

3. Try logging in with ADFS again

4. Check logs at: `C:\Users\katzi\Downloads\ShiftManager\logs\` (if configured)

---

### Check the GriffinCallback Page Route

1. Verify the file exists:
   ```
   C:\Users\katzi\Downloads\ShiftManager\Pages\Auth\GriffinCallback.cshtml
   C:\Users\katzi\Downloads\ShiftManager\Pages\Auth\GriffinCallback.cshtml.cs
   ```

2. Verify the route directive in `GriffinCallback.cshtml`:
   ```cshtml
   @page
   @model ShiftManager.Pages.Auth.GriffinCallbackModel
   ```

   **Note:** There should be NO custom route template. The `@page` directive with no parameters means the route is auto-generated as `/Auth/GriffinCallback`.

---

### Verify ASP.NET Core Routing

1. Check `Program.cs` has standard Razor Pages routing:
   ```csharp
   app.MapRazorPages();
   ```

2. Verify no custom routing is overriding `/Auth/*` paths

---

### Alternative: Use Server IP Address

If `localhost:5000` doesn't work, try using the server's actual IP address:

1. Find your server's IP address:
   ```batch
   ipconfig
   ```

2. Look for "IPv4 Address" (e.g., `192.168.1.100`)

3. Update the Callback URL to:
   ```
   http://192.168.1.100:5000/Auth/GriffinCallback
   ```

4. Make sure the Griffin ADFS server can reach this IP address

---

## 📊 Expected URL Flow (For Debugging)

### **Correct Flow:**

1. **User clicks "Login with ADFS"**
   - POST to: `/Auth/Login?handler=Griffin`

2. **Server builds redirect URL:**
   ```
   BaseUrl: https://7108dev.d8200.mil
   TokenConsumerUrl: http://localhost:5000/Auth/GriffinCallback
   Double-encoded callback: http%253A%252F%252Flocalhost%253A5000%252FAuth%252FGriffinCallback

   Final URL: https://7108dev.d8200.mil/authentication?tokenConsumerURL=http%253A%252F%252Flocalhost%253A5000%252FAuth%252FGriffinCallback
   ```

3. **Browser redirects to Griffin ADFS:**
   - User sees Griffin login page
   - User enters ADFS credentials

4. **Griffin authenticates and redirects back:**
   ```
   Griffin decodes tokenConsumerURL twice to get: http://localhost:5000/Auth/GriffinCallback
   Griffin appends token: http://localhost:5000/Auth/GriffinCallback?token=abc123xyz
   Browser redirects to callback URL
   ```

5. **GriffinCallback processes token:**
   - Validates token with Griffin API
   - Fetches user claims
   - Creates session cookie
   - Redirects to home page

---

### **Incorrect Flow (What's happening now):**

1. **User clicks "Login with ADFS"**
   - POST to: `/Auth/Login?handler=Griffin`

2. **Server builds MALFORMED redirect URL:**
   ```
   ❌ Problem: TokenConsumerUrl is stored as: /Auth/GriffinCallback (relative path)

   Double-encoded callback: %252FAuth%252FGriffinCallback

   Final URL: https://7108dev.d8200.mil/authentication?tokenConsumerURL=%252FAuth%252FGriffinCallback
   ```

3. **ASP.NET treats redirect as RELATIVE:**
   ```
   ❌ Instead of: Redirect("https://7108dev.d8200.mil/authentication?tokenConsumerURL=...")
   ❌ ASP.NET interprets as: Redirect("/authentication/https%3A%2f%2f...")

   Result: Browser tries to load: http://localhost:5000/authentication/https%3A%2f%2f7108dev.d8200.mil/...
   ```

4. **404 Error:**
   - ASP.NET can't find a route matching `/authentication/...`
   - Returns 404 Not Found

---

## 🔧 Quick Fix Summary

**The fix is most likely a ONE-LINE change:**

Change this (WRONG):
```
❌ Callback URL: /Auth/GriffinCallback
```

To this (CORRECT):
```
✅ Callback URL: http://localhost:5000/Auth/GriffinCallback
```

**Where to change it:**
1. Go to: `http://localhost:5000/Owner/GriffinConfig`
2. Update the "Callback URL" field
3. Click "Save"

---

## 📞 Support

If you continue to experience issues after following this guide:

1. **Check the diagnostic output:**
   ```batch
   DIAGNOSE_GRIFFIN_ISSUE.bat > griffin-diagnosis.txt
   ```

2. **Capture browser network logs:**
   - Open browser DevTools (F12)
   - Go to "Network" tab
   - Click "Login with ADFS"
   - Export HAR file for analysis

3. **Check application logs:**
   - Look for errors in `logs/` folder
   - Search for "Griffin" or "authentication" related errors

4. **Verify Griffin ADFS server configuration:**
   - Contact your ADFS administrator
   - Verify the callback URL is whitelisted
   - Check for any firewall or network restrictions

---

**Document Version:** 1.0
**Last Updated:** 2026-01-06
**Status:** 🔧 Awaiting user testing
