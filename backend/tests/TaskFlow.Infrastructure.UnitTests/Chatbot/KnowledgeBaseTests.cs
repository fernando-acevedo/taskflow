using TaskFlow.Application.Chatbot.KnowledgeBase;
using TaskFlow.Infrastructure.Chatbot;

namespace TaskFlow.Infrastructure.UnitTests.Chatbot;

/// <summary>Loader and validator, against the frozen KB (embedded resources) and small broken variants of it.</summary>
public class KnowledgeBaseTests
{
    private static KnowledgeBase Load() => JsonKnowledgeBaseLoader.LoadFromAssembly(typeof(JsonKnowledgeBaseLoader).Assembly);

    // Writes the embedded KB files to a folder so a test can break one of them.
    private static string ExportKb()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kb-" + Guid.NewGuid().ToString("N"));
        var assembly = typeof(JsonKnowledgeBaseLoader).Assembly;
        foreach (var name in assembly.GetManifestResourceNames()
                     .Where(n => n.StartsWith(JsonKnowledgeBaseLoader.ResourcePrefix, StringComparison.Ordinal)))
        {
            var target = Path.Combine(dir, name[JsonKnowledgeBaseLoader.ResourcePrefix.Length..]);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var file = File.Create(target);
            stream.CopyTo(file);
        }

        return dir;
    }

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
        KnowledgeBaseValidator.Validate(content);

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

    private static Dictionary<string, IReadOnlyList<string>> Text(string en, string es) =>
        new() { ["en"] = [en], ["es"] = [es] };

    private static Dictionary<string, IReadOnlyList<string>> Variants(string[] en, string[] es) =>
        new() { ["en"] = en, ["es"] = es };

    [Fact]
    public void FrozenKbLoadsAndValidates()
    {
        var kb = Load();

        kb.Settings.Version.ShouldBe("1.0.0");
        kb.Settings.ConfidenceThreshold.ShouldBe(0.65);
        kb.Settings.MinMargin.ShouldBe(0.1);
        kb.Settings.MaxConsecutiveFallbacks.ShouldBe(2);
        kb.Settings.SessionIdleMinutes.ShouldBe(30);
        kb.Settings.Links.Keys.ShouldBe(["support", "help"], ignoreOrder: true);
        kb.Intents.Count.ShouldBe(13);
        kb.Intents.ShouldAllBe(i => i.Utterances["en"].Count == 48 && i.Utterances["es"].Count == 48);
        kb.Steps.Count.ShouldBe(29);
        kb.Steps.Values.ShouldAllBe(s => s.Text["en"].Count == 5 && s.Text["es"].Count == 5);
        kb.Intents.Single(i => i.Intent == "Greeting").EntryStep.ShouldBe("start");
        kb.Intents.Single(i => i.Intent == "HumanAgent").EntryStep.ShouldBe("escalate");
        Validate(Content()).ShouldBeEmpty();
    }

    [Fact]
    public void GetTextsReturnsEveryVariantAndLeavesUnresolvedPlaceholdersVisible()
    {
        var kb = Load();

        var texts = kb.GetTexts("escalate", "es");

        texts.Count.ShouldBe(5);
        texts.ShouldAllBe(t => t.Contains("{support}", StringComparison.Ordinal));
        kb.GetText("escalate", "es").ShouldBe(texts[0]);
    }

    [Fact]
    public void LinksFromSettingsReplaceThePlaceholders()
    {
        var content = Content();
        var kb = new KnowledgeBase(content with
        {
            Settings = content.Settings with { Links = new Dictionary<string, string> { ["support"] = "ayuda@example.test", ["help"] = "https://example.test/help" } },
        });

        kb.GetTexts("escalate", "en").ShouldAllBe(t => t.Contains("ayuda@example.test", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingEsTextIsReported()
    {
        var c = WithStep(Content(), "close", s => s with { Text = new Dictionary<string, IReadOnlyList<string>> { ["en"] = ["Did that solve it?"] } });

        Validate(c).ShouldContain(p => p.Contains("step 'close'") && p.Contains("missing or empty 'es'"));
    }

    [Fact]
    public void ExtraLocaleIsReported()
    {
        var c = WithStep(Content(), "end", s => s with { Text = new Dictionary<string, IReadOnlyList<string>>(s.Text) { ["fr"] = ["Parfait"] } });

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
        var shared = Content().Intents.Single(i => i.Intent == "ForgotPassword").Utterances["en"][0];
        var c = WithIntent(Content(), "LinkExpired", i => i with
        {
            Utterances = new Dictionary<string, IReadOnlyList<string>>
            {
                ["en"] = [.. i.Utterances["en"], "  " + shared.ToUpperInvariant() + " "],
                ["es"] = i.Utterances["es"],
            },
        });

        Validate(c).ShouldContain(p => p.Contains("LinkExpired") && p.Contains("also in") && p.Contains("ForgotPassword"));
    }

    [Fact]
    public void DuplicateUtteranceInsideAnIntentIsReported()
    {
        var c = WithIntent(Content(), "LinkExpired", i => i with
        {
            Utterances = new Dictionary<string, IReadOnlyList<string>>
            {
                ["en"] = [.. i.Utterances["en"], i.Utterances["en"][0].ToUpperInvariant()],
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
            QuickReplies = [s.QuickReplies[0] with { Label = new Dictionary<string, string> { ["en"] = "Yes", ["es"] = "Sí, volvé" } }, s.QuickReplies[1]],
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
    public void DifferentNumberOfVariantsBetweenLocalesIsReported()
    {
        var c = WithStep(Content(), "close", s => s with { Text = Variants(["One", "Two", "Three"], ["Uno", "Dos"]) });

        Validate(c).ShouldContain(p => p.Contains("step 'close'") && p.Contains("different number of variants") && p.Contains("en: 3") && p.Contains("es: 2"));
    }

    [Fact]
    public void EmptyVariantIsReported()
    {
        var c = WithStep(Content(), "close", s => s with { Text = Variants(["One", " "], ["Uno", "Dos"]) });

        Validate(c).ShouldContain(p => p.Contains("step 'close'") && p.Contains("'en' variant 2 is empty"));
    }

    [Fact]
    public void DuplicateVariantInALocaleIsReported()
    {
        var c = WithStep(Content(), "close", s => s with { Text = Variants(["One", "Two"], ["Uno", "  UNO "]) });

        Validate(c).ShouldContain(p => p.Contains("step 'close'") && p.Contains("'es' variant 2 repeats variant 1"));
    }

    [Fact]
    public void LocaleWithoutVariantsIsReported()
    {
        var c = WithStep(Content(), "close", s => s with { Text = Variants(["One"], []) });

        Validate(c).ShouldContain(p => p.Contains("step 'close'") && p.Contains("missing or empty 'es'"));
    }

    [Fact]
    public void PlaceholderMissingFromOneVariantIsReported()
    {
        var c = WithStep(Content(), "escalate", s => s with
        {
            Text = Variants(["Write to {support}.", "Write to us."], ["Escribe a {support}.", "Escribe a {support} ahora."]),
        });

        Validate(c).ShouldContain(p => p.Contains("step 'escalate'") && p.Contains("placeholders differ") && p.Contains("'en' variant 2"));
    }

    [Fact]
    public void PlaceholderDifferingInASecondSpanishVariantIsReported()
    {
        var c = WithStep(Content(), "escalate", s => s with
        {
            Text = Variants(["Write to {support}.", "Mail {support}."], ["Escribe a {support}.", "Escribe a {help}."]),
        });

        Validate(c).ShouldContain(p => p.Contains("step 'escalate'") && p.Contains("placeholders differ") && p.Contains("'es' variant 2"));
    }

    [Fact]
    public void VoseoInAnySpanishVariantIsReported()
    {
        var c = WithStep(Content(), "close", s => s with
        {
            Text = Variants(["One", "Two", "Three"], ["Uno", "Dos", "¿Podés escribirnos?"]),
        });

        Validate(c).ShouldContain(p => p.Contains("step 'close'") && p.Contains("variant 3") && p.Contains("Podés"));
    }

    [Fact]
    public void UnknownPlaceholderInAQuickReplyUrlIsReported()
    {
        var c = WithStep(Content(), "close", s => s with
        {
            QuickReplies = [s.QuickReplies[0] with { Url = "{nowhere}" }, s.QuickReplies[1]],
        });

        Validate(c).ShouldContain(p => p.Contains("quick reply 'yes'") && p.Contains("url placeholder {nowhere}"));
    }

    [Fact]
    public void SingleVariantPerLocaleIsValid()
    {
        var c = WithStep(Content(), "close", s => s with { Text = Text("One", "Uno") });

        Validate(c).ShouldBeEmpty();
    }

    [Fact]
    public void TooFewUtterancesAreReported()
    {
        var c = WithIntent(Content(), "LoginIssue", i => i with
        {
            Utterances = new Dictionary<string, IReadOnlyList<string>>
            {
                ["en"] = i.Utterances["en"],
                ["es"] = [.. i.Utterances["es"].Take(5)],
            },
        });

        Validate(c).ShouldContain(p => p.Contains("LoginIssue") && p.Contains("'es' has 5 utterances, at least 30 required"));
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
        var dir = ExportKb();
        try
        {
            var close = Path.Combine(dir, "common.json");
            var original = File.ReadAllText(close);
            var changed = original
                .Replace("\"Did this solve your problem?\"", "\"\"", StringComparison.Ordinal)
                .Replace("\"¿Esto resolvió tu problema?\"", "\"¿Podés confirmarlo?\"", StringComparison.Ordinal);
            changed.ShouldNotBe(original);
            File.WriteAllText(close, changed);

            var ex = Should.Throw<KnowledgeBaseException>(() => JsonKnowledgeBaseLoader.LoadFromDirectory(dir));

            ex.Problems.Count.ShouldBe(2);
            ex.Message.ShouldContain("2 problems");
            ex.Message.ShouldContain("'en' variant 1 is empty");
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
        var dir = ExportKb();
        try
        {
            var path = Path.Combine(dir, "synonyms.json");
            File.WriteAllText(path, "// comment\n{ \"password\": [\"clave\",], }");

            JsonKnowledgeBaseLoader.LoadFromDirectory(dir).Synonyms["password"].ShouldBe(["clave"]);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
