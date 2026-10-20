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

    /// <summary>Lets a deactivated user sign in again.</summary>
    [HttpPost("{userId:guid}/activate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> Activate(Guid userId, [FromServices] SetUserActiveHandler handler, CancellationToken cancellationToken) =>
        NoContentOrProblem(await handler.HandleAsync(new SetUserActiveCommand(userId, IsActive: true), cancellationToken));

    /// <summary>Blocks sign-in and ends the user's sessions.</summary>
    [HttpPost("{userId:guid}/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> Deactivate(Guid userId, [FromServices] SetUserActiveHandler handler, CancellationToken cancellationToken) =>
        NoContentOrProblem(await handler.HandleAsync(new SetUserActiveCommand(userId, IsActive: false), cancellationToken));

    /// <summary>Grants the Admin role.</summary>
    [HttpPut("{userId:guid}/admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> GrantAdmin(Guid userId, [FromServices] SetUserAdminHandler handler, CancellationToken cancellationToken) =>
        NoContentOrProblem(await handler.HandleAsync(new SetUserAdminCommand(userId, IsAdmin: true), cancellationToken));

    /// <summary>Removes the Admin role.</summary>
    [HttpDelete("{userId:guid}/admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> RevokeAdmin(Guid userId, [FromServices] SetUserAdminHandler handler, CancellationToken cancellationToken) =>
        NoContentOrProblem(await handler.HandleAsync(new SetUserAdminCommand(userId, IsAdmin: false), cancellationToken));
}
