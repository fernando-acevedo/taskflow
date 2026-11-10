using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;
using TaskFlow.Application.Notifications;
using TaskFlow.Application.Projects;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Notifications;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Comments;

public sealed record UpdateTaskCommentCommand(
    Guid ProjectId,
    Guid TaskId,
    Guid CommentId,
    string Body,
    IReadOnlyList<Guid>? MentionedUserIds);

public sealed class UpdateTaskCommentValidator : AbstractValidator<UpdateTaskCommentCommand>
{
    public UpdateTaskCommentValidator() =>
        RuleFor(command => command.Body).NotEmpty().MaximumLength(TaskComment.BodyMaxLength);
}

/// <summary>The author edits their comment; the mentions are replaced by the ones sent.</summary>
public sealed class UpdateTaskCommentHandler(
    IAppDbContext db,
    ProjectAccess access,
    ICurrentUser currentUser,
    IIdentityService identity,
    IValidator<UpdateTaskCommentCommand> validator,
    Notifier notifier,
    TimeProvider time)
{
    public async Task<Result<TaskCommentDto>> HandleAsync(UpdateTaskCommentCommand command, CancellationToken cancellationToken)
    {
        var allowed = await access.GetAsync(command.ProjectId, ProjectAccessLevel.Work, cancellationToken);
        if (!allowed.IsSuccess)
        {
            return allowed.Error;
        }

        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        var comment = await db.TaskComments.Include(comment => comment.Mentions).FirstOrDefaultAsync(
            comment => comment.Id == command.CommentId
                && comment.TaskItemId == command.TaskId
                && db.Tasks.Any(task => task.Id == comment.TaskItemId && task.ProjectId == command.ProjectId),
            cancellationToken);
        if (comment is null)
        {
            return TaskCommentErrors.NotFound;
        }

        var mentionedBefore = comment.Mentions.Select(mention => mention.UserId).ToList();
        var edited = comment.Edit(allowed.Value, currentUser.Id, command.Body, command.MentionedUserIds, time.GetUtcNow());
        if (!edited.IsSuccess)
        {
            return edited.Error;
        }

        if (!await MentionRules.AllActiveAsync(identity, command.MentionedUserIds?.ToList(), cancellationToken))
        {
            return TaskCommentErrors.MentionNotMember;
        }

        // Only the people this edit adds are notified; the ones already mentioned were notified before.
        var added = comment.Mentions.Select(mention => mention.UserId).Except(mentionedBefore).ToList();
        if (added.Count > 0)
        {
            var taskTitle = await db.Tasks
                .Where(task => task.Id == command.TaskId)
                .Select(task => task.Title)
                .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
            foreach (var userId in added)
            {
                await notifier.NotifyAsync(
                    NotificationType.Mentioned, userId, command.ProjectId, allowed.Value.Name, command.TaskId, taskTitle, comment.Id, cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        var author = await identity.FindByIdAsync(comment.AuthorId, cancellationToken);
        return TaskCommentDto.From(comment, author?.DisplayName ?? string.Empty);
    }
}
