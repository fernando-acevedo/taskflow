using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Application.Labels;
using TaskFlow.Domain.Common;

namespace TaskFlow.Api.Controllers;

/// <summary>Labels of a project. Any member reads them; owners manage them.</summary>
[Authorize]
[Route("api/projects/{projectId:guid}/labels")]
public sealed class LabelsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<LabelDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<LabelDto>>> List(
        Guid projectId,
        [FromServices] ListLabelsHandler handler,
        CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(new ListLabelsQuery(projectId), cancellationToken));

    /// <summary>Adds a label. The name is unique in the project and the color is <c>#RRGGBB</c>.</summary>
    [HttpPost]
    [ProducesResponseType<LabelDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LabelDto>> Create(
        Guid projectId,
        LabelRequest request,
        [FromServices] CreateLabelHandler handler,
        CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(new CreateLabelCommand(projectId, request.Name, request.Color), cancellationToken));

    [HttpPut("{labelId:guid}")]
    [ProducesResponseType<LabelDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<LabelDto>> Update(
        Guid projectId,
        Guid labelId,
        UpdateLabelRequest request,
        [FromServices] UpdateLabelHandler handler,
        CancellationToken cancellationToken)
    {
        if (!HasPrecondition(request.RowVersion))
        {
            return ProblemFrom(CommonErrors.PreconditionRequired);
        }

        return OkOrProblem(await handler.HandleAsync(new UpdateLabelCommand(projectId, labelId, request.Name, request.Color, RowVersionFrom(request.RowVersion)), cancellationToken));
    }

    /// <summary>Deletes the label and removes it from the tasks that have it.</summary>
    [HttpDelete("{labelId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed)]
    public async Task<ActionResult> Delete(
        Guid projectId,
        Guid labelId,
        [FromServices] DeleteLabelHandler handler,
        CancellationToken cancellationToken) =>
        NoContentOrProblem(await handler.HandleAsync(new DeleteLabelCommand(projectId, labelId, OptionalRowVersion()), cancellationToken));
}

public sealed record LabelRequest(string Name, string Color);

public sealed record UpdateLabelRequest(string Name, string Color, byte[]? RowVersion);
