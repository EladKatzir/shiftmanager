using ShiftManager.ViewComponents;

namespace ShiftManager.Tests.ViewComponents;

public class ExcelCalendarOrderingTests
{
    private static ExcelCalendarRow Row(string id, string group) => new() { Id = id, GroupId = group };
    private static ExcelCalendarGroup Grp(string id, int sort) => new() { Id = id, SortOrder = sort };

    [Fact]
    public void NoSavedOrder_KeepsDefaults()
    {
        var rows = new List<ExcelCalendarRow> { Row("user-1", "category-5"), Row("user-2", "category-5") };
        var groups = new List<ExcelCalendarGroup> { Grp("category-5", 0) };
        CalendarOrderApplier.Apply(rows, groups, new());
        Assert.Equal(new[] { "user-1", "user-2" }, rows.ConvertAll(r => r.Id));
    }

    [Fact]
    public void RowOrder_PositionedFirst_ThenUnpositionedInDefaultOrder()
    {
        var rows = new List<ExcelCalendarRow> {
            Row("user-1", "category-5"), Row("user-2", "category-5"), Row("user-3", "category-5") };
        var groups = new List<ExcelCalendarGroup> { Grp("category-5", 0) };
        var map = new Dictionary<(string, string), int> {
            { ("category-5", "user-3"), 0 }, { ("category-5", "user-1"), 1 } }; // user-2 un-positioned
        CalendarOrderApplier.Apply(rows, groups, map);
        Assert.Equal(new[] { "user-3", "user-1", "user-2" }, rows.ConvertAll(r => r.Id));
    }

    [Fact]
    public void CategoryOrder_ReordersGroups_UnpositionedFallBackToSortOrder()
    {
        var rows = new List<ExcelCalendarRow>();
        var groups = new List<ExcelCalendarGroup> { Grp("category-5", 0), Grp("category-6", 1), Grp("category-7", 2) };
        var map = new Dictionary<(string, string), int> {
            { ("", "category-7"), 0 }, { ("", "category-5"), 1 } }; // category-6 un-positioned
        CalendarOrderApplier.Apply(rows, groups, map);
        Assert.Equal(new[] { "category-7", "category-5", "category-6" }, groups.ConvertAll(g => g.Id));

        // CRITICAL: Default.cshtml renders groups via `Model.Groups.OrderBy(g => g.SortOrder)`,
        // so the applier MUST rewrite SortOrder — reordering the list alone is silently ignored.
        // Assert SortOrder reflects the new positions (this is what actually drives rendering).
        var renderedOrder = groups.OrderBy(g => g.SortOrder).Select(g => g.Id).ToArray();
        Assert.Equal(new[] { "category-7", "category-5", "category-6" }, renderedOrder);
    }
}
