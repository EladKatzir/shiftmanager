using FluentAssertions;
using ShiftManager.Services;
using ShiftManager.ViewComponents;
using Xunit;

namespace ShiftManager.Tests.UnitTests.ViewComponents;

/// <summary>
/// Whether a day note's delete × renders, for every persona.
///
/// This exists because the rule used to live inline in Default.cshtml, where the only way to check it
/// was to log in as each kind of user and look. The persona that matters most is the one holding
/// WriteOverviewNotes WITHOUT AssignShifts — a plain employee, which is most of the deployment. The
/// original rule gated the × on <c>!IsReadOnly</c> alone (i.e. the AssignShifts grant), so that user
/// could ADD a note through Quick Entry and then had no way whatsoever to remove it.
///
/// <para><b>The other half of the invariant:</b> this must stay equivalent to DeleteDayNote's 403. A ×
/// that 403s is a broken button; a hidden × where the server would allow is a lie about what the user
/// can do. <see cref="TheViewRuleAndTheServerRuleAgree_ForEveryCombination"/> pins the truth table the
/// endpoint implements (author OR assign-tier, gated behind the note-permission check) against the
/// view's, so the two cannot drift apart silently.</para>
/// </summary>
public class DayNoteDeleteVisibilityTests
{
    private const int Me = 7, SomeoneElse = 8;

    /// <summary>IsReadOnly is !CanEdit, so an assigner has isReadOnly == false.</summary>
    private const bool Assigner = false, NotAssigner = true;

    [Theory]
    // A notes-only writer (WriteOverviewNotes, no AssignShifts): canWriteNote true, isReadOnly TRUE.
    [InlineData(true, NotAssigner, Me, true, "their own note — the case that was broken")]
    [InlineData(true, NotAssigner, SomeoneElse, false, "someone else's note — not theirs to remove")]
    // An assigner: canWriteNote true (CanEdit implies it), isReadOnly FALSE.
    [InlineData(true, Assigner, Me, true, "their own note")]
    [InlineData(true, Assigner, SomeoneElse, true, "anyone's note on a calendar they can assign")]
    // No note permission at all: nothing is deletable, whoever wrote it.
    [InlineData(false, NotAssigner, Me, false, "cannot write notes, so cannot delete their own")]
    [InlineData(false, Assigner, SomeoneElse, false, "cannot write notes at all")]
    public void TheDeleteAffordance_FollowsAuthorOrAssignTier(
        bool canWriteNote, bool isReadOnly, int authorId, bool expected, string because)
    {
        ExcelCalendarTableViewModel
            .CanDeleteDayNote(canWriteNote, isReadOnly, Me, authorId)
            .Should().Be(expected, because);
    }

    [Fact]
    public void AnOrphanedNote_IsRemovableOnlyByAnAssigner()
    {
        // The author FK is SetNull, so deleting a user leaves their notes with a null author. Nobody
        // can then claim ownership, which is the intended outcome rather than an accident.
        ExcelCalendarTableViewModel.CanDeleteDayNote(
            canWriteNote: true, isReadOnly: NotAssigner, currentUserId: Me, noteAuthorId: null)
            .Should().BeFalse("a null author satisfies no ownership claim");

        ExcelCalendarTableViewModel.CanDeleteDayNote(
            canWriteNote: true, isReadOnly: Assigner, currentUserId: Me, noteAuthorId: null)
            .Should().BeTrue("an assigner can still clear it");
    }

    [Fact]
    public void TheInstanceOverload_ReadsTheNotesOwnAuthor()
    {
        var model = new ExcelCalendarTableViewModel
        {
            CanWriteNote = true,
            IsReadOnly = NotAssigner,
            CurrentUserId = Me
        };

        var mine = new DayNoteView(1, "mine", "Me", Me, null, null, null, true, null);
        var theirs = new DayNoteView(2, "theirs", "Them", SomeoneElse, null, null, null, true, null);

        model.CanDeleteDayNote(mine).Should().BeTrue();
        model.CanDeleteDayNote(theirs).Should().BeFalse();
    }

    [Fact]
    public void TheViewRuleAndTheServerRuleAgree_ForEveryCombination()
    {
        // DeleteDayNote's ladder, reduced to the part that decides allow/deny once the note has been
        // found and its molecule authorised:
        //   403 unless HasCalendarNotePermissionAsync  -> canWriteNote
        //   then: author OR HasGrantWithScopeAsync(AssignShifts, note's scope)
        static bool ServerWouldAllow(bool hasNotePermission, bool hasAssignShifts, int callerId, int? authorId)
            => hasNotePermission && (authorId == callerId || hasAssignShifts);

        foreach (var hasNotePermission in new[] { true, false })
        foreach (var hasAssignShifts in new[] { true, false })
        foreach (var authorId in new int?[] { Me, SomeoneElse, null })
        {
            // The view expresses the assign grant as its inverse, IsReadOnly.
            var viewSaysYes = ExcelCalendarTableViewModel.CanDeleteDayNote(
                canWriteNote: hasNotePermission, isReadOnly: !hasAssignShifts,
                currentUserId: Me, noteAuthorId: authorId);

            var serverSaysYes = ServerWouldAllow(hasNotePermission, hasAssignShifts, Me, authorId);

            viewSaysYes.Should().Be(serverSaysYes,
                $"view and server must agree (notePermission={hasNotePermission}, " +
                $"assignShifts={hasAssignShifts}, author={authorId?.ToString() ?? "null"})");
        }
    }
}
