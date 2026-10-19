using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Application.Clients;
using TaskFlow.Application.Common;
using TaskFlow.Application.Users;

namespace TaskFlow.Api.Controllers;

/// <summary>The agency's clients. Everyone can read them to pick one for a project; only admins change them.</summary>
[Authorize]
[Route("api/clients")]
public sealed class ClientsController : ApiControllerBase
{
    /// <summary>Lists clients by name, active ones only unless <paramref name="includeArchived"/> is set.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<ClientDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ClientDto>>> List(
        [FromQuery] string? search,
        [FromServices] ListClientsHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] bool includeArchived = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50) =>
        OkOrProblem(await handler.HandleAsync(new ListClientsQuery(search, includeArchived, page, pageSize), cancellationToken));

    [HttpGet("{clientId:guid}")]
    [ProducesResponseType<ClientDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ClientDto>> Get(Guid clientId, [FromServices] GetClientHandler handler, CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(clientId, cancellationToken));

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ClientDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ClientDto>> Create(
        CreateClientCommand command,
        [FromServices] CreateClientHandler handler,
        CancellationToken cancellationToken) =>
        CreatedOrProblem(await handler.HandleAsync(command, cancellationToken), nameof(Get), client => new { clientId = client.Id });

    [HttpPut("{clientId:guid}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Update(
        Guid clientId,
        ClientRequest request,
        [FromServices] UpdateClientHandler handler,
        CancellationToken cancellationToken) =>
        NoContentOrProblem(await handler.HandleAsync(
            new UpdateClientCommand(clientId, request.Name, request.ContactEmail, request.Notes), cancellationToken));

    /// <summary>Hides the client from pickers. Fails while it has active projects.</summary>
    [HttpPost("{clientId:guid}/archive")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Archive(Guid clientId, [FromServices] SetClientArchivedHandler handler, CancellationToken cancellationToken) =>
        NoContentOrProblem(await handler.HandleAsync(new SetClientArchivedCommand(clientId, IsArchived: true), cancellationToken));

    [HttpPost("{clientId:guid}/restore")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> Restore(Guid clientId, [FromServices] SetClientArchivedHandler handler, CancellationToken cancellationToken) =>
        NoContentOrProblem(await handler.HandleAsync(new SetClientArchivedCommand(clientId, IsArchived: false), cancellationToken));
}

public sealed record ClientRequest(string Name, string? ContactEmail, string? Notes);
