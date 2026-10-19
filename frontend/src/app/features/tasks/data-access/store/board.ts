import { TASK_STATUSES, Task, TaskStatus } from '../tasks-api.service';

/** Board order: by column, then by position inside the column. */
export function compareTasks(a: Task, b: Task): number {
  return TASK_STATUSES.indexOf(a.status) - TASK_STATUSES.indexOf(b.status) || a.position - b.position;
}

export function groupByStatus(tasks: readonly Task[]): Record<TaskStatus, Task[]> {
  const columns = Object.fromEntries(TASK_STATUSES.map((status) => [status, [] as Task[]])) as Record<TaskStatus, Task[]>;
  for (const task of [...tasks].sort(compareTasks)) {
    columns[task.status].push(task);
  }
  return columns;
}

/**
 * The tasks whose status or position change when `taskId` is dropped at `position` of `status`, mirroring what
 * the API does: insert into the target column and renumber both columns from 0. Used for the optimistic update.
 */
export function moveTask(tasks: readonly Task[], taskId: string, status: TaskStatus, position: number): Task[] {
  const moved = tasks.find((task) => task.id === taskId);
  if (!moved) {
    return [];
  }

  const columns = groupByStatus(tasks.filter((task) => task.id !== taskId));
  const target = columns[status];
  target.splice(Math.min(Math.max(position, 0), target.length), 0, { ...moved, status });

  const affected = status === moved.status ? [target] : [target, columns[moved.status]];
  return affected.flatMap((column) =>
    column
      .map((task, index) => ({ task, index }))
      .filter(({ task, index }) => task.id === taskId || task.position !== index)
      .map(({ task, index }) => ({ ...task, position: index })),
  );
}
