using TaskFlow.Domain.Chatbot;

namespace TaskFlow.Domain.UnitTests.Chatbot;

public sealed class IntentLogTests
{
    [Fact]
    public void StartsWithoutFeedbackAndCanBeMarkedAccepted()
    {
        var now = DateTimeOffset.UtcNow;
        var log = new IntentLog(Guid.NewGuid(), null, IntentSource.Text, "hi", "hi", "greeting", 0.9, "other", 0.05, "en", "en", "v1", ChatOutcome.Answered, now);

        log.Accepted.ShouldBeNull();
        log.MarkAccepted(true);
        log.Accepted.ShouldBe(true);
        log.MarkAccepted(false);
        log.Accepted.ShouldBe(false);
        log.CreatedAt.ShouldBe(now);
    }
}
