using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Application.Activity;
using TaskFlow.Application.Common;

namespace TaskFlow.Api.Controllers;

/// <summary>A project's history and a task's history, newest first. Anyone who sees the project reads them.</summary>
[Authorize]
[Route("api/projects/{projectId:guid}")]
public sealed class ActivityController : ApiControllerBase
{
    [HttpGet("activity")]
    [ProducesResponseType<PagedResult<ActivityEntryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<ActivityEntryDto>>> ListProject(
        Guid projectId,
        [FromServices] ListProjectActivityHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50) =>
        OkOrProblem(await handler.HandleAsync(new ListProjectActivityQuery(projectId, page, pageSize), cancellationToken));

    [HttpGet("tasks/{taskId:guid}/activity")]
    [ProducesResponseType<PagedResult<ActivityEntryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<ActivityEntryDto>>> ListTask(
        Guid projectId,
        Guid taskId,
        [FromServices] ListTaskActivityHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50) =>
        OkOrProblem(await handler.HandleAsync(new ListTaskActivityQuery(projectId, taskId, page, pageSize), cancellationToken));
}
