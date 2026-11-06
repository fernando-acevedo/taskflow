import { Task } from '../tasks-api.service';

/** A task with sensible defaults for specs. */
export function aTask(overrides: Partial<Task> & Pick<Task, 'id'>): Task {
  return {
    projectId: 'p1',
    title: overrides.id.toUpperCase(),
    description: null,
    status: 'ToDo',
    priority: 'Medium',
    assigneeId: null,
    dueDate: null,
    labels: [],
    estimatedMinutes: null,
    isOverdue: false,
    position: 0,
    createdById: 'u1',
    createdAt: '2026-10-19T12:00:00Z',
    updatedAt: '2026-10-19T12:00:00Z',
    completedAt: null,
    rowVersion: `v-${overrides.id}`,
    externalKey: null,
    startDate: null,
    isBlocked: false,
    statusNote: null,
    dependsOn: [],
    ...overrides,
  };
}
