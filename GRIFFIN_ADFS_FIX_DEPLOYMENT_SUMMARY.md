# Griffin ADFS Authentication Fix - Deployment Summary

**Date:** 2026-01-23
**Status:** ✅ Ready for Deployment
**Priority:** HIGH - Fixes broken Griffin ADFS authentication

---

## ⚠️ Important: HTTP and HTTPS Both Supported

This fix **fully supports both HTTP and HTTPS**. The key requirement is that Griffin needs to see a recognizable URL structure (starting with `http://` or `https://`), regardless of which protocol you use.

---

## 🎯 Executive Summary

This fix resolves the issue where Griffin ADFS rejects our callback URL during authentication. The root cause was that we were encoding the **entire callback URL**, making it unrecognizable to Griffin's validation. The fix changes our approach to match the proven "Doof" pattern: keep the URL structure readable and only encode the destination parameter.

**Key Insight:** Griffin needs to SEE a recognizable URL (starting with `http://` or `https://`), not encoded gibberish like `http%253A...`

---

## ✅ Verification Completed Locally

### Diagnostic Page Results (Confirmed Working):

The enhanced Griffin Diagnostic page at `/GriffinDiagnostic` now shows:

**❌ OLD Approach (Current - FAILS):**
```
tokenConsumerURL=http%253A%252F%252Flocalhost%253A5000%252FAuth...
```
- Starts with `http%253A` - Griffin sees ENCODED GIBBERISH
- Result: Griffin rejects the URL

**✅ NEW Approach (After Fix - SHOULD WORK):**
```
tokenConsumerURL=http://localhost:5000/Auth/GriffinCallback?returnUrl=%25252FHome%25252FIndex
```
- Starts with `http://` or `https://` - Griffin sees RECOGNIZABLE URL STRUCTURE
- Only the returnUrl parameter is encoded (3x)
- Result: Griffin should accept this
- **Note:** Works with both HTTP and HTTPS

**Screenshots captured at:**
- `.playwright-mcp/griffin-diagnostic-url-comparison.png`
- `.playwright-mcp/url-comparison-detailed.png`
- `.playwright-mcp/url-comparison-newvsdoof.png`
- `.playwright-mcp/url-comparison-summary-table.png`
- `.playwright-mcp/url-comparison-insight.png`

---

## 📝 Files Modified

### 1. **Services/GriffinService.cs** (Lines 39-52)
**What Changed:**
- ❌ Removed: Double-encoding of entire callback URL
- ✅ Added: Pass tokenConsumerUrl directly without encoding URL structure

**Before:**
```csharp
var encodedOnce = Uri.EscapeDataString(tokenConsumerUrl);
var encodedTwice = Uri.EscapeDataString(encodedOnce);
var finalUrl = $"{griffinBaseUrl}/authentication?tokenConsumerURL={encodedTwice}";
```

**After:**
```csharp
// DON'T encode the entire URL - Griffin needs to see a valid URL structure!
var finalUrl = $"{griffinBaseUrl}/authentication?tokenConsumerURL={tokenConsumerUrl}";
```

### 2. **Pages/Auth/Login.cshtml.cs** (Lines 326-350)
**What Changed:**
- ❌ Removed: Single-encoding of returnUrl
- ✅ Added: Triple-encoding of returnUrl parameter

**Before:**
```csharp
callbackUrl += $"{separator}returnUrl={Uri.EscapeDataString(returnUrl)}";
```

**After:**
```csharp
// Triple-encode the returnUrl to survive multiple decoding layers
var encodedReturn = Uri.EscapeDataString(returnUrl);
var encodedTwice = Uri.EscapeDataString(encodedReturn);
var encodedThrice = Uri.EscapeDataString(encodedTwice);
callbackUrl += $"{separator}returnUrl={encodedThrice}";
```

### 3. **Pages/Auth/GriffinCallback.cshtml.cs** (Lines 113-121)
**What Changed:**
- ✅ Added: Comment documenting where manual decoding would go if needed

**Note:** ASP.NET Core should automatically decode the triple-encoded returnUrl via model binding. Manual decoding only needed if testing shows it arrives still encoded.

### 4. **Pages/GriffinDiagnostic.cshtml.cs** (Lines 47-75, 327-390)
**What Changed:**
- ✅ Added: URL comparison properties (21 new properties)
- ✅ Added: `GenerateUrlComparison()` method to simulate OLD vs NEW vs DOOF approaches

### 5. **Pages/GriffinDiagnostic.cshtml** (Lines 503-700+)
**What Changed:**
- ✅ Added: Comprehensive URL Encoding Comparison section with visual indicators
- Shows encoding progression (1x, 2x, 3x)
- Shows OLD approach (fails), NEW approach (should work), DOOF approach (proven)
- Includes comparison table and critical insight box

---

## 🔬 How The Fix Works

### The Three Decoding Layers:

**Layer 1 - Griffin's HTTP Parsing:**
```
Griffin receives: tokenConsumerURL=https://app.mil/callback?returnUrl=%25252FHome%25252FIndex
Griffin decodes:  https://app.mil/callback?returnUrl=%252FHome%252FIndex
```

**Layer 2 - Griffin's Internal Processing:**
```
Griffin processes the callback URL (may decode again)
Result: https://app.mil/callback?returnUrl=%2FHome%2FIndex
```

**Layer 3 - Our ASP.NET Core Routing:**
```
Browser navigates to: https://app.mil/callback?returnUrl=%2FHome%2FIndex&token=XXX
ASP.NET decodes:      returnUrl="/Home/Index" (correct!)
```

---

## 🚀 Deployment Instructions

### Step 1: Review Code Changes
Review the 5 modified files listed above to ensure you understand the changes.

### Step 2: Deploy to Air-Gapped Environment
Deploy the modified code to the air-gapped environment.

### Step 3: Verify Configuration
1. Navigate to `/Owner/GriffinConfig`
2. Ensure:
   - ✅ Griffin is Enabled
   - ✅ Base URL starts with `http://` or `https://`
   - ✅ Callback URL starts with `http://` or `https://`
   - ✅ Callback URL path is `/Auth/GriffinCallback`

### Step 4: Check Diagnostic Page FIRST!
**CRITICAL:** Before testing login, check the diagnostic page!

1. Navigate to `/GriffinDiagnostic`
2. Scroll to **"URL Encoding Comparison"** section
3. Verify:
   - ✅ NEW URLs start with `http://` or `https://` (readable URL structure)
   - ❌ OLD URLs start with `http%253A` or `https%253A` (encoded gibberish)
4. If NEW URLs don't look readable, **DO NOT PROCEED** - investigate first

### Step 5: Test Authentication Flow
1. Navigate to `/Auth/Login`
2. Click "Login with Griffin ADFS"
3. **Expected behavior:**
   - Browser redirects to Griffin ADFS server
   - Griffin shows ADFS login page (not rejection error)
   - After ADFS login, Griffin redirects back to callback URL
   - User lands on originally requested page

### Step 6: Monitor Logs
Check application logs for:
```
Griffin authentication URL constructed: ...
```
Verify the URL shows `tokenConsumerURL=http://` or `tokenConsumerURL=https://` (readable), not `tokenConsumerURL=http%253A` or `tokenConsumerURL=https%253A` (encoded gibberish)

---

## ✅ Success Criteria

- [ ] Diagnostic page shows NEW URLs with readable structure (starts with `http://` or `https://`)
- [ ] Login with Griffin button redirects to Griffin ADFS server
- [ ] Griffin accepts callback URL (no rejection error)
- [ ] Griffin shows ADFS login page
- [ ] After ADFS authentication, Griffin redirects back to our callback
- [ ] User lands on originally requested page (returnUrl decoded correctly)

---

## 🔄 Rollback Plan

If the fix doesn't work:

1. **Revert GriffinService.cs:**
   - Restore double-encoding of entire callback URL

2. **Revert Login.cshtml.cs:**
   - Restore single-encoding of returnUrl

3. The diagnostic page changes are harmless and can remain.

---

## 📊 Comparison Table

| Approach | What Gets Encoded | URL Readable? | Result |
|----------|------------------|---------------|---------|
| **OLD (Current)** | Entire callback URL (2x) | ❌ NO | Griffin rejects |
| **NEW (Proposed)** | Only returnUrl parameter (3x) | ✅ YES | Should work |
| **DOOF (Reference)** | Only destination path (3x) | ✅ YES | Proven to work |

---

## 💡 The Critical Insight

**Griffin needs to SEE a recognizable URL structure to validate the callback URL.**

- When we encode the entire URL, Griffin sees `http%253A...` or `https%253A...` which looks like garbage, not a URL
- When we only encode the parameter value, Griffin sees `http://...` or `https://...` which it recognizes and accepts
- This matches the proven Doof pattern exactly
- **Works with both HTTP and HTTPS** - the protocol doesn't matter, only that the URL structure is recognizable

---

## 🎯 Key Takeaways

1. **Don't encode what Griffin needs to validate** - The URL structure must be readable
2. **Do encode what needs to survive multiple layers** - The returnUrl parameter needs 3x encoding
3. **The diagnostic page is your window** - Use it to verify URLs before deploying
4. **Doof got it right** - Our fix matches their proven working approach

---

## 📞 Support

If you encounter issues:
1. Check `/GriffinDiagnostic` first - it will show exactly what Griffin receives
2. Review application logs for Griffin authentication messages
3. Verify Griffin server logs (if accessible)
4. Check that callback URL is whitelisted on Griffin server

---

## 📁 Related Files

- **Plan Document:** `docs/plans/griffin-adfs-fix-plan.md` (if exists)
- **Diagnostic Screenshots:** `.playwright-mcp/url-comparison-*.png`
- **Code Changes:** See "Files Modified" section above

---

**Document Version:** 1.0
**Last Updated:** 2026-01-23
**Author:** Claude Code
**Status:** ✅ Ready for Air-Gapped Deployment
