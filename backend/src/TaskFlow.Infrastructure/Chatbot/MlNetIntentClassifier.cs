using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using Microsoft.ML;
using Microsoft.ML.Data;
using TaskFlow.Application.Chatbot;

namespace TaskFlow.Infrastructure.Chatbot;

/// <summary>
/// <see cref="IIntentClassifier"/> over a persisted ML.NET model. <see cref="PredictionEngine{TSrc,TDst}"/> is not
/// thread-safe, so engines come from an object pool. Without a model the instance reports <see cref="IsLoaded"/> false.
/// </summary>
public sealed class MlNetIntentClassifier : IIntentClassifier
{
    internal const string MetadataEntry = "TaskFlow/metadata.json";

    private readonly ObjectPool<PredictionEngine<IntentRow, IntentScores>>? _engines;
    private readonly string[] _labels = [];

    /// <summary>Loads from the configured file, else from the embedded resource; stays unloaded if neither exists.</summary>
    public MlNetIntentClassifier(IOptions<ChatbotModelOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        using var stream = OpenModel(options.Value);
        if (stream is null)
        {
            return;
        }

        (_engines, _labels, Metadata) = Build(stream);
    }

    private MlNetIntentClassifier(Stream stream)
    {
        (_engines, _labels, Metadata) = Build(stream);
    }

    public bool IsLoaded => _engines is not null;

    public IntentModelMetadata? Metadata { get; }

    public string ModelVersion => Metadata?.ModelVersion ?? "";

    public static MlNetIntentClassifier Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return new MlNetIntentClassifier(stream);
    }

    public static MlNetIntentClassifier LoadFromFile(string path)
    {
        using var stream = File.OpenRead(path);
        return new MlNetIntentClassifier(stream);
    }

    public IntentPrediction Classify(string normalizedText)
    {
        if (_engines is null)
        {
            throw new InvalidOperationException("The intent model is not loaded.");
        }

        var engine = _engines.Get();
        float[] scores;
        try
        {
            scores = engine.Predict(new IntentRow { Text = normalizedText ?? "" }).Score;
        }
        finally
        {
            _engines.Return(engine);
        }

        var ranked = scores
            .Select((score, index) => new IntentScore(_labels[index], score))
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Intent, StringComparer.Ordinal)
            .ToList();
        return new IntentPrediction(ranked, ModelVersion);
    }

    private static Stream? OpenModel(ChatbotModelOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Path) && File.Exists(options.Path))
        {
            return File.OpenRead(options.Path);
        }

        return typeof(MlNetIntentClassifier).Assembly.GetManifestResourceStream(options.EmbeddedResource);
    }

    private static (ObjectPool<PredictionEngine<IntentRow, IntentScores>>, string[], IntentModelMetadata) Build(Stream stream)
    {
        // The zip is read twice (model, then metadata), so buffer it.
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        buffer.Position = 0;

        var context = new MLContext();
        var model = context.Model.Load(buffer, out _);

        buffer.Position = 0;
        using var zip = new ZipArchive(buffer, ZipArchiveMode.Read);
        var entry = zip.GetEntry(MetadataEntry)
            ?? throw new InvalidDataException("The intent model has no metadata entry.");
        using var reader = entry.Open();
        var metadata = JsonSerializer.Deserialize<IntentModelMetadata>(reader, IntentModelTrainer.JsonOptions)
            ?? throw new InvalidDataException("The intent model metadata is empty.");

        var probe = context.Model.CreatePredictionEngine<IntentRow, IntentScores>(model);
        var labels = ReadLabels(probe.OutputSchema);
        var pool = new DefaultObjectPool<PredictionEngine<IntentRow, IntentScores>>(
            new EnginePolicy(context, model), Environment.ProcessorCount * 2);
        pool.Return(probe);
        return (pool, labels, metadata);
    }

    private static string[] ReadLabels(DataViewSchema schema)
    {
        VBuffer<ReadOnlyMemory<char>> names = default;
        schema["Score"].Annotations.GetValue("SlotNames", ref names);
        return names.DenseValues().Select(n => n.ToString()).ToArray();
    }

    private sealed class EnginePolicy(MLContext context, ITransformer model)
        : IPooledObjectPolicy<PredictionEngine<IntentRow, IntentScores>>
    {
        public PredictionEngine<IntentRow, IntentScores> Create() =>
            context.Model.CreatePredictionEngine<IntentRow, IntentScores>(model);

        public bool Return(PredictionEngine<IntentRow, IntentScores> obj) => true;
    }
}
