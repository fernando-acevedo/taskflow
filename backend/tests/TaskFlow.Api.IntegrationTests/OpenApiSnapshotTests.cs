using System.Text.Json;
using System.Text.Json.Nodes;
using TaskFlow.Api.IntegrationTests.Infrastructure;

namespace TaskFlow.Api.IntegrationTests;

/// <summary>
/// The OpenAPI document is the contract the frontend generates its types from, so a change to it must be deliberate:
/// it is compared with <c>docs/api/openapi-1.0.json</c>.
/// </summary>
public sealed class OpenApiSnapshotTests(NoDatabaseApiFactory factory) : IClassFixture<NoDatabaseApiFactory>
{
    private const string UpdateVariable = "UPDATE_OPENAPI_SNAPSHOT";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheOpenApiDocumentMatchesTheCommittedSnapshot()
    {
        using var client = factory.CreateClient();
        var raw = await client.GetStringAsync(new Uri("/openapi/1.0.json", UriKind.Relative), Ct);
        var actual = Normalize(raw);
        var path = SnapshotPath();

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            await File.WriteAllTextAsync(path, actual, Ct);
            return;
        }

        File.Exists(path).ShouldBeTrue($"{path} is missing. Create it by running the tests with {UpdateVariable}=1.");
        var expected = (await File.ReadAllTextAsync(path, Ct)).Replace("\r\n", "\n", StringComparison.Ordinal);
        (actual == expected).ShouldBeTrue(
            $"The OpenAPI contract changed. If that is intended, run the tests with {UpdateVariable}=1 "
            + $"(for example: {UpdateVariable}=1 dotnet test --filter OpenApiSnapshotTests) and commit {path}.\n"
            + FirstDifference(expected, actual));
    }

    private static string FirstDifference(string expected, string actual)
    {
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        var line = 0;
        while (line < expectedLines.Length && line < actualLines.Length && expectedLines[line] == actualLines[line])
        {
            line++;
        }

        string Around(string[] lines) => string.Join('\n', lines.Skip(Math.Max(0, line - 3)).Take(7));
        return $"First difference at line {line + 1}.\nExpected:\n{Around(expectedLines)}\nActual:\n{Around(actualLines)}";
    }

    [Fact]
    public async Task TheDocumentDescribesTheTypesTheFrontendGenerates()
    {
        using var client = factory.CreateClient();
        var document = JsonNode.Parse(await client.GetStringAsync(new Uri("/openapi/1.0.json", UriKind.Relative), Ct))!;
        var schemas = document["components"]!["schemas"]!;

        schemas["MoveTaskResultDto"].ShouldNotBeNull();
        schemas["TaskItemStatus"]!["type"]!.GetValue<string>().ShouldBe("string");
        schemas["TaskItemStatus"]!["enum"]!.ToJsonString().ShouldContain("InProgress");
        var rowVersion = schemas["TaskDto"]!["properties"]!["rowVersion"]!;
        rowVersion["type"]!.GetValue<string>().ShouldBe("string");
        rowVersion["format"]!.GetValue<string>().ShouldBe("byte");

        var responses = document["paths"]!["/api/projects/{projectId}/tasks/{taskId}"]!["put"]!["responses"]!.AsObject();
        responses.ContainsKey("412").ShouldBeTrue();
        responses.ContainsKey("409").ShouldBeTrue();
        responses.ContainsKey("429").ShouldBeTrue();
        document["paths"]!["/api/projects/{projectId}/tasks/{taskId}"]!["get"]!["responses"]!.AsObject().ContainsKey("304").ShouldBeTrue();
    }

    /// <summary>
    /// Sorted keys, LF, and no servers (they hold the host the test happened to run on). The Identity endpoints under
    /// <c>/api/auth</c> are left out, with the schemas only they use: ASP.NET Core describes them itself and its patch
    /// releases change that description (10.0.15 added 400 responses that 10.0.11 does not have), so they would make
    /// the snapshot depend on the installed runtime. The frontend does not generate types for them.
    /// </summary>
    private static string Normalize(string json)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        node.Remove("servers");
        RemoveFrameworkEndpoints(node);
        return Sort(node)!.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })
            .Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    private static void RemoveFrameworkEndpoints(JsonObject document)
    {
        if (document["paths"] is not JsonObject paths)
        {
            return;
        }

        foreach (var path in paths.Select(p => p.Key).Where(p => p.StartsWith("/api/auth/", StringComparison.Ordinal)).ToList())
        {
            paths.Remove(path);
        }

        if (document["components"]?["schemas"] is not JsonObject schemas)
        {
            return;
        }

        // Keep only the schemas still reachable from the remaining paths.
        const string prefix = "#/components/schemas/";
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<JsonNode?>([paths]);
        while (pending.Count > 0)
        {
            switch (pending.Pop())
            {
                case JsonObject obj:
                    foreach (var (key, value) in obj)
                    {
                        if (key == "$ref" && value?.GetValue<string>() is { } reference && reference.StartsWith(prefix, StringComparison.Ordinal)
                            && reachable.Add(reference[prefix.Length..]))
                        {
                            pending.Push(schemas[reference[prefix.Length..]]);
                        }
                        else
                        {
                            pending.Push(value);
                        }
                    }

                    break;
                case JsonArray array:
                    foreach (var item in array)
                    {
                        pending.Push(item);
                    }

                    break;
            }
        }

        foreach (var name in schemas.Select(s => s.Key).Where(name => !reachable.Contains(name)).ToList())
        {
            schemas.Remove(name);
        }
    }

    private static JsonNode? Sort(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj
            .OrderBy(property => property.Key, StringComparer.Ordinal)
            .Select(property => KeyValuePair.Create(property.Key, Sort(property.Value)))),
        JsonArray array => new JsonArray(array.Select(Sort).ToArray()),
        _ => node?.DeepClone(),
    };

    private static string SnapshotPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CLAUDE.md")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository root not found."), "docs", "api", "openapi-1.0.json");
    }
}
