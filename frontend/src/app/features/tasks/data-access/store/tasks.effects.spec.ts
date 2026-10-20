import { Actions } from '@ngrx/effects';
import { Action, Store } from '@ngrx/store';
import { Observable, of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { TasksApiService } from '../tasks-api.service';
import { TasksApiActions, TasksPageActions } from './tasks.actions';
import { createTask, moveTask, updateTask } from './tasks.effects';
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

const input = { title: 'T', description: null, priority: 'Medium', assigneeId: null, dueDate: null } as const;

describe('tasks effects', () => {
  it('moves with the version from before the optimistic drop and reports the stored task', () => {
    const before = aTask({ id: 'a', status: 'ToDo', rowVersion: 'v-before' });
    const after = aTask({ id: 'a', status: 'Done', rowVersion: 'v-after' });
    const api = { move: vi.fn(() => of(after)) } as unknown as TasksApiService;
    const actions$ = of(TasksPageActions.taskDropped({ taskId: 'a', status: 'Done', position: 0 })) as Actions;

    const emitted = run(moveTask(actions$, storeWith(new Map([[tasksFeature.selectMoveUndo, [before]]])), api));

    expect(api.move).toHaveBeenCalledWith(before, 'Done', 0);
    expect(emitted).toEqual([TasksApiActions.taskMoved({ task: after })]);
  });

  it('reports a failed move so the reducer puts the card back', () => {
    const api = {
      move: vi.fn(() => throwError(() => new HttpErrorResponse({ status: 409, error: { code: 'Concurrency.Stale' } }))),
    } as unknown as TasksApiService;
    const actions$ = of(TasksPageActions.taskDropped({ taskId: 'a', status: 'Done', position: 0 })) as Actions;

    const emitted = run(moveTask(actions$, storeWith(new Map([[tasksFeature.selectMoveUndo, [aTask({ id: 'a' })]]])), api));

    expect(emitted).toEqual([TasksApiActions.taskMoveFailed({ error: { code: 'Concurrency.Stale', fields: {} } })]);
  });

  it("answers an update of a task that's gone without calling the API, so the form gets its outcome", () => {
    const api = { update: vi.fn() } as unknown as TasksApiService;
    const actions$ = of(TasksPageActions.updateSubmitted({ taskId: 'gone', changes: input, submissionId: 4 })) as Actions;

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
