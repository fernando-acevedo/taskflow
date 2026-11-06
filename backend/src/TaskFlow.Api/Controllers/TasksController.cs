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
        [FromQuery] Guid? labelId,
        [FromServices] ListTasksHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] bool overdue = false) =>
        OkOrProblem(await handler.HandleAsync(
            new ListTasksQuery(projectId, status, priority, assigneeId, overdue, search, labelId), cancellationToken));

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
                new CreateTaskCommand(projectId, request.Title, request.Description, request.Priority, request.AssigneeId, request.DueDate, request.EstimatedMinutes, request.LabelIds),
                cancellationToken),
            nameof(Get),
            task => new { projectId, taskId = task.Id });

    /// <summary>
    /// Creates or replaces the task with this external key (200 when it existed, 201 when created). Repeating the call is
    /// safe. Every field replaces the stored value, except <c>status</c> (omitted keeps it; a new task starts in To Do) and
    /// <c>dependsOn</c> (omitted keeps the dependencies, an empty list clears them). A status change lands the task at the bottom
    /// of that column. There is no <c>rowVersion</c>: the last write wins. Needs a key or user with write access.
    /// </summary>
    [HttpPut("by-key/{externalKey}")]
    [ProducesResponseType<TaskDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<TaskDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TaskDto>> Upsert(
        Guid projectId,
        string externalKey,
        UpsertTaskRequest request,
        [FromServices] UpsertTaskHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new UpsertTaskCommand(projectId, externalKey, request.ToFields()), cancellationToken);
        if (!result.IsSuccess)
        {
            return ProblemFrom(result.Error);
        }

        var task = result.Value.Task;
        return result.Value.Created
            ? CreatedAtAction(nameof(Get), new { projectId, taskId = task.Id }, task)
            : Ok(task);
    }

    /// <summary>
    /// Creates or updates up to 200 tasks by <c>externalKey</c> in one transaction. It is all or nothing: if any item fails,
    /// nothing is saved, the answer is 422 and each item says what happened (<c>Failed</c> with its <c>errorCode</c>, or
    /// <c>NotApplied</c> for the ones that were fine). On success (200) each item is <c>Created</c> or <c>Updated</c>. Items follow
    /// the rules of <c>PUT by-key</c>; <c>dependsOn</c> may name tasks of the same request, in any order.
    /// </summary>
    [HttpPost("sync")]
    [ProducesResponseType<SyncTasksResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<SyncTasksResult>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SyncTasksResult>> Sync(
        Guid projectId,
        SyncTasksRequest request,
        [FromServices] SyncTasksHandler handler,
        CancellationToken cancellationToken)
    {
        var items = (request.Items ?? []).Select(item => new SyncTaskItem(item.ExternalKey, item.ToFields())).ToList();
        var result = await handler.HandleAsync(new SyncTasksCommand(projectId, items), cancellationToken);
        if (!result.IsSuccess)
        {
            return ProblemFrom(result.Error);
        }

        return result.Value.Applied ? Ok(result.Value) : UnprocessableEntity(result.Value);
    }

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
                projectId, taskId, request.Title, request.Description, request.Priority, request.AssigneeId, request.DueDate, request.EstimatedMinutes, request.RowVersion, request.LabelIds, request.StartDate, request.IsBlocked, request.StatusNote),
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
    DateOnly? DueDate = null,
    int? EstimatedMinutes = null,
    IReadOnlyList<Guid>? LabelIds = null);

public sealed record UpdateTaskRequest(
    string Title,
    string? Description,
    TaskPriority Priority,
    Guid? AssigneeId,
    DateOnly? DueDate,
    int? EstimatedMinutes,
    byte[] RowVersion,
    IReadOnlyList<Guid>? LabelIds = null,
    DateOnly? StartDate = null,
    bool IsBlocked = false,
    string? StatusNote = null);

/// <param name="Status">Omit to keep the status (To Do for a new task).</param>
/// <param name="DependsOn">External keys of the tasks this one depends on. Omit to keep them, send <c>[]</c> to clear them.</param>
/// <param name="Labels">Names of project labels. Omit to keep the task's labels, send a list to replace them. A name the project doesn't have is created, if the caller may manage labels (a person who owns the project; never an API key).</param>
public sealed record UpsertTaskRequest(
    string Title,
    string? Description,
    TaskPriority Priority = TaskPriority.Medium,
    Guid? AssigneeId = null,
    DateOnly? StartDate = null,
    DateOnly? DueDate = null,
    int? EstimatedMinutes = null,
    TaskItemStatus? Status = null,
    bool IsBlocked = false,
    string? StatusNote = null,
    IReadOnlyList<string>? DependsOn = null,
    IReadOnlyList<string>? Labels = null)
{
    internal TaskUpsertFields ToFields() =>
        new(Title, Description, Priority, AssigneeId, StartDate, DueDate, EstimatedMinutes, Status, IsBlocked, StatusNote, DependsOn, Labels);
}

/// <summary>One task of a sync: the fields of <see cref="UpsertTaskRequest"/> plus the key that identifies it.</summary>
public sealed record SyncTaskItemRequest(
    string ExternalKey,
    string Title,
    string? Description,
    TaskPriority Priority = TaskPriority.Medium,
    Guid? AssigneeId = null,
    DateOnly? StartDate = null,
    DateOnly? DueDate = null,
    int? EstimatedMinutes = null,
    TaskItemStatus? Status = null,
    bool IsBlocked = false,
    string? StatusNote = null,
    IReadOnlyList<string>? DependsOn = null,
    IReadOnlyList<string>? Labels = null)
{
    internal TaskUpsertFields ToFields() =>
        new(Title, Description, Priority, AssigneeId, StartDate, DueDate, EstimatedMinutes, Status, IsBlocked, StatusNote, DependsOn, Labels);
}

/// <param name="Items">Between 1 and 200 tasks, each with a different <c>externalKey</c>.</param>
public sealed record SyncTasksRequest(IReadOnlyList<SyncTaskItemRequest> Items);

public sealed record MoveTaskRequest(TaskItemStatus Status, int Position, byte[] RowVersion);
