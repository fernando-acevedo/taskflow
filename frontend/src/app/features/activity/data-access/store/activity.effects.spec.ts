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

const result = { items: [], nextCursor: null };

function storeWith(taskId: string | null, nextCursor: string | null): Store {
  const state = { ...initialActivityState, project: { ...initialActivityState.project, projectId: 'p1', taskId, nextCursor } };
  return { select: (selector: unknown) => of(selector === activityFeature.selectActivityState ? state : undefined) } as unknown as Store;
}

describe('activity effects', () => {
  it('loads the first page of the opened feed', () => {
    const api = { list: vi.fn(() => of(result)) } as unknown as ActivityApiService;
    const actions$ = of(ActivityPageActions.opened({ scope: 'task', projectId: 'p1', taskId: 't1' })) as Actions;

    expect(run(loadActivity(actions$, api))).toEqual([
      ActivityApiActions.pageLoaded({ scope: 'task', projectId: 'p1', taskId: 't1', append: false, result }),
    ]);
    expect(api.list).toHaveBeenCalledWith('p1', 't1', null);
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
      ActivityApiActions.pageLoaded({ scope: 'project', projectId: 'p1', taskId: null, append: false, result }),
      ActivityApiActions.pageLoaded({ scope: 'task', projectId: 'p1', taskId: 't1', append: false, result }),
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

    expect(emitted).toEqual([ActivityApiActions.pageLoaded({ scope: 'task', projectId: 'p1', taskId: 't2', append: false, result })]);
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

  it('asks for the page after the cursor of the last one loaded', () => {
    const api = { list: vi.fn(() => of(result)) } as unknown as ActivityApiService;
    const actions$ = of(ActivityPageActions.loadMoreClicked({ scope: 'project' })) as Actions;

    const emitted = run(loadMoreActivity(actions$, storeWith(null, 'c2'), api));

    expect(api.list).toHaveBeenCalledWith('p1', null, 'c2');
    expect(emitted).toEqual([ActivityApiActions.pageLoaded({ scope: 'project', projectId: 'p1', taskId: null, append: true, result })]);
  });

  it('does not ask for more when there is no next cursor', () => {
    const api = { list: vi.fn(() => of(result)) } as unknown as ActivityApiService;
    const actions$ = of(ActivityPageActions.loadMoreClicked({ scope: 'project' })) as Actions;

    expect(run(loadMoreActivity(actions$, storeWith(null, null), api))).toEqual([]);
    expect(api.list).not.toHaveBeenCalled();
  });
});
