using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.Api.Identity;

/// <summary>
/// Identity's <c>/resetPassword</c> renews the security stamp. This makes this instance reject the user's old access
/// tokens at once instead of when <see cref="SessionValidator"/>'s cached stamp expires.
/// </summary>
internal sealed class ForgetSessionsOnPasswordReset : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);

        if (context.Arguments.OfType<ResetPasswordRequest>().FirstOrDefault() is { } request)
        {
            var services = context.HttpContext.RequestServices;
            if (await services.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(request.Email) is { } user)
            {
                services.GetRequiredService<SessionValidator>().Forget(user.Id);
            }
        }

        return result;
    }
}
