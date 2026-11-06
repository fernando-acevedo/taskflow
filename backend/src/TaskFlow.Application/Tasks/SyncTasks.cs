using FluentValidation;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;
using TaskFlow.Application.Projects;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Tasks;

public sealed record SyncTaskItem(string ExternalKey, TaskUpsertFields Fields);

public sealed record SyncTasksCommand(Guid ProjectId, IReadOnlyList<SyncTaskItem> Items);

public enum SyncOutcome
{
    Created,
    Updated,
    Failed,

    /// <summary>The item was fine, but another one failed and the whole sync was rolled back.</summary>
    NotApplied,
}

/// <param name="ErrorCode">The error code (as in <c>extensions.code</c> elsewhere) when <paramref name="Outcome"/> is Failed.</param>
/// <param name="Message">English text of the error; use the code to react to it.</param>
public sealed record SyncItemResult(string ExternalKey, SyncOutcome Outcome, Guid? TaskId, string? ErrorCode, string? Message);

/// <param name="Applied">False when some item failed: nothing was saved.</param>
public sealed record SyncTasksResult(bool Applied, IReadOnlyList<SyncItemResult> Items);

/// <summary>
/// Creates or updates up to <see cref="MaxItems"/> tasks by external key in one serializable transaction (ADR 0017). It is
/// all or nothing: if any item fails, nothing is saved and every item reports its own outcome. Dependencies are external keys of
/// tasks in the same request or already in the project, so the request may list a task before the ones it depends on.
/// </summary>
public sealed class SyncTasksHandler(
    ProjectAccess access,
    IValidator<TaskUpsertFields> validator,
    TaskUpserter upserter,
    ITransactionRunner transactions)
{
    public const int MaxItems = 200;

    public async Task<Result<SyncTasksResult>> HandleAsync(SyncTasksCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Items.Count == 0)
        {
            return TaskSyncErrors.SyncEmpty;
        }

        if (command.Items.Count > MaxItems)
        {
            return TaskSyncErrors.SyncTooLarge;
        }

        SyncTasksResult? outcome = null;
        var result = await transactions.RunSerializableAsync(async token =>
        {
            var allowed = await access.GetAsync(command.ProjectId, ProjectAccessLevel.Work, token);
            if (!allowed.IsSuccess)
            {
                return allowed;
            }

            var project = allowed.Value;
            var results = new SyncItemResult?[command.Items.Count];
            var tasks = new Dictionary<string, TaskItem>(StringComparer.OrdinalIgnoreCase);
            var created = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var failed = false;

            // Pass 1: the tasks themselves. Each is saved before the next so that positions in a column see the earlier ones;
            // after a failure nothing more is saved (the transaction will be rolled back anyway).
            for (var index = 0; index < command.Items.Count; index++)
            {
                var item = command.Items[index];
                var error = await ApplyItemAsync(project, item, tasks, created, save: !failed, token);
                if (error is null)
                {
                    var key = item.ExternalKey.Trim();
                    results[index] = new SyncItemResult(
                        item.ExternalKey, created.Contains(key) ? SyncOutcome.Created : SyncOutcome.Updated, tasks[key].Id, null, null);
                    continue;
                }

                failed = true;
                results[index] = Failure(item.ExternalKey, error);
            }

            // Pass 2: dependencies, once every task of the request exists.
            var edges = await upserter.LoadEdgesAsync(project.Id, token);
            for (var index = 0; index < command.Items.Count; index++)
            {
                var item = command.Items[index];
                if (results[index]!.Outcome == SyncOutcome.Failed || item.Fields.DependsOn is not { } keys)
                {
                    continue;
                }

                var linked = await upserter.ApplyDependenciesAsync(project, tasks[item.ExternalKey.Trim()], keys, tasks, edges, token);
                if (!linked.IsSuccess)
                {
                    failed = true;
                    results[index] = Failure(item.ExternalKey, linked.Error);
                }
            }

            outcome = Finish(results, applied: !failed);
            if (failed)
            {
                return TaskSyncErrors.NotApplied;
            }

            return await upserter.SaveAsync(token);
        }, cancellationToken);

        if (outcome is not null && !outcome.Applied)
        {
            return outcome;
        }

        if (!result.IsSuccess)
        {
            return result.Error;
        }

        return outcome ?? throw new InvalidOperationException("The sync finished without a result.");
    }

    private async Task<Error?> ApplyItemAsync(Project project, SyncTaskItem item, Dictionary<string, TaskItem> tasks, HashSet<string> created, bool save, CancellationToken token)
    {
        var validation = await validator.ValidateAsync(item.Fields, token);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        if (string.IsNullOrWhiteSpace(item.ExternalKey))
        {
            return TaskErrors.ExternalKeyInvalid;
        }

        var key = item.ExternalKey.Trim();
        if (tasks.ContainsKey(key))
        {
            return TaskSyncErrors.DuplicateExternalKey;
        }

        var applied = await upserter.ApplyAsync(project, key, item.Fields, token);
        if (!applied.IsSuccess)
        {
            return applied.Error;
        }

        tasks[key] = applied.Value.Task;
        if (save)
        {
            var saved = await upserter.SaveAsync(token);
            if (!saved.IsSuccess)
            {
                tasks.Remove(key);
                return saved.Error;
            }
        }

        if (applied.Value.Created)
        {
            created.Add(key);
        }

        return null;
    }

    private static SyncTasksResult Finish(SyncItemResult?[] results, bool applied) =>
        new(
            applied,
            results.Select(result => !applied && result!.Outcome != SyncOutcome.Failed ? result with { Outcome = SyncOutcome.NotApplied } : result!).ToList());

    private static SyncItemResult Failure(string key, Error error)
    {
        var message = error.Type == ErrorType.Validation && error.ValidationErrors is { } fields
            ? string.Join(" ", fields.SelectMany(field => field.Value.Select(text => $"{field.Key}: {text}")))
            : error.Description;
        return new SyncItemResult(key, SyncOutcome.Failed, null, error.Code, message);
    }
}
