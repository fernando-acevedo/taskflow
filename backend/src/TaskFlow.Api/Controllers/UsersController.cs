using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Application.Users;

namespace TaskFlow.Api.Controllers;

[Authorize]
[Route("api/users")]
public sealed class UsersController : ApiControllerBase
{
    /// <summary>The signed-in user's profile and roles. The bearer token is opaque, so the frontend reads them here.</summary>
    [HttpGet("me")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UserDto>> GetMe([FromServices] GetCurrentUserHandler handler, CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(cancellationToken));

    /// <summary>Updates the signed-in user's display name.</summary>
    [HttpPut("me")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> UpdateMe(
        UpdateProfileCommand command,
        [FromServices] UpdateProfileHandler handler,
        CancellationToken cancellationToken) =>
        NoContentOrProblem(await handler.HandleAsync(command, cancellationToken));
}
