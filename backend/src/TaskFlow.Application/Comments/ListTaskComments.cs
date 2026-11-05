using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Projects;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Comments;

public sealed record ListTaskCommentsQuery(Guid ProjectId, Guid TaskId);

/// <summary>The task's comments, oldest first, with the author's name. Anyone who can see the project can read them.</summary>
public sealed class ListTaskCommentsHandler(IAppDbContext db, ProjectAccess access, IIdentityService identity)
{
    public async Task<Result<IReadOnlyList<TaskCommentDto>>> HandleAsync(ListTaskCommentsQuery query, CancellationToken cancellationToken)
    {
        var allowed = await access.GetAsync(query.ProjectId, ProjectAccessLevel.View, cancellationToken);
        if (!allowed.IsSuccess)
        {
            return allowed.Error;
        }

        if (!await db.Tasks.AnyAsync(task => task.Id == query.TaskId && task.ProjectId == query.ProjectId, cancellationToken))
        {
            return TaskErrors.NotFound;
        }

        var comments = await db.TaskComments.AsNoTracking()
            .Include(comment => comment.Mentions)
            .Where(comment => comment.TaskItemId == query.TaskId)
            .OrderBy(comment => comment.CreatedAt)
            .ThenBy(comment => comment.Id)
            .ToListAsync(cancellationToken);

        var authors = (await identity.FindByIdsAsync([.. comments.Select(comment => comment.AuthorId).Distinct()], cancellationToken))
            .ToDictionary(user => user.Id, user => user.DisplayName);

        return comments
            .Select(comment => TaskCommentDto.From(comment, authors.GetValueOrDefault(comment.AuthorId, string.Empty)))
            .ToList();
    }
}
