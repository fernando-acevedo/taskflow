using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using TaskFlow.Application.Demo;

namespace TaskFlow.Api.Identity;

/// <summary>
/// In the public demo everybody shares the demo accounts, so the Identity endpoints that would let a visitor lock
/// the others out are closed: <c>/manage/*</c> writes (password, email, two-factor), the password reset flow and resending confirmation emails.
/// </summary>
internal sealed class DemoRestrictionsFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        if (!services.GetRequiredService<IDemoMode>().IsEnabled || !IsRestricted(context.HttpContext.Request))
        {
            return await next(context);
        }

        var error = DemoErrors.ActionDisabled;
        var localized = services.GetRequiredService<IStringLocalizer<SharedResource>>()[error.Code];
        return Results.Problem(
            statusCode: StatusCodes.Status403Forbidden,
            title: localized.ResourceNotFound ? error.Description : localized.Value,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }

    private static bool IsRestricted(HttpRequest request)
    {
        var path = request.Path;
        if (path.StartsWithSegments("/api/auth/manage", StringComparison.OrdinalIgnoreCase))
        {
            return !HttpMethods.IsGet(request.Method);
        }

        return path.StartsWithSegments("/api/auth/resendConfirmationEmail", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/api/auth/forgotPassword", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/api/auth/resetPassword", StringComparison.OrdinalIgnoreCase);
    }
}
