using System.Text.RegularExpressions;
using Shouldly;
using TaskFlow.Application.Chatbot;
using TaskFlow.Application.Chatbot.Dialogue;
using TaskFlow.Application.Chatbot.KnowledgeBase;
using TaskFlow.Infrastructure.Chatbot;

namespace TaskFlow.Infrastructure.UnitTests.Chatbot;

/// <summary>Walks every flow of the real frozen KB in both locales through the dialogue engine.</summary>
public partial class DialogueFlowTests
{
    private static readonly string[] Locales = ["en", "es"];

    private static readonly IReadOnlyDictionary<string, string> Links = new Dictionary<string, string>
    {
        ["support"] = "support@example.test",
        ["help"] = "https://help.example.test",
    };

    public static TheoryData<string> LocaleData => new(Locales);

    private static KnowledgeBase Kb { get; } =
        JsonKnowledgeBaseLoader.LoadFromAssembly(typeof(JsonKnowledgeBaseLoader).Assembly);

    private sealed class FixedPicker(int index) : IVariantPicker
    {
        public int Pick(int count) => Math.Min(index, count - 1);
    }

    private static DialogueEngine Engine(IReadOnlyDictionary<string, string>? links = null, int pick = 0) =>
        new(Kb, new LocalizedReplyProvider(Kb, new FixedPicker(pick), links ?? Links));

    private static DialogueState At(string locale, string step) => new(locale, null, step, 0);

    private static IntentPrediction Predict(string intent, double score = 0.9, double second = 0.05) =>
        new([new IntentScore(intent, score), new IntentScore("Other", second)], "test");

    private static IntentPrediction Unsure() =>
        new([new IntentScore("LoginIssue", 0.4), new IntentScore("Greeting", 0.3)], "test");

    [Theory]
    [MemberData(nameof(LocaleData))]
    public void EveryQuickReplyOfEveryStepLeadsToAReply(string locale)
    {
        var engine = Engine();
        foreach (var (stepId, step) in Kb.Steps)
        {
            foreach (var quickReply in step.QuickReplies)
            {
                var turn = engine.Advance(At(locale, stepId), DialogueInput.QuickReply(quickReply.Id));
                turn.IsSuccess.ShouldBeTrue($"{stepId}/{quickReply.Id}");
                if (quickReply.Next is not null)
                {
                    turn.Value.Reply.StepId.ShouldBe(quickReply.Next);
                }

                if (quickReply.Url is not null)
                {
                    turn.Value.Reply.Url.ShouldNotBeNullOrEmpty($"{stepId}/{quickReply.Id}");
                    turn.Value.Reply.Url.ShouldNotContain("{");
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(LocaleData))]
    public void EveryStepIsReachableFromStartByQuickRepliesOrIntents(string locale)
    {
        var engine = Engine();
        var seen = new HashSet<string> { Kb.Settings.StartStep };
        var queue = new Queue<string>([Kb.Settings.StartStep]);

        // Intents are reachable through free text from any step where the user can type (start, fallback, other).
        foreach (var intent in Kb.Intents)
        {
            var turn = engine.Advance(At(locale, Kb.Settings.StartStep), DialogueInput.Text(Predict(intent.Intent))).Value;
            if (seen.Add(turn.Reply.StepId))
            {
                queue.Enqueue(turn.Reply.StepId);
            }
        }

        seen.Add(Kb.Settings.FallbackStep);
        queue.Enqueue(Kb.Settings.FallbackStep);
        while (queue.TryDequeue(out var id))
        {
            foreach (var quickReply in Kb.Steps[id].QuickReplies)
            {
                var turn = engine.Advance(At(locale, id), DialogueInput.QuickReply(quickReply.Id)).Value;
                if (seen.Add(turn.Reply.StepId))
                {
                    queue.Enqueue(turn.Reply.StepId);
                }
            }
        }

        Kb.Steps.Keys.Except(seen).ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(LocaleData))]
    public void EveryStepCanReachAnEndStep(string locale)
    {
        var engine = Engine();
        var edges = new Dictionary<string, HashSet<string>>();
        foreach (var (stepId, step) in Kb.Steps)
        {
            // A step without buttons (it expects text) continues by typing: a miss leads to fallback, then escalation.
            edges[stepId] = step.End
                ? []
                : step.QuickReplies.Count == 0
                    ? [Kb.Settings.FallbackStep]
                : step.QuickReplies
                    .Select(q => engine.Advance(At(locale, stepId), DialogueInput.QuickReply(q.Id)).Value.Reply.StepId)
                    .Where(next => next != stepId)
                    .ToHashSet();
        }

        var canEnd = Kb.Steps.Where(s => s.Value.End).Select(s => s.Key).ToHashSet();
        bool changed;
        do
        {
            changed = false;
            foreach (var (stepId, next) in edges)
            {
                if (!canEnd.Contains(stepId) && next.Overlaps(canEnd))
                {
                    canEnd.Add(stepId);
                    changed = true;
                }
            }
        }
        while (changed);

        Kb.Steps.Keys.Except(canEnd).ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(LocaleData))]
    public void EndStepsEndTheSessionWithTheirOutcome(string locale)
    {
        var engine = Engine();
        foreach (var (stepId, step) in Kb.Steps.Where(s => s.Value.End))
        {
            var entry = Kb.Steps.First(s => s.Value.QuickReplies.Any(q => q.Next == stepId) && !s.Value.End);
            var click = entry.Value.QuickReplies.First(q => q.Next == stepId);
            var turn = engine.Advance(At(locale, entry.Key), DialogueInput.QuickReply(click.Id)).Value;
            turn.Reply.Ended.ShouldBeTrue(stepId);
            turn.Reply.Outcome.ShouldBe(step.Outcome);
            turn.Reply.QuickReplies.ShouldBeEmpty();
            turn.State.Ended.ShouldBeTrue();
            engine.Advance(turn.State, DialogueInput.QuickReply(click.Id)).IsSuccess.ShouldBeFalse();
        }
    }

    [Theory]
    [MemberData(nameof(LocaleData))]
    public void EveryIntentGoesToItsEntryStep(string locale)
    {
        var engine = Engine();
        foreach (var intent in Kb.Intents)
        {
            var turn = engine.Advance(At(locale, Kb.Settings.StartStep), DialogueInput.Text(Predict(intent.Intent))).Value;
            turn.Reply.StepId.ShouldBe(intent.EntryStep, intent.Intent);
            turn.Reply.IsFallback.ShouldBeFalse();
            turn.Reply.MatchedIntent.ShouldBe(intent.Intent);
        }

        var human = engine.Advance(At(locale, Kb.Settings.StartStep), DialogueInput.Text(Predict("HumanAgent"))).Value;
        human.Reply.Outcome.ShouldBe(ChatOutcome.Escalated);
    }

    [Fact]
    public void QuickRepliesAndPlaceholdersMatchBetweenLocales()
    {
        foreach (var (stepId, step) in Kb.Steps)
        {
            var en = Kb.FindStep(stepId)!.QuickReplies.Select(q => (q.Id, Placeholders(q.Label["en"]))).ToList();
            var es = step.QuickReplies.Select(q => (q.Id, Placeholders(q.Label["es"]))).ToList();
            es.ShouldBe(en, stepId);
            step.QuickReplies.ShouldAllBe(q => q.Label["en"].Length > 0 && q.Label["es"].Length > 0);

            var enPh = step.Text["en"].Select(Placeholders).ToList();
            var esPh = step.Text["es"].Select(Placeholders).ToList();
            enPh.Concat(esPh).ShouldAllBe(p => p.SequenceEqual(enPh[0]), stepId);
            step.Text["en"].Count.ShouldBe(step.Text["es"].Count, stepId);
        }
    }

    [Theory]
    [InlineData("en", "Hi")]
    [InlineData("es", "Hola")]
    public void RepliesAreInTheSessionLocale(string locale, string startsWith)
    {
        var turn = Engine().Start(locale).Value;
        turn.State.Locale.ShouldBe(locale);
        turn.Reply.Text.ShouldContain(startsWith);
        turn.Reply.QuickReplies.Select(q => q.Label).ShouldBe(
            Kb.Steps[Kb.Settings.StartStep].QuickReplies.Select(q => q.Label[locale]));
        turn.State.Step.ShouldBe(Kb.Settings.StartStep);
    }

    [Theory]
    [MemberData(nameof(LocaleData))]
    public void EachVariantIsPickedByItsIndex(string locale)
    {
        var variants = Kb.Steps[Kb.Settings.StartStep].Text[locale];
        for (var i = 0; i < variants.Count; i++)
        {
            var turn = Engine(pick: i).Start(locale).Value;
            turn.Reply.Text.ShouldBe(variants[i]);
        }
    }

    [Fact]
    public void LinksAreFilledIntoTexts()
    {
        var supportSteps = Kb.Steps.Where(s => s.Value.Text["en"].Any(t => t.Contains("{support}"))).Select(s => s.Key).ToList();
        supportSteps.ShouldNotBeEmpty();
        var engine = Engine();
        foreach (var locale in Locales)
        {
            foreach (var id in supportSteps)
            {
                var step = Kb.Steps[id];
                step.Text[locale].Select((_, i) => Engine(pick: i)).ShouldNotBeEmpty();
                var reply = ReplyOf(engine, locale, id);
                reply.Text.ShouldNotContain("{support}");
                reply.Text.ShouldContain("support@example.test");
            }
        }
    }

    [Theory]
    [MemberData(nameof(LocaleData))]
    public void HelpQuickReplyIsHiddenWhenTheHelpLinkIsEmpty(string locale)
    {
        var noHelp = new Dictionary<string, string> { ["support"] = "s@example.test", ["help"] = "" };
        var withHelp = Engine().Advance(At(locale, Kb.Settings.StartStep), DialogueInput.Text(Unsure())).Value;
        withHelp.Reply.QuickReplies.ShouldContain(q => q.Id == "help" && q.Url == "https://help.example.test");

        var engine = Engine(noHelp);
        var hidden = engine.Advance(At(locale, Kb.Settings.StartStep), DialogueInput.Text(Unsure())).Value;
        hidden.Reply.QuickReplies.ShouldNotContain(q => q.Id == "help");
        engine.Advance(hidden.State, DialogueInput.QuickReply("help")).Error!.Code.ShouldBe("Chatbot.UnknownQuickReply");
    }

    [Theory]
    [MemberData(nameof(LocaleData))]
    public void EscalatesAfterTwoFallbacksAndSuccessResetsTheStreak(string locale)
    {
        var engine = Engine();
        var turn = engine.Start(locale).Value;
        turn = engine.Advance(turn.State, DialogueInput.Text(Unsure())).Value;
        turn.Reply.StepId.ShouldBe(Kb.Settings.FallbackStep);
        turn = engine.Advance(turn.State, DialogueInput.Text(Unsure())).Value;
        turn.Reply.StepId.ShouldBe(Kb.Settings.FallbackStep);
        turn.State.ConsecutiveFallbacks.ShouldBe(2);

        var reset = engine.Advance(turn.State, DialogueInput.QuickReply("cant_login")).Value;
        reset.State.ConsecutiveFallbacks.ShouldBe(0);

        turn = engine.Advance(turn.State, DialogueInput.Text(Unsure())).Value;
        turn.Reply.StepId.ShouldBe(Kb.Settings.EscalateStep);
        turn.Reply.Ended.ShouldBeTrue();
        turn.Reply.Outcome.ShouldBe(ChatOutcome.Escalated);
    }

    private static DialogueReply ReplyOf(DialogueEngine engine, string locale, string stepId)
    {
        // Show a step by jumping to it: the first step with a quick reply (or intent entry) that leads there.
        var from = Kb.Steps.FirstOrDefault(s => s.Value.QuickReplies.Any(q => q.Next == stepId));
        if (from.Key is not null)
        {
            return engine.Advance(At(locale, from.Key), DialogueInput.QuickReply(from.Value.QuickReplies.First(q => q.Next == stepId).Id)).Value.Reply;
        }

        var intent = Kb.Intents.First(i => i.EntryStep == stepId);
        return engine.Advance(At(locale, Kb.Settings.StartStep), DialogueInput.Text(Predict(intent.Intent))).Value.Reply;
    }

    private static string[] Placeholders(string text) =>
        PlaceholderRegex().Matches(text).Select(m => m.Value).Order().Distinct().ToArray();

    [GeneratedRegex(@"\{\w+\}")]
    private static partial Regex PlaceholderRegex();
}
