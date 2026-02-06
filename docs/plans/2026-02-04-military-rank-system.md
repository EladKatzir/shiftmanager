# Military Rank System Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Implement the Military Rank system to enable rank-based eligibility for on-duty assignments, particularly enforcing officer rank requirements for Katzin duty.

**Architecture:** Add `MilitaryRank` enum and `Rank` property to `AppUser`. Create eligibility service methods that check rank requirements. Integrate with OnDutyService to filter eligible users. Add UI for rank display and selection.

**Tech Stack:** C# ASP.NET Core, Entity Framework Core, Razor Pages, PostgreSQL

---

## Goals

- Add military rank attribute to all users
- Enforce rank-based eligibility for Katzin on-duty assignments
- Display rank in user profiles and on-duty UI
- Support rank filtering in eligible assignee lists
- Maintain backward compatibility (existing users default to lowest rank)

## Non-Goals

- Rank-based pay/compensation calculation
- Automatic rank progression
- Rank ceremony notifications
- Historical rank tracking (promotions/demotions log)
- Rank-based calendar visibility (out of scope for this feature)

---

## Task 1: Create MilitaryRank Enum

**Files:**
- Create: `Models/Support/MilitaryRank.cs`

**Step 1: Create the enum file**

```csharp
namespace ShiftManager.Models.Support;

/// <summary>
/// IDF military rank system for eligibility checks.
/// Enlisted ranks are 0-8, Officers are 9+.
/// </summary>
public enum MilitaryRank
{
    // Enlisted (0-8)
    Turai = 0,              // טוראי - Private
    TuraiRishon = 1,        // טוראי ראשון - Private First Class
    RavTurai = 2,           // רב טוראי - Corporal
    Samal = 3,              // סמל - Sergeant
    SamalRishon = 4,        // סמל ראשון - Staff Sergeant
    RavSamal = 5,           // רב סמל - Sergeant First Class
    RavSamalMitkadam = 6,   // רב סמל מתקדם - Master Sergeant
    RavSamalBakhir = 7,     // רב סמל בכיר - Senior Master Sergeant
    RavNagad = 8,           // רב נגד - Warrant Officer

    // Officers (9+)
    SegenMishne = 9,        // סגן משנה - Second Lieutenant
    Segen = 10,             // סגן - Lieutenant
    Seren = 11,             // סרן - Captain
    RavSeren = 12,          // רב סרן - Major
    SganAluf = 13,          // סגן אלוף - Lieutenant Colonel
    AlufMishne = 14,        // אלוף משנה - Colonel
    TatAluf = 15,           // תת אלוף - Brigadier General
    Aluf = 16,              // אלוף - Major General
    RavAluf = 17            // רב אלוף - Lieutenant General
}
```

**Step 2: Commit**

```bash
git add Models/Support/MilitaryRank.cs
git commit -m "feat: add MilitaryRank enum with IDF ranks"
```

---

## Task 2: Create Rank Extension Methods

**Files:**
- Create: `Models/Support/MilitaryRankExtensions.cs`
- Test: `ShiftManager.Tests/UnitTests/Models/MilitaryRankExtensionsTests.cs`

**Step 1: Write failing tests**

```csharp
using FluentAssertions;
using ShiftManager.Models.Support;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Models;

public class MilitaryRankExtensionsTests
{
    [Theory]
    [InlineData(MilitaryRank.Turai, false)]
    [InlineData(MilitaryRank.RavNagad, false)]
    [InlineData(MilitaryRank.SegenMishne, true)]
    [InlineData(MilitaryRank.RavAluf, true)]
    public void IsOfficer_ReturnsCorrectValue(MilitaryRank rank, bool expected)
    {
        rank.IsOfficer().Should().Be(expected);
    }

    [Theory]
    [InlineData(MilitaryRank.Turai, true)]
    [InlineData(MilitaryRank.RavTurai, true)]
    [InlineData(MilitaryRank.Samal, false)]
    [InlineData(MilitaryRank.SegenMishne, false)]
    public void IsEnlisted_ReturnsCorrectValue(MilitaryRank rank, bool expected)
    {
        rank.IsEnlisted().Should().Be(expected);
    }

    [Theory]
    [InlineData(MilitaryRank.Samal, true)]
    [InlineData(MilitaryRank.RavNagad, true)]
    [InlineData(MilitaryRank.Turai, false)]
    [InlineData(MilitaryRank.SegenMishne, false)]
    public void IsNCO_ReturnsCorrectValue(MilitaryRank rank, bool expected)
    {
        rank.IsNCO().Should().Be(expected);
    }

    [Theory]
    [InlineData(MilitaryRank.Turai, "he", "טוראי")]
    [InlineData(MilitaryRank.Seren, "he", "סרן")]
    [InlineData(MilitaryRank.Turai, "en", "Private")]
    [InlineData(MilitaryRank.Seren, "en", "Captain")]
    public void GetDisplayName_ReturnsLocalizedName(MilitaryRank rank, string lang, string expected)
    {
        rank.GetDisplayName(lang).Should().Be(expected);
    }

    [Theory]
    [InlineData(MilitaryRank.Turai, "טר'")]
    [InlineData(MilitaryRank.Samal, "סמל")]
    [InlineData(MilitaryRank.Seren, "סרן")]
    public void GetAbbreviation_ReturnsShortForm(MilitaryRank rank, string expected)
    {
        rank.GetAbbreviation().Should().Be(expected);
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test ShiftManager.Tests --filter "FullyQualifiedName~MilitaryRankExtensionsTests" -v n`
Expected: FAIL - methods don't exist

**Step 3: Implement extension methods**

```csharp
namespace ShiftManager.Models.Support;

public static class MilitaryRankExtensions
{
    /// <summary>
    /// Returns true if rank is Officer level (Segen Mishne and above).
    /// </summary>
    public static bool IsOfficer(this MilitaryRank rank) => (int)rank >= 9;

    /// <summary>
    /// Returns true if rank is NCO level (Samal through Rav Nagad).
    /// </summary>
    public static bool IsNCO(this MilitaryRank rank) => (int)rank >= 3 && (int)rank <= 8;

    /// <summary>
    /// Returns true if rank is basic enlisted (Turai through Rav Turai).
    /// </summary>
    public static bool IsEnlisted(this MilitaryRank rank) => (int)rank < 3;

    /// <summary>
    /// Gets localized display name for the rank.
    /// </summary>
    public static string GetDisplayName(this MilitaryRank rank, string language = "he")
    {
        return language == "he" ? GetHebrewName(rank) : GetEnglishName(rank);
    }

    /// <summary>
    /// Gets abbreviated form (Hebrew).
    /// </summary>
    public static string GetAbbreviation(this MilitaryRank rank)
    {
        return rank switch
        {
            MilitaryRank.Turai => "טר'",
            MilitaryRank.TuraiRishon => "טר\"ר",
            MilitaryRank.RavTurai => "רב\"ט",
            MilitaryRank.Samal => "סמל",
            MilitaryRank.SamalRishon => "סמ\"ר",
            MilitaryRank.RavSamal => "רס\"ל",
            MilitaryRank.RavSamalMitkadam => "רס\"מ",
            MilitaryRank.RavSamalBakhir => "רס\"ב",
            MilitaryRank.RavNagad => "רנ\"ג",
            MilitaryRank.SegenMishne => "סג\"מ",
            MilitaryRank.Segen => "סג'",
            MilitaryRank.Seren => "סרן",
            MilitaryRank.RavSeren => "רס\"ן",
            MilitaryRank.SganAluf => "סא\"ל",
            MilitaryRank.AlufMishne => "אל\"מ",
            MilitaryRank.TatAluf => "תא\"ל",
            MilitaryRank.Aluf => "אלוף",
            MilitaryRank.RavAluf => "רא\"ל",
            _ => rank.ToString()
        };
    }

    private static string GetHebrewName(MilitaryRank rank)
    {
        return rank switch
        {
            MilitaryRank.Turai => "טוראי",
            MilitaryRank.TuraiRishon => "טוראי ראשון",
            MilitaryRank.RavTurai => "רב טוראי",
            MilitaryRank.Samal => "סמל",
            MilitaryRank.SamalRishon => "סמל ראשון",
            MilitaryRank.RavSamal => "רב סמל",
            MilitaryRank.RavSamalMitkadam => "רב סמל מתקדם",
            MilitaryRank.RavSamalBakhir => "רב סמל בכיר",
            MilitaryRank.RavNagad => "רב נגד",
            MilitaryRank.SegenMishne => "סגן משנה",
            MilitaryRank.Segen => "סגן",
            MilitaryRank.Seren => "סרן",
            MilitaryRank.RavSeren => "רב סרן",
            MilitaryRank.SganAluf => "סגן אלוף",
            MilitaryRank.AlufMishne => "אלוף משנה",
            MilitaryRank.TatAluf => "תת אלוף",
            MilitaryRank.Aluf => "אלוף",
            MilitaryRank.RavAluf => "רב אלוף",
            _ => rank.ToString()
        };
    }

    private static string GetEnglishName(MilitaryRank rank)
    {
        return rank switch
        {
            MilitaryRank.Turai => "Private",
            MilitaryRank.TuraiRishon => "Private First Class",
            MilitaryRank.RavTurai => "Corporal",
            MilitaryRank.Samal => "Sergeant",
            MilitaryRank.SamalRishon => "Staff Sergeant",
            MilitaryRank.RavSamal => "Sergeant First Class",
            MilitaryRank.RavSamalMitkadam => "Master Sergeant",
            MilitaryRank.RavSamalBakhir => "Senior Master Sergeant",
            MilitaryRank.RavNagad => "Warrant Officer",
            MilitaryRank.SegenMishne => "Second Lieutenant",
            MilitaryRank.Segen => "Lieutenant",
            MilitaryRank.Seren => "Captain",
            MilitaryRank.RavSeren => "Major",
            MilitaryRank.SganAluf => "Lieutenant Colonel",
            MilitaryRank.AlufMishne => "Colonel",
            MilitaryRank.TatAluf => "Brigadier General",
            MilitaryRank.Aluf => "Major General",
            MilitaryRank.RavAluf => "Lieutenant General",
            _ => rank.ToString()
        };
    }
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test ShiftManager.Tests --filter "FullyQualifiedName~MilitaryRankExtensionsTests" -v n`
Expected: PASS

**Step 5: Commit**

```bash
git add Models/Support/MilitaryRankExtensions.cs ShiftManager.Tests/UnitTests/Models/MilitaryRankExtensionsTests.cs
git commit -m "feat: add MilitaryRank extension methods with tests"
```

---

## Task 3: Add Rank Property to AppUser

**Files:**
- Modify: `Models/AppUser.cs`
- Create: `Migrations/YYYYMMDDHHMMSS_AddMilitaryRankToAppUser.cs` (via EF migration)

**Step 1: Add Rank property to AppUser**

In `Models/AppUser.cs`, add after the `Certifications` property (around line 30):

```csharp
    // Military Rank (for eligibility checks)
    /// <summary>
    /// User's military rank. Used for duty eligibility (e.g., Katzin requires officer rank).
    /// Defaults to Turai (lowest enlisted rank) for backward compatibility.
    /// </summary>
    public MilitaryRank Rank { get; set; } = MilitaryRank.Turai;
```

**Step 2: Create migration**

Run: `dotnet ef migrations add AddMilitaryRankToAppUser --project ShiftManager`
Expected: New migration file created

**Step 3: Review migration file**

Verify the Up method contains:
```csharp
migrationBuilder.AddColumn<int>(
    name: "Rank",
    table: "Users",
    type: "integer",
    nullable: false,
    defaultValue: 0);  // Turai = 0
```

**Step 4: Apply migration (dev only)**

Run: `dotnet ef database update --project ShiftManager`
Expected: Migration applied successfully

**Step 5: Commit**

```bash
git add Models/AppUser.cs Migrations/*AddMilitaryRankToAppUser*
git commit -m "feat: add Rank property to AppUser with migration"
```

---

## Task 4: Add Rank-Based Eligibility to OnDutyService

**Files:**
- Modify: `Services/OnDutyService.cs`
- Modify: `Services/IOnDutyService.cs`
- Test: `ShiftManager.Tests/UnitTests/Services/OnDutyServiceEligibilityTests.cs`

**Step 1: Write failing tests**

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

public class OnDutyServiceEligibilityTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly OnDutyService _service;

    public OnDutyServiceEligibilityTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options, null!, null!);
        _service = new OnDutyService(_db, null!, null!, null!);

        // Seed test users
        _db.Users.AddRange(
            new AppUser { Id = 1, DisplayName = "Enlisted", Rank = MilitaryRank.Samal, IsActive = true, CompanyId = 1 },
            new AppUser { Id = 2, DisplayName = "Officer", Rank = MilitaryRank.Seren, IsActive = true, CompanyId = 1 },
            new AppUser { Id = 3, DisplayName = "Inactive Officer", Rank = MilitaryRank.Segen, IsActive = false, CompanyId = 1 }
        );
        _db.SaveChanges();
    }

    [Fact]
    public async Task GetEligibleUsersForKatzinDuty_ReturnsOnlyOfficers()
    {
        var eligible = await _service.GetEligibleUsersForDutyAsync(OnDutyType.Lead, requireOfficer: true);

        eligible.Should().HaveCount(1);
        eligible.First().Id.Should().Be(2);
    }

    [Fact]
    public async Task GetEligibleUsersForHakamDuty_ReturnsAllActiveUsers()
    {
        var eligible = await _service.GetEligibleUsersForDutyAsync(OnDutyType.Hakam, requireOfficer: false);

        eligible.Should().HaveCount(2); // Excludes inactive
    }

    [Fact]
    public async Task IsUserEligibleForDuty_ReturnsFalse_WhenEnlistedForOfficerDuty()
    {
        var isEligible = await _service.IsUserEligibleForDutyAsync(1, OnDutyType.Lead, requireOfficer: true);

        isEligible.Should().BeFalse();
    }

    [Fact]
    public async Task IsUserEligibleForDuty_ReturnsTrue_WhenOfficerForOfficerDuty()
    {
        var isEligible = await _service.IsUserEligibleForDutyAsync(2, OnDutyType.Lead, requireOfficer: true);

        isEligible.Should().BeTrue();
    }

    public void Dispose() => _db.Dispose();
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test ShiftManager.Tests --filter "FullyQualifiedName~OnDutyServiceEligibilityTests" -v n`
Expected: FAIL - methods don't exist

**Step 3: Add interface methods to IOnDutyService**

In `Services/IOnDutyService.cs`, add:

```csharp
    /// <summary>
    /// Get users eligible for a specific duty type, optionally requiring officer rank.
    /// </summary>
    Task<List<AppUser>> GetEligibleUsersForDutyAsync(OnDutyType dutyType, bool requireOfficer = false);

    /// <summary>
    /// Check if a specific user is eligible for a duty type.
    /// </summary>
    Task<bool> IsUserEligibleForDutyAsync(int userId, OnDutyType dutyType, bool requireOfficer = false);
```

**Step 4: Implement methods in OnDutyService**

In `Services/OnDutyService.cs`, add:

```csharp
    public async Task<List<AppUser>> GetEligibleUsersForDutyAsync(OnDutyType dutyType, bool requireOfficer = false)
    {
        var query = _db.Users
            .IgnoreQueryFilters() // OnDuty is cross-company
            .Where(u => u.IsActive);

        if (requireOfficer)
        {
            // Officers have rank >= 9 (SegenMishne)
            query = query.Where(u => (int)u.Rank >= 9);
        }

        return await query
            .OrderBy(u => u.DisplayName)
            .ToListAsync();
    }

    public async Task<bool> IsUserEligibleForDutyAsync(int userId, OnDutyType dutyType, bool requireOfficer = false)
    {
        var user = await _db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null || !user.IsActive)
            return false;

        if (requireOfficer && !user.Rank.IsOfficer())
            return false;

        return true;
    }
```

**Step 5: Run tests to verify they pass**

Run: `dotnet test ShiftManager.Tests --filter "FullyQualifiedName~OnDutyServiceEligibilityTests" -v n`
Expected: PASS

**Step 6: Commit**

```bash
git add Services/IOnDutyService.cs Services/OnDutyService.cs ShiftManager.Tests/UnitTests/Services/OnDutyServiceEligibilityTests.cs
git commit -m "feat: add rank-based eligibility to OnDutyService"
```

---

## Task 5: Add RequiresOfficerRank to OnDutyTypeConfig

**Files:**
- Modify: `Models/OnDutyTypeConfig.cs`
- Create: `Migrations/YYYYMMDDHHMMSS_AddRequiresOfficerRankToOnDutyTypeConfig.cs`

**Step 1: Add RequiresOfficerRank property**

In `Models/OnDutyTypeConfig.cs`, add after `IsActive`:

```csharp
    /// <summary>
    /// If true, only officers (rank >= SegenMishne) can be assigned this duty type.
    /// Used for Katzin duties.
    /// </summary>
    public bool RequiresOfficerRank { get; set; } = false;
```

**Step 2: Create migration**

Run: `dotnet ef migrations add AddRequiresOfficerRankToOnDutyTypeConfig --project ShiftManager`

**Step 3: Apply migration (dev only)**

Run: `dotnet ef database update --project ShiftManager`

**Step 4: Commit**

```bash
git add Models/OnDutyTypeConfig.cs Migrations/*AddRequiresOfficerRankToOnDutyTypeConfig*
git commit -m "feat: add RequiresOfficerRank flag to OnDutyTypeConfig"
```

---

## Task 6: Integrate Eligibility Check in OnDuty Assignment

**Files:**
- Modify: `Services/OnDutyService.cs` (update `CreateOnDutyAsync`)
- Modify: `Pages/Api/Calendar/QuickAddOnDuty.cshtml.cs`

**Step 1: Update CreateOnDutyAsync to check eligibility**

In `Services/OnDutyService.cs`, modify `CreateOnDutyAsync` to add eligibility check:

```csharp
public async Task<(bool Success, string? Error, OnDuty? OnDuty)> CreateOnDutyAsync(
    int userId, DateOnly date, OnDutyType type, string? notes, int createdBy)
{
    // Check if duty type requires officer rank
    var requiresOfficer = await RequiresOfficerForDutyTypeAsync(type);

    if (requiresOfficer)
    {
        var isEligible = await IsUserEligibleForDutyAsync(userId, type, requireOfficer: true);
        if (!isEligible)
        {
            _logger.LogWarning(
                "User {UserId} is not eligible for duty type {DutyType} - officer rank required",
                userId, type);
            return (false, "OFFICER_RANK_REQUIRED", null);
        }
    }

    // ... rest of existing implementation
}

private async Task<bool> RequiresOfficerForDutyTypeAsync(OnDutyType type)
{
    // Check built-in types
    if (type == OnDutyType.Lead) // Katzin/Lead requires officer
        return true;

    // Check custom types
    var customConfig = await _db.OnDutyTypeConfigs
        .FirstOrDefaultAsync(c => c.TypeValue == (int)type && c.IsActive);

    return customConfig?.RequiresOfficerRank ?? false;
}
```

**Step 2: Update QuickAddOnDuty to return eligibility error**

In `Pages/Api/Calendar/QuickAddOnDuty.cshtml.cs`, handle the new error:

```csharp
if (result.Error == "OFFICER_RANK_REQUIRED")
{
    return new JsonResult(new {
        success = false,
        error = "OFFICER_RANK_REQUIRED",
        message = _localizer["OnDuty_Error_OfficerRankRequired"].Value
    });
}
```

**Step 3: Commit**

```bash
git add Services/OnDutyService.cs Pages/Api/Calendar/QuickAddOnDuty.cshtml.cs
git commit -m "feat: enforce officer rank requirement on on-duty assignment"
```

---

## Task 7: Add Rank to User Profile UI

**Files:**
- Modify: `Pages/My/Settings.cshtml`
- Modify: `Pages/My/Settings.cshtml.cs`
- Create: `Resources/SharedResources.resx` entries (if not exist)

**Step 1: Add Rank dropdown to Settings page**

In `Pages/My/Settings.cshtml`, add in the profile section:

```html
<div class="form-group">
    <label asp-for="Input.Rank" class="form-label">
        <loc key="Profile_Rank">Military Rank</loc>
    </label>
    <select asp-for="Input.Rank" class="form-select" asp-items="@Model.RankOptions">
    </select>
    <span asp-validation-for="Input.Rank" class="text-danger"></span>
</div>
```

**Step 2: Add to page model**

In `Pages/My/Settings.cshtml.cs`:

```csharp
public List<SelectListItem> RankOptions { get; set; } = new();

public async Task<IActionResult> OnGetAsync()
{
    // ... existing code ...

    // Populate rank options
    RankOptions = Enum.GetValues<MilitaryRank>()
        .Select(r => new SelectListItem
        {
            Value = ((int)r).ToString(),
            Text = r.GetDisplayName(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
        })
        .ToList();

    return Page();
}
```

**Step 3: Add to InputModel**

```csharp
public class InputModel
{
    // ... existing properties ...

    [Display(Name = "Military Rank")]
    public MilitaryRank Rank { get; set; }
}
```

**Step 4: Add localization keys**

Add to `Resources/SharedResources.resx`:
- `Profile_Rank` = "Military Rank"
- `OnDuty_Error_OfficerRankRequired` = "This duty type requires officer rank"

Add to `Resources/SharedResources.he-IL.resx`:
- `Profile_Rank` = "דרגה"
- `OnDuty_Error_OfficerRankRequired` = "סוג התורנות הזה דורש דרגת קצונה"

**Step 5: Commit**

```bash
git add Pages/My/Settings.cshtml Pages/My/Settings.cshtml.cs Resources/SharedResources*.resx
git commit -m "feat: add rank selection to user profile settings"
```

---

## Task 8: Add Rank Display to On-Duty Views

**Files:**
- Modify: `Pages/Public/OnDuty.cshtml`
- Modify: `ViewComponents/OnCallWidgetViewComponent.cs`

**Step 1: Show rank in On-Duty calendar**

In `Pages/Public/OnDuty.cshtml`, update the assignee display:

```html
<span class="on-duty-assignee">
    @if (onDuty.User != null)
    {
        <span class="rank-badge" title="@onDuty.User.Rank.GetDisplayName()">
            @onDuty.User.Rank.GetAbbreviation()
        </span>
        @onDuty.User.DisplayName
    }
</span>
```

**Step 2: Show rank in On-Call widget**

In `ViewComponents/OnCallWidgetViewComponent.cs`, include rank in the view model.

**Step 3: Add CSS for rank badge**

In `wwwroot/css/site.css`:

```css
.rank-badge {
    display: inline-block;
    padding: 0.125rem 0.375rem;
    font-size: 0.75rem;
    font-weight: 600;
    background: var(--surface-alt);
    border-radius: var(--radius-sm);
    margin-right: 0.25rem;
}

.rank-badge.officer {
    background: var(--accent);
    color: var(--text-on-accent);
}
```

**Step 4: Commit**

```bash
git add Pages/Public/OnDuty.cshtml ViewComponents/OnCallWidgetViewComponent.cs wwwroot/css/site.css
git commit -m "feat: display rank in on-duty views"
```

---

## Task 9: Add Admin UI for Rank Management

**Files:**
- Modify: `Pages/Admin/Users/Edit.cshtml`
- Modify: `Pages/Admin/Users/Edit.cshtml.cs`

**Step 1: Add rank dropdown to admin user edit**

Similar to Task 7, add rank selection to admin user edit page with full rank options.

**Step 2: Commit**

```bash
git add Pages/Admin/Users/Edit.cshtml Pages/Admin/Users/Edit.cshtml.cs
git commit -m "feat: add rank management to admin user edit"
```

---

## Task 10: Update Eligible Assignee Dropdowns

**Files:**
- Modify: `Pages/Public/OnDuty.cshtml.cs`
- Modify: `Services/OnDutyService.cs`

**Step 1: Filter eligible assignees by duty type**

In `Pages/Public/OnDuty.cshtml.cs`, update `OnGetAsync`:

```csharp
// Get eligible assignees based on duty type requirements
var requiresOfficer = dutyType == OnDutyType.Lead; // Katzin
EligibleAssignees = await _onDutyService.GetEligibleUsersForDutyAsync(dutyType, requiresOfficer);
```

**Step 2: Update UI to show rank in dropdown**

```html
<select asp-for="SelectedUserId" class="form-select">
    @foreach (var user in Model.EligibleAssignees)
    {
        <option value="@user.Id">
            @user.Rank.GetAbbreviation() @user.DisplayName
        </option>
    }
</select>
```

**Step 3: Commit**

```bash
git add Pages/Public/OnDuty.cshtml Pages/Public/OnDuty.cshtml.cs
git commit -m "feat: filter eligible assignees by rank requirement"
```

---

## Task 11: Add Feature Flag

**Files:**
- Modify: `appsettings.json`
- Modify: `Services/OnDutyService.cs`

**Step 1: Add feature flag**

In `appsettings.json`:

```json
{
  "Features": {
    "EnforceRankEligibility": false
  }
}
```

**Step 2: Check flag in eligibility enforcement**

```csharp
private async Task<bool> ShouldEnforceRankEligibility()
{
    return _configuration.GetValue<bool>("Features:EnforceRankEligibility", false);
}

// In CreateOnDutyAsync:
if (await ShouldEnforceRankEligibility() && requiresOfficer)
{
    // ... eligibility check
}
```

**Step 3: Commit**

```bash
git add appsettings.json Services/OnDutyService.cs
git commit -m "feat: add feature flag for rank eligibility enforcement"
```

---

## Task 12: Integration Tests

**Files:**
- Create: `ShiftManager.Tests/IntegrationTests/MilitaryRankIntegrationTests.cs`

**Step 1: Write integration tests**

```csharp
public class MilitaryRankIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task CreateKatzinDuty_WithEnlistedUser_ReturnsBadRequest()
    {
        // Arrange: Create enlisted user, enable feature flag
        // Act: Try to assign Katzin duty
        // Assert: Returns 400 with OFFICER_RANK_REQUIRED error
    }

    [Fact]
    public async Task CreateKatzinDuty_WithOfficer_Succeeds()
    {
        // Arrange: Create officer user, enable feature flag
        // Act: Assign Katzin duty
        // Assert: Returns 200 with duty created
    }

    [Fact]
    public async Task EligibleAssignees_ForKatzin_ReturnsOnlyOfficers()
    {
        // Arrange: Create mix of enlisted and officers
        // Act: Get eligible assignees for Katzin
        // Assert: Only officers returned
    }
}
```

**Step 2: Run integration tests**

Run: `dotnet test ShiftManager.Tests --filter "FullyQualifiedName~MilitaryRankIntegrationTests" -v n`

**Step 3: Commit**

```bash
git add ShiftManager.Tests/IntegrationTests/MilitaryRankIntegrationTests.cs
git commit -m "test: add military rank integration tests"
```

---

## Task 13: Documentation & Audit Logging

**Files:**
- Modify: `Services/AuditLogService.cs`
- Update: `docs/CURRENT_STATE_REFERENCE.md`

**Step 1: Add audit logging for rank changes**

```csharp
await _auditLogService.LogUserActionAsync(
    adminUserId,
    "UserRankChanged",
    "UserManagement",
    userId,
    $"Rank changed from {oldRank} to {newRank}",
    new { OldRank = oldRank, NewRank = newRank });
```

**Step 2: Update documentation**

Add section on Military Rank to CURRENT_STATE_REFERENCE.md

**Step 3: Commit**

```bash
git add Services/AuditLogService.cs docs/CURRENT_STATE_REFERENCE.md
git commit -m "docs: add military rank documentation and audit logging"
```

---

## Rollout Plan

### Phase 1: Database Migration (Day 1)
1. Deploy migration to staging
2. Verify all users have default rank (Turai)
3. Run smoke tests

### Phase 2: Feature Flag OFF (Day 2-3)
1. Deploy code with `EnforceRankEligibility: false`
2. Rank UI visible but not enforced
3. Allow admins to set ranks

### Phase 3: Data Backfill (Day 4-5)
1. Import officer ranks from external source (if available)
2. Or: Admin manually sets officer ranks
3. Verify officer count matches expectations

### Phase 4: Feature Flag ON (Day 6)
1. Enable `EnforceRankEligibility: true`
2. Monitor for eligibility errors
3. Verify Katzin assignments only going to officers

### Rollback Plan
1. Set `EnforceRankEligibility: false` - immediate
2. Does not require code rollback
3. Rank data preserved for later enablement

---

## Observability

### Logging
- `LogWarning` when eligibility check fails
- `LogInformation` on successful duty assignment with rank

### Metrics (if applicable)
- `onduty_assignment_rejected_rank_total` - count of rejections
- `onduty_officer_assignments_total` - count of officer-only assignments

### Audit Trail
- `UserRankChanged` action logged with old/new values
- `OnDutyAssigned` includes assignee rank

---

## Edge Cases & Failure Modes

| Scenario | Handling |
|----------|----------|
| No officers in system | Show empty eligible list, prevent assignment |
| Officer demoted after assignment | Existing assignments preserved, new blocked |
| Custom duty type changes RequiresOfficer | Only affects new assignments |
| Rank import fails | Fallback to Turai (lowest), log error |
| Feature flag toggled mid-assignment | Check at assignment time, not page load |

---

**Plan complete and saved to `docs/plans/2026-02-04-military-rank-system.md`.**

**Two execution options:**

1. **Subagent-Driven (this session)** - I dispatch fresh subagent per task, review between tasks, fast iteration

2. **Parallel Session (separate)** - Open new session with executing-plans, batch execution with checkpoints

**Which approach?**
