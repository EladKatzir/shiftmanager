using System;
using System.Collections.Generic;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

public class GenerateDatesFromRuleTests
{
    [Fact]
    public void GenerateDatesFromRule_IsDeterministic_AcrossDifferentRanges()
    {
        // Cycle 3 weeks, weekOffsets [0], homeDays Sunday, anchor Mar 30 2026 (Monday)
        var rule = new DerivedRotationRule(
            CycleWeeks: 3,
            HomeDays: new List<DayOfWeek> { DayOfWeek.Sunday },
            WeekOffsets: new List<int> { 0 },
            Anchor: new DateOnly(2026, 3, 30),
            StartTime: null, EndTime: null);

        var dates1 = HomeTypeService.GenerateDatesFromRulePublicForTest(
            rule, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30));

        var dates2 = HomeTypeService.GenerateDatesFromRulePublicForTest(
            rule, new DateOnly(2026, 5, 5), new DateOnly(2026, 5, 12));

        var dates3 = HomeTypeService.GenerateDatesFromRulePublicForTest(
            rule, new DateOnly(2026, 4, 1), new DateOnly(2026, 5, 31));

        // Sun May 10 2026 is week (May10 - Mar30)/7 = 41/7 = 5; cycleWeek = 5 % 3 = 2; not in [0] → SKIP
        // Sun May 24 2026 is week (May24 - Mar30)/7 = 55/7 = 7; cycleWeek = 7 % 3 = 1; not in [0] → SKIP
        // dates2 covers May 5-12: only Sun in range is May 10, which is SKIP → dates2 should be empty
        Assert.Empty(dates2);

        // dates1's matches must equal dates3's matches within Apr 1-30
        var dates3InApril = dates3.FindAll(d => d <= new DateOnly(2026, 4, 30));
        Assert.Equal(dates1, dates3InApril);
    }

    [Fact]
    public void GenerateDatesFromRule_DatesBeforeAnchor_AreSkipped()
    {
        var rule = new DerivedRotationRule(
            CycleWeeks: 2,
            HomeDays: new List<DayOfWeek> { DayOfWeek.Sunday },
            WeekOffsets: new List<int> { 0 },
            Anchor: new DateOnly(2026, 6, 1),  // Mon Jun 1 2026
            StartTime: null, EndTime: null);

        // Range entirely before anchor — should produce no dates
        var dates = HomeTypeService.GenerateDatesFromRulePublicForTest(
            rule, new DateOnly(2026, 4, 1), new DateOnly(2026, 5, 31));
        Assert.Empty(dates);
    }
}
