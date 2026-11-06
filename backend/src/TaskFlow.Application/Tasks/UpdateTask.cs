using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Activity;
using TaskFlow.Application.Common;
using TaskFlow.Application.Notifications;
using TaskFlow.Application.Projects;
using TaskFlow.Domain.Activity;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Notifications;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Tasks;

/// <param name="RowVersion">The version the user edited; a newer one in the database means a conflicting edit.</param>
public sealed record UpdateTaskCommand(
    Guid ProjectId,
    Guid TaskId,
    string Title,
    string? Description,
    TaskPriority Priority,
    Guid? AssigneeId,
    DateOnly? DueDate,
    int? EstimatedMinutes,
    byte[] RowVersion,
    IReadOnlyList<Guid>? LabelIds = null,
    DateOnly? StartDate = null,
    bool IsBlocked = false,
    string? StatusNote = null) : ITaskFields;

/// <summary>Unlike creation, an update may keep a due date that has passed.</summary>
public sealed class UpdateTaskValidator : AbstractValidator<UpdateTaskCommand>
{
    public UpdateTaskValidator()
    {
        this.AddTaskFieldRules();
        RuleFor(command => command.RowVersion).NotEmpty();
        RuleFor(command => command.StatusNote).MaximumLength(TaskItem.StatusNoteMaxLength);
    }
}

public sealed class UpdateTaskHandler(
    IAppDbContext db,
    ProjectAccess access,
    IValidator<UpdateTaskCommand> validator,
    GetTaskHandler getTask,
    ActivityLog activity,
    Notifier notifier,
    TimeProvider time)
{
    public async Task<Result<TaskDto>> HandleAsync(UpdateTaskCommand command, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        var allowed = await access.GetAsync(command.ProjectId, ProjectAccessLevel.Work, cancellationToken);
        if (!allowed.IsSuccess)
        {
            return allowed.Error;
        }

        var task = await db.Tasks.Include(task => task.Labels).FirstOrDefaultAsync(
            task => task.Id == command.TaskId && task.ProjectId == command.ProjectId, cancellationToken);
        if (task is null)
        {
            return TaskErrors.NotFound;
        }

        if (!task.RowVersion.SequenceEqual(command.RowVersion))
        {
            return CommonErrors.Stale;
        }

        var before = (task.Title, task.Description, task.Priority, task.AssigneeId, task.DueDate, task.EstimatedMinutes, task.StartDate, task.IsBlocked, task.StatusNote);
        var labelsBefore = task.Labels.Select(label => label.Name).ToList();

        var now = time.GetUtcNow();
        var updated = task.SetSchedule(allowed.Value, command.StartDate, command.DueDate, now);
        if (updated.IsSuccess)
        {
            updated = task.SetPlanning(allowed.Value, null, command.StartDate, command.IsBlocked, command.StatusNote, now);
        }

        if (updated.IsSuccess)
        {
            updated = task.Update(
                allowed.Value,
                command.Title.Trim(),
                command.Description.TrimToNull(),
                command.Priority,
                command.AssigneeId,
                command.DueDate,
                now,
                command.EstimatedMinutes);
        }

        if (!updated.IsSuccess)
        {
            return updated.Error;
        }

        var labelled = await task.ApplyLabelsAsync(db, allowed.Value, command.LabelIds, cancellationToken);
        if (!labelled.IsSuccess)
        {
            return labelled.Error;
        }

        RecordChanges(command.ProjectId, task, before, labelsBefore);
        if (before.AssigneeId != task.AssigneeId && task.AssigneeId is { } assigneeId)
        {
            await notifier.NotifyAsync(
                NotificationType.TaskAssigned, assigneeId, command.ProjectId, allowed.Value.Name, task.Id, task.Title, null, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await notifier.SendEmailsAsync(cancellationToken);
        return await getTask.HandleAsync(new GetTaskQuery(command.ProjectId, task.Id), cancellationToken);
    }

    /// <summary>One TaskUpdated entry for the fields and labels that changed, and a TaskAssigned one if the assignee did.</summary>
    private void RecordChanges(
        Guid projectId,
        TaskItem task,
        (string Title, string? Description, TaskPriority Priority, Guid? AssigneeId, DateOnly? DueDate, int? EstimatedMinutes, DateOnly? StartDate, bool IsBlocked, string? StatusNote) before,
        List<string> labelsBefore)
    {
        var labelsAfter = task.Labels.Select(label => label.Name).ToList();
        var added = labelsAfter.Except(labelsBefore).Order().ToList();
        var removed = labelsBefore.Except(labelsAfter).Order().ToList();

        var titleChanged = before.Title != task.Title;
        var priorityChanged = before.Priority != task.Priority;
        var dueDateChanged = before.DueDate != task.DueDate;
        var descriptionChanged = before.Description != task.Description;
        var estimateChanged = before.EstimatedMinutes != task.EstimatedMinutes;
        var startDateChanged = before.StartDate != task.StartDate;
        var blockedChanged = before.IsBlocked != task.IsBlocked;
        var noteChanged = before.StatusNote != task.StatusNote;
        if (titleChanged || priorityChanged || dueDateChanged || descriptionChanged || estimateChanged || startDateChanged || blockedChanged || noteChanged || added.Count > 0 || removed.Count > 0)
        {
            activity.Record(
                projectId,
                ActivityType.TaskUpdated,
                new
                {
                    taskTitle = task.Title,
                    title = titleChanged ? new ValueChange<string>(before.Title, task.Title) : null,
                    priority = priorityChanged ? new ValueChange<TaskPriority>(before.Priority, task.Priority) : null,
                    dueDate = dueDateChanged ? new ValueChange<DateOnly?>(before.DueDate, task.DueDate) : null,
                    estimatedMinutes = estimateChanged ? new ValueChange<int?>(before.EstimatedMinutes, task.EstimatedMinutes) : null,
                    startDate = startDateChanged ? new ValueChange<DateOnly?>(before.StartDate, task.StartDate) : null,
                    isBlocked = blockedChanged ? new ValueChange<bool>(before.IsBlocked, task.IsBlocked) : null,
                    statusNoteChanged = noteChanged ? true : (bool?)null,
                    descriptionChanged = descriptionChanged ? true : (bool?)null,
                    labelsAdded = added.Count > 0 ? added : null,
                    labelsRemoved = removed.Count > 0 ? removed : null,
                },
                task.Id);
        }

        if (before.AssigneeId != task.AssigneeId)
        {
            activity.Record(
                projectId,
                ActivityType.TaskAssigned,
                new { taskTitle = task.Title, assigneeId = task.AssigneeId, previousAssigneeId = before.AssigneeId },
                task.Id);
        }
    }

    // Always writes both keys, so that setting or clearing a value shows up as a null instead of a missing key.
    private sealed record ValueChange<T>(
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] T From,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] T To);
}
