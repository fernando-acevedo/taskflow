using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Activity;
using TaskFlow.Domain.Activity;

namespace TaskFlow.Application.UnitTests.Activity;

public sealed class ActivityLogTests
{
    [Fact]
    public void EntriesRecordedAtTheSameInstantGetIdsInRecordingOrder()
    {
        var added = new List<ActivityEntry>();
        var set = FakeDbSet.Of<ActivityEntry>();
        set.When(s => s.Add(Arg.Any<ActivityEntry>())).Do(call => added.Add(call.Arg<ActivityEntry>()));
        var db = Substitute.For<IAppDbContext>();
        db.ActivityEntries.Returns(set);
        var user = Substitute.For<ICurrentUser>();
        user.Id.Returns(Guid.NewGuid());
        var log = new ActivityLog(db, user, new FixedTime(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero)));
        var projectId = Guid.NewGuid();

        for (var i = 0; i < 50; i++)
        {
            log.Record(projectId, ActivityType.TaskUpdated, new { });
        }

        added.Count.ShouldBe(50);
        added.Select(e => e.Id).ShouldBe(added.Select(e => e.Id).Order(), ignoreOrder: false);
        added.Select(e => e.Id).Distinct().Count().ShouldBe(50);
        added.Select(e => e.OccurredAt).Distinct().Count().ShouldBe(1);
    }

    [Fact]
    public void EntriesCarryTheApiKeyTheActorUsed()
    {
        var added = new List<ActivityEntry>();
        var set = FakeDbSet.Of<ActivityEntry>();
        set.When(s => s.Add(Arg.Any<ActivityEntry>())).Do(call => added.Add(call.Arg<ActivityEntry>()));
        var db = Substitute.For<IAppDbContext>();
        db.ActivityEntries.Returns(set);
        var keyId = Guid.NewGuid();
        var user = Substitute.For<ICurrentUser>();
        user.Id.Returns(Guid.NewGuid());
        user.ApiKeyId.Returns(keyId);
        var log = new ActivityLog(db, user, new FixedTime(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero)));

        log.Record(Guid.NewGuid(), ActivityType.TaskUpdated, new { });

        added.ShouldHaveSingleItem().ApiKeyId.ShouldBe(keyId);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
