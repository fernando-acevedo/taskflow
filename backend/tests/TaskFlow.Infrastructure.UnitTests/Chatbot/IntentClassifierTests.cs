using TaskFlow.Application.Chatbot;
using TaskFlow.Application.Chatbot.KnowledgeBase;
using TaskFlow.Infrastructure.Chatbot;

namespace TaskFlow.Infrastructure.UnitTests.Chatbot;

/// <summary>Trains on the frozen KB and checks the classifier plumbing, not its accuracy.</summary>
public class IntentClassifierTests
{
    private static readonly Lazy<KnowledgeBase> Kb = new(() =>
        JsonKnowledgeBaseLoader.LoadFromAssembly(typeof(JsonKnowledgeBaseLoader).Assembly));

    private static readonly Lazy<TrainedIntentModel> Trained = new(() => IntentModelTrainer.Train(Kb.Value));

    private static readonly Lazy<MlNetIntentClassifier> Classifier = new(() => RoundTrip(Trained.Value));

    private static readonly Lazy<TextNormalizer> Normalizer = new(() => new TextNormalizer(Kb.Value.Synonyms));

    private static MlNetIntentClassifier RoundTrip(TrainedIntentModel model)
    {
        using var stream = new MemoryStream();
        model.Save(stream);
        stream.Position = 0;
        return MlNetIntentClassifier.Load(stream);
    }

    private static IntentPrediction Classify(string text) => Classifier.Value.Classify(Normalizer.Value.Normalize(text));

    public static TheoryData<string, string> KnownUtterances()
    {
        var data = new TheoryData<string, string>();
        foreach (var intent in Kb.Value.Intents)
        {
            foreach (var locale in new[] { "en", "es" })
            {
                data.Add(intent.Intent, intent.Utterances[locale][0]);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(KnownUtterances))]
    public void ClassifiesKnownUtteranceOfEveryIntentInBothLocales(string intent, string utterance)
    {
        // Plumbing check, not an accuracy gate: the frozen KB has intents that overlap by design (ResetPassword and
        // ForgotPassword), so landing on one of the intent's declared confusedWith neighbors is accepted.
        var accepted = Kb.Value.Intents.Single(i => i.Intent == intent).ConfusedWith.Append(intent);

        accepted.ShouldContain(Classify(utterance).Top.Intent);
    }

    [Theory]
    [InlineData("WrongPassword", "my pasword is incorect")]
    [InlineData("WrongPassword", "dice contrasena incorrecta")]
    [InlineData("OutOfScope", "tell me a jok")]
    [InlineData("LoginIssue", "no puedo entar")]
    public void ToleratesMisspellings(string intent, string utterance)
    {
        Classify(utterance).Top.Intent.ShouldBe(intent);
    }

    [Fact]
    public void ScoresSumToOneAndAreRanked()
    {
        var prediction = Classify("I can't log in");

        prediction.Ranked.Count.ShouldBe(Kb.Value.Intents.Count);
        prediction.Ranked.Sum(s => s.Score).ShouldBe(1.0, 0.001);
        prediction.Ranked.Select(s => s.Score).ShouldBe(prediction.Ranked.Select(s => s.Score).OrderDescending());
        prediction.Second.ShouldNotBeNull();
        prediction.ModelVersion.ShouldBe(Trained.Value.Metadata.ModelVersion);
    }

    [Fact]
    public void SaveAndLoadGiveTheSamePrediction()
    {
        var text = Normalizer.Value.Normalize("el correo no llega");
        var path = Path.Combine(Path.GetTempPath(), $"intent-model-{Guid.NewGuid():N}.zip");
        try
        {
            Trained.Value.Save(path);
            var fromFile = MlNetIntentClassifier.LoadFromFile(path);
            var a = Classifier.Value.Classify(text);
            var b = fromFile.Classify(text);

            fromFile.IsLoaded.ShouldBeTrue();
            b.Ranked.Select(s => s.Intent).ShouldBe(a.Ranked.Select(s => s.Intent));
            b.Top.Score.ShouldBe(a.Top.Score, 1e-6);
            fromFile.ModelVersion.ShouldBe(Trained.Value.Metadata.ModelVersion);
            fromFile.Metadata!.Intents.ShouldBe(Trained.Value.Metadata.Intents);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MetadataHashMatchesTheKnowledgeBase()
    {
        var metadata = Classifier.Value.Metadata.ShouldNotBeNull();

        metadata.TrainingSetHash.ShouldBe(IntentModelTrainer.HashTrainingSet(Kb.Value));
        metadata.SynonymsHash.ShouldBe(IntentModelTrainer.HashSynonyms(Kb.Value.Synonyms));
        metadata.KbVersion.ShouldBe(Kb.Value.Settings.Version);
        metadata.UtteranceCount.ShouldBe(Kb.Value.Intents.Sum(i => i.Utterances.Sum(u => u.Value.Count)));
    }

    [Fact]
    public void HashChangesWhenAnUtteranceChanges()
    {
        var content = new KnowledgeBaseContent
        {
            Settings = Kb.Value.Settings,
            Synonyms = Kb.Value.Synonyms,
            Intents = Kb.Value.Intents
                .Select((intent, i) => i == 0
                    ? intent with { Utterances = intent.Utterances.ToDictionary(u => u.Key, u => (IReadOnlyList<string>)[.. u.Value, "one more"]) }
                    : intent)
                .ToList(),
        };

        IntentModelTrainer.HashTrainingSet(new KnowledgeBase(content)).ShouldNotBe(Trained.Value.Metadata.TrainingSetHash);
    }

    [Fact]
    public void IsSafeUnderParallelCalls()
    {
        var expected = Classify("it says wrong password").Top.Intent;
        var texts = new[] { "it says wrong password", "no puedo entrar", "tell me a joke", "el enlace venció" };
        var expectedByText = texts.ToDictionary(t => t, t => Classify(t).Top.Intent);

        Parallel.For(0, 400, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            var text = texts[i % texts.Length];
            Classify(text).Top.Intent.ShouldBe(expectedByText[text]);
        });

        expectedByText["it says wrong password"].ShouldBe(expected);
    }

    [Fact]
    public void WithoutAModelItIsNotLoadedAndThrows()
    {
        var classifier = new MlNetIntentClassifier(Microsoft.Extensions.Options.Options.Create(
            new ChatbotModelOptions { Path = "does-not-exist.zip", EmbeddedResource = "nope" }));

        classifier.IsLoaded.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => classifier.Classify("hello"));
    }

    [Fact]
    public void EvaluationReportsMetricsForEveryLocale()
    {
        var metrics = IntentModelTrainer.Evaluate(Kb.Value);

        metrics.Samples.ShouldBe(Trained.Value.Metadata.UtteranceCount);
        metrics.PerLocaleAccuracy.Keys.ShouldBe(["en", "es"], ignoreOrder: true);
        metrics.Intents.Count.ShouldBe(Kb.Value.Intents.Count);
        metrics.ConfusionMatrix.Sum(row => row.Sum()).ShouldBe(metrics.Samples);
        metrics.MacroAccuracy.ShouldBeInRange(0, 1);
        metrics.SuggestedThreshold.ShouldBeInRange(0.3, 1.0);
    }
}
