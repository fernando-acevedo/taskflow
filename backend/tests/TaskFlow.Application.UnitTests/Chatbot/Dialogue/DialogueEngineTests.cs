using Shouldly;
using TaskFlow.Application.Chatbot;
using TaskFlow.Application.Chatbot.Dialogue;
using TaskFlow.Application.Chatbot.KnowledgeBase;

namespace TaskFlow.Application.UnitTests.Chatbot.Dialogue;

/// <summary>Engine rules on a tiny in-memory KB (the real KB is walked in TaskFlow.Infrastructure.UnitTests).</summary>
public class DialogueEngineTests
{
    private sealed class FixedPicker(int index) : IVariantPicker
    {
        public int Pick(int count) => Math.Min(index, count - 1);
    }

    private static Dictionary<string, IReadOnlyList<string>> Text(string name) =>
        new()
        {
            ["en"] = [$"{name} en 0", $"{name} en 1"],
            ["es"] = [$"{name} es 0", $"{name} es 1"],
        };

    private static QuickReply Reply(string id, string? next = null, string? url = null, string? intent = null) =>
        new()
        {
            Id = id,
            Label = new Dictionary<string, string> { ["en"] = id + " en", ["es"] = id + " es" },
            Next = next,
            Url = url,
            Intent = intent,
        };

    private static KnowledgeBase Kb() => new(new KnowledgeBaseContent
    {
        Settings = new KnowledgeBaseSettings
        {
            Locales = ["en", "es"],
            ConfidenceThreshold = 0.65,
            MinMargin = 0.1,
            MaxConsecutiveFallbacks = 2,
            StartStep = "start",
            FallbackStep = "fallback",
            EscalateStep = "escalate",
            OutOfScopeIntent = "OutOfScope",
        },
        CommonSteps = new Dictionary<string, DialogStep>
        {
            ["start"] = new() { Text = Text("start"), QuickReplies = [Reply("a", next: "a.start"), Reply("jump", intent: "A")] },
            ["fallback"] = new() { Text = Text("fallback"), QuickReplies = [Reply("a", next: "a.start"), Reply("help", url: "{help}")] },
            ["escalate"] = new() { Text = Text("escalate"), End = true, Outcome = ChatOutcome.Escalated },
            ["end"] = new() { Text = Text("end"), End = true, Outcome = ChatOutcome.Completed },
        },
        Intents =
        [
            new IntentDefinition
            {
                Intent = "A",
                EntryStep = "a.start",
                Steps = new Dictionary<string, DialogStep>
                {
                    ["a.start"] = new() { Text = Text("a"), QuickReplies = [Reply("done", next: "end"), Reply("open", url: "/x")] },
                },
            },
            new IntentDefinition { Intent = "OutOfScope", EntryStep = "end" },
        ],
    });

    private static DialogueEngine Engine(int pick = 0, string help = "https://h.test")
    {
        var kb = Kb();
        var links = new Dictionary<string, string> { ["help"] = help };
        return new DialogueEngine(kb, new LocalizedReplyProvider(kb, new FixedPicker(pick), links));
    }

    private static IntentPrediction Predict(double top, double second, string intent = "A") =>
        new([new IntentScore(intent, top), new IntentScore("B", second)], "t");

    [Fact]
    public void StartRejectsUnsupportedLocale() =>
        Engine().Start("fr").Error!.Code.ShouldBe("Chatbot.InvalidLocale");

    [Fact]
    public void PickerChoosesTheVariant()
    {
        Engine(pick: 1).Start("en").Value.Reply.Text.ShouldBe("start en 1");
        Engine(pick: 0).Start("es").Value.Reply.Text.ShouldBe("start es 0");
    }

    [Fact]
    public void RandomPickerStaysInRangeAndCoversEveryIndex()
    {
        var picker = new RandomVariantPicker();
        var seen = Enumerable.Range(0, 500).Select(_ => picker.Pick(5)).ToHashSet();
        seen.ShouldBe([0, 1, 2, 3, 4], ignoreOrder: true);
        picker.Pick(1).ShouldBe(0);
    }

    [Fact]
    public void QuickReplyGoesToNextAndPassesTheUrl()
    {
        var engine = Engine();
        var turn = engine.Start("en").Value;
        turn = engine.Advance(turn.State, DialogueInput.QuickReply("a")).Value;
        turn.Reply.StepId.ShouldBe("a.start");
        turn = engine.Advance(turn.State, DialogueInput.QuickReply("open")).Value;
        turn.Reply.Url.ShouldBe("/x");
        turn.Reply.StepId.ShouldBe("a.start");
    }

    [Fact]
    public void QuickReplyWithIntentGoesToItsEntryStep()
    {
        var engine = Engine();
        var turn = engine.Advance(engine.Start("en").Value.State, DialogueInput.QuickReply("jump")).Value;
        turn.Reply.StepId.ShouldBe("a.start");
        turn.State.Intent.ShouldBe("A");
    }

    [Fact]
    public void UnknownQuickReplyIsAnError()
    {
        var engine = Engine();
        engine.Advance(engine.Start("en").Value.State, DialogueInput.QuickReply("nope")).Error!.Code
            .ShouldBe("Chatbot.UnknownQuickReply");
    }

    [Fact]
    public void EndedSessionRejectsInput()
    {
        var engine = Engine();
        var state = engine.Start("en").Value.State;
        state = engine.Advance(state, DialogueInput.QuickReply("a")).Value.State;
        var ended = engine.Advance(state, DialogueInput.QuickReply("done")).Value;
        ended.Reply.Ended.ShouldBeTrue();
        ended.Reply.Outcome.ShouldBe(ChatOutcome.Completed);
        engine.Advance(ended.State, DialogueInput.Text(null)).Error!.Code.ShouldBe("Chatbot.SessionClosed");
    }

    [Theory]
    [InlineData(0.65, 0.5, true)]
    [InlineData(0.64, 0.0, false)]
    [InlineData(0.9, 0.85, false)]
    [InlineData(0.9, 0.8, true)]
    public void AcceptsOnlyConfidentPredictionsWithMargin(double top, double second, bool accepted)
    {
        var engine = Engine();
        var turn = engine.Advance(engine.Start("en").Value.State, DialogueInput.Text(Predict(top, second))).Value;
        turn.Reply.IsFallback.ShouldBe(!accepted);
        turn.Reply.StepId.ShouldBe(accepted ? "a.start" : "fallback");
    }

    [Fact]
    public void OutOfScopeIntentGoesToItsFlow()
    {
        var engine = Engine();
        var turn = engine.Advance(engine.Start("en").Value.State, DialogueInput.Text(Predict(0.95, 0.01, "OutOfScope"))).Value;
        turn.Reply.StepId.ShouldBe("end");
    }

    [Fact]
    public void MissingPredictionAndUnknownIntentAreFallbacks()
    {
        var engine = Engine();
        var state = engine.Start("en").Value.State;
        engine.Advance(state, DialogueInput.Text(null)).Value.Reply.IsFallback.ShouldBeTrue();
        engine.Advance(state, DialogueInput.Text(Predict(0.95, 0.01, "Ghost"))).Value.Reply.IsFallback.ShouldBeTrue();
    }

    [Fact]
    public void EscalatesAfterMaxFallbacksAndSuccessResetsTheCounter()
    {
        var engine = Engine();
        var miss = DialogueInput.Text(Predict(0.2, 0.1));
        var state = engine.Start("en").Value.State;

        var t1 = engine.Advance(state, miss).Value;
        var t2 = engine.Advance(t1.State, miss).Value;
        t2.State.ConsecutiveFallbacks.ShouldBe(2);
        t2.Reply.StepId.ShouldBe("fallback");

        var ok = engine.Advance(t2.State, DialogueInput.QuickReply("a")).Value;
        ok.State.ConsecutiveFallbacks.ShouldBe(0);
        engine.Advance(ok.State, miss).Value.State.ConsecutiveFallbacks.ShouldBe(1);

        var t3 = engine.Advance(t2.State, miss).Value;
        t3.Reply.StepId.ShouldBe("escalate");
        t3.Reply.Ended.ShouldBeTrue();
        t3.Reply.Outcome.ShouldBe(ChatOutcome.Escalated);
        t3.State.Ended.ShouldBeTrue();
    }

    [Fact]
    public void HelpQuickReplyIsHiddenWhenTheLinkIsEmpty()
    {
        var miss = DialogueInput.Text(Predict(0.2, 0.1));
        var shown = Engine().Advance(Engine().Start("en").Value.State, miss).Value;
        shown.Reply.QuickReplies.Select(q => q.Id).ShouldBe(["a", "help"]);
        shown.Reply.QuickReplies[1].Url.ShouldBe("https://h.test");

        var engine = Engine(help: "");
        var hidden = engine.Advance(engine.Start("en").Value.State, miss).Value;
        hidden.Reply.QuickReplies.Select(q => q.Id).ShouldBe(["a"]);
        engine.Advance(hidden.State, DialogueInput.QuickReply("help")).IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public void LabelsFollowTheSessionLocale()
    {
        var turn = Engine().Start("es").Value;
        turn.Reply.QuickReplies.Select(q => q.Label).ShouldBe(["a es", "jump es"]);
    }
}
