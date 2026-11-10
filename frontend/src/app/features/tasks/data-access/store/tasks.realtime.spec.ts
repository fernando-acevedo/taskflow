import { Actions } from '@ngrx/effects';
import { Action, Store } from '@ngrx/store';
import { Observable, of } from 'rxjs';
import { TaskDto } from '@core/api/types';
import { RealtimeActions } from '@core/realtime/realtime.actions';
import { TasksApiService } from '../tasks-api.service';
import { TasksApiActions, TasksPageActions } from './tasks.actions';
import { loadTasks, reloadAfterMove } from './tasks.effects';
import { initialTasksState, tasksFeature } from './tasks.feature';
import { aTask } from './testing';

const { reducer } = tasksFeature;

function loaded() {
  const opened = reducer(initialTasksState, TasksPageActions.opened({ projectId: 'p1' }));
  return reducer(
    opened,
    TasksApiActions.tasksLoaded({
      projectId: 'p1',
      tasks: [
        aTask({ id: 'a', status: 'ToDo', position: 0 }),
        aTask({ id: 'b', status: 'ToDo', position: 1 }),
        aTask({ id: 'c', status: 'Done', position: 0 }),
      ],
    }),
  );
}

const asDto = (task: ReturnType<typeof aTask>): TaskDto => task as unknown as TaskDto;

function run(effect: Observable<Action>): Action[] {
  const emitted: Action[] = [];
  effect.subscribe((action) => emitted.push(action));
  return emitted;
}

describe('tasks reducer: realtime events', () => {
  it('upserts a task that somebody else changed or created', () => {
    const state = reducer(loaded(), RealtimeActions.taskChanged({ projectId: 'p1', task: asDto(aTask({ id: 'a', title: 'Theirs' })) }));
    const created = reducer(state, RealtimeActions.taskChanged({ projectId: 'p1', task: asDto(aTask({ id: 'n', position: 5 })) }));

    expect(state.entities['a']?.title).toBe('Theirs');
    expect(created.ids).toContain('n');
  });

  it('removes a task somebody else deleted', () => {
    const state = reducer(loaded(), RealtimeActions.taskDeleted({ projectId: 'p1', taskId: 'a' }));

    expect(state.ids).not.toContain('a');
  });

  it('applies the new positions, statuses and versions of a reorder', () => {
    const state = reducer(
      loaded(),
      RealtimeActions.tasksReordered({
        projectId: 'p1',
        tasks: [
          { id: 'b', status: 'Done', position: 1, rowVersion: 'v2' },
          { id: 'a', status: 'ToDo', position: 0, rowVersion: 'v3' },
        ],
      }),
    );

    expect(state.entities['b']).toMatchObject({ status: 'Done', position: 1, rowVersion: 'v2' });
    expect(state.entities['a']?.rowVersion).toBe('v3');
  });

  it('ignores events of another project', () => {
    const before = loaded();

    expect(reducer(before, RealtimeActions.taskDeleted({ projectId: 'p2', taskId: 'a' }))).toBe(before);
  });

  describe('during an own move in flight', () => {
    const moving = () => reducer(loaded(), TasksPageActions.taskDropped({ taskId: 'a', status: 'Done', position: 0 }));
    const pending = (resyncAfterMove: boolean) =>
      ({ select: (selector: unknown) => of(selector === tasksFeature.selectResyncAfterMove ? resyncAfterMove : 'p1') }) as unknown as Store;
    const moved = of(TasksApiActions.taskMoved({ result: { task: aTask({ id: 'a' }), reordered: [] } })) as Actions;

    it('holds back a reorder by somebody else instead of overwriting the optimistic cards', () => {
      const before = moving();
      const state = reducer(
        before,
        RealtimeActions.tasksReordered({ projectId: 'p1', tasks: [{ id: 'a', status: 'ToDo', position: 9, rowVersion: 'x' }] }),
      );

      expect(state.entities['a']).toEqual(before.entities['a']);
      expect(state.moveUndo).toBe(before.moveUndo);
      expect(state.resyncAfterMove).toBe(true);
    });

    it('holds back a change to a task the move touched, but applies a new task', () => {
      const held = reducer(moving(), RealtimeActions.taskChanged({ projectId: 'p1', task: asDto(aTask({ id: 'a', title: 'Theirs' })) }));
      const other = reducer(moving(), RealtimeActions.taskChanged({ projectId: 'p1', task: asDto(aTask({ id: 'n', title: 'Theirs' })) }));

      expect(held.entities['a']?.title).toBe('A');
      expect(held.resyncAfterMove).toBe(true);
      expect(other.entities['n']?.title).toBe('Theirs');
    });

    it('does not let a reload undo the optimistic card, and asks for another one after', () => {
      const state = reducer(moving(), TasksApiActions.tasksLoaded({ projectId: 'p1', tasks: [aTask({ id: 'a', status: 'ToDo' })] }));

      expect(state.entities['a']?.status).toBe('Done');
      expect(state.resyncAfterMove).toBe(true);
    });

    it('reloads when the move settles, and clears the flag when the load lands', () => {
      const held = reducer(moving(), RealtimeActions.tasksReordered({ projectId: 'p1', tasks: [] }));
      const settled = reducer(held, TasksApiActions.taskMoved({ result: { task: aTask({ id: 'a', status: 'Done' }), reordered: [] } }));

      expect(settled.resyncAfterMove).toBe(true);
      expect(run(reloadAfterMove(moved, pending(settled.resyncAfterMove)))).toEqual([
        RealtimeActions.resyncRequested({ projectId: 'p1' }),
      ]);
      expect(reducer(settled, TasksApiActions.tasksLoaded({ projectId: 'p1', tasks: [] })).resyncAfterMove).toBe(false);
    });

    it('does not reload after a move when nothing was held back', () => {
      expect(run(reloadAfterMove(moved, pending(false)))).toEqual([]);
    });
  });
});

describe('loadTasks on a resync', () => {
  it('reloads the tasks of the project the event names', () => {
    const tasks = [aTask({ id: 'a' })];
    const api = { list: vi.fn(() => of(tasks)) } as unknown as TasksApiService;
    const store = { select: () => of('p1') } as unknown as Store;
    const actions$ = of(RealtimeActions.resyncRequested({ projectId: 'p1' })) as Actions;

    expect(run(loadTasks(actions$, store, api))).toEqual([TasksApiActions.tasksLoaded({ projectId: 'p1', tasks })]);
  });
});
