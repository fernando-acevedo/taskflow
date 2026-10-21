import { Params } from '@angular/router';
import { TASK_PRIORITIES, TASK_STATUSES, Task, TaskPriority, TaskStatus } from '../tasks-api.service';

/** The list view's filters. They live in the URL query string so a filtered list survives a reload or a shared link. */
export interface TaskFilters {
  status: TaskStatus | null;
  priority: TaskPriority | null;
  /** A user id, `me`, or `none` for unassigned tasks. */
  assignee: string | null;
  overdue: boolean;
  search: string;
}

export const NO_FILTERS: TaskFilters = { status: null, priority: null, assignee: null, overdue: false, search: '' };

export function hasActiveFilters(filters: TaskFilters): boolean {
  return (
    filters.status !== null ||
    filters.priority !== null ||
    filters.assignee !== null ||
    filters.overdue ||
    filters.search.trim() !== ''
  );
}

/** Narrows a query parameter (anything the user typed in the URL) to one of the allowed values. */
function oneOf<T extends string>(values: readonly T[], value: unknown): T | null {
  return values.find((allowed) => allowed === value) ?? null;
}

export function parseTaskFilters(params: Params): TaskFilters {
  const assignee: unknown = params['assignee'];
  return {
    status: oneOf(TASK_STATUSES, params['status']),
    priority: oneOf(TASK_PRIORITIES, params['priority']),
    assignee: typeof assignee === 'string' && assignee ? assignee : null,
    overdue: params['overdue'] === 'true',
    search: typeof params['q'] === 'string' ? params['q'] : '',
  };
}

/** Query params for `filters`, with null for the ones not set so that a merge removes them from the URL. */
export function toQueryParams(filters: TaskFilters): Params {
  return {
    status: filters.status,
    priority: filters.priority,
    assignee: filters.assignee,
    overdue: filters.overdue ? 'true' : null,
    q: filters.search.trim() || null,
  };
}

export function applyTaskFilters(tasks: readonly Task[], filters: TaskFilters, myId: string | null): Task[] {
  const search = filters.search.trim().toLocaleLowerCase();
  const assignee = filters.assignee === 'me' ? myId : filters.assignee;
  return tasks.filter(
    (task) =>
      (!filters.status || task.status === filters.status) &&
      (!filters.priority || task.priority === filters.priority) &&
      (!assignee || (assignee === 'none' ? task.assigneeId === null : task.assigneeId === assignee)) &&
      (!filters.overdue || task.isOverdue) &&
      (!search || task.title.toLocaleLowerCase().includes(search)),
  );
}
