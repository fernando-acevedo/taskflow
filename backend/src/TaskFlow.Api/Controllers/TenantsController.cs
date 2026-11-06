using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Application.Tenants;
using TaskFlow.Application.Users;

namespace TaskFlow.Api.Controllers;

/// <summary>
/// The tenants of the platform (ADR 0013). Listing your own needs no tenant in <c>X-Tenant</c>, so the frontend can
/// offer the choice; creating tenants and listing all of them is for the super-admin.
/// </summary>
[Authorize]
[Route("api/tenants")]
public sealed class TenantsController : ApiControllerBase
{
    /// <summary>The tenants the current user belongs to, with their role in each.</summary>
    [HttpGet("mine")]
    [ProducesResponseType<IReadOnlyList<MyTenantDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MyTenantDto>>> Mine([FromServices] ListMyTenantsHandler handler, CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(cancellationToken));

    /// <summary>Every tenant of the platform.</summary>
    [HttpGet]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<IReadOnlyList<TenantDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TenantDto>>> List([FromServices] ListTenantsHandler handler, CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(cancellationToken));

    /// <summary>
    /// Creates a tenant and, when <c>ownerEmail</c> is given, its first Owner: an existing account gets the membership
    /// and a notice, a new email gets an account and the password invitation.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<CreatedTenantDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreatedTenantDto>> Create(
        CreateTenantCommand command,
        [FromServices] CreateTenantHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command, cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : ProblemFrom(result.Error);
    }
}
