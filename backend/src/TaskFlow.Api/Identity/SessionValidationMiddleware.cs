using TaskFlow.Infrastructure.ApiKeys;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.Api.Identity;

/// <summary>
/// Answers 401 to an authenticated request whose token outlived its session (the user was deactivated or their
/// role changed). The frontend then tries a refresh, which fails for the same reason, and signs the user out.
/// </summary>
internal sealed class SessionValidationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, SessionValidator sessions)
    {
        // API keys have no security stamp: the key handler checks the key and its owner (ADR 0016).
        if (context.User.Identity?.IsAuthenticated == true
            && !context.User.HasClaim(claim => claim.Type == ApiKeyDefaults.KeyIdClaim)
            && !await sessions.IsCurrentAsync(context.User, context.RequestAborted))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }
}
