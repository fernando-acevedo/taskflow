using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.Identity;
using TaskFlow.Application.Tenants;
using TaskFlow.Domain.Tenancy;

namespace TaskFlow.Api.Controllers;

/// <summary>Members of the current tenant (the one in <c>X-Tenant</c>), for its Owners and Admins (ADR 0013).</summary>
[Authorize(Policy = AuthPolicies.TenantAdmin)]
[Route("api/tenant")]
public sealed class TenantMembersController : ApiControllerBase
{
    [HttpGet("members")]
    [ProducesResponseType<IReadOnlyList<TenantMemberDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TenantMemberDto>>> List([FromServices] ListTenantMembersHandler handler, CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(cancellationToken));

    /// <summary>
    /// Adds a member by email. An existing account only gets the membership (and a notice email); a new email gets an
    /// account without password and the invitation of ADR 0008. <c>invitationSent</c> is false when the email failed.
    /// </summary>
    [HttpPost("members")]
    [ProducesResponseType<InvitedMemberDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InvitedMemberDto>> Invite(
        InviteTenantMemberCommand command,
        [FromServices] InviteTenantMemberHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command, cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : ProblemFrom(result.Error);
    }

    [HttpPut("members/{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> ChangeRole(
        Guid userId,
        ChangeTenantRoleRequest request,
        [FromServices] ChangeTenantMemberRoleHandler handler,
        CancellationToken cancellationToken) =>
        NoContentOrProblem(await handler.HandleAsync(new ChangeTenantMemberRoleCommand(userId, request.Role), cancellationToken));

    /// <summary>Removes the membership; the account stays.</summary>
    [HttpDelete("members/{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Remove(Guid userId, [FromServices] RemoveTenantMemberHandler handler, CancellationToken cancellationToken) =>
        NoContentOrProblem(await handler.HandleAsync(new RemoveTenantMemberCommand(userId), cancellationToken));

    /// <summary>Emails a new invitation link to a member who has not chosen a password yet.</summary>
    [HttpPost("members/{userId:guid}/invitation")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> ResendInvitation(Guid userId, [FromServices] ResendTenantInvitationHandler handler, CancellationToken cancellationToken) =>
        NoContentOrProblem(await handler.HandleAsync(new ResendTenantInvitationCommand(userId), cancellationToken));

    /// <summary>Times a platform administrator entered this tenant without belonging to it (newest first, last 100).</summary>
    [HttpGet("support-access")]
    [ProducesResponseType<IReadOnlyList<SupportAccessDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SupportAccessDto>>> SupportAccess([FromServices] ListSupportAccessHandler handler, CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(cancellationToken));
}

/// <summary>Request body of <c>PUT /api/tenant/members/{userId}</c>.</summary>
public sealed record ChangeTenantRoleRequest(TenantRole Role);
