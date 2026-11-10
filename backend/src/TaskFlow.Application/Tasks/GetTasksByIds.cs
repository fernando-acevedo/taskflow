using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Projects;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Tasks;

public sealed record GetTasksByIdsQuery(Guid ProjectId, IReadOnlyCollection<Guid> TaskIds);

/// <summary>Several tasks of a project as the API serves them, in one query and with the project access checked once (real-time publishing of a bulk change). Ids that no longer exist are left out.</summary>
public sealed class GetTasksByIdsHandler(IAppDbContext db, ProjectAccess access, TimeProvider time)
{
    public async Task<Result<IReadOnlyList<TaskDto>>> HandleAsync(GetTasksByIdsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var allowed = await access.GetAsync(query.ProjectId, ProjectAccessLevel.View, cancellationToken);
        if (!allowed.IsSuccess)
        {
            return allowed.Error;
        }

        var ids = query.TaskIds.ToList();
        IReadOnlyList<TaskDto> tasks = await db.Tasks.AsNoTracking()
            .Where(task => task.ProjectId == query.ProjectId && ids.Contains(task.Id))
            .ToDtos(time.Today())
            .ToListAsync(cancellationToken);
        return Result<IReadOnlyList<TaskDto>>.Success(tasks);
    }
}
