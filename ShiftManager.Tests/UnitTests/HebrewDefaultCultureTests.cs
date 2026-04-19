using FluentAssertions;
using ShiftManager.Data.SeedData;

namespace ShiftManager.Tests.UnitTests;

/// <summary>
/// Verifies the wiring of the <c>FF_HEBREW_DEFAULT</c> feature flag that gates the
/// Hebrew-as-default rollout. The actual runtime read happens at startup in Program.cs
/// via raw SQLite query (before DI is built) — untestable in isolation — but this suite
/// locks in the seed definition + rollback-safety default.
/// </summary>
public class HebrewDefaultCultureTests
{
    [Fact]
    public void HebrewDefaultFlag_IsInFlagsClass()
    {
        FeatureFlagSeed.Flags.HebrewDefault.Should().Be("FF_HEBREW_DEFAULT",
            "the flag name is referenced from Program.cs raw SQL — it must not drift");
    }

    [Fact]
    public void HebrewDefaultFlag_IsSeededAsDisabled()
    {
        // Disabled-by-default protects against a premature flip in production —
        // Owners must explicitly turn the flag on via /Owner/FeatureFlags.
        var flags = FeatureFlagSeed.GetFeatureFlags();
        var hebrewFlag = flags.SingleOrDefault(f => f.Name == FeatureFlagSeed.Flags.HebrewDefault);

        hebrewFlag.Should().NotBeNull("FF_HEBREW_DEFAULT must be in the seed list");
        hebrewFlag!.IsEnabled.Should().BeFalse(
            "Hebrew default must ship DISABLED — Owners toggle it in the admin UI + restart");
        hebrewFlag.CompanyId.Should().BeNull("global flag, not tenant-scoped");
        hebrewFlag.UserId.Should().BeNull("global flag, not user-scoped");
    }

    [Fact]
    public void HebrewDefaultFlag_DescriptionMentionsRestartRequired()
    {
        // The flag is read at startup, so the UI must warn Owners that a restart is required.
        var flags = FeatureFlagSeed.GetFeatureFlags();
        var hebrewFlag = flags.Single(f => f.Name == FeatureFlagSeed.Flags.HebrewDefault);

        hebrewFlag.Description.Should().Contain("restart",
            "Owners must see in the admin UI that this flag requires an app restart");
    }
}
