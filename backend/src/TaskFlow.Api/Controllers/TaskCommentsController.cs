using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Application.Comments;
using TaskFlow.Domain.Common;

namespace TaskFlow.Api.Controllers;

/// <summary>Comments on a task. Anyone who sees the project reads them; members comment; the author edits.</summary>
[Authorize]
[Route("api/projects/{projectId:guid}/tasks/{taskId:guid}/comments")]
public sealed class TaskCommentsController : ApiControllerBase
{
    /// <summary>The comments, oldest first.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<TaskCommentDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<TaskCommentDto>>> List(
        Guid projectId,
        Guid taskId,
        [FromServices] ListTaskCommentsHandler handler,
        CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(new ListTaskCommentsQuery(projectId, taskId), cancellationToken));

    [HttpPost]
    [ProducesResponseType<TaskCommentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TaskCommentDto>> Create(
        Guid projectId,
        Guid taskId,
        TaskCommentRequest request,
        [FromServices] CreateTaskCommentHandler handler,
        CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(
            new CreateTaskCommentCommand(projectId, taskId, request.Body, request.MentionedUserIds), cancellationToken));

    [HttpPut("{commentId:guid}")]
    [ProducesResponseType<TaskCommentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<TaskCommentDto>> Update(
        Guid projectId,
        Guid taskId,
        Guid commentId,
        UpdateTaskCommentRequest request,
        [FromServices] UpdateTaskCommentHandler handler,
        CancellationToken cancellationToken)
    {
        if (!HasPrecondition(request.RowVersion))
        {
            return ProblemFrom(CommonErrors.PreconditionRequired);
        }

        return OkOrProblem(await handler.HandleAsync(
            new UpdateTaskCommentCommand(projectId, taskId, commentId, request.Body, request.MentionedUserIds, RowVersionFrom(request.RowVersion)), cancellationToken));
    }

    [HttpDelete("{commentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed)]
    public async Task<ActionResult> Delete(
        Guid projectId,
        Guid taskId,
        Guid commentId,
        [FromServices] DeleteTaskCommentHandler handler,
        CancellationToken cancellationToken) =>
        NoContentOrProblem(await handler.HandleAsync(new DeleteTaskCommentCommand(projectId, taskId, commentId, OptionalRowVersion()), cancellationToken));
}

public sealed record TaskCommentRequest(string Body, IReadOnlyList<Guid>? MentionedUserIds);

public sealed record UpdateTaskCommentRequest(string Body, IReadOnlyList<Guid>? MentionedUserIds, byte[]? RowVersion);
