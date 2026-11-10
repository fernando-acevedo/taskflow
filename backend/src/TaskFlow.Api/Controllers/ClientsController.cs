using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.Identity;
using TaskFlow.Application.Clients;
using TaskFlow.Application.Common;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Common;

namespace TaskFlow.Api.Controllers;

/// <summary>
/// The agency's clients, with their contact details: tenant admins only. Everyone else gets the pickers' list of names
/// from <c>GET /api/clients/options</c>.
/// </summary>
[Authorize]
[Route("api/clients")]
public sealed class ClientsController : ApiControllerBase
{
    /// <summary>Lists clients by name, active ones only unless <paramref name="includeArchived"/> is set.</summary>
    [HttpGet]
    [Authorize(Policy = AuthPolicies.TenantAdmin)]
    [ProducesResponseType<PagedResult<ClientDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ClientDto>>> List(
        [FromQuery] string? search,
        [FromServices] ListClientsHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] bool includeArchived = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50) =>
        OkOrProblem(await handler.HandleAsync(new ListClientsQuery(search, includeArchived, page, pageSize), cancellationToken));

    /// <summary>All active clients, id and name only, for pickers.</summary>
    [HttpGet("options")]
    [ProducesResponseType<IReadOnlyList<ClientOptionDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ClientOptionDto>>> Options(
        [FromServices] ListClientOptionsHandler handler,
        CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(cancellationToken));

    [HttpGet("{clientId:guid}")]
    [Authorize(Policy = AuthPolicies.TenantAdmin)]
    [ProducesResponseType<ClientDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    public async Task<ActionResult<ClientDto>> Get(Guid clientId, [FromServices] GetClientHandler handler, CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(clientId, cancellationToken), client => client.RowVersion);

    [HttpPost]
    [Authorize(Policy = AuthPolicies.TenantAdmin)]
    [ProducesResponseType<ClientDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ClientDto>> Create(
        CreateClientCommand command,
        [FromServices] CreateClientHandler handler,
        CancellationToken cancellationToken) =>
        CreatedOrProblem(await handler.HandleAsync(command, cancellationToken), nameof(Get), client => new { clientId = client.Id });

    [HttpPut("{clientId:guid}")]
    [Authorize(Policy = AuthPolicies.TenantAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult> Update(
        Guid clientId,
        ClientRequest request,
        [FromServices] UpdateClientHandler handler,
        CancellationToken cancellationToken)
    {
        if (!HasPrecondition(request.RowVersion))
        {
            return ProblemFrom(CommonErrors.PreconditionRequired);
        }

        return NoContentOrProblem(await handler.HandleAsync(
            new UpdateClientCommand(clientId, request.Name, request.ContactEmail, request.Notes, RowVersionFrom(request.RowVersion)), cancellationToken));
    }

    /// <summary>Hides the client from pickers. Fails while it has active projects.</summary>
    [HttpPost("{clientId:guid}/archive")]
    [Authorize(Policy = AuthPolicies.TenantAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult> Archive(
        Guid clientId,
        ClientVersionRequest? request,
        [FromServices] SetClientArchivedHandler handler,
        CancellationToken cancellationToken) =>
        await SetArchivedAsync(handler, clientId, isArchived: true, request, cancellationToken);

    [HttpPost("{clientId:guid}/restore")]
    [Authorize(Policy = AuthPolicies.TenantAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult> Restore(
        Guid clientId,
        ClientVersionRequest? request,
        [FromServices] SetClientArchivedHandler handler,
        CancellationToken cancellationToken) =>
        await SetArchivedAsync(handler, clientId, isArchived: false, request, cancellationToken);

    private async Task<ActionResult> SetArchivedAsync(
        SetClientArchivedHandler handler, Guid clientId, bool isArchived, ClientVersionRequest? request, CancellationToken cancellationToken)
    {
        if (!HasPrecondition(request?.RowVersion))
        {
            return ProblemFrom(CommonErrors.PreconditionRequired);
        }

        return NoContentOrProblem(await handler.HandleAsync(new SetClientArchivedCommand(clientId, isArchived, RowVersionFrom(request?.RowVersion)), cancellationToken));
    }
}

/// <summary>The body of an update: <paramref name="RowVersion"/> is the version the user edited (base64).</summary>
public sealed record ClientRequest(string Name, string? ContactEmail, string? Notes, byte[]? RowVersion);

/// <summary>The body of archive and restore.</summary>
public sealed record ClientVersionRequest(byte[]? RowVersion);
