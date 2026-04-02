# Session Handoff — ChoreType Bilingual Localization (Deferred Task #1)

---

## Goal
Add `NameEn`/`NameHe` bilingual fields to `ChoreType` so Hebrew and English users see the correct localized name. Currently `ChoreType.DisplayName` is a single untyped language string. Follow the `OnDutyTypeConfig` pattern.

---

## Current Status (verified 2026-04-01)
Zero code changed. `Models/ChoreType.cs` has no `NameEn` or `NameHe` fields. Greenfield.

---

## Steps (in order)

### Step 1 — Add fields to `Models/ChoreType.cs`
```csharp
public string? NameEn { get; set; }
public string? NameHe { get; set; }
```
Keep `DisplayName` — backward-compat fallback. Reference: `Models/OnDutyTypeConfig.cs` lines 30–35.

### Step 2 — EF migration
```
dotnet ef migrations add ChoreTypeBilingualNames
```
Add data migration in `Up()`: `migrationBuilder.Sql("UPDATE ChoreTypes SET NameEn = DisplayName WHERE NameEn IS NULL");`

### Step 3 — Update service interface + implementation
In `Services/IChoreTypeService.cs` and `Services/ChoreTypeService.cs`: add `string? nameEn, string? nameHe` params to `CreateAsync` and `UpdateAsync`. Set them on the entity.

### Step 4 — Update admin PageModel (`Pages/Admin/Organization/ChoreTypes/Index.cshtml.cs`)
- Add `[BindProperty] public string? ChoreTypeNameEn { get; set; }` and `ChoreTypeNameHe`
- Add `[BindProperty] public string? EditNameEn { get; set; }` and `EditNameHe`
- Update `OnPostCreateAsync` and `OnPostUpdateAsync` to pass them
- Add `NameEn`/`NameHe` to the `ChoreTypeVM` record and its `Select()` projection

### Step 5 — Update admin CRUD UI (`Pages/Admin/Organization/ChoreTypes/Index.cshtml`)
Add `NameEn`/`NameHe` inputs to the create form and inline edit row. Reference: `Pages/Admin/Organization/DutyTypes/Index.cshtml` for the two-field bilingual form pattern.

### Step 6 — Update all rendering sites
Resolution logic: `CultureInfo.CurrentUICulture.Name.StartsWith("he") && !string.IsNullOrWhiteSpace(ct.NameHe) ? ct.NameHe : ct.NameEn ?? ct.DisplayName`

Sites to update:
| File | Location | Note |
|---|---|---|
| `Pages/Calendar/Chores.cshtml` | line 44 | dropdown option text |
| `Pages/Calendar/Chores.cshtml` | line 130 | legend label |
| `Pages/Calendar/Chores.cshtml` | line 173 | hidden quick-entry select |
| `Pages/Calendar/Chores.cshtml.cs` | line 274 | `ExcelCalendarGroup` builder |
| `Pages/Calendar/Chores.cshtml.cs` | line 348 | calendar row builder |
| `Pages/Api/Calendar/GetChoresData.cshtml.cs` | line 101 | API JSON output — **must materialize query first** (EF can't call methods inline) |
| `Pages/Api/Calendar/GetChoresData.cshtml.cs` | line 128 | `name = ct.DisplayName` in chore types JSON |
| `Pages/Calendar/Shifts.cshtml` | line 258 | hidden quick-entry chore-type select |
| `Pages/Calendar/Overview.cshtml.cs` | ~line 351 | chore name display |

**EF projection note:** `GetChoresData.cshtml.cs:101` is inside a LINQ `.Select()` translated to SQL. Break the query: add `.ToListAsync()` first, then map in-memory.

---

## Key Files

- `Models/ChoreType.cs` — add fields here first
- `Models/OnDutyTypeConfig.cs` — reference model
- `Services/IChoreTypeService.cs` + `Services/ChoreTypeService.cs`
- `Pages/Admin/Organization/ChoreTypes/Index.cshtml` + `.cshtml.cs`
- `Pages/Admin/Organization/DutyTypes/Index.cshtml` — reference UI form
- `Pages/Calendar/Chores.cshtml` + `.cshtml.cs`
- `Pages/Api/Calendar/GetChoresData.cshtml.cs`
- `Pages/Calendar/Shifts.cshtml` (line 258) + `Overview.cshtml.cs` (~line 351)
- `docs/deferred-tasks.md §1` — original specification
