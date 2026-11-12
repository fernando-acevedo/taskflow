using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaskFlow.Api.Identity;
using TaskFlow.Application.Chatbot;

namespace TaskFlow.Api.Controllers;

/// <summary>Support chat for people who may not be able to sign in, so it is anonymous (ADR 0026).</summary>
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Chatbot)]
[Route("api/chatbot")]
public sealed class ChatbotController : ApiControllerBase
{
    /// <summary>
    /// One turn of the conversation. Omit <c>sessionId</c> to open a session (or send an unknown, closed or idle one:
    /// the answer then has <c>newSession</c> true and a new id). Send <c>text</c> or <c>quickReplyId</c>. The locale
    /// (en or es) counts only when a session opens; when missing it comes from Accept-Language.
    /// </summary>
    [HttpPost("message")]
    [RequestSizeLimit(16 * 1024)]
    [ProducesResponseType<SendChatMessageResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SendChatMessageResult>> SendMessage(
        SendChatMessageRequest request,
        [FromServices] SendChatMessageHandler handler,
        CancellationToken cancellationToken)
    {
        var locale = string.IsNullOrWhiteSpace(request.Locale)
            ? HttpContext.Features.Get<IRequestCultureFeature>()?.RequestCulture.UICulture.TwoLetterISOLanguageName ?? "en"
            : request.Locale.Trim().ToLowerInvariant();
        return OkOrProblem(await handler.HandleAsync(
            new SendChatMessageCommand(request.SessionId, locale, request.Text, request.QuickReplyId), cancellationToken));
    }
}

/// <param name="SessionId">The id from the previous answer; null on the first turn.</param>
/// <param name="Locale">en or es, the active UI language; used only when a session opens. Defaults to Accept-Language.</param>
/// <param name="Text">Free text, at most 1000 characters.</param>
/// <param name="QuickReplyId">A quick reply of the last answer; wins over <c>text</c>.</param>
public sealed record SendChatMessageRequest(Guid? SessionId, string? Locale, string? Text, string? QuickReplyId);
