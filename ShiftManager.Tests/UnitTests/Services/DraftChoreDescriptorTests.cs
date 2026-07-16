using FluentAssertions;
using ShiftManager.Models;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// The chore-cell diff unit is a SET of rich descriptors (Spec C §2), serialized to a canonical string so the
/// Foundation's conflict primitive stays a plain string compare. These cover the reversible per-descriptor
/// encoding + the order-independent, de-duplicated set serialization that makes staged==baseline / live==baseline
/// a stable equality test.
/// </summary>
public sealed class DraftChoreDescriptorTests
{
    [Fact]
    public void Encode_Decode_RoundTrips_All_Fields_Including_Delimiter_Chars_In_Free_Text()
    {
        var d = DraftChoreDescriptor.Create(
            choreTypeId: 7,
            title: "Guard | Gate=A\nNorth",           // contains the field, kv, and set delimiters
            startTime: new TimeOnly(10, 0),
            endTime: new TimeOnly(14, 30),
            notes: "watch | the=fence");

        var round = DraftChoreDescriptor.Decode(d.Encode());

        round.Should().Be(d, "URL-escaping free-text keeps '|', '=', and '\\n' out of values so encode is reversible");
    }

    [Fact]
    public void Create_Trims_Title_And_Folds_Blank_Notes_To_Null()
    {
        var d = DraftChoreDescriptor.Create(null, "  Kitchen  ", null, null, "   ");
        d.Title.Should().Be("Kitchen");
        d.Notes.Should().BeNull();
    }

    [Fact]
    public void EncodeSet_Is_Order_Independent_And_Deduplicated()
    {
        var a = DraftChoreDescriptor.Create(1, "A", null, null, null);
        var b = DraftChoreDescriptor.Create(2, "B", null, null, null);

        var s1 = DraftChoreDescriptor.EncodeSet(new[] { a, b });
        var s2 = DraftChoreDescriptor.EncodeSet(new[] { b, a, a }); // reordered + duplicate

        s2.Should().Be(s1, "the set is sorted + de-duplicated so equality is a plain string compare (the conflict primitive)");
    }

    [Fact]
    public void EncodeSet_Empty_Is_Empty_String_And_DecodeSet_Inverts()
    {
        DraftChoreDescriptor.EncodeSet(System.Array.Empty<DraftChoreDescriptor>()).Should().BeEmpty();
        DraftChoreDescriptor.DecodeSet("").Should().BeEmpty();

        var set = new[]
        {
            DraftChoreDescriptor.Create(1, "A", new TimeOnly(8, 0), new TimeOnly(9, 0), "n"),
            DraftChoreDescriptor.Create(null, "B", null, null, null)
        };
        var canonical = DraftChoreDescriptor.EncodeSet(set);
        DraftChoreDescriptor.EncodeSet(DraftChoreDescriptor.DecodeSet(canonical)).Should().Be(canonical);
    }

    [Fact]
    public void Different_Titles_Are_Different_Descriptors()
    {
        var a = DraftChoreDescriptor.Create(1, "Kitchen", null, null, null);
        var b = DraftChoreDescriptor.Create(1, "Bathroom", null, null, null);
        a.Encode().Should().NotBe(b.Encode(), "Title is part of the user-authored identity (Spec C §6 recommendation)");
    }
}
