using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Application.Dashboard;

namespace TaskFlow.Api.Controllers;

/// <summary>Aggregated metrics, computed in the database.</summary>
[Authorize]
[Route("api")]
public sealed class DashboardController : ApiControllerBase
{
    /// <summary>The signed-in user's open tasks and a summary per client of the projects they can see.</summary>
    [HttpGet("dashboard")]
    [ProducesResponseType<MyDashboardDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MyDashboardDto>> Mine([FromServices] GetMyDashboardHandler handler, CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(cancellationToken));

    /// <summary>A project's tasks by status, open tasks by priority and by assignee, overdue and due this week.</summary>
    [HttpGet("projects/{projectId:guid}/dashboard")]
    [ProducesResponseType<ProjectDashboardDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectDashboardDto>> Project(
        Guid projectId,
        [FromServices] GetProjectDashboardHandler handler,
        CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(projectId, cancellationToken));
}
