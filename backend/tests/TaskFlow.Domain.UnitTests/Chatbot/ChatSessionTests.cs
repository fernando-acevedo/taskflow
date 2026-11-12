using TaskFlow.Domain.Chatbot;

namespace TaskFlow.Domain.UnitTests.Chatbot;

public sealed class ChatSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(30);

    private static ChatSession NewSession(string locale = "en") => ChatSession.Start(locale, Now, Idle).Value;

    [Fact]
    public void StartsActiveWithIdleExpiry()
    {
        var session = NewSession("es");

        session.Id.ShouldNotBe(Guid.Empty);
        session.Status.ShouldBe(ChatSessionStatus.Active);
        session.Locale.ShouldBe("es");
        session.ExpiresAt.ShouldBe(Now + Idle);
        session.UserId.ShouldBeNull();
        session.IsExpired(Now).ShouldBeFalse();
        session.IsExpired(Now + Idle).ShouldBeTrue();
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("")]
    [InlineData("EN")]
    public void RejectsInvalidLocale(string locale)
    {
        ChatSession.Start(locale, Now, Idle).Error.ShouldBe(ChatbotErrors.InvalidLocale);
        NewSession().ChangeLocale(locale).Error.ShouldBe(ChatbotErrors.InvalidLocale);
    }

    [Fact]
    public void ChangesLocale()
    {
        var session = NewSession();

        session.ChangeLocale("es").IsSuccess.ShouldBeTrue();
        session.Locale.ShouldBe("es");
    }

    [Fact]
    public void AdvanceStoresStateResetsFallbacksAndExtendsExpiry()
    {
        var session = NewSession();
        session.RegisterFallback(Now, Idle);
        session.RegisterFallback(Now, Idle);
        session.ConsecutiveFallbacks.ShouldBe(2);
        var later = Now.AddMinutes(10);

        session.Advance("login_problem", "ask_email", new Dictionary<string, string> { ["email"] = "x" }, later, Idle).IsSuccess.ShouldBeTrue();

        session.CurrentIntent.ShouldBe("login_problem");
        session.CurrentStep.ShouldBe("ask_email");
        session.Slots["email"].ShouldBe("x");
        session.ConsecutiveFallbacks.ShouldBe(0);
        session.LastActivityAt.ShouldBe(later);
        session.ExpiresAt.ShouldBe(later + Idle);
    }

    [Fact]
    public void FallbackCountsConsecutively()
    {
        var session = NewSession();

        session.RegisterFallback(Now, Idle);
        session.RegisterFallback(Now.AddMinutes(1), Idle);

        session.ConsecutiveFallbacks.ShouldBe(2);
        session.ExpiresAt.ShouldBe(Now.AddMinutes(1) + Idle);
    }

    [Fact]
    public void CompleteAndEscalateCloseTheSession()
    {
        var completed = NewSession();
        completed.Complete(Now).IsSuccess.ShouldBeTrue();
        completed.Status.ShouldBe(ChatSessionStatus.Completed);

        var escalated = NewSession();
        escalated.Escalate(Now).IsSuccess.ShouldBeTrue();
        escalated.Status.ShouldBe(ChatSessionStatus.Escalated);
    }

    [Fact]
    public void ClosedSessionRejectsEveryOperation()
    {
        var session = NewSession();
        session.Complete(Now);

        session.Complete(Now).Error.ShouldBe(ChatbotErrors.SessionClosed);
        session.Escalate(Now).Error.ShouldBe(ChatbotErrors.SessionClosed);
        session.ChangeLocale("es").Error.ShouldBe(ChatbotErrors.SessionClosed);
        session.RegisterFallback(Now, Idle).Error.ShouldBe(ChatbotErrors.SessionClosed);
        session.Advance("a", "b", new Dictionary<string, string>(), Now, Idle).Error.ShouldBe(ChatbotErrors.SessionClosed);
        session.AddMessage(ChatSender.User, "hi", null, Now).Error.ShouldBe(ChatbotErrors.SessionClosed);
    }

    [Fact]
    public void ExpireOnlyAffectsActiveSessions()
    {
        var active = NewSession();
        active.Expire();
        active.Status.ShouldBe(ChatSessionStatus.Expired);
        active.AddMessage(ChatSender.User, "hi", null, Now).Error.ShouldBe(ChatbotErrors.SessionClosed);

        var completed = NewSession();
        completed.Complete(Now);
        completed.Expire();
        completed.Status.ShouldBe(ChatSessionStatus.Completed);
    }

    [Fact]
    public void MessagesGetIncreasingSequence()
    {
        var session = NewSession();

        var first = session.AddMessage(ChatSender.User, "hello", "qr1", Now).Value;
        var second = session.AddMessage(ChatSender.Bot, "hi", null, Now).Value;

        first.Sequence.ShouldBe(1);
        second.Sequence.ShouldBe(2);
        first.SessionId.ShouldBe(session.Id);
        first.QuickReplyId.ShouldBe("qr1");
        session.Messages.Count.ShouldBe(2);
    }
}
