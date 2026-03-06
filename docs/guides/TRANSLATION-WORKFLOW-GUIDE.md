# Translation Workflow Guide

## Adding Translations for English-Only Keys

### Overview

When a resource key exists in English (`SharedResources.resx`) but **not** in the Hebrew (or alternate language) `.resx` file, you can still create custom translations using the **Language Management** feature.

### How It Works

The localization system has a **fallback hierarchy**:

```
1. Company Override (if exists) → Use this
2. Culture .resx file (if key exists) → Use this
3. Base English .resx file → Fallback to this
```

**Key Point:** Even if a key doesn't exist in `SharedResources.he-IL.resx`, you can still add a Hebrew override via the Language Management UI, and it will work!

---

## Workflow: Adding Missing Hebrew Translations

### Method 1: Via Override Management (Manual)

1. **Navigate to Language Management**
   ```
   /Owner/LanguageManagement
   ```

2. **Select your company** from the dropdown

3. **Scroll to "Add Translation Override"** section

4. **Fill in the form:**
   - **Resource Key**: The English key (e.g., `Button_Save`)
   - **Culture**: Select `he-IL` (or your target language)
   - **Override Value**: Your custom Hebrew translation (e.g., `שמור`)

5. **Click "Add Override"**

6. **Test**: Navigate to any page that uses this key and switch to Hebrew → Your override will appear

**Example:**
```
Resource Key: Dashboard_Welcome
Culture: he-IL
Override Value: ברוך הבא ללוח הבקרה
```

Even if `Dashboard_Welcome` doesn't exist in `SharedResources.he-IL.resx`, the override will work because the system falls back to the English value and then applies your override.

---

### Method 2: Via Edit Mode (In-App)

1. **Navigate to Language Management**
   ```
   /Owner/LanguageManagement
   ```

2. **Click "Enter Edit Mode"**

3. **Select culture**: `he-IL`

4. **Click "Start"** → You're redirected to the home page with edit mode active

5. **Enable "Freeze Interactions" toggle** (🔒 checkbox in banner)
   - This prevents buttons/links from triggering while you edit

6. **Navigate to the page** with the English text you want to translate

7. **Switch language to Hebrew** (if not already)
   - Text will appear in English (because it's missing from Hebrew .resx)

8. **Click on the English text**
   - Editor modal opens

9. **Enter your Hebrew translation** in the "Draft Value" field

10. **Click "Save Draft"**
    - Text immediately updates to show your translation
    - A green indicator appears

11. **Navigate to other pages** and repeat for other keys

12. **Click "Save & Exit"** in the banner when done
    - Confirms: "Save X draft changes?"
    - All overrides are saved to the database

13. **Test**: Reload the page → Your Hebrew translations persist

---

## Understanding the System

### What Happens Behind the Scenes

When you render a localized string like:

```cshtml
<loc key="Dashboard_Welcome" />
```

The `LocalizationTagHelper` does the following:

1. **Check for company override** in `CompanyLocalizationOverrides` table:
   ```sql
   SELECT OverrideValue
   FROM CompanyLocalizationOverrides
   WHERE CompanyId = ?
     AND Culture = 'he-IL'
     AND ResourceKey = 'Dashboard_Welcome'
   ```

2. **If override exists**: Use the override value ✅

3. **If no override**: Fallback to `IStringLocalizer["Dashboard_Welcome"]`
   - `IStringLocalizer` first checks `SharedResources.he-IL.resx`
   - If key doesn't exist there, it falls back to `SharedResources.resx` (English)

4. **Result**: Your override always takes priority, regardless of whether the key exists in the Hebrew .resx file

---

## Common Scenarios

### Scenario 1: Key exists in English, missing in Hebrew
**English .resx:**
```xml
<data name="Button_Submit">
  <value>Submit</value>
</data>
```

**Hebrew .resx:** *(missing)*

**Solution:** Add override via Language Management:
```
Resource Key: Button_Submit
Culture: he-IL
Override Value: שלח
```

**Result:** Hebrew users see "שלח", English users see "Submit"

---

### Scenario 2: Key exists in both languages, want custom terminology
**English .resx:**
```xml
<data name="Employee_Label">
  <value>Employee</value>
</data>
```

**Hebrew .resx:**
```xml
<data name="Employee_Label">
  <value>עובד</value>
</data>
```

**Your company calls them "חבר צוות" (team member) instead:**

**Solution:** Add override:
```
Resource Key: Employee_Label
Culture: he-IL
Override Value: חבר צוות
```

**Result:**
- Your company sees: "חבר צוות"
- Other companies (without override) see: "עובד"
- English users see: "Employee"

---

### Scenario 3: Parameterized strings with missing translations
**English .resx:**
```xml
<data name="Welcome_Message">
  <value>Welcome back, {0}!</value>
</data>
```

**Hebrew .resx:** *(missing)*

**Solution:** Add override with placeholders:
```
Resource Key: Welcome_Message
Culture: he-IL
Override Value: ברוך שובך, {0}!
```

⚠️ **Important**: The system validates that your override preserves all placeholders (`{0}`, `{1}`, etc.) from the base value.

---

## Adding New Languages

### Step 1: Create Resource File

If you want to support a new language (e.g., Spanish), create a `.resx` file:

```
Resources/SharedResources.es-ES.resx
```

**Copy from English base:**
```bash
cp Resources/SharedResources.resx Resources/SharedResources.es-ES.resx
```

**Translate all values** in the new file.

---

### Step 2: Update Program.cs

Add the culture to supported cultures:

```csharp
// Program.cs
var supportedCultures = new[] { "en-US", "he-IL", "es-ES" }; // Add es-ES
```

---

### Step 3: Restart Application

The Language Management page will automatically show the new culture in dropdowns.

---

### Step 4: Add Translations

You can now:
- Configure Spanish as **Default** or **Alternate** language for a company
- Add overrides for Spanish via Language Management
- Use Edit Mode to translate keys directly

---

## Best Practices

### 1. Use Edit Mode for Bulk Translation
- Faster than adding overrides manually
- See the text in context
- Draft mode lets you review before committing

### 2. Freeze Interactions When Editing Buttons
- Always enable "🔒 Freeze Interactions" toggle
- Prevents accidental button clicks while editing
- Uncheck to test button functionality

### 3. Search for Missing Translations
On the Language Management page:
1. **Filter by culture**: Select `he-IL`
2. **Search**: Leave empty to see all overrides
3. **Look for keys** that you know should have Hebrew translations
4. If missing, add them manually or via Edit Mode

### 4. Backup Before Bulk Editing
Overrides are stored in the database (`CompanyLocalizationOverrides` table). Before making large changes:
```bash
sqlite3 ShiftManager.db ".backup overrides_backup.db"
```

### 5. Test After Adding Translations
- Switch to the target language
- Navigate through the app
- Verify text appears correctly
- Check RTL layout (for Hebrew/Arabic)

---

## Troubleshooting

### Issue: Override doesn't appear
**Possible causes:**
1. Cache not invalidated → Wait 1 hour or restart app
2. Wrong culture selected → Verify culture code (case-sensitive)
3. Typo in resource key → Key must match exactly
4. Page not using `<loc>` tag → Check if page still uses `@Localizer` (override won't apply)

**Solution:**
- Clear browser cache
- Verify override exists in database:
  ```sql
  SELECT * FROM CompanyLocalizationOverrides
  WHERE CompanyId = ? AND ResourceKey = 'YourKey';
  ```

---

### Issue: Placeholder validation fails
**Error:** "Override value must preserve all placeholders"

**Cause:** Your translation is missing `{0}`, `{1}`, etc.

**English base:**
```
You have {0} new notifications
```

**❌ Wrong Hebrew override:**
```
יש לך הודעות חדשות (missing {0})
```

**✅ Correct Hebrew override:**
```
יש לך {0} הודעות חדשות
```

---

### Issue: Text still appears in English after adding Hebrew override
**Possible causes:**
1. User cookie is set to English → Check language toggle
2. Override was added for wrong company → Verify `SelectedCompanyId`
3. Key doesn't match → Verify exact key name (case-sensitive)
4. Page hasn't been migrated to `<loc>` → Some pages still use old `@Localizer` syntax

**Solution:**
- Switch language to Hebrew using toggle
- Verify override in database for YOUR company
- Check if page uses `<loc key="...">` or old `@Localizer["..."]` syntax

---

## Summary

**Key Takeaways:**

✅ You can add Hebrew (or any language) translations for English-only keys using overrides

✅ Overrides work even if the key doesn't exist in the Hebrew `.resx` file

✅ Two methods: Manual (Override Management) or In-App (Edit Mode)

✅ Edit Mode is faster for bulk translation and lets you see text in context

✅ Always enable "Freeze Interactions" when editing buttons/links

✅ Overrides are company-scoped (multi-tenant safe)

✅ To add new languages, create `.resx` file + update `Program.cs`

---

**Need Help?** Check:
- [11-LOCALIZATION-AND-RTL.md](./genesis/11-LOCALIZATION-AND-RTL.md) - Full localization documentation
- [Language Management Feature](#) - Genesis docs section

**Questions?** Open an issue in the repository.
