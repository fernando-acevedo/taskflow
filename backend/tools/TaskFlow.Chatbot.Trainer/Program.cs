using System.Globalization;
using System.Text.Json;
using TaskFlow.Infrastructure.Chatbot;

// Trains the chatbot intent model from a knowledge base folder.
// Usage: dotnet run --project tools/TaskFlow.Chatbot.Trainer -- --kb <dir> --out <dir> [--min-utterances N]
var arguments = ParseArguments(args);
if (!arguments.TryGetValue("kb", out var kbDirectory) || !arguments.TryGetValue("out", out var outDirectory))
{
    Console.Error.WriteLine("Usage: --kb <knowledge base dir> --out <output dir> [--min-utterances N]");
    return 2;
}

try
{
    var options = arguments.TryGetValue("min-utterances", out var min)
        ? new TaskFlow.Application.Chatbot.KnowledgeBase.KnowledgeBaseValidationOptions(int.Parse(min, CultureInfo.InvariantCulture))
        : null;
    var kb = JsonKnowledgeBaseLoader.LoadFromDirectory(kbDirectory, options);

    Console.WriteLine($"Training on {kb.Intents.Count} intents (KB {kb.Settings.Version})...");
    var model = IntentModelTrainer.Train(kb);
    var metrics = IntentModelTrainer.Evaluate(kb);

    Directory.CreateDirectory(outDirectory);
    var modelPath = Path.Combine(outDirectory, "intent-model.zip");
    model.Save(modelPath);
    var metricsPath = Path.Combine(outDirectory, "metrics.json");
    File.WriteAllText(
        metricsPath,
        JsonSerializer.Serialize(new { model.Metadata, Metrics = metrics }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

    Console.WriteLine(FormattableString.Invariant(
        $"Accuracy {metrics.Accuracy:P1}, macro {metrics.MacroAccuracy:P1}, suggested threshold {metrics.SuggestedThreshold:0.00} ({metrics.CoverageAtSuggestedThreshold:P0} coverage)"));
    foreach (var (locale, accuracy) in metrics.PerLocaleAccuracy)
    {
        Console.WriteLine(FormattableString.Invariant($"  {locale}: {accuracy:P1}"));
    }

    Console.WriteLine($"Wrote {modelPath} ({new FileInfo(modelPath).Length} bytes) and {metricsPath}");
    return 0;
}
catch (KnowledgeBaseException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static Dictionary<string, string> ParseArguments(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 0; i + 1 < args.Length; i += 2)
    {
        result[args[i].TrimStart('-')] = args[i + 1];
    }

    return result;
}
