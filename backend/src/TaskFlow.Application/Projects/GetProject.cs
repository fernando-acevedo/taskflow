using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Projects;

public sealed class GetProjectHandler(IAppDbContext db, ProjectAccess access, ICurrentUser currentUser)
{
    public async Task<Result<ProjectDto>> HandleAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var allowed = await access.GetAsync(projectId, ProjectAccessLevel.View, cancellationToken);
        if (!allowed.IsSuccess)
        {
            return allowed.Error;
        }

        return await db.Projects.AsNoTracking()
            .Where(project => project.Id == projectId)
            .ToDtos(db.Clients, currentUser.Id)
            .FirstAsync(cancellationToken);
    }
}
