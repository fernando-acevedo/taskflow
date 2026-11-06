using System.Security.Claims;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Tenancy;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Api.Identity;

internal sealed class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor, TenantContext tenant) : ICurrentUser
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

    public bool IsSuperAdmin => httpContextAccessor.HttpContext?.User.IsInRole(Roles.Admin) ?? false;

    public Guid? TenantId => tenant.TenantId;

    public TenantRole? TenantRole => tenant.Role;
}
