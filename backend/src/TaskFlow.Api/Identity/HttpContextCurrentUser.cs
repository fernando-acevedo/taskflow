using System.Security.Claims;
using TaskFlow.Application.Abstractions;

namespace TaskFlow.Api.Identity;

internal sealed class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid Id
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out var id)
                ? id
                : throw new InvalidOperationException("There is no authenticated user in the current request.");
        }
    }
}
