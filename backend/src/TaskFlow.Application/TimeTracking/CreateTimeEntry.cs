using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;
using TaskFlow.Application.Projects;
using TaskFlow.Application.Tasks;
using TaskFlow.Application.Users;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.TimeTracking;

public sealed record CreateTimeEntryCommand(Guid ProjectId, Guid TaskId, DateOnly Date, int Minutes, string? Note);

public sealed class CreateTimeEntryValidator : AbstractValidator<CreateTimeEntryCommand>
{
    public CreateTimeEntryValidator()
    {
        RuleFor(command => command.Minutes).InclusiveBetween(TimeEntry.MinMinutes, TimeEntry.MaxMinutes);
        RuleFor(command => command.Note).MaximumLength(TimeEntry.NoteMaxLength);
    }
}

/// <summary>Any member logs time on a task for themselves. It doesn't write to the activity log.</summary>
public sealed class CreateTimeEntryHandler(
    IAppDbContext db,
    ProjectAccess access,
    ICurrentUser currentUser,
    IIdentityService identity,
    IValidator<CreateTimeEntryCommand> validator,
    TimeProvider time)
{
    public async Task<Result<TimeEntryDto>> HandleAsync(CreateTimeEntryCommand command, CancellationToken cancellationToken)
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

        if (!await db.Tasks.AnyAsync(task => task.Id == command.TaskId && task.ProjectId == command.ProjectId, cancellationToken))
        {
            return TaskErrors.NotFound;
        }

        var created = TimeEntry.Create(
            allowed.Value, command.TaskId, currentUser.Id, command.Date, command.Minutes, command.Note, time.Today(), time.GetUtcNow());
        if (!created.IsSuccess)
        {
            return created.Error;
        }

        db.TimeEntries.Add(created.Value);
        await db.SaveChangesAsync(cancellationToken);

        var user = await identity.FindByIdAsync(created.Value.UserId, cancellationToken);
        return TimeEntryDto.From(created.Value, user?.DisplayName ?? string.Empty);
    }
}
