using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using ShiftManager.Models.Schedule;
using ShiftManager.Services;

namespace ShiftManager.ViewComponents;

/// <summary>
/// The Home "schedule spine" (Phase 6 — "Home is the schedule-spine for everyone"). Renders the
/// signed-in user's upcoming personal items (next two weeks) grouped Today / Tomorrow / This week /
/// Later, reusing the SAME <see cref="IPersonalTimelineService"/> that feeds the rich /My timeline.
/// A "view full schedule" link opens /My for the full filterable view + detail drawer. Self-contained
/// markup (no dependency on the /My drawer JS), so it can drop into any page. A manager/director is
/// still an employee with their own shifts — so this renders for everyone, with role widgets stacking
/// around it on Home.
/// </summary>
public class PersonalSpineViewComponent : ViewComponent
{
    private readonly IPersonalTimelineService _timeline;
    private readonly ICurrentUserService _currentUser;

    public PersonalSpineViewComponent(IPersonalTimelineService timeline, ICurrentUserService currentUser)
    {
        _timeline = timeline;
        _currentUser = currentUser;
    }

    /// <summary>A labelled bucket of timeline items (label is a loc key resolved in the view).</summary>
    public record SpineGroup(string LabelKey, IReadOnlyList<TimelineItem> Items);

    public record SpineVm(IReadOnlyList<SpineGroup> Groups, int TotalCount);

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var userId = _currentUser.UserId;
        if (userId <= 0) return View(new SpineVm(new List<SpineGroup>(), 0));

        var today = DateOnly.FromDateTime(DateTime.Today);
        var horizon = today.AddDays(14);
        var timeline = await _timeline.GetTimelineAsync(userId, today, horizon);

        var tomorrow = today.AddDays(1);
        var weekEnd = today.AddDays(7);

        var groups = new List<SpineGroup>();
        void AddGroup(string key, Func<TimelineItem, bool> pred)
        {
            var items = timeline.Items.Where(pred).ToList();
            if (items.Count > 0) groups.Add(new SpineGroup(key, items));
        }

        AddGroup("Today", i => i.Date == today);
        AddGroup("Tomorrow", i => i.Date == tomorrow);
        AddGroup("ThisWeek", i => i.Date > tomorrow && i.Date <= weekEnd);
        AddGroup("Later", i => i.Date > weekEnd);

        return View(new SpineVm(groups, timeline.Items.Count));
    }
}
