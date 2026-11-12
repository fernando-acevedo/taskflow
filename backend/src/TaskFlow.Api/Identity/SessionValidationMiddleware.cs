using Microsoft.AspNetCore.Authorization;
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
        // An anonymous endpoint (the support chat, say) must not fail because the caller sends a token that outlived its
        // session: the call goes on as anonymous instead of answering 401.
        if (context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            if (context.User.Identity?.IsAuthenticated == true
                && !context.User.HasClaim(claim => claim.Type == ApiKeyDefaults.KeyIdClaim)
                && !await sessions.IsCurrentAsync(context.User, context.RequestAborted))
            {
                context.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity());
            }

            await next(context);
            return;
        }

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
