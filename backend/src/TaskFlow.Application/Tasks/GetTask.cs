using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Projects;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Tasks;

public sealed record GetTaskQuery(Guid ProjectId, Guid TaskId);

public sealed class GetTaskHandler(IAppDbContext db, ProjectAccess access, TimeProvider time)
{
    public async Task<Result<TaskDto>> HandleAsync(GetTaskQuery query, CancellationToken cancellationToken)
    {
        var allowed = await access.GetAsync(query.ProjectId, ProjectAccessLevel.View, cancellationToken);
        if (!allowed.IsSuccess)
        {
            return allowed.Error;
        }

        var task = await db.Tasks.AsNoTracking()
            .Where(task => task.Id == query.TaskId && task.ProjectId == query.ProjectId)
            .ToDtos(time.Today())
            .FirstOrDefaultAsync(cancellationToken);

        return task is null ? TaskErrors.NotFound : task;
    }
}
