using TaskFlow.Application.Demo;

namespace TaskFlow.Application.UnitTests.Demo;

public sealed class DemoScheduleTests
{
    private const string Daily = "0 3 * * *";

    private static DateTimeOffset Utc(int day, int hour, int minute = 0) => new(2026, 11, day, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public void IsDueWhenThereWasNoReset() =>
        DemoSchedule.IsDue(Daily, "UTC", null, Utc(10, 12)).ShouldBeTrue();

    [Fact]
    public void IsDueWhenTheLastResetIsBeforeTheLatestOccurrence() =>
        DemoSchedule.IsDue(Daily, "UTC", Utc(9, 3, 5), Utc(10, 3, 1)).ShouldBeTrue();

    [Fact]
    public void IsNotDueWhenTheLastResetIsAfterTheLatestOccurrence() =>
        DemoSchedule.IsDue(Daily, "UTC", Utc(10, 3, 5), Utc(10, 23)).ShouldBeFalse();

    [Fact]
    public void IsNotDueBeforeTheFirstOccurrenceOfTheDayWhenYesterdayHadItsReset() =>
        DemoSchedule.IsDue(Daily, "UTC", Utc(9, 3, 5), Utc(10, 2, 59)).ShouldBeFalse();

    [Fact]
    public void ARunMissedWhileTheAppSleptIsDueOnTheNextCheck() =>
        DemoSchedule.IsDue(Daily, "UTC", Utc(5, 3, 5), Utc(10, 14)).ShouldBeTrue();

    [Fact]
    public void TheScheduleIsEvaluatedInTheConfiguredTimeZone()
    {
        // Buenos Aires is UTC-3 all year, so 03:00 there is 06:00 UTC.
        const string zone = "America/Argentina/Buenos_Aires";

        DemoSchedule.MostRecentOccurrence(Daily, zone, Utc(10, 5)).ShouldBe(Utc(9, 6));
        DemoSchedule.MostRecentOccurrence(Daily, zone, Utc(10, 7)).ShouldBe(Utc(10, 6));
        DemoSchedule.IsDue(Daily, zone, Utc(10, 4), Utc(10, 7)).ShouldBeTrue();
        DemoSchedule.IsDue(Daily, zone, Utc(10, 6, 30), Utc(10, 7)).ShouldBeFalse();
    }

    [Fact]
    public void FindsOccurrencesOfSparseSchedules()
    {
        // 2026-11-10 is a Tuesday; the last Monday 06:00 before it is the 9th.
        DemoSchedule.MostRecentOccurrence("0 6 * * 1", "UTC", Utc(10, 12)).ShouldBe(Utc(9, 6));
        DemoSchedule.MostRecentOccurrence("0 0 1 1 *", "UTC", Utc(10, 12)).ShouldBe(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void NextOccurrenceIsAfterNow()
    {
        DemoSchedule.NextOccurrence(Daily, "UTC", Utc(10, 12)).ShouldBe(Utc(11, 3));
        DemoSchedule.NextOccurrence(Daily, "UTC", Utc(10, 3)).ShouldBe(Utc(11, 3));
    }

    [Theory]
    [InlineData("0 3 * * *", "UTC", true)]
    [InlineData("0 3 * * *", "", true)]
    [InlineData("not a cron", "UTC", false)]
    [InlineData("", "UTC", false)]
    [InlineData("0 3 * * *", "Mars/Olympus", false)]
    public void ValidatesCronAndTimeZone(string cron, string zone, bool valid) =>
        (DemoSchedule.Validate(cron, zone) is null).ShouldBe(valid);
}
