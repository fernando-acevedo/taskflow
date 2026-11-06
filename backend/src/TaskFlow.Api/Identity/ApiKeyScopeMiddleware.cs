using Microsoft.Extensions.Localization;
using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.ApiKeys;
using TaskFlow.Domain.Common;

namespace TaskFlow.Api.Identity;

/// <summary>
/// Keeps an API key to what it is for (ADR 0016): the projects API. A <c>Read</c> key can only use safe methods. Handlers
/// that go through <c>ProjectAccess</c> check the scope again; this closes the endpoints that do not (clients, users,
/// tenants, the keys themselves). Runs after the tenant is resolved.
/// </summary>
internal sealed class ApiKeyScopeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        ICurrentUser user,
        IProblemDetailsService problemDetails,
        IStringLocalizer<SharedResource> localizer)
    {
        if (context.User.Identity?.IsAuthenticated == true && user.ApiKeyId is not null)
        {
            var error = Check(context.Request, user.ApiKeyScope);
            if (error is not null)
            {
                await TenantResolutionMiddleware.WriteAsync(context, problemDetails, localizer, error);
                return;
            }
        }

        await next(context);
    }

    private static Error? Check(HttpRequest request, ApiKeyScope? scope)
    {
        if (!request.Path.StartsWithSegments("/api/projects", StringComparison.OrdinalIgnoreCase))
        {
            return ApiKeyErrors.NotAllowedWithKey;
        }

        var safe = HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method);
        return scope == ApiKeyScope.Write || safe ? null : ApiKeyErrors.ScopeInsufficient;
    }
}
