using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Application.Projects.Members;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Api.Controllers;

/// <summary>Members of a project. Owners manage them; any member can leave by removing themselves.</summary>
[Authorize]
[Route("api/projects/{projectId:guid}/members")]
public sealed class ProjectMembersController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ProjectMemberDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<ProjectMemberDto>>> List(
        Guid projectId,
        [FromServices] ListProjectMembersHandler handler,
        CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(projectId, cancellationToken));

    /// <summary>Adds a registered user by email.</summary>
    [HttpPost]
    [ProducesResponseType<ProjectMemberDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProjectMemberDto>> Add(
        Guid projectId,
        AddMemberRequest request,
        [FromServices] AddProjectMemberHandler handler,
        CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(new AddProjectMemberCommand(projectId, request.Email, request.Role), cancellationToken));

    /// <summary>Promotes or demotes a member. The last owner can't step down.</summary>
    [HttpPut("{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> ChangeRole(
        Guid projectId,
        Guid userId,
        ChangeRoleRequest request,
        [FromServices] ChangeProjectMemberRoleHandler handler,
        CancellationToken cancellationToken) =>
        NoContentOrProblem(await handler.HandleAsync(new ChangeProjectMemberRoleCommand(projectId, userId, request.Role), cancellationToken));

    [HttpDelete("{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Remove(
        Guid projectId,
        Guid userId,
        [FromServices] RemoveProjectMemberHandler handler,
        CancellationToken cancellationToken) =>
        NoContentOrProblem(await handler.HandleAsync(new RemoveProjectMemberCommand(projectId, userId), cancellationToken));
}

public sealed record AddMemberRequest(string Email, ProjectRole Role = ProjectRole.Member);

public sealed record ChangeRoleRequest(ProjectRole Role);
