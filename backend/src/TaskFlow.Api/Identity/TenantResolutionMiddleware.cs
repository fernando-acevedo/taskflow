using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using TaskFlow.Api.Realtime;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Tenancy;
using TaskFlow.Infrastructure.ApiKeys;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.Infrastructure.Tenancy;

namespace TaskFlow.Api.Identity;

/// <summary>
/// Sets the request's tenant from the <c>X-Tenant</c> header after checking the membership (ADR 0013). Without the
/// header the user's only tenant is used; with several there is none and the data access fails closed. A tenant
/// that does not exist answers 404 and one the user does not belong to answers 403. Runs after authentication and
/// <see cref="SessionValidationMiddleware"/>.
/// </summary>
internal sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Tenant";

    public async Task InvokeAsync(
        HttpContext context,
        TenantAccess access,
        TenantContext tenant,
        IProblemDetailsService problemDetails,
        IStringLocalizer<SharedResource> localizer)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            // WebSockets cannot send headers, so a hub connection names its tenant in the query string (ADR 0023).
            var header = context.Request.Path.StartsWithSegments(ProjectsHub.Path, StringComparison.OrdinalIgnoreCase)
                ? context.Request.Query["tenant"].ToString()
                : context.Request.Headers[HeaderName].ToString();
            if (context.User.FindFirstValue(ApiKeyDefaults.TenantClaim) is { } keyTenant)
            {
                // An API key is bound to its tenant; X-Tenant is ignored (ADR 0016).
                var keyError = await ResolveAsync(context, access, tenant, userId, Guid.Parse(keyTenant));
                if (keyError is not null)
                {
                    await WriteAsync(context, problemDetails, localizer, keyError);
                    return;
                }
            }
            else if (string.IsNullOrWhiteSpace(header))
            {
                if (await access.DefaultAsync(userId, context.RequestAborted) is var (defaultId, role))
                {
                    tenant.Use(defaultId, role);
                }
            }
            else
            {
                var error = Guid.TryParse(header, out var tenantId)
                    ? await ResolveAsync(context, access, tenant, userId, tenantId)
                    : TenantErrors.NotFound;
                if (error is not null)
                {
                    await WriteAsync(context, problemDetails, localizer, error);
                    return;
                }
            }
        }

        if (tenant.TenantId is { } resolved)
        {
            context.Items[ProjectsHub.TenantItem] = new HubTenant(resolved, tenant.Role);
        }

        await next(context);
    }

    private static async Task<Error?> ResolveAsync(HttpContext context, TenantAccess access, TenantContext tenant, Guid userId, Guid tenantId)
    {
        var isSuperAdmin = context.User.IsInRole(Roles.Admin);
        var result = await access.CheckAsync(userId, tenantId, isSuperAdmin, context.RequestAborted);
        var error = TenantAccess.ErrorFor(result);
        if (error is null)
        {
            tenant.Use(tenantId, result.Role);
            if (result.IsSupportAccess)
            {
                await access.RecordSupportAccessAsync(userId, tenantId, context.RequestAborted);
            }
        }

        return error;
    }

    internal static async Task WriteAsync(HttpContext context, IProblemDetailsService problemDetails, IStringLocalizer<SharedResource> localizer, Error error)
    {
        var status = error.Type == ErrorType.NotFound ? StatusCodes.Status404NotFound : StatusCodes.Status403Forbidden;
        context.Response.StatusCode = status;
        var problem = new ProblemDetails { Status = status, Title = localizer[error.Code] };
        problem.Extensions["code"] = error.Code;
        await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem });
    }
}
