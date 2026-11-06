import { ActivityEntry } from '../activity-api.service';

/** An activity entry with sensible defaults for specs. */
export function anEntry(overrides: Partial<ActivityEntry> & Pick<ActivityEntry, 'id'>): ActivityEntry {
  return {
    taskId: 't1',
    actorId: 'u1',
    actorName: 'Ana',
    type: 'TaskCreated',
    occurredAt: '2026-11-01T10:00:00Z',
    changes: { taskTitle: 'Write docs' },
    ...overrides,
  };
}
