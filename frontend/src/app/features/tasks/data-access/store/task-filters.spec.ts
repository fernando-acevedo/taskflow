import { NO_FILTERS, applyTaskFilters, hasActiveFilters, parseTaskFilters, toQueryParams } from './task-filters';
import { aTask } from './testing';

const tasks = [
  aTask({ id: 'mine', title: 'Write copy', assigneeId: 'me-id', priority: 'High', status: 'Review' }),
  aTask({ id: 'theirs', title: 'Design logo', assigneeId: 'other-id', isOverdue: true }),
  aTask({ id: 'nobody', title: 'Copy review', priority: 'High' }),
];

describe('task filters', () => {
  it('reads filters from the query string and ignores unknown values', () => {
    expect(parseTaskFilters({ status: 'Review', priority: 'Urgent', assignee: 'me', overdue: 'true', q: 'copy' })).toEqual({
      status: 'Review',
      priority: null,
      assignee: 'me',
      overdue: true,
      search: 'copy',
    });
    expect(parseTaskFilters({})).toEqual(NO_FILTERS);
  });

  it('writes nulls for unset filters so a merge removes them from the URL', () => {
    expect(toQueryParams({ ...NO_FILTERS, priority: 'High', search: '  ' })).toEqual({
      status: null,
      priority: 'High',
      assignee: null,
      overdue: null,
      q: null,
    });
  });

  it('combines filters, resolving "me" to the signed-in user', () => {
    const ids = (filters: Partial<typeof NO_FILTERS>) =>
      applyTaskFilters(tasks, { ...NO_FILTERS, ...filters }, 'me-id').map((task) => task.id);

    expect(ids({})).toEqual(['mine', 'theirs', 'nobody']);
    expect(ids({ assignee: 'me' })).toEqual(['mine']);
    expect(ids({ assignee: 'none' })).toEqual(['nobody']);
    expect(ids({ priority: 'High', search: 'COPY' })).toEqual(['mine', 'nobody']);
    expect(ids({ overdue: true })).toEqual(['theirs']);
    expect(ids({ status: 'Review', assignee: 'other-id' })).toEqual([]);
  });

  it('knows when any filter is active', () => {
    expect(hasActiveFilters(NO_FILTERS)).toBe(false);
    expect(hasActiveFilters({ ...NO_FILTERS, overdue: true })).toBe(true);
  });
});
