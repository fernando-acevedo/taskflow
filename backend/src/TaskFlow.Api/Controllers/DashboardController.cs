using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaskFlow.Api.Identity;
using TaskFlow.Application.Dashboard;

namespace TaskFlow.Api.Controllers;

/// <summary>Aggregated metrics, computed in the database.</summary>
[Authorize]
[EnableRateLimiting(RateLimitPolicies.Report)]
[Route("api")]
public sealed class DashboardController : ApiControllerBase
{
    /// <summary>
    /// The signed-in user's open tasks and a summary per client of the projects they can see. <c>today</c> (yyyy-MM-dd) is the
    /// client's local date; within one day of UTC today it picks the week of the logged hours.
    /// </summary>
    [HttpGet("dashboard")]
    [ProducesResponseType<MyDashboardDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MyDashboardDto>> Mine(
        [FromServices] GetMyDashboardHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] DateOnly? today = null) =>
        OkOrProblem(await handler.HandleAsync(today, cancellationToken));

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
