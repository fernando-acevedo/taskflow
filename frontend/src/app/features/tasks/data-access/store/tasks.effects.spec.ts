import { Actions } from '@ngrx/effects';
import { Action, Store } from '@ngrx/store';
import { Observable, of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { TasksApiService } from '../tasks-api.service';
import { TasksApiActions, TasksPageActions } from './tasks.actions';
import { Notifier } from '../../../../core/notifications/notifier';
import { createTask, loadTasks, moveTask, notify, refreshTask, reloadAfterStaleMove, updateTask } from './tasks.effects';
import { tasksFeature } from './tasks.feature';
import { aTask } from './testing';

/** A store whose selectors answer with fixed values. */
function storeWith(values: Map<unknown, unknown>): Store {
  return { select: (selector: unknown) => of(values.get(selector)) } as unknown as Store;
}

function run(effect: Observable<Action>): Action[] {
  const emitted: Action[] = [];
  effect.subscribe((action) => emitted.push(action));
  return emitted;
}

const input = { title: 'T', description: null, priority: 'Medium', assigneeId: null, dueDate: null, labelIds: [], estimatedMinutes: null, startDate: null, isBlocked: false, statusNote: null } as const;

describe('tasks effects', () => {
  it('moves with the version from before the optimistic drop and reports the stored task', () => {
    const before = aTask({ id: 'a', status: 'ToDo', rowVersion: 'v-before' });
    const after = aTask({ id: 'a', status: 'Done', rowVersion: 'v-after' });
    const result = { task: after, reordered: [{ id: 'b', status: 'ToDo' as const, position: 0, rowVersion: 'v-b' }] };
    const api = { move: vi.fn(() => of(result)) } as unknown as TasksApiService;
    const actions$ = of(TasksPageActions.taskDropped({ taskId: 'a', status: 'Done', position: 0 })) as Actions;

    const emitted = run(moveTask(actions$, storeWith(new Map([[tasksFeature.selectMoveUndo, [before]]])), api));

    expect(api.move).toHaveBeenCalledWith(before, 'Done', 0);
    expect(emitted).toEqual([TasksApiActions.taskMoved({ result })]);
  });

  it('reports a failed move so the reducer puts the card back', () => {
    const api = {
      move: vi.fn(() => throwError(() => new HttpErrorResponse({ status: 409, error: { code: 'Concurrency.Stale' } }))),
    } as unknown as TasksApiService;
    const actions$ = of(TasksPageActions.taskDropped({ taskId: 'a', status: 'Done', position: 0 })) as Actions;

    const emitted = run(moveTask(actions$, storeWith(new Map([[tasksFeature.selectMoveUndo, [aTask({ id: 'a' })]]])), api));

    expect(emitted).toEqual([TasksApiActions.taskMoveFailed({ error: { code: 'Concurrency.Stale', fields: {} } })]);
  });

  it('does not reload the tasks after a move or a failed move, only after a label change', () => {
    const api = { list: vi.fn(() => of([])) } as unknown as TasksApiService;
    const store = storeWith(new Map([[tasksFeature.selectProjectId, 'p1']]));
    const moved = { task: aTask({ id: 'a' }), reordered: [] };

    run(loadTasks(of(TasksApiActions.taskMoved({ result: moved }), TasksApiActions.taskMoveFailed({ error: { code: 'unexpected', fields: {} } })) as Actions, store, api));

    expect(api.list).not.toHaveBeenCalled();
  });

  it('reloads the tasks when a move fails because the task is stale, and only then', () => {
    const api = { list: vi.fn(() => of([aTask({ id: 'a' })])) } as unknown as TasksApiService;
    const store = storeWith(new Map([[tasksFeature.selectProjectId, 'p1']]));
    const stale = { code: 'Concurrency.Stale', fields: {} };

    const other = run(reloadAfterStaleMove(of(TasksApiActions.taskMoveFailed({ error: { code: 'Tasks.NotFound', fields: {} } })) as Actions, store, api));
    expect(other).toEqual([]);
    expect(api.list).not.toHaveBeenCalled();

    const emitted = run(reloadAfterStaleMove(of(TasksApiActions.taskMoveFailed({ error: stale })) as Actions, store, api));
    expect(api.list).toHaveBeenCalledWith('p1');
    expect(emitted).toEqual([TasksApiActions.tasksLoaded({ projectId: 'p1', tasks: [aTask({ id: 'a' })] })]);
  });

  it("fetches the server's task for the dialog and answers with its submission id", () => {
    const fresh = aTask({ id: 'a', rowVersion: 'v-fresh' });
    const api = { get: vi.fn(() => of(fresh)) } as unknown as TasksApiService;
    const actions$ = of(TasksPageActions.refreshRequested({ taskId: 'a', submissionId: 9 })) as Actions;

    const emitted = run(refreshTask(actions$, storeWith(new Map([[tasksFeature.selectProjectId, 'p1']])), api));

    expect(api.get).toHaveBeenCalledWith('p1', 'a');
    expect(emitted).toEqual([TasksApiActions.taskRefreshed({ task: fresh, submissionId: 9 })]);
  });

  describe('notify', () => {
    const stale = { code: 'Concurrency.Stale', fields: {} };
    const other = { code: 'Tasks.NotFound', fields: {} };

    function notifications(action: Action) {
      const notifier = { error: vi.fn(), warning: vi.fn(), success: vi.fn() };
      run(notify(of<Action>(action), notifier as unknown as Notifier));
      return notifier;
    }

    it('shows the specific message when a move fails as stale, and the generic error otherwise', () => {
      const staleMove = notifications(TasksApiActions.taskMoveFailed({ error: stale }));
      expect(staleMove.warning).toHaveBeenCalledWith('tasks.moveStale');
      expect(staleMove.error).not.toHaveBeenCalled();

      const otherMove = notifications(TasksApiActions.taskMoveFailed({ error: other }));
      expect(otherMove.error).toHaveBeenCalledWith(other);
    });

    it('leaves a stale save to the dialog that submitted it, but not one without a dialog', () => {
      expect(notifications(TasksApiActions.taskSaveFailed({ error: stale, submissionId: 1 })).error).not.toHaveBeenCalled();
      expect(notifications(TasksApiActions.taskSaveFailed({ error: stale })).error).toHaveBeenCalledWith(stale);
      expect(notifications(TasksApiActions.taskSaveFailed({ error: other, submissionId: 1 })).error).toHaveBeenCalledWith(other);
    });
  });

  it('sends the version the dialog opened with, not the one a background reload put in the store', () => {
    const api = { update: vi.fn(() => of(aTask({ id: 'a' }))) } as unknown as TasksApiService;
    const actions$ = of(TasksPageActions.updateSubmitted({ taskId: 'a', changes: input, rowVersion: 'rv-opened', submissionId: 12 })) as Actions;
    const store = storeWith(new Map([[tasksFeature.selectEntities, { a: aTask({ id: 'a', rowVersion: 'rv-theirs' }) }]]));

    run(updateTask(actions$, store, api));

    expect(api.update).toHaveBeenCalledWith(expect.objectContaining({ id: 'a', rowVersion: 'rv-opened' }), input);
  });

  it("answers an update of a task that's gone without calling the API, so the form gets its outcome", () => {
    const api = { update: vi.fn() } as unknown as TasksApiService;
    const actions$ = of(TasksPageActions.updateSubmitted({ taskId: 'gone', changes: input, rowVersion: 'rv1', submissionId: 4 })) as Actions;

    const emitted = run(updateTask(actions$, storeWith(new Map([[tasksFeature.selectEntities, {}]])), api));

    expect(api.update).not.toHaveBeenCalled();
    expect(emitted).toEqual([
      TasksApiActions.taskSaveFailed({ error: { code: 'Tasks.NotFound', fields: {} }, submissionId: 4 }),
    ]);
  });

  it('does not create a task without an open project', () => {
    const api = { create: vi.fn() } as unknown as TasksApiService;
    const actions$ = of(TasksPageActions.createSubmitted({ task: input, submissionId: 5 })) as Actions;

    const emitted = run(createTask(actions$, storeWith(new Map([[tasksFeature.selectProjectId, null]])), api));

    expect(api.create).not.toHaveBeenCalled();
    expect(emitted).toEqual([TasksApiActions.taskSaveFailed({ error: { code: 'unexpected', fields: {} }, submissionId: 5 })]);
  });

  it('passes the submission id on so the dialog can tell its own save apart', () => {
    const created = aTask({ id: 'n' });
    const api = { create: vi.fn(() => of(created)) } as unknown as TasksApiService;
    const actions$ = of(TasksPageActions.createSubmitted({ task: input, submissionId: 6 })) as Actions;

    const emitted = run(createTask(actions$, storeWith(new Map([[tasksFeature.selectProjectId, 'p1']])), api));

    expect(api.create).toHaveBeenCalledWith('p1', input);
    expect(emitted).toEqual([TasksApiActions.taskSaved({ task: created, message: 'tasks.created', submissionId: 6 })]);
  });
});
