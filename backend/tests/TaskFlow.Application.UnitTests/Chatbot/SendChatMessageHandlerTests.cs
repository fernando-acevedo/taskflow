using Microsoft.EntityFrameworkCore;
using NSubstitute;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Chatbot;
using TaskFlow.Application.Chatbot.Dialogue;
using TaskFlow.Application.Chatbot.KnowledgeBase;
using TaskFlow.Domain.Chatbot;
using KbOutcome = TaskFlow.Application.Chatbot.KnowledgeBase.ChatOutcome;
using LogOutcome = TaskFlow.Domain.Chatbot.ChatOutcome;

namespace TaskFlow.Application.UnitTests.Chatbot;

public sealed class SendChatMessageHandlerTests
{
    private sealed class ManualTime(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class FirstPicker : IVariantPicker
    {
        public int Pick(int count) => 0;
    }

    private static readonly DateTimeOffset Start = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    private readonly ManualTime _time = new(Start);
    private readonly IAppDbContext _db = Substitute.For<IAppDbContext>();
    private readonly IIntentClassifier _classifier = Substitute.For<IIntentClassifier>();
    private readonly List<ChatSession> _sessions = [];
    private readonly DbSet<ChatSession> _sessionSet;
    private readonly DbSet<IntentLog> _logs = FakeDbSet.Of<IntentLog>();
    private readonly KnowledgeBase _kb = Kb();
    private readonly SendChatMessageHandler _handler;

    public SendChatMessageHandlerTests()
    {
        _sessionSet = FakeDbSet.Of<ChatSession>();
        _db.ChatSessions.Returns(_ => _sessionSet);
        _db.IntentLogs.Returns(_logs);
        _classifier.IsLoaded.Returns(true);
        _sessionSet.When(set => set.Add(Arg.Any<ChatSession>())).Do(call => _sessions.Add(call.Arg<ChatSession>()));
        var replies = new LocalizedReplyProvider(_kb, new FirstPicker(), new Dictionary<string, string> { ["help"] = "https://h.test" });
        _handler = new SendChatMessageHandler(
            _db, new SendChatMessageValidator(), _kb, new DialogueEngine(_kb, replies), _classifier, new TextNormalizer(_kb.Synonyms), _time);
    }

    private static Dictionary<string, IReadOnlyList<string>> Text(string name) =>
        new() { ["en"] = [$"{name} en"], ["es"] = [$"{name} es"] };

    private static QuickReply Reply(string id, string? next = null, string? url = null) =>
        new()
        {
            Id = id,
            Label = new Dictionary<string, string> { ["en"] = id + " en", ["es"] = id + " es" },
            Next = next,
            Url = url,
        };

    private static KnowledgeBase Kb() => new(new KnowledgeBaseContent
    {
        Settings = new KnowledgeBaseSettings
        {
            Locales = ["en", "es"],
            ConfidenceThreshold = 0.65,
            MinMargin = 0.1,
            MaxConsecutiveFallbacks = 2,
            SessionIdleMinutes = 30,
            StartStep = "start",
            FallbackStep = "fallback",
            EscalateStep = "escalate",
            OutOfScopeIntent = "OutOfScope",
        },
        CommonSteps = new Dictionary<string, DialogStep>
        {
            ["start"] = new() { Text = Text("start"), QuickReplies = [Reply("a", next: "a.start")] },
            ["fallback"] = new() { Text = Text("fallback"), QuickReplies = [Reply("a", next: "a.start")] },
            ["escalate"] = new() { Text = Text("escalate"), End = true, Outcome = KbOutcome.Escalated },
            ["end"] = new() { Text = Text("end"), End = true, Outcome = KbOutcome.Completed },
        },
        Intents =
        [
            new IntentDefinition
            {
                Intent = "A",
                EntryStep = "a.start",
                Steps = new Dictionary<string, DialogStep> { ["a.start"] = new() { Text = Text("a"), QuickReplies = [Reply("done", next: "end")] } },
            },
            new IntentDefinition { Intent = "OutOfScope", EntryStep = "end" },
        ],
    });

    private static IntentPrediction Predict(double top, double second) =>
        new([new IntentScore("A", top), new IntentScore("B", second)], "m1");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<SendChatMessageResult> Send(Guid? session, string locale, string? text = null, string? quick = null)
    {
        var result = await _handler.HandleAsync(new SendChatMessageCommand(session, locale, text, quick), Ct);
        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
        return result.Value;
    }

    private async Task<ChatSession> Existing(string locale = "en")
    {
        var opened = await Send(null, locale);
        var session = _sessions.Single(s => s.Id == opened.SessionId);
        _sessionSet.ClearReceivedCalls();
        RebindSessions(session);
        return session;
    }

    private void RebindSessions(params ChatSession[] sessions)
    {
        var set = FakeDbSet.Of(sessions);
        _db.ChatSessions.Returns(set);
    }

    [Fact]
    public async Task NewSessionWithoutInputReturnsTheStartStep()
    {
        var result = await Send(null, "es");

        result.NewSession.ShouldBeTrue();
        result.Locale.ShouldBe("es");
        result.Reply.StepId.ShouldBe("start");
        result.Reply.Text.ShouldBe("start es");
        var session = _sessions.Single();
        session.Id.ShouldBe(result.SessionId);
        session.CurrentStep.ShouldBe("start");
        session.Messages.Select(m => m.Sender).ShouldBe([ChatSender.Bot]);
        await _db.Received(1).SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task RejectsAnUnsupportedLocaleForANewSession()
    {
        var result = await _handler.HandleAsync(new SendChatMessageCommand(null, "fr", null, null), Ct);

        result.Error!.Code.ShouldBe("Chatbot.InvalidLocale");
        _sessions.ShouldBeEmpty();
    }

    [Fact]
    public async Task QuickReplyAdvancesTheSessionAndStoresBothMessages()
    {
        var session = await Existing();

        var result = await Send(session.Id, "en", quick: "a");

        result.NewSession.ShouldBeFalse();
        result.Reply.StepId.ShouldBe("a.start");
        session.CurrentStep.ShouldBe("a.start");
        session.Messages.Select(m => (m.Sequence, m.Sender)).ShouldBe([(1, ChatSender.Bot), (2, ChatSender.User), (3, ChatSender.Bot)]);
        session.Messages.Single(m => m.Sender == ChatSender.User).QuickReplyId.ShouldBe("a");
        _logs.DidNotReceive().Add(Arg.Any<IntentLog>());
    }

    [Fact]
    public async Task UnknownQuickReplyIsRejected()
    {
        var session = await Existing();

        var result = await _handler.HandleAsync(new SendChatMessageCommand(session.Id, "en", null, "nope"), Ct);

        result.Error!.Code.ShouldBe("Chatbot.UnknownQuickReply");
    }

    [Fact]
    public async Task LocaleIsFixedForTheSession()
    {
        var session = await Existing("en");

        var result = await Send(session.Id, "es", quick: "a");

        result.Locale.ShouldBe("en");
        result.Reply.Text.ShouldBe("a en");
    }

    [Fact]
    public async Task IdleSessionStartsANewOne()
    {
        var old = await Existing();
        _time.Advance(TimeSpan.FromMinutes(31));

        var result = await Send(old.Id, "es");

        result.NewSession.ShouldBeTrue();
        result.SessionId.ShouldNotBe(old.Id);
        result.Locale.ShouldBe("es");
        old.Status.ShouldBe(ChatSessionStatus.Expired);
    }

    [Fact]
    public async Task ActivityKeepsTheSessionAliveBeyondTheIdleWindow()
    {
        var session = await Existing();
        _time.Advance(TimeSpan.FromMinutes(20));
        await Send(session.Id, "en", quick: "a");
        _time.Advance(TimeSpan.FromMinutes(20));

        var result = await Send(session.Id, "en", quick: "done");

        result.NewSession.ShouldBeFalse();
        result.Reply.Ended.ShouldBeTrue();
        result.Reply.Outcome.ShouldBe(KbOutcome.Completed);
        session.Status.ShouldBe(ChatSessionStatus.Completed);
    }

    [Fact]
    public async Task UnknownSessionIdStartsANewSession()
    {
        RebindSessions();

        var result = await Send(Guid.NewGuid(), "en");

        result.NewSession.ShouldBeTrue();
    }

    [Fact]
    public async Task ClassifiedTextRoutesToTheIntentAndLogsRedactedText()
    {
        var session = await Existing();
        _classifier.Classify(Arg.Any<string>()).Returns(Predict(0.9, 0.05));
        IntentLog? logged = null;
        _logs.When(set => set.Add(Arg.Any<IntentLog>())).Do(call => logged = call.Arg<IntentLog>());

        var result = await Send(session.Id, "en", text: "I forgot my password, write to me at ana@example.com");

        result.Reply.StepId.ShouldBe("a.start");
        result.Reply.IsFallback.ShouldBeFalse();
        var userMessage = session.Messages.Single(m => m.Sender == ChatSender.User);
        userMessage.Text.ShouldNotContain("ana@example.com");
        logged.ShouldNotBeNull();
        logged.RedactedText.ShouldNotContain("ana@example.com");
        logged.NormalizedText.ShouldNotContain("example");
        logged.PredictedIntent.ShouldBe("A");
        logged.Confidence.ShouldBe(0.9);
        logged.SecondIntent.ShouldBe("B");
        logged.SecondConfidence.ShouldBe(0.05);
        logged.ModelVersion.ShouldBe("m1");
        logged.Outcome.ShouldBe(LogOutcome.Answered);
        logged.ReceivedLocale.ShouldBe("en");
        logged.MessageId.ShouldBe(userMessage.Id);
        logged.SessionId.ShouldBe(session.Id);
    }

    [Fact]
    public async Task ThresholdComesFromTheKnowledgeBase()
    {
        var session = await Existing();
        _classifier.Classify(Arg.Any<string>()).Returns(Predict(_kb.Settings.ConfidenceThreshold - 0.01, 0.0));

        var result = await Send(session.Id, "en", text: "something vague");

        result.Reply.IsFallback.ShouldBeTrue();
        result.Reply.StepId.ShouldBe("fallback");
    }

    [Fact]
    public async Task RepeatedFallbacksEscalateAndCloseTheSession()
    {
        var session = await Existing();
        _classifier.Classify(Arg.Any<string>()).Returns(Predict(0.2, 0.1));
        var logged = new List<IntentLog>();
        _logs.When(set => set.Add(Arg.Any<IntentLog>())).Do(call => logged.Add(call.Arg<IntentLog>()));

        var first = await Send(session.Id, "en", text: "blah");
        var second = await Send(session.Id, "en", text: "blah blah");
        session.ConsecutiveFallbacks.ShouldBe(2);
        var third = await Send(session.Id, "en", text: "blah blah blah");

        first.Reply.StepId.ShouldBe("fallback");
        second.Reply.StepId.ShouldBe("fallback");
        third.Reply.Ended.ShouldBeTrue();
        third.Reply.Outcome.ShouldBe(KbOutcome.Escalated);
        session.Status.ShouldBe(ChatSessionStatus.Escalated);
        logged.Select(l => l.Outcome).ShouldBe([LogOutcome.Fallback, LogOutcome.Fallback, LogOutcome.Escalated]);

        var after = await Send(session.Id, "en", text: "hello again");
        after.NewSession.ShouldBeTrue();
    }

    [Fact]
    public async Task UnloadedClassifierAnswersWithTheFallback()
    {
        var session = await Existing();
        _classifier.IsLoaded.Returns(false);
        IntentLog? logged = null;
        _logs.When(set => set.Add(Arg.Any<IntentLog>())).Do(call => logged = call.Arg<IntentLog>());

        var result = await Send(session.Id, "en", text: "password");

        result.Reply.IsFallback.ShouldBeTrue();
        logged!.PredictedIntent.ShouldBeNull();
        _classifier.DidNotReceive().Classify(Arg.Any<string>());
    }

    [Fact]
    public async Task EmptyTurnOnAnExistingSessionIsRejected()
    {
        var session = await Existing();

        var result = await _handler.HandleAsync(new SendChatMessageCommand(session.Id, "en", "   ", null), Ct);

        result.Error!.Code.ShouldBe("Chatbot.EmptyMessage");
    }

    [Fact]
    public async Task TooLongTextIsRejected()
    {
        var result = await _handler.HandleAsync(
            new SendChatMessageCommand(null, "en", new string('a', ChatMessage.TextMaxLength + 1), null), Ct);

        result.Error!.Code.ShouldBe("Chatbot.MessageTooLong");
    }

    [Fact]
    public async Task ConcurrentTurnsReportStale()
    {
        var session = await Existing();
        _db.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns<int>(_ => throw new DbUpdateConcurrencyException());

        var result = await _handler.HandleAsync(new SendChatMessageCommand(session.Id, "en", null, "a"), Ct);

        result.Error!.Code.ShouldBe("Concurrency.Stale");
    }
}
