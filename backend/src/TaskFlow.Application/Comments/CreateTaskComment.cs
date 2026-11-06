using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Activity;
using TaskFlow.Application.Common;
using TaskFlow.Application.Projects;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Activity;
using TaskFlow.Domain.Common;
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

        var taskTitle = await db.Tasks
            .Where(task => task.Id == command.TaskId && task.ProjectId == command.ProjectId)
            .Select(task => task.Title)
            .FirstOrDefaultAsync(cancellationToken);
        if (taskTitle is null)
        {
            return TaskErrors.NotFound;
        }

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
        db.TaskComments.Add(created.Value);
        await db.SaveChangesAsync(cancellationToken);

        var author = await identity.FindByIdAsync(currentUser.Id, cancellationToken);
        return TaskCommentDto.From(created.Value, author?.DisplayName ?? string.Empty);
    }
}
