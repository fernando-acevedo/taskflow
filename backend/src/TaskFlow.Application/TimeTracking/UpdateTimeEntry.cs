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

public sealed record UpdateTimeEntryCommand(Guid ProjectId, Guid TaskId, Guid EntryId, DateOnly Date, int Minutes, string? Note);

public sealed class UpdateTimeEntryValidator : AbstractValidator<UpdateTimeEntryCommand>
{
    public UpdateTimeEntryValidator()
    {
        RuleFor(command => command.Minutes).InclusiveBetween(TimeEntry.MinMinutes, TimeEntry.MaxMinutes);
        RuleFor(command => command.Note).MaximumLength(TimeEntry.NoteMaxLength);
    }
}

/// <summary>The owner of the entry, or an owner of the project, corrects the date, minutes and note.</summary>
public sealed class UpdateTimeEntryHandler(
    IAppDbContext db,
    ProjectAccess access,
    ICurrentUser currentUser,
    IIdentityService identity,
    IValidator<UpdateTimeEntryCommand> validator,
    TimeProvider time)
{
    public async Task<Result<TimeEntryDto>> HandleAsync(UpdateTimeEntryCommand command, CancellationToken cancellationToken)
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

        var entry = await db.TimeEntries.FirstOrDefaultAsync(
            entry => entry.Id == command.EntryId
                && entry.TaskItemId == command.TaskId
                && db.Tasks.Any(task => task.Id == entry.TaskItemId && task.ProjectId == command.ProjectId),
            cancellationToken);
        if (entry is null)
        {
            return TimeEntryErrors.NotFound;
        }

        var canManage = ProjectAccess.Allows(allowed.Value, ProjectAccessLevel.Manage, currentUser);
        var edited = entry.Edit(allowed.Value, currentUser.Id, canManage, command.Date, command.Minutes, command.Note, time.Today());
        if (!edited.IsSuccess)
        {
            return edited.Error;
        }

        await db.SaveChangesAsync(cancellationToken);

        var user = await identity.FindByIdAsync(entry.UserId, cancellationToken);
        return TimeEntryDto.From(entry, user?.DisplayName ?? string.Empty);
    }
}
