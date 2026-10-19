import { ShellActions } from '../../../../core/auth/store/auth.actions';
import { TasksApiActions, TasksPageActions } from './tasks.actions';
import { initialTasksState, tasksFeature } from './tasks.feature';
import { aTask } from './testing';

const { reducer } = tasksFeature;
const tasks = [
  aTask({ id: 'a', status: 'ToDo', position: 0 }),
  aTask({ id: 'b', status: 'ToDo', position: 1 }),
  aTask({ id: 'c', status: 'Done', position: 0 }),
];

function loaded() {
  const opened = reducer(initialTasksState, TasksPageActions.opened({ projectId: 'p1' }));
  return reducer(opened, TasksApiActions.tasksLoaded({ projectId: 'p1', tasks }));
}

function column(state: ReturnType<typeof loaded>, status: string) {
  return Object.values(state.entities)
    .filter((task) => task?.status === status)
    .sort((x, y) => (x?.position ?? 0) - (y?.position ?? 0))
    .map((task) => task?.id);
}

describe('tasksFeature', () => {
  it('moves the card as soon as it is dropped and remembers how to undo it', () => {
    const state = reducer(loaded(), TasksPageActions.taskDropped({ taskId: 'a', status: 'Done', position: 1 }));

    expect(column(state, 'ToDo')).toEqual(['b']);
    expect(column(state, 'Done')).toEqual(['c', 'a']);
    expect(state.moveUndo?.map((task) => task.id).sort()).toEqual(['a', 'b']);
    expect(tasksFeature.selectMoving.projector(state.moveUndo)).toBe(true);
  });

  it('puts the cards back when the API rejects the move', () => {
    const dropped = reducer(loaded(), TasksPageActions.taskDropped({ taskId: 'a', status: 'Done', position: 1 }));

    const state = reducer(dropped, TasksApiActions.taskMoveFailed({ error: { code: 'Concurrency.Stale', fields: {} } }));

    expect(column(state, 'ToDo')).toEqual(['a', 'b']);
    expect(column(state, 'Done')).toEqual(['c']);
    expect(state.moveUndo).toBeNull();
  });

  it('keeps the server version of the moved task when the API accepts it', () => {
    const dropped = reducer(loaded(), TasksPageActions.taskDropped({ taskId: 'a', status: 'Done', position: 0 }));
    const fromServer = { ...tasks[0], status: 'Done' as const, position: 0, rowVersion: 'v-new', completedAt: '2026-10-19T13:00:00Z' };

    const state = reducer(dropped, TasksApiActions.taskMoved({ task: fromServer }));

    expect(state.entities['a']).toEqual(fromServer);
    expect(state.moveUndo).toBeNull();
  });

  it('ignores tasks that arrive for a project that is no longer open', () => {
    const switched = reducer(loaded(), TasksPageActions.opened({ projectId: 'p2' }));

    const state = reducer(switched, TasksApiActions.tasksLoaded({ projectId: 'p1', tasks }));

    expect(state.ids).toEqual([]);
    expect(state.loading).toBe(true);
  });

  it('forgets every task on sign-out', () => {
    expect(reducer(loaded(), ShellActions.logoutClicked())).toEqual(initialTasksState);
  });
});
