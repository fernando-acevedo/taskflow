using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Projects;
using TaskFlow.Domain.Common;

namespace TaskFlow.Application.Labels;

public sealed record ListLabelsQuery(Guid ProjectId);

/// <summary>The project's labels by name. Any member (or admin, read-only) can see them.</summary>
public sealed class ListLabelsHandler(IAppDbContext db, ProjectAccess access)
{
    public async Task<Result<IReadOnlyList<LabelDto>>> HandleAsync(ListLabelsQuery query, CancellationToken cancellationToken)
    {
        var allowed = await access.GetAsync(query.ProjectId, ProjectAccessLevel.View, cancellationToken);
        if (!allowed.IsSuccess)
        {
            return allowed.Error;
        }

        var labels = await db.Labels.AsNoTracking()
            .Where(label => label.ProjectId == query.ProjectId)
            .OrderBy(label => label.Name)
            .Select(label => new LabelDto(label.Id, label.Name, label.Color))
            .ToListAsync(cancellationToken);
        return labels;
    }
}
