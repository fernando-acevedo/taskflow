import { HttpErrorResponse } from '@angular/common/http';
import { Actions } from '@ngrx/effects';
import { Action, Store } from '@ngrx/store';
import { Observable, Subject, of, throwError } from 'rxjs';
import { ActivityApiService } from '../activity-api.service';
import { ActivityApiActions, ActivityPageActions } from './activity.actions';
import { loadActivity, loadMoreActivity } from './activity.effects';
import { activityFeature, initialActivityState } from './activity.feature';

function run(effect: Observable<Action>): Action[] {
  const emitted: Action[] = [];
  effect.subscribe((action) => emitted.push(action));
  return emitted;
}

const result = { items: [], page: 1, pageSize: 50, totalCount: 0 };

function storeWith(taskId: string | null, page: number): Store {
  const state = { ...initialActivityState, project: { ...initialActivityState.project, projectId: 'p1', taskId, page } };
  return { select: (selector: unknown) => of(selector === activityFeature.selectActivityState ? state : undefined) } as unknown as Store;
}

describe('activity effects', () => {
  it('loads the first page of the opened feed', () => {
    const api = { list: vi.fn(() => of(result)) } as unknown as ActivityApiService;
    const actions$ = of(ActivityPageActions.opened({ scope: 'task', projectId: 'p1', taskId: 't1' })) as Actions;

    expect(run(loadActivity(actions$, api))).toEqual([
      ActivityApiActions.pageLoaded({ scope: 'task', projectId: 'p1', taskId: 't1', result }),
    ]);
    expect(api.list).toHaveBeenCalledWith('p1', 't1', 1);
  });

  it('does not let a load of one scope cancel the other', () => {
    const project$ = new Subject<typeof result>();
    const task$ = new Subject<typeof result>();
    const api = {
      list: vi.fn((_projectId: string, taskId: string | null) => (taskId === null ? project$ : task$)),
    } as unknown as ActivityApiService;
    const actions$ = new Subject<Action>();
    const emitted = run(loadActivity(actions$, api));

    actions$.next(ActivityPageActions.opened({ scope: 'project', projectId: 'p1', taskId: null }));
    actions$.next(ActivityPageActions.opened({ scope: 'task', projectId: 'p1', taskId: 't1' }));
    project$.next(result);
    task$.next(result);

    expect(emitted).toEqual([
      ActivityApiActions.pageLoaded({ scope: 'project', projectId: 'p1', taskId: null, result }),
      ActivityApiActions.pageLoaded({ scope: 'task', projectId: 'p1', taskId: 't1', result }),
    ]);
  });

  it('still cancels an older load of the same scope', () => {
    const first$ = new Subject<typeof result>();
    const second$ = new Subject<typeof result>();
    const api = { list: vi.fn().mockReturnValueOnce(first$).mockReturnValueOnce(second$) } as unknown as ActivityApiService;
    const actions$ = new Subject<Action>();
    const emitted = run(loadActivity(actions$, api));

    actions$.next(ActivityPageActions.opened({ scope: 'task', projectId: 'p1', taskId: 't1' }));
    actions$.next(ActivityPageActions.opened({ scope: 'task', projectId: 'p1', taskId: 't2' }));
    first$.next(result);
    second$.next(result);

    expect(emitted).toEqual([ActivityApiActions.pageLoaded({ scope: 'task', projectId: 'p1', taskId: 't2', result })]);
  });

  it('reports a failed load with the API error', () => {
    const api = {
      list: vi.fn(() => throwError(() => new HttpErrorResponse({ status: 403, error: { code: 'Projects.Forbidden' } }))),
    } as unknown as ActivityApiService;
    const actions$ = of(ActivityPageActions.opened({ scope: 'project', projectId: 'p1', taskId: null })) as Actions;

    expect(run(loadActivity(actions$, api))).toEqual([
      ActivityApiActions.loadFailed({ scope: 'project', projectId: 'p1', taskId: null, error: { code: 'Projects.Forbidden', fields: {} } }),
    ]);
  });

  it('asks for the page after the last one loaded', () => {
    const api = { list: vi.fn(() => of({ ...result, page: 3 })) } as unknown as ActivityApiService;
    const actions$ = of(ActivityPageActions.loadMoreClicked({ scope: 'project' })) as Actions;

    run(loadMoreActivity(actions$, storeWith(null, 2), api));

    expect(api.list).toHaveBeenCalledWith('p1', null, 3);
  });
});
