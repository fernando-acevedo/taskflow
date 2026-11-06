using FluentValidation;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;
using TaskFlow.Application.Projects;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Tasks;

public sealed record UpsertTaskCommand(Guid ProjectId, string ExternalKey, TaskUpsertFields Fields);

/// <param name="Created">True when the key was new and the task was created.</param>
public sealed record UpsertTaskResult(TaskDto Task, bool Created);

/// <summary>
/// Creates or replaces the task with this external key (ADR 0017); repeating the call changes nothing. Last write wins: there
/// is no row version to send. Unlike creating a task by hand, the due date may be in the past and the assignee is not notified.
/// </summary>
public sealed class UpsertTaskHandler(
    ProjectAccess access,
    IValidator<TaskUpsertFields> validator,
    TaskUpserter upserter,
    GetTaskHandler getTask,
    ITransactionRunner transactions)
{
    public async Task<Result<UpsertTaskResult>> HandleAsync(UpsertTaskCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await validator.ValidateAsync(command.Fields, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        if (string.IsNullOrWhiteSpace(command.ExternalKey))
        {
            return TaskErrors.ExternalKeyInvalid;
        }

        var created = false;
        Guid taskId = default;

        // Serializable: two requests with the same new key can't both create it.
        var result = await transactions.RunSerializableAsync(async token =>
        {
            var allowed = await access.GetAsync(command.ProjectId, ProjectAccessLevel.Work, token);
            if (!allowed.IsSuccess)
            {
                return allowed;
            }

            var applied = await upserter.ApplyAsync(allowed.Value, command.ExternalKey, command.Fields, token);
            if (!applied.IsSuccess)
            {
                return applied;
            }

            var task = applied.Value.Task;
            if (command.Fields.DependsOn is { } keys)
            {
                var edges = await upserter.LoadEdgesAsync(command.ProjectId, token);
                var known = new Dictionary<string, TaskItem>(StringComparer.OrdinalIgnoreCase) { [command.ExternalKey.Trim()] = task };
                var linked = await upserter.ApplyDependenciesAsync(allowed.Value, task, keys, known, edges, token);
                if (!linked.IsSuccess)
                {
                    return linked;
                }
            }

            var saved = await upserter.SaveAsync(token);
            created = applied.Value.Created;
            taskId = task.Id;
            return saved;
        }, cancellationToken);

        if (!result.IsSuccess)
        {
            return result.Error;
        }

        var dto = await getTask.HandleAsync(new GetTaskQuery(command.ProjectId, taskId), cancellationToken);
        return dto.IsSuccess ? new UpsertTaskResult(dto.Value, created) : dto.Error;
    }
}
