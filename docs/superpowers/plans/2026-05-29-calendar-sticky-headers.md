# Calendar Sticky Headers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the full "frozen panes" experience for ShiftManager calendars — sticky toolbar, sticky readonly banner, sticky date row, sticky left column, sticky group bands, RTL correctness, edge shadows, mobile collapse, accessibility additions — across the four pages that share `ExcelCalendarTable` (Shifts, OnCall, Chores, Overview).

**Architecture:** Single-component fix. The shared `Pages/Shared/Components/ExcelCalendarTable` component becomes the only sticky-grid surface; its CSS lives in `wwwroot/css/calendar.css` and uses a new tier of z-index tokens and shadow tokens added to `wwwroot/css/tokens.css`. Layout becomes a height-locked flex chain rooted in `_Layout.cshtml`'s existing `.app-shell → .app-main → .app-content` flex column, replacing the magic-number `max-height: calc(100vh - 200px)`. Scroll-state classes are toggled by a new `~50 LOC` IntersectionObserver-driven `wwwroot/js/calendar-sticky-shadows.js`.

**Tech Stack:** ASP.NET Core 8.0 Razor Pages, vanilla JavaScript (no framework), CSS custom properties (design tokens), xUnit + FluentAssertions for token/parity tests, Playwright via the `webapp-testing` skill for visual regression baselines.

**Spec:** `docs/superpowers/specs/2026-05-29-calendar-sticky-headers-design.md` — every Task references the spec section it implements.

---

## File Structure

### Created
| Path | Responsibility |
|---|---|
| `wwwroot/js/calendar-sticky-shadows.js` | IntersectionObserver-driven sticky-state class toggler, sentinel injection, ResizeObserver for `--cal-toolbar-height`, condense listener, next-group click handler |
| `Pages/Shared/_CalendarReadonlyBanner.cshtml` | Houses the readonly banner markup (currently inline at top of `ExcelCalendarTable/Default.cshtml`), now rendered by each caller page so it can live OUTSIDE the calendar's scroll container as a sticky second tier |
| `ShiftManager.Tests/UnitTests/Css/CalendarStickyTokenTests.cs` | xUnit guard for z-index tier ordering and presence of shadow tokens in `tokens.css` |
| `ShiftManager.Tests/UnitTests/Css/CalendarStickyRtlSweepTests.cs` | xUnit guard ensuring no `left:` / `right:` declarations remain in `.excel-calendar*` rules in `calendar.css` |
| `ShiftManager.Tests/UnitTests/Localization/CalendarStickyKeyParityTests.cs` | xUnit guard for the 13 new resx keys, EN ↔ HE parity |
| `tests/visual/sticky-headers/baseline.spec.js` | Playwright visual regression test (run via `webapp-testing` skill) — 16 screenshots per calendar × 4 calendars = 64 baseline images |
| `memory/calendar_sticky_layout_invariant.md` | New MEMORY topic file documenting the flex-column `min-height: 0` chain and z-index tier convention |

### Modified
| Path | Change |
|---|---|
| `wwwroot/css/tokens.css` | Add z-index tier tokens (`--z-sticky-col/group/header/corner`), shadow tokens (`--shadow-sticky-inline/inline-rtl/block` × light & dark), header height token (`--excel-calendar-header-height`), strong primary (`--primary-strong`). Bump `--z-dropdown` to `1060`. |
| `wwwroot/css/calendar.css` | All sticky CSS, RTL sweep (`left/right` → `inset-inline-*`), edge-shadow rules with `[dir="rtl"]` flips, weekend dark-mode contrast fix, forced-colors fallback, auto-condense rules, mobile collapse rules. Drop `max-height: calc(100vh - 200px)`. |
| `wwwroot/css/site.css` | `.app-shell` `min-height: 100vh` → `height: 100dvh`. `.app-main` adds `min-height: 0`. `.app-content` gains flex-column styling. `.data-table--sticky-*` tier-token alignment. |
| `wwwroot/css/distribution-lists.css` | `.dl-dropdown__menu` z-index swaps from `var(--z-dropdown, 1100)` to `var(--z-popover, 1060)` to survive the `--z-dropdown` bump. |
| `ViewComponents/ExcelCalendarTableViewComponent.cs` | Add `RowMode` (PascalCase string), `TotalRows` (int), `RowModeLabelKey` (computed) to `ExcelCalendarTableViewModel`. |
| `Pages/Shared/Components/ExcelCalendarTable/Default.cshtml` | Remove inline readonly-banner markup (lines 19–40). Add `aria-rowcount` to `<table>`. Add visually-hidden `<loc>` label + mode token markup in `<th class="excel-calendar__corner">`. Add `<div class="cal-toolbar-sentinel">` sentinel hook. |
| `Pages/Shared/Components/ExcelCalendarTable/_CalendarRow.cshtml` | Add `aria-rowindex` per `<tr>`. Add "Next group ↓" button inside `.excel-calendar__group-header td` (conditional). Mark group count span as `data-group-count` for filter recompute. |
| `Pages/Calendar/Shifts.cshtml` (+ `OnCall`, `Chores`, `Overview`) | Add `<partial name="_CalendarReadonlyBanner">` between toolbar and component invoke. Add sticky-shadows script include. Add Tools-overflow mobile markup. |
| `Pages/Calendar/Shifts.cshtml.cs` (+ siblings) | Set `RowMode` and `TotalRows` on view model. |
| `wwwroot/js/calendar-keyboard-nav.js` | `focusCell()` adds `cell.scrollIntoView({ block: 'nearest', inline: 'nearest' })` + nudge for sticky-obscured cells. |
| `wwwroot/js/excel-calendar-groups.js` | New `updateGroupCounts()` helper called from `applyFilter` / `clearFilter`. |
| `Resources/SharedResources.resx` + `Resources/SharedResources.he-IL.resx` | 13 new `Calendar_*` keys. |
| `memory/MEMORY.md` | One-line index entry pointing to `calendar_sticky_layout_invariant.md`. |

### Not modified (explicitly out-of-scope per spec §3)
- `Pages/Chores/Calendar.cshtml` and its `.calendar-grid` markup.
- `wwwroot/js/calendar-lazy-rows.js`.
- `@media print` rules.

---

## Tasks

The 20 tasks below follow the spec's 14-phase ordering, with some phases split for atomic commits and a few merged when dependencies tie them together. Every task ends with a commit so a stop at task N leaves a clean checkout.

---

### Task 1: Add z-index tier tokens to `tokens.css`

**Implements:** Spec §6 — Z-index tier plan (without the `--z-dropdown` bump, which is Task 2).

**Files:**
- Create: `ShiftManager.Tests/UnitTests/Css/CalendarStickyTokenTests.cs`
- Modify: `wwwroot/css/tokens.css` (locate the existing `Z-INDEX SCALE` section — usually around line 280)

- [ ] **Step 1: Create the test category folder and write the failing token-order test**

Create the file `ShiftManager.Tests/UnitTests/Css/CalendarStickyTokenTests.cs`:

```csharp
using System.Text.RegularExpressions;
using FluentAssertions;

namespace ShiftManager.Tests.UnitTests.Css;

/// <summary>
/// Locks the z-index tier convention for the calendar sticky-headers feature
/// (spec §6). Any future edit that breaks the ordering — e.g. setting
/// --z-sticky-col higher than --z-sticky-header — fails here.
/// </summary>
public class CalendarStickyTokenTests
{
    private static readonly Regex TokenRegex =
        new(@"--(?<name>[a-z0-9\-]+)\s*:\s*(?<value>[^;]+);", RegexOptions.Compiled);

    private static Dictionary<string, string> LoadTokens()
    {
        var repoRoot = LocateRepoRoot();
        var path = Path.Combine(repoRoot, "wwwroot", "css", "tokens.css");
        File.Exists(path).Should().BeTrue($"tokens.css must exist at {path}");

        var css = File.ReadAllText(path);
        return TokenRegex.Matches(css)
            .Cast<Match>()
            .GroupBy(m => m.Groups["name"].Value)
            .ToDictionary(g => g.Key, g => g.First().Groups["value"].Value.Trim());
    }

    private static int ParseZ(string raw) =>
        int.Parse(raw.Split(' ', '/', '*')[0].TrsetimEnd(';').Trim());

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ShiftManager.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("ShiftManager.csproj not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void StickyTierTokens_AreDefinedAndOrderedCorrectly()
    {
        var tokens = LoadTokens();

        tokens.Should().ContainKey("z-sticky-col");
        tokens.Should().ContainKey("z-sticky");
        tokens.Should().ContainKey("z-sticky-group");
        tokens.Should().ContainKey("z-sticky-header");
        tokens.Should().ContainKey("z-sticky-corner");

        var col    = int.Parse(tokens["z-sticky-col"]);
        var sticky = int.Parse(tokens["z-sticky"]);
        var group  = int.Parse(tokens["z-sticky-group"]);
        var header = int.Parse(tokens["z-sticky-header"]);
        var corner = int.Parse(tokens["z-sticky-corner"]);

        col.Should().BeLessThan(sticky, "sticky col is below default sticky / toolbar");
        sticky.Should().BeLessThan(group, "default sticky is below group bands");
        group.Should().BeLessThan(header, "group bands are below the table header");
        header.Should().BeLessThan(corner, "table header is below the corner cell");
    }

    [Fact]
    public void StickyShadowTokens_ArePresent()
    {
        var tokens = LoadTokens();
        tokens.Should().ContainKey("shadow-sticky-inline");
        tokens.Should().ContainKey("shadow-sticky-inline-rtl");
        tokens.Should().ContainKey("shadow-sticky-block");
    }

    [Fact]
    public void ExcelCalendarHeaderHeight_TokenIsPresent()
    {
        var tokens = LoadTokens();
        tokens.Should().ContainKey("excel-calendar-header-height");
    }
}
```

NOTE: there's a deliberate typo in `TrsetimEnd` above — fix it to `TrimEnd` before running. (Sanity check that you read this step rather than copy-pasting blind.)

- [ ] **Step 2: Run test — expect failure with "key not found"**

Run: `dotnet test --filter "FullyQualifiedName~CalendarStickyTokenTests" --no-restore`
Expected: 3 failing tests, each saying `Expected tokens to contain key "z-sticky-col"` (or similar).

- [ ] **Step 3: Add the tier tokens to `tokens.css`**

Locate the existing `Z-INDEX SCALE` section in `wwwroot/css/tokens.css` (around line 280; search for `--z-sticky:`). Insert ABOVE the existing `--z-sticky: 1020;` line:

```css
    /* Sticky tiers — calendar uses all five; data tables use first two.
       NEVER reorder. Locked by ShiftManager.Tests/UnitTests/Css/CalendarStickyTokenTests. */
    --z-sticky-col: 1019;
```

And insert AFTER `--z-sticky: 1020;`:

```css
    --z-sticky-group: 1021;
    --z-sticky-header: 1022;
    --z-sticky-corner: 1023;
```

In the same `:root` block, add the header-height token (near the other sizing tokens):

```css
    /* Header row inside the .excel-calendar table — used by sticky group-band `top` offset. */
    --excel-calendar-header-height: 44px;
```

Locate the SHADOW SYSTEM section (search `--shadow-sm:` to find it). After the existing shadow tokens, add:

```css
    /* Sticky edge shadows. Horizontally-oriented (column edge) and vertically-
       oriented (row edge) variants for spreadsheet-style frozen-pane affordance.
       RTL: box-shadow is a physical property — explicit `-rtl` variant required. */
    --shadow-sticky-inline:      2px 0 4px -2px rgba(0, 0, 0, 0.15);
    --shadow-sticky-inline-rtl: -2px 0 4px -2px rgba(0, 0, 0, 0.15);
    --shadow-sticky-block:       0 2px 4px -2px rgba(0, 0, 0, 0.15);
```

Locate the `[data-theme="dark"]` block (search `[data-theme="dark"]` in the same file). Add the dark-mode overrides:

```css
    --shadow-sticky-inline:      2px 0 4px -2px rgba(0, 0, 0, 0.45);
    --shadow-sticky-inline-rtl: -2px 0 4px -2px rgba(0, 0, 0, 0.45);
    --shadow-sticky-block:       0 2px 4px -2px rgba(0, 0, 0, 0.45);
```

- [ ] **Step 4: Run test — expect pass**

Run: `dotnet test --filter "FullyQualifiedName~CalendarStickyTokenTests" --no-restore`
Expected: 3 passing.

- [ ] **Step 5: Commit**

```bash
git add wwwroot/css/tokens.css ShiftManager.Tests/UnitTests/Css/CalendarStickyTokenTests.cs
git commit -m "feat(calendar/sticky): add z-index tier tokens, shadow tokens, header-height token

Spec §6, §7.1. Introduces the sticky CSS variable surface that
subsequent tasks will consume. No visible change yet.

CalendarStickyTokenTests locks tier ordering so future edits cannot
silently regress the stacking story."
```

---

### Task 2: Bump `--z-dropdown` and align `distribution-lists.css`

**Implements:** Spec §6 — Latent DL-dropdown clipping bug. Must ship in same PR as toolbar pinning to avoid regression.

**Files:**
- Modify: `wwwroot/css/tokens.css`
- Modify: `wwwroot/css/distribution-lists.css`
- Modify: `ShiftManager.Tests/UnitTests/Css/CalendarStickyTokenTests.cs` (extend)

- [ ] **Step 1: Extend the token test to assert `--z-dropdown ≥ --z-sticky-corner`**

In `CalendarStickyTokenTests.cs`, append this test inside the class:

```csharp
    [Fact]
    public void DropdownZIndex_IsAboveStickyCorner()
    {
        var tokens = LoadTokens();
        tokens.Should().ContainKey("z-dropdown");
        tokens.Should().ContainKey("z-sticky-corner");

        var dropdown = int.Parse(tokens["z-dropdown"]);
        var corner   = int.Parse(tokens["z-sticky-corner"]);

        dropdown.Should().BeGreaterThanOrEqualTo(corner,
            "dropdown menus must paint above the sticky corner — " +
            "otherwise the DL dropdown opens behind the sticky thead when " +
            "the toolbar pins. This was a latent bug fixed alongside the " +
            "sticky toolbar in the 2026-05-29 calendar-sticky-headers PR.");
    }
```

- [ ] **Step 2: Run test — expect failure**

Run: `dotnet test --filter "DropdownZIndex_IsAboveStickyCorner" --no-restore`
Expected: FAIL, `1000 should be greater than or equal to 1023`.

- [ ] **Step 3: Bump the token**

In `wwwroot/css/tokens.css`, locate `--z-dropdown:` and change its value:

```css
    /* Bumped from 1000 to 1060 on 2026-05-29 so dropdowns survive above
       the new sticky-corner tier (1023). Without this, the DL dropdown menu
       opens BELOW the sticky thead when the toolbar pins. */
    --z-dropdown: 1060;
```

- [ ] **Step 4: Update `distribution-lists.css` to use `--z-popover`**

In `wwwroot/css/distribution-lists.css`, locate `.dl-dropdown__menu` (search `dl-dropdown__menu`). Change its `z-index` declaration:

```css
.dl-dropdown__menu {
    /* … existing styles … */
    /* Use --z-popover (1060) explicitly. The raw 1100 fallback was a
       holdover from before --z-dropdown existed. */
    z-index: var(--z-popover, var(--z-dropdown, 1060));
}
```

- [ ] **Step 5: Run test — expect pass**

Run: `dotnet test --filter "FullyQualifiedName~CalendarStickyTokenTests" --no-restore`
Expected: 4 passing.

- [ ] **Step 6: Commit**

```bash
git add wwwroot/css/tokens.css wwwroot/css/distribution-lists.css ShiftManager.Tests/UnitTests/Css/CalendarStickyTokenTests.cs
git commit -m "fix(z-index): bump --z-dropdown above sticky-corner tier

Spec §6. The DL dropdown menu would have clipped behind the sticky
thead (1022) the moment the toolbar pins in a later task. Bumping
--z-dropdown to 1060 prevents that regression and aligns
distribution-lists.css to consume --z-popover explicitly.

No visible change today (no sticky toolbar yet); enabling the
prerequisite so the rest of the sticky work doesn't ship a known bug."
```

---

### Task 3: Add 13 new localization keys (EN + HE) with parity test

**Implements:** Spec §10 — Localization additions.

**Files:**
- Create: `ShiftManager.Tests/UnitTests/Localization/CalendarStickyKeyParityTests.cs`
- Modify: `Resources/SharedResources.resx`
- Modify: `Resources/SharedResources.he-IL.resx`

- [ ] **Step 1: Write the parity test, modeled on the existing `ErrorKeyParityTests`**

Create `ShiftManager.Tests/UnitTests/Localization/CalendarStickyKeyParityTests.cs`:

```csharp
using System.Xml.Linq;
using FluentAssertions;

namespace ShiftManager.Tests.UnitTests.Localization;

/// <summary>
/// Locks EN ↔ HE parity for the 13 new Calendar_* keys introduced by the
/// 2026-05-29 calendar-sticky-headers overhaul (spec §10). Pattern mirrors
/// ErrorKeyParityTests.
/// </summary>
public class CalendarStickyKeyParityTests
{
    private static readonly string[] RequiredKeys = new[]
    {
        "Calendar_StickyToolbar_AriaLabel",
        "Calendar_StickyHeader_AriaLabel",
        "Calendar_PinnedGroup_AriaLabel",
        "Calendar_RowLabel_ColumnHeader",
        "Calendar_NextGroup",
        "Calendar_ScrollHint_Horizontal",
        "Calendar_ScrollHint_Vertical",
        "Calendar_RowMode_Shifts",
        "Calendar_RowMode_Users",
        "Calendar_RowMode_Duty",
        "Calendar_RowMode_Chores",
        "Calendar_Tools_Overflow_Label",
        "Calendar_Tools_Overflow_AriaLabel",
    };

    private static (HashSet<string> En, HashSet<string> He) LoadKeys()
    {
        var repoRoot = LocateRepoRoot();
        var enPath = Path.Combine(repoRoot, "Resources", "SharedResources.resx");
        var hePath = Path.Combine(repoRoot, "Resources", "SharedResources.he-IL.resx");

        return (ExtractKeys(enPath), ExtractKeys(hePath));
    }

    private static HashSet<string> ExtractKeys(string resxPath)
    {
        var doc = XDocument.Load(resxPath);
        return doc.Descendants("data")
            .Select(d => d.Attribute("name")?.Value ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ShiftManager.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("ShiftManager.csproj not found");
    }

    [Fact]
    public void AllRequiredKeysPresentInBothLanguages()
    {
        var (en, he) = LoadKeys();
        var missingEn = RequiredKeys.Where(k => !en.Contains(k)).ToList();
        var missingHe = RequiredKeys.Where(k => !he.Contains(k)).ToList();

        missingEn.Should().BeEmpty("English resx must contain every Calendar_* sticky key");
        missingHe.Should().BeEmpty("Hebrew resx must contain every Calendar_* sticky key");
    }

    [Fact]
    public void HebrewValuesAreNonEmpty()
    {
        var repoRoot = LocateRepoRoot();
        var hePath = Path.Combine(repoRoot, "Resources", "SharedResources.he-IL.resx");
        var doc = XDocument.Load(hePath);
        var heValues = doc.Descendants("data")
            .Where(d => RequiredKeys.Contains(d.Attribute("name")?.Value ?? ""))
            .ToDictionary(
                d => d.Attribute("name")!.Value,
                d => d.Element("value")?.Value ?? "");

        foreach (var key in RequiredKeys)
        {
            heValues.Should().ContainKey(key);
            heValues[key].Should().NotBeNullOrWhiteSpace($"{key} must have a Hebrew translation");
        }
    }
}
```

- [ ] **Step 2: Run test — expect failure**

Run: `dotnet test --filter "FullyQualifiedName~CalendarStickyKeyParityTests" --no-restore`
Expected: 2 failing — missing keys in both files.

- [ ] **Step 3: Add the 13 keys to `Resources/SharedResources.resx`**

Open `Resources/SharedResources.resx`. Find a logical insertion point — search for an existing `Calendar_` key (e.g., `Calendar_ReadOnlyMode`) and insert the new keys near it. Add these `<data>` blocks (each one a sibling of the existing `<data>` elements):

```xml
  <data name="Calendar_StickyToolbar_AriaLabel" xml:space="preserve">
    <value>Calendar controls</value>
  </data>
  <data name="Calendar_StickyHeader_AriaLabel" xml:space="preserve">
    <value>Calendar dates</value>
  </data>
  <data name="Calendar_PinnedGroup_AriaLabel" xml:space="preserve">
    <value>Pinned group header</value>
  </data>
  <data name="Calendar_RowLabel_ColumnHeader" xml:space="preserve">
    <value>Name</value>
  </data>
  <data name="Calendar_NextGroup" xml:space="preserve">
    <value>Next group</value>
  </data>
  <data name="Calendar_ScrollHint_Horizontal" xml:space="preserve">
    <value>Scroll horizontally to see more dates</value>
  </data>
  <data name="Calendar_ScrollHint_Vertical" xml:space="preserve">
    <value>Scroll down to see more rows</value>
  </data>
  <data name="Calendar_RowMode_Shifts" xml:space="preserve">
    <value>Shifts</value>
  </data>
  <data name="Calendar_RowMode_Users" xml:space="preserve">
    <value>People</value>
  </data>
  <data name="Calendar_RowMode_Duty" xml:space="preserve">
    <value>Duty</value>
  </data>
  <data name="Calendar_RowMode_Chores" xml:space="preserve">
    <value>Chores</value>
  </data>
  <data name="Calendar_Tools_Overflow_Label" xml:space="preserve">
    <value>Tools</value>
  </data>
  <data name="Calendar_Tools_Overflow_AriaLabel" xml:space="preserve">
    <value>More calendar tools</value>
  </data>
```

- [ ] **Step 4: Add the 13 keys to `Resources/SharedResources.he-IL.resx`**

Open `Resources/SharedResources.he-IL.resx`. Insert the same 13 `<data>` blocks with Hebrew values:

```xml
  <data name="Calendar_StickyToolbar_AriaLabel" xml:space="preserve">
    <value>פקדי לוח שנה</value>
  </data>
  <data name="Calendar_StickyHeader_AriaLabel" xml:space="preserve">
    <value>תאריכי לוח שנה</value>
  </data>
  <data name="Calendar_PinnedGroup_AriaLabel" xml:space="preserve">
    <value>כותרת קבוצה מוצמדת</value>
  </data>
  <data name="Calendar_RowLabel_ColumnHeader" xml:space="preserve">
    <value>שם</value>
  </data>
  <data name="Calendar_NextGroup" xml:space="preserve">
    <value>קבוצה הבאה</value>
  </data>
  <data name="Calendar_ScrollHint_Horizontal" xml:space="preserve">
    <value>גלול אופקית לראות תאריכים נוספים</value>
  </data>
  <data name="Calendar_ScrollHint_Vertical" xml:space="preserve">
    <value>גלול מטה לראות שורות נוספות</value>
  </data>
  <data name="Calendar_RowMode_Shifts" xml:space="preserve">
    <value>משמרות</value>
  </data>
  <data name="Calendar_RowMode_Users" xml:space="preserve">
    <value>אנשים</value>
  </data>
  <data name="Calendar_RowMode_Duty" xml:space="preserve">
    <value>תורנות</value>
  </data>
  <data name="Calendar_RowMode_Chores" xml:space="preserve">
    <value>מטלות</value>
  </data>
  <data name="Calendar_Tools_Overflow_Label" xml:space="preserve">
    <value>כלים</value>
  </data>
  <data name="Calendar_Tools_Overflow_AriaLabel" xml:space="preserve">
    <value>כלי לוח שנה נוספים</value>
  </data>
```

- [ ] **Step 5: Run test — expect pass**

Run: `dotnet test --filter "FullyQualifiedName~CalendarStickyKeyParityTests" --no-restore`
Expected: 2 passing.

- [ ] **Step 6: Commit**

```bash
git add Resources/SharedResources.resx Resources/SharedResources.he-IL.resx ShiftManager.Tests/UnitTests/Localization/CalendarStickyKeyParityTests.cs
git commit -m "i18n(calendar): add 13 sticky-overhaul resx keys (EN + HE)

Spec §10. Adds Calendar_StickyToolbar_AriaLabel, _StickyHeader_AriaLabel,
_PinnedGroup_AriaLabel, _RowLabel_ColumnHeader, _NextGroup,
_ScrollHint_*, _RowMode_* (4), _Tools_Overflow_* (2). Parity locked
by CalendarStickyKeyParityTests."
```

---

### Task 4: Flex-column layout chain — replace the magic-number height

**Implements:** Spec §4 (layout chain), §2 (magic-number elimination).

**Files:**
- Modify: `wwwroot/css/site.css`
- Modify: `wwwroot/css/calendar.css`

- [ ] **Step 1: Inspect the current `.app-shell` and `.app-main` declarations**

Open `wwwroot/css/site.css` and locate `.app-shell` (around line 307) and `.app-main` (around line 422). Note their current values.

- [ ] **Step 2: Change `.app-shell` to height-locked and `.app-main` to allow shrink**

Replace the `.app-shell` declaration:

```css
.app-shell {
  display: flex;
  /* Was: min-height: 100vh. Switched to a hard height so the flex chain
     below can claim a deterministic height for the calendar's inner scroll. */
  height: 100dvh;
  background: var(--bg);
}
```

Replace the `.app-main` declaration:

```css
.app-main {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-width: 0;
  /* Mirror of min-width: 0 — required so the calendar's inner scroll
     can take ownership instead of forcing a body-level scrollbar. */
  min-height: 0;
}
```

- [ ] **Step 3: Add `.app-content` flex behavior so `.cal-page` can grow**

Still in `wwwroot/css/site.css`, search for `.app-content {`. If it doesn't exist as a class rule (it's used on the `<main>` element in `_Layout.cshtml:849`), add one:

```css
/* The <main class="app-content"> is the page slot. Pages that opt into a
   full-bleed flex-column layout (like .cal-page) need this to propagate the
   min-height: 0 chain. Pages that don't care are unaffected — they only
   require their own content to size naturally. */
.app-content {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-height: 0;
}
```

- [ ] **Step 4: Convert `.cal-page` to flex column and drop the calendar's `max-height`**

Open `wwwroot/css/calendar.css`. Locate `.cal-page` (search for it; there are several rules — find the base one). Add or modify:

```css
.cal-page {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-height: 0;
  /* existing padding / data-ui-version rules unchanged */
}
```

Locate `.excel-calendar` (around line 2580). Replace its declaration:

```css
.excel-calendar {
    /* Inner scroll on both axes. Sticky elements inside use this as their
       containing block. Flex chain (.app-shell → .app-main → .app-content
       → .cal-page → .excel-calendar) provides the height — no magic number.
       Per-link `min-height: 0` is load-bearing; if any link loses it the
       flex chain collapses and the body becomes the scroll authority,
       defeating sticky. */
    flex: 1 1 auto;
    min-height: 0;
    overflow: auto;
    border: 1px solid var(--border);
    border-radius: var(--radius-md);
    background: var(--surface);
}
```

(Removed `max-height: calc(100vh - 200px);`.)

- [ ] **Step 5: Build and run the app to verify the layout still renders**

Run: `dotnet build` to confirm no syntax errors.

Then start the app (if not already running). Navigate to `/Calendar/Shifts` and verify visually:
- The calendar grid fills the available vertical space below the toolbar.
- The inner scrollbar appears INSIDE the calendar (not at the page bottom) when content overflows.
- The page does not show a body-level vertical or horizontal scrollbar.
- The toolbar still sits above the calendar (no sticky behavior YET — that's Task 7).

Test at three widths: 1920, 1366, 768.

- [ ] **Step 6: Commit**

```bash
git add wwwroot/css/site.css wwwroot/css/calendar.css
git commit -m "refactor(calendar): replace magic-number height with flex-column chain

Spec §4. Drops .excel-calendar { max-height: calc(100vh - 200px) } in favor
of a height-locked .app-shell → .app-main → .app-content → .cal-page →
.excel-calendar flex chain. Every link gets min-height: 0 so the inner
calendar scroll takes ownership of the overflow on both axes.

No sticky behavior yet; this is the prerequisite for Tasks 7+."
```

---

### Task 5: Diagnose Zone B root cause (left-column sticky not engaging)

**Implements:** Spec §5.1 root-cause caveat, spec §12 phase 2.

This is an INVESTIGATION task, not a TDD task. No test code is written; the outcome is a documented root cause and a targeted fix.

**Files:**
- Modify: `wwwroot/css/calendar.css` (at the fix location, plus an explanatory comment)

- [ ] **Step 1: Run the app and load `/Calendar/Shifts` in month view**

Pick a molecule + job-type combination with at least 15 users so the table is wider than the viewport. Open Chrome / Edge DevTools.

- [ ] **Step 2: Inspect `.excel-calendar__row-label` computed style**

In DevTools Elements panel, select any `<td class="excel-calendar__row-label">`. In Computed:
- Confirm `position: sticky`.
- Confirm `inset-inline-start: 0` (this should be present after Task 8; for now `left: 0`).
- Note the **containing block** value (in DevTools, hover the cell; the highlighted "containing block" frame will indicate which ancestor is the sticky containing block).

If the containing block is NOT `.excel-calendar`, the sticky is broken because an ancestor between `.excel-calendar__row-label` and `.excel-calendar` has created a new containing block.

- [ ] **Step 3: Inspect each ancestor for sticky-breaking properties**

Walk the ancestor chain in DevTools (Elements panel): `td.excel-calendar__row-label` → `tr` → `tbody` → `table.excel-calendar__table` → `div.excel-calendar` → `div.cal-page` → `main.app-content` → `div.app-main` → `div.app-shell` → `body` → `html`.

For each ancestor, check Computed Style for these properties:
- `transform` (anything other than `none` creates a containing block)
- `filter` (same)
- `perspective` (same)
- `contain: layout` / `contain: paint` / `contain: strict` (same)
- `will-change: transform` / `will-change: filter` (same)
- `clip-path` (same)
- `border-collapse: collapse` on `table.excel-calendar__table` (breaks sticky on `<td>` even with everything else correct)

Note the first ancestor that has any of these. That's the breaker.

- [ ] **Step 4: Document the finding and write the fix**

Open `wwwroot/css/calendar.css` and locate `.excel-calendar__row-label`. Add a comment ABOVE the rule documenting the root cause, then write the targeted fix. The fix takes ONE of the following shapes depending on what Step 3 found:

(a) **If an ancestor has `transform` / `filter` / `contain`**: locate that rule in CSS and either remove the property or, if it's load-bearing for something else, scope it via a class so it doesn't apply to `.cal-page` ancestors. Add a comment in the fixed rule explaining why.

(b) **If `border-collapse: collapse` is inherited from an unexpected rule**: explicitly set `border-collapse: separate` on `.excel-calendar__table` (it's already declared at line 2589, but if the cascade overrides it, force it).

(c) **If `min-width: 0` is missing somewhere in the flex chain post-Task-4**: this should have been fixed by Task 4, but verify and re-fix.

(d) **If the DevTools inspection confirms sticky positioning IS engaging but the visual result still looks wrong**: investigate `z-index` and background opacity (an alpha-channel background would let scrolled content show through).

(e) **None of the above**: open a deeper diagnostic — recreate the issue in a minimal CodePen using the same DOM shape; if it works there, the bug is environmental (a browser extension); if not, escalate.

Document the actual root cause in a multi-line comment above `.excel-calendar__row-label`:

```css
/* Sticky Zone B (left column).
 *
 * Root cause of pre-2026-05-29 failure: <DOCUMENT WHAT YOU FOUND IN STEP 3>.
 * Fix: <DOCUMENT THE FIX YOU APPLIED>.
 *
 * Regression guard: see CalendarStickyRtlSweepTests (Task 8) and the Playwright
 * baseline at tests/visual/sticky-headers/baseline.spec.js (Task 19).
 */
.excel-calendar__row-label {
    position: sticky;
    inset-inline-start: 0;          /* will become inset-inline-start in Task 8 */
    z-index: var(--z-sticky-col);   /* updated in Task 7 to use new token */
    /* ... rest unchanged for now ... */
}
```

(Note: this task ONLY documents the root cause and applies the targeted fix. The token swaps to `--z-sticky-col` and the conversion of `left:0` to `inset-inline-start:0` happen in later tasks.)

- [ ] **Step 5: Reload the page and verify Zone B sticky now engages**

In DevTools, with `.excel-calendar__row-label` selected, scroll the calendar horizontally. The row-label cell should now visually stick to the left edge. Confirm at multiple scroll positions.

- [ ] **Step 6: Commit**

```bash
git add wwwroot/css/calendar.css
git commit -m "fix(calendar/sticky): diagnose and repair Zone B left-column sticky

Spec §5.1. Investigated via DevTools containing-block inspection.
Root cause: <ONE-LINE SUMMARY OF WHAT WAS FOUND>.
Fix: <ONE-LINE SUMMARY OF WHAT WAS CHANGED>.

Zone B sticky now engages in production. Token / RTL / shadow polish
follows in subsequent tasks (7, 8, 10)."
```

---

### Task 6: Extract readonly banner to its own partial

**Implements:** Spec §11 — readonly-banner relocation strategy (locked).

**Files:**
- Create: `Pages/Shared/_CalendarReadonlyBanner.cshtml`
- Modify: `Pages/Shared/Components/ExcelCalendarTable/Default.cshtml`
- Modify: `Pages/Calendar/Shifts.cshtml`, `OnCall.cshtml`, `Chores.cshtml`, `Overview.cshtml`

- [ ] **Step 1: Create `Pages/Shared/_CalendarReadonlyBanner.cshtml`**

Create `Pages/Shared/_CalendarReadonlyBanner.cshtml`:

```cshtml
@using Microsoft.Extensions.Localization
@using ShiftManager.Resources
@inject IStringLocalizer<SharedResources> Localizer
@model ShiftManager.Pages.Shared.CalendarReadonlyBannerModel

@if (Model.IsReadOnly)
{
    <div class="excel-calendar__readonly-banner">
        <span class="excel-calendar__readonly-text"><loc key="Calendar_ReadOnlyMode" /></span>
        @if (Model.RequiredGrantNameKeys.Any())
        {
            <button type="button" class="excel-calendar__readonly-help"
                    loc-aria-label="Calendar_ReadOnlyHelp_AriaLabel"
                    loc-title="Calendar_ReadOnlyHelp_AriaLabel"
                    onclick="window.showReadOnlyGrantHelp && window.showReadOnlyGrantHelp(this)">?</button>
            @* Hidden, fully-localized payload; the "?" button reads this to build the FeedbackModal message. *@
            <span class="excel-calendar__readonly-help-data" hidden
                  data-intro="@Localizer["Calendar_ReadOnlyHelp_Intro"]"
                  data-ask="@Localizer["Calendar_ReadOnlyHelp_AskAdmin"]">
                @foreach (var nameKey in Model.RequiredGrantNameKeys)
                {
                    <span class="excel-calendar__readonly-help-grant">@Localizer[nameKey]</span>
                }
            </span>
        }
    </div>
}
```

- [ ] **Step 2: Create the partial model class**

Create or extend a model class file at `Pages/Shared/CalendarReadonlyBannerModel.cs`:

```csharp
namespace ShiftManager.Pages.Shared;

public class CalendarReadonlyBannerModel
{
    public bool IsReadOnly { get; set; }
    public List<string> RequiredGrantNameKeys { get; set; } = new();
}
```

- [ ] **Step 3: Remove the inline banner from `Default.cshtml`**

In `Pages/Shared/Components/ExcelCalendarTable/Default.cshtml`, delete lines 19–40 (the `@if (Model.IsReadOnly) { <div class="excel-calendar__readonly-banner">...</div> }` block).

The file should now jump directly from the `@{ var days = ... }` block to `<div class="excel-calendar @(...)">`.

- [ ] **Step 4: Add the partial render to each caller page**

In `Pages/Calendar/Shifts.cshtml`, locate the `@* Calendar Table *@` comment (~line 240). INSERT this line just BEFORE the `@if (Model.CalendarData.Rows.Any())` block:

```cshtml
    @* Sticky readonly banner — pinned as second tier below the toolbar.
       Moved OUT of the ExcelCalendarTable component so it lives in .cal-page
       scope and can stick relative to the page, not the calendar viewport. *@
    <partial name="_CalendarReadonlyBanner" model="@(new ShiftManager.Pages.Shared.CalendarReadonlyBannerModel
    {
        IsReadOnly = Model.CalendarData.IsReadOnly,
        RequiredGrantNameKeys = Model.CalendarData.RequiredGrantNameKeys
    })" />

```

Repeat the same insertion in `Pages/Calendar/OnCall.cshtml`, `Pages/Calendar/Chores.cshtml`, and `Pages/Calendar/Overview.cshtml`, placing the partial render at the equivalent location (just before the `@await Component.InvokeAsync("ExcelCalendarTable", ...)` call).

- [ ] **Step 5: Build, run, and verify**

Run: `dotnet build`
Expected: clean build.

Start the app, navigate to a calendar page in read-only mode (impersonate a user without edit grants). Verify:
- The readonly banner still renders.
- The banner is NOT inside `.excel-calendar` (inspect in DevTools — its parent should be `.cal-page`, not `.excel-calendar`).
- The "?" help button still works.

- [ ] **Step 6: Commit**

```bash
git add Pages/Shared/_CalendarReadonlyBanner.cshtml Pages/Shared/CalendarReadonlyBannerModel.cs Pages/Shared/Components/ExcelCalendarTable/Default.cshtml Pages/Calendar/Shifts.cshtml Pages/Calendar/OnCall.cshtml Pages/Calendar/Chores.cshtml Pages/Calendar/Overview.cshtml
git commit -m "refactor(calendar): extract readonly banner to its own partial

Spec §11. Moves the readonly banner OUT of ExcelCalendarTable into
_CalendarReadonlyBanner partial rendered by each caller page. Banner
now lives in .cal-page scope so it can be sticky as a second tier
below the toolbar (Task 7), instead of scrolling away with the table."
```

---

### Task 7: Sticky toolbar + sticky readonly banner (CSS only)

**Implements:** Spec §5.2 (toolbar), §5.3 (readonly banner), §2 (locked decisions).

**Files:**
- Modify: `wwwroot/css/calendar.css`

- [ ] **Step 1: Locate `.cal-toolbar` in `calendar.css`**

Search `.cal-toolbar` in `wwwroot/css/calendar.css`. There's an existing block defining its visual styling (border, padding, radius). Note the line range.

- [ ] **Step 2: Add sticky properties to `.cal-toolbar`**

Inside the existing `.cal-toolbar` rule, add:

```css
.cal-toolbar {
    /* ... existing visual styles (background, border, border-radius, padding) ... */

    /* Sticky to .cal-page scope (sibling of .excel-calendar, NOT inside it).
       Pinning detected via .cal-toolbar-sentinel element above the toolbar
       (injected by Default.cshtml, observed by calendar-sticky-shadows.js). */
    position: sticky;
    top: 0;
    z-index: var(--z-sticky);
    transition: box-shadow var(--transition-fast),
                padding var(--transition-fast);
}

/* Only sticky when there's actually a calendar to scroll. Without this,
   .cal-empty pages would have a useless pinned toolbar over empty space. */
.cal-page:not(:has(.excel-calendar)) .cal-toolbar {
    position: static;
}

/* Pinned-state shadow (applied by JS when sentinel leaves viewport). */
.cal-toolbar.is-pinned {
    box-shadow: var(--shadow-md);
    border-color: var(--border-strong);
}
```

- [ ] **Step 3: Add sticky CSS for `.excel-calendar__readonly-banner`**

Locate the existing `.excel-calendar__readonly-banner` rule. Add (or extend):

```css
.excel-calendar__readonly-banner {
    /* ... existing visual styles ... */

    /* Second-tier sticky beneath the toolbar. Top offset uses --cal-toolbar-height
       (set by calendar-sticky-shadows.js via ResizeObserver) with a 64px fallback
       if JS is disabled. */
    position: sticky;
    top: var(--cal-toolbar-height, 64px);
    z-index: var(--z-sticky);
}
```

- [ ] **Step 4: Build and verify the toolbar pins on scroll**

Run: `dotnet build`
Expected: clean build.

Start the app, load `/Calendar/Shifts` in month view with enough rows to overflow. Scroll down inside the calendar. Verify:
- The toolbar stays at the top of the viewport as you scroll.
- The readonly banner (if visible) stays below the toolbar.
- Pages without a calendar (empty state) do NOT have a pinned toolbar.

NOTE: the `.is-pinned` shadow won't appear yet — that requires Task 10's IntersectionObserver. For now, just confirm the position behavior.

- [ ] **Step 5: Commit**

```bash
git add wwwroot/css/calendar.css
git commit -m "feat(calendar/sticky): pin cal-toolbar and readonly banner to .cal-page

Spec §5.2, §5.3. Toolbar sticks to top: 0 in .cal-page scope; readonly
banner sticks as second tier below it via --cal-toolbar-height. Empty
calendars (.cal-empty) skip sticky via :has() gating.

Shadow on .is-pinned awaits Task 10 (IntersectionObserver wiring).
Visual verification at /Calendar/Shifts month view, 30+ rows."
```

---

### Task 8: RTL sweep — `left/right` → `inset-inline-*`

**Implements:** Spec §8 — RTL discipline.

**Files:**
- Create: `ShiftManager.Tests/UnitTests/Css/CalendarStickyRtlSweepTests.cs`
- Modify: `wwwroot/css/calendar.css`

- [ ] **Step 1: Write the failing CSS-text guard test**

Create `ShiftManager.Tests/UnitTests/Css/CalendarStickyRtlSweepTests.cs`:

```csharp
using System.Text.RegularExpressions;
using FluentAssertions;

namespace ShiftManager.Tests.UnitTests.Css;

/// <summary>
/// Locks RTL discipline for the .excel-calendar* and .cal-* selectors in
/// calendar.css. Any new `left:` or `right:` declaration inside one of these
/// rules fails here — the codebase contract is to use logical properties
/// (inset-inline-start, inset-inline-end) so Hebrew mode pins the correct
/// logical side automatically.
///
/// Exception: explicit `[dir="rtl"]` overrides for box-shadow are ALLOWED
/// (box-shadow is a physical property; no logical equivalent exists).
/// The regex below excludes those.
/// </summary>
public class CalendarStickyRtlSweepTests
{
    private static readonly string[] GuardedSelectorPrefixes = new[]
    {
        ".excel-calendar",
        ".cal-toolbar",
        ".cal-page",
    };

    private static readonly Regex RuleRegex =
        new(@"(?<selector>[^{};]+?)\s*\{(?<body>[^{}]*)\}", RegexOptions.Compiled);

    private static readonly Regex PhysicalSideRegex =
        new(@"(?<![\w-])(left|right)\s*:\s*[^;]+;", RegexOptions.Compiled);

    [Fact]
    public void NoPhysicalLeftRightInCalendarStickySelectors()
    {
        var repoRoot = LocateRepoRoot();
        var path = Path.Combine(repoRoot, "wwwroot", "css", "calendar.css");
        var css = File.ReadAllText(path);

        var offenders = new List<string>();
        foreach (Match rule in RuleRegex.Matches(css))
        {
            var selector = rule.Groups["selector"].Value.Trim();
            var body = rule.Groups["body"].Value;

            // Skip if selector doesn't touch our guarded prefixes.
            if (!GuardedSelectorPrefixes.Any(p => selector.Contains(p)))
                continue;

            // Skip `[dir="rtl"]` overrides — they're allowed to use physical
            // properties because they're explicitly redeclaring for RTL.
            if (selector.Contains("[dir=\"rtl\"]") || selector.Contains("[dir='rtl']"))
                continue;

            foreach (Match m in PhysicalSideRegex.Matches(body))
            {
                offenders.Add($"selector `{selector}` contains `{m.Value.Trim()}`");
            }
        }

        offenders.Should().BeEmpty(
            "calendar.css must use inset-inline-start / inset-inline-end " +
            "instead of left/right inside .excel-calendar / .cal-toolbar / " +
            ".cal-page rules so RTL (Hebrew) mode pins the correct logical side. " +
            "If you genuinely need a physical override, wrap it in a " +
            "[dir=\"rtl\"] selector.");
    }

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ShiftManager.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("ShiftManager.csproj not found");
    }
}
```

- [ ] **Step 2: Run test — expect failure listing every offender**

Run: `dotnet test --filter "FullyQualifiedName~CalendarStickyRtlSweepTests" --no-restore`
Expected: FAIL with a list of selectors containing `left:` or `right:`. Read the list — it tells you exactly what to fix.

- [ ] **Step 3: Convert each offender to logical properties**

For each offender reported by Step 2, find the rule in `wwwroot/css/calendar.css` and change:
- `left: 0` → `inset-inline-start: 0`
- `right: 0` → `inset-inline-end: 0`
- `left: <n>` → `inset-inline-start: <n>`
- `right: <n>` → `inset-inline-end: <n>`

Notable conversions:
- `.excel-calendar__row-label { left: 0 }` → `inset-inline-start: 0`
- `.excel-calendar__corner { left: 0 }` → `inset-inline-start: 0`
- `.shift-table td:first-child { left: 0 }` → `inset-inline-start: 0` (if matched by selector prefix `.cal`)

For positioned children (e.g., `position: absolute; left: 8px`), the same rule applies: `inset-inline-start: 8px`.

- [ ] **Step 4: Run test — expect pass**

Run: `dotnet test --filter "FullyQualifiedName~CalendarStickyRtlSweepTests" --no-restore`
Expected: PASS.

- [ ] **Step 5: Visually verify Hebrew RTL mode**

Start the app, switch language to Hebrew (the language switcher uses culture cookies). Navigate to `/Calendar/Shifts`. Verify:
- Row labels stick to the RIGHT (logical start) when scrolling.
- The corner cell sits at the top-RIGHT.
- The table flows right-to-left as expected.
- Switch back to English; row labels stick to the LEFT.

- [ ] **Step 6: Commit**

```bash
git add wwwroot/css/calendar.css ShiftManager.Tests/UnitTests/Css/CalendarStickyRtlSweepTests.cs
git commit -m "i18n(calendar/rtl): sweep left/right → inset-inline-* in calendar.css

Spec §8. Every .excel-calendar* and .cal-* rule now uses logical
properties. Hebrew RTL mode pins row labels to the visual right
(logical start) automatically — no [dir=\"rtl\"] override needed.

Regression locked by CalendarStickyRtlSweepTests. Future edits that
add `left:` or `right:` inside guarded selectors fail at test time."
```

---

### Task 9: Edge shadows via class toggles (CSS only)

**Implements:** Spec §5 (consumer shadow rules), §7.1 (token usage), §7.3 (forced-colors fallback).

**Files:**
- Modify: `wwwroot/css/calendar.css`

- [ ] **Step 1: Add scroll-state shadow rules for row-label, corner, and header**

In `wwwroot/css/calendar.css`, locate `.excel-calendar__row-label`. AFTER its base rule, add:

```css
/* Scroll-activated edge shadow (LTR). Class `.is-scrolled-x` toggled by JS. */
.excel-calendar.is-scrolled-x .excel-calendar__row-label {
    box-shadow: var(--shadow-sticky-inline);
}

/* RTL: flip shadow x offset (box-shadow is physical). */
[dir="rtl"] .excel-calendar.is-scrolled-x .excel-calendar__row-label {
    box-shadow: var(--shadow-sticky-inline-rtl);
}
```

Locate `.excel-calendar__corner`. AFTER its base rule, add:

```css
/* Corner shadows on either axis. */
.excel-calendar.is-scrolled-x .excel-calendar__corner,
.excel-calendar.is-scrolled-y .excel-calendar__corner {
    box-shadow: var(--shadow-sticky-block),
                var(--shadow-sticky-inline);
}

[dir="rtl"] .excel-calendar.is-scrolled-x .excel-calendar__corner,
[dir="rtl"] .excel-calendar.is-scrolled-y .excel-calendar__corner {
    box-shadow: var(--shadow-sticky-block),
                var(--shadow-sticky-inline-rtl);
}
```

Locate `.excel-calendar__header`. AFTER its base rule, add:

```css
/* Reduce existing border-bottom from 2px to 1px (at-rest separator only). */
.excel-calendar__header th {
    border-bottom: 1px solid var(--border-strong);   /* was 2px solid */
}

/* Scroll-activated block-axis shadow when content slides under the thead. */
.excel-calendar.is-scrolled-y .excel-calendar__header {
    box-shadow: var(--shadow-sticky-block);
}
```

Find and REMOVE the old `border-bottom: 2px solid var(--border-strong);` declaration on `.excel-calendar__header th` (it should be around line 2612). The new 1px declaration above replaces it.

- [ ] **Step 2: Update sticky-element z-index to use the new tokens**

In `wwwroot/css/calendar.css`:

- `.excel-calendar__row-label { z-index: calc(var(--z-sticky) - 1); }` → `z-index: var(--z-sticky-col);`
- `.excel-calendar__header { z-index: var(--z-sticky); }` → `z-index: var(--z-sticky-header);`
- `.excel-calendar__corner { z-index: calc(var(--z-sticky) + 1); }` → `z-index: var(--z-sticky-corner);`

- [ ] **Step 3: Add the forced-colors fallback**

At the end of the sticky CSS section (after `.excel-calendar__corner` rules), add:

```css
/* Forced-colors (Windows high-contrast) mode: box-shadow is often ignored.
   Replace edge shadows with solid CanvasText borders — always-on, scroll-
   independent. Forced-colors users don't need motion cues; they need
   static separators that survive the system theme. */
@media (forced-colors: active) {
    .excel-calendar__row-label,
    .excel-calendar__corner {
        border-inline-end: 2px solid CanvasText;
    }
    .excel-calendar__header,
    .excel-calendar__group-header td,
    .cal-toolbar {
        border-bottom: 2px solid CanvasText;
    }
}
```

- [ ] **Step 4: Build and visually verify the shadow CSS is correct (even without JS toggling)**

Run: `dotnet build`. Reload `/Calendar/Shifts`. In DevTools, manually add the class `is-scrolled-x` to the `.excel-calendar` element (Elements → select → edit class). Verify the row-label gains a shadow on its inline-end side.

Repeat for `is-scrolled-y` on the wrapper → verify the thead gains a shadow.

Manually toggle `[dir="rtl"]` on the `<html>` element and re-add `is-scrolled-x` → verify the shadow flips direction.

Remove the JS-injected classes when done.

- [ ] **Step 5: Commit**

```bash
git add wwwroot/css/calendar.css
git commit -m "feat(calendar/sticky): scroll-activated edge shadows + token swap

Spec §5, §7. Adds .is-scrolled-x / .is-scrolled-y shadow rules for
row-label, corner, and header. Each has an explicit [dir=\"rtl\"]
flip (box-shadow is physical — no logical equivalent).

Sticky z-index swapped to new tier tokens (--z-sticky-col / -header
/ -corner). Header at-rest border reduced from 2px → 1px so the
scroll shadow doesn't double up visually.

Forced-colors (Windows high-contrast) mode falls back to solid
CanvasText borders — always-on, scroll-independent.

JS class toggling (the wire that activates these) is Task 10."
```

---

### Task 10: Sticky-shadows JavaScript

**Implements:** Spec §7.4 — IntersectionObserver-driven scroll-state class toggler.

**Files:**
- Create: `wwwroot/js/calendar-sticky-shadows.js`
- Modify: `Pages/Shared/Components/ExcelCalendarTable/Default.cshtml`
- Modify: `Pages/Calendar/Shifts.cshtml`, `OnCall.cshtml`, `Chores.cshtml`, `Overview.cshtml`

- [ ] **Step 1: Create `wwwroot/js/calendar-sticky-shadows.js`**

```javascript
// calendar-sticky-shadows.js
// Manages sticky-related CSS classes via IntersectionObserver:
//   .is-scrolled-x   — set when the calendar's inline-start edge has been scrolled past
//   .is-scrolled-y   — set when the calendar's block-start edge has been scrolled past
//   .is-group-pinned — set when any group header band is currently pinned
//   .is-pinned       — set on individual band <tr>s when they're pinned
//   .is-pinned       — set on .cal-toolbar when its sentinel leaves the viewport
//   .is-condensed    — set on .cal-toolbar after 200px of vertical scroll on .excel-calendar
//
// Also sets --cal-toolbar-height on .cal-page via ResizeObserver so the
// readonly banner's `top:` offset stays in sync with the actual toolbar height.

(function () {
    'use strict';

    const SCROLL_X_CLASS = 'is-scrolled-x';
    const SCROLL_Y_CLASS = 'is-scrolled-y';
    const GROUP_PIN_CLASS = 'is-group-pinned';
    const BAND_PIN_CLASS = 'is-pinned';
    const TOOLBAR_PIN_CLASS = 'is-pinned';
    const TOOLBAR_CONDENSE_CLASS = 'is-condensed';
    const CONDENSE_THRESHOLD = 200; // px of vertical scroll on .excel-calendar

    function init() {
        document.querySelectorAll('.excel-calendar').forEach(wireCalendar);
        document.querySelectorAll('.cal-toolbar').forEach(wireToolbar);
    }

    function wireCalendar(calendar) {
        // Inject sentinels at the leading edges (1×1 absolute boxes).
        const yToken = document.createElement('div');
        yToken.className = 'excel-calendar__scroll-sentinel excel-calendar__scroll-sentinel--y';
        yToken.setAttribute('aria-hidden', 'true');
        const xToken = document.createElement('div');
        xToken.className = 'excel-calendar__scroll-sentinel excel-calendar__scroll-sentinel--x';
        xToken.setAttribute('aria-hidden', 'true');
        calendar.prepend(yToken, xToken);

        const ioY = new IntersectionObserver(
            (entries) => calendar.classList.toggle(SCROLL_Y_CLASS, !entries[0].isIntersecting),
            { root: calendar, threshold: 0 }
        );
        ioY.observe(yToken);

        const ioX = new IntersectionObserver(
            (entries) => calendar.classList.toggle(SCROLL_X_CLASS, !entries[0].isIntersecting),
            { root: calendar, threshold: 0 }
        );
        ioX.observe(xToken);

        // Group bands — pin detection.
        const bands = calendar.querySelectorAll('.excel-calendar__group-header');
        bands.forEach((band) => {
            const headerHeight = parseInt(
                getComputedStyle(calendar).getPropertyValue('--excel-calendar-header-height') || '44',
                10
            );
            const ioBand = new IntersectionObserver(
                (entries) => {
                    const pinned = !entries[0].isIntersecting;
                    band.classList.toggle(BAND_PIN_CLASS, pinned);
                    const anyPinned = !!calendar.querySelector('.excel-calendar__group-header.is-pinned');
                    calendar.classList.toggle(GROUP_PIN_CLASS, anyPinned);
                },
                { root: calendar, rootMargin: `-${headerHeight + 1}px 0px 0px 0px`, threshold: 0 }
            );
            ioBand.observe(band);
        });

        // Condense listener.
        let lastScrollTop = 0;
        calendar.addEventListener('scroll', () => {
            if (calendar.scrollTop === lastScrollTop) return;
            lastScrollTop = calendar.scrollTop;
            requestAnimationFrame(() => {
                const toolbar = calendar.closest('.cal-page')?.querySelector('.cal-toolbar');
                if (toolbar) {
                    toolbar.classList.toggle(
                        TOOLBAR_CONDENSE_CLASS,
                        calendar.scrollTop > CONDENSE_THRESHOLD
                    );
                }
            });
        }, { passive: true });
    }

    function wireToolbar(toolbar) {
        const page = toolbar.closest('.cal-page');
        if (!page) return;

        // Sentinel sits immediately above the toolbar — when it leaves the
        // viewport, the toolbar is pinned.
        const sentinel = document.createElement('div');
        sentinel.className = 'cal-toolbar-sentinel';
        sentinel.setAttribute('aria-hidden', 'true');
        toolbar.parentNode.insertBefore(sentinel, toolbar);

        const io = new IntersectionObserver(
            (entries) => toolbar.classList.toggle(TOOLBAR_PIN_CLASS, !entries[0].isIntersecting),
            { threshold: 0 }
        );
        io.observe(sentinel);

        // ResizeObserver: keep --cal-toolbar-height in sync.
        const ro = new ResizeObserver(() => {
            page.style.setProperty('--cal-toolbar-height', toolbar.offsetHeight + 'px');
        });
        ro.observe(toolbar);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
```

- [ ] **Step 2: Add the sentinel CSS to `calendar.css`**

In `wwwroot/css/calendar.css`, add at the end of the EXCEL CALENDAR section:

```css
/* Scroll-state sentinels — invisible 1×1 markers that the IntersectionObserver
   in calendar-sticky-shadows.js watches to set .is-scrolled-* classes. */
.excel-calendar__scroll-sentinel {
    position: absolute;
    width: 1px;
    height: 1px;
    pointer-events: none;
    background: transparent;
}
.excel-calendar__scroll-sentinel--y {
    top: 0;
    inset-inline-start: 0;
}
.excel-calendar__scroll-sentinel--x {
    top: 0;
    inset-inline-start: 0;
}
.excel-calendar { position: relative; }   /* required so sentinels position to it */

.cal-toolbar-sentinel {
    height: 1px;
    width: 100%;
    pointer-events: none;
}
```

- [ ] **Step 3: Wire the script include into each caller page**

In `Pages/Calendar/Shifts.cshtml`, locate the `@section Scripts {` block (~line 362). Add this script tag among the others (place it AFTER `calendar-lazy-rows.js` to ensure rows are settled before sentinels are observed):

```cshtml
    <script src="~/js/calendar-sticky-shadows.js" asp-append-version="true"></script>
```

Repeat the addition in `OnCall.cshtml`, `Chores.cshtml`, `Overview.cshtml`.

- [ ] **Step 4: Build and verify scroll-state classes appear**

Run: `dotnet build`. Reload `/Calendar/Shifts`. Open DevTools → Elements → select `.excel-calendar`. Scroll right inside the calendar. Verify the element gains `is-scrolled-x` (and the row-label shows a shadow). Scroll down → `is-scrolled-y` appears (header shows a shadow). Scroll back to origin → classes removed, shadows gone.

Scroll into a group → verify the group band gains `.is-pinned`, the wrapper gains `.is-group-pinned`, and the group-band shadow appears (Task 11 adds the band sticky CSS — if you haven't done that yet, this step verifies the wire only).

Scroll the toolbar's sentinel out of view (scroll the calendar down) → verify `.cal-toolbar` gains `.is-pinned` and the toolbar shadow appears.

- [ ] **Step 5: Commit**

```bash
git add wwwroot/js/calendar-sticky-shadows.js wwwroot/css/calendar.css Pages/Calendar/Shifts.cshtml Pages/Calendar/OnCall.cshtml Pages/Calendar/Chores.cshtml Pages/Calendar/Overview.cshtml
git commit -m "feat(calendar/sticky): IntersectionObserver-driven scroll-state classes

Spec §7.4. Adds wwwroot/js/calendar-sticky-shadows.js which manages
.is-scrolled-x / .is-scrolled-y / .is-group-pinned / .is-pinned classes
via IntersectionObserver against injected 1px sentinels. Also wires
--cal-toolbar-height via ResizeObserver and toggles .is-condensed
after 200px of vertical scroll (auto-condense, spec §2).

Edge shadows declared in Task 9 now activate on scroll. Toolbar
shadow visible when pinned. Condense behavior visible when scrolling
deep into a calendar with many rows."
```

---

### Task 11: Sticky group bands (Zone C)

**Implements:** Spec §5.4 — Zone C group bands, §2 (stack-and-replace, collapsed handling).

**Files:**
- Modify: `wwwroot/css/calendar.css`
- Modify: `wwwroot/js/excel-calendar-groups.js`

- [ ] **Step 1: Add sticky CSS for `.excel-calendar__group-header td`**

In `wwwroot/css/calendar.css`, locate `.excel-calendar__group-header td` (around line 2909). Replace / extend:

```css
.excel-calendar__group-header td {
    /* Sticky applied to <td> inside <tr>. Sticky on <tr> is unreliable
       cross-browser; sticky on the td with full colspan paints reliably. */
    position: sticky;
    top: var(--excel-calendar-header-height, 44px);   /* Below sticky thead */
    z-index: var(--z-sticky-group);

    /* Existing visual styles preserved */
    background: var(--surface-soft);
    padding: var(--space-2) var(--space-3);
    font-weight: var(--font-semibold);
    border-top: 2px solid var(--border-strong);
    border-inline-start: 4px solid var(--group-color, var(--primary));
}

/* Scroll-activated shadow on pinned bands. */
.excel-calendar.is-group-pinned .excel-calendar__group-header.is-pinned td {
    box-shadow: var(--shadow-sticky-block);
}

/* Collapsed groups: band IS the only visible row of its group. Don't sticky. */
.excel-calendar__group-header.is-collapsed td {
    position: static;
}
```

- [ ] **Step 2: Ensure JS adds the `.is-collapsed` class to collapsed-group bands**

Open `wwwroot/js/excel-calendar-groups.js`. Locate the function that toggles a group's collapsed state (search for `IsCollapsed` or `toggleGroup`). When a group is collapsed, ensure the `.is-collapsed` class is applied to its `.excel-calendar__group-header` `<tr>`.

If the existing code uses inline style `display: none` on rows without setting a class on the band, add this logic. Find the section that handles collapse (likely in `toggleGroup(groupId)`):

```javascript
function toggleGroup(groupId) {
    const band = document.querySelector(`.excel-calendar__group-header[data-group-id="${groupId}"]`);
    if (!band) return;
    const rows = document.querySelectorAll(`tr[data-group-id="${groupId}"]:not(.excel-calendar__group-header)`);
    const willCollapse = !band.classList.contains('is-collapsed');

    band.classList.toggle('is-collapsed', willCollapse);
    rows.forEach((r) => {
        r.style.display = willCollapse ? 'none' : '';
    });

    // ... existing chevron rotation handling ...
}
```

If the existing function doesn't match this shape, adapt minimally — the ONLY change required is the `band.classList.toggle('is-collapsed', willCollapse)` line.

ALSO: ensure server-side-collapsed groups already render with the `.is-collapsed` class on the band. In `Pages/Shared/Components/ExcelCalendarTable/Default.cshtml`, locate the `<tr class="excel-calendar__group-header"` line (~line 81) and add the conditional class:

```cshtml
<tr class="excel-calendar__group-header @(group.IsCollapsed ? "is-collapsed" : "")" data-group-id="@group.Id">
```

- [ ] **Step 3: Build and verify group bands pin and unpin correctly**

Run: `dotnet build`. Load `/Calendar/Shifts` in a configuration with at least 2 groups, each with many rows. Scroll down through Group 1 — verify the band pins at `top: 44px`. Scroll past Group 1 — verify the band un-pins as Group 2's band scrolls up to push it off.

Toggle a group to collapsed via the chevron → verify the band does NOT pin (position: static).

Test in dark mode and Hebrew RTL.

- [ ] **Step 4: Commit**

```bash
git add wwwroot/css/calendar.css wwwroot/js/excel-calendar-groups.js Pages/Shared/Components/ExcelCalendarTable/Default.cshtml
git commit -m "feat(calendar/sticky): sticky group header bands (Zone C)

Spec §5.4. Group bands now stick below the sticky thead at
top: var(--excel-calendar-header-height). Stack-and-replace behavior
via natural sticky (the next band scrolls up to push the previous
one off).

Collapsed groups: band is position: static — pinning a band that's
already the only visible row of its group would create a double-border
glitch. .is-collapsed class set both server-side (Default.cshtml) and
client-side (excel-calendar-groups.js toggleGroup).

Shadow on .is-pinned bands lit by Task 10's IntersectionObserver."
```

---

### Task 12: Mode token in corner cell + view-model `RowMode`/`TotalRows`

**Implements:** Spec §5.6 (mode token), §11 (view-model additions).

**Files:**
- Modify: `ViewComponents/ExcelCalendarTableViewComponent.cs`
- Modify: `Pages/Shared/Components/ExcelCalendarTable/Default.cshtml`
- Modify: `Pages/Calendar/Shifts.cshtml.cs`, `OnCall.cshtml.cs`, `Chores.cshtml.cs`, `Overview.cshtml.cs`
- Modify: `wwwroot/css/calendar.css`

- [ ] **Step 1: Extend the view model**

In `ViewComponents/ExcelCalendarTableViewComponent.cs`, locate `public class ExcelCalendarTableViewModel`. Add these properties:

```csharp
    /// <summary>
    /// What each row represents. PascalCase ("Shifts" | "Users" | "Duty" | "Chores")
    /// so it appends directly to the resx key "Calendar_RowMode_" + RowMode.
    /// Used by the sticky corner-cell mode token (spec §5.6).
    /// </summary>
    public string RowMode { get; set; } = "Shifts";

    /// <summary>
    /// Total row count INCLUDING group-header rows. Used as aria-rowcount
    /// on the &lt;table&gt; for screen-reader "row N of M" context.
    /// </summary>
    public int TotalRows { get; set; }

    /// <summary>
    /// resx key for the mode label, computed from RowMode.
    /// </summary>
    public string RowModeLabelKey => $"Calendar_RowMode_{RowMode}";
```

- [ ] **Step 2: Populate `RowMode` and `TotalRows` in each caller page**

In `Pages/Calendar/Shifts.cshtml.cs`, locate where `CalendarData` is constructed (search for `new ExcelCalendarTableViewModel` or `CalendarData =`). After the existing assignments, add:

```csharp
CalendarData.RowMode = Mode == "user" ? "Users" : "Shifts";
CalendarData.TotalRows = CalendarData.Rows.Count + (CalendarData.Groups?.Count ?? 0);
```

In `Pages/Calendar/OnCall.cshtml.cs`:

```csharp
CalendarData.RowMode = "Duty";
CalendarData.TotalRows = CalendarData.Rows.Count + (CalendarData.Groups?.Count ?? 0);
```

In `Pages/Calendar/Chores.cshtml.cs`:

```csharp
CalendarData.RowMode = "Chores";
CalendarData.TotalRows = CalendarData.Rows.Count + (CalendarData.Groups?.Count ?? 0);
```

In `Pages/Calendar/Overview.cshtml.cs`:

```csharp
CalendarData.RowMode = "Shifts";
CalendarData.TotalRows = CalendarData.Rows.Count + (CalendarData.Groups?.Count ?? 0);
```

- [ ] **Step 3: Update the corner cell markup**

In `Pages/Shared/Components/ExcelCalendarTable/Default.cshtml`, replace the existing empty corner cell:

```cshtml
<th class="excel-calendar__corner" scope="col"></th>
```

with:

```cshtml
<th class="excel-calendar__corner" scope="col" data-mode="@Model.RowMode.ToLowerInvariant()">
    <span class="visually-hidden"><loc key="Calendar_RowLabel_ColumnHeader" /></span>
    <span class="excel-calendar__mode-token" aria-hidden="true">
        <loc key="@Model.RowModeLabelKey" />
        <span class="excel-calendar__mode-chevron">▾</span>
    </span>
</th>
```

Also add `aria-rowcount` to the table opening tag:

```cshtml
<table class="excel-calendar__table" role="grid" aria-rowcount="@Model.TotalRows">
```

- [ ] **Step 4: Style the mode token**

In `wwwroot/css/calendar.css`, locate `.excel-calendar__corner`. AFTER its rules, add:

```css
.excel-calendar__mode-token {
    display: inline-flex;
    align-items: center;
    gap: var(--space-1);
    font-size: var(--text-small);
    font-weight: var(--font-semibold);
    /* Color inherits from .excel-calendar__corner (var(--primary-contrast)). */
}

.excel-calendar__mode-chevron {
    font-size: 0.75em;
    opacity: 0.7;
}
```

- [ ] **Step 5: Build and visually verify**

Run: `dotnet build`. Load `/Calendar/Shifts` (mode=user) — corner should read "People ▾". Switch to mode=shift — corner reads "Shifts ▾". Load OnCall — "Duty ▾". Load Chores — "Chores ▾". Load Overview — "Shifts ▾".

Inspect with screen reader (or DevTools accessibility tree): the column header announces "Name" (the visually-hidden label) when navigating into the table.

Switch to Hebrew → corner reads "אנשים ▾" / "משמרות ▾" / etc.

- [ ] **Step 6: Commit**

```bash
git add ViewComponents/ExcelCalendarTableViewComponent.cs Pages/Calendar/Shifts.cshtml.cs Pages/Calendar/OnCall.cshtml.cs Pages/Calendar/Chores.cshtml.cs Pages/Calendar/Overview.cshtml.cs Pages/Shared/Components/ExcelCalendarTable/Default.cshtml wwwroot/css/calendar.css
git commit -m "feat(calendar/sticky): mode token in corner cell + aria-rowcount

Spec §5.6, §9.1, §11. The previously-empty corner cell now shows
'Shifts ▾' / 'People ▾' / 'Duty ▾' / 'Chores ▾' (sighted) and announces
'Name' to screen readers via a visually-hidden label.

Adds RowMode + TotalRows + RowModeLabelKey to ExcelCalendarTableViewModel.
Each caller page sets RowMode appropriately and computes TotalRows
including group headers for accurate aria-rowcount."
```

---

### Task 13: `aria-rowindex` per row + visually-hidden corner label (already done in Task 12, verify)

**Implements:** Spec §9.1 — ARIA additions.

**Files:**
- Modify: `Pages/Shared/Components/ExcelCalendarTable/_CalendarRow.cshtml`
- Modify: `Pages/Shared/Components/ExcelCalendarTable/Default.cshtml` (verify aria-rowcount from Task 12)

- [ ] **Step 1: Pass row index from `Default.cshtml` to `_CalendarRow.cshtml`**

In `Pages/Shared/Components/ExcelCalendarTable/Default.cshtml`, look at where the partial is invoked (around line 99–139). Each `Html.PartialAsync` call passes `ExcelCalendarRowViewModel`. We need to add a 1-based row index.

Edit `_CalendarRow.cshtml`'s @functions or its model. Easier: pass `RowIndex` in the partial model. Update the `ExcelCalendarRowViewModel` class in `Default.cshtml`'s `@functions` block (lines 145–153):

```csharp
public class ExcelCalendarRowViewModel
{
    public ShiftManager.ViewComponents.ExcelCalendarRow Row { get; set; } = new();
    public List<DateOnly> Days { get; set; } = new();
    public DateOnly Today { get; set; }
    public bool IsReadOnly { get; set; }
    public bool IsCollapsed { get; set; }
    public bool HasWeeklyHours { get; set; }
    public int RowIndex { get; set; }   // NEW — 1-based aria-rowindex
}
```

Update each `Html.PartialAsync` call site (4 occurrences in `Default.cshtml`) to track and pass the index. Wrap the loops with a counter:

```cshtml
@{ var ariaRowIndex = 1; }
@if (Model.Groups != null)
{
    foreach (var group in Model.Groups.OrderBy(g => g.SortOrder))
    {
        <tr class="excel-calendar__group-header @(group.IsCollapsed ? "is-collapsed" : "")"
            data-group-id="@group.Id"
            aria-rowindex="@ariaRowIndex">
            @* ... existing td colspan content ... *@
        </tr>
        @{ ariaRowIndex++; }

        foreach (var row in Model.Rows.Where(r => r.GroupId == group.Id))
        {
            @await Html.PartialAsync("~/Pages/Shared/Components/ExcelCalendarTable/_CalendarRow.cshtml", new ExcelCalendarRowViewModel
            {
                Row = row,
                Days = days,
                Today = today,
                IsReadOnly = Model.IsReadOnly,
                IsCollapsed = group.IsCollapsed,
                HasWeeklyHours = hasWeeklyHours,
                RowIndex = ariaRowIndex
            })
            @{ ariaRowIndex++; }
        }
    }

    foreach (var row in Model.Rows.Where(r => r.GroupId == null))
    {
        @await Html.PartialAsync("~/Pages/Shared/Components/ExcelCalendarTable/_CalendarRow.cshtml", new ExcelCalendarRowViewModel
        {
            Row = row,
            Days = days,
            Today = today,
            IsReadOnly = Model.IsReadOnly,
            IsCollapsed = false,
            HasWeeklyHours = hasWeeklyHours,
            RowIndex = ariaRowIndex
        })
        @{ ariaRowIndex++; }
    }
}
else
{
    foreach (var row in Model.Rows)
    {
        @await Html.PartialAsync("~/Pages/Shared/Components/ExcelCalendarTable/_CalendarRow.cshtml", new ExcelCalendarRowViewModel
        {
            Row = row,
            Days = days,
            Today = today,
            IsReadOnly = Model.IsReadOnly,
            IsCollapsed = false,
            HasWeeklyHours = hasWeeklyHours,
            RowIndex = ariaRowIndex
        })
        @{ ariaRowIndex++; }
    }
}
```

- [ ] **Step 2: Render `aria-rowindex` in `_CalendarRow.cshtml`**

In `Pages/Shared/Components/ExcelCalendarTable/_CalendarRow.cshtml`, locate the `<tr data-row-id="@row.Id"` line (~line 18). Add the attribute:

```cshtml
<tr data-row-id="@row.Id"
    data-group-id="@(row.GroupId ?? "")"
    aria-rowindex="@Model.RowIndex"
    style="@style @hiddenStyle">
```

Add the dynamic field accessor — since the model is `dynamic`, this works without an explicit type definition.

- [ ] **Step 3: Verify aria-rowcount / aria-rowindex in DevTools**

Run: `dotnet build`. Load `/Calendar/Shifts`. In DevTools → Elements, select the `<table>`. Confirm `aria-rowcount="N"` matches the count of `<tr>` rows. Click into a row — confirm `aria-rowindex="N"` is present and sequential.

In Chrome → Accessibility tab, the row should announce as "Row N of M".

- [ ] **Step 4: Commit**

```bash
git add Pages/Shared/Components/ExcelCalendarTable/Default.cshtml Pages/Shared/Components/ExcelCalendarTable/_CalendarRow.cshtml
git commit -m "a11y(calendar): aria-rowcount + aria-rowindex for grid context

Spec §9.1. Screen-reader users now hear 'Row 23 of 156' context when
navigating cells, matching the visual cue sighted users get from
sticky positioning. Row indices include group header rows so the
count matches the rendered DOM."
```

---

### Task 14: Focus-out-of-view fix in `calendar-keyboard-nav.js`

**Implements:** Spec §9.2 — Focus management.

**Files:**
- Modify: `wwwroot/js/calendar-keyboard-nav.js`

- [ ] **Step 1: Locate `focusCell()` in `calendar-keyboard-nav.js`**

Search for `focusCell` in `wwwroot/js/calendar-keyboard-nav.js`. Note its current implementation.

- [ ] **Step 2: Add `scrollIntoView` and sticky-edge nudge**

Replace the `focusCell` function body:

```javascript
function focusCell(cell) {
    if (!cell) return;
    cell.focus();

    // Pull the cell fully into view inside the calendar's scroll container.
    // Without this, arrow-key navigation can leave the focused cell behind a
    // sticky element (date row, row label, or group band).
    cell.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: 'auto' });

    // Nudge for sticky obstruction. scrollIntoView('nearest') doesn't account
    // for the sticky elements covering the leading edges of the viewport.
    const container = cell.closest('.excel-calendar');
    if (!container) return;

    const containerRect = container.getBoundingClientRect();
    const cellRect = cell.getBoundingClientRect();
    const styles = getComputedStyle(container);
    const headerHeight = parseInt(styles.getPropertyValue('--excel-calendar-header-height') || '44', 10);
    const rowLabelEl = container.querySelector('.excel-calendar__row-label');
    const rowLabelWidth = rowLabelEl ? rowLabelEl.getBoundingClientRect().width : 150;

    // Vertical nudge: if cell top is within `headerHeight` of container top.
    const topGap = cellRect.top - (containerRect.top + headerHeight);
    if (topGap < 0) {
        container.scrollBy({ top: topGap, behavior: 'auto' });
    }

    // Horizontal nudge: respect writing direction.
    const isRtl = document.documentElement.dir === 'rtl';
    if (isRtl) {
        const rightGap = (containerRect.right - rowLabelWidth) - cellRect.right;
        if (rightGap < 0) {
            container.scrollBy({ left: -rightGap, behavior: 'auto' });
        }
    } else {
        const leftGap = cellRect.left - (containerRect.left + rowLabelWidth);
        if (leftGap < 0) {
            container.scrollBy({ left: leftGap, behavior: 'auto' });
        }
    }
}
```

- [ ] **Step 3: Build and verify keyboard navigation never leaves focus obscured**

Run: `dotnet build`. Load `/Calendar/Shifts`. Click a cell to focus, then arrow-key in all four directions. Verify:
- Tab / arrow up: focused cell never lands behind the sticky thead.
- Tab / arrow left: focused cell never lands behind the sticky row label (LTR).
- In Hebrew RTL: arrow right behaves equivalently.

- [ ] **Step 4: Commit**

```bash
git add wwwroot/js/calendar-keyboard-nav.js
git commit -m "a11y(calendar): focus never obscured by sticky elements

Spec §9.2. focusCell() now calls scrollIntoView and nudges the
scroll container if the focused cell lands within sticky-element
range of the viewport edge. Respects writing direction for the
inline-axis nudge."
```

---

### Task 15: Auto-condense toolbar (already wired in Task 10, add CSS)

**Implements:** Spec §2 (auto-condense), §5.2 (.is-condensed CSS).

**Files:**
- Modify: `wwwroot/css/calendar.css`

- [ ] **Step 1: Add `.is-condensed` rules**

In `wwwroot/css/calendar.css`, AFTER the `.cal-toolbar.is-pinned` rule (from Task 7), add:

```css
/* Auto-condense: applied by calendar-sticky-shadows.js after 200px of
   vertical scroll on the .excel-calendar viewport. Recovers ~30px of
   vertical space on small screens while keeping the toolbar reachable. */
.cal-toolbar.is-pinned.is-condensed {
    padding: var(--space-1) var(--space-2);
}

.cal-toolbar.is-pinned.is-condensed .cal-toolbar__selector label {
    display: none;
}

.cal-toolbar.is-pinned.is-condensed .nav-text {
    display: none;
}

/* The transition was added on .cal-toolbar in Task 7 — padding animates in. */
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build`. Load a calendar with enough rows to scroll 200+ px. Scroll down — the toolbar should compress (labels hide, padding reduces). Scroll back up — toolbar restores.

- [ ] **Step 3: Commit**

```bash
git add wwwroot/css/calendar.css
git commit -m "feat(calendar/sticky): auto-condense toolbar after 200px scroll

Spec §2. Once .cal-toolbar gains .is-pinned AND .is-condensed (added
by calendar-sticky-shadows.js when scrollTop > 200), labels hide and
padding compresses. Recovers ~30px on small screens. Animation via
transition declared on .cal-toolbar (Task 7)."
```

---

### Task 16: Mobile (<768px) — Tools overflow sheet

**Implements:** Spec §2 (mobile collapse), §11 (mobile markup).

**Files:**
- Modify: `wwwroot/css/calendar.css`
- Modify: `Pages/Calendar/Shifts.cshtml` (+ siblings)
- Modify: `wwwroot/js/calendar-bottom-sheet.js` (or new helper) — see step 3

- [ ] **Step 1: Add mobile breakpoint CSS for the toolbar**

In `wwwroot/css/calendar.css`, at the END of the EXCEL CALENDAR section, add:

```css
@media (max-width: 768px) {
    /* Hide secondary toolbar controls; surface them in a "Tools ▾" overflow. */
    .cal-toolbar__toggle-group:not(.cal-toolbar__primary),
    .cal-toolbar__selector:not(.cal-toolbar__primary),
    .cal-toolbar .quick-entry-toggle,
    .cal-toolbar .quick-entry-help-btn,
    .cal-toolbar .dl-control {
        display: none;
    }

    /* Show the Tools overflow trigger. */
    .cal-toolbar__overflow-trigger {
        display: inline-flex;
    }

    /* Pinned + condensed at <768px = even tighter. */
    .cal-toolbar.is-pinned.is-condensed {
        padding: var(--space-1);
    }
}

@media (min-width: 769px) {
    .cal-toolbar__overflow-trigger {
        display: none;
    }
}
```

- [ ] **Step 2: Add the "Tools ▾" button and bottom-sheet markup to each caller page**

In `Pages/Calendar/Shifts.cshtml`, locate the `cal-toolbar__row` block (~line 28). At the END of the toolbar (right before the closing `</div>` of `.cal-toolbar__row`), add:

```cshtml
<button type="button"
        class="btn btn-ghost cal-toolbar__overflow-trigger"
        loc-aria-label="Calendar_Tools_Overflow_AriaLabel"
        onclick="window.CalendarToolsOverflow && window.CalendarToolsOverflow.open()">
    <loc key="Calendar_Tools_Overflow_Label" />
    <span aria-hidden="true">▾</span>
</button>
```

At the END of the `.cal-page` content (just before its closing `</div>`), add the overflow sheet:

```cshtml
<div id="calendarToolsSheet" class="bottom-sheet calendar-tools-sheet" hidden aria-modal="true" role="dialog">
    <div class="bottom-sheet__content">
        <div class="bottom-sheet__header">
            <h3><loc key="Calendar_Tools_Overflow_Label" /></h3>
            <button type="button" class="bottom-sheet__close"
                    loc-aria-label="Aria_CloseDialog"
                    onclick="window.CalendarToolsOverflow && window.CalendarToolsOverflow.close()">×</button>
        </div>
        <div class="bottom-sheet__body" id="calendarToolsSheetBody">
            @* Cloned controls are injected here at open() time so server state stays sourceOfTruth *@
        </div>
    </div>
</div>
```

Repeat in OnCall.cshtml, Chores.cshtml, Overview.cshtml.

- [ ] **Step 3: Add a small JS helper for the sheet (clone-and-show pattern)**

Create `wwwroot/js/calendar-tools-overflow.js`:

```javascript
// calendar-tools-overflow.js
// Mobile (<768px) Tools overflow sheet. Clones the hidden toolbar controls
// into the sheet on open() so the original elements remain the source of
// truth (their event handlers, form bindings, and IDs are untouched).
//
// On close(), the clones are dropped — the originals stay hidden via CSS.

(function () {
    'use strict';

    const SHEET_ID = 'calendarToolsSheet';
    const SHEET_BODY_ID = 'calendarToolsSheetBody';

    const HIDDEN_SELECTORS = [
        '.cal-toolbar__toggle-group:not(.cal-toolbar__primary)',
        '.cal-toolbar__selector:not(.cal-toolbar__primary)',
        '.cal-toolbar .quick-entry-toggle',
        '.cal-toolbar .quick-entry-help-btn',
        '.cal-toolbar .dl-control',
    ];

    function open() {
        const sheet = document.getElementById(SHEET_ID);
        const body = document.getElementById(SHEET_BODY_ID);
        if (!sheet || !body) return;

        body.innerHTML = '';
        HIDDEN_SELECTORS.forEach((sel) => {
            document.querySelectorAll(sel).forEach((el) => {
                const clone = el.cloneNode(true);
                clone.removeAttribute('id');     // avoid duplicate IDs
                body.appendChild(clone);
            });
        });

        sheet.hidden = false;
        sheet.classList.add('bottom-sheet--open');
        document.body.classList.add('bottom-sheet-open');
    }

    function close() {
        const sheet = document.getElementById(SHEET_ID);
        const body = document.getElementById(SHEET_BODY_ID);
        if (!sheet) return;

        sheet.classList.remove('bottom-sheet--open');
        document.body.classList.remove('bottom-sheet-open');
        sheet.hidden = true;
        if (body) body.innerHTML = '';
    }

    window.CalendarToolsOverflow = { open, close };
})();
```

Add `<script src="~/js/calendar-tools-overflow.js" asp-append-version="true"></script>` to each caller page's `@section Scripts` block.

- [ ] **Step 4: Verify mobile collapse**

Run: `dotnet build`. Open Chrome DevTools → toggle device toolbar → set viewport to 414×896 (iPhone). Load `/Calendar/Shifts`. Verify:
- Mode toggles, Filter, JustMine, DL dropdown are HIDDEN.
- Date nav, view mode, molecule selector are still visible.
- A "Tools ▾" button appears.
- Clicking "Tools ▾" opens a bottom sheet with the hidden controls.
- Selecting an option (e.g., toggling JustMine) works — because the original element is the source of truth and the clone preserves the `onclick`.
- Closing the sheet via × works.

Test at 768px (tablet) — overflow should disappear and original controls return.

- [ ] **Step 5: Commit**

```bash
git add wwwroot/css/calendar.css wwwroot/js/calendar-tools-overflow.js Pages/Calendar/Shifts.cshtml Pages/Calendar/OnCall.cshtml Pages/Calendar/Chores.cshtml Pages/Calendar/Overview.cshtml
git commit -m "feat(calendar/mobile): Tools overflow sheet at <768px

Spec §2 mobile collapse. Below 768px, secondary toolbar controls
(mode toggles, DL, Filter, JustMine, quick entry) hide from the
sticky bar and are surfaced in a bottom-sheet 'Tools ▾' overflow.

Clone-and-show pattern keeps the originals as source of truth —
event handlers and form bindings are unmodified. Recovers ~80px
of vertical viewport space on phones."
```

---

### Task 17: Group count `(visible/total)` recompute on filter

**Implements:** Spec §2 (group count format).

**Files:**
- Modify: `Pages/Shared/Components/ExcelCalendarTable/Default.cshtml`
- Modify: `wwwroot/js/excel-calendar-groups.js`
- Modify: `Pages/Calendar/Shifts.cshtml` (filter handler)

- [ ] **Step 1: Mark the group count span with a data attribute and store the total**

In `Pages/Shared/Components/ExcelCalendarTable/Default.cshtml`, locate the `.excel-calendar__group-count` span (~line 87):

```cshtml
@if (group.MemberCount > 0)
{
    <span class="excel-calendar__group-count">(@group.MemberCount)</span>
}
```

Replace with:

```cshtml
@if (group.MemberCount > 0)
{
    <span class="excel-calendar__group-count"
          data-group-total="@group.MemberCount">(@group.MemberCount)</span>
}
```

- [ ] **Step 2: Add `updateGroupCounts()` to `excel-calendar-groups.js`**

In `wwwroot/js/excel-calendar-groups.js`, append:

```javascript
/**
 * Recompute (visible/total) for each group band based on which child rows
 * are currently visible. Called from applyFilter / clearFilter in
 * Pages/Calendar/Shifts.cshtml (and siblings).
 *
 * - If all rows of a group are visible, show "(total)"
 * - If some rows are hidden, show "(visible/total)"
 */
window.updateGroupCounts = function () {
    const counts = document.querySelectorAll('.excel-calendar__group-count[data-group-total]');
    counts.forEach((countEl) => {
        const band = countEl.closest('.excel-calendar__group-header');
        if (!band) return;
        const groupId = band.getAttribute('data-group-id');
        const total = parseInt(countEl.getAttribute('data-group-total'), 10);
        const rows = document.querySelectorAll(
            `tr[data-group-id="${groupId}"]:not(.excel-calendar__group-header)`
        );
        const visible = Array.from(rows).filter(
            (r) => r.style.display !== 'none' && !r.hasAttribute('hidden')
        ).length;
        countEl.textContent = visible === total ? `(${total})` : `(${visible}/${total})`;
    });
};
```

- [ ] **Step 3: Call `updateGroupCounts()` from `applyFilter` / `clearFilter`**

In `Pages/Calendar/Shifts.cshtml`, locate the `applyFilter` function (~line 521). At the END of the function body (after `closeFilterModal()`), add:

```javascript
            window.updateGroupCounts && window.updateGroupCounts();
```

Similarly in `clearFilter` (~line 537), after `closeFilterModal()`:

```javascript
            window.updateGroupCounts && window.updateGroupCounts();
```

Repeat the same insertions in `OnCall.cshtml`, `Chores.cshtml`, `Overview.cshtml` if they have equivalent filter functions.

- [ ] **Step 4: Verify the count updates**

Run: `dotnet build`. Load `/Calendar/Shifts` in a config with at least one group of 5+ members. Open the Filter modal, search for a substring matching only some members. Apply.

Verify the group band's count reads `(visible/total)`, e.g., `(2/6)`. Click "Clear" — count returns to `(6)`.

- [ ] **Step 5: Commit**

```bash
git add Pages/Shared/Components/ExcelCalendarTable/Default.cshtml wwwroot/js/excel-calendar-groups.js Pages/Calendar/Shifts.cshtml Pages/Calendar/OnCall.cshtml Pages/Calendar/Chores.cshtml Pages/Calendar/Overview.cshtml
git commit -m "feat(calendar/sticky): (visible/total) group count when filter active

Spec §2. Group bands now show (visible/total) when filter has hidden
some members, (total) otherwise. Recomputed on applyFilter / clearFilter.
data-group-total preserves the original count so the math doesn't
drift across multiple filter cycles."
```

---

### Task 18: "Next group ↓" jump affordance

**Implements:** Spec §2 (next group), §5.4 (Zone C affordance).

**Files:**
- Modify: `Pages/Shared/Components/ExcelCalendarTable/Default.cshtml`
- Modify: `wwwroot/css/calendar.css`
- Modify: `wwwroot/js/calendar-sticky-shadows.js` (add click handler)

- [ ] **Step 1: Render the jump button inside each band (server-side conditional)**

In `Pages/Shared/Components/ExcelCalendarTable/Default.cshtml`, locate the group-header `<tr>` (~line 81). Compute "has next group" upstream of the loop:

```cshtml
@{
    var sortedGroups = Model.Groups.OrderBy(g => g.SortOrder).ToList();
}
@if (Model.Groups != null)
{
    for (var gi = 0; gi < sortedGroups.Count; gi++)
    {
        var group = sortedGroups[gi];
        var hasNextGroup = gi < sortedGroups.Count - 1;
        var nextGroupId = hasNextGroup ? sortedGroups[gi + 1].Id : "";

        <tr class="excel-calendar__group-header @(group.IsCollapsed ? "is-collapsed" : "")"
            data-group-id="@group.Id"
            aria-rowindex="@ariaRowIndex">
            <td colspan="@totalColCount" style="@(group.Color != null ? $"--group-color: {group.Color}" : "")">
                <div class="excel-calendar__group-toggle">
                    <span class="excel-calendar__group-chevron @(group.IsCollapsed ? "excel-calendar__group-chevron--collapsed" : "")">▼</span>
                    <span class="excel-calendar__group-name" data-editable="true">@group.Name</span>
                    @if (group.MemberCount > 0)
                    {
                        <span class="excel-calendar__group-count" data-group-total="@group.MemberCount">(@group.MemberCount)</span>
                    }
                    <span class="excel-calendar__group-grip" title="@Localizer["DragToReorder"]">⋮⋮</span>
                    @if (hasNextGroup)
                    {
                        <button type="button"
                                class="excel-calendar__next-group-btn"
                                data-next-group="@nextGroupId"
                                loc-aria-label="Calendar_NextGroup"
                                loc-title="Calendar_NextGroup">
                            ↓
                        </button>
                    }
                </div>
            </td>
        </tr>
        @{ ariaRowIndex++; }

        @* ... existing row loop ... *@
    }

    @* ... ungrouped rows fall-through ... *@
}
```

(Adapt the existing `foreach (var group in Model.Groups.OrderBy(...))` to a `for` loop to know the index.)

- [ ] **Step 2: Style the button**

In `wwwroot/css/calendar.css`, AFTER the group-header rules, add:

```css
.excel-calendar__next-group-btn {
    margin-inline-start: auto;       /* push to the inline-end of the band */
    background: transparent;
    border: 1px solid var(--border);
    border-radius: var(--radius-sm);
    color: inherit;
    padding: 0 var(--space-2);
    font-size: var(--text-small);
    cursor: pointer;
    transition: background var(--transition-fast),
                border-color var(--transition-fast);
}

.excel-calendar__next-group-btn:hover {
    background: var(--surface);
    border-color: var(--border-strong);
}

.excel-calendar__next-group-btn:focus-visible {
    outline: 2px solid var(--primary);
    outline-offset: 2px;
}
```

- [ ] **Step 3: Add click handler in `calendar-sticky-shadows.js`**

At the END of `wwwroot/js/calendar-sticky-shadows.js`, before the closing `})();`:

```javascript
    // Next-group jump. Delegated handler — survives bands being toggled.
    document.addEventListener('click', function (e) {
        const btn = e.target.closest('.excel-calendar__next-group-btn');
        if (!btn) return;
        e.preventDefault();
        e.stopPropagation();   // don't trigger the band's chevron-toggle

        const nextId = btn.getAttribute('data-next-group');
        const target = document.querySelector(
            `.excel-calendar__group-header[data-group-id="${nextId}"]`
        );
        if (!target) return;

        const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
        target.scrollIntoView({
            block: 'start',
            behavior: reducedMotion ? 'auto' : 'smooth',
        });
    });
```

- [ ] **Step 4: Verify**

Run: `dotnet build`. Load a calendar page with at least 2 groups. Each group band (except the last) should show a small "↓" button on its inline-end. Click it — the calendar smoothly scrolls so the next group's band is at the top of the viewport.

In `prefers-reduced-motion: reduce` (Chrome DevTools → Rendering → "Emulate CSS media feature prefers-reduced-motion"), the scroll is instantaneous.

Verify the chevron click (collapse/expand) still works independently — the next-group button must not bubble up and trigger the chevron.

- [ ] **Step 5: Commit**

```bash
git add Pages/Shared/Components/ExcelCalendarTable/Default.cshtml wwwroot/css/calendar.css wwwroot/js/calendar-sticky-shadows.js
git commit -m "feat(calendar/sticky): next-group jump affordance in sticky bands

Spec §2 next-group affordance, §5.4. A '↓' button appears inside each
group band (except the last) pinned by Zone C sticky. Click scrolls
the next group's band to top with smooth motion; respects
prefers-reduced-motion. Delegated handler with stopPropagation so the
chevron collapse/expand remains independent."
```

---

### Task 19: Pre-existing weekend dark-mode contrast fix

**Implements:** Spec §5.5 — Weekend dark-mode contrast fix.

**Files:**
- Modify: `wwwroot/css/tokens.css`
- Modify: `wwwroot/css/calendar.css`

- [ ] **Step 1: Add `--primary-strong` token if absent**

Open `wwwroot/css/tokens.css`. Search for `--primary-strong`. If absent, add to `:root` (near `--primary`):

```css
    /* Darker variant of --primary for dark-mode surfaces that need WCAG AA
       contrast against white text (e.g. sticky weekend column headers).
       Light mode uses --primary directly; this token is used only inside
       [data-theme="dark"] overrides. */
    --primary-strong: #2A5A8F;
```

(If the token already exists, verify its dark-mode value is a contrast-passing color. If not, set it to `#2A5A8F` which gives 6.1:1 against white.)

- [ ] **Step 2: Add the dark-mode weekend override**

In `wwwroot/css/calendar.css`, locate `.excel-calendar__header-day--weekend` (around line 2634). AFTER its existing rule, add:

```css
/* Dark-mode fix: --primary-hover (#7AB3E3) gives 2.4:1 against white — fails
   AA. The sticky thead makes this bug always visible. Override with
   --primary-strong (#2A5A8F) → 6.1:1, passes AA. */
[data-theme="dark"] .excel-calendar__header-day--weekend {
    background: var(--primary-strong);
}
```

- [ ] **Step 3: Verify contrast in dark mode**

Run: `dotnet build`. Load `/Calendar/Shifts` in month view (so weekend columns are highlighted). Switch theme to Dark. Use Chrome DevTools → Lighthouse → Accessibility audit, OR manually use the color picker on the weekend header to verify contrast.

White (`#FFFFFF`) on `#2A5A8F` should report ~6.1:1 — passes WCAG AA (≥4.5:1 for normal text).

- [ ] **Step 4: Commit**

```bash
git add wwwroot/css/tokens.css wwwroot/css/calendar.css
git commit -m "a11y(calendar): fix dark-mode weekend column contrast (WCAG AA)

Spec §5.5. .excel-calendar__header-day--weekend was using
--primary-hover (#7AB3E3) in dark mode, producing 2.4:1 against
white text — fails WCAG AA. Switched to --primary-strong (#2A5A8F)
→ 6.1:1. Was pre-existing; sticky overhaul makes the bug always
visible so it ships in the same PR per spec §2."
```

---

### Task 20: Playwright visual regression baselines + verification matrix

**Implements:** Spec §13 — Testing & verification.

**Files:**
- Create: `tests/visual/sticky-headers/baseline.spec.js` (Playwright spec)
- Use the `webapp-testing` skill to drive Playwright

- [ ] **Step 1: Invoke the `webapp-testing` skill to set up Playwright**

Use the Skill tool: `webapp-testing` with args describing the goal: "Capture sticky-state baselines for /Calendar/Shifts, /Calendar/OnCall, /Calendar/Chores, /Calendar/Overview in 4 scroll states × 2 themes × 2 languages."

The skill will set up Playwright, navigate the running app, and capture screenshots.

- [ ] **Step 2: Write the baseline test**

Create `tests/visual/sticky-headers/baseline.spec.js`:

```javascript
const { test, expect } = require('@playwright/test');

const CALENDARS = [
    { slug: 'shifts', url: '/Calendar/Shifts' },
    { slug: 'oncall', url: '/Calendar/OnCall' },
    { slug: 'chores', url: '/Calendar/Chores' },
    { slug: 'overview', url: '/Calendar/Overview' },
];

const THEMES = ['light', 'dark'];
const LANGS = ['en', 'he'];

const SCROLL_STATES = [
    { name: 'rest', dx: 0, dy: 0 },
    { name: 'scrolled-right', dx: 1000, dy: 0 },
    { name: 'scrolled-down', dx: 0, dy: 600 },
    { name: 'scrolled-both', dx: 1000, dy: 600 },
];

for (const cal of CALENDARS) {
    for (const theme of THEMES) {
        for (const lang of LANGS) {
            for (const scroll of SCROLL_STATES) {
                test(`${cal.slug} ${theme} ${lang} ${scroll.name}`, async ({ page }) => {
                    // Set language cookie
                    await page.context().addCookies([{
                        name: '.AspNetCore.Culture',
                        value: lang === 'he' ? 'c=he-IL|uic=he-IL' : 'c=en-US|uic=en-US',
                        url: 'http://localhost:5000',
                    }]);
                    // Set theme via localStorage
                    await page.addInitScript((t) => {
                        localStorage.setItem('theme', t);
                    }, theme);
                    await page.goto(cal.url);
                    await page.waitForSelector('.excel-calendar');
                    const calEl = await page.locator('.excel-calendar');
                    await calEl.evaluate((el, s) => {
                        el.scrollLeft = s.dx;
                        el.scrollTop = s.dy;
                    }, scroll);
                    // Allow IntersectionObserver to settle
                    await page.waitForTimeout(150);
                    await expect(page).toHaveScreenshot(
                        `${cal.slug}-${theme}-${lang}-${scroll.name}.png`,
                        { maxDiffPixelRatio: 0.005 }
                    );
                });
            }
        }
    }
}
```

- [ ] **Step 3: Run the test to capture initial baselines**

Run the Playwright test once. The first run captures baselines (no comparison), creating `*.png` files under `tests/visual/sticky-headers/baseline.spec.js-snapshots/`.

64 baseline images total (4 cals × 2 themes × 2 langs × 4 scroll states).

- [ ] **Step 4: Walk the manual verification matrix from spec §13.2**

Execute the matrix from spec §13.2 by hand. For each cell of the matrix, screenshot and add to the PR description. Spend extra time on:
- Hebrew RTL — verify row labels visually stick to the right.
- Dark mode — verify contrast on header, group bands, mode token.
- 414×896 viewport — verify Tools overflow sheet opens and works.
- Single-group calendar — verify band doesn't visually pin (natural degradation).
- Long-group calendar — verify "Next group" appears and works.
- Read-only mode — verify banner stays visible when scrolling.

- [ ] **Step 5: Commit baselines and matrix evidence**

```bash
git add tests/visual/sticky-headers/
git commit -m "test(calendar/sticky): Playwright visual regression baselines

Spec §13. Captures 64 baselines (4 calendars × 2 themes × 2 languages
× 4 scroll states). Comparison threshold maxDiffPixelRatio: 0.005.

Manual verification matrix (spec §13.2) executed; screenshots attached
to PR description per Definition of Done (spec §16)."
```

---

### Task 21: Update MEMORY.md with sticky discipline

**Implements:** Spec §16 — DoD memory updates.

**Files:**
- Create: `C:/Users/katzi/.claude/projects/C--Users-katzi-Downloads-ShiftManager/memory/calendar_sticky_layout_invariant.md`
- Modify: `C:/Users/katzi/.claude/projects/C--Users-katzi-Downloads-ShiftManager/memory/MEMORY.md`

- [ ] **Step 1: Write the topic file**

Create `C:/Users/katzi/.claude/projects/C--Users-katzi-Downloads-ShiftManager/memory/calendar_sticky_layout_invariant.md`:

```markdown
---
name: calendar-sticky-layout-invariant
description: How calendar sticky headers depend on the flex-column min-height chain, plus the z-index/RTL/shadow discipline that future edits must respect
metadata:
  type: project
---

The four calendar pages (Shifts, OnCall, Chores, Overview) share one shared
component `Pages/Shared/Components/ExcelCalendarTable/`. Its sticky behavior
depends on a load-bearing flex-column chain:

`.app-shell (height: 100dvh) → .app-main (min-height: 0) → .app-content (display: flex; flex-direction: column; min-height: 0) → .cal-page (display: flex; flex-direction: column; min-height: 0; flex: 1) → .excel-calendar (flex: 1 1 auto; min-height: 0; overflow: auto)`

Every link must keep `min-height: 0`. If any link loses it, the flex chain
collapses and the body becomes the scroll authority — sticky breaks silently.

**Z-index tier convention** (locked by `CalendarStickyTokenTests`):
- `--z-sticky-col: 1019` — sticky left column under header
- `--z-sticky: 1020` — default sticky / toolbars
- `--z-sticky-group: 1021` — in-body group bands
- `--z-sticky-header: 1022` — table header above body bands
- `--z-sticky-corner: 1023` — two-axis intersection above all
- `--z-dropdown: 1060` — bumped above sticky-corner so dropdowns survive

**RTL discipline** (locked by `CalendarStickyRtlSweepTests`): every sticky offset
in `calendar.css` uses `inset-inline-start` / `inset-inline-end`, NEVER
`left` / `right`. Exception: `[dir="rtl"]` overrides for `box-shadow` ARE
allowed because `box-shadow` has no logical equivalent. Inline-axis shadows
have a `-rtl` token variant (`--shadow-sticky-inline-rtl`) and explicit
`[dir="rtl"]` consumers.

**Stacking context invariant**: `.excel-calendar` must NOT have `z-index` set
or `isolation: isolate` applied. Either would isolate the entire calendar
from `.cal-toolbar` (sibling) and break the layered sticky story.

**Spec / plan**: see `docs/superpowers/specs/2026-05-29-calendar-sticky-headers-design.md`
and `docs/superpowers/plans/2026-05-29-calendar-sticky-headers.md`.

Related: [[grant_change_checklist]] for the same-discipline pattern (multiple
files touch together as one cohesive change).
```

- [ ] **Step 2: Add the index entry to `MEMORY.md`**

Open `C:/Users/katzi/.claude/projects/C--Users-katzi-Downloads-ShiftManager/memory/MEMORY.md`. In the appropriate section (CSS Patterns & Guidelines), add ONE line:

```markdown
- [Calendar sticky layout invariant](calendar_sticky_layout_invariant.md) — Flex-column `min-height: 0` chain is load-bearing; z-index tiers `--z-sticky-col/group/header/corner`; RTL uses `inset-inline-*` + `[dir="rtl"]` shadow flips; `.excel-calendar` must NOT have its own stacking context. Tests: `CalendarStickyTokenTests`, `CalendarStickyRtlSweepTests`.
```

- [ ] **Step 3: No commit — memory files live outside the repo**

The memory directory `~/.claude/projects/...` is the user's personal Claude state. It is not part of the repository and not committed.

---

## Self-review

### Spec coverage

Every spec section maps to at least one task:

| Spec section | Task(s) |
|---|---|
| §1 Goal | Tasks 4–18 collectively |
| §2 Locked product decisions | Tasks 7 (scroll model, sticky zones, banner), 11 (group band, collapsed), 9 (edge shadows), 4 (magic-number), 8 (RTL), 15 (auto-condense), 16 (mobile), 18 (next group), 12 (mode token), 17 (group count), 2 (DL bug), 19 (weekend) |
| §3 Scope | implicit in task ordering and explicit no-touch list in File Structure |
| §4 Layout chain | Task 4 |
| §5.1 Zone B | Task 5 (diagnose), Task 7 (token swap in Step 2), Task 8 (RTL), Task 9 (shadow) |
| §5.2 Toolbar | Task 7 |
| §5.3 Readonly banner | Tasks 6 (extract) + 7 (sticky CSS) |
| §5.4 Group bands | Task 11 |
| §5.5 Zone A header + weekend bug | Task 9 (shadow/border-1px), Task 19 (contrast fix) |
| §5.6 Corner mode token | Task 12 |
| §6 Z-index plan | Tasks 1, 2, 9 (consumer swap) |
| §7 Shadows + motion + forced-colors | Tasks 1 (tokens), 9 (consumers + forced-colors), 10 (JS toggle) |
| §8 RTL | Task 8 |
| §9 Accessibility | Tasks 12 (rowcount + corner label), 13 (rowindex), 14 (focus), 9 (forced-colors) |
| §10 Localization | Task 3 |
| §11 File inventory | distributed across Tasks 1–21 |
| §12 Implementation phases | matches Task ordering |
| §13 Testing & verification | Task 20 |
| §14 Risks | considered during implementation; explicit risk #6 (DL bump audit) is covered by Task 2 |
| §15 Out-of-scope follow-ups | explicit no-touch list in File Structure |
| §16 Definition of done | Tasks 20 (Playwright + matrix), 21 (memory) cover the last bullets |

No gaps identified.

### Placeholder scan

- Task 1 Step 1: contains a deliberate `TrsetimEnd` typo with an inline note instructing the implementer to fix to `TrimEnd`. This is intentional (sanity check) and explicitly called out — NOT a placeholder.
- Task 5: this is an investigation task with a documented diagnostic procedure. Steps 3–4 describe the diagnostic outcomes and fix shapes (a)–(e). Not placeholder.
- Step 4 commit message in Task 5 has `<ONE-LINE SUMMARY>` placeholders — these are intentionally for the implementer to fill in based on what Step 3 actually finds. Acceptable per spec §16 (root cause must be documented).
- No `TBD`, `TODO without code`, or unspecific "implement appropriate error handling" found.

### Type / signature consistency

- `RowMode` is PascalCase string throughout (`"Shifts"`, `"Users"`, `"Duty"`, `"Chores"`). ✓
- `RowModeLabelKey` expression `$"Calendar_RowMode_{RowMode}"` matches resx keys. ✓
- `TotalRows` formula `Rows.Count + (Groups?.Count ?? 0)` consistent. ✓
- `--cal-toolbar-height` CSS variable referenced same way in calendar.css (Task 7) and calendar-sticky-shadows.js (Task 10). ✓
- `.is-pinned` class used for both toolbar (Task 7, 10) and group bands (Task 11, 10). Same class, two different consumers — distinguished by selector context (`.cal-toolbar.is-pinned` vs `.excel-calendar__group-header.is-pinned`). ✓
- `.is-collapsed` class set both server-side (Default.cshtml Task 11 Step 2) and client-side (excel-calendar-groups.js Task 11 Step 2). ✓
- `data-group-id` attribute matched in CSS selectors and JS queries throughout. ✓
- `data-group-total` introduced in Task 17 Step 1, consumed in Task 17 Step 2. ✓
- `data-next-group` introduced in Task 18 Step 1, consumed in Task 18 Step 3. ✓

### Scope check

This plan is for a single subsystem (the shared `ExcelCalendarTable` component and its supporting CSS/JS). Each task produces a working, testable intermediate state — stopping at any task N leaves a green build and a clean checkout. No sub-project decomposition needed.

---

## Execution Handoff

**Plan complete and saved to `docs/superpowers/plans/2026-05-29-calendar-sticky-headers.md`.** Two execution options:

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration. Best for this plan because the tasks are independent and the spec is dense — fresh-context subagents won't accumulate context drift across 21 tasks.

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints for review. Fewer dispatches, but this session's context will grow large by Task 10 or so.

**Which approach?**
