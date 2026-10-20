using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Application.Common;
using TaskFlow.Application.Users;

namespace TaskFlow.Api.Controllers;

[Authorize(Roles = Roles.Admin)]
[Route("api/admin/users")]
public sealed class AdminUsersController : ApiControllerBase
{
    /// <summary>Lists users, optionally filtered by email or display name.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<UserDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<UserDto>>> List(
        [FromQuery] string? search,
        [FromServices] ListUsersHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20) =>
        OkOrProblem(await handler.HandleAsync(new ListUsersQuery(search, page, pageSize), cancellationToken));

    [HttpGet("{userId:guid}")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDto>> Get(Guid userId, [FromServices] GetUserHandler handler, CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(userId, cancellationToken));

    /// <summary>
    /// Creates an account, optionally as an admin, and emails the user a link to choose their password. The
    /// account is created even when the email can't be sent: <c>invitationSent</c> is then false and the
    /// invitation can be resent.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<CreatedUserDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreatedUserDto>> Create(
        CreateUserCommand command,
        [FromServices] CreateUserHandler handler,
        CancellationToken cancellationToken) =>
        CreatedOrProblem(await handler.HandleAsync(command, cancellationToken), nameof(Get), created => new { userId = created.User.Id });

    /// <summary>Emails a new invitation link to a user who hasn't chosen a password yet.</summary>
    [HttpPost("{userId:guid}/invitation")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> ResendInvitation(Guid userId, [FromServices] ResendInvitationHandler handler, CancellationToken cancellationToken) =>
        NoContentOrProblem(await handler.HandleAsync(new ResendInvitationCommand(userId), cancellationToken));

    /// <summary>Lets a deactivated user sign in again.</summary>
    [HttpPost("{userId:guid}/activate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Activate(Guid userId, [FromServices] SetUserActiveHandler handler, CancellationToken cancellationToken) =>
        NoContentOrProblem(await handler.HandleAsync(new SetUserActiveCommand(userId, IsActive: true), cancellationToken));

    /// <summary>Blocks sign-in and ends the user's sessions, including tokens already issued.</summary>
    [HttpPost("{userId:guid}/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Deactivate(Guid userId, [FromServices] SetUserActiveHandler handler, CancellationToken cancellationToken) =>
        NoContentOrProblem(await handler.HandleAsync(new SetUserActiveCommand(userId, IsActive: false), cancellationToken));

    /// <summary>Grants the Admin role.</summary>
    [HttpPut("{userId:guid}/admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> GrantAdmin(Guid userId, [FromServices] SetUserAdminHandler handler, CancellationToken cancellationToken) =>
        NoContentOrProblem(await handler.HandleAsync(new SetUserAdminCommand(userId, IsAdmin: true), cancellationToken));

    /// <summary>Removes the Admin role.</summary>
    [HttpDelete("{userId:guid}/admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> RevokeAdmin(Guid userId, [FromServices] SetUserAdminHandler handler, CancellationToken cancellationToken) =>
        NoContentOrProblem(await handler.HandleAsync(new SetUserAdminCommand(userId, IsAdmin: false), cancellationToken));
}
