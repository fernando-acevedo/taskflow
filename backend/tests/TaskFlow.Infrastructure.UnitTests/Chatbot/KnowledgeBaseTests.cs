using TaskFlow.Application.Chatbot.KnowledgeBase;
using TaskFlow.Infrastructure.Chatbot;

namespace TaskFlow.Infrastructure.UnitTests.Chatbot;

/// <summary>Loader and validator, against the DRAFT fixture (not the real KB) and small broken variants of it.</summary>
public class KnowledgeBaseTests
{
    private static readonly string FixtureDir = Path.Combine(AppContext.BaseDirectory, "Chatbot", "Fixtures", "kb-draft");

    // The fixture only has 5 utterances per locale.
    private static readonly KnowledgeBaseValidationOptions Relaxed = new(MinUtterancesPerLocale: 5);

    private static KnowledgeBase Load() => JsonKnowledgeBaseLoader.LoadFromDirectory(FixtureDir, Relaxed);

    // Rebuilds the unvalidated content of the fixture so tests can break one thing at a time.
    private static KnowledgeBaseContent Content()
    {
        var kb = Load();
        var inIntents = kb.Intents.SelectMany(i => i.Steps.Keys).ToHashSet();
        return new KnowledgeBaseContent
        {
            Settings = kb.Settings,
            Synonyms = kb.Synonyms,
            Intents = kb.Intents,
            CommonSteps = kb.Steps.Where(s => !inIntents.Contains(s.Key)).ToDictionary(s => s.Key, s => s.Value),
        };
    }

    private static IReadOnlyList<string> Validate(KnowledgeBaseContent content) =>
        KnowledgeBaseValidator.Validate(content, Relaxed);

    private static KnowledgeBaseContent WithIntent(KnowledgeBaseContent c, string name, Func<IntentDefinition, IntentDefinition> change) =>
        c with { Intents = c.Intents.Select(i => i.Intent == name ? change(i) : i).ToList() };

    private static KnowledgeBaseContent WithStep(KnowledgeBaseContent c, string id, Func<DialogStep, DialogStep> change) =>
        c with
        {
            CommonSteps = c.CommonSteps.ToDictionary(s => s.Key, s => s.Key == id ? change(s.Value) : s.Value),
            Intents = c.Intents.Select(i => i with
            {
                Steps = i.Steps.ToDictionary(s => s.Key, s => s.Key == id ? change(s.Value) : s.Value),
            }).ToList(),
        };

    private static Dictionary<string, string> Text(string en, string es) => new() { ["en"] = en, ["es"] = es };

    [Fact]
    public void DraftFixtureLoadsAndValidates()
    {
        var kb = Load();

        kb.Settings.Version.ShouldBe("0.0.0-draft");
        kb.Intents.Count.ShouldBe(8);
        kb.Steps.ShouldContainKey("start");
        kb.GetText("escalate", "es").ShouldBe("Te comunicamos con una persona: escribe a soporte en support@taskflow.example.");
        kb.GetText("reset.start", "en").ShouldContain("Forgot my password");
        Validate(Content()).ShouldBeEmpty();
    }

    [Fact]
    public void FixtureDoesNotSatisfyTheRealMinimumOfUtterances()
    {
        Should.Throw<KnowledgeBaseException>(() => JsonKnowledgeBaseLoader.LoadFromDirectory(FixtureDir))
            .Problems.ShouldContain(p => p.Contains("at least 30 required", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingEsTextIsReported()
    {
        var c = WithStep(Content(), "close", s => s with { Text = new Dictionary<string, string> { ["en"] = "Did that solve it?" } });

        Validate(c).ShouldContain(p => p.Contains("step 'close'") && p.Contains("missing or empty 'es'"));
    }

    [Fact]
    public void ExtraLocaleIsReported()
    {
        var c = WithStep(Content(), "end", s => s with { Text = new Dictionary<string, string>(s.Text) { ["fr"] = "Parfait" } });

        Validate(c).ShouldContain(p => p.Contains("step 'end'") && p.Contains("locale 'fr' is not in settings.locales"));
    }

    [Fact]
    public void DanglingNextIsReported()
    {
        var c = WithStep(Content(), "close", s => s with
        {
            QuickReplies = [s.QuickReplies[0] with { Next = "nowhere" }, s.QuickReplies[1]],
        });

        Validate(c).ShouldContain(p => p.Contains("quick reply 'yes'") && p.Contains("next 'nowhere' does not exist"));
    }

    [Fact]
    public void QuickReplyWithoutNextOrUrlIsReported()
    {
        var c = WithStep(Content(), "close", s => s with
        {
            QuickReplies = [s.QuickReplies[0] with { Next = null }, s.QuickReplies[1]],
        });

        Validate(c).ShouldContain(p => p.Contains("quick reply 'yes'") && p.Contains("needs next or url"));
    }

    [Fact]
    public void UnreachableStepIsReported()
    {
        var c = WithIntent(Content(), "ServerError", i => i with
        {
            Steps = new Dictionary<string, DialogStep>(i.Steps)
            {
                ["error.orphan"] = new() { Text = Text("Orphan", "Huérfano"), End = true },
            },
        });

        Validate(c).ShouldContain(p => p.Contains("step 'error.orphan'") && p.Contains("not reachable"));
    }

    [Fact]
    public void NonEndStepWithoutQuickRepliesIsReported()
    {
        var c = WithStep(Content(), "close", s => s with { QuickReplies = [] });

        Validate(c).ShouldContain(p => p.Contains("step 'close'") && p.Contains("needs quick replies or expectsText"));
    }

    [Fact]
    public void DuplicateUtteranceAcrossIntentsIsReported()
    {
        var c = WithIntent(Content(), "LinkExpired", i => i with
        {
            Utterances = new Dictionary<string, IReadOnlyList<string>>
            {
                ["en"] = [.. i.Utterances["en"], "  I FORGOT my password "],
                ["es"] = i.Utterances["es"],
            },
        });

        Validate(c).ShouldContain(p => p.Contains("LinkExpired") && p.Contains("also in") && p.Contains("ResetPassword"));
    }

    [Fact]
    public void DuplicateUtteranceInsideAnIntentIsReported()
    {
        var c = WithIntent(Content(), "LinkExpired", i => i with
        {
            Utterances = new Dictionary<string, IReadOnlyList<string>>
            {
                ["en"] = [.. i.Utterances["en"], "The link has EXPIRED"],
                ["es"] = i.Utterances["es"],
            },
        });

        Validate(c).ShouldContain(p => p.Contains("LinkExpired") && p.Contains("repeated in 'en'"));
    }

    [Fact]
    public void PlaceholderMismatchBetweenLocalesIsReported()
    {
        var c = WithStep(Content(), "escalate", s => s with
        {
            Text = Text("Write to support at {support}.", "Escribe a soporte."),
        });

        Validate(c).ShouldContain(p => p.Contains("step 'escalate'") && p.Contains("placeholders differ"));
    }

    [Fact]
    public void UnknownPlaceholderIsReported()
    {
        var c = WithStep(Content(), "escalate", s => s with
        {
            Text = Text("Write to {nobody}.", "Escribe a {nobody}."),
        });

        Validate(c).ShouldContain(p => p.Contains("{nobody}") && p.Contains("not in settings.links"));
    }

    [Theory]
    [InlineData("¿Qué querés hacer?", "querés")]
    [InlineData("Revisá tu carpeta de spam", "Revisá")]
    [InlineData("Podes intentarlo de nuevo", "Podes")]
    [InlineData("Escribí tu correo", "Escribí")]
    [InlineData("VOS puedes escribirnos", "VOS")]
    public void VoseoInSpanishTextIsReported(string es, string word)
    {
        var c = WithStep(Content(), "close", s => s with { Text = Text("Did that solve it?", es) });

        Validate(c).ShouldContain(p => p.Contains("step 'close'") && p.Contains($"'{word}'") && p.Contains("voseo"));
    }

    [Fact]
    public void VoseoInALabelIsReported()
    {
        var c = WithStep(Content(), "close", s => s with
        {
            QuickReplies = [s.QuickReplies[0] with { Label = Text("Yes", "Sí, volvé") }, s.QuickReplies[1]],
        });

        Validate(c).ShouldContain(p => p.Contains("quick reply 'yes'") && p.Contains("volvé"));
    }

    [Fact]
    public void NeutralTuteoIsNotFlagged()
    {
        var c = WithStep(Content(), "close", s => s with
        {
            Text = Text("Did that solve it?", "Revisa tu carpeta, prueba de nuevo y escribe tu correo. ¿Sabes qué hacer?"),
        });

        Validate(c).ShouldBeEmpty();
    }

    [Fact]
    public void TooFewUtterancesAreReportedWithTheRealMinimum()
    {
        var problems = KnowledgeBaseValidator.Validate(Content());

        problems.ShouldContain(p => p.Contains("LoginIssue") && p.Contains("'es' has 5 utterances, at least 30 required"));
    }

    [Fact]
    public void QuickReplyIdsMustBeUniqueWithinAStep()
    {
        var c = WithStep(Content(), "close", s => s with
        {
            QuickReplies = [s.QuickReplies[0], s.QuickReplies[1] with { Id = "yes" }],
        });

        Validate(c).ShouldContain(p => p.Contains("quick reply 'yes'") && p.Contains("repeated in the step"));
    }

    [Fact]
    public void DuplicateStepIdsAcrossFilesAreReported()
    {
        var c = WithIntent(Content(), "ServerError", i => i with
        {
            Steps = new Dictionary<string, DialogStep>(i.Steps) { ["start"] = i.Steps["error.start"] },
        });

        Validate(c).ShouldContain(p => p.Contains("step 'start' is already defined in common.json"));
    }

    [Fact]
    public void MissingReferencesAreReported()
    {
        var c = WithIntent(Content(), "ServerError", i => i with { EntryStep = "nope", ConfusedWith = ["Ghost"] });
        c = c with { Settings = c.Settings with { FallbackStep = "gone", OutOfScopeIntent = "Nothing" } };

        var problems = Validate(c);

        problems.ShouldContain(p => p.Contains("entryStep 'nope'"));
        problems.ShouldContain(p => p.Contains("confusedWith 'Ghost'"));
        problems.ShouldContain(p => p.Contains("fallbackStep 'gone'"));
        problems.ShouldContain(p => p.Contains("outOfScopeIntent 'Nothing'"));
    }

    [Fact]
    public void LoaderThrowsOneExceptionListingEveryProblem()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kb-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDirectory(FixtureDir, dir);
            var close = Path.Combine(dir, "common.json");
            File.WriteAllText(close, File.ReadAllText(close)
                .Replace("\"Did that solve it?\"", "\"\"", StringComparison.Ordinal)
                .Replace("\"¿Eso lo resolvió?\"", "\"¿Podés confirmarlo?\"", StringComparison.Ordinal));

            var ex = Should.Throw<KnowledgeBaseException>(() => JsonKnowledgeBaseLoader.LoadFromDirectory(dir, Relaxed));

            ex.Problems.Count.ShouldBe(2);
            ex.Message.ShouldContain("2 problems");
            ex.Message.ShouldContain("missing or empty 'en'");
            ex.Message.ShouldContain("Podés");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void LoaderReportsInvalidJsonAndMissingSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kb-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "common.json"), "{ not json");

            var ex = Should.Throw<KnowledgeBaseException>(() => JsonKnowledgeBaseLoader.LoadFromDirectory(dir));

            ex.Problems.ShouldContain(p => p.StartsWith("settings.json: file not found", StringComparison.Ordinal));
            ex.Problems.ShouldContain(p => p.StartsWith("common.json: invalid JSON", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void LoaderAllowsCommentsAndTrailingCommas()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kb-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDirectory(FixtureDir, dir);
            var path = Path.Combine(dir, "synonyms.json");
            File.WriteAllText(path, "// comment\n{ \"password\": [\"clave\",], }");

            JsonKnowledgeBaseLoader.LoadFromDirectory(dir, Relaxed).Synonyms["password"].ShouldBe(["clave"]);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static void CopyDirectory(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
