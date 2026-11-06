using TaskFlow.Domain.Tenancy;

namespace TaskFlow.Domain.Tasks;

/// <summary>
/// "<see cref="TaskItemId"/> depends on <see cref="DependsOnTaskId"/>": the second one should finish first. Both tasks
/// belong to the same project. Created through <see cref="TaskItem.SetDependencies"/>, which keeps the graph acyclic.
/// </summary>
public sealed class TaskDependency : ITenantOwned
{
    internal TaskDependency(Guid taskItemId, Guid dependsOnTaskId)
    {
        TaskItemId = taskItemId;
        DependsOnTaskId = dependsOnTaskId;
    }

    public Guid TaskItemId { get; private set; }

    public Guid DependsOnTaskId { get; private set; }

    /// <summary>
    /// Whether making <paramref name="taskId"/> depend on <paramref name="dependsOnIds"/> would close a loop, given the
    /// other edges of the project as (task, depends on) pairs. The edges that start at <paramref name="taskId"/> must not be in
    /// <paramref name="otherEdges"/>: they are the ones being replaced.
    /// </summary>
    public static bool CreatesCycle(Guid taskId, IEnumerable<Guid> dependsOnIds, IEnumerable<(Guid TaskId, Guid DependsOnId)> otherEdges)
    {
        ArgumentNullException.ThrowIfNull(dependsOnIds);
        ArgumentNullException.ThrowIfNull(otherEdges);

        var next = otherEdges.ToLookup(edge => edge.TaskId, edge => edge.DependsOnId);
        var seen = new HashSet<Guid>();
        var pending = new Stack<Guid>(dependsOnIds);
        while (pending.TryPop(out var current))
        {
            if (current == taskId)
            {
                return true;
            }

            if (seen.Add(current))
            {
                foreach (var onward in next[current])
                {
                    pending.Push(onward);
                }
            }
        }

        return false;
    }
}
