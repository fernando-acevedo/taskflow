using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Application.Tasks;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Api.Controllers;

/// <summary>Tasks of a project. Members read and change them; admins who aren't members can only read.</summary>
[Authorize]
[Route("api/projects/{projectId:guid}/tasks")]
public sealed class TasksController : ApiControllerBase
{
    /// <summary>Tasks in board order (status, then position), optionally filtered.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<TaskDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<TaskDto>>> List(
        Guid projectId,
        [FromQuery] TaskItemStatus? status,
        [FromQuery] TaskPriority? priority,
        [FromQuery] Guid? assigneeId,
        [FromQuery] string? search,
        [FromServices] ListTasksHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] bool overdue = false) =>
        OkOrProblem(await handler.HandleAsync(
            new ListTasksQuery(projectId, status, priority, assigneeId, overdue, search), cancellationToken));

    [HttpGet("{taskId:guid}")]
    [ProducesResponseType<TaskDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TaskDto>> Get(
        Guid projectId,
        Guid taskId,
        [FromServices] GetTaskHandler handler,
        CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(new GetTaskQuery(projectId, taskId), cancellationToken));

    /// <summary>Adds a task at the bottom of To Do. The due date can't be in the past.</summary>
    [HttpPost]
    [ProducesResponseType<TaskDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TaskDto>> Create(
        Guid projectId,
        CreateTaskRequest request,
        [FromServices] CreateTaskHandler handler,
        CancellationToken cancellationToken) =>
        CreatedOrProblem(
            await handler.HandleAsync(
                new CreateTaskCommand(projectId, request.Title, request.Description, request.Priority, request.AssigneeId, request.DueDate),
                cancellationToken),
            nameof(Get),
            task => new { projectId, taskId = task.Id });

    /// <summary>Edits the task's details. Send the <c>rowVersion</c> you read; a newer one answers 409.</summary>
    [HttpPut("{taskId:guid}")]
    [ProducesResponseType<TaskDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TaskDto>> Update(
        Guid projectId,
        Guid taskId,
        UpdateTaskRequest request,
        [FromServices] UpdateTaskHandler handler,
        CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(
            new UpdateTaskCommand(
                projectId, taskId, request.Title, request.Description, request.Priority, request.AssigneeId, request.DueDate, request.RowVersion),
            cancellationToken));

    /// <summary>Moves the task to a column and position, as a Kanban drag and drop does.</summary>
    [HttpPut("{taskId:guid}/position")]
    [ProducesResponseType<TaskDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TaskDto>> Move(
        Guid projectId,
        Guid taskId,
        MoveTaskRequest request,
        [FromServices] MoveTaskHandler handler,
        CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(
            new MoveTaskCommand(projectId, taskId, request.Status, request.Position, request.RowVersion), cancellationToken));

    [HttpDelete("{taskId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(
        Guid projectId,
        Guid taskId,
        [FromServices] DeleteTaskHandler handler,
        CancellationToken cancellationToken) =>
        NoContentOrProblem(await handler.HandleAsync(new DeleteTaskCommand(projectId, taskId), cancellationToken));
}

public sealed record CreateTaskRequest(
    string Title,
    string? Description,
    TaskPriority Priority = TaskPriority.Medium,
    Guid? AssigneeId = null,
    DateOnly? DueDate = null);

public sealed record UpdateTaskRequest(
    string Title,
    string? Description,
    TaskPriority Priority,
    Guid? AssigneeId,
    DateOnly? DueDate,
    byte[] RowVersion);

public sealed record MoveTaskRequest(TaskItemStatus Status, int Position, byte[] RowVersion);
