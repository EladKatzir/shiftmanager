using System.Collections.Generic;
using FluentAssertions;
using ShiftManager.Models;
using ShiftManager.Pages.Calendar;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Calendar;

public class ShiftsTabResolutionTests
{
    private static List<ShiftTab> Tabs(params int[] ids)
    {
        var list = new List<ShiftTab>();
        foreach (var id in ids) list.Add(new ShiftTab { Id = id });
        return list;
    }

    [Fact]
    public void NoTabs_AlwaysAll()
        => ShiftsModel.ResolveActiveTab(Tabs(), tabParamPresent: true, requestedTab: 5, rememberedTab: 5)
            .Should().BeNull();

    [Fact]
    public void ExplicitRealTab_Selected()
        => ShiftsModel.ResolveActiveTab(Tabs(3, 4), tabParamPresent: true, requestedTab: 4, rememberedTab: 3)
            .Should().Be(4);

    [Fact]
    public void ExplicitZero_ResolvesToAll()
        => ShiftsModel.ResolveActiveTab(Tabs(3, 4), tabParamPresent: true, requestedTab: 0, rememberedTab: 3)
            .Should().BeNull();

    [Fact]
    public void ExplicitForeignId_ResolvesToAll()   // STR-8: deleted / foreign id -> All (valid, non-empty)
        => ShiftsModel.ResolveActiveTab(Tabs(3, 4), tabParamPresent: true, requestedTab: 99, rememberedTab: 3)
            .Should().BeNull();

    [Fact]
    public void NoParam_RememberedValid_Restored()   // LTM-1
        => ShiftsModel.ResolveActiveTab(Tabs(3, 4), tabParamPresent: false, requestedTab: null, rememberedTab: 3)
            .Should().Be(3);

    [Fact]
    public void NoParam_RememberedDeleted_FallsBackToAll()   // LTM-2 (post-UD2: All is the safe default)
        => ShiftsModel.ResolveActiveTab(Tabs(3, 4), tabParamPresent: false, requestedTab: null, rememberedTab: 99)
            .Should().BeNull();

    [Fact]
    public void NoParam_NoPreference_DefaultsToAll()   // UD2 first-visit
        => ShiftsModel.ResolveActiveTab(Tabs(3, 4), tabParamPresent: false, requestedTab: null, rememberedTab: null)
            .Should().BeNull();
}
