# Language Management Feature - Testing Guide

**Feature Version:** 1.0
**Test Date:** 2026-01-05
**Status:** Ready for Testing

## Overview

This guide provides comprehensive test scenarios for the Language Management feature, which enables:
- Company-scoped translation overrides
- In-app click-to-edit workflow for translations
- Two-language toggle per company (default + alternate)
- Company default language sets initial preference for new users

---

## Prerequisites

### Database
- ✅ Migration `20260105003950_AddLanguageManagementTables` applied
- ✅ Tables created: `CompanyLanguageSettings`, `CompanyLocalizationOverrides`

### User Accounts
- **Owner account** required for all tests
- Must belong to a company (CompanyId > 0)
- Owner role permissions verified

### Initial State
```bash
# Verify tables exist
sqlite3 app.db "SELECT name FROM sqlite_master WHERE type='table' AND (name='CompanyLanguageSettings' OR name='CompanyLocalizationOverrides');"

# Expected output:
# CompanyLanguageSettings
# CompanyLocalizationOverrides
```

---

## Test Scenarios

### **Phase 1: Owner Language Management Page**

#### Test 1.1: Access Language Management Page
**Steps:**
1. Log in as Owner
2. Navigate to `/Owner/Index` (Owner Admin Panel)
3. Locate "Language Management" card (🌐 icon)
4. Click to navigate to `/Owner/LanguageManagement`

**Expected Results:**
- ✅ Page loads successfully
- ✅ Page title: "Language Management"
- ✅ Breadcrumb shows: Admin Panel > Language Management
- ✅ Company selector dropdown visible (if Owner manages multiple companies)
- ✅ Four main sections visible:
  1. Company Language Settings card
  2. In-App Translation Editor card (blue gradient)
  3. Translation Overrides table
  4. Add Translation Override form

#### Test 1.2: Configure Company Language Settings
**Steps:**
1. On Language Management page, locate "Company Language Settings" section
2. Select **Default Language**: `en-US`
3. Select **Alternate Language**: `he-IL`
4. Click **"💾 Save Language Settings"**

**Expected Results:**
- ✅ Success message: "Language settings saved successfully"
- ✅ Page reloads with saved settings displayed
- ✅ Database record created in `CompanyLanguageSettings`:
  ```sql
  SELECT * FROM CompanyLanguageSettings WHERE CompanyId = [YOUR_COMPANY_ID];
  ```

**Test Data Variations:**
- Default: `he-IL`, Alternate: `en-US` (reversed)
- Try same language for both (should fail with error)

#### Test 1.3: Add Translation Override
**Steps:**
1. Scroll to "Add Translation Override" section
2. Fill in:
   - **Resource Key**: `Dashboard_UpcomingShifts`
   - **Culture**: `he-IL`
   - **Override Value**: `משמרות קרובות (מותאם אישית)`
3. Click **"💾 Add Override"**

**Expected Results:**
- ✅ Success message: "Override for 'Dashboard_UpcomingShifts' saved successfully"
- ✅ New row appears in "Translation Overrides" table
- ✅ Columns show: Resource Key, Culture (badge), Base Value, Override Value, Last Updated, Actions
- ✅ Database record created:
  ```sql
  SELECT * FROM CompanyLocalizationOverrides WHERE ResourceKey = 'Dashboard_UpcomingShifts';
  ```

#### Test 1.4: Search and Filter Overrides
**Steps:**
1. Add multiple overrides (at least 5, mix of en-US and he-IL)
2. Use search box: Enter "Dashboard"
3. Verify only Dashboard-related keys appear
4. Use culture filter: Select "Hebrew"
5. Verify only he-IL overrides appear
6. Click "Clear filters" link

**Expected Results:**
- ✅ Search filters results dynamically
- ✅ Culture filter works correctly
- ✅ Clear filters resets to full list

#### Test 1.5: Delete Translation Override
**Steps:**
1. Locate an override in the table
2. Click **🗑️** (delete button)
3. Confirm deletion in browser prompt
4. Verify override is removed from table

**Expected Results:**
- ✅ Confirmation prompt appears
- ✅ Override deleted from UI
- ✅ Database record removed (IsActive = 0 or deleted)

---

### **Phase 2: Language Toggle Integration**

#### Test 2.1: Language Toggle with Company Settings
**Steps:**
1. As Owner, configure company settings: Default = `en-US`, Alternate = `he-IL`
2. Navigate to dashboard (`/Index`)
3. Click language toggle button (🌐 icon in header)
4. Observe page reload in Hebrew
5. Click toggle again
6. Observe page reload in English

**Expected Results:**
- ✅ Toggle switches between company-configured languages only
- ✅ Does NOT switch to other languages (e.g., Spanish)
- ✅ Cookie `.AspNetCore.Culture` set correctly
- ✅ Page direction changes (LTR ↔ RTL)

#### Test 2.2: New User Language Preference
**Steps:**
1. Configure company: Default = `he-IL`, Alternate = `en-US`
2. Create new user account for this company
3. Log in as new user (no culture cookie)
4. Observe initial language

**Expected Results:**
- ✅ New user sees company default language (`he-IL`)
- ✅ Can toggle to alternate (`en-US`)
- ✅ Preference saved in cookie for subsequent visits

---

### **Phase 3: Tag Helper (`<loc>` Tag)**

#### Test 3.1: Tag Helper Rendering
**Steps:**
1. Navigate to `/Index` (Dashboard)
2. View page source (Ctrl+U)
3. Search for `data-loc-key="Dashboard_UpcomingShifts"`
4. Verify `<span>` element structure

**Expected Results:**
- ✅ HTML output: `<span data-loc-key="Dashboard_UpcomingShifts">Upcoming Shifts</span>`
- ✅ Text content matches base localization (or override if exists)
- ✅ No edit mode class in normal mode

#### Test 3.2: Tag Helper with Company Override
**Steps:**
1. Add override: Key = `Dashboard_UpcomingShifts`, Culture = `en-US`, Value = `Custom Shifts`
2. Refresh dashboard in English
3. Verify custom text appears

**Expected Results:**
- ✅ Text shows "Custom Shifts" instead of "Upcoming Shifts"
- ✅ Other pages using same key show override
- ✅ Cache working (no database query on subsequent loads)

---

### **Phase 4: Edit Mode Workflow**

#### Test 4.1: Enter Edit Mode
**Steps:**
1. Navigate to `/Owner/LanguageManagement`
2. Locate "In-App Translation Editor" card (blue gradient)
3. Select culture: `he-IL`
4. Click **"🚀 Enter Edit Mode"**

**Expected Results:**
- ✅ Redirects to `/Index` (Dashboard)
- ✅ Blue banner appears at top: "✏️ Edit Mode ON | Company: X | Culture: he-IL | Drafts: 0"
- ✅ Cookies set:
  - `language_edit_mode=true`
  - `language_edit_companyId=X`
  - `language_edit_culture=he-IL`
- ✅ Edit mode CSS and JS loaded
- ✅ All text with `data-loc-key` has yellow hover outline

#### Test 4.2: Click to Edit Translation
**Steps:**
1. In edit mode, hover over any localized text (e.g., "Upcoming Shifts")
2. Observe yellow outline on hover
3. Click the text
4. Modal opens with editor

**Expected Results:**
- ✅ Modal title: "✏️ Edit Translation"
- ✅ Read-only fields show: Resource Key, Current Value
- ✅ Editable textarea shows current text
- ✅ Textarea is focused and cursor at end
- ✅ Buttons: Cancel, 💾 Save Draft

#### Test 4.3: Save Draft
**Steps:**
1. In editor modal, change text to: "משמרות קרובות - טיוטה"
2. Click **"💾 Save Draft"**
3. Observe modal closes
4. Observe draft count in banner updates to "Drafts: 1"
5. Observe text on page updates immediately with green dotted underline

**Expected Results:**
- ✅ Modal closes
- ✅ Banner shows "Drafts: 1"
- ✅ Text element shows new value
- ✅ Element has `has-draft` class (green underline)
- ✅ Draft stored in sessionStorage: `language-override-drafts:{companyId}:{culture}`

#### Test 4.4: Draft Persistence Across Pages
**Steps:**
1. With 1+ drafts, navigate to another page (e.g., `/Calendar`)
2. Observe banner still shows draft count
3. Observe drafted changes still visible on new page (if key used there)
4. Return to `/Index`
5. Verify drafts still present

**Expected Results:**
- ✅ Drafts persist across navigation (sessionStorage)
- ✅ Banner remains visible on all pages
- ✅ Draft count accurate
- ✅ Drafted text shown with green underline

#### Test 4.5: View All Drafts
**Steps:**
1. Create 3-5 drafts on different keys
2. Click **"📋 View Drafts"** in banner
3. Modal opens showing all drafts

**Expected Results:**
- ✅ Modal title: "📋 Draft Changes (X)"
- ✅ Each draft shows: Resource Key, Draft Value
- ✅ Close button works

#### Test 4.6: Save & Exit
**Steps:**
1. Create 3+ drafts
2. Click **"💾 Save & Exit"** in banner
3. Confirm save prompt: "Save X draft change(s) to the database?"
4. Click OK

**Expected Results:**
- ✅ POST request to `/Owner/LanguageManagement?handler=ApiSaveDrafts`
- ✅ Request body contains: `{ companyId, culture, drafts: { ... } }`
- ✅ Success alert: "Successfully saved X translation(s)!"
- ✅ Edit mode cookies cleared
- ✅ Redirects to `/Owner/LanguageManagement`
- ✅ Overrides table shows new entries
- ✅ Database has new records:
  ```sql
  SELECT * FROM CompanyLocalizationOverrides ORDER BY UpdatedAt DESC LIMIT 10;
  ```

#### Test 4.7: Discard Drafts
**Steps:**
1. Create 2+ drafts
2. Click **"🗑️ Discard"** in banner
3. Confirm discard prompt: "Discard X draft change(s) without saving?"
4. Click OK

**Expected Results:**
- ✅ Confirmation prompt appears
- ✅ Page reloads
- ✅ Edit mode cookies cleared
- ✅ Banner disappears
- ✅ No changes saved to database
- ✅ Page shows original text (no drafts)

---

### **Phase 5: Security & Validation**

#### Test 5.1: Multi-Tenant Isolation
**Steps:**
1. As Owner of Company A, add override: Key = `Test`, Value = `Company A Value`
2. Switch to Company B (if Owner manages multiple)
3. Verify override NOT visible in Company B
4. Add override for Company B: Same key, different value
5. Verify both companies have separate overrides

**Expected Results:**
- ✅ Overrides scoped to company
- ✅ Company A cannot see/edit Company B overrides
- ✅ Database query filters: `WHERE CompanyId = X`

#### Test 5.2: XSS Prevention
**Steps:**
1. Attempt to add override with value: `<script>alert('XSS')</script>`
2. Save and view on page
3. Verify script does NOT execute

**Expected Results:**
- ✅ Value is HTML-encoded in database
- ✅ Rendered as plain text: `&lt;script&gt;alert('XSS')&lt;/script&gt;`
- ✅ No JavaScript execution

#### Test 5.3: Placeholder Preservation
**Steps:**
1. Find a parameterized string (e.g., `Welcome_Message` with `{0}`)
2. Add override WITHOUT preserving placeholder: `Welcome User`
3. Attempt to save

**Expected Results:**
- ✅ Validation error: "Override must preserve placeholders {0}"
- ✅ Override not saved
- ✅ User prompted to correct

#### Test 5.4: Unauthorized Access
**Steps:**
1. Log in as non-Owner user (Manager, Employee)
2. Attempt to navigate to `/Owner/LanguageManagement`

**Expected Results:**
- ✅ Access denied (403) or redirect to `/AccessDenied`
- ✅ Cannot access edit mode
- ✅ Cannot view Owner pages

---

### **Phase 6: Performance & Caching**

#### Test 6.1: Cache Hit Rate
**Steps:**
1. Add 50+ overrides for a company
2. Navigate to dashboard
3. Check logs for cache hits
4. Refresh page 5 times
5. Verify no additional database queries after first load

**Expected Results:**
- ✅ First load: Database query to load overrides
- ✅ Subsequent loads: Cache hit (no database query)
- ✅ Cache key format: `localization:{companyId}:{culture}`
- ✅ Cache TTL: 1 hour

#### Test 6.2: Cache Invalidation
**Steps:**
1. With overrides cached, add a new override via Owner page
2. Immediately navigate to dashboard
3. Verify new override is visible (cache invalidated)

**Expected Results:**
- ✅ New override appears immediately
- ✅ Cache invalidated on save: `InvalidateCache(companyId, culture)`
- ✅ Fresh data loaded from database

---

### **Phase 7: RTL Compatibility**

#### Test 7.1: Hebrew UI Rendering
**Steps:**
1. Switch to Hebrew (`he-IL`)
2. Verify page direction: RTL
3. Enter edit mode in Hebrew
4. Verify banner, modal, all UI elements are RTL-aligned

**Expected Results:**
- ✅ Page direction: `dir="rtl"`
- ✅ Text alignment: right-to-left
- ✅ Banner buttons: right-to-left order
- ✅ Modal: text fields RTL
- ✅ No layout breaks

#### Test 7.2: Mixed Language Content
**Steps:**
1. Add Hebrew override for English page
2. Verify Hebrew text displays correctly in LTR context
3. Add English override for Hebrew page
4. Verify English text displays correctly in RTL context

**Expected Results:**
- ✅ Mixed text renders correctly
- ✅ No direction conflicts
- ✅ `<span>` wrapper does not break layout

---

## Edge Cases & Error Scenarios

### Edge 1: Empty Database (No Settings)
**Test:**
1. Company with no `CompanyLanguageSettings` record
2. Navigate to dashboard

**Expected:**
- ✅ Defaults used: en-US (default), he-IL (alternate)
- ✅ No errors
- ✅ Language toggle works with defaults

### Edge 2: Invalid Resource Key
**Test:**
1. Use tag helper with non-existent key: `<loc key="NonExistent_Key" />`

**Expected:**
- ✅ Renders key as fallback: `[NonExistent_Key]`
- ✅ No crash
- ✅ Logged as warning

### Edge 3: SessionStorage Full
**Test:**
1. Create 500+ drafts (simulate full sessionStorage)

**Expected:**
- ✅ Warning if draft count > 500
- ✅ Graceful degradation
- ✅ User prompted to save

### Edge 4: Concurrent Edit Mode (Multi-Tab)
**Test:**
1. Open edit mode in Tab A
2. Open edit mode in Tab B (same company, different culture)

**Expected:**
- ✅ Each tab has independent sessionStorage
- ✅ Warning if detected
- ✅ Last save wins (no corruption)

### Edge 5: Network Error on Save
**Test:**
1. Create drafts
2. Disconnect network
3. Click "Save & Exit"

**Expected:**
- ✅ Error alert: "Failed to save drafts: [error]"
- ✅ Drafts remain in sessionStorage
- ✅ User can retry

---

## Verification Checklist

After completing all tests, verify:

- [ ] All database tables created and populated
- [ ] Owner can configure language settings
- [ ] Owner can add/edit/delete overrides
- [ ] Language toggle uses company settings
- [ ] Tag helper renders correctly
- [ ] Edit mode workflow complete (enter → edit → save → exit)
- [ ] Drafts persist across navigation
- [ ] Multi-tenant isolation enforced
- [ ] XSS prevention working
- [ ] Placeholder preservation enforced
- [ ] Caching working (no unnecessary DB queries)
- [ ] RTL mode works correctly
- [ ] No console errors in browser
- [ ] No server errors in logs

---

## Rollback Plan

If critical issues found:

1. **Disable Edit Mode:**
   ```csharp
   // In _Layout.cshtml, comment out:
   // @await Component.InvokeAsync("LanguageEditModeBanner")
   // <script src="~/js/language-edit-mode.js"></script>
   ```

2. **Revert Language Toggle:**
   ```bash
   git revert [commit-hash-of-language-toggle-changes]
   ```

3. **Remove Database Tables:**
   ```sql
   DROP TABLE CompanyLocalizationOverrides;
   DROP TABLE CompanyLanguageSettings;
   ```

4. **Unregister Services:**
   ```csharp
   // In Program.cs, remove:
   // builder.Services.AddScoped<ICompanyLocalizationService, CompanyLocalizationService>();
   // builder.Services.AddScoped<ILanguageManagementService, LanguageManagementService>();
   ```

---

## Success Criteria

Feature is ready for production when:
- ✅ All test scenarios pass
- ✅ No critical bugs found
- ✅ Performance acceptable (< 200ms page load overhead)
- ✅ Security review passed
- ✅ Owner documentation complete
- ✅ Developer documentation complete

---

## Known Limitations

1. **Tag Helper Migration:** Pages using `@Localizer[...]` need incremental migration to `<loc>` tag
2. **SessionStorage:** Drafts lost if browser tab closed
3. **Async Query:** Language toggle requires database query (cached after first load)

---

## Support & Troubleshooting

### Common Issues

**Issue:** Overrides not appearing on page
**Solution:** Check cache invalidation, verify company ID matches, check culture spelling

**Issue:** Edit mode banner not showing
**Solution:** Verify cookies set, check browser console for JavaScript errors

**Issue:** Save & Exit fails with 401
**Solution:** Check ApiAuthenticationMiddleware whitelist (should NOT be needed - uses cookie auth)

**Issue:** Hebrew text not RTL
**Solution:** Verify `dir="rtl"` in `<html>` tag, check `rtl.css` loaded

---

**Testing Complete!**
Report issues to: [Owner/Development Team]
