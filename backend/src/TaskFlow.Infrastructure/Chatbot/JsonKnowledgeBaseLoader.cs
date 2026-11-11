using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaskFlow.Application.Chatbot.KnowledgeBase;

namespace TaskFlow.Infrastructure.Chatbot;

/// <summary>The knowledge base is invalid or unreadable; <see cref="Problems"/> lists everything found.</summary>
public sealed class KnowledgeBaseException : Exception
{
    public KnowledgeBaseException()
    {
        Problems = [];
    }

    public KnowledgeBaseException(string message) : base(message)
    {
        Problems = [];
    }

    public KnowledgeBaseException(string message, Exception innerException) : base(message, innerException)
    {
        Problems = [];
    }

    public KnowledgeBaseException(IReadOnlyList<string> problems)
        : base($"The chatbot knowledge base is invalid ({problems.Count} problems):{Environment.NewLine}"
               + string.Join(Environment.NewLine, problems.Select(p => "  - " + p)))
    {
        Problems = problems;
    }

    public IReadOnlyList<string> Problems { get; }
}

/// <summary>
/// Loads a KB (<c>settings.json</c>, <c>synonyms.json</c>, <c>common.json</c>, <c>intents/*.json</c>) from a directory
/// or from embedded resources, validates it and throws <see cref="KnowledgeBaseException"/> listing every problem.
/// Embedded resources are named <c>TaskFlow.KnowledgeBase/&lt;relative path&gt;</c> (see the Infrastructure csproj).
/// </summary>
public static class JsonKnowledgeBaseLoader
{
    public const string ResourcePrefix = "TaskFlow.KnowledgeBase/";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static KnowledgeBase LoadFromDirectory(string path, KnowledgeBaseValidationOptions? options = null)
    {
        if (!Directory.Exists(path))
        {
            throw new KnowledgeBaseException($"Knowledge base directory '{path}' does not exist.");
        }

        var files = Directory.EnumerateFiles(path, "*.json", SearchOption.AllDirectories)
            .ToDictionary(
                f => Path.GetRelativePath(path, f).Replace('\\', '/'),
                f => (Func<string>)(() => File.ReadAllText(f)),
                StringComparer.Ordinal);
        return Load(files, options);
    }

    public static KnowledgeBase LoadFromAssembly(Assembly assembly, KnowledgeBaseValidationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var files = new Dictionary<string, Func<string>>(StringComparer.Ordinal);
        foreach (var name in assembly.GetManifestResourceNames()
                     .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
        {
            var resource = name;
            files[resource[ResourcePrefix.Length..].Replace('\\', '/')] = () =>
            {
                using var stream = assembly.GetManifestResourceStream(resource)!;
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            };
        }

        return Load(files, options);
    }

    private static KnowledgeBase Load(Dictionary<string, Func<string>> files, KnowledgeBaseValidationOptions? options)
    {
        var problems = new List<string>();

        T? Read<T>(string file, bool required)
            where T : class
        {
            if (!files.TryGetValue(file, out var read))
            {
                if (required)
                {
                    problems.Add($"{file}: file not found.");
                }

                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<T>(read(), JsonOptions)
                       ?? throw new JsonException("the file is empty or null");
            }
            catch (JsonException ex)
            {
                problems.Add($"{file}: invalid JSON ({ex.Message})");
                return null;
            }
        }

        var settings = Read<KnowledgeBaseSettings>("settings.json", true);
        var synonyms = Read<Dictionary<string, IReadOnlyList<string>>>("synonyms.json", false);
        var common = Read<CommonFile>("common.json", false);

        var intents = new List<IntentDefinition>();
        foreach (var file in files.Keys.Where(f => f.StartsWith("intents/", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            var intent = Read<IntentDefinition>(file, true);
            if (intent is not null)
            {
                intents.Add(intent with { Source = file });
            }
        }

        if (problems.Count > 0 || settings is null)
        {
            throw new KnowledgeBaseException(problems);
        }

        var content = new KnowledgeBaseContent
        {
            Settings = settings,
            Synonyms = synonyms ?? new Dictionary<string, IReadOnlyList<string>>(),
            CommonSteps = common?.Steps ?? new Dictionary<string, DialogStep>(),
            Intents = intents,
        };
        problems.AddRange(KnowledgeBaseValidator.Validate(content, options));
        return problems.Count > 0 ? throw new KnowledgeBaseException(problems) : new KnowledgeBase(content);
    }

    private sealed record CommonFile
    {
        public IReadOnlyDictionary<string, DialogStep> Steps { get; init; } = new Dictionary<string, DialogStep>();
    }
}
