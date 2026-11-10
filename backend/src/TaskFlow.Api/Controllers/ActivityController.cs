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
    [ProducesResponseType<CursorPage<ActivityEntryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CursorPage<ActivityEntryDto>>> ListProject(
        Guid projectId,
        [FromServices] ListProjectActivityHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = 50) =>
        OkOrProblem(await handler.HandleAsync(new ListProjectActivityQuery(projectId, cursor, pageSize), cancellationToken));

    [HttpGet("tasks/{taskId:guid}/activity")]
    [ProducesResponseType<CursorPage<ActivityEntryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CursorPage<ActivityEntryDto>>> ListTask(
        Guid projectId,
        Guid taskId,
        [FromServices] ListTaskActivityHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = 50) =>
        OkOrProblem(await handler.HandleAsync(new ListTaskActivityQuery(projectId, taskId, cursor, pageSize), cancellationToken));
}
