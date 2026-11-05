import { TimeEntry } from '../time-tracking-api.service';

/** A time entry with sensible defaults for specs. */
export function anEntry(overrides: Partial<TimeEntry> & Pick<TimeEntry, 'id'>): TimeEntry {
  return {
    taskId: 't1',
    userId: 'u1',
    userName: 'Ana',
    date: '2026-11-03',
    minutes: 60,
    note: null,
    createdAt: '2026-11-03T10:00:00Z',
    ...overrides,
  };
}
