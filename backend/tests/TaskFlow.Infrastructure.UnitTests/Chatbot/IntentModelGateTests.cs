using Microsoft.Extensions.Options;
using TaskFlow.Application.Chatbot.KnowledgeBase;
using TaskFlow.Infrastructure.Chatbot;

namespace TaskFlow.Infrastructure.UnitTests.Chatbot;

/// <summary>
/// Quality gates for the intent model (ADR 0026): per-locale cross-validation accuracy floors and a stale-model check.
/// To refresh the embedded model and docs/analysis/f13-classifier-eval.md after a KB change run, from backend/:
/// <c>dotnet run --project tools/TaskFlow.Chatbot.Trainer -- --kb src/TaskFlow.Infrastructure/Chatbot/KnowledgeBase
/// --out src/TaskFlow.Infrastructure/Chatbot/Model --report ../docs/analysis/f13-classifier-eval.md</c>.
/// </summary>
public class IntentModelGateTests
{
    // Measured 5-fold CV accuracy with seed 42 is 0.867 (en) and 0.869 (es); the floor leaves a little room.
    private const double LocaleAccuracyFloor = 0.85;

    private static readonly Lazy<KnowledgeBase> Kb = new(() =>
        JsonKnowledgeBaseLoader.LoadFromAssembly(typeof(JsonKnowledgeBaseLoader).Assembly));

    private static readonly Lazy<IntentModelMetrics> Metrics = new(() => IntentModelTrainer.Evaluate(Kb.Value));

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public void CrossValidationAccuracyPerLocaleStaysAboveTheFloor(string locale)
    {
        Metrics.Value.PerLocaleAccuracy[locale].ShouldBeGreaterThanOrEqualTo(LocaleAccuracyFloor);
    }

    [Fact]
    public void EmbeddedModelIsLoadedAndMatchesTheCurrentKnowledgeBase()
    {
        var classifier = new MlNetIntentClassifier(Options.Create(new ChatbotModelOptions()));

        classifier.IsLoaded.ShouldBeTrue("the embedded intent-model.zip is missing");
        var metadata = classifier.Metadata.ShouldNotBeNull();
        var message = "The embedded intent model is stale: retrain it with the TaskFlow.Chatbot.Trainer tool (see this class).";
        metadata.TrainingSetHash.ShouldBe(IntentModelTrainer.HashTrainingSet(Kb.Value), message);
        metadata.SynonymsHash.ShouldBe(IntentModelTrainer.HashSynonyms(Kb.Value.Synonyms), message);
        metadata.KbVersion.ShouldBe(Kb.Value.Settings.Version, message);
        metadata.Intents.Count.ShouldBe(Kb.Value.Intents.Count, message);
    }
}
