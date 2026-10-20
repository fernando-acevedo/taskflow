using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaskFlow.Api.Identity;
using TaskFlow.Application.Users;

namespace TaskFlow.Api.Controllers;

[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Auth)]
[Route("api/invitations")]
public sealed class InvitationsController : ApiControllerBase
{
    /// <summary>Sets the invited user's first password using the token from the invitation email.</summary>
    [HttpPost("accept")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> Accept(
        AcceptInvitationCommand command,
        [FromServices] AcceptInvitationHandler handler,
        CancellationToken cancellationToken) =>
        NoContentOrProblem(await handler.HandleAsync(command, cancellationToken));
}
