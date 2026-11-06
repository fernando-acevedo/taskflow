using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Application.TimeTracking;

namespace TaskFlow.Api.Controllers;

/// <summary>Hours logged on a project, in total, per person and per task. Only project owners read it.</summary>
[Authorize]
[Route("api/projects/{projectId:guid}")]
public sealed class TimeSummaryController : ApiControllerBase
{
    /// <summary>The summary of a date range (both ends inclusive, at most 366 days; omit them for all time).</summary>
    [HttpGet("time-summary")]
    [ProducesResponseType<TimeSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TimeSummaryDto>> Summary(
        Guid projectId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid? userId,
        [FromServices] GetTimeSummaryHandler handler,
        CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(new GetTimeSummaryQuery(projectId, from, to, userId), cancellationToken));

    /// <summary>The entries behind the summary as CSV (UTF-8 with BOM, comma separated); `from` and `to` are required.</summary>
    [HttpGet("time-summary.csv")]
    [Produces("text/csv")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> SummaryCsv(
        Guid projectId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid? userId,
        [FromServices] ExportTimeSummaryCsvHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetTimeSummaryQuery(projectId, from, to, userId), cancellationToken);
        return result.IsSuccess ? File(result.Value, "text/csv; charset=utf-8", "time-summary.csv") : ProblemFrom(result.Error);
    }
}
