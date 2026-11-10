using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Projects;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Comments;

public sealed record DeleteTaskCommentCommand(Guid ProjectId, Guid TaskId, Guid CommentId, byte[]? RowVersion = null);

/// <summary>The author, or an owner of the project, deletes a comment.</summary>
public sealed class DeleteTaskCommentHandler(IAppDbContext db, ProjectAccess access, ICurrentUser currentUser)
{
    public async Task<Result> HandleAsync(DeleteTaskCommentCommand command, CancellationToken cancellationToken)
    {
        var allowed = await access.GetAsync(command.ProjectId, ProjectAccessLevel.Work, cancellationToken);
        if (!allowed.IsSuccess)
        {
            return allowed;
        }

        var comment = await db.TaskComments.FirstOrDefaultAsync(
            comment => comment.Id == command.CommentId
                && comment.TaskItemId == command.TaskId
                && db.Tasks.Any(task => task.Id == comment.TaskItemId && task.ProjectId == command.ProjectId),
            cancellationToken);
        if (comment is null)
        {
            return TaskCommentErrors.NotFound;
        }

        if (command.RowVersion is not null && !RowVersions.Matches(comment.RowVersion, command.RowVersion))
        {
            return CommonErrors.Stale;
        }

        var canManage = ProjectAccess.Allows(allowed.Value, ProjectAccessLevel.Manage, currentUser);
        var deletable = comment.CanDelete(allowed.Value, currentUser.Id, canManage);
        if (!deletable.IsSuccess)
        {
            return deletable;
        }

        comment.MarkDeleted(allowed.Value);
        db.TaskComments.Remove(comment);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone else deleted the same comment first.
            return TaskCommentErrors.NotFound;
        }

        return Result.Success();
    }
}
