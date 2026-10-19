import { groupByStatus, moveTask } from './board';
import { aTask } from './testing';

const tasks = [
  aTask({ id: 'a', status: 'ToDo', position: 0 }),
  aTask({ id: 'b', status: 'ToDo', position: 1 }),
  aTask({ id: 'c', status: 'ToDo', position: 2 }),
  aTask({ id: 'd', status: 'InProgress', position: 0 }),
];

function layout(changed: ReturnType<typeof moveTask>) {
  const byId = new Map(tasks.map((task) => [task.id, task]));
  changed.forEach((task) => byId.set(task.id, task));
  const columns = groupByStatus([...byId.values()]);
  return Object.fromEntries(Object.entries(columns).map(([status, column]) => [status, column.map((task) => task.id)]));
}

describe('moveTask', () => {
  it('moves a task to another column and renumbers both columns', () => {
    const changed = moveTask(tasks, 'a', 'InProgress', 0);

    expect(layout(changed)).toEqual({ ToDo: ['b', 'c'], InProgress: ['a', 'd'], Review: [], Done: [] });
    expect(changed.map((task) => [task.id, task.status, task.position])).toEqual([
      ['a', 'InProgress', 0],
      ['d', 'InProgress', 1],
      ['b', 'ToDo', 0],
      ['c', 'ToDo', 1],
    ]);
  });

  it('reorders within a column and only returns the tasks whose position changed', () => {
    const changed = moveTask(tasks, 'c', 'ToDo', 0);

    expect(layout(changed)['ToDo']).toEqual(['c', 'a', 'b']);
    expect(changed.map((task) => task.id).sort()).toEqual(['a', 'b', 'c']);
  });

  it('puts a task dropped past the end at the bottom', () => {
    const changed = moveTask(tasks, 'a', 'InProgress', 10);

    expect(layout(changed)['InProgress']).toEqual(['d', 'a']);
    expect(changed[0]).toEqual({ ...tasks[0], status: 'InProgress', position: 1 });
  });

  it('returns nothing for an unknown task', () => {
    expect(moveTask(tasks, 'zzz', 'Done', 0)).toEqual([]);
  });
});
