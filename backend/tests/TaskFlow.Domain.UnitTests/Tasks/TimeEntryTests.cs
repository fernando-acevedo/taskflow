using TaskFlow.Domain.Clients;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Domain.UnitTests.Tasks;

public sealed class TimeEntryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 19);
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();

    private readonly Project _project =
        Project.Create(Client.Create("Acme", null, null, Now), "Website", null, OwnerId, Now).Value;

    [Fact]
    public void CreateTrimsTheNoteAndKeepsTheValues()
    {
        var taskId = Guid.NewGuid();

        var entry = TimeEntry.Create(_project, taskId, MemberId, Today, 90, "  Wireframes ", Today, Now).Value;

        (entry.TaskItemId, entry.UserId, entry.Date, entry.Minutes, entry.Note, entry.CreatedAt)
            .ShouldBe((taskId, MemberId, Today, 90, "Wireframes", Now));
    }

    [Fact]
    public void ABlankNoteBecomesNull() =>
        NewEntry(note: "   ").Note.ShouldBeNull();

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1441)]
    public void MinutesOutsideOneToADayAreRejected(int minutes) =>
        TimeEntry.Create(_project, Guid.NewGuid(), MemberId, Today, minutes, null, Today, Now).Error.ShouldBe(TimeEntryErrors.InvalidMinutes);

    [Fact]
    public void TheLimitsOfTheRangeAreAccepted()
    {
        TimeEntry.Create(_project, Guid.NewGuid(), MemberId, Today, 1, null, Today, Now).IsSuccess.ShouldBeTrue();
        TimeEntry.Create(_project, Guid.NewGuid(), MemberId, Today, 1440, null, Today, Now).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void ANoteOver500CharactersIsRejected() =>
        TimeEntry.Create(_project, Guid.NewGuid(), MemberId, Today, 30, new string('x', 501), Today, Now).Error.ShouldBe(TimeEntryErrors.NoteTooLong);

    [Fact]
    public void FutureDatesAreRejectedButTodayIsFine()
    {
        TimeEntry.Create(_project, Guid.NewGuid(), MemberId, Today.AddDays(2), 30, null, Today, Now).Error.ShouldBe(TimeEntryErrors.FutureDate);
        TimeEntry.Create(_project, Guid.NewGuid(), MemberId, Today.AddDays(1), 30, null, Today, Now).IsSuccess.ShouldBeTrue();
        TimeEntry.Create(_project, Guid.NewGuid(), MemberId, Today, 30, null, Today, Now).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void AnArchivedProjectIsReadOnly()
    {
        var entry = NewEntry();
        _project.Archive(Now);

        TimeEntry.Create(_project, Guid.NewGuid(), MemberId, Today, 30, null, Today, Now).Error.ShouldBe(ProjectErrors.Archived);
        entry.Edit(_project, MemberId, false, Today, 10, null, Today).Error.ShouldBe(ProjectErrors.Archived);
        entry.CanChange(_project, MemberId, true).Error.ShouldBe(ProjectErrors.Archived);
    }

    [Fact]
    public void OnlyTheOwnerOfTheEntryOrAManagerChangesIt()
    {
        var entry = NewEntry();
        var someoneElse = Guid.NewGuid();

        entry.Edit(_project, someoneElse, false, Today, 10, null, Today).Error.ShouldBe(TimeEntryErrors.NotOwner);
        entry.CanChange(_project, someoneElse, false).Error.ShouldBe(TimeEntryErrors.NotOwner);
        entry.Edit(_project, someoneElse, true, Today.AddDays(-1), 10, "Fixed", Today).IsSuccess.ShouldBeTrue();
        (entry.Date, entry.Minutes, entry.Note, entry.UserId).ShouldBe((Today.AddDays(-1), 10, "Fixed", MemberId));
        entry.Edit(_project, MemberId, false, Today, 20, null, Today).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void EditAppliesTheSameRulesAsCreate()
    {
        var entry = NewEntry();

        entry.Edit(_project, MemberId, false, Today.AddDays(2), 10, null, Today).Error.ShouldBe(TimeEntryErrors.FutureDate);
        entry.Edit(_project, MemberId, false, Today, 0, null, Today).Error.ShouldBe(TimeEntryErrors.InvalidMinutes);
        entry.Minutes.ShouldBe(30);
    }

    private TimeEntry NewEntry(string? note = null) =>
        TimeEntry.Create(_project, Guid.NewGuid(), MemberId, Today, 30, note, Today, Now).Value;
}
