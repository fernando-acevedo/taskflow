using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Activity;
using TaskFlow.Application.Common;
using TaskFlow.Application.Notifications;
using TaskFlow.Application.Projects;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Activity;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Notifications;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Comments;

public sealed record CreateTaskCommentCommand(Guid ProjectId, Guid TaskId, string Body, IReadOnlyList<Guid>? MentionedUserIds);

public sealed class CreateTaskCommentValidator : AbstractValidator<CreateTaskCommentCommand>
{
    public CreateTaskCommentValidator() =>
        RuleFor(command => command.Body).NotEmpty().MaximumLength(TaskComment.BodyMaxLength);
}

/// <summary>Any member comments on a task, mentioning other members of the project if they want.</summary>
public sealed class CreateTaskCommentHandler(
    IAppDbContext db,
    ProjectAccess access,
    ICurrentUser currentUser,
    IIdentityService identity,
    IValidator<CreateTaskCommentCommand> validator,
    ActivityLog activity,
    Notifier notifier,
    TimeProvider time)
{
    public async Task<Result<TaskCommentDto>> HandleAsync(CreateTaskCommentCommand command, CancellationToken cancellationToken)
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

        var found = await db.Tasks
            .Where(task => task.Id == command.TaskId && task.ProjectId == command.ProjectId)
            .Select(task => new { task.Title, task.AssigneeId })
            .FirstOrDefaultAsync(cancellationToken);
        if (found is null)
        {
            return TaskErrors.NotFound;
        }

        var taskTitle = found.Title;

        var created = TaskComment.Create(
            allowed.Value, command.TaskId, currentUser.Id, command.Body, command.MentionedUserIds, time.GetUtcNow());
        if (!created.IsSuccess)
        {
            return created.Error;
        }

        if (!await MentionRules.AllActiveAsync(identity, command.MentionedUserIds?.ToList(), cancellationToken))
        {
            return TaskCommentErrors.MentionNotMember;
        }

        activity.Record(
            command.ProjectId,
            ActivityType.CommentAdded,
            new { taskTitle, commentId = created.Value.Id },
            command.TaskId);
        var projectName = allowed.Value.Name;
        var mentioned = created.Value.Mentions.Select(mention => mention.UserId).ToList();
        foreach (var userId in mentioned)
        {
            await notifier.NotifyAsync(
                NotificationType.Mentioned, userId, command.ProjectId, projectName, command.TaskId, taskTitle, created.Value.Id, cancellationToken);
        }

        // A mention already tells the assignee about this comment, so they get one notification, not two.
        if (found.AssigneeId is { } assigneeId && !mentioned.Contains(assigneeId))
        {
            await notifier.NotifyAsync(
                NotificationType.CommentOnYourTask, assigneeId, command.ProjectId, projectName, command.TaskId, taskTitle, created.Value.Id, cancellationToken);
        }

        db.TaskComments.Add(created.Value);
        await db.SaveChangesAsync(cancellationToken);
        await notifier.SendEmailsAsync(cancellationToken);

        var author = await identity.FindByIdAsync(currentUser.Id, cancellationToken);
        return TaskCommentDto.From(created.Value, author?.DisplayName ?? string.Empty);
    }
}
