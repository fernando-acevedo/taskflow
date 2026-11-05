using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Application.TimeTracking;

namespace TaskFlow.Api.Controllers;

/// <summary>Time logged on a task. Members log their own time; the owner of an entry or a project owner corrects it.</summary>
[Authorize]
[Route("api/projects/{projectId:guid}/tasks/{taskId:guid}/time-entries")]
public sealed class TimeEntriesController : ApiControllerBase
{
    /// <summary>
    /// The signed-in user's entries on the task (all of them for project owners), newest day first, with the task's total
    /// minutes (everyone's) and its estimate.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<TaskTimeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TaskTimeDto>> List(
        Guid projectId,
        Guid taskId,
        [FromServices] ListTimeEntriesHandler handler,
        CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(new ListTimeEntriesQuery(projectId, taskId), cancellationToken));

    /// <summary>Logs time for the signed-in user. The date can't be more than a day ahead of UTC today.</summary>
    [HttpPost]
    [ProducesResponseType<TimeEntryDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TimeEntryDto>> Create(
        Guid projectId,
        Guid taskId,
        TimeEntryRequest request,
        [FromServices] CreateTimeEntryHandler handler,
        CancellationToken cancellationToken) =>
        CreatedOrProblem(
            await handler.HandleAsync(
                new CreateTimeEntryCommand(projectId, taskId, request.Date, request.Minutes, request.Note), cancellationToken),
            nameof(List),
            _ => new { projectId, taskId });

    [HttpPut("{entryId:guid}")]
    [ProducesResponseType<TimeEntryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TimeEntryDto>> Update(
        Guid projectId,
        Guid taskId,
        Guid entryId,
        TimeEntryRequest request,
        [FromServices] UpdateTimeEntryHandler handler,
        CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(
            new UpdateTimeEntryCommand(projectId, taskId, entryId, request.Date, request.Minutes, request.Note), cancellationToken));

    [HttpDelete("{entryId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Delete(
        Guid projectId,
        Guid taskId,
        Guid entryId,
        [FromServices] DeleteTimeEntryHandler handler,
        CancellationToken cancellationToken) =>
        NoContentOrProblem(await handler.HandleAsync(new DeleteTimeEntryCommand(projectId, taskId, entryId), cancellationToken));
}

public sealed record TimeEntryRequest(DateOnly Date, int Minutes, string? Note);
