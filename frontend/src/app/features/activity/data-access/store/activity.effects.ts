import { inject } from '@angular/core';
import { Actions, createEffect, ofType } from '@ngrx/effects';
import { Store } from '@ngrx/store';
import { EMPTY, catchError, exhaustMap, groupBy, map, mergeMap, of, switchMap, withLatestFrom } from 'rxjs';
import { toApiError } from '../../../../core/http/api-error';
import { ActivityApiService } from '../activity-api.service';
import { ActivityApiActions, ActivityPageActions, ActivityScope } from './activity.actions';
import { activityFeature } from './activity.feature';
import { untilTenantChanges } from '../../../../shared/data-access/until-tenant-changes';

function fetchPage(api: ActivityApiService, scope: ActivityScope, projectId: string, taskId: string | null, page: number) {
  return api.list(projectId, taskId, page).pipe(
    map((result) => ActivityApiActions.pageLoaded({ scope, projectId, taskId, result })),
    catchError((error: unknown) => of(ActivityApiActions.loadFailed({ scope, projectId, taskId, error: toApiError(error) }))),
  );
}

/** First page of a feed; reopening a feed cancels its own load still running, never the other scope's. */
export const loadActivity = createEffect(
  (actions$ = inject(Actions), api = inject(ActivityApiService)) =>
    actions$.pipe(
      ofType(ActivityPageActions.opened),
      groupBy(({ scope }) => scope),
      mergeMap((group$) =>
        group$.pipe(
          switchMap(({ scope, projectId, taskId }) =>
            fetchPage(api, scope, projectId, taskId, 1).pipe(untilTenantChanges(actions$)),
          ),
        ),
      ),
    ),
  { functional: true },
);

/** The next page; clicks while one is loading are ignored. */
export const loadMoreActivity = createEffect(
  (actions$ = inject(Actions), store = inject(Store), api = inject(ActivityApiService)) =>
    actions$.pipe(
      ofType(ActivityPageActions.loadMoreClicked),
      withLatestFrom(store.select(activityFeature.selectActivityState)),
      exhaustMap(([{ scope }, state]) => {
        const feed = state[scope];
        return feed.projectId === null
          ? EMPTY
          : fetchPage(api, scope, feed.projectId, feed.taskId, feed.page + 1).pipe(untilTenantChanges(actions$));
      }),
    ),
  { functional: true },
);
