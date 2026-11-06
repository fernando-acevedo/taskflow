using Microsoft.AspNetCore.Authorization;
using TaskFlow.Application.Abstractions;

namespace TaskFlow.Api.Identity;

internal static class AuthPolicies
{
    /// <summary>Owner or Admin of the request's tenant; a super-admin counts as Owner in the tenant it chose (ADR 0013).</summary>
    public const string TenantAdmin = "TenantAdmin";
}

internal sealed class TenantAdminRequirement : IAuthorizationRequirement;

internal sealed class TenantAdminHandler(ICurrentUser currentUser) : AuthorizationHandler<TenantAdminRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, TenantAdminRequirement requirement)
    {
        if (currentUser.IsTenantAdmin)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
