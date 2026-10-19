using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Application.Common;
using TaskFlow.Application.Projects;

namespace TaskFlow.Api.Controllers;

/// <summary>
/// Projects the signed-in user belongs to (admins can read all). Access per project is checked by the
/// use cases: viewing needs membership, editing and archiving need the Owner role.
/// </summary>
[Authorize]
[Route("api/projects")]
public sealed class ProjectsController : ApiControllerBase
{
    /// <summary>Lists active projects, or archived ones with <paramref name="archived"/>, most recently updated first.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<ProjectDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ProjectDto>>> List(
        [FromQuery] string? search,
        [FromQuery] Guid? clientId,
        [FromServices] ListProjectsHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] bool archived = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20) =>
        OkOrProblem(await handler.HandleAsync(new ListProjectsQuery(search, clientId, archived, page, pageSize), cancellationToken));

    [HttpGet("{projectId:guid}")]
    [ProducesResponseType<ProjectDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectDto>> Get(Guid projectId, [FromServices] GetProjectHandler handler, CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(projectId, cancellationToken));

    /// <summary>Creates a project for an active client, with the caller as owner.</summary>
    [HttpPost]
    [ProducesResponseType<ProjectDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProjectDto>> Create(
        CreateProjectCommand command,
        [FromServices] CreateProjectHandler handler,
        CancellationToken cancellationToken) =>
        CreatedOrProblem(await handler.HandleAsync(command, cancellationToken), nameof(Get), project => new { projectId = project.Id });

    /// <summary>Edits the project. Send the <c>rowVersion</c> you read; a newer one answers 409.</summary>
    [HttpPut("{projectId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Update(
        Guid projectId,
        UpdateProjectRequest request,
        [FromServices] UpdateProjectHandler handler,
        CancellationToken cancellationToken) =>
        NoContentOrProblem(await handler.HandleAsync(
            new UpdateProjectCommand(projectId, request.ClientId, request.Name, request.Description, request.RowVersion),
            cancellationToken));

    /// <summary>Makes the project read-only and moves it to the archived list.</summary>
    [HttpPost("{projectId:guid}/archive")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> Archive(Guid projectId, [FromServices] SetProjectArchivedHandler handler, CancellationToken cancellationToken) =>
        NoContentOrProblem(await handler.HandleAsync(new SetProjectArchivedCommand(projectId, IsArchived: true), cancellationToken));

    [HttpPost("{projectId:guid}/restore")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Restore(Guid projectId, [FromServices] SetProjectArchivedHandler handler, CancellationToken cancellationToken) =>
        NoContentOrProblem(await handler.HandleAsync(new SetProjectArchivedCommand(projectId, IsArchived: false), cancellationToken));
}

public sealed record UpdateProjectRequest(Guid ClientId, string Name, string? Description, byte[] RowVersion);
