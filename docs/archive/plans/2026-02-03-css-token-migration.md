# CSS Token Migration Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Migrate all 33 remaining files from undefined CSS tokens (`--text-primary`, `--text-secondary`) to the correct tokens defined in tokens.css (`--text`, `--text-muted`).

**Architecture:** Mechanical search-and-replace operation across all .cshtml files. Use the pre-commit CSS audit script to verify completion.

**Tech Stack:** CSS, Razor Pages, existing design token system (tokens.css v3.0)

---

## Background

The design system defines these tokens in `wwwroot/css/tokens.css`:
- `--text` - Primary text color
- `--text-muted` - Secondary/muted text color

However, 33 files still use the undefined tokens:
- `--text-primary` → should be `--text`
- `--text-secondary` → should be `--text-muted`

---

## Files to Migrate

Based on grep results, these 33 files need updating:

1. `Pages/Calendar/Table.cshtml`
2. `Pages/Admin/EditProfile.cshtml`
3. `Pages/Admin/Users.cshtml`
4. `Pages/Requests/Index.cshtml`
5. `Views/Shared/Components/OwnerCompanySelector/Default.cshtml`
6. `Pages/Schedule/Index.cshtml`
7. `Pages/Owner/Programs.cshtml`
8. `Pages/Owner/MasterPrograms.cshtml`
9. `Pages/Owner/GameConfig.cshtml`
10. `Pages/Owner/Blueprints.cshtml`
11. `Pages/My/Settings.cshtml`
12. `Pages/My/Requests.cshtml`
13. `Pages/My/Profile.cshtml`
14. `Pages/Friends/Index.cshtml`
15. `Pages/Director/Index.cshtml`
16. `Pages/Admin/SetupTasks/Index.cshtml`
17. `Pages/Admin/Settings/Index.cshtml`
18. `Pages/Admin/Organization/ShiftGroupings/Index.cshtml`
19. `Pages/Admin/Organization/Roles/Index.cshtml`
20. `Pages/Admin/Organization/Roles/Assign.cshtml`
21. `Pages/Admin/Organization/Projects/Index.cshtml`
22. `Pages/Admin/Organization/Molecules/Index.cshtml`
23. `Pages/Admin/Organization/JobTypes/Index.cshtml`
24. `Pages/Admin/Organization/Index.cshtml`
25. `Pages/Admin/Organization/Grants/Index.cshtml`
26. `Pages/Admin/Organization/Grants/Assign.cshtml`
27. `Pages/Admin/Organization/Departments/Index.cshtml`
28. `Pages/Admin/Organization/Areas/Index.cshtml`
29. `Pages/Admin/Directors.cshtml`
30. `Pages/Admin/Config.cshtml`
31. `Pages/Admin/Companies.cshtml`
32. `Pages/Admin/AuditLog.cshtml`
33. `Pages/Admin/Analytics.cshtml`

---

## Task 1: Migrate Admin Pages (Batch 1)

**Files:**
- `Pages/Admin/Analytics.cshtml`
- `Pages/Admin/AuditLog.cshtml`
- `Pages/Admin/Companies.cshtml`
- `Pages/Admin/Config.cshtml`
- `Pages/Admin/Directors.cshtml`
- `Pages/Admin/EditProfile.cshtml`
- `Pages/Admin/Users.cshtml`

**Step 1: Replace tokens in each file**

For each file, replace:
```css
var(--text-primary)  →  var(--text)
var(--text-secondary)  →  var(--text-muted)
```

**Step 2: Verify no undefined tokens remain**

Run:
```powershell
Select-String -Path "Pages\Admin\*.cshtml" -Pattern "var\(--text-primary\)|var\(--text-secondary\)"
```
Expected: No matches

**Step 3: Build to verify no syntax errors**

```bash
dotnet build ShiftManager.sln --no-restore
```

**Step 4: Commit**

```bash
git add Pages/Admin/*.cshtml
git commit -m "fix: migrate CSS tokens in Admin pages (--text-primary → --text)"
```

---

## Task 2: Migrate Admin/Organization Pages

**Files:**
- `Pages/Admin/Organization/Index.cshtml`
- `Pages/Admin/Organization/Areas/Index.cshtml`
- `Pages/Admin/Organization/Departments/Index.cshtml`
- `Pages/Admin/Organization/Grants/Index.cshtml`
- `Pages/Admin/Organization/Grants/Assign.cshtml`
- `Pages/Admin/Organization/JobTypes/Index.cshtml`
- `Pages/Admin/Organization/Molecules/Index.cshtml`
- `Pages/Admin/Organization/Projects/Index.cshtml`
- `Pages/Admin/Organization/Roles/Index.cshtml`
- `Pages/Admin/Organization/Roles/Assign.cshtml`
- `Pages/Admin/Organization/ShiftGroupings/Index.cshtml`

**Step 1: Replace tokens in each file**

For each file, replace:
```css
var(--text-primary)  →  var(--text)
var(--text-secondary)  →  var(--text-muted)
```

**Step 2: Verify no undefined tokens remain**

```powershell
Select-String -Path "Pages\Admin\Organization\**\*.cshtml" -Pattern "var\(--text-primary\)|var\(--text-secondary\)" -Recurse
```
Expected: No matches

**Step 3: Commit**

```bash
git add Pages/Admin/Organization/
git commit -m "fix: migrate CSS tokens in Admin/Organization pages"
```

---

## Task 3: Migrate Admin/Settings and Admin/SetupTasks

**Files:**
- `Pages/Admin/Settings/Index.cshtml`
- `Pages/Admin/SetupTasks/Index.cshtml`

**Step 1: Replace tokens**

**Step 2: Verify and commit**

```bash
git add Pages/Admin/Settings/ Pages/Admin/SetupTasks/
git commit -m "fix: migrate CSS tokens in Admin/Settings and SetupTasks"
```

---

## Task 4: Migrate My/ Pages

**Files:**
- `Pages/My/Profile.cshtml`
- `Pages/My/Requests.cshtml`
- `Pages/My/Settings.cshtml`

**Step 1: Replace tokens**

**Step 2: Verify and commit**

```bash
git add Pages/My/
git commit -m "fix: migrate CSS tokens in My/ pages"
```

---

## Task 5: Migrate Owner/ Pages

**Files:**
- `Pages/Owner/Blueprints.cshtml`
- `Pages/Owner/GameConfig.cshtml`
- `Pages/Owner/MasterPrograms.cshtml`
- `Pages/Owner/Programs.cshtml`

**Step 1: Replace tokens**

**Step 2: Verify and commit**

```bash
git add Pages/Owner/
git commit -m "fix: migrate CSS tokens in Owner/ pages"
```

---

## Task 6: Migrate Remaining Pages

**Files:**
- `Pages/Calendar/Table.cshtml`
- `Pages/Director/Index.cshtml`
- `Pages/Friends/Index.cshtml`
- `Pages/Requests/Index.cshtml`
- `Pages/Schedule/Index.cshtml`
- `Views/Shared/Components/OwnerCompanySelector/Default.cshtml`

**Step 1: Replace tokens**

**Step 2: Verify and commit**

```bash
git add Pages/Calendar/Table.cshtml Pages/Director/Index.cshtml Pages/Friends/Index.cshtml Pages/Requests/Index.cshtml Pages/Schedule/Index.cshtml Views/Shared/Components/
git commit -m "fix: migrate CSS tokens in remaining pages"
```

---

## Task 7: Final Verification

**Step 1: Run full CSS audit**

```powershell
.\build\Test-BeforeCommit.ps1
```

Expected: CSS Audit step passes with "No undefined CSS tokens found"

**Step 2: Visual inspection**

Start the application and visually verify key pages:
- Dashboard
- Calendar
- Admin pages
- Profile page

Verify text colors render correctly in both light and dark modes.

**Step 3: Final commit (if any fixes needed)**

```bash
git add -A
git commit -m "fix: complete CSS token migration - all files use defined tokens"
```

---

## Verification Checklist

- [ ] All 33 files migrated
- [ ] `Test-BeforeCommit.ps1` CSS audit passes
- [ ] Application builds without errors
- [ ] Visual inspection confirms correct text colors
- [ ] Light mode looks correct
- [ ] Dark mode looks correct

---

## Estimated Effort: 2-4 hours

This is a mechanical task. Can be automated with a PowerShell script:

```powershell
# Automated migration script
$files = Get-ChildItem -Path "Pages","Views" -Recurse -Include "*.cshtml"
foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw
    if ($content -match 'var\(--text-primary\)|var\(--text-secondary\)') {
        $newContent = $content -replace 'var\(--text-primary\)', 'var(--text)'
        $newContent = $newContent -replace 'var\(--text-secondary\)', 'var(--text-muted)'
        Set-Content -Path $file.FullName -Value $newContent -NoNewline
        Write-Host "Updated: $($file.FullName)"
    }
}
```

