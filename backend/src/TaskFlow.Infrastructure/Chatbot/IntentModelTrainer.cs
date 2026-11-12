using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Transforms;
using Microsoft.ML.Transforms.Text;
using TaskFlow.Application.Chatbot;
using TaskFlow.Application.Chatbot.KnowledgeBase;

namespace TaskFlow.Infrastructure.Chatbot;

/// <summary>Evaluation of the pipeline by stratified k-fold cross validation (ADR 0026).</summary>
public sealed record IntentModelMetrics
{
    public int Folds { get; init; }

    public int Samples { get; init; }

    public double Accuracy { get; init; }

    /// <summary>Mean of the per-intent recall.</summary>
    public double MacroAccuracy { get; init; }

    public IReadOnlyDictionary<string, double> PerLocaleAccuracy { get; init; } = new Dictionary<string, double>();

    public IReadOnlyDictionary<string, double> PerIntentRecall { get; init; } = new Dictionary<string, double>();

    /// <summary>Intent order of the matrix rows (actual) and columns (predicted).</summary>
    public IReadOnlyList<string> Intents { get; init; } = [];

    public IReadOnlyList<IReadOnlyList<int>> ConfusionMatrix { get; init; } = [];

    /// <summary>Smallest top-score threshold whose accepted predictions are at least 95% correct.</summary>
    public double SuggestedThreshold { get; init; }

    /// <summary>Share of held-out utterances accepted at <see cref="SuggestedThreshold"/>.</summary>
    public double CoverageAtSuggestedThreshold { get; init; }

    /// <summary>Every held-out prediction, for calibration reports.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<HeldOutPrediction> Predictions { get; init; } = [];
}

/// <summary>One cross-validation prediction with the margin between its top two scores.</summary>
public sealed record HeldOutPrediction(
    string Utterance, string Intent, string Locale, string Predicted, double Score, double Margin);

/// <summary>A trained pipeline plus what it was trained on.</summary>
public sealed class TrainedIntentModel
{
    internal TrainedIntentModel(MLContext context, ITransformer model, DataViewSchema inputSchema, IntentModelMetadata metadata)
    {
        Context = context;
        Model = model;
        InputSchema = inputSchema;
        Metadata = metadata;
    }

    public IntentModelMetadata Metadata { get; }

    internal MLContext Context { get; }

    internal ITransformer Model { get; }

    internal DataViewSchema InputSchema { get; }

    /// <summary>Writes the model zip, with the metadata as one extra entry.</summary>
    public void Save(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        using var buffer = new MemoryStream();
        Context.Model.Save(Model, InputSchema, buffer);
        buffer.Position = 0;
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.CreateEntry(MlNetIntentClassifier.MetadataEntry);
            using var writer = entry.Open();
            JsonSerializer.Serialize(writer, Metadata, IntentModelTrainer.JsonOptions);
        }

        buffer.Position = 0;
        buffer.CopyTo(destination);
    }

    public void Save(string path)
    {
        using var stream = File.Create(path);
        Save(stream);
    }
}

/// <summary>
/// Trains the single multilingual intent model: normalized utterances of every locale together,
/// word n-grams 1-2 + char n-grams 2-4 (no stopwords, the text is bilingual), SDCA maximum entropy.
/// Used by the <c>TaskFlow.Chatbot.Trainer</c> tool and by the tests.
/// </summary>
public static class IntentModelTrainer
{
    public const int DefaultSeed = 42;

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private const double TargetPrecision = 0.95;

    public static TrainedIntentModel Train(IKnowledgeBase kb, int seed = DefaultSeed)
    {
        ArgumentNullException.ThrowIfNull(kb);
        var normalizer = new TextNormalizer(kb.Synonyms);
        var samples = Samples(kb);
        var context = new MLContext(seed);
        var data = ToData(context, samples, normalizer);
        var model = BuildPipeline(context).Fit(data);
        var metadata = new IntentModelMetadata
        {
            KbVersion = kb.Settings.Version,
            TrainingSetHash = HashTrainingSet(samples),
            SynonymsHash = HashSynonyms(kb.Synonyms),
            Intents = samples.Select(s => s.Intent).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(),
            UtteranceCount = samples.Count,
        };
        return new TrainedIntentModel(context, model, data.Schema, metadata);
    }

    /// <summary>Hash the model stores; compare with the KB to detect a stale model.</summary>
    public static string HashTrainingSet(IKnowledgeBase kb)
    {
        ArgumentNullException.ThrowIfNull(kb);
        return HashTrainingSet(Samples(kb));
    }

    public static string HashSynonyms(IReadOnlyDictionary<string, IReadOnlyList<string>> synonyms)
    {
        ArgumentNullException.ThrowIfNull(synonyms);
        var lines = synonyms
            .OrderBy(e => e.Key, StringComparer.Ordinal)
            .Select(e => e.Key + "\t" + string.Join("\u001f", e.Value.Order(StringComparer.Ordinal)));
        return Sha256(string.Join('\n', lines));
    }

    /// <summary>Stratified (by intent and locale) k-fold cross validation with a fixed seed.</summary>
    public static IntentModelMetrics Evaluate(IKnowledgeBase kb, int folds = 5, int seed = DefaultSeed)
    {
        ArgumentNullException.ThrowIfNull(kb);
        ArgumentOutOfRangeException.ThrowIfLessThan(folds, 2);
        var normalizer = new TextNormalizer(kb.Synonyms);
        var samples = Samples(kb);

        // Deal each (intent, locale) group round-robin into folds after a seeded shuffle.
        var foldOf = new int[samples.Count];
        var random = new Random(seed);
        foreach (var group in samples.Select((s, i) => (s, i)).GroupBy(x => (x.s.Intent, x.s.Locale)).OrderBy(g => g.Key))
        {
            var indexes = group.Select(x => x.i).OrderBy(_ => random.Next()).ToList();
            for (var k = 0; k < indexes.Count; k++)
            {
                foldOf[indexes[k]] = k % folds;
            }
        }

        var results = new List<(string Intent, string Locale, string Predicted, double Score, double Margin, string Utterance)>();
        for (var fold = 0; fold < folds; fold++)
        {
            var train = samples.Where((_, i) => foldOf[i] != fold).ToList();
            var test = samples.Where((_, i) => foldOf[i] == fold).ToList();
            if (test.Count == 0)
            {
                continue;
            }

            var context = new MLContext(seed);
            var model = BuildPipeline(context).Fit(ToData(context, train, normalizer));
            using var engine = context.Model.CreatePredictionEngine<IntentRow, IntentScores>(model);
            var labels = SlotNames(engine.OutputSchema);
            foreach (var sample in test)
            {
                var scores = engine.Predict(new IntentRow { Text = normalizer.Normalize(sample.Utterance) }).Score;
                var best = Array.IndexOf(scores, scores.Max());
                var second = scores.Where((_, i) => i != best).DefaultIfEmpty(0f).Max();
                results.Add((sample.Intent, sample.Locale, labels[best], scores[best], scores[best] - second, sample.Utterance));
            }
        }

        return Summarize(results, folds);
    }

    private static IntentModelMetrics Summarize(
        List<(string Intent, string Locale, string Predicted, double Score, double Margin, string Utterance)> results, int folds)
    {
        var intents = results.Select(r => r.Intent).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var index = intents.Select((name, i) => (name, i)).ToDictionary(x => x.name, x => x.i, StringComparer.Ordinal);
        var matrix = new int[intents.Count][];
        for (var i = 0; i < matrix.Length; i++)
        {
            matrix[i] = new int[intents.Count];
        }

        foreach (var r in results)
        {
            matrix[index[r.Intent]][index[r.Predicted]]++;
        }

        var recall = intents.ToDictionary(
            name => name,
            name => results.Count(r => r.Intent == name && r.Predicted == name) / (double)results.Count(r => r.Intent == name),
            StringComparer.Ordinal);
        var perLocale = results.GroupBy(r => r.Locale).OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(r => r.Intent == r.Predicted) / (double)g.Count(), StringComparer.Ordinal);

        double threshold = 1.0;
        double coverage = 0;
        for (var t = 0.30; t <= 1.0 + 1e-9; t += 0.05)
        {
            var accepted = results.Where(r => r.Score >= t).ToList();
            if (accepted.Count == 0)
            {
                break;
            }

            if (accepted.Count(r => r.Intent == r.Predicted) / (double)accepted.Count >= TargetPrecision)
            {
                threshold = Math.Round(t, 2);
                coverage = accepted.Count / (double)results.Count;
                break;
            }
        }

        return new IntentModelMetrics
        {
            Folds = folds,
            Samples = results.Count,
            Accuracy = results.Count(r => r.Intent == r.Predicted) / (double)results.Count,
            MacroAccuracy = recall.Values.Average(),
            PerLocaleAccuracy = perLocale,
            PerIntentRecall = recall,
            Intents = intents,
            ConfusionMatrix = matrix.Select(row => (IReadOnlyList<int>)row).ToList(),
            SuggestedThreshold = threshold,
            CoverageAtSuggestedThreshold = coverage,
            Predictions = results.Select(r => new HeldOutPrediction(r.Utterance, r.Intent, r.Locale, r.Predicted, r.Score, r.Margin)).ToList(),
        };
    }

    private static EstimatorChain<KeyToValueMappingTransformer> BuildPipeline(MLContext context)
    {
        var featurize = context.Transforms.Text.FeaturizeText(
            "Features",
            new TextFeaturizingEstimator.Options
            {
                StopWordsRemoverOptions = null,
                CaseMode = TextNormalizingEstimator.CaseMode.None, // Already lowercased by TextNormalizer.
                KeepDiacritics = true,
                KeepPunctuations = false,
                KeepNumbers = true,
                WordFeatureExtractor = new WordBagEstimator.Options { NgramLength = 2, UseAllLengths = true },
                CharFeatureExtractor = new WordBagEstimator.Options { NgramLength = 4, UseAllLengths = true },
            },
            nameof(IntentRow.Text));

        var trainer = context.MulticlassClassification.Trainers.SdcaMaximumEntropy(
            new Microsoft.ML.Trainers.SdcaMaximumEntropyMulticlassTrainer.Options
            {
                LabelColumnName = "LabelKey",
                FeatureColumnName = "Features",
                NumberOfThreads = 1, // Deterministic.
                MaximumNumberOfIterations = 200,
            });

        return context.Transforms.Conversion.MapValueToKey("LabelKey", nameof(IntentRow.Label))
            .Append(featurize)
            .Append(trainer)
            .Append(context.Transforms.Conversion.MapKeyToValue("PredictedLabel"));
    }

    private static IDataView ToData(MLContext context, IReadOnlyList<Sample> samples, TextNormalizer normalizer) =>
        context.Data.LoadFromEnumerable(samples.Select(s => new IntentRow
        {
            Text = normalizer.Normalize(s.Utterance),
            Label = s.Intent,
        }));

    private static string[] SlotNames(DataViewSchema schema)
    {
        VBuffer<ReadOnlyMemory<char>> names = default;
        schema["Score"].Annotations.GetValue("SlotNames", ref names);
        return names.DenseValues().Select(n => n.ToString()).ToArray();
    }

    private static List<Sample> Samples(IKnowledgeBase kb) =>
        kb.Intents
            .SelectMany(intent => intent.Utterances.SelectMany(
                locale => locale.Value.Select(u => new Sample(intent.Intent, locale.Key, u))))
            .OrderBy(s => s.Intent, StringComparer.Ordinal)
            .ThenBy(s => s.Locale, StringComparer.Ordinal)
            .ThenBy(s => s.Utterance, StringComparer.Ordinal)
            .ToList();

    private static string HashTrainingSet(List<Sample> samples) =>
        Sha256(string.Join('\n', samples.Select(s => $"{s.Intent}\t{s.Locale}\t{s.Utterance}")));

    private static string Sha256(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private sealed record Sample(string Intent, string Locale, string Utterance);
}
