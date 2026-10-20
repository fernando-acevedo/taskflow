using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Tasks;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Dashboard;

public sealed record MyTaskDto(
    Guid Id,
    Guid ProjectId,
    string ProjectName,
    string ClientName,
    string Title,
    TaskItemStatus Status,
    TaskPriority Priority,
    DateOnly? DueDate,
    bool IsOverdue);

public sealed record ClientSummary(Guid ClientId, string ClientName, int ActiveProjects, int OpenTasks, int OverdueTasks);

/// <summary>
/// The home page: the user's open tasks in the active projects they belong to, and per client the active projects, open tasks
/// and overdue tasks the user can see (their projects; every project for admins).
/// </summary>
/// <param name="Tasks">The first <see cref="GetMyDashboardHandler.TaskLimit"/> open tasks, overdue first, then by due date.</param>
public sealed record MyDashboardDto(
    int OpenTasks,
    int OverdueTasks,
    int DueThisWeek,
    IReadOnlyList<MyTaskDto> Tasks,
    IReadOnlyList<ClientSummary> Clients);

public sealed class GetMyDashboardHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider time)
{
    public const int TaskLimit = 50;

    public async Task<Result<MyDashboardDto>> HandleAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.Id;
        var today = time.Today();
        var weekEnd = today.AddDays(6);

        var activeProjects = db.Projects.Where(project => !project.IsArchived);
        var visibleProjects = currentUser.IsAdmin
            ? activeProjects
            : activeProjects.Where(project => project.Members.Any(member => member.UserId == userId));

        var myTasks =
            from task in db.Tasks
            join project in activeProjects on task.ProjectId equals project.Id
            join client in db.Clients on project.ClientId equals client.Id
            where task.AssigneeId == userId
                && task.Status != TaskItemStatus.Done
                && project.Members.Any(member => member.UserId == userId)
            select new { task, project, client };

        var openCount = await myTasks.CountAsync(cancellationToken);
        var overdueCount = await myTasks.CountAsync(row => row.task.DueDate < today, cancellationToken);
        var dueThisWeek = await myTasks.CountAsync(
            row => row.task.DueDate >= today && row.task.DueDate <= weekEnd, cancellationToken);

        var tasks = await myTasks
            .OrderBy(row => row.task.DueDate == null)
            .ThenBy(row => row.task.DueDate)
            .ThenByDescending(row => row.task.Priority)
            .ThenBy(row => row.task.Title)
            .Take(TaskLimit)
            .Select(row => new MyTaskDto(
                row.task.Id,
                row.project.Id,
                row.project.Name,
                row.client.Name,
                row.task.Title,
                row.task.Status,
                row.task.Priority,
                row.task.DueDate,
                row.task.DueDate < today))
            .ToListAsync(cancellationToken);

        var projectsByClient = await visibleProjects
            .GroupBy(project => project.ClientId)
            .Select(group => new { ClientId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var tasksByClient = await (
                from task in db.Tasks
                join project in visibleProjects on task.ProjectId equals project.Id
                where task.Status != TaskItemStatus.Done
                group task by project.ClientId into tasksOfClient
                select new
                {
                    ClientId = tasksOfClient.Key,
                    Open = tasksOfClient.Count(),
                    Overdue = tasksOfClient.Count(task => task.DueDate < today),
                })
            .ToDictionaryAsync(row => row.ClientId, cancellationToken);

        var clientIds = projectsByClient.Select(row => row.ClientId).ToList();
        var clientNames = await db.Clients
            .Where(client => clientIds.Contains(client.Id))
            .ToDictionaryAsync(client => client.Id, client => client.Name, cancellationToken);

        var clients = projectsByClient
            .Select(row =>
            {
                var taskCounts = tasksByClient.GetValueOrDefault(row.ClientId);
                return new ClientSummary(
                    row.ClientId,
                    clientNames[row.ClientId],
                    row.Count,
                    taskCounts?.Open ?? 0,
                    taskCounts?.Overdue ?? 0);
            })
            .OrderByDescending(client => client.OverdueTasks)
            .ThenByDescending(client => client.OpenTasks)
            .ThenBy(client => client.ClientName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new MyDashboardDto(openCount, overdueCount, dueThisWeek, tasks, clients);
    }
}
