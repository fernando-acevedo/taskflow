using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;
using TaskFlow.Application.Projects;
using TaskFlow.Application.Users;
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

        var exists = await db.Tasks.AnyAsync(task => task.Id == command.TaskId && task.ProjectId == command.ProjectId, cancellationToken);
        if (!exists)
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

        db.TaskComments.Add(created.Value);
        await db.SaveChangesAsync(cancellationToken);

        var author = await identity.FindByIdAsync(currentUser.Id, cancellationToken);
        return TaskCommentDto.From(created.Value, author?.DisplayName ?? string.Empty);
    }
}
