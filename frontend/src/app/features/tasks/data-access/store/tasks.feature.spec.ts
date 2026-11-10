import { ShellActions } from '@core/auth/store/auth.actions';
import { RealtimeActions } from '@core/realtime/realtime.actions';
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

    const state = reducer(
      dropped,
      TasksApiActions.taskMoved({
        result: { task: fromServer, reordered: [{ id: 'b', status: 'ToDo', position: 0, rowVersion: 'v-b-new' }] },
      }),
    );

    expect(state.entities['a']).toEqual(fromServer);
    expect(state.entities['b']).toMatchObject({ position: 0, rowVersion: 'v-b-new' });
    expect(state.moveUndo).toBeNull();
  });

  it('applies the neighbors renumbered by the API: status, position and version', () => {
    const dropped = reducer(loaded(), TasksPageActions.taskDropped({ taskId: 'a', status: 'Done', position: 0 }));
    const moved = { ...tasks[0], status: 'Done' as const, position: 0, rowVersion: 'v-a-new' };

    const state = reducer(
      dropped,
      TasksApiActions.taskMoved({
        result: {
          task: moved,
          reordered: [
            { id: 'c', status: 'Done', position: 1, rowVersion: 'v-c-new' },
            { id: 'b', status: 'ToDo', position: 0, rowVersion: 'v-b-new' },
          ],
        },
      }),
    );

    expect(column(state, 'Done')).toEqual(['a', 'c']);
    expect(state.entities['c']?.rowVersion).toBe('v-c-new');
    expect(state.entities['b']?.rowVersion).toBe('v-b-new');
  });

  it('stores the server version of a task that was refreshed after a stale save', () => {
    const state = reducer(loaded(), TasksApiActions.taskRefreshed({ task: { ...tasks[0], title: 'Fresh', rowVersion: 'v-fresh' } }));

    expect(state.entities['a']).toMatchObject({ title: 'Fresh', rowVersion: 'v-fresh' });
  });

  it('ignores tasks that arrive for a project that is no longer open', () => {
    const switched = reducer(loaded(), TasksPageActions.opened({ projectId: 'p2' }));

    const state = reducer(switched, TasksApiActions.tasksLoaded({ projectId: 'p1', tasks }));

    expect(state.ids).toEqual([]);
    expect(state.loading).toBe(true);
  });

  it('keeps the tasks of the same project on screen while it reloads, and replaces them on arrival', () => {
    const reopened = reducer(loaded(), TasksPageActions.opened({ projectId: 'p1' }));

    expect(reopened.ids).toHaveLength(tasks.length);
    expect(reopened.refreshing).toBe(true);
    expect(reopened.loading).toBe(false);

    const state = reducer(reopened, TasksApiActions.tasksLoaded({ projectId: 'p1', tasks: [tasks[0]] }));

    expect(state.ids).toEqual(['a']);
    expect(state.refreshing).toBe(false);
  });

  it('empties the board and shows loading when another project opens', () => {
    const state = reducer(loaded(), TasksPageActions.opened({ projectId: 'p2' }));

    expect(state.ids).toEqual([]);
    expect(state.loading).toBe(true);
    expect(state.refreshing).toBe(false);
  });

  it('keeps the tasks of the same project on screen while it reloads, and replaces them on arrival', () => {
    const reopened = reducer(loaded(), TasksPageActions.opened({ projectId: 'p1' }));

    expect(reopened.ids).toHaveLength(tasks.length);
    expect(reopened.refreshing).toBe(true);
    expect(reopened.loading).toBe(false);

    const state = reducer(reopened, TasksApiActions.tasksLoaded({ projectId: 'p1', tasks: [tasks[0]] }));

    expect(state.ids).toEqual(['a']);
    expect(state.refreshing).toBe(false);
  });

  it('empties the board and shows loading when another project opens', () => {
    const state = reducer(loaded(), TasksPageActions.opened({ projectId: 'p2' }));

    expect(state.ids).toEqual([]);
    expect(state.loading).toBe(true);
    expect(state.refreshing).toBe(false);
  });

  it('does not bring back a task deleted by someone else when an own move is undone', () => {
    const dropped = reducer(loaded(), TasksPageActions.taskDropped({ taskId: 'a', status: 'Done', position: 1 }));

    const deleted = reducer(dropped, RealtimeActions.taskDeleted({ projectId: 'p1', taskId: 'b' }));
    expect(deleted.resyncAfterMove).toBe(true);
    const state = reducer(deleted, TasksApiActions.taskMoveFailed({ error: { code: 'Concurrency.Stale', fields: {} } }));

    expect(state.entities['b']).toBeUndefined();
    expect(column(state, 'ToDo')).toEqual(['a']);
  });

  it('does not ask for a reload when a task is deleted outside any move', () => {
    const state = reducer(loaded(), RealtimeActions.taskDeleted({ projectId: 'p1', taskId: 'b' }));

    expect(state.entities['b']).toBeUndefined();
    expect(state.resyncAfterMove).toBe(false);
  });

  it('forgets every task on sign-out', () => {
    expect(reducer(loaded(), ShellActions.logoutClicked())).toEqual(initialTasksState);
  });
});
