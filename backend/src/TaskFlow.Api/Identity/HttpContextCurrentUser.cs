using System.Security.Claims;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Users;
using TaskFlow.Domain.ApiKeys;
using TaskFlow.Infrastructure.ApiKeys;
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

    public Guid? ApiKeyId =>
        Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirstValue(ApiKeyDefaults.KeyIdClaim), out var id) ? id : null;

    public ApiKeyScope? ApiKeyScope =>
        ApiKeyId is null ? null
        : Enum.TryParse<ApiKeyScope>(httpContextAccessor.HttpContext?.User.FindFirstValue(ApiKeyDefaults.ScopeClaim), out var scope) ? scope
        : Domain.ApiKeys.ApiKeyScope.Read; // an unreadable scope gets the least access

    public Guid? TenantId => tenant.TenantId;

    public TenantRole? TenantRole => tenant.Role;
}
