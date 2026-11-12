using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Api.IntegrationTests.Infrastructure;
using TaskFlow.Application.Chatbot.KnowledgeBase;

namespace TaskFlow.Api.IntegrationTests;

/// <summary>The anonymous support chat endpoint (ADR 0026). Variant texts are random, so tests check the step and that the text is one of its variants.</summary>
[Collection(SqlServerTestGroup.Name)]
public sealed class ChatbotTests(SqlServerApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Uri Url = new("/api/chatbot/message", UriKind.Relative);

    private static async Task<(HttpResponseMessage Response, ChatTurn? Turn)> SendAsync(
        HttpClient client, string? locale, Guid? sessionId = null, string? text = null, string? quickReplyId = null)
    {
        var response = await client.PostAsJsonAsync(Url, new { sessionId, locale, text, quickReplyId }, Ct);
        return (response, response.IsSuccessStatusCode ? await response.ReadAsync<ChatTurn>() : null);
    }

    private void AssertIsVariant(ChatTurn turn, string locale) =>
        factory.Services.GetRequiredService<IKnowledgeBase>().GetTexts(turn.Reply.StepId, locale).ShouldContain(turn.Reply.Text);

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public async Task AnonymousVisitorStartsASessionInTheirLocale(string locale)
    {
        using var client = factory.CreateClient();

        var (response, turn) = await SendAsync(client, locale);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        turn!.NewSession.ShouldBeTrue();
        turn.Locale.ShouldBe(locale);
        turn.SessionId.ShouldNotBe(Guid.Empty);
        turn.Reply.StepId.ShouldBe("start");
        turn.Reply.Ended.ShouldBeFalse();
        turn.Reply.QuickReplies.ShouldNotBeEmpty();
        AssertIsVariant(turn, locale);
    }

    [Fact]
    public async Task MissingLocaleFallsBackToTheRequestCulture()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es");

        var (_, turn) = await SendAsync(client, locale: null);

        turn!.Locale.ShouldBe("es");
    }

    [Fact]
    public async Task QuickRepliesLeadToACompletedEnd()
    {
        using var client = factory.CreateClient();
        var (_, opened) = await SendAsync(client, "en");

        var (_, forgot) = await SendAsync(client, "en", opened!.SessionId, quickReplyId: "forgot");
        forgot!.NewSession.ShouldBeFalse();
        forgot.SessionId.ShouldBe(opened.SessionId);
        forgot.Reply.StepId.ShouldBe("forgot.start");
        AssertIsVariant(forgot, "en");

        var (_, after) = await SendAsync(client, "en", opened.SessionId, quickReplyId: "done");
        after!.Reply.StepId.ShouldBe("forgot.after");

        var (_, end) = await SendAsync(client, "en", opened.SessionId, quickReplyId: "yes");
        end!.Reply.StepId.ShouldBe("end");
        end.Reply.Ended.ShouldBeTrue();
        end.Reply.Outcome.ShouldBe("Completed");
        end.Reply.QuickReplies.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("en", "I forgot my password")]
    [InlineData("es", "olvidé mi contraseña")]
    public async Task FreeTextIsClassified(string locale, string text)
    {
        using var client = factory.CreateClient();
        var (_, opened) = await SendAsync(client, locale);

        var (_, turn) = await SendAsync(client, locale, opened!.SessionId, text: text);

        turn!.Reply.StepId.ShouldBe("forgot.start");
        turn.Reply.IsFallback.ShouldBeFalse();
        AssertIsVariant(turn, locale);
    }

    [Fact]
    public async Task RepeatedGibberishFallsBackThenEscalates()
    {
        using var client = factory.CreateClient();
        var (_, opened) = await SendAsync(client, "en");

        var (_, first) = await SendAsync(client, "en", opened!.SessionId, text: "qzxv wkjh plmn");
        first!.Reply.StepId.ShouldBe("fallback");
        first.Reply.IsFallback.ShouldBeTrue();
        first.Reply.Ended.ShouldBeFalse();

        var (_, second) = await SendAsync(client, "en", opened.SessionId, text: "zzzx qqqv bbbn");
        second!.Reply.StepId.ShouldBe("fallback");

        // maxConsecutiveFallbacks is 2: the next failed turn escalates.
        var (_, third) = await SendAsync(client, "en", opened.SessionId, text: "vvvk jjjh gggf");
        third!.Reply.StepId.ShouldBe("escalate");
        third.Reply.Ended.ShouldBeTrue();
        third.Reply.Outcome.ShouldBe("Escalated");
    }

    [Fact]
    public async Task LocaleStaysFixedForTheSession()
    {
        using var client = factory.CreateClient();
        var (_, opened) = await SendAsync(client, "es");

        var (_, turn) = await SendAsync(client, "en", opened!.SessionId, quickReplyId: "forgot");

        turn!.Locale.ShouldBe("es");
        turn.Reply.StepId.ShouldBe("forgot.start");
        AssertIsVariant(turn, "es");
    }

    [Fact]
    public async Task UnsupportedLocaleIsRejectedWithItsCode()
    {
        using var client = factory.CreateClient();

        var (response, _) = await SendAsync(client, "fr");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadAsync<ProblemResponse>()).Code.ShouldBe("Chatbot.InvalidLocale");
    }

    [Fact]
    public async Task TooLongMessageIsRejectedWithItsCode()
    {
        using var client = factory.CreateClient();
        var (_, opened) = await SendAsync(client, "en");

        var (response, _) = await SendAsync(client, "en", opened!.SessionId, text: new string('a', 1001));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadAsync<ProblemResponse>()).Code.ShouldBe("Chatbot.MessageTooLong");
    }

    [Fact]
    public async Task UnknownQuickReplyIsRejectedWithItsCode()
    {
        using var client = factory.CreateClient();
        var (_, opened) = await SendAsync(client, "en");

        var (response, _) = await SendAsync(client, "en", opened!.SessionId, quickReplyId: "nope");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadAsync<ProblemResponse>()).Code.ShouldBe("Chatbot.UnknownQuickReply");
    }

    [Fact]
    public async Task AnIdleSessionIsReplacedByANewOne()
    {
        using var client = factory.CreateClient();
        var (_, opened) = await SendAsync(client, "en");

        factory.Time.Advance(TimeSpan.FromMinutes(31));
        var (_, turn) = await SendAsync(client, "en", opened!.SessionId, text: "hello");

        turn!.NewSession.ShouldBeTrue();
        turn.SessionId.ShouldNotBe(opened.SessionId);
    }

    [Fact]
    public async Task TheEndpointHasItsOwnRateLimit()
    {
        using var limited = factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:Chatbot:PermitLimit", "2"));
        using var client = limited.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var call = 0; call < 3; call++)
        {
            statuses.Add((await SendAsync(client, "en")).Response.StatusCode);
        }

        statuses.ShouldBe([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests]);
    }

    private sealed record ChatTurn(Guid SessionId, string Locale, bool NewSession, ChatReply Reply);

    private sealed record ChatReply(
        string Text, IReadOnlyList<ChatQuickReply> QuickReplies, string? Url, string StepId, bool Ended, string? Outcome, bool IsFallback);

    private sealed record ChatQuickReply(string Id, string Label, string? Url);
}
