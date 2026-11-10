using System.Security.Claims;
using TaskFlow.Api.Realtime;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Users;
using TaskFlow.Domain.ApiKeys;
using TaskFlow.Infrastructure.ApiKeys;
using TaskFlow.Domain.Tenancy;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Api.Identity;

internal sealed class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor, TenantContext tenant, HubCaller hubCaller) : ICurrentUser
{
    // A SignalR hub call has no reliable HTTP request of its own, so the hub hands over the connection's user (ADR 0023).
    private ClaimsPrincipal? Principal => hubCaller.User ?? httpContextAccessor.HttpContext?.User;

    public Guid Id
    {
        get
        {
            var value = Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out var id)
                ? id
                : throw new InvalidOperationException("There is no authenticated user in the current request.");
        }
    }

    public bool IsSuperAdmin => Principal?.IsInRole(Roles.Admin) ?? false;

    public Guid? ApiKeyId =>
        Guid.TryParse(Principal?.FindFirstValue(ApiKeyDefaults.KeyIdClaim), out var id) ? id : null;

    public ApiKeyScope? ApiKeyScope =>
        ApiKeyId is null ? null
        : Enum.TryParse<ApiKeyScope>(Principal?.FindFirstValue(ApiKeyDefaults.ScopeClaim), out var scope) ? scope
        : Domain.ApiKeys.ApiKeyScope.Read; // an unreadable scope gets the least access

    public Guid? TenantId => tenant.TenantId;

    public TenantRole? TenantRole => tenant.Role;
}
