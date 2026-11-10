using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.Api.Identity;

/// <summary>
/// Identity's <c>/resetPassword</c> and <c>/manage/info</c> (a new password or email) renew the security stamp. This makes
/// this instance reject the user's old access tokens at once instead of when <see cref="SessionValidator"/>'s cached
/// stamp expires (and, with a shared cache, on every instance).
/// </summary>
internal sealed class ForgetSessionsOnCredentialChange : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);

        var services = context.HttpContext.RequestServices;
        Guid? userId = null;
        if (context.Arguments.OfType<ResetPasswordRequest>().FirstOrDefault() is { } reset)
        {
            userId = (await services.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(reset.Email))?.Id;
        }
        else if (context.Arguments.OfType<InfoRequest>().FirstOrDefault() is { } info
            && (info.NewPassword is not null || info.NewEmail is not null)
            && Guid.TryParse(context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var currentUser))
        {
            userId = currentUser;
        }

        if (userId is { } id)
        {
            await services.GetRequiredService<SessionValidator>().ForgetAsync(id, context.HttpContext.RequestAborted);
        }

        return result;
    }
}
